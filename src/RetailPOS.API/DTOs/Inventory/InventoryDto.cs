namespace RetailPOS.API.DTOs.Inventory;

/// <summary>
/// Inventory information for a product variant at a specific location
/// </summary>
public class InventoryDto
{
    public long Id { get; set; }
    public long ProductId { get; set; }
    public long ProductVariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductCode { get; set; }
    public string VariantName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Barcode { get; set; }
    public long? OutletId { get; set; }
    public string? OutletName { get; set; }
    public long? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string LocationType { get; set; } = string.Empty; // "Outlet" or "Warehouse"
    public int Quantity { get; set; }
    public int ReorderLevel { get; set; }
    public int? MaxStockLevel { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public decimal CostPrice { get; set; }
    public decimal RetailPrice { get; set; }
    public DateTime? LastRestockedAt { get; set; }
    public bool IsLowStock => Quantity <= ReorderLevel;
    public bool IsOutOfStock => Quantity == 0;
    public bool IsExpiringSoon => ExpiryDate.HasValue && ExpiryDate.Value <= DateTime.UtcNow.AddDays(30);
    public decimal TotalValue => Quantity * CostPrice;
}

/// <summary>
/// Summary of inventory by location
/// </summary>
public class InventoryByLocationDto
{
    public long? OutletId { get; set; }
    public string? OutletName { get; set; }
    public long? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string LocationType { get; set; } = string.Empty;
    public int TotalProducts { get; set; }
    public int TotalQuantity { get; set; }
    public int LowStockCount { get; set; }
    public int OutOfStockCount { get; set; }
    public decimal TotalValue { get; set; }
}

/// <summary>
/// Low stock alert information
/// </summary>
public class LowStockDto
{
    public long InventoryId { get; set; }
    public long ProductVariantId { get; set; }
    public long LocationId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string VariantName { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public int CurrentQuantity { get; set; }
    public int ReorderLevel { get; set; }
    public int ShortageQuantity => Math.Max(0, ReorderLevel - CurrentQuantity);
    public string LocationName { get; set; } = string.Empty;
    public string LocationType { get; set; } = string.Empty;
    public DateTime? LastRestockedAt { get; set; }
}

/// <summary>
/// Inventory valuation summary
/// </summary>
public class InventoryValuationDto
{
    public string LocationName { get; set; } = string.Empty;
    public string LocationType { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public int TotalQuantity { get; set; }
    public decimal TotalCostValue { get; set; }
    public decimal TotalRetailValue { get; set; }
    public decimal PotentialProfit => TotalRetailValue - TotalCostValue;
    public decimal ProfitMarginPercentage => TotalCostValue > 0 ? (PotentialProfit / TotalCostValue * 100) : 0;
    public List<CategoryValuationDto> CategoryBreakdown { get; set; } = new();
}

/// <summary>
/// Valuation by category
/// </summary>
public class CategoryValuationDto
{
    public string CategoryName { get; set; } = string.Empty;
    public int ProductCount { get; set; }
    public int TotalQuantity { get; set; }
    public decimal TotalCostValue { get; set; }
    public decimal TotalRetailValue { get; set; }
}

/// <summary>
/// Update stock threshold request
/// </summary>
public class UpdateStockThresholdDto
{
    public int ReorderLevel { get; set; }
    public int MaxStockLevel { get; set; }
}

/// <summary>
/// Inventory search/filter request
/// </summary>
public class InventorySearchDto
{
    public string? ProductSearch { get; set; }
    public long? VariantId { get; set; }
    public string? VariantSearch { get; set; }
    public long? CategoryId { get; set; }
    public long? OutletId { get; set; }
    public long? WarehouseId { get; set; }
    public bool? LowStockOnly { get; set; }
    public bool? OutOfStockOnly { get; set; }
    public bool? ExpiringSoon { get; set; }
    public int? ExpiringWithinDays { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
