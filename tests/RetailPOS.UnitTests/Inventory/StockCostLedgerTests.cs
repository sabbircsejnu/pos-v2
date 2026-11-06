using RetailPOS.Application.Inventory.Costing;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.Inventory;

public class StockCostLedgerTests
{
    private const long Variant = 7;
    private static readonly StockCostLocation Warehouse = new(1, "warehouse");
    private static readonly StockCostLocation Outlet = new(2, "outlet");

    private static StockCostMovement In(
        StockCostLocation location,
        int quantity,
        decimal unitCost,
        string referenceType = StockLedgerReferenceType.Grn,
        long referenceId = 1,
        bool reliable = true) =>
        new(Variant, location.LocationId, location.LocationType, referenceType, referenceId, quantity, 0, unitCost, quantity * unitCost, reliable);

    private static StockCostMovement Out(StockCostLocation location, int quantity, decimal unitCost, bool reliable = true) =>
        new(Variant, location.LocationId, location.LocationType, StockLedgerReferenceType.Sale, 2, 0, quantity, unitCost, quantity * unitCost, reliable);

    private static StockCostLedger Ledger(params StockCostMovement[] movements)
    {
        var ledger = new StockCostLedger();
        foreach (var movement in movements)
            ledger.Apply(movement);
        return ledger;
    }

    private static StockCostEvidence Evidence(long? provenGrn = null, Dictionary<long, StockCostLocation>? origins = null) => new(
        provenGrn is null ? new HashSet<(long, long)>() : new HashSet<(long, long)> { (provenGrn.Value, Variant) },
        origins ?? []);

    [Fact]
    public void ReliableHistory_ThatExplainsTheBalance_IsValuedAtWeightedAverage()
    {
        var ledger = Ledger(In(Warehouse, 10, 5m), In(Warehouse, 10, 7m, referenceId: 3), Out(Warehouse, 5, 99m));

        var position = ledger.Classify(Variant, Warehouse, 15, StockCostEvidence.None);

        Assert.Equal(StockCostKind.Valued, position.Kind);
        Assert.Equal(90m, position.Value);
        Assert.Equal(6m, position.UnitCost);
    }

    [Fact]
    public void NoHistory_IsUnvalued() =>
        Assert.Equal(StockCostKind.Unvalued, new StockCostLedger().Classify(Variant, Warehouse, 5, StockCostEvidence.None).Kind);

    [Fact]
    public void BalanceTheReplayCannotExplain_IsUnvalued()
    {
        var ledger = Ledger(In(Warehouse, 10, 5m));

        Assert.Equal(StockCostKind.Unvalued, ledger.Classify(Variant, Warehouse, 11, StockCostEvidence.None).Kind);
    }

    [Fact]
    public void UnreliableReceipt_LeavesTheBalanceUnexplained()
    {
        var ledger = Ledger(In(Warehouse, 10, 0m, reliable: false), Out(Warehouse, 4, 0m));

        Assert.Equal(StockCostKind.Unvalued, ledger.Classify(Variant, Warehouse, 6, StockCostEvidence.None).Kind);
    }

    [Fact]
    public void UnprovenZero_IsUnvalued()
    {
        var ledger = Ledger(In(Warehouse, 10, 0m));

        Assert.Equal(StockCostKind.Unvalued, ledger.Classify(Variant, Warehouse, 10, StockCostEvidence.None).Kind);
        Assert.Contains(1L, ledger.ZeroInboundGrnIds);
    }

    [Fact]
    public void ProvenZero_IsVerifiedZero()
    {
        var position = Ledger(In(Warehouse, 10, 0m)).Classify(Variant, Warehouse, 10, Evidence(provenGrn: 1));

        Assert.Equal(StockCostKind.VerifiedZero, position.Kind);
        Assert.Equal(0m, position.Value);
    }

    [Fact]
    public void UnprovenZero_SpoilsAPositiveBalance()
    {
        var ledger = Ledger(In(Warehouse, 5, 0m), In(Warehouse, 5, 10m, referenceId: 3));

        Assert.Equal(StockCostKind.Unvalued, ledger.Classify(Variant, Warehouse, 10, StockCostEvidence.None).Kind);
    }

