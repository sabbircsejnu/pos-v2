using RetailPOS.Core.Entities.Exchanges;

namespace RetailPOS.Application.Exchanges;

public sealed record ExchangeReceiptLine(string Kind, long VariantId, decimal Quantity, string? UnitLabel, decimal? EnteredQuantity, decimal Net, decimal Vat, decimal Gross, string? InvoiceNumber);

public sealed record ExchangeReceiptSettlement(string Direction, string Method, decimal Amount, decimal? Tendered, decimal? ChangeGiven, string? ReferenceLastFour);

/// <summary>
/// The immutable customer-facing exchange document taken at completion (EX-12). It never carries the internal note
/// (EX-17) and never shows loyalty points while loyalty is inactive (D-05). Reprints use this snapshot unchanged.
/// </summary>
public sealed record ExchangeReceiptDocument(
    int Version,
    string ExchangeNumber,
    long ExchangeId,
    long BusinessId,
    long OutletId,
    string BusinessDate,
    DateTime CompletedAtUtc,
    long CashierUserId,
    long? SalespersonUserId,
    long CustomerId,
    IReadOnlyList<string> OriginalInvoiceNumbers,
    string ReasonCode,
    string? ReasonOtherText,
    IReadOnlyList<ExchangeReceiptLine> Returned,
    IReadOnlyList<ExchangeReceiptLine> Replacements,
    decimal ReturnGross,
    decimal ReplacementGross,
    decimal ReturnVat,
    decimal ReplacementVat,
    decimal ReplacementDiscount,
    decimal NetAdjustmentGross,
    string SettlementDirection,
    decimal SettlementAmount,
    IReadOnlyList<ExchangeReceiptSettlement> Settlements,
    string LoyaltyStatus);

public static class ExchangeReceiptComposer
{
    public static ExchangeReceiptDocument Compose(Exchange exchange, EvaluatedExchange evaluated, IReadOnlyList<ExchangeSettlement> settlements)
    {
        var invoices = evaluated.SourceSales.ToDictionary(sale => sale.Id, sale => sale.SaleNumber);
        return new ExchangeReceiptDocument(
            1,
            exchange.ExchangeNumber,
            exchange.Id,
            exchange.BusinessId,
            exchange.ReceivingOutletId,
            exchange.BusinessDate.ToString("yyyy-MM-dd"),
            exchange.CompletedAtUtc,
            exchange.CashierUserId,
            exchange.SalespersonUserId,
            exchange.CustomerId,
            exchange.SourceInvoices.Select(invoice => invoice.InvoiceNumberSnapshot).ToList(),
            exchange.ReasonCode,
            exchange.ReasonOtherText,
            exchange.ReturnLines.Select(line => new ExchangeReceiptLine(
                "returned", line.VariantId, line.ReturnedQuantity, line.UomLabel, line.UomEnteredQuantity,
                line.CreditNet, line.CreditVat, line.CreditGross,
                line.SaleId is { } saleId ? invoices.GetValueOrDefault(saleId) : null)).ToList(),
            exchange.ReplacementLines.Select(line => new ExchangeReceiptLine(
                "replacement", line.VariantId, line.IssuedQuantity, line.UomLabel, line.UomEnteredQuantity,
                line.TaxableAmount, line.VatAmount, line.TaxableAmount + line.VatAmount, null)).ToList(),
            exchange.ReturnGross,
            exchange.ReplacementGross,
            exchange.ReturnVat,
            exchange.ReplacementVat,
            exchange.ReplacementDiscount,
            exchange.NetAdjustmentGross,
            exchange.SettlementDirection,
            exchange.SettlementAmount,
            settlements.Select(settlement => new ExchangeReceiptSettlement(
                settlement.Direction, settlement.Method, settlement.Amount, settlement.Tendered, settlement.ChangeGiven,
                settlement.ExternalReference is { Length: > 4 } reference ? reference[^4..] : settlement.ExternalReference)).ToList(),
            exchange.LoyaltyStatus);
    }
}
