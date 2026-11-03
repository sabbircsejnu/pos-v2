namespace RetailPOS.Application.Inventory.StockCounts;

public sealed record StockCountCreateInput(
    long BusinessId,
    long LocationId,
    string LocationType,
    DateTime StockCountDate,
    string? Remarks,
    long CreatedBy);
