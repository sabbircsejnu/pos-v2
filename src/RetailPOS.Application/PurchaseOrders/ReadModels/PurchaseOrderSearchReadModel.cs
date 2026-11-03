namespace RetailPOS.Application.PurchaseOrders.ReadModels;

public sealed record PurchaseOrderSearchReadModel(
    IReadOnlyList<PurchaseOrderReadModel> PurchaseOrders,
    int TotalCount,
    int PageNumber,
    int PageSize);
