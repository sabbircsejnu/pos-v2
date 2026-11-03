using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RetailPOS.Application.Pos.Abstractions;

namespace RetailPOS.Infrastructure.Pos;

/// <summary>
/// Closing summary PDF (FR-CS-05). Every cash figure comes from the immutable close snapshot; only the variance decision
/// and the handover confirmation are current workflow states, and they are printed as such with the generation time.
/// </summary>
public sealed class CashSessionCloseSummaryPdfRenderer : ICashSessionCloseSummaryPdfRenderer
{
    public byte[] Render(CashSessionDetailReadModel detail, DateTime generatedAtUtc)
    {
        var snapshot = detail.CloseSnapshot
            ?? throw new InvalidOperationException("Only a closed cash session has a closing summary.");
        var session = detail.Session;
        var zone = ResolveZone(session.TimeZoneId);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(style => style.FontSize(10).FontFamily("Helvetica"));

                page.Header().Column(header =>
                {
                    header.Item().Text("Cash Session Closing Summary").SemiBold().FontSize(15);
                    header.Item().Text($"{detail.OutletName} · {detail.TerminalName} · Session #{session.Id}").FontSize(10);
                });

                page.Content().PaddingVertical(10).Column(column =>
                {
                    column.Spacing(6);
                    Section(column, "Session", table =>
                    {
                        Row(table, "Business date", session.BusinessDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
                        Row(table, "Time zone", session.TimeZoneId);
                        Row(table, "Opened", $"{Local(session.OpenedAt, zone)} by {detail.OpenedByName ?? $"user {session.OpenedByUserId}"}");
                        Row(table, "Closed", $"{Local(snapshot.ClosedAtUtc, zone)} by {detail.ClosedByName ?? $"user {snapshot.ClosedByUserId}"}");
                        Row(table, "Duration limit", $"{session.MaxDurationHours} h (snapshot at opening)");
                        Row(table, "Overdue at close", snapshot.WasOverdueAtClose ? "Yes" : "No");
                        Row(table, "Sales in session", detail.Summary.SaleCount.ToString(CultureInfo.InvariantCulture));
                    });

                    Section(column, "Cash reconciliation (frozen at close)", table =>
                    {
                        Row(table, "Opening cash", Money(snapshot.OpeningCash));
                        Row(table, "Cash sales retained", Money(snapshot.CashSalesRetained));
                        Row(table, "Cash tendered / change given", $"{Money(snapshot.CashTendered)} / {Money(snapshot.ChangeGiven)}");
                        Row(table, "Drawer expenses paid", $"- {Money(snapshot.DrawerExpenses)}");
                        Row(table, "Cash refunds paid", $"- {Money(snapshot.CashRefunds)}");
                        Row(table, "Approved corrections", Money(snapshot.Corrections));
                        Row(table, "Expected cash", Money(snapshot.ExpectedCash), bold: true);
                        Row(table, "Counted cash", Money(snapshot.CountedCash), bold: true);
                        Row(table, "Variance (counted - expected)", Money(snapshot.Variance), bold: true);
                        if (!string.IsNullOrWhiteSpace(snapshot.VarianceReason))
                            Row(table, "Variance reason", snapshot.VarianceReason!);
                        Row(table, "Card sales (not in drawer)", Money(snapshot.CardTotal));
                        Row(table, "Mobile sales (not in drawer)", Money(snapshot.MobileTotal));
                    });

                    Section(column, "Handover", table =>
                    {
                        Row(table, "Handed over", Money(snapshot.HandedOverTotal));
                        Row(table, "Retained at outlet", Money(snapshot.RetainedAtOutlet));
                        foreach (var handover in detail.Handovers)
                        {
                            var to = handover.RecipientName ?? handover.BankName ?? handover.BankAccountReference ?? "-";
                            Row(table, MethodLabel(handover.Method), $"{Money(handover.Amount)} to {to} ({handover.Status.Replace('_', ' ')})");
                        }
                    });

                    Section(column, "Variance approval (current status)", table =>
                    {
                        if (detail.Variance is null)
                        {
                            Row(table, "Status", snapshot.Variance == 0m ? "No difference to approve" : "Not recorded");
                            return;
                        }

                        Row(table, "Decision", detail.Variance.Status);
                        Row(table, "Accounting posting", detail.Variance.PostingStatus.Replace('_', ' '));
                        if (detail.Variance.ReviewedAtUtc.HasValue)
                            Row(table, "Reviewed", Local(detail.Variance.ReviewedAtUtc.Value, zone));
                    });

                    if (!string.IsNullOrWhiteSpace(snapshot.Notes))
                        column.Item().Text($"Closing notes: {snapshot.Notes}");
                });

                page.Footer().Column(footer =>
                {
                    footer.Item().Text($"Snapshot hash (SHA-256): {snapshot.SnapshotHash}").FontSize(7);
                    footer.Item().Text($"Generated {Local(generatedAtUtc, zone)}. Figures are the immutable closing snapshot; statuses are as of generation.")
                        .FontSize(7);
                });
            });
        }).GeneratePdf();
    }

    private static void Section(ColumnDescriptor column, string title, Action<TableDescriptor> rows)
    {
        column.Item().PaddingTop(4).Text(title).SemiBold().FontSize(11);
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2);
                columns.RelativeColumn(3);
            });
            rows(table);
        });
    }

    private static void Row(TableDescriptor table, string label, string value, bool bold = false)
    {
        table.Cell().BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).Text(label);
        var cell = table.Cell().BorderBottom(0.4f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).AlignRight().Text(value);
        if (bold)
            cell.SemiBold();
    }

    private static string Money(decimal amount) => amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string MethodLabel(string method) => method switch
    {
        "cash_to_manager" => "Cash to manager",
        "cash_to_owner" => "Cash to owner",
        "bank_deposit" => "Bank deposit",
        _ => method
    };

    private static TimeZoneInfo ResolveZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static string Local(DateTime utc, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        return $"{local.ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture)} ({zone.Id})";
    }
}
