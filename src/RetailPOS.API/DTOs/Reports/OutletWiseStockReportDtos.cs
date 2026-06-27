namespace RetailPOS.API.DTOs.Reports;

/// <summary>
/// One row per outlet+product variant combination — quantity and value at that outlet.
/// </summary>
public class OutletWiseStockRowDto
{
    public long    OutletId       { get; set; }
    public string  OutletName     { get; set; } = string.Empty;
    public long    VariantId      { get; set; }
    public string  ProductCode    { get; set; } = string.Empty;
    public string  Barcode        { get; set; } = string.Empty;
    public string  Sku            { get; set; } = string.Empty;
    public string  ProductName    { get; set; } = string.Empty;
    public string  CategoryName   { get; set; } = string.Empty;
    public int     Quantity       { get; set; }
    public decimal UnitCost       { get; set; }
    public decimal StockValue     { get; set; }
}

/// <summary>Per-outlet summary row used in the breakdown panel.</summary>
public class OutletStockSummaryDto
{
    public long    OutletId    { get; set; }
    public string  OutletName  { get; set; } = string.Empty;
    public int     TotalSkus   { get; set; }
    public int     TotalQty    { get; set; }
    public decimal StockValue  { get; set; }
}

public class OutletWiseStockSummaryDto
{
    public int                         TotalOutlets   { get; set; }
    public int                         TotalSkus      { get; set; }
    public int                         TotalQuantity  { get; set; }
    public decimal                     TotalStockValue { get; set; }
    public List<OutletStockSummaryDto> ByOutlet       { get; set; } = new();
}

public class OutletWiseStockReportDto
{
    public List<OutletWiseStockRowDto> Items      { get; set; } = new();
    public int                         TotalCount { get; set; }
    public OutletWiseStockSummaryDto   Summary    { get; set; } = new();
}

public class OutletWiseStockFilterDto
{
    public long?   OutletId   { get; set; }
    public long?   CategoryId { get; set; }
    public string? Search     { get; set; }
    public string  SortBy     { get; set; } = "outletName";
    public string  SortDir    { get; set; } = "asc";
    public int     Page       { get; set; } = 1;
    public int     PageSize   { get; set; } = 50;
}
