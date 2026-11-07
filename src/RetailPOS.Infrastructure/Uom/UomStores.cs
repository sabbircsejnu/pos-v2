using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RetailPOS.Application.Inventory.Costing;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Application.Uom;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Uom;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ReadStores;

namespace RetailPOS.Infrastructure.Uom;

internal sealed class UomTransaction(IDbContextTransaction? transaction) : IUomTransaction
{
    public Task CommitAsync(CancellationToken cancellationToken) =>
        transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

    public Task RollbackAsync(CancellationToken cancellationToken) =>
        transaction?.RollbackAsync(cancellationToken) ?? Task.CompletedTask;

    public ValueTask DisposeAsync() => transaction?.DisposeAsync() ?? ValueTask.CompletedTask;
}

public sealed class VariantUomReader(RetailPOSDbContext dbContext) : IVariantUomReader
{
    public async Task<IReadOnlyDictionary<long, ResolvedVariantUom>> GetCurrentAsync(
        long businessId,
        IReadOnlyCollection<long> variantIds,
        CancellationToken cancellationToken)
    {
        var ids = variantIds.Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<long, ResolvedVariantUom>();

        // Inside a stock transaction the configuration cannot change until it commits, so a tracking switch never
        // classifies stock that an in-flight sale is about to move. Config saves hold FOR UPDATE on the same rows.
        if (dbContext.Database.IsNpgsql() && dbContext.Database.CurrentTransaction is not null)
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM product_variants WHERE id = ANY({ids.ToArray()}) AND business_id = {businessId} ORDER BY id FOR SHARE",
                cancellationToken);

        var configs = await dbContext.VariantUomConfigs
            .AsNoTracking()
            .AsSplitQuery()
            .Include(config => config.BaseUnit)
            .Include(config => config.PurchaseOptions).ThenInclude(option => option.Unit)
            .Include(config => config.SellingOptions).ThenInclude(option => option.Unit)
            .Include(config => config.SellingOptions).ThenInclude(option => option.Components)
            .Where(config => config.BusinessId == businessId
                && ids.Contains(config.VariantId)
                && config.EffectiveToUtc == null)
            .ToListAsync(cancellationToken);

        return configs.ToDictionary(config => config.VariantId, Map);
    }

    public async Task<IReadOnlySet<long>> GetSealedOptionIdsAsync(IReadOnlyCollection<long> optionIds, CancellationToken cancellationToken)
    {
        var ids = optionIds.Distinct().ToList();
        if (ids.Count == 0)
            return new HashSet<long>();
        var sealedIds = await dbContext.VariantPurchaseUnitOptions.AsNoTracking()
            .Where(option => ids.Contains(option.Id) && option.IsSealedPack)
            .Select(option => option.Id)
            .ToListAsync(cancellationToken);
        return sealedIds.ToHashSet();
    }

    private static ResolvedVariantUom Map(VariantUomConfig config) => new(
        config.Id,
        config.Version,
        config.VariantId,
        Unit(config.BaseUnit),
        config.PackStateTrackingEnabled,
        config.RepackingEnabled,
        config.PurchaseOptions
            .Where(option => option.IsActive)
            .OrderBy(option => option.Id)
            .Select(option => new ResolvedPurchaseOption(
                option.Id, option.Code, option.Label, Unit(option.Unit), option.FactorNumerator, option.FactorDenominator,
                option.IsDefault, option.IsSealedPack))
            .ToList(),
        config.SellingOptions
            .Where(option => option.IsActive)
            .OrderBy(option => option.Id)
            .Select(option => new ResolvedSellingOption(
                option.Id, option.Kind, option.Code, option.Label, option.Unit is null ? null : Unit(option.Unit),
                option.BaseQuantity, option.FactorNumerator, option.FactorDenominator, option.Price, option.IsDefault,
                option.SealedPackCode, option.IsPreparedEligible,
                option.Components.OrderBy(component => component.Id)
                    .Select(component => new ResolvedComponent(component.ComponentVariantId, component.Quantity, component.IsRequired))
                    .ToList()))
            .ToList());

    private static ResolvedUnit Unit(UomUnit unit) =>
        new(unit.Id, unit.Code, unit.Symbol, unit.Dimension, unit.QuantityPrecision, unit.IsWholeUnit);
}

