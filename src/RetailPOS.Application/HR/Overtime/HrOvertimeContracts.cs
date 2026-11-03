using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Organisation;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Overtime;

namespace RetailPOS.Application.HR.Overtime;

public enum HrOvertimeOperation
{
    View,
    ManagePolicy,
    Calculate,
    Submit,
    Approve,
    Cancel,
    Override
}

public sealed class HrOvertimeConcurrencyException(Exception innerException)
    : Exception("Overtime data changed concurrently; reload and retry.", innerException)
{
}

public sealed record HrOvertimePolicyInput(
    string Code,
    string Name,
    HrOvertimePolicyScopeType ScopeType,
    string? ScopeReference,
    int VersionNo,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    int MinimumEligibleMinutes,
    int? MaximumEligibleMinutes,
    int RoundingUnitMinutes,
    HrOvertimeRoundingMethod RoundingMethod,
    bool IncludePreShift,
    bool IncludePostShift,
    bool AllowNormalWorkingDay,
    bool AllowWeeklyOff,
    bool AllowHoliday,
    bool AllowSpecialDay,
    HrOvertimeRateType RateType,
    decimal NormalDayRate,
    decimal WeeklyOffRate,
    decimal HolidayRate,
    decimal SpecialDayRate,
    bool IsActive);

public sealed record HrOvertimePolicyModel(
    long Id,
    string Code,
    string Name,
    HrOvertimePolicyScopeType ScopeType,
    string? ScopeReference,
    int VersionNo,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    int MinimumEligibleMinutes,
    int? MaximumEligibleMinutes,
    int RoundingUnitMinutes,
    HrOvertimeRoundingMethod RoundingMethod,
    bool IncludePreShift,
    bool IncludePostShift,
    bool AllowNormalWorkingDay,
    bool AllowWeeklyOff,
    bool AllowHoliday,
    bool AllowSpecialDay,
    HrOvertimeRateType RateType,
    decimal NormalDayRate,
    decimal WeeklyOffRate,
    decimal HolidayRate,
    decimal SpecialDayRate,
    bool IsActive);

public sealed record HrOvertimeCalculationEvidenceModel(
    int CalculationVersion,
    int AttendanceProcessingVersion,
    long PolicyVersionId,
    int EligibleMinutes,
    int CalculatedMinutes,
    string DayCategory,
    string CalculationFingerprint,
    string PolicySnapshotJson,
    string AttendanceSnapshotJson,
    string CalculationInputsJson,
    DateTimeOffset CreatedAtUtc,
    long CreatedByUserId);

public sealed record HrOvertimeModel(
    long Id,
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    DateOnly WorkDate,
    long DailyAttendanceId,
    long OperationalLocationProjectionId,
    string DayCategory,
    int AttendanceProcessingVersion,
    long PolicyVersionId,
    int PolicyVersionNo,
    int EligibleMinutes,
    int CalculatedMinutes,
    int? ApprovedMinutes,
    string Status,
    int CalculationVersion,
    long? ApprovalInstanceId,
    ApprovalInstanceStatus? ApprovalStatus,
    int? CurrentApprovalStep,
    long Version,
    IReadOnlyList<HrOvertimeCalculationEvidenceModel> Evidence);

public sealed record HrOvertimeScope(long EmployeeId, long OperationalLocationProjectionId);

public interface IHrOvertimeAccess
{
    long RequireBusinessId();
    long RequireRealUserId();
    Task EnsureAsync(
        long businessId,
        HrOvertimeOperation operation,
        long? employeeId,
        long? locationId,
        CancellationToken cancellationToken);
    Task EnsurePolicyScopeAsync(
        long businessId,
        HrOvertimePolicyScopeType scopeType,
        string? scopeReference,
        CancellationToken cancellationToken);
    Task<HrOperationalLocationReadScope> GetLocationReadScopeAsync(
        long businessId,
        CancellationToken cancellationToken);
}

public interface IHrOvertimeStore
{
    Task<HrOvertimePolicyModel> CreatePolicyAsync(
        long businessId, HrOvertimePolicyInput input, CancellationToken cancellationToken);
    Task<IReadOnlyList<HrOvertimePolicyModel>> ListPoliciesAsync(
        long businessId, CancellationToken cancellationToken);
    Task<IReadOnlyList<HrOvertimeModel>> CalculateAsync(
        long businessId, long actorUserId, DateOnly from, DateOnly to, long? employeeId,
        long? locationId, HrOperationalLocationReadScope scope, CancellationToken cancellationToken);
    Task<IReadOnlyList<HrOvertimeModel>> ListAsync(
        long businessId, DateOnly from, DateOnly to, long? employeeId, long? locationId,
        string? status, HrOperationalLocationReadScope scope, CancellationToken cancellationToken);
    Task<HrOvertimeModel?> GetAsync(long businessId, long overtimeId, CancellationToken cancellationToken);
    Task<HrOvertimeScope?> GetScopeAsync(long businessId, long overtimeId, CancellationToken cancellationToken);
    Task<long?> GetApprovalInstanceIdAsync(long businessId, long overtimeId, CancellationToken cancellationToken);
    Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId, string targetType, string targetId, CancellationToken cancellationToken);
}

