namespace RetailPOS.Application.Customers;

public sealed record CustomerReadModel(
    long Id,
    string? Name,
    string? Phone,
    string? Email,
    int LoyaltyPoints,
    DateTime CreatedAt,
    int TotalPurchases);

public sealed record CustomerListReadModel(
    IReadOnlyList<CustomerReadModel> Customers,
    int TotalCount,
    int PageNumber,
    int PageSize);

public sealed record CustomerCreateValues(
    string Name,
    string? Phone,
    string? Email,
    string? PhoneNormalized = null);

public sealed record CustomerUpdateSnapshot(
    CustomerReadModel Customer,
    bool IsSystem);

public sealed record CustomerUpdateValues(
    long Id,
    string Name,
    string? Phone,
    string? Email,
    string? PhoneNormalized = null);

public sealed record CustomerDeleteSnapshot(bool IsSystem);

public sealed record CustomerLoyaltySnapshot(
    long Id,
    int LoyaltyPoints);
