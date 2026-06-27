using ClosedXML.Excel;
using RetailPOS.API.DTOs.Reports;

namespace RetailPOS.API.Services;

/// <summary>
/// Builds CSV and Excel exports for the Product Ledger Report.
/// Operates on the complete filtered dataset — pagination is not applied.
/// </summary>
public static class ProductLedgerExporter
{
    private static readonly string[] Headers =
    [
        "Date", "Transaction Type", "Reference Number",
        "SKU", "Location",
        "Opening Qty", "Stock In", "Stock Out", "Closing Qty",
        "Unit Cost", "Transaction Value",
        "Performed By", "Remarks"
    ];

    public static string ToCsv(
        IReadOnlyList<ProductLedgerRowDto> rows,
        ProductLedgerSummaryDto summary,
        string productName)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Product Ledger Report — {EscapeCsv(productName)}");
        sb.AppendLine($"Generated: {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine(string.Join(",", Headers.Select(EscapeCsv)));

        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                EscapeCsv(r.TransactionDate.ToString("dd/MM/yyyy HH:mm")),
                EscapeCsv(r.TransactionTypeLabel),
                EscapeCsv(r.ReferenceNumber),
                EscapeCsv(r.Sku),
                EscapeCsv(r.LocationName),
                r.OpeningQuantity.ToString(),
                r.StockIn.ToString(),
                r.StockOut.ToString(),
                r.ClosingQuantity.ToString(),
                r.UnitCost.ToString("F2"),
                r.TransactionValue.ToString("F2"),
                EscapeCsv(r.PerformedBy ?? ""),
                EscapeCsv(r.Remarks ?? "")
            }));
        }

        // Totals row
        sb.AppendLine(string.Join(",", new[]
        {
            EscapeCsv("TOTAL"), "", $"{rows.Count} transactions", "", "",
            summary.OpeningStock.ToString(),
            summary.TotalStockIn.ToString(),
            summary.TotalStockOut.ToString(),
            summary.ClosingStock.ToString(),
            "",
            summary.TotalTransactionValue.ToString("F2"),
            "", ""
        }));

        return sb.ToString();
    }

    public static byte[] ToExcel(
        IReadOnlyList<ProductLedgerRowDto> rows,
        ProductLedgerSummaryDto summary,
        string productName)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Product Ledger");

        ws.Cell(1, 1).Value = $"Product Ledger Report — {productName}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"Generated: {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC";
        ws.Cell(3, 1).Value = $"Total Transactions: {rows.Count}";

        int headerRow = 5;
        for (int c = 0; c < Headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = Headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        int row = headerRow + 1;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value  = r.TransactionDate.ToString("dd/MM/yyyy HH:mm");
            ws.Cell(row, 2).Value  = r.TransactionTypeLabel;
            ws.Cell(row, 3).Value  = r.ReferenceNumber;
            ws.Cell(row, 4).Value  = r.Sku;
            ws.Cell(row, 5).Value  = r.LocationName;
            ws.Cell(row, 6).Value  = r.OpeningQuantity;
            ws.Cell(row, 7).Value  = r.StockIn;
            ws.Cell(row, 8).Value  = r.StockOut;
            ws.Cell(row, 9).Value  = r.ClosingQuantity;
            ws.Cell(row, 10).Value = r.UnitCost;
            ws.Cell(row, 11).Value = r.TransactionValue;
            ws.Cell(row, 12).Value = r.PerformedBy ?? "";
            ws.Cell(row, 13).Value = r.Remarks ?? "";

            ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0.00";

            if (row % 2 == 0)
                ws.Range(row, 1, row, Headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8f9fa");

            row++;
        }

        // Totals
        ws.Cell(row, 1).Value  = "TOTAL";
        ws.Cell(row, 3).Value  = $"{rows.Count} transactions";
        ws.Cell(row, 6).Value  = summary.OpeningStock;
        ws.Cell(row, 7).Value  = summary.TotalStockIn;
        ws.Cell(row, 8).Value  = summary.TotalStockOut;
        ws.Cell(row, 9).Value  = summary.ClosingStock;
        ws.Cell(row, 11).Value = summary.TotalTransactionValue;
        ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0.00";
        ws.Range(row, 1, row, Headers.Length).Style.Font.Bold = true;
        ws.Range(row, 1, row, Headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f0fe");

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(headerRow);

        using var ms = new System.IO.MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
