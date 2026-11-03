using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Entities.HR.Leave;

namespace RetailPOS.Application.HR.Leave;

public enum HrLeaveOperation
{
    ManageTypes,
    ViewTypes,
    ManagePolicies,
    ViewPolicies,
    ViewBalances,
    AdjustBalances,
    AccrueBalances,
    ReviewLeaveYearClose,
    CloseLeaveYear,
    ReopenLeaveYear,
    CreateRequest,
    ViewRequests,
    SubmitRequest,
    CancelRequest,
    DecideRequest
}

public sealed record HrLeaveTypeModel(
    long Id, string Code, string Name, bool IsPaid, bool RequiresDocument, bool IsActive, long Version);

public sealed record HrLeaveTypeInput(string Code, string Name, bool IsPaid, bool RequiresDocument);

public sealed record HrLeavePolicyModel(
    long Id,
    long LeaveTypeId,
    string Code,
    string Name,
    HrLeavePolicyScopeType ScopeType,
    string? ScopeReference,
    int VersionNo,
    HrLeaveAccrualMethod AccrualMethod,
    decimal AnnualEntitlement,
    bool CarryForwardAllowed,
    decimal? CarryForwardCap,
    int? CarryForwardExpiryMonths,
    decimal? MaximumBalance,
    bool NegativeBalanceAllowed,
    string? PayrollImpactCode,
    int LeaveYearStartMonth,
    int LeaveYearStartDay,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive);

public sealed record HrLeavePolicyInput(
    long LeaveTypeId,
    string Code,
    string Name,
    HrLeavePolicyScopeType ScopeType,
    string? ScopeReference,
    int VersionNo,
    HrLeaveAccrualMethod? AccrualMethod,
    decimal AnnualEntitlement,
    bool CarryForwardAllowed,
    decimal? CarryForwardCap,
    int? CarryForwardExpiryMonths,
    decimal? MaximumBalance,
    bool NegativeBalanceAllowed,
    string? PayrollImpactCode,
    int LeaveYearStartMonth,
    int LeaveYearStartDay,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive);

public sealed record HrLeaveBalanceModel(
    long EmployeeId,
    long LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    DateOnly LeaveYearStart,
    DateOnly LeaveYearEnd,
    int PolicyVersionNo,
    HrLeaveAccrualMethod AccrualMethod,
    decimal Opening,
    decimal Accrued,
    decimal Used,
    decimal Restored,
    decimal Adjusted,
    decimal CarriedForward,
    decimal Expired,
    decimal Remaining,
    long Version,
    string Status,
    int CloseVersion,
    decimal? ClosingBalance,
    decimal? CloseCarryForward,
    decimal? CloseExpired,
    DateOnly? CarryForwardExpiresOn,
    DateTimeOffset? LastLifecycleAtUtc,
    string? LastLifecycleAction,
    string? LastLifecycleReason);

public sealed record HrLeaveYearLifecycleModel(
    long EmployeeId,
    long LeaveTypeId,
    DateOnly LeaveYearStart,
    DateOnly LeaveYearEnd,
    string Status,
    int CloseVersion,
    long Version,
    int PolicyVersionNo,
    decimal BalanceBeforeClose,
    decimal CarryForwardAmount,
    decimal ExpiredAmount,
    DateOnly? CarryForwardExpiresOn,
    DateTimeOffset OccurredAtUtc,
    string Action,
    string Reason,
    bool IsReplay);

