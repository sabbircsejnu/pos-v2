namespace RetailPOS.Application.PurchaseOrders.ReadModels;

public sealed record PurchaseSummaryReadModel(
    int TotalOrders,
    decimal TotalAmount,
    int PendingApprovals,
    int ReceivedOrders);

public sealed record PurchaseBySupplierReadModel(
    long SupplierId,
    string SupplierName,
    int OrderCount,
    decimal TotalAmount);
