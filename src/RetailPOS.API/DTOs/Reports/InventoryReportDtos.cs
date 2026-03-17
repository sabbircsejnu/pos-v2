namespace RetailPOS.API.DTOs.Reports;

public class StockLevelDto
{
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int ReorderLevel { get; set; }
    public bool IsLowStock { get; set; }
    public decimal EstimatedValue { get; set; }
}

public class InventoryValuationDto
{
    public decimal TotalValue { get; set; }
    public int TotalItems { get; set; }
    public List<CategoryValuationDto> ByCategory { get; set; } = new();
}

public class CategoryValuationDto
{
    public string CategoryName { get; set; } = string.Empty;
    public decimal Value { get; set; }
    public int ItemCount { get; set; }
}

public class SlowMovingItemDto
{
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int CurrentStock { get; set; }
    public int DaysSinceLastSale { get; set; }
    public decimal EstimatedValue { get; set; }
}
