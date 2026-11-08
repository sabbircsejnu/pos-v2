using System.Security.Cryptography;
using MediatR;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Authentication.Abstractions;
using RetailPOS.Application.Authentication.Commands;
using RetailPOS.Application.Exchanges.Abstractions;
using RetailPOS.Application.Identity.LocationAccess;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Core.Entities.Exchanges;

namespace RetailPOS.Application.Exchanges;

// ======================= Policy (D-16, D-07, DN-01) =======================

public sealed record ExchangePolicyReadModel(
    bool IsConfigured,
    int? VersionNo,
    bool IsEnabled,
    bool AllowLowerPriceExchange,
    int? ExchangeWindowDays,
    bool AllowReplacementReExchange,
    int? ReplacementWindowDays,
    int? MaxExchangeCountPerLineage,
    string PhoneVerificationMode,
    bool RequiredPhoneVerificationAvailable,
    decimal? HighValueApprovalThreshold,
    string NegativeTargetAdjustmentMode,
    DateTime? UpdatedAtUtc);

public sealed record GetExchangePolicyQuery : IRequest<ExchangePolicyReadModel>;

public sealed record SaveExchangePolicyCommand(
    int? ExpectedVersionNo,
    bool IsEnabled,
    bool AllowLowerPriceExchange,
    int ExchangeWindowDays,
    bool AllowReplacementReExchange,
    int? ReplacementWindowDays,
    int? MaxExchangeCountPerLineage,
    string PhoneVerificationMode,
    decimal? HighValueApprovalThreshold,
    string NegativeTargetAdjustmentMode,
    long ActorUserId) : IRequest<ExchangePolicyReadModel>;

