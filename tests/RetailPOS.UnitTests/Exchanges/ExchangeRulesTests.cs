using RetailPOS.Application.Exchanges;
using Xunit;

namespace RetailPOS.UnitTests.Exchanges;

public sealed class ExchangeRulesTests
{
    // ---- OC-07: Day 0 = purchase local date; the whole last day is valid ----

    [Theory]
    [InlineData("2026-11-01", 7, "2026-11-01", false)] // same day
    [InlineData("2026-11-01", 7, "2026-11-08", false)] // last eligible day (Day N)
    [InlineData("2026-11-01", 7, "2026-11-09", true)]  // first expired day (Day N+1)
    [InlineData("2026-01-31", 1, "2026-02-01", false)] // month boundary
    [InlineData("2028-02-28", 1, "2028-02-29", false)] // leap day
    [InlineData("2028-02-28", 1, "2028-03-01", true)]
    public void Window_counts_calendar_days_from_day_zero(string anchor, int days, string eligibility, bool expired)
    {
        Assert.Equal(expired, ExchangeCalculator.IsWindowExpired(DateOnly.Parse(anchor), days, DateOnly.Parse(eligibility)));
    }

    [Fact]
    public void Purchase_near_local_midnight_counts_as_that_local_date()
    {
        // 23:50 in Dhaka (UTC+6) on 1 Nov is 17:50 UTC on 1 Nov; 00:10 local on 2 Nov is 18:10 UTC on 1 Nov.
        Assert.Equal(new DateOnly(2026, 11, 1), ExchangeCalculator.LocalDate(new DateTime(2026, 11, 1, 17, 50, 0, DateTimeKind.Utc), "Asia/Dhaka"));
        Assert.Equal(new DateOnly(2026, 11, 2), ExchangeCalculator.LocalDate(new DateTime(2026, 11, 1, 18, 10, 0, DateTimeKind.Utc), "Asia/Dhaka"));
    }

    [Fact]
    public void Unspecified_kind_is_treated_as_utc_and_local_kind_is_converted()
    {
        var unspecified = new DateTime(2026, 11, 1, 18, 10, 0, DateTimeKind.Unspecified);
        Assert.Equal(new DateOnly(2026, 11, 2), ExchangeCalculator.LocalDate(unspecified, "Asia/Dhaka"));
    }

    [Fact]
    public void Daylight_saving_transition_keeps_calendar_counting()
    {
        // Europe/London springs forward on 29 Mar 2026; a 23:30 local purchase on 28 Mar is still 28 Mar.
        var purchase = ExchangeCalculator.LocalDate(new DateTime(2026, 3, 28, 23, 30, 0, DateTimeKind.Utc), "Europe/London");
        Assert.Equal(new DateOnly(2026, 3, 28), purchase);
        Assert.False(ExchangeCalculator.IsWindowExpired(purchase, 1, new DateOnly(2026, 3, 29)));
        Assert.True(ExchangeCalculator.IsWindowExpired(purchase, 1, new DateOnly(2026, 3, 30)));
    }

    // ---- OC-05: max(return credit, replacement value) >= threshold ----

    [Theory]
    [InlineData(8000, 9000, 9000)]
    [InlineData(12000, 9000, 12000)]
    [InlineData(5000, 5000, 5000)]
    [InlineData(50000, 50000, 50000)]
    public void High_value_amount_is_the_larger_leg(decimal returnCredit, decimal replacement, decimal expected)
    {
        Assert.Equal(expected, ExchangeCalculator.HighValueAmount(returnCredit, replacement));
    }

    [Theory]
    [InlineData(10000, 10000.0, true)]   // exactly equal requires approval
    [InlineData(9999.99, 10000.0, false)]
    [InlineData(50000, null, false)]   // disabled
    [InlineData(50000, 0.0, false)]      // a non-positive threshold never applies
    public void High_value_threshold_boundary(decimal amount, double? threshold, bool required)
    {
        Assert.Equal(required, ExchangeCalculator.RequiresHighValueApproval(amount, threshold is null ? null : (decimal)threshold));
    }

    [Fact]
    public void Even_exchange_with_large_legs_is_still_high_value()
    {
        var totals = new ExchangeTotals(45454.55m, 4545.45m, 0m, 45454.55m, 4545.45m, 0m, 0m);
        Assert.Equal(0m, totals.NetAdjustmentGross);
        Assert.True(ExchangeCalculator.RequiresHighValueApproval(totals.HighValueAmount, 10000m));
    }

    // ---- Chain depth (EX-04) ----

