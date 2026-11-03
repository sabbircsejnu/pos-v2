using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Sales;

namespace RetailPOS.Tests.Inventory;

public sealed class MobileStockTakeReviewPostgresHttpTests
    : IClassFixture<MobileSalesDashboardPostgresFixture>
{
    private const string OwnerPermissions =
        "StockCount.ViewAll,StockCount.Create,StockCount.Upload,StockCount.Submit";
    private const string OwnPermissions =
        "StockCount.ViewOwn,StockCount.Create,StockCount.Upload,StockCount.Submit";
    private const string Sessions = "/api/mobile/v1/stock-take/sessions";
    private static long _outletSeed = 6300;
    private static long _keySeed;

    private readonly MobileSalesDashboardPostgresFixture _fixture;

    public MobileStockTakeReviewPostgresHttpTests(MobileSalesDashboardPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task History_IsPaginatedNewestFirstAndFilteredByLifecycleAndSubmissionState()
    {
        using var owner = OwnerClient();
        var first = await NewSessionAsync(owner);
        var second = await NewSessionAsync(owner);
        var third = await NewSessionAsync(owner);
        await SetSessionStatusAsync(first.SessionId, StockCount.StatusRejected);
        await InsertJobAsync(second.SessionId, StockCountSubmissionJobStatus.Failed, "ERP_SUBMISSION_REJECTED", 1);
        await ApplyCountAsync(owner, third.SessionId, 0, 1, 9103, 2m);
        using (var accepted = await SubmitAsync(owner, third.SessionId, 1))
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);

        var page = await GetDataAsync(owner, $"{Sessions}/history?pageSize=2&page=1");
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.True(page.GetProperty("totalCount").GetInt32() >= 3);
        var ids = page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt64()).ToArray();
        Assert.Equal([third.SessionId, second.SessionId], ids);

        var inProgress = await GetDataAsync(owner, $"{Sessions}/history?submissionStatus=accepted&outletId={third.OutletId}");
        var progressItem = Assert.Single(inProgress.GetProperty("items").EnumerateArray());
        Assert.Equal("SubmissionInProgress", progressItem.GetProperty("stage").GetString());
        Assert.Equal("Draft", progressItem.GetProperty("erpStatus").GetString());
        Assert.Equal("Accepted", progressItem.GetProperty("latestSubmission").GetProperty("status").GetString());

        var failed = await GetDataAsync(owner, $"{Sessions}/history?submissionStatus=Failed&outletId={second.OutletId}");
        var failedItem = Assert.Single(failed.GetProperty("items").EnumerateArray());
        Assert.Equal("ERP_SUBMISSION_REJECTED", failedItem.GetProperty("latestSubmission").GetProperty("failureCode").GetString());
        Assert.True(failedItem.GetProperty("latestSubmission").GetProperty("isTerminal").GetBoolean());

        var rejected = await GetDataAsync(owner, $"{Sessions}/history?status=Rejected&outletId={first.OutletId}");
        var rejectedItem = Assert.Single(rejected.GetProperty("items").EnumerateArray());
        Assert.Equal("RejectedByErp", rejectedItem.GetProperty("stage").GetString());
        Assert.Equal(JsonValueKind.Null, rejectedItem.GetProperty("latestSubmission").ValueKind);

        var dated = await GetDataAsync(owner,
            $"{Sessions}/history?outletId={first.OutletId}&createdFrom={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-30).ToString("O"))}");
        Assert.Single(dated.GetProperty("items").EnumerateArray());
        var future = await GetDataAsync(owner,
            $"{Sessions}/history?outletId={first.OutletId}&createdFrom={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}");
        Assert.Empty(future.GetProperty("items").EnumerateArray());

        foreach (var invalid in new[]
                 {
                     "page=0", "pageSize=101", "status=Bogus", "submissionStatus=Bogus", "outletId=-1",
                     $"createdFrom={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}&createdTo={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}"
                 })
        {
            using var response = await owner.GetAsync($"{Sessions}/history?{invalid}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task SalesManagerHistoryAndReadsAreLimitedToAuthorizedOutlets_AndOtherRolesAreDenied()
    {
        using var owner = OwnerClient();
        var outside = await NewSessionAsync(owner);
        using var manager = _fixture.CreateClient(4101, 4104, "SalesManager", OwnerPermissions);

        using (var denied = await manager.GetAsync($"{Sessions}/history?outletId={outside.OutletId}"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var managerHistory = await GetDataAsync(manager, $"{Sessions}/history?pageSize=100");
        Assert.DoesNotContain(
            managerHistory.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetInt64() == outside.SessionId);

        foreach (var path in ReviewPaths(outside.SessionId))
        {
            using var response = await manager.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        foreach (var (role, userId) in new[] { ("SalesPerson", 4102L), ("Cashier", 4105L) })
        {
            using var client = _fixture.CreateClient(4101, userId, role, OwnerPermissions);
            foreach (var path in ReviewPaths(outside.SessionId).Append($"{Sessions}/history"))
            {
                using var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }

        using var noPermission = _fixture.CreateClient(4101, 4110, "BusinessOwner", "StockCount.Create");
        using var roleSwitched = _fixture.CreateClient(4101, 4110, "BusinessOwner", OwnerPermissions, true, "SalesManager");
        using var foreignBusiness = _fixture.CreateClient(4202, 4110, "BusinessOwner", OwnerPermissions);
        using var anonymous = _fixture.CreateUnauthenticatedClient();
        foreach (var client in new[] { noPermission, roleSwitched, foreignBusiness, anonymous })
        {
            foreach (var path in ReviewPaths(outside.SessionId).Append($"{Sessions}/history"))
            {
                using var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }

        foreach (var path in new[]
                 {
                     $"{Sessions}/0/lifecycle", $"{Sessions}/0/submissions", $"{Sessions}/0/results",
                     $"{Sessions}/{outside.SessionId}/submissions?page=0",
                     $"{Sessions}/{outside.SessionId}/results?pageSize=500"
                 })
        {
            using var response = await owner.GetAsync(path);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        using var unknown = await owner.GetAsync($"{Sessions}/987654/lifecycle");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task ViewOwnSeesOnlyOwnSessions_WhileViewAllSeesTheAuthorizedOutlet()
    {
        using var owner = OwnerClient();
        using var ownManager = _fixture.CreateClient(4101, 4104, "SalesManager", OwnPermissions);
        using var allManager = _fixture.CreateClient(4101, 4104, "SalesManager", OwnerPermissions);
        var ownerSession = await CreateSessionAsync(owner, 5101);
        var managerSession = await CreateSessionAsync(ownManager, 5102);

        var ownHistory = await GetDataAsync(ownManager, $"{Sessions}/history?pageSize=100");
        var ownIds = ownHistory.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt64()).ToArray();
        Assert.Contains(managerSession, ownIds);
        Assert.DoesNotContain(ownerSession, ownIds);

        foreach (var path in ReviewPaths(ownerSession).Concat([$"{Sessions}/{ownerSession}", $"{Sessions}/{ownerSession}/lines"]))
        {
            using var response = await ownManager.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        foreach (var path in ReviewPaths(managerSession))
        {
            using var response = await ownManager.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var allIds = (await GetDataAsync(allManager, $"{Sessions}/history?pageSize=100"))
            .GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt64()).ToArray();
        Assert.Contains(ownerSession, allIds);
        Assert.Contains(managerSession, allIds);
        using var allSees = await allManager.GetAsync($"{Sessions}/{ownerSession}/lifecycle");
        Assert.Equal(HttpStatusCode.OK, allSees.StatusCode);

        var ownList = await GetDataAsync(ownManager, $"{Sessions}?pageSize=100");
        Assert.DoesNotContain(
            ownList.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetInt64() == ownerSession);
    }

    [Fact]
    public async Task DraftAndSubmittedSessionsStayBlind_AndSeparateSubmissionFromReconciliationApprovalAndAdjustment()
    {
        await _fixture.AddUserWithRoleAsync(4110, "BusinessOwner", "StockCount.Submit", "StockCount.ViewAll");
        using var owner = OwnerClient();
        var outletId = await SeedOutletAsync(10);
        var sessionId = await CreateSessionAsync(owner, outletId);
        var revision = await ApplyCountAsync(owner, sessionId, 0, 1, 9103, 8m);

        var draft = await GetDataAsync(owner, $"{Sessions}/{sessionId}/lifecycle");
        Assert.Equal("Counting", draft.GetProperty("stage").GetString());
        Assert.Equal("Draft", draft.GetProperty("erpStatus").GetString());
        Assert.Equal("NotEvaluated", draft.GetProperty("reconciliation").GetProperty("evidenceStatus").GetString());
        Assert.Equal("SESSION_NOT_SUBMITTED",
            draft.GetProperty("reconciliation").GetProperty("reasons")[0].GetProperty("code").GetString());
        Assert.Equal("NotSubmitted", draft.GetProperty("approval").GetProperty("state").GetString());
        Assert.Equal("NotAvailable", draft.GetProperty("results").GetProperty("availability").GetString());

        using var accepted = await SubmitAsync(owner, sessionId, revision);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var jobId = (await ReadAsync(accepted)).GetProperty("data").GetProperty("jobId").GetGuid();
        var queued = await GetDataAsync(owner, $"{Sessions}/{sessionId}/lifecycle");
        Assert.Equal("SubmissionInProgress", queued.GetProperty("stage").GetString());
        Assert.Equal("Accepted", queued.GetProperty("latestSubmission").GetProperty("status").GetString());
        Assert.Equal(1, queued.GetProperty("submissionJobCount").GetInt32());

        await ProcessJobAsync(jobId);
        var submissions = await GetDataAsync(owner, $"{Sessions}/{sessionId}/submissions");
        var job = Assert.Single(submissions.GetProperty("items").EnumerateArray());
        Assert.Equal("ErpSubmitted", job.GetProperty("status").GetString());

        var submitted = await GetDataAsync(owner, $"{Sessions}/{sessionId}/lifecycle");
        Assert.Equal("SubmittedForErpReview", submitted.GetProperty("stage").GetString());
        Assert.Equal("PendingReview", submitted.GetProperty("approval").GetProperty("state").GetString());
        Assert.Equal("NotGenerated", submitted.GetProperty("inventoryAdjustment").GetProperty("state").GetString());
        Assert.False(submitted.GetProperty("inventoryAdjustment").GetProperty("isInventoryPosted").GetBoolean());
        Assert.Equal("Unresolved", submitted.GetProperty("reconciliation").GetProperty("erpStatus").GetString());
        Assert.Equal("NoConflictDetected", submitted.GetProperty("reconciliation").GetProperty("evidenceStatus").GetString());
        Assert.False(submitted.GetProperty("reconciliation").GetProperty("resolvesErpReconciliation").GetBoolean());
        Assert.Equal("NotAvailable", submitted.GetProperty("results").GetProperty("availability").GetString());

        var results = await GetDataAsync(owner, $"{Sessions}/{sessionId}/results");
        Assert.Equal("NotAvailable", results.GetProperty("availability").GetString());
        Assert.Equal(JsonValueKind.Null, results.GetProperty("lines").ValueKind);

        foreach (var path in new[] { $"{Sessions}/{sessionId}/lifecycle", $"{Sessions}/{sessionId}/submissions", $"{Sessions}/history?outletId={outletId}" })
        {
            var text = await owner.GetStringAsync(path);
            foreach (var forbidden in new[] { "baselineQuantity", "currentStock", "expected", "variance", "netDifference", "countedQuantity" })
                Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(JsonValueKind.Null, results.GetProperty("netDifference").ValueKind);
        Assert.Equal(JsonValueKind.Null, results.GetProperty("differenceLineCount").ValueKind);
    }

    [Fact]
    public async Task RejectedSessionWithholdsReviewerTextAndResults()
    {
        using var owner = OwnerClient();
        var session = await NewSessionAsync(owner);
        await using (var scope = _fixture.CreateServiceScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.StockCounts.Where(count => count.Id == session.SessionId).ExecuteUpdateAsync(setters => setters
                .SetProperty(count => count.Status, StockCount.StatusRejected)
                .SetProperty(count => count.RejectedAt, DateTime.UtcNow)
                .SetProperty(count => count.RejectedBy, 4110L)
                .SetProperty(count => count.RejectionReason, "Short by 12 units on shelf A"));
        }

        var text = await owner.GetStringAsync($"{Sessions}/{session.SessionId}/lifecycle");
        Assert.DoesNotContain("Short by", text);
        var lifecycle = (await ReadJsonAsync(text)).GetProperty("data");
        Assert.Equal("RejectedByErp", lifecycle.GetProperty("stage").GetString());
        Assert.Equal("Rejected", lifecycle.GetProperty("approval").GetProperty("state").GetString());
        Assert.True(lifecycle.GetProperty("approval").GetProperty("hasRejectionReason").GetBoolean());
        Assert.Equal("SESSION_REJECTED",
            lifecycle.GetProperty("reconciliation").GetProperty("reasons")[0].GetProperty("code").GetString());
        var results = await GetDataAsync(owner, $"{Sessions}/{session.SessionId}/results");
        Assert.Equal("NotAvailable", results.GetProperty("availability").GetString());
    }

    [Fact]
    public async Task FailedAndRetriedSubmissions_AreListedNewestFirstWithStableFailureCodes()
    {
        using var owner = OwnerClient();
        var session = await NewSessionAsync(owner);
        await ApplyCountAsync(owner, session.SessionId, 0, 1, 9103, 1m);
        await InsertJobAsync(session.SessionId, StockCountSubmissionJobStatus.Failed, "RETRY_ATTEMPTS_EXHAUSTED", 1, attempts: 5);
        await Task.Delay(20);

        var failed = await GetDataAsync(owner, $"{Sessions}/{session.SessionId}/lifecycle");
        Assert.Equal("SubmissionFailed", failed.GetProperty("stage").GetString());
        Assert.Equal("RETRY_ATTEMPTS_EXHAUSTED", failed.GetProperty("latestSubmission").GetProperty("failureCode").GetString());

        using (var retry = await SubmitAsync(owner, session.SessionId, 1))
            Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        var list = await GetDataAsync(owner, $"{Sessions}/{session.SessionId}/submissions?pageSize=1");
        Assert.Equal(2, list.GetProperty("totalCount").GetInt32());
        var newest = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal("Accepted", newest.GetProperty("status").GetString());
        var all = await GetDataAsync(owner, $"{Sessions}/{session.SessionId}/submissions");
        Assert.Equal(
            ["Accepted", "Failed"],
            all.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("status").GetString()!).ToArray());
        Assert.Equal(5, all.GetProperty("items")[1].GetProperty("attemptCount").GetInt32());
    }

    [Fact]
    public async Task ApprovedCountWithConsistentMovementEvidence_ExposesFinalResultsWithoutPostingInventory()
    {
        using var owner = OwnerClient();
        var outletId = await SeedOutletAsync(10);
        var sessionId = await CreateSessionAsync(owner, outletId);
        await ApplyCountAsync(owner, sessionId, 0, 1, 9103, 8m);
        await SubmitViaErpAsync(sessionId);
        await Task.Delay(30);
        await AddLedgerMovementAsync(outletId, 2, StockLedgerTransactionType.Sale, 0, 1, 9, DateTime.UtcNow);
        await ApproveViaErpAsync(sessionId);
        var before = await InventorySnapshotAsync();

        var lifecycle = await GetDataAsync(owner, $"{Sessions}/{sessionId}/lifecycle");
        Assert.Equal("ApprovedByErp", lifecycle.GetProperty("stage").GetString());
        Assert.Equal("Approved", lifecycle.GetProperty("approval").GetProperty("state").GetString());
        Assert.Equal("NoConflictDetected", lifecycle.GetProperty("reconciliation").GetProperty("evidenceStatus").GetString());
        Assert.Equal(1, lifecycle.GetProperty("reconciliation").GetProperty("movements").GetProperty("postCountMovementCount").GetInt32());
        Assert.Equal("Unresolved", lifecycle.GetProperty("reconciliation").GetProperty("erpStatus").GetString());
        Assert.True(lifecycle.GetProperty("inventoryAdjustment").GetProperty("draftBlockedByErpReconciliation").GetBoolean());
        Assert.False(lifecycle.GetProperty("inventoryAdjustment").GetProperty("isInventoryPosted").GetBoolean());
        Assert.Equal("Available", lifecycle.GetProperty("results").GetProperty("availability").GetString());

        var results = await GetDataAsync(owner, $"{Sessions}/{sessionId}/results?pageSize=100");
        Assert.Equal("Available", results.GetProperty("availability").GetString());
        Assert.Equal(1, results.GetProperty("differenceLineCount").GetInt32());
        Assert.Equal(-2m, results.GetProperty("netDifference").GetDecimal());
        var lines = results.GetProperty("lines").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(results.GetProperty("countedLineCount").GetInt32(), lines.Length);
        var counted = Assert.Single(lines, line => line.GetProperty("variantId").GetInt64() == 9103);
        Assert.Equal(8m, counted.GetProperty("countedQuantity").GetDecimal());
        Assert.Equal(10m, counted.GetProperty("baselineQuantity").GetDecimal());
        Assert.Equal(-2m, counted.GetProperty("difference").GetDecimal());

        var differences = await GetDataAsync(owner, $"{Sessions}/{sessionId}/results?differencesOnly=true");
        Assert.Equal(1, differences.GetProperty("lines").GetProperty("totalCount").GetInt32());
        Assert.Equal(before, await InventorySnapshotAsync());

        // Final results are not an inventory adjustment: the ERP draft path still fails closed.
        await using var scope = _fixture.CreateServiceScope();
        var session = await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>()
            .StockCounts.AsNoTracking().SingleAsync(count => count.Id == sessionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IStockCountAdjustmentDraftStore>().GenerateAsync(
                session.Id, session.BusinessId, session.LocationId, session.LocationType, 4110, default));
    }

    [Fact]
    public async Task MovementDuringTheCountWindow_LeavesApprovedResultsWithheldAndUnresolved()
    {
        using var owner = OwnerClient();
        var outletId = await SeedOutletAsync(10);
        var sessionId = await CreateSessionAsync(owner, outletId);
        await Task.Delay(30);
        await AddLedgerMovementAsync(outletId, 2, StockLedgerTransactionType.Sale, 0, 1, 9, DateTime.UtcNow);
        await Task.Delay(30);
        await ApplyCountAsync(owner, sessionId, 0, 1, 9103, 9m);
        await SubmitViaErpAsync(sessionId);
        await ApproveViaErpAsync(sessionId);

        var lifecycle = await GetDataAsync(owner, $"{Sessions}/{sessionId}/lifecycle");
        var reconciliation = lifecycle.GetProperty("reconciliation");
        Assert.Equal("Unresolved", reconciliation.GetProperty("evidenceStatus").GetString());
        Assert.Equal("MOVEMENT_DURING_COUNT_WINDOW", reconciliation.GetProperty("reasons")[0].GetProperty("code").GetString());
        Assert.Equal(1, reconciliation.GetProperty("movements").GetProperty("saleCount").GetInt32());
        Assert.Equal("Withheld", lifecycle.GetProperty("results").GetProperty("availability").GetString());

        var results = await GetDataAsync(owner, $"{Sessions}/{sessionId}/results");
        Assert.Equal("Withheld", results.GetProperty("availability").GetString());
        Assert.Contains("MOVEMENT_DURING_COUNT_WINDOW",
            results.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()));
        Assert.Equal(JsonValueKind.Null, results.GetProperty("lines").ValueKind);
        Assert.Equal(JsonValueKind.Null, results.GetProperty("netDifference").ValueKind);
    }

    [Theory]
    [InlineData("NoLedger", "BASELINE_LEDGER_EVIDENCE_MISSING")]
    [InlineData("LedgerMismatch", "BASELINE_LEDGER_MISMATCH")]
    [InlineData("UnledgeredChange", "INVENTORY_LEDGER_MISMATCH")]
    [InlineData("BrokenChain", "MOVEMENT_BALANCE_CHAIN_BROKEN")]
    [InlineData("BackdatedSequence", "MOVEMENT_ORDER_ANOMALY")]
    [InlineData("UnverifiedBaseline", "BASELINE_NOT_VERIFIABLE")]
    public async Task MissingOrContradictoryMovementEvidence_IsBlockedAndResultsAreWithheld(
        string scenario,
        string expectedCode)
    {
        using var owner = OwnerClient();
        var outletId = scenario switch
        {
            "NoLedger" => await SeedOutletAsync(10, withLedger: false),
            "LedgerMismatch" => await SeedOutletAsync(10, ledgerBalance: 9),
            _ => await SeedOutletAsync(10)
        };
        var sessionId = await CreateSessionAsync(owner, outletId);
        await Task.Delay(30);
        switch (scenario)
        {
            case "UnledgeredChange":
                await SetInventoryAsync(outletId, 12);
                break;
            case "BrokenChain":
                await AddLedgerMovementAsync(outletId, 2, StockLedgerTransactionType.Sale, 0, 1, 7, DateTime.UtcNow);
                await SetInventoryAsync(outletId, 7);
                break;
            case "BackdatedSequence":
                await AddLedgerMovementAsync(outletId, 1, StockLedgerTransactionType.Sale, 0, 1, 9, DateTime.UtcNow);
                await SetInventoryAsync(outletId, 9);
                break;
            case "UnverifiedBaseline":
                await using (var scope = _fixture.CreateServiceScope())
                {
                    await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>().StockCounts
                        .Where(count => count.Id == sessionId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(count => count.BaselineConsistencyStatus, "Unverified"));
                }
                break;
        }

        await ApplyCountAsync(owner, sessionId, 0, 1, 9103, 9m);
        await SubmitViaErpAsync(sessionId);
        var lifecycle = await GetDataAsync(owner, $"{Sessions}/{sessionId}/lifecycle");
        var reconciliation = lifecycle.GetProperty("reconciliation");
        Assert.Equal("Blocked", reconciliation.GetProperty("evidenceStatus").GetString());
        Assert.Contains(
            reconciliation.GetProperty("reasons").EnumerateArray(),
            reason => reason.GetProperty("code").GetString() == expectedCode);

        await ApproveViaErpAsync(sessionId);
        var results = await GetDataAsync(owner, $"{Sessions}/{sessionId}/results");
        Assert.Equal("Withheld", results.GetProperty("availability").GetString());
        Assert.Contains(expectedCode, results.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()));
        Assert.Equal(JsonValueKind.Null, results.GetProperty("lines").ValueKind);
    }

    [Fact]
    public async Task ReviewReadsAreReadOnly_ExposeNoWriteRoutes_AndDoNotBlockPosMovements()
    {
        await _fixture.AddUserWithRoleAsync(4110, "BusinessOwner", "StockCount.Submit", "StockCount.ViewAll");
        using var owner = OwnerClient();
        var outletId = await SeedOutletAsync(10);
        var sessionId = await CreateSessionAsync(owner, outletId);
        await ApplyCountAsync(owner, sessionId, 0, 1, 9103, 8m);
        await SubmitViaErpAsync(sessionId);
        var before = await InventorySnapshotAsync();
        var sessionBefore = await SessionFingerprintAsync(sessionId);
        foreach (var path in ReviewPaths(sessionId).Append($"{Sessions}/history?outletId={outletId}"))
        {
            using var response = await owner.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(before, await InventorySnapshotAsync());
        Assert.Equal(sessionBefore, await SessionFingerprintAsync(sessionId));

        await using var lockScope = _fixture.CreateServiceScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await using var transaction = await lockDb.Database.BeginTransactionAsync();
        await lockDb.StockCounts
            .FromSqlInterpolated($"SELECT * FROM stock_counts WHERE id = {sessionId} FOR UPDATE")
            .SingleAsync();
        var read = owner.GetAsync($"{Sessions}/{sessionId}/lifecycle");
        var posMovement = AddLedgerMovementAsync(outletId, 2, StockLedgerTransactionType.Sale, 0, 1, 9, DateTime.UtcNow);
        var both = Task.WhenAll(read, posMovement);
        Assert.Same(both, await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(15))));
        await both;
        using (var response = await read)
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await transaction.RollbackAsync();

        foreach (var route in new[] { "approve", "reject", "adjust", "adjustments", "reconcile", "post", "results" })
        {
            using var post = await owner.PostAsync($"{Sessions}/{sessionId}/{route}", null);
            Assert.True(post.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, route);
        }

        Assert.Equal(sessionBefore, await SessionFingerprintAsync(sessionId));
    }

    private static IEnumerable<string> ReviewPaths(long sessionId) =>
    [
        $"{Sessions}/{sessionId}/lifecycle",
        $"{Sessions}/{sessionId}/submissions",
        $"{Sessions}/{sessionId}/results"
    ];

    private HttpClient OwnerClient() => _fixture.CreateClient(4101, 4110, "BusinessOwner", OwnerPermissions);

    private static string NewKey() => $"review-key-{Interlocked.Increment(ref _keySeed):D6}-{Guid.NewGuid():N}";

    private async Task<(long SessionId, long OutletId)> NewSessionAsync(HttpClient client)
    {
        var outletId = Interlocked.Increment(ref _outletSeed);
        await _fixture.AddOutletAsync(outletId);
        return (await CreateSessionAsync(client, outletId), outletId);
    }

    private async Task<long> SeedOutletAsync(int stock, bool withLedger = true, int? ledgerBalance = null)
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
        if (withLedger)
        {
            db.StockLedgers.Add(NewLedger(
                outletId, 1, StockLedgerTransactionType.Grn, stock, 0, ledgerBalance ?? stock, DateTime.UtcNow.AddHours(-2)));
        }

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
            UnitCost = 1m,
            MovementValue = Math.Max(qtyIn, qtyOut),
            IsCostValuationReliable = true,
            BusinessDate = createdAt.Date,
            MovementSequence = sequence,
            ReferenceType = type == StockLedgerTransactionType.Sale
                ? StockLedgerReferenceType.Sale
                : StockLedgerReferenceType.Grn,
            ReferenceId = 700 + sequence,
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

    private async Task SetInventoryAsync(long outletId, int quantity)
    {
        await using var scope = _fixture.CreateServiceScope();
        await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>().Inventories
            .Where(row => row.VariantId == 9103 && row.LocationId == outletId && row.LocationType == "outlet")
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Quantity, quantity));
    }

    private async Task SetSessionStatusAsync(long sessionId, string status)
    {
        await using var scope = _fixture.CreateServiceScope();
        await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>().StockCounts
            .Where(count => count.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(count => count.Status, status));
    }

    private async Task InsertJobAsync(
        long sessionId,
        string status,
        string? failureCode,
        long acceptedRevision,
        int attempts = 1)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var session = await db.StockCounts.AsNoTracking().SingleAsync(count => count.Id == sessionId);
        var now = DateTime.UtcNow;
        db.StockCountSubmissionJobs.Add(new StockCountSubmissionJob
        {
            Id = Guid.NewGuid(),
            StockCountId = sessionId,
            BusinessId = session.BusinessId,
            OutletId = session.LocationId,
            ActorUserId = 4110,
            ActorRoleName = "BusinessOwner",
            IdempotencyKey = NewKey(),
            RequestFingerprint = new string('a', 64),
            AcceptedCountRevision = acceptedRevision,
            Status = status,
            AttemptCount = attempts,
            FailureCode = failureCode,
            OutboxMessageId = Guid.NewGuid(),
            AuditCorrelationId = Guid.NewGuid(),
            RequestCorrelationId = "review-test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CompletedAtUtc = StockCountSubmissionJobStatus.IsTerminal(status) ? now : null
        });
        await db.SaveChangesAsync();
    }

    private async Task ProcessJobAsync(Guid jobId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<IMobileStockTakeSubmissionProcessor>()
            .ProcessAsync(jobId, null, default);
        Assert.Equal(RetailPOS.Application.Inventory.StockCounts.MobileStockTakeSubmissionProcessingOutcome.ErpSubmitted, outcome);
    }

    private async Task SubmitViaErpAsync(long sessionId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var session = await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>()
            .StockCounts.AsNoTracking().SingleAsync(count => count.Id == sessionId);
        await scope.ServiceProvider.GetRequiredService<IStockCountLifecycleStore>().SubmitAsync(
            session.Id, session.BusinessId, session.LocationId, session.LocationType, 4110, default);
    }

    private async Task ApproveViaErpAsync(long sessionId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.StockCountLines
            .Where(line => line.StockCountId == sessionId && line.PhysicalStock == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(line => line.PhysicalStock, line => line.CurrentStock)
                .SetProperty(line => line.Difference, 0m));
        var session = await db.StockCounts.AsNoTracking().SingleAsync(count => count.Id == sessionId);
        await scope.ServiceProvider.GetRequiredService<IStockCountApprovalStore>().ApproveAsync(
            session.Id, session.BusinessId, session.LocationId, session.LocationType, 4110, default);
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

    private async Task<string> SessionFingerprintAsync(long sessionId)
    {
        await using var scope = _fixture.CreateServiceScope();
        var session = await scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>()
            .StockCounts.AsNoTracking().SingleAsync(count => count.Id == sessionId);
        return $"{session.Status}|{session.CountRevision}|{session.MovementReconciliationStatus}|{session.ApprovedAt}|{session.SubmittedAt}";
    }

    private static async Task<long> CreateSessionAsync(HttpClient client, long outletId)
    {
        using var response = await client.PostAsJsonAsync(Sessions, new
        {
            outletId,
            stockCountDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            remarks = "Review test"
        });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
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
        using var response = await client.PostAsJsonAsync($"{Sessions}/{sessionId}/operations:batch", new
        {
            deviceSessionId = "review-test-device",
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
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await ReadAsync(response)).GetProperty("data").GetProperty("currentRevision").GetInt64();
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, long sessionId, long revision)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"{Sessions}/{sessionId}/submissions")
        {
            Content = JsonContent.Create(new { acceptedRevision = revision })
        };
        request.Headers.Add("Idempotency-Key", NewKey());
        return client.SendAsync(request);
    }

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path}: {response.StatusCode} {text}");
        return (await ReadJsonAsync(text)).GetProperty("data");
    }

    private static Task<JsonElement> ReadJsonAsync(string text)
    {
        using var json = JsonDocument.Parse(text);
        return Task.FromResult(json.RootElement.Clone());
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await ReadJsonAsync(await response.Content.ReadAsStringAsync());
}