public sealed class ExchangePolicyHandlers(IBusinessScope businessScope, IExchangeStore store, IExchangeAuditWriter audit)
    : IRequestHandler<GetExchangePolicyQuery, ExchangePolicyReadModel>, IRequestHandler<SaveExchangePolicyCommand, ExchangePolicyReadModel>
{
    public const int MaxWindowDays = 3650;

    public async Task<ExchangePolicyReadModel> Handle(GetExchangePolicyQuery request, CancellationToken cancellationToken) =>
        Read(await store.FindCurrentPolicyAsync(businessScope.RequireBusinessId(), cancellationToken));

    public async Task<ExchangePolicyReadModel> Handle(SaveExchangePolicyCommand request, CancellationToken cancellationToken)
    {
        Validate(request);
        var businessId = businessScope.RequireBusinessId();
        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        try
        {
            var current = await store.FindCurrentPolicyAsync(businessId, cancellationToken);
            if (current?.VersionNo != request.ExpectedVersionNo)
                throw ExchangeException.Conflict(ExchangeErrorCodes.PolicyVersionConflict,
                    "The exchange policy was changed by someone else. Reload it and try again.");

            var next = new ExchangePolicyVersion
            {
                BusinessId = businessId,
                VersionNo = (current?.VersionNo ?? 0) + 1,
                IsEnabled = request.IsEnabled,
                AllowLowerPriceExchange = request.AllowLowerPriceExchange,
                ExchangeWindowDays = request.ExchangeWindowDays,
                AllowReplacementReExchange = request.AllowReplacementReExchange,
                ReplacementWindowDays = request.AllowReplacementReExchange ? request.ReplacementWindowDays : null,
                MaxExchangeCountPerLineage = request.AllowReplacementReExchange ? request.MaxExchangeCountPerLineage : null,
                PhoneVerificationMode = request.PhoneVerificationMode,
                HighValueApprovalThreshold = request.HighValueApprovalThreshold,
                NegativeTargetAdjustmentMode = request.NegativeTargetAdjustmentMode,
                CreatedByUserId = request.ActorUserId,
                CreatedAtUtc = DateTime.UtcNow
            };
            await store.AddPolicyAsync(next, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            await audit.RecordAsync("exchange.policy_saved", $"Exchange policy version {next.VersionNo} saved", 0,
                new Dictionary<string, object?>
                {
                    ["versionNo"] = next.VersionNo,
                    ["isEnabled"] = next.IsEnabled,
                    ["allowLowerPriceExchange"] = next.AllowLowerPriceExchange,
                    ["exchangeWindowDays"] = next.ExchangeWindowDays,
                    ["allowReplacementReExchange"] = next.AllowReplacementReExchange,
                    ["replacementWindowDays"] = next.ReplacementWindowDays,
                    ["maxExchangeCountPerLineage"] = next.MaxExchangeCountPerLineage,
                    ["highValueApprovalThreshold"] = next.HighValueApprovalThreshold,
                    ["negativeTargetAdjustmentMode"] = next.NegativeTargetAdjustmentMode
                },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Read(next);
        }
        catch (Exception exception) when (store.IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw ExchangeException.Conflict(ExchangeErrorCodes.PolicyVersionConflict, "The exchange policy was changed by someone else. Reload it and try again.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void Validate(SaveExchangePolicyCommand request)
    {
        static ExchangeException Invalid(string message) => ExchangeException.BadRequest(ExchangeErrorCodes.InvalidPolicy, message);

        if (request.ExchangeWindowDays is < 0 or > MaxWindowDays)
            throw Invalid($"The exchange window must be between 0 and {MaxWindowDays} days.");
        if (request.AllowReplacementReExchange)
        {
            if (request.ReplacementWindowDays is null or < 0 or > MaxWindowDays)
                throw Invalid("Set the replacement window (days) when replacements may be exchanged again.");
            if (request.MaxExchangeCountPerLineage is null or < 1 or > 50)
                throw Invalid("Set the maximum number of exchanges per item (1 to 50) when replacements may be exchanged again.");
        }

        if (request.HighValueApprovalThreshold is { } threshold && (threshold <= 0m || threshold != decimal.Round(threshold, 2)))
            throw Invalid("The high-value threshold must be a positive amount with at most two decimals, or empty to switch it off.");
        if (!ExchangeNegativeTargetModes.All.Contains(request.NegativeTargetAdjustmentMode))
            throw Invalid("Choose how negative salesperson adjustments are treated.");

        // DN-01: there is no trustworthy phone verification, so 'required' cannot be offered (it would look secure and is not).
        if (request.PhoneVerificationMode == ExchangePhoneVerificationModes.Required)
            throw ExchangeException.Unprocessable(ExchangeErrorCodes.PhoneVerificationUnavailable,
                "Required phone verification is not available yet: customer phone numbers cannot currently be verified.");
        if (request.PhoneVerificationMode != ExchangePhoneVerificationModes.Optional)
            throw Invalid("Choose a phone verification mode.");
    }

    private static ExchangePolicyReadModel Read(ExchangePolicyVersion? policy) => policy is null
        ? new ExchangePolicyReadModel(false, null, false, false, null, false, null, null, ExchangePhoneVerificationModes.Optional,
            false, null, ExchangeNegativeTargetModes.TrackOnly, null)
        : new ExchangePolicyReadModel(true, policy.VersionNo, policy.IsEnabled, policy.AllowLowerPriceExchange, policy.ExchangeWindowDays,
            policy.AllowReplacementReExchange, policy.ReplacementWindowDays, policy.MaxExchangeCountPerLineage, policy.PhoneVerificationMode,
            false, policy.HighValueApprovalThreshold, policy.NegativeTargetAdjustmentMode, policy.CreatedAtUtc);
}

// ======================= Preview (EX-29) =======================

public sealed record ExchangePreviewReturnLine(
    string SourceKind, long SourceLineId, long VariantId, decimal Quantity, decimal CreditNet, decimal CreditVat, decimal CreditGross,
    string AnchorDate, bool WindowExpired, bool ChainExceeded);

public sealed record ExchangePreviewReplacementLine(
    long VariantId, long? SellingOptionId, decimal BaseQuantity, decimal UnitPrice, decimal LineDiscount, decimal InvoiceDiscountShare,
    decimal TaxableAmount, decimal VatRate, decimal VatAmount, decimal Gross, decimal AvailableStock);

public sealed record ExchangePreviewReadModel(
    string PreviewHash,
    string EligibilityLocalDate,
    long ReceivingOutletId,
    long TerminalId,
    long CustomerId,
    string PhoneVerificationResult,
    IReadOnlyList<ExchangePreviewReturnLine> Returns,
    IReadOnlyList<ExchangePreviewReplacementLine> Replacements,
    decimal ReturnNet, decimal ReturnVat, decimal ReturnGross,
    decimal ReplacementNet, decimal ReplacementVat, decimal ReplacementGross, decimal ReplacementDiscount,
    decimal NetAdjustmentGross, string SettlementDirection, decimal SettlementAmount, decimal HighValueAmount,
    IReadOnlyList<string> RequiredApprovals,
    int PolicyVersionNo,
    string LoyaltyStatus);

public sealed record PreviewExchangeQuery(ExchangeRequest Request, ExchangeActor Actor) : IRequest<ExchangePreviewReadModel>;

public sealed class PreviewExchangeQueryHandler(IBusinessScope businessScope, IExchangeStore store, ExchangeEvaluator evaluator)
    : IRequestHandler<PreviewExchangeQuery, ExchangePreviewReadModel>
{
    public async Task<ExchangePreviewReadModel> Handle(PreviewExchangeQuery request, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var evaluated = await ExchangeSources.EvaluateUnlockedAsync(store, evaluator, businessId, request.Request, request.Actor, cancellationToken);
        var stock = await store.ReadOutletStockAsync(
            request.Request.ReceivingOutletId,
            evaluated.Replacements.Select(line => line.Input.VariantId).Distinct().ToList(),
            cancellationToken);
        foreach (var group in evaluated.Replacements.GroupBy(line => line.Input.VariantId))
            if (stock.GetValueOrDefault(group.Key) < group.Sum(line => line.Uom.BaseQuantity))
                throw ExchangeException.Unprocessable(ExchangeErrorCodes.StockInsufficient,
                    $"Not enough stock of variant ID {group.Key} at this outlet for the replacement.");
        return ExchangeSources.ToPreview(evaluated, stock);
    }
}

internal static class ExchangeSources
{
    public static async Task<EvaluatedExchange> EvaluateUnlockedAsync(
        IExchangeStore store, ExchangeEvaluator evaluator, long businessId, ExchangeRequest request, ExchangeActor actor, CancellationToken cancellationToken)
    {
        var sales = await store.LoadSourceSalesAsync(businessId, request.SourceSaleIds.Distinct().ToList(), forUpdate: false, cancellationToken);
        var replacementIds = request.Returns.Where(line => line.SourceKind == ExchangeSourceKinds.ReplacementLine).Select(line => line.SourceLineId).Distinct().ToList();
        var sources = replacementIds.Count == 0
            ? []
            : await store.LoadReplacementLinesAsync(businessId, replacementIds, forUpdate: false, cancellationToken);
        return await evaluator.EvaluateAsync(businessId, request, actor, sales, sources, DateTime.UtcNow, cancellationToken);
    }

    public static ExchangePreviewReadModel ToPreview(EvaluatedExchange evaluated, IReadOnlyDictionary<long, decimal>? stock = null) => new(
        evaluated.PreviewHash,
        evaluated.EligibilityLocalDate.ToString("yyyy-MM-dd"),
        evaluated.ReceivingOutletId,
        evaluated.TerminalId,
        evaluated.CustomerId,
        evaluated.PhoneVerificationResult,
        evaluated.Returns.Select(line => new ExchangePreviewReturnLine(
            line.Input.SourceKind, line.Input.SourceLineId, line.VariantId, line.Input.Quantity,
            line.CreditNet, line.CreditVat, line.CreditNet + line.CreditVat,
            line.AnchorDate.ToString("yyyy-MM-dd"), line.WindowExpired, line.ChainExceeded)).ToList(),
        evaluated.Replacements.Select(line => new ExchangePreviewReplacementLine(
            line.Input.VariantId, line.Input.SellingOptionId, line.Line.Quantity, line.Line.UnitPrice, line.Line.LineDiscount,
            line.Line.InvoiceDiscountShare, line.Line.TaxableAmount, line.Line.TaxRatePercent, line.Line.VatAmount,
            line.Line.TaxableAmount + line.Line.VatAmount, stock?.GetValueOrDefault(line.Input.VariantId) ?? 0m)).ToList(),
        evaluated.Totals.ReturnNet, evaluated.Totals.ReturnVat, evaluated.Totals.ReturnGross,
        evaluated.Totals.ReplacementNet, evaluated.Totals.ReplacementVat, evaluated.Totals.ReplacementGross, evaluated.Totals.ReplacementDiscount,
        evaluated.Totals.NetAdjustmentGross, evaluated.Totals.Direction, evaluated.Totals.SettlementAmount, evaluated.Totals.HighValueAmount,
        evaluated.RequiredApprovals,
        evaluated.Policy.VersionNo,
        ExchangeLoyaltyStatuses.ModuleInactive);
}

// ======================= Supervisor approval (D-04, C-02, C-10, OC-01, OC-02, OC-03) =======================

public sealed record ExchangeApprovalItem(string Type, string Reason);

public sealed record ExchangeRefundApprovalTerms(decimal Amount, string Method);

public sealed record GrantExchangeApprovalsCommand(
    ExchangeRequest Request,
    ExchangeActor Actor,
    string PreviewHash,
    IReadOnlyList<ExchangeApprovalItem> Approvals,
    string SupervisorLogin,
    string SupervisorPassword,
    ExchangeRefundApprovalTerms? RefundTerms) : IRequest<IReadOnlyList<ExchangeApprovalTokenReadModel>>;

public sealed record ExchangeApprovalTokenReadModel(long ApprovalId, string ApprovalToken, string Type, DateTime ExpiresAtUtc, string SupervisorName);

public sealed class GrantExchangeApprovalsCommandHandler(
    IBusinessScope businessScope,
    IExchangeStore store,
    ExchangeEvaluator evaluator,
    IAuthenticationStore users,
    IPasswordHasher passwordHasher,
    IUserOutletAccess outletAccess,
    IExchangeAuditWriter audit) : IRequestHandler<GrantExchangeApprovalsCommand, IReadOnlyList<ExchangeApprovalTokenReadModel>>
{
    private static string? unknownUserHash;

    /// <summary>Technical lifetime of a one-time approval (C-02 "short-lived"); not a business rule.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// L1b lockout (technical security control, not a business rule): failures are counted per supervisor login and per
    /// requesting cashier within the business over a sliding window. The per-IP-and-cashier rate limit stays in front.
    /// </summary>
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    public const int SupervisorFailureLimit = 5;
    public const int CashierFailureLimit = 10;
    public const string SupervisorSubject = "supervisor";
    public const string CashierSubject = "cashier";

    public static string SupervisorKey(long businessId, string? login) =>
        ExchangeCalculator.Sha256($"exchange-approval|{businessId}|supervisor|{(login ?? string.Empty).Trim().ToLowerInvariant()}");

    public static string CashierKey(long businessId, long cashierUserId) =>
        ExchangeCalculator.Sha256($"exchange-approval|{businessId}|cashier|{cashierUserId.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

    public static readonly IReadOnlyDictionary<string, string> RequiredPermission = new Dictionary<string, string>
    {
        [ExchangeApprovalTypes.Window] = PermissionCatalog.ExchangeOverrideWindow,
        [ExchangeApprovalTypes.HighValue] = PermissionCatalog.ExchangeOverrideWindow, // C-10
        [ExchangeApprovalTypes.Chain] = PermissionCatalog.ExchangeOverrideChain,       // OC-02
        [ExchangeApprovalTypes.Refund] = PermissionCatalog.ExchangeRefund              // OC-03 Case B
    };

    public async Task<IReadOnlyList<ExchangeApprovalTokenReadModel>> Handle(GrantExchangeApprovalsCommand request, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var types = request.Approvals.Select(item => item.Type).ToList();
        if (types.Count == 0 || types.Count != types.Distinct(StringComparer.Ordinal).Count() || types.Any(type => !RequiredPermission.ContainsKey(type)))
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "List each required approval once.");
        if (request.Approvals.Any(item => string.IsNullOrWhiteSpace(item.Reason) || item.Reason.Trim().Length > 500))
            throw ExchangeException.BadRequest(ExchangeErrorCodes.ReasonRequired, "Give a reason for every approval.");

        // The approval covers exactly what was reviewed: recompute it now (with today's local date).
        var evaluated = await ExchangeSources.EvaluateUnlockedAsync(store, evaluator, businessId, request.Request, request.Actor, cancellationToken);
        if (!string.Equals(evaluated.PreviewHash, request.PreviewHash, StringComparison.Ordinal))
            throw ExchangeException.Conflict(ExchangeErrorCodes.PreviewStale, "The exchange changed since it was reviewed. Review it again.");
        if (types.Any(type => !evaluated.RequiredApprovals.Contains(type)))
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "Only approvals this exchange needs can be granted.");
        if (types.Contains(ExchangeApprovalTypes.Refund)
            && (request.RefundTerms is null
                || request.RefundTerms.Amount != evaluated.Totals.SettlementAmount
                || !ExchangeSettlementMethods.All.Contains(request.RefundTerms.Method?.Trim().ToLowerInvariant() ?? string.Empty)))
            throw ExchangeException.BadRequest(ExchangeErrorCodes.SettlementMismatch, "A refund approval names the exact refund amount and method.");

        // L1b: a locked supervisor login or cashier is refused before any credential check. The key is the login as typed,
        // so an unknown login locks exactly like a real one and the answer never reveals which accounts exist.
        var supervisorKey = SupervisorKey(businessId, request.SupervisorLogin);
        var cashierKey = CashierKey(businessId, request.Actor.UserId);
        var since = DateTime.UtcNow.Subtract(FailureWindow);
        if (await store.CountApprovalFailuresAsync(businessId, SupervisorSubject, supervisorKey, since, cancellationToken) >= SupervisorFailureLimit
            || await store.CountApprovalFailuresAsync(businessId, CashierSubject, cashierKey, since, cancellationToken) >= CashierFailureLimit)
        {
            await audit.RecordAsync("exchange.approval_locked", "Exchange supervisor approval refused: too many failed attempts", 0,
                new Dictionary<string, object?> { ["types"] = string.Join(',', types), ["cashierUserId"] = request.Actor.UserId }, cancellationToken);
            throw ExchangeException.TooManyRequests(ExchangeErrorCodes.ApprovalLocked,
                $"Too many failed supervisor sign-ins. Wait {FailureWindow.TotalMinutes:0} minutes and try again.");
        }

        // Independent supervisor authentication; one generic answer for every credential failure.
        var supervisor = await users.FindByEmailAsync(request.SupervisorLogin?.Trim() ?? string.Empty, cancellationToken);
        var credentialsValid = supervisor is not null
            && !string.IsNullOrEmpty(request.SupervisorPassword)
            && passwordHasher.Verify(request.SupervisorPassword, supervisor.PasswordHash);
        if (supervisor is null)
            // Same hashing work for an unknown login, so response time does not reveal which supervisor emails exist.
            passwordHasher.Verify(request.SupervisorPassword ?? string.Empty,
                unknownUserHash ??= passwordHasher.Hash(Guid.NewGuid().ToString("N")));
        if (!credentialsValid || supervisor is null || !supervisor.IsActive || supervisor.MustResetPassword
            || supervisor.BusinessId != businessId || supervisor.Id == request.Actor.UserId)
        {
            var failedAt = DateTime.UtcNow;
            await store.RecordApprovalFailuresAsync(
            [
                new ExchangeApprovalFailure { BusinessId = businessId, SubjectKind = SupervisorSubject, SubjectHash = supervisorKey, AttemptedAtUtc = failedAt },
                new ExchangeApprovalFailure { BusinessId = businessId, SubjectKind = CashierSubject, SubjectHash = cashierKey, AttemptedAtUtc = failedAt }
            ], cancellationToken);
            await audit.RecordAsync("exchange.approval_denied", "Exchange supervisor approval denied", 0,
                new Dictionary<string, object?> { ["types"] = string.Join(',', types), ["cashierUserId"] = request.Actor.UserId }, cancellationToken);
            throw ExchangeException.Forbidden(ExchangeErrorCodes.ApprovalInvalid,
                "The supervisor could not be verified. A different, active supervisor of this business must sign in.");
        }

        var basis = await ExchangeSupervisorAuthorization.AuthorizeAsync(outletAccess, supervisor.Id, supervisor.RoleName,
            supervisor.RolePermissions, supervisor.PermissionOverrides, businessId, request.Request.ReceivingOutletId, types);

        var now = DateTime.UtcNow;
        var group = Guid.NewGuid();
        var issued = new List<(ExchangeApproval Approval, string Token)>();
        foreach (var item in request.Approvals)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            issued.Add((new ExchangeApproval
            {
                BusinessId = businessId,
                ApprovalType = item.Type,
                ReceivingOutletId = request.Request.ReceivingOutletId,
                TerminalId = evaluated.TerminalId,
                CashierUserId = request.Actor.UserId,
                SupervisorUserId = supervisor.Id,
                PermissionUsed = RequiredPermission[item.Type],
                SupervisorOutletAccessBasis = basis,
                Reason = item.Reason.Trim(),
                PayloadHash = PayloadHash(item.Type, evaluated.PreviewHash, request.RefundTerms),
                TokenHash = ExchangeCalculator.Sha256(token),
                AuthenticationGroupId = group,
                Status = "granted",
                ExpiresAtUtc = now.Add(Lifetime),
                CreatedAtUtc = now
            }, token));
        }

        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        try
        {
            await store.AddApprovalsAsync(issued.Select(entry => entry.Approval).ToList(), cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            await store.ClearApprovalFailuresAsync(businessId, [supervisorKey, cashierKey], cancellationToken);
            await audit.RecordAsync("exchange.approval_granted", $"Exchange approval granted: {string.Join(", ", types)}", 0,
                new Dictionary<string, object?>
                {
                    ["types"] = string.Join(',', types),
                    ["cashierUserId"] = request.Actor.UserId,
                    ["supervisorUserId"] = supervisor.Id,
                    ["outletAccessBasis"] = basis,
                    ["receivingOutletId"] = request.Request.ReceivingOutletId,
                    ["highValueAmount"] = evaluated.Totals.HighValueAmount,
                    ["highValueThreshold"] = evaluated.Policy.HighValueApprovalThreshold,
                    ["policyVersionNo"] = evaluated.Policy.VersionNo,
                    ["refundAmount"] = request.RefundTerms?.Amount,
                    ["refundMethod"] = request.RefundTerms?.Method,
                    ["eligibilityLocalDate"] = evaluated.EligibilityLocalDate.ToString("yyyy-MM-dd")
                },
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        return issued.Select(entry => new ExchangeApprovalTokenReadModel(
            entry.Approval.Id, entry.Token, entry.Approval.ApprovalType, entry.Approval.ExpiresAtUtc, supervisor.Name)).ToList();
    }

    public static string PayloadHash(string type, string previewHash, ExchangeRefundApprovalTerms? refundTerms) =>
        type == ExchangeApprovalTypes.Refund && refundTerms is not null
            ? ExchangeCalculator.Sha256($"{previewHash}|refund|{ExchangeCalculator.Money(refundTerms.Amount)}|{refundTerms.Method.Trim().ToLowerInvariant()}")
            : previewHash;
}
