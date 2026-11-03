using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Exit;

namespace RetailPOS.Application.HR.Exit;

public sealed class HrExitConcurrencyException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public enum HrExitOperation
{
    View,
    Initiate,
    Approve,
    Clearance,
    FinalSettlementView,
    FinalSettlementCalculate,
    FinalSettlementSubmit,
    FinalSettlementApprove,
    FinalSettlementReject,
    FinalSettlementCancel,
    FinalSettlementFinancialView,
    FinalSettlementAccountingPost,
    FinalSettlementPaymentCreate,
    FinalSettlementPaymentView,
    FinalSettlementPaymentHold,
    FinalSettlementPaymentRelease,
    FinalSettlementPaymentProcess,
    FinalSettlementPaymentFail,
    FinalSettlementPaymentReturn,
    FinalSettlementPaymentRetry,
    FinalSettlementPaymentAccountingPost
}

public sealed record HrClearanceTemplateItemInput(
    int Sequence,
    string Code,
    string Name,
    string? ResponsibleRole,
    string? ResponsibleReference,
    bool IsMandatory);

public sealed record HrClearanceTemplateInput(
    string Code,
    string Name,
    HrClearanceTemplateScopeType ScopeType,
    string? ScopeReference,
    int VersionNo,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    IReadOnlyList<HrClearanceTemplateItemInput> Items);

public sealed record HrExitCaseInput(
    long EmployeeId,
    string ExitType,
    DateOnly InitiatedDate,
    DateOnly ProposedLastWorkingDate,
    string Reason);

public sealed record HrClearanceUpdateInput(
    long ItemId,
    long ExpectedVersion,
    string Status,
    string? Comment,
    string? EvidenceReference);

public sealed record HrClearanceTemplateItemModel(
    long Id,
    int Sequence,
    string Code,
    string Name,
    string? ResponsibleRole,
    string? ResponsibleReference,
    bool IsMandatory);

public sealed record HrClearanceTemplateModel(
    long Id,
    string Code,
    string Name,
    HrClearanceTemplateScopeType ScopeType,
    string? ScopeReference,
    int VersionNo,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsActive,
    IReadOnlyList<HrClearanceTemplateItemModel> Items);

public sealed record HrClearanceItemModel(
    long Id,
    int Sequence,
    string Code,
    string Name,
    string? ResponsibleRole,
    string? ResponsibleReference,
    bool IsMandatory,
    string Status,
    long? ClearedByUserId,
    DateTimeOffset? ClearedAtUtc,
    string? Comment,
    string? EvidenceReference,
    long Version);

public sealed record HrExitCaseEventModel(
    int Sequence,
    string EventType,
    string? PreviousStatus,
    string NewStatus,
    DateOnly? ActualLastWorkingDate,
    string? Reason,
    long ActorUserId,
    DateTimeOffset OccurredAtUtc);

public sealed record HrExitCaseModel(
    long Id,
    long EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string ExitType,
    DateOnly InitiatedDate,
    DateOnly ProposedLastWorkingDate,
    DateOnly? ActualLastWorkingDate,
    string Reason,
    string Status,
    long? ApprovalInstanceId,
    long? PrimaryLocationProjectionId,
    string? LocationType,
    long ClearanceTemplateId,
    int ClearanceTemplateVersionNo,
    long Version,
    IReadOnlyList<HrClearanceItemModel> ClearanceItems,
    IReadOnlyList<HrExitCaseEventModel> Events);

public sealed record HrExitCompletionSettlementModel(
    long SettlementId,
    int RevisionNo,
    string Status,
    string CompletionMode,
    decimal NetSettlement,
    string CurrencyCode,
    string? SubmittedEvidenceHash,
    bool PaymentRequired,
    int ActivePaymentCount,
    decimal OutstandingAmount,
    bool PaymentsSatisfied,
    bool ReconciliationRequired,
    bool ReconciliationSatisfied,
    bool AccountingRequired,
    string? RequiredPostingType,
    bool AccountingSatisfied,
    int RequiredPaymentAccountingCount,
    int PostedPaymentAccountingCount);

