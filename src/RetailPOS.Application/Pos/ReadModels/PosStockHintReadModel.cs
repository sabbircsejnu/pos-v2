namespace RetailPOS.Application.Pos.ReadModels;

public sealed record PosStockHintReadModel(
    long VariantId,
    long OutletId,
    int AvailableQty,
    bool Sufficient,
    bool FromCache);
