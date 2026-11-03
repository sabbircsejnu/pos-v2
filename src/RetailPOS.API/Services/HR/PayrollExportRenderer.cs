using RetailPOS.Application.Common.Formatting;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RetailPOS.Application.HR.Payroll;

namespace RetailPOS.API.Services.HR;

public sealed class PayrollExportRenderer : IPayrollExportRenderer
{
    private static readonly UTF8Encoding CsvEncoding = new(encoderShouldEmitUTF8Identifier: true);

    public byte[] RenderCsv(PayrollExportSource source)
    {
        var columns = PayrollExportLayout.GetComponentColumns(source);
        var headers = PayrollExportLayout.GetHeaders(source, columns);
        var output = new StringBuilder();
        AppendCsvRow(output, headers.Select(CsvText));

        foreach (var employee in source.Employees)
        {
            var values = PayrollExportLayout.GetComponentValues(employee);
            var row = PayrollExportLayout.GetFixedTextValues(source.Run, employee, source.DateFormat)
                .Select(CsvText)
                .Concat(columns.Select(column => values.TryGetValue(column.Key, out var amount)
                    ? CsvNumber(amount)
                    : string.Empty))
                .Append(CsvNumber(employee.GrossEarnings))
                .Append(CsvNumber(employee.TotalDeductions))
                .Append(CsvNumber(employee.NetPayable));
            if (source.IncludesPaymentData)
            {
                row = row
                    .Append(employee.Payment is null ? string.Empty : CsvNumber(employee.Payment.PaidAmount))
                    .Append(employee.Payment is null ? string.Empty : CsvNumber(employee.Payment.OutstandingAmount))
                    .Append(CsvText(employee.Payment?.Status))
                    .Append(CsvText(employee.Payment is null
                        ? null
                        : string.Join(", ", employee.Payment.ReconciliationStatuses)));
            }
            AppendCsvRow(output, row);
        }

        var totals = Enumerable.Repeat(string.Empty, headers.Count).ToArray();
        totals[0] = CsvText("TOTAL");
        var totalStart = PayrollExportLayout.FixedTextColumnCount + columns.Count;
        totals[totalStart] = CsvNumber(source.GrossTotal);
        totals[totalStart + 1] = CsvNumber(source.TotalDeductions);
        totals[totalStart + 2] = CsvNumber(source.NetPayable);
        if (source.IncludesPaymentData)
        {
            totals[totalStart + 3] = CsvNumber(source.PaidTotal ?? 0m);
            totals[totalStart + 4] = CsvNumber(source.OutstandingTotal ?? 0m);
        }
        AppendCsvRow(output, totals);

        var body = CsvEncoding.GetBytes(output.ToString());
        var preamble = CsvEncoding.GetPreamble();
        var content = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, content, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, content, preamble.Length, body.Length);
        return content;
    }

    public byte[] RenderExcel(PayrollExportSource source)
    {
        var columns = PayrollExportLayout.GetComponentColumns(source);
        var headers = PayrollExportLayout.GetHeaders(source, columns);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Payroll Register");
        sheet.SheetView.FreezeRows(1);

        for (var columnIndex = 0; columnIndex < headers.Count; columnIndex++)
        {
            var cell = sheet.Cell(1, columnIndex + 1);
            SetText(cell, headers[columnIndex]);
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
            cell.Style.Font.FontColor = XLColor.White;
        }

        var rowIndex = 2;
        var amountFormat = AmountFormat(source.Run.DecimalPrecision);
        foreach (var employee in source.Employees)
        {
            var componentValues = PayrollExportLayout.GetComponentValues(employee);
            var textValues = PayrollExportLayout.GetFixedTextValues(source.Run, employee, source.DateFormat);
            var columnIndex = 1;
            for (var fixedIndex = 0; fixedIndex < textValues.Count; fixedIndex++)
            {
                var cell = sheet.Cell(rowIndex, columnIndex++);
                switch (fixedIndex)
                {
                    case 9:
                        SetDate(cell, source.Run.PeriodStart, source.DateFormat);
                        break;
                    case 10:
                        SetDate(cell, source.Run.PeriodEnd, source.DateFormat);
                        break;
                    case 11:
                        SetDate(cell, source.Run.PayDate, source.DateFormat);
                        break;
                    default:
                        SetText(cell, textValues[fixedIndex]);
                        break;
                }
            }
            foreach (var column in columns)
            {
                if (componentValues.TryGetValue(column.Key, out var amount))
                    SetNumber(sheet.Cell(rowIndex, columnIndex), amount, amountFormat);
                columnIndex++;
            }
            SetNumber(sheet.Cell(rowIndex, columnIndex++), employee.GrossEarnings, amountFormat);
            SetNumber(sheet.Cell(rowIndex, columnIndex++), employee.TotalDeductions, amountFormat);
            SetNumber(sheet.Cell(rowIndex, columnIndex++), employee.NetPayable, amountFormat);
            if (source.IncludesPaymentData)
            {
                if (employee.Payment is not null)
                {
                    SetNumber(sheet.Cell(rowIndex, columnIndex), employee.Payment.PaidAmount, amountFormat);
                    SetNumber(sheet.Cell(rowIndex, columnIndex + 1), employee.Payment.OutstandingAmount, amountFormat);
                }
                columnIndex += 2;
                SetText(sheet.Cell(rowIndex, columnIndex++), employee.Payment?.Status);
                SetText(sheet.Cell(rowIndex, columnIndex),
                    employee.Payment is null
                        ? string.Empty
                        : string.Join(", ", employee.Payment.ReconciliationStatuses));
            }
            rowIndex++;
        }

        var totalRow = rowIndex;
        SetText(sheet.Cell(totalRow, 1), "TOTAL");
        var totalColumn = PayrollExportLayout.FixedTextColumnCount + columns.Count + 1;
        SetNumber(sheet.Cell(totalRow, totalColumn++), source.GrossTotal, amountFormat);
        SetNumber(sheet.Cell(totalRow, totalColumn++), source.TotalDeductions, amountFormat);
        SetNumber(sheet.Cell(totalRow, totalColumn++), source.NetPayable, amountFormat);
        if (source.IncludesPaymentData)
        {
            SetNumber(sheet.Cell(totalRow, totalColumn++), source.PaidTotal ?? 0m, amountFormat);
            SetNumber(sheet.Cell(totalRow, totalColumn), source.OutstandingTotal ?? 0m, amountFormat);
        }
        sheet.Range(1, 1, totalRow, headers.Count).SetAutoFilter();
        sheet.Range(totalRow, 1, totalRow, headers.Count).Style.Font.Bold = true;
        sheet.Range(1, 1, 1, headers.Count).Style.Alignment.WrapText = true;
        sheet.Columns(1, headers.Count).AdjustToContents(8, 40);
        sheet.Row(1).Height = 32;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] RenderPdfSummary(PayrollExportSource source)
    {
        var run = source.Run;
        var period = $"{UiDateFormatting.Format(run.PeriodStart, source.DateFormat)} to {UiDateFormatting.Format(run.PeriodEnd, source.DateFormat)}";
        var bytes = Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(style => style.FontSize(10));
                page.Header().Text("Payroll Summary").FontSize(20).SemiBold();
                page.Content().PaddingVertical(14).Column(column =>
                {
                    column.Spacing(8);
                    column.Item().Text($"Business: {run.BusinessName}");
                    column.Item().Text($"Payroll Period: {run.PeriodCode} ({period})");
                    if (run.PayDate is DateOnly payDate)
                        column.Item().Text($"Pay Date: {UiDateFormatting.Format(payDate, source.DateFormat)}");
                    column.Item().Text(
                        $"Run: {run.RunReference} · ID {run.RunId} · Type {run.RunType} · Revision {run.Revision}");
                    column.Item().Text($"Run Status: {run.Status}");
                    column.Item().Text($"Currency: {run.CurrencyCode}");
                    column.Item().Text($"Employee Count: {source.Employees.Count}");
                    column.Item().PaddingTop(8).Text("Authoritative Payroll Totals").SemiBold();
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(2);
                        });
                        AddPdfRow(table, "Gross Earnings", FormatAmount(source.GrossTotal, run));
                        AddPdfRow(table, "Total Deductions", FormatAmount(source.TotalDeductions, run));
                        AddPdfRow(table, "Net Payable", FormatAmount(source.NetPayable, run));
                        if (source.IncludesPaymentData)
                        {
                            AddPdfRow(table, "Paid Total", FormatAmount(source.PaidTotal ?? 0m, run));
                            AddPdfRow(table, "Outstanding Total",
                                FormatAmount(source.OutstandingTotal ?? 0m, run));
                            AddPdfRow(table, "Payment Status Summary",
                                FormatStatusCounts(source.PaymentStatuses));
                            AddPdfRow(table, "Reconciliation Status Summary",
                                FormatStatusCounts(source.ReconciliationStatuses));
                        }
                    });
                    column.Item().PaddingTop(12).Text(
                        "Amounts are sourced from persisted Payroll employee results.");
                });
                page.Footer().AlignRight().Text($"Run {run.RunId} · {run.RunType} · Revision {run.Revision}");
            });
        }).GeneratePdf();
        return bytes;
    }

    private static void AddPdfRow(TableDescriptor table, string label, string value)
    {
        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).Text(label);
        table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5)
            .AlignRight().Text(value);
    }

    private static string FormatAmount(decimal amount, PayrollExportRun run) =>
        $"{run.CurrencyCode} {amount.ToString($"N{run.DecimalPrecision}", CultureInfo.InvariantCulture)}";

    private static string FormatStatusCounts(IReadOnlyList<PayrollExportStatusCount> statuses) =>
        statuses.Count == 0
            ? "None"
            : string.Join(", ", statuses.Select(row => $"{row.Status}: {row.Count}"));

    private static string AmountFormat(int precision) =>
        precision == 0 ? "#,##0" : $"#,##0.{new string('0', precision)}";

    private static void SetText(IXLCell cell, string? value)
    {
        cell.Value = value ?? string.Empty;
    }

    private static void SetNumber(IXLCell cell, decimal value, string format)
    {
        cell.Value = value;
        cell.Style.NumberFormat.Format = format;
    }

    private static void SetDate(IXLCell cell, DateOnly? value, string dateFormat)
    {
        if (value is DateOnly date)
            cell.Value = date.ToDateTime(TimeOnly.MinValue);
        cell.Style.NumberFormat.Format = UiDateFormatting.Normalize(dateFormat).ToLowerInvariant();
    }

    private static string CsvText(string? value)
    {
        var text = value ?? string.Empty;
        if (IsFormulaLike(text))
            text = $"'{text}";
        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static bool IsFormulaLike(string value)
    {
        foreach (var character in value)
        {
            if (character is '=' or '+' or '-' or '@')
                return true;
            if (!char.IsWhiteSpace(character) && !char.IsControl(character))
                return false;
        }
        return false;
    }

    private static string CsvNumber(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static void AppendCsvRow(StringBuilder output, IEnumerable<string> fields)
    {
        output.AppendJoin(',', fields);
        output.Append("\r\n");
    }
}

internal static class PayrollExportLayout
{
    public const int FixedTextColumnCount = 21;

    public static IReadOnlyList<PayrollExportComponentColumn> GetComponentColumns(
        PayrollExportSource source)
    {
        var keys = new HashSet<PayrollExportComponentKey>();
        foreach (var employee in source.Employees)
        {
            foreach (var line in GetComponentOccurrences(employee))
                keys.Add(line.Key);
        }
        return keys
            .OrderBy(key => key.Type, StringComparer.Ordinal)
            .ThenBy(key => key.Code, StringComparer.Ordinal)
            .ThenBy(key => key.Name, StringComparer.Ordinal)
            .ThenBy(key => key.Occurrence)
            .Select(key => new PayrollExportComponentColumn(
                key,
                $"{key.Name} [{key.Code}] ({key.Type})"
                    + (key.Occurrence == 1 ? string.Empty : $" #{key.Occurrence}")))
            .ToArray();
    }

    public static IReadOnlyList<string> GetHeaders(
        PayrollExportSource source, IReadOnlyList<PayrollExportComponentColumn> componentColumns)
    {
        var headers = new List<string>
        {
            "Employee Code",
            "Employee Name",
            "Historical Position",
            "Historical Position Code",
            "Historical Location",
            "Historical Location Reference",
            "Historical Location Type",
            "Historical Location ID",
            "Period Code",
            "Period Start",
            "Period End",
            "Pay Date",
            "Currency",
            "Run ID",
            "Run Number",
            "Run Reference",
            "Run Type",
            "Run Revision",
            "Original Run ID",
            "Correction Reference",
            "Correction Type"
        };
        headers.AddRange(componentColumns.Select(column => column.Header));
        headers.AddRange(["Gross Earnings", "Total Deductions", "Net Payable"]);
        if (source.IncludesPaymentData)
            headers.AddRange(["Paid Amount", "Outstanding Amount", "Payment Status", "Reconciliation Status"]);
        return headers;
    }

    public static IReadOnlyList<string?> GetFixedTextValues(
        PayrollExportRun run, PayrollExportEmployee employee, string dateFormat) =>
    [
        employee.EmployeeCode,
        employee.EmployeeName,
        employee.HistoricalPositionName,
        employee.HistoricalPositionCode,
        employee.HistoricalLocationName,
        employee.HistoricalLocationReference,
        employee.HistoricalLocationType,
        employee.HistoricalLocationId?.ToString(CultureInfo.InvariantCulture),
        run.PeriodCode,
        UiDateFormatting.Format(run.PeriodStart, dateFormat),
        UiDateFormatting.Format(run.PeriodEnd, dateFormat),
        run.PayDate.HasValue ? UiDateFormatting.Format(run.PayDate.Value, dateFormat) : null,
        run.CurrencyCode,
        run.RunId.ToString(CultureInfo.InvariantCulture),
        run.RunNo.ToString(CultureInfo.InvariantCulture),
        run.RunReference,
        run.RunType,
        run.Revision.ToString(CultureInfo.InvariantCulture),
        run.OriginalRunId?.ToString(CultureInfo.InvariantCulture),
        run.CorrectionReference,
        run.CorrectionType
    ];

    public static IReadOnlyDictionary<PayrollExportComponentKey, decimal> GetComponentValues(
        PayrollExportEmployee employee) =>
        GetComponentOccurrences(employee).ToDictionary(row => row.Key, row => row.Line.Amount);

    private static IReadOnlyList<PayrollExportComponentOccurrence> GetComponentOccurrences(
        PayrollExportEmployee employee)
    {
        var occurrences = new Dictionary<(string Type, string Code, string Name), int>();
        var rows = new List<PayrollExportComponentOccurrence>();
        foreach (var line in employee.Lines
            .OrderBy(row => row.Order)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .ThenBy(row => row.Name, StringComparer.Ordinal)
            .ThenBy(row => row.Id))
        {
            var identity = (line.Type, line.Code, line.Name);
            occurrences.TryGetValue(identity, out var occurrence);
            occurrence++;
            occurrences[identity] = occurrence;
            rows.Add(new PayrollExportComponentOccurrence(
                new PayrollExportComponentKey(line.Type, line.Code, line.Name, occurrence), line));
        }
        return rows;
    }
}

internal sealed record PayrollExportComponentKey(
    string Type, string Code, string Name, int Occurrence);

internal sealed record PayrollExportComponentColumn(
    PayrollExportComponentKey Key, string Header);

internal sealed record PayrollExportComponentOccurrence(
    PayrollExportComponentKey Key, PayrollExportComponentLine Line);
