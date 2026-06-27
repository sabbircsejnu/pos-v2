using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace RetailPOS.API.Documents.Pdf;

public sealed class QuestDocumentPdfRenderer : IDocumentPdfRenderer
{
    public Task<byte[]> RenderAsync(DocumentPdfRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pdfBytes = Document.Create(container => ComposeDocument(container, request)).GeneratePdf();
        return Task.FromResult(pdfBytes);
    }

    private static void ComposeDocument(IDocumentContainer container, DocumentPdfRequest request)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(24);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(style => style.FontFamily("Helvetica").FontSize(9));

            page.Header().Element(header => RenderHeader(header, request));
            page.Content().PaddingTop(10).Column(column =>
            {
                column.Spacing(12);
                column.Item().Element(meta => RenderDocumentMeta(meta, request));
                column.Item().Element(block => RenderCompanyAndPartyBlocks(block, request));
                column.Item().Element(table => RenderItemsTable(table, request));
                column.Item().Element(summary => RenderSummary(summary, request));

                if (!string.IsNullOrWhiteSpace(request.Notes))
                {
                    column.Item().Element(notes => RenderNotes(notes, request));
                }

                if (request.Signatures is { Count: > 0 })
                {
                    column.Item().Element(signatures => RenderSignatures(signatures, request));
                }
            });

