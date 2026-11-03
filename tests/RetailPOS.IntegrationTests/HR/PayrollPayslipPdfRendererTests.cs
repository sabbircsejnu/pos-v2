using System.Globalization;
using System.Text;
using UglyToad.PdfPig;
using QuestPDF.Infrastructure;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Infrastructure.HR.Payroll;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollPayslipPdfRendererTests
{
    [Theory]
    [InlineData("yyyy-MM-dd", "2026-10-01", "2026-10-31", "2026-11-05")]
    [InlineData("dd/MM/yyyy", "01/10/2026", "31/10/2026", "05/11/2026")]
    public void RenderProducesPdfFromThePersistedPayrollValues(
        string dateFormat, string from, string to, string payDate)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var source = new PayrollPayslipSource(
            1, "IraniMart Company", 17, 2, "Correction", "CORR-17", "BDT", 2,
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
            new DateOnly(2026, 11, 5), 42, "EMP-42", "Test Employee", 10, "Historical Position (POS-7)",
            "employee@example.invalid", 91, 2, new string('A', 64),
            1200.50m, 99.10m, 1101.40m,
            [
                new PayrollPayslipLine("BASE", "Base salary", "Earning", 1000.25m, 1),
                new PayrollPayslipLine("ALW", "Transport allowance", "Earning", 200.25m, 2),
                new PayrollPayslipLine("TAX", "Tax deduction", "Deduction", 99.10m, 3)
            ],
            new PayrollPayslipPayment("Bank Transfer", 800m, 301.40m, "PartiallyPaid",
                "…4321", ["Matched"]),
            "Historical Outlet", "OUT-10", "Outlet") { DateFormat = dateFormat };

        var pdf = new PayrollPayslipPdfRenderer().Render(source, "payslip-v1");

        Assert.True(pdf.Length > 100);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        using var document = PdfDocument.Open(pdf);
        var text = string.Join('\n', document.GetPages().Select(page => page.Text));

        Assert.Contains(source.BusinessName, text);
        Assert.Contains(source.EmployeeName, text);
        Assert.Contains(source.EmployeeCode, text);
        Assert.Contains("Historical Position (POS-7)", text);
        Assert.Contains("Historical location: Historical Outlet", text);
        Assert.Contains("reference OUT-10", text);
        Assert.Contains($"Payroll period: {from} to {to}", text);
        Assert.Contains($"Pay date: {payDate}", text);
        Assert.Contains($"Currency: {source.CurrencyCode}", text);
        Assert.Contains("Base salary", text);
        Assert.Contains("Transport allowance", text);
        Assert.Contains("Tax deduction", text);
        Assert.Contains($"Gross earnings: {Format(source.GrossEarnings, source)}", text);
        Assert.Contains($"Total deductions: {Format(source.TotalDeductions, source)}", text);
        Assert.Contains($"Net payable: {Format(source.NetPayable, source)}", text);
        Assert.Contains(Format(source.Lines[0].Amount, source), text);
        Assert.Contains($"Method: {source.Payment!.Method}", text);
        Assert.Contains($"Paid: {Format(source.Payment.PaidAmount, source)}", text);
        Assert.Contains($"Outstanding: {Format(source.Payment.OutstandingAmount, source)}", text);
        Assert.Contains($"Status: {source.Payment.Status}", text);
        Assert.Contains($"Reconciliation: {string.Join(", ", source.Payment.ReconciliationStatuses)}", text);
    }

    private static string Format(decimal amount, PayrollPayslipSource source) =>
        $"{source.CurrencyCode} {amount.ToString($"N{source.DecimalPrecision}", CultureInfo.InvariantCulture)}";
}