    [Theory]
    [InlineData(0, 2, false)]
    [InlineData(1, 2, false)]
    [InlineData(2, 2, true)]
    [InlineData(5, null, false)]
    public void Chain_count(int sourceDepth, int? max, bool exceeded)
    {
        Assert.Equal(exceeded, ExchangeCalculator.ChainCountExceeded(sourceDepth, max));
    }

    // ---- OC-06 and DN-01 ----

    [Fact]
    public void Optional_mode_needs_no_verification()
    {
        Assert.Equal(ExchangePhoneVerificationResults.NotRequired, ExchangeCalculator.ClassifyPhoneVerification("optional", isWalkIn: false));
    }

    [Fact]
    public void Required_mode_skips_walk_in_invoices()
    {
        Assert.Equal(ExchangePhoneVerificationResults.SkippedWalkIn, ExchangeCalculator.ClassifyPhoneVerification("required", isWalkIn: true));
    }

    [Fact]
    public void Required_mode_fails_closed_for_registered_customers()
    {
        var error = Assert.Throws<ExchangeException>(() => ExchangeCalculator.ClassifyPhoneVerification("required", isWalkIn: false));
        Assert.Equal(ExchangeErrorCodes.PhoneVerificationUnavailable, error.Code);
        Assert.Equal(422, error.StatusCode);
    }

    // ---- Settlement (EX-07, EX-08, D-09) ----

    private static ExchangeTotals Collect(decimal difference) => new(100m, 0m, 0m, 100m + difference, 0m, 0m, 0m);

    [Fact]
    public void Even_exchange_takes_no_settlement()
    {
        Assert.Empty(ExchangeSettlementPlanner.Plan(Collect(0m), []));
        Assert.Throws<ExchangeException>(() => ExchangeSettlementPlanner.Plan(Collect(0m),
            [new ExchangeSettlementInput("collect", "cash", 1m)]));
    }

    [Fact]
    public void Split_collection_must_equal_the_difference_and_cash_records_change()
    {
        var plans = ExchangeSettlementPlanner.Plan(Collect(200m),
        [
            new ExchangeSettlementInput("collect", "card", 150m),
            new ExchangeSettlementInput("collect", "cash", 50m, Tendered: 100m)
        ]);
        Assert.Equal(2, plans.Count);
        var cash = plans.Single(plan => plan.Method == "cash");
        Assert.Equal(50m, cash.ChangeGiven);
        Assert.Equal(50m, cash.CashEffect);
        Assert.Equal(0m, plans.Single(plan => plan.Method == "card").CashEffect);

        var error = Assert.Throws<ExchangeException>(() => ExchangeSettlementPlanner.Plan(Collect(200m),
            [new ExchangeSettlementInput("collect", "card", 150m)]));
        Assert.Equal(ExchangeErrorCodes.SettlementMismatch, error.Code);
    }

    [Fact]
    public void Cash_refund_takes_cash_out_of_the_drawer()
    {
        var plan = Assert.Single(ExchangeSettlementPlanner.Plan(Collect(-300m), [new ExchangeSettlementInput("refund", "cash", 300m)]));
        Assert.Equal(-300m, plan.CashEffect);
    }

    [Fact]
    public void Card_or_mobile_refund_needs_reference_and_confirmation()
    {
        var error = Assert.Throws<ExchangeException>(() => ExchangeSettlementPlanner.Plan(Collect(-300m),
            [new ExchangeSettlementInput("refund", "mobile", 300m, ExternalReference: "TX-1")]));
        Assert.Equal(ExchangeErrorCodes.RefundEvidenceRequired, error.Code);

        var plan = Assert.Single(ExchangeSettlementPlanner.Plan(Collect(-300m),
            [new ExchangeSettlementInput("refund", "mobile", 300m, ExternalReference: " TX-1 ", ExternalRefundConfirmed: true)]));
        Assert.Equal("TX-1", plan.ExternalReference);
        Assert.Equal(0m, plan.CashEffect);
    }

    [Fact]
    public void Refund_uses_exactly_one_method_and_rejects_wrong_direction()
    {
        Assert.Throws<ExchangeException>(() => ExchangeSettlementPlanner.Plan(Collect(-300m),
        [
            new ExchangeSettlementInput("refund", "cash", 100m),
            new ExchangeSettlementInput("refund", "cash", 200m)
        ]));
        Assert.Throws<ExchangeException>(() => ExchangeSettlementPlanner.Plan(Collect(-300m),
            [new ExchangeSettlementInput("collect", "cash", 300m)]));
        Assert.Throws<ExchangeException>(() => ExchangeSettlementPlanner.Plan(Collect(-300m),
            [new ExchangeSettlementInput("refund", "bank", 300m)]));
    }

