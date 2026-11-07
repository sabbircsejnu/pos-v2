using RetailPOS.Application.OwnerDashboards;

namespace RetailPOS.Tests.OwnerDashboards;

/// <summary>Gap 4.5: a measured item's selling rate is never compared with a threshold written for pieces.</summary>
public sealed class UnitAwareSlowMoverTests
{
    private static readonly OwnerDashboardPolicy Policy = new();

    private static VariantLocationFact Fact(string unit, decimal onHand, decimal sold, int days = 30) =>
        TestFacts.Outlet(1, 0, 0, days) with { BaseUnitCode = unit, OnHand = onHand, NetUnitsSold = sold };

    [Theory]
    // Clothing in pcs keeps the 0.2 pieces per day rule exactly as before.
    [InlineData("pcs", 10, 3, SlowClassifications.LowSales)]
    [InlineData("pcs", 10, 9, SlowClassifications.Normal)]
    // Attar: 300 ml sold in 30 days is 10 ml a day. 500 ml lasts 50 days, which is normal.
    [InlineData("ml", 500, 300, SlowClassifications.Normal)]
    // 60 ml in 30 days is 2 ml a day. It far exceeds 0.2, yet 500 ml would last 250 days: slow.
    [InlineData("ml", 500, 60, SlowClassifications.LowSales)]
    // Fabric: 3 m in 30 days is 0.1 m a day, below 0.2, but 5 m on hand lasts 50 days: normal.
    [InlineData("m", 5, 3, SlowClassifications.Normal)]
    [InlineData("m", 100, 3, SlowClassifications.LowSales)]
    // Grocery: 150 kg in 30 days, 20 kg on hand: 4 days of cover.
    [InlineData("kg", 20, 150, SlowClassifications.Normal)]
    public void Classify_UsesThePieceRuleForPcs_AndStockCoverForMeasuredUnits(string unit, double onHand, double sold, string expected)
    {
        var result = SlowMoverClassifier.Classify(Fact(unit, (decimal)onHand, (decimal)sold), Policy);

        Assert.Equal(expected, result.Classification);
        if (unit != "pcs")
            Assert.Contains(unit, result.Reason);
    }

    [Fact]
    public void Eligibility_HalfALitre_IsNotJudgedAgainstAOnePieceMinimum()
    {
        var policy = Policy with { MinOnHandQty = 1 };

        Assert.True(SlowMoverClassifier.IsEligible(Fact("l", 0.5m, 0m), policy));
        Assert.False(SlowMoverClassifier.IsEligible(Fact("pcs", 0.5m, 0m), policy));
        Assert.False(SlowMoverClassifier.IsEligible(Fact("ml", 0m, 0m), policy));
    }

    [Fact]
    public void Classify_AMeasuredItemThatNeverSold_IsStillNeverSold()
    {
        var result = SlowMoverClassifier.Classify(Fact("ml", 500m, 0m) with { EverSold = false }, Policy);

        Assert.Equal(SlowClassifications.NeverSold, result.Classification);
    }

    [Fact]
    public void TransferOpportunities_AMeasuredItem_IsNotHeldToPieceThresholds()
    {
        // 0.3 ml a day is real demand for Attar, though below the 0.5 pieces-a-day minimum.
        var source = TestFacts.Outlet(1, 0, 0, 30) with { BaseUnitCode = "ml", OnHand = 1000m, NetUnitsSold = 30m };
        var destination = TestFacts.Outlet(2, 0, 0, 30) with { BaseUnitCode = "ml", OnHand = 1m, NetUnitsSold = 9m };

        var measured = TransferOpportunityEngine.Build([source, destination], Policy);
        var pieces = TransferOpportunityEngine.Build(
            [source with { BaseUnitCode = "pcs" }, destination with { BaseUnitCode = "pcs" }],
            Policy);

        var suggestion = Assert.Single(measured);
        // 21 days of cover at 0.3 ml a day is 7 ml; 1 ml is there.
        Assert.Equal(6m, suggestion.SuggestedQty);
        Assert.Empty(pieces);
    }
}
