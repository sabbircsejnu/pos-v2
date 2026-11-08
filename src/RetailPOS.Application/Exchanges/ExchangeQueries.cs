using MediatR;
using RetailPOS.Application.Abstractions;
using RetailPOS.Application.Exchanges.Abstractions;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Entities;

namespace RetailPOS.Application.Exchanges;

public sealed record ExchangeSourceLineReadModel(
    long SaleItemId,
    long VariantId,
    string ProductName,
    string VariantName,
    string? Sku,
    string? Barcode,
    decimal Quantity,
    decimal ReturnedQuantity,
    decimal RemainingQuantity,
    string? UnitLabel,
    decimal? EnteredQuantity,
    decimal? PaidGross,
    bool EvidenceAvailable,
    string SourceKind = ExchangeSourceKinds.SaleItem);

public sealed record ExchangeSourceInvoiceReadModel(
    long SaleId,
    string InvoiceNumber,
    string PurchaseLocalDate,
    long OutletId,
    string? OutletName,
    long? CustomerId,
    string? CustomerName,
    string? CustomerPhoneMasked,
    bool IsWalkIn,
    string Status,
    bool Eligible,
    bool WindowExpired,
    string? LastEligibleDate,
    IReadOnlyList<ExchangeSourceLineReadModel> Lines,
    string Kind = ExchangeSourceDocumentKinds.Sale);

/// <summary>What an exchange source lookup found: an original sale, or an earlier exchange whose replacements can be re-exchanged (EX-04).</summary>
public static class ExchangeSourceDocumentKinds
{
    public const string Sale = "sale";
    public const string Exchange = "exchange";
}

/// <summary>
/// D-10: exact invoice-number lookup across the business's outlets, for exchange only, with exchange-relevant fields.
/// EX-04: when no sale has the number but a completed exchange of the business does, that exchange is returned as a
/// pseudo-invoice (<c>kind = exchange</c>, <c>saleId = 0</c>) whose lines are its replacement lines; they are returned
/// with source kind <c>exchange_replacement_line</c> and the pseudo-invoice is never a source sale id.
/// </summary>
public sealed record GetExchangeSourceInvoiceQuery(string InvoiceNumber) : IRequest<ExchangeSourceInvoiceReadModel>;

