using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Application.Inventory.StockTransfers;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Uom;

namespace RetailPOS.Application.Uom;

public sealed record TransferAllocationInput(string State, string? PackCode, decimal BaseQtyPerPack, int PackCount, decimal BaseQuantity);

/// <summary>Unit-aware entry for transfers and adjustments (D09). Every stored quantity remains in base units.</summary>
public static class InventoryUomRules
{
    public static async Task<StockTransferCreateInput> ResolveTransferAsync(
        StockTransferCreateInput input,
        long businessId,
        IVariantUomReader? reader,
        CancellationToken cancellationToken)
    {
        var configs = reader is null
            ? new Dictionary<long, ResolvedVariantUom>()
            : await reader.GetCurrentAsync(businessId, input.Items.Select(item => item.VariantId).ToList(), cancellationToken);
        return input with { Items = input.Items.Select(item => ResolveTransferItem(item, configs.GetValueOrDefault(item.VariantId))).ToArray() };
    }

    private static StockTransferCreateItem ResolveTransferItem(StockTransferCreateItem item, ResolvedVariantUom? config)
    {
        if (item.EnteredQuantity is { } entered)
        {
            if (config is null)
                throw new UomValidationException(UomErrorCodes.OptionNotFound, $"Variant {item.VariantId} has no configured units.");
            UomQuantityMath.EnsurePositive(entered, "Transfer quantity");
            var resolved = UomLineResolver.ResolveStockEntry(config, item.UnitOptionId, entered, item.ExpectedConfigVersionId);
            item = item with
            {
                Quantity = resolved.BaseQuantity,
                TransferQuantity = resolved.BaseQuantity,
                RequestedQuantity = item.RequestedQuantity ?? resolved.BaseQuantity,
                Snapshot = resolved.Snapshot
            };
        }
        else if (config is null)
        {
            UomLineResolver.EnsureLegacyWholeQuantity(item.TransferQuantity ?? item.Quantity, $"Transfer quantity of variant {item.VariantId}");
            UomLineResolver.EnsureLegacyWholeQuantity(item.RequestedQuantity ?? item.Quantity, $"Requested quantity of variant {item.VariantId}");
        }
        else
        {
            UomLineResolver.EnsureCurrentVersion(config, item.ExpectedConfigVersionId);
            UomQuantityMath.EnsurePrecision(item.TransferQuantity ?? item.Quantity, config.BaseUnit.Precision, "Transfer quantity");
        }

        if (item.Allocations is { Count: > 0 } allocations)
        {
            if (config?.PackStateTrackingEnabled != true)
                throw new UomValidationException(UomErrorCodes.PackStateNotEnabled, $"Variant {item.VariantId} does not track sealed and loose stock.");
            var moved = item.TransferQuantity is > 0m ? item.TransferQuantity.Value : item.Quantity;
            foreach (var allocation in allocations)
                ValidateAllocation(allocation);
            if (allocations.Sum(allocation => allocation.BaseQuantity) != moved)
                throw new UomValidationException(UomErrorCodes.QuantityMismatch, $"The sealed and loose quantities of variant {item.VariantId} do not add up to {moved}.");
        }

        return item;
    }

    private static void ValidateAllocation(TransferAllocationInput allocation)
    {
        if (allocation.State is not (PackStates.Loose or PackStates.Sealed or PackStates.Prepared))
            throw new UomValidationException(UomErrorCodes.PackStateMismatch, $"'{allocation.State}' is not a stock form.");
        if (allocation.BaseQuantity <= 0m)
            throw new UomValidationException(UomErrorCodes.InvalidQuantity, "An allocated quantity must be greater than zero.");
        if (allocation.State != PackStates.Loose && (allocation.PackCount <= 0 || allocation.PackCount * allocation.BaseQtyPerPack != allocation.BaseQuantity))
            throw new UomValidationException(UomErrorCodes.QuantityMismatch, "Sealed and prepared stock moves in whole packs.");
    }

    public static async Task<IReadOnlyList<StockAdjustmentCreateLine>> ResolveAdjustmentLinesAsync(
        IReadOnlyList<StockAdjustmentCreateLine> lines,
        long businessId,
        IVariantUomReader? reader,
        CancellationToken cancellationToken)
    {
        var configs = reader is null
            ? new Dictionary<long, ResolvedVariantUom>()
            : await reader.GetCurrentAsync(businessId, lines.Select(line => line.VariantId).ToList(), cancellationToken);
        return lines.Select(line => ResolveAdjustmentLine(line, configs.GetValueOrDefault(line.VariantId))).ToArray();
    }

