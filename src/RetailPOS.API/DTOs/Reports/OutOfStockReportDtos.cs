namespace RetailPOS.API.DTOs.Reports;

public class OutOfStockRowDto
{
    public long   VariantId      { get; set; }
    public string ProductCode    { get; set; } = string.Empty;
    public string Barcode        { get; set; } = string.Empty;
    public string Sku            { get; set; } = string.Empty;
    public string ProductName    { get; set; } = string.Empty;
    public string CategoryName   { get; set; } = string.Empty;
    public long   LocationId     { get; set; }
    public string LocationType   { get; set; } = string.Empty;
    public string LocationName   { get; set; } = string.Empty;
    public int    ReorderLevel   { get; set; }
    public decimal UnitCost      { get; set; }
}

public class OutOfStockSummaryDto
{
    public int     TotalSkus         { get; set; }
    public int     TotalLocations    { get; set; }
    public decimal EstimatedCostImpact { get; set; }
}

public class OutOfStockReportDto
{
    public List<OutOfStockRowDto> Items      { get; set; } = new();
    public int                    TotalCount { get; set; }
    public OutOfStockSummaryDto   Summary    { get; set; } = new();
}

public class OutOfStockFilterDto
{
    public long?  OutletId     { get; set; }
    public long?  WarehouseId  { get; set; }
    public long?  CategoryId   { get; set; }
    public string Search       { get; set; } = string.Empty;
    public string SortBy       { get; set; } = "productName";
    public string SortDir      { get; set; } = "asc";
    public int    Page         { get; set; } = 1;
    public int    PageSize     { get; set; } = 50;
}
