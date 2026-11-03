using MediatR;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Application.HR.Payroll;

/// <summary>A frozen line in a calculated Payroll final settlement revision.</summary>
public sealed record PayrollFinalSettlementLineModel(
    int LineNo,
    string ComponentCode,
    string ComponentName,
    string LineType,
    decimal Amount,
    bool IsApplicable,
    string? NotApplicableReason,
    string SourceType,
    string? SourceId,
    string EvidenceSnapshotJson);

/// <summary>A versioned final settlement calculation tied to an approved exit case.</summary>
public sealed record PayrollFinalSettlementModel(
    long Id,
    long ExitCaseId,
    long EmployeeId,
    int RevisionNo,
    DateOnly SettlementDate,
    string CurrencyCode,
    int DecimalPrecision,
    string RoundingMode,
    decimal GrossDue,
    decimal TotalDeductions,
    decimal NetSettlement,
    string CalculationInputHash,
    string SnapshotHash,
    string SnapshotJson,
    DateTime CalculatedAtUtc,
    long CalculatedByUserId,
    IReadOnlyList<PayrollFinalSettlementLineModel> Lines,
    bool IsReplay = false,
    string Status = "Calculated",
    long? ApprovalInstanceId = null,
    long? RequestedByUserId = null,
    DateTime? RequestedAtUtc = null,
    string? ApprovalPolicyCode = null,
    int? ApprovalPolicyVersion = null,
    string? SubmittedEvidenceHash = null,
    IReadOnlyList<PayrollFinalSettlementApprovalDecisionModel>? ApprovalHistory = null)
{
    public long RevisionId { get; init; }
}

public sealed record PayrollFinalSettlementApprovalDecisionModel(
    string Decision,
    long ActorUserId,
    DateTime OccurredAtUtc,
    string? Reason,
    string InstanceStatusAfterDecision);

public interface IPayrollFinalSettlementStore
{
    Task<HrExitScope?> GetExitScopeAsync(
        long businessId, long exitCaseId, CancellationToken cancellationToken);
    Task<HrExitScope?> GetSettlementScopeAsync(
        long businessId, long settlementId, CancellationToken cancellationToken);
    Task<PayrollFinalSettlementModel?> GetForExitAsync(
        long businessId, long exitCaseId, CancellationToken cancellationToken);
    Task<PayrollFinalSettlementModel?> GetByIdAsync(
        long businessId, long settlementId, CancellationToken cancellationToken);
    Task<PayrollFinalSettlementModel> CalculateAsync(
        long businessId, long actorUserId, long exitCaseId, long expectedExitVersion,
        string idempotencyKey, CancellationToken cancellationToken);
    Task<ApprovalTargetSnapshot> GetSubmissionTargetAsync(
        long businessId, long settlementId, int revisionNo, string idempotencyKey,
        CancellationToken cancellationToken);
    Task<ApprovalTargetSnapshot?> ReadApprovalTargetAsync(
        long businessId, string targetType, string targetId, CancellationToken cancellationToken);
    Task<long?> GetPendingApprovalInstanceIdAsync(
        long businessId, long settlementId, int revisionNo,
        CancellationToken cancellationToken);
    Task<ApprovalDecisionResult?> FindSubmissionReplayAsync(
        long businessId, SubmitApprovalCommand request, CancellationToken cancellationToken);
}

public sealed record CalculatePayrollFinalSettlementCommand(
    long ExitCaseId, long ExpectedExitVersion, string IdempotencyKey)
    : IRequest<PayrollFinalSettlementModel>;

public sealed record GetPayrollFinalSettlementForExitQuery(long ExitCaseId)
    : IRequest<PayrollFinalSettlementModel?>;

public sealed record GetPayrollFinalSettlementQuery(long Id)
    : IRequest<PayrollFinalSettlementModel?>;

public sealed record SubmitPayrollFinalSettlementCommand(
    long Id, int RevisionNo, string IdempotencyKey) : IRequest<ApprovalDecisionResult>;

public sealed record ApprovePayrollFinalSettlementCommand(
    long Id, int RevisionNo, string DecisionKey, string? Reason) : IRequest<ApprovalDecisionResult>;

