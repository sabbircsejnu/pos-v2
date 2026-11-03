namespace RetailPOS.Application.FinancialYearCloseRuns.Abstractions;

public sealed record FinancialYearCloseJournalLine(
    long AccountId,
    decimal DebitAmount,
    decimal CreditAmount,
    string Description);

public sealed record FinancialYearCloseJournalDraft(
    string JournalType,
    DateTime TransactionDate,
    DateTime PostingDate,
    DateTime BusinessDate,
    string ReferenceType,
    string ReferenceId,
    string Description,
    IReadOnlyList<FinancialYearCloseJournalLine> Lines);

public interface IAccountingFinancialYearCloseJournalPosting
{
    Task<long> CreateOrGetSystemDraftAsync(
        long businessId,
        FinancialYearCloseJournalDraft draft,
        long actorUserId,
        string idempotencyDiscriminator,
        CancellationToken cancellationToken);

    Task<long> PostApprovedYearEndClosingAsync(
        long businessId,
        long journalEntryId,
        long financialYearCloseRunId,
        long actorUserId,
        CancellationToken cancellationToken);

    Task<long> ReverseYearEndJournalAsync(
        long businessId,
        long journalEntryId,
        string reason,
        DateTime postingDate,
        long actorUserId,
        CancellationToken cancellationToken);
}
