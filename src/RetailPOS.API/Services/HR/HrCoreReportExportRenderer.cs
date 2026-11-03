using RetailPOS.Application.Common.Formatting;
using System.Text;
using ClosedXML.Excel;
using RetailPOS.Application.HR.Reports;

namespace RetailPOS.API.Services.HR;

public sealed class HrCoreReportExportRenderer
{
    public const int MaximumRows = HrCoreReportLimits.MaximumExportRows;

    private static readonly string[] Headers =
    [
        "Employee ID", "Employee Code", "Employee Name", "Category", "Effective Date", "Effective To",
        "Status", "Location", "Detail", "Metric", "Employment Type", "Department", "Position",
        "Location Type", "Current", "Actor User ID", "Occurred At UTC", "Quantity", "Completion Mode",
        "Payroll Enabled", "Scheduled Days", "Present Days", "Absent Days", "Late Days", "Holiday Days",
        "Weekly Off Days", "Approved Leave Units", "Worked Minutes", "Approved Overtime Entries",
        "Opening", "Accrued", "Used", "Restored", "Adjusted", "Carried Forward", "Expired", "Balance",
        "Mandatory Clearance Items", "Completed Mandatory Clearance Items"
    ];

    public byte[] RenderCsv(IReadOnlyList<HrCoreReportRow> rows, string dateFormat = "yyyy-MM-dd")
    {
        EnsureBounded(rows);
        var output = new StringBuilder();
        AppendRow(output, Headers);
        foreach (var row in rows)
            AppendRow(output, Values(row).Select(value => CsvValue(value, dateFormat)));
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(output.ToString())).ToArray();
    }

    public byte[] RenderExcel(IReadOnlyList<HrCoreReportRow> rows, string dateFormat = "yyyy-MM-dd")
    {
        EnsureBounded(rows);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("HR Report");
        sheet.SheetView.FreezeRows(1);
        for (var index = 0; index < Headers.Length; index++)
        {
            var cell = sheet.Cell(1, index + 1);
            cell.Value = Headers[index];
            cell.Style.Font.Bold = true;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var values = Values(rows[rowIndex]);
            for (var columnIndex = 0; columnIndex < values.Length; columnIndex++)
            {
                var cell = sheet.Cell(rowIndex + 2, columnIndex + 1);
                SetCell(cell, values[columnIndex], dateFormat);
            }
        }

        sheet.Range(1, 1, Math.Max(1, rows.Count + 1), Headers.Length).SetAutoFilter();
        sheet.Columns(1, Headers.Length).AdjustToContents(8, 40);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static object?[] Values(HrCoreReportRow row)
    {
        var measures = row.Measures;
        return
        [
            row.EmployeeId, row.EmployeeCode, row.EmployeeName, row.Category, row.EffectiveDate,
            row.EffectiveTo, row.Status, row.LocationName, row.Detail, row.Metric, row.EmploymentType,
            row.Department, row.Position, row.LocationType, row.IsCurrent, row.ActorUserId,
            row.OccurredAtUtc, row.Quantity, row.CompletionMode, row.PayrollEnabled,
            measures?.ScheduledDays, measures?.PresentDays, measures?.AbsentDays, measures?.LateDays,
            measures?.HolidayDays, measures?.WeeklyOffDays, measures?.ApprovedLeaveUnits,
            measures?.WorkedMinutes, measures?.ApprovedOvertimeEntries, measures?.Opening,
            measures?.Accrued, measures?.Used, measures?.Restored, measures?.Adjusted,
            measures?.CarriedForward, measures?.Expired, measures?.Balance,
            measures?.MandatoryClearanceItems, measures?.CompletedMandatoryClearanceItems
        ];
    }

    private static void EnsureBounded(IReadOnlyCollection<HrCoreReportRow> rows)
    {
        if (rows.Count > MaximumRows)
            throw new ArgumentException($"An HR report export cannot exceed {MaximumRows} rows.");
    }

    private static void AppendRow(StringBuilder output, IEnumerable<string> values) =>
        output.AppendLine(string.Join(",", values.Select(value => $"\"{value.Replace("\"", "\"\"")}\"")));

    private static string CsvValue(object? value, string dateFormat) =>
        value is null ? string.Empty : SafeText(UiDateFormatting.Format(value, dateFormat));

    private static void SetCell(IXLCell cell, object? value, string dateFormat)
    {
        if (value is null)
        {
            cell.Clear(XLClearOptions.Contents);
            return;
        }

        switch (value)
        {
            case int number: cell.Value = number; break;
            case long number: cell.Value = number; break;
            case decimal number: cell.Value = number; break;
            case bool boolean: cell.Value = boolean; break;
            default:
                cell.Value = SafeText(UiDateFormatting.Format(value, dateFormat));
                break;
        }
    }

    private static string SafeText(string value)
    {
        var firstMeaningful = value.TrimStart(' ', '\t', '\r', '\n');
        return firstMeaningful.Length > 0 && firstMeaningful[0] is '=' or '+' or '-' or '@'
            ? "'" + value
            : value;
    }
}
