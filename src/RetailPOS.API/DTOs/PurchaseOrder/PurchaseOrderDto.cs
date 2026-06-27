namespace RetailPOS.API.DTOs.PurchaseOrder;

/// <summary>
/// DTO for Purchase Order item details
/// </summary>
public class PurchaseOrderItemDto
{
    public long Id { get; set; }
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string? ProductCode { get; set; }
    public string? Sku { get; set; }
    public string? VariantAttributes { get; set; }
    public int Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Item-level discount percentage (0–100).</summary>
    public decimal Discount { get; set; }
    /// <summary>Item-level tax percentage (0–100).</summary>
    public decimal Tax { get; set; }
    /// <summary>Line total = Quantity × UnitPrice × (1 - Discount/100) × (1 + Tax/100)</summary>
    public decimal TotalPrice =>
        Math.Round(Quantity * UnitPrice * (1 - Discount / 100m) * (1 + Tax / 100m), 2);
}

/// <summary>
/// DTO for Purchase Order response
/// </summary>
public class PurchaseOrderDto
{
    public long Id { get; set; }
    /// <summary>Human-readable reference, e.g. PO-20260607-0001.</summary>
    public string PoNumber { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public long WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDelivery { get; set; }
    public decimal TotalAmount { get; set; }
    /// <summary>draft | pending | sent_back | approved | partially_received | fully_received | completed | cancelled | rejected</summary>
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    /// <summary>Populated when PO is rejected or sent back.</summary>
    public string? RejectionReason { get; set; }
    public long? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long? LatestGrnId { get; set; }
    public string? LatestGrnNumber => LatestGrnId.HasValue ? $"GRN-{LatestGrnId.Value:D6}" : null;
    public List<PurchaseOrderItemDto> Items { get; set; } = new();
    public int TotalItems => Items.Count;
    public int TotalQuantity => Items.Sum(i => i.Quantity);
}

/// <summary>
/// DTO for creating a Purchase Order item
/// </summary>
public class CreatePurchaseOrderItemDto
{
    public long VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Discount percentage (0–100). Default: 0.</summary>
    public decimal Discount { get; set; } = 0;
    /// <summary>Tax percentage (0–100). Default: 0.</summary>
    public decimal Tax { get; set; } = 0;
    /// <summary>Unit of measurement (e.g. pcs, kg, box). Optional.</summary>
    public string? Unit { get; set; }
}

/// <summary>
/// DTO for creating a new Purchase Order
/// </summary>
public class CreatePurchaseOrderDto
{
    public long SupplierId { get; set; }
    public long WarehouseId { get; set; }
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedDelivery { get; set; }
    public string Status { get; set; } = "draft"; // draft or pending
    public string? Notes { get; set; }
    public List<CreatePurchaseOrderItemDto> Items { get; set; } = new();
}

/// <summary>
/// DTO for immediate purchase and receive flow.
/// Creates PO, auto-generates GRN, and updates stock in one transaction.
/// </summary>
public class CreatePurchaseAndReceiveDto : CreatePurchaseOrderDto
{
    /// <summary>
    /// Client-generated unique request key (UUID) used to prevent duplicate submissions.
    /// </summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Result payload for immediate purchase and receive flow.
/// </summary>
public class PurchaseAndReceiveResultDto
{
    public PurchaseOrderDto PurchaseOrder { get; set; } = new();
    public long GrnId { get; set; }
    public string GrnNumber => $"GRN-{GrnId:D6}";
    public bool IsDuplicateRequest { get; set; }
}

/// <summary>
/// DTO for updating an existing Purchase Order (only allowed when draft or sent_back)
/// </summary>
public class UpdatePurchaseOrderDto
{
    public long SupplierId { get; set; }
    public long WarehouseId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDelivery { get; set; }
    public string? Notes { get; set; }
    public List<CreatePurchaseOrderItemDto> Items { get; set; } = new();
}

/// <summary>
/// DTO for Purchase Order list with pagination
/// </summary>
public class PurchaseOrderListDto
{
    public List<PurchaseOrderDto> PurchaseOrders { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}

/// <summary>
/// DTO for Purchase Order search filters
/// </summary>
public class PurchaseOrderSearchDto
{
    public string? Status { get; set; }
    public long? SupplierId { get; set; }
    public long? WarehouseId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "order_date";
    public string SortOrder { get; set; } = "desc";
}

/// <summary>
/// DTO for updating Purchase Order status (used by reject, cancel, send-back)
/// </summary>
public class UpdatePurchaseOrderStatusDto
{
    /// <summary>Target status: rejected | cancelled | sent_back</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Reason for rejection, cancellation, or send-back.</summary>
    public string? Reason { get; set; }
}
