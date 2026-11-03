using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

public sealed class PayrollAccountingSettlementPostingService(
    IPayrollRunAccess access,
    IHrPayrollCapabilityService capabilities,
    IPayrollAccountingPostingStore store,
    IAccountingPostingAccountReadStore accounts,
    IFrozenSystemJournalStore accounting,
    IJournalEntryReadStore journals,
    IRequiredAuditTransaction audit,
    ILogger<PayrollAccountingSettlementPostingService> logger)
{
    public async Task<PayrollAccountingPostingResponse> PostAsync(
        long paymentId, long allocationId, long evidenceId, CancellationToken cancellationToken)
    {
        if (paymentId <= 0 || allocationId <= 0 || evidenceId <= 0)
            throw new ArgumentOutOfRangeException(nameof(paymentId));

        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        await EnsurePayrollAccessAsync(businessId, "payroll.accounting.post", cancellationToken);
        var initialSource = await store.FindSettlementSourceAsync(
            businessId, paymentId, allocationId, evidenceId, cancellationToken)
            ?? throw new KeyNotFoundException("Verified Payroll settlement evidence was not found.");
        await access.EnsureScopeAsync(
            businessId, initialSource.ScopeType, initialSource.ScopeId, cancellationToken);

        FrozenSystemJournalRequest? expectedRequest = null;
        string? preflightFailure = null;
        try
        {
            ValidateSource(initialSource);
            var mappings = await store.GetSettlementAccountMappingsAsync(businessId, cancellationToken);
            expectedRequest = await BuildRequestAsync(initialSource, mappings, businessId, cancellationToken);
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
                CreateAuditOperation(businessId, initialSource, expectedRequest,
                    accountingEnabled == true ? preflightFailure : null),
                async token =>
                {
                    await store.LockSettlementPostingIdentityAsync(
                        businessId, evidenceId, allocationId, token);
                    var source = await store.FindSettlementSourceAsync(
                        businessId, paymentId, allocationId, evidenceId, token)
                        ?? throw new KeyNotFoundException(
                            "Verified Payroll settlement evidence was not found.");
                    await access.EnsureScopeAsync(
                        businessId, source.ScopeType, source.ScopeId, token);

                    if (await store.IsAccountingEnabledAsync(businessId, token) != true)
                        return new PayrollAccountingPostingResponse(
                            null, "Accounting is not explicitly enabled for this Business; no journal was created.");

                    var existing = await store.FindSettlementPostingAsync(
                        businessId, evidenceId, allocationId, token);
                    if (existing is not null)
                    {
                        if (!HasExpectedIdentity(existing, source))
                            throw new PayrollAccountingPostingConflictException(
                                "The settlement identity is already bound to different Payroll evidence.");
                        if (existing.Status is PayrollAccountingPostingStatuses.Posted
                            or PayrollAccountingPostingStatuses.Conflict)
                            return new PayrollAccountingPostingResponse(Map(existing));
                    }

                    try
                    {
                        ValidateSource(source);
                        var mappings = await store.GetSettlementAccountMappingsAsync(businessId, token);
                        var request = await BuildRequestAsync(source, mappings, businessId, token);
                        attempt = new PostingAttempt(source, request);
                        if (expectedRequest is not null
                            && !StringComparer.Ordinal.Equals(expectedRequest.PayloadHash, request.PayloadHash))
                        {
                            var changed = existing ?? CreatePosting(
                                businessId, actorUserId, source, expectedRequest);
                            changed.ConflictPayloadHash = request.PayloadHash;
                            changed.RequestPayloadJson ??= expectedRequest.CanonicalPayloadJson;
                            changed.RequestPayloadHash ??= expectedRequest.PayloadHash;
                            changed.ErrorMessage =
                                "Settlement evidence or account mapping changed while the request was being prepared.";
                            if (changed.Status != PayrollAccountingPostingStatuses.Posted)
                                changed.Status = PayrollAccountingPostingStatuses.Conflict;
                            changed.Version = checked(changed.Version + 1);
                            await store.SavePostingAsync(changed, token);
                            return new PayrollAccountingPostingResponse(
                                Map(changed), "Settlement posting content changed before it could be posted.");
                        }

                        if (existing is not null && existing.RequestPayloadHash is not null
                            && !StringComparer.Ordinal.Equals(
                                existing.RequestPayloadHash, request.PayloadHash))
                        {
                            existing.ConflictPayloadHash = request.PayloadHash;
                            existing.ErrorMessage =
                                "The immutable settlement identity was requested with different canonical content.";
                            existing.Status = PayrollAccountingPostingStatuses.Conflict;
                            existing.Version = checked(existing.Version + 1);
                            await store.SavePostingAsync(existing, token);
                            return new PayrollAccountingPostingResponse(
                                Map(existing), "This settlement identity is bound to different frozen content.");
                        }

                        var recoveredJournalId = await accounting.ResolveExistingAsync(request, token);
                        if (recoveredJournalId.HasValue)
                        {
                            var recoveredJournal = await journals.GetByIdAsync(
                                businessId, recoveredJournalId.Value, token);
                            if (recoveredJournal.Status != JournalEntryStatuses.Posted
                                || recoveredJournal.ReferenceId != request.PostingReference
                                || !recoveredJournal.IsSystemGenerated)
                                throw new FrozenJournalConflictException(
                                    "Accounting found the settlement identity, but its journal is not the exact active Posted journal.");

                            var recovered = existing ?? CreatePosting(
                                businessId, actorUserId, source, request);
                            recovered.RequestPayloadJson = request.CanonicalPayloadJson;
                            recovered.RequestPayloadHash = request.PayloadHash;
                            recovered.AccountingJournalId = recoveredJournalId.Value;
                            recovered.AccountingReference = recoveredJournal.JournalNumber
                                ?? recoveredJournalId.Value.ToString(CultureInfo.InvariantCulture);
                            recovered.PayrollPaymentReconciliationId = source.ReconciliationId;
                            recovered.Status = PayrollAccountingPostingStatuses.Posted;
                            recovered.ErrorMessage = null;
                            recovered.PostedAtUtc ??= recoveredJournal.PostedAt;
                            recovered.AttemptCount = checked(recovered.AttemptCount + 1);
                            recovered.Version = checked(recovered.Version + 1);
                            await store.SavePostingAsync(recovered, token);
                            logger.LogInformation(
                                "Recovered Payroll settlement Accounting posting for Business {BusinessId}, payment {PaymentId}, allocation {AllocationId}, evidence {EvidenceId}, reference {PostingReference}, journal {JournalId}.",
                                businessId, paymentId, allocationId, evidenceId,
                                request.PostingReference, recoveredJournalId.Value);
                            await store.TryMarkPeriodPostedAsync(
                                businessId, source.PayrollPeriodId, actorUserId, token);
                            return new PayrollAccountingPostingResponse(Map(recovered));
                        }

                        var posting = existing ?? CreatePosting(
                            businessId, actorUserId, source, request);
                        posting.RequestPayloadJson = request.CanonicalPayloadJson;
                        posting.RequestPayloadHash = request.PayloadHash;
                        posting.ConflictPayloadHash = null;
                        posting.ErrorMessage = null;
                        posting.PayrollPaymentReconciliationId = source.ReconciliationId;
                        posting.Status = PayrollAccountingPostingStatuses.Processing;
                        posting.AttemptCount = checked(posting.AttemptCount + 1);
                        posting.Version = checked(posting.Version + 1);
                        await store.SavePostingAsync(posting, token);

                        var journalId = await accounting.PostAsync(request, actorUserId, token);
                        var journal = await journals.GetByIdAsync(businessId, journalId, token);
                        if (journal.Status != JournalEntryStatuses.Posted
                            || journal.ReferenceId != request.PostingReference)
                            throw new InvalidOperationException(
                                "Accounting did not confirm the exact Payroll settlement journal as Posted.");

                        posting.AccountingJournalId = journalId;
                        posting.AccountingReference = journal.JournalNumber
                            ?? journalId.ToString(CultureInfo.InvariantCulture);
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
                "Payroll settlement Accounting posting failed for Business {BusinessId}, payment {PaymentId}, allocation {AllocationId}, evidence {EvidenceId}.",
                businessId, paymentId, allocationId, evidenceId);
            store.DiscardPendingChanges();
            var status = exception is FrozenJournalConflictException
                or PayrollAccountingPostingConflictException
                    ? PayrollAccountingPostingStatuses.Conflict
                    : PayrollAccountingPostingStatuses.Failed;
            var source = await store.FindSettlementSourceAsync(
                businessId, paymentId, allocationId, evidenceId, cancellationToken)
                ?? throw new KeyNotFoundException("Verified Payroll settlement evidence was not found.");
            var failed = await audit.ExecuteAsync(
                CreateAuditOperation(businessId, source, attempt.Request, SafeFailureMessage(exception)),
                async token =>
                {
                    await store.LockSettlementPostingIdentityAsync(
                        businessId, evidenceId, allocationId, token);
                    return await SaveFailureAsync(
                        businessId, actorUserId, source, attempt.Request,
                        SafeFailureMessage(exception), status, token);
                },
                cancellationToken);
            return new PayrollAccountingPostingResponse(Map(failed));
        }
    }

    private async Task<FrozenSystemJournalRequest> BuildRequestAsync(
        PayrollAccountingSettlementSource source,
        PayrollAccountingSettlementAccountMappings? mappings,
        long businessId,
        CancellationToken cancellationToken)
    {
        if (mappings is null || mappings.NetPayableMapping is null)
            throw new PayrollAccountingPostingException(
                "Business Accounting configuration and an active NET_PAYABLE mapping are required.");

        var settlementAccountId = source.AllocationMethod switch
        {
            PayrollPaymentMethods.Cash => mappings.CashAccountId,
            PayrollPaymentMethods.Bank => mappings.DefaultBankAccountId,
            PayrollPaymentMethods.Mobile => mappings.MobileMfsClearingAccountId,
            _ => null
        };
        if (settlementAccountId is null)
            throw new PayrollAccountingPostingException(
                $"No Business-scoped Accounting settlement account is configured for {source.AllocationMethod}.");

        var ids = new[] { settlementAccountId.Value, mappings.NetPayableMapping.AccountId };
        var loaded = await accounts.GetChartAccountsForBusinessAsync(businessId, ids, cancellationToken);
        var valid = loaded.Where(row => row.IsActive).ToDictionary(row => row.Id);
        if (!valid.TryGetValue(settlementAccountId.Value, out var settlementAccount)
            || settlementAccount.AccountType != ChartAccountTypes.Asset)
            throw new PayrollAccountingPostingException(
                $"The configured {source.AllocationMethod} settlement account is missing, inactive, cross-Business, or not an asset account.");
        if (!valid.TryGetValue(mappings.NetPayableMapping.AccountId, out var payableAccount)
            || payableAccount.AccountType != ChartAccountTypes.Liability)
            throw new PayrollAccountingPostingException(
                "The active NET_PAYABLE mapping must reference an active Business liability account.");

        var amount = source.EvidenceAmount;
        if (amount <= 0m)
            throw new PayrollAccountingPostingException(
                "Only a verified amount from a fully settled allocation can be posted.");

        var costCenter = source.HistoricalLocationId is > 0
            ? $"HR_LOCATION:{source.HistoricalLocationId.Value.ToString(CultureInfo.InvariantCulture)}"
            : null;
        var reference = PostingReference(source);
        var date = new DateTime(
            source.EvidenceDate.Year, source.EvidenceDate.Month, source.EvidenceDate.Day,
            0, 0, 0, DateTimeKind.Utc);
        var input = new ManualJournalDraftInput(
            AccountingJournalTypes.SystemJournal,
            date,
            date,
            date,
            "PayrollSalaryPayment",
            reference,
            source.EvidenceReference,
            $"Payroll salary settlement {source.AllocationMethod} payment " +
                $"{source.PaymentId.ToString(CultureInfo.InvariantCulture)}, allocation " +
                $"{source.AllocationNo.ToString(CultureInfo.InvariantCulture)}",
            null, null, null, null,
            [
                new ManualJournalDraftLineInput(
                    settlementAccount.Id,
                    $"Payroll settlement received by {source.AllocationMethod}",
                    amount, 0m, null, null, null, null, null, costCenter),
                new ManualJournalDraftLineInput(
                    payableAccount.Id,
                    "Reduce Payroll salary payable",
                    0m, amount, null, null, null, null, null, costCenter)
            ],
            source.CurrencyCode,
            source.RunRevision,
            SourceEvidenceHash(source, mappings));
        return FrozenSystemJournalRequest.ForPosting(businessId, reference, input);
    }

    private static void ValidateSource(PayrollAccountingSettlementSource source)
    {
        if (source.RunStatus is not (PayrollRunStatuses.Approved
                or PayrollRunStatuses.PaymentInProgress or PayrollRunStatuses.Paid
                or PayrollRunStatuses.Completed)
            || source.PeriodStatus is not (PayrollPeriodStatuses.Approved or PayrollPeriodStatuses.Posted)
            || source.RunType == PayrollRunTypes.Correction
            || source.RunRevision <= 0
            || string.IsNullOrWhiteSpace(source.CalculationInputHash)
            || string.IsNullOrWhiteSpace(source.ApprovalEvidenceHash)
            || source.CurrencyCode is null || source.CurrencyCode.Length != 3
            || source.DecimalPrecision is < 0 or > 6
            || source.EmployeeResultId <= 0 || source.NetPay <= 0m
            || source.PaymentId <= 0 || source.AllocationId <= 0 || source.SettlementEvidenceId <= 0
            || source.PaymentStatus is not (PayrollPaymentStatuses.PartiallyPaid or PayrollPaymentStatuses.Paid)
            || source.ResolutionStatus != PayrollPaymentResolutionStatuses.None
            || source.EvidenceType != PayrollPaymentEvidenceTypes.Settlement
            || !PayrollPaymentMethods.IsAllocationMethod(source.AllocationMethod)
            || source.AllocationState != PayrollPaymentAllocationStates.Settled
            || source.AllocationAmount <= 0m
            || source.AllocationSettledAmount != source.AllocationAmount
            || source.ReconciliationId is null
            || source.ReconciliationVersion is null
            || source.ReconciliationStatus != PayrollPaymentReconciliationStatuses.Reconciled
            || source.ReconciliationEvidenceSource != source.AllocationMethod
            || source.ReconciliationExpectedAmount != source.AllocationAmount
            || source.ReconciliationSettledAmount != source.AllocationSettledAmount
            || source.EvidenceAmount <= 0m || source.EvidenceAmount > source.AllocationSettledAmount
            || string.IsNullOrWhiteSpace(source.EvidenceReference)
            || source.EvidencePayloadHash.Length != 64
            || source.PaymentStatus == PayrollPaymentStatuses.PartiallyPaid
                && (source.PaidAmount <= 0m || source.OutstandingAmount <= 0m)
            || source.PaymentStatus == PayrollPaymentStatuses.Paid
                && (source.OutstandingAmount != 0m || source.PaidAmount != source.PayableAmount))
            throw new PayrollAccountingPostingException(
                "Payroll settlement posting requires an approved, non-correction run and a fully settled, fully reconciled allocation with verified settlement evidence; unresolved payment states are not eligible.");

        if (source.AllocationEvidenceJson.Length == 0
            || source.PayableAmount <= 0m
            || source.PaidAmount + source.OutstandingAmount != source.PayableAmount)
            throw new PayrollAccountingPostingException(
                "Payroll payment totals or allocation evidence are incomplete.");
    }

    private async Task EnsurePayrollAccessAsync(
        long businessId, string permission, CancellationToken cancellationToken)
    {
        await access.EnsurePermissionAsync(businessId, permission, cancellationToken);
        await access.EnsurePermissionAsync(businessId, "payroll.salary.view", cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Hr, cancellationToken);
        await capabilities.EnsureEnabledAsync(HrPayrollCapabilityKeys.Payroll, cancellationToken);
    }

    private async Task<PayrollAccountingPosting> SaveFailureAsync(
        long businessId,
        long actorUserId,
        PayrollAccountingSettlementSource source,
        FrozenSystemJournalRequest? request,
        string message,
        string status,
        CancellationToken cancellationToken)
    {
        var posting = await store.FindSettlementPostingAsync(
            businessId, source.SettlementEvidenceId, source.AllocationId, cancellationToken);
        if (posting?.Status == PayrollAccountingPostingStatuses.Posted)
            return posting;
        posting ??= CreatePosting(businessId, actorUserId, source, request);
        posting.PayrollPaymentReconciliationId = source.ReconciliationId;
        if (request is not null && posting.RequestPayloadHash is not null
            && !StringComparer.Ordinal.Equals(posting.RequestPayloadHash, request.PayloadHash))
        {
            posting.Status = PayrollAccountingPostingStatuses.Conflict;
            posting.ConflictPayloadHash = request.PayloadHash;
            posting.ErrorMessage =
                "The immutable settlement identity was requested with different canonical content.";
        }
        else
        {
            if (request is not null)
            {
                posting.RequestPayloadJson = request.CanonicalPayloadJson;
                posting.RequestPayloadHash = request.PayloadHash;
            }
            posting.Status = status;
            posting.ErrorMessage = message;
            posting.AttemptCount = checked(posting.AttemptCount + 1);
        }
        posting.Version = checked(posting.Version + 1);
        await store.SavePostingAsync(posting, cancellationToken);
        return posting;
    }

    private static PayrollAccountingPosting CreatePosting(
        long businessId,
        long actorUserId,
        PayrollAccountingSettlementSource source,
        FrozenSystemJournalRequest? request)
    {
        var reference = request?.PostingReference ?? PostingReference(source);
        return new PayrollAccountingPosting
        {
            BusinessId = businessId,
            PayrollPeriodId = source.PayrollPeriodId,
            PayrollRunId = source.PayrollRunId,
            RunRevision = source.RunRevision,
            PostingType = PayrollAccountingPostingTypes.SalaryPayment,
            PaymentBatchId = source.PaymentBatchId,
            PayrollPaymentId = source.PaymentId,
            PayrollPaymentAllocationId = source.AllocationId,
            PayrollPaymentEvidenceId = source.SettlementEvidenceId,
            PayrollPaymentReconciliationId = source.ReconciliationId,
            PostingReference = reference,
            IdempotencyKey = reference,
            RequestedByUserId = actorUserId,
            RequestedAtUtc = DateTime.UtcNow,
            Version = 1
        };
    }

    private static string PostingReference(PayrollAccountingSettlementSource source) =>
        $"payroll-settlement:{source.BusinessId.ToString(CultureInfo.InvariantCulture)}:" +
        $"{source.SettlementEvidenceId.ToString(CultureInfo.InvariantCulture)}:" +
        source.AllocationId.ToString(CultureInfo.InvariantCulture);

    private static bool HasExpectedIdentity(
        PayrollAccountingPosting posting, PayrollAccountingSettlementSource source) =>
        posting.PostingType == PayrollAccountingPostingTypes.SalaryPayment
        && posting.PayrollPaymentId == source.PaymentId
        && posting.PayrollPaymentAllocationId == source.AllocationId
        && posting.PayrollPaymentEvidenceId == source.SettlementEvidenceId;

    private static string SourceEvidenceHash(
        PayrollAccountingSettlementSource source,
        PayrollAccountingSettlementAccountMappings mappings)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            source.BusinessId,
            source.PayrollPeriodId,
            source.PayrollRunId,
            source.RunRevision,
            source.EmployeeResultId,
            source.PaymentId,
            source.PaymentBatchId,
            source.PaymentStatus,
            source.PayableAmount,
            source.PaidAmount,
            source.OutstandingAmount,
            source.AllocationId,
            source.AllocationNo,
            source.AllocationMethod,
            source.AllocationAmount,
            source.AllocationSettledAmount,
            source.SettlementEvidenceId,
            source.EvidenceAmount,
            source.EvidenceDate,
            source.EvidenceReference,
            source.EvidencePayloadHash,
            source.ReconciliationId,
            source.ReconciliationVersion,
            source.ReconciliationExpectedAmount,
            source.ReconciliationSettledAmount,
            source.ReconciliationEvidenceSource,
            mappings.CashAccountId,
            mappings.DefaultBankAccountId,
            mappings.MobileMfsClearingAccountId,
            mappings.UpdatedAt,
            PayableMappingId = mappings.NetPayableMapping?.Id,
            PayableMappingAccountId = mappings.NetPayableMapping?.AccountId,
            PayableMappingVersion = mappings.NetPayableMapping?.Version
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static RequiredAuditOperation CreateAuditOperation(
        long businessId,
        PayrollAccountingSettlementSource source,
        FrozenSystemJournalRequest? request,
        string? validationMessage) =>
        new(businessId, "payroll.accounting.settlement_requested", "Payroll",
            "Payroll salary settlement posting request.",
            "PayrollPayment", source.PaymentId.ToString(CultureInfo.InvariantCulture),
            [
                new AuditEventEntityWriteRequest(
                    "PayrollAccountingPosting", PostingReference(source), "Requested",
                    JsonSerializer.Serialize(new
                    {
                        source.BusinessId,
                        source.PayrollRunId,
                        source.RunRevision,
                        source.PaymentId,
                        source.AllocationId,
                        source.SettlementEvidenceId,
                        source.ReconciliationId,
                        PostingType = PayrollAccountingPostingTypes.SalaryPayment,
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
        PayrollAccountingPostingException or FrozenJournalConflictException
            or PayrollAccountingPostingConflictException =>
            exception.Message.Length <= 1000 ? exception.Message : exception.Message[..1000],
        _ => "Accounting settlement posting failed or could not be confirmed. Refresh posting status before retrying."
    };

    private sealed record PostingAttempt(
        PayrollAccountingSettlementSource Source,
        FrozenSystemJournalRequest? Request);
}

public sealed class PostPayrollAccountingSettlementCommandHandler(
    PayrollAccountingSettlementPostingService service)
    : IRequestHandler<PostPayrollAccountingSettlementCommand, PayrollAccountingPostingResponse>
{
    public Task<PayrollAccountingPostingResponse> Handle(
        PostPayrollAccountingSettlementCommand request, CancellationToken cancellationToken) =>
        service.PostAsync(request.PaymentId, request.AllocationId, request.EvidenceId, cancellationToken);
}
