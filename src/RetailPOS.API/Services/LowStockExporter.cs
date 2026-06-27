using System.Text;
using ClosedXML.Excel;
using RetailPOS.API.DTOs.Reports;

namespace RetailPOS.API.Services;

public static class LowStockExporter
{
    public static byte[] ToCsv(List<LowStockRowDto> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Product Code,Barcode,SKU,Product Name,Category,Location,Location Type,Current Stock,Reorder Level,Suggested Order Qty,Urgency");
        foreach (var r in rows)
            sb.AppendLine($"{Csv(r.ProductCode)},{Csv(r.Barcode)},{Csv(r.Sku)},{Csv(r.ProductName)},{Csv(r.CategoryName)},{Csv(r.LocationName)},{Csv(r.LocationType)},{r.CurrentStock},{r.ReorderLevel},{r.SuggestedOrderQty},{Csv(r.UrgencyLevel)}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] ToExcel(List<LowStockRowDto> rows, LowStockSummaryDto summary)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Low Stock Report");

        // Title
        ws.Cell(1, 1).Value = "Low Stock Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, 11).Merge();

        // Summary row
        ws.Cell(2, 1).Value = $"Total SKUs: {summary.TotalSkus}   |   Total Deficit Qty: {summary.TotalDeficitQty}   |   Critical: {summary.CriticalCount}   |   Low: {summary.LowCount}";
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Range(2, 1, 2, 11).Merge();

        // Headers
        string[] headers = { "Product Code", "Barcode", "SKU", "Product Name", "Category", "Location", "Location Type", "Current Stock", "Reorder Level", "Suggested Order Qty", "Urgency" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(4, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.DarkBlue;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Data
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            int row = i + 5;
            bool alt = i % 2 == 1;
            var bg = alt ? XLColor.FromHtml("#F8F9FA") : XLColor.White;

            // urgency colour
            var urgencyBg = r.UrgencyLevel switch
            {
                "Critical"      => XLColor.FromHtml("#FEE2E2"),
                "Low"           => XLColor.FromHtml("#FEF3C7"),
                _               => XLColor.FromHtml("#FFF7ED")
            };

            ws.Cell(row, 1).Value = r.ProductCode;
            ws.Cell(row, 2).Value = r.Barcode;
            ws.Cell(row, 3).Value = r.Sku;
            ws.Cell(row, 4).Value = r.ProductName;
            ws.Cell(row, 5).Value = r.CategoryName;
            ws.Cell(row, 6).Value = r.LocationName;
            ws.Cell(row, 7).Value = r.LocationType;
            ws.Cell(row, 8).Value = r.CurrentStock;
            ws.Cell(row, 9).Value = r.ReorderLevel;
            ws.Cell(row, 10).Value = r.SuggestedOrderQty;
            ws.Cell(row, 11).Value = r.UrgencyLevel;

            for (int c = 1; c <= 11; c++)
                ws.Cell(row, c).Style.Fill.BackgroundColor = bg;

            ws.Cell(row, 8).Style.Fill.BackgroundColor = urgencyBg;
            ws.Cell(row, 11).Style.Fill.BackgroundColor = urgencyBg;
        }

        // Totals row
        int tot = rows.Count + 5;
        ws.Cell(tot, 7).Value = "TOTAL";
        ws.Cell(tot, 8).Value = rows.Sum(r => r.CurrentStock);
        ws.Cell(tot, 9).Value = rows.Sum(r => r.ReorderLevel);
        ws.Cell(tot, 10).Value = rows.Sum(r => r.SuggestedOrderQty);
        for (int c = 1; c <= 11; c++)
        {
            ws.Cell(tot, c).Style.Font.Bold = true;
            ws.Cell(tot, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#DBEAFE");
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Csv(string? s) => s == null ? "" : $"\"{s.Replace("\"", "\"\"")}\"";
}
