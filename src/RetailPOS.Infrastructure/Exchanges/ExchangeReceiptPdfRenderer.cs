using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RetailPOS.Application.Exchanges;
using RetailPOS.Application.Exchanges.Abstractions;

namespace RetailPOS.Infrastructure.Exchanges;

/// <summary>
/// The exchange receipt (EX-12) in the sale receipt's style: an 80 mm thermal roll or an A4 page. It prints only the
/// immutable snapshot plus display names; it never prints the internal note (EX-17) or loyalty points (D-05).
/// </summary>
public sealed class ExchangeReceiptPdfRenderer : IExchangeReceiptPdfRenderer
{
    private const string QuantityColor = "#00008B";

    public byte[] Render(ExchangeReceiptPdfModel model) =>
        string.Equals(model.Mode, "a4", StringComparison.OrdinalIgnoreCase) ? RenderA4(model) : RenderThermal(model);

    private static byte[] RenderThermal(ExchangeReceiptPdfModel model) =>
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
                    column.Item().AlignCenter().Text(model.BusinessName ?? "Business").Bold().FontSize(12);
                    if (!string.IsNullOrWhiteSpace(model.OutletName))
                        column.Item().AlignCenter().Text($"Branch: {model.OutletName}");
                    column.Item().PaddingTop(5).AlignCenter().Text("Exchange Receipt").Bold().FontSize(9.5f);
                    Separator(column);
                    ComposeMeta(column, model, thermal: true);
                    Separator(column);
                    ComposeLines(column, model, "Returned", model.Document.Returned, thermal: true);
                    ComposeLines(column, model, "Replacement", model.Document.Replacements, thermal: true);
                    Separator(column);
                    ComposeTotals(column, model);
                    Separator(column);
                    column.Item().AlignCenter().Text("Thank you").Bold();
                });
            });
        }).GeneratePdf();

    private static byte[] RenderA4(ExchangeReceiptPdfModel model) =>
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
                    column.Item().AlignCenter().Text(model.BusinessName ?? "Business").SemiBold().FontSize(14);
                    if (!string.IsNullOrWhiteSpace(model.OutletName))
                        column.Item().AlignCenter().Text(model.OutletName).FontSize(10);
                    column.Item().PaddingTop(4).AlignCenter().Text("Exchange Receipt").SemiBold().FontSize(12);
                    column.Item().PaddingVertical(2).LineHorizontal(0.6f);
                    ComposeMeta(column, model, thermal: false);
                    column.Item().PaddingVertical(2).LineHorizontal(0.6f);
                    ComposeLines(column, model, "Returned", model.Document.Returned, thermal: false);
                    ComposeLines(column, model, "Replacement", model.Document.Replacements, thermal: false);
                    column.Item().PaddingVertical(2).LineHorizontal(0.6f);
                    ComposeTotals(column, model);
                    column.Item().PaddingTop(4).AlignCenter().Text("Thank you").SemiBold();
                });
            });
        }).GeneratePdf();

    private static void ComposeMeta(ColumnDescriptor column, ExchangeReceiptPdfModel model, bool thermal)
    {
        var document = model.Document;
        MetaRow(column, "Exchange No", document.ExchangeNumber, thermal);
        MetaRow(column, "Date", model.LocalCompletedAt.ToString("dd-MM-yyyy hh:mm tt", CultureInfo.InvariantCulture), thermal);
        MetaRow(column, "Business date", document.BusinessDate, thermal);
        if (document.OriginalInvoiceNumbers.Count > 0)
            MetaRow(column, "Original inv.", string.Join(", ", document.OriginalInvoiceNumbers), thermal);
        if (!string.IsNullOrWhiteSpace(model.CustomerName))
            MetaRow(column, "Customer", model.CustomerName, thermal);
        MetaRow(column, "Cashier", model.CashierName ?? string.Empty, thermal);
        if (!string.IsNullOrWhiteSpace(model.SalespersonName))
            MetaRow(column, "Salesperson", model.SalespersonName, thermal);
        MetaRow(column, "Reason", Reason(document.ReasonCode, document.ReasonOtherText), thermal);
    }

    private static void ComposeLines(ColumnDescriptor column, ExchangeReceiptPdfModel model, string title, IReadOnlyList<ExchangeReceiptLine> lines, bool thermal)
    {
        if (lines.Count == 0)
            return;
        column.Item().PaddingTop(2).Text(title).Bold();
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(thermal ? 34 : 80);
                columns.ConstantColumn(thermal ? 46 : 90);
            });
            table.Header(row =>
            {
                row.Cell().Element(HeaderCell).Text("Item").Bold();
                row.Cell().Element(HeaderCell).AlignCenter().Text("Qty").Bold();
                row.Cell().Element(HeaderCell).AlignRight().Text("Amount").Bold();
            });
            foreach (var line in lines)
            {
                var name = model.VariantNames.GetValueOrDefault(line.VariantId) ?? $"Item {line.VariantId}";
                if (!string.IsNullOrWhiteSpace(line.InvoiceNumber))
                    name = $"{name} ({line.InvoiceNumber})";
                var quantity = line.EnteredQuantity is { } entered && !string.IsNullOrWhiteSpace(line.UnitLabel)
                    ? $"{entered:0.##} {line.UnitLabel}"
                    : $"{line.Quantity:0.##}";
                table.Cell().Element(BodyCell).Text(name);
                table.Cell().Element(BodyCell).AlignCenter().Text(quantity).Bold().FontColor(QuantityColor);
                table.Cell().Element(BodyCell).AlignRight().Text($"{line.Gross:0.00}");
            }
        });
    }

    private static void ComposeTotals(ColumnDescriptor column, ExchangeReceiptPdfModel model)
    {
        var document = model.Document;
        AmountRow(column, "Returned value", document.ReturnGross);
        AmountRow(column, "Replacement value", document.ReplacementGross);
        if (document.ReplacementDiscount > 0m)
            AmountRow(column, "Replacement discount", document.ReplacementDiscount);
        AmountRow(column, "VAT (returned)", document.ReturnVat);
        AmountRow(column, "VAT (replacement)", document.ReplacementVat);
        AmountRow(column, "Difference", document.NetAdjustmentGross, bold: true);
        var label = document.SettlementDirection switch
        {
            ExchangeSettlementDirections.Collect => "Collected",
            ExchangeSettlementDirections.Refund => "Refunded",
            _ => "Even exchange"
        };
        AmountRow(column, label, document.SettlementAmount, bold: true);
        foreach (var settlement in document.Settlements)
        {
            var reference = string.IsNullOrWhiteSpace(settlement.ReferenceLastFour) ? string.Empty : $" (ref …{settlement.ReferenceLastFour})";
            AmountRow(column, $"  {settlement.Method}{reference}", settlement.Amount);
            if (settlement.Tendered is { } tendered)
                AmountRow(column, "  Tendered", tendered);
            if (settlement.ChangeGiven is { } change && change > 0m)
                AmountRow(column, "  Change", change);
        }
    }

    private static string Reason(string code, string? other) => code switch
    {
        ExchangeReasonCodes.SizeFitting => "Size / fitting",
        ExchangeReasonCodes.ColourDesign => "Colour / design",
        ExchangeReasonCodes.WrongProductPurchased => "Wrong product purchased",
        ExchangeReasonCodes.CustomerPreferenceChanged => "Customer preference changed",
        ExchangeReasonCodes.Other => string.IsNullOrWhiteSpace(other) ? "Other" : $"Other: {other}",
        _ => code
    };

    private static IContainer HeaderCell(IContainer cell) => cell.BorderBottom(0.6f).BorderColor(Colors.Black).PaddingBottom(2);

    private static IContainer BodyCell(IContainer cell) => cell.PaddingTop(2);

    private static void Separator(ColumnDescriptor column) =>
        column.Item().PaddingVertical(2.5f).LineHorizontal(0.6f).LineDashPattern([2f, 2f]);

    private static void MetaRow(ColumnDescriptor column, string label, string value, bool thermal) =>
        column.Item().Row(row =>
        {
            row.ConstantItem(thermal ? 58 : 110).Text(label);
            row.ConstantItem(10).Text(":");
            row.RelativeItem().Text(value);
        });

    private static void AmountRow(ColumnDescriptor column, string label, decimal amount, bool bold = false) =>
        column.Item().Row(row =>
        {
            var text = row.RelativeItem().Text(label);
            if (bold)
                text.Bold();
            row.ConstantItem(60).AlignRight().Text($"{amount:0.00}").Bold();
        });
}
