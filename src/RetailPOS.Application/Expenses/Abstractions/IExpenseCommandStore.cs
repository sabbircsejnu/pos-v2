namespace RetailPOS.Application.Expenses.Abstractions;

public interface IExpenseCommandStore
{
    Task<ExpenseCategoryCommandResult> CreateCategoryAsync(
        long businessId,
        long actorUserId,
        ExpenseCategoryCommandInput input,
        CancellationToken cancellationToken);

    Task<ExpenseCategoryCommandResult> UpdateCategoryAsync(
        long businessId,
        long actorUserId,
        long categoryId,
        ExpenseCategoryCommandInput input,
        CancellationToken cancellationToken);

    Task<long> CreateAsync(
        long businessId,
        long actorUserId,
        ExpenseCreationInput input,
        CancellationToken cancellationToken);

    Task<long> UpdateAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        ExpenseUpdateInput input,
        CancellationToken cancellationToken);

    Task<long> SubmitAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        CancellationToken cancellationToken);

    Task<long> ApproveAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        long? expenseAccountId,
        CancellationToken cancellationToken);

    Task<long> RejectAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        string reason,
        CancellationToken cancellationToken);

    Task<long> CancelAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        string reason,
        CancellationToken cancellationToken);

    Task<long> DeleteAsync(
        long businessId,
        long actorUserId,
        long expenseId,
        CancellationToken cancellationToken);
}

public sealed record ExpenseCategoryCommandInput(
    string Name,
    long ExpenseAccountId,
    bool IsActive);

public sealed record ExpenseCategoryCommandResult(
    long Id,
    string Name,
    long ExpenseAccountId,
    string ExpenseAccountCode,
    string ExpenseAccountName,
    bool IsActive);

public interface IExpenseApprovalJournalPoster
{
    Task<long> CreateOrGetAndPostAsync(
        long businessId,
        long actorUserId,
        ExpenseApprovalJournalInput journal,
        CancellationToken cancellationToken);
}

public interface IExpensePostingAccountResolver
{
    Task<long> ResolveAccountIdAsync(
        long businessId,
        string accountSlot,
        long? outletId,
        CancellationToken cancellationToken);
}

public sealed record ExpenseApprovalJournalInput(
    DateTime TransactionDate,
    DateTime PostingDate,
    DateTime BusinessDate,
    string ReferenceType,
    string ReferenceId,
    string? ReferenceNumber,
    string Description,
    long? OutletId,
    IReadOnlyList<ExpenseApprovalJournalLineInput> Lines);

public sealed record ExpenseApprovalJournalLineInput(
    long AccountId,
    string? Description,
    decimal DebitAmount,
    decimal CreditAmount,
    long? OutletId);

public sealed record ExpenseCreationInput(
    long ExpenseCategoryId,
    decimal Amount,
    decimal? VatRate,
    decimal? VatAmount,
    string? TaxCode,
    bool? TaxInclusive,
    string? PaymentMethodId,
    long PaymentAccountId,
    string? ReferenceNumber,
    string? Description,
    string? AttachmentPath,
    DateTime ExpenseDate,
    long? OutletId,
    string? PaymentSource = null);

public sealed record ExpenseUpdateInput(
    long ExpenseCategoryId,
    decimal Amount,
    decimal? VatRate,
    decimal? VatAmount,
    string? TaxCode,
    bool? TaxInclusive,
    string? PaymentMethodId,
    long PaymentAccountId,
    string? ReferenceNumber,
    string? Description,
    string? AttachmentPath,
    DateTime ExpenseDate,
    long? OutletId,
    string? PaymentSource = null);
