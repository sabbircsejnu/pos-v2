namespace RetailPOS.API.DTOs.Reports;

/// <summary>One row per product variant + location in the valuation report.</summary>
public class StockValuationRowDto
{
    public long    VariantId      { get; set; }
    public string  ProductCode    { get; set; } = string.Empty;
    public string  Barcode        { get; set; } = string.Empty;
    public string  Sku            { get; set; } = string.Empty;
    public string  ProductName    { get; set; } = string.Empty;
    public string  CategoryName   { get; set; } = string.Empty;
    public long    LocationId     { get; set; }
    public string  LocationType   { get; set; } = string.Empty;
    public string  LocationName   { get; set; } = string.Empty;
    public int     Quantity       { get; set; }
    public decimal UnitCost       { get; set; }
    public decimal InventoryValue { get; set; }

    /// <summary>Percentage of total inventory value (0-100), computed from full dataset.</summary>
    public decimal PercentOfTotal { get; set; }
}

public class StockValuationSummaryDto
{
    public int     TotalProducts      { get; set; }
    public int     TotalQuantity      { get; set; }
    public decimal TotalInventoryValue { get; set; }

    /// <summary>Per-category breakdown, sorted by value descending.</summary>
    public List<StockValuationCategoryDto> ByCategory { get; set; } = new();
}

public class StockValuationCategoryDto
{
    public string  CategoryName    { get; set; } = string.Empty;
    public int     Quantity        { get; set; }
    public decimal InventoryValue  { get; set; }
    public decimal PercentOfTotal  { get; set; }
}

public class StockValuationReportDto
{
    public List<StockValuationRowDto> Items      { get; set; } = new();
    public int                        TotalCount { get; set; }
    public StockValuationSummaryDto   Summary    { get; set; } = new();
}

public class StockValuationFilterDto
{
    public long?  OutletId    { get; set; }
    public long?  WarehouseId { get; set; }
    public long?  CategoryId  { get; set; }
    public string? Search     { get; set; }
    public string  SortBy     { get; set; } = "inventoryValue";
    public string  SortDir    { get; set; } = "desc";
    public int     Page       { get; set; } = 1;
    public int     PageSize   { get; set; } = 50;
}