    // ---- Journal (C-04, C-06) ----

    [Theory]
    [InlineData(1818.18, 181.82, 1200, 2000.00, 200.00, 0, 1300, 200.00)]   // collect 200
    [InlineData(1818.18, 181.82, 1200, 1545.45, 154.55, 0, 1100, -300.00)]  // refund 300
    [InlineData(1000.00, 0.00, 600, 1000.00, 0.00, 0, 650, 0.00)]           // even
    [InlineData(1000.00, 150.00, 700, 950.00, 142.50, 50, 640, -57.50)]     // replacement discount
    public void Journal_balances_for_every_leg_combination(
        decimal returnNet, decimal returnVat, decimal returnCost,
        decimal replacementNet, decimal replacementVat, decimal discount, decimal replacementCost,
        decimal expectedDifference)
    {
        var totals = new ExchangeTotals(returnNet, returnVat, returnCost, replacementNet, replacementVat, discount, replacementCost);
        Assert.Equal(expectedDifference, totals.NetAdjustmentGross);
        var settlements = totals.Direction switch
        {
            ExchangeSettlementDirections.Collect => ExchangeSettlementPlanner.Plan(totals, [new ExchangeSettlementInput("collect", "cash", totals.SettlementAmount)]),
            ExchangeSettlementDirections.Refund => ExchangeSettlementPlanner.Plan(totals, [new ExchangeSettlementInput("refund", "cash", totals.SettlementAmount)]),
            _ => ExchangeSettlementPlanner.Plan(totals, [])
        };

        var lines = ExchangeJournalComposer.Compose(totals, settlements);
        Assert.Equal(lines.Sum(line => line.Debit), lines.Sum(line => line.Credit));
        Assert.All(lines, line => Assert.True(line.Debit >= 0m && line.Credit >= 0m && (line.Debit == 0m || line.Credit == 0m)));

        var reversed = ExchangeJournalComposer.Compose(totals, settlements, reverse: true);
        Assert.Equal(reversed.Sum(line => line.Debit), reversed.Sum(line => line.Credit));
        Assert.Equal(lines.Sum(line => line.Debit), reversed.Sum(line => line.Credit));
    }

    [Fact]
    public void Return_leg_reverses_revenue_vat_and_cost()
    {
        var totals = new ExchangeTotals(100m, 15m, 60m, 100m, 15m, 0m, 60m);
        var lines = ExchangeJournalComposer.Compose(totals, []);
        Assert.Contains(lines, line => line.Slot == ExchangeJournalComposer.SalesReturn && line.Debit == 100m);
        Assert.Contains(lines, line => line.Slot == ExchangeJournalComposer.VatPayable && line.Debit == 15m);
        Assert.Contains(lines, line => line.Slot == ExchangeJournalComposer.VatPayable && line.Credit == 15m);
        Assert.Contains(lines, line => line.Slot == ExchangeJournalComposer.Inventory && line.Debit == 60m);
    }

    // ---- D-07 salesperson attribution ----

    [Fact]
    public void Track_only_records_negative_without_reducing_target()
    {
        var fact = ExchangeCalculator.SalespersonFact(-300m, ExchangeNegativeTargetModes.TrackOnly);
        Assert.Equal(0m, fact.Positive);
        Assert.Equal(-300m, fact.Negative);
        Assert.Equal(-300m, fact.Signed);
        Assert.Equal(0m, fact.TargetEffect);
    }

    [Fact]
    public void Deduct_mode_reduces_target_and_positive_always_credits()
    {
        Assert.Equal(-300m, ExchangeCalculator.SalespersonFact(-300m, ExchangeNegativeTargetModes.DeductFromExchangeSalesperson).TargetEffect);
        Assert.Equal(200m, ExchangeCalculator.SalespersonFact(200m, ExchangeNegativeTargetModes.TrackOnly).TargetEffect);
    }

    // ---- C-03: refunds and exchanges of one line sum exactly ----

    [Fact]
    public void Cumulative_shares_sum_to_the_recorded_amount()
    {
        const decimal recorded = 100m;
        var a = ExchangeCalculator.ReturnShare(recorded, 0m, 1m, 3m);
        var b = ExchangeCalculator.ReturnShare(recorded, 1m, 1m, 3m);
        var c = ExchangeCalculator.ReturnShare(recorded, 2m, 1m, 3m);
        Assert.Equal(recorded, a + b + c);
    }
}