    private static StockAdjustmentCreateLine ResolveAdjustmentLine(StockAdjustmentCreateLine line, ResolvedVariantUom? config)
    {
        if (line.EnteredQuantity is { } entered)
        {
            if (config is null)
                throw new UomValidationException(UomErrorCodes.OptionNotFound, $"Variant {line.VariantId} has no configured units.");
            if (entered == 0m)
                throw new UomValidationException(UomErrorCodes.InvalidQuantity, "Adjustment quantity cannot be zero.");
            var resolved = UomLineResolver.ResolveStockEntry(config, line.UnitOptionId, Math.Abs(entered), line.ExpectedConfigVersionId);
            line = line with
            {
                QuantityChange = entered < 0m ? -resolved.BaseQuantity : resolved.BaseQuantity,
                Snapshot = resolved.Snapshot with { EnteredQuantity = entered }
            };
        }
        else
        {
            UomLineResolver.EnsureBaseQuantityAllowed(config, line.QuantityChange, $"Adjustment quantity of variant {line.VariantId}");
        }

        if (line.PackState is null)
            return line;
        if (config?.PackStateTrackingEnabled != true)
            throw new UomValidationException(UomErrorCodes.PackStateNotEnabled, $"Variant {line.VariantId} does not track sealed and loose stock.");
        switch (line.PackState)
        {
            case PackStates.Loose:
                return line;
            case PackStates.Sealed:
                if (line.BaseQtyPerPack is not > 0m || string.IsNullOrWhiteSpace(line.PackCode) || line.QuantityChange % line.BaseQtyPerPack.Value != 0m)
                    throw new UomValidationException(UomErrorCodes.QuantityMismatch, "Sealed stock is adjusted in whole packs.");
                return line;
            case PackStates.Prepared:
                // The journal for a prepared pack would have to write off its capitalized bottle, which has no approved rule (REP-008).
                throw new UomValidationException(
                    UomErrorCodes.UnsupportedOperation,
                    "Prepared packs cannot be adjusted directly. Reverse the repack job if it is still unsold.");
            default:
                throw new UomValidationException(UomErrorCodes.PackStateMismatch, $"'{line.PackState}' is not a stock form.");
        }
    }

    /// <summary>Applies a posted adjustment line to physical state. Reversal passes the opposite sign.</summary>
    public static async Task ApplyAdjustmentAsync(
        IPackStateLedger ledger,
        long businessId,
        long locationId,
        string locationType,
        StockAdjustmentLine line,
        decimal quantityDelta,
        long adjustmentId,
        long? actorUserId,
        decimal onHandAfter,
        CancellationToken cancellationToken)
    {
        var state = line.PackState ?? PackStates.Loose;
        var perPack = state == PackStates.Loose ? 0m : line.BaseQtyPerPack ?? 0m;
        await ledger.ApplyAsync(
            new PackStateChange(businessId, line.VariantId, locationId, locationType, state, line.PackCode ?? string.Empty, perPack,
                perPack > 0m ? (int)(quantityDelta / perPack) : 0, quantityDelta, 0m,
                PackStateOperations.Adjustment, StockLedgerReferenceType.StockAdjustment, adjustmentId, actorUserId),
            cancellationToken);
        await ledger.EnsureReconciledAsync(businessId, line.VariantId, locationId, locationType, onHandAfter, cancellationToken);
    }
}

public sealed record StockCountEntryInput(
    long? UnitOptionId,
    decimal EnteredQuantity,
    string? State = null,
    string? PackCode = null,
    decimal BaseQtyPerPack = 0m);

public sealed record StockCountEntryRecord(
    decimal BaseQuantity,
    string? State,
    string? PackCode,
    decimal BaseQtyPerPack,
    int PackCount,
    UnitTransactionSnapshot Snapshot);

public interface IStockCountEntryStore
{
    Task<(long BusinessId, long LocationId, string LocationType, string Status, long VariantId, decimal CurrentStock)?> FindLineAsync(
        long stockCountId, long lineId, CancellationToken cancellationToken);

    Task SaveEntriesAsync(long stockCountId, long lineId, IReadOnlyList<StockCountEntryRecord> entries, decimal physicalStock, CancellationToken cancellationToken);
}

public sealed record StockCountEntriesResult(long LineId, decimal PhysicalStock, decimal Difference);

/// <summary>Records a mixed-unit physical count on one line (<c>2 × Jar500</c>, <c>175 × ml</c>). The line stores their sum in base units.</summary>
public sealed record SaveStockCountEntriesCommand(long StockCountId, long LineId, IReadOnlyList<StockCountEntryInput> Entries) : ICommand<StockCountEntriesResult>;

