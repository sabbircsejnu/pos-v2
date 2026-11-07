using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Costing;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.StockTransfers;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed record StockCostLocationSummary(
    string LocationType,
    long LocationId,
    int Records,
    decimal Quantity,
    int ValuedRecords,
    decimal ValuedQuantity,
    decimal Value,
    int ZeroCostRecords,
    int UnvaluedRecords,
    decimal UnvaluedQuantity);

public sealed record StockCostCorrectionPreview(
    StockCostCorrectionPlan Plan,
    IReadOnlyList<StockCostLocationSummary> Before,
    IReadOnlyList<StockCostLocationSummary> After,
    decimal InventoryGlBalance);

/// <summary>
/// Plans and applies the approved correction of uncosted receipts from their GRN evidence. It only ever appends: original
/// ledger rows, stock balances and journals are untouched. Corrections, their audit event and their claim on each stock
/// row commit together, so a retry finds nothing left to do.
/// </summary>
public sealed class StockCostCorrectionService(RetailPOSDbContext dbContext, IStockLedgerWriter stockLedger)
{
    public async Task<StockCostCorrectionPreview> PreviewAsync(long businessId, CancellationToken cancellationToken)
    {
        var (plan, movements, existing) = await BuildPlanAsync(businessId, cancellationToken);
        var planned = plan.Corrections.ToDictionary(item => item.Movement.Id, item => (item.UnitCost, item.Value));
        foreach (var movement in movements.Where(row => !planned.ContainsKey(row.Id)))
        {
            var resolved = existing.Resolve(movement.Id, movement.UnitCost, movement.MovementValue, movement.IsReliable);
            if (resolved.IsReliable != movement.IsReliable || resolved.Value != movement.MovementValue)
                planned[movement.Id] = (resolved.UnitCost, resolved.Value);
        }

        return new StockCostCorrectionPreview(
            plan,
            await SummarizeAsync(businessId, movements, existing, cancellationToken),
            await SummarizeAsync(businessId, movements, new StockCostCorrectionSet(planned), cancellationToken),
            await InventoryGlBalanceAsync(businessId, cancellationToken));
    }

