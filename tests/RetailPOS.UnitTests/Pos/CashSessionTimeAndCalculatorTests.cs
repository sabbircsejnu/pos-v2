using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.Pos;

public sealed class CashSessionTimeTests
{
    [Theory]
    [InlineData("UTC", "2026-11-03T23:30:00Z", "2026-11-03")]
    [InlineData("UTC", "2026-11-04T00:00:00Z", "2026-11-04")]
    // Asia/Dhaka is UTC+6 all year: 20:00Z is already the next local day.
    [InlineData("Asia/Dhaka", "2026-11-03T17:59:59Z", "2026-11-03")]
    [InlineData("Asia/Dhaka", "2026-11-03T18:00:00Z", "2026-11-04")]
    // America/New_York: EDT (UTC-4) before 1 Nov 2026 06:00Z, EST (UTC-5) afterwards.
    [InlineData("America/New_York", "2026-10-31T03:59:59Z", "2026-10-30")]
    [InlineData("America/New_York", "2026-10-31T04:00:00Z", "2026-10-31")]
    [InlineData("America/New_York", "2026-11-01T03:59:59Z", "2026-10-31")]
    [InlineData("America/New_York", "2026-11-01T04:00:00Z", "2026-11-01")]
    [InlineData("America/New_York", "2026-11-02T04:59:59Z", "2026-11-01")]
    [InlineData("America/New_York", "2026-11-02T05:00:00Z", "2026-11-02")]
    public void BusinessDate_is_the_local_calendar_date_in_the_effective_zone(string zone, string utc, string expected)
    {
        var instant = DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

        Assert.Equal(DateTime.Parse(expected), CashSessionTime.BusinessDateFor(instant, zone));
    }

    [Fact]
    public void BusinessDate_ignores_the_host_time_zone_and_the_datetime_kind()
    {
        var utc = new DateTime(2026, 11, 3, 18, 30, 0, DateTimeKind.Utc);
        var unspecified = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
        var local = utc.ToLocalTime();

        Assert.Equal(new DateTime(2026, 11, 4), CashSessionTime.BusinessDateFor(utc, "Asia/Dhaka"));
        Assert.Equal(new DateTime(2026, 11, 4), CashSessionTime.BusinessDateFor(unspecified, "Asia/Dhaka"));
        Assert.Equal(new DateTime(2026, 11, 4), CashSessionTime.BusinessDateFor(local, "Asia/Dhaka"));
    }

    [Fact]
    public void Dst_fall_back_day_has_25_hours_but_a_session_cutoff_is_still_elapsed_utc_time()
    {
        // 2026-11-01 00:30 New York (EDT) = 04:30Z; a 20 h session ends at 00:30Z next UTC day, 19:30 EST local.
        var opened = new DateTime(2026, 11, 1, 4, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateTime(2026, 11, 2, 0, 30, 0, DateTimeKind.Utc), CashSessionTime.CutoffUtc(opened, 20));
        Assert.Equal(new DateTime(2026, 11, 1), CashSessionTime.BusinessDateFor(opened, "America/New_York"));
    }

    [Fact]
    public void An_open_session_keeps_its_date_across_local_midnight()
    {
        var opened = new DateTime(2026, 11, 3, 17, 0, 0, DateTimeKind.Utc); // 23:00 in Dhaka
        var date = CashSessionTime.BusinessDateFor(opened, "Asia/Dhaka");
        var afterMidnight = opened.AddHours(3);

        Assert.Equal(new DateTime(2026, 11, 3), date);
        Assert.Equal(new DateTime(2026, 11, 4), CashSessionTime.BusinessDateFor(afterMidnight, "Asia/Dhaka"));
        Assert.False(CashSessionTime.IsOverdue(opened, 20, afterMidnight));
    }

    [Fact]
    public void Outlet_override_wins_over_the_business_default()
    {
        Assert.Equal("Asia/Dhaka", CashSessionTime.ResolveEffectiveTimeZoneId(null, "Asia/Dhaka"));
        Assert.Equal("Asia/Dhaka", CashSessionTime.ResolveEffectiveTimeZoneId("  ", "Asia/Dhaka"));
        Assert.Equal("America/New_York", CashSessionTime.ResolveEffectiveTimeZoneId("America/New_York", "Asia/Dhaka"));
    }

    [Fact]
    public void Unknown_zone_is_rejected_with_a_stable_code()
    {
        var error = Assert.Throws<CashSessionException>(() => CashSessionTime.FindZone("Not/AZone"));

        Assert.Equal(CashSessionErrorCodes.TimeZoneInvalid, error.Code);
    }