public sealed class UomCatalogStore(RetailPOSDbContext dbContext) : IUomCatalogStore
{
    public async Task<IUomTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new UomTransaction(await dbContext.Database.BeginTransactionAsync(cancellationToken));

    public async Task<UomCatalogReadModel> GetCatalogAsync(long businessId, bool activeOnly, string? search, CancellationToken cancellationToken)
    {
        var query = dbContext.UomUnits.AsNoTracking()
            .Where(unit => unit.BusinessId == null || unit.BusinessId == businessId);
        if (activeOnly)
            query = query.Where(unit => unit.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(unit => unit.Code.ToLower().Contains(term)
                || unit.Name.ToLower().Contains(term)
                || unit.Symbol.ToLower().Contains(term));
        }

        var units = await query.OrderBy(unit => unit.Dimension).ThenBy(unit => unit.Name).Take(500).ToListAsync(cancellationToken);
        var ids = units.Select(unit => unit.Id).ToList();
        var used = await UsedUnitIdsAsync(ids, cancellationToken);
        var conversions = await dbContext.UomStandardConversions.AsNoTracking()
            .Where(conversion => conversion.IsActive)
            .OrderBy(conversion => conversion.Id)
            .Select(conversion => new UomConversionReadModel(
                conversion.FromUnitId, conversion.FromUnit.Symbol, conversion.ToUnitId, conversion.ToUnit.Symbol,
                conversion.Numerator, conversion.Denominator))
            .ToListAsync(cancellationToken);
        return new UomCatalogReadModel(
            units.Select(unit => new UomUnitReadModel(
                unit.Id, unit.BusinessId is null, unit.Code, unit.Name, unit.Symbol, unit.Dimension,
                unit.QuantityPrecision, unit.IsWholeUnit, unit.IsActive, used.Contains(unit.Id))).ToList(),
            conversions);
    }

    public Task<UomUnit?> FindBusinessUnitAsync(long businessId, long unitId, CancellationToken cancellationToken) =>
        dbContext.UomUnits.FirstOrDefaultAsync(unit => unit.Id == unitId && unit.BusinessId == businessId, cancellationToken);

    public Task<bool> CodeExistsAsync(long businessId, string code, long? excludingUnitId, CancellationToken cancellationToken)
    {
        var lower = code.ToLower();
        return dbContext.UomUnits.AnyAsync(unit =>
            (unit.BusinessId == null || unit.BusinessId == businessId)
            && unit.Code.ToLower() == lower
            && unit.Id != excludingUnitId, cancellationToken);
    }

    // Symbols are compared exactly: "m" and "M" can be different units.
    public Task<bool> SymbolExistsAsync(long businessId, string symbol, long? excludingUnitId, CancellationToken cancellationToken) =>
        dbContext.UomUnits.AnyAsync(unit =>
            (unit.BusinessId == null || unit.BusinessId == businessId)
            && unit.Symbol == symbol
            && unit.Id != excludingUnitId, cancellationToken);

    public async Task<bool> IsUnitUsedAsync(long unitId, CancellationToken cancellationToken) =>
        (await UsedUnitIdsAsync([unitId], cancellationToken)).Contains(unitId);

    private async Task<HashSet<long>> UsedUnitIdsAsync(List<long> ids, CancellationToken cancellationToken)
    {
        var used = await dbContext.VariantUomConfigs.AsNoTracking()
            .Where(config => ids.Contains(config.BaseUnitId))
            .Select(config => config.BaseUnitId)
            .Distinct()
            .ToListAsync(cancellationToken);
        used.AddRange(await dbContext.VariantPurchaseUnitOptions.AsNoTracking()
            .Where(option => ids.Contains(option.UnitId))
            .Select(option => option.UnitId)
            .Distinct()
            .ToListAsync(cancellationToken));
        used.AddRange(await dbContext.VariantSellingOptions.AsNoTracking()
            .Where(option => option.UnitId != null && ids.Contains(option.UnitId.Value))
            .Select(option => option.UnitId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken));
        return used.ToHashSet();
    }

