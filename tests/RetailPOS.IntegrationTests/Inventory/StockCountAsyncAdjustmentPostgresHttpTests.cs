using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RabbitMQ.Client;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.Integration;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Application.Settings.Abstractions;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Integration;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Integration;
using RetailPOS.Infrastructure.StockCounts;
using RetailPOS.Tests.Sales;

namespace RetailPOS.Tests.Inventory;

public sealed class StockCountAsyncAdjustmentPostgresHttpTests
    : IClassFixture<MobileSalesDashboardPostgresFixture>
{
    private const string ApproverPermissions =
        "StockCount.ViewAll,StockCount.Create,StockCount.Upload,StockCount.Approve,stock_adjustments.approve";
    private const long ApproverId = 4110;
    private const long SubmitterId = 4104;
    private const string Sessions = "/api/mobile/v1/stock-take/sessions";
    private static long _outletSeed = 7300;

    private readonly MobileSalesDashboardPostgresFixture _fixture;

    public StockCountAsyncAdjustmentPostgresHttpTests(MobileSalesDashboardPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Approve_ReturnsImmediatelyAdjustmentPending_WithDurableJobAndOutbox_AndNoStockChange()
    {
        await GrantApproverAsync();
        using var approver = ApproverClient();
        var (sessionId, outletId) = await SubmittedSessionAsync(approver, 10, 8m);
        var before = await StockSnapshotAsync();

        using var response = await approver.PostAsync($"/api/stock-counts/{sessionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await ReadAsync(response)).GetProperty("data");
        Assert.Equal("Approved", data.GetProperty("status").GetString());
        Assert.Equal("Pending", data.GetProperty("adjustmentState").GetString());
        var jobData = data.GetProperty("adjustmentJob");
        Assert.False(jobData.GetProperty("isTerminal").GetBoolean());
        Assert.Equal(JsonValueKind.Null, jobData.GetProperty("stockAdjustmentId").ValueKind);

        var job = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal(StockCountAdjustmentJobStatus.Pending, job.Status);
        Assert.Equal(ApproverId, job.ApproverUserId);
        Assert.Equal(SubmitterId, job.SubmittedByUserId);
        var outbox = Assert.Single(await OutboxAsync(job.OutboxMessageId));
        Assert.Equal("Pending", outbox.Status);
        Assert.Equal("stock-count.adjustment.requested", outbox.MessageType);
        Assert.Equal(job.Id.ToString(), outbox.AggregateId);
        Assert.True(await AuditActionExistsAsync(sessionId, "approve"));

        Assert.Equal(before, await StockSnapshotAsync());
        Assert.Empty(await AdjustmentsAsync(sessionId));
        Assert.Equal(StockCount.StatusApproved, (await SessionAsync(sessionId)).Status);

        // A repeated click returns the same durable job.
        using var repeat = await approver.PostAsync($"/api/stock-counts/{sessionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Single(await JobsAsync(sessionId));
        _ = outletId;
    }

    [Fact]
    public async Task Worker_PostsInventoryLedgerAndCompletesCountExactlyOnce_WithFullAttribution()
    {
        await GrantApproverAsync();
        using var approver = ApproverClient();
        var (sessionId, outletId) = await SubmittedSessionAsync(approver, 10, 8m);
        using var approval = await approver.PostAsync($"/api/stock-counts/{sessionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var job = Assert.Single(await JobsAsync(sessionId));

        var outcomes = await Task.WhenAll(ProcessAsync(job.Id), ProcessAsync(job.Id), ProcessAsync(job.Id));
        Assert.Single(outcomes, outcome => outcome == StockCountAdjustmentProcessingOutcome.Completed);
        Assert.All(outcomes.Where(outcome => outcome != StockCountAdjustmentProcessingOutcome.Completed),
            outcome => Assert.True(outcome is StockCountAdjustmentProcessingOutcome.ClaimedElsewhere
                or StockCountAdjustmentProcessingOutcome.AlreadyTerminal));
        Assert.Equal(StockCountAdjustmentProcessingOutcome.AlreadyTerminal, await ProcessAsync(job.Id));

        var completed = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal(StockCountAdjustmentJobStatus.Completed, completed.Status);
        Assert.Equal(1, completed.AttemptCount);
        Assert.Equal("NoConflictDetected", completed.EvidenceStatus);
        var adjustment = Assert.Single(await AdjustmentsAsync(sessionId));
        Assert.Equal(adjustment.Id, completed.StockAdjustmentId);
        Assert.Equal(StockAdjustment.StatusApproved, adjustment.Status);
        Assert.Equal(SubmitterId, adjustment.AdjustedBy);
        Assert.Equal(ApproverId, adjustment.ApprovedBy);

        var session = await SessionAsync(sessionId);
        Assert.Equal(StockCount.StatusCompleted, session.Status);
        Assert.Equal(StockCount.MovementReconciliationVerified, session.MovementReconciliationStatus);
        Assert.Equal(8, await InventoryAsync(outletId));
        var ledger = Assert.Single(await AdjustmentLedgerAsync(outletId));
        Assert.Equal(StockLedgerReferenceType.StockAdjustment, ledger.ReferenceType);
        Assert.Equal(adjustment.Id, ledger.ReferenceId);
        Assert.Equal(2, ledger.QtyOut);
        Assert.Equal(8, ledger.BalanceAfter);
        Assert.Equal(ApproverId, ledger.CreatedBy);

        Assert.True(await AuditActorActionExistsAsync(sessionId, "adjust", ApproverId));
        Assert.Equal(job.AuditCorrelationId, completed.AuditCorrelationId);

        var detail = (await ReadAsync(await approver.GetAsync($"/api/stock-counts/{sessionId}"))).GetProperty("data");
        Assert.Equal("Completed", detail.GetProperty("status").GetString());
        Assert.Equal("Completed", detail.GetProperty("adjustmentState").GetString());
        Assert.Equal(adjustment.Id, detail.GetProperty("adjustmentJob").GetProperty("stockAdjustmentId").GetInt64());

        var lifecycle = (await ReadAsync(await approver.GetAsync($"{Sessions}/{sessionId}/lifecycle")))
            .GetProperty("data").GetProperty("inventoryAdjustment");
        Assert.True(lifecycle.GetProperty("isInventoryPosted").GetBoolean());
        Assert.Equal("Completed", lifecycle.GetProperty("job").GetProperty("status").GetString());
    }

    [Fact]
    public async Task ConcurrentApprovals_CreateExactlyOneJobAndOneOutboxMessage()
    {
        await GrantApproverAsync();
        using var approver = ApproverClient();
        var (sessionId, _) = await SubmittedSessionAsync(approver, 10, 7m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => approver.PostAsync($"/api/stock-counts/{sessionId}/approve", null)));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var job = Assert.Single(await JobsAsync(sessionId));
        Assert.Single(await OutboxAsync(job.OutboxMessageId));
        foreach (var response in responses)
            response.Dispose();
    }

    [Fact]
    public async Task MakerCheckerAndCombinedAuthorizationAreEnforcedAtApproval()
    {
        await GrantApproverAsync();
        using var approver = ApproverClient();
        var (sessionId, _) = await SubmittedSessionAsync(approver, 10, 8m, submitter: ApproverId);

        using (var selfApproval = await approver.PostAsync($"/api/stock-counts/{sessionId}/approve", null))
        {
            Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);
            Assert.Contains("different", await selfApproval.Content.ReadAsStringAsync());
        }

        Assert.Empty(await JobsAsync(sessionId));
        Assert.Equal(StockCount.StatusSubmitted, (await SessionAsync(sessionId)).Status);

        var (otherSession, _) = await SubmittedSessionAsync(approver, 10, 8m);
        using var onlyCountApprove = _fixture.CreateClient(4101, ApproverId, "BusinessOwner",
            "StockCount.ViewAll,StockCount.Approve");
        using var onlyPost = _fixture.CreateClient(4101, ApproverId, "BusinessOwner",
            "StockCount.ViewAll,stock_adjustments.approve");
        using var roleSwitched = _fixture.CreateClient(
            4101, ApproverId, "BusinessOwner", ApproverPermissions, true, "SalesManager");
        using var salesperson = _fixture.CreateClient(4101, 4102, "SalesPerson", "StockCount.ViewAll");
        foreach (var client in new[] { onlyCountApprove, onlyPost, roleSwitched, salesperson })
        {
            using var denied = await client.PostAsync($"/api/stock-counts/{otherSession}/approve", null);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        Assert.Empty(await JobsAsync(otherSession));
        Assert.Equal(StockCount.StatusSubmitted, (await SessionAsync(otherSession)).Status);
    }

    [Fact]
    public async Task AmbiguousMovementEvidence_BlocksTheJobWithoutChangingInventory()
    {
        await GrantApproverAsync();
        using var approver = ApproverClient();
        var (sessionId, outletId) = await SubmittedSessionAsync(approver, 10, 9m, movementDuringWindow: true);
        using var approval = await approver.PostAsync($"/api/stock-counts/{sessionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var job = Assert.Single(await JobsAsync(sessionId));
        var before = await StockSnapshotAsync();

        Assert.Equal(StockCountAdjustmentProcessingOutcome.Blocked, await ProcessAsync(job.Id));
        var blocked = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal(StockCountAdjustmentJobStatus.Blocked, blocked.Status);
        Assert.Equal("Unresolved", blocked.EvidenceStatus);
        Assert.Contains("MOVEMENT_DURING_COUNT_WINDOW", blocked.BlockedReasonCodes!);
        Assert.Equal(before, await StockSnapshotAsync());
        Assert.Empty(await AdjustmentsAsync(sessionId));
        Assert.Equal(StockCount.StatusApproved, (await SessionAsync(sessionId)).Status);
        Assert.Equal(StockCountAdjustmentProcessingOutcome.AlreadyTerminal, await ProcessAsync(job.Id));

        var detail = (await ReadAsync(await approver.GetAsync($"/api/stock-counts/{sessionId}"))).GetProperty("data");
        Assert.Equal("Blocked", detail.GetProperty("adjustmentState").GetString());
        Assert.Contains("MOVEMENT_DURING_COUNT_WINDOW", detail.GetProperty("adjustmentJob")
            .GetProperty("blockedReasonCodes").EnumerateArray().Select(code => code.GetString()));
        _ = outletId;
    }

    [Fact]
    public async Task NegativeStockFailsPermanently_AndRollsBackTheDraft()
    {
        await GrantApproverAsync();
        using var approver = ApproverClient();
        var (sessionId, outletId) = await SubmittedSessionAsync(approver, 10, 0m);
        await Task.Delay(30);
        // A sale after the count leaves too little stock for the counted shortage (existing negative-stock rule).
        await AddLedgerMovementAsync(outletId, 2, StockLedgerTransactionType.Sale, 0, 5, 5, DateTime.UtcNow);
        using var approval = await approver.PostAsync($"/api/stock-counts/{sessionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var job = Assert.Single(await JobsAsync(sessionId));
        var before = await StockSnapshotAsync();

        Assert.Equal(StockCountAdjustmentProcessingOutcome.Failed, await ProcessAsync(job.Id));
        var failed = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal(StockCountAdjustmentJobStatus.Failed, failed.Status);
        Assert.Equal("ADJUSTMENT_VALIDATION_FAILED", failed.FailureCode);
        Assert.Equal(before, await StockSnapshotAsync());
        Assert.Empty(await AdjustmentsAsync(sessionId));
        Assert.Equal(StockCount.StatusApproved, (await SessionAsync(sessionId)).Status);
    }

    [Fact]
    public async Task RevokedApproverPermissionStopsPostingBeforeAnyStockChange()
    {
        await GrantApproverAsync();
        using var approver = ApproverClient();
        var (sessionId, _) = await SubmittedSessionAsync(approver, 10, 8m);
        using var approval = await approver.PostAsync($"/api/stock-counts/{sessionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var job = Assert.Single(await JobsAsync(sessionId));
        var before = await StockSnapshotAsync();

        await _fixture.SetRolePermissionsAsync(ApproverId, "BusinessOwner", "StockCount.Approve", "StockCount.ViewAll");
        try
        {
            Assert.Equal(StockCountAdjustmentProcessingOutcome.Failed, await ProcessAsync(job.Id));
        }
        finally
        {
            await GrantApproverAsync();
        }

        var failed = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal("APPROVER_NOT_AUTHORIZED", failed.FailureCode);
        Assert.Equal(before, await StockSnapshotAsync());
        Assert.Empty(await AdjustmentsAsync(sessionId));
    }

    [Fact]
    public async Task TransientFailureRollsBackThenRecoversWithoutDuplicatePosting()
    {
        await GrantApproverAsync();
        using var approver = ApproverClient();
        var (sessionId, outletId) = await SubmittedSessionAsync(approver, 10, 8m);
        using var approval = await approver.PostAsync($"/api/stock-counts/{sessionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var job = Assert.Single(await JobsAsync(sessionId));
        var before = await StockSnapshotAsync();

        var failingStore = new Mock<IStockAdjustmentApprovalStore>();
        failingStore.Setup(store => store.ApproveAsync(
                It.IsAny<long>(), It.IsAny<long>(), It.IsAny<StockAdjustmentCreateContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("database connection lost"));
        Assert.Equal(StockCountAdjustmentProcessingOutcome.RetryScheduled,
            await ProcessWithAsync(job.Id, failingStore.Object, new StockCountAdjustmentOptions()));
        var retry = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal(StockCountAdjustmentJobStatus.RetryScheduled, retry.Status);
        Assert.Equal("TRANSIENT_FAILURE", retry.FailureCode);
        Assert.Equal(before, await StockSnapshotAsync());
        Assert.Empty(await AdjustmentsAsync(sessionId));
        Assert.Equal(StockCountAdjustmentProcessingOutcome.NotDue, await ProcessAsync(job.Id));

        await UpdateJobAsync(job.Id, setters => setters.SetProperty(row => row.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        var recoverable = await RecoverableAsync();
        Assert.True(recoverable.Contains(job.Id), JsonSerializer.Serialize(await JobsAsync(sessionId)));
        Assert.Equal(StockCountAdjustmentProcessingOutcome.Completed, await ProcessAsync(job.Id));
        Assert.Single(await AdjustmentsAsync(sessionId));
        Assert.Equal(8, await InventoryAsync(outletId));
        Assert.Single(await AdjustmentLedgerAsync(outletId));
    }

    [Fact]
    public async Task CrashedWorkerLeaseIsRecoveredAndRetryAttemptsAreBounded()
    {
        await GrantApproverAsync();
        using var approver = ApproverClient();
        var (sessionId, outletId) = await SubmittedSessionAsync(approver, 10, 8m);
        using var approval = await approver.PostAsync($"/api/stock-counts/{sessionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var job = Assert.Single(await JobsAsync(sessionId));

        await UpdateJobAsync(job.Id, setters => setters
            .SetProperty(row => row.Status, StockCountAdjustmentJobStatus.Processing)
            .SetProperty(row => row.LeaseOwner, Guid.NewGuid())
            .SetProperty(row => row.LeaseExpiresAtUtc, DateTime.UtcNow.AddMinutes(5))
            .SetProperty(row => row.AttemptCount, 1));
        Assert.Equal(StockCountAdjustmentProcessingOutcome.ClaimedElsewhere, await ProcessAsync(job.Id));
        Assert.DoesNotContain(job.Id, await RecoverableAsync());

        await UpdateJobAsync(job.Id, setters => setters
            .SetProperty(row => row.LeaseExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        Assert.Contains(job.Id, await RecoverableAsync());
        Assert.Equal(StockCountAdjustmentProcessingOutcome.Completed, await ProcessAsync(job.Id));
        Assert.Equal(8, await InventoryAsync(outletId));

        var (exhaustedSession, exhaustedOutlet) = await SubmittedSessionAsync(approver, 10, 8m);
        using var secondApproval = await approver.PostAsync($"/api/stock-counts/{exhaustedSession}/approve", null);
        var exhausted = Assert.Single(await JobsAsync(exhaustedSession));
        await UpdateJobAsync(exhausted.Id, setters => setters
            .SetProperty(row => row.Status, StockCountAdjustmentJobStatus.RetryScheduled)
            .SetProperty(row => row.AttemptCount, 5)
            .SetProperty(row => row.NextAttemptAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        Assert.Equal(StockCountAdjustmentProcessingOutcome.Failed, await ProcessAsync(exhausted.Id));
        Assert.Equal("RETRY_ATTEMPTS_EXHAUSTED", (await JobsAsync(exhaustedSession)).Single().FailureCode);
        Assert.Equal(10, await InventoryAsync(exhaustedOutlet));
    }

    [Fact]
    public async Task CountWithoutDifferencesCompletesWithoutCreatingAnAdjustment()
    {
        await GrantApproverAsync();
        using var approver = ApproverClient();
        var (sessionId, outletId) = await SubmittedSessionAsync(approver, 10, 10m);
        using var approval = await approver.PostAsync($"/api/stock-counts/{sessionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        var job = Assert.Single(await JobsAsync(sessionId));

        Assert.Equal(StockCountAdjustmentProcessingOutcome.Completed, await ProcessAsync(job.Id));
        var completed = Assert.Single(await JobsAsync(sessionId));
        Assert.Null(completed.StockAdjustmentId);
        Assert.Empty(await AdjustmentsAsync(sessionId));
        Assert.Equal(StockCount.StatusCompleted, (await SessionAsync(sessionId)).Status);
        Assert.Equal(10, await InventoryAsync(outletId));
        Assert.Empty(await AdjustmentLedgerAsync(outletId));
    }

    [Fact]
    public async Task ManualDraftGenerationIsDisabledWhileAsyncAdjustmentIsOn_AndPosReadsAreNotBlocked()
    {
        await GrantApproverAsync();
        using var approver = ApproverClient();
        var (sessionId, outletId) = await SubmittedSessionAsync(approver, 10, 8m);
        using var approval = await approver.PostAsync($"/api/stock-counts/{sessionId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        using var draft = await approver.PostAsync($"/api/stock-counts/{sessionId}/generate-adjustment-draft", null);
        Assert.Equal(HttpStatusCode.Forbidden, draft.StatusCode);
        Assert.Empty(await AdjustmentsAsync(sessionId));

        await using var lockScope = _fixture.CreateServiceScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await using var transaction = await lockDb.Database.BeginTransactionAsync();
        await lockDb.StockCounts
            .FromSqlInterpolated($"SELECT * FROM stock_counts WHERE id = {sessionId} FOR UPDATE")
            .SingleAsync();
        var read = approver.GetAsync($"/api/stock-counts/{sessionId}");
        var posMovement = AddLedgerMovementAsync(outletId, 2, StockLedgerTransactionType.Sale, 0, 1, 9, DateTime.UtcNow);
        var both = Task.WhenAll(read, posMovement);
        Assert.Same(both, await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(15))));
        await both;
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task DeliveryHandlerAcknowledgesOnlyAfterProcessing_AndNeverRequeues()
    {
        var jobId = Guid.NewGuid();
        var processor = new Mock<IStockCountAdjustmentProcessor>();
        processor.Setup(candidate => candidate.ProcessAsync(jobId, 4101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StockCountAdjustmentProcessingOutcome.Completed);
        var handler = new StockCountAdjustmentDelivery(
            processor.Object, NullLogger<StockCountAdjustmentDelivery>.Instance);

        var channel = new Mock<IChannel>();
        await handler.HandleAsync(channel.Object, 1, Serialize(Envelope(jobId)), default);
        channel.Verify(candidate => candidate.BasicAckAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);

        var poison = new Mock<IChannel>();
        await handler.HandleAsync(poison.Object, 2,
            Serialize(Envelope(jobId) with { MessageType = "unexpected.message" }), default);
        await handler.HandleAsync(poison.Object, 3, "not-json"u8.ToArray(), default);
        poison.Verify(candidate => candidate.BasicNackAsync(
            It.IsAny<ulong>(), false, false, It.IsAny<CancellationToken>()), Times.Exactly(2));
        poison.Verify(candidate => candidate.BasicAckAsync(
            It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

        var failing = new Mock<IStockCountAdjustmentProcessor>();
        failing.Setup(candidate => candidate.ProcessAsync(
                It.IsAny<Guid>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("database unavailable"));
        var failingChannel = new Mock<IChannel>();
        await new StockCountAdjustmentDelivery(
                failing.Object, NullLogger<StockCountAdjustmentDelivery>.Instance)
            .HandleAsync(failingChannel.Object, 4, Serialize(Envelope(jobId)), default);
        failingChannel.Verify(candidate => candidate.BasicNackAsync(
            4, false, false, It.IsAny<CancellationToken>()), Times.Once);
        failingChannel.Verify(candidate => candidate.BasicAckAsync(
            It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private HttpClient ApproverClient() => _fixture.CreateClient(4101, ApproverId, "BusinessOwner", ApproverPermissions);

    private Task GrantApproverAsync() => _fixture.AddUserWithRoleAsync(
        ApproverId, "BusinessOwner", "StockCount.Approve", "stock_adjustments.approve", "StockCount.ViewAll");

    private async Task<(long SessionId, long OutletId)> SubmittedSessionAsync(
        HttpClient client,
        int baselineStock,
        decimal counted,
        bool movementDuringWindow = false,
        long submitter = SubmitterId)
    {
        var outletId = await SeedOutletAsync(baselineStock);
        var sessionId = await CreateSessionAsync(client, outletId);
        if (movementDuringWindow)
        {
            await Task.Delay(30);
            await AddLedgerMovementAsync(outletId, 2, StockLedgerTransactionType.Sale, 0, 1, baselineStock - 1, DateTime.UtcNow);
            await Task.Delay(30);
        }

        await ApplyCountAsync(client, sessionId, counted);
        await using (var scope = _fixture.CreateServiceScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.StockCountLines
                .Where(line => line.StockCountId == sessionId && line.PhysicalStock == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(line => line.PhysicalStock, line => line.CurrentStock)
                    .SetProperty(line => line.Difference, 0m));
            var session = await db.StockCounts.AsNoTracking().SingleAsync(count => count.Id == sessionId);
            await scope.ServiceProvider.GetRequiredService<IStockCountLifecycleStore>().SubmitAsync(
                session.Id, session.BusinessId, session.LocationId, session.LocationType, submitter, default);
        }

        await Task.Delay(30);
        return (sessionId, outletId);
    }

    private async Task<long> SeedOutletAsync(int stock)
    {
        var outletId = Interlocked.Increment(ref _outletSeed);
        await _fixture.AddOutletAsync(outletId);
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.Inventories.Add(new RetailPOS.Core.Entities.Inventory
        {
            VariantId = 9103,
            LocationId = outletId,
            LocationType = "outlet",
            Quantity = stock,
            LowStockThreshold = 5
        });
        db.StockLedgers.Add(NewLedger(
            outletId, 1, StockLedgerTransactionType.Grn, stock, 0, stock, DateTime.UtcNow.AddHours(-2)));
        await db.SaveChangesAsync();
        return outletId;
    }

    private static StockLedger NewLedger(
        long outletId,
        long sequence,
        string type,
        int qtyIn,
        int qtyOut,
        int balance,
        DateTime createdAt) =>
        new()
        {
            VariantId = 9103,
            LocationId = outletId,
            LocationType = "outlet",
            TransactionType = type,
            QtyIn = qtyIn,
            QtyOut = qtyOut,
            BalanceAfter = balance,
            UnitCost = 0m,
            MovementValue = 0m,
            IsCostValuationReliable = true,
            BusinessDate = createdAt.Date,
            MovementSequence = sequence,
            ReferenceType = type == StockLedgerTransactionType.Sale
                ? StockLedgerReferenceType.Sale
                : StockLedgerReferenceType.Grn,
            ReferenceId = 900 + sequence,
            CreatedAt = createdAt
        };

    private async Task AddLedgerMovementAsync(
        long outletId,
        long sequence,
        string type,
        int qtyIn,
        int qtyOut,
        int balance,
        DateTime createdAt)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.StockLedgers.Add(NewLedger(outletId, sequence, type, qtyIn, qtyOut, balance, createdAt));
        var inventory = await db.Inventories.SingleAsync(row =>
            row.VariantId == 9103 && row.LocationId == outletId && row.LocationType == "outlet");
        inventory.Quantity = balance;
        await db.SaveChangesAsync();
    }

    private static async Task<long> CreateSessionAsync(HttpClient client, long outletId)
    {
        using var response = await client.PostAsJsonAsync(Sessions, new
        {
            outletId,
            stockCountDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            remarks = "Async adjustment test"
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await ReadAsync(response)).GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task ApplyCountAsync(HttpClient client, long sessionId, decimal quantity)
    {
        using var response = await client.PostAsJsonAsync($"{Sessions}/{sessionId}/operations:batch", new
        {
            deviceSessionId = "async-adjustment-device",
            expectedRevision = 0L,
            operations = new[]
            {
                new
                {
                    clientOperationId = Guid.NewGuid(),
                    clientSequence = 1L,
                    productVariantId = 9103L,
                    countedQuantity = quantity,
                    rawBarcode = (string?)null,
                    clientObservedAt = (DateTimeOffset?)null
                }
            }
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private async Task<StockCountAdjustmentJob[]> JobsAsync(long sessionId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return await db.StockCountAdjustmentJobs.AsNoTracking()
            .Where(job => job.StockCountId == sessionId).ToArrayAsync();
    }

    private async Task<IntegrationOutboxMessage[]> OutboxAsync(Guid messageId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return await db.Set<IntegrationOutboxMessage>().AsNoTracking()
            .Where(row => row.Id == messageId).ToArrayAsync();
    }

    private async Task<StockAdjustment[]> AdjustmentsAsync(long sessionId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return await db.StockAdjustments.AsNoTracking()
            .Where(adjustment => adjustment.SourceStockCountId == sessionId).ToArrayAsync();
    }

    private async Task<StockLedger[]> AdjustmentLedgerAsync(long outletId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return await db.StockLedgers.AsNoTracking()
            .Where(row => row.LocationId == outletId
                          && row.TransactionType == StockLedgerTransactionType.Adjustment)
            .ToArrayAsync();
    }

    private async Task<int> InventoryAsync(long outletId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return await db.Inventories.AsNoTracking()
            .Where(row => row.VariantId == 9103 && row.LocationId == outletId && row.LocationType == "outlet")
            .Select(row => row.Quantity)
            .SingleAsync();
    }

    private async Task<StockCount> SessionAsync(long sessionId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return await db.StockCounts.AsNoTracking().SingleAsync(count => count.Id == sessionId);
    }

    private async Task<string> StockSnapshotAsync()
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var inventories = await db.Inventories.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => row.Id + ":" + row.Quantity).ToListAsync();
        var ledgerCount = await db.StockLedgers.CountAsync();
        var adjustmentCount = await db.StockAdjustments.CountAsync();
        return string.Join('|', inventories) + $"#ledger={ledgerCount}#adjustments={adjustmentCount}";
    }

    private async Task UpdateJobAsync(
        Guid jobId,
        System.Linq.Expressions.Expression<Func<
            Microsoft.EntityFrameworkCore.Query.SetPropertyCalls<StockCountAdjustmentJob>,
            Microsoft.EntityFrameworkCore.Query.SetPropertyCalls<StockCountAdjustmentJob>>> configure)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.StockCountAdjustmentJobs.Where(job => job.Id == jobId).ExecuteUpdateAsync(configure);
    }

    private async Task<IReadOnlyList<Guid>> RecoverableAsync()
    {
        await using var scope = _fixture.CreateServiceScope();
        return await scope.ServiceProvider.GetRequiredService<IStockCountAdjustmentProcessor>()
            .FindRecoverableJobIdsAsync(default);
    }

    private async Task<StockCountAdjustmentProcessingOutcome> ProcessAsync(Guid jobId)
    {
        await using var scope = _fixture.CreateServiceScope();
        return await scope.ServiceProvider.GetRequiredService<IStockCountAdjustmentProcessor>()
            .ProcessAsync(jobId, null, default);
    }

    private async Task<StockCountAdjustmentProcessingOutcome> ProcessWithAsync(
        Guid jobId,
        IStockAdjustmentApprovalStore approvalStore,
        StockCountAdjustmentOptions options)
    {
        await using var scope = _fixture.CreateServiceScope();
        var provider = scope.ServiceProvider;
        var processor = new StockCountAdjustmentProcessor(
            provider.GetRequiredService<RetailPOSDbContext>(),
            provider.GetRequiredService<IAuditContext>(),
            provider.GetRequiredService<IRequiredAuditTransaction>(),
            provider.GetRequiredService<IStockCountAdjustmentApproverPolicy>(),
            provider.GetRequiredService<StockCountMovementEvidenceService>(),
            provider.GetRequiredService<StockCountAdjustmentDraftBuilder>(),
            approvalStore,
            provider.GetRequiredService<ISystemSettingsStore>(),
            options,
            TimeProvider.System,
            NullLogger<StockCountAdjustmentProcessor>.Instance);
        return await processor.ProcessAsync(jobId, null, default);
    }

    private async Task<bool> AuditActionExistsAsync(long sessionId, string actionType)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var id = sessionId.ToString();
        return await db.AuditEvents.AnyAsync(entry =>
            entry.ActionType == actionType && entry.PrimaryEntityId == id);
    }

    private async Task<bool> AuditActorActionExistsAsync(long sessionId, string actionType, long actorUserId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var id = sessionId.ToString();
        return await db.AuditEvents.AnyAsync(entry =>
            entry.ActionType == actionType
            && entry.PrimaryEntityId == id
            && entry.RealUserId == actorUserId
            && entry.Source == "Integration");
    }

    private static IntegrationEnvelope Envelope(Guid jobId)
    {
        var payload = StockCountAdjustmentMessage.Serialize(
            new StockCountAdjustmentMessage.Payload(4101, jobId, 1, 1));
        return new IntegrationEnvelope(
            Guid.NewGuid(), 4101, "RetailPOS", "stock-count.adjustment.requested", 1,
            "StockCountAdjustmentJob", jobId.ToString(), Guid.NewGuid(), null, DateTime.UtcNow,
            payload, IntegrationEnvelope.Hash(payload));
    }

    private static ReadOnlyMemory<byte> Serialize(IntegrationEnvelope envelope) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope);
}