public sealed record HrLeaveLedgerModel(
    long Id,
    long EmployeeId,
    long LeaveTypeId,
    DateOnly LeaveYearStart,
    HrLeaveLedgerTransactionType TransactionType,
    decimal Quantity,
    DateOnly EffectiveDate,
    string SourceType,
    string SourceId,
    string Reason,
    long ActorRealUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record HrLeavePolicyContext(
    HrLeavePolicyVersion Policy,
    DateOnly JoiningDate,
    long? PrimaryLocationProjectionId,
    string? LocationType);

public sealed record HrLeaveLedgerWrite(
    HrLeaveLedgerTransactionType TransactionType,
    decimal Quantity,
    DateOnly EffectiveDate,
    string SourceType,
    string SourceId,
    string IdempotencyKey,
    string PayloadHash,
    string Reason,
    DateOnly? ExpiresOn = null);

public sealed record HrLeaveLedgerWriteResult(
    IReadOnlyList<HrLeaveLedgerEntry> Entries,
    bool IsReplay);

public sealed record HrLeaveRequestDayModel(
    DateOnly WorkDate,
    long? RosterId,
    long OperationalLocationProjectionId,
    string DayClassification,
    decimal Units,
    int PolicyVersionNo,
    string ShiftSnapshotJson,
    string CalendarEvidenceJson);

public sealed record HrLeaveRequestModel(
    long Id,
    long EmployeeId,
    string EmployeeName,
    long LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal RequestedUnits,
    string Reason,
    string Status,
    long ScopeLocationProjectionId,
    long RequestedByUserId,
    DateTimeOffset RequestedAtUtc,
    long? DecidedByUserId,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason,
    long? ApprovalInstanceId,
    RetailPOS.Core.Entities.HR.Approvals.ApprovalInstanceStatus? ApprovalStatus,
    int? CurrentApprovalStep,
    Guid? AttachmentVersionId,
    long Version,
    IReadOnlyList<HrLeaveRequestDayModel> Days);

public sealed record HrLeaveRequestDayEvidence(
    DateOnly WorkDate,
    long? RosterId,
    long OperationalLocationProjectionId,
    long PolicyVersionId,
    decimal Units,
    string DayClassification,
    string ShiftSnapshotJson,
    string CalendarEvidenceJson);

public sealed record HrLeaveRequestSnapshot(
    long Id,
    long EmployeeId,
    long LeaveTypeId,
    long ScopeLocationProjectionId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal RequestedUnits,
    string Reason,
    string Status,
    long RequestedByUserId,
    Guid? CurrentAttachmentVersionId,
    string? RequestHash,
    long Version,
    IReadOnlyList<HrLeaveRequestDayEvidence> Days);

public interface IHrLeaveAccess
{
    long RequireBusinessId();
    long RequireRealUserId();
    Task<long> RequireCurrentEmployeeIdAsync(long businessId, CancellationToken cancellationToken);
    Task EnsureAsync(
        long businessId,
        HrLeaveOperation operation,
        HrLeavePolicyScopeType? scopeType,
        string? scopeReference,
        long? employeeId,
        CancellationToken cancellationToken);
}

public interface IHrLeaveRequestAccess
{
    long RequireBusinessId();
    long RequireRealUserId();
    Task<long> RequireCurrentEmployeeIdAsync(long businessId, CancellationToken cancellationToken);
    Task EnsureAsync(
        long businessId,
        HrLeaveOperation operation,
        long? employeeId,
        long? locationId,
        CancellationToken cancellationToken);
    Task<bool> CanViewRequestAsync(
        long businessId, long employeeId, long locationId, CancellationToken cancellationToken);
}

public sealed record HrLeaveRequestAttachmentModel(
    Guid Id, int VersionNo, string FileName, string ContentType, long SizeBytes,
    RetailPOS.Application.ConfidentialFiles.ConfidentialFileScanStatus ScanStatus,
    long UploadedByUserId, DateTimeOffset UploadedAtUtc);

public interface IHrLeaveRequestStore
{
    Task<HrLeaveRequestModel> CreateDraftAsync(
        long businessId, long employeeId, long actorUserId, HrLeaveRequestDraftInput input,
        CancellationToken cancellationToken);
    Task<HrLeaveRequestModel?> GetAsync(long businessId, long requestId, CancellationToken cancellationToken);
    Task<IReadOnlyList<HrLeaveRequestModel>> ListAsync(
        long businessId, long? employeeId, string? status, CancellationToken cancellationToken);
    Task<HrLeaveRequestSnapshot?> FindForApprovalAsync(
        long businessId, long requestId, CancellationToken cancellationToken);
    Task<ApprovalTargetSnapshot?> PrepareSubmissionAsync(
        long businessId, long requesterUserId, SubmitApprovalCommand request,
        CancellationToken cancellationToken);
    Task SubmissionCreatedAsync(
        long businessId, RetailPOS.Core.Entities.HR.Approvals.ApprovalInstance instance,
        CancellationToken cancellationToken);
    Task DecisionCompletedAsync(
        long businessId, RetailPOS.Core.Entities.HR.Approvals.ApprovalInstance instance,
        long actorUserId, string? reason, CancellationToken cancellationToken);
    Task CancelAsync(long businessId, long requestId, long actorUserId, string? reason,
        CancellationToken cancellationToken);
    Task<bool> IsCurrentCleanAttachmentAsync(
        long businessId, long requestId, Guid versionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<HrLeaveRequestAttachmentModel>> GetAttachmentsAsync(
        long businessId, long requestId, CancellationToken cancellationToken);
}

public sealed record HrLeaveRequestDraftInput(
    long LeaveTypeId, DateOnly StartDate, DateOnly EndDate, string Reason);
public sealed record CreateHrLeaveRequestDraftCommand(HrLeaveRequestDraftInput Data)
    : IRequest<HrLeaveRequestModel>;
public sealed record GetMyHrLeaveRequestsQuery : IRequest<IReadOnlyList<HrLeaveRequestModel>>;
public sealed record GetHrLeaveRequestsQuery(long? EmployeeId, string? Status)
    : IRequest<IReadOnlyList<HrLeaveRequestModel>>;
public sealed record SubmitHrLeaveRequestCommand(long RequestId, long ExpectedVersion)
    : IRequest<ApprovalDecisionResult>;
public sealed record CancelHrLeaveRequestCommand(long RequestId, string DecisionKey, string? Reason)
    : IRequest<HrLeaveRequestModel?>;
public sealed record GetHrLeaveRequestQuery(long RequestId) : IRequest<HrLeaveRequestModel?>;
public sealed record GetHrLeaveRequestAttachmentsQuery(long RequestId)
    : IRequest<IReadOnlyList<HrLeaveRequestAttachmentModel>>;
public sealed record UploadHrLeaveRequestAttachmentCommand(
    long RequestId, string FileName, string ContentType, Stream Content)
    : IRequest<HrLeaveRequestAttachmentModel>;
public sealed record DownloadHrLeaveRequestAttachmentQuery(long RequestId, Guid VersionId)
    : IRequest<AuthorizedConfidentialFile>;
public static class HrLeaveApprovalConstants { public const string TargetType = "HrLeaveRequest"; }

public interface IHrLeaveStore
{
    Task<IReadOnlyList<HrLeaveTypeModel>> GetTypesAsync(long businessId, CancellationToken ct);
    Task<HrLeaveTypeModel> CreateTypeAsync(long businessId, HrLeaveTypeInput input, CancellationToken ct);
    Task<IReadOnlyList<HrLeavePolicyModel>> GetPoliciesAsync(
        long businessId, long? leaveTypeId, CancellationToken ct);
    Task<HrLeavePolicyModel> CreatePolicyVersionAsync(
        long businessId, HrLeavePolicyInput input, CancellationToken ct);
    Task<IReadOnlyList<HrLeaveBalanceModel>> GetBalancesAsync(
        long businessId, long employeeId, long? leaveTypeId, CancellationToken ct);
    Task<IReadOnlyList<HrLeaveLedgerModel>> GetLedgerAsync(
        long businessId, long employeeId, long? leaveTypeId, CancellationToken ct);
    Task<HrLeavePolicyContext> ResolvePolicyAsync(
        long businessId, long employeeId, long leaveTypeId, DateOnly effectiveOn, CancellationToken ct);
    Task<HrLeaveLedgerWriteResult> PostLedgerEntriesAsync(
        long businessId,
        long employeeId,
        long leaveTypeId,
        HrLeavePolicyContext context,
        IReadOnlyList<HrLeaveLedgerWrite> writes,
        long actorUserId,
        CancellationToken ct,
        bool allowClosingReview = false);
    Task<HrLeaveYearLifecycleModel> ReviewLeaveYearCloseAsync(
        long businessId, long employeeId, long leaveTypeId, DateOnly leaveYearStart,
        long expectedVersion, string idempotencyKey, long actorUserId, CancellationToken ct);
    Task<HrLeaveYearLifecycleModel> CloseLeaveYearAsync(
        long businessId, long employeeId, long leaveTypeId, DateOnly leaveYearStart,
        long expectedVersion, string idempotencyKey, long actorUserId, CancellationToken ct);
    Task<HrLeaveYearLifecycleModel> ReopenLeaveYearAsync(
        long businessId, long employeeId, long leaveTypeId, DateOnly leaveYearStart,
        long expectedVersion, string idempotencyKey, string reason, long actorUserId, CancellationToken ct);
    Task<HrLeaveLedgerWriteResult> ExpireCarryForwardBalancesAsync(
        long businessId, long employeeId, long leaveTypeId, DateOnly throughDate,
        HrLeavePolicyContext context, long actorUserId, CancellationToken ct);
}

public sealed record CreateHrLeaveTypeCommand(HrLeaveTypeInput Data) : IRequest<HrLeaveTypeModel>;
public sealed record GetHrLeaveTypesQuery : IRequest<IReadOnlyList<HrLeaveTypeModel>>;
public sealed record CreateHrLeavePolicyVersionCommand(HrLeavePolicyInput Data) : IRequest<HrLeavePolicyModel>;
public sealed record GetHrLeavePoliciesQuery(long? LeaveTypeId) : IRequest<IReadOnlyList<HrLeavePolicyModel>>;
public sealed record GetHrLeaveBalancesQuery(long EmployeeId, long? LeaveTypeId) : IRequest<IReadOnlyList<HrLeaveBalanceModel>>;
public sealed record GetHrLeaveLedgerQuery(long EmployeeId, long? LeaveTypeId) : IRequest<IReadOnlyList<HrLeaveLedgerModel>>;
public sealed record PostHrLeaveLedgerEntryCommand(
    long EmployeeId,
    long LeaveTypeId,
    HrLeaveLedgerTransactionType TransactionType,
    decimal Quantity,
    DateOnly EffectiveDate,
    string IdempotencyKey,
    string Reason) : IRequest<HrLeaveLedgerWriteResult>;
public sealed record AccrueHrLeaveCommand(
    long EmployeeId,
    long LeaveTypeId,
    DateOnly ThroughDate) : IRequest<HrLeaveLedgerWriteResult>;
public sealed record ReviewHrLeaveYearCloseCommand(
    long EmployeeId, long LeaveTypeId, DateOnly LeaveYearStart, long ExpectedVersion,
    string IdempotencyKey) : IRequest<HrLeaveYearLifecycleModel>;
public sealed record CloseHrLeaveYearCommand(
    long EmployeeId, long LeaveTypeId, DateOnly LeaveYearStart, long ExpectedVersion,
    string IdempotencyKey) : IRequest<HrLeaveYearLifecycleModel>;
public sealed record ReopenHrLeaveYearCommand(
    long EmployeeId, long LeaveTypeId, DateOnly LeaveYearStart, long ExpectedVersion,
    string IdempotencyKey, string Reason) : IRequest<HrLeaveYearLifecycleModel>;

public sealed class CreateHrLeaveTypeHandler(
    IHrLeaveAccess access,
    IHrLeaveStore store,
    IRequiredAuditTransaction audit)
    : IRequestHandler<CreateHrLeaveTypeCommand, HrLeaveTypeModel>
{
    public async Task<HrLeaveTypeModel> Handle(CreateHrLeaveTypeCommand request, CancellationToken ct)
    {
        ValidateType(request.Data);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrLeaveOperation.ManageTypes, null, null, null, ct);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "hr.leave_type.created", "HR", "Created a leave type."),
            token => store.CreateTypeAsync(businessId, request.Data with
            {
                Code = request.Data.Code.Trim(),
                Name = request.Data.Name.Trim()
            }, token),
            ct);
    }

    internal static void ValidateType(HrLeaveTypeInput data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ValidateText(data.Code, 50, "Leave type code");
        ValidateText(data.Name, 100, "Leave type name");
    }

    internal static void ValidateText(string? value, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max)
            throw new ArgumentException($"{label} is required and must not exceed {max} characters.");
    }
}

