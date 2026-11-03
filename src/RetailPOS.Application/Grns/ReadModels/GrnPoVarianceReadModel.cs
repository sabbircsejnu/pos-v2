namespace RetailPOS.Application.Grns.ReadModels;

public sealed record GrnPoVarianceReadModel(
    long PurchaseOrderId,
    string PurchaseOrderStatus,
    int TotalGrns,
    int CompletedGrns,
    IReadOnlyList<GrnPoVarianceItemReadModel> Items);

public sealed record GrnPoVarianceItemReadModel(
    long PurchaseOrderItemId,
    long VariantId,
    string ProductName,
    string VariantName,
    int OrderedQuantity,
    int TotalReceivedQuantity,
    decimal UnitPrice,
    decimal TotalReceivedCost);
