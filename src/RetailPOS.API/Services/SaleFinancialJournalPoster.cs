using MediatR;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

/// <summary>
/// D10 journal producer for sales and their reversals, built only from the immutable commit-time snapshot (P3.2-D01).
/// <para>Sale: Dr payments (= payable) · Dr Sales discount (discounts) · Cr Sales (net revenue + discounts) ·
/// Cr VAT payable (VAT) · Dr COGS / Cr Inventory (historical cost). Net revenue = payable − VAT for inclusive and
/// exclusive pricing alike, so the journal balances exactly without any balancing line.</para>
/// <para>Reversal: Dr Sales return (returned net revenue) · Dr VAT payable (returned VAT) · Cr the original payment
/// accounts (net + VAT, allocated in whole cents) · Dr Inventory / Cr COGS (historical cost), dated on the reversal's
/// own accounting date (P3.2-D02).</para>
/// The journal reference (<c>Sale</c>/<c>SaleRefund</c> + sale id) is unchanged, so a journal that already exists for
/// the sale is resolved instead of duplicated. The Business always comes from the snapshot, never from an HTTP context.
/// </summary>
public sealed class SaleFinancialJournalPoster(
    ISender sender,
    IAccountingConfigurationService accountingConfigurationService) : ISaleFinancialJournalPoster
{
    public async Task<SaleJournalPostingResult> PostSaleAsync(
        SaleJournalSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        RequireSaleEvidence(snapshot);
        var lines = new List<CreateJournalEntryLineDto>();
        var paymentAccounts = new Dictionary<long, long>();
        var saleDate = snapshot.SaleDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        foreach (var payment in snapshot.Payments.Where(payment => payment.Amount != 0m))
        {
            var paymentAccountId = payment.PaymentAccountId
                ?? await ResolvePaymentPostingAccountAsync(snapshot.OutletId, payment.Method, saleDate);
            if (!payment.PaymentAccountId.HasValue)
                paymentAccounts[payment.PaymentId] = paymentAccountId;
            lines.Add(Line(paymentAccountId, $"Sale payment via {payment.Method}", payment.Amount, 0, snapshot.OutletId));
        }

        if (snapshot.Discount > 0)
            lines.Add(Line(
                await ResolveRequiredPostingAccountAsync(PostingAccountSlots.SalesDiscount, snapshot.OutletId),
                "Sales discount", snapshot.Discount, 0, snapshot.OutletId));

        if (snapshot.NetRevenue + snapshot.Discount > 0)
            lines.Add(Line(
                await ResolveRequiredPostingAccountAsync(PostingAccountSlots.Sales, snapshot.OutletId),
                "Sales revenue", 0, snapshot.NetRevenue + snapshot.Discount, snapshot.OutletId));

        if (snapshot.Vat > 0)
            lines.Add(Line(
                await ResolveRequiredPostingAccountAsync(PostingAccountSlots.VatPayable, snapshot.OutletId),
                "VAT payable", 0, snapshot.Vat, snapshot.OutletId));

        await AddCostLinesAsync(lines, snapshot.Cost, snapshot.OutletId, "Cost of goods sold", "Inventory reduction", reverse: false);
        RequireBalanced(lines, snapshot.SaleId);
        cancellationToken.ThrowIfCancellationRequested();
        var journalId = await CreateAndPostAsync(
            snapshot.BusinessId,
            snapshot.ActorUserId,
            SaleJournalInputFactory.Create(
                "SalePosting",
                snapshot.TransactionAtUtc,
                snapshot.BusinessDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                "Sale",
                snapshot.SaleId.ToString(),
                snapshot.SaleNumber,
                $"POS sale posting for {snapshot.SaleNumber}",
                snapshot.OutletId,
                snapshot.TerminalId,
                snapshot.CashSessionId,
                lines),
            SaleFinancialIntentFactory.SaleSourceKey(snapshot.SaleId),
            cancellationToken);
        return new SaleJournalPostingResult(journalId, paymentAccounts);
    }

    public async Task<long> PostReversalAsync(
        SaleReversalJournalSnapshot snapshot,
        IReadOnlyDictionary<long, long?> paymentAccounts,
        CancellationToken cancellationToken)
    {
        var accountingDate = RequireReversalEvidence(snapshot);
        var lines = new List<CreateJournalEntryLineDto>();
        if (snapshot.RefundAmount > 0)
            lines.Add(Line(
                await ResolveRequiredPostingAccountAsync(PostingAccountSlots.SalesReturn, snapshot.OutletId),
                "Sales return", snapshot.RefundAmount, 0, snapshot.OutletId));
        if (snapshot.RefundVat > 0)
            lines.Add(Line(
                await ResolveRequiredPostingAccountAsync(PostingAccountSlots.VatPayable, snapshot.OutletId),
                "VAT payable reversal", snapshot.RefundVat, 0, snapshot.OutletId));

        foreach (var payment in snapshot.Payments.Where(payment => payment.Amount != 0m))
        {
            var paymentAccountId = (paymentAccounts.TryGetValue(payment.PaymentId, out var recorded) ? recorded : null)
                ?? await ResolveLegacyPaymentReversalAccountAsync(snapshot.OutletId, payment.Method);
            lines.Add(Line(paymentAccountId, $"Refund reversal via {payment.Method}", 0, payment.Amount, snapshot.OutletId));
        }

        await AddCostLinesAsync(lines, snapshot.Cost, snapshot.OutletId, "COGS reversal", "Inventory restoration", reverse: true);
        RequireBalanced(lines, snapshot.SaleId);
        cancellationToken.ThrowIfCancellationRequested();
        var accountingDay = accountingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return await CreateAndPostAsync(
            snapshot.BusinessId,
            snapshot.ActorUserId,
            SaleJournalInputFactory.Create(
                "SalesReturnPosting",
                accountingDay,
                accountingDay,
                "SaleRefund",
                snapshot.SaleId.ToString(),
                snapshot.SaleNumber,
                $"POS {snapshot.ReversalType} reversal for {snapshot.SaleNumber} (sale business date {snapshot.BusinessDate:yyyy-MM-dd})",
                snapshot.OutletId,
                snapshot.TerminalId,
                snapshot.CashSessionId,
                lines,
                snapshot.RequestedAtUtc),
            SaleFinancialIntentFactory.ReversalSourceKey(snapshot.SaleId),
            cancellationToken);
    }

    private static void RequireSaleEvidence(SaleJournalSnapshot snapshot)
    {
        if (snapshot.Payments.Sum(payment => payment.Amount) != snapshot.TotalAmount)
            throw new SaleFinancialEvidenceException(
                SaleFinancialPostingErrorCodes.PaymentAllocationMismatch,
                $"Payments of sale {snapshot.SaleId} ({snapshot.Payments.Sum(payment => payment.Amount):0.00}) do not equal its payable ({snapshot.TotalAmount:0.00}).");
        if (snapshot.NetRevenue < 0 || snapshot.Vat < 0)
            throw new SaleFinancialEvidenceException(
                SaleFinancialPostingErrorCodes.AllocationEvidenceMissing,
                $"Sale {snapshot.SaleId} has a negative net revenue or VAT.");
        // v2 snapshots carry the line allocations; when complete they must agree with the sale totals.
        if (snapshot.Lines is { Count: > 0 } lines && lines.All(line => line.TaxableAmount.HasValue && line.VatAmount.HasValue)
            && (lines.Sum(line => line.TaxableAmount!.Value) != snapshot.NetRevenue
                || lines.Sum(line => line.VatAmount!.Value) != snapshot.Vat))
            throw new SaleFinancialEvidenceException(
                SaleFinancialPostingErrorCodes.AllocationEvidenceMissing,
                $"Line net/VAT allocations of sale {snapshot.SaleId} do not reconcile to its payable and VAT.");
    }

    private static DateOnly RequireReversalEvidence(SaleReversalJournalSnapshot snapshot)
    {
        if (snapshot.Version < 2 || snapshot.AccountingDate is null)
            throw new SaleFinancialEvidenceException(
                SaleFinancialPostingErrorCodes.SnapshotRequiresReview,
                $"Reversal of sale {snapshot.SaleId} was recorded before Phase 3.2 without an accounting date or VAT allocation; it needs review.");
        if (snapshot.MissingEvidence is not null)
            throw new SaleFinancialEvidenceException(
                SaleFinancialPostingErrorCodes.AllocationEvidenceMissing, snapshot.MissingEvidence);
        if (snapshot.Payments.Sum(payment => payment.Amount) != snapshot.RefundTotal
            || snapshot.RefundAmount + snapshot.RefundVat != snapshot.RefundTotal)
            throw new SaleFinancialEvidenceException(
                SaleFinancialPostingErrorCodes.PaymentAllocationMismatch,
                $"Reversal allocations of sale {snapshot.SaleId} do not reconcile to the reversed amount.");
        return snapshot.AccountingDate.Value;
    }

    private static void RequireBalanced(IReadOnlyCollection<CreateJournalEntryLineDto> lines, long saleId)
    {
        var debit = lines.Sum(line => line.DebitAmount);
        var credit = lines.Sum(line => line.CreditAmount);
        if (debit != credit)
            throw new SaleFinancialEvidenceException(
                SaleFinancialPostingErrorCodes.PaymentAllocationMismatch,
                $"Journal for sale {saleId} would not balance (debit {debit:0.00}, credit {credit:0.00}).");
    }

    private async Task AddCostLinesAsync(
        List<CreateJournalEntryLineDto> lines,
        decimal cost,
        long outletId,
        string cogsDescription,
        string inventoryDescription,
        bool reverse)
    {
        if (cost <= 0)
            return;
        var cogs = await ResolveRequiredPostingAccountAsync(PostingAccountSlots.Cogs, outletId);
        var inventory = await ResolveRequiredPostingAccountAsync(PostingAccountSlots.Inventory, outletId);
        if (reverse)
        {
            lines.Add(Line(inventory, inventoryDescription, cost, 0, outletId));
            lines.Add(Line(cogs, cogsDescription, 0, cost, outletId));
        }
        else
        {
            lines.Add(Line(cogs, cogsDescription, cost, 0, outletId));
            lines.Add(Line(inventory, inventoryDescription, 0, cost, outletId));
        }
    }

    private async Task<long> CreateAndPostAsync(
        long businessId,
        long actorUserId,
        Application.AccountingJournals.Abstractions.ManualJournalDraftInput journal,
        string sourceKey,
        CancellationToken cancellationToken)
    {
        var journalId = await sender.Send(
            new CreateOrGetSystemJournalDraftCommand(businessId, actorUserId, journal, sourceKey),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await sender.Send(new PostJournalEntryCommand(businessId, actorUserId, journalId), cancellationToken);
        return journalId;
    }

    private static CreateJournalEntryLineDto Line(
        long accountId,
        string description,
        decimal debit,
        decimal credit,
        long outletId) =>
        new()
        {
            AccountId = accountId,
            Description = description,
            DebitAmount = debit,
            CreditAmount = credit,
            OutletId = outletId
        };

    private async Task<long> ResolveRequiredPostingAccountAsync(string slot, long outletId) =>
        (await accountingConfigurationService.ResolvePostingAccountAsync(slot, outletId)).AccountId;

    private async Task<long> ResolvePaymentPostingAccountAsync(long outletId, string? method, DateTime saleDate)
    {
        var configuration = await accountingConfigurationService.GetBusinessConfigurationAsync();
        var slot = PaymentPostingPolicy.ResolveSlot(method, saleDate, configuration?.BankClearingActivationDate);
        return await ResolveRequiredPostingAccountAsync(slot, outletId);
    }

    private async Task<long> ResolveLegacyPaymentReversalAccountAsync(long outletId, string? method) =>
        (await accountingConfigurationService.ResolvePostingAccountAsync(
            string.Equals(method, "cash", StringComparison.OrdinalIgnoreCase)
                ? PostingAccountSlots.Cash
                : PostingAccountSlots.Bank,
            outletId)).AccountId;
}
