using Moq;
using RetailPOS.Application.Pos;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.Commands;
using RetailPOS.Application.Pos.ReadModels;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.Pos;

public sealed class CashSessionCloseEvidenceValidatorTests
{
    [Fact]
    public void Denominations_must_total_the_counted_cash()
    {
        var error = Assert.Throws<CashSessionException>(() => CashSessionCloseEvidenceValidator.Validate(
            100m, [new CashDenominationInput(50m, 1), new CashDenominationInput(20m, 2)], []));

        Assert.Equal(CashSessionErrorCodes.DenominationMismatch, error.Code);
    }

    [Fact]
    public void Matching_denominations_are_accepted_and_optional()
    {
        CashSessionCloseEvidenceValidator.Validate(110m, [new CashDenominationInput(50m, 2), new CashDenominationInput(10m, 1)], []);
        CashSessionCloseEvidenceValidator.Validate(110m, [], []);
    }

    [Fact]
    public void Handed_over_cash_cannot_exceed_the_count()
    {
        var error = Assert.Throws<CashSessionException>(() => CashSessionCloseEvidenceValidator.Validate(
            100m, [], [new CashHandoverInput(CashHandoverMethods.CashToManager, 60m, RecipientName: "A"),
                new CashHandoverInput(CashHandoverMethods.CashToOwner, 60m, RecipientName: "B")]));

        Assert.Equal(CashSessionErrorCodes.HandoverExceedsCount, error.Code);
    }