public sealed class GetHrLeaveTypesHandler(IHrLeaveAccess access, IHrLeaveStore store)
    : IRequestHandler<GetHrLeaveTypesQuery, IReadOnlyList<HrLeaveTypeModel>>
{
    public async Task<IReadOnlyList<HrLeaveTypeModel>> Handle(GetHrLeaveTypesQuery request, CancellationToken ct)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrLeaveOperation.ViewTypes, null, null, null, ct);
        return await store.GetTypesAsync(businessId, ct);
    }
}

public sealed class CreateHrLeavePolicyVersionHandler(
    IHrLeaveAccess access,
    IHrLeaveStore store,
    IRequiredAuditTransaction audit)
    : IRequestHandler<CreateHrLeavePolicyVersionCommand, HrLeavePolicyModel>
{
    public async Task<HrLeavePolicyModel> Handle(CreateHrLeavePolicyVersionCommand request, CancellationToken ct)
    {
        Validate(request.Data);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrLeaveOperation.ManagePolicies,
            request.Data.ScopeType, request.Data.ScopeReference, EmployeeId(request.Data), ct);
        var data = request.Data with
        {
            Code = request.Data.Code.Trim(),
            Name = request.Data.Name.Trim(),
            ScopeReference = NormalizeScope(request.Data.ScopeType, request.Data.ScopeReference),
            PayrollImpactCode = string.IsNullOrWhiteSpace(request.Data.PayrollImpactCode)
                ? null : request.Data.PayrollImpactCode.Trim()
        };
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "hr.leave_policy.version_created", "HR",
                $"Created leave policy {data.Code} version {data.VersionNo}."),
            token => store.CreatePolicyVersionAsync(businessId, data, token),
            ct);
    }

    internal static void Validate(HrLeavePolicyInput data)
    {
        ArgumentNullException.ThrowIfNull(data);
        CreateHrLeaveTypeHandler.ValidateText(data.Code, 100, "Leave policy code");
        CreateHrLeaveTypeHandler.ValidateText(data.Name, 150, "Leave policy name");
        if (data.LeaveTypeId <= 0 || data.VersionNo <= 0 || !Enum.IsDefined(data.ScopeType)
            || data.AccrualMethod is null || !Enum.IsDefined(data.AccrualMethod.Value))
            throw new ArgumentException("Leave policy type, version, scope and explicit accrual method must be valid.");
        if (data.ScopeType == HrLeavePolicyScopeType.Business
            ? !string.IsNullOrWhiteSpace(data.ScopeReference)
            : string.IsNullOrWhiteSpace(data.ScopeReference))
            throw new ArgumentException("Leave policy scope reference does not match its scope type.");
        if (data.ScopeType == HrLeavePolicyScopeType.Location
            && (!long.TryParse(data.ScopeReference, out var locationId) || locationId <= 0)
            || data.ScopeType == HrLeavePolicyScopeType.Employee
            && (!long.TryParse(data.ScopeReference, out var employeeId) || employeeId <= 0))
            throw new ArgumentException("Leave policy location or employee scope reference is invalid.");
        if (data.AnnualEntitlement < 0 || data.AnnualEntitlement > 1_000_000
            || DecimalPlaces(data.AnnualEntitlement) > 6)
            throw new ArgumentException("Annual entitlement must be non-negative with at most six decimal places.");
        if (data.CarryForwardCap < 0 || data.MaximumBalance < 0
            || data.CarryForwardExpiryMonths <= 0
            || !data.CarryForwardAllowed
                && (data.CarryForwardCap.HasValue || data.CarryForwardExpiryMonths.HasValue))
            throw new ArgumentException("Leave carry-forward and maximum-balance settings are invalid.");
        if (data.EffectiveTo < data.EffectiveFrom
            || data.LeaveYearStartMonth is < 1 or > 12
            || data.LeaveYearStartDay < 1
            || data.LeaveYearStartDay > DateTime.DaysInMonth(2000, data.LeaveYearStartMonth))
            throw new ArgumentException("Leave policy effective dates or leave-year start are invalid.");
        if (data.PayrollImpactCode?.Length > 100)
            throw new ArgumentException("Payroll impact code exceeds 100 characters.");
    }

    private static int DecimalPlaces(decimal value) =>
        (decimal.GetBits(value)[3] >> 16) & 0x7F;

    private static long? EmployeeId(HrLeavePolicyInput data) =>
        data.ScopeType == HrLeavePolicyScopeType.Employee
            ? long.Parse(data.ScopeReference!, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    private static string? NormalizeScope(HrLeavePolicyScopeType scope, string? reference) =>
        scope == HrLeavePolicyScopeType.Business
            ? null
            : scope == HrLeavePolicyScopeType.LocationType
                ? reference?.Trim()
                : long.Parse(reference!, System.Globalization.CultureInfo.InvariantCulture)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class GetHrLeavePoliciesHandler(IHrLeaveAccess access, IHrLeaveStore store)
    : IRequestHandler<GetHrLeavePoliciesQuery, IReadOnlyList<HrLeavePolicyModel>>
{
    public async Task<IReadOnlyList<HrLeavePolicyModel>> Handle(GetHrLeavePoliciesQuery request, CancellationToken ct)
    {
        if (request.LeaveTypeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.LeaveTypeId));
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrLeaveOperation.ViewPolicies, null, null, null, ct);
        return await store.GetPoliciesAsync(businessId, request.LeaveTypeId, ct);
    }
}

