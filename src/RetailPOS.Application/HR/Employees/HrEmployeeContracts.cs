using RetailPOS.Application.HR.Organisation;

namespace RetailPOS.Application.HR.Employees;

public sealed record HrEmploymentTypeReadModel(long Id, string Code, string Name);

public sealed record HrEmployeeSearchItem(
    long Id,
    string EmployeeCode,
    string FirstName,
    string? MiddleName,
    string? LastName,
    string DisplayName,
    string? WorkEmail,
    string CurrentEmploymentStatus,
    long CurrentEmploymentTypeId,
    string CurrentEmploymentTypeName,
    DateOnly JoiningDate,
    long Version);

public sealed record HrEmployeeSensitiveFields(
    DateOnly? DateOfBirth,
    string? GenderCode,
    string? PersonalEmail,
    string? Mobile,
    string? PresentAddress,
    string? PermanentAddress,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? NationalId,
    string? PassportNo);

public sealed record HrEmployeeProfile(
    long Id,
    string EmployeeCode,
    string FirstName,
    string? MiddleName,
    string? LastName,
    string DisplayName,
    string? WorkEmail,
    string CurrentEmploymentStatus,
    long CurrentEmploymentTypeId,
    string CurrentEmploymentTypeName,
    long? CurrentPositionId,
    long? CurrentOrgUnitId,
    DateOnly JoiningDate,
    DateOnly? ConfirmationDate,
    long Version,
    HrEmployeeSensitiveFields SensitiveFields);

public sealed record HrEmployeeCreateData(
    string EmployeeCode,
    string FirstName,
    string? MiddleName,
    string? LastName,
    string DisplayName,
    DateOnly? DateOfBirth,
    string? GenderCode,
    string? NationalId,
    string? PassportNo,
    string? PersonalEmail,
    string? WorkEmail,
    string? Mobile,
    string? PresentAddress,
    string? PermanentAddress,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    long EmploymentTypeId,
    string EmploymentStatus,
    DateOnly JoiningDate,
    DateOnly? ConfirmationDate);

public sealed record HrEmployeeUpdateData(
    string FirstName,
    string? MiddleName,
    string? LastName,
    string DisplayName,
    DateOnly? DateOfBirth,
    string? GenderCode,
    string? NationalId,
    string? PassportNo,
    string? PersonalEmail,
    string? WorkEmail,
    string? Mobile,
    string? PresentAddress,
    string? PermanentAddress,
    string? EmergencyContactName,
    string? EmergencyContactPhone);

public sealed record HrEmployeeFieldAccess(bool CanViewSensitive, bool CanViewIdentity);

public sealed record HrEmployeeUserLinkReadModel(
    long Id,
    long EmployeeId,
    long IdentityUserId,
    DateTimeOffset LinkedAtUtc,
    long LinkedByUserId,
    string LinkReason,
    DateTimeOffset? UnlinkedAtUtc,
    long? UnlinkedByUserId,
    string? UnlinkReason,
    bool IsActive,
    long Version);

public sealed record HrEmployeeUserLinkState(HrEmployeeUserLinkReadModel? ActiveLink);

public sealed record HrEmployeeSelfReadModel(
    long Id,
    string EmployeeCode,
    string DisplayName,
    string CurrentEmploymentStatus);

public sealed record HrEmployeeLocationAssignmentReadModel(
    long Id,
    long EmployeeId,
    long OperationalLocationProjectionId,
    string LocationName,
    string SourceSystem,
    string SourceEntityType,
    string SourceEntityId,
    long LocationSourceVersion,
    string AssignmentType,
    bool IsTemporary,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Status,
    string Reason,
    long RequestedByUserId,
    DateTimeOffset RequestedAtUtc,
    long? ApprovedByUserId,
    DateTimeOffset? ApprovedAtUtc,
    long? ApprovalInstanceId,
    long Version);

public sealed record HrEmployeeLocationTransferReadModel(
    Guid Id,
    long EmployeeId,
    long TargetLocationProjectionId,
    string LocationName,
    string SourceSystem,
    string SourceEntityType,
    string SourceEntityId,
    DateOnly EffectiveFrom,
    string Status,
    string Reason,
    long RequestedByUserId,
    DateTimeOffset RequestedAtUtc,
    long? ApprovalInstanceId,
    long Version);

public sealed record HrEmployeeLocationTransferActivationResult(
    bool Changed,
    bool Activated,
    bool Superseded);

public sealed record GetHrEmployeeLocationTransfersQuery(long EmployeeId)
    : MediatR.IRequest<IReadOnlyList<HrEmployeeLocationTransferReadModel>>;