public sealed record HrExitCompletionPreviewModel(
    long ExitCaseId,
    long ExitVersion,
    bool PayrollEnabled,
    bool CanComplete,
    int PendingMandatoryClearanceCount,
    int ReasonlessNotApplicableCount,
    IReadOnlyList<string> Blockers,
    HrExitCompletionSettlementModel? Settlement);

public sealed record HrExitScope(
    long EmployeeId,
    long? PrimaryLocationProjectionId,
    string? LocationType);

public sealed record HrExitApprovalSubmission(
    long Id,
    long Version,
    string RequestHash);

public interface IHrExitAccess
{
    long RequireBusinessId();
    long RequireRealUserId();
    Task EnsureAsync(
        long businessId,
        HrExitOperation operation,
        long? employeeId,
        long? locationId,
        CancellationToken cancellationToken);
    Task EnsureTemplateScopeAsync(
        long businessId,
        HrClearanceTemplateScopeType scopeType,
        string? scopeReference,
        CancellationToken cancellationToken);
    Task<IReadOnlyCollection<long>?> GetAccessibleLocationIdsAsync(
        long businessId, CancellationToken cancellationToken);
}

public interface IHrExitStore
{
    Task<HrClearanceTemplateModel> CreateTemplateAsync(
        long businessId, long actorUserId, HrClearanceTemplateInput input, CancellationToken cancellationToken);
    Task<IReadOnlyList<HrClearanceTemplateModel>> ListTemplatesAsync(
        long businessId, CancellationToken cancellationToken);
    Task<HrExitCaseModel> InitiateAsync(
        long businessId, long actorUserId, HrExitCaseInput input, CancellationToken cancellationToken);
    Task<IReadOnlyList<HrExitCaseModel>> ListAsync(
        long businessId, DateOnly? from, DateOnly? to, string? status, long? employeeId,
        IReadOnlyCollection<long>? allowedLocationIds, CancellationToken cancellationToken);
    Task<HrExitCaseModel?> GetAsync(long businessId, long exitCaseId, CancellationToken cancellationToken);
    Task<HrExitCompletionPreviewModel?> GetCompletionPreviewAsync(
        long businessId, long exitCaseId, CancellationToken cancellationToken);
    Task<HrExitScope?> GetScopeAsync(long businessId, long exitCaseId, CancellationToken cancellationToken);
    Task<HrExitScope?> GetClearanceItemScopeAsync(
        long businessId, long clearanceItemId, CancellationToken cancellationToken);
    Task<HrExitApprovalSubmission?> PrepareSubmitAsync(
        long businessId, long exitCaseId, long expectedVersion, CancellationToken cancellationToken);
    Task<ApprovalScopeType?> FindApprovalScopeTypeAsync(
        long businessId, long exitCaseId, CancellationToken cancellationToken);
    Task<string?> GetApprovalScopeReferenceAsync(
        long businessId, long exitCaseId, ApprovalScopeType scopeType, CancellationToken cancellationToken);
    Task<long?> GetApprovalInstanceIdAsync(
        long businessId, long exitCaseId, CancellationToken cancellationToken);
    Task<HrExitCaseModel?> UpdateClearanceAsync(
        long businessId, long actorUserId, HrClearanceUpdateInput input, CancellationToken cancellationToken);
    Task<HrExitCaseModel?> CompleteNonFinancialAsync(
        long businessId, long actorUserId, long exitCaseId, long expectedVersion,
        DateOnly actualLastWorkingDate, string reason, CancellationToken cancellationToken);
    Task<HrExitCaseModel?> CancelDraftAsync(
        long businessId, long actorUserId, long exitCaseId, long expectedVersion,
        string reason, CancellationToken cancellationToken);
    Task ApproveWithoutApprovalAsync(
        long businessId, long actorUserId, long exitCaseId, long expectedVersion, CancellationToken cancellationToken);
    Task<ApprovalTargetSnapshot?> ReadAsync(
        long businessId, string targetType, string targetId, CancellationToken cancellationToken);
}

