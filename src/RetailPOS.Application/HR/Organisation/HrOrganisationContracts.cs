using RetailPOS.Application.Integration;

namespace RetailPOS.Application.HR.Organisation;

public enum HrOrganisationOperation
{
    ViewOrgUnits,
    ManageOrgUnits,
    ViewOperationalLocations,
    ReconcileOperationalLocations,
    ViewPositions,
    ManagePositions,
    ViewEmployees,
    CreateEmployees,
    EditEmployees,
    ViewEmployeeLifecycle,
    ManageEmployeeLifecycle,
    RehireEmployee,
    ManageEmployeePositionAssignments,
    LinkEmployeeUser,
    UnlinkEmployeeUser,
    ResolveCurrentEmployee,
    ViewEmployeeLocationAssignments,
    ManageEmployeeLocationAssignments,
    RequestEmployeeTransfer,
    ApproveEmployeeTransfer,
    CancelEmployeeTransfer,
    ImportEmployees,
    ViewImports,
    ViewShifts,
    ManageShifts,
    ViewHolidays,
    ManageHolidays,
    ViewWeeklyOff,
    ManageWeeklyOff,
    ViewRosters,
    ManageRosters,
    ViewDevices,
    ManageDevices,
    ViewDeviceMappings,
    ManageDeviceMappings,
    ViewAttendance,
    ViewAttendanceReports,
    CreateManualAttendancePunch,
    ViewRawAttendancePunches,
    ProcessAttendance,
    CorrectAttendance
}

public sealed record OperationalLocationSourceIdentity(string EntityType, string EntityId);

public sealed record OperationalLocationReadModel(
    long Id,
    long BusinessId,
    string SourceSystem,
    string SourceEntityType,
    string SourceEntityId,
    string Name,
    string Address,
    string? ContactNumber,
    string? ManagerExternalUserId,
    long SourceVersion,
    DateTime SourceUpdatedAtUtc,
    DateTime LastSyncedAtUtc,
    bool IsSourceActive,
    bool IsSourceDeleted,
    string SyncStatus);

public sealed record HrOrgUnitReadModel(
    long Id,
    long BusinessId,
    string Code,
    string Name,
    string UnitType,
    long? ParentOrgUnitId,
    long? OperationalLocationProjectionId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    long Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record HrOrgUnitWriteData(
    string Code,
    string Name,
    string UnitType,
    long? ParentOrgUnitId = null,
    long? OperationalLocationProjectionId = null,
    DateOnly? EffectiveFrom = null,
    DateOnly? EffectiveTo = null,
    bool IsActive = true);

public sealed record HrOperationalLocationReadScope(
    long BusinessId,
    IReadOnlyCollection<OperationalLocationSourceIdentity>? AllowedSources);

public sealed record OperationalLocationReconciliationResult(
    long BusinessId,
    int OutletRecordsRead,
    int WarehouseRecordsRead,
    int ProjectionsUpdated,
    int StaleSourcesIgnored);

public interface IHrOrganisationAccess
{
    long RequireBusinessId();
    long RequireRealUserId();
    Task EnsureAsync(
        long businessId,
        HrOrganisationOperation operation,
        long? locationProjectionId,
        CancellationToken cancellationToken);
    Task<HrOperationalLocationReadScope> GetLocationReadScopeAsync(
        long businessId,
        CancellationToken cancellationToken);
    Task EnsureLocationAccessibleAsync(
        long businessId,
        long locationProjectionId,
        CancellationToken cancellationToken);
}

public interface IHrOrganisationReadStore
{
    Task<bool> IsLocationAccessibleAsync(
        long businessId,
        long projectionId,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<HrOrgUnitReadModel>> GetOrgUnitsAsync(
        long businessId,
        string? search,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<OperationalLocationReadModel>> GetOperationalLocationsAsync(
        long businessId,
        string? sourceEntityType,
        string? search,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken);
    Task<OperationalLocationReadModel?> GetOperationalLocationAsync(
        long businessId,
        long projectionId,
        HrOperationalLocationReadScope scope,
        CancellationToken cancellationToken);
}

public interface IHrOrgUnitWriteStore
{
    Task<long?> GetLocationProjectionIdAsync(
        long businessId,
        long orgUnitId,
        CancellationToken cancellationToken);
    Task<HrOrgUnitReadModel> CreateAsync(
        long businessId,
        long actorUserId,
        HrOrgUnitWriteData data,
        CancellationToken cancellationToken);
    Task<HrOrgUnitReadModel?> UpdateAsync(
        long businessId,
        long actorUserId,
        long orgUnitId,
        long expectedVersion,
        HrOrgUnitWriteData data,
        CancellationToken cancellationToken);
}

public interface IOperationalLocationProjectionWriteStore
{
    Task<ProjectionUpsertResult> UpsertFromSourceAsync(
        OperationalLocationSourceEvent source,
        CancellationToken cancellationToken);
}

public enum ProjectionUpsertResult
{
    Inserted,
    Updated,
    Unchanged,
    Stale
}

public sealed class HrOrganisationConcurrencyException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);

public interface IOperationalLocationSourceReadStore
{
    Task<IReadOnlyList<OperationalLocationSourceEvent>> GetPageAsync(
        long businessId,
        string sourceEntityType,
        long afterId,
        int pageSize,
        CancellationToken cancellationToken);
}