public sealed record HrEmployeeLocationTransferSubmissionResult(
    Guid TransferId,
    long ApprovalInstanceId,
    string Status);

public sealed record HrEmployeeLocationAssignmentData(
    long OperationalLocationProjectionId,
    string AssignmentType,
    bool IsTemporary,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string Reason);

public sealed record HrEmployeeLocationTransferData(
    long OperationalLocationProjectionId,
    DateOnly EffectiveFrom,
    long ExpectedEmployeeVersion,
    string Reason);

public sealed record HrEmployeeLocationTransferPayload(
    Guid TransferId,
    long EmployeeId,
    long OperationalLocationProjectionId,
    DateOnly EffectiveFrom,
    long ExpectedEmployeeVersion,
    string Reason);

public sealed record ProcessDueHrEmployeeLocationTransfersCommand(
    long BusinessId,
    DateOnly EffectiveThrough,
    int BatchSize = 100) : MediatR.IRequest<int>;

public interface IHrEmployeeLocationTransferActivationStore
{
    Task<IReadOnlyList<long>> GetBusinessesWithDueTransfersAsync(
        DateOnly effectiveThrough,
        int batchSize,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetDueTransferIdsAsync(
        long businessId,
        DateOnly effectiveThrough,
        int batchSize,
        CancellationToken cancellationToken);
    Task<HrEmployeeLocationTransferActivationResult> ActivateDueTransferAsync(
        long businessId,
        Guid transferId,
        DateOnly effectiveThrough,
        CancellationToken cancellationToken);
}

public static class HrEmployeeLocationTransferConstants
{
    public const string TargetType = "HrEmployeeLocationTransfer";
}

public interface IHrEmployeeLocationAssignmentStore
{
    Task<bool> IsEmployeeAccessibleAsync(
        long businessId,
        long employeeId,
        IReadOnlyCollection<OperationalLocationSourceIdentity>? allowedLocations,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<HrEmployeeLocationAssignmentReadModel>> GetHistoryAsync(
        long businessId,
        long employeeId,
        IReadOnlyCollection<OperationalLocationSourceIdentity>? allowedLocations,
        CancellationToken cancellationToken);
    Task<HrEmployeeLocationAssignmentReadModel?> GetEffectivePrimaryAsync(
        long businessId,
        long employeeId,
        CancellationToken cancellationToken);
    Task<HrEmployeeLocationAssignmentReadModel?> CreateAsync(
        long businessId,
        long actorUserId,
        long employeeId,
        long expectedEmployeeVersion,
        HrEmployeeLocationAssignmentData data,
        CancellationToken cancellationToken);
    Task<HrEmployeeLocationTransferReadModel?> GetTransferAsync(
        long businessId,
        long employeeId,
        Guid transferId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<HrEmployeeLocationTransferReadModel>> GetTransfersAsync(
        long businessId,
        long employeeId,
        IReadOnlyCollection<OperationalLocationSourceIdentity>? allowedLocations,
        CancellationToken cancellationToken);
    Task<long?> GetTransferApprovalInstanceIdAsync(
        long businessId,
        long employeeId,
        Guid transferId,
        CancellationToken cancellationToken);
    Task<bool> CancelScheduledTransferAsync(
        long businessId,
        long employeeId,
        Guid transferId,
        long actorUserId,
        string reason,
        CancellationToken cancellationToken);
}

public sealed record IdentityUserDirectoryEntry(long Id, long? BusinessId, bool IsActive);

public interface IIdentityUserDirectory
{
    Task<IdentityUserDirectoryEntry?> FindAsync(long identityUserId, CancellationToken cancellationToken);
}

public sealed record HrEmployeeLifecycleEventReadModel(
    long Id,
    long EmployeeId,
    long EmploymentPeriodId,
    string EventType,
    DateOnly EffectiveDate,
    string PreviousStateJson,
    string NewStateJson,
    string? Reason,
    long? ActorUserId,
    DateTimeOffset OccurredAtUtc);

public sealed record HrEmployeeLifecycleTransitionData(
    string EventType,
    DateOnly EffectiveDate,
    string? Reason,
    long? EmploymentTypeId = null);

public sealed record HrEmployeeRehireData(
    long EmploymentTypeId,
    string EmploymentStatus,
    DateOnly EffectiveDate,
    string? Reason);

public sealed record HrEmployeePositionAssignmentReadModel(
    long Id,
    long EmployeeId,
    long PositionId,
    string PositionCode,
    string PositionName,
    long OrgUnitId,
    string OrgUnitName,
    long? ReportsToEmployeeId,
    string? ReportsToEmployeeName,
    string AssignmentType,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Reason,
    bool IsCurrent,
    long Version);

public sealed record HrEmployeePositionAssignmentData(
    long PositionId,
    long OrgUnitId,
    long? ReportsToEmployeeId,
    string AssignmentType,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Reason);

public sealed record HrEmployeeSearchPage(
    IReadOnlyList<HrEmployeeSearchItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed class HrEmployeeConcurrencyException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed record HrEmploymentTypeDefinition(string Code, string Name);

public static class HrEmploymentTypeCatalog
{
    public static IReadOnlyList<HrEmploymentTypeDefinition> Defaults { get; } =
    [
        new("Permanent", "Permanent"),
        new("Probation", "Probation"),
        new("Contractual", "Contractual"),
        new("PartTime", "Part-time"),
        new("Temporary", "Temporary"),
        new("Intern", "Intern"),
        new("Other", "Other")
    ];
}

public interface IHrEmployeeAccess
{
    long RequireBusinessId();
    long RequireRealUserId();
    Task EnsureAsync(long businessId, HrEmployeeOperation operation, CancellationToken cancellationToken);
    HrEmployeeFieldAccess GetFieldAccess();
    Task EnsureEmployeeLocationAccessAsync(
        long businessId, long employeeId, CancellationToken cancellationToken) => Task.CompletedTask;
    Task<IReadOnlyCollection<OperationalLocationSourceIdentity>?> GetEmployeeSearchLocationScopeAsync(
        long businessId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyCollection<OperationalLocationSourceIdentity>?>(null);
}

public enum HrEmployeeOperation
{
    View,
    Create,
    Edit,
    ViewLifecycle,
    ManageLifecycle,
    Rehire,
    ViewPositionHistory,
    ManagePositionAssignments,
    LinkUser,
    UnlinkUser,
    ResolveCurrentEmployee
}

public interface IHrEmployeeStore
{
    Task<IReadOnlyList<HrEmploymentTypeReadModel>> GetEmploymentTypesAsync(
        long businessId, CancellationToken cancellationToken);
    Task<HrEmployeeSearchPage> SearchAsync(
        long businessId, string? search, int page, int pageSize,
        IReadOnlyCollection<OperationalLocationSourceIdentity>? allowedLocations,
        CancellationToken cancellationToken);
    Task<HrEmployeeProfile?> GetProfileAsync(
        long businessId, long employeeId, HrEmployeeFieldAccess fieldAccess, CancellationToken cancellationToken);
    Task<HrEmployeeProfile> CreateAsync(
        long businessId, long actorUserId, HrEmployeeCreateData data, HrEmployeeFieldAccess fieldAccess,
        CancellationToken cancellationToken);
    Task<HrEmployeeProfile?> UpdateAsync(
        long businessId, long employeeId, long expectedVersion, HrEmployeeUpdateData data,
        HrEmployeeFieldAccess fieldAccess, CancellationToken cancellationToken);
    Task<IReadOnlyList<HrEmployeeLifecycleEventReadModel>> GetLifecycleHistoryAsync(
        long businessId, long employeeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<HrEmployeePositionAssignmentReadModel>> GetPositionHistoryAsync(
        long businessId, long employeeId, CancellationToken cancellationToken);
    Task<HrEmployeeLifecycleEventReadModel?> TransitionLifecycleAsync(
        long businessId, long actorUserId, long employeeId, long expectedVersion,
        HrEmployeeLifecycleTransitionData data, CancellationToken cancellationToken);
    Task<HrEmployeeLifecycleEventReadModel?> RehireAsync(
        long businessId, long actorUserId, long employeeId, long expectedVersion,
        HrEmployeeRehireData data, CancellationToken cancellationToken);
    Task<HrEmployeePositionAssignmentReadModel?> AssignPositionAsync(
        long businessId, long actorUserId, long employeeId, long expectedVersion,
        HrEmployeePositionAssignmentData data, CancellationToken cancellationToken);
    Task<HrEmployeeUserLinkReadModel?> GetActiveUserLinkAsync(
        long businessId, long employeeId, CancellationToken cancellationToken);
    Task<HrEmployeeUserLinkReadModel?> LinkUserAsync(
        long businessId, long actorUserId, long employeeId, long identityUserId, string reason,
        CancellationToken cancellationToken);
    Task<HrEmployeeUserLinkReadModel?> UnlinkUserAsync(
        long businessId, long actorUserId, long employeeId, long expectedVersion, string reason,
        CancellationToken cancellationToken);
    Task<HrEmployeeSelfReadModel?> GetCurrentEmployeeAsync(
        long businessId, long identityUserId, CancellationToken cancellationToken);
}
