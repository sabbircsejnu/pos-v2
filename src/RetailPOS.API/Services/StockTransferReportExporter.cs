using System.Text;
using ClosedXML.Excel;
using RetailPOS.API.DTOs.Reports;

namespace RetailPOS.API.Services;

public static class StockTransferReportExporter
{
    public static byte[] ToCsv(List<StockTransferRowDto> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Transfer ID,Date,From Location,From Type,To Location,To Type,Status,Product Code,Barcode,SKU,Product Name,Category,Quantity,Unit Cost,Transfer Value,Created By");
        foreach (var r in rows)
            sb.AppendLine($"{r.TransferId},{r.TransferDate:yyyy-MM-dd},{Csv(r.FromLocationName)},{Csv(r.FromLocationType)},{Csv(r.ToLocationName)},{Csv(r.ToLocationType)},{Csv(r.Status)},{Csv(r.ProductCode)},{Csv(r.Barcode)},{Csv(r.Sku)},{Csv(r.ProductName)},{Csv(r.CategoryName)},{r.Quantity},{r.UnitCost:F2},{r.TransferValue:F2},{Csv(r.CreatedBy)}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] ToExcel(List<StockTransferRowDto> rows, StockTransferSummaryDto summary)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Stock Transfer Report");

        ws.Cell(1, 1).Value = "Stock Transfer Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, 16).Merge();

        ws.Cell(2, 1).Value =
            $"Transfers: {summary.TotalTransfers}   |   Lines: {summary.TotalLines}   |   Total Qty: {summary.TotalQuantity}   |   Total Value: {summary.TotalValue:N2}   |   Pending: {summary.PendingCount}   |   Completed: {summary.CompletedCount}";
        ws.Cell(2, 1).Style.Font.Italic = true;
        ws.Range(2, 1, 2, 16).Merge();

        string[] headers = { "Transfer ID", "Date", "From Location", "From Type", "To Location", "To Type", "Status", "Product Code", "Barcode", "SKU", "Product Name", "Category", "Quantity", "Unit Cost", "Transfer Value", "Created By" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(4, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E3A5F");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        long? lastTransferId = null;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            int row = i + 5;
            var bg = r.TransferId != lastTransferId ? XLColor.FromHtml("#EFF6FF") : XLColor.White;
            lastTransferId = r.TransferId;

            var statusBg = r.Status switch
            {
                "completed" => XLColor.FromHtml("#D1FAE5"),
                "pending"   => XLColor.FromHtml("#FEF3C7"),
                _           => XLColor.FromHtml("#F3F4F6")
            };

            ws.Cell(row, 1).Value = (double)r.TransferId;
            ws.Cell(row, 2).Value = r.TransferDate;
            ws.Cell(row, 2).Style.DateFormat.Format = "yyyy-MM-dd";
            ws.Cell(row, 3).Value = r.FromLocationName;
            ws.Cell(row, 4).Value = r.FromLocationType;
            ws.Cell(row, 5).Value = r.ToLocationName;
            ws.Cell(row, 6).Value = r.ToLocationType;
            ws.Cell(row, 7).Value = r.Status;
            ws.Cell(row, 7).Style.Fill.BackgroundColor = statusBg;
            ws.Cell(row, 8).Value = r.ProductCode;
            ws.Cell(row, 9).Value = r.Barcode;
            ws.Cell(row, 10).Value = r.Sku;
            ws.Cell(row, 11).Value = r.ProductName;
            ws.Cell(row, 12).Value = r.CategoryName;
            ws.Cell(row, 13).Value = r.Quantity;
            ws.Cell(row, 14).Value = (double)r.UnitCost;
            ws.Cell(row, 14).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 15).Value = (double)r.TransferValue;
            ws.Cell(row, 15).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 16).Value = r.CreatedBy;

            for (int c = 1; c <= 16; c++)
                if (c != 7) ws.Cell(row, c).Style.Fill.BackgroundColor = bg;
        }

        // Totals row
        int tot = rows.Count + 5;
        ws.Cell(tot, 12).Value = "TOTAL";
        ws.Cell(tot, 13).Value = rows.Sum(r => r.Quantity);
        ws.Cell(tot, 15).Value = (double)rows.Sum(r => r.TransferValue);
        ws.Cell(tot, 15).Style.NumberFormat.Format = "#,##0.00";
        for (int c = 1; c <= 16; c++)
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
