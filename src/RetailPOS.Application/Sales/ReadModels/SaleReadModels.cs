namespace RetailPOS.Application.Sales.ReadModels;

public sealed record SaleItemReadModel(
    long Id,
    long VariantId,
    string? ProductName,
    string VariantSku,
    int Quantity,
    decimal UnitPrice,
    decimal? HistoricalUnitCost,
    decimal DiscountAmount,
    decimal Subtotal,
    string? AppliedRuleName,
    string? VariantName,
    decimal? VatRate = null,
    decimal? VatAmount = null,
    bool TaxInclusive = false);

public sealed record SalePaymentReadModel(
    long Id,
    string Method,
    decimal Amount,
    decimal? Tendered,
    decimal Change);

public sealed record SaleReadModel(
    long Id,
    string SaleNumber,
    long OutletId,
    string? OutletName,
    string? OutletAddress,
    string? OutletContactNumber,
    long? TerminalId,
    string? TerminalName,
    long? CashSessionId,
    DateTime? BusinessDate,
    DateTime? SaleDateTime,
    long? CustomerId,
    string? CustomerName,
    long CashierId,
    string? CashierName,
    DateTime SaleDate,
    decimal TotalAmount,
    decimal Discount,
    decimal Tax,
    decimal NetTotal,
    string PaymentMethod,
    string Status,
    DateTime CreatedAt,
    IReadOnlyList<SaleItemReadModel> Items,
    IReadOnlyList<SalePaymentReadModel> Payments,
    long? SalespersonUserId = null,
    string? SalespersonName = null)
{
    public string? CustomerPhone { get; init; }

    /// <summary>The outlet's invoice print settings; only populated for receipt reads.</summary>
    public SaleOutletPrintReadModel? OutletPrint { get; init; }

    /// <summary>The outlet override or Business default (D9); only populated for receipt reads.</summary>
    public string? EffectiveTimeZoneId { get; init; }

    /// <summary>The Business's platform-controlled footer settings; only populated for receipt reads.</summary>
    public bool ShowPoweredByOnReceipt { get; init; }
    public string? PoweredByText { get; init; }
}

public sealed record SaleOutletPrintReadModel(
    string? BranchName,
    string? BinNumber,
    string? Mushak,
    string? ReturnExchangePolicy,
    string? VatLawNotification);

public sealed record SaleSearchCriteria(
    long? OutletId,
    long? CashierId,
    long? CustomerId,
    DateTime? StartDate,
    DateTime? EndDate,
    string? Status,
    int PageNumber,
    int PageSize);

public sealed record SaleSearchReadModel(
    IReadOnlyList<SaleReadModel> Sales,
    int TotalCount,
    int PageNumber,
    int PageSize);
