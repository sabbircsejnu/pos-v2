// =====================================================================
// NEW — DTOs for the Stock Ledger module
// =====================================================================
namespace RetailPOS.API.DTOs.StockLedger;

// ---------------------------------------------------------------------------
// Response DTO
// ---------------------------------------------------------------------------
public class StockLedgerDto
{
    public long Id { get; set; }

    public long VariantId { get; set; }
    public string VariantSku { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;

    public long LocationId { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;

    /// <summary>e.g. "grn", "sale", "return", "adjustment", "transfer_in", "transfer_out"</summary>
    public string TransactionType { get; set; } = string.Empty;

    public int QtyIn { get; set; }
    public int QtyOut { get; set; }
    public int BalanceAfter { get; set; }

    /// <summary>e.g. "grn", "sale", "stock_adjustment", "stock_transfer"</summary>
    public string ReferenceType { get; set; } = string.Empty;
    public long ReferenceId { get; set; }

    public string? Remarks { get; set; }

    public long? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ---------------------------------------------------------------------------
// Paginated list wrapper
// ---------------------------------------------------------------------------
public class StockLedgerListDto
{
    public List<StockLedgerDto> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public bool HasPreviousPage { get; set; }
    public bool HasNextPage { get; set; }
}

// ---------------------------------------------------------------------------
// Search / filter DTO
// ---------------------------------------------------------------------------
public class StockLedgerSearchDto
{
    public long? VariantId { get; set; }
    public long? LocationId { get; set; }
    public string? LocationType { get; set; }

    /// <summary>Filter by transaction type: grn | sale | return | adjustment | transfer_in | transfer_out</summary>
    public string? TransactionType { get; set; }

    /// <summary>Filter by reference document type: grn | sale | stock_adjustment | stock_transfer</summary>
    public string? ReferenceType { get; set; }

    /// <summary>Filter by reference document ID (use together with ReferenceType).</summary>
    public long? ReferenceId { get; set; }

    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
