using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RabbitMQ.Client;
using RetailPOS.Application.AuditEvents.Abstractions;
using RetailPOS.Application.Integration;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Application.Inventory.StockCounts.Commands;
using RetailPOS.Core.Audit;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Integration;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Integration;
using RetailPOS.Infrastructure.StockCounts;
using RetailPOS.Tests.Sales;

namespace RetailPOS.Tests.Inventory;

public sealed class MobileStockTakeSubmissionPostgresHttpTests
    : IClassFixture<MobileSalesDashboardPostgresFixture>
{
    private const string OwnerPermissions =
        "StockCount.ViewAll,StockCount.Create,StockCount.Upload,StockCount.Submit";
    private static long _outletSeed = 5300;
    private static long _keySeed;

    private readonly MobileSalesDashboardPostgresFixture _fixture;

    public MobileStockTakeSubmissionPostgresHttpTests(MobileSalesDashboardPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OwnerSubmission_Returns202OnlyAfterDurableJobAndOutboxCommit_AndLocksCountWrites()
    {
        using var owner = OwnerClient();
        var (sessionId, _, revision) = await NewCountedSessionAsync(owner);

        using var response = await SubmitAsync(owner, sessionId, revision);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await ReadAsync(response);
        var data = body.GetProperty("data");
        var jobId = data.GetProperty("jobId").GetGuid();
        Assert.Equal("Accepted", data.GetProperty("status").GetString());
        Assert.Equal(revision, data.GetProperty("acceptedRevision").GetInt64());
        Assert.False(data.GetProperty("isTerminal").GetBoolean());
        Assert.False(data.GetProperty("isReplay").GetBoolean());
        var statusUrl = data.GetProperty("statusUrl").GetString()!;
        Assert.EndsWith($"/stock-take/sessions/{sessionId}/submissions/{jobId}", statusUrl);
        Assert.EndsWith(statusUrl, response.Headers.Location!.ToString());

        var job = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal(StockCountSubmissionJobStatus.Accepted, job.Status);
        Assert.Equal(4110, job.ActorUserId);
        Assert.Equal("BusinessOwner", job.ActorRoleName);
        Assert.Equal(64, job.RequestFingerprint.Length);
        var outbox = Assert.Single(await OutboxAsync(job.OutboxMessageId));
        Assert.Equal("Pending", outbox.Status);
        Assert.Equal("mobile.stock-take.submission.requested", outbox.MessageType);
        Assert.Equal(job.Id.ToString(), outbox.AggregateId);
        Assert.Equal(job.BusinessId, outbox.BusinessId);

        // HTTP 202 is durable acceptance only: the ERP StockCount has not been submitted.
        Assert.Equal(StockCount.StatusDraft, (await SessionAsync(sessionId)).Status);
        Assert.True(await AuditActionExistsAsync(sessionId, "submit"));

        using var lockedBatch = await owner.PostAsJsonAsync(
            $"/api/mobile/v1/stock-take/sessions/{sessionId}/operations:batch",
            CountBatch(revision, 2, 9104, 1m));
        Assert.Equal(HttpStatusCode.Conflict, lockedBatch.StatusCode);
        Assert.Equal("STOCK_TAKE_SUBMISSION_IN_PROGRESS",
            (await ReadAsync(lockedBatch)).GetProperty("message").GetString());

        using var status = await owner.GetAsync(statusUrl);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var statusText = await status.Content.ReadAsStringAsync();
        foreach (var forbidden in new[] { "cost", "valuation", "currentStock", "expected", "variance", "difference" })
            Assert.DoesNotContain(forbidden, statusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SalesManagerSubmitsWithinAssignedOutlet_AndOtherRolesAndScopesAreDenied()
    {
        using var manager = _fixture.CreateClient(4101, 4104, "SalesManager",
            "StockCount.ViewOwn,StockCount.Create,StockCount.Upload,StockCount.Submit");
        var sessionId = await CreateSessionAsync(manager, 5102);
        var revision = await ApplyCountAsync(manager, sessionId, 0, 1, 9103, 2m);
        using var accepted = await SubmitAsync(manager, sessionId, revision);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var job = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal("SalesManager", job.ActorRoleName);
        Assert.Equal(5102, job.OutletId);

        using var owner = OwnerClient();
        var (ownerSession, _, ownerRevision) = await NewCountedSessionAsync(owner);
        using var outsideOutlet = await SubmitAsync(manager, ownerSession, ownerRevision);
        Assert.Equal(HttpStatusCode.NotFound, outsideOutlet.StatusCode);
        Assert.Empty(await JobsAsync(ownerSession));

        foreach (var (role, userId) in new[] { ("SalesPerson", 4102L), ("Cashier", 4105L) })
        {
            using var denied = _fixture.CreateClient(4101, userId, role, OwnerPermissions);
            using var response = await SubmitAsync(denied, ownerSession, ownerRevision);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        using var noPermission = _fixture.CreateClient(4101, 4110, "BusinessOwner",
            "StockCount.ViewAll,StockCount.Create,StockCount.Upload");
        using (var response = await SubmitAsync(noPermission, ownerSession, ownerRevision))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var roleSwitched = _fixture.CreateClient(
            4101, 4110, "BusinessOwner", OwnerPermissions, true, "SalesManager");
        using (var response = await SubmitAsync(roleSwitched, ownerSession, ownerRevision))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var foreignBusiness = _fixture.CreateClient(4202, 4110, "BusinessOwner", OwnerPermissions);
        using (var response = await SubmitAsync(foreignBusiness, ownerSession, ownerRevision))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // The shared test host authenticates every request with a default identity, so an
        // "anonymous" client is a user without a supported Mobile role and must be denied.
        using var anonymous = _fixture.CreateUnauthenticatedClient();
        using (var response = await SubmitAsync(anonymous, ownerSession, ownerRevision))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var approvalRoute = await owner.PostAsync(
            $"/api/mobile/v1/stock-take/sessions/{ownerSession}/approve", null);
        Assert.Equal(HttpStatusCode.NotFound, approvalRoute.StatusCode);
        Assert.Empty(await JobsAsync(ownerSession));
    }

    [Fact]
    public async Task IdempotencyKeyAndRequestShapeAreValidated_AndReplayIsStable()
    {
        using var owner = OwnerClient();
        var (sessionId, _, revision) = await NewCountedSessionAsync(owner);

        using (var missing = await SendSubmitAsync(owner, sessionId, null, new { acceptedRevision = revision }))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        foreach (var invalid in new[] { "short", "has spaces in the key", "bad/slash/character", new string('a', 101) })
        {
            using var response = await SendSubmitAsync(owner, sessionId, invalid, new { acceptedRevision = revision });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var zeroRevision = await SendSubmitAsync(owner, sessionId, NewKey(), new { acceptedRevision = 0 }))
            Assert.Equal(HttpStatusCode.BadRequest, zeroRevision.StatusCode);
        Assert.Empty(await JobsAsync(sessionId));

        var key = NewKey();
        using var first = await SendSubmitAsync(owner, sessionId, key, new { acceptedRevision = revision });
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        var firstJob = (await ReadAsync(first)).GetProperty("data").GetProperty("jobId").GetGuid();

        // A client that timed out can safely retry the identical request and recover the same job.
        using var replay = await SendSubmitAsync(owner, sessionId, key, new { acceptedRevision = revision });
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        var replayData = (await ReadAsync(replay)).GetProperty("data");
        Assert.Equal(firstJob, replayData.GetProperty("jobId").GetGuid());
        Assert.True(replayData.GetProperty("isReplay").GetBoolean());
        Assert.Single(await JobsAsync(sessionId));
        Assert.Single(await OutboxByAggregateAsync(firstJob));

        using var mismatch = await SendSubmitAsync(owner, sessionId, key, new { acceptedRevision = revision + 1 });
        Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_PAYLOAD_MISMATCH",
            (await ReadAsync(mismatch)).GetProperty("message").GetString());

        using var differentKey = await SendSubmitAsync(owner, sessionId, NewKey(), new { acceptedRevision = revision });
        Assert.Equal(HttpStatusCode.Conflict, differentKey.StatusCode);
        var conflict = await ReadAsync(differentKey);
        Assert.Equal("STOCK_TAKE_SUBMISSION_IN_PROGRESS", conflict.GetProperty("message").GetString());
        Assert.Equal(firstJob, conflict.GetProperty("data").GetProperty("jobId").GetGuid());
        Assert.Single(await JobsAsync(sessionId));
    }

    [Fact]
    public async Task ConcurrentSubmissions_CreateExactlyOneActiveJobAndOutboxMessage()
    {
        using var owner = OwnerClient();
        var (sessionId, _, revision) = await NewCountedSessionAsync(owner);

        var competing = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            SendSubmitAsync(owner, sessionId, NewKey(), new { acceptedRevision = revision })));
        try
        {
            Assert.Single(competing, response => response.StatusCode == HttpStatusCode.Accepted);
            Assert.Equal(7, competing.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var response in competing)
                response.Dispose();
        }
        var job = Assert.Single(await JobsAsync(sessionId));
        Assert.Single(await OutboxByAggregateAsync(job.Id));

        // The same key raced from many retries converges on one durable job.
        var (otherSession, _, otherRevision) = await NewCountedSessionAsync(owner);
        var sharedKey = NewKey();
        var sameKey = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            SendSubmitAsync(owner, otherSession, sharedKey, new { acceptedRevision = otherRevision })));
        try
        {
            Assert.All(sameKey, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
            var ids = new HashSet<Guid>();
            foreach (var response in sameKey)
                ids.Add((await ReadAsync(response)).GetProperty("data").GetProperty("jobId").GetGuid());
            Assert.Single(ids);
        }
        finally
        {
            foreach (var response in sameKey)
                response.Dispose();
        }
        Assert.Single(await JobsAsync(otherSession));
    }

    [Fact]
    public async Task StaleRevisionNoCountsAndInvalidLifecycleAreRejectedWithoutDurableJobs()
    {
        using var owner = OwnerClient();
        var (staleSession, _, revision) = await NewCountedSessionAsync(owner);
        await ApplyCountAsync(owner, staleSession, revision, 2, 9104, 4m);
        using (var stale = await SubmitAsync(owner, staleSession, revision))
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("STALE_SESSION_REVISION", (await ReadAsync(stale)).GetProperty("message").GetString());
        }
        Assert.Empty(await JobsAsync(staleSession));

        var emptyOutlet = await NewOutletAsync();
        var emptySession = await CreateSessionAsync(owner, emptyOutlet);
        using (var none = await SubmitAsync(owner, emptySession, 1))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, none.StatusCode);
            Assert.Equal("NO_ACCEPTED_COUNTS", (await ReadAsync(none)).GetProperty("message").GetString());
        }

        var (approvedSession, _, approvedRevision) = await NewCountedSessionAsync(owner);
        await SetSessionStatusAsync(approvedSession, StockCount.StatusApproved);
        using (var notDraft = await SubmitAsync(owner, approvedSession, approvedRevision))
        {
            Assert.Equal(HttpStatusCode.Conflict, notDraft.StatusCode);
            Assert.Equal("STOCK_TAKE_NOT_DRAFT", (await ReadAsync(notDraft)).GetProperty("message").GetString());
        }
        Assert.Empty(await JobsAsync(emptySession));
        Assert.Empty(await JobsAsync(approvedSession));
    }

    [Fact]
    public async Task TransactionFailureBeforeResponse_LeavesNoJobOrOutboxAndAllowsCleanRetry()
    {
        using var owner = OwnerClient();
        var (sessionId, outletId, revision) = await NewCountedSessionAsync(owner);
        var key = NewKey();

        await using (var scope = _fixture.CreateServiceScope())
        {
            var provider = scope.ServiceProvider;
            var audit = provider.GetRequiredService<IAuditContext>();
            audit.Initialize(AuditContext.ForBusinessSystemJob("submission-failure-test", 4101));
            var failingOutbox = new Mock<IIntegrationOutbox>();
            failingOutbox.Setup(candidate => candidate.AppendAsync(
                    It.IsAny<IntegrationEnvelope>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("injected outbox failure"));
            var store = new MobileStockTakeSubmissionStore(
                provider.GetRequiredService<RetailPOSDbContext>(),
                provider.GetRequiredService<IRequiredAuditTransaction>(),
                failingOutbox.Object,
                audit,
                TimeProvider.System);
            await Assert.ThrowsAsync<IOException>(() => store.AcceptAsync(
                new AcceptMobileStockTakeSubmissionInput(
                    sessionId, 4101, outletId, 4110, "BusinessOwner", key, revision, "trace"),
                default));
        }

        Assert.Empty(await JobsAsync(sessionId));
        Assert.Equal(StockCount.StatusDraft, (await SessionAsync(sessionId)).Status);
        using var retry = await SendSubmitAsync(owner, sessionId, key, new { acceptedRevision = revision });
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Single(await JobsAsync(sessionId));
    }

    [Fact]
    public async Task OutboxPublicationRecoversAfterBrokerOutage_WithoutChangingJobState()
    {
        using var owner = OwnerClient();
        var (sessionId, _, revision) = await NewCountedSessionAsync(owner);
        using var response = await SubmitAsync(owner, sessionId, revision);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var job = Assert.Single(await JobsAsync(sessionId));

        var attempts = new Dictionary<Guid, int>();
        var published = new List<IntegrationEnvelope>();
        var publisher = new Mock<IConfirmedIntegrationPublisher>();
        publisher.Setup(candidate => candidate.PublishAsync(
                It.IsAny<IntegrationEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns<IntegrationEnvelope, CancellationToken>((envelope, _) =>
            {
                attempts[envelope.MessageId] = attempts.GetValueOrDefault(envelope.MessageId) + 1;
                if (attempts[envelope.MessageId] == 1)
                    throw new IOException("broker unavailable");
                published.Add(envelope);
                return Task.CompletedTask;
            });

        await using (var scope = _fixture.CreateServiceScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await DeferOtherOutboxMessagesAsync(db, job.OutboxMessageId);
            var dispatcher = new OutboxDispatcher(db, publisher.Object, new DurableDeliveryOptions(),
                TimeProvider.System, NullLogger<OutboxDispatcher>.Instance);
            await dispatcher.DispatchBatchAsync(default);
        }
        var afterOutage = Assert.Single(await OutboxAsync(job.OutboxMessageId));
        Assert.Equal("Pending", afterOutage.Status);
        Assert.Equal(1, afterOutage.AttemptCount);
        Assert.Equal(StockCountSubmissionJobStatus.Accepted, (await JobsAsync(sessionId)).Single().Status);

        await using (var scope = _fixture.CreateServiceScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.Set<IntegrationOutboxMessage>().Where(row => row.Id == job.OutboxMessageId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(row => row.NextAttemptAtUtc, ClientUtcNow().AddSeconds(-1)));
            await DeferOtherOutboxMessagesAsync(db, job.OutboxMessageId);
            var dispatcher = new OutboxDispatcher(db, publisher.Object, new DurableDeliveryOptions(),
                TimeProvider.System, NullLogger<OutboxDispatcher>.Instance);
            await dispatcher.DispatchBatchAsync(default);
        }

        var recovered = Assert.Single(await OutboxAsync(job.OutboxMessageId));
        Assert.Equal("Published", recovered.Status);
        var envelope = Assert.Single(published, candidate => candidate.MessageId == job.OutboxMessageId);
        Assert.Equal(job.Id, MobileStockTakeSubmissionMessage.Deserialize(envelope.PayloadJson)!.JobId);
        Assert.Equal(StockCountSubmissionJobStatus.Accepted, (await JobsAsync(sessionId)).Single().Status);
    }

    [Fact]
    public async Task RedeliveryAndDuplicateConsumers_SubmitTheErpStockCountExactlyOnce_WithoutApprovalOrPosting()
    {
        await _fixture.AddUserWithRoleAsync(4110, "BusinessOwner", "StockCount.Submit", "StockCount.ViewAll");
        using var owner = OwnerClient();
        var (sessionId, _, revision) = await NewCountedSessionAsync(owner);
        using var accepted = await SubmitAsync(owner, sessionId, revision);
        var jobId = (await ReadAsync(accepted)).GetProperty("data").GetProperty("jobId").GetGuid();
        var inventoryBefore = await InventorySnapshotAsync();

        var outcomes = await Task.WhenAll(
            ProcessAsync(jobId), ProcessAsync(jobId), ProcessAsync(jobId));
        Assert.Single(outcomes, outcome => outcome == MobileStockTakeSubmissionProcessingOutcome.ErpSubmitted);
        Assert.All(outcomes.Where(outcome => outcome != MobileStockTakeSubmissionProcessingOutcome.ErpSubmitted),
            outcome => Assert.True(
                outcome is MobileStockTakeSubmissionProcessingOutcome.ClaimedElsewhere
                    or MobileStockTakeSubmissionProcessingOutcome.AlreadyTerminal));

        // A broker redelivery after completion is a no-op.
        Assert.Equal(MobileStockTakeSubmissionProcessingOutcome.AlreadyTerminal, await ProcessAsync(jobId));
        var job = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal(StockCountSubmissionJobStatus.ErpSubmitted, job.Status);
        Assert.Equal(1, job.AttemptCount);
        Assert.NotNull(job.CompletedAtUtc);
        Assert.Null(job.LeaseOwner);

        var session = await SessionAsync(sessionId);
        Assert.Equal(StockCount.StatusSubmitted, session.Status);
        Assert.Equal(4110, session.SubmittedBy);
        Assert.Null(session.ApprovedBy);
        Assert.Null(session.ApprovedAt);
        Assert.Equal(StockCount.MovementReconciliationUnresolved, session.MovementReconciliationStatus);
        Assert.Equal(inventoryBefore, await InventorySnapshotAsync());
        Assert.True(await AuditActorActionExistsAsync(sessionId, "submit", 4110));

        using var status = await owner.GetAsync($"/api/mobile/v1/stock-take/sessions/{sessionId}/submissions/{jobId}");
        var data = (await ReadAsync(status)).GetProperty("data");
        Assert.Equal("ErpSubmitted", data.GetProperty("status").GetString());
        Assert.True(data.GetProperty("isTerminal").GetBoolean());
        Assert.Equal("Submitted", data.GetProperty("sessionStatus").GetString());
        Assert.Equal("Unresolved", data.GetProperty("movementReconciliationStatus").GetString());

        // Final stock reconciliation stays blocked: the ERP adjustment draft fails closed.
        Assert.True(await _fixture.MobileAdjustmentDraftIsBlockedAsync(sessionId));
    }

    [Fact]
    public async Task WorkerCrashRecovery_ReclaimsExpiredLeaseAndCompletesOnce()
    {
        await _fixture.AddUserWithRoleAsync(4110, "BusinessOwner", "StockCount.Submit", "StockCount.ViewAll");
        using var owner = OwnerClient();
        var (sessionId, _, revision) = await NewCountedSessionAsync(owner);
        using var accepted = await SubmitAsync(owner, sessionId, revision);
        var jobId = (await ReadAsync(accepted)).GetProperty("data").GetProperty("jobId").GetGuid();

        await UpdateJobAsync(jobId, setters => setters
            .SetProperty(job => job.Status, StockCountSubmissionJobStatus.Processing)
            .SetProperty(job => job.AttemptCount, 1)
            .SetProperty(job => job.LeaseOwner, Guid.NewGuid())
            .SetProperty(job => job.LeaseExpiresAtUtc, ClientUtcNow().AddMinutes(5)));
        Assert.Equal(MobileStockTakeSubmissionProcessingOutcome.ClaimedElsewhere, await ProcessAsync(jobId));
        Assert.DoesNotContain(jobId, await RecoverableAsync());

        await UpdateJobAsync(jobId, setters => setters
            .SetProperty(job => job.LeaseExpiresAtUtc, ClientUtcNow().AddSeconds(-1)));
        Assert.Contains(jobId, await RecoverableAsync());
        Assert.Equal(MobileStockTakeSubmissionProcessingOutcome.ErpSubmitted, await ProcessAsync(jobId));
        var job = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal(2, job.AttemptCount);
        Assert.Equal(StockCount.StatusSubmitted, (await SessionAsync(sessionId)).Status);
        Assert.DoesNotContain(jobId, await RecoverableAsync());
    }

    [Fact]
    public async Task AcceptedJobWhoseMessageNeverArrives_IsRecoveredFromPostgreSql()
    {
        using var owner = OwnerClient();
        var (sessionId, _, revision) = await NewCountedSessionAsync(owner);
        using var accepted = await SubmitAsync(owner, sessionId, revision);
        var jobId = (await ReadAsync(accepted)).GetProperty("data").GetProperty("jobId").GetGuid();
        Assert.DoesNotContain(jobId, await RecoverableAsync());

        await UpdateJobAsync(jobId, setters => setters
            .SetProperty(job => job.CreatedAtUtc, ClientUtcNow().AddMinutes(-10)));
        Assert.Contains(jobId, await RecoverableAsync());
    }

    [Fact]
    public async Task RetryableFailuresAreBoundedAndTerminalFailuresAreNotRetried()
    {
        await _fixture.AddUserWithRoleAsync(4110, "BusinessOwner", "StockCount.Submit", "StockCount.ViewAll");
        using var owner = OwnerClient();
        var (sessionId, _, revision) = await NewCountedSessionAsync(owner);
        using var accepted = await SubmitAsync(owner, sessionId, revision);
        var jobId = (await ReadAsync(accepted)).GetProperty("data").GetProperty("jobId").GetGuid();

        var failingSender = new Mock<ISender>();
        failingSender.Setup(candidate => candidate.Send<long>(
                It.IsAny<IRequest<long>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("injected transient failure"));
        var options = new MobileStockTakeSubmissionOptions(MaxAttempts: 2);

        Assert.Equal(MobileStockTakeSubmissionProcessingOutcome.RetryScheduled,
            await ProcessWithAsync(jobId, failingSender.Object, options));
        var retrying = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal(StockCountSubmissionJobStatus.RetryScheduled, retrying.Status);
        Assert.Equal(1, retrying.AttemptCount);
        Assert.Equal("TRANSIENT_FAILURE", retrying.FailureCode);
        Assert.True(retrying.NextAttemptAtUtc > DateTime.UtcNow);
        Assert.Equal(StockCount.StatusDraft, (await SessionAsync(sessionId)).Status);
        Assert.Equal(MobileStockTakeSubmissionProcessingOutcome.NotDue,
            await ProcessWithAsync(jobId, failingSender.Object, options));

        await UpdateJobAsync(jobId, setters => setters
            .SetProperty(job => job.NextAttemptAtUtc, ClientUtcNow().AddSeconds(-1)));
        Assert.Contains(jobId, await RecoverableAsync());
        Assert.Equal(MobileStockTakeSubmissionProcessingOutcome.Failed,
            await ProcessWithAsync(jobId, failingSender.Object, options));
        var exhausted = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal(StockCountSubmissionJobStatus.Failed, exhausted.Status);
        Assert.Equal("RETRY_ATTEMPTS_EXHAUSTED", exhausted.FailureCode);
        Assert.Equal(2, exhausted.AttemptCount);
        Assert.Equal(MobileStockTakeSubmissionProcessingOutcome.AlreadyTerminal,
            await ProcessWithAsync(jobId, failingSender.Object, options));
        Assert.Equal(StockCount.StatusDraft, (await SessionAsync(sessionId)).Status);

        // A terminal failure releases the session lock; a new request can be accepted.
        using var resubmitted = await SubmitAsync(owner, sessionId, revision);
        Assert.Equal(HttpStatusCode.Accepted, resubmitted.StatusCode);

        await AssertTerminalFailureAsync(owner, "COUNT_REVISION_CHANGED", async id =>
            await SetCountRevisionAsync(id, 99));
        await AssertTerminalFailureAsync(owner, "STOCK_TAKE_STATE_CHANGED", async id =>
            await SetSessionStatusAsync(id, StockCount.StatusApproved));
        await AssertTerminalFailureAsync(owner, "ERP_SUBMISSION_REJECTED", async id =>
            await ClearCountsAsync(id));
    }

    [Fact]
    public async Task WorkerRechecksThePersistedActorAndNeverSubstitutesAPrivilegedIdentity()
    {
        await _fixture.AddUserWithRoleAsync(4121, "Manager", "StockCount.Submit", "StockCount.ViewAll",
            "StockCount.Create", "StockCount.Upload");
        using var manager = _fixture.CreateClient(4101, 4121, "Manager", OwnerPermissions);
        var (sessionId, _, revision) = await NewCountedSessionAsync(manager);
        using var accepted = await SubmitAsync(manager, sessionId, revision);
        var jobId = (await ReadAsync(accepted)).GetProperty("data").GetProperty("jobId").GetGuid();

        await _fixture.SetRolePermissionsAsync(4121, "Manager");
        Assert.Equal(MobileStockTakeSubmissionProcessingOutcome.Failed, await ProcessAsync(jobId));
        var job = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal("ACTOR_NOT_AUTHORIZED", job.FailureCode);
        Assert.Equal(StockCount.StatusDraft, (await SessionAsync(sessionId)).Status);
        Assert.Null((await SessionAsync(sessionId)).SubmittedBy);
    }

    [Fact]
    public async Task StatusPollingIsScopedToTheAuthorizedSessionAndOutlet()
    {
        using var owner = OwnerClient();
        var (sessionId, _, revision) = await NewCountedSessionAsync(owner);
        var (otherSession, _, otherRevision) = await NewCountedSessionAsync(owner);
        using var accepted = await SubmitAsync(owner, sessionId, revision);
        var jobId = (await ReadAsync(accepted)).GetProperty("data").GetProperty("jobId").GetGuid();
        using var otherAccepted = await SubmitAsync(owner, otherSession, otherRevision);
        Assert.Equal(HttpStatusCode.Accepted, otherAccepted.StatusCode);

        using (var crossSession = await owner.GetAsync(
                   $"/api/mobile/v1/stock-take/sessions/{otherSession}/submissions/{jobId}"))
            Assert.Equal(HttpStatusCode.NotFound, crossSession.StatusCode);
        using (var unknown = await owner.GetAsync(
                   $"/api/mobile/v1/stock-take/sessions/{sessionId}/submissions/{Guid.NewGuid()}"))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using (var invalid = await owner.GetAsync(
                   $"/api/mobile/v1/stock-take/sessions/{sessionId}/submissions/{Guid.Empty}"))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var manager = _fixture.CreateClient(4101, 4104, "SalesManager",
            "StockCount.ViewOwn,StockCount.Create,StockCount.Upload,StockCount.Submit");
        using (var outsideOutlet = await manager.GetAsync(
                   $"/api/mobile/v1/stock-take/sessions/{sessionId}/submissions/{jobId}"))
            Assert.Equal(HttpStatusCode.NotFound, outsideOutlet.StatusCode);

        using var salesperson = _fixture.CreateClient(4101, 4102, "SalesPerson", "StockCount.ViewAll");
        using (var denied = await salesperson.GetAsync(
                   $"/api/mobile/v1/stock-take/sessions/{sessionId}/submissions/{jobId}"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task SubmissionHoldsOnlyTheSessionRow_SoPosInventoryMovementsAreNotBlocked()
    {
        using var owner = OwnerClient();
        var (sessionId, _, _) = await NewCountedSessionAsync(owner);
        await using var lockScope = _fixture.CreateServiceScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await using var transaction = await lockDb.Database.BeginTransactionAsync();
        await lockDb.StockCounts
            .FromSqlInterpolated($"SELECT * FROM stock_counts WHERE id = {sessionId} FOR UPDATE")
            .SingleAsync();

        var posMovement = Task.Run(async () =>
        {
            await using var scope = _fixture.CreateServiceScope();
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var inventory = await db.Inventories.FirstAsync(row => row.VariantId == 9104 && row.LocationId == 5101);
            inventory.Quantity += 1;
            db.StockLedgers.Add(new StockLedger
            {
                VariantId = 9104,
                LocationId = 5101,
                LocationType = "outlet",
                TransactionType = StockLedgerTransactionType.Return,
                QtyIn = 1,
                BalanceAfter = inventory.Quantity,
                UnitCost = 1m,
                MovementValue = 1,
                IsCostValuationReliable = true,
                BusinessDate = DateTime.UtcNow.Date,
                MovementSequence = 9_000_001,
                ReferenceType = StockLedgerReferenceType.Sale,
                ReferenceId = 10101,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        });
        var completed = await Task.WhenAny(posMovement, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(posMovement, completed);
        await posMovement;
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task DeliveryHandlerAcknowledgesOnceAndNeverRequeuesPoisonOrFailingMessages()
    {
        var jobId = Guid.NewGuid();
        var processor = new Mock<IMobileStockTakeSubmissionProcessor>();
        processor.Setup(candidate => candidate.ProcessAsync(jobId, 4101, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MobileStockTakeSubmissionProcessingOutcome.ErpSubmitted);
        var handler = new MobileStockTakeSubmissionDelivery(
            processor.Object, NullLogger<MobileStockTakeSubmissionDelivery>.Instance);

        var channel = new Mock<IChannel>();
        await handler.HandleAsync(channel.Object, 1, Serialize(Envelope(jobId)), default);
        channel.Verify(candidate => candidate.BasicAckAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);
        processor.Verify(candidate => candidate.ProcessAsync(jobId, 4101, It.IsAny<CancellationToken>()), Times.Once);

        var poisonChannel = new Mock<IChannel>();
        await handler.HandleAsync(poisonChannel.Object, 2,
            Serialize(Envelope(jobId) with { MessageType = "unexpected.message" }), default);
        await handler.HandleAsync(poisonChannel.Object, 3, "not-json"u8.ToArray(), default);
        poisonChannel.Verify(candidate => candidate.BasicNackAsync(
            It.IsAny<ulong>(), false, false, It.IsAny<CancellationToken>()), Times.Exactly(2));
        poisonChannel.Verify(candidate => candidate.BasicAckAsync(
            It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

        var failingProcessor = new Mock<IMobileStockTakeSubmissionProcessor>();
        failingProcessor.Setup(candidate => candidate.ProcessAsync(
                It.IsAny<Guid>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("database unavailable"));
        var failingChannel = new Mock<IChannel>();
        await new MobileStockTakeSubmissionDelivery(
                failingProcessor.Object, NullLogger<MobileStockTakeSubmissionDelivery>.Instance)
            .HandleAsync(failingChannel.Object, 4, Serialize(Envelope(jobId)), default);
        failingChannel.Verify(candidate => candidate.BasicNackAsync(
            4, false, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    private async Task AssertTerminalFailureAsync(
        HttpClient owner,
        string expectedFailureCode,
        Func<long, Task> sabotage)
    {
        var (sessionId, _, revision) = await NewCountedSessionAsync(owner);
        using var accepted = await SubmitAsync(owner, sessionId, revision);
        var jobId = (await ReadAsync(accepted)).GetProperty("data").GetProperty("jobId").GetGuid();
        await sabotage(sessionId);
        Assert.Equal(MobileStockTakeSubmissionProcessingOutcome.Failed, await ProcessAsync(jobId));
        var job = Assert.Single(await JobsAsync(sessionId));
        Assert.Equal(StockCountSubmissionJobStatus.Failed, job.Status);
        Assert.Equal(expectedFailureCode, job.FailureCode);
        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(MobileStockTakeSubmissionProcessingOutcome.AlreadyTerminal, await ProcessAsync(jobId));
    }

    private HttpClient OwnerClient() => _fixture.CreateClient(4101, 4110, "BusinessOwner", OwnerPermissions);

    private static string NewKey() => $"submission-key-{Interlocked.Increment(ref _keySeed):D6}-{Guid.NewGuid():N}";

    private async Task<long> NewOutletAsync()
    {
        var outletId = Interlocked.Increment(ref _outletSeed);
        await _fixture.AddOutletAsync(outletId);
        return outletId;
    }

    private async Task<(long SessionId, long OutletId, long Revision)> NewCountedSessionAsync(
        HttpClient client,
        decimal quantity = 3m)
    {
        var outletId = await NewOutletAsync();
        var sessionId = await CreateSessionAsync(client, outletId);
        var revision = await ApplyCountAsync(client, sessionId, 0, 1, 9103, quantity);
        return (sessionId, outletId, revision);
    }

    private static async Task<long> CreateSessionAsync(HttpClient client, long outletId)
    {
        using var response = await client.PostAsJsonAsync("/api/mobile/v1/stock-take/sessions", new
        {
            outletId,
            stockCountDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            remarks = "Submission test"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadAsync(response)).GetProperty("data").GetProperty("id").GetInt64();
    }

    private static async Task<long> ApplyCountAsync(
        HttpClient client,
        long sessionId,
        long expectedRevision,
        long sequence,
        long variantId,
        decimal quantity)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/mobile/v1/stock-take/sessions/{sessionId}/operations:batch",
            CountBatch(expectedRevision, sequence, variantId, quantity));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await ReadAsync(response)).GetProperty("data").GetProperty("currentRevision").GetInt64();
    }

    private static object CountBatch(long expectedRevision, long sequence, long variantId, decimal quantity) => new
    {
        deviceSessionId = "submission-test-device",
        expectedRevision,
        operations = new[]
        {
            new
            {
                clientOperationId = Guid.NewGuid(),
                clientSequence = sequence,
                productVariantId = variantId,
                countedQuantity = quantity,
                rawBarcode = (string?)null,
                clientObservedAt = (DateTimeOffset?)null
            }
        }
    };

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, long sessionId, long revision) =>
        SendSubmitAsync(client, sessionId, NewKey(), new { acceptedRevision = revision });

    private static Task<HttpResponseMessage> SendSubmitAsync(
        HttpClient client,
        long sessionId,
        string? idempotencyKey,
        object body)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/mobile/v1/stock-take/sessions/{sessionId}/submissions")
        {
            Content = JsonContent.Create(body)
        };
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private async Task<StockCountSubmissionJob[]> JobsAsync(long sessionId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return await db.StockCountSubmissionJobs.AsNoTracking()
            .Where(job => job.StockCountId == sessionId).ToArrayAsync();
    }

    private async Task<IntegrationOutboxMessage[]> OutboxAsync(Guid messageId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return await db.Set<IntegrationOutboxMessage>().AsNoTracking()
            .Where(row => row.Id == messageId).ToArrayAsync();
    }

    private async Task<IntegrationOutboxMessage[]> OutboxByAggregateAsync(Guid jobId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var aggregateId = jobId.ToString();
        return await db.Set<IntegrationOutboxMessage>().AsNoTracking()
            .Where(row => row.AggregateType == "StockCountSubmissionJob" && row.AggregateId == aggregateId)
            .ToArrayAsync();
    }

    private async Task<StockCount> SessionAsync(long sessionId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return await db.StockCounts.AsNoTracking().SingleAsync(count => count.Id == sessionId);
    }

    private async Task SetSessionStatusAsync(long sessionId, string status)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.StockCounts.Where(count => count.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(count => count.Status, status));
    }

    private async Task SetCountRevisionAsync(long sessionId, long revision)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.StockCounts.Where(count => count.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(count => count.CountRevision, revision));
    }

    private async Task ClearCountsAsync(long sessionId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.StockCountLines.Where(line => line.StockCountId == sessionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(line => line.PhysicalStock, (decimal?)null)
                .SetProperty(line => line.Remarks, (string?)null));
    }

    private async Task UpdateJobAsync(
        Guid jobId,
        System.Linq.Expressions.Expression<Func<
            Microsoft.EntityFrameworkCore.Query.SetPropertyCalls<StockCountSubmissionJob>,
            Microsoft.EntityFrameworkCore.Query.SetPropertyCalls<StockCountSubmissionJob>>> configure)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.StockCountSubmissionJobs.Where(job => job.Id == jobId).ExecuteUpdateAsync(configure);
    }

    // DateTime.UtcNow inside ExecuteUpdate is translated to PostgreSQL now(), but the processors and the outbox
    // dispatcher compare against the application clock. A method call is evaluated by EF on the client, so every
    // time a test writes comes from the same clock the code under test reads (a container clock running ahead of the
    // host by more than the 1-second margin made these tests fail intermittently).
    private static DateTime ClientUtcNow() => DateTime.UtcNow;

    // The class shares one database: undispatched messages of earlier tests would otherwise compete for the
    // dispatcher's bounded batch (20) and decide, by Guid order, whether this test's message is attempted at all.
    private static Task DeferOtherOutboxMessagesAsync(RetailPOSDbContext db, Guid messageId) =>
        db.Set<IntegrationOutboxMessage>()
            .Where(row => row.Id != messageId && row.Status == "Pending")
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.NextAttemptAtUtc, ClientUtcNow().AddHours(1)));

    private async Task<IReadOnlyList<Guid>> RecoverableAsync()
    {
        await using var scope = _fixture.CreateServiceScope();
        return await scope.ServiceProvider.GetRequiredService<IMobileStockTakeSubmissionProcessor>()
            .FindRecoverableJobIdsAsync(default);
    }

    private async Task<MobileStockTakeSubmissionProcessingOutcome> ProcessAsync(Guid jobId)
    {
        await using var scope = _fixture.CreateServiceScope();
        return await scope.ServiceProvider.GetRequiredService<IMobileStockTakeSubmissionProcessor>()
            .ProcessAsync(jobId, null, default);
    }

    private async Task<MobileStockTakeSubmissionProcessingOutcome> ProcessWithAsync(
        Guid jobId,
        ISender sender,
        MobileStockTakeSubmissionOptions options)
    {
        await using var scope = _fixture.CreateServiceScope();
        var provider = scope.ServiceProvider;
        var processor = new MobileStockTakeSubmissionProcessor(
            provider.GetRequiredService<RetailPOSDbContext>(),
            provider.GetRequiredService<IAuditContext>(),
            provider.GetRequiredService<IRequiredAuditTransaction>(),
            sender,
            provider.GetRequiredService<IMobileStockTakeSubmissionActorPolicy>(),
            options,
            TimeProvider.System,
            NullLogger<MobileStockTakeSubmissionProcessor>.Instance);
        return await processor.ProcessAsync(jobId, null, default);
    }

    private async Task<string> InventorySnapshotAsync()
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var inventories = await db.Inventories.AsNoTracking().OrderBy(row => row.Id)
            .Select(row => row.Id + ":" + row.Quantity).ToListAsync();
        var ledgerCount = await db.StockLedgers.CountAsync();
        return string.Join('|', inventories) + $"#ledger={ledgerCount}";
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
        var payload = MobileStockTakeSubmissionMessage.Serialize(
            new MobileStockTakeSubmissionMessage.Payload(4101, jobId, 1, 1));
        return new IntegrationEnvelope(
            Guid.NewGuid(), 4101, "RetailPOS", "mobile.stock-take.submission.requested", 1,
            "StockCountSubmissionJob", jobId.ToString(), Guid.NewGuid(), null, DateTime.UtcNow,
            payload, IntegrationEnvelope.Hash(payload));
    }

    private static ReadOnlyMemory<byte> Serialize(IntegrationEnvelope envelope) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope);
}