public sealed record RejectPayrollFinalSettlementCommand(
    long Id, int RevisionNo, string DecisionKey, string Reason) : IRequest<ApprovalDecisionResult>;

public sealed record CancelPayrollFinalSettlementApprovalCommand(
    long Id, int RevisionNo, string DecisionKey, string Reason) : IRequest<ApprovalDecisionResult>;

public sealed class PayrollFinalSettlementHandlers(
    IPayrollFinalSettlementStore store,
    IHrExitAccess access,
    IRequiredAuditTransaction audit) :
    IRequestHandler<CalculatePayrollFinalSettlementCommand, PayrollFinalSettlementModel>,
    IRequestHandler<GetPayrollFinalSettlementForExitQuery, PayrollFinalSettlementModel?>,
    IRequestHandler<GetPayrollFinalSettlementQuery, PayrollFinalSettlementModel?>
{
    public async Task<PayrollFinalSettlementModel> Handle(
        CalculatePayrollFinalSettlementCommand request, CancellationToken cancellationToken)
    {
        if (request.ExitCaseId <= 0 || request.ExpectedExitVersion <= 0
            || string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > 150)
            throw new ArgumentException(
                "A valid exit identity, expected version and idempotency key are required.");

        var businessId = access.RequireBusinessId();
        var scope = await store.GetExitScopeAsync(
            businessId, request.ExitCaseId, cancellationToken)
            ?? throw new KeyNotFoundException("Exit case was not found in this Business.");
        await access.EnsureAsync(businessId, HrExitOperation.FinalSettlementCalculate,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        var actorUserId = access.RequireRealUserId();
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.final_settlement.calculated", "Payroll",
                "Calculated a versioned final settlement from persisted Payroll evidence.",
                "HrExitCase", request.ExitCaseId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            token => store.CalculateAsync(businessId, actorUserId, request.ExitCaseId,
                request.ExpectedExitVersion, request.IdempotencyKey.Trim(), token),
            result => !result.IsReplay,
            cancellationToken);
    }

    public async Task<PayrollFinalSettlementModel?> Handle(
        GetPayrollFinalSettlementForExitQuery request, CancellationToken cancellationToken)
    {
        if (request.ExitCaseId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ExitCaseId));
        var businessId = access.RequireBusinessId();
        var scope = await store.GetExitScopeAsync(businessId, request.ExitCaseId, cancellationToken);
        if (scope is null)
            return null;
        await access.EnsureAsync(businessId, HrExitOperation.FinalSettlementView,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.final_settlement.accessed", "Payroll",
                "Accessed confidential final settlement evidence.",
                "HrExitCase", request.ExitCaseId.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            token => store.GetForExitAsync(businessId, request.ExitCaseId, token),
            cancellationToken);
    }

    public async Task<PayrollFinalSettlementModel?> Handle(
        GetPayrollFinalSettlementQuery request, CancellationToken cancellationToken)
    {
        if (request.Id <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Id));
        var businessId = access.RequireBusinessId();
        var scope = await store.GetSettlementScopeAsync(businessId, request.Id, cancellationToken);
        if (scope is null)
            return null;
        await access.EnsureAsync(businessId, HrExitOperation.FinalSettlementView,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(businessId, "payroll.final_settlement.accessed", "Payroll",
                "Accessed confidential final settlement evidence.",
                "PayrollFinalSettlement", request.Id.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
            token => store.GetByIdAsync(businessId, request.Id, token),
            cancellationToken);
    }
}

