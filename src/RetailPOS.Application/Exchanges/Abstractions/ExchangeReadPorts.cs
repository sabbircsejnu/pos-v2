using System.Text.Json;

namespace RetailPOS.Application.Exchanges.Abstractions;

public sealed record ExchangeHistoryItemReadModel(
    long Id,
    string ExchangeNumber,
    string BusinessDate,
    DateTime CompletedAtUtc,
    long ReceivingOutletId,
    string? ReceivingOutletName,
    string? CustomerName,
    IReadOnlyList<string> OriginalInvoiceNumbers,
    string ReasonCode,
    decimal NetAdjustmentGross,
    string SettlementDirection,
    decimal SettlementAmount,
    string Status,
    string? CashierName,
    string? SalespersonName);

public sealed record ExchangeHistoryPageReadModel(IReadOnlyList<ExchangeHistoryItemReadModel> Items, int TotalCount, int Page, int PageSize);

public sealed record ExchangeOriginalInvoiceReadModel(long SaleId, string InvoiceNumber, string PurchaseLocalDate);

public sealed record ExchangeReturnLineReadModel(
    long Id,
    string SourceKind,
    long? SaleItemId,
    long? SourceReplacementLineId,
    string? InvoiceNumber,
    long VariantId,
    string? ProductName,
    string? VariantName,
    string? Sku,
    decimal Quantity,
    string? UnitLabel,
    decimal CreditNet,
    decimal CreditVat,
    decimal CreditGross);

public sealed record ExchangeReplacementLineReadModel(
    long Id,
    long VariantId,
    string? ProductName,
    string? VariantName,
    string? Sku,
    decimal Quantity,
    string? UnitLabel,
    decimal UnitPrice,
    decimal LineDiscount,
    decimal InvoiceDiscountShare,
    decimal TaxableAmount,
    decimal VatAmount,
    decimal Gross,
    decimal ReturnedQuantity);

public sealed record ExchangeSettlementReadModel(
    string Direction, string Method, decimal Amount, decimal? Tendered, decimal? ChangeGiven, string? ReferenceLastFour, bool IsReversal);

public sealed record ExchangeApprovalReadModel(string Type, string? SupervisorName, string Reason, DateTime CreatedAtUtc);

public sealed record ExchangeReversalSummaryReadModel(long Id, string ReversalNumber, string BusinessDate, string Reason, string? ActorName, DateTime CompletedAtUtc);

/// <summary>Exchange detail for staff (D-11). The internal note is shown here only; it never reaches a receipt (EX-17).</summary>
public sealed record ExchangeDetailReadModel(
    long Id,
    string ExchangeNumber,
    string BusinessDate,
    string EligibilityLocalDate,
    DateTime CompletedAtUtc,
    string Status,
    long ReceivingOutletId,
    string? ReceivingOutletName,
    string? CashierName,
    string? SalespersonName,
    long CustomerId,
    string? CustomerName,
    string ReasonCode,
    string? ReasonOtherText,
    string? InternalNote,
    string PhoneVerificationResult,
    string LoyaltyStatus,
    long? JournalEntryId,
    decimal ReturnGross,
    decimal ReturnVat,
    decimal ReplacementGross,
    decimal ReplacementVat,
    decimal ReplacementDiscount,
    decimal NetAdjustmentGross,
    string SettlementDirection,
    decimal SettlementAmount,
    decimal HighValueAmount,
    IReadOnlyList<ExchangeOriginalInvoiceReadModel> OriginalInvoices,
    IReadOnlyList<ExchangeReturnLineReadModel> Returns,
    IReadOnlyList<ExchangeReplacementLineReadModel> Replacements,
    IReadOnlyList<ExchangeSettlementReadModel> Settlements,
    IReadOnlyList<ExchangeApprovalReadModel> Approvals,
    ExchangeReversalSummaryReadModel? Reversal);

/// <param name="Document">The completion's receipt snapshot exactly as stored.</param>
public sealed record ExchangeReceiptDocumentReadModel(
    JsonElement Document,
    string? BusinessName,
    string? OutletName,
    string? CashierName,
    string? SalespersonName,
    IReadOnlyDictionary<string, string> VariantNames);

/// <summary>Everything the exchange receipt PDF prints; built from the immutable snapshot.</summary>
public sealed record ExchangeReceiptPdfModel(
    string Mode,
    ExchangeReceiptDocument Document,
    string? BusinessName,
    string? OutletName,
    string? CashierName,
    string? SalespersonName,
    string? CustomerName,
    DateTime LocalCompletedAt,
    IReadOnlyDictionary<long, string> VariantNames);

