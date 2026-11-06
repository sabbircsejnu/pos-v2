using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Application.Common.Formatting;

namespace RetailPOS.Infrastructure.Sales;

public sealed class SaleReceiptPdfRenderer : ISaleReceiptPdfRenderer
{
    private const string QuantityColor = "#00008B";

    public byte[] Render(SaleReceiptDocumentReadModel document) =>
        string.Equals(document.OutputMode, "A4", StringComparison.OrdinalIgnoreCase)
            ? RenderA4(document)
            : RenderThermal(document);

    /// <summary>The 80 mm thermal invoice; its height follows the content, like a receipt roll.</summary>
    private static byte[] RenderThermal(SaleReceiptDocumentReadModel document) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.ContinuousSize(80, Unit.Millimetre);
                page.Margin(4, Unit.Millimetre);
                page.DefaultTextStyle(style => style.FontSize(8.25f).FontFamily("Arial").LineHeight(1.1f));

                page.Content().Column(column =>
                {
                    column.Spacing(1.5f);
                    ComposeThermalHeader(column, document);
                    column.Item().PaddingTop(5).AlignCenter().Text("Invoice").Bold().FontSize(9.5f);
                    Separator(column);
                    ComposeThermalMeta(column, document);
                    Separator(column);
                    ComposeThermalItems(column, document);
                    Separator(column);
                    ComposeThermalTotals(column, document);
                    ComposeThermalFooter(column, document);
                });
            });
        }).GeneratePdf();

    private static void ComposeThermalHeader(ColumnDescriptor column, SaleReceiptDocumentReadModel document)
    {
        var header = document.Header;
        column.Item().AlignCenter().Text(header.BusinessName ?? "Business").Bold().FontSize(12);
        CenteredLine(column, header.ConcernOf, "A concern of ");
        CenteredLine(column, header.BranchName ?? header.OutletName, "Branch: ");
        CenteredLine(column, header.OutletAddress, "Address: ");
        CenteredLine(column, header.ShowroomHotline, "Showroom Hotline: ");
        CenteredLine(column, header.CentralHotline, "Central Hotline: ");
        CenteredLine(column, header.Website, "Web: ");
        if (!string.IsNullOrWhiteSpace(header.TaxIdentityLine))
        {
            column.Item().PaddingTop(3).AlignCenter()
                .Border(0.6f).PaddingVertical(1.5f).PaddingHorizontal(5)
                .Text(header.TaxIdentityLine).Bold().FontSize(7.5f);
        }

        CenteredLine(column, header.HeaderText);
    }

    private static void ComposeThermalMeta(ColumnDescriptor column, SaleReceiptDocumentReadModel document)
    {
        var sale = document.SaleInfo;
        MetaRow(column, "Date", sale.InvoiceDateText ?? UiDateFormatting.Format(sale.SalesDateTime, document.DateFormat));
        MetaRow(column, "Invoice No", sale.InvoiceNumber);
        MetaRow(column, "Customer", sale.CustomerName);
        if (!string.IsNullOrWhiteSpace(sale.CustomerMobile))
            MetaRow(column, "Mobile", sale.CustomerMobile);
    }

    private static void ComposeThermalItems(ColumnDescriptor column, SaleReceiptDocumentReadModel document)
    {
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(20);
                columns.ConstantColumn(40);
                columns.ConstantColumn(26);
                columns.ConstantColumn(42);
            });

            table.Header(row =>
            {
                row.Cell().Element(HeaderCell).Text("Item").Bold();
                row.Cell().Element(HeaderCell).AlignCenter().Text("Qty").Bold();
                row.Cell().Element(HeaderCell).AlignRight().Text("U.Price").Bold();
                row.Cell().Element(HeaderCell).AlignRight().Text("Dis%").Bold();
                row.Cell().Element(HeaderCell).AlignRight().Text("Total").Bold();
            });

            foreach (var item in document.Items)
            {
                table.Cell().Element(BodyCell).Text(item.PrintName ?? item.ProductName);
                table.Cell().Element(BodyCell).AlignCenter().Text($"{item.Quantity:0.##}").Bold().FontColor(QuantityColor);
                table.Cell().Element(BodyCell).AlignRight().Text($"{item.UnitPrice:0.00}");
                table.Cell().Element(BodyCell).AlignRight().Text($"{item.DiscountPercent:0.##}");
                table.Cell().Element(BodyCell).AlignRight().Text($"{item.LineTotal:0.00}");
            }
        });

        if (document.TaxInclusive)
            column.Item().PaddingTop(2).Text("All prices are inclusive of VAT.").Italic().FontSize(7.5f);
    }

    private static void ComposeThermalTotals(ColumnDescriptor column, SaleReceiptDocumentReadModel document)
    {
        var summary = document.Summary;
        AmountRow(column, "Sub Total", summary.Subtotal);
        if (summary.Discount > 0m)
            AmountRow(column, "Discount", summary.Discount);
        if (!document.TaxInclusive && summary.Tax != 0m)
            AmountRow(column, "VAT", summary.Tax);
        AmountRow(column, "(+/-) Rounding", summary.RoundOff);
        Separator(column);
        AmountRow(column, "Amount Due", summary.GrandTotal, boldLabel: true);
        AmountRow(column, "Paid", summary.PaidAmount);
        foreach (var payment in document.PaymentLines)
            AmountRow(column, payment.Label, payment.Amount);
        AmountRow(column, "Change", summary.Change);

        if (!string.IsNullOrWhiteSpace(document.AmountInWords))
        {
            Separator(column);
            column.Item().Text("Amount In Words:").Bold();
            column.Item().Text(document.AmountInWords);
        }
    }

    private static void ComposeThermalFooter(ColumnDescriptor column, SaleReceiptDocumentReadModel document)
    {
        var footer = document.Footer;
        Separator(column);
        if (!string.IsNullOrWhiteSpace(footer.ReturnPolicy))
        {
            column.Item().AlignCenter().Text("Return / Exchange Policy").Bold();
            column.Item().AlignCenter().Text(text =>
            {
                text.AlignCenter();
                text.Span(footer.ReturnPolicy);
            });
        }

        if (!string.IsNullOrWhiteSpace(footer.VatLawNotification))
        {
            column.Item().AlignCenter().Text(text =>
            {
                text.AlignCenter();
                text.Span(footer.VatLawNotification).Bold();
            });
        }

        if (!string.IsNullOrWhiteSpace(footer.ThankYouMessage))
            column.Item().AlignCenter().Text(footer.ThankYouMessage).Bold();

        if (footer.ShowBarcode || footer.ShowQr)
            column.Item().AlignCenter().Text(footer.CodeValue ?? document.SaleInfo.InvoiceNumber);

        var hasFooterText = !string.IsNullOrWhiteSpace(footer.FooterText);
        var hasPoweredBy = !string.IsNullOrWhiteSpace(footer.PoweredByText);
        if (hasFooterText || hasPoweredBy)
            Separator(column);
        if (hasFooterText)
            column.Item().AlignCenter().Text(footer.FooterText!);
        if (hasPoweredBy)
            column.Item().AlignCenter().Text(footer.PoweredByText!);
    }

    private static IContainer HeaderCell(IContainer cell) =>
        cell.BorderBottom(0.6f).BorderColor(Colors.Black).PaddingBottom(2);

    private static IContainer BodyCell(IContainer cell) => cell.PaddingTop(2);

    private static void Separator(ColumnDescriptor column) =>
        column.Item().PaddingVertical(2.5f).LineHorizontal(0.6f).LineDashPattern([2f, 2f]);

    private static void CenteredLine(ColumnDescriptor column, string? value, string prefix = "")
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        column.Item().AlignCenter().Text(text =>
        {
            text.AlignCenter();
            text.Span(prefix + value);
        });
    }

    private static void MetaRow(ColumnDescriptor column, string label, string value) =>
        column.Item().Row(row =>
        {
            row.ConstantItem(52).Text(label);
            row.ConstantItem(10).Text(":");
            row.RelativeItem().Text(value);
        });

    private static void AmountRow(ColumnDescriptor column, string label, decimal amount, bool boldLabel = false) =>
        column.Item().Row(row =>
        {
            var labelText = row.RelativeItem().Text(label);
            if (boldLabel)
                labelText.Bold();
            row.ConstantItem(60).AlignRight().Text($"{amount:0.00}").Bold();
        });

    private static byte[] RenderA4(SaleReceiptDocumentReadModel document) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(style => style.FontSize(10).FontFamily("Helvetica"));

                page.Content().Column(column =>
                {
                    column.Spacing(3);
                    column.Item().AlignCenter().Text(document.Header.BusinessName ?? "Business")
                        .SemiBold().FontSize(14);
                    if (!string.IsNullOrWhiteSpace(document.Header.ConcernOf))
                    {
                        column.Item().AlignCenter().Text($"A concern of {document.Header.ConcernOf}").FontSize(9);
                    }

                    var branch = document.Header.BranchName ?? document.Header.OutletName;
                    if (!string.IsNullOrWhiteSpace(branch))
                    {
                        column.Item().AlignCenter().Text(branch).FontSize(10);
                    }

                    if (!string.IsNullOrWhiteSpace(document.Header.OutletAddress))
                    {
                        column.Item().AlignCenter().Text(document.Header.OutletAddress).FontSize(9);
                    }

                    if (!string.IsNullOrWhiteSpace(document.Header.Phone))
                    {
                        column.Item().AlignCenter().Text($"Tel: {document.Header.Phone}").FontSize(9);
                    }

                    var taxLine = document.Header.TaxIdentityLine
                        ?? (string.IsNullOrWhiteSpace(document.Header.BinOrVatNo) ? null : $"BIN/VAT: {document.Header.BinOrVatNo}");
                    if (taxLine is not null)
                    {
                        column.Item().AlignCenter().Text(taxLine).FontSize(9);
                    }

                    column.Item().PaddingTop(2).Text($"Invoice: {document.SaleInfo.InvoiceNumber}");
                    column.Item().Text($"Date: {UiDateFormatting.Format(document.SaleInfo.LocalSalesDateTime ?? document.SaleInfo.SalesDateTime, document.DateFormat)}");
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
                            column.Item().Text($"  Variant: {item.VariantLabel}").FontSize(9);
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
                        column.Item().AlignCenter().Text(document.Footer.ReturnPolicy).FontSize(8);
                    }

                    if (!string.IsNullOrWhiteSpace(document.Footer.VatLawNotification))
                    {
                        column.Item().AlignCenter().Text(document.Footer.VatLawNotification).FontSize(8).SemiBold();
                    }

                    if (!string.IsNullOrWhiteSpace(document.Footer.FooterText))
                    {
                        column.Item().AlignCenter().Text(document.Footer.FooterText).FontSize(8);
                    }

                    if (!string.IsNullOrWhiteSpace(document.Footer.PoweredByText))
                    {
                        column.Item().AlignCenter().Text(document.Footer.PoweredByText).FontSize(8);
                    }

                    column.Item().AlignCenter()
                        .Text($"Generated: {UiDateFormatting.Format(document.Footer.GeneratedAt, document.DateFormat)}")
                        .FontSize(8);
                });
            });
        }).GeneratePdf();
}
