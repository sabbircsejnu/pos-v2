using RetailPOS.Application.Common.Formatting;
using System.Text;
using ClosedXML.Excel;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.API.Services.Payroll;

public sealed class PayrollAdvancedReportExportRenderer
{
    private static readonly string[] Headers =
    [
        "Employee ID", "Employee Code", "Employee Name", "Period ID", "Period Code", "Run ID",
        "Run Revision", "Run Type", "Original Run ID", "Category", "Component Code",
        "Component Name", "Gross", "Deductions", "Net", "Status", "Location ID",
        "Location Name", "Effective Date", "Effective To", "Salary Version ID", "Opening",
        "Recovered", "Outstanding", "Detail"
    ];

    public byte[] RenderCsv(IReadOnlyList<PayrollAdvancedReportRow> rows, string dateFormat = "yyyy-MM-dd")
    {
        EnsureBounded(rows);
        var output = new StringBuilder();
        AppendRow(output, Headers);
        foreach (var row in rows)
            AppendRow(output, Values(row).Select(value => CsvValue(value, dateFormat)));
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(output.ToString())).ToArray();
    }

    public byte[] RenderExcel(IReadOnlyList<PayrollAdvancedReportRow> rows, string dateFormat = "yyyy-MM-dd")
    {
        EnsureBounded(rows);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Payroll Report");
        sheet.SheetView.FreezeRows(1);
        for (var index = 0; index < Headers.Length; index++)
        {
            sheet.Cell(1, index + 1).Value = Headers[index];
            sheet.Cell(1, index + 1).Style.Font.Bold = true;
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var values = Values(rows[rowIndex]);
            for (var columnIndex = 0; columnIndex < values.Length; columnIndex++)
            {
                var cell = sheet.Cell(rowIndex + 2, columnIndex + 1);
                if (values[columnIndex] is null)
                    continue;
                if (values[columnIndex] is string text)
                    cell.Value = SafeText(text);
                else
                    cell.Value = UiDateFormatting.Format(values[columnIndex]!, dateFormat);
            }
        }

        sheet.Range(1, 1, Math.Max(1, rows.Count + 1), Headers.Length).SetAutoFilter();
        sheet.Columns(1, Headers.Length).AdjustToContents(8, 40);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static object?[] Values(PayrollAdvancedReportRow row) =>
    [
        row.EmployeeId, row.EmployeeCode, row.EmployeeName, row.PeriodId, row.PeriodCode,
        row.RunId, row.RunRevision, row.RunType, row.OriginalRunId, row.Category,
        row.ComponentCode, row.ComponentName, row.Gross, row.Deductions, row.Net,
        row.Status, row.LocationId, row.LocationName, row.EffectiveDate, row.EffectiveTo,
        row.SalaryVersionId, row.Opening, row.Recovered, row.Outstanding, row.Detail
    ];

    private static void EnsureBounded(IReadOnlyCollection<PayrollAdvancedReportRow> rows)
    {
        if (rows.Count > PayrollAdvancedReportLimits.MaximumExportRows)
            throw new ArgumentException(
                $"A Payroll report export cannot exceed {PayrollAdvancedReportLimits.MaximumExportRows} rows.");
    }

    private static void AppendRow(StringBuilder output, IEnumerable<string> values) =>
        output.AppendLine(string.Join(",", values.Select(value => $"\"{value.Replace("\"", "\"\"")}\"")));

    private static string CsvValue(object? value, string dateFormat) =>
        value is null ? string.Empty : SafeText(UiDateFormatting.Format(value, dateFormat));

    private static string SafeText(string value)
    {
        var firstMeaningful = value.TrimStart(' ', '\t', '\r', '\n');
        return firstMeaningful.Length > 0 && firstMeaningful[0] is '=' or '+' or '-' or '@'
            ? "'" + value
            : value;
    }
}
