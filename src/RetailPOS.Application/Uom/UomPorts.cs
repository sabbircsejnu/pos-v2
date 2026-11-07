using RetailPOS.Core.Entities.Uom;

namespace RetailPOS.Application.Uom;

/// <summary>Reads the current unit configuration of variants inside the caller's Business.</summary>
public interface IVariantUomReader
{
    /// <returns>Only configured variants appear. A missing key means plain pcs.</returns>
    Task<IReadOnlyDictionary<long, ResolvedVariantUom>> GetCurrentAsync(
        long businessId,
        IReadOnlyCollection<long> variantIds,
        CancellationToken cancellationToken);

    /// <summary>Of these option ids from any version, the ones that arrive as sealed packs. Option rows never change.</summary>
    Task<IReadOnlySet<long>> GetSealedOptionIdsAsync(IReadOnlyCollection<long> optionIds, CancellationToken cancellationToken);
}

public sealed record PackStateChange(
    long BusinessId,
    long VariantId,
    long LocationId,
    string LocationType,
    string State,
    string PackCode,
    decimal BaseQtyPerPack,
    int PackCountDelta,
    decimal BaseQuantityDelta,
    decimal CapitalizedValueDelta,
    string OperationType,
    string ReferenceType,
    long ReferenceId,
    long? ActorUserId,
    string? IdempotencyKey = null);

public sealed record PackStateBalanceRow(
    string State,
    string PackCode,
    decimal BaseQtyPerPack,
    int PackCount,
    decimal BaseQuantity,
    decimal CapitalizedValue);

/// <summary>
/// The only writer of pack-state balances. It joins the caller's transaction, locks each balance row, refuses to go
/// negative and writes one immutable movement per change. It posts no stock and no journal: callers do that.
/// </summary>
public interface IPackStateLedger
{
    Task<IReadOnlyList<PackStateBalanceRow>> GetBalancesForUpdateAsync(
        long businessId, long variantId, long locationId, string locationType, CancellationToken cancellationToken);

    Task ApplyAsync(PackStateChange change, CancellationToken cancellationToken);

    /// <summary>Fails unless the location's allocations sum exactly to the authoritative stock quantity.</summary>
    Task EnsureReconciledAsync(
        long businessId, long variantId, long locationId, string locationType, decimal onHand, CancellationToken cancellationToken);
}

public sealed record VariantUomContext(long VariantId, long BusinessId, string VariantName, string Sku, string ProductName);

public sealed record StockedLocation(long LocationId, string LocationType, decimal Quantity);

public sealed record UomUnitInput(string Code, string Name, string Symbol, string Dimension, int QuantityPrecision, bool IsWholeUnit);

public sealed record UomUnitReadModel(
    long Id,
    bool IsGlobal,
    string Code,
    string Name,
    string Symbol,
    string Dimension,
    int QuantityPrecision,
    bool IsWholeUnit,
    bool IsActive,
    bool IsInUse);

public sealed record UomConversionReadModel(long FromUnitId, string FromSymbol, long ToUnitId, string ToSymbol, decimal Numerator, decimal Denominator);

public sealed record UomCatalogReadModel(IReadOnlyList<UomUnitReadModel> Units, IReadOnlyList<UomConversionReadModel> Conversions);

public interface IUomCatalogStore
{
    Task<IUomTransaction> BeginAsync(CancellationToken cancellationToken);
    Task<UomCatalogReadModel> GetCatalogAsync(long businessId, bool activeOnly, string? search, CancellationToken cancellationToken);
    Task<UomUnit?> FindBusinessUnitAsync(long businessId, long unitId, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(long businessId, string code, long? excludingUnitId, CancellationToken cancellationToken);
    Task<bool> SymbolExistsAsync(long businessId, string symbol, long? excludingUnitId, CancellationToken cancellationToken);
    Task<bool> IsUnitUsedAsync(long unitId, CancellationToken cancellationToken);
    Task AddAsync(UomUnit unit, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ResolvedUnit>> GetUnitsAsync(long businessId, IReadOnlyCollection<long> unitIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<StandardConversionRow>> GetStandardConversionsAsync(CancellationToken cancellationToken);
}

public sealed record PurchaseOptionInput(string Code, long UnitId, string Label, decimal FactorNumerator, decimal FactorDenominator, bool IsDefault, bool IsSealedPack);

public sealed record SellingComponentInput(long ComponentVariantId, decimal Quantity, bool IsRequired);

public sealed record SellingOptionInput(
    string Kind,
    string Code,
    string Label,
    long? UnitId,
    decimal? BaseQuantity,
    decimal Price,
    bool IsDefault,
    string? SealedPackCode,
    bool IsPreparedEligible,
    IReadOnlyList<SellingComponentInput> Components);

public sealed record VariantUomConfigInput(
    long BaseUnitId,
    bool PackStateTrackingEnabled,
    bool RepackingEnabled,
    IReadOnlyList<PurchaseOptionInput> PurchaseOptions,
    IReadOnlyList<SellingOptionInput> SellingOptions);

public sealed record VariantUnitConfigHistoryRow(long ConfigId, int Version, string BaseUnitCode, DateTime EffectiveFromUtc, DateTime? EffectiveToUtc, long? CreatedBy);

public sealed record VariantUnitConfigReadModel(
    long VariantId,
    bool HasStockActivity,
    ResolvedVariantUom? Current,
    IReadOnlyList<VariantUnitConfigHistoryRow> History);

public interface IVariantUomConfigStore
{
    Task<VariantUomContext?> FindVariantAsync(long businessId, long variantId, CancellationToken cancellationToken);
    Task<bool> HasStockActivityAsync(long variantId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VariantUnitConfigHistoryRow>> GetHistoryAsync(long businessId, long variantId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<long>> FindBusinessVariantIdsAsync(long businessId, IReadOnlyCollection<long> variantIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<StockedLocation>> GetStockedLocationsAsync(long variantId, CancellationToken cancellationToken);
    Task<bool> HasPhysicalPackBalancesAsync(long variantId, CancellationToken cancellationToken);
    Task<bool> IsUsedAsPackagingAsync(long variantId, CancellationToken cancellationToken);

    /// <summary>Opens the transaction and serializes writers of this variant's configuration.</summary>
    Task<IUomTransaction> BeginAsync(long variantId, CancellationToken cancellationToken);
    Task<VariantUomConfig?> FindCurrentForUpdateAsync(long businessId, long variantId, CancellationToken cancellationToken);
    Task AddAsync(VariantUomConfig config, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IUomTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
}

/// <summary>The caller's identity and location rights for unit and pack-state operations.</summary>
public interface IUomOperationScope
{
    long RequireBusinessId();
    long RequireActorUserId();
    Task AuthorizeWriteLocationAsync(long locationId, string locationType, CancellationToken cancellationToken);
    Task AuthorizeReadLocationAsync(long locationId, string locationType, CancellationToken cancellationToken);

    /// <summary>Locations the caller may read stock at. Null lists mean every location of the business.</summary>
    Task<(IReadOnlyList<long>? OutletIds, IReadOnlyList<long>? WarehouseIds)> GetReadableLocationsAsync(CancellationToken cancellationToken);
}

public interface IUomAuditWriter
{
    Task RecordAsync(string summary, string entityType, string entityId, IReadOnlyDictionary<string, object?> metadata, CancellationToken cancellationToken);
}
