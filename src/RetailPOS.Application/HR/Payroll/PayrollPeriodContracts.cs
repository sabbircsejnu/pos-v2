using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Capabilities;

namespace RetailPOS.Application.HR.Payroll;

public sealed record CreatePayrollPeriodRequest(
    string PeriodCode,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? PayDate);

public sealed record PayrollPeriodResponse(
    long Id,
    long BusinessId,
    string PeriodCode,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? PayDate,
    string Status,
    long Version,
    DateTime CreatedAtUtc,
    long CreatedByUserId,
    DateTime OpenedAtUtc,
    long OpenedByUserId,
    DateTime? ProcessingAtUtc,
    long? ProcessingByUserId,
    DateTime? LockedAtUtc,
    long? LockedByUserId,
    DateTime? ApprovedAtUtc,
    long? ApprovedByUserId,
    DateTime? PostedAtUtc,
    long? PostedByUserId,
    DateTime? ClosedAtUtc,
    long? ClosedByUserId,
    string? ClosedReason,
    DateTime? ReopenedAtUtc,
    long? ReopenedByUserId,
    string? ReopenReason,
    bool IsReplay = false);

public sealed record PayrollPeriodLifecycleRequest(long ExpectedVersion);
public sealed record ReopenPayrollPeriodRequest(long ExpectedVersion, string Reason);

public sealed record PayrollPeriodCloseExceptionRequest(
    string GateCode,
    string TargetReference,
    string Reason,
    string ApprovalReference);

public sealed record PayrollPeriodCloseExceptionPolicySnapshot(
    int Version,
    IReadOnlyList<string> AllowedGateCodes,
    bool RequiresApprovalReference);

public interface IPayrollPeriodCloseExceptionPolicy
{
    Task<PayrollPeriodCloseExceptionPolicySnapshot?> GetApprovedPolicyAsync(
        long businessId, CancellationToken cancellationToken);
}

public sealed record ClosePayrollPeriodRequest(
    string IdempotencyKey,
    string Reason,
    long ExpectedVersion,
    int PolicyVersion,
    IReadOnlyList<PayrollPeriodCloseExceptionRequest>? Exceptions = null);

public sealed record PayrollPeriodCloseGate(
    string Code,
    bool Passed,
    bool Blocking,
    string Message,
    string? ReferenceId = null);

public sealed record PayrollPeriodCloseResponse(
    long PayrollPeriodId,
    string Status,
    long Version,
    long AssessmentId,
    int PolicyVersion,
    IReadOnlyList<PayrollPeriodCloseGate> Gates,
    bool Closed,
    bool IsReplay = false);

public sealed record PayrollPeriodClosePreview(
    PayrollPeriodCloseResponse Response,
    string StateFingerprint);

public interface IPayrollPeriodAccess
{
    long RequireBusinessId();
    long RequireRealUserId();
    Task EnsureBusinessPermissionAsync(long businessId, string permission, CancellationToken cancellationToken);
}

public interface IPayrollPeriodStore
{
    Task<IReadOnlyList<PayrollPeriodResponse>> ListAsync(long businessId, CancellationToken cancellationToken);
    Task<PayrollPeriodResponse> GetAsync(long businessId, long id, CancellationToken cancellationToken);
    Task<PayrollPeriodResponse> CreateAsync(
        long businessId, long actorUserId, CreatePayrollPeriodRequest request, CancellationToken cancellationToken);
    Task<PayrollPeriodResponse> StartProcessingAsync(
        long businessId, long actorUserId, long id, long expectedVersion, CancellationToken cancellationToken);
    Task<PayrollPeriodResponse> LockAsync(
        long businessId, long actorUserId, long id, long expectedVersion, CancellationToken cancellationToken);
    Task<PayrollPeriodResponse> ReopenAsync(
        long businessId, long actorUserId, long id, ReopenPayrollPeriodRequest request,
        CancellationToken cancellationToken);
    Task<PayrollPeriodResponse> ReturnToOpenAsync(
        long businessId, long actorUserId, long id, ReopenPayrollPeriodRequest request,
        CancellationToken cancellationToken);
    Task<PayrollPeriodClosePreview> PreviewCloseAsync(
        long businessId, long actorUserId, long id, ClosePayrollPeriodRequest request,
        CancellationToken cancellationToken);
    Task<PayrollPeriodCloseResponse> CloseAsync(
        long businessId, long actorUserId, long id, ClosePayrollPeriodRequest request,
        string expectedStateFingerprint, CancellationToken cancellationToken);
}