public static class HrExitApprovalConstants
{
    public const string TargetType = "HrExitCase";
}

public sealed record CreateHrClearanceTemplateCommand(HrClearanceTemplateInput Input)
    : ICommand<HrClearanceTemplateModel>;
public sealed record GetHrClearanceTemplatesQuery : IRequest<IReadOnlyList<HrClearanceTemplateModel>>;
public sealed record InitiateHrExitCommand(HrExitCaseInput Input) : ICommand<HrExitCaseModel>;
public sealed record GetHrExitCasesQuery(DateOnly? From, DateOnly? To, string? Status, long? EmployeeId)
    : IRequest<IReadOnlyList<HrExitCaseModel>>;
public sealed record GetHrExitCaseQuery(long Id) : IRequest<HrExitCaseModel?>;
public sealed record GetHrExitCompletionPreviewQuery(long Id) : IRequest<HrExitCompletionPreviewModel?>;
public sealed record SubmitHrExitCommand(long Id, long ExpectedVersion) : ICommand<ApprovalDecisionResult>;
public sealed record UpdateHrClearanceItemCommand(HrClearanceUpdateInput Input) : ICommand<HrExitCaseModel?>;
public sealed record CompleteNonFinancialHrExitCommand(
    long Id, long ExpectedVersion, DateOnly ActualLastWorkingDate, string Reason) : ICommand<HrExitCaseModel?>;
public sealed record CancelHrExitCommand(long Id, long ExpectedVersion, string DecisionKey, string Reason)
    : ICommand<ApprovalDecisionResult>;

