using RetailPOS.Application.Inventory.Costing;
using RetailPOS.Application.OwnerDashboards;

namespace RetailPOS.Tests.OwnerDashboards;

public sealed class SalesFinanceMathTests
{
    [Fact]
    public void Target_BelowTarget_ReportsShortfallAndAchievement()
    {
        var outcome = SalesFinanceMath.EvaluateTarget(85_500m, 100_000m);

        Assert.Equal(14_500m, outcome.Shortfall);
        Assert.Equal(0m, outcome.Exceeded);
        Assert.Equal(85.5m, outcome.AchievementPercent);
    }

    [Fact]
    public void Target_Exceeded_IsPositiveAndShortfallIsNeverNegative()
    {
        var outcome = SalesFinanceMath.EvaluateTarget(120_000m, 100_000m);

        Assert.Equal(0m, outcome.Shortfall);
        Assert.Equal(20_000m, outcome.Exceeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Target_MissingOrZero_IsNotConfiguredWithoutDivideByZero(int? target)
    {
        var outcome = SalesFinanceMath.EvaluateTarget(500m, target);

        Assert.Equal(MetricStatuses.NotConfigured, outcome.Status);
        Assert.Null(outcome.Shortfall);
        Assert.Null(outcome.AchievementPercent);
    }

    [Fact]
    public void Profit_IsRevenueLessCogsLessExpense_AndLabelledEstimated()
    {
        var profit = SalesFinanceMath.EstimateProfit(1000m, 600m, 0, 0, 150m);

        Assert.Equal(250m, profit.Value);
        Assert.Equal(MetricStatuses.Estimated, profit.Status);
    }

    [Fact]
    public void Profit_WithMissingLineCost_IsUnavailableNotGuessed()
    {
        var profit = SalesFinanceMath.EstimateProfit(1000m, 600m, 2, 0, 150m);

        Assert.Null(profit.Value);
        Assert.Equal(MetricStatuses.Unavailable, profit.Status);
    }

    [Fact]
    public void Profit_WithUntrustedReversal_IsUnavailable()
    {
        var profit = SalesFinanceMath.EstimateProfit(1000m, 600m, 0, 1, 150m);

        Assert.Null(profit.Value);
        Assert.Equal(MetricStatuses.Unavailable, profit.Status);
    }

    [Fact]
    public void Change_WithoutPreviousSales_HasNoPercentage()
    {
        Assert.Null(SalesFinanceMath.ChangePercent(100m, 0m));
        Assert.Equal(25m, SalesFinanceMath.ChangePercent(125m, 100m));
    }

    [Fact]
    public void PreviousPeriod_HasTheSameLengthAndEndsTheDayBefore()
    {
        var (from, to) = SalesFinanceMath.PreviousPeriod(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7));

        Assert.Equal(new DateOnly(2026, 10, 25), from);
        Assert.Equal(new DateOnly(2026, 10, 31), to);
    }

    [Fact]
    public void Buckets_SwitchToWeeklyAfter45Days_AndCoverTheWholeRangeOnce()
    {
        var from = new DateOnly(2026, 1, 1);
        Assert.Equal(DashboardBuckets.Daily, DashboardBuckets.KindFor(from, from.AddDays(44)));
        Assert.Equal(DashboardBuckets.Weekly, DashboardBuckets.KindFor(from, from.AddDays(45)));

        var buckets = DashboardBuckets.Build(from, from.AddDays(45));
        Assert.Equal(from, buckets[0].From);
        Assert.Equal(from.AddDays(45), buckets[^1].To);
        for (var index = 1; index < buckets.Count; index++)
            Assert.Equal(buckets[index - 1].To.AddDays(1), buckets[index].From);
    }
}

public sealed class WeightedAverageCostPoolTests
{
    [Fact]
    public void Replay_MatchesTheAcceptedWeightedAverageExample()
    {
        // P2-06: receipts at 10 and 20 average to 15; a five-unit issue consumes at that average.
        var pool = new WeightedAverageCostPool();
        pool.Apply(10, 0, 100m, 10m);
        pool.Apply(10, 0, 200m, 20m);
        pool.Apply(0, 5, 0m, 15m);

        Assert.Equal(15, pool.Quantity);
        Assert.Equal(225m, pool.Value);
    }

