using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.HR.Exit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Application.HR.Payroll;

public sealed class PayrollFinalSettlementAccountingService(
    IHrExitAccess access,
    IPayrollFinalSettlementStore settlements,
    IPayrollAccountingPostingStore payrollAccounting,
    IPayrollFinalSettlementExecutionStore execution,
    IAccountingPostingAccountReadStore accounts,
    IFrozenSystemJournalStore accountingJournal,
    IJournalEntryReadStore journals,
    IRequiredAuditTransaction audit) :
    IRequestHandler<PostPayrollFinalSettlementAccountingCommand,
        PayrollFinalSettlementAccountingStatus>,
    IRequestHandler<PostPayrollFinalSettlementPaymentAccountingCommand,
        PayrollFinalSettlementAccountingStatus>
{
    private const string ExpenseReferenceType = "PayrollFinalSettlement";
    private const string PaymentReferenceType = "PayrollFinalSettlementPayment";

    public async Task<PayrollFinalSettlementAccountingStatus> Handle(
        PostPayrollFinalSettlementAccountingCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Id <= 0 || command.RevisionNo <= 0
            || string.IsNullOrWhiteSpace(command.IdempotencyKey)
            || command.IdempotencyKey.Trim().Length > 80)
            throw new ArgumentException(
                "A valid Final Settlement, revision and bounded idempotency key are required.");
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        var scope = await settlements.GetSettlementScopeAsync(
            businessId, command.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Final Settlement was not found.");
        await access.EnsureAsync(businessId, HrExitOperation.FinalSettlementAccountingPost,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        var source = await settlements.GetByIdAsync(businessId, command.Id, cancellationToken)
            ?? throw new KeyNotFoundException("Final Settlement was not found.");
        EnsureApproved(source, command.RevisionNo);
        if (source.NetSettlement == 0m)
            return NoPostingRequired(command.Id, command.RevisionNo, source.ApprovalInstanceId);

        var postingType = source.NetSettlement > 0m
            ? PayrollFinalSettlementPostingTypes.Expense
            : PayrollFinalSettlementPostingTypes.Receivable;
        var reference = $"FS-{command.Id.ToString(CultureInfo.InvariantCulture)}-r" +
            $"{command.RevisionNo.ToString(CultureInfo.InvariantCulture)}";
        var result = await audit.ExecuteAsync(
            AuditOperation(businessId, "payroll.final_settlement.accounting.posting_requested",
                "Requested Accounting posting for approved Final Settlement evidence.",
                command.Id),
            token => PostAsync(
                businessId, actorUserId, command.Id, command.RevisionNo, source.RevisionId, postingType,
                null, null, reference, command.IdempotencyKey.Trim(),
                async ct =>
                {
                    var current = await settlements.GetByIdAsync(businessId, command.Id, ct)
                        ?? throw new KeyNotFoundException("Final Settlement was not found.");
                    EnsureApproved(current, command.RevisionNo);
                    if (current.NetSettlement == 0m)
                        throw new PayrollFinalSettlementAccountingConflictException(
                            "The Final Settlement outcome changed to zero before Accounting posting.");
                    return await BuildExpenseRequestAsync(
                        businessId, current, scope, reference, ct);
                },
                token),
            cancellationToken);
        return result;
    }

    public async Task<PayrollFinalSettlementAccountingStatus> Handle(
        PostPayrollFinalSettlementPaymentAccountingCommand command,
        CancellationToken cancellationToken)
    {
        if (command.PaymentId <= 0 || command.EvidenceId <= 0)
            throw new ArgumentOutOfRangeException(nameof(command.PaymentId));
        var businessId = access.RequireBusinessId();
        var actorUserId = access.RequireRealUserId();
        var source = await execution.GetPaymentSourceAsync(
            businessId, command.PaymentId, command.EvidenceId, cancellationToken)
            ?? throw new KeyNotFoundException(
                "Verified Final Settlement payment evidence was not found.");
        var scope = await settlements.GetSettlementScopeAsync(
            businessId, source.FinalSettlementId, cancellationToken)
            ?? throw new KeyNotFoundException("Final Settlement was not found.");
        await access.EnsureAsync(businessId,
            HrExitOperation.FinalSettlementPaymentAccountingPost,
            scope.EmployeeId, scope.PrimaryLocationProjectionId, cancellationToken);
        var reference = $"FSP-{source.FinalSettlementId.ToString(CultureInfo.InvariantCulture)}-" +
            $"r{source.RevisionNo.ToString(CultureInfo.InvariantCulture)}-" +
            $"{command.PaymentId.ToString(CultureInfo.InvariantCulture)}-" +
            $"{command.EvidenceId.ToString(CultureInfo.InvariantCulture)}";
        return await audit.ExecuteAsync(
            AuditOperation(businessId, "payroll.final_settlement.payment.accounting.posting_requested",
                "Requested Accounting posting for verified Final Settlement payment evidence.",
                source.FinalSettlementId),
            token => PostAsync(
                businessId, actorUserId, source.FinalSettlementId, source.RevisionNo,
                source.RevisionId,
                PayrollFinalSettlementPostingTypes.Payment, command.PaymentId,
                command.EvidenceId, reference, reference,
                ct => BuildPaymentRequestAsync(businessId, source, reference, ct),
                token),
            cancellationToken);
    }

    private async Task<PayrollFinalSettlementAccountingStatus> PostAsync(
        long businessId,
        long actorUserId,
        long settlementId,
        int revisionNo,
        long revisionId,
        string postingType,
        long? paymentId,
        long? evidenceId,
        string reference,
        string idempotencyKey,
        Func<CancellationToken, Task<FrozenSystemJournalRequest>> buildRequest,
        CancellationToken cancellationToken)
    {
        if (await payrollAccounting.IsAccountingEnabledAsync(
                businessId, cancellationToken) != true)
            return new PayrollFinalSettlementAccountingStatus(
                0, revisionNo, postingType, reference, "NotEnabled",
                null, null, "Accounting is not explicitly enabled for this Business.",
                0, 0, null);

        await execution.LockPostingIdentityAsync(businessId, reference, cancellationToken);
        var request = await buildRequest(cancellationToken);
        var posting = await execution.FindPostingAsync(
            businessId, settlementId, revisionNo, postingType, paymentId, evidenceId,
            cancellationToken);
        if (posting is not null
            && !StringComparer.Ordinal.Equals(posting.RequestPayloadHash, request.PayloadHash))
        {
            posting.ConflictPayloadHash = request.PayloadHash;
            posting.Status = PayrollAccountingPostingStatuses.Conflict;
            posting.ErrorMessage =
                "The stable Final Settlement posting identity was requested with different frozen content.";
            posting.Version = checked(posting.Version + 1);
            await execution.SavePostingAsync(posting, cancellationToken);
            return Map(posting);
        }
        if (posting?.Status == PayrollAccountingPostingStatuses.Posted)
            return Map(posting);

        posting ??= new PayrollFinalSettlementAccountingPosting
        {
            BusinessId = businessId,
            FinalSettlementId = settlementId,
            RevisionId = revisionId,
            RevisionNo = revisionNo,
            PostingType = postingType,
            PaymentId = paymentId,
            PaymentEvidenceId = evidenceId,
            PostingReference = reference,
            IdempotencyKey = idempotencyKey,
            RequestPayloadJson = request.CanonicalPayloadJson,
            RequestPayloadHash = request.PayloadHash,
            RequestedByUserId = actorUserId,
            RequestedAtUtc = DateTime.UtcNow,
            Status = PayrollAccountingPostingStatuses.Processing,
            AttemptCount = 1,
            Version = 1
        };
        if (posting.Id > 0)
        {
            posting.Status = PayrollAccountingPostingStatuses.Processing;
            posting.ErrorMessage = null;
            posting.AttemptCount = checked(posting.AttemptCount + 1);
            posting.Version = checked(posting.Version + 1);
        }
        await execution.SavePostingAsync(posting, cancellationToken);

        var journalId = await accountingJournal.ResolveExistingAsync(request, cancellationToken);
        if (journalId is null)
            journalId = await accountingJournal.PostAsync(request, actorUserId, cancellationToken);
        var journal = await journals.GetByIdAsync(businessId, journalId.Value, cancellationToken);
        if (journal.Status != JournalEntryStatuses.Posted
            || !journal.IsSystemGenerated
            || !StringComparer.Ordinal.Equals(journal.ReferenceId, reference))
            throw new FrozenJournalConflictException(
                "Accounting did not resolve this stable identity to the exact active Posted journal.");
        posting.AccountingJournalId = journalId;
        posting.AccountingReference = journal.ReferenceNumber;
        posting.Status = PayrollAccountingPostingStatuses.Posted;
        posting.ErrorMessage = null;
        posting.PostedAtUtc = DateTime.UtcNow;
        posting.Version = checked(posting.Version + 1);
        await execution.SavePostingAsync(posting, cancellationToken);
        return Map(posting);
    }

    private async Task<FrozenSystemJournalRequest> BuildExpenseRequestAsync(
        long businessId,
        PayrollFinalSettlementModel source,
        HrExitScope scope,
        string reference,
        CancellationToken cancellationToken)
    {
        var mappings = await payrollAccounting.GetMappingsAsync(businessId, cancellationToken);
        var active = mappings.Where(row => row.IsActive)
            .ToDictionary(row => (row.ComponentType, row.ComponentCode));
        var requiredMappingKeys = source.Lines
            .Where(line => line.IsApplicable && line.Amount != 0m
                && line.LineType is PayrollAccountingComponentTypes.Earning
                    or PayrollAccountingComponentTypes.Deduction)
            .Select(line => (
                ComponentType: line.LineType,
                ComponentCode: line.ComponentCode.ToUpperInvariant()))
            .Distinct()
            .ToList();
        if (source.NetSettlement > 0m)
            requiredMappingKeys.Add((PayrollAccountingComponentTypes.NetPayable, "NET_PAYABLE"));
        else if (source.NetSettlement < 0m)
            requiredMappingKeys.Add((
                PayrollAccountingComponentTypes.FinalSettlementReceivable,
                "FINAL_SETTLEMENT_RECEIVABLE"));
        var missing = requiredMappingKeys
            .Where(key => !active.ContainsKey(key))
            .ToArray();
        if (missing.Length != 0)
            throw new PayrollFinalSettlementAccountingException(
                $"Active Business-scoped Accounting mappings are required for: {string.Join(", ", missing.Select(key => $"{key.ComponentType}/{key.ComponentCode}"))}.");
        var accountIds = requiredMappingKeys.Select(key => active[key].AccountId).Distinct().ToArray();
        var chartAccounts = await accounts.GetChartAccountsForBusinessAsync(
            businessId, accountIds, cancellationToken);
        var byId = chartAccounts.Where(row => row.IsActive).ToDictionary(row => row.Id);
        var lines = new List<ManualJournalDraftLineInput>();
        var costCenter = scope.PrimaryLocationProjectionId is > 0
            ? $"HR_LOCATION:{scope.PrimaryLocationProjectionId.Value.ToString(CultureInfo.InvariantCulture)}"
            : null;
        foreach (var line in source.Lines.Where(line => line.IsApplicable
                     && line.Amount != 0m
                     && line.LineType is PayrollAccountingComponentTypes.Earning
                         or PayrollAccountingComponentTypes.Deduction))
        {
            if (line.Amount < 0m)
                throw new PayrollFinalSettlementAccountingException(
                    "Final Settlement Accounting lines cannot contain negative component amounts.");
            var key = (
                LineType: line.LineType,
                ComponentCode: line.ComponentCode.ToUpperInvariant());
            var mapping = active[key];
            if (!byId.TryGetValue(mapping.AccountId, out var account)
                || line.LineType == PayrollAccountingComponentTypes.Earning
                    && account.AccountType is not (ChartAccountTypes.Expense or ChartAccountTypes.CostOfGoodsSold)
                || line.LineType == PayrollAccountingComponentTypes.Deduction
                    && account.AccountType is not (ChartAccountTypes.Asset or ChartAccountTypes.Liability
                        or ChartAccountTypes.Expense or ChartAccountTypes.CostOfGoodsSold))
                throw new PayrollFinalSettlementAccountingException(
                    $"Accounting mapping for {key.LineType}/{key.ComponentCode} must reference an active, same-Business account of the correct type.");
            lines.Add(new ManualJournalDraftLineInput(
                account.Id, $"Final Settlement {line.LineType}: {line.ComponentCode}",
                line.LineType == PayrollAccountingComponentTypes.Earning ? line.Amount : 0m,
                line.LineType == PayrollAccountingComponentTypes.Deduction ? line.Amount : 0m,
                null, null, null, null, null, costCenter));
        }

        if (source.NetSettlement != 0m)
        {
            var type = source.NetSettlement > 0m
                ? PayrollAccountingComponentTypes.NetPayable
                : PayrollAccountingComponentTypes.FinalSettlementReceivable;
            var code = source.NetSettlement > 0m ? "NET_PAYABLE" : "FINAL_SETTLEMENT_RECEIVABLE";
            var mapping = active[(type, code)];
            if (!byId.TryGetValue(mapping.AccountId, out var account)
                || source.NetSettlement > 0m && account.AccountType != ChartAccountTypes.Liability
                || source.NetSettlement < 0m && account.AccountType != ChartAccountTypes.Asset)
                throw new PayrollFinalSettlementAccountingException(
                    $"The {code} mapping must reference an active Business liability/receivable account.");
            var amount = Math.Abs(source.NetSettlement);
            lines.Add(new ManualJournalDraftLineInput(
                account.Id,
                source.NetSettlement > 0m
                    ? "Final Settlement net payable"
                    : "Employee Final Settlement receivable",
                source.NetSettlement < 0m ? amount : 0m,
                source.NetSettlement > 0m ? amount : 0m,
                null, null, null, null, null, costCenter));
        }

        if (lines.Sum(line => line.DebitAmount) != lines.Sum(line => line.CreditAmount))
            throw new PayrollFinalSettlementAccountingException(
                "The Final Settlement component snapshot does not produce a balanced Accounting journal.");
        var date = new DateTime(source.SettlementDate.Year, source.SettlementDate.Month,
            source.SettlementDate.Day, 0, 0, 0, DateTimeKind.Utc);
        var input = new ManualJournalDraftInput(
            AccountingJournalTypes.SystemJournal, date, date, date, ExpenseReferenceType,
            reference, reference, $"Final Settlement revision {source.RevisionNo}",
            null, null, null, null, lines, source.CurrencyCode, source.RevisionNo,
            EvidenceHash(source.SubmittedEvidenceHash!, mappings, source.RevisionNo));
        return FrozenSystemJournalRequest.ForPosting(businessId, reference, input);
    }

    private async Task<FrozenSystemJournalRequest> BuildPaymentRequestAsync(
        long businessId,
        PayrollFinalSettlementPaymentAccountingSource source,
        string reference,
        CancellationToken cancellationToken)
    {
        if (source.EvidenceType != PayrollPaymentEvidenceTypes.Settlement
            || source.PaymentStatus is not (PayrollPaymentStatuses.PartiallyPaid or PayrollPaymentStatuses.Paid)
            || source.ResolutionStatus != PayrollPaymentResolutionStatuses.None
            || source.PaidAmount <= 0m
            || source.PaidAmount + source.OutstandingAmount != source.PayableAmount
            || source.Allocations.Count == 0
            || source.Allocations.Any(allocation =>
                !allocation.IsReconciled
                || allocation.State != PayrollPaymentAllocationStates.Settled
                || allocation.EvidenceAmount != allocation.Amount
                || allocation.SettledAmount != allocation.Amount))
            throw new PayrollFinalSettlementAccountingException(
                "Final Settlement payment posting requires confirmed, fully settled and reconciled payment evidence.");

        var mappings = await payrollAccounting.GetSettlementAccountMappingsAsync(
            businessId, cancellationToken);
        if (mappings?.NetPayableMapping is null)
            throw new PayrollFinalSettlementAccountingException(
                "Accounting configuration and an active NET_PAYABLE mapping are required.");
        var settlementAccounts = source.Allocations.Select(allocation =>
            allocation.Method switch
            {
                PayrollPaymentMethods.Cash => mappings.CashAccountId,
                PayrollPaymentMethods.Bank => mappings.DefaultBankAccountId,
                PayrollPaymentMethods.Mobile => mappings.MobileMfsClearingAccountId,
                _ => null
            }).ToArray();
        if (settlementAccounts.Any(id => id is null))
            throw new PayrollFinalSettlementAccountingException(
                "A Business-scoped settlement account must be configured for every payment method.");
        var ids = settlementAccounts.Select(id => id!.Value)
            .Append(mappings.NetPayableMapping.AccountId).Distinct().ToArray();
        var chartAccounts = await accounts.GetChartAccountsForBusinessAsync(
            businessId, ids, cancellationToken);
        var byId = chartAccounts.Where(row => row.IsActive).ToDictionary(row => row.Id);
        var payableId = mappings.NetPayableMapping.AccountId;
        if (!byId.TryGetValue(payableId, out var payable)
            || payable.AccountType != ChartAccountTypes.Liability)
            throw new PayrollFinalSettlementAccountingException(
                "NET_PAYABLE must map to an active Business liability account.");
        var journalLines = new List<ManualJournalDraftLineInput>();
        for (var index = 0; index < source.Allocations.Count; index++)
        {
            var allocation = source.Allocations[index];
            var accountId = settlementAccounts[index]!.Value;
            if (!byId.TryGetValue(accountId, out var account)
                || account.AccountType != ChartAccountTypes.Asset)
                throw new PayrollFinalSettlementAccountingException(
                    $"The configured {allocation.Method} account must be an active Business asset account.");
            journalLines.Add(new ManualJournalDraftLineInput(
                accountId, $"Final Settlement payment {allocation.Method}",
                allocation.EvidenceAmount, 0m, null, null, null, null, null,
                source.HistoricalLocationId is > 0
                    ? $"HR_LOCATION:{source.HistoricalLocationId.Value.ToString(CultureInfo.InvariantCulture)}"
                    : null));
        }
        var total = source.Allocations.Sum(allocation => allocation.EvidenceAmount);
        journalLines.Add(new ManualJournalDraftLineInput(
            payableId, "Reduce Final Settlement payable", 0m, total,
            null, null, null, null, null,
            source.HistoricalLocationId is > 0
                ? $"HR_LOCATION:{source.HistoricalLocationId.Value.ToString(CultureInfo.InvariantCulture)}"
                : null));
        var date = new DateTime(source.EvidenceDate.Year, source.EvidenceDate.Month,
            source.EvidenceDate.Day, 0, 0, 0, DateTimeKind.Utc);
        var input = new ManualJournalDraftInput(
            AccountingJournalTypes.SystemJournal, date, date, date, PaymentReferenceType,
            reference, source.EvidenceReference,
            $"Final Settlement payment {source.PaymentId.ToString(CultureInfo.InvariantCulture)}",
            null, null, null, null, journalLines, source.CurrencyCode, source.RevisionNo,
            EvidenceHash(source.SubmittedEvidenceHash,
                [new PayrollAccountingMappingReadModel(
                    PayrollAccountingComponentTypes.NetPayable, "NET_PAYABLE", payableId,
                    payable.AccountCode, payable.AccountName, true, mappings.NetPayableMapping.Version)],
                source.RevisionNo, source.EvidencePayloadHash));
        return FrozenSystemJournalRequest.ForPosting(businessId, reference, input);
    }

    private static string EvidenceHash(
        string submittedEvidenceHash,
        IReadOnlyList<PayrollAccountingMappingReadModel> mappings,
        int revisionNo,
        string? paymentEvidenceHash = null)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            ContractVersion = 1,
            SubmittedEvidenceHash = submittedEvidenceHash,
            RevisionNo = revisionNo,
            PaymentEvidenceHash = paymentEvidenceHash,
            Mappings = mappings.OrderBy(row => row.ComponentType, StringComparer.Ordinal)
                .ThenBy(row => row.ComponentCode, StringComparer.Ordinal)
                .Select(row => new
                {
                    row.ComponentType, row.ComponentCode, row.AccountId, row.IsActive, row.Version
                })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static PayrollFinalSettlementAccountingStatus NoPostingRequired(
        long settlementId, int revisionNo, long? approvalInstanceId) =>
        new(0, revisionNo, PayrollFinalSettlementPostingTypes.Expense,
            $"FS-{settlementId.ToString(CultureInfo.InvariantCulture)}-r{revisionNo.ToString(CultureInfo.InvariantCulture)}",
            "NoPostingRequired", null, null,
            approvalInstanceId is > 0 ? "Zero-value approved settlement; no journal or payment is created." :
                "No approved settlement evidence was found.",
            0, 0, null);

    private static void EnsureApproved(
        PayrollFinalSettlementModel source, int revisionNo)
    {
        if (source.RevisionNo != revisionNo
            || source.RevisionId <= 0
            || source.Status != "Approved"
            || source.ApprovalInstanceId is not > 0
            || source.SubmittedEvidenceHash?.Length != 64)
            throw new PayrollFinalSettlementAccountingConflictException(
                "Only the currently approved revision with frozen submitted evidence can be posted.");
    }

    private static PayrollFinalSettlementAccountingStatus Map(
        PayrollFinalSettlementAccountingPosting posting) =>
        new(posting.Id, posting.RevisionNo, posting.PostingType, posting.PostingReference,
            posting.Status, posting.AccountingJournalId, posting.AccountingReference,
            posting.ErrorMessage, posting.AttemptCount, posting.Version, posting.PostedAtUtc);

    private static RequiredAuditOperation AuditOperation(
        long businessId, string eventType, string description, long settlementId) =>
        new(businessId, eventType, "Payroll", description,
            "PayrollFinalSettlement", settlementId.ToString(CultureInfo.InvariantCulture));
}