public sealed record ExchangeVisibility(long Id, long ReceivingOutletId);

public sealed record ExchangeOutletMetricsReadModel(long OutletId, string? OutletName, decimal NetAdjustmentNet, decimal NetAdjustmentGross, int ExchangeCount);

public sealed record ExchangeSalespersonMetricsReadModel(
    long? SalespersonUserId, string? SalespersonName, decimal PositiveNet, decimal NegativeNet, decimal SignedNet, decimal TargetEffectNet);

/// <summary>
/// Exchange metrics by business date (EX-20, D-06, D-07). Exchanges count on their own business date whatever their
/// current status; a reversal counts on its own business date and offsets every amount of its exchange, so the money
/// totals are net of reversals dated in the range. Salesperson figures are the immutable attribution facts.
/// </summary>
public sealed record ExchangeMetricsReadModel(
    string From,
    string To,
    int ExchangeCount,
    int ReversalCount,
    decimal ReplacementGross,
    decimal ReturnGross,
    decimal NetAdjustmentGross,
    decimal ReplacementNet,
    decimal ReturnNet,
    decimal NetAdjustmentNet,
    IReadOnlyList<ExchangeOutletMetricsReadModel> ByOutlet,
    IReadOnlyList<ExchangeSalespersonMetricsReadModel> BySalesperson);

/// <summary>A completed exchange found by its number, as a source for re-exchange of its replacement lines (EX-04).</summary>
public sealed record ExchangeReExchangeSourceLine(
    long ReplacementLineId,
    long VariantId,
    string? ProductName,
    string? VariantName,
    string? Sku,
    string? Barcode,
    decimal IssuedQuantity,
    decimal ReturnedQuantity,
    string? UnitLabel,
    decimal? EnteredQuantity,
    decimal PaidGross);

public sealed record ExchangeReExchangeSource(
    long ExchangeId,
    string ExchangeNumber,
    DateTime EligibilityLocalDate,
    long ReceivingOutletId,
    string? OutletName,
    long CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    bool CustomerIsSystem,
    string Status,
    IReadOnlyList<ExchangeReExchangeSourceLine> Lines);

/// <summary>
/// Read-only exchange projections. Every method filters by the business and the caller's readable outlets (receiving
/// outlet); anything outside them is reported as absent.
/// </summary>
public interface IExchangeReadStore
{
    Task<ExchangeHistoryPageReadModel> SearchAsync(
        long businessId, IReadOnlyList<long> outletIds, string? search, DateOnly? from, DateOnly? to, string? status,
        int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The sale belongs to the business and to one of the outlets the user can read (L3).</summary>
    Task<bool> SaleVisibleAsync(long businessId, IReadOnlyList<long> outletIds, long saleId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExchangeHistoryItemReadModel>> ListBySaleAsync(long businessId, IReadOnlyList<long> outletIds, long saleId, CancellationToken cancellationToken);

    Task<ExchangeDetailReadModel?> GetDetailAsync(long businessId, IReadOnlyList<long> outletIds, long exchangeId, CancellationToken cancellationToken);

    Task<ExchangeVisibility?> FindVisibleAsync(long businessId, IReadOnlyList<long> outletIds, long exchangeId, CancellationToken cancellationToken);

    /// <summary>The completion snapshot JSON (no reversal id) and the names it is printed with.</summary>
    Task<(string Json, string? BusinessName, string? OutletName, string? CashierName, string? SalespersonName, string? CustomerName)?> GetReceiptAsync(
        long businessId, IReadOnlyList<long> outletIds, long exchangeId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<long, string>> GetVariantNamesAsync(long businessId, IReadOnlyCollection<long> variantIds, CancellationToken cancellationToken);

    Task<bool> IsOutletTerminalAsync(long outletId, long terminalId, CancellationToken cancellationToken);

    Task<ExchangeMetricsReadModel> GetMetricsAsync(
        long businessId, IReadOnlyList<long> outletIds, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>A completed exchange of the business with this exact number, or null.</summary>
    Task<ExchangeReExchangeSource?> FindCompletedExchangeByNumberAsync(long businessId, string exchangeNumber, CancellationToken cancellationToken);
    /// <summary>An exchange of the business with this number exists and has been reversed (I-12, item 3).</summary>
    Task<bool> IsReversedExchangeNumberAsync(long businessId, string exchangeNumber, CancellationToken cancellationToken);
}

public interface IExchangeReceiptPdfRenderer
{
    byte[] Render(ExchangeReceiptPdfModel model);
}