    [Fact]
    public void Issue_FromAnEmptyPool_UsesTheMovementCost()
    {
        var pool = new WeightedAverageCostPool();
        pool.Apply(0, 2, 0m, 7m);

        Assert.Equal(-2, pool.Quantity);
        Assert.Equal(-14m, pool.Value);
    }
}

public sealed class InStockDayTrackerTests
{
    private static readonly DateOnly From = new(2026, 10, 1);
    private static readonly DateOnly To = new(2026, 10, 30);

    [Fact]
    public void NoMovements_HasNoEvidenceAndNoDays()
    {
        var tracker = new InStockDayTracker(From, To);

        Assert.Equal(0, tracker.Complete());
        Assert.False(tracker.HasEvidence);
    }

    [Fact]
    public void StockHeldBeforeTheWindow_CountsEveryDay()
    {
        var tracker = new InStockDayTracker(From, To);
        tracker.Apply(new DateOnly(2026, 9, 1), 10);

        Assert.Equal(30, tracker.Complete());
    }

    [Fact]
    public void Stockout_DaysAreNotEligible()
    {
        var tracker = new InStockDayTracker(From, To);
        tracker.Apply(new DateOnly(2026, 9, 1), 5);
        tracker.Apply(new DateOnly(2026, 10, 10), 0); // Sold out on the 10th: day counts.
        tracker.Apply(new DateOnly(2026, 10, 21), 5); // Restocked on the 21st: day counts.

        // 1st to 10th (10 days) plus 21st to 30th (10 days).
        Assert.Equal(20, tracker.Complete());
    }

    [Fact]
    public void SeveralMovementsOnOneDay_CountThatDayOnce()
    {
        var tracker = new InStockDayTracker(From, From);
        tracker.Apply(From, 5);
        tracker.Apply(From, 3);
        tracker.Apply(From, 0);

        Assert.Equal(1, tracker.Complete());
    }

    [Fact]
    public void ReceiptAfterAnEmptyMorning_CountsThatDay()
    {
        var tracker = new InStockDayTracker(From, To);
        tracker.Apply(new DateOnly(2026, 10, 29), 4);

        Assert.Equal(2, tracker.Complete());
    }

    [Fact]
    public void MovementAfterTheWindow_DoesNotChangeTheCount_ButUpdatesLatestBalance()
    {
        var tracker = new InStockDayTracker(From, To);
        tracker.Apply(new DateOnly(2026, 9, 1), 5);
        tracker.Apply(new DateOnly(2026, 11, 5), 0);

        Assert.Equal(30, tracker.Complete());
        Assert.Equal(0, tracker.LatestBalance);
    }

    [Fact]
    public void ZeroBalanceThroughout_IsEvidenceWithNoDays()
    {
        var tracker = new InStockDayTracker(From, To);
        tracker.Apply(new DateOnly(2026, 9, 1), 0);

        Assert.Equal(0, tracker.Complete());
        Assert.True(tracker.HasEvidence);
    }
}

public sealed class SlowMoverClassifierTests
{
    private static readonly OwnerDashboardPolicy Policy = new();

    [Fact]
    public void NewItem_WithTwoSellingDays_IsNotSlow()
    {
        var result = SlowMoverClassifier.Classify(TestFacts.Outlet(1, available: 10, sold: 0, days: 2), Policy);

        Assert.Equal(SlowClassifications.InsufficientEvidence, result.Classification);
        Assert.False(SlowClassifications.IsSlow(result.Classification));
    }

