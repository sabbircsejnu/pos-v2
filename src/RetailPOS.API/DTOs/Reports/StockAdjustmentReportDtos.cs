namespace RetailPOS.API.DTOs.Reports;

public class StockAdjustmentRowDto
{
    public long     Id              { get; set; }
    public DateTime AdjustmentDate  { get; set; }
    public string   ProductCode     { get; set; } = string.Empty;
    public string   Barcode         { get; set; } = string.Empty;
    public string   Sku             { get; set; } = string.Empty;
    public string   ProductName     { get; set; } = string.Empty;
    public string   CategoryName    { get; set; } = string.Empty;
    public long     LocationId      { get; set; }
    public string   LocationType    { get; set; } = string.Empty;
    public string   LocationName    { get; set; } = string.Empty;
    public string   AdjustmentType  { get; set; } = string.Empty;  // "Addition" | "Reduction"
    public int      QuantityChange  { get; set; }
    public string   Reason          { get; set; } = string.Empty;
    public string   AdjustedBy      { get; set; } = string.Empty;
}

public class StockAdjustmentSummaryDto
{
    public int TotalAdjustments  { get; set; }
    public int TotalAdditions    { get; set; }
    public int TotalReductions   { get; set; }
    public int NetQuantityChange { get; set; }
}

public class StockAdjustmentReportDto
{
    public List<StockAdjustmentRowDto> Items      { get; set; } = new();
    public int                         TotalCount { get; set; }
    public StockAdjustmentSummaryDto   Summary    { get; set; } = new();
}

public class StockAdjustmentFilterDto
{
    public long?    OutletId     { get; set; }
    public long?    WarehouseId  { get; set; }
    public long?    CategoryId   { get; set; }
    public string   Search       { get; set; } = string.Empty;
    public DateTime? DateFrom    { get; set; }
    public DateTime? DateTo      { get; set; }
    public string   SortBy       { get; set; } = "adjustmentDate";
    public string   SortDir      { get; set; } = "desc";
    public int      Page         { get; set; } = 1;
    public int      PageSize     { get; set; } = 50;
}