public sealed class GetHrLeaveBalancesHandler(IHrLeaveAccess access, IHrLeaveStore store)
    : IRequestHandler<GetHrLeaveBalancesQuery, IReadOnlyList<HrLeaveBalanceModel>>
{
    public async Task<IReadOnlyList<HrLeaveBalanceModel>> Handle(GetHrLeaveBalancesQuery request, CancellationToken ct)
    {
        ValidateEmployee(request.EmployeeId, request.LeaveTypeId);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrLeaveOperation.ViewBalances, null, null, request.EmployeeId, ct);
        return await store.GetBalancesAsync(businessId, request.EmployeeId, request.LeaveTypeId, ct);
    }

    internal static void ValidateEmployee(long employeeId, long? leaveTypeId)
    {
        if (employeeId <= 0 || leaveTypeId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(employeeId));
    }
}

public sealed class GetHrLeaveLedgerHandler(IHrLeaveAccess access, IHrLeaveStore store)
    : IRequestHandler<GetHrLeaveLedgerQuery, IReadOnlyList<HrLeaveLedgerModel>>
{
    public async Task<IReadOnlyList<HrLeaveLedgerModel>> Handle(GetHrLeaveLedgerQuery request, CancellationToken ct)
    {
        GetHrLeaveBalancesHandler.ValidateEmployee(request.EmployeeId, request.LeaveTypeId);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrLeaveOperation.ViewBalances, null, null, request.EmployeeId, ct);
        return await store.GetLedgerAsync(businessId, request.EmployeeId, request.LeaveTypeId, ct);
    }
}