    public async Task AddAsync(UomUnit unit, CancellationToken cancellationToken) =>
        await dbContext.UomUnits.AddAsync(unit, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public async Task<IReadOnlyList<ResolvedUnit>> GetUnitsAsync(long businessId, IReadOnlyCollection<long> unitIds, CancellationToken cancellationToken)
    {
        var ids = unitIds.ToList();
        return await dbContext.UomUnits.AsNoTracking()
            .Where(unit => ids.Contains(unit.Id) && unit.IsActive && (unit.BusinessId == null || unit.BusinessId == businessId))
            .Select(unit => new ResolvedUnit(unit.Id, unit.Code, unit.Symbol, unit.Dimension, unit.QuantityPrecision, unit.IsWholeUnit))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StandardConversionRow>> GetStandardConversionsAsync(CancellationToken cancellationToken) =>
        await dbContext.UomStandardConversions.AsNoTracking()
            .Where(conversion => conversion.IsActive)
            .Select(conversion => new StandardConversionRow(conversion.FromUnitId, conversion.ToUnitId, conversion.Numerator, conversion.Denominator))
            .ToListAsync(cancellationToken);
}

public sealed class VariantUomConfigStore(RetailPOSDbContext dbContext) : IVariantUomConfigStore
{
    public Task<VariantUomContext?> FindVariantAsync(long businessId, long variantId, CancellationToken cancellationToken) =>
        dbContext.ProductVariants.AsNoTracking()
            .Where(variant => variant.Id == variantId && variant.BusinessId == businessId)
            .Select(variant => new VariantUomContext(variant.Id, variant.BusinessId, variant.Name, variant.Sku, variant.Product.Name))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> HasStockActivityAsync(long variantId, CancellationToken cancellationToken) =>
        await dbContext.Inventories.AnyAsync(row => row.VariantId == variantId && row.Quantity != 0m, cancellationToken)
        || await dbContext.StockLedgers.AnyAsync(row => row.VariantId == variantId, cancellationToken)
        || await dbContext.SaleItems.AnyAsync(row => row.VariantId == variantId, cancellationToken)
        || await dbContext.PurchaseOrderItems.AnyAsync(row => row.VariantId == variantId, cancellationToken)
        || await dbContext.StockTransferItems.AnyAsync(row => row.VariantId == variantId, cancellationToken)
        || await dbContext.StockAdjustmentLines.AnyAsync(row => row.VariantId == variantId, cancellationToken);

    public async Task<IReadOnlyList<VariantUnitConfigHistoryRow>> GetHistoryAsync(long businessId, long variantId, CancellationToken cancellationToken) =>
        await dbContext.VariantUomConfigs.AsNoTracking()
            .Where(config => config.BusinessId == businessId && config.VariantId == variantId)
            .OrderByDescending(config => config.Version)
            .Select(config => new VariantUnitConfigHistoryRow(
                config.Id, config.Version, config.BaseUnit.Code, config.EffectiveFromUtc, config.EffectiveToUtc, config.CreatedBy))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<long>> FindBusinessVariantIdsAsync(long businessId, IReadOnlyCollection<long> variantIds, CancellationToken cancellationToken)
    {
        var ids = variantIds.ToList();
        return await dbContext.ProductVariants.AsNoTracking()
            .Where(variant => variant.BusinessId == businessId && ids.Contains(variant.Id))
            .Select(variant => variant.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockedLocation>> GetStockedLocationsAsync(long variantId, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql() && dbContext.Database.CurrentTransaction is not null)
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM inventories WHERE variant_id = {variantId} ORDER BY id FOR UPDATE", cancellationToken);
        return await dbContext.Inventories.AsNoTracking()
            .Where(row => row.VariantId == variantId)
            .Select(row => new StockedLocation(row.LocationId, row.LocationType, row.Quantity))
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasPhysicalPackBalancesAsync(long variantId, CancellationToken cancellationToken) =>
        dbContext.PackStateBalances.AnyAsync(row => row.VariantId == variantId && row.State != PackStates.Loose && row.PackCount > 0, cancellationToken);

    public Task<bool> IsUsedAsPackagingAsync(long variantId, CancellationToken cancellationToken) =>
        dbContext.SellingOptionComponents.AnyAsync(row => row.ComponentVariantId == variantId && row.SellingOption.Config.EffectiveToUtc == null, cancellationToken);

    public async Task<IUomTransaction> BeginAsync(long variantId, CancellationToken cancellationToken)
    {
        var transaction = new UomTransaction(await dbContext.Database.BeginTransactionAsync(cancellationToken));
        if (dbContext.Database.IsNpgsql())
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM product_variants WHERE id = {variantId} FOR UPDATE", cancellationToken);
        return transaction;
    }

    public Task<VariantUomConfig?> FindCurrentForUpdateAsync(long businessId, long variantId, CancellationToken cancellationToken) =>
        dbContext.VariantUomConfigs.FirstOrDefaultAsync(
            config => config.BusinessId == businessId && config.VariantId == variantId && config.EffectiveToUtc == null,
            cancellationToken);

    public async Task AddAsync(VariantUomConfig config, CancellationToken cancellationToken) =>
        await dbContext.VariantUomConfigs.AddAsync(config, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class PackStateLedger(RetailPOSDbContext dbContext, ICompanyTimeZoneProvider timeZones) : IPackStateLedger
{
    /// <summary>One lock order everywhere: the stock row first, then its balances. A sale's flush takes them in that order.</summary>
    private async Task LockInventoryAsync(long variantId, long locationId, string locationType, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql() && dbContext.Database.CurrentTransaction is not null)
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM inventories WHERE variant_id = {variantId} AND location_id = {locationId} AND location_type = {locationType} FOR UPDATE",
                cancellationToken);
    }

    public async Task<IReadOnlyList<PackStateBalanceRow>> GetBalancesForUpdateAsync(
        long businessId, long variantId, long locationId, string locationType, CancellationToken cancellationToken)
    {
        await LockInventoryAsync(variantId, locationId, locationType, cancellationToken);
        if (dbContext.Database.IsNpgsql() && dbContext.Database.CurrentTransaction is not null)
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM pack_state_balances WHERE business_id = {businessId} AND variant_id = {variantId} AND location_id = {locationId} AND location_type = {locationType} ORDER BY id FOR UPDATE",
                cancellationToken);

        var rows = await dbContext.PackStateBalances
            .Where(row => row.BusinessId == businessId && row.VariantId == variantId && row.LocationId == locationId && row.LocationType == locationType)
            .ToListAsync(cancellationToken);
        return rows
            .Where(row => row.BaseQuantity != 0m || row.PackCount != 0)
            .Select(row => new PackStateBalanceRow(row.State, row.PackCode, row.BaseQtyPerPack, row.PackCount, row.BaseQuantity, row.CapitalizedValue))
            .ToList();
    }

    public async Task ApplyAsync(PackStateChange change, CancellationToken cancellationToken)
    {
        await LockInventoryAsync(change.VariantId, change.LocationId, change.LocationType, cancellationToken);
        var balance = await FindOrCreateAsync(change, cancellationToken);
        var nextCount = balance.PackCount + change.PackCountDelta;
        var nextQuantity = balance.BaseQuantity + change.BaseQuantityDelta;
        var nextValue = balance.CapitalizedValue + change.CapitalizedValueDelta;
        if (nextCount < 0 || nextQuantity < 0m || nextValue < 0m)
            throw new UomValidationException(
                change.State switch
                {
                    PackStates.Sealed => UomErrorCodes.InsufficientSealedStock,
                    PackStates.Prepared => UomErrorCodes.InsufficientPreparedStock,
                    _ => UomErrorCodes.InsufficientLooseStock
                },
                change.State == PackStates.Loose
                    ? $"Not enough loose stock. Available: {UomQuantityMath.Normalize(balance.BaseQuantity)}, needed: {UomQuantityMath.Normalize(-change.BaseQuantityDelta)}. Open a sealed pack first if one is available."
                    : $"Not enough {change.State} stock of '{change.PackCode}'. Available: {balance.PackCount}, needed: {-change.PackCountDelta}.");

        balance.PackCount = nextCount;
        balance.BaseQuantity = UomQuantityMath.Normalize(nextQuantity);
        balance.CapitalizedValue = nextValue;
        balance.UpdatedAt = DateTime.UtcNow;

        var businessDate = await timeZones.GetTodayAsync(change.BusinessId, cancellationToken);
        dbContext.PackStateMovements.Add(new PackStateMovement
        {
            BusinessId = change.BusinessId,
            VariantId = change.VariantId,
            LocationId = change.LocationId,
            LocationType = change.LocationType,
            OperationType = change.OperationType,
            State = change.State,
            PackCode = change.PackCode,
            BaseQtyPerPack = change.BaseQtyPerPack,
            PackCountDelta = change.PackCountDelta,
            BaseQuantityDelta = UomQuantityMath.Normalize(change.BaseQuantityDelta),
            CapitalizedValueDelta = change.CapitalizedValueDelta,
            ReferenceType = change.ReferenceType,
            ReferenceId = change.ReferenceId,
            IdempotencyKey = change.IdempotencyKey,
            ActorUserId = change.ActorUserId,
            BusinessDate = businessDate.Date,
            CreatedAt = DateTime.UtcNow
        });
    }

    private async Task<PackStateBalance> FindOrCreateAsync(PackStateChange change, CancellationToken cancellationToken)
    {
        var local = dbContext.PackStateBalances.Local.FirstOrDefault(row =>
            row.VariantId == change.VariantId && row.LocationId == change.LocationId && row.LocationType == change.LocationType
            && row.State == change.State && row.PackCode == change.PackCode && row.BaseQtyPerPack == change.BaseQtyPerPack);
        // A row already tracked may hold this transaction's unsaved changes, so it is used as it stands; xmin guards it.
        if (local is not null)
            return local;

        var balance = await dbContext.PackStateBalances.FirstOrDefaultAsync(row =>
            row.VariantId == change.VariantId && row.LocationId == change.LocationId && row.LocationType == change.LocationType
            && row.State == change.State && row.PackCode == change.PackCode && row.BaseQtyPerPack == change.BaseQtyPerPack,
            cancellationToken);
        if (balance is not null)
        {
            if (dbContext.Database.IsNpgsql() && dbContext.Database.CurrentTransaction is not null)
            {
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT id FROM pack_state_balances WHERE id = {balance.Id} FOR UPDATE", cancellationToken);
                await dbContext.Entry(balance).ReloadAsync(cancellationToken);
            }

            return balance;
        }

        balance = new PackStateBalance
        {
            BusinessId = change.BusinessId,
            VariantId = change.VariantId,
            LocationId = change.LocationId,
            LocationType = change.LocationType,
            State = change.State,
            PackCode = change.PackCode,
            BaseQtyPerPack = change.BaseQtyPerPack
        };
        dbContext.PackStateBalances.Add(balance);
        return balance;
    }

    public async Task EnsureReconciledAsync(
        long businessId, long variantId, long locationId, string locationType, decimal onHand, CancellationToken cancellationToken)
    {
        var saved = await dbContext.PackStateBalances.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.VariantId == variantId && row.LocationId == locationId && row.LocationType == locationType)
            .Select(row => new { row.Id, row.BaseQuantity })
            .ToListAsync(cancellationToken);
        // Rows changed in this unit of work but not yet saved count at their pending value.
        var pending = dbContext.PackStateBalances.Local
            .Where(row => row.BusinessId == businessId && row.VariantId == variantId && row.LocationId == locationId && row.LocationType == locationType)
            .ToDictionary(row => row.Id == 0 ? -row.GetHashCode() : row.Id, row => row.BaseQuantity);
        foreach (var row in saved)
            pending.TryAdd(row.Id, row.BaseQuantity);

        var allocated = pending.Values.Sum();
        if (allocated != onHand)
            throw new UomConflictException(
                UomErrorCodes.PackStateMismatch,
                $"Physical allocations of variant {variantId} ({UomQuantityMath.Normalize(allocated)}) do not match stock on hand ({UomQuantityMath.Normalize(onHand)}). Nothing was changed.");
    }
}

public sealed class PackStateStore(RetailPOSDbContext dbContext) : IPackStateStore
{
    public async Task<IReadOnlyList<PackStateBalanceRow>> ReadBalancesAsync(long businessId, long variantId, long locationId, string locationType, CancellationToken cancellationToken) =>
        await dbContext.PackStateBalances.AsNoTracking()
            .Where(row => row.BusinessId == businessId && row.VariantId == variantId && row.LocationId == locationId && row.LocationType == locationType
                && (row.BaseQuantity != 0m || row.PackCount != 0))
            .OrderBy(row => row.State)
            .ThenBy(row => row.PackCode)
            .Select(row => new PackStateBalanceRow(row.State, row.PackCode, row.BaseQtyPerPack, row.PackCount, row.BaseQuantity, row.CapitalizedValue))
            .ToListAsync(cancellationToken);

    public async Task<decimal> GetOnHandAsync(long variantId, long locationId, string locationType, CancellationToken cancellationToken) =>
        await dbContext.Inventories.AsNoTracking()
            .Where(row => row.VariantId == variantId && row.LocationId == locationId && row.LocationType == locationType)
            .Select(row => (decimal?)row.Quantity)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

    public Task<bool> IdempotencyKeyUsedAsync(long businessId, string key, CancellationToken cancellationToken) =>
        dbContext.PackStateMovements.AnyAsync(row => row.BusinessId == businessId && row.IdempotencyKey == key, cancellationToken);

    public async Task<IUomTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new UomTransaction(await dbContext.Database.BeginTransactionAsync(cancellationToken));

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}

public sealed class RepackStore(RetailPOSDbContext dbContext) : IRepackStore
{
    public async Task<IUomTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new UomTransaction(await dbContext.Database.BeginTransactionAsync(cancellationToken));

    public async Task<RepackJob?> FindForUpdateAsync(long businessId, long jobId, CancellationToken cancellationToken)
    {
        // The row lock makes two concurrent executions run one after the other, so the second sees the final status.
        if (dbContext.Database.IsNpgsql() && dbContext.Database.CurrentTransaction is not null)
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM repack_jobs WHERE id = {jobId} AND business_id = {businessId} FOR UPDATE", cancellationToken);
        return await dbContext.RepackJobs
            .Include(job => job.Components)
            .FirstOrDefaultAsync(job => job.Id == jobId && job.BusinessId == businessId, cancellationToken);
    }

    public Task<RepackJob?> FindByIdempotencyKeyAsync(long businessId, string key, CancellationToken cancellationToken) =>
        dbContext.RepackJobs.AsNoTracking().FirstOrDefaultAsync(job => job.BusinessId == businessId && job.IdempotencyKey == key, cancellationToken);

    public async Task<string> NextReferenceAsync(long businessId, CancellationToken cancellationToken)
    {
        var count = await dbContext.RepackJobs.CountAsync(job => job.BusinessId == businessId, cancellationToken);
        return $"RPK-{DateTime.UtcNow.Year}-{count + 1:D5}";
    }

    public async Task AddAsync(RepackJob job, CancellationToken cancellationToken) =>
        await dbContext.RepackJobs.AddAsync(job, cancellationToken);

    public async Task<Inventory?> FindInventoryForUpdateAsync(long variantId, long locationId, string locationType, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql() && dbContext.Database.CurrentTransaction is not null)
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM inventories WHERE variant_id = {variantId} AND location_id = {locationId} AND location_type = {locationType} FOR UPDATE",
                cancellationToken);
        return await dbContext.Inventories.FirstOrDefaultAsync(
            row => row.VariantId == variantId && row.LocationId == locationId && row.LocationType == locationType,
            cancellationToken);
    }

