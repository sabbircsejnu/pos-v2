namespace RetailPOS.API.DTOs.Warehouse;

public class WarehouseDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public long? ManagerId { get; set; }
    public string? ManagerName { get; set; }
    public int PurchaseOrderCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateWarehouseDto
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public long? ManagerId { get; set; }
}

public class UpdateWarehouseDto
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public long? ManagerId { get; set; }
}

public class WarehouseStatsDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public int PurchaseOrderCount { get; set; }
    public int CurrentStock { get; set; }
    public decimal CapacityUtilization { get; set; }
}
