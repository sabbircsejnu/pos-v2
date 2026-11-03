namespace RetailPOS.Application.OtherIncome.Abstractions;

public interface IOtherIncomeCommandStore
{
    Task<long> CreateAsync(
        long businessId,
        long actorUserId,
        OtherIncomeCreationInput input,
        CancellationToken cancellationToken);

    Task<long> UpdateAsync(
        long businessId,
        long actorUserId,
        long entryId,
        OtherIncomeCreationInput input,
        CancellationToken cancellationToken);

    Task<long> SubmitAsync(
        long businessId,
        long actorUserId,
        long entryId,
        CancellationToken cancellationToken);

    Task<long> ApproveAsync(
        long businessId,
        long actorUserId,
        long entryId,
        long? incomeAccountId,
        CancellationToken cancellationToken);

    Task<long> RejectAsync(
        long businessId,
        long actorUserId,
        long entryId,
        string reason,
        CancellationToken cancellationToken);

    Task<long> CancelAsync(
        long businessId,
        long actorUserId,
        long entryId,
        string reason,
        CancellationToken cancellationToken);
}

public sealed record OtherIncomeCreationInput(
    DateTime IncomeDate,
    string IncomeCategory,
    long? IncomeAccountId,
    decimal Amount,
    decimal? VatRate,
    decimal? VatAmount,
    string? TaxCode,
    bool? TaxInclusive,
    string? PaymentMethod,
    long? PaymentAccountId,
    string? ReferenceNumber,
    string? Description,
    long? OutletId);

public interface IOtherIncomeApprovalJournalPoster
{
    Task<long> CreateOrGetAndPostAsync(
        long businessId,
        long actorUserId,
        OtherIncomeApprovalJournalInput journal,
        CancellationToken cancellationToken);
}

public interface IOtherIncomePostingAccountResolver
{
    Task<long> ResolveAccountIdAsync(
        long businessId,
        string accountSlot,
        long? outletId,
        CancellationToken cancellationToken);
}

public sealed record OtherIncomeApprovalJournalInput(
    DateTime TransactionDate,
    DateTime PostingDate,
    DateTime BusinessDate,
    string ReferenceType,
    string ReferenceId,
    string? ReferenceNumber,
    string Description,
    long? OutletId,
    IReadOnlyList<OtherIncomeApprovalJournalLineInput> Lines);

public sealed record OtherIncomeApprovalJournalLineInput(
    long AccountId,
    string? Description,
    decimal DebitAmount,
    decimal CreditAmount,
    long? OutletId);
