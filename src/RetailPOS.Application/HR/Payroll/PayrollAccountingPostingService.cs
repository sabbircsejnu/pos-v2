using System.Globalization;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Capabilities;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Application.HR.Payroll;

public sealed class PayrollAccountingPostingService(
    IPayrollRunAccess access,
    IHrPayrollCapabilityService capabilities,
    IPayrollAccountingPostingStore store,
    IAccountingPostingAccountReadStore accounts,
    IFrozenSystemJournalStore accounting,
    IJournalEntryReadStore journals,
    IRequiredAuditTransaction audit,
    IPayrollAccountingCorrectionPolicy correctionPolicy,
    ILogger<PayrollAccountingPostingService> logger)
{
    private const string PostPermission = "payroll.accounting.post";
    private const string ViewPermission = "payroll.accounting.view_posting";
    private const string SalaryViewPermission = "payroll.salary.view";
    private const string MappingViewPermission = "accounting.configuration.view";
    private const string MappingEditPermission = "accounting.configuration.edit";

    public async Task<PayrollAccountingPostingResponse> PostAsync(
        long runId, CancellationToken cancellationToken)
    {
        if (runId <= 0)
            throw new ArgumentOutOfRangeException(nameof(runId));

        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await EnsurePayrollAccessAsync(businessId, PostPermission, cancellationToken);
        var initialSource = await store.FindSourceAsync(businessId, runId, cancellationToken)
            ?? throw new KeyNotFoundException("Approved Payroll run not found.");
        await access.EnsureScopeAsync(
            businessId, initialSource.ScopeType, initialSource.ScopeId, cancellationToken);
        if (initialSource.RunType == PayrollRunTypes.Correction)
            return await PostCorrectionAsync(
                businessId, actorUserId, initialSource, cancellationToken);

        FrozenSystemJournalRequest? expectedRequest = null;
        string? preflightFailure = null;
        try
        {
            ValidateSource(initialSource);
            var initialMappings = await store.GetMappingRowsAsync(businessId, cancellationToken);
            expectedRequest = await BuildRequestAsync(
                initialSource, initialMappings, businessId, cancellationToken);
        }
        catch (PayrollAccountingPostingException exception)
        {
            preflightFailure = exception.Message;
        }
        var attempt = new PostingAttempt(initialSource, expectedRequest);
        try
        {
            var accountingEnabled = await store.IsAccountingEnabledAsync(businessId, cancellationToken);
            return await audit.ExecuteAsync(
                CreateAuditOperation(
                    businessId, runId, "payroll.accounting.post_requested",
                    initialSource, expectedRequest,
                    accountingEnabled == true ? preflightFailure : null),
                async token =>
                {
                    await store.LockPostingIdentityAsync(businessId, runId, token);
                    var source = await store.FindSourceAsync(businessId, runId, token)
                        ?? throw new KeyNotFoundException("Approved Payroll run not found.");
                    await access.EnsureScopeAsync(
                        businessId, source.ScopeType, source.ScopeId, token);

                    var accountingEnabled = await store.IsAccountingEnabledAsync(businessId, token);
                    if (accountingEnabled != true)
                        return new PayrollAccountingPostingResponse(
                            null, "Accounting is not explicitly enabled for this Business; no journal was created.");

                    var existing = await store.FindPostingAsync(
                        businessId, runId, source.Revision, token);
                    if (existing?.Status == PayrollAccountingPostingStatuses.Conflict)
                        return new PayrollAccountingPostingResponse(Map(existing));

                    try
                    {
                        ValidateSource(source);
                        if (await store.HasUnresolvedCorrectionAsync(businessId, runId, token))
                            throw new PayrollAccountingPostingException(
                                "An active Payroll correction must be resolved before posting this run.");

                        var mappingRows = await store.GetMappingRowsAsync(businessId, token);
                        var request = await BuildRequestAsync(source, mappingRows, businessId, token);
                        attempt = new PostingAttempt(source, request);
                        if (expectedRequest is not null
                            && !StringComparer.Ordinal.Equals(
                                expectedRequest.PayloadHash, request.PayloadHash))
                        {
                            var changed = existing ?? CreatePosting(
                                businessId, actorUserId, source, expectedRequest);
                            changed.ConflictPayloadHash = request.PayloadHash;
                            changed.RequestPayloadJson ??= expectedRequest.CanonicalPayloadJson;
                            changed.RequestPayloadHash ??= expectedRequest.PayloadHash;
                            changed.ErrorMessage =
                                "Payroll posting evidence or account mapping changed while the request was being prepared.";
                            if (changed.Status != PayrollAccountingPostingStatuses.Posted)
                                changed.Status = PayrollAccountingPostingStatuses.Conflict;
                            changed.Version = checked(changed.Version + 1);
                            await store.SavePostingAsync(changed, token);
                            return new PayrollAccountingPostingResponse(
                                Map(changed), "The prepared Payroll posting content changed before it could be posted.");
                        }

                        if (existing is not null && existing.RequestPayloadHash is not null
                            && !StringComparer.Ordinal.Equals(
                                existing.RequestPayloadHash, request.PayloadHash))
                        {
                            existing.ConflictPayloadHash = request.PayloadHash;
                            existing.ErrorMessage =
                                "The immutable posting identity was requested with different canonical content.";
                            if (existing.Status != PayrollAccountingPostingStatuses.Posted)
                                existing.Status = PayrollAccountingPostingStatuses.Conflict;
                            existing.Version = checked(existing.Version + 1);
                            await store.SavePostingAsync(existing, token);
                            return new PayrollAccountingPostingResponse(
                                Map(existing), "This run revision is bound to different frozen posting content.");
                        }

                        if (existing?.Status == PayrollAccountingPostingStatuses.Posted)
                            return new PayrollAccountingPostingResponse(Map(existing));

                        var recoveredJournalId = await accounting.ResolveExistingAsync(request, token);
                        if (recoveredJournalId.HasValue)
                        {
                            var recoveredJournal = await journals.GetByIdAsync(
                                businessId, recoveredJournalId.Value, token);
                            if (recoveredJournal.Status != JournalEntryStatuses.Posted
                                || recoveredJournal.ReferenceId != request.PostingReference
                                || !recoveredJournal.IsSystemGenerated)
                                throw new FrozenJournalConflictException(
                                    "Accounting found the Payroll posting identity, but its journal is not the exact active Posted journal.");

                            var recovered = existing ?? new PayrollAccountingPosting
                            {
                                BusinessId = businessId,
                                PayrollPeriodId = source.PayrollPeriodId,
                                PayrollRunId = runId,
                                RunRevision = source.Revision,
                                PostingType = PayrollAccountingPostingTypes.PayrollExpense,
                                PostingReference = request.PostingReference,
                                IdempotencyKey = request.PostingReference,
                                RequestedByUserId = actorUserId,
                                RequestedAtUtc = DateTime.UtcNow,
                                Version = 1
                            };
                            recovered.RequestPayloadJson = request.CanonicalPayloadJson;
                            recovered.RequestPayloadHash = request.PayloadHash;
                            recovered.AccountingJournalId = recoveredJournalId.Value;
                            recovered.AccountingReference = recoveredJournal.JournalNumber
                                ?? recoveredJournalId.Value.ToString(CultureInfo.InvariantCulture);
                            recovered.Status = PayrollAccountingPostingStatuses.Posted;
                            recovered.ErrorMessage = null;
                            recovered.PostedAtUtc ??= recoveredJournal.PostedAt;
                            recovered.AttemptCount = checked(recovered.AttemptCount + 1);
                            recovered.Version = checked(recovered.Version + 1);
                            await store.SavePostingAsync(recovered, token);
                            logger.LogInformation(
                                "Recovered Payroll Accounting posting for Business {BusinessId}, run {PayrollRunId}, reference {PostingReference}, journal {JournalId}.",
                                businessId, runId, request.PostingReference, recoveredJournalId.Value);
                            await store.TryMarkPeriodPostedAsync(
                                businessId, source.PayrollPeriodId, actorUserId, token);
                            return new PayrollAccountingPostingResponse(Map(recovered));
                        }

                        var posting = existing ?? new PayrollAccountingPosting
                        {
                            BusinessId = businessId,
                            PayrollPeriodId = source.PayrollPeriodId,
                            PayrollRunId = runId,
                            RunRevision = source.Revision,
                            PostingType = PayrollAccountingPostingTypes.PayrollExpense,
                            PostingReference = request.PostingReference,
                            IdempotencyKey = request.PostingReference,
                            RequestedByUserId = actorUserId,
                            RequestedAtUtc = DateTime.UtcNow,
                            Version = 1
                        };
                        posting.RequestPayloadJson = request.CanonicalPayloadJson;
                        posting.RequestPayloadHash = request.PayloadHash;
                        posting.ConflictPayloadHash = null;
                        posting.ErrorMessage = null;
                        posting.Status = PayrollAccountingPostingStatuses.Processing;
                        posting.AttemptCount = checked(posting.AttemptCount + 1);
                        posting.Version = checked(posting.Version + 1);
                        await store.SavePostingAsync(posting, token);

                        var journalId = await accounting.PostAsync(request, actorUserId, token);
                        var journal = await journals.GetByIdAsync(businessId, journalId, token);
                        if (journal.Status != JournalEntryStatuses.Posted
                            || journal.ReferenceId != request.PostingReference)
                            throw new InvalidOperationException(
                                "Accounting did not confirm the exact Payroll journal as Posted.");

                        posting.AccountingJournalId = journalId;
                        posting.AccountingReference = journal.JournalNumber ?? journalId.ToString(
                            CultureInfo.InvariantCulture);
                        posting.Status = PayrollAccountingPostingStatuses.Posted;
                        posting.ErrorMessage = null;
                        posting.PostedAtUtc = DateTime.UtcNow;
                        posting.Version = checked(posting.Version + 1);
                        await store.SavePostingAsync(posting, token);
                        await store.TryMarkPeriodPostedAsync(
                            businessId, source.PayrollPeriodId, actorUserId, token);
                        return new PayrollAccountingPostingResponse(Map(posting));
                    }
                    catch (PayrollAccountingPostingException exception)
                    {
                        var failed = await SaveFailureAsync(
                            businessId, actorUserId, source, attempt.Request, exception.Message,
                            PayrollAccountingPostingStatuses.Failed, token);
                        return new PayrollAccountingPostingResponse(Map(failed));
                    }
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            store.DiscardPendingChanges();
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Payroll Accounting posting failed for Business {BusinessId}, run {PayrollRunId}.",
                businessId, runId);
            store.DiscardPendingChanges();
            var status = exception is FrozenJournalConflictException
                ? PayrollAccountingPostingStatuses.Conflict
                : PayrollAccountingPostingStatuses.Failed;
            var source = await store.FindSourceAsync(businessId, runId, cancellationToken)
                ?? throw new KeyNotFoundException("Approved Payroll run not found.");
            var failed = await audit.ExecuteAsync(
                CreateAuditOperation(businessId, runId,
                    status == PayrollAccountingPostingStatuses.Conflict
                        ? "payroll.accounting.conflict"
                        : "payroll.accounting.failed",
                    source, attempt.Request, SafeFailureMessage(exception)),
                async token =>
                {
                    await store.LockPostingIdentityAsync(businessId, runId, token);
                    return await SaveFailureAsync(
                        businessId, actorUserId, source, attempt.Request,
                        SafeFailureMessage(exception), status, token);
                },
                cancellationToken);
            return new PayrollAccountingPostingResponse(Map(failed));
        }
    }

    public async Task<PayrollAccountingPostingResponse> GetAsync(
        long runId, CancellationToken cancellationToken)
    {
        if (runId <= 0)
            throw new ArgumentOutOfRangeException(nameof(runId));
        var businessId = access.RequireBusinessId();
        await EnsurePayrollAccessAsync(businessId, ViewPermission, cancellationToken);
        await access.EnsurePermissionAsync(businessId, SalaryViewPermission, cancellationToken);
        var source = await store.FindSourceAsync(businessId, runId, cancellationToken)
            ?? throw new KeyNotFoundException("Approved Payroll run not found.");
        await access.EnsureScopeAsync(businessId, source.ScopeType, source.ScopeId, cancellationToken);
        var posting = await store.FindPostingAsync(businessId, runId, source.Revision, cancellationToken);
        return new PayrollAccountingPostingResponse(posting is null ? null : Map(posting));
    }

    public async Task<IReadOnlyList<PayrollAccountingMappingReadModel>> GetMappingsAsync(
        CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        await access.EnsurePermissionAsync(businessId, MappingViewPermission, cancellationToken);
        await EnsureCapabilitiesAsync(cancellationToken);
        return await store.GetMappingsAsync(businessId, cancellationToken);
    }

    public async Task<PayrollAccountingMappingReadModel> UpsertMappingAsync(
        string componentType, string componentCode, long accountId, CancellationToken cancellationToken)
    {
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await access.EnsurePermissionAsync(businessId, MappingEditPermission, cancellationToken);
        await EnsureCapabilitiesAsync(cancellationToken);
        var normalizedType = NormalizeComponentType(componentType);
        var normalizedCode = NormalizeComponentCode(normalizedType, componentCode);
        if (accountId <= 0)
            throw new ArgumentOutOfRangeException(nameof(accountId));

        var account = await accounts.GetChartAccountAsync(businessId, accountId, cancellationToken);
        if (account is null || !account.IsActive)
            throw new PayrollAccountingPostingException(
                "The selected Accounting account is not active in this Business.");
        if (normalizedType == PayrollAccountingComponentTypes.Earning
            && account.AccountType is not (ChartAccountTypes.Expense or ChartAccountTypes.CostOfGoodsSold))
            throw new PayrollAccountingPostingException(
                "Earning mappings must use an expense or cost-of-goods-sold account.");
        if (normalizedType == PayrollAccountingComponentTypes.Deduction
            && !IsAllowedDeductionAccountType(account.AccountType))
            throw new PayrollAccountingPostingException(
                "Deduction mappings must use an asset, liability, expense, or cost-of-goods-sold account.");
        if (normalizedType == PayrollAccountingComponentTypes.NetPayable
            && account.AccountType != ChartAccountTypes.Liability)
            throw new PayrollAccountingPostingException(
                "Net salary payable must use a liability account.");
        if (normalizedType == PayrollAccountingComponentTypes.FinalSettlementReceivable
            && account.AccountType != ChartAccountTypes.Asset)
            throw new PayrollAccountingPostingException(
                "Final Settlement receivable must use an asset account.");

        return await audit.ExecuteAsync(
            new RequiredAuditOperation(
                businessId, "payroll.accounting.mapping_saved", "Payroll",
                "Saved a Payroll-to-Accounting account mapping.",
                "PayrollAccountingComponentMapping", $"{normalizedType}:{normalizedCode}"),
            async token =>
            {
                var mapping = await store.FindMappingAsync(
                    businessId, normalizedType, normalizedCode, token);
                if (mapping is null)
                {
                    mapping = new PayrollAccountingComponentMapping
                    {
                        BusinessId = businessId,
                        ComponentType = normalizedType,
                        ComponentCode = normalizedCode,
                        AccountId = accountId,
                        UpdatedByUserId = actorUserId,
                        UpdatedAtUtc = DateTime.UtcNow,
                        Version = 1
                    };
                }
                else
                {
                    mapping.AccountId = accountId;
                    mapping.IsActive = true;
                    mapping.UpdatedByUserId = actorUserId;
                    mapping.UpdatedAtUtc = DateTime.UtcNow;
                    mapping.Version = checked(mapping.Version + 1);
                }

                await store.SaveMappingAsync(mapping, token);
                return (await store.GetMappingsAsync(businessId, token)).Single(row =>
                    row.ComponentType == normalizedType && row.ComponentCode == normalizedCode);
            },
            cancellationToken);
    }

    private async Task<PayrollAccountingPosting> SaveFailureAsync(
        long businessId,
        long actorUserId,
        PayrollAccountingSource source,
        FrozenSystemJournalRequest? request,
        string message,
        string status,
        CancellationToken cancellationToken)
    {
        var posting = await store.FindPostingAsync(
            businessId, source.PayrollRunId, source.Revision, cancellationToken);
        if (posting?.Status == PayrollAccountingPostingStatuses.Posted)
        {
            if (request is null
                || StringComparer.Ordinal.Equals(posting.RequestPayloadHash, request.PayloadHash))
                return posting;
            posting.ConflictPayloadHash = request.PayloadHash;
            posting.ErrorMessage =
                "The immutable posting identity was requested with different canonical content.";
            posting.Version = checked(posting.Version + 1);
            await store.SavePostingAsync(posting, cancellationToken);
            return posting;
        }

        posting ??= CreatePosting(
            businessId, actorUserId, source, request);
        if (request is not null)
        {
            if (posting.RequestPayloadHash is not null
                && !StringComparer.Ordinal.Equals(posting.RequestPayloadHash, request.PayloadHash))
            {
                posting.Status = PayrollAccountingPostingStatuses.Conflict;
                posting.ConflictPayloadHash = request.PayloadHash;
                posting.ErrorMessage =
                    "The immutable posting identity was requested with different canonical content.";
            }
            else
            {
                posting.RequestPayloadJson = request.CanonicalPayloadJson;
                posting.RequestPayloadHash = request.PayloadHash;
                posting.Status = status;
                posting.ErrorMessage = message;
                posting.AttemptCount = checked(posting.AttemptCount + 1);
            }
        }
        else
        {
            posting.Status = status;
            posting.ErrorMessage = message;
            posting.AttemptCount = checked(posting.AttemptCount + 1);
        }
        posting.Version = checked(posting.Version + 1);
        await store.SavePostingAsync(posting, cancellationToken);
        return posting;
    }

    private async Task<PayrollAccountingPostingResponse> PostCorrectionAsync(
        long businessId,
        long actorUserId,
        PayrollAccountingSource initialSource,
        CancellationToken cancellationToken)
    {
        var operation = new RequiredAuditOperation(
            businessId, "payroll.accounting.correction_requested", "Payroll",
            "Processed Payroll correction Accounting using the resolved correction strategy.",
            "PayrollRun", initialSource.PayrollRunId.ToString(CultureInfo.InvariantCulture));
        try
        {
            if (string.IsNullOrWhiteSpace(initialSource.CorrectionReference))
                throw new PayrollAccountingPostingException(
                    "Correction Accounting requires a persisted CorrectionReference.");
            var policy = await correctionPolicy.ResolveAsync(
                businessId, initialSource.CorrectionReference, cancellationToken);
            if (policy.Strategy != PayrollAccountingCorrectionStrategy.ReversalAndReplacement
                || string.IsNullOrWhiteSpace(policy.Version))
            {
                var message = policy.Strategy == PayrollAccountingCorrectionStrategy.None
                    ? "No approved ADR-06 Accounting correction strategy is configured; correction posting is blocked."
                    : "The configured ADR-06 Accounting correction strategy is not supported by this workflow.";
                return await audit.ExecuteAsync(
                    new RequiredAuditOperation(
                        businessId, "payroll.accounting.correction_unconfigured", "Payroll",
                        "Rejected Payroll correction posting because no supported correction strategy is resolved.",
                        "PayrollRun", initialSource.PayrollRunId.ToString(CultureInfo.InvariantCulture)),
                    _ => Task.FromResult(new PayrollAccountingPostingResponse(null, message)),
                    cancellationToken);
            }

            var initialReplacement = await PrepareCorrectionRequestsAsync(
                businessId, initialSource, cancellationToken);
            return await audit.ExecuteAsync(
                operation,
                async token =>
                {
                    await store.LockPostingIdentityAsync(
                        businessId, initialSource.PayrollRunId, token);
                    var source = await store.FindSourceAsync(
                        businessId, initialSource.PayrollRunId, token)
                        ?? throw new KeyNotFoundException("Approved Payroll correction run not found.");
                    await access.EnsureScopeAsync(businessId, source.ScopeType, source.ScopeId, token);
                    if (await store.IsAccountingEnabledAsync(businessId, token) != true)
                        return new PayrollAccountingPostingResponse(
                            null, "Accounting is not explicitly enabled for this Business; no journal was created.");
                    ValidateSource(source);
                    if (source.RunType != PayrollRunTypes.Correction
                        || source.CorrectionReference != initialSource.CorrectionReference
                        || source.OriginalRunId != initialSource.OriginalRunId)
                        throw new PayrollAccountingPostingConflictException(
                            "Payroll correction identity changed while Accounting was being prepared.");

                    var requests = await PrepareCorrectionRequestsAsync(businessId, source, token);
                    if (!StringComparer.Ordinal.Equals(
                            requests.Replacement.PayloadHash, initialReplacement.Replacement.PayloadHash)
                        || !StringComparer.Ordinal.Equals(
                            requests.Reversal.PayloadHash, initialReplacement.Reversal.PayloadHash))
                        throw new PayrollAccountingPostingConflictException(
                            "Payroll correction evidence changed while Accounting was being prepared.");

                    var correctionPosting = await store.FindCorrectionPostingAsync(
                        businessId, source.PayrollRunId, source.Revision, token);
                    if (correctionPosting is not null
                        && (correctionPosting.CorrectionReference != source.CorrectionReference
                            || correctionPosting.OriginalPostingId != requests.OriginalPosting.Id
                            || correctionPosting.CorrectionPolicyVersion != policy.Version
                            || correctionPosting.RequestPayloadHash != requests.Replacement.PayloadHash))
                    {
                        correctionPosting.Status = PayrollAccountingPostingStatuses.Conflict;
                        correctionPosting.ConflictPayloadHash = requests.Replacement.PayloadHash;
                        correctionPosting.ErrorMessage =
                            "The immutable Payroll correction identity was requested with different frozen content.";
                        correctionPosting.Version = checked(correctionPosting.Version + 1);
                        await store.SavePostingAsync(correctionPosting, token);
                        throw new PayrollAccountingPostingConflictException(
                            "The immutable Payroll correction identity was requested with different frozen content.");
                    }

                    if (correctionPosting is null)
                    {
                        var competing = await store.FindCorrectionByOriginalAsync(
                            businessId, requests.OriginalPosting.Id, token);
                        if (competing is not null)
                            throw new PayrollAccountingPostingConflictException(
                                "The original Payroll posting already has a different correction identity.");

                        correctionPosting = new PayrollAccountingPosting
                        {
                            BusinessId = businessId,
                            PayrollPeriodId = source.PayrollPeriodId,
                            PayrollRunId = source.PayrollRunId,
                            RunRevision = source.Revision,
                            PostingType = PayrollAccountingPostingTypes.PayrollCorrection,
                            CorrectionReference = source.CorrectionReference,
                            OriginalPostingId = requests.OriginalPosting.Id,
                            CorrectionPolicyVersion = policy.Version,
                            PostingReference = requests.Replacement.PostingReference,
                            IdempotencyKey = requests.Replacement.PostingReference,
                            RequestPayloadJson = requests.Replacement.CanonicalPayloadJson,
                            RequestPayloadHash = requests.Replacement.PayloadHash,
                            Status = PayrollAccountingPostingStatuses.Processing,
                            RequestedByUserId = actorUserId,
                            RequestedAtUtc = DateTime.UtcNow,
                            Version = 1
                        };
                        await store.SavePostingAsync(correctionPosting, token);
                    }

                    var reversalJournalId = await accounting.ResolveExistingAsync(
                        requests.Reversal, token);
                    if (!reversalJournalId.HasValue)
                        reversalJournalId = await accounting.ReverseAsync(
                            requests.Reversal, actorUserId, token);
                    var reversalJournal = await journals.GetByIdAsync(
                        businessId, reversalJournalId.Value, token);
                    if (reversalJournal.Status != JournalEntryStatuses.Posted
                        || !reversalJournal.IsSystemGenerated
                        || reversalJournal.ReversalOfJournalEntryId != requests.OriginalPosting.AccountingJournalId)
                        throw new FrozenJournalConflictException(
                            "Accounting correction did not resolve to the exact linked reversal journal.");
                    correctionPosting.ReversalJournalId = reversalJournalId.Value;
                    correctionPosting.Status = PayrollAccountingPostingStatuses.Processing;
                    correctionPosting.ErrorMessage = null;
                    correctionPosting.Version = checked(correctionPosting.Version + 1);
                    await store.SavePostingAsync(correctionPosting, token);

                    var replacementJournalId = await accounting.ResolveExistingAsync(
                        requests.Replacement, token);
                    if (!replacementJournalId.HasValue)
                        replacementJournalId = await accounting.PostAsync(
                            requests.Replacement, actorUserId, token);
                    var replacementJournal = await journals.GetByIdAsync(
                        businessId, replacementJournalId.Value, token);
                    if (replacementJournal.Status != JournalEntryStatuses.Posted
                        || !replacementJournal.IsSystemGenerated
                        || replacementJournal.ReferenceId != requests.Replacement.PostingReference)
                        throw new FrozenJournalConflictException(
                            "Accounting correction did not resolve to the exact replacement journal.");
                    correctionPosting.AccountingJournalId = replacementJournalId.Value;
                    correctionPosting.AccountingReference = replacementJournal.JournalNumber
                        ?? replacementJournalId.Value.ToString(CultureInfo.InvariantCulture);
                    correctionPosting.Status = PayrollAccountingPostingStatuses.Posted;
                    correctionPosting.PostedAtUtc = replacementJournal.PostedAt;
                    correctionPosting.ErrorMessage = null;
                    correctionPosting.AttemptCount = checked(correctionPosting.AttemptCount + 1);
                    correctionPosting.Version = checked(correctionPosting.Version + 1);
                    await store.SavePostingAsync(correctionPosting, token);
                    return new PayrollAccountingPostingResponse(Map(correctionPosting));
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
            and not UnauthorizedAccessException)
        {
            logger.LogError(exception,
                "Payroll correction Accounting failed for Business {BusinessId}, run {PayrollRunId}.",
                businessId, initialSource.PayrollRunId);
            store.DiscardPendingChanges();
            var status = exception is FrozenJournalConflictException
                or PayrollAccountingPostingConflictException
                ? PayrollAccountingPostingStatuses.Conflict
                : PayrollAccountingPostingStatuses.Failed;
            return await audit.ExecuteAsync(
                new RequiredAuditOperation(
                    businessId,
                    status == PayrollAccountingPostingStatuses.Conflict
                        ? "payroll.accounting.correction_conflict"
                        : "payroll.accounting.correction_failed",
                    "Payroll",
                    "Payroll correction Accounting could not be completed.",
                    "PayrollRun",
                    initialSource.PayrollRunId.ToString(CultureInfo.InvariantCulture)),
                async token =>
                {
                    await store.LockPostingIdentityAsync(
                        businessId, initialSource.PayrollRunId, token);
                    var correctionPosting = await store.FindCorrectionPostingAsync(
                        businessId, initialSource.PayrollRunId, initialSource.Revision, token);
                    if (correctionPosting is null)
                        return new PayrollAccountingPostingResponse(
                            null, SafeFailureMessage(exception));
                    if (correctionPosting.Status != PayrollAccountingPostingStatuses.Posted)
                    {
                        correctionPosting.Status = status;
                        correctionPosting.ErrorMessage = SafeFailureMessage(exception);
                        correctionPosting.AttemptCount = checked(correctionPosting.AttemptCount + 1);
                        correctionPosting.Version = checked(correctionPosting.Version + 1);
                        await store.SavePostingAsync(correctionPosting, token);
                    }
                    return new PayrollAccountingPostingResponse(Map(correctionPosting));
                },
                cancellationToken);
        }
    }

    private async Task<CorrectionRequests> PrepareCorrectionRequestsAsync(
        long businessId,
        PayrollAccountingSource correction,
        CancellationToken cancellationToken)
    {
        ValidateSource(correction);
        if (correction.RunType != PayrollRunTypes.Correction
            || correction.OriginalRunId is not > 0
            || string.IsNullOrWhiteSpace(correction.CorrectionReference))
            throw new PayrollAccountingPostingException(
                "Correction Accounting requires an approved correction run with stable original-run lineage.");

        var original = await store.FindSourceAsync(
            businessId, correction.OriginalRunId.Value, cancellationToken)
            ?? throw new PayrollAccountingPostingException(
                "The original approved Payroll run for this correction was not found.");
        await access.EnsureScopeAsync(businessId, original.ScopeType, original.ScopeId, cancellationToken);
        if (original.RunType == PayrollRunTypes.Correction
            || original.ScopeType != correction.ScopeType
            || original.ScopeId != correction.ScopeId)
            throw new PayrollAccountingPostingException(
                "Payroll corrections must target the original run in the same Business and scope.");

        var originalPosting = await store.FindPostingAsync(
            businessId, original.PayrollRunId, original.Revision, cancellationToken)
            ?? throw new PayrollAccountingPostingException(
                "The original Payroll run has no Accounting expense posting.");
        if (originalPosting.Status != PayrollAccountingPostingStatuses.Posted
            || originalPosting.AccountingJournalId is not > 0
            || string.IsNullOrWhiteSpace(originalPosting.RequestPayloadHash))
            throw new PayrollAccountingPostingException(
                "The original Payroll Accounting expense posting is not fully resolved.");
        if (original.Revision <= 0
            || string.IsNullOrWhiteSpace(original.CalculationInputHash)
            || string.IsNullOrWhiteSpace(original.ApprovalEvidenceHash)
            || original.CurrencyCode is null || original.CurrencyCode.Length != 3
            || original.DecimalPrecision is < 0 or > 6
            || original.Employees.Count == 0)
            throw new PayrollAccountingPostingException(
                "The original Payroll run no longer has complete frozen Accounting evidence.");

        var mappings = await store.GetMappingRowsAsync(businessId, cancellationToken);
        var baseReplacement = await BuildRequestAsync(
            correction, mappings, businessId, cancellationToken);
        var replacementReference = CorrectionPostingReference(
            businessId, correction.PayrollRunId, originalPosting.Id);
        var replacementInput = baseReplacement.ReadFrozenPayload<ManualJournalDraftInput>() with
        {
            ReferenceType = "PayrollCorrectionReplacement",
            ReferenceId = replacementReference,
            ReferenceNumber = correction.CorrectionReference,
            Description = $"Payroll correction replacement {correction.CorrectionReference}"
        };
        var replacement = FrozenSystemJournalRequest.ForPosting(
            businessId, replacementReference, replacementInput);
        var date = new DateTime(
            correction.PeriodEnd.Year, correction.PeriodEnd.Month, correction.PeriodEnd.Day,
            0, 0, 0, DateTimeKind.Utc);
        var reversal = FrozenSystemJournalRequest.ForReversal(
            businessId,
            CorrectionReversalReference(
                businessId, correction.PayrollRunId, originalPosting.Id),
            originalPosting.AccountingJournalId.Value,
            new ManualJournalReversalInput(
                $"Payroll correction {correction.CorrectionReference}", date, date));
        return new CorrectionRequests(originalPosting, reversal, replacement);
    }

    private static PayrollAccountingPosting CreatePosting(
        long businessId,
        long actorUserId,
        PayrollAccountingSource source,
        FrozenSystemJournalRequest? request)
    {
        var reference = request?.PostingReference ?? PostingReference(source);
        return new PayrollAccountingPosting
        {
            BusinessId = businessId,
            PayrollPeriodId = source.PayrollPeriodId,
            PayrollRunId = source.PayrollRunId,
            RunRevision = source.Revision,
            PostingType = PayrollAccountingPostingTypes.PayrollExpense,
            PostingReference = reference,
            IdempotencyKey = reference,
            RequestedByUserId = actorUserId,
            RequestedAtUtc = DateTime.UtcNow,
            Version = 1
        };
    }

    private async Task<FrozenSystemJournalRequest> BuildRequestAsync(
        PayrollAccountingSource source,
        IReadOnlyList<PayrollAccountingComponentMapping> mappings,
        long businessId,
        CancellationToken cancellationToken)
    {
        var mappingByKey = mappings.Where(row => row.IsActive)
            .ToDictionary(row => (row.ComponentType, row.ComponentCode.ToUpperInvariant()));
        var accountIds = mappings.Select(row => row.AccountId).Distinct().ToArray();
        var loadedAccounts = await accounts.GetChartAccountsForBusinessAsync(
            businessId, accountIds, cancellationToken);
        var validAccounts = loadedAccounts
            .Where(row => row.IsActive)
            .ToDictionary(row => row.Id);
        var journalLines = new List<ManualJournalDraftLineInput>();

        foreach (var employee in source.Employees)
        {
            var costCenter = employee.HistoricalLocationId is > 0
                ? $"HR_LOCATION:{employee.HistoricalLocationId.Value.ToString(CultureInfo.InvariantCulture)}"
                : null;
            foreach (var line in employee.Lines)
            {
                if (line.Amount == 0m)
                    continue;
                if (!mappingByKey.TryGetValue((line.LineType, line.ComponentCode.ToUpperInvariant()),
                        out var mapping))
                    throw new PayrollAccountingPostingException(
                        $"No active Accounting mapping exists for Payroll {line.LineType} component '{line.ComponentCode}'.");
                if (!validAccounts.TryGetValue(mapping.AccountId, out var account))
                    throw new PayrollAccountingPostingException(
                        $"Accounting mapping for Payroll component '{line.ComponentCode}' references an inactive or cross-Business account.");
                if (line.LineType == PayrollAccountingComponentTypes.Earning
                    && account.AccountType is not (ChartAccountTypes.Expense or ChartAccountTypes.CostOfGoodsSold))
                    throw new PayrollAccountingPostingException(
                        $"Payroll earning component '{line.ComponentCode}' is not mapped to an expense account.");
                if (line.LineType == PayrollAccountingComponentTypes.Deduction
                    && !IsAllowedDeductionAccountType(account.AccountType))
                    throw new PayrollAccountingPostingException(
                        $"Payroll deduction component '{line.ComponentCode}' must be mapped to an asset, liability, expense, or cost-of-goods-sold account.");

                journalLines.Add(new ManualJournalDraftLineInput(
                    mapping.AccountId,
                    $"Payroll {line.LineType}: {line.ComponentCode}",
                    line.LineType == PayrollAccountingComponentTypes.Earning ? line.Amount : 0m,
                    line.LineType == PayrollAccountingComponentTypes.Deduction ? line.Amount : 0m,
                    null, null, null, null, null, costCenter));
            }

            if (employee.NetPay > 0m)
            {
                if (!mappingByKey.TryGetValue(
                        (PayrollAccountingComponentTypes.NetPayable, "NET_PAYABLE"), out var payableMapping)
                    || !validAccounts.TryGetValue(payableMapping.AccountId, out var payableAccount)
                    || payableAccount.AccountType != ChartAccountTypes.Liability)
                    throw new PayrollAccountingPostingException(
                        "An active Business-scoped liability mapping for NET_PAYABLE is required.");
                journalLines.Add(new ManualJournalDraftLineInput(
                    payableMapping.AccountId, "Payroll employee salary payable", 0m, employee.NetPay,
                    null, null, null, null, null, costCenter));
            }
        }

        var debits = journalLines.Sum(line => line.DebitAmount);
        var credits = journalLines.Sum(line => line.CreditAmount);
        if (journalLines.Count == 0 || debits <= 0m || debits != credits)
            throw new PayrollAccountingPostingException(
                "Payroll expense instruction does not balance using the persisted Payroll amounts.");

        var postingReference = PostingReference(source);
        var date = new DateTime(
            source.PeriodEnd.Year, source.PeriodEnd.Month, source.PeriodEnd.Day, 0, 0, 0, DateTimeKind.Utc);
        var input = new ManualJournalDraftInput(
            AccountingJournalTypes.SystemJournal,
            date,
            date,
            date,
            "PayrollExpense",
            postingReference,
            source.CorrectionReference ?? source.RunReference
                ?? $"Run-{source.RunNo.ToString(CultureInfo.InvariantCulture)}",
            $"Payroll expense accrual run {source.RunNo.ToString(CultureInfo.InvariantCulture)} " +
                $"revision {source.Revision.ToString(CultureInfo.InvariantCulture)}",
            null, null, null, null,
            journalLines
                .OrderBy(line => line.CostCenterId, StringComparer.Ordinal)
                .ThenBy(line => line.AccountId)
                .ThenBy(line => line.Description, StringComparer.Ordinal)
                .ToArray(),
            source.CurrencyCode,
            source.Revision,
            SourceEvidenceHash(source));
        return FrozenSystemJournalRequest.ForPosting(businessId, postingReference, input);
    }

    private static void ValidateSource(PayrollAccountingSource source)
    {
        if (source.RunStatus is not (PayrollRunStatuses.Approved or PayrollRunStatuses.PaymentInProgress
                or PayrollRunStatuses.Paid or PayrollRunStatuses.Completed)
            || source.PeriodStatus is not (PayrollPeriodStatuses.Approved or PayrollPeriodStatuses.Posted)
            || source.Revision <= 0
            || string.IsNullOrWhiteSpace(source.CalculationInputHash)
            || string.IsNullOrWhiteSpace(source.ApprovalEvidenceHash)
            || source.CurrencyCode is null || source.CurrencyCode.Length != 3
            || source.DecimalPrecision is < 0 or > 6
            || source.Employees.Count == 0)
            throw new PayrollAccountingPostingException(
                "Payroll expense posting requires a protected, approved run with complete frozen results.");
    }

    private async Task EnsurePayrollAccessAsync(
        long businessId, string permission, CancellationToken cancellationToken)
    {
        await access.EnsurePermissionAsync(businessId, permission, cancellationToken);
        await access.EnsurePermissionAsync(businessId, SalaryViewPermission, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Payroll, cancellationToken);
    }

    private async Task EnsureCapabilitiesAsync(CancellationToken cancellationToken)
    {
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Payroll, cancellationToken);
    }

    private static string NormalizeComponentType(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized is not (PayrollAccountingComponentTypes.Earning
                or PayrollAccountingComponentTypes.Deduction
                or PayrollAccountingComponentTypes.NetPayable
                or PayrollAccountingComponentTypes.FinalSettlementReceivable))
            throw new ArgumentException("Unsupported Payroll Accounting component mapping type.", nameof(value));
        return normalized;
    }

    private static bool IsAllowedDeductionAccountType(string accountType) =>
        accountType is ChartAccountTypes.Asset or ChartAccountTypes.Liability
            or ChartAccountTypes.Expense or ChartAccountTypes.CostOfGoodsSold;

    private static string NormalizeComponentCode(string componentType, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToUpperInvariant();
        if (componentType is PayrollAccountingComponentTypes.NetPayable
            or PayrollAccountingComponentTypes.FinalSettlementReceivable)
        {
            var expected = componentType == PayrollAccountingComponentTypes.NetPayable
                ? "NET_PAYABLE"
                : "FINAL_SETTLEMENT_RECEIVABLE";
            if (normalized != expected)
                throw new ArgumentException(
                    $"{componentType} mapping must use {expected}.", nameof(value));
            return normalized;
        }
        if (normalized.Length > 50)
            throw new ArgumentOutOfRangeException(nameof(value), "Payroll component code is too long.");
        return normalized;
    }

    private static string PostingReference(PayrollAccountingSource source) =>
        $"payroll-expense:{source.BusinessId.ToString(CultureInfo.InvariantCulture)}:" +
        $"{source.PayrollRunId.ToString(CultureInfo.InvariantCulture)}:r" +
        source.Revision.ToString(CultureInfo.InvariantCulture);

    private static string CorrectionPostingReference(
        long businessId, long runId, long originalPostingId) =>
        $"paycorr:{businessId.ToString(CultureInfo.InvariantCulture)}:" +
        $"{runId.ToString(CultureInfo.InvariantCulture)}:" +
        $"{originalPostingId.ToString(CultureInfo.InvariantCulture)}:replacement";

    private static string CorrectionReversalReference(
        long businessId, long runId, long originalPostingId) =>
        $"paycorr:{businessId.ToString(CultureInfo.InvariantCulture)}:" +
        $"{runId.ToString(CultureInfo.InvariantCulture)}:" +
        $"{originalPostingId.ToString(CultureInfo.InvariantCulture)}:reversal";

    private static string SourceEvidenceHash(PayrollAccountingSource source)
    {
        var canonical = System.Text.Json.JsonSerializer.Serialize(new
        {
            source.BusinessId,
            source.PayrollPeriodId,
            source.PayrollRunId,
            source.Revision,
            source.CalculationInputHash,
            source.ApprovalEvidenceHash,
            source.CurrencyCode,
            source.DecimalPrecision,
            Results = source.Employees.OrderBy(row => row.EmployeeResultId).Select(row => new
            {
                row.EmployeeResultId,
                row.SnapshotHash
            })
        });
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(canonical)));
    }

    private static RequiredAuditOperation CreateAuditOperation(
        long businessId,
        long runId,
        string action,
        PayrollAccountingSource source,
        FrozenSystemJournalRequest? request,
        string? validationMessage) =>
        new(businessId, action, "Payroll", "Payroll expense posting request.",
            "PayrollRun", runId.ToString(CultureInfo.InvariantCulture),
            [
                new AuditEventEntityWriteRequest(
                    "PayrollAccountingPosting", PostingReference(source), "Requested",
                    JsonSerializer.Serialize(new
                    {
                        source.BusinessId,
                        source.PayrollRunId,
                        source.Revision,
                        PostingType = PayrollAccountingPostingTypes.PayrollExpense,
                        PostingReference = request?.PostingReference ?? PostingReference(source),
                        PayloadHash = request?.PayloadHash,
                        ValidationMessage = validationMessage
                    }))
            ]);

    private static PayrollAccountingPostingReadModel Map(PayrollAccountingPosting row) =>
        new(row.Id, row.BusinessId, row.PayrollPeriodId, row.PayrollRunId, row.RunRevision,
            row.PostingType, row.PostingReference, row.IdempotencyKey, row.RequestPayloadHash,
            row.ConflictPayloadHash, row.AccountingJournalId, row.AccountingReference,
            row.Status, row.ErrorMessage, row.AttemptCount, row.Version, row.PostedAtUtc);

    private static string SafeFailureMessage(Exception exception) => exception switch
    {
        PayrollAccountingPostingException or PayrollAccountingPostingConflictException
            or FrozenJournalConflictException =>
            exception.Message.Length <= 1000 ? exception.Message : exception.Message[..1000],
        _ => "Accounting posting failed or could not be confirmed. Refresh the posting status before retrying."
    };

    private sealed record PostingAttempt(
        PayrollAccountingSource Source,
        FrozenSystemJournalRequest? Request);

    private sealed record CorrectionRequests(
        PayrollAccountingPosting OriginalPosting,
        FrozenSystemJournalRequest Reversal,
        FrozenSystemJournalRequest Replacement);
}

