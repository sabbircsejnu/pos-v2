namespace RetailPOS.Application.Sales.ReadModels;

public sealed record SaleSummaryLineReadModel(
    string PaymentMethod,
    decimal TotalAmount,
    decimal Discount,
    decimal Tax);

public sealed record SaleTodaySummaryReadModel(
    int TotalSales,
    decimal TotalAmount,
    decimal TotalDiscount,
    decimal TotalTax,
    IReadOnlyDictionary<string, decimal> PaymentBreakdown);