    /// <param name="expectedCount">The count the operator reviewed. Anything else means the data moved since the preview.</param>
    public async Task<int> ApplyAsync(
        long businessId,
        long actorUserId,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Users.AnyAsync(
                user => user.Id == actorUserId && user.BusinessId == businessId,
                cancellationToken))
        {
            throw new InvalidOperationException("The actor must be a user of this Business.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (dbContext.Database.IsNpgsql())
        {
            // Serializes applies. NO KEY UPDATE does not block ordinary writes that only reference the Business.
            await dbContext.Businesses
                .FromSqlInterpolated($"SELECT * FROM businesses WHERE id = {businessId} FOR NO KEY UPDATE")
                .Select(business => business.Id)
                .SingleOrDefaultAsync(cancellationToken);
        }

        var (plan, _, _) = await BuildPlanAsync(businessId, cancellationToken);
        if (plan.Corrections.Count != expectedCount)
        {
            throw new InvalidOperationException(
                $"The preview found {expectedCount} correction(s), but {plan.Corrections.Count} are due now. Preview again.");
        }

        if (plan.Corrections.Count == 0)
            return 0;

        var audit = new AuditEvent
        {
            CorrelationId = Guid.NewGuid(),
            BusinessId = businessId,
            RealUserId = actorUserId,
            ActingUserId = actorUserId,
            ActionType = AuditActionType.Adjustment,
            ActionSummary = $"Corrected the cost of {plan.Corrections.Count} stock ledger movement(s) from GRN evidence",
            Module = AuditModule.Inventory,
            Source = AuditSource.SystemJob,
            PrimaryEntityType = "StockLedger",
            PrimaryEntityId = plan.Corrections[0].Movement.Id.ToString(),
            Metadata = JsonSerializer.Serialize(new
            {
                count = plan.Corrections.Count,
                totalInboundValue = plan.Corrections
                    .Where(item => item.Movement.QtyIn > 0 && item.Movement.ReferenceType == StockLedgerReferenceType.Grn)
                    .Sum(item => item.Value)
            })
        };

        foreach (var correction in plan.Corrections)
        {
            var movement = correction.Movement;
            var inventory = await dbContext.Inventories.FirstOrDefaultAsync(
                row => row.VariantId == movement.VariantId
                    && row.LocationId == movement.LocationId
                    && row.LocationType.ToLower() == movement.LocationType,
                cancellationToken);

            // Takes a ledger sequence at this location, so it must conflict with any movement there.
            StockTransferWriteRules.ClaimForEvidenceOnlyMovement(dbContext, inventory);
            stockLedger.WriteEntryWithValuation(
                variantId: movement.VariantId,
                locationId: movement.LocationId,
                locationType: movement.LocationType,
                transactionType: StockLedgerTransactionType.CostCorrection,
                qtyIn: 0,
                qtyOut: 0,
                balanceAfter: inventory?.Quantity ?? 0,
                referenceType: StockLedgerReferenceType.StockCostCorrection,
                referenceId: movement.Id,
                remarks: $"Cost of movement {movement.Id} corrected: {correction.Basis}",
                createdBy: actorUserId,
                unitCost: correction.UnitCost,
                movementValue: correction.Value);
            audit.Entities.Add(new AuditEventEntity
            {
                EntityType = "StockLedger",
                EntityId = movement.Id.ToString(),
                OperationType = AuditOperationType.Insert,
                Metadata = JsonSerializer.Serialize(new
                {
                    movement.VariantId,
                    movement.LocationId,
                    movement.LocationType,
                    previousUnitCost = movement.UnitCost,
                    previousValue = movement.MovementValue,
                    previousReliable = movement.IsReliable,
                    unitCost = correction.UnitCost,
                    value = correction.Value,
                    basis = correction.Basis
                }),
                CreatedAt = DateTime.UtcNow
            });
        }

        dbContext.AuditEvents.Add(audit);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return plan.Corrections.Count;
    }

    private async Task<(StockCostCorrectionPlan Plan, List<StockCostPlanMovement> Movements, StockCostCorrectionSet Existing)> BuildPlanAsync(
        long businessId,
        CancellationToken cancellationToken)
    {
        var movements = await dbContext.StockLedgers
            .AsNoTracking()
            .Where(row => row.Variant.BusinessId == businessId)
            .OrderBy(row => row.Id)
            .Select(row => new StockCostPlanMovement(
                row.Id,
                row.VariantId,
                row.LocationId,
                row.LocationType,
                row.TransactionType,
                row.ReferenceType,
                row.ReferenceId,
                row.QtyIn,
                row.QtyOut,
                row.BalanceAfter,
                row.UnitCost,
                row.MovementValue,
                row.IsCostValuationReliable))
            .ToListAsync(cancellationToken);
        var existing = await StockCostReader.ReadCorrectionsAsync(dbContext, businessId, null, cancellationToken);

        var grnIds = movements
            .Where(row => row.ReferenceType == StockLedgerReferenceType.Grn)
            .Select(row => row.ReferenceId)
            .Distinct()
            .ToList();
        var grnLines = await dbContext.GrnItems
            .AsNoTracking()
            .Where(item => grnIds.Contains(item.GrnId)
                && item.Grn.Status == "completed"
                && item.PurchaseOrderItem.Variant.BusinessId == businessId)
            .Select(item => new GrnCostLine(
                item.GrnId,
                item.PurchaseOrderItem.VariantId,
                item.ReceivedQty,
                item.UnitCost,
                item.TaxableAmount,
                item.PurchaseOrderItem.UnitPrice,
                item.PurchaseOrderItem.Discount))
            .ToListAsync(cancellationToken);

        var transferIds = movements
            .Where(row => row.ReferenceType == StockLedgerReferenceType.StockTransfer)
            .Select(row => row.ReferenceId)
            .Distinct()
            .ToList();
        var originalIds = await dbContext.StockTransfers
            .AsNoTracking()
            .Where(transfer => transferIds.Contains(transfer.Id) && transfer.OriginalTransferId != null)
            .Select(transfer => transfer.OriginalTransferId!.Value)
            .ToListAsync(cancellationToken);
        var allTransferIds = transferIds.Concat(originalIds).Distinct().ToList();
        var transferLines = await dbContext.StockTransferItems
            .AsNoTracking()
            .Where(item => allTransferIds.Contains(item.TransferId) && item.Variant.BusinessId == businessId)
            .Select(item => new TransferCostLine(
                item.TransferId,
                item.VariantId,
                item.Transfer.OriginalTransferId,
                item.AcceptedQuantity,
                item.RejectedQuantity,
                item.MissingQuantity,
                item.DamagedQuantity))
            .ToListAsync(cancellationToken);

        return (
            StockCostCorrectionPlanner.Build(
                movements,
                grnLines,
                await JournalDebitsAsync(businessId, grnIds, cancellationToken),
                // A variant listed twice on a transfer cannot be split safely, so it is left out and stays unvalued.
                transferLines.GroupBy(line => (line.TransferId, line.VariantId)).Where(group => group.Count() == 1).Select(group => group.Single()).ToList(),
                existing),
            movements,
            existing);
    }

    private async Task<Dictionary<long, decimal>> JournalDebitsAsync(
        long businessId,
        List<long> grnIds,
        CancellationToken cancellationToken)
    {
        var inventoryAccountId = await InventoryAccountIdAsync(businessId, cancellationToken);
        if (inventoryAccountId is null)
            return [];

        var references = grnIds.Select(id => id.ToString()).ToList();
        var rows = await dbContext.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.BusinessId == businessId
                && entry.Status == JournalEntryStatuses.Posted
                && entry.ReferenceType == "Grn"
                && references.Contains(entry.ReferenceId!))
            .SelectMany(entry => entry.Lines
                .Where(line => line.AccountId == inventoryAccountId.Value)
                .Select(line => new { entry.ReferenceId, line.DebitAmount, line.CreditAmount }))
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(row => long.Parse(row.ReferenceId!))
            .ToDictionary(group => group.Key, group => group.Sum(row => row.DebitAmount - row.CreditAmount));
    }