public sealed class PayrollFinalSettlementApprovalHandlers(
    IPayrollFinalSettlementStore store,
    IHrExitAccess access,
    ISender sender) :
    IRequestHandler<SubmitPayrollFinalSettlementCommand, ApprovalDecisionResult>,
    IRequestHandler<ApprovePayrollFinalSettlementCommand, ApprovalDecisionResult>,
    IRequestHandler<RejectPayrollFinalSettlementCommand, ApprovalDecisionResult>,
    IRequestHandler<CancelPayrollFinalSettlementApprovalCommand, ApprovalDecisionResult>
{
    public async Task<ApprovalDecisionResult> Handle(
        SubmitPayrollFinalSettlementCommand request, CancellationToken cancellationToken)
    {
        ValidateIdentity(request.Id, request.RevisionNo);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Trim().Length > 150)
            throw new ArgumentException("A valid submission idempotency key is required.");

        var businessId = access.RequireBusinessId();
        var scope = await store.GetSettlementScopeAsync(businessId, request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Final settlement was not found in this Business.");
        await access.EnsureAsync(businessId, HrExitOperation.FinalSettlementSubmit,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        var actorUserId = access.RequireRealUserId();
        var target = await store.GetSubmissionTargetAsync(
            businessId, request.Id, request.RevisionNo, request.IdempotencyKey.Trim(), cancellationToken);
        return await sender.Send(new SubmitApprovalCommand(
            ApprovalProcessType.FinalSettlement,
            PayrollFinalSettlementApprovalConstants.TargetType,
            request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            scope.PrimaryLocationProjectionId is > 0
                ? ApprovalScopeType.Location
                : ApprovalScopeType.Business,
            scope.PrimaryLocationProjectionId?.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            target.Version,
            target.Hash,
            IdempotencyKey: request.IdempotencyKey.Trim()), cancellationToken);
    }

    public Task<ApprovalDecisionResult> Handle(
        ApprovePayrollFinalSettlementCommand request, CancellationToken cancellationToken) =>
        DecideAsync(request.Id, request.RevisionNo, request.DecisionKey, request.Reason,
            ApprovalDecisionType.Approve, HrExitOperation.FinalSettlementApprove, cancellationToken);

    public Task<ApprovalDecisionResult> Handle(
        RejectPayrollFinalSettlementCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("A rejection reason is required.");
        return DecideAsync(request.Id, request.RevisionNo, request.DecisionKey, request.Reason,
            ApprovalDecisionType.Reject, HrExitOperation.FinalSettlementReject, cancellationToken);
    }

    public async Task<ApprovalDecisionResult> Handle(
        CancelPayrollFinalSettlementApprovalCommand request, CancellationToken cancellationToken)
    {
        ValidateIdentity(request.Id, request.RevisionNo);
        ValidateDecisionKey(request.DecisionKey);
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("A cancellation reason is required.");
        var businessId = access.RequireBusinessId();
        var scope = await store.GetSettlementScopeAsync(businessId, request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Final settlement was not found in this Business.");
        await access.EnsureAsync(businessId, HrExitOperation.FinalSettlementCancel,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        var approvalId = await store.GetPendingApprovalInstanceIdAsync(
            businessId, request.Id, request.RevisionNo, cancellationToken)
            ?? throw new InvalidOperationException("The requested settlement revision has no pending approval.");
        return await sender.Send(new CancelApprovalCommand(
            approvalId, request.DecisionKey.Trim(), request.Reason.Trim()), cancellationToken);
    }

    private async Task<ApprovalDecisionResult> DecideAsync(
        long settlementId,
        int revisionNo,
        string decisionKey,
        string? reason,
        ApprovalDecisionType decision,
        HrExitOperation operation,
        CancellationToken cancellationToken)
    {
        ValidateIdentity(settlementId, revisionNo);
        ValidateDecisionKey(decisionKey);
        var businessId = access.RequireBusinessId();
        var scope = await store.GetSettlementScopeAsync(businessId, settlementId, cancellationToken)
            ?? throw new KeyNotFoundException("Final settlement was not found in this Business.");
        await access.EnsureAsync(businessId, operation,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        var approvalId = await store.GetPendingApprovalInstanceIdAsync(
            businessId, settlementId, revisionNo, cancellationToken)
            ?? throw new InvalidOperationException("The requested settlement revision has no pending approval.");
        return await sender.Send(new RecordApprovalDecisionCommand(
            approvalId, decisionKey.Trim(), decision, reason), cancellationToken);
    }

    private static void ValidateIdentity(long settlementId, int revisionNo)
    {
        if (settlementId <= 0 || revisionNo <= 0)
            throw new ArgumentOutOfRangeException(nameof(settlementId));
    }

    private static void ValidateDecisionKey(string decisionKey)
    {
        if (string.IsNullOrWhiteSpace(decisionKey) || decisionKey.Trim().Length > 150)
            throw new ArgumentException("A valid decision idempotency key is required.");
    }
}

public static class PayrollFinalSettlementApprovalConstants
{
    public const string TargetType = "PayrollFinalSettlement";
}
