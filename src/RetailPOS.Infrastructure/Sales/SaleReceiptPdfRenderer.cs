using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Application.Common.Formatting;

namespace RetailPOS.Infrastructure.Sales;

public sealed class SaleReceiptPdfRenderer : ISaleReceiptPdfRenderer
{
    public byte[] Render(SaleReceiptDocumentReadModel document)
    {
        var isA4 = string.Equals(document.OutputMode, "A4", StringComparison.OrdinalIgnoreCase);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                if (isA4)
                {
                    page.Size(PageSizes.A4);
                    page.Margin(24);
                    page.DefaultTextStyle(style => style.FontSize(10).FontFamily("Helvetica"));
                }
                else
                {
                    page.Size(80, 200, Unit.Millimetre);
                    page.Margin(3, Unit.Millimetre);
                    page.DefaultTextStyle(style => style.FontSize(8.5f).FontFamily("Consolas"));
                }

                page.Content().Column(column =>
                {
                    column.Spacing(3);
                    column.Item().AlignCenter().Text(document.Header.BusinessName ?? "Business")
                        .SemiBold().FontSize(isA4 ? 14 : 11);
                    if (!string.IsNullOrWhiteSpace(document.Header.OutletName))
                    {
                        column.Item().AlignCenter().Text(document.Header.OutletName)
                            .FontSize(isA4 ? 10 : 8);
                    }

                    if (!string.IsNullOrWhiteSpace(document.Header.OutletAddress))
                    {
                        column.Item().AlignCenter().Text(document.Header.OutletAddress)
                            .FontSize(isA4 ? 9 : 8);
                    }

                    if (!string.IsNullOrWhiteSpace(document.Header.Phone))
                    {
                        column.Item().AlignCenter().Text($"Tel: {document.Header.Phone}")
                            .FontSize(isA4 ? 9 : 8);
                    }

                    if (!string.IsNullOrWhiteSpace(document.Header.BinOrVatNo))
                    {
                        column.Item().AlignCenter().Text($"BIN/VAT: {document.Header.BinOrVatNo}")
                            .FontSize(isA4 ? 9 : 8);
                    }

                    column.Item().PaddingTop(2).Text($"Invoice: {document.SaleInfo.InvoiceNumber}");
                    column.Item().Text($"Date: {UiDateFormatting.Format(document.SaleInfo.SalesDateTime, document.DateFormat)}");
                    if (document.SaleInfo.BusinessDate.HasValue)
                    {
                        column.Item().Text($"Business date: {UiDateFormatting.Format(document.SaleInfo.BusinessDate.Value, document.DateFormat)}");
                    }

                    column.Item().Text($"Cashier: {document.SaleInfo.Cashier}");
                    if (!string.IsNullOrWhiteSpace(document.SaleInfo.Salesperson))
                    {
                        column.Item().Text($"Salesperson: {document.SaleInfo.Salesperson}");
                    }

                    column.Item().Text($"Terminal: {document.SaleInfo.Terminal}");
                    column.Item().Text($"Customer: {document.SaleInfo.CustomerName}");
                    column.Item().PaddingVertical(2).LineHorizontal(0.6f);

                    foreach (var item in document.Items)
                    {
                        column.Item().Text(item.ProductName).SemiBold();
                        if (!string.IsNullOrWhiteSpace(item.VariantLabel))
                        {
                            column.Item().Text($"  Variant: {item.VariantLabel}")
                                .FontSize(isA4 ? 9 : 8);
                        }

                        column.Item().Row(row =>
                        {
                            row.RelativeItem().Text($"{item.Quantity} x {item.UnitPrice:0.00}");
                            row.ConstantItem(60).AlignRight().Text($"Disc {item.Discount:0.00}");
                            row.ConstantItem(70).AlignRight().Text($"{item.LineTotal:0.00}");
                        });
                    }

                    column.Item().PaddingVertical(2).LineHorizontal(0.6f);
                    column.Item().Text($"Items: {document.Summary.ItemCount}");
                    column.Item().Text($"Qty: {document.Summary.Quantity:0.##}");
                    column.Item().Text($"Subtotal: {document.Summary.Subtotal:0.00}");
                    column.Item().Text($"Discount: {document.Summary.Discount:0.00}");
                    column.Item().Text(document.TaxInclusive
                        ? $"VAT (included): {document.Summary.Tax:0.00}"
                        : $"VAT: {document.Summary.Tax:0.00}");
                    column.Item().Text($"Round Off: {document.Summary.RoundOff:0.00}");
                    column.Item().Text($"Grand Total: {document.Summary.GrandTotal:0.00}").SemiBold();
                    if (document.Payments.Count > 0)
                    {
                        foreach (var payment in document.Payments)
                        {
                            column.Item().Text(payment.Tendered.HasValue
                                ? $"{payment.Method}: {payment.Amount:0.00} (tendered {payment.Tendered.Value:0.00}, change {payment.Change:0.00})"
                                : $"{payment.Method}: {payment.Amount:0.00}");
                        }
                    }
                    else
                    {
                        column.Item().Text($"Payment: {document.Summary.PaymentMethod}");
                    }

                    column.Item().Text($"Paid: {document.Summary.PaidAmount:0.00}");
                    column.Item().Text($"Change: {document.Summary.Change:0.00}");

                    column.Item().PaddingTop(4).AlignCenter()
                        .Text(document.Footer.ThankYouMessage ?? "Thank you").SemiBold();
                    if (!string.IsNullOrWhiteSpace(document.Footer.ReturnPolicy))
                    {
                        column.Item().AlignCenter().Text(document.Footer.ReturnPolicy)
                            .FontSize(isA4 ? 8 : 7);
                    }

                    if (!string.IsNullOrWhiteSpace(document.Footer.FooterText))
                    {
                        column.Item().AlignCenter().Text(document.Footer.FooterText)
                            .FontSize(isA4 ? 8 : 7);
                    }

                    column.Item().AlignCenter()
                        .Text($"Generated: {UiDateFormatting.Format(document.Footer.GeneratedAt, document.DateFormat)}")
                        .FontSize(isA4 ? 8 : 7);
                });
            });
        }).GeneratePdf();
    }
}