    private Task<long?> InventoryAccountIdAsync(long businessId, CancellationToken cancellationToken) =>
        dbContext.AccountingConfigurations
            .AsNoTracking()
            .Where(configuration => configuration.BusinessId == businessId)
            .Select(configuration => configuration.InventoryAccountId)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<decimal> InventoryGlBalanceAsync(long businessId, CancellationToken cancellationToken)
    {
        var inventoryAccountId = await InventoryAccountIdAsync(businessId, cancellationToken);
        if (inventoryAccountId is null)
            return 0m;

        return await dbContext.JournalEntries
            .AsNoTracking()
            .Where(entry => entry.BusinessId == businessId && entry.Status == JournalEntryStatuses.Posted)
            .SelectMany(entry => entry.Lines)
            .Where(line => line.AccountId == inventoryAccountId.Value)
            .SumAsync(line => line.DebitAmount - line.CreditAmount, cancellationToken);
    }

    private async Task<List<StockCostLocationSummary>> SummarizeAsync(
        long businessId,
        List<StockCostPlanMovement> movements,
        StockCostCorrectionSet corrections,
        CancellationToken cancellationToken)
    {
        var ledger = new StockCostLedger();
        var ordered = await dbContext.StockLedgers
            .AsNoTracking()
            .Where(row => row.Variant.BusinessId == businessId)
            .OrderBy(row => row.BusinessDate)
            .ThenBy(row => row.MovementSequence)
            .ThenBy(row => row.Id)
            .Select(row => row.Id)
            .ToListAsync(cancellationToken);
        var byId = movements.ToDictionary(row => row.Id);
        foreach (var movement in ordered.Where(byId.ContainsKey).Select(id => byId[id]))
        {
            var resolved = corrections.Resolve(movement.Id, movement.UnitCost, movement.MovementValue, movement.IsReliable);
            ledger.Apply(new StockCostMovement(
                movement.VariantId,
                movement.LocationId,
                movement.LocationType,
                movement.ReferenceType,
                movement.ReferenceId,
                movement.QtyIn,
                movement.QtyOut,
                resolved.UnitCost,
                resolved.Value,
                resolved.IsReliable));
        }

        var evidence = await StockCostReader.ReadEvidenceAsync(dbContext, ledger, cancellationToken);
        var stock = await dbContext.Inventories
            .AsNoTracking()
            .Where(row => row.Variant.BusinessId == businessId && row.Quantity > 0)
            .Select(row => new { row.VariantId, row.LocationId, row.LocationType, row.Quantity })
            .ToListAsync(cancellationToken);

        return stock
            .Select(row =>
            {
                var type = row.LocationType.Trim().ToLowerInvariant();
                return new
                {
                    Type = type,
                    row.LocationId,
                    row.Quantity,
                    Position = ledger.Classify(row.VariantId, new StockCostLocation(row.LocationId, type), row.Quantity, evidence)
                };
            })
            .GroupBy(row => (row.Type, row.LocationId))
            .OrderBy(group => group.Key.Type)
            .ThenBy(group => group.Key.LocationId)
            .Select(group => new StockCostLocationSummary(
                group.Key.Type,
                group.Key.LocationId,
                group.Count(),
                group.Sum(row => row.Quantity),
                group.Count(row => row.Position.Kind == StockCostKind.Valued),
                group.Where(row => row.Position.Kind == StockCostKind.Valued).Sum(row => row.Quantity),
                group.Sum(row => row.Position.Value ?? 0m),
                group.Count(row => row.Position.Kind == StockCostKind.VerifiedZero),
                group.Count(row => row.Position.Kind == StockCostKind.Unvalued),
                group.Where(row => row.Position.Kind == StockCostKind.Unvalued).Sum(row => row.Quantity)))
            .ToList();
    }
}
