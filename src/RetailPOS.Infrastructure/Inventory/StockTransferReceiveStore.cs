using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.StockTransfers;

public sealed class StockTransferReceiveStore(
    RetailPOSDbContext dbContext,
    IStockLedgerWriter stockLedger,
    IStockTransferReceiptAuditWriter auditWriter,
    ILogger<StockTransferReceiveStore> logger)
    : IStockTransferReceiveStore
{
    public async Task<StockTransferReceiveResult> ReceiveAsync(
        long transferId,
        long businessId,
        StockTransferReceiveInput? input,
        long? receivedBy,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (dbContext.Database.IsNpgsql())
            {
                await dbContext.StockTransfers
                    .FromSqlInterpolated(
                        $"SELECT * FROM stock_transfers WHERE id = {transferId} FOR UPDATE")
                    .Select(candidate => candidate.Id)
                    .SingleOrDefaultAsync(cancellationToken);
            }

            var transfer = await StockTransferWriteRules.TransfersInBusiness(dbContext, businessId)
                .Include(candidate => candidate.Items)
                .FirstOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
            if (transfer is null)
                throw new KeyNotFoundException($"Stock transfer with ID {transferId} not found");

            if (!transfer.Status.Equals(StockTransfer.StatusInTransit, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "This transfer must be In Transit before it can be received.");
            }

            var normalizedToType = NormalizeLocationType(transfer.ToLocationType);
            var requestedLines = ResolveRequestedLines(transfer, input);
            var isSystemReturn = transfer.OriginalTransferId.HasValue;
            var totals = new ReceiptTotals();
            var auditLines = new List<StockTransferReceiptAuditLine>();

            foreach (var item in transfer.Items)
            {
                var dispatched = ResolveTransferQuantity(item);
                var line = requestedLines is null
                    ? new StockTransferReceiveItemInput(item.VariantId, dispatched, 0, item.Remarks)
                    : requestedLines[item.VariantId];
                ValidateReconciliation(item.VariantId, dispatched, line, isSystemReturn);

                item.AcceptedQuantity = line.AcceptedQuantity;
                item.RejectedQuantity = line.RejectedQuantity;
                item.MissingQuantity = line.MissingQuantity;
                item.DamagedQuantity = line.DamagedQuantity;
                item.Remarks = line.Remarks;
                totals.Add(line);

                var inventory = await dbContext.Inventories.FirstOrDefaultAsync(
                    row => row.VariantId == item.VariantId
                        && row.LocationId == transfer.ToLocationId
                        && row.LocationType.ToLower() == normalizedToType,
                    cancellationToken);

                if (line.AcceptedQuantity > 0)
                {
                    if (inventory is null)
                    {
                        inventory = new Inventory
                        {
                            VariantId = item.VariantId,
                            LocationId = transfer.ToLocationId,
                            LocationType = normalizedToType,
                            Quantity = 0
                        };
                        dbContext.Inventories.Add(inventory);
                    }

                    inventory.Quantity += line.AcceptedQuantity;
                    var cost = await StockTransferCostFlow.ResolveReceiptCostAsync(
                        dbContext,
                        businessId,
                        transfer,
                        item,
                        line.AcceptedQuantity,
                        cancellationToken);
                    StockTransferCostFlow.Write(
                        stockLedger,
                        cost,
                        item.VariantId,
                        transfer.ToLocationId,
                        normalizedToType,
                        transfer.TransferType.Equals(
                            StockTransfer.TransferTypeReturn,
                            StringComparison.OrdinalIgnoreCase)
                            ? StockLedgerTransactionType.ReturnTransferIn
                            : StockLedgerTransactionType.TransferIn,
                        qtyIn: line.AcceptedQuantity,
                        qtyOut: 0,
                        balanceAfter: inventory.Quantity,
                        transfer.Id,
                        $"Received from {transfer.FromLocationType} {transfer.FromLocationId}",
                        receivedBy ?? transfer.CreatedBy);
                }
                else if (line.RejectedQuantity + line.MissingQuantity + line.DamagedQuantity > 0)
                {
                    StockTransferWriteRules.ClaimForEvidenceOnlyMovement(dbContext, inventory);
                }

                if (line.RejectedQuantity > 0)
                {
                    WriteEvidence(
                        transfer,
                        item,
                        normalizedToType,
                        inventory,
                        StockLedgerTransactionType.TransferRejection,
                        $"Rejected {line.RejectedQuantity}; awaiting return to source. {line.Remarks}".TrimEnd(),
                        receivedBy);
                }

                if (line.MissingQuantity + line.DamagedQuantity > 0)
                {
                    WriteEvidence(
                        transfer,
                        item,
                        normalizedToType,
                        inventory,
                        StockLedgerTransactionType.TransferDiscrepancy,
                        $"Missing {line.MissingQuantity}, damaged {line.DamagedQuantity}. {line.Remarks}",
                        receivedBy);
                }

                if (line.AcceptedQuantity != dispatched)
                {
                    auditLines.Add(new StockTransferReceiptAuditLine(
                        item.VariantId,
                        dispatched,
                        line.AcceptedQuantity,
                        line.RejectedQuantity,
                        line.MissingQuantity,
                        line.DamagedQuantity,
                        line.Remarks));
                }
            }

            transfer.Status = totals.ResolveStatus();
            transfer.ReceivedBy = receivedBy ?? transfer.UpdatedBy ?? transfer.CreatedBy;
            transfer.ReceivedAt = DateTime.UtcNow;
            transfer.Notes = string.IsNullOrWhiteSpace(input?.Notes) ? transfer.Notes : input!.Notes;
            transfer.UpdatedBy = transfer.ReceivedBy;
            transfer.UpdatedAt = DateTime.UtcNow;

            await StockTransferWriteRules.CreateSystemReturnAsync(
                dbContext,
                transfer,
                transfer.ReceivedBy,
                cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            if (auditLines.Count > 0)
            {
                await auditWriter.RecordAsync(
                    new StockTransferReceiptAudit(
                        transfer.Id,
                        transfer.TransferNo,
                        transfer.Status,
                        transfer.ToLocationId,
                        normalizedToType,
                        transfer.ReceivedBy,
                        input?.Notes,
                        auditLines),
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation(
                "Stock transfer {TransferId} received; destination stock updated",
                transferId);

            return new StockTransferReceiveResult(
                transfer.Id,
                transfer.ToLocationType,
                transfer.ToLocationId,
                transfer.Items.Select(item => item.VariantId).ToArray());
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// No lines means the whole transfer was accepted. Once lines are sent, every transfer line must be accounted for.
    /// </summary>
    private static Dictionary<long, StockTransferReceiveItemInput>? ResolveRequestedLines(
        StockTransfer transfer,
        StockTransferReceiveInput? input)
    {
        if (input?.Items is not { Count: > 0 })
            return null;

        var lines = new Dictionary<long, StockTransferReceiveItemInput>();
        foreach (var line in input.Items)
        {
            if (!lines.TryAdd(line.VariantId, line))
                throw new InvalidOperationException($"Variant {line.VariantId} appears more than once in this receipt.");
        }

        var expected = transfer.Items.Select(item => item.VariantId).ToHashSet();
        foreach (var variantId in lines.Keys.Where(variantId => !expected.Contains(variantId)))
            throw new InvalidOperationException($"Variant {variantId} is not part of this transfer.");

        foreach (var variantId in expected.Where(variantId => !lines.ContainsKey(variantId)))
            throw new InvalidOperationException($"Every transfer line must be accounted for. Missing variant {variantId}.");

        return lines;
    }

    private static void ValidateReconciliation(
        long variantId,
        int dispatched,
        StockTransferReceiveItemInput line,
        bool isSystemReturn)
    {
        if (line.AcceptedQuantity < 0
            || line.RejectedQuantity < 0
            || line.MissingQuantity < 0
            || line.DamagedQuantity < 0)
        {
            throw new InvalidOperationException(
                "Accepted, rejected, missing and damaged quantities cannot be negative.");
        }

        var recorded = line.AcceptedQuantity + line.RejectedQuantity + line.MissingQuantity + line.DamagedQuantity;
        if (recorded != dispatched)
        {
            throw new InvalidOperationException(
                $"Accepted + rejected + missing + damaged quantities must equal the dispatched quantity for variant {variantId}. "
                + $"Dispatched: {dispatched}, Recorded: {recorded}");
        }

        if (isSystemReturn && line.RejectedQuantity > 0)
        {
            throw new InvalidOperationException(
                "A return of rejected stock cannot be rejected again. Record any units not received as missing or damaged.");
        }

        if (line.MissingQuantity + line.DamagedQuantity > 0 && string.IsNullOrWhiteSpace(line.Remarks))
        {
            throw new InvalidOperationException(
                $"A reason is required for missing or damaged units of variant {variantId}.");
        }
    }

    private void WriteEvidence(
        StockTransfer transfer,
        StockTransferItem item,
        string locationType,
        Inventory? inventory,
        string transactionType,
        string remarks,
        long? actorId) =>
        stockLedger.WriteEntryWithValuation(
            variantId: item.VariantId,
            locationId: transfer.ToLocationId,
            locationType: locationType,
            transactionType: transactionType,
            qtyIn: 0,
            qtyOut: 0,
            balanceAfter: inventory?.Quantity ?? 0,
            referenceType: StockLedgerReferenceType.StockTransfer,
            referenceId: transfer.Id,
            remarks: remarks.Length > 500 ? remarks[..500] : remarks,
            createdBy: actorId ?? transfer.CreatedBy,
            unitCost: item.UnitCost);

    private static int ResolveTransferQuantity(StockTransferItem item) =>
        item.TransferQuantity > 0 ? item.TransferQuantity : item.Quantity;

    private static string NormalizeLocationType(string locationType)
    {
        if (string.IsNullOrWhiteSpace(locationType))
            throw new InvalidOperationException("Location type is required.");

        var normalized = locationType.Trim().ToLowerInvariant();
        if (normalized != "outlet" && normalized != "warehouse")
            throw new InvalidOperationException($"Invalid location type '{locationType}'.");

        return normalized;
    }

    private sealed class ReceiptTotals
    {
        private int _accepted;
        private int _rejected;
        private int _discrepancy;

        public void Add(StockTransferReceiveItemInput line)
        {
            _accepted += line.AcceptedQuantity;
            _rejected += line.RejectedQuantity;
            _discrepancy += line.MissingQuantity + line.DamagedQuantity;
        }

        public string ResolveStatus()
        {
            if (_rejected == 0 && _discrepancy == 0)
                return StockTransfer.StatusReceived;

            return _accepted == 0 && _rejected > 0
                ? StockTransfer.StatusRejected
                : StockTransfer.StatusPartiallyReceived;
        }
    }
}
