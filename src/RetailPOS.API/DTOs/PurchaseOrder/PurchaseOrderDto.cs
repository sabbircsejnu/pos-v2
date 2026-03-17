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
    public string? VariantAttributes { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice => Quantity * UnitPrice;
}

/// <summary>
/// DTO for Purchase Order response
/// </summary>
public class PurchaseOrderDto
{
    public long Id { get; set; }
    public long SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public long WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDelivery { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<PurchaseOrderItemDto> Items { get; set; } = new();
    public int TotalItems => Items.Count;
    public int TotalQuantity => Items.Sum(i => i.Quantity);
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
    public string Status { get; set; } = "draft"; // draft, pending, approved, received, cancelled
    public List<CreatePurchaseOrderItemDto> Items { get; set; } = new();
}

/// <summary>
/// DTO for creating a Purchase Order item
/// </summary>
public class CreatePurchaseOrderItemDto
{
    public long VariantId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}

/// <summary>
/// DTO for updating an existing Purchase Order
/// </summary>
public class UpdatePurchaseOrderDto
{
    public long SupplierId { get; set; }
    public long WarehouseId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDelivery { get; set; }
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
/// DTO for updating Purchase Order status
/// </summary>
public class UpdatePurchaseOrderStatusDto
{
    public string Status { get; set; } = string.Empty; // pending, approved, received, cancelled, rejected
    public string? Reason { get; set; } // Optional reason for rejection/cancellation
}