public sealed class HrExitHandlers(
    IHrExitStore store,
    IHrExitAccess access,
    ISender sender,
    IHrPayrollCapabilityService capabilities,
    IRequiredAuditTransaction audit)
    : IRequestHandler<CreateHrClearanceTemplateCommand, HrClearanceTemplateModel>,
      IRequestHandler<GetHrClearanceTemplatesQuery, IReadOnlyList<HrClearanceTemplateModel>>,
      IRequestHandler<InitiateHrExitCommand, HrExitCaseModel>,
      IRequestHandler<GetHrExitCasesQuery, IReadOnlyList<HrExitCaseModel>>,
      IRequestHandler<GetHrExitCaseQuery, HrExitCaseModel?>,
      IRequestHandler<GetHrExitCompletionPreviewQuery, HrExitCompletionPreviewModel?>,
      IRequestHandler<SubmitHrExitCommand, ApprovalDecisionResult>,
      IRequestHandler<UpdateHrClearanceItemCommand, HrExitCaseModel?>,
      IRequestHandler<CompleteNonFinancialHrExitCommand, HrExitCaseModel?>,
      IRequestHandler<CancelHrExitCommand, ApprovalDecisionResult>
{
    public async Task<HrClearanceTemplateModel> Handle(
        CreateHrClearanceTemplateCommand request, CancellationToken cancellationToken)
    {
        ValidateTemplate(request.Input);
        var businessId = access.RequireBusinessId();
        await access.EnsureTemplateScopeAsync(businessId, request.Input.ScopeType,
            request.Input.ScopeReference, cancellationToken);
        await access.EnsureAsync(businessId, HrExitOperation.Clearance, null,
            ParseLocation(request.Input), cancellationToken);
        var actor = access.RequireRealUserId();
        return await audit.ExecuteAsync(new RequiredAuditOperation(
                businessId, "hr.exit.clearance_template.created", "HR",
                "Created a versioned non-financial clearance template.",
                "HrClearanceTemplate", $"{request.Input.Code}:{request.Input.VersionNo}"),
            token => store.CreateTemplateAsync(businessId, actor, request.Input, token), cancellationToken);
    }

    public async Task<IReadOnlyList<HrClearanceTemplateModel>> Handle(
        GetHrClearanceTemplatesQuery request, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrExitOperation.Clearance, null, null, cancellationToken);
        return await store.ListTemplatesAsync(businessId, cancellationToken);
    }

    public async Task<HrExitCaseModel> Handle(
        InitiateHrExitCommand request, CancellationToken cancellationToken)
    {
        ValidateExit(request.Input);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrExitOperation.Initiate,
            request.Input.EmployeeId, null, cancellationToken);
        var actor = access.RequireRealUserId();
        return await audit.ExecuteAsync(new RequiredAuditOperation(
                businessId, "hr.exit.initiated", "HR", "Initiated a non-financial employee exit.",
                "HrEmployee", request.Input.EmployeeId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.InitiateAsync(businessId, actor, request.Input, token), cancellationToken);
    }

    public async Task<IReadOnlyList<HrExitCaseModel>> Handle(
        GetHrExitCasesQuery request, CancellationToken cancellationToken)
    {
        ValidateDateRange(request.From, request.To);
        var businessId = access.RequireBusinessId();
        await access.EnsureAsync(businessId, HrExitOperation.View, request.EmployeeId, null, cancellationToken);
        var locationIds = await access.GetAccessibleLocationIdsAsync(businessId, cancellationToken);
        var rows = await store.ListAsync(businessId, request.From, request.To, request.Status,
            request.EmployeeId, locationIds, cancellationToken);
        foreach (var row in rows)
            await access.EnsureAsync(businessId, HrExitOperation.View, row.EmployeeId,
                row.PrimaryLocationProjectionId, cancellationToken);
        return rows;
    }

    public async Task<HrExitCaseModel?> Handle(
        GetHrExitCaseQuery request, CancellationToken cancellationToken)
    {
        if (request.Id <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Id));
        var businessId = access.RequireBusinessId();
        var scope = await store.GetScopeAsync(businessId, request.Id, cancellationToken);
        if (scope is null)
            return null;
        await access.EnsureAsync(businessId, HrExitOperation.View, scope.EmployeeId,
            scope.PrimaryLocationProjectionId, cancellationToken);
        return await store.GetAsync(businessId, request.Id, cancellationToken);
    }

    public async Task<HrExitCompletionPreviewModel?> Handle(
        GetHrExitCompletionPreviewQuery request, CancellationToken cancellationToken)
    {
        if (request.Id <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Id));
        var businessId = access.RequireBusinessId();
        var scope = await store.GetScopeAsync(businessId, request.Id, cancellationToken);
        if (scope is null)
            return null;
        await access.EnsureAsync(businessId, HrExitOperation.Clearance,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        var capabilitySet = await capabilities.GetAsync(cancellationToken);
        if (capabilitySet.PayrollEnabled)
            await access.EnsureAsync(businessId, HrExitOperation.FinalSettlementView,
                scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        return await audit.ExecuteAsync(
            new RequiredAuditOperation(
                businessId, "hr.exit.completion.previewed", "HR",
                "Accessed exit completion readiness and financial clearance preview.",
                "HrExitCase", request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.GetCompletionPreviewAsync(businessId, request.Id, token),
            result => result is not null,
            cancellationToken);
    }

    public async Task<ApprovalDecisionResult> Handle(
        SubmitHrExitCommand request, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        var scope = await store.GetScopeAsync(businessId, request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Exit case was not found in this Business.");
        await access.EnsureAsync(businessId, HrExitOperation.Initiate,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        var prepared = await store.PrepareSubmitAsync(
            businessId, request.Id, request.ExpectedVersion, cancellationToken)
            ?? throw new InvalidOperationException("Exit case is not a submittable draft.");
        var approvalScope = await store.FindApprovalScopeTypeAsync(businessId, request.Id, cancellationToken);
        if (approvalScope is null)
        {
            await audit.ExecuteAsync(new RequiredAuditOperation(
                    businessId, "hr.exit.approved_without_policy", "HR",
                    "Approved an exit where no Approval Kernel policy is configured.",
                    "HrExitCase", request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                async token =>
                {
                    await store.ApproveWithoutApprovalAsync(
                        businessId, access.RequireRealUserId(), request.Id, prepared.Version, token);
                    return true;
                },
                cancellationToken);
            return new ApprovalDecisionResult(0, ApprovalInstanceStatus.Approved, null, false, "Approved");
        }

        var approvalScopeReference = await store.GetApprovalScopeReferenceAsync(
            businessId, request.Id, approvalScope.Value, cancellationToken);
        return await sender.Send(new SubmitApprovalCommand(
            ApprovalProcessType.Exit,
            HrExitApprovalConstants.TargetType,
            request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            approvalScope.Value,
            approvalScopeReference,
            prepared.Version,
            prepared.RequestHash), cancellationToken);
    }

    public async Task<HrExitCaseModel?> Handle(
        UpdateHrClearanceItemCommand request, CancellationToken cancellationToken)
    {
        ValidateClearanceUpdate(request.Input);
        var businessId = access.RequireBusinessId();
        var scope = await store.GetClearanceItemScopeAsync(
            businessId, request.Input.ItemId, cancellationToken);
        if (scope is null)
            return null;
        await access.EnsureAsync(businessId, HrExitOperation.Clearance,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        var actor = access.RequireRealUserId();
        return await audit.ExecuteAsync(new RequiredAuditOperation(
                businessId, "hr.exit.clearance_item.updated", "HR",
                "Updated an exit clearance item.",
                "HrClearanceItem", request.Input.ItemId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.UpdateClearanceAsync(businessId, actor, request.Input, token),
            result => result is not null, cancellationToken);
    }

    public async Task<HrExitCaseModel?> Handle(
        CompleteNonFinancialHrExitCommand request, CancellationToken cancellationToken)
    {
        ValidateCompletion(request);
        var businessId = access.RequireBusinessId();
        var scope = await store.GetScopeAsync(businessId, request.Id, cancellationToken);
        if (scope is null)
            return null;
        await access.EnsureAsync(businessId, HrExitOperation.Clearance,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        return await audit.ExecuteAsync(new RequiredAuditOperation(
                businessId, "hr.exit.completed", "HR",
                "Completed employee exit after authoritative clearance and financial validation.",
                "HrExitCase", request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.CompleteNonFinancialAsync(businessId, access.RequireRealUserId(),
                request.Id, request.ExpectedVersion, request.ActualLastWorkingDate, request.Reason, token),
            result => result is not null, cancellationToken);
    }

    public async Task<ApprovalDecisionResult> Handle(
        CancelHrExitCommand request, CancellationToken cancellationToken)
    {
        if (request.Id <= 0 || request.ExpectedVersion <= 0
            || string.IsNullOrWhiteSpace(request.DecisionKey) || request.DecisionKey.Length > 150
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000)
            throw new ArgumentException("Exit ID, expected version, decision key and reason are required.");
        var businessId = access.RequireBusinessId();
        var scope = await store.GetScopeAsync(businessId, request.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Exit case was not found in this Business.");
        await access.EnsureAsync(businessId, HrExitOperation.Initiate,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        var instanceId = await store.GetApprovalInstanceIdAsync(businessId, request.Id, cancellationToken);
        if (instanceId is > 0)
            return await sender.Send(new CancelApprovalCommand(instanceId.Value,
                request.DecisionKey, request.Reason), cancellationToken);
        var cancelled = await audit.ExecuteAsync(new RequiredAuditOperation(
                businessId, "hr.exit.cancelled", "HR", "Cancelled a draft exit case.",
                "HrExitCase", request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            token => store.CancelDraftAsync(businessId, access.RequireRealUserId(), request.Id,
                request.ExpectedVersion, request.Reason, token),
            result => result is not null, cancellationToken);
        return cancelled is null
            ? throw new KeyNotFoundException("Exit case was not found in this Business.")
            : new ApprovalDecisionResult(0, ApprovalInstanceStatus.Cancelled, null, false, cancelled.Status);
    }

    private static void ValidateTemplate(HrClearanceTemplateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Trim().Length > 50
            || string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 100
            || input.VersionNo <= 0 || input.EffectiveFrom == default
            || input.EffectiveTo < input.EffectiveFrom
            || input.Items is null || input.Items.Count is < 1 or > 100)
            throw new ArgumentException("Clearance template identity, dates and 1-100 items are required.");
        if (!Enum.IsDefined(input.ScopeType)
            || (input.ScopeType == HrClearanceTemplateScopeType.Business)
                != string.IsNullOrWhiteSpace(input.ScopeReference))
            throw new ArgumentException("Clearance template scope reference does not match its scope type.");
        if (input.ScopeType == HrClearanceTemplateScopeType.Location
            && (!long.TryParse(input.ScopeReference, out var locationId) || locationId <= 0))
            throw new ArgumentException("Location template scope must be a positive location ID.");
        if (input.ScopeType == HrClearanceTemplateScopeType.LocationType
            && input.ScopeReference is not ("Outlet" or "Warehouse" or "Factory"))
            throw new ArgumentException("Location-type template scope is not supported.");
        if (input.Items.Any(item => item.Sequence <= 0 || string.IsNullOrWhiteSpace(item.Code)
                || item.Code.Trim().Length > 50 || string.IsNullOrWhiteSpace(item.Name)
                || item.Name.Trim().Length > 100
                || item.ResponsibleRole?.Length > 100 || item.ResponsibleReference?.Length > 150)
            || input.Items.Select(item => item.Sequence).Distinct().Count() != input.Items.Count
            || input.Items.Select(item => item.Code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                != input.Items.Count)
            throw new ArgumentException("Clearance template items must have unique sequences and codes.");
    }

    private static long? ParseLocation(HrClearanceTemplateInput input) =>
        input.ScopeType == HrClearanceTemplateScopeType.Location
            && long.TryParse(input.ScopeReference, out var id) ? id : null;

    private static void ValidateExit(HrExitCaseInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.EmployeeId <= 0 || string.IsNullOrWhiteSpace(input.ExitType)
            || input.ExitType.Trim().Length > 50 || input.InitiatedDate == default
            || input.ProposedLastWorkingDate < input.InitiatedDate
            || string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 1000)
            throw new ArgumentException("Employee, exit type, valid dates and reason are required.");
    }

    private static void ValidateClearanceUpdate(HrClearanceUpdateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.ItemId <= 0 || input.ExpectedVersion <= 0
            || input.Status is not ("Cleared" or "Exception" or "NotApplicable" or "Pending")
            || input.Comment?.Trim().Length > 1000 || input.EvidenceReference?.Trim().Length > 500)
            throw new ArgumentException("Clearance item status or evidence fields are invalid.");
        if (input.Status is "Exception" or "NotApplicable" && string.IsNullOrWhiteSpace(input.Comment))
            throw new ArgumentException("Exception and NotApplicable clearance statuses require a reason.");
    }

    private static void ValidateCompletion(CompleteNonFinancialHrExitCommand request)
    {
        if (request.Id <= 0 || request.ExpectedVersion <= 0 || request.ActualLastWorkingDate == default
            || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            throw new ArgumentException("Exit ID, expected version, actual last working date and reason are required.");
    }

    private static void ValidateDateRange(DateOnly? from, DateOnly? to)
    {
        if (from.HasValue != to.HasValue
            || from.HasValue && (to!.Value < from.Value
                || to.Value.DayNumber - from.Value.DayNumber > 366))
            throw new ArgumentException("Provide both valid inclusive dates or neither.");
    }
}