public sealed record CreateHrOvertimePolicyCommand(HrOvertimePolicyInput Input) : ICommand<HrOvertimePolicyModel>;
public sealed record GetHrOvertimePoliciesQuery : IRequest<IReadOnlyList<HrOvertimePolicyModel>>;
public sealed record CalculateHrOvertimeCommand(
    DateOnly From, DateOnly To, long? EmployeeId, long? LocationId) : ICommand<IReadOnlyList<HrOvertimeModel>>;
public sealed record GetHrOvertimeQuery(
    DateOnly From, DateOnly To, long? EmployeeId, long? LocationId, string? Status)
    : IRequest<IReadOnlyList<HrOvertimeModel>>;
public sealed record GetHrOvertimeEntryQuery(long Id) : IRequest<HrOvertimeModel?>;
public sealed record SubmitHrOvertimeCommand(long Id, long ExpectedVersion) : ICommand<ApprovalDecisionResult>;
public sealed record CancelHrOvertimeCommand(long Id, string DecisionKey, string Reason)
    : ICommand<ApprovalDecisionResult>;

public static class HrOvertimeApprovalConstants
{
    public const string TargetType = "HrOvertimeEntry";
}

public sealed class HrOvertimeHandlers(
    IHrOvertimeStore store,
    IHrOvertimeAccess access,
    ISender sender,
    IRequiredAuditTransaction audit)
    : IRequestHandler<CreateHrOvertimePolicyCommand, HrOvertimePolicyModel>,
      IRequestHandler<GetHrOvertimePoliciesQuery, IReadOnlyList<HrOvertimePolicyModel>>,
      IRequestHandler<CalculateHrOvertimeCommand, IReadOnlyList<HrOvertimeModel>>,
      IRequestHandler<GetHrOvertimeQuery, IReadOnlyList<HrOvertimeModel>>,
      IRequestHandler<GetHrOvertimeEntryQuery, HrOvertimeModel?>,
      IRequestHandler<SubmitHrOvertimeCommand, ApprovalDecisionResult>,
      IRequestHandler<CancelHrOvertimeCommand, ApprovalDecisionResult>
{
    public async Task<HrOvertimePolicyModel> Handle(
        CreateHrOvertimePolicyCommand request, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsurePolicyScopeAsync(businessId, request.Input.ScopeType,
            request.Input.ScopeReference, cancellationToken);
        await access.EnsureAsync(businessId, HrOvertimeOperation.ManagePolicy, null,
            ParseLocationScope(request.Input), cancellationToken);
        access.RequireRealUserId();
        return await audit.ExecuteAsync(new RequiredAuditOperation(
            businessId, "hr.overtime.policy.created", "HR",
            "Created an effective-dated overtime policy version.",
            "HrOvertimePolicyVersion", $"{request.Input.Code}:{request.Input.VersionNo}"),
            token => store.CreatePolicyAsync(businessId, request.Input, token), cancellationToken);
    }

    public async Task<IReadOnlyList<HrOvertimePolicyModel>> Handle(
        GetHrOvertimePoliciesQuery request, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOvertimeOperation.View, null, null, cancellationToken);
        return await store.ListPoliciesAsync(businessId, cancellationToken);
    }

    public async Task<IReadOnlyList<HrOvertimeModel>> Handle(
        CalculateHrOvertimeCommand request, CancellationToken cancellationToken)
    {
        ValidateRange(request.From, request.To);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOvertimeOperation.Calculate,
            request.EmployeeId, request.LocationId, cancellationToken);
        var scope = await access.GetLocationReadScopeAsync(businessId, cancellationToken);
        var rows = await audit.ExecuteAsync(new RequiredAuditOperation(
            businessId, "hr.overtime.calculated", "HR",
            "Calculated eligible overtime from frozen attendance evidence.",
            "HrOvertimeEntry", $"{request.From:yyyy-MM-dd}:{request.To:yyyy-MM-dd}"),
            token => store.CalculateAsync(businessId, access.RequireRealUserId(),
                request.From, request.To, request.EmployeeId, request.LocationId, scope, token),
            cancellationToken);
        foreach (var row in rows)
            await access.EnsureAsync(businessId, HrOvertimeOperation.Calculate,
                row.EmployeeId, row.OperationalLocationProjectionId, cancellationToken);
        return rows;
    }

    public async Task<IReadOnlyList<HrOvertimeModel>> Handle(
        GetHrOvertimeQuery request, CancellationToken cancellationToken)
    {
        ValidateRange(request.From, request.To);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrOvertimeOperation.View,
            request.EmployeeId, request.LocationId, cancellationToken);
        var rows = await store.ListAsync(businessId, request.From, request.To,
            request.EmployeeId, request.LocationId, request.Status,
            await access.GetLocationReadScopeAsync(businessId, cancellationToken), cancellationToken);
        foreach (var row in rows)
            await access.EnsureAsync(businessId, HrOvertimeOperation.View,
                row.EmployeeId, row.OperationalLocationProjectionId, cancellationToken);
        return rows;
    }

    public async Task<HrOvertimeModel?> Handle(
        GetHrOvertimeEntryQuery request, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        var model = await store.GetAsync(businessId, request.Id, cancellationToken);
        if (model is null)
            return null;
        await access.EnsureAsync(businessId, HrOvertimeOperation.View,
            model.EmployeeId, model.OperationalLocationProjectionId, cancellationToken);
        return model;
    }

    public async Task<ApprovalDecisionResult> Handle(
        SubmitHrOvertimeCommand request, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        var scope = await store.GetScopeAsync(businessId, request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Overtime entry was not found in this Business.");
        await access.EnsureAsync(businessId, HrOvertimeOperation.Submit,
            scope.EmployeeId, scope.OperationalLocationProjectionId, cancellationToken);
        var current = await store.GetAsync(businessId, request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Overtime entry was not found in this Business.");
        if (current.Version != request.ExpectedVersion)
            throw new ApprovalKernelConflictException("Overtime entry changed before submission.");
        if (current.Status == "PendingApproval" && current.ApprovalInstanceId is > 0)
        {
            var hash = OvertimeRequestHash(current);
            var target = await store.ReadAsync(businessId, HrOvertimeApprovalConstants.TargetType,
                current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
            if (target is not null && target.Hash == hash)
                return new ApprovalDecisionResult(current.ApprovalInstanceId.Value,
                    current.ApprovalStatus ?? ApprovalInstanceStatus.Pending,
                    current.CurrentApprovalStep, true, current.Status);
            throw new ApprovalKernelConflictException("A different overtime submission already exists.");
        }
        if (current.CalculatedMinutes <= 0 || current.Status != "Calculated")
            throw new InvalidOperationException("Only positive, current calculated overtime can be submitted.");

        var requestHash = OvertimeRequestHash(current);
        return await sender.Send(new SubmitApprovalCommand(
            ApprovalProcessType.Overtime,
            HrOvertimeApprovalConstants.TargetType,
            current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ApprovalScopeType.Location,
            current.OperationalLocationProjectionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            current.Version,
            requestHash), cancellationToken);
    }

    public async Task<ApprovalDecisionResult> Handle(
        CancelHrOvertimeCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DecisionKey) || request.DecisionKey.Length > 150
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000)
            throw new ArgumentException("A decision key and cancellation reason are required.");
        var businessId = access.RequireBusinessId();
        var scope = await store.GetScopeAsync(businessId, request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Overtime entry was not found in this Business.");
        await access.EnsureAsync(businessId, HrOvertimeOperation.Cancel,
            scope.EmployeeId, scope.OperationalLocationProjectionId, cancellationToken);
        var approvalId = await store.GetApprovalInstanceIdAsync(businessId, request.Id, cancellationToken)
            ?? throw new InvalidOperationException("Overtime entry has no pending approval to cancel.");
        return await sender.Send(new CancelApprovalCommand(approvalId, request.DecisionKey, request.Reason),
            cancellationToken);
    }

    private static long? ParseLocationScope(HrOvertimePolicyInput input) =>
        input.ScopeType == HrOvertimePolicyScopeType.Location
            && long.TryParse(input.ScopeReference, out var locationId) ? locationId : null;

    private static string OvertimeRequestHash(HrOvertimeModel model)
    {
        var evidence = model.Evidence.LastOrDefault()
            ?? throw new InvalidOperationException("Overtime calculation evidence is missing.");
        return HrOvertimeRules.CreateRequestHash(
            model.DailyAttendanceId, model.EmployeeId, model.WorkDate,
            model.AttendanceProcessingVersion, model.PolicyVersionId, model.PolicyVersionNo,
            model.CalculatedMinutes, model.DayCategory, evidence.CalculationFingerprint);
    }

    private static void ValidateRange(DateOnly from, DateOnly to)
    {
        if (from == default || to < from || to.DayNumber - from.DayNumber > 366)
            throw new ArgumentException("Provide a valid inclusive overtime date range of at most 367 days.");
    }
}
