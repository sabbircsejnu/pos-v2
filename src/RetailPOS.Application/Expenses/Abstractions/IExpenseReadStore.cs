namespace RetailPOS.Application.Expenses.Abstractions;

public interface IExpenseReadStore
{
    Task<IReadOnlyList<ExpenseCategoryReadModel>> GetCategoriesAsync(
        long businessId,
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<ExpenseListReadModel> SearchAsync(
        ExpenseSearchFilter filter,
        CancellationToken cancellationToken);

    Task<ExpenseReadModel> GetByIdAsync(
        long businessId,
        long expenseId,
        CancellationToken cancellationToken);

    Task<ExpenseSummaryReadModel> GetCurrentMonthSummaryAsync(
        long businessId,
        CancellationToken cancellationToken);
}

public sealed record ExpenseCategoryReadModel(
    long Id,
    string Name,
    long ExpenseAccountId,
    string? ExpenseAccountCode,
    string? ExpenseAccountName,
    bool IsActive);

public sealed record ExpenseSearchFilter(
    long BusinessId,
    string? Category,
    string? Status,
    long? OutletId,
    DateTime? StartDate,
    DateTime? EndDate,
    int PageNumber,
    int PageSize);

public sealed record ExpenseReadModel(
    long Id,
    string? ExpenseNumber,
    string Category,
    long? ExpenseCategoryId,
    long? ExpenseAccountId,
    decimal Amount,
    string? PaymentMethodId,
    long? PaymentAccountId,
    string? ReferenceNumber,
    string? Description,
    string? AttachmentPath,
    DateTime ExpenseDate,
    long? OutletId,
    string? OutletName,
    string Status,
    long? JournalEntryId,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? ApprovedAt,
    DateTime? RejectedAt,
    string? RejectionReason,
    DateTime? CancelledAt,
    string? CancellationReason,
    string? PaymentSource = null,
    DateTime? DisbursedAtUtc = null,
    long? DisbursedCashSessionId = null,
    bool SelfApprovedUnderOwnerException = false,
    long? CreatedBy = null,
    long? SubmittedBy = null,
    long? ApprovedBy = null);

public sealed record ExpenseListReadModel(
    IReadOnlyList<ExpenseReadModel> Expenses,
    int TotalCount,
    int PageNumber,
    int PageSize,
    decimal TotalAmount);

public sealed record ExpenseSummaryReadModel(
    decimal TotalAmount,
    IReadOnlyDictionary<string, decimal> ByCategory,
    int Count);
