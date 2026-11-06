using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Settings;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.ReadStores;

public sealed class StockLedgerWriter(
    RetailPOSDbContext dbContext,
    ILogger<StockLedgerWriter> logger,
    ICompanyTimeZoneSnapshot? sharedCompanyTimeZone = null) : IStockLedgerWriter
{
    private readonly Dictionary<(long LocationId, string LocationType), string> _timeZones = new();

    public void WriteEntry(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int qtyIn,
        int qtyOut,
        int balanceAfter,
        string referenceType,
        long referenceId,
        string? remarks = null,
        long? createdBy = null,
        DateTime? createdAt = null,
        decimal? unitCost = null,
        decimal? movementValue = null,
        DateTime? businessDate = null,
        long? movementSequence = null,
        long? reversalOfMovementId = null) =>
        WriteEntryInternal(
            variantId,
            locationId,
            locationType,
            transactionType,
            qtyIn,
            qtyOut,
            balanceAfter,
            referenceType,
            referenceId,
            remarks,
            createdBy,
            createdAt,
            unitCost,
            movementValue,
            businessDate,
            movementSequence,
            reversalOfMovementId);

    public void WriteEntryWithValuation(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int qtyIn,
        int qtyOut,
        int balanceAfter,
        string referenceType,
        long referenceId,
        string? remarks = null,
        long? createdBy = null,
        DateTime? createdAt = null,
        decimal? unitCost = null,
        decimal? movementValue = null,
        DateTime? businessDate = null,
        long? movementSequence = null,
        long? reversalOfMovementId = null) =>
        WriteEntryInternal(
            variantId,
            locationId,
            locationType,
            transactionType,
            qtyIn,
            qtyOut,
            balanceAfter,
            referenceType,
            referenceId,
            remarks,
            createdBy,
            createdAt,
            unitCost,
            movementValue,
            businessDate,
            movementSequence,
            reversalOfMovementId);

    public void WriteVerifiedZeroCostEntry(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int qtyIn,
        int qtyOut,
        int balanceAfter,
        string referenceType,
        long referenceId,
        string? remarks = null,
        long? createdBy = null,
        DateTime? createdAt = null) =>
        WriteEntryInternal(
            variantId,
            locationId,
            locationType,
            transactionType,
            qtyIn,
            qtyOut,
            balanceAfter,
            referenceType,
            referenceId,
            remarks,
            createdBy,
            createdAt,
            0m,
            0m,
            null,
            null,
            null,
            zeroCostVerified: true);

    private void WriteEntryInternal(
        long variantId,
        long locationId,
        string locationType,
        string transactionType,
        int qtyIn,
        int qtyOut,
        int balanceAfter,
        string referenceType,
        long referenceId,
        string? remarks,
        long? createdBy,
        DateTime? createdAt,
        decimal? unitCost,
        decimal? movementValue,
        DateTime? businessDate,
        long? movementSequence,
        long? reversalOfMovementId,
        bool zeroCostVerified = false)
    {
        var normalizedLocationType = (locationType ?? string.Empty).Trim().ToLowerInvariant();
        var effectiveCreatedAt = createdAt ?? DateTime.UtcNow;
        var hasCost = unitCost.HasValue || movementValue.HasValue;
        // A movement of stock is reliable only at a known positive cost or a zero proven by documents. Supplying a
        // value proves nothing. Evidence rows move no stock, so they cannot make a value unreliable.
        var hasReliableCost = qtyIn == 0 && qtyOut == 0
            ? hasCost
            : zeroCostVerified || unitCost > 0m || movementValue > 0m;
        var resolvedUnitCost = unitCost ?? 0m;
        var resolvedMovementValue = movementValue
            ?? (hasCost ? resolvedUnitCost * (qtyIn > 0 ? qtyIn : qtyOut) : 0m);
        var resolvedBusinessDate = businessDate
            ?? ResolveCompanyBusinessDate(effectiveCreatedAt, locationId, normalizedLocationType);
        var resolvedMovementSequence = movementSequence
            ?? ResolveNextMovementSequence(variantId, locationId, normalizedLocationType);

        var entry = new StockLedger
        {
            VariantId = variantId,
            LocationId = locationId,
            LocationType = normalizedLocationType,
            TransactionType = transactionType,
            QtyIn = qtyIn,
            QtyOut = qtyOut,
            BalanceAfter = balanceAfter,
            UnitCost = resolvedUnitCost,
            MovementValue = resolvedMovementValue,
            IsCostValuationReliable = hasReliableCost,
            BusinessDate = resolvedBusinessDate,
            MovementSequence = resolvedMovementSequence,
            ReversalOfMovementId = reversalOfMovementId,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Remarks = remarks,
            CreatedBy = createdBy,
            CreatedAt = effectiveCreatedAt
        };

        dbContext.StockLedgers.Add(entry);

        logger.LogDebug(
            "StockLedger entry queued: variant={VariantId} loc={LocationId}/{LocationType} " +
            "type={TransactionType} in={QtyIn} out={QtyOut} balance={BalanceAfter} ref={RefType}#{RefId} unitCost={UnitCost} movementValue={MovementValue}",
            variantId,
            locationId,
            normalizedLocationType,
            transactionType,
            qtyIn,
            qtyOut,
            balanceAfter,
            referenceType,
            referenceId,
            entry.UnitCost,
            entry.MovementValue);
    }

    /// <summary>
    /// The next sequence of one variant at one location, from the (variant, location, type, sequence) index.
    /// Entries still waiting to be saved were already numbered from the database in this unit of work, so a
    /// multi-line document asks once per key. Callers keep this safe under concurrency: they save the location's
    /// xmin-protected Inventory row in the same transaction, so a writer that read a stale sequence fails to commit.
    /// </summary>
    private long ResolveNextMovementSequence(long variantId, long locationId, string locationType)
    {
        var tracked = dbContext.StockLedgers.Local
            .Where(row => row.VariantId == variantId
                && row.LocationId == locationId
                && row.LocationType == locationType)
            .ToList();
        var trackedMax = tracked.Count == 0 ? 0 : tracked.Max(row => row.MovementSequence);
        if (tracked.Exists(row => dbContext.Entry(row).State == EntityState.Added))
            return trackedMax + 1;

        var savedMax = dbContext.StockLedgers
            .AsNoTracking()
            .Where(row => row.VariantId == variantId
                && row.LocationId == locationId
                && row.LocationType == locationType)
            .Max(row => (long?)row.MovementSequence) ?? 0;

        return Math.Max(trackedMax, savedMax) + 1;
    }

    /// <summary>The date of the movement in the Company time zone of the Business that owns the location.</summary>
    private DateTime ResolveCompanyBusinessDate(DateTime createdAt, long locationId, string locationType)
    {
        var instantUtc = createdAt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(createdAt, DateTimeKind.Utc)
            : createdAt.ToUniversalTime();
        return CompanyDates.DateFor(instantUtc, ResolveCompanyTimeZoneId(locationId, locationType));
    }

    private string ResolveCompanyTimeZoneId(long locationId, string locationType)
    {
        if (_timeZones.TryGetValue((locationId, locationType), out var known))
            return known;

        long? businessId = locationType == "warehouse"
            ? dbContext.Warehouses.AsNoTracking()
                .Where(warehouse => warehouse.Id == locationId)
                .Select(warehouse => (long?)warehouse.BusinessId)
                .FirstOrDefault()
            : dbContext.Outlets.AsNoTracking()
                .Where(outlet => outlet.Id == locationId)
                .Select(outlet => (long?)outlet.BusinessId)
                .FirstOrDefault();
        var businessZone = businessId is null
            ? null
            : dbContext.BusinessPosSettings.AsNoTracking()
                .Where(settings => settings.BusinessId == businessId.Value)
                .Select(settings => settings.DefaultTimeZoneId)
                .FirstOrDefault();
        var timeZoneId = CashSessionTime.ResolveEffectiveTimeZoneId(
            null,
            businessZone ?? sharedCompanyTimeZone?.GetCompanyTimeZoneId() ?? BusinessPosSettings.DefaultTimeZone);
        _timeZones[(locationId, locationType)] = timeZoneId;
        return timeZoneId;
    }
}
