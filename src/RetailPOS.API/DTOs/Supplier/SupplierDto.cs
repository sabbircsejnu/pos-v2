namespace RetailPOS.API.DTOs.Supplier;

public class SupplierDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? Address { get; set; }
    public decimal CreditLimit { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Computed properties (for performance view)
    public int TotalPurchaseOrders { get; set; }
    public int TotalBills { get; set; }
    public decimal TotalPurchaseAmount { get; set; }
    public decimal OutstandingBalance { get; set; }
}

public class CreateSupplierDto
{
    public string Name { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? Address { get; set; }
    public decimal CreditLimit { get; set; } = 0;
}

public class UpdateSupplierDto
{
    public string Name { get; set; } = string.Empty;
    public string? Contact { get; set; }
    public string? Address { get; set; }
    public decimal CreditLimit { get; set; }
}

public class SupplierPerformanceDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TotalPurchaseOrders { get; set; }
    public int TotalBills { get; set; }
    public decimal TotalPurchaseAmount { get; set; }
    public decimal OutstandingBalance { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal CreditUtilizationPercentage { get; set; }
    public string HealthStatus { get; set; } = "Good"; // Good, Warning, Critical
}

/// <summary>
/// Search request for suppliers with filters and pagination
/// </summary>
public class SupplierSearchDto
{
    public string? SearchQuery { get; set; }
    public decimal? MinCreditLimit { get; set; }
    public decimal? MaxCreditLimit { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string SortBy { get; set; } = "name";
    public string SortOrder { get; set; } = "asc";
}

/// <summary>
/// Paginated supplier list response
/// </summary>
public class SupplierListDto
{
    public List<SupplierDto> Suppliers { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