    [Theory]
    [InlineData(20, 17.99, CashSessionWarningLevel.None)]
    [InlineData(20, 18.0, CashSessionWarningLevel.Warning)]
    [InlineData(20, 18.99, CashSessionWarningLevel.Warning)]
    [InlineData(20, 19.0, CashSessionWarningLevel.Urgent)]
    [InlineData(20, 19.99, CashSessionWarningLevel.Urgent)]
    [InlineData(20, 20.0, CashSessionWarningLevel.Overdue)]
    [InlineData(20, 31.0, CashSessionWarningLevel.Overdue)]
    // Shorter snapshotted limits warn two and one hours before their own cutoff.
    [InlineData(8, 5.99, CashSessionWarningLevel.None)]
    [InlineData(8, 6.0, CashSessionWarningLevel.Warning)]
    [InlineData(8, 7.0, CashSessionWarningLevel.Urgent)]
    public void Warning_levels_follow_the_snapshotted_duration(int hours, double elapsedHours, CashSessionWarningLevel expected)
    {
        var opened = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, CashSessionTime.WarningLevel(opened, hours, opened.AddHours(elapsedHours)));
    }

    [Fact]
    public void The_exact_cutoff_instant_is_already_overdue_and_one_tick_before_is_not()
    {
        var opened = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
        var cutoff = CashSessionTime.CutoffUtc(opened, 20);

        Assert.True(CashSessionTime.IsOverdue(opened, 20, cutoff));
        Assert.False(CashSessionTime.IsOverdue(opened, 20, cutoff.AddTicks(-1)));
    }

    [Fact]
    public void Local_kind_values_from_legacy_npgsql_reads_do_not_shift_the_cutoff()
    {
        var opened = new DateTime(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);
        var asReadByLegacyNpgsql = opened.ToLocalTime();

        Assert.Equal(CashSessionTime.CutoffUtc(opened, 20), CashSessionTime.CutoffUtc(asReadByLegacyNpgsql, 20));
        Assert.Equal(CashSessionTime.ElapsedSeconds(opened, opened.AddHours(2)),
            CashSessionTime.ElapsedSeconds(asReadByLegacyNpgsql, opened.AddHours(2)));
    }
}

public sealed class CashSessionExpectedCashCalculatorTests
{
    private static readonly DateTime Opened = new(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);

    private static CashSessionSnapshot Session(decimal opening = 100m) =>
        new(1, 1, 10, 20, new DateTime(2026, 11, 3), Opened, null, opening, null, null, null, "open", 5, null, null, null,
            "Asia/Dhaka", 20);

    private static CashSessionSaleFact Sale(long id, string status, params CashSessionPaymentFact[] payments) =>
        new(id, status, null, payments.Sum(p => p.Amount), payments);

    private static CashSessionPaymentFact Cash(decimal amount, decimal? tendered = null, decimal? change = null, decimal? retained = null) =>
        new("cash", amount, tendered ?? amount, change ?? 0m, retained ?? amount);

    private static CashSessionMovementFact Movement(string type, decimal effect, long id = 1) =>
        new(id, type, effect, "test", null, null, null, Opened);

    private static CashSessionCloseSummaryReadModel Calculate(
        CashSessionSnapshot session,
        IReadOnlyList<CashSessionSaleFact>? sales = null,
        IReadOnlyList<CashSessionMovementFact>? movements = null) =>
        CashSessionExpectedCashCalculator.Calculate(
            new CashSessionFacts(session, sales ?? [], movements ?? []),
            Opened.AddHours(8));

    [Fact]
    public void Exchange_cash_in_and_out_are_part_of_the_expected_drawer_and_its_components()
    {
        var summary = Calculate(Session(100m), [Sale(1, "completed", Cash(40m))],
        [
            Movement(CashSessionMovementTypes.ExchangeCashIn, 22m, 1),
            Movement(CashSessionMovementTypes.ExchangeCashOut, -33m, 2),
            Movement(CashSessionMovementTypes.CashRefund, -5m, 3)
        ]);

        Assert.Equal(124m, summary.ExpectedClosingCash);    // 100 + 40 + 22 − 33 − 5
        Assert.Equal(62m, summary.CashSales);        // exchange collections are cash received
        Assert.Equal(38m, summary.CashRefunds);              // exchange refunds are cash refunds
    }

    [Fact]
    public void An_unknown_movement_type_never_silently_vanishes_from_the_drawer()
    {
        Assert.Throws<InvalidOperationException>(() => Calculate(Session(100m), movements: [Movement("mystery", 10m)]));
    }

    [Fact]
    public void Opening_plus_cash_sales()
    {
        var summary = Calculate(Session(100m), [Sale(1, "completed", Cash(40m)), Sale(2, "completed", Cash(60.50m))]);

        Assert.Equal(200.50m, summary.ExpectedClosingCash);
        Assert.Equal(100.50m, summary.CashSales);
        Assert.Equal(2, summary.SaleCount);
    }

