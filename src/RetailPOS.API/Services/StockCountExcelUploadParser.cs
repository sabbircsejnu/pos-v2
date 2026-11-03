using System.Globalization;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Core.Entities;

namespace RetailPOS.API.Services;

internal static class StockCountExcelUploadParser
{
    public static IReadOnlyList<StockCountUploadLineUpdate> Parse(
        StockCountDetailReadModel stockCount,
        IFormFile file)
    {
        if (stockCount.Status != StockCount.StatusDraft)
        {
            throw new InvalidOperationException(
                "Excel upload is only allowed while stock count is in Draft status.");
        }

        if (file.Length == 0)
        {
            throw new InvalidOperationException("Uploaded file is empty.");
        }

        var extension = Path.GetExtension(file.FileName);
        if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .xlsx files are supported for stock count upload.");
        }

        var orderedLines = stockCount.Lines
            .OrderBy(line => line.ProductName)
            .ThenBy(line => line.VariantName)
            .Select(line => new WorkingLine(
                line.Id,
                line.CurrentStock,
                line.PhysicalStock,
                line.Difference,
                line.Remarks))
            .ToArray();

        using var stream = file.OpenReadStream();
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("Uploaded workbook does not contain any worksheet.");

        var updates = new List<StockCountUploadLineUpdate>();
        var meaningfulUpdates = 0;
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 7;
        for (var row = 8; row <= lastRow; row++)
        {
            var serialCell = worksheet.Cell(row, 1).GetValue<string>().Trim();
            var physicalCell = worksheet.Cell(row, 6).GetValue<string>().Trim();
            var remarksCell = worksheet.Cell(row, 8).GetValue<string>();

            if (string.IsNullOrWhiteSpace(serialCell)
                && string.IsNullOrWhiteSpace(physicalCell)
                && string.IsNullOrWhiteSpace(remarksCell))
            {
                continue;
            }

            if (!int.TryParse(serialCell, out var serial)
                || serial < 1
                || serial > orderedLines.Length)
            {
                throw new InvalidOperationException(
                    $"Invalid serial number at row {row}: '{serialCell}'");
            }

            var line = orderedLines[serial - 1];
            var previousPhysical = line.PhysicalStock;
            var previousDifference = line.Difference;
            var previousRemarks = line.Remarks;
            if (string.IsNullOrWhiteSpace(physicalCell))
            {
                line.PhysicalStock = null;
                line.Difference = null;
            }
            else
            {
                if (!decimal.TryParse(
                        physicalCell,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var physicalStock)
                    && !decimal.TryParse(
                        physicalCell,
                        NumberStyles.Number,
                        CultureInfo.CurrentCulture,
                        out physicalStock))
                {
                    throw new InvalidOperationException(
                        $"Invalid physical count value at row {row}: '{physicalCell}'");
                }

                if (physicalStock < 0)
                {
                    throw new InvalidOperationException(
                        $"Physical count cannot be negative at row {row}.");
                }

                line.PhysicalStock = physicalStock;
                line.Difference = physicalStock - line.CurrentStock;
            }

            line.Remarks = string.IsNullOrWhiteSpace(remarksCell)
                ? null
                : remarksCell.Trim();
            updates.Add(new StockCountUploadLineUpdate(
                line.Id,
                line.PhysicalStock,
                line.Difference,
                line.Remarks));

            if (line.PhysicalStock != previousPhysical
                || line.Difference != previousDifference
                || !string.Equals(line.Remarks, previousRemarks, StringComparison.Ordinal))
            {
                meaningfulUpdates++;
            }
        }

        if (updates.Count == 0)
        {
            throw new InvalidOperationException("No stock count rows were detected in uploaded Excel.");
        }

        if (meaningfulUpdates == 0)
        {
            throw new InvalidOperationException(
                "Uploaded Excel does not contain any changes to physical count or remarks.");
        }

        return updates;
    }

    private sealed class WorkingLine(
        long id,
        decimal currentStock,
        decimal? physicalStock,
        decimal? difference,
        string? remarks)
    {
        public long Id { get; } = id;
        public decimal CurrentStock { get; } = currentStock;
        public decimal? PhysicalStock { get; set; } = physicalStock;
        public decimal? Difference { get; set; } = difference;
        public string? Remarks { get; set; } = remarks;
    }
}
