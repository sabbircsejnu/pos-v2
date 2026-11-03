namespace RetailPOS.Application.Suppliers;

public sealed record SupplierReadModel(
    long Id,
    string Name,
    string? Contact,
    string? Address,
    decimal CreditLimit,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SupplierSearchReadModel(
    IReadOnlyList<SupplierReadModel> Suppliers,
    int TotalCount);

public sealed record SupplierPerformanceDataReadModel(
    long Id,
    string Name,
    int TotalPurchaseOrders,
    int TotalBills,
    decimal TotalPurchaseAmount,
    decimal OutstandingBalance,
    decimal CreditLimit);

public sealed record SupplierPerformanceReadModel(
    long Id,
    string Name,
    int TotalPurchaseOrders,
    int TotalBills,
    decimal TotalPurchaseAmount,
    decimal OutstandingBalance,
    decimal CreditLimit,
    decimal CreditUtilizationPercentage,
    string HealthStatus);
