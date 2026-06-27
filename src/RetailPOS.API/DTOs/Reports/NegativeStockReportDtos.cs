namespace RetailPOS.API.DTOs.Reports;

public class NegativeStockRowDto
{
    public long    VariantId     { get; set; }
    public string  ProductCode   { get; set; } = string.Empty;
    public string  Barcode       { get; set; } = string.Empty;
    public string  Sku           { get; set; } = string.Empty;
    public string  ProductName   { get; set; } = string.Empty;
    public string  CategoryName  { get; set; } = string.Empty;
    public long    LocationId    { get; set; }
    public string  LocationType  { get; set; } = string.Empty;
    public string  LocationName  { get; set; } = string.Empty;
    public int     CurrentStock  { get; set; }
    public decimal UnitCost      { get; set; }
    public decimal StockValue    { get; set; }
}

public class NegativeStockSummaryDto
{
    public int     TotalSkus          { get; set; }
    public int     TotalNegativeQty   { get; set; }
    public decimal TotalNegativeValue { get; set; }
}

public class NegativeStockReportDto
{
    public List<NegativeStockRowDto> Items      { get; set; } = new();
    public int                       TotalCount { get; set; }
    public NegativeStockSummaryDto   Summary    { get; set; } = new();
}

public class NegativeStockFilterDto
{
    public long?  OutletId     { get; set; }
    public long?  WarehouseId  { get; set; }
    public long?  CategoryId   { get; set; }
    public string Search       { get; set; } = string.Empty;
    public string SortBy       { get; set; } = "currentStock";
    public string SortDir      { get; set; } = "asc";
    public int    Page         { get; set; } = 1;
    public int    PageSize     { get; set; } = 50;
}