public sealed class PostHrLeaveLedgerEntryHandler(
    IHrLeaveAccess access,
    IHrLeaveStore store,
    IRequiredAuditTransaction audit)
    : IRequestHandler<PostHrLeaveLedgerEntryCommand, HrLeaveLedgerWriteResult>
{
    public async Task<HrLeaveLedgerWriteResult> Handle(PostHrLeaveLedgerEntryCommand request, CancellationToken ct)
    {
        GetHrLeaveBalancesHandler.ValidateEmployee(request.EmployeeId, request.LeaveTypeId);
        Validate(request);
        var businessId = access.RequireBusinessId();
        var actor = access.RequireRealUserId();
        await access.EnsureAsync(businessId, HrLeaveOperation.AdjustBalances, null, null, request.EmployeeId, ct);
        var context = await HrLeaveRules.ResolvePolicyForLeaveYearAsync(store,
            businessId, request.EmployeeId, request.LeaveTypeId, request.EffectiveDate, ct);
        var write = MakeWrite(request.TransactionType, request.Quantity, request.EffectiveDate,
            "Manual", request.IdempotencyKey.Trim(), request.IdempotencyKey.Trim(), request.Reason.Trim());
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "hr.leave_ledger.entry_posted", "HR",
                $"Posted a {request.TransactionType} leave ledger entry.", "HrEmployee",
                request.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.PostLedgerEntriesAsync(
                businessId, request.EmployeeId, request.LeaveTypeId, context, [write], actor, token),
            result => !result.IsReplay,
            ct);
    }

    internal static void Validate(PostHrLeaveLedgerEntryCommand request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TransactionType is not (HrLeaveLedgerTransactionType.Opening or HrLeaveLedgerTransactionType.Adjustment))
            throw new ArgumentException("Only opening and adjustment entries can be manually posted.");
        if (request.Quantity == 0 || Math.Abs(request.Quantity) > 1_000_000
            || ((decimal.GetBits(request.Quantity)[3] >> 16) & 0x7F) > 6)
            throw new ArgumentException("Leave ledger quantity must be non-zero with at most six decimal places.");
        CreateHrLeaveTypeHandler.ValidateText(request.IdempotencyKey, 150, "Idempotency key");
        CreateHrLeaveTypeHandler.ValidateText(request.Reason, 1000, "Reason");
        if (request.EffectiveDate == default || request.TransactionType == HrLeaveLedgerTransactionType.Opening
            && request.Quantity < 0)
            throw new ArgumentException("Opening entries must be positive and effective dates must be valid.");
    }

    public static HrLeaveLedgerWrite MakeWrite(
        HrLeaveLedgerTransactionType type,
        decimal quantity,
        DateOnly effectiveDate,
        string sourceType,
        string sourceId,
        string key,
        string reason,
        DateOnly? expiresOn = null)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            Type = type.ToString(), Quantity = quantity, EffectiveDate = effectiveDate,
            SourceType = sourceType, SourceId = sourceId, Reason = reason,
            ExpiresOn = expiresOn
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        return new HrLeaveLedgerWrite(type, quantity, effectiveDate, sourceType,
            sourceId, key, hash, reason, expiresOn);
    }
}

