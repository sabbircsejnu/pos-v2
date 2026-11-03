using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Application.Common.Formatting;

namespace RetailPOS.Infrastructure.HR.Payroll;

public sealed class PayrollPayslipPdfRenderer : IPayrollPayslipRenderer
{
    public byte[] Render(PayrollPayslipSource source, string templateVersion)
    {
        if (string.IsNullOrWhiteSpace(templateVersion))
            throw new ArgumentException("A versioned Payslip template is required.", nameof(templateVersion));
        if (source.DecimalPrecision is < 0 or > 6)
            throw new InvalidDataException("The frozen Payroll precision is invalid.");

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Margin(36);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(style => style.FontSize(10));
                page.Header().Column(header =>
                {
                    header.Item().Text(source.BusinessName).FontSize(19).Bold();
                    header.Item().Text($"PAYSLIP · {source.RunType}").FontSize(14).SemiBold();
                    header.Item().Text($"Payroll period: {UiDateFormatting.Format(source.PeriodStart, source.DateFormat)} to {UiDateFormatting.Format(source.PeriodEnd, source.DateFormat)}");
                    if (source.PayDate is { } payDate)
                        header.Item().Text($"Pay date: {UiDateFormatting.Format(payDate, source.DateFormat)}");
                });
                page.Content().PaddingVertical(18).Column(content =>
                {
                    content.Spacing(8);
                    content.Item().Column(employee =>
                    {
                        employee.Item().Text($"Employee: {source.EmployeeName}");
                        employee.Item().Text($"Employee code: {source.EmployeeCode}");
                        if (!string.IsNullOrWhiteSpace(source.PositionSnapshot))
                            employee.Item().Text($"Position: {source.PositionSnapshot}");
                        if (!string.IsNullOrWhiteSpace(source.HistoricalLocationName))
                        {
                            var type = string.IsNullOrWhiteSpace(source.HistoricalLocationType)
                                ? string.Empty
                                : $" · {source.HistoricalLocationType}";
                            var reference = string.IsNullOrWhiteSpace(source.HistoricalLocationReference)
                                ? string.Empty
                                : $" · reference {source.HistoricalLocationReference}";
                            employee.Item().Text(
                                $"Historical location: {source.HistoricalLocationName}{type}{reference}");
                        }
                        else if (source.HistoricalLocationId is { } locationId)
                            employee.Item().Text($"Historical location reference: {locationId}");
                        employee.Item().Text($"Currency: {source.CurrencyCode}");
                    });
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(7);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(3);
                        });
                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Type");
                            header.Cell().Element(HeaderCell).Text("Component");
                            header.Cell().Element(HeaderCell).Text("Code");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Amount");
                        });
                        foreach (var line in source.Lines.OrderBy(row => row.Order)
                                     .ThenBy(row => row.Code, StringComparer.Ordinal))
                        {
                            table.Cell().Element(BodyCell).Text(line.Type);
                            table.Cell().Element(BodyCell).Text(line.Name);
                            table.Cell().Element(BodyCell).Text(line.Code);
                            table.Cell().Element(BodyCell).AlignRight()
                                .Text(Amount(line.Amount, source));
                        }
                    });
                    content.Item().AlignRight().Column(totals =>
                    {
                        totals.Item().Text($"Gross earnings: {Amount(source.GrossEarnings, source)}").SemiBold();
                        totals.Item().Text($"Total deductions: {Amount(source.TotalDeductions, source)}").SemiBold();
                        totals.Item().Text($"Net payable: {Amount(source.NetPayable, source)}")
                            .FontSize(13).Bold();
                    });
                    if (source.Payment is { } payment)
                    {
                        content.Item().PaddingTop(8).Column(paymentBlock =>
                        {
                            paymentBlock.Item().Text("Payment summary").SemiBold();
                            paymentBlock.Item().Text($"Method: {payment.Method}");
                            paymentBlock.Item().Text($"Paid: {Amount(payment.PaidAmount, source)}");
                            paymentBlock.Item().Text($"Outstanding: {Amount(payment.OutstandingAmount, source)}");
                            paymentBlock.Item().Text($"Status: {payment.Status}");
                            if (!string.IsNullOrWhiteSpace(payment.ReferenceSummary))
                                paymentBlock.Item().Text($"Reference: {payment.ReferenceSummary}");
                            if (payment.ReconciliationStatuses.Count != 0)
                                paymentBlock.Item().Text(
                                    $"Reconciliation: {string.Join(", ", payment.ReconciliationStatuses)}");
                        });
                    }
                });
                page.Footer().AlignCenter().Text($"Confidential · Template {templateVersion}");
            });
        }).GeneratePdf();
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Colors.Grey.Lighten2).Padding(5).DefaultTextStyle(style => style.Bold());

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(5);

    private static string Amount(decimal value, PayrollPayslipSource source) =>
        $"{source.CurrencyCode} {value.ToString($"N{source.DecimalPrecision}", CultureInfo.InvariantCulture)}";
}
