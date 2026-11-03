namespace RetailPOS.Application.HR.Organisation;

/// <summary>A Business-scoped reusable role, including retained legacy location/date evidence.</summary>
public sealed record HrPositionReadModel(
    long Id,
    long BusinessId,
    string Code,
    string Name,
    long OrgUnitId,
    long? OperationalLocationProjectionId,
    long? ReportsToPositionId,
    string Status,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    long Version);

/// <summary>
/// Maintains a reusable Department/Team role. Location and dates are accepted for legacy
/// request compatibility only; normal writes do not populate or change those stored values.
/// </summary>
public sealed record HrPositionWriteData(
    string Code,
    string Name,
    long OrgUnitId,
    long? OperationalLocationProjectionId = null,
    long? ReportsToPositionId = null,
    string Status = "Vacant",
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null);

public interface IHrPositionStore
{
    Task<IReadOnlyList<HrPositionReadModel>> GetPositionsAsync(
        long businessId,
        string? search,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken);

    Task<long?> GetPositionLocationProjectionIdAsync(
        long businessId,
        long positionId,
        CancellationToken cancellationToken);

    Task<HrPositionReadModel> CreateAsync(
        long businessId,
        HrPositionWriteData data,
        CancellationToken cancellationToken);

    Task<HrPositionReadModel?> UpdateAsync(
        long businessId,
        long positionId,
        long expectedVersion,
        HrPositionWriteData data,
        CancellationToken cancellationToken);

    Task<HrPositionReadModel?> DeactivateAsync(
        long businessId,
        long positionId,
        long expectedVersion,
        DateOnly effectiveTo,
        CancellationToken cancellationToken);
}