            page.Footer().PaddingTop(8).Element(footer => RenderFooter(footer, request));
        });
    }

    private static void RenderHeader(IContainer container, DocumentPdfRequest request)
    {
        container
            .Background("#0B4E8A")
            .PaddingVertical(14)
            .PaddingHorizontal(18)
            .Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text(request.Title).FontSize(20).SemiBold().FontColor(Colors.White);
                    column.Item().PaddingTop(2).Text(request.Company.CompanyName).FontSize(10).FontColor(Colors.White);
                });

                row.ConstantItem(120).AlignRight().AlignMiddle().Element(logo => RenderCompanyLogo(logo, request.Company));
            });
    }

    private static void RenderCompanyLogo(IContainer container, DocumentPdfCompanyInfo company)
    {
        if (!string.IsNullOrWhiteSpace(company.LogoBase64))
        {
            try
            {
                container.AlignRight().Width(100).Height(42).Image(Convert.FromBase64String(company.LogoBase64));
                return;
            }
            catch
            {
                // Fallback to text brand block when the configured logo cannot be decoded.
            }
        }

        container
            .AlignRight()
            .Text(company.CompanyName)
            .FontSize(15)
            .SemiBold()
            .FontColor(Colors.White)
            .AlignRight();
    }

    private static void RenderDocumentMeta(IContainer container, DocumentPdfRequest request)
    {
        container
            .Border(1)
            .BorderColor("#B8D3EB")
            .Background("#F3F8FC")
            .Padding(10)
            .Row(row =>
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text(request.DocumentNumber).FontSize(14).SemiBold().FontColor("#0B4E8A");

                    if (!string.IsNullOrWhiteSpace(request.ReferenceNumber))
                    {
                        column.Item().PaddingTop(2).Text($"Reference: {request.ReferenceNumber}").FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                    }
                });

                row.RelativeItem().AlignRight().Column(column =>
                {
                    column.Item().AlignRight().Text($"Date: {DocumentPdfFormatting.FormatDateTime(request.DocumentDate, request.Formatting)}").SemiBold();

                    if (!string.IsNullOrWhiteSpace(request.Status))
                    {
                        column.Item().AlignRight().PaddingTop(2).Text($"Status: {request.Status}").FontColor(Colors.Grey.Darken2);
                    }
                });
            });
    }

    private static void RenderCompanyAndPartyBlocks(IContainer container, DocumentPdfRequest request)
    {
        container.Column(column =>
        {
            column.Spacing(10);

            column.Item().Row(row =>
            {
                row.RelativeItem().Element(block => RenderInfoCard(block, "Company Information", new object[]
                {
                    request.Company.CompanyName,
                    request.Company.AddressLines,
                    request.Company.Phone,
                    request.Company.Email,
                    request.Company.Website,
                    request.Company.TaxNumber
                }));

                row.RelativeItem().Element(block => RenderPartyCard(block, request.BillTo));
            });

            if (request.ShipTo != null)
            {
                column.Item().Element(block => RenderPartyCard(block, request.ShipTo));
            }
        });
    }

    private static void RenderInfoCard(IContainer container, string title, object[] values)
    {
        container
            .Border(1)
            .BorderColor("#D9E6F2")
            .Background(Colors.White)
            .Padding(10)
            .Column(column =>
            {
                column.Item().Text(title).SemiBold().FontColor("#0B4E8A");

                foreach (var value in values)
                {
                    switch (value)
                    {
                        case string text when !string.IsNullOrWhiteSpace(text):
                            column.Item().PaddingTop(1).Text(text).FontSize(8.5f);
                            break;

                        case IReadOnlyList<string> lines when lines.Count > 0:
                            foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
                            {
                                column.Item().PaddingTop(1).Text(line).FontSize(8.5f);
                            }

                            break;
                    }
                }
            });
    }

    private static void RenderPartyCard(IContainer container, DocumentPdfPartyInfo? party)
    {
        if (party == null)
        {
            return;
        }

        container
            .Border(1)
            .BorderColor("#D9E6F2")
            .Background(Colors.White)
            .Padding(10)
            .Column(column =>
            {
                column.Item().Text(party.Label).SemiBold().FontColor("#0B4E8A");
                column.Item().PaddingTop(2).Text(party.Name).SemiBold();

                foreach (var line in party.AddressLines.Where(line => !string.IsNullOrWhiteSpace(line)))
                {
                    column.Item().PaddingTop(1).Text(line).FontSize(8.5f);
                }

                if (!string.IsNullOrWhiteSpace(party.Contact))
                {
                    column.Item().PaddingTop(1).Text(party.Contact).FontSize(8.5f);
                }

                if (!string.IsNullOrWhiteSpace(party.Reference))
                {
                    column.Item().PaddingTop(1).Text(party.Reference).FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                }
            });
    }

    private static void RenderItemsTable(IContainer container, DocumentPdfRequest request)
    {
        var showCostFields = request.ShowCostFields;

        container
            .Border(1)
            .BorderColor("#D9E6F2")
            .Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(28);
                    columns.RelativeColumn(5);
                    columns.ConstantColumn(42);
                    columns.ConstantColumn(48);

                    if (showCostFields)
                    {
                        columns.ConstantColumn(64);
                        columns.ConstantColumn(78);
                    }
                });

                table.Header(header =>
                {
                    RenderHeaderCell(header.Cell(), "#");
                    RenderHeaderCell(header.Cell(), "Item");
                    RenderHeaderCell(header.Cell(), "Qty");
                    RenderHeaderCell(header.Cell(), "Unit");

                    if (showCostFields)
                    {
                        RenderHeaderCell(header.Cell(), "Rate");
                        RenderHeaderCell(header.Cell(), "Amount");
                    }
                });

                foreach (var item in request.Items)
                {
                    RenderBodyCell(table.Cell(), item.LineNo.ToString(), false);
                    RenderBodyCell(table.Cell(), BuildItemTitle(item), false);
                    RenderBodyCell(table.Cell(), item.Quantity.ToString("0.##"), true);
                    RenderBodyCell(table.Cell(), item.Unit ?? string.Empty, false);

                    if (showCostFields)
                    {
                        RenderBodyCell(table.Cell(), item.UnitRate.HasValue ? DocumentPdfFormatting.FormatCurrency(item.UnitRate.Value, request.Formatting) : string.Empty, true);
                        RenderBodyCell(table.Cell(), item.LineTotal.HasValue ? DocumentPdfFormatting.FormatCurrency(item.LineTotal.Value, request.Formatting) : string.Empty, true);
                    }
                }
            });
    }

    private static string BuildItemTitle(DocumentPdfLineItem item)
    {
        var parts = new List<string> { item.ItemName };

        if (HasDisplayText(item.ProductCode))
        {
            parts.Add($"Code: {item.ProductCode!.Trim()}");
        }

        var variantParts = new List<string>();
        if (HasDisplayText(item.VariantName))
        {
            variantParts.Add(item.VariantName!.Trim());
        }

        if (HasDisplayText(item.Description))
        {
            variantParts.Add(item.Description!.Trim());
        }

        if (variantParts.Count > 0)
        {
            parts.Add($"Variant: {string.Join(" - ", variantParts)}");
        }

        return string.Join("\n", parts);
    }

    private static bool HasDisplayText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        // Avoid rendering serialized empty payloads in line-item titles.
        if (string.Equals(trimmed, "{}", StringComparison.Ordinal)
            || string.Equals(trimmed, "[]", StringComparison.Ordinal)
            || string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Suppress raw JSON payloads (object/array) from appearing in PDF lines.
        if ((trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal))
            || (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)))
        {
            return false;
        }

        return true;
    }

    private static void RenderHeaderCell(IContainer container, string text)
    {
        container
            .Background("#0B4E8A")
            .PaddingVertical(6)
            .PaddingHorizontal(6)
            .Text(text)
            .FontSize(8.5f)
            .SemiBold()
            .FontColor(Colors.White);
    }

    private static void RenderBodyCell(IContainer container, string text, bool alignRight)
    {
        if (alignRight)
        {
            container
                .BorderBottom(1)
                .BorderColor("#E1ECF5")
                .PaddingVertical(5)
                .PaddingHorizontal(6)
                .AlignRight()
                .Text(text)
                .FontSize(8.5f);

            return;
        }

        container
            .BorderBottom(1)
            .BorderColor("#E1ECF5")
            .PaddingVertical(5)
            .PaddingHorizontal(6)
            .Text(text)
            .FontSize(8.5f);
    }

    private static void RenderSummary(IContainer container, DocumentPdfRequest request)
    {
        container
            .AlignRight()
            .Width(240)
            .Border(1)
            .BorderColor("#D9E6F2")
            .Padding(10)
            .Column(column =>
            {
                column.Item().Text("Summary").SemiBold().FontColor("#0B4E8A");
                column.Item().PaddingTop(4).Row(row => RenderSummaryRow(row, "Subtotal", request.Summary.Subtotal, request.Formatting));
                column.Item().PaddingTop(2).Row(row => RenderSummaryRow(row, "Discount", request.Summary.Discount, request.Formatting));
                column.Item().PaddingTop(2).Row(row => RenderSummaryRow(row, $"Tax/VAT", request.Summary.Tax, request.Formatting));

                column.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Text("Grand Total").SemiBold();
                    row.ConstantItem(110).AlignRight().Text(DocumentPdfFormatting.FormatCurrency(request.Summary.GrandTotal, request.Formatting)).SemiBold();
                });
            });
    }

    private static void RenderSummaryRow(RowDescriptor row, string label, decimal amount, DocumentPdfFormattingOptions formatting)
    {
        row.RelativeItem().Text(label);
        row.ConstantItem(110).AlignRight().Text(DocumentPdfFormatting.FormatCurrency(amount, formatting));
    }

    private static void RenderNotes(IContainer container, DocumentPdfRequest request)
    {
        container
            .Border(1)
            .BorderColor("#D9E6F2")
            .Padding(10)
            .Column(column =>
            {
                column.Item().Text("Notes / Special Instructions").SemiBold().FontColor("#0B4E8A");
                column.Item().PaddingTop(4).Text(request.Notes).FontSize(8.5f);
            });
    }

    private static void RenderSignatures(IContainer container, DocumentPdfRequest request)
    {
        container.Row(row =>
        {
            foreach (var signature in (request.Signatures ?? Array.Empty<DocumentPdfSignature>()).Where(signature => !string.IsNullOrWhiteSpace(signature.Value)))
            {
                row.RelativeItem().PaddingRight(12).Element(block =>
                {
                    block.BorderTop(1).BorderColor("#8492A6").PaddingTop(8).Column(column =>
                    {
                        column.Item().Text(signature.Label).FontSize(8.5f).SemiBold();
                        column.Item().PaddingTop(18).Text(signature.Value ?? string.Empty).FontSize(8.5f);
                    });
                });
            }
        });
    }

    private static void RenderFooter(IContainer container, DocumentPdfRequest request)
    {
        container
            .BorderTop(1)
            .BorderColor("#D9E6F2")
            .PaddingTop(6)
            .Row(row =>
            {
                row.RelativeItem().Text($"Generated: {DocumentPdfFormatting.FormatDateTime(DateTime.UtcNow, request.Formatting)}").FontSize(8).FontColor(Colors.Grey.Darken1);

                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
    }
}