using ClosedXML.Excel;
using RetailPOS.API.DTOs.Reports;

namespace RetailPOS.API.Services;

/// <summary>
/// Builds CSV and Excel exports for the Stock Valuation Report.
/// All methods operate on the complete filtered dataset — pagination is not applied.
/// </summary>
public static class StockValuationExporter
{
    private static readonly string[] Headers =
    [
        "Product Code", "Barcode", "SKU", "Product Name", "Category",
        "Location", "Location Type",
        "Quantity", "Unit Cost", "Inventory Value", "% of Total"
    ];

    public static string ToCsv(IReadOnlyList<StockValuationRowDto> rows, StockValuationSummaryDto summary)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("Stock Valuation Report");
        sb.AppendLine($"Generated:,{DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC");
        sb.AppendLine($"Total Inventory Value:,{summary.TotalInventoryValue:F2}");
        sb.AppendLine();

        sb.AppendLine(string.Join(",", Headers.Select(EscapeCsv)));

        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                EscapeCsv(r.ProductCode),
                EscapeCsv(r.Barcode),
                EscapeCsv(r.Sku),
                EscapeCsv(r.ProductName),
                EscapeCsv(r.CategoryName),
                EscapeCsv(r.LocationName),
                EscapeCsv(r.LocationType),
                r.Quantity.ToString(),
                r.UnitCost.ToString("F4"),
                r.InventoryValue.ToString("F2"),
                r.PercentOfTotal.ToString("F2") + "%"
            }));
        }

        // Totals row
        sb.AppendLine(string.Join(",", new[]
        {
            "TOTAL", "", "", $"{rows.Count} rows", "", "", "",
            summary.TotalQuantity.ToString(),
            "",
            summary.TotalInventoryValue.ToString("F2"),
            "100%"
        }));

        // Category breakdown
        sb.AppendLine();
        sb.AppendLine("Category Breakdown");
        sb.AppendLine("Category,Quantity,Inventory Value,% of Total");
        foreach (var c in summary.ByCategory)
            sb.AppendLine($"{EscapeCsv(c.CategoryName)},{c.Quantity},{c.InventoryValue:F2},{c.PercentOfTotal:F2}%");

        return sb.ToString();
    }

    public static byte[] ToExcel(IReadOnlyList<StockValuationRowDto> rows, StockValuationSummaryDto summary)
    {
        using var wb = new XLWorkbook();

        // ── Sheet 1: Detail ────────────────────────────────────────────
        var ws = wb.Worksheets.Add("Stock Valuation");

        ws.Cell(1, 1).Value = "Stock Valuation Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"Generated: {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC";
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Cell(3, 1).Value = $"Total Inventory Value: {summary.TotalInventoryValue:N2}";
        ws.Cell(3, 1).Style.Font.Bold = true;

        const int headerRow = 5;
        for (int c = 0; c < Headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = Headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var r   = rows[i];
            var row = headerRow + 1 + i;
            if (i % 2 == 1)
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#f0f4f8");

            ws.Cell(row, 1).Value  = r.ProductCode;
            ws.Cell(row, 2).Value  = r.Barcode;
            ws.Cell(row, 3).Value  = r.Sku;
            ws.Cell(row, 4).Value  = r.ProductName;
            ws.Cell(row, 5).Value  = r.CategoryName;
            ws.Cell(row, 6).Value  = r.LocationName;
            ws.Cell(row, 7).Value  = r.LocationType;
            ws.Cell(row, 8).Value  = r.Quantity;
            ws.Cell(row, 9).Value  = r.UnitCost;
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.0000";
            ws.Cell(row, 10).Value = r.InventoryValue;
            ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 11).Value = (double)r.PercentOfTotal / 100;
            ws.Cell(row, 11).Style.NumberFormat.Format = "0.00%";
        }

        int totalsRow = headerRow + 1 + rows.Count;
        ws.Cell(totalsRow, 1).Value  = "TOTAL";
        ws.Cell(totalsRow, 4).Value  = $"{rows.Count} rows";
        ws.Cell(totalsRow, 8).Value  = summary.TotalQuantity;
        ws.Cell(totalsRow, 10).Value = summary.TotalInventoryValue;
        ws.Cell(totalsRow, 10).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(totalsRow, 11).Value = 1.0;
        ws.Cell(totalsRow, 11).Style.NumberFormat.Format = "0.00%";
        ws.Row(totalsRow).Style.Font.Bold = true;
        ws.Row(totalsRow).Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");

        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents();

        // ── Sheet 2: Category Summary ──────────────────────────────────
        var ws2 = wb.Worksheets.Add("By Category");
        ws2.Cell(1, 1).Value = "Category";
        ws2.Cell(1, 2).Value = "Quantity";
        ws2.Cell(1, 3).Value = "Inventory Value";
        ws2.Cell(1, 4).Value = "% of Total";
        ws2.Row(1).Style.Font.Bold = true;
        ws2.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
        ws2.Row(1).Style.Font.FontColor = XLColor.White;

        for (int i = 0; i < summary.ByCategory.Count; i++)
        {
            var cat = summary.ByCategory[i];
            int row = i + 2;
            if (i % 2 == 1) ws2.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#f0f4f8");
            ws2.Cell(row, 1).Value = cat.CategoryName;
            ws2.Cell(row, 2).Value = cat.Quantity;
            ws2.Cell(row, 3).Value = cat.InventoryValue;
            ws2.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";
            ws2.Cell(row, 4).Value = (double)cat.PercentOfTotal / 100;
            ws2.Cell(row, 4).Style.NumberFormat.Format = "0.00%";
        }
        ws2.Columns().AdjustToContents();

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
