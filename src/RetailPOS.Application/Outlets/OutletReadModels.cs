namespace RetailPOS.Application.Outlets;

public sealed record OutletReadModel(
    long Id,
    string Name,
    string Address,
    string? ContactNumber,
    long? ManagerId,
    string? ManagerName,
    int UserCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? InvoicePrefix = null,
    bool InvoicePrefixLocked = false,
    string? TimeZoneId = null,
    IReadOnlyList<OutletTerminalReadModel>? Terminals = null);

public sealed record OutletStatsReadModel(
    long Id,
    string Name,
    int UserCount,
    int SalesCount,
    decimal TotalSales);
