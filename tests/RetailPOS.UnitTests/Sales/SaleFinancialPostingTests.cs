using Moq;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.Commands;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Core.Entities;

namespace RetailPOS.Tests.Sales;

/// <summary>
/// Phase 3.1 (D10) state machine and intent rules without a database. The transactional, locking and concurrency
/// guarantees are proven on PostgreSQL in <c>SaleFinancialPostingPostgresTests</c>.
/// </summary>
public sealed class SaleFinancialPostingTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

    // ── intent factory ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ForSale_snapshots_the_committed_amounts_with_a_stable_key_and_hash()
    {
        var posting = SaleFinancialIntentFactory.ForSale(Sale(), 1, Now);

        Assert.Equal(SaleFinancialPostingKinds.Sale, posting.Kind);
        Assert.Null(posting.ReversalType);
        Assert.Equal("sale:42:posting:v1", posting.SourceKey);
        Assert.Equal(SaleFinancialPostingStatuses.Pending, posting.Status);
        Assert.Equal(Now, posting.NextAttemptAtUtc);
        Assert.Equal(new DateTime(2026, 11, 3), posting.BusinessDate);
        Assert.True(SaleFinancialIntentFactory.IsIntact(posting));

        var snapshot = SaleFinancialIntentFactory.ReadSaleSnapshot(posting);
        Assert.Equal(200m, snapshot.GrossSales);
        Assert.Equal(15m, snapshot.Discount);
        Assert.Equal(9.25m, snapshot.Vat);
        Assert.Equal(140m, snapshot.Cost);
        Assert.Equal(194.25m, snapshot.TotalAmount);
        Assert.Equal(new DateOnly(2026, 11, 3), snapshot.BusinessDate);
        Assert.Equal([194.25m], snapshot.Payments.Select(payment => payment.Amount));
        Assert.Equal(Cashier, snapshot.ActorUserId);
        Assert.Equal(DateTimeKind.Utc, snapshot.TransactionAtUtc.Kind);
    }

    [Fact]
    public void ForReversal_takes_the_returned_share_of_the_recorded_line_net_and_VAT()
    {
        var sale = Sale();
        var posting = SaleFinancialIntentFactory.ForReversal(
            sale, 1, [new SaleRefundLine(501, 3201, 1)], SaleFinancialPostingReversalTypes.Refund, Manager, Now);

        Assert.Equal("sale:42:reversal:v1", posting.SourceKey);
        Assert.Equal(Manager, posting.ActorUserId);
        var snapshot = SaleFinancialIntentFactory.ReadReversalSnapshot(posting);
        Assert.Equal(2, snapshot.Version);
        Assert.Equal(92.5m, snapshot.RefundAmount);   // recorded net 185 × 1/2
        Assert.Equal(4.63m, snapshot.RefundVat);      // recorded VAT 9.25 × 1/2
        Assert.Equal(97.13m, snapshot.RefundTotal);
        Assert.Equal(70m, snapshot.Cost);
        Assert.Equal(97.13m, snapshot.Payments.Single().Amount);
        Assert.Null(snapshot.MissingEvidence);
        Assert.Equal(SaleFinancialPostingReversalTypes.Refund, snapshot.ReversalType);
    }

    [Fact]
    public void A_whole_line_reversal_returns_exactly_the_recorded_net_and_VAT()
    {
        var snapshot = SaleFinancialIntentFactory.ReadReversalSnapshot(SaleFinancialIntentFactory.ForReversal(
            Sale(), 1, [new SaleRefundLine(501, 3201, 2)], SaleFinancialPostingReversalTypes.Void, Manager, Now));
        Assert.Equal(185m, snapshot.RefundAmount);
        Assert.Equal(9.25m, snapshot.RefundVat);
        Assert.Equal(194.25m, snapshot.Payments.Single().Amount);
    }

    [Theory]
    [InlineData("2026-11-03T17:59:59Z", "Asia/Dhaka", "2026-11-03")]
    [InlineData("2026-11-03T18:00:00Z", "Asia/Dhaka", "2026-11-04")]
    [InlineData("2026-11-03T23:30:00Z", "UTC", "2026-11-03")]
    public void The_reversal_accounting_date_is_its_own_date_in_the_effective_zone(string occurred, string zone, string expected)
    {
        var snapshot = SaleFinancialIntentFactory.ReadReversalSnapshot(SaleFinancialIntentFactory.ForReversal(
            Sale(), 1, [new SaleRefundLine(501, 3201, 2)], SaleFinancialPostingReversalTypes.Void, Manager,
            DateTime.Parse(occurred, null, System.Globalization.DateTimeStyles.AdjustToUniversal), zone));
        Assert.Equal(DateOnly.Parse(expected), snapshot.AccountingDate);
        Assert.Equal(zone, snapshot.TimeZoneId);
        Assert.Equal(new DateOnly(2026, 11, 3), snapshot.BusinessDate);   // the original sale's, unchanged
    }

    [Fact]
    public void Missing_line_allocations_are_recorded_as_missing_evidence_not_guessed()
    {
        var sale = Sale();
        sale.Items.Single().TaxableAmount = null;
        var snapshot = SaleFinancialIntentFactory.ReadReversalSnapshot(SaleFinancialIntentFactory.ForReversal(
            sale, 1, [new SaleRefundLine(501, 3201, 1)], SaleFinancialPostingReversalTypes.Refund, Manager, Now));
        Assert.Contains("no recorded net/VAT allocation", snapshot.MissingEvidence);
        Assert.Equal(0m, snapshot.RefundTotal);
        Assert.All(snapshot.Payments, payment => Assert.Equal(0m, payment.Amount));
    }

    [Theory]
    [InlineData(100.00, new[] { 1.0, 1.0, 1.0 }, new[] { 33.34, 33.33, 33.33 })]
    [InlineData(10.00, new[] { 7.0, 3.0 }, new[] { 7.00, 3.00 })]
    [InlineData(0.05, new[] { 1.0, 1.0 }, new[] { 0.03, 0.02 })]
    public void Allocation_splits_in_whole_cents_and_sums_exactly(double total, double[] weights, double[] expected)
    {
        var parts = SaleFinancialIntentFactory.Allocate((decimal)total, weights.Select(weight => (decimal)weight).ToArray());
        Assert.Equal(expected.Select(value => (decimal)value), parts);
        Assert.Equal((decimal)total, parts.Sum());
    }

    [Fact]
    public void The_factory_refuses_unscoped_or_unknown_intents()
    {
        Assert.Throws<InvalidOperationException>(() => SaleFinancialIntentFactory.ForSale(Sale(), 2, Now));
        Assert.Throws<InvalidOperationException>(() => SaleFinancialIntentFactory.ForSale(new Sale { Id = 0, BusinessId = 1 }, 1, Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => SaleFinancialIntentFactory.ForReversal(
            Sale(), 1, [new SaleRefundLine(501, 3201, 1)], "exchange", Manager, Now));
        Assert.Throws<InvalidOperationException>(() => SaleFinancialIntentFactory.ForReversal(
            Sale(), 1, [], SaleFinancialPostingReversalTypes.Void, Manager, Now));
    }

    [Fact]
    public void A_changed_snapshot_no_longer_matches_its_hash()
    {
        var posting = SaleFinancialIntentFactory.ForSale(Sale(), 1, Now);
        posting.SnapshotJson = posting.SnapshotJson.Replace("194.25", "1.00");
        Assert.False(SaleFinancialIntentFactory.IsIntact(posting));
        Assert.Throws<InvalidOperationException>(() => SaleFinancialIntentFactory.ReadReversalSnapshot(posting));
    }

    // ── retry policy and failure labels ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Backoff_doubles_from_the_base_and_is_capped()
    {
        var options = new SaleFinancialPostingOptions { BaseRetrySeconds = 30, MaxRetrySeconds = 3600 };
        Assert.Equal([30d, 60d, 120d, 240d, 3600d, 3600d],
            new[] { 1, 2, 3, 4, 8, 50 }.Select(attempt => options.Backoff(attempt).TotalSeconds));
        Assert.Throws<InvalidOperationException>(() => new SaleFinancialPostingOptions { BatchSize = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() =>
            new SaleFinancialPostingOptions { BaseRetrySeconds = 60, MaxRetrySeconds = 30 }.Validate());
    }

    [Theory]
    [InlineData("Posting date 2026-11-03 belongs to accounting period 'Nov' with status 'Closed'. Only Open periods allow posting.", SaleFinancialPostingErrorCodes.AccountingPeriod)]
    [InlineData("Posting date 2027-01-01 does not match any financial year for this business.", SaleFinancialPostingErrorCodes.AccountingPeriod)]
    [InlineData("No posting account mapping configured for slot 'sales'.", SaleFinancialPostingErrorCodes.AccountingConfiguration)]
    [InlineData("Inactive accounts cannot be used for journal posting: 4000.", SaleFinancialPostingErrorCodes.AccountingConfiguration)]
    [InlineData("Journal is not balanced. Total debit must equal total credit.", SaleFinancialPostingErrorCodes.PostingRejected)]
    public void Failures_are_labelled_for_operators(string message, string code) =>
        Assert.Equal(code, SaleFinancialPostingFailure.Classify(new InvalidOperationException(message)).Code);

    [Fact]
    public void Infrastructure_failures_are_transient_and_messages_are_bounded()
    {
        var (code, message) = SaleFinancialPostingFailure.Classify(new TimeoutException(new string('x', 2000)));
        Assert.Equal(SaleFinancialPostingErrorCodes.Transient, code);
        Assert.Equal(500, message.Length);
        Assert.Equal(SaleFinancialPostingErrorCodes.SnapshotMismatch,
            SaleFinancialPostingFailure.Classify(new SaleFinancialSnapshotException("changed")).Code);
    }

    // ── posting state machine ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_pending_sale_posting_is_posted_once_with_its_journal_and_payment_accounts()
    {
        var world = new World();
        var posting = world.Add(SaleFinancialIntentFactory.ForSale(Sale(), 1, Now));

        var result = await world.Handler().Handle(new ProcessSaleFinancialPostingCommand(posting.Id), default);

        Assert.Equal(SaleFinancialPostingOutcome.Posted, result.Outcome);
        Assert.Equal(SaleFinancialPostingStatuses.Posted, posting.Status);
        Assert.Equal(900L, posting.JournalEntryId);
        Assert.Equal(Now, posting.PostedAtUtc);
        Assert.Equal(1, posting.Attempts);
        Assert.Null(posting.NextAttemptAtUtc);
        Assert.Equal(new Dictionary<long, long> { [601] = 11 }, world.Store.SnapshottedAccounts);
        Assert.Equal(["posted:1"], world.Events.Select(e => e.Discriminator));
        Assert.True(world.Store.Committed);

        var again = await world.Handler().Handle(new ProcessSaleFinancialPostingCommand(posting.Id), default);
        Assert.Equal(SaleFinancialPostingOutcome.AlreadyPosted, again.Outcome);
        Assert.Equal(1, world.Poster.SalePosts);
    }

    [Fact]
    public async Task A_failed_attempt_rolls_back_and_schedules_a_retry_with_backoff()
    {
        var world = new World();
        world.Poster.Failure = new TimeoutException("accounting database unavailable");
        var posting = world.Add(SaleFinancialIntentFactory.ForSale(Sale(), 1, Now));

        var first = await world.Handler().Handle(new ProcessSaleFinancialPostingCommand(posting.Id), default);
        Assert.Equal(SaleFinancialPostingOutcome.Failed, first.Outcome);
        Assert.Equal(SaleFinancialPostingStatuses.Failed, posting.Status);
        Assert.Equal(1, posting.Attempts);
        Assert.Equal(SaleFinancialPostingErrorCodes.Transient, posting.LastErrorCode);
        Assert.Contains("accounting database unavailable", posting.LastError);
        Assert.Equal(Now.AddSeconds(30), posting.NextAttemptAtUtc);
        Assert.Null(posting.JournalEntryId);
        Assert.True(world.Store.RolledBack);

        await world.Handler().Handle(new ProcessSaleFinancialPostingCommand(posting.Id), default);
        Assert.Equal(2, posting.Attempts);
        Assert.Equal(Now.AddSeconds(60), posting.NextAttemptAtUtc);

        world.Poster.Failure = null;
        var recovered = await world.Handler().Handle(new ProcessSaleFinancialPostingCommand(posting.Id), default);
        Assert.Equal(SaleFinancialPostingOutcome.Posted, recovered.Outcome);
        Assert.Equal(3, posting.Attempts);
        Assert.Null(posting.LastErrorCode);
        Assert.Equal(["failed:1", "failed:2", "posted:3"], world.Events.Select(e => e.Discriminator));
    }

    [Fact]
    public async Task A_row_claimed_by_another_worker_is_skipped_untouched()
    {
        var world = new World();
        var posting = world.Add(SaleFinancialIntentFactory.ForSale(Sale(), 1, Now));
        world.Store.LockedElsewhere = true;

        var result = await world.Handler().Handle(new ProcessSaleFinancialPostingCommand(posting.Id), default);

        Assert.Equal(SaleFinancialPostingOutcome.ClaimedElsewhere, result.Outcome);
        Assert.Equal(0, world.Poster.SalePosts);
        Assert.Equal(0, posting.Attempts);
        Assert.Empty(world.Events);
    }

    [Fact]
    public async Task A_posting_of_another_Business_is_refused_without_recording_anything()
    {
        var world = new World(activeBusinessId: 2);
        var posting = world.Add(SaleFinancialIntentFactory.ForSale(Sale(), 1, Now));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            world.Handler().Handle(new ProcessSaleFinancialPostingCommand(posting.Id), default));

        Assert.Equal(SaleFinancialPostingStatuses.Pending, posting.Status);
        Assert.Equal(0, posting.Attempts);
        Assert.Equal(0, world.Poster.SalePosts);
        Assert.True(world.Store.RolledBack);
    }

    [Fact]
    public async Task A_reversal_waits_until_the_sale_journal_has_posted()
    {
        var world = new World();
        var sale = world.Add(SaleFinancialIntentFactory.ForSale(Sale(), 1, Now));
        var reversal = world.Add(SaleFinancialIntentFactory.ForReversal(
            Sale(), 1, [new SaleRefundLine(501, 3201, 2)], SaleFinancialPostingReversalTypes.Void, Manager, Now));

        var deferred = await world.Handler().Handle(new ProcessSaleFinancialPostingCommand(reversal.Id), default);
        Assert.Equal(SaleFinancialPostingOutcome.Deferred, deferred.Outcome);
        Assert.Equal(SaleFinancialPostingStatuses.Pending, reversal.Status);
        Assert.Equal(SaleFinancialPostingErrorCodes.AwaitingSalePosting, reversal.LastErrorCode);
        Assert.Equal(Now.AddSeconds(30), reversal.NextAttemptAtUtc);
        Assert.Equal(0, reversal.Attempts);
        Assert.Equal(0, world.Poster.ReversalPosts);

        await world.Handler().Handle(new ProcessSaleFinancialPostingCommand(sale.Id), default);
        var posted = await world.Handler().Handle(new ProcessSaleFinancialPostingCommand(reversal.Id), default);
        Assert.Equal(SaleFinancialPostingOutcome.Posted, posted.Outcome);
        Assert.Equal(901L, reversal.JournalEntryId);
        Assert.Equal(1, world.Poster.ReversalPosts);
    }

    [Fact]
    public async Task A_reversal_of_a_pre_D10_sale_without_an_intent_posts_directly()
    {
        var world = new World();
        var reversal = world.Add(SaleFinancialIntentFactory.ForReversal(
            Sale(), 1, [new SaleRefundLine(501, 3201, 2)], SaleFinancialPostingReversalTypes.Void, Manager, Now));

        var result = await world.Handler().Handle(new ProcessSaleFinancialPostingCommand(reversal.Id), default);

        Assert.Equal(SaleFinancialPostingOutcome.Posted, result.Outcome);
    }

    [Fact]
    public async Task A_snapshot_that_no_longer_matches_its_hash_is_never_posted()
    {
        var world = new World();
        var posting = world.Add(SaleFinancialIntentFactory.ForSale(Sale(), 1, Now));
        posting.SnapshotJson = posting.SnapshotJson.Replace("194.25", "1.00");

        var result = await world.Handler().Handle(new ProcessSaleFinancialPostingCommand(posting.Id), default);

        Assert.Equal(SaleFinancialPostingOutcome.Failed, result.Outcome);
        Assert.Equal(SaleFinancialPostingErrorCodes.SnapshotMismatch, posting.LastErrorCode);
        Assert.Equal(0, world.Poster.SalePosts);
    }

    // ── variance worker mode ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_variance_worker_skips_an_approval_another_instance_is_posting()
    {
        var approval = Approval();
        var differences = new Mock<ICashDifferenceStore>();
        differences.Setup(s => s.LockApprovalAsync(approval.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CashDifferenceApproval?)null);
        differences.Setup(s => s.FindAsync(approval.Id, It.IsAny<CancellationToken>())).ReturnsAsync(approval);
        var poster = new Mock<ICashSessionVarianceJournalPoster>(MockBehavior.Strict);

        var result = await VarianceHandler(differences, poster)
            .Handle(new PostCashDifferenceCommand(approval.Id, Reviewer, SkipIfLocked: true), default);

        Assert.True(result.Replayed);
        Assert.Equal(CashDifferencePostingStatuses.Pending, result.Approval.PostingStatus);
    }

    [Fact]
    public async Task A_variance_without_a_journal_is_failed_not_posted()
    {
        var approval = Approval();
        var differences = new Mock<ICashDifferenceStore>();
        differences.Setup(s => s.LockApprovalAsync(approval.Id, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(approval);
        differences.Setup(s => s.LockApprovalAsync(approval.Id, It.IsAny<CancellationToken>())).ReturnsAsync(approval);
        var poster = new Mock<ICashSessionVarianceJournalPoster>();
        poster.Setup(p => p.PostAsync(It.IsAny<CashSessionVarianceJournalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);

        var result = await VarianceHandler(differences, poster)
            .Handle(new PostCashDifferenceCommand(approval.Id, Reviewer, SkipIfLocked: true), default);

        Assert.Equal(CashDifferencePostingStatuses.Failed, result.Approval.PostingStatus);
        Assert.Null(result.Approval.JournalEntryId);
        Assert.Equal(1, result.Approval.PostingAttempts);
    }

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────────────────────────

    private const long Cashier = 7001;
    private const long Manager = 7002;
    private const long Reviewer = 7003;

    private static Sale Sale()
    {
        var first = new SaleItem
        {
            Id = 501, VariantId = 3201, Quantity = 2, UnitPrice = 100m, DiscountAmount = 15m, Subtotal = 185m,
            TaxableAmount = 185m, VatAmount = 9.25m, VatRate = 5m, TaxClassification = "standard", TaxInclusive = false
        };
        first.CaptureHistoricalUnitCost(70m);
        return new Sale
        {
            Id = 42,
            BusinessId = 1,
            OutletId = 5001,
            TerminalId = 8001,
            CashSessionId = 77,
            SaleNumber = "DHK2611031",
            CashierId = Cashier,
            BusinessDate = new DateTime(2026, 11, 3),
            SaleDateTime = Now.AddMinutes(-5),
            SaleDate = Now.AddMinutes(-5),
            Tax = 9.25m,
            TotalAmount = 194.25m,
            Items = [first],
            Payments = [new SalePayment { Id = 601, Method = "cash", Amount = 194.25m }]
        };
    }

    private static CashDifferenceApproval Approval() => new()
    {
        Id = 31,
        BusinessId = 1,
        OutletId = 5001,
        CashSessionId = 77,
        VarianceAmount = -20m,
        Status = CashDifferenceStatuses.Approved,
        CreatedByUserId = Manager,
        ReviewedByUserId = Reviewer,
        PostingStatus = CashDifferencePostingStatuses.Pending
    };

    private static PostCashDifferenceCommandHandler VarianceHandler(
        Mock<ICashDifferenceStore> differences,
        Mock<ICashSessionVarianceJournalPoster> poster)
    {
        var sessions = new Mock<ICashSessionStore>();
        sessions.Setup(s => s.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ICashSessionTransaction>());
        sessions.Setup(s => s.FindAsync(77, It.IsAny<CancellationToken>())).ReturnsAsync(new CashSession
        {
            Id = 77, BusinessId = 1, OutletId = 5001, TerminalId = 8001, BusinessDate = new DateTime(2026, 11, 3),
            Status = CashSessionStatuses.Closed, ClosedAt = Now
        });
        return new PostCashDifferenceCommandHandler(
            differences.Object, sessions.Object, poster.Object, Mock.Of<ICashSessionEventWriter>(),
            Mock.Of<ICashSessionAuditWriter>(), new FixedClock(Now));
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class World(long activeBusinessId = 1)
    {
        private long _nextId = 1;

        public FakePostingStore Store { get; } = new();
        public FakeJournalPoster Poster { get; } = new();
        public List<CashSessionEvent> Events { get; } = [];

        public SaleFinancialPosting Add(SaleFinancialPosting posting)
        {
            posting.Id = _nextId++;
            Store.Rows.Add(posting);
            return posting;
        }

        public ProcessSaleFinancialPostingCommandHandler Handler()
        {
            var events = new Mock<ICashSessionEventWriter>();
            events.Setup(e => e.AppendAsync(It.IsAny<CashSessionEvent>(), It.IsAny<CancellationToken>()))
                .Callback<CashSessionEvent, CancellationToken>((e, _) => Events.Add(e))
                .Returns(Task.CompletedTask);
            var tenant = new Mock<IAccountingConfigurationTenantScope>();
            tenant.Setup(t => t.GetBusinessIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(activeBusinessId);
            return new ProcessSaleFinancialPostingCommandHandler(
                Store, Poster, events.Object, tenant.Object, new SaleFinancialPostingOptions(), new FixedClock(Now));
        }
    }

    private sealed class FakeJournalPoster : ISaleFinancialJournalPoster
    {
        public Exception? Failure { get; set; }
        public int SalePosts { get; private set; }
        public int ReversalPosts { get; private set; }

        public Task<SaleJournalPostingResult> PostSaleAsync(SaleJournalSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (Failure is not null)
                throw Failure;
            SalePosts++;
            return Task.FromResult(new SaleJournalPostingResult(
                900, snapshot.Payments.ToDictionary(payment => payment.PaymentId, _ => 11L)));
        }

        public Task<long> PostReversalAsync(
            SaleReversalJournalSnapshot snapshot,
            IReadOnlyDictionary<long, long?> paymentAccounts,
            CancellationToken cancellationToken)
        {
            if (Failure is not null)
                throw Failure;
            ReversalPosts++;
            return Task.FromResult(901L);
        }
    }

    private sealed class FakePostingStore : ISaleFinancialPostingStore
    {
        public List<SaleFinancialPosting> Rows { get; } = [];
        public bool LockedElsewhere { get; set; }
        public bool Committed { get; private set; }
        public bool RolledBack { get; private set; }
        public Dictionary<long, long> SnapshottedAccounts { get; } = [];

        public Task<ISaleFinancialPostingTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ISaleFinancialPostingTransaction>(new Transaction(this));

        public Task<SaleFinancialPosting?> LockAsync(long postingId, bool skipLocked, CancellationToken cancellationToken) =>
            Task.FromResult(skipLocked && LockedElsewhere ? null : Rows.SingleOrDefault(row => row.Id == postingId));

        public Task<SaleFinancialPosting?> FindAsync(long postingId, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.SingleOrDefault(row => row.Id == postingId));

        public Task<string?> FindSalePostingStatusAsync(long saleId, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.SingleOrDefault(row => row.SaleId == saleId && row.Kind == SaleFinancialPostingKinds.Sale)?.Status);

        public Task<IReadOnlyDictionary<long, long?>> GetPaymentAccountsAsync(long saleId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<long, long?>>(
                SnapshottedAccounts.ToDictionary(entry => entry.Key, entry => (long?)entry.Value));

        public Task SnapshotPaymentAccountsAsync(long saleId, IReadOnlyDictionary<long, long> accounts, CancellationToken cancellationToken)
        {
            foreach (var (payment, account) in accounts)
                SnapshottedAccounts.TryAdd(payment, account);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SaleFinancialPostingDue>> ListDueAsync(DateTime nowUtc, int take, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SaleFinancialPosting>> ListAsync(long businessId, string? status, int take, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SaleFinancialPostingSummary> SummarizeAsync(long businessId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void DiscardPendingChanges()
        {
        }

        private sealed class Transaction(FakePostingStore store) : ISaleFinancialPostingTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken)
            {
                store.Committed = true;
                return Task.CompletedTask;
            }

            public Task RollbackAsync(CancellationToken cancellationToken)
            {
                store.RolledBack = true;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