    [Fact]
    public void WithoutStockHistory_IsInsufficientEvidence()
    {
        var result = SlowMoverClassifier.Classify(
            TestFacts.Outlet(1, available: 10, sold: 0, days: 30) with { HasStockEvidence = false },
            Policy);

        Assert.Equal(SlowClassifications.InsufficientEvidence, result.Classification);
    }

    [Fact]
    public void NoSalesInPeriod_ButSoldEarlier_IsZeroSales()
    {
        var result = SlowMoverClassifier.Classify(
            TestFacts.Outlet(1, available: 10, sold: 0, days: 30) with { EverSold = true },
            Policy);

        Assert.Equal(SlowClassifications.ZeroSales, result.Classification);
    }

    [Fact]
    public void NoSaleEver_WithFullEvidence_IsNeverSold()
    {
        var result = SlowMoverClassifier.Classify(TestFacts.Outlet(1, available: 10, sold: 0, days: 30), Policy);

        Assert.Equal(SlowClassifications.NeverSold, result.Classification);
    }

    [Fact]
    public void VelocityBelowThreshold_IsLowSales()
    {
        var result = SlowMoverClassifier.Classify(TestFacts.Outlet(1, available: 10, sold: 3, days: 30), Policy);

        Assert.Equal(SlowClassifications.LowSales, result.Classification);
        Assert.Equal(0.1m, result.VelocityPerDay);
    }

    [Fact]
    public void VelocityUsesInStockDays_NotCalendarDays()
    {
        // 3 units in 14 in-stock days is 0.21 a day: normal, although low over a 30-day calendar.
        var result = SlowMoverClassifier.Classify(TestFacts.Outlet(1, available: 10, sold: 3, days: 14), Policy);

        Assert.Equal(SlowClassifications.Normal, result.Classification);
    }

    [Fact]
    public void Warehouses_AreNotEligibleForDemandClassification()
    {
        var warehouse = TestFacts.Outlet(1, available: 10, sold: 0, days: 30)
            with { LocationType = OwnerDashboardLocationTypes.Warehouse };

        Assert.False(SlowMoverClassifier.IsEligible(warehouse, Policy));
    }
}

public sealed class TransferOpportunityEngineTests
{
    private static readonly OwnerDashboardPolicy Policy = new();

    [Fact]
    public void WorkedExample_FromTheLld_SuggestsTwentyEightUnits()
    {
        // LLD §7.4: destination 48 sold / 30 days with 6 on hand; source 3 sold / 30 days with 42 on hand.
        var result = TransferOpportunityEngine.Build(
            [TestFacts.Outlet(5, available: 42, sold: 3, days: 30), TestFacts.Outlet(1, available: 6, sold: 48, days: 30)],
            Policy);

        var opportunity = Assert.Single(result);
        Assert.Equal(5, opportunity.Source.LocationId);
        Assert.Equal(1, opportunity.Destination.LocationId);
        Assert.Equal(28, opportunity.SuggestedQty);
        Assert.Equal(3, opportunity.SourceProtectedQty);
        Assert.Equal(TransferPriorities.High, opportunity.Priority);
        Assert.Contains(TransferReasonCodes.VelocityGap, opportunity.ReasonCodes);
    }

    [Fact]
    public void Suggestion_NeverExceedsSafeSourceSpare()
    {
        var result = TransferOpportunityEngine.Build(
            [TestFacts.Outlet(5, available: 10, sold: 3, days: 30), TestFacts.Outlet(1, available: 0, sold: 48, days: 30)],
            Policy);

        var opportunity = Assert.Single(result);
        Assert.Equal(7, opportunity.SuggestedQty); // 10 available less 3 protected.
        Assert.Contains(TransferReasonCodes.LimitedBySourceSpare, opportunity.ReasonCodes);
        Assert.Contains(TransferReasonCodes.DestinationOutOfStock, opportunity.ReasonCodes);
    }