public sealed record CreatePayrollPeriodCommand(CreatePayrollPeriodRequest Data)
    : IRequest<PayrollPeriodResponse>;
public sealed record GetPayrollPeriodsQuery : IRequest<IReadOnlyList<PayrollPeriodResponse>>;
public sealed record GetPayrollPeriodQuery(long Id) : IRequest<PayrollPeriodResponse>;
public sealed record StartPayrollProcessingCommand(long Id, long ExpectedVersion)
    : IRequest<PayrollPeriodResponse>;
public sealed record LockPayrollPeriodCommand(long Id, long ExpectedVersion)
    : IRequest<PayrollPeriodResponse>;
public sealed record ReopenPayrollPeriodCommand(long Id, ReopenPayrollPeriodRequest Request)
    : IRequest<PayrollPeriodResponse>;
public sealed record ReturnPayrollPeriodToOpenCommand(long Id, ReopenPayrollPeriodRequest Request)
    : IRequest<PayrollPeriodResponse>;
public sealed record ClosePayrollPeriodCommand(long Id, ClosePayrollPeriodRequest Request)
    : IRequest<PayrollPeriodCloseResponse>;

public sealed class PayrollPeriodHandlers(
    IHrPayrollCapabilityService capabilities,
    IPayrollPeriodAccess access,
    IPayrollPeriodStore store,
    IRequiredAuditTransaction audit) :
    IRequestHandler<CreatePayrollPeriodCommand, PayrollPeriodResponse>,
    IRequestHandler<GetPayrollPeriodsQuery, IReadOnlyList<PayrollPeriodResponse>>,
    IRequestHandler<GetPayrollPeriodQuery, PayrollPeriodResponse>,
    IRequestHandler<StartPayrollProcessingCommand, PayrollPeriodResponse>,
    IRequestHandler<LockPayrollPeriodCommand, PayrollPeriodResponse>,
    IRequestHandler<ReopenPayrollPeriodCommand, PayrollPeriodResponse>,
    IRequestHandler<ReturnPayrollPeriodToOpenCommand, PayrollPeriodResponse>,
    IRequestHandler<ClosePayrollPeriodCommand, PayrollPeriodCloseResponse>
{
    public async Task<PayrollPeriodResponse> Handle(
        CreatePayrollPeriodCommand command, CancellationToken cancellationToken)
    {
        Validate(command.Data);
        await EnsureCapabilitiesAsync(cancellationToken);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsureBusinessPermissionAsync(businessId, "payroll.period.create", cancellationToken);
        var periodCode = command.Data.PeriodCode.Trim().ToUpperInvariant();
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.period.created", "Payroll",
                "Created a Payroll period.", "PayrollPeriod", periodCode),
            token => store.CreateAsync(businessId, actorUserId, command.Data with { PeriodCode = periodCode }, token),
            cancellationToken);
    }

    public async Task<IReadOnlyList<PayrollPeriodResponse>> Handle(
        GetPayrollPeriodsQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilitiesAsync(cancellationToken);
        var businessId = access.RequireBusinessId();
        await access.EnsureBusinessPermissionAsync(businessId, "payroll.period.view", cancellationToken);
        return await store.ListAsync(businessId, cancellationToken);
    }

    public async Task<PayrollPeriodResponse> Handle(
        GetPayrollPeriodQuery query, CancellationToken cancellationToken)
    {
        await EnsureCapabilitiesAsync(cancellationToken);
        var businessId = access.RequireBusinessId();
        await access.EnsureBusinessPermissionAsync(businessId, "payroll.period.view", cancellationToken);
        return await store.GetAsync(businessId, query.Id, cancellationToken);
    }

    public async Task<PayrollPeriodResponse> Handle(
        StartPayrollProcessingCommand command, CancellationToken cancellationToken)
    {
        await EnsureCapabilitiesAsync(cancellationToken);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsureBusinessPermissionAsync(businessId, "payroll.run.create", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.period.processing_started", "Payroll",
                "Started Payroll period processing.", "PayrollPeriod",
                command.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.StartProcessingAsync(
                businessId, actorUserId, command.Id, command.ExpectedVersion, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<PayrollPeriodResponse> Handle(
        LockPayrollPeriodCommand command, CancellationToken cancellationToken)
    {
        await EnsureCapabilitiesAsync(cancellationToken);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsureBusinessPermissionAsync(businessId, "payroll.period.lock", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.period.locked", "Payroll",
                "Locked a Payroll period.", "PayrollPeriod",
                command.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.LockAsync(
                businessId, actorUserId, command.Id, command.ExpectedVersion, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<PayrollPeriodResponse> Handle(
        ReopenPayrollPeriodCommand command, CancellationToken cancellationToken)
    {
        ValidateReopen(command.Request);
        await EnsureCapabilitiesAsync(cancellationToken);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsureBusinessPermissionAsync(businessId, "payroll.period.reopen", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.period.reopened", "Payroll",
                "Reopened a Payroll period.", "PayrollPeriod",
                command.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.ReopenAsync(
                businessId, actorUserId, command.Id, command.Request, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<PayrollPeriodResponse> Handle(
        ReturnPayrollPeriodToOpenCommand command, CancellationToken cancellationToken)
    {
        ValidateReopen(command.Request);
        await EnsureCapabilitiesAsync(cancellationToken);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsureBusinessPermissionAsync(businessId, "payroll.period.reopen", cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.period.processing_returned_to_open", "Payroll",
            "Returned an unused Payroll period from Processing to Open.",
            "PayrollPeriod", command.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.ReturnToOpenAsync(
            businessId, actorUserId, command.Id, command.Request, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<PayrollPeriodCloseResponse> Handle(
        ClosePayrollPeriodCommand command, CancellationToken cancellationToken)
    {
        ValidateClose(command.Request);
        await EnsureCapabilitiesAsync(cancellationToken);
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsureBusinessPermissionAsync(businessId, "payroll.period.close", cancellationToken);
        if (command.Request.Exceptions is { Count: > 0 })
        {
            await access.EnsureBusinessPermissionAsync(
                businessId, "payroll.period.close_with_exception", cancellationToken);
        }

        var preview = await store.PreviewCloseAsync(
            businessId, actorUserId, command.Id, command.Request, cancellationToken);
        if (preview.Response.IsReplay)
            return preview.Response;

        var canClose = preview.Response.Closed;
        var operation = new RequiredAuditOperation(businessId,
            canClose
                ? command.Request.Exceptions is { Count: > 0 }
                    ? "payroll.period.closed_with_exception"
                    : "payroll.period.closed"
                : "payroll.period.close_blocked",
            "Payroll", canClose
                ? command.Request.Exceptions is { Count: > 0 }
                    ? "Closed a Payroll period under an approved close exception."
                    : "Closed a Payroll period."
                : "Assessed Payroll period close blockers.",
            "PayrollPeriod", command.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return await audit.ExecuteAsync(
            operation,
            token => store.CloseAsync(businessId, actorUserId, command.Id, command.Request,
                preview.StateFingerprint, token),
            result => !result.IsReplay,
            cancellationToken);
    }

    private async Task EnsureCapabilitiesAsync(CancellationToken cancellationToken)
    {
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Payroll, cancellationToken);
    }

    private static void Validate(CreatePayrollPeriodRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PeriodCode) || request.PeriodCode.Trim().Length > 30)
            throw new ArgumentException("A Payroll period code of at most 30 characters is required.");
        if (request.PeriodStart > request.PeriodEnd)
            throw new ArgumentException("Payroll period start must not be after its end.");
        if (request.PeriodCode.Any(char.IsControl))
            throw new ArgumentException("Payroll period code contains invalid characters.");
    }

    private static void ValidateReopen(ReopenPayrollPeriodRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedVersion <= 0)
            throw new ArgumentException("A positive expected Payroll period version is required.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            throw new ArgumentException("A Payroll period reopen reason of at most 1000 characters is required.");
    }

    private static void ValidateClose(ClosePayrollPeriodRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > 150)
            throw new ArgumentException("A Payroll period close idempotency key is required.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            throw new ArgumentException("A Payroll period close reason of at most 1000 characters is required.");
        if (request.ExpectedVersion <= 0)
            throw new ArgumentException("A positive expected Payroll period version is required.");
        if (request.PolicyVersion != 1)
            throw new PayrollPeriodConflictException("The requested Payroll close policy version is unsupported.");
    }
}

public sealed class PayrollPeriodConflictException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
