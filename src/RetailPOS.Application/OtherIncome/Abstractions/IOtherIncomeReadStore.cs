namespace RetailPOS.Application.OtherIncome.Abstractions;

public interface IOtherIncomeReadStore
{
    Task<OtherIncomeListReadModel> SearchAsync(
        OtherIncomeSearchFilter filter,
        CancellationToken cancellationToken);

    Task<OtherIncomeEntryReadModel> GetByIdAsync(
        long businessId,
        long entryId,
        CancellationToken cancellationToken);
}

public sealed record OtherIncomeSearchFilter(
    long BusinessId,
    DateTime? StartDate,
    DateTime? EndDate,
    string? Status,
    long? OutletId,
    int PageNumber,
    int PageSize);

public sealed record OtherIncomeListReadModel(
    IReadOnlyList<OtherIncomeEntryReadModel> Items,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record OtherIncomeEntryReadModel(
    long Id,
    long BusinessId,
    DateTime IncomeDate,
    string IncomeCategory,
    long? IncomeAccountId,
    decimal Amount,
    string? PaymentMethod,
    long? PaymentAccountId,
    string? ReferenceNumber,
    string? Description,
    string Status,
    long? OutletId,
    long? JournalEntryId,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? ApprovedAt,
    DateTime? RejectedAt,
    string? RejectionReason,
    DateTime? CancelledAt,
    string? CancellationReason);
