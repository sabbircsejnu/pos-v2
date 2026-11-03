using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Inventory;

public sealed class MobileStockTakeReconciliationEvaluatorTests
{
    private static readonly DateTime Start = new(2026, 10, 17, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Submitted = Start.AddHours(3);

    private static ReconciliationBaselineEvidence Baseline(
        string consistency = "InventoryStatementSnapshot",
        DateTime? start = null,
        DateTime? completed = null,
        DateTime? submitted = null) =>
        new(
            consistency,
            start ?? Start,
            completed ?? Start.AddSeconds(1),
            submitted ?? Submitted);

    private static ReconciliationLineEvidence Line(
        decimal baseline = 10,
        decimal? counted = 8,
        decimal current = 10,
        DateTime? observed = null) =>
        new(1, baseline, counted, observed, current);

    private static ReconciliationLedgerEntry Entry(
        long sequence,
        int qtyIn,
        int qtyOut,
        int balance,
        DateTime createdAt,
        string type = StockLedgerTransactionType.Sale) =>
        new(1, type, qtyIn, qtyOut, balance, sequence, createdAt);

    private static Dictionary<long, ReconciliationLedgerEntry> Before(int balance = 10) =>
        new() { [1] = Entry(1, balance, 0, balance, Start.AddHours(-2), StockLedgerTransactionType.Grn) };

    private static MobileStockTakeReconciliationEvaluation Run(
        ReconciliationLineEvidence line,
        Dictionary<long, ReconciliationLedgerEntry>? before = null,
        params ReconciliationLedgerEntry[] entries) =>
        MobileStockTakeReconciliationEvaluator.Evaluate(
            Baseline(), [line], before ?? Before(), entries, Submitted.AddMinutes(1));

    [Fact]
    public void ConsistentLedgerWithoutMovementsInTheWindow_ReportsNoConflict()
    {
        var result = Run(Line());

        Assert.Equal(MobileStockTakeReconciliationStatus.NoConflictDetected, result.Status);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void NoLedgerAndZeroBaselineWithZeroInventory_IsConsistent()
    {
        var result = Run(Line(baseline: 0, counted: 0, current: 0), new Dictionary<long, ReconciliationLedgerEntry>());

        Assert.Equal(MobileStockTakeReconciliationStatus.NoConflictDetected, result.Status);
    }

    [Fact]
    public void MovementCreatedBetweenBaselineAndCountObservation_IsUnresolvedWithRowCountsOnly()
    {
        var movement = Entry(2, 0, 1, 9, Start.AddMinutes(30));
        var returned = Entry(3, 1, 0, 10, Start.AddMinutes(40), StockLedgerTransactionType.Return);
        var result = Run(
            Line(observed: Start.AddHours(1)),
            Before(),
            movement,
            returned);

        Assert.Equal(MobileStockTakeReconciliationStatus.Unresolved, result.Status);
        var reason = Assert.Single(result.Reasons);
        Assert.Equal(MobileStockTakeReconciliationReasonCodes.MovementDuringCountWindow, reason.Code);
        Assert.Equal(1, reason.AffectedLineCount);
        Assert.Equal(1, result.Movements.SaleCount);
        Assert.Equal(1, result.Movements.ReturnCount);
        Assert.Equal(0, result.Movements.PostCountMovementCount);
    }

    [Fact]
    public void MovementAfterTheLatestPossibleObservation_DoesNotConflictWithTheCount()
    {
        var late = Entry(2, 0, 1, 9, Start.AddHours(2));
        var result = Run(Line(current: 9, observed: Start.AddHours(1)), Before(), late);

        Assert.Equal(MobileStockTakeReconciliationStatus.NoConflictDetected, result.Status);
        Assert.Equal(1, result.Movements.PostCountMovementCount);
    }

    [Fact]
    public void WithoutReceiptEvidence_TheErpSubmissionTimeIsTheOnlySoundObservationBound()
    {
        var movement = Entry(2, 0, 1, 9, Start.AddHours(2));
        var result = Run(Line(current: 9, observed: null), Before(), movement);

        Assert.Equal(MobileStockTakeReconciliationStatus.Unresolved, result.Status);
    }

    [Fact]
    public void ReceiptTimeLaterThanSubmission_IsCappedAtSubmission()
    {
        var movement = Entry(2, 0, 1, 9, Submitted.AddMinutes(5));
        var result = Run(Line(current: 9, observed: Submitted.AddHours(1)), Before(), movement);

        Assert.Equal(MobileStockTakeReconciliationStatus.NoConflictDetected, result.Status);
    }

    [Theory]
    [InlineData(MobileStockTakeReconciliationReasonCodes.BaselineLedgerEvidenceMissing)]
    [InlineData(MobileStockTakeReconciliationReasonCodes.BaselineLedgerMismatch)]
    [InlineData(MobileStockTakeReconciliationReasonCodes.InventoryLedgerMismatch)]
    [InlineData(MobileStockTakeReconciliationReasonCodes.MovementBalanceChainBroken)]
    [InlineData(MobileStockTakeReconciliationReasonCodes.MovementOrderAnomaly)]
    public void InsufficientOrContradictoryEvidence_IsBlocked(string expectedCode)
    {
        var result = expectedCode switch
        {
            MobileStockTakeReconciliationReasonCodes.BaselineLedgerEvidenceMissing =>
                Run(Line(), new Dictionary<long, ReconciliationLedgerEntry>()),
            MobileStockTakeReconciliationReasonCodes.BaselineLedgerMismatch =>
                Run(Line(), Before(9)),
            MobileStockTakeReconciliationReasonCodes.InventoryLedgerMismatch =>
                Run(Line(current: 12)),
            MobileStockTakeReconciliationReasonCodes.MovementBalanceChainBroken =>
                Run(Line(current: 7), Before(), Entry(2, 0, 1, 7, Start.AddMinutes(5))),
            _ =>
                Run(Line(current: 9), Before(), Entry(1, 0, 1, 9, Start.AddMinutes(5)))
        };

        Assert.Equal(MobileStockTakeReconciliationStatus.Blocked, result.Status);
        Assert.Contains(result.Reasons, reason => reason.Code == expectedCode);
    }

    [Fact]
    public void BackdatedCreationTimeBelowAnEarlierSequence_IsAnOrderAnomaly()
    {
        var result = Run(
            Line(current: 8),
            Before(),
            Entry(2, 0, 1, 9, Start.AddMinutes(30)),
            Entry(3, 0, 1, 8, Start.AddMinutes(10)));

        Assert.Equal(MobileStockTakeReconciliationStatus.Blocked, result.Status);
        Assert.Contains(result.Reasons, reason =>
            reason.Code == MobileStockTakeReconciliationReasonCodes.MovementOrderAnomaly);
    }

    [Theory]
    [InlineData("Unverified", true, true)]
    [InlineData("InventoryStatementSnapshot", false, true)]
    [InlineData("InventoryStatementSnapshot", true, false)]
    public void UnverifiableBaselineOrMissingSubmissionTime_IsBlocked(
        string consistency,
        bool hasStart,
        bool hasSubmission)
    {
        var baseline = new ReconciliationBaselineEvidence(
            consistency,
            hasStart ? Start : null,
            hasStart ? Start.AddSeconds(1) : null,
            hasSubmission ? Submitted : null);
        var result = MobileStockTakeReconciliationEvaluator.Evaluate(
            baseline,
            [Line()],
            Before(),
            [],
            Submitted);

        Assert.Equal(MobileStockTakeReconciliationStatus.Blocked, result.Status);
    }

    [Fact]
    public void NoCountedLines_IsBlockedNotClean()
    {
        var result = Run(Line(counted: null));

        Assert.Equal(MobileStockTakeReconciliationStatus.Blocked, result.Status);
        Assert.Contains(result.Reasons, reason =>
            reason.Code == MobileStockTakeReconciliationReasonCodes.NoCountedLines);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Submitted")]
    [InlineData("Rejected")]
    public void ResultsAreNotAvailableBeforeErpApproval(string status)
    {
        var (availability, reasons) = MobileStockTakeResultsPolicy.Decide(
            status, null, null, 3, 3, 0);

        Assert.Equal(MobileStockTakeResultsAvailability.NotAvailable, availability);
        Assert.Equal([MobileStockTakeResultsReasonCodes.NotApproved], reasons);
    }

    [Fact]
    public void ApprovedResultsRequireApprovalEvidenceCompleteCountsConsistentDifferencesAndNoConflict()
    {
        var clean = Run(Line());
        var unresolved = Run(Line(current: 9), Before(), Entry(2, 0, 1, 9, Start.AddMinutes(5)));

        Assert.Equal(
            MobileStockTakeResultsAvailability.Available,
            MobileStockTakeResultsPolicy.Decide("Approved", Submitted, clean, 2, 2, 0).Availability);
        Assert.Contains(
            MobileStockTakeResultsReasonCodes.ApprovalEvidenceMissing,
            MobileStockTakeResultsPolicy.Decide("Approved", null, clean, 2, 2, 0).Reasons);
        Assert.Contains(
            MobileStockTakeResultsReasonCodes.LinesUncounted,
            MobileStockTakeResultsPolicy.Decide("Approved", Submitted, clean, 2, 1, 0).Reasons);
        Assert.Contains(
            MobileStockTakeResultsReasonCodes.DifferenceInconsistent,
            MobileStockTakeResultsPolicy.Decide("Completed", Submitted, clean, 2, 2, 1).Reasons);
        var withheld = MobileStockTakeResultsPolicy.Decide("AdjustmentGenerated", Submitted, unresolved, 2, 2, 0);
        Assert.Equal(MobileStockTakeResultsAvailability.Withheld, withheld.Availability);
        Assert.Contains(MobileStockTakeReconciliationReasonCodes.MovementDuringCountWindow, withheld.Reasons);
    }

    [Theory]
    [InlineData("Draft", null, null, 5L, "Counting")]
    [InlineData("Draft", "Accepted", null, 5L, "SubmissionInProgress")]
    [InlineData("Draft", "RetryScheduled", null, 5L, "SubmissionInProgress")]
    [InlineData("Draft", "Failed", 5L, 5L, "SubmissionFailed")]
    [InlineData("Draft", "Failed", 4L, 5L, "Counting")]
    [InlineData("Draft", "ErpSubmitted", 4L, 5L, "Counting")]
    [InlineData("Submitted", "ErpSubmitted", 5L, 5L, "SubmittedForErpReview")]
    [InlineData("Rejected", null, null, 5L, "RejectedByErp")]
    [InlineData("Approved", null, null, 5L, "ApprovedByErp")]
    [InlineData("AdjustmentGenerated", null, null, 5L, "AdjustmentDraftGenerated")]
    [InlineData("Completed", null, null, 5L, "Completed")]
    [InlineData("Surprise", null, null, 5L, "Unknown")]
    public void LifecycleStageSeparatesSubmissionFromErpDecisions(
        string status,
        string? jobStatus,
        long? jobRevision,
        long revision,
        string expected) =>
        Assert.Equal(
            expected,
            MobileStockTakeLifecycleStageResolver.Resolve(status, revision, jobStatus, jobRevision));
}
