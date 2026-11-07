namespace RetailPOS.API.DTOs.Grn;

/// <summary>
/// DTO for GRN item details
/// </summary>
public class GrnItemDto
{
    public long Id { get; set; }
    public long PoItemId { get; set; }
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductCode { get; set; }
    public string VariantName { get; set; } = string.Empty;
    public string? VariantAttributes { get; set; }
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }              // NEW — actual received cost
    public string? Notes { get; set; }                 // NEW — per-line notes
    public decimal TotalCost => UnitCost * ReceivedQty; // NEW — computed line total cost
    public decimal DamagedQty { get; set; }
    public decimal RejectedQty { get; set; }
    public int? AcceptedSealedPacks { get; set; }
    public decimal? AcceptedLooseQty { get; set; }
    public string? UomUnitCode { get; set; }
    public string? UomLabel { get; set; }
    public string? UomBaseUnitCode { get; set; }
}

/// <summary>
/// DTO for full GRN response with items
/// </summary>
public class GrnDto
{
    public long Id { get; set; }
    public string GrnNumber => $"GRN-{Id:D6}";          // NEW — human-readable reference
    public long PoId { get; set; }
    public string PoOrderNumber { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    /// <summary>Destination warehouse; null when goods are received into an outlet.</summary>
    public long? WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    /// <summary>Destination outlet; null when goods are received into a warehouse.</summary>
    public long? OutletId { get; set; }
    public string? OutletName { get; set; }
    /// <summary>outlet | warehouse</summary>
    public string LocationType { get; set; } = "warehouse";
    public long LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public DateTime ReceivedDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }                  // NEW — receipt-level notes
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime? SupplierInvoiceDate { get; set; }
    public long? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<GrnItemDto> Items { get; set; } = new();
    public int TotalItems => Items.Count;
    public decimal TotalReceivedQty => Items.Sum(i => i.ReceivedQty);
    public decimal TotalReceivedCost => Items.Sum(i => i.TotalCost); // NEW
}

/// <summary>
/// DTO for creating a GRN item
/// </summary>
public class CreateGrnItemDto
{
    public long PoItemId { get; set; }
    public decimal ReceivedQty { get; set; }
    /// <summary>Actual unit cost — if omitted, the PO unit price is used.</summary>
    public decimal? UnitCost { get; set; }              // NEW — optional override
    public string? Notes { get; set; }                  // NEW — per-line notes
    /// <summary>Physically received but not accepted. Not stocked and not payable.</summary>
    public decimal DamagedQty { get; set; }
    public decimal RejectedQty { get; set; }
    public int? AcceptedSealedPacks { get; set; }
    public decimal? AcceptedLooseQty { get; set; }
}

/// <summary>
/// DTO for creating a new GRN from a PO
/// </summary>
public class CreateGrnDto
{
    public long PoId { get; set; }
    public DateTime ReceivedDate { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }                  // NEW — receipt-level notes
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime? SupplierInvoiceDate { get; set; }
    public List<CreateGrnItemDto> Items { get; set; } = new();
}

/// <summary>
/// DTO for paginated GRN list
/// </summary>
public class GrnListDto
{
    public List<GrnDto> Grns { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}

/// <summary>
/// DTO for GRN search/filter parameters
/// </summary>
public class GrnSearchDto
{
    public long? PoId { get; set; }
    public string? Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    /// <summary>outlet | warehouse</summary>
    public string? LocationType { get; set; }
    public long? LocationId { get; set; }
    /// <summary>Inclusive date in the Company Settings time zone (yyyy-MM-dd).</summary>
    public DateOnly? FromDate { get; set; }
    /// <summary>Inclusive date in the Company Settings time zone (yyyy-MM-dd).</summary>
    public DateOnly? ToDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>
/// DTO for variance item (ordered vs received in this specific GRN).
/// For the cumulative view across all GRNs on a PO, see <see cref="GrnPoVarianceItemDto"/>.
/// </summary>
public class GrnVarianceItemDto
{
    public long PoItemId { get; set; }
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public decimal OrderedQty { get; set; }
    public decimal ReceivedQty { get; set; }
    public decimal VarianceQty => ReceivedQty - OrderedQty;
    public bool IsFullyReceived => ReceivedQty >= OrderedQty;
}

/// <summary>
/// DTO for GRN variance report (ordered vs received in this specific GRN).
/// </summary>
public class GrnVarianceDto
{
    public long GrnId { get; set; }
    public string GrnNumber => $"GRN-{GrnId:D6}";      // NEW
    public long PoId { get; set; }
    public string PoOrderNumber { get; set; } = string.Empty;
    public string GrnStatus { get; set; } = string.Empty;
    public List<GrnVarianceItemDto> Items { get; set; } = new();
    public bool IsFullReceipt => Items.All(i => i.IsFullyReceived);
}

// ─── NEW: Cumulative PO-level variance (across ALL GRNs) ──────────────────────

/// <summary>
/// Per-item cumulative variance across all GRNs on a PO.
/// </summary>
public class GrnPoVarianceItemDto
{
    public long PoItemId { get; set; }
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public decimal OrderedQty { get; set; }
    /// <summary>Total received across ALL completed GRNs on this PO.</summary>
    public decimal TotalReceivedQty { get; set; }
    /// <summary>Remaining quantity still to be received.</summary>
    public decimal RemainingQty => Math.Max(0, OrderedQty - TotalReceivedQty);
    public decimal VarianceQty => TotalReceivedQty - OrderedQty;
    public bool IsFullyReceived => TotalReceivedQty >= OrderedQty;
    public decimal UnitPrice { get; set; }
    public decimal TotalOrderedCost => UnitPrice * OrderedQty;
    public decimal TotalReceivedCost { get; set; }
}

/// <summary>
/// Cumulative PO-level variance report spanning all GRNs.
/// </summary>
public class GrnPoVarianceDto
{
    public long PoId { get; set; }
    public string PoOrderNumber { get; set; } = string.Empty;
    public string PoStatus { get; set; } = string.Empty;
    public int TotalGrns { get; set; }
    public int CompletedGrns { get; set; }
    public List<GrnPoVarianceItemDto> Items { get; set; } = new();
    public bool IsFullyReceived => Items.All(i => i.IsFullyReceived);
    public decimal TotalOrderedQty => Items.Sum(i => i.OrderedQty);
    public decimal TotalReceivedQty => Items.Sum(i => i.TotalReceivedQty);
    public decimal TotalRemainingQty => Items.Sum(i => i.RemainingQty);
}

/// <summary>
/// Simplified PO DTO for GRN creation (POs awaiting receipt)
/// </summary>
public class PurchaseOrderForGrnDto
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    /// <summary>Destination warehouse; null when goods are received into an outlet.</summary>
    public long? WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    /// <summary>Destination outlet; null when goods are received into a warehouse.</summary>
    public long? OutletId { get; set; }
    public string? OutletName { get; set; }
    /// <summary>outlet | warehouse</summary>
    public string LocationType { get; set; } = "warehouse";
    public long LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<PurchaseOrderItemForGrnDto> Items { get; set; } = new();
}

/// <summary>
/// Simplified PO item DTO for GRN creation
/// </summary>
public class PurchaseOrderItemForGrnDto
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductCode { get; set; }
    public string VariantName { get; set; } = string.Empty;
    public string? VariantAttributes { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? PrimaryImageThumb { get; set; }
}
