using RetailPOS.Application.Expenses.Abstractions;

namespace RetailPOS.API.DTOs.Expense;

internal static class ExpenseReadModelMapper
{
    public static ExpenseCategoryDto ToDto(ExpenseCategoryReadModel readModel) => new()
    {
        Id = readModel.Id,
        Name = readModel.Name,
        ExpenseAccountId = readModel.ExpenseAccountId,
        ExpenseAccountCode = readModel.ExpenseAccountCode,
        ExpenseAccountName = readModel.ExpenseAccountName,
        IsActive = readModel.IsActive
    };

    public static ExpenseListDto ToDto(ExpenseListReadModel readModel) => new()
    {
        Expenses = readModel.Expenses.Select(ToDto).ToList(),
        TotalCount = readModel.TotalCount,
        PageNumber = readModel.PageNumber,
        PageSize = readModel.PageSize,
        TotalAmount = readModel.TotalAmount
    };

    public static ExpenseDto ToDto(ExpenseReadModel readModel) => new()
    {
        Id = readModel.Id,
        ExpenseNumber = readModel.ExpenseNumber,
        Category = readModel.Category,
        ExpenseCategoryId = readModel.ExpenseCategoryId,
        ExpenseAccountId = readModel.ExpenseAccountId,
        Amount = readModel.Amount,
        PaymentMethodId = readModel.PaymentMethodId,
        PaymentAccountId = readModel.PaymentAccountId,
        ReferenceNumber = readModel.ReferenceNumber,
        Description = readModel.Description,
        AttachmentPath = readModel.AttachmentPath,
        ExpenseDate = readModel.ExpenseDate,
        OutletId = readModel.OutletId,
        OutletName = readModel.OutletName,
        Status = readModel.Status,
        JournalEntryId = readModel.JournalEntryId,
        CreatedAt = readModel.CreatedAt,
        SubmittedAt = readModel.SubmittedAt,
        ApprovedAt = readModel.ApprovedAt,
        RejectedAt = readModel.RejectedAt,
        RejectionReason = readModel.RejectionReason,
        CancelledAt = readModel.CancelledAt,
        CancellationReason = readModel.CancellationReason,
        PaymentSource = readModel.PaymentSource,
        DisbursedAtUtc = readModel.DisbursedAtUtc,
        DisbursedCashSessionId = readModel.DisbursedCashSessionId,
        SelfApprovedUnderOwnerException = readModel.SelfApprovedUnderOwnerException,
        CreatedBy = readModel.CreatedBy,
        SubmittedBy = readModel.SubmittedBy,
        ApprovedBy = readModel.ApprovedBy
    };

    public static ExpenseSummaryDto ToDto(ExpenseSummaryReadModel readModel) => new()
    {
        TotalAmount = readModel.TotalAmount,
        ByCategory = readModel.ByCategory.ToDictionary(pair => pair.Key, pair => pair.Value),
        Count = readModel.Count
    };
}