    [Fact]
    public void A_bank_deposit_needs_the_bank_and_a_cash_handover_needs_a_recipient()
    {
        Assert.Equal(CashSessionErrorCodes.HandoverInvalid, Assert.Throws<CashSessionException>(() =>
            CashSessionCloseEvidenceValidator.Validate(100m, [], [new CashHandoverInput(CashHandoverMethods.BankDeposit, 10m)])).Code);
        Assert.Equal(CashSessionErrorCodes.HandoverInvalid, Assert.Throws<CashSessionException>(() =>
            CashSessionCloseEvidenceValidator.Validate(100m, [], [new CashHandoverInput(CashHandoverMethods.CashToManager, 10m)])).Code);
        CashSessionCloseEvidenceValidator.Validate(100m, [], [
            new CashHandoverInput(CashHandoverMethods.BankDeposit, 10m, BankName: "City Bank"),
            new CashHandoverInput(CashHandoverMethods.CashToOwner, 90m, RecipientUserId: 3)]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Handover_amounts_must_be_positive(string amount)
    {
        var error = Assert.Throws<CashSessionException>(() => CashSessionCloseEvidenceValidator.Validate(
            100m, [], [new CashHandoverInput(CashHandoverMethods.CashToOwner, decimal.Parse(amount), RecipientName: "A")]));

        Assert.Equal(CashSessionErrorCodes.HandoverInvalid, error.Code);
    }

    [Fact]
    public void Counted_cash_cannot_be_negative()
    {
        Assert.Equal(CashSessionErrorCodes.CountInvalid,
            Assert.Throws<CashSessionException>(() => CashSessionCloseEvidenceValidator.Validate(-1m, [], [])).Code);
    }
}

public sealed class CloseCashSessionCommandHandlerTests
{
    private static readonly DateTime Opened = new(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);

    private sealed class Harness
    {
        public Mock<ICashSessionStore> Store { get; } = new();
        public RecordingCashSessionEvents Events { get; } = new();
        public NoopCashSessionTransaction Transaction { get; } = new();
        public CashSessionCloseSnapshot? Snapshot { get; private set; }
        public IReadOnlyList<CashSessionHandover> Handovers { get; private set; } = [];
        public CashDifferenceApproval? Approval { get; private set; }
        public CashSessionClosePersist? Persisted { get; private set; }
        public CloseCashSessionCommandHandler Handler { get; }

        public Harness(string status = "open", IReadOnlyList<CashSessionSaleFact>? sales = null, DateTime? now = null)
        {
            var session = new CashSession
            {
                Id = 1, BusinessId = 1, OutletId = 10, TerminalId = 20, OpenedAt = Opened, OpeningCashAmount = 100m,
                BusinessDate = new DateTime(2026, 11, 3), Status = status, DurationSnapshotHours = 20,
                TimeZoneIdSnapshot = "Asia/Dhaka", OpenedByUserId = 5,
                ClosedAt = status == "closed" ? Opened.AddHours(9) : null, ClosedByUserId = status == "closed" ? 6 : null,
                ExpectedClosingCashAmount = status == "closed" ? 150m : null, ActualClosingCashAmount = status == "closed" ? 150m : null
            };
            var authorizer = new Mock<ICashSessionWriteOutletAuthorizer>();
            authorizer.Setup(a => a.AuthorizeWriteAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(10);
            Store.Setup(s => s.GetOutletIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(10);
            Store.Setup(s => s.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Transaction);
            Store.Setup(s => s.LockAsync(1, CashSessionLockMode.Update, It.IsAny<CancellationToken>())).ReturnsAsync(session);
            Store.Setup(s => s.GetFactsAsync(1, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CashSessionFacts(CashSessionMapper.ToSnapshot(session), sales ?? [], []));
            Store.Setup(s => s.MarkClosedAsync(It.IsAny<CashSessionClosePersist>(), It.IsAny<CancellationToken>()))
                .Callback<CashSessionClosePersist, CancellationToken>((p, _) => Persisted = p)
                .ReturnsAsync(true);
            Store.Setup(s => s.AddCloseEvidenceAsync(
                    It.IsAny<CashSessionCloseSnapshot>(), It.IsAny<IReadOnlyList<CashSessionHandover>>(),
                    It.IsAny<CashDifferenceApproval?>(), It.IsAny<CancellationToken>()))
                .Callback<CashSessionCloseSnapshot, IReadOnlyList<CashSessionHandover>, CashDifferenceApproval?, CancellationToken>(
                    (snapshot, handovers, approval, _) =>
                    {
                        Snapshot = snapshot;
                        Handovers = handovers;
                        Approval = approval;
                        if (approval is not null) approval.Id = 55;
                    })
                .Returns(Task.CompletedTask);

            Handler = new CloseCashSessionCommandHandler(
                Store.Object,
                authorizer.Object,
                Mock.Of<ICashSessionCloseAuditWriter>(),
                Mock.Of<ICashSessionClosedPublisher>(),
                Events,
                new FixedTimeProvider(now ?? Opened.AddHours(9)));
        }
    }

    private static CashSessionSaleFact CashSale(decimal amount) =>
        new(1, "completed", "cash", amount, [new CashSessionPaymentFact("cash", amount, amount, 0m, amount)]);

    [Fact]
    public async Task Close_snapshots_the_authoritative_summary_and_variance_without_touching_the_ledger()
    {
        var harness = new Harness(sales: [CashSale(50m)]); // expected = 100 + 50 = 150

        var result = await harness.Handler.Handle(
            new CloseCashSessionCommand(1, 140m, "short by 10", 6), CancellationToken.None);

        Assert.Equal("closed", result.State);
        Assert.Equal(150m, harness.Snapshot!.ExpectedCash);
        Assert.Equal(140m, harness.Snapshot.CountedCash);
        Assert.Equal(-10m, harness.Snapshot.Variance);
        Assert.Equal(50m, harness.Snapshot.CashSalesRetained);
        Assert.NotNull(harness.Approval);
        Assert.Equal(CashDifferenceStatuses.Pending, harness.Approval!.Status);
        Assert.Equal(CashDifferencePostingStatuses.NotRequired, harness.Approval.PostingStatus);
        Assert.Equal(-10m, harness.Approval.VarianceAmount);
        Assert.Equal(6, harness.Approval.CreatedByUserId);
        Assert.True(harness.Transaction.Committed);
        Assert.Contains(harness.Events.Events, e => e.MessageType == CashSessionEventTypes.Closed);
        Assert.Contains(harness.Events.Events, e => e.MessageType == CashSessionEventTypes.VarianceRaised);
    }

    [Fact]
    public async Task Zero_variance_creates_no_approval_and_no_variance_event()
    {
        var harness = new Harness(sales: [CashSale(50m)]);

        await harness.Handler.Handle(new CloseCashSessionCommand(1, 150m, null, 6), CancellationToken.None);

        Assert.Null(harness.Approval);
        Assert.DoesNotContain(harness.Events.Events, e => e.MessageType == CashSessionEventTypes.VarianceRaised);
    }

    [Fact]
    public async Task A_non_zero_variance_requires_a_reason_and_rolls_back()
    {
        var harness = new Harness(sales: [CashSale(50m)]);

        var error = await Assert.ThrowsAsync<CashSessionException>(() =>
            harness.Handler.Handle(new CloseCashSessionCommand(1, 149m, null, 6), CancellationToken.None));

        Assert.Equal(CashSessionErrorCodes.VarianceReasonRequired, error.Code);
        Assert.True(harness.Transaction.RolledBack);
        Assert.Null(harness.Persisted);
    }

    [Fact]
    public async Task An_overdue_session_can_still_be_closed()
    {
        var harness = new Harness(now: Opened.AddHours(26));

        var result = await harness.Handler.Handle(new CloseCashSessionCommand(1, 100m, null, 6), CancellationToken.None);

        Assert.Equal("closed", result.State);
        Assert.True(harness.Snapshot!.WasOverdueAtClose);
    }

    [Fact]
    public async Task Handovers_reconcile_counted_equals_handed_over_plus_retained()
    {
        var harness = new Harness();

        await harness.Handler.Handle(
            new CloseCashSessionCommand(
                1, 100m, null, 6,
                Handovers: [new CashHandoverInput(CashHandoverMethods.BankDeposit, 70m, BankName: "City"),
                    new CashHandoverInput(CashHandoverMethods.CashToManager, 10m, RecipientName: "Manager")]),
            CancellationToken.None);

        Assert.Equal(80m, harness.Snapshot!.HandedOverTotal);
        Assert.Equal(20m, harness.Snapshot.RetainedAtOutlet);
        Assert.Equal(CashHandoverStatuses.PendingBankVerification, harness.Handovers.Single(h => h.Method == CashHandoverMethods.BankDeposit).Status);
        Assert.Equal(CashHandoverStatuses.Handed, harness.Handovers.Single(h => h.Method == CashHandoverMethods.CashToManager).Status);
    }

    [Fact]
    public async Task Closing_an_already_closed_session_returns_the_existing_result_and_writes_nothing()
    {
        var harness = new Harness(status: "closed");

        var result = await harness.Handler.Handle(new CloseCashSessionCommand(1, 1m, "ignored", 9), CancellationToken.None);

        Assert.Equal("closed", result.State);
        Assert.Equal(150m, result.ExpectedClosingCashAmount);
        Assert.Null(harness.Persisted);
        Assert.Null(harness.Snapshot);
        Assert.Empty(harness.Events.Events);
        harness.Store.Verify(s => s.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Close_by_a_user_outside_the_outlet_scope_is_rejected_before_any_lock()
    {
        var harness = new Harness();
        var authorizer = new Mock<ICashSessionWriteOutletAuthorizer>();
        authorizer.Setup(a => a.AuthorizeWriteAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());
        var handler = new CloseCashSessionCommandHandler(
            harness.Store.Object, authorizer.Object, Mock.Of<ICashSessionCloseAuditWriter>(),
            Mock.Of<ICashSessionClosedPublisher>(), harness.Events, new FixedTimeProvider(Opened.AddHours(9)));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.Handle(new CloseCashSessionCommand(1, 100m, null, 6), CancellationToken.None));

        harness.Store.Verify(s => s.LockAsync(It.IsAny<long>(), It.IsAny<CashSessionLockMode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_close_lock_is_taken_before_the_summary_is_computed()
    {
        var harness = new Harness();
        var order = new List<string>();
        harness.Store.Setup(s => s.LockAsync(1, CashSessionLockMode.Update, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("lock"))
            .ReturnsAsync(new CashSession
            {
                Id = 1, BusinessId = 1, OutletId = 10, TerminalId = 20, OpenedAt = Opened, OpeningCashAmount = 100m,
                BusinessDate = new DateTime(2026, 11, 3), Status = "open", DurationSnapshotHours = 20, TimeZoneIdSnapshot = "UTC"
            });
        harness.Store.Setup(s => s.GetFactsAsync(1, It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("facts"))
            .ReturnsAsync(new CashSessionFacts(
                new CashSessionSnapshot(1, 1, 10, 20, new DateTime(2026, 11, 3), Opened, null, 100m, null, null, null, "open", 5, null, null, null),
                [], []));

        await harness.Handler.Handle(new CloseCashSessionCommand(1, 100m, null, 6), CancellationToken.None);

        Assert.Equal(["lock", "facts"], order);
    }
}
