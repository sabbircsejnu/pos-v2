using System.Text;
using ClosedXML.Excel;
using RetailPOS.API.DTOs.Reports;

namespace RetailPOS.API.Services;

public static class OutOfStockExporter
{
    public static byte[] ToCsv(List<OutOfStockRowDto> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Product Code,Barcode,SKU,Product Name,Category,Location,Location Type,Reorder Level,Unit Cost");
        foreach (var r in rows)
            sb.AppendLine($"{Csv(r.ProductCode)},{Csv(r.Barcode)},{Csv(r.Sku)},{Csv(r.ProductName)},{Csv(r.CategoryName)},{Csv(r.LocationName)},{Csv(r.LocationType)},{r.ReorderLevel},{r.UnitCost:F2}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] ToExcel(List<OutOfStockRowDto> rows, OutOfStockSummaryDto summary)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Out Of Stock Report");

        ws.Cell(1, 1).Value = "Out Of Stock Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, 9).Merge();

        ws.Cell(2, 1).Value = $"Total SKUs: {summary.TotalSkus}   |   Locations Affected: {summary.TotalLocations}   |   Est. Cost to Restock: {summary.EstimatedCostImpact:N2}";
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Range(2, 1, 2, 9).Merge();

        string[] headers = { "Product Code", "Barcode", "SKU", "Product Name", "Category", "Location", "Location Type", "Reorder Level", "Unit Cost" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(4, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.DarkRed;
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
            ws.Cell(row, 8).Value = r.ReorderLevel;
            ws.Cell(row, 9).Value = (double)r.UnitCost;
            ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0.00";

            for (int c = 1; c <= 9; c++)
                ws.Cell(row, c).Style.Fill.BackgroundColor = bg;
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Csv(string? s) => s == null ? "" : $"\"{s.Replace("\"", "\"\"")}\"";
}