    public async Task<Inventory> GetOrCreateInventoryForUpdateAsync(long variantId, long locationId, string locationType, CancellationToken cancellationToken)
    {
        var inventory = await FindInventoryForUpdateAsync(variantId, locationId, locationType, cancellationToken);
        if (inventory is not null)
            return inventory;
        inventory = new Inventory { VariantId = variantId, LocationId = locationId, LocationType = locationType, Quantity = 0m };
        dbContext.Inventories.Add(inventory);
        return inventory;
    }

    public async Task<StockCostPosition> GetCostPositionAsync(long businessId, long variantId, long locationId, string locationType, decimal onHand, CancellationToken cancellationToken)
    {
        var ledger = await StockCostReader.ReadVariantAsync(dbContext, businessId, variantId, cancellationToken);
        var evidence = await StockCostReader.ReadEvidenceAsync(dbContext, ledger, cancellationToken);
        return ledger.Classify(variantId, new StockCostLocation(locationId, locationType), onHand, evidence);
    }

    public Task<bool> LocationBelongsToBusinessAsync(long businessId, long locationId, string locationType, CancellationToken cancellationToken) =>
        locationType == "outlet"
            ? dbContext.Outlets.AnyAsync(outlet => outlet.Id == locationId && outlet.BusinessId == businessId, cancellationToken)
            : dbContext.Warehouses.AnyAsync(warehouse => warehouse.Id == locationId && warehouse.BusinessId == businessId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public async Task<RepackJobReadModel?> ReadAsync(long businessId, long jobId, CancellationToken cancellationToken)
    {
        var rows = await ReadJobsAsync(dbContext.RepackJobs.Where(job => job.BusinessId == businessId && job.Id == jobId), cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task<RepackJobPage> ListAsync(
        long businessId,
        long? locationId,
        string? locationType,
        string? status,
        int page,
        int pageSize,
        IReadOnlyList<long>? readableOutlets,
        IReadOnlyList<long>? readableWarehouses,
        CancellationToken cancellationToken)
    {
        var query = dbContext.RepackJobs.Where(job => job.BusinessId == businessId);
        if (readableOutlets is not null && readableWarehouses is not null)
        {
            var outlets = readableOutlets.ToList();
            var warehouses = readableWarehouses.ToList();
            query = query.Where(job =>
                (job.LocationType == "outlet" && outlets.Contains(job.LocationId))
                || (job.LocationType == "warehouse" && warehouses.Contains(job.LocationId)));
        }

        if (locationId.HasValue)
            query = query.Where(job => job.LocationId == locationId.Value);
        if (!string.IsNullOrWhiteSpace(locationType))
            query = query.Where(job => job.LocationType == locationType);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(job => job.Status == status);
        var total = await query.CountAsync(cancellationToken);
        var items = await ReadJobsAsync(
            query.OrderByDescending(job => job.Id).Skip((page - 1) * pageSize).Take(pageSize),
            cancellationToken);
        return new RepackJobPage(items, total);
    }

    private async Task<IReadOnlyList<RepackJobReadModel>> ReadJobsAsync(IQueryable<RepackJob> query, CancellationToken cancellationToken)
    {
        var jobs = await query.AsNoTracking()
            .AsSplitQuery()
            .Include(job => job.SourceVariant).ThenInclude(variant => variant.Product)
            .Include(job => job.Components).ThenInclude(line => line.ComponentVariant).ThenInclude(variant => variant.Product)
            .ToListAsync(cancellationToken);
        var variantIds = jobs.Select(job => job.SourceVariantId).Distinct().ToList();
        var baseUnits = await dbContext.VariantUomConfigs.AsNoTracking()
            .Where(config => variantIds.Contains(config.VariantId) && config.EffectiveToUtc == null)
            .Select(config => new { config.VariantId, config.BaseUnit.Code })
            .ToDictionaryAsync(row => row.VariantId, row => row.Code, cancellationToken);
        return jobs.Select(job => new RepackJobReadModel(
            job.Id, job.Reference, job.Status, job.LocationId, job.LocationType, job.SourceVariantId,
            job.SourceVariant.Product.Name, job.SourceVariant.Sku, job.OutputPackCode, job.OutputLabel, job.PackBaseQuantity,
            job.OutputCount, job.ContentBaseQuantity, baseUnits.GetValueOrDefault(job.SourceVariantId), job.ContentUnitCost, job.ContentValue,
            job.PackagingValue,
            job.ContentValue.HasValue && job.PackagingValue.HasValue
                ? Math.Round((job.ContentValue.Value + job.PackagingValue.Value) / job.OutputCount, 2, MidpointRounding.AwayFromZero)
                : null,
            job.Notes, job.CreatedAt, job.ExecutedAt, job.ReversedAt,
            job.Components.OrderBy(line => line.Id).Select(line => new RepackComponentReadModel(
                line.ComponentVariantId, line.ComponentVariant.Product.Name, line.ComponentVariant.Sku, line.Quantity, line.UnitCost, line.Value)).ToList()))
            .ToList();
    }
}

public sealed class StockCountEntryStore(RetailPOSDbContext dbContext) : IStockCountEntryStore
{
    public async Task<(long BusinessId, long LocationId, string LocationType, string Status, long VariantId, decimal CurrentStock)?> FindLineAsync(
        long stockCountId, long lineId, CancellationToken cancellationToken)
    {
        var row = await dbContext.StockCountLines.AsNoTracking()
            .Where(line => line.Id == lineId && line.StockCountId == stockCountId)
            .Select(line => new
            {
                line.StockCount.BusinessId,
                line.StockCount.LocationId,
                line.StockCount.LocationType,
                line.StockCount.Status,
                line.VariantId,
                line.CurrentStock
            })
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : (row.BusinessId, row.LocationId, row.LocationType, row.Status, row.VariantId, row.CurrentStock);
    }

    public async Task SaveEntriesAsync(long stockCountId, long lineId, IReadOnlyList<StockCountEntryRecord> entries, decimal physicalStock, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsNpgsql())
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM stock_counts WHERE id = {stockCountId} FOR UPDATE", cancellationToken);

        var stockCount = await dbContext.StockCounts.FirstAsync(count => count.Id == stockCountId, cancellationToken);
        if (stockCount.Status != StockCount.StatusDraft)
            throw new InvalidOperationException("Counted quantities can only be changed while the stock count is in Draft status.");

        var line = await dbContext.StockCountLines
            .Include(candidate => candidate.Entries)
            .FirstAsync(candidate => candidate.Id == lineId, cancellationToken);
        dbContext.StockCountLineEntries.RemoveRange(line.Entries);
        foreach (var record in entries)
        {
            var entry = new StockCountLineEntry
            {
                BaseQuantity = record.BaseQuantity,
                State = record.State,
                PackCode = record.PackCode,
                BaseQtyPerPack = record.BaseQtyPerPack,
                PackCount = record.PackCount
            };
            entry.CaptureUomSnapshot(record.Snapshot);
            line.Entries.Add(entry);
        }

        line.PhysicalStock = physicalStock;
        line.Difference = physicalStock - line.CurrentStock;
        stockCount.CountRevision++;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