    [Fact]
    public void PendingOutbound_ReducesTransferableSourceStock()
    {
        var result = TransferOpportunityEngine.Build(
            [
                TestFacts.Outlet(5, available: 10, sold: 3, days: 30) with { PendingOutbound = 5 },
                TestFacts.Outlet(1, available: 0, sold: 48, days: 30)
            ],
            Policy);

        Assert.Equal(2, Assert.Single(result).SuggestedQty);
    }

    [Fact]
    public void SourceWithNothingAboveItsProtection_IsNotRecommended()
    {
        var result = TransferOpportunityEngine.Build(
            [TestFacts.Outlet(5, available: 3, sold: 3, days: 30), TestFacts.Outlet(1, available: 0, sold: 48, days: 30)],
            Policy);

        Assert.Empty(result);
    }

    [Fact]
    public void InboundStock_ReducesDestinationNeed_AndCanRemoveIt()
    {
        var reduced = TransferOpportunityEngine.Build(
            [
                TestFacts.Outlet(5, available: 42, sold: 3, days: 30),
                TestFacts.Outlet(1, available: 6, sold: 48, days: 30) with { PendingInbound = 8 }
            ],
            Policy);
        Assert.Equal(20, Assert.Single(reduced).SuggestedQty);

        var removed = TransferOpportunityEngine.Build(
            [
                TestFacts.Outlet(5, available: 42, sold: 3, days: 30),
                TestFacts.Outlet(1, available: 6, sold: 48, days: 30) with { PendingInbound = 28 }
            ],
            Policy);
        Assert.Empty(removed);
    }

    [Fact]
    public void DifferentVariants_AreNeverMatched()
    {
        var result = TransferOpportunityEngine.Build(
            [
                TestFacts.Outlet(5, available: 42, sold: 3, days: 30) with { VariantId = 11 },
                TestFacts.Outlet(1, available: 6, sold: 48, days: 30) with { VariantId = 22 }
            ],
            Policy);

        Assert.Empty(result);
    }

    [Fact]
    public void Warehouses_AreNeitherSourceNorDestination()
    {
        var result = TransferOpportunityEngine.Build(
            [
                TestFacts.Outlet(5, available: 42, sold: 3, days: 30)
                    with { LocationType = OwnerDashboardLocationTypes.Warehouse },
                TestFacts.Outlet(1, available: 6, sold: 48, days: 30)
            ],
            Policy);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(5, 30)] // Source without enough in-stock history.
    [InlineData(30, 5)] // Destination without enough in-stock history.
    public void InsufficientHistory_ProducesNoSuggestion(int sourceDays, int destinationDays)
    {
        var result = TransferOpportunityEngine.Build(
            [
                TestFacts.Outlet(5, available: 42, sold: 0, days: sourceDays),
                TestFacts.Outlet(1, available: 6, sold: 48, days: destinationDays)
            ],
            Policy);

        Assert.Empty(result);
    }

    [Fact]
    public void WeakDemand_ProducesNoSuggestion()
    {
        var result = TransferOpportunityEngine.Build(
            [TestFacts.Outlet(5, available: 42, sold: 0, days: 30), TestFacts.Outlet(1, available: 1, sold: 10, days: 30)],
            Policy);

        Assert.Empty(result);
    }

    [Fact]
    public void InactiveProducts_AndMissingEvidence_ProduceNoSuggestion()
    {
        var inactive = TransferOpportunityEngine.Build(
            [
                TestFacts.Outlet(5, available: 42, sold: 3, days: 30) with { ProductActive = false },
                TestFacts.Outlet(1, available: 6, sold: 48, days: 30) with { ProductActive = false }
            ],
            Policy);
        var noEvidence = TransferOpportunityEngine.Build(
            [
                TestFacts.Outlet(5, available: 42, sold: 3, days: 30) with { HasStockEvidence = false },
                TestFacts.Outlet(1, available: 6, sold: 48, days: 30)
            ],
            Policy);

        Assert.Empty(inactive);
        Assert.Empty(noEvidence);
    }

