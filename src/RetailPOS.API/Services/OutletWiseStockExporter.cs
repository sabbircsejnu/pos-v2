using ClosedXML.Excel;
using RetailPOS.API.DTOs.Reports;

namespace RetailPOS.API.Services;

/// <summary>
/// Builds CSV and Excel exports for the Outlet Wise Stock Report.
/// All methods operate on the complete filtered dataset — pagination is not applied.
/// </summary>
public static class OutletWiseStockExporter
{
    private static readonly string[] Headers =
    [
        "Outlet", "Product Code", "Barcode", "SKU", "Product Name",
        "Category", "Quantity", "Unit Cost", "Stock Value"
    ];

    public static string ToCsv(IReadOnlyList<OutletWiseStockRowDto> rows, OutletWiseStockSummaryDto summary)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("Outlet Wise Stock Report");
        sb.AppendLine($"Generated:,{DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC");
        sb.AppendLine($"Total Outlets:,{summary.TotalOutlets}");
        sb.AppendLine($"Total Stock Value:,{summary.TotalStockValue:F2}");
        sb.AppendLine();

        sb.AppendLine(string.Join(",", Headers.Select(EscapeCsv)));

        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                EscapeCsv(r.OutletName),
                EscapeCsv(r.ProductCode),
                EscapeCsv(r.Barcode),
                EscapeCsv(r.Sku),
                EscapeCsv(r.ProductName),
                EscapeCsv(r.CategoryName),
                r.Quantity.ToString(),
                r.UnitCost.ToString("F4"),
                r.StockValue.ToString("F2")
            }));
        }

        sb.AppendLine(string.Join(",", new[]
        {
            "TOTAL", "", "", "", $"{rows.Count} rows", "",
            summary.TotalQuantity.ToString(), "", summary.TotalStockValue.ToString("F2")
        }));

        // Per-outlet summary
        sb.AppendLine();
        sb.AppendLine("Outlet Summary");
        sb.AppendLine("Outlet,Total SKUs,Total Qty,Stock Value");
        foreach (var o in summary.ByOutlet)
            sb.AppendLine($"{EscapeCsv(o.OutletName)},{o.TotalSkus},{o.TotalQty},{o.StockValue:F2}");

        return sb.ToString();
    }

    public static byte[] ToExcel(IReadOnlyList<OutletWiseStockRowDto> rows, OutletWiseStockSummaryDto summary)
    {
        using var wb = new XLWorkbook();

        // ── Sheet 1: Detail ────────────────────────────────────────────
        var ws = wb.Worksheets.Add("Outlet Stock");

        ws.Cell(1, 1).Value = "Outlet Wise Stock Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"Generated: {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC";
        ws.Cell(2, 1).Style.Font.Italic = true;

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

        string? lastOutlet = null;
        for (int i = 0; i < rows.Count; i++)
        {
            var r   = rows[i];
            var row = headerRow + 1 + i;

            // Shade outlet group rows alternately
            if (r.OutletName != lastOutlet)
            {
                lastOutlet = r.OutletName;
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#eff6ff");
                ws.Row(row).Style.Font.Bold = true;
            }
            else if (i % 2 == 1)
            {
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
            }

            ws.Cell(row, 1).Value = r.OutletName;
            ws.Cell(row, 2).Value = r.ProductCode;
            ws.Cell(row, 3).Value = r.Barcode;
            ws.Cell(row, 4).Value = r.Sku;
            ws.Cell(row, 5).Value = r.ProductName;
            ws.Cell(row, 6).Value = r.CategoryName;
            ws.Cell(row, 7).Value = r.Quantity;
            ws.Cell(row, 8).Value = r.UnitCost;
            ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0.0000";
            ws.Cell(row, 9).Value = r.StockValue;
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.00";
        }

        int totalsRow = headerRow + 1 + rows.Count;
        ws.Cell(totalsRow, 1).Value = "TOTAL";
        ws.Cell(totalsRow, 5).Value = $"{rows.Count} rows";
        ws.Cell(totalsRow, 7).Value = summary.TotalQuantity;
        ws.Cell(totalsRow, 9).Value = summary.TotalStockValue;
        ws.Cell(totalsRow, 9).Style.NumberFormat.Format = "#,##0.00";
        ws.Row(totalsRow).Style.Font.Bold = true;
        ws.Row(totalsRow).Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");

        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents();

        // ── Sheet 2: Outlet Summary ────────────────────────────────────
        var ws2 = wb.Worksheets.Add("By Outlet");
        ws2.Cell(1, 1).Value = "Outlet";
        ws2.Cell(1, 2).Value = "Total SKUs";
        ws2.Cell(1, 3).Value = "Total Qty";
        ws2.Cell(1, 4).Value = "Stock Value";
        ws2.Row(1).Style.Font.Bold = true;
        ws2.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
        ws2.Row(1).Style.Font.FontColor = XLColor.White;

        for (int i = 0; i < summary.ByOutlet.Count; i++)
        {
            var o   = summary.ByOutlet[i];
            var row = i + 2;
            if (i % 2 == 1) ws2.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#f0f4f8");
            ws2.Cell(row, 1).Value = o.OutletName;
            ws2.Cell(row, 2).Value = o.TotalSkus;
            ws2.Cell(row, 3).Value = o.TotalQty;
            ws2.Cell(row, 4).Value = o.StockValue;
            ws2.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";
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
