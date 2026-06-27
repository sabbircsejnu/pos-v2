namespace RetailPOS.API.DTOs.Reports;

/// <summary>One aggregated row per product variant + location for the selected period.</summary>
public class StockMovementRowDto
{
    public long   VariantId    { get; set; }
    public string ProductCode  { get; set; } = string.Empty;
    public string Sku          { get; set; } = string.Empty;
    public string ProductName  { get; set; } = string.Empty;
    public string VariantName  { get; set; } = string.Empty;
    public string? VariantAttributes { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public long   LocationId   { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;

    /// <summary>BalanceAfter of the last StockLedger row strictly before DateFrom (0 if no prior rows).</summary>
    public int OpeningStock { get; set; }

    /// <summary>Sum of QtyIn during the selected period.</summary>
    public int StockIn { get; set; }

    /// <summary>Sum of QtyOut during the selected period.</summary>
    public int StockOut { get; set; }

    /// <summary>OpeningStock + StockIn - StockOut</summary>
    public int ClosingStock { get; set; }

    /// <summary>StockIn - StockOut</summary>
    public int NetMovement { get; set; }
}

public class StockMovementSummaryDto
{
    public int TotalOpeningStock { get; set; }
    public int TotalStockIn      { get; set; }
    public int TotalStockOut     { get; set; }
    public int TotalClosingStock { get; set; }
    public int NetMovement       { get; set; }
}

public class StockMovementReportDto
{
    public List<StockMovementRowDto> Items      { get; set; } = new();
    public int                       TotalCount { get; set; }
    public StockMovementSummaryDto   Summary    { get; set; } = new();
}

public class StockMovementFilterDto
{
    public long?     OutletId    { get; set; }
    public long?     WarehouseId { get; set; }
    public long?     CategoryId  { get; set; }
    public string?   Search      { get; set; }
    public DateTime? DateFrom    { get; set; }
    public DateTime? DateTo      { get; set; }
    public string    SortBy      { get; set; } = "productName";
    public string    SortDir     { get; set; } = "asc";
    public int       Page        { get; set; } = 1;
    public int       PageSize    { get; set; } = 50;
}
