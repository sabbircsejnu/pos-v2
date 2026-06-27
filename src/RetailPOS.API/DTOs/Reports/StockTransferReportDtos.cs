namespace RetailPOS.API.DTOs.Reports;

public class StockTransferRowDto
{
    public long     TransferId       { get; set; }
    public DateTime TransferDate     { get; set; }
    public string   FromLocationName { get; set; } = string.Empty;
    public string   FromLocationType { get; set; } = string.Empty;
    public string   ToLocationName   { get; set; } = string.Empty;
    public string   ToLocationType   { get; set; } = string.Empty;
    public string   Status           { get; set; } = string.Empty;
    public string   ProductCode      { get; set; } = string.Empty;
    public string   Barcode          { get; set; } = string.Empty;
    public string   Sku              { get; set; } = string.Empty;
    public string   ProductName      { get; set; } = string.Empty;
    public string   CategoryName     { get; set; } = string.Empty;
    public int      Quantity         { get; set; }
    public decimal  UnitCost         { get; set; }
    public decimal  TransferValue    { get; set; }
    public string   CreatedBy        { get; set; } = string.Empty;
}

public class StockTransferSummaryDto
{
    public int     TotalTransfers { get; set; }
    public int     TotalLines     { get; set; }
    public int     TotalQuantity  { get; set; }
    public decimal TotalValue     { get; set; }
    public int     PendingCount   { get; set; }
    public int     CompletedCount { get; set; }
}

public class StockTransferReportDto
{
    public List<StockTransferRowDto> Items      { get; set; } = new();
    public int                       TotalCount { get; set; }
    public StockTransferSummaryDto   Summary    { get; set; } = new();
}

public class StockTransferFilterDto
{
    public long?    FromOutletId  { get; set; }
    public long?    ToOutletId    { get; set; }
    public long?    CategoryId    { get; set; }
    public string   Status        { get; set; } = string.Empty;  // "", "pending", "completed"
    public string   Search        { get; set; } = string.Empty;
    public DateTime? DateFrom     { get; set; }
    public DateTime? DateTo       { get; set; }
    public string   SortBy        { get; set; } = "transferDate";
    public string   SortDir       { get; set; } = "desc";
    public int      Page          { get; set; } = 1;
    public int      PageSize      { get; set; } = 50;
}
