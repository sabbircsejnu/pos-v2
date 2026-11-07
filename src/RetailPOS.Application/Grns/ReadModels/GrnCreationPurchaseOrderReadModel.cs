namespace RetailPOS.Application.Grns.ReadModels;

public sealed record GrnCreationPurchaseOrderReadModel(
    long Id,
    long? BusinessId,
    long SupplierId,
    long LocationId,
    string LocationType,
    string Status,
    IReadOnlyList<GrnCreationPurchaseOrderItemReadModel> Items);

public sealed record GrnCreationPurchaseOrderItemReadModel(
    long Id,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal Tax,
    bool? TaxInclusive,
    long VariantId = 0,
    RetailPOS.Core.Entities.Uom.UnitTransactionSnapshot? Snapshot = null);
