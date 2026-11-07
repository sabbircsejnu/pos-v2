using System.Globalization;
using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.BusinessAdministration;
using RetailPOS.Application.Common.Formatting;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;

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
            item.EnteredQuantity ?? item.Quantity,
            item.EnteredUnitPrice ?? item.UnitPrice,
            item.DiscountAmount,
            item.Subtotal,
            item.VatRate,
            item.VatAmount)
        {
            PrintName = BuildPrintName(item),
            DiscountPercent = DisplayDiscountPercent(item)
        }).ToArray();
        var paidAmount = sale.Payments.Count > 0
            ? sale.Payments.Sum(payment => payment.Amount)
            : sale.TotalAmount;
        var change = sale.Payments.Sum(payment => payment.Change);
        var outletPrint = sale.OutletPrint;
        var outletName = sale.OutletName ?? sale.OutletId.ToString();
        var binOrVatNo = TextOrNull(outletPrint?.BinNumber) ?? TextOrNull(settings.TaxNumber);
        var localSaleDate = ToCompanyLocalTime(sale);

        return new SaleReceiptDocumentReadModel(
            normalizedMode,
            new SaleReceiptHeaderReadModel(
                settings.ShowLogo,
                settings.LogoBase64,
                settings.CompanyName,
                outletName,
                sale.OutletAddress,
                sale.OutletContactNumber ?? settings.CompanyPhone,
                binOrVatNo,
                settings.Header)
            {
                ConcernOf = TextOrNull(settings.ConcernOf),
                BranchName = TextOrNull(outletPrint?.BranchName) ?? outletName,
                ShowroomHotline = TextOrNull(sale.OutletContactNumber),
                CentralHotline = TextOrNull(settings.CompanyPhone),
                Website = TextOrNull(settings.Website),
                TaxIdentityLine = BuildTaxIdentityLine(binOrVatNo, TextOrNull(outletPrint?.Mushak))
            },
            new SaleReceiptSaleInfoReadModel(
                sale.SaleNumber,
                sale.SaleDate,
                sale.CashierName ?? sale.CashierId.ToString(),
                sale.TerminalName ?? sale.TerminalId?.ToString() ?? "N/A",
                sale.CustomerName ?? "Walk-in Customer",
                sale.SalespersonName,
                sale.BusinessDate)
            {
                CustomerMobile = TextOrNull(sale.CustomerPhone),
                LocalSalesDateTime = localSaleDate,
                InvoiceDateText = localSaleDate.ToString("dd-MMM-yyyy hh:mm tt", CultureInfo.InvariantCulture)
            },
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
                TextOrNull(outletPrint?.ReturnExchangePolicy) ?? settings.ReturnPolicy,
                BusinessReceiptFooterRules.RemovePoweredByLines(settings.Footer),
                settings.ShowBarcode,
                settings.ShowQr,
                sale.SaleNumber,
                DateTime.UtcNow)
            {
                VatLawNotification = TextOrNull(outletPrint?.VatLawNotification),
                PoweredByText = sale.ShowPoweredByOnReceipt ? TextOrNull(sale.PoweredByText) : null
            })
        {
            DateFormat = settings.DateFormat,
            Payments = sale.Payments
                .Select(payment => new SaleReceiptPaymentReadModel(payment.Method, payment.Amount, payment.Tendered, payment.Change))
                .ToArray(),
            TaxInclusive = sale.Items.Count > 0 && sale.Items.All(item => item.TaxInclusive),
            AmountInWords = FormatAmountInWords(sale.TotalAmount, settings),
            PaymentLines = BuildPaymentLines(sale)
        };
    }

    private static string? TextOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string BuildPrintName(SaleItemReadModel item)
    {
        var name = item.ProductName ?? item.VariantName ?? "Item";
        var variant = VariantSuffix(name, item.VariantName, item.VariantSku);
        if (variant is not null)
        {
            name = $"{name} - {variant}";
        }

        if (item.UnitLabel is not null)
            name = $"{name} [{item.UnitLabel}]";
        return string.IsNullOrWhiteSpace(item.VariantSku) ? name : $"{name} ({item.VariantSku})";
    }

    /// <summary>What the variant adds to the product name; variant names usually repeat it ("Laptop - Default", "Phone - Black").</summary>
    private static string? VariantSuffix(string productName, string? variantName, string sku)
    {
        var variant = TextOrNull(variantName);
        if (variant is not null && variant.StartsWith(productName, StringComparison.OrdinalIgnoreCase))
        {
            variant = TextOrNull(variant[productName.Length..].TrimStart(' ', '-'));
        }

        return variant is null
            || string.Equals(variant, "Default", StringComparison.OrdinalIgnoreCase)
            || string.Equals(variant, sku, StringComparison.OrdinalIgnoreCase)
            ? null
            : variant;
    }

    private static decimal DisplayDiscountPercent(SaleItemReadModel item)
    {
        var gross = item.Quantity * item.UnitPrice;
        return gross > 0m ? Math.Round(item.DiscountAmount / gross * 100m, 2) : 0m;
    }

    private static string? BuildTaxIdentityLine(string? binNumber, string? mushak)
    {
        var mushakText = mushak is null
            ? null
            : mushak.StartsWith("Mushak", StringComparison.OrdinalIgnoreCase) ? mushak : $"Mushak-{mushak}";
        return (binNumber, mushakText) switch
        {
            (null, null) => null,
            (not null, null) => $"BIN: {binNumber}",
            (null, not null) => mushakText,
            _ => $"BIN: {binNumber}, {mushakText}"
        };
    }

    private static DateTime ToCompanyLocalTime(SaleReadModel sale)
    {
        var zone = CashSessionTime.FindZone(
            CashSessionTime.ResolveEffectiveTimeZoneId(sale.EffectiveTimeZoneId, BusinessPosSettings.DefaultTimeZone));
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(sale.SaleDate, DateTimeKind.Utc), zone);
    }

    private static string FormatAmountInWords(decimal amount, SaleReceiptSettingsReadModel settings)
    {
        var isTaka = string.IsNullOrWhiteSpace(settings.CurrencyCode)
            || string.Equals(settings.CurrencyCode, "BDT", StringComparison.OrdinalIgnoreCase);
        return isTaka
            ? AmountInWordsFormatter.Format(amount, "Taka", "Paisa")
            : AmountInWordsFormatter.Format(amount, TextOrNull(settings.CurrencyName) ?? settings.CurrencyCode!.Trim(), "Cents");
    }

    /// <summary>Only recorded amounts are used; a sale without payment rows is attributed to its primary method.</summary>
    private static IReadOnlyList<SaleReceiptPaymentLineReadModel> BuildPaymentLines(SaleReadModel sale)
    {
        var totals = sale.Payments.Count > 0
            ? sale.Payments
                .GroupBy(payment => payment.Method, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Sum(payment => payment.Amount), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { [sale.PaymentMethod] = sale.TotalAmount };

        var lines = new List<SaleReceiptPaymentLineReadModel>
        {
            new("Cash Amount", totals.GetValueOrDefault("cash")),
            new("Card Amount", totals.GetValueOrDefault("card")),
            new("Mobile Banking Amount", totals.GetValueOrDefault("mobile"))
        };
        lines.AddRange(totals
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key)
                && !string.Equals(entry.Key, "cash", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(entry.Key, "card", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(entry.Key, "mobile", StringComparison.OrdinalIgnoreCase))
            .Select(entry => new SaleReceiptPaymentLineReadModel(
                $"{char.ToUpperInvariant(entry.Key[0])}{entry.Key[1..]} Amount",
                entry.Value)));
        return lines.Where(line => line.Amount > 0m).ToList();
    }
}
