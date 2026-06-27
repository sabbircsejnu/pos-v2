using ClosedXML.Excel;
using RetailPOS.API.DTOs.Reports;

namespace RetailPOS.API.Services;

/// <summary>
/// Builds CSV and Excel file exports for the Current Stock Report.
/// All methods operate on the complete filtered dataset — pagination is not applied.
/// </summary>
public static class CurrentStockExporter
{
    private static readonly string[] Headers =
    [
        "Product Code", "Barcode", "Product Name", "Category",
        "Location", "Location Type",
        "Available Qty", "Reserved Qty", "Reorder Level",
        "Unit Cost", "Stock Value",
        "Last Purchase Date", "Last Sale Date", "Stock Status"
    ];

    public static string ToCsv(IReadOnlyList<CurrentStockRowDto> rows)
    {
        var sb = new System.Text.StringBuilder();

        // Header
        sb.AppendLine(string.Join(",", Headers.Select(EscapeCsv)));

        // Data rows
        foreach (var r in rows)
        {
            sb.AppendLine(string.Join(",", new[]
            {
                EscapeCsv(r.ProductCode),
                EscapeCsv(r.Barcode ?? ""),
                EscapeCsv(r.ProductName),
                EscapeCsv(r.Category),
                EscapeCsv(r.LocationName),
                EscapeCsv(r.LocationType),
                r.AvailableQuantity.ToString(),
                r.ReservedQuantity.ToString(),
                r.ReorderLevel.ToString(),
                r.UnitCost.ToString("F2"),
                r.StockValue.ToString("F2"),
                r.LastPurchaseDate.HasValue ? r.LastPurchaseDate.Value.ToString("dd/MM/yyyy") : "",
                r.LastSaleDate.HasValue ? r.LastSaleDate.Value.ToString("dd/MM/yyyy") : "",
                EscapeCsv(r.StockStatus)
            }));
        }

        // Totals row
        var totalQty   = rows.Sum(r => r.AvailableQuantity);
        var totalValue = rows.Sum(r => r.StockValue);
        sb.AppendLine(string.Join(",", new[]
        {
            EscapeCsv("TOTAL"),
            "", EscapeCsv($"{rows.Count} products"), "", "", "",
            totalQty.ToString(),
            "",  "", "",
            totalValue.ToString("F2"),
            "", "", ""
        }));

        return sb.ToString();
    }

    public static byte[] ToExcel(IReadOnlyList<CurrentStockRowDto> rows)
    {
        using var workbook  = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Current Stock");

        // ── Report title ────────────────────────────────────────────
        ws.Cell(1, 1).Value = "Current Stock Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Cell(2, 1).Value = $"Generated: {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC";
        ws.Cell(3, 1).Value = $"Total Records: {rows.Count}";

        // ── Column headers ───────────────────────────────────────────
        int headerRow = 5;
        for (int c = 0; c < Headers.Length; c++)
        {
            var cell = ws.Cell(headerRow, c + 1);
            cell.Value = Headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        }

        // ── Data rows ────────────────────────────────────────────────
        int row = headerRow + 1;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value  = r.ProductCode;
            ws.Cell(row, 2).Value  = r.Barcode ?? "";
            ws.Cell(row, 3).Value  = r.ProductName;
            ws.Cell(row, 4).Value  = r.Category;
            ws.Cell(row, 5).Value  = r.LocationName;
            ws.Cell(row, 6).Value  = r.LocationType;
            ws.Cell(row, 7).Value  = r.AvailableQuantity;
            ws.Cell(row, 8).Value  = r.ReservedQuantity;
            ws.Cell(row, 9).Value  = r.ReorderLevel;
            ws.Cell(row, 10).Value = r.UnitCost;
            ws.Cell(row, 11).Value = r.StockValue;
            ws.Cell(row, 12).Value = r.LastPurchaseDate.HasValue
                ? r.LastPurchaseDate.Value.ToString("dd/MM/yyyy") : "";
            ws.Cell(row, 13).Value = r.LastSaleDate.HasValue
                ? r.LastSaleDate.Value.ToString("dd/MM/yyyy") : "";
            ws.Cell(row, 14).Value = r.StockStatus;

            // Number formatting for currency columns
            ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0.00";

            // Alternate row shading
            if (row % 2 == 0)
            {
                ws.Range(row, 1, row, Headers.Length)
                  .Style.Fill.BackgroundColor = XLColor.FromHtml("#f8f9fa");
            }

            row++;
        }

        // ── Totals row ───────────────────────────────────────────────
        var totalQty   = rows.Sum(r => r.AvailableQuantity);
        var totalValue = rows.Sum(r => r.StockValue);

        ws.Cell(row, 1).Value  = "TOTAL";
        ws.Cell(row, 3).Value  = $"{rows.Count} products";
        ws.Cell(row, 7).Value  = totalQty;
        ws.Cell(row, 11).Value = totalValue;
        ws.Cell(row, 11).Style.NumberFormat.Format = "#,##0.00";

        var totalsRange = ws.Range(row, 1, row, Headers.Length);
        totalsRange.Style.Font.Bold = true;
        totalsRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#e8f0fe");

        // ── Column widths ────────────────────────────────────────────
        ws.Columns().AdjustToContents();

        // ── Freeze header row ────────────────────────────────────────
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