    [Fact]
    public void ProvenZero_BlendsIntoTheAverage()
    {
        var position = Ledger(In(Warehouse, 5, 0m), In(Warehouse, 5, 10m, referenceId: 3))
            .Classify(Variant, Warehouse, 10, Evidence(provenGrn: 1));

        Assert.Equal(StockCostKind.Valued, position.Kind);
        Assert.Equal(50m, position.Value);
        Assert.Equal(5m, position.UnitCost);
    }

    [Fact]
    public void TransferZero_IsProvenOnlyWhenItsOriginWasProvenFree()
    {
        var ledger = Ledger(
            In(Warehouse, 10, 0m),
            Out(Warehouse, 4, 0m),
            In(Outlet, 4, 0m, StockLedgerReferenceType.StockTransfer, 50));
        var origins = new Dictionary<long, StockCostLocation> { [50] = Warehouse };

        Assert.Equal(StockCostKind.VerifiedZero, ledger.Classify(Variant, Outlet, 4, Evidence(1, origins)).Kind);
        Assert.Equal(StockCostKind.Unvalued, ledger.Classify(Variant, Outlet, 4, Evidence(null, origins)).Kind);
    }

    [Fact]
    public void OldZeroTransfer_FromAPricedSource_IsUnvalued()
    {
        // The form used to send a cost of zero even though the source held priced stock.
        var ledger = Ledger(
            In(Warehouse, 10, 80m),
            Out(Warehouse, 4, 0m),
            In(Outlet, 4, 0m, StockLedgerReferenceType.StockTransfer, 50));
        var origins = new Dictionary<long, StockCostLocation> { [50] = Warehouse };

        Assert.Equal(StockCostKind.Unvalued, ledger.Classify(Variant, Outlet, 4, Evidence(null, origins)).Kind);
    }

    [Fact]
    public void ZeroTransfersInACycle_AreNeverProven()
    {
        var ledger = Ledger(
            In(Warehouse, 2, 0m, StockLedgerReferenceType.StockTransfer, 50),
            In(Outlet, 2, 0m, StockLedgerReferenceType.StockTransfer, 51));
        var origins = new Dictionary<long, StockCostLocation> { [50] = Outlet, [51] = Warehouse };

        Assert.Equal(StockCostKind.Unvalued, ledger.Classify(Variant, Warehouse, 2, Evidence(null, origins)).Kind);
    }

    [Fact]
    public void EvidenceRows_MoveNoStock_AndChangeNothing()
    {
        var evidenceRow = new StockCostMovement(
            Variant, Warehouse.LocationId, Warehouse.LocationType, StockLedgerReferenceType.StockTransfer, 9, 0, 0, 0m, 0m, false);

        var position = Ledger(In(Warehouse, 10, 5m), evidenceRow).Classify(Variant, Warehouse, 10, StockCostEvidence.None);

        Assert.Equal(StockCostKind.Valued, position.Kind);
    }

    [Fact]
    public void GrnCost_FollowsTheJournalValue()
    {
        var cost = GrnLedgerCost.Resolve(5, 8m, 36m, 8m, 10m);

        Assert.Equal(7.2m, cost.UnitCost);
        Assert.Equal(36m, cost.MovementValue);
        Assert.False(cost.ZeroCostVerified);
    }

    [Theory]
    [InlineData(0, 0, 0, true)]   // Nothing was priced: both documents say free.
    [InlineData(10, 10, 100, true)] // The order discounted the line in full.
    [InlineData(0, 25, 0, false)]  // The GRN says zero, the order priced it: the documents disagree.
    public void GrnZero_IsVerifiedOnlyWhenDocumentsAgree(decimal grnCost, decimal orderPrice, decimal discount, bool verified)
    {
        var cost = GrnLedgerCost.Resolve(5, grnCost, 0m, orderPrice, discount);

        Assert.Equal(verified, cost.ZeroCostVerified);
        Assert.Equal(verified ? 0m : null, cost.UnitCost);
        Assert.Equal(verified ? 0m : null, cost.MovementValue);
    }
}
