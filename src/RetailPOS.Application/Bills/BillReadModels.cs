namespace RetailPOS.Application.Bills;

public sealed record BillReadModel(
    long Id,
    long SupplierId,
    string SupplierName,
    long? PurchaseOrderId,
    decimal AmountDue,
    DateTime DueDate,
    string Status,
    bool IsOverdue);

public sealed record BillSearchReadModel(
    IReadOnlyList<BillReadModel> Bills,
    int TotalCount);

public sealed record BillSummaryReadModel(
    decimal TotalUnpaid,
    decimal TotalOverdue,
    int UnpaidCount,
    int OverdueCount);
