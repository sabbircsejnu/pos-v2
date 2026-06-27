using ClosedXML.Excel;
using RetailPOS.API.DTOs.Reports;

namespace RetailPOS.API.Services;

/// <summary>
/// Builds CSV and Excel exports for the Stock Movement Report.
/// All methods operate on the complete filtered dataset — pagination is not applied.
/// </summary>
public static class StockMovementExporter
{
    private static readonly string[] Headers =
    [
        "Product Code", "SKU", "Product Name", "Category",
        "Location", "Location Type",
        "Opening Stock", "Stock In", "Stock Out", "Closing Stock", "Net Movement"
    ];

    public static string ToCsv(IReadOnlyList<StockMovementRowDto> rows, StockMovementSummaryDto summary)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("Stock Movement Report");
        sb.AppendLine($"Generated:,{DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC");
        sb.AppendLine();

        sb.AppendLine(string.Join(",", Headers.Select(EscapeCsv)));

        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                EscapeCsv(r.ProductCode),
                EscapeCsv(r.Sku),
                EscapeCsv(r.ProductName),
                EscapeCsv(r.CategoryName),
                EscapeCsv(r.LocationName),
                EscapeCsv(r.LocationType),
                r.OpeningStock.ToString(),
                r.StockIn.ToString(),
                r.StockOut.ToString(),
                r.ClosingStock.ToString(),
                r.NetMovement.ToString()
            }));
        }

        // Totals row
        sb.AppendLine(string.Join(",", new[]
        {
            "TOTAL", "", $"{rows.Count} rows", "", "", "",
            summary.TotalOpeningStock.ToString(),
            summary.TotalStockIn.ToString(),
            summary.TotalStockOut.ToString(),
            summary.TotalClosingStock.ToString(),
            summary.NetMovement.ToString()
        }));

        return sb.ToString();
    }

    public static byte[] ToExcel(IReadOnlyList<StockMovementRowDto> rows, StockMovementSummaryDto summary)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Stock Movement");

        // ── Title ──────────────────────────────────────────────────────
        ws.Cell(1, 1).Value = "Stock Movement Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"Generated: {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC";
        ws.Cell(2, 1).Style.Font.Italic = true;

        // ── Header row ─────────────────────────────────────────────────
        const int headerRow = 4;
        for (int c = 0; c < Headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = Headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // ── Data rows ──────────────────────────────────────────────────
        for (int i = 0; i < rows.Count; i++)
        {
            var r   = rows[i];
            var row = headerRow + 1 + i;
            if (i % 2 == 1)
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#f0f4f8");

            ws.Cell(row, 1).Value  = r.ProductCode;
            ws.Cell(row, 2).Value  = r.Sku;
            ws.Cell(row, 3).Value  = r.ProductName;
            ws.Cell(row, 4).Value  = r.CategoryName;
            ws.Cell(row, 5).Value  = r.LocationName;
            ws.Cell(row, 6).Value  = r.LocationType;
            ws.Cell(row, 7).Value  = r.OpeningStock;
            ws.Cell(row, 8).Value  = r.StockIn;
            ws.Cell(row, 9).Value  = r.StockOut;
            ws.Cell(row, 10).Value = r.ClosingStock;
            ws.Cell(row, 11).Value = r.NetMovement;

            // Colour Net Movement: green positive, red negative
            var netCell = ws.Cell(row, 11);
            if (r.NetMovement > 0) netCell.Style.Font.FontColor = XLColor.DarkGreen;
            else if (r.NetMovement < 0) netCell.Style.Font.FontColor = XLColor.Red;
        }

        // ── Totals row ─────────────────────────────────────────────────
        int totalsRow = headerRow + 1 + rows.Count;
        ws.Cell(totalsRow, 1).Value  = "TOTAL";
        ws.Cell(totalsRow, 3).Value  = $"{rows.Count} rows";
        ws.Cell(totalsRow, 7).Value  = summary.TotalOpeningStock;
        ws.Cell(totalsRow, 8).Value  = summary.TotalStockIn;
        ws.Cell(totalsRow, 9).Value  = summary.TotalStockOut;
        ws.Cell(totalsRow, 10).Value = summary.TotalClosingStock;
        ws.Cell(totalsRow, 11).Value = summary.NetMovement;

        ws.Row(totalsRow).Style.Font.Bold = true;
        ws.Row(totalsRow).Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
        ws.Cell(totalsRow, 11).Style.Font.FontColor =
            summary.NetMovement >= 0 ? XLColor.DarkGreen : XLColor.Red;

        // ── Freeze header, auto-fit ────────────────────────────────────
        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents();

        using var ms = new System.IO.MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