public sealed class PostPayrollAccountingExpenseCommandHandler(
    PayrollAccountingPostingService service)
    : IRequestHandler<PostPayrollAccountingExpenseCommand, PayrollAccountingPostingResponse>
{
    public Task<PayrollAccountingPostingResponse> Handle(
        PostPayrollAccountingExpenseCommand request, CancellationToken cancellationToken) =>
        service.PostAsync(request.RunId, cancellationToken);
}

public sealed class GetPayrollAccountingPostingQueryHandler(
    PayrollAccountingPostingService service)
    : IRequestHandler<GetPayrollAccountingPostingQuery, PayrollAccountingPostingResponse>
{
    public Task<PayrollAccountingPostingResponse> Handle(
        GetPayrollAccountingPostingQuery request, CancellationToken cancellationToken) =>
        service.GetAsync(request.RunId, cancellationToken);
}

public sealed class GetPayrollAccountingMappingsQueryHandler(
    PayrollAccountingPostingService service)
    : IRequestHandler<GetPayrollAccountingMappingsQuery, IReadOnlyList<PayrollAccountingMappingReadModel>>
{
    public Task<IReadOnlyList<PayrollAccountingMappingReadModel>> Handle(
        GetPayrollAccountingMappingsQuery request, CancellationToken cancellationToken) =>
        service.GetMappingsAsync(cancellationToken);
}

public sealed class UpsertPayrollAccountingMappingCommandHandler(
    PayrollAccountingPostingService service)
    : IRequestHandler<UpsertPayrollAccountingMappingCommand, PayrollAccountingMappingReadModel>
{
    public Task<PayrollAccountingMappingReadModel> Handle(
        UpsertPayrollAccountingMappingCommand request, CancellationToken cancellationToken) =>
        service.UpsertMappingAsync(
            request.ComponentType, request.ComponentCode, request.AccountId, cancellationToken);
}
