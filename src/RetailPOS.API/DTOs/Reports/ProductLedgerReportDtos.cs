namespace RetailPOS.API.DTOs.Reports;

public class ProductLedgerRowDto
{
    public long LedgerId { get; set; }
    public DateTime TransactionDate { get; set; }
    public string TransactionType { get; set; } = string.Empty;
    public string TransactionTypeLabel { get; set; } = string.Empty;
    public string ReferenceNumber { get; set; } = string.Empty;
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public long LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    /// <summary>Inventory balance before this transaction.</summary>
    public int OpeningQuantity { get; set; }
    /// <summary>Units received / added during this transaction.</summary>
    public int StockIn { get; set; }
    /// <summary>Units dispatched / removed during this transaction.</summary>
    public int StockOut { get; set; }
    /// <summary>Inventory balance after this transaction (= BalanceAfter from StockLedger).</summary>
    public int ClosingQuantity { get; set; }
    /// <summary>Unit cost at reporting time (Product.CostPrice + Variant.CostAdjustment).</summary>
    public decimal UnitCost { get; set; }
    /// <summary>UnitCost × (StockIn + StockOut).</summary>
    public decimal TransactionValue { get; set; }
    public string? PerformedBy { get; set; }
    public string? Remarks { get; set; }
}

public class ProductLedgerSummaryDto
{
    public int OpeningStock { get; set; }
    public int TotalStockIn { get; set; }
    public int TotalStockOut { get; set; }
    public int ClosingStock { get; set; }
    public decimal TotalTransactionValue { get; set; }
}

public class ProductLedgerReportDto
{
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductSku { get; set; }
    public long? VariantId { get; set; }
    public string? VariantName { get; set; }
    public List<ProductLedgerRowDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public ProductLedgerSummaryDto Summary { get; set; } = new();
}

public class ProductLedgerFilterDto
{
    public long ProductId { get; set; }
    public long? VariantId { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    /// <summary>Null/empty = all types. E.g. "grn", "sale", "adjustment", "transfer_in", "transfer_out", "return".</summary>
    public string? TransactionType { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
