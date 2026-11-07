namespace RetailPOS.Application.Pos.ReadModels;

public sealed record PosStockHintReadModel(
    long VariantId,
    long OutletId,
    decimal AvailableQty,
    bool Sufficient,
    bool FromCache);