public sealed class AccrueHrLeaveHandler(
    IHrLeaveAccess access,
    IHrLeaveStore store,
    IRequiredAuditTransaction audit)
    : IRequestHandler<AccrueHrLeaveCommand, HrLeaveLedgerWriteResult>
{
    public async Task<HrLeaveLedgerWriteResult> Handle(AccrueHrLeaveCommand request, CancellationToken ct)
    {
        GetHrLeaveBalancesHandler.ValidateEmployee(request.EmployeeId, request.LeaveTypeId);
        if (request.ThroughDate == default)
            throw new ArgumentException("Accrual through-date is required.");
        var businessId = access.RequireBusinessId();
        var actor = access.RequireRealUserId();
        await access.EnsureAsync(businessId, HrLeaveOperation.AccrueBalances, null, null, request.EmployeeId, ct);
        var context = await HrLeaveRules.ResolvePolicyForLeaveYearAsync(store,
            businessId, request.EmployeeId, request.LeaveTypeId, request.ThroughDate, ct);
        var policy = context.Policy;
        var (yearStart, yearEnd) = HrLeaveRules.GetLeaveYear(
            request.ThroughDate, policy.LeaveYearStartMonth, policy.LeaveYearStartDay);
        var due = HrLeaveRules.GetAccrualDues(
            policy.AccrualMethod, policy.AnnualEntitlement,
            context.JoiningDate, yearStart, yearEnd, request.ThroughDate);
        var writes = due.Select(item => PostHrLeaveLedgerEntryHandler.MakeWrite(
            HrLeaveLedgerTransactionType.Accrual, item.Quantity, item.DueDate,
            "Accrual", $"{request.EmployeeId}:{request.LeaveTypeId}:{policy.Id}:{item.DueDate:yyyyMMdd}",
            $"leave-accrual:{request.EmployeeId}:{request.LeaveTypeId}:{policy.Id}:{item.DueDate:yyyyMMdd}",
            $"Policy {policy.Code} version {policy.VersionNo} accrual.")).ToArray();
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "hr.leave_ledger.accrued", "HR",
                $"Accrued leave under policy {policy.Code} version {policy.VersionNo}.",
                "HrEmployee", request.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            async token =>
            {
                var accrual = writes.Length == 0
                    ? new HrLeaveLedgerWriteResult([], true)
                    : await store.PostLedgerEntriesAsync(
                        businessId, request.EmployeeId, request.LeaveTypeId, context, writes, actor, token);
                var expiry = await store.ExpireCarryForwardBalancesAsync(
                    businessId, request.EmployeeId, request.LeaveTypeId,
                    request.ThroughDate, context, actor, token);
                return new HrLeaveLedgerWriteResult(
                    accrual.Entries.Concat(expiry.Entries).ToArray(),
                    accrual.IsReplay && expiry.IsReplay);
            },
            result => !result.IsReplay,
            ct);
    }
}

