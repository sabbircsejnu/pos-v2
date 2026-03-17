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
    public string VariantName { get; set; } = string.Empty;
    public string? VariantAttributes { get; set; }
    public int OrderedQty { get; set; }
    public int ReceivedQty { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// DTO for full GRN response with items
/// </summary>
public class GrnDto
{
    public long Id { get; set; }
    public long PoId { get; set; }
    public string PoOrderNumber { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public long WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public DateTime ReceivedDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<GrnItemDto> Items { get; set; } = new();
    public int TotalItems => Items.Count;
    public int TotalReceivedQty => Items.Sum(i => i.ReceivedQty);
}

/// <summary>
/// DTO for creating a GRN item
/// </summary>
public class CreateGrnItemDto
{
    public long PoItemId { get; set; }
    public int ReceivedQty { get; set; }
}

/// <summary>
/// DTO for creating a new GRN from a PO
/// </summary>
public class CreateGrnDto
{
    public long PoId { get; set; }
    public DateTime ReceivedDate { get; set; } = DateTime.UtcNow;
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
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

/// <summary>
/// DTO for variance item (ordered vs received)
/// </summary>
public class GrnVarianceItemDto
{
    public long PoItemId { get; set; }
    public long VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public int OrderedQty { get; set; }
    public int ReceivedQty { get; set; }
    public int VarianceQty => ReceivedQty - OrderedQty;
    public bool IsFullyReceived => ReceivedQty >= OrderedQty;
}

/// <summary>
/// DTO for GRN variance report (ordered vs received)
/// </summary>
public class GrnVarianceDto
{
    public long GrnId { get; set; }
    public long PoId { get; set; }
    public string PoOrderNumber { get; set; } = string.Empty;
    public string GrnStatus { get; set; } = string.Empty;
    public List<GrnVarianceItemDto> Items { get; set; } = new();
    public bool IsFullReceipt => Items.All(i => i.IsFullyReceived);
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
    public long WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
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
    public string VariantName { get; set; } = string.Empty;
    public string? VariantAttributes { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
