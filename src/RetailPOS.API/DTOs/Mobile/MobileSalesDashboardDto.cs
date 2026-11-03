namespace RetailPOS.API.DTOs.Mobile;

public sealed record MobileSalesDashboardDto(
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    decimal TotalRevenue,
    decimal NetRevenue,
    int InvoiceCount,
    long UnitCount,
    decimal AverageInvoiceValue);

public sealed record MobileSalesActivityTrendDto(
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    string TimeZoneId,
    IReadOnlyList<MobileDailySalesActivityDto> Days);

public sealed record MobileDailySalesActivityDto(
    DateOnly Date,
    int InvoiceCount);