public sealed record HrLeaveAccrualDue(DateOnly DueDate, decimal Quantity);

public static class HrLeaveRules
{
    public static async Task<HrLeavePolicyContext> ResolvePolicyForLeaveYearAsync(
        IHrLeaveStore store,
        long businessId,
        long employeeId,
        long leaveTypeId,
        DateOnly effectiveOn,
        CancellationToken cancellationToken)
    {
        var effectiveContext = await store.ResolvePolicyAsync(
            businessId, employeeId, leaveTypeId, effectiveOn, cancellationToken);
        var (leaveYearStart, _) = GetLeaveYear(effectiveOn,
            effectiveContext.Policy.LeaveYearStartMonth, effectiveContext.Policy.LeaveYearStartDay);
        var leaveYearContext = await store.ResolvePolicyAsync(
            businessId, employeeId, leaveTypeId, leaveYearStart, cancellationToken);
        var (resolvedLeaveYearStart, _) = GetLeaveYear(effectiveOn,
            leaveYearContext.Policy.LeaveYearStartMonth, leaveYearContext.Policy.LeaveYearStartDay);
        if (resolvedLeaveYearStart != leaveYearStart)
            throw new InvalidOperationException("Leave-year boundaries differ between effective policies within the leave year.");
        return leaveYearContext;
    }