public sealed class SaveStockCountEntriesCommandHandler(
    IStockCountEntryStore store,
    IVariantUomReader reader,
    IPackStateStore packStore,
    IUomOperationScope scope,
    IUomAuditWriter audit) : IRequestHandler<SaveStockCountEntriesCommand, StockCountEntriesResult>
{
    public async Task<StockCountEntriesResult> Handle(SaveStockCountEntriesCommand request, CancellationToken cancellationToken)
    {
        var businessId = scope.RequireBusinessId();
        var line = await store.FindLineAsync(request.StockCountId, request.LineId, cancellationToken);
        if (line is null || line.Value.BusinessId != businessId)
            throw new KeyNotFoundException($"Stock count line {request.LineId} not found");
        await scope.AuthorizeWriteLocationAsync(line.Value.LocationId, line.Value.LocationType, cancellationToken);
        if (line.Value.Status != StockCount.StatusDraft)
            throw new InvalidOperationException("Counted quantities can only be changed while the stock count is in Draft status.");
        if (request.Entries.Count == 0)
            throw new UomValidationException(UomErrorCodes.InvalidQuantity, "Enter at least one counted quantity.");

        var variantId = line.Value.VariantId;
        var configs = await reader.GetCurrentAsync(businessId, [variantId], cancellationToken);
        if (!configs.TryGetValue(variantId, out var config))
            throw new UomValidationException(UomErrorCodes.OptionNotFound, "This item is counted in pcs. Enter its count directly.");

        var records = new List<StockCountEntryRecord>(request.Entries.Count);
        foreach (var entry in request.Entries)
        {
            if (entry.EnteredQuantity < 0m)
                throw new UomValidationException(UomErrorCodes.InvalidQuantity, "A counted quantity cannot be negative.");
            var resolved = UomLineResolver.ResolveStockEntry(config, entry.UnitOptionId, entry.EnteredQuantity, null);
            var state = config.PackStateTrackingEnabled ? entry.State ?? PackStates.Loose : null;
            var option = config.PurchaseOptions.FirstOrDefault(candidate => candidate.Id == entry.UnitOptionId);
            var perPack = entry.BaseQtyPerPack > 0m ? entry.BaseQtyPerPack : option?.IsSealedPack == true ? option.BaseQuantityPerUnit : 0m;
            var packCount = 0;
            if (state is PackStates.Sealed or PackStates.Prepared)
            {
                if (perPack <= 0m || resolved.BaseQuantity % perPack != 0m)
                    throw new UomValidationException(UomErrorCodes.QuantityMismatch, "Sealed and prepared stock is counted in whole packs.");
                packCount = (int)(resolved.BaseQuantity / perPack);
            }

            records.Add(new StockCountEntryRecord(resolved.BaseQuantity, state, entry.PackCode ?? option?.Code, perPack, packCount, resolved.Snapshot));
        }

        if (config.PackStateTrackingEnabled)
            await EnsureOnlyLooseVariesAsync(businessId, line.Value.LocationId, line.Value.LocationType, variantId, records, cancellationToken);

        var physical = UomQuantityMath.Normalize(records.Sum(record => record.BaseQuantity));
        await using var transaction = await packStore.BeginAsync(cancellationToken);
        await store.SaveEntriesAsync(request.StockCountId, request.LineId, records, physical, cancellationToken);
        await audit.RecordAsync($"Mixed-unit count recorded on stock count line {request.LineId}", "StockCountLine", request.LineId.ToString(),
            new Dictionary<string, object?> { ["stockCountId"] = request.StockCountId, ["physicalBase"] = physical, ["entries"] = records.Count },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StockCountEntriesResult(request.LineId, physical, physical - line.Value.CurrentStock);
    }

    /// <summary>
    /// A variance posts as one adjustment of loose stock. Sealed and prepared counts must therefore agree with the system,
    /// otherwise the difference would be booked against the wrong physical stock. Open or adjust those packs first.
    /// </summary>
    private async Task EnsureOnlyLooseVariesAsync(
        long businessId,
        long locationId,
        string locationType,
        long variantId,
        IReadOnlyList<StockCountEntryRecord> records,
        CancellationToken cancellationToken)
    {
        var balances = await packStore.ReadBalancesAsync(businessId, variantId, locationId, locationType, cancellationToken);
        var expected = balances.Where(row => row.State != PackStates.Loose)
            .ToDictionary(row => (row.State, row.PackCode, row.BaseQtyPerPack), row => row.PackCount);
        var counted = records.Where(record => record.State is PackStates.Sealed or PackStates.Prepared)
            .GroupBy(record => (record.State!, record.PackCode ?? string.Empty, record.BaseQtyPerPack))
            .ToDictionary(group => group.Key, group => group.Sum(record => record.PackCount));
        foreach (var key in expected.Keys.Union(counted.Keys))
            if (expected.GetValueOrDefault(key) != counted.GetValueOrDefault(key))
                throw new UomValidationException(
                    UomErrorCodes.PackStateMismatch,
                    $"The count shows {counted.GetValueOrDefault(key)} {key.Item1} '{key.Item2}' but the system holds {expected.GetValueOrDefault(key)}. Open or adjust those packs first, then count again.");
    }
}
