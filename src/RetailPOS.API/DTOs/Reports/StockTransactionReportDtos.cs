namespace RetailPOS.API.DTOs.Reports;

public class StockTransactionReportRowDto
{
    public long Id { get; set; }
    public DateTime TransactionDate { get; set; }
    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = string.Empty;
    public long ReferenceId { get; set; }
    public string ReferenceNo { get; set; } = string.Empty;

    public long VariantId { get; set; }
    public string? VariantCode { get; set; }
    public string? VariantAttributes { get; set; }

    public int QuantityIn { get; set; }
    public int QuantityOut { get; set; }
    public int RunningBalance { get; set; }
    public string? Remarks { get; set; }
    public string? CreatedByName { get; set; }
}

public class StockTransactionReportSummaryDto
{
    public int OpeningStock { get; set; }
    public int StockIn { get; set; }
    public int StockOut { get; set; }
    public int ClosingStock { get; set; }
    public int CurrentStock { get; set; }
}

public class StockTransactionReportDto
{
    public long ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductCode { get; set; }

    /// <summary>"product" when no variant filter applied, "variant" when scoped to a single variant.</summary>
    public string Level { get; set; } = "product";

    public long? VariantId { get; set; }
    public string? VariantCode { get; set; }
    public string? VariantName { get; set; }
    public string? VariantAttributes { get; set; }

    public string Sku { get; set; } = string.Empty;

    public long? OutletId { get; set; }
    public string? LocationType { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }

    public StockTransactionReportSummaryDto Summary { get; set; } = new();
    public List<StockTransactionReportRowDto> Rows { get; set; } = new();
}
