using System.Text;
using ClosedXML.Excel;
using RetailPOS.API.DTOs.Reports;

namespace RetailPOS.API.Services;

public static class StockAdjustmentExporter
{
    public static byte[] ToCsv(List<StockAdjustmentRowDto> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Date,Product Code,Barcode,SKU,Product Name,Category,Location,Location Type,Type,Quantity Change,Reason,Adjusted By");
        foreach (var r in rows)
            sb.AppendLine($"{r.AdjustmentDate:yyyy-MM-dd HH:mm},{Csv(r.ProductCode)},{Csv(r.Barcode)},{Csv(r.Sku)},{Csv(r.ProductName)},{Csv(r.CategoryName)},{Csv(r.LocationName)},{Csv(r.LocationType)},{Csv(r.AdjustmentType)},{r.QuantityChange},{Csv(r.Reason)},{Csv(r.AdjustedBy)}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] ToExcel(List<StockAdjustmentRowDto> rows, StockAdjustmentSummaryDto summary)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Stock Adjustment Report");

        ws.Cell(1, 1).Value = "Stock Adjustment Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, 12).Merge();

        ws.Cell(2, 1).Value =
            $"Total: {summary.TotalAdjustments}   |   Additions: +{summary.TotalAdditions}   |   Reductions: -{summary.TotalReductions}   |   Net: {(summary.NetQuantityChange >= 0 ? "+" : "")}{summary.NetQuantityChange}";
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Range(2, 1, 2, 12).Merge();

        string[] headers = { "Date", "Product Code", "Barcode", "SKU", "Product Name", "Category", "Location", "Location Type", "Type", "Qty Change", "Reason", "Adjusted By" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(4, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.DarkBlue;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            int row = i + 5;
            var bg = i % 2 == 1 ? XLColor.FromHtml("#F8F9FA") : XLColor.White;

            ws.Cell(row, 1).Value = r.AdjustmentDate;
            ws.Cell(row, 1).Style.DateFormat.Format = "yyyy-MM-dd HH:mm";
            ws.Cell(row, 2).Value = r.ProductCode;
            ws.Cell(row, 3).Value = r.Barcode;
            ws.Cell(row, 4).Value = r.Sku;
            ws.Cell(row, 5).Value = r.ProductName;
            ws.Cell(row, 6).Value = r.CategoryName;
            ws.Cell(row, 7).Value = r.LocationName;
            ws.Cell(row, 8).Value = r.LocationType;
            ws.Cell(row, 9).Value = r.AdjustmentType;
            ws.Cell(row, 10).Value = r.QuantityChange;
            ws.Cell(row, 11).Value = r.Reason;
            ws.Cell(row, 12).Value = r.AdjustedBy;

            var typeBg = r.AdjustmentType == "Addition" ? XLColor.FromHtml("#D1FAE5") : XLColor.FromHtml("#FEE2E2");
            ws.Cell(row, 9).Style.Fill.BackgroundColor = typeBg;
            ws.Cell(row, 10).Style.Font.FontColor = r.QuantityChange >= 0 ? XLColor.DarkGreen : XLColor.Red;

            for (int c = 1; c <= 12; c++)
                if (c != 9) ws.Cell(row, c).Style.Fill.BackgroundColor = bg;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Csv(string? s) => s == null ? "" : $"\"{s.Replace("\"", "\"\"")}\"";
}
