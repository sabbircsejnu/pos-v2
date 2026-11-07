namespace RetailPOS.Application.Sales.ReadModels;

public sealed record SaleItemReadModel(
    long Id,
    long VariantId,
    string? ProductName,
    string VariantSku,
    decimal Quantity,
    decimal UnitPrice,
    decimal? HistoricalUnitCost,
    decimal DiscountAmount,
    decimal Subtotal,
    string? AppliedRuleName,
    string? VariantName,
    decimal? VatRate = null,
    decimal? VatAmount = null,
    bool TaxInclusive = false,
    string? UnitCode = null,
    string? UnitLabel = null,
    decimal? EnteredQuantity = null,
    decimal? EnteredUnitPrice = null,
    string? BaseUnitCode = null,
    string? PackState = null,
    decimal PackagingCost = 0m,
    decimal ReturnedQuantity = 0m,
    decimal RemainingReturnableQuantity = 0m);

public sealed record SaleRefundLineReadModel(
    long SaleItemId,
    decimal Quantity,
    string Condition,
    decimal RestockedQuantity,
    bool PackagingReusable,
    decimal? NetAmount,
    decimal? VatAmount);

public sealed record SaleRefundPaymentReadModel(string Method, decimal Amount);

/// <summary>One refund of a sale. It carries no cost: this model also reaches users without <c>products.view_cost</c>.</summary>
public sealed record SaleRefundReadModel(
    long Id,
    string RefundNumber,
    int Sequence,
    DateTime CreatedAtUtc,
    string? Reason,
    long ActorUserId,
    string? ActorName,
    decimal NetAmount,
    decimal VatAmount,
    decimal TotalAmount,
    decimal CashAmount,
    IReadOnlyList<SaleRefundLineReadModel> Lines,
    IReadOnlyList<SaleRefundPaymentReadModel> Payments);

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

    /// <summary>Every refund transaction of the sale, oldest first. Refunds from before multiple refunds have no row.</summary>
    public IReadOnlyList<SaleRefundReadModel> Refunds { get; init; } = [];

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