public sealed class GetExchangeSourceInvoiceQueryHandler(
    IBusinessScope businessScope, IExchangeStore store, IExchangeReadStore readStore, ICompanyTimeZoneProvider timeZones)
    : IRequestHandler<GetExchangeSourceInvoiceQuery, ExchangeSourceInvoiceReadModel>
{
    public async Task<ExchangeSourceInvoiceReadModel> Handle(GetExchangeSourceInvoiceQuery request, CancellationToken cancellationToken)
    {
        var businessId = businessScope.RequireBusinessId();
        var number = request.InvoiceNumber?.Trim();
        if (string.IsNullOrEmpty(number) || number.Length > 60)
            throw ExchangeException.BadRequest(ExchangeErrorCodes.InvalidRequest, "Scan or enter the invoice number from the receipt.");

        var policy = await store.FindCurrentPolicyAsync(businessId, cancellationToken);
        if (policy is null)
            throw ExchangeException.Conflict(ExchangeErrorCodes.NotConfigured, "Product exchange has not been configured for this business yet.");

        var saleId = await store.FindSaleIdByInvoiceNumberAsync(businessId, number, cancellationToken);
        var walkInId = await store.FindWalkInCustomerIdAsync(businessId, cancellationToken);
        var zone = await timeZones.GetTimeZoneIdAsync(businessId, cancellationToken);
        if (saleId is null)
        {
            var exchange = await readStore.FindCompletedExchangeByNumberAsync(businessId, number, cancellationToken);
            if (exchange is null)
            {
                // I-12 item 3: a reversed exchange gets the same answer Preview and Confirm give for it (C-05, FR-EX-162:
                // the customer handed the replacements back), not a misleading "not found".
                if (await readStore.IsReversedExchangeNumberAsync(businessId, number, cancellationToken))
                    throw ExchangeException.Unprocessable(ExchangeErrorCodes.SourceIneligible, "That exchange was reversed, so its items cannot be exchanged again.");
                throw ExchangeException.NotFound(ExchangeErrorCodes.SourceNotFound, "No invoice with that number was found.");
            }
            return FromExchange(exchange, policy, walkInId, zone);
        }

        var sale = (await store.LoadSourceSalesAsync(businessId, [saleId.Value], forUpdate: false, cancellationToken)).Single();
        var purchase = ExchangeCalculator.LocalDate(sale.SaleDate, zone);
        var today = ExchangeCalculator.LocalDate(DateTime.UtcNow, zone);
        var statusOk = string.Equals(sale.Status, SaleStatuses.Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sale.Status, SaleStatuses.PartiallyRefunded, StringComparison.OrdinalIgnoreCase);
        var isWalkIn = sale.CustomerId is null || sale.CustomerId == walkInId || sale.Customer?.IsSystem == true;

        return new ExchangeSourceInvoiceReadModel(
            sale.Id,
            sale.SaleNumber,
            purchase.ToString("yyyy-MM-dd"),
            sale.OutletId,
            sale.Outlet?.Name,
            sale.CustomerId,
            isWalkIn ? null : sale.Customer?.Name,
            isWalkIn ? null : Mask(sale.Customer?.Phone),
            isWalkIn,
            sale.Status,
            statusOk,
            ExchangeCalculator.IsWindowExpired(purchase, policy.ExchangeWindowDays, today),
            ExchangeCalculator.LastEligibleDate(purchase, policy.ExchangeWindowDays).ToString("yyyy-MM-dd"),
            sale.Items.OrderBy(item => item.Id).Select(item => new ExchangeSourceLineReadModel(
                item.Id,
                item.VariantId,
                item.Variant?.Product?.Name ?? string.Empty,
                item.Variant?.Name ?? string.Empty,
                item.Variant?.Sku,
                item.Variant?.Barcode,
                item.Quantity,
                item.ReturnedQuantity,
                statusOk ? Math.Max(0m, item.Quantity - item.ReturnedQuantity) : 0m,
                item.UomLabel,
                item.UomEnteredQuantity,
                item.TaxableAmount is { } taxable && item.VatAmount is { } vat ? taxable + vat : null,
                item.TaxableAmount is not null && item.VatAmount is not null && item.HistoricalUnitCost is not null)).ToList());
    }

    private static ExchangeSourceInvoiceReadModel FromExchange(
        ExchangeReExchangeSource exchange, RetailPOS.Core.Entities.Exchanges.ExchangePolicyVersion policy, long? walkInId, string zone)
    {
        var anchor = DateOnly.FromDateTime(exchange.EligibilityLocalDate);
        var today = ExchangeCalculator.LocalDate(DateTime.UtcNow, zone);
        var windowDays = policy.ReplacementWindowDays ?? 0;
        var isWalkIn = exchange.CustomerId == walkInId || exchange.CustomerIsSystem;
        return new ExchangeSourceInvoiceReadModel(
            0,
            exchange.ExchangeNumber,
            anchor.ToString("yyyy-MM-dd"),
            exchange.ReceivingOutletId,
            exchange.OutletName,
            exchange.CustomerId,
            isWalkIn ? null : exchange.CustomerName,
            isWalkIn ? null : Mask(exchange.CustomerPhone),
            isWalkIn,
            exchange.Status,
            policy.AllowReplacementReExchange,
            ExchangeCalculator.IsWindowExpired(anchor, windowDays, today),
            ExchangeCalculator.LastEligibleDate(anchor, windowDays).ToString("yyyy-MM-dd"),
            exchange.Lines.Select(line => new ExchangeSourceLineReadModel(
                line.ReplacementLineId,
                line.VariantId,
                line.ProductName ?? string.Empty,
                line.VariantName ?? string.Empty,
                line.Sku,
                line.Barcode,
                line.IssuedQuantity,
                line.ReturnedQuantity,
                Math.Max(0m, line.IssuedQuantity - line.ReturnedQuantity),
                line.UnitLabel,
                line.EnteredQuantity,
                line.PaidGross,
                true,
                ExchangeSourceKinds.ReplacementLine)).ToList(),
            ExchangeSourceDocumentKinds.Exchange);
    }

    private static string? Mask(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? null : phone.Length <= 4 ? new string('*', phone.Length) : new string('*', phone.Length - 4) + phone[^4..];
}
