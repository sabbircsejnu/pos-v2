namespace RetailPOS.Application.SalesReports;

public sealed record SalesReportReadScope(IReadOnlyList<long> OutletIds);

public sealed record SalesReportFilter(
    DateTime StartDate,
    DateTime EndDate,
    long? OutletId,
    string? PaymentMethod,
    int Limit = 10,
    long? CashierId = null,
    string? TimeZoneId = null);

public sealed record SalesReportSummaryReadModel(
    decimal TotalRevenue,
    decimal TotalDiscount,
    decimal TotalTax,
    decimal NetRevenue,
    int TotalTransactions,
    decimal AverageOrderValue);

public sealed record TopProductReadModel(
    long VariantId,
    string ProductName,
    string Sku,
    int TotalQuantity,
    decimal TotalRevenue);

public sealed record SalesByOutletReadModel(
    long OutletId,
    string OutletName,
    int TransactionCount,
    decimal TotalRevenue);

public sealed record SalesByPaymentMethodReadModel(
    string PaymentMethod,
    int Count,
    decimal Amount);

public sealed record DailySalesTrendReadModel(
    DateTime Date,
    int TransactionCount,
    decimal Revenue);

public sealed record MobileSalesDashboardReadModel(
    DateTime StartDateUtc,
    DateTime EndDateUtc,
    decimal TotalRevenue,
    decimal NetRevenue,
    int InvoiceCount,
    long UnitCount,
    decimal AverageInvoiceValue);

public sealed record MobileDailySalesActivityReadModel(
    DateTime DateUtc,
    int InvoiceCount);