    public static (DateOnly Start, DateOnly End) GetLeaveYear(DateOnly date, int startMonth, int startDay)
    {
        if (startMonth is < 1 or > 12 || startDay < 1
            || startDay > DateTime.DaysInMonth(2000, startMonth))
            throw new ArgumentOutOfRangeException(nameof(startMonth));
        var candidate = CreateAnniversary(date.Year, startMonth, startDay);
        var start = date >= candidate ? candidate : CreateAnniversary(date.Year - 1, startMonth, startDay);
        var next = CreateAnniversary(start.Year + 1, startMonth, startDay);
        return (start, next.AddDays(-1));
    }

    public static IReadOnlyList<HrLeaveAccrualDue> GetAccrualDues(
        HrLeaveAccrualMethod method,
        decimal annualEntitlement,
        DateOnly joiningDate,
        DateOnly leaveYearStart,
        DateOnly leaveYearEnd,
        DateOnly throughDate)
    {
        if (!Enum.IsDefined(method) || annualEntitlement < 0 || leaveYearEnd < leaveYearStart)
            throw new ArgumentException("Leave accrual inputs are invalid.");
        if (throughDate < leaveYearStart || throughDate > leaveYearEnd)
            throw new ArgumentOutOfRangeException(nameof(throughDate));
        var dues = new List<HrLeaveAccrualDue>();
        switch (method)
        {
            case HrLeaveAccrualMethod.AnnualGrantAtLeaveYearStart:
                var grantDate = joiningDate > leaveYearStart ? joiningDate : leaveYearStart;
                if (grantDate <= throughDate)
                    dues.Add(new HrLeaveAccrualDue(grantDate, annualEntitlement));
                break;
            case HrLeaveAccrualMethod.MonthlyOnJoiningDateAnniversary:
                var monthlyQuantity = annualEntitlement / 12m;
                for (var month = 1; ; month++)
                {
                    var dueDate = AddMonthsClamped(joiningDate, month);
                    if (dueDate > throughDate || dueDate > leaveYearEnd)
                        break;
                    if (dueDate >= leaveYearStart)
                        dues.Add(new HrLeaveAccrualDue(dueDate, monthlyQuantity));
                }
                break;
            case HrLeaveAccrualMethod.AnnualOnJoiningDateAnniversary:
                for (var year = 0; ; year++)
                {
                    var dueDate = AddYearsClamped(joiningDate, year);
                    if (dueDate > throughDate || dueDate > leaveYearEnd)
                        break;
                    if (dueDate >= leaveYearStart)
                        dues.Add(new HrLeaveAccrualDue(dueDate, annualEntitlement));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(method));
        }
        return dues;
    }

    private static DateOnly AddMonthsClamped(DateOnly date, int months)
    {
        var targetMonth = date.AddMonths(months);
        return new DateOnly(targetMonth.Year, targetMonth.Month,
            Math.Min(date.Day, DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month)));
    }

    private static DateOnly AddYearsClamped(DateOnly date, int years) =>
        CreateAnniversary(date.Year + years, date.Month, date.Day);

    private static DateOnly CreateAnniversary(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
}

public sealed record HrLeaveYearCloseCalculation(
    decimal BalanceBeforeClose,
    decimal CarryForwardAmount,
    decimal ExpiredAmount,
    DateOnly? CarryForwardExpiresOn);

public static class HrLeaveYearCloseRules
{
    public static HrLeaveYearCloseCalculation Calculate(
        decimal balance, HrLeavePolicyVersion policy, DateOnly leaveYearEnd)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (leaveYearEnd == default || balance < -1_000_000m || balance > 1_000_000m)
            throw new ArgumentOutOfRangeException(nameof(balance));
        var positiveBalance = Math.Max(0m, balance);
        var carryForward = policy.CarryForwardAllowed
            ? Math.Min(positiveBalance, policy.CarryForwardCap ?? positiveBalance)
            : 0m;
        DateOnly? expiresOn = carryForward > 0 && policy.CarryForwardExpiryMonths is int months
            ? leaveYearEnd.AddDays(1).AddMonths(months)
            : null;
        return new HrLeaveYearCloseCalculation(
            balance, carryForward, positiveBalance - carryForward, expiresOn);
    }
}
