using MediatR;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Application.HR.Payroll;

public sealed record CreatePayrollRunRequest(
    string ScopeType,
    long? ScopeId,
    string IdempotencyKey,
    long ExpectedPeriodVersion);

public sealed record CreatePayrollCorrectionRequest(
    string CorrectionReference,
    string CorrectionType,
    string Reason,
    IReadOnlyList<long> EmployeeIds,
    string IdempotencyKey,
    long ExpectedPeriodVersion,
    long ExpectedOriginalRunVersion);

public sealed record CreateOffCyclePayrollRunRequest(
    string RunReference,
    string Reason,
    string ScopeType,
    long? ScopeId,
    IReadOnlyList<long> EmployeeIds,
    string IdempotencyKey,
    long ExpectedPeriodVersion);

public sealed record PayrollRunResponse(
    long Id,
    long BusinessId,
    long PayrollPeriodId,
    int RunNo,
    string RunType,
    string ScopeType,
    long? ScopeId,
    string Status,
    long Version,
    string? CurrencyCode,
    int? DecimalPrecision,
    string? RoundingMode,
    long? MonetaryConfigurationVersionId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int IncludedEmployeeCount,
    int ExcludedEmployeeCount,
    DateTime CreatedAtUtc,
    long CreatedByUserId,
    bool IsReplay = false,
    long? ApprovalInstanceId = null,
    long? SubmittedByUserId = null,
    DateTime? SubmittedAtUtc = null,
    long? DecidedByUserId = null,
    DateTime? DecidedAtUtc = null,
    string? DecisionReason = null,
    string? RunReference = null,
    string? CorrectionReference = null,
    long? OriginalPayrollRunId = null,
    int? OriginalCalculationVersion = null,
    string? CorrectionType = null,
    string? CorrectionReason = null,
    string? OriginalEvidenceHash = null,
    bool DownstreamReversalRequired = false,
    bool PaymentCorrectionRequired = false,
    bool AccountingCorrectionRequired = false);

public sealed record PayrollRunCorrectionResponse(
    long BusinessId,
    long OriginalPayrollPeriodId,
    long OriginalPayrollRunId,
    long CorrectionPayrollRunId,
    string CorrectionReference,
    string CorrectionType,
    string Reason,
    long RequestedByUserId,
    DateTime RequestedAtUtc,
    string Status,
    int OriginalCalculationVersion,
    string OriginalEvidenceHash,
    string? NewCalculationInputHash,
    long Version,
    IReadOnlyList<long> AffectedEmployeeIds,
    bool DownstreamReversalRequired,
    bool PaymentCorrectionRequired,
    bool AccountingCorrectionRequired);

public sealed record PayrollRunEmployeeSelectionResponse(
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    long? LocationId,
    DateOnly EligibilityDate,
    string? EmploymentStatus,
    DateOnly? EmploymentStartDate,
    DateOnly? EmploymentEndDate,
    bool IsIncluded,
    string? ExclusionReasonCode);

public sealed record PayrollRunEligibilityPageResponse(
    long PayrollRunId,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<PayrollRunEmployeeSelectionResponse> Items);

public interface IPayrollRunAccess
{
    long RequireBusinessId();
    long RequireRealUserId();
    Task EnsurePermissionAsync(long businessId, string permission, CancellationToken cancellationToken);
    Task EnsureScopeAsync(
        long businessId,
        string scopeType,
        long? scopeId,
        CancellationToken cancellationToken);
}

