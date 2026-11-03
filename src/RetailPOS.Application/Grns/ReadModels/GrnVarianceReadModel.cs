namespace RetailPOS.Application.Grns.ReadModels;

public sealed record GrnVarianceReadModel(
    long GrnId,
    long PurchaseOrderId,
    string Status,
    IReadOnlyList<GrnVarianceItemReadModel> Items);

public sealed record GrnVarianceItemReadModel(
    long PurchaseOrderItemId,
    long VariantId,
    string ProductName,
    string VariantName,
    int OrderedQuantity,
    int ReceivedQuantity);
