namespace RetailPOS.API.DTOs.Reports;

public class CurrentStockRowDto
{
    public long VariantId { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public int AvailableQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int ReorderLevel { get; set; }
    public decimal UnitCost { get; set; }
    public decimal StockValue { get; set; }
    public DateTime? LastPurchaseDate { get; set; }
    public DateTime? LastSaleDate { get; set; }
    public string StockStatus { get; set; } = string.Empty; // in-stock | low-stock | out-of-stock
}

public class CurrentStockSummaryDto
{
    public int TotalProducts { get; set; }
    public int TotalQuantity { get; set; }
    public decimal TotalStockValue { get; set; }
}

public class CurrentStockReportDto
{
    public List<CurrentStockRowDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public CurrentStockSummaryDto Summary { get; set; } = new();
}

public class CurrentStockFilterDto
{
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public long? CategoryId { get; set; }
    public long? ProductId { get; set; }
    public string? StockStatus { get; set; }  // all | in-stock | low-stock | out-of-stock
    public string? Search { get; set; }
    public string SortBy { get; set; } = "productName";
    public string SortDir { get; set; } = "asc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
