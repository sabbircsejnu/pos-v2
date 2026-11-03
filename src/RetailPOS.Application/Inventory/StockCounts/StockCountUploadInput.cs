namespace RetailPOS.Application.Inventory.StockCounts;

public sealed record StockCountUploadLineUpdate(
    long LineId,
    decimal? PhysicalStock,
    decimal? Difference,
    string? Remarks);

public sealed record StockCountUploadInput(
    long StockCountId,
    long BusinessId,
    long LocationId,
    string LocationType,
    long ActorUserId,
    IReadOnlyList<StockCountUploadLineUpdate> Lines);