    [Fact]
    public void CompetingSources_NeverJointlyExceedDestinationNeed()
    {
        var result = TransferOpportunityEngine.Build(
            [
                TestFacts.Outlet(5, available: 42, sold: 0, days: 30),
                TestFacts.Outlet(6, available: 42, sold: 0, days: 30),
                TestFacts.Outlet(1, available: 6, sold: 48, days: 30)
            ],
            Policy);

        Assert.Equal(28, result.Sum(opportunity => opportunity.SuggestedQty));
        Assert.Equal(1, result.Select(opportunity => opportunity.Source.LocationId).Distinct().Count());
    }

    [Fact]
    public void CompetingDestinations_NeverJointlyExceedSourceSpare()
    {
        var result = TransferOpportunityEngine.Build(
            [
                TestFacts.Outlet(5, available: 12, sold: 0, days: 30),
                TestFacts.Outlet(1, available: 0, sold: 48, days: 30),
                TestFacts.Outlet(2, available: 1, sold: 48, days: 30)
            ],
            Policy);

        Assert.Equal(11, result.Sum(opportunity => opportunity.SuggestedQty)); // 12 less 1 retained unit.
    }

    [Fact]
    public void Keys_AreUnique_AndOutputIsDeterministic()
    {
        IReadOnlyList<VariantLocationFact> facts =
        [
            TestFacts.Outlet(5, available: 50, sold: 0, days: 30),
            TestFacts.Outlet(6, available: 50, sold: 0, days: 30),
            TestFacts.Outlet(1, available: 0, sold: 48, days: 30),
            TestFacts.Outlet(2, available: 0, sold: 30, days: 30)
        ];

        var first = TransferOpportunityEngine.Build(facts, Policy);
        var second = TransferOpportunityEngine.Build(facts.Reverse().ToList(), Policy);

        Assert.Equal(first.Count, first.Select(row => row.RecommendationKey).Distinct().Count());
        Assert.Equal(
            first.Select(row => (row.RecommendationKey, row.SuggestedQty)),
            second.Select(row => (row.RecommendationKey, row.SuggestedQty)));
    }

    [Fact]
    public void PolicyCap_LimitsTheSuggestion_AndIsExplained()
    {
        var result = TransferOpportunityEngine.Build(
            [TestFacts.Outlet(5, available: 42, sold: 3, days: 30), TestFacts.Outlet(1, available: 6, sold: 48, days: 30)],
            Policy with { MaxSuggestedQty = 20 });

        var opportunity = Assert.Single(result);
        Assert.Equal(20, opportunity.SuggestedQty);
        Assert.Contains(TransferReasonCodes.LimitedByPolicyCap, opportunity.ReasonCodes);
    }

    [Fact]
    public void CanInitiate_ComesFromTheCallersSourceRule()
    {
        var result = TransferOpportunityEngine.Build(
            [TestFacts.Outlet(5, available: 42, sold: 3, days: 30), TestFacts.Outlet(1, available: 6, sold: 48, days: 30)],
            Policy,
            source => source.LocationId == 99);

        Assert.False(Assert.Single(result).CanInitiate);
    }
}

internal static class TestFacts
{
    public static VariantLocationFact Outlet(long locationId, int available, long sold, int days) =>
        new(
            VariantId: 11,
            ProductId: 7,
            MainCode: "BRK-1005",
            ProductName: "Premium Borka",
            Sku: "BRK-1005-BLK-XL",
            VariantLabel: "Black / XL",
            CategoryId: 3,
            CategoryName: "Borka",
            ProductActive: true,
            LocationId: locationId,
            LocationType: OwnerDashboardLocationTypes.Outlet,
            LocationName: $"Outlet {locationId}",
            OnHand: available,
            PendingOutbound: 0,
            PendingInbound: 0,
            NetUnitsSold: sold,
            NetSales: sold * 100m,
            EligibleSellingDays: days,
            HasStockEvidence: true,
            EverSold: sold > 0,
            LastSaleDate: null,
            UnitCost: 50m,
            StockValue: available * 50m);
}
