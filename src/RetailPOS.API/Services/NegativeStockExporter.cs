using System.Text;
using ClosedXML.Excel;
using RetailPOS.API.DTOs.Reports;

namespace RetailPOS.API.Services;

public static class NegativeStockExporter
{
    public static byte[] ToCsv(List<NegativeStockRowDto> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Product Code,Barcode,SKU,Product Name,Category,Location,Location Type,Current Stock,Unit Cost,Stock Value");
        foreach (var r in rows)
            sb.AppendLine($"{Csv(r.ProductCode)},{Csv(r.Barcode)},{Csv(r.Sku)},{Csv(r.ProductName)},{Csv(r.CategoryName)},{Csv(r.LocationName)},{Csv(r.LocationType)},{r.CurrentStock},{r.UnitCost:F2},{r.StockValue:F2}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] ToExcel(List<NegativeStockRowDto> rows, NegativeStockSummaryDto summary)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Negative Stock Report");

        ws.Cell(1, 1).Value = "Negative Stock Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, 10).Merge();

        ws.Cell(2, 1).Value = $"Total SKUs: {summary.TotalSkus}   |   Total Negative Qty: {summary.TotalNegativeQty}   |   Total Negative Value: {summary.TotalNegativeValue:N2}";
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Range(2, 1, 2, 10).Merge();

        string[] headers = { "Product Code", "Barcode", "SKU", "Product Name", "Category", "Location", "Location Type", "Current Stock", "Unit Cost", "Stock Value" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(4, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.DarkViolet;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            int row = i + 5;
            var bg = i % 2 == 1 ? XLColor.FromHtml("#F8F9FA") : XLColor.White;

            ws.Cell(row, 1).Value = r.ProductCode;
            ws.Cell(row, 2).Value = r.Barcode;
            ws.Cell(row, 3).Value = r.Sku;
            ws.Cell(row, 4).Value = r.ProductName;
            ws.Cell(row, 5).Value = r.CategoryName;
            ws.Cell(row, 6).Value = r.LocationName;
            ws.Cell(row, 7).Value = r.LocationType;
            ws.Cell(row, 8).Value = r.CurrentStock;
            ws.Cell(row, 9).Value = (double)r.UnitCost;
            ws.Cell(row, 10).Value = (double)r.StockValue;

            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 10).Style.NumberFormat.Format = "#,##0.00";
            // Negative value cells highlighted in red
            ws.Cell(row, 8).Style.Font.FontColor = XLColor.Red;
            ws.Cell(row, 10).Style.Font.FontColor = XLColor.Red;

            for (int c = 1; c <= 10; c++)
                ws.Cell(row, c).Style.Fill.BackgroundColor = bg;
        }

        // Totals row
        int tot = rows.Count + 5;
        ws.Cell(tot, 7).Value = "TOTAL";
        ws.Cell(tot, 8).Value = rows.Sum(r => r.CurrentStock);
        ws.Cell(tot, 10).Value = (double)rows.Sum(r => r.StockValue);
        ws.Cell(tot, 10).Style.NumberFormat.Format = "#,##0.00";
        for (int c = 1; c <= 10; c++)
        {
            ws.Cell(tot, c).Style.Font.Bold = true;
            ws.Cell(tot, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE7F3");
        }
        ws.Cell(tot, 8).Style.Font.FontColor = XLColor.Red;
        ws.Cell(tot, 10).Style.Font.FontColor = XLColor.Red;

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Csv(string? s) => s == null ? "" : $"\"{s.Replace("\"", "\"\"")}\"";
}
