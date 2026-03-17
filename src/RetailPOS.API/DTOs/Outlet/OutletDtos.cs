namespace RetailPOS.API.DTOs.Outlet;

public class OutletDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? ContactNumber { get; set; }
    public long? ManagerId { get; set; }
    public string? ManagerName { get; set; }
    public int UserCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateOutletDto
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? ContactNumber { get; set; }
    public long? ManagerId { get; set; }
}

public class UpdateOutletDto
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? ContactNumber { get; set; }
    public long? ManagerId { get; set; }
}

public class OutletStatsDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int UserCount { get; set; }
    public int SalesCount { get; set; }
    public decimal TotalSales { get; set; }
}