public interface IPayrollRunStore
{
    Task<PayrollRunResponse> CreateAsync(
        long businessId,
        long actorUserId,
        long periodId,
        CreatePayrollRunRequest request,
        CancellationToken cancellationToken);
    Task<PayrollRunResponse> CreateCorrectionAsync(
        long businessId,
        long actorUserId,
        long periodId,
        long originalRunId,
        CreatePayrollCorrectionRequest request,
        CancellationToken cancellationToken);
    Task<PayrollRunResponse> CreateOffCycleAsync(
        long businessId,
        long actorUserId,
        long periodId,
        CreateOffCyclePayrollRunRequest request,
        CancellationToken cancellationToken);
    Task<PayrollRunCorrectionResponse> GetCorrectionAsync(
        long businessId, long periodId, long runId, CancellationToken cancellationToken);
    Task<PayrollRunResponse> GetAsync(
        long businessId, long periodId, long runId, CancellationToken cancellationToken);
    Task<PayrollRunEligibilityPageResponse> GetEligibilityPageAsync(
        long businessId,
        long periodId,
        long runId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
}

public interface IPayrollRunApprovalWorkflow : IApprovalKernelWorkflow, IApprovalTargetSnapshotReader
{
}

public static class PayrollRunApprovalConstants
{
    public const string TargetType = "PayrollRun";
}

public sealed record SubmitPayrollRunCommand(long PeriodId, long RunId, long ExpectedVersion)
    : IRequest<ApprovalDecisionResult>;
public sealed record SubmitPayrollRunRequest(long ExpectedVersion);
public sealed record PayrollRunApprovalDecisionRequest(string DecisionKey, string? Reason);

public sealed record CreatePayrollRunCommand(long PeriodId, CreatePayrollRunRequest Data)
    : IRequest<PayrollRunResponse>;
public sealed record CreatePayrollCorrectionCommand(
    long PeriodId, long OriginalRunId, CreatePayrollCorrectionRequest Data) : IRequest<PayrollRunResponse>;
public sealed record CreateOffCyclePayrollRunCommand(
    long PeriodId, CreateOffCyclePayrollRunRequest Data) : IRequest<PayrollRunResponse>;
public sealed record GetPayrollRunQuery(long PeriodId, long RunId) : IRequest<PayrollRunResponse>;
public sealed record GetPayrollRunCorrectionQuery(long PeriodId, long RunId)
    : IRequest<PayrollRunCorrectionResponse>;
public sealed record GetPayrollRunEligibilityQuery(long PeriodId, long RunId, int Page, int PageSize)
    : IRequest<PayrollRunEligibilityPageResponse>;

public sealed class PayrollRunHandlers(
    IPayrollRunAccess access,
    IPayrollRunStore store,
    IApprovalTargetSnapshotReader approvalTargets,
    ISender sender,
    IRequiredAuditTransaction audit) :
    IRequestHandler<CreatePayrollRunCommand, PayrollRunResponse>,
    IRequestHandler<CreatePayrollCorrectionCommand, PayrollRunResponse>,
    IRequestHandler<CreateOffCyclePayrollRunCommand, PayrollRunResponse>,
    IRequestHandler<SubmitPayrollRunCommand, ApprovalDecisionResult>,
    IRequestHandler<GetPayrollRunQuery, PayrollRunResponse>,
    IRequestHandler<GetPayrollRunCorrectionQuery, PayrollRunCorrectionResponse>,
    IRequestHandler<GetPayrollRunEligibilityQuery, PayrollRunEligibilityPageResponse>
{
    public async Task<PayrollRunResponse> Handle(
        CreatePayrollRunCommand command, CancellationToken cancellationToken)
    {
        Validate(command.PeriodId, command.Data);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsurePermissionAsync(businessId, "payroll.run.create", cancellationToken);
        await access.EnsureScopeAsync(
            businessId, command.Data.ScopeType, command.Data.ScopeId, cancellationToken);
        var request = command.Data with { ScopeType = command.Data.ScopeType.Trim() };
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.run.created", "Payroll",
                "Created a Payroll run and captured eligibility evidence.", "PayrollRun",
                $"{command.PeriodId}:{request.ScopeType}:{request.ScopeId?.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) ?? "BUSINESS"}"),
            token => store.CreateAsync(businessId, actorUserId, command.PeriodId, request, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<ApprovalDecisionResult> Handle(
        SubmitPayrollRunCommand command, CancellationToken cancellationToken)
    {
        if (command.PeriodId <= 0 || command.RunId <= 0 || command.ExpectedVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(command));
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsurePermissionAsync(businessId, "payroll.run.submit", cancellationToken);
        await access.EnsurePermissionAsync(businessId, "payroll.salary.view", cancellationToken);
        var run = await store.GetAsync(businessId, command.PeriodId, command.RunId, cancellationToken);
        await access.EnsureScopeAsync(businessId, run.ScopeType, run.ScopeId, cancellationToken);
        if (run.Status != PayrollRunStatuses.Draft)
        {
            if (run.ApprovalInstanceId.HasValue && run.SubmittedByUserId == actorUserId)
            {
                var status = run.Status switch
                {
                    PayrollRunStatuses.Approved => ApprovalInstanceStatus.Approved,
                    PayrollRunStatuses.Rejected => ApprovalInstanceStatus.Rejected,
                    PayrollRunStatuses.Cancelled => ApprovalInstanceStatus.Cancelled,
                    _ => ApprovalInstanceStatus.Pending
                };
                return new ApprovalDecisionResult(run.ApprovalInstanceId.Value,
                    status, status == ApprovalInstanceStatus.Pending ? 1 : null, true, run.Status);
            }
            throw new PayrollRunConflictException("Only a Draft Payroll run can be submitted.");
        }
        if (run.Version != command.ExpectedVersion)
            throw new PayrollRunConflictException("Payroll run changed since it was read.");
        var targetType = PayrollRunApprovalConstants.TargetType;
        var targetId = command.RunId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var snapshot = await approvalTargets.ReadAsync(
            businessId, targetType, targetId, cancellationToken)
            ?? throw new PayrollRunConflictException("Payroll run approval evidence is unavailable.");
        return await sender.Send(new SubmitApprovalCommand(
            ApprovalProcessType.PayrollRun,
            targetType,
            targetId,
            run.ScopeType == PayrollRunScopeTypes.Location
                ? ApprovalScopeType.Location : ApprovalScopeType.Business,
            run.ScopeId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            snapshot.Version,
            snapshot.Hash), cancellationToken);
    }

    public async Task<PayrollRunResponse> Handle(
        CreatePayrollCorrectionCommand command, CancellationToken cancellationToken)
    {
        ValidateCorrection(command.PeriodId, command.OriginalRunId, command.Data);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsurePermissionAsync(businessId, "payroll.run.reopen", cancellationToken);
        await access.EnsurePermissionAsync(businessId, "payroll.run.create", cancellationToken);
        var original = await store.GetAsync(
            businessId, command.PeriodId, command.OriginalRunId, cancellationToken);
        await access.EnsureScopeAsync(businessId, original.ScopeType, original.ScopeId, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.run.correction_created", "Payroll",
                "Created a linked Payroll correction run and reopened its period for controlled correction.",
                "PayrollRun", command.Data.CorrectionReference.Trim()),
            token => store.CreateCorrectionAsync(businessId, actorUserId, command.PeriodId,
                command.OriginalRunId, command.Data, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<PayrollRunResponse> Handle(
        CreateOffCyclePayrollRunCommand command, CancellationToken cancellationToken)
    {
        ValidateOffCycle(command.PeriodId, command.Data);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsurePermissionAsync(businessId, "payroll.run.create", cancellationToken);
        await access.EnsureScopeAsync(
            businessId, command.Data.ScopeType, command.Data.ScopeId, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.run.off_cycle_created", "Payroll",
                "Created an Off-Cycle Payroll run for the authorized employee subset.",
                "PayrollRun", command.Data.RunReference.Trim()),
            token => store.CreateOffCycleAsync(
                businessId, actorUserId, command.PeriodId, command.Data, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<PayrollRunResponse> Handle(
        GetPayrollRunQuery query, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsurePermissionAsync(businessId, "payroll.run.view", cancellationToken);
        var run = await store.GetAsync(businessId, query.PeriodId, query.RunId, cancellationToken);
        await access.EnsureScopeAsync(businessId, run.ScopeType, run.ScopeId, cancellationToken);
        return run;
    }

    public async Task<PayrollRunEligibilityPageResponse> Handle(
        GetPayrollRunEligibilityQuery query, CancellationToken cancellationToken)
    {
        if (query.Page < 1 || query.PageSize is < 1 or > 200)
            throw new ArgumentException("Page must be positive and page size must be between 1 and 200.");
        var businessId = access.RequireBusinessId();
        await access.EnsurePermissionAsync(businessId, "payroll.run.view", cancellationToken);
        var run = await store.GetAsync(businessId, query.PeriodId, query.RunId, cancellationToken);
        await access.EnsureScopeAsync(businessId, run.ScopeType, run.ScopeId, cancellationToken);
        return await store.GetEligibilityPageAsync(
            businessId, query.PeriodId, query.RunId, query.Page, query.PageSize, cancellationToken);
    }

    public async Task<PayrollRunCorrectionResponse> Handle(
        GetPayrollRunCorrectionQuery query, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsurePermissionAsync(businessId, "payroll.run.view", cancellationToken);
        var correction = await store.GetCorrectionAsync(
            businessId, query.PeriodId, query.RunId, cancellationToken);
        var original = await store.GetAsync(
            businessId, query.PeriodId, correction.OriginalPayrollRunId, cancellationToken);
        var replacement = await store.GetAsync(
            businessId, query.PeriodId, correction.CorrectionPayrollRunId, cancellationToken);
        await access.EnsureScopeAsync(businessId, original.ScopeType, original.ScopeId, cancellationToken);
        await access.EnsureScopeAsync(businessId, replacement.ScopeType, replacement.ScopeId, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.run.correction_accessed", "Payroll",
                "Viewed Payroll correction lineage and affected-employee evidence.",
                "PayrollRun", query.RunId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            _ => Task.FromResult(correction),
            cancellationToken);
    }

    private static void Validate(long periodId, CreatePayrollRunRequest? request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (periodId <= 0)
            throw new ArgumentOutOfRangeException(nameof(periodId));
        if (request.ScopeType is not (PayrollRunScopeTypes.Business or PayrollRunScopeTypes.Location))
            throw new ArgumentException("Payroll runs support Business or Location scope.");
        if (request.ScopeType == PayrollRunScopeTypes.Business && request.ScopeId is not null)
            throw new ArgumentException("Business-scoped Payroll runs must not specify a scope ID.");
        if (request.ScopeType == PayrollRunScopeTypes.Location && request.ScopeId is not > 0)
            throw new ArgumentException("Location-scoped Payroll runs require a positive location ID.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Trim().Length > 150)
            throw new ArgumentException("An idempotency key of at most 150 characters is required.");
        if (request.ExpectedPeriodVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ExpectedPeriodVersion));
    }

    private static void ValidateCorrection(
        long periodId, long originalRunId, CreatePayrollCorrectionRequest? request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (periodId <= 0 || originalRunId <= 0 || request.ExpectedPeriodVersion <= 0
            || request.ExpectedOriginalRunVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(periodId));
        ValidateReference(request.CorrectionReference, nameof(request.CorrectionReference));
        if (request.CorrectionType is not ("SalaryInput" or "Attendance" or "Leave"
            or "Overtime" or "ManualAdjustment" or "LoanRecovery" or "Recalculation"))
            throw new ArgumentException("Unsupported Payroll correction type.");
        ValidateReason(request.Reason);
        ValidateEmployeeIds(request.EmployeeIds);
        ValidateIdempotencyKey(request.IdempotencyKey);
    }

    private static void ValidateOffCycle(
        long periodId, CreateOffCyclePayrollRunRequest? request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (periodId <= 0 || request.ExpectedPeriodVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(periodId));
        ValidateReference(request.RunReference, nameof(request.RunReference));
        ValidateReason(request.Reason);
        ValidateEmployeeIds(request.EmployeeIds);
        ValidateIdempotencyKey(request.IdempotencyKey);
        if (request.ScopeType is not (PayrollRunScopeTypes.Business or PayrollRunScopeTypes.Location)
            || request.ScopeType == PayrollRunScopeTypes.Business && request.ScopeId is not null
            || request.ScopeType == PayrollRunScopeTypes.Location && request.ScopeId is not > 0)
            throw new ArgumentException("Off-Cycle Payroll scope is invalid.");
    }

    private static void ValidateReference(string? reference, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Trim().Length > 150)
            throw new ArgumentException("A reference of at most 150 characters is required.", parameterName);
    }

    private static void ValidateReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000)
            throw new ArgumentException("A reason of at most 1000 characters is required.", nameof(reason));
    }

    private static void ValidateEmployeeIds(IReadOnlyList<long>? employeeIds)
    {
        if (employeeIds is null || employeeIds.Count == 0
            || employeeIds.Any(id => id <= 0)
            || employeeIds.Distinct().Count() != employeeIds.Count)
            throw new ArgumentException("A non-empty list of unique employee IDs is required.");
    }

    private static void ValidateIdempotencyKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 150)
            throw new ArgumentException("An idempotency key of at most 150 characters is required.");
    }
}

public sealed class PayrollRunConflictException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