    [Fact]
    public void Change_returned_to_the_customer_is_never_retained_cash()
    {
        // The customer handed over 100.00 for a 62.30 sale; only 62.30 stayed in the drawer.
        var summary = Calculate(Session(0m), [Sale(1, "completed", Cash(62.30m, tendered: 100m, change: 37.70m, retained: 62.30m))]);

        Assert.Equal(62.30m, summary.ExpectedClosingCash);
        Assert.Equal(100m, summary.CashTendered);
        Assert.Equal(37.70m, summary.ChangeGiven);
    }

    [Fact]
    public void Split_payment_counts_only_the_cash_allocation()
    {
        var summary = Calculate(Session(50m), [Sale(1, "completed",
            Cash(30m, tendered: 50m, change: 20m, retained: 30m),
            new CashSessionPaymentFact("card", 70m, null, null, null))]);

        Assert.Equal(80m, summary.ExpectedClosingCash);
        Assert.Equal(70m, summary.CardSales);
    }

    [Fact]
    public void Card_mobile_and_other_tenders_never_enter_the_drawer()
    {
        var summary = Calculate(Session(10m), [Sale(1, "completed",
            new CashSessionPaymentFact("card", 25m, null, null, null),
            new CashSessionPaymentFact("mobile", 15m, null, null, null),
            new CashSessionPaymentFact("loyalty", 5m, null, null, null))]);

        Assert.Equal(10m, summary.ExpectedClosingCash);
        Assert.Equal(25m, summary.CardSales);
        Assert.Equal(15m, summary.MobileSales);
        Assert.Equal(5m, summary.OtherNonCashSales);
    }

    [Fact]
    public void A_void_without_a_cash_payout_does_not_fabricate_an_outflow()
    {
        var summary = Calculate(Session(0m), [Sale(1, "voided", Cash(40m))]);

        Assert.Equal(40m, summary.ExpectedClosingCash);
    }

    [Fact]
    public void A_same_session_void_that_was_paid_back_nets_to_zero_exactly_once()
    {
        var summary = Calculate(
            Session(100m),
            [Sale(1, "voided", Cash(40m))],
            [Movement(CashSessionMovementTypes.CashRefund, -40m)]);

        Assert.Equal(100m, summary.ExpectedClosingCash);
        Assert.Equal(40m, summary.CashRefunds);
    }

    [Fact]
    public void A_later_session_refund_is_the_dispensing_sessions_outflow_not_the_original_sessions()
    {
        var original = Calculate(Session(0m), [Sale(1, "refunded", Cash(40m))]);
        var dispensing = Calculate(Session(200m), [], [Movement(CashSessionMovementTypes.CashRefund, -40m)]);

        Assert.Equal(40m, original.ExpectedClosingCash);
        Assert.Equal(160m, dispensing.ExpectedClosingCash);
    }

    [Fact]
    public void Drawer_expenses_reduce_cash_and_their_reversal_returns_it()
    {
        var summary = Calculate(Session(100m), [], [
            Movement(CashSessionMovementTypes.DrawerExpense, -30m, 1),
            Movement(CashSessionMovementTypes.DrawerExpense, -20m, 2),
            Movement(CashSessionMovementTypes.DrawerExpenseReversal, 20m, 3)]);

        Assert.Equal(70m, summary.ExpectedClosingCash);
        Assert.Equal(30m, summary.DrawerExpenses);
    }

    [Fact]
    public void Approved_corrections_are_signed()
    {
        var summary = Calculate(Session(100m), [], [
            Movement(CashSessionMovementTypes.Correction, 5m, 1),
            Movement(CashSessionMovementTypes.Correction, -2m, 2)]);

        Assert.Equal(103m, summary.ExpectedClosingCash);
        Assert.Equal(3m, summary.Corrections);
    }

    [Fact]
    public void Legacy_payment_rows_without_d14_evidence_fall_back_to_the_allocated_amount()
    {
        var legacy = new CashSessionSaleFact(1, "completed", "cash", 55m, []);
        var legacyRow = new CashSessionSaleFact(2, "completed", null, 20m,
            [new CashSessionPaymentFact("cash", 20m, null, null, null)]);

        Assert.Equal(75m, Calculate(Session(0m), [legacy, legacyRow]).ExpectedClosingCash);
    }

    [Fact]
    public void Sales_that_never_completed_are_ignored()
    {
        var summary = Calculate(Session(0m), [Sale(1, "held", Cash(40m)), Sale(2, "cancelled", Cash(10m))]);

        Assert.Equal(0m, summary.ExpectedClosingCash);
        Assert.Equal(0, summary.SaleCount);
    }

    [Fact]
    public void An_overdue_session_is_reported_but_still_summarised()
    {
        var summary = CashSessionExpectedCashCalculator.Calculate(
            new CashSessionFacts(Session(), [], []),
            Opened.AddHours(21));

        Assert.True(summary.IsOverdue);
        Assert.Equal(100m, summary.ExpectedClosingCash);
    }
}
