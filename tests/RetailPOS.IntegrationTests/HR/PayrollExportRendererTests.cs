using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Infrastructure;
using RetailPOS.API.Services.HR;
using RetailPOS.API.Services.Payroll;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Application.HR.Reports;
using UglyToad.PdfPig;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollExportRendererTests
{
    private readonly PayrollExportRenderer _renderer = new();

    [Fact]
    public void HrAndAdvancedPayrollReportsUseCompanyFormatInCsvAndExcel()
    {
        var date = new DateOnly(2026, 10, 5);
        var hr = new HrCoreReportExportRenderer();
        HrCoreReportRow[] hrRows = [new(1, "E1", "Employee", "Assignment", date, "Active", null, null, null)];
        Assert.Contains("\"05/10/2026\"", Encoding.UTF8.GetString(hr.RenderCsv(hrRows, "dd/MM/yyyy")));
        using var hrStream = new MemoryStream(hr.RenderExcel(hrRows, "dd/MM/yyyy"));
        using var hrWorkbook = new XLWorkbook(hrStream);
        Assert.Equal("05/10/2026", hrWorkbook.Worksheet("HR Report").Cell(2, 5).GetString());

        var payroll = new PayrollAdvancedReportExportRenderer();
        PayrollAdvancedReportRow[] payrollRows = [
            new(1, "E1", "Employee", null, null, null, null, null, null, "Salary",
                null, null, null, null, null, "Active", null, null, date, null, null, null, null, null, null)
        ];
        Assert.Contains("\"05/10/2026\"", Encoding.UTF8.GetString(payroll.RenderCsv(payrollRows, "dd/MM/yyyy")));
        using var payrollStream = new MemoryStream(payroll.RenderExcel(payrollRows, "dd/MM/yyyy"));
        using var payrollWorkbook = new XLWorkbook(payrollStream);
        Assert.Equal("05/10/2026", payrollWorkbook.Worksheet("Payroll Report").Cell(2, 19).GetString());
    }

    [Theory]
    [InlineData("dd/MM/yyyy", "01/10/2026", "05/11/2026")]
    [InlineData("MM/DD/YYYY", "10/01/2026", "11/05/2026")]
    public void ConfiguredCompanyFormatAppliesToCsvExcelAndPdf(string format, string periodStart, string payDate)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var source = CreateSource("Employee") with { DateFormat = format };

        Assert.Contains($"\"{periodStart}\"", Encoding.UTF8.GetString(_renderer.RenderCsv(source)));
        using var stream = new MemoryStream(_renderer.RenderExcel(source));
        using var workbook = new XLWorkbook(stream);
        var cell = workbook.Worksheet("Payroll Register").Cell(2, 10);
        Assert.Equal(XLDataType.DateTime, cell.DataType);
        Assert.Equal(periodStart, cell.GetFormattedString());

        using var pdf = PdfDocument.Open(_renderer.RenderPdfSummary(source));
        Assert.Contains($"Pay Date: {payDate}", string.Join('\n', pdf.GetPages().Select(page => page.Text)));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"https://example.invalid\")")]
    [InlineData("+SUM(1,1)")]
    [InlineData("-1+2")]
    [InlineData("@SUM(1,1)")]
    public void CsvEscapesFormulaLikeTextAndExportsPersistedAmounts(string formulaLikeText)
    {
        var source = CreateSource(formulaLikeText);

        var csv = Encoding.UTF8.GetString(_renderer.RenderCsv(source));

        var expectedEscapedText = "\"" + ("'" + formulaLikeText).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        Assert.Contains(expectedEscapedText, csv);
        Assert.Contains(",1250.75,75.25,1175.50,800,375.50,", csv);
        Assert.Contains("\"Base Salary [BASE] (Earning)\"", csv);
    }

    [Theory]
    [InlineData("=SUM(1,1)")]
    [InlineData("+SUM(1,1)")]
    [InlineData("-1+2")]
    [InlineData("@SUM(1,1)")]
    public void ExcelKeepsFormulaLikeTextAsTextAndMonetaryCellsNumeric(string formulaLikeText)
    {
        var source = CreateSource(formulaLikeText);
        using var stream = new MemoryStream(_renderer.RenderExcel(source));
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Payroll Register");
        var employeeName = sheet.Row(2).CellsUsed().Single(cell =>
            cell.Value.ToString() == formulaLikeText);
        var gross = sheet.Row(2).CellsUsed().Single(cell =>
            cell.Value.ToString() == "1250.75");
        var component = sheet.Row(2).CellsUsed().Single(cell =>
            cell.Value.ToString() == "1000.5");

        Assert.Equal(XLDataType.Text, employeeName.DataType);
        Assert.Equal(string.Empty, employeeName.FormulaA1);
        Assert.Equal(XLDataType.Number, gross.DataType);
        Assert.Equal(XLDataType.Number, component.DataType);
        Assert.Equal("2026-10-01", sheet.Cell(2, 10).GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void PdfSummaryRendersPersistedPayrollTotalsAndPermittedPaymentSummary()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var source = CreateSource("Employee");

        var pdf = _renderer.RenderPdfSummary(source);

        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        using var document = PdfDocument.Open(pdf);
        var text = string.Join('\n', document.GetPages().Select(page => page.Text));

        Assert.Contains("IraniMart Business", text);
        Assert.Contains("Payroll Period: 2026-10", text);
        Assert.Contains("Pay Date: 2026-11-05", text);
        Assert.Contains("Currency: BDT", text);
        Assert.Contains("Gross Earnings", text);
        Assert.Contains("BDT 1,250.75", text);
        Assert.Contains("Total Deductions", text);
        Assert.Contains("BDT 75.25", text);
        Assert.Contains("Net Payable", text);
        Assert.Contains("BDT 1,175.50", text);
        Assert.Contains("Paid Total", text);
        Assert.Contains("BDT 800.00", text);
        Assert.Contains("Outstanding Total", text);
        Assert.Contains("BDT 375.50", text);
        Assert.Contains("PartiallyPaid: 1", text);
        Assert.Contains("Matched: 1", text);
        Assert.Contains("persisted Payroll employee results", text);
    }

    private static PayrollExportSource CreateSource(string employeeName)
    {
        var run = new PayrollExportRun(
            1, "IraniMart Business", 12, 4, 2, "Regular", "PAY-2026-10", null,
            null, null, "Business", null, "Approved", 3, "BDT", 2, "2026-10",
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
            new DateOnly(2026, 11, 5));
        var employee = new PayrollExportEmployee(
            42, "EMP-42", employeeName, 7, "Historical Outlet", "OUT-7", "Outlet",
            "Historical Position", "POS-3", 1250.75m, 75.25m, 1175.50m,
            [
                new PayrollExportComponentLine(1, "BASE", "Base Salary", "Earning", 1000.50m, 1),
                new PayrollExportComponentLine(2, "ALW", "Transport", "Earning", 250.25m, 2),
                new PayrollExportComponentLine(3, "TAX", "Tax", "Deduction", 75.25m, 3)
            ],
            new PayrollExportPayment(800m, 375.50m, "PartiallyPaid", ["Matched"]));
        return new PayrollExportSource(
            run, [employee], true, 1250.75m, 75.25m, 1175.50m, 800m, 375.50m,
            [new PayrollExportStatusCount("PartiallyPaid", 1)],
            [new PayrollExportStatusCount("Matched", 1)]);
    }
}
