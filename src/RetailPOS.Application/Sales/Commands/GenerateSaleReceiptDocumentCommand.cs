using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Application.Sales.Commands;

public sealed record GenerateSaleReceiptDocumentCommand(
    long SaleId,
    string? OutputMode,
    long? ActorUserId) : ICommand<SaleReceiptDocumentReadModel>;

public sealed class GenerateSaleReceiptDocumentCommandHandler(
    ISaleReadStore saleReadStore,
    ISaleReceiptSettingsProvider settingsProvider,
    ISaleReceiptHistoryWriter historyWriter,
    ISaleReceiptActionAuditWriter auditWriter)
    : IRequestHandler<GenerateSaleReceiptDocumentCommand, SaleReceiptDocumentReadModel>
{
    public async Task<SaleReceiptDocumentReadModel> Handle(
        GenerateSaleReceiptDocumentCommand request,
        CancellationToken cancellationToken)
    {
        var sale = await saleReadStore.FindForReceiptAsync(request.SaleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sale with ID {request.SaleId} not found");
        var settings = await settingsProvider.GetAsync(cancellationToken);
        var document = ComposeDocument(sale, settings, request.OutputMode);

        await historyWriter.RecordAsync(
            sale.Id,
            sale.TerminalId,
            request.ActorUserId,
            "generated",
            document.Footer.GeneratedAt,
            cancellationToken);

        await auditWriter.RecordBestEffortAsync(
            new SaleReceiptActionAuditRecord(
                "ReceiptGenerated",
                $"Receipt generated for {sale.SaleNumber}",
                sale.Id,
                sale.SaleNumber,
                sale.OutletId,
                sale.TerminalId,
                document.OutputMode,
                "generated"),
            cancellationToken);

        return document;
    }

    internal static SaleReceiptDocumentReadModel ComposeDocument(
        SaleReadModel sale,
        SaleReceiptSettingsReadModel settings,
        string? outputMode)
    {
        var normalizedMode = string.IsNullOrWhiteSpace(outputMode)
            ? (string.IsNullOrWhiteSpace(settings.DefaultReceiptSize)
                ? settings.PaperSize
                : settings.DefaultReceiptSize)
            : outputMode;

        normalizedMode = string.IsNullOrWhiteSpace(normalizedMode)
            ? "80mm"
            : normalizedMode.Trim().ToLowerInvariant() == "a4"
                ? "A4"
                : "80mm";

        var items = sale.Items.Select(item => new SaleReceiptLineItemReadModel(
            item.ProductName ?? item.VariantName ?? "Item",
            item.VariantName ?? item.VariantSku,
            item.Quantity,
            item.UnitPrice,
            item.DiscountAmount,
            item.Subtotal,
            item.VatRate,
            item.VatAmount)).ToArray();
        var paidAmount = sale.Payments.Count > 0
            ? sale.Payments.Sum(payment => payment.Amount)
            : sale.TotalAmount;
        var change = sale.Payments.Sum(payment => payment.Change);

        return new SaleReceiptDocumentReadModel(
            normalizedMode,
            new SaleReceiptHeaderReadModel(
                settings.ShowLogo,
                settings.LogoBase64,
                settings.CompanyName,
                sale.OutletName ?? sale.OutletId.ToString(),
                sale.OutletAddress,
                sale.OutletContactNumber ?? settings.CompanyPhone,
                settings.TaxNumber,
                settings.Header),
            new SaleReceiptSaleInfoReadModel(
                sale.SaleNumber,
                sale.SaleDate,
                sale.CashierName ?? sale.CashierId.ToString(),
                sale.TerminalName ?? sale.TerminalId?.ToString() ?? "N/A",
                sale.CustomerName ?? "Walk-in Customer",
                sale.SalespersonName,
                sale.BusinessDate),
            items,
            new SaleReceiptSummaryReadModel(
                items.Length,
                items.Sum(item => item.Quantity),
                items.Sum(item => item.LineTotal + item.Discount),
                sale.Discount + items.Sum(item => item.Discount),
                sale.Tax,
                0,
                sale.TotalAmount,
                sale.PaymentMethod,
                paidAmount,
                change),
            new SaleReceiptFooterReadModel(
                string.IsNullOrWhiteSpace(settings.ThankYouMessage)
                    ? "Thank you for your purchase!"
                    : settings.ThankYouMessage,
                settings.ReturnPolicy,
                settings.Footer,
                settings.ShowBarcode,
                settings.ShowQr,
                sale.SaleNumber,
                DateTime.UtcNow))
        {
            DateFormat = settings.DateFormat,
            Payments = sale.Payments
                .Select(payment => new SaleReceiptPaymentReadModel(payment.Method, payment.Amount, payment.Tendered, payment.Change))
                .ToArray(),
            TaxInclusive = sale.Items.Count > 0 && sale.Items.All(item => item.TaxInclusive)
        };
    }
}
