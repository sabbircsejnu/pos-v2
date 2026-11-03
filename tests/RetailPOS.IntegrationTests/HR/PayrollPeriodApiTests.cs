using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;
using Swashbuckle.AspNetCore.Swagger;

namespace RetailPOS.IntegrationTests.HR;

public sealed class PayrollPeriodApiTests
{
    [Fact]
    public async Task PeriodEndpointsEnforcePermissionsAndBusinessIsolation()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        Authorize(client, "");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/payroll/v1/periods")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            "/api/payroll/v1/periods", Period("2026-10", new(2026, 10, 1), new(2026, 10, 31)))).StatusCode);

        SetPermissions(client, "payroll.period.view,payroll.period.create");
        var createdResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/periods", Period("2026-10", new(2026, 10, 1), new(2026, 10, 31)));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var period = (await createdResponse.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!;

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            "/api/payroll/v1/periods", Period("INVALID", new(2026, 11, 1), new(2026, 10, 31)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            "/api/payroll/v1/periods", Period("OVERLAP", new(2026, 10, 31), new(2026, 11, 30)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/start-processing", new { expectedVersion = 1 })).StatusCode);

        SetPermissions(client, "payroll.period.view,payroll.run.create");
        var started = await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/start-processing", new { expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var replay = await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/start-processing", new { expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!.IsReplay);

        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/payroll/v1/periods/{period.Id}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Single(await db.PayrollPeriods.Where(row => row.BusinessId == 1).ToListAsync());
        Assert.Empty(await db.PayrollPeriods.Where(row => row.BusinessId == 2).ToListAsync());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PeriodEndpointsRequireHrAndPayrollCapabilities(bool hrEnabled, bool payrollEnabled)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, hrEnabled, payrollEnabled);
        Authorize(client, "payroll.period.view,payroll.period.create");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/payroll/v1/periods")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            "/api/payroll/v1/periods", Period("2026-10", new(2026, 10, 1), new(2026, 10, 31)))).StatusCode);
    }

    [Fact]
    public void PeriodEndpointsArePublishedInOpenApi()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        var paths = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1").Paths.Keys;

        Assert.Contains("/api/payroll/v1/periods", paths);
        Assert.Contains("/api/payroll/v1/periods/{id}", paths);
        Assert.Contains("/api/payroll/v1/periods/{id}/start-processing", paths);
        Assert.Contains("/api/payroll/v1/periods/{id}/lock", paths);
        Assert.Contains("/api/payroll/v1/periods/{id}/reopen", paths);
        Assert.Contains("/api/payroll/v1/periods/{id}/return-to-open", paths);
        Assert.Contains("/api/payroll/v1/periods/{id}/close", paths);
    }

    [Fact]
    public async Task UnusedProcessingPeriodCanReturnToOpenWithReasonAndAudit()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        Authorize(client, "payroll.period.create,payroll.run.create,payroll.period.reopen");
        var create = await client.PostAsJsonAsync(
            "/api/payroll/v1/periods", Period("2026-12", new(2026, 12, 1), new(2026, 12, 31)));
        var period = (await create.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!;
        var start = await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/start-processing",
            new { expectedVersion = period.Version });
        var processing = (await start.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!;
        var request = new ReopenPayrollPeriodRequest(processing.Version, "No payroll run was needed");

        factory.RoleAuditFailureInterceptor.FailAuditActionType =
            "payroll.period.processing_returned_to_open";
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/return-to-open", request)).StatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        using (var rollbackScope = factory.Services.CreateScope())
        {
            var rollbackDb = rollbackScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var unchanged = await rollbackDb.PayrollPeriods.SingleAsync(row => row.Id == period.Id);
            Assert.Equal(PayrollPeriodStatuses.Processing, unchanged.Status);
            Assert.Equal(processing.Version, unchanged.Version);
        }

        var returned = await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/return-to-open", request);
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        var reopened = (await returned.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!;
        Assert.Equal(PayrollPeriodStatuses.Open, reopened.Status);
        Assert.Equal(processing.Version + 1, reopened.Version);
        Assert.Equal(request.Reason, reopened.ReopenReason);
        var replay = await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/return-to-open", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!.IsReplay);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Single(await verifyDb.AuditEvents.Where(row =>
            row.ActionType == "payroll.period.processing_returned_to_open").ToArrayAsync());
    }

    [Fact]
    public async Task LockAndReopenRequireSubmissionReasonPermissionAndCurrentVersion()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        Authorize(client, "payroll.period.create,payroll.run.create");

        var created = await client.PostAsJsonAsync(
            "/api/payroll/v1/periods", Period("2026-11", new(2026, 11, 1), new(2026, 11, 30)));
        var period = (await created.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!;
        var started = await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/start-processing",
            new { expectedVersion = period.Version });
        var processing = (await started.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.PayrollRuns.Add(new PayrollRun
            {
                BusinessId = 1,
                PayrollPeriodId = period.Id,
                RunNo = 1,
                RunType = PayrollRunTypes.Regular,
                ScopeType = PayrollRunScopeTypes.Business,
                ScopeKey = PayrollRunScopeTypes.BusinessKey,
                Status = PayrollRunStatuses.PendingApproval,
                Version = 2,
                CalculationVersion = 1,
                CalculationInputHash = new string('A', 64),
                ApprovalEvidenceHash = new string('B', 64),
                IdempotencyKey = "pending-lock-test",
                IdempotencyPayloadHash = new string('C', 64),
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        SetPermissions(client, "payroll.period.reopen");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/return-to-open",
            new ReopenPayrollPeriodRequest(processing.Version, "A run already exists"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/lock",
            new PayrollPeriodLifecycleRequest(processing.Version))).StatusCode);
        SetPermissions(client, "payroll.period.lock,payroll.period.reopen");
        var lockResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/lock",
            new PayrollPeriodLifecycleRequest(processing.Version));
        Assert.True(lockResponse.StatusCode == HttpStatusCode.OK,
            $"Lock failed with {(int)lockResponse.StatusCode}: {await lockResponse.Content.ReadAsStringAsync()}");
        var locked = (await lockResponse.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!;
        Assert.Equal(PayrollPeriodStatuses.Locked, locked.Status);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/reopen",
            new ReopenPayrollPeriodRequest(locked.Version, " "))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/reopen",
            new ReopenPayrollPeriodRequest(locked.Version, "Correct attendance"))).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.PayrollRuns.Where(item => item.PayrollPeriodId == period.Id)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(item => item.Status, PayrollRunStatuses.Approved));
        }

        var stale = await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/reopen",
            new ReopenPayrollPeriodRequest(locked.Version - 1, "Correct attendance"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var reopenedResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/reopen",
            new ReopenPayrollPeriodRequest(locked.Version, "Correct attendance"));
        Assert.True(reopenedResponse.StatusCode == HttpStatusCode.OK,
            $"Reopen failed with {(int)reopenedResponse.StatusCode}: {await reopenedResponse.Content.ReadAsStringAsync()}");
        var reopened = (await reopenedResponse.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!;
        Assert.Equal(PayrollPeriodStatuses.Processing, reopened.Status);
        Assert.Equal("Correct attendance", reopened.ReopenReason);
        Assert.Equal(locked.Version + 1, reopened.Version);
    }

    [Fact]
    public async Task ClosePersistsAssessmentAndIsIdempotentWhenAllStrictGatesPass()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedCloseablePeriodAsync(factory.Services, accountingConfigured: true);
        Authorize(client, "payroll.period.close");
        var request = CloseRequest("final-close");

        factory.RoleAuditFailureInterceptor.FailAuditActionType = "payroll.period.closed";
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await client.PostAsJsonAsync("/api/payroll/v1/periods/1/close", request)).StatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        using (var rollbackScope = factory.Services.CreateScope())
        {
            var rollbackDb = rollbackScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Equal(PayrollPeriodStatuses.Approved,
                (await rollbackDb.PayrollPeriods.SingleAsync(row => row.Id == 1)).Status);
            Assert.Empty(await rollbackDb.PayrollPeriodCloseAssessments.ToArrayAsync());
        }

        var response = await client.PostAsJsonAsync("/api/payroll/v1/periods/1/close", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Close failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var closed = (await response.Content.ReadFromJsonAsync<PayrollPeriodCloseResponse>())!;
        Assert.True(closed.Closed);
        Assert.Equal(PayrollPeriodStatuses.Closed, closed.Status);
        Assert.All(closed.Gates, gate => Assert.True(gate.Passed, gate.Code));

        var replayResponse = await client.PostAsJsonAsync("/api/payroll/v1/periods/1/close", request);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<PayrollPeriodCloseResponse>())!;
        Assert.True(replay.IsReplay);
        Assert.Equal(closed.AssessmentId, replay.AssessmentId);

        var conflict = await client.PostAsJsonAsync(
            "/api/payroll/v1/periods/1/close", request with { Reason = "Different content" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(PayrollPeriodStatuses.Closed,
            (await db.PayrollPeriods.SingleAsync(row => row.Id == 1)).Status);
        Assert.Single(await db.PayrollPeriodCloseAssessments.ToArrayAsync());
        var persistedAssessment = await db.PayrollPeriodCloseAssessments.SingleAsync();
        Assert.Equal(1L, persistedAssessment.BusinessId);
        Assert.Equal(1L, persistedAssessment.PayrollPeriodId);
        Assert.Equal(3, persistedAssessment.PeriodVersion);
        Assert.Equal(PayrollPeriodStatuses.Closed, persistedAssessment.StatusSnapshot);
        Assert.Equal(1, persistedAssessment.PolicyVersion);
        Assert.NotEmpty(persistedAssessment.RunRevisionRefsJson);
        Assert.Equal("[]", persistedAssessment.PaymentStateVersionsJson);
        Assert.Equal("[]", persistedAssessment.PostingStateVersionsJson);
        Assert.Equal("Payroll finalized", persistedAssessment.Reason);
        Assert.Equal(101L, persistedAssessment.ActorUserId);
        Assert.NotNull(persistedAssessment.ClosedAtUtc);
        Assert.Equal(persistedAssessment.AssessedAtUtc, persistedAssessment.ClosedAtUtc);
        Assert.Single(await db.AuditEvents.Where(row => row.ActionType == "payroll.period.closed")
            .ToArrayAsync());
    }

    [Fact]
    public async Task PeriodTransitionsRollbackWhenRequiredAuditCannotBePersisted()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        Authorize(client, "payroll.period.create,payroll.run.create,payroll.period.lock,payroll.period.reopen");
        var createRequest = Period("2026-12", new(2026, 12, 1), new(2026, 12, 31));
        factory.RoleAuditFailureInterceptor.FailAuditActionType = "payroll.period.created";
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await client.PostAsJsonAsync("/api/payroll/v1/periods", createRequest)).StatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.PayrollPeriods.ToArrayAsync());
        }

        var created = await client.PostAsJsonAsync("/api/payroll/v1/periods", createRequest);
        var period = (await created.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!;
        factory.RoleAuditFailureInterceptor.FailAuditActionType = "payroll.period.processing_started";
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/start-processing",
            new { expectedVersion = period.Version })).StatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var unchanged = await db.PayrollPeriods.SingleAsync(row => row.Id == period.Id);
            Assert.Equal(PayrollPeriodStatuses.Open, unchanged.Status);
            Assert.Equal(period.Version, unchanged.Version);
        }

        var started = await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/start-processing",
            new { expectedVersion = period.Version });
        var processing = (await started.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.PayrollRuns.Add(new PayrollRun
            {
                BusinessId = 1,
                PayrollPeriodId = period.Id,
                RunNo = 1,
                RunType = PayrollRunTypes.Regular,
                ScopeType = PayrollRunScopeTypes.Business,
                ScopeKey = PayrollRunScopeTypes.BusinessKey,
                Status = PayrollRunStatuses.PendingApproval,
                Version = 1,
                CalculationVersion = 1,
                CalculationInputHash = new string('A', 64),
                ApprovalEvidenceHash = new string('B', 64),
                IdempotencyKey = "period-audit-rollback",
                IdempotencyPayloadHash = new string('C', 64),
                CreatedByUserId = 101,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        factory.RoleAuditFailureInterceptor.FailAuditActionType = "payroll.period.locked";
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/lock",
            new PayrollPeriodLifecycleRequest(processing.Version))).StatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var unchanged = await db.PayrollPeriods.SingleAsync(row => row.Id == period.Id);
            Assert.Equal(PayrollPeriodStatuses.Processing, unchanged.Status);
            Assert.Equal(processing.Version, unchanged.Version);
        }

        var lockedResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/lock",
            new PayrollPeriodLifecycleRequest(processing.Version));
        var locked = (await lockedResponse.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.PayrollRuns.Where(row => row.PayrollPeriodId == period.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(
                    row => row.Status, PayrollRunStatuses.Approved));
        }

        factory.RoleAuditFailureInterceptor.FailAuditActionType = "payroll.period.reopened";
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/{period.Id}/reopen",
            new ReopenPayrollPeriodRequest(locked.Version, "Audit rollback"))).StatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var stillLocked = await verifyDb.PayrollPeriods.SingleAsync(row => row.Id == period.Id);
        Assert.Equal(PayrollPeriodStatuses.Locked, stillLocked.Status);
        Assert.Equal(locked.Version, stillLocked.Version);
        Assert.Null(stillLocked.ReopenedAtUtc);
        Assert.Null(stillLocked.ReopenReason);
    }

    [Fact]
    public async Task CloseFailsClosedAndPersistsStructuredBlockersWhenAccountingStateIsUnknown()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedCloseablePeriodAsync(factory.Services, accountingConfigured: false);
        Authorize(client, "payroll.period.close");

        var response = await client.PostAsJsonAsync(
            "/api/payroll/v1/periods/1/close", CloseRequest("blocked-close"));
        Assert.True(response.StatusCode == HttpStatusCode.Conflict,
            $"Close assessment failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var assessment = (await response.Content.ReadFromJsonAsync<PayrollPeriodCloseResponse>())!;
        Assert.False(assessment.Closed);
        Assert.Contains(assessment.Gates, gate =>
            gate.Code == "accounting_configuration" && !gate.Passed);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(PayrollPeriodStatuses.Approved,
            (await db.PayrollPeriods.SingleAsync(row => row.Id == 1)).Status);
        Assert.Single(await db.PayrollPeriodCloseAssessments.ToArrayAsync());
        Assert.Single(await db.AuditEvents.Where(row => row.ActionType == "payroll.period.close_blocked")
            .ToArrayAsync());
        Assert.Null((await db.PayrollPeriodCloseAssessments.SingleAsync()).ClosedAtUtc);
    }

    [Fact]
    public async Task CloseRemainsBlockedWhenAccountingIsEnabledWithoutPhaseSixPostingEvidence()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedCloseablePeriodAsync(factory.Services, accountingConfigured: true);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.BusinessFeatureSettings
                .Where(setting => setting.BusinessId == 1 && setting.FeatureKey == "accounting.enabled")
                .ExecuteUpdateAsync(update => update.SetProperty(setting => setting.IsEnabled, true));
        }
        Authorize(client, "payroll.period.close");

        var response = await client.PostAsJsonAsync(
            "/api/payroll/v1/periods/1/close", CloseRequest("accounting-enabled-close"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var assessment = (await response.Content.ReadFromJsonAsync<PayrollPeriodCloseResponse>())!;
        Assert.False(assessment.Closed);
        Assert.Contains(assessment.Gates, gate =>
            gate.Code == "accounting_posting" && !gate.Passed && gate.Blocking);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(PayrollPeriodStatuses.Approved,
            (await verifyDb.PayrollPeriods.SingleAsync(row => row.Id == 1)).Status);
        Assert.Null((await verifyDb.PayrollPeriodCloseAssessments.SingleAsync()).ClosedAtUtc);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Processing")]
    [InlineData("Failed")]
    [InlineData("Conflict")]
    public async Task AccountingExpensePostingStatesIndependentlyBlockClose(string status)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedCompleteCloseablePayrollAsync(factory.Services);
        await UpdatePostingAsync(factory.Services, PayrollAccountingPostingTypes.PayrollExpense, status);
        await AssertCloseBlockedAsync(factory, "accounting_posting");
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Processing")]
    [InlineData("Failed")]
    [InlineData("Conflict")]
    public async Task AccountingSettlementPostingStatesIndependentlyBlockClose(string status)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedCompleteCloseablePayrollAsync(factory.Services);
        await UpdatePostingAsync(factory.Services, PayrollAccountingPostingTypes.SalaryPayment, status);
        await AssertCloseBlockedAsync(factory, "accounting_settlement_posting");
    }

    [Fact]
    public async Task MissingExpensePostingBlocksClose()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedCompleteCloseablePayrollAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.PayrollAccountingPostings
                .Where(row => row.PostingType == PayrollAccountingPostingTypes.PayrollExpense)
                .ExecuteDeleteAsync();
        }

        await AssertCloseBlockedAsync(factory, "accounting_posting");
    }

    [Fact]
    public async Task MissingRequiredSettlementPostingBlocksClose()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedCompleteCloseablePayrollAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.PayrollAccountingPostings
                .Where(row => row.PostingType == PayrollAccountingPostingTypes.SalaryPayment)
                .ExecuteDeleteAsync();
        }

        await AssertCloseBlockedAsync(factory, "accounting_settlement_posting");
    }

    [Fact]
    public async Task UncertainAccountingPostingAndPayloadConflictCannotBeBypassedByExceptionPolicy()
    {
        foreach (var status in new[]
                 {
                     PayrollAccountingPostingStatuses.Processing,
                     PayrollAccountingPostingStatuses.Conflict
                 })
        {
            var policy = new TestCloseExceptionPolicy(1, ["held_payment_exception"]);
            using var factory = new SqliteRoleGovernanceApiFactory
            {
                PayrollPeriodCloseExceptionPolicy = policy
            };
            await SeedCompleteCloseablePayrollAsync(factory.Services);
            await UpdatePostingAsync(factory.Services, PayrollAccountingPostingTypes.PayrollExpense, status);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                await db.PayrollPayments.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.Status, PayrollPaymentStatuses.Held));
            }
            await AssertCloseBlockedAsync(factory, "accounting_posting",
                [Exception("held_payment_exception", 1)]);
        }
    }

    [Theory]
    [InlineData("Processing", "None", "processing_payment")]
    [InlineData("Failed", "PendingInvestigation", "failed_returned_payment_resolution")]
    [InlineData("Returned", "PendingInvestigation", "failed_returned_payment_resolution")]
    [InlineData("Held", "None", "held_payment_exception")]
    [InlineData("PartiallyPaid", "None", "unpaid_partial_liability")]
    public async Task PaymentStatesIndependentlyBlockClose(
        string status, string resolutionStatus, string expectedGate)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedCompleteCloseablePayrollAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.PayrollPayments.ExecuteUpdateAsync(update => update
                .SetProperty(row => row.Status, status)
                .SetProperty(row => row.ResolutionStatus, resolutionStatus)
                .SetProperty(row => row.PaidAmount, status == "PartiallyPaid" ? 500m : 1000m)
                .SetProperty(row => row.OutstandingAmount, status == "PartiallyPaid" ? 500m : 0m));
            await db.PayrollPaymentAllocations.ExecuteUpdateAsync(update => update
                .SetProperty(row => row.SettledAmount, status == "PartiallyPaid" ? 500m : 1000m)
                .SetProperty(row => row.State, status == "PartiallyPaid"
                    ? PayrollPaymentAllocationStates.PartiallySettled
                    : PayrollPaymentAllocationStates.Settled));
        }

        await AssertCloseBlockedAsync(factory, expectedGate);
    }

    [Theory]
    [InlineData("Unreconciled")]
    [InlineData("PartiallyReconciled")]
    [InlineData("Exception")]
    public async Task ReconciliationStatesIndependentlyBlockClose(string status)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedCompleteCloseablePayrollAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.PayrollPaymentReconciliations.ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.Status, status));
        }

        await AssertCloseBlockedAsync(factory, "payment_reconciliation");
    }

    [Fact]
    public async Task UnresolvedCorrectionAndPendingApprovalBlockClose()
    {
        using (var factory = new SqliteRoleGovernanceApiFactory())
        {
            await SeedCompleteCloseablePayrollAsync(factory.Services);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                var original = await db.PayrollRuns.SingleAsync();
                db.PayrollRuns.Add(new PayrollRun
                {
                    BusinessId = 1,
                    PayrollPeriodId = 1,
                    RunNo = 2,
                    RunType = PayrollRunTypes.Correction,
                    OriginalPayrollRunId = original.Id,
                    OriginalCalculationVersion = original.CalculationVersion,
                    Status = PayrollRunStatuses.Approved,
                    CalculationVersion = 1,
                    CalculationInputHash = new string('D', 64),
                    ApprovalEvidenceHash = new string('E', 64),
                    IdempotencyKey = "unresolved-correction",
                    IdempotencyPayloadHash = new string('F', 64),
                    CreatedByUserId = 101,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
                db.PayrollAccountingPostings.Add(new PayrollAccountingPosting
                {
                    BusinessId = 1,
                    PayrollPeriodId = 1,
                    PayrollRunId = original.Id + 1,
                    RunRevision = 1,
                    PostingType = PayrollAccountingPostingTypes.PayrollExpense,
                    PostingReference = "CORRECTION",
                    IdempotencyKey = "correction-posting",
                    RequestPayloadHash = new string('1', 64),
                    AccountingJournalId = 901,
                    Status = PayrollAccountingPostingStatuses.Posted,
                    RequestedByUserId = 101,
                    RequestedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            await AssertCloseBlockedAsync(factory, "unresolved_correction");
        }

        using (var factory = new SqliteRoleGovernanceApiFactory())
        {
            await SeedCompleteCloseablePayrollAsync(factory.Services);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                await db.PayrollRuns.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.Status, PayrollRunStatuses.PendingApproval));
            }

            await AssertCloseBlockedAsync(factory, "no_pending_work");
        }
    }

    [Fact]
    public async Task MissingFrozenCalculationArtifactBlocksFinalization()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedCompleteCloseablePayrollAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.PayrollEmployeeResults.ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.CalculationSnapshotHash, new string('0', 64)));
        }

        await AssertCloseBlockedAsync(factory, "calculation_snapshots");
    }

    [Fact]
    public async Task FullyResolvedAccountingEnabledPeriodClosesWithAudit()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedCompleteCloseablePayrollAsync(factory.Services);
        Authorize(client, "payroll.period.close");

        var response = await client.PostAsJsonAsync(
            "/api/payroll/v1/periods/1/close", CloseRequest("fully-resolved-close"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<PayrollPeriodCloseResponse>())!.Closed);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(PayrollPeriodStatuses.Closed,
            (await db.PayrollPeriods.SingleAsync(row => row.Id == 1)).Status);
        Assert.Single(await db.AuditEvents.Where(row => row.ActionType == "payroll.period.closed")
            .ToArrayAsync());
    }

    [Fact]
    public async Task AccountingDisabledSkipsOnlyPostingGatesAndDoesNotForceClose()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedCompleteCloseablePayrollAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.BusinessFeatureSettings.Where(row => row.FeatureKey == "accounting.enabled")
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsEnabled, false));
            await db.PayrollAccountingPostings.ExecuteDeleteAsync();
            await db.PayrollPayments.ExecuteUpdateAsync(update => update
                .SetProperty(row => row.Status, PayrollPaymentStatuses.Held)
                .SetProperty(row => row.PaidAmount, 0m)
                .SetProperty(row => row.OutstandingAmount, 1000m));
            await db.PayrollPaymentAllocations.ExecuteUpdateAsync(update => update
                .SetProperty(row => row.SettledAmount, 0m)
                .SetProperty(row => row.State, PayrollPaymentAllocationStates.Pending));
            await db.PayrollPaymentReconciliations.ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.Status,
                    PayrollPaymentReconciliationStatuses.Unreconciled));
        }

        using var client = factory.CreateClient();
        Authorize(client, "payroll.period.close");
        var response = await client.PostAsJsonAsync(
            "/api/payroll/v1/periods/1/close", CloseRequest("accounting-disabled-not-forced"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var assessment = (await response.Content.ReadFromJsonAsync<PayrollPeriodCloseResponse>())!;
        Assert.Contains(assessment.Gates, gate => gate.Code == "accounting_posting" && gate.Passed);
        Assert.Contains(assessment.Gates, gate => gate.Code == "accounting_settlement_posting" && gate.Passed);
        Assert.Contains(assessment.Gates, gate => gate.Code == "held_payment_exception" && !gate.Passed);
        Assert.Contains(assessment.Gates, gate => gate.Code == "payment_reconciliation" && !gate.Passed);
        Assert.Contains(assessment.Gates, gate => gate.Code == "unpaid_partial_liability" && !gate.Passed);
    }

    [Theory]
    [InlineData("Processing", "None", "processing_payment")]
    [InlineData("Failed", "PendingInvestigation", "failed_returned_payment_resolution")]
    [InlineData("Returned", "PendingInvestigation", "failed_returned_payment_resolution")]
    [InlineData("Held", "None", "held_payment_exception")]
    [InlineData("PartiallyPaid", "None", "unpaid_partial_liability")]
    public async Task AccountingDisabledStillEnforcesPaymentGates(
        string status, string resolutionStatus, string expectedGate)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedCompleteCloseablePayrollAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.BusinessFeatureSettings.Where(row => row.FeatureKey == "accounting.enabled")
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsEnabled, false));
            await db.PayrollAccountingPostings.ExecuteDeleteAsync();
            await db.PayrollPayments.ExecuteUpdateAsync(update => update
                .SetProperty(row => row.Status, status)
                .SetProperty(row => row.ResolutionStatus, resolutionStatus)
                .SetProperty(row => row.PaidAmount, status == "PartiallyPaid" ? 500m : 1000m)
                .SetProperty(row => row.OutstandingAmount, status == "PartiallyPaid" ? 500m : 0m));
        }

        using var client = factory.CreateClient();
        Authorize(client, "payroll.period.close");
        var response = await client.PostAsJsonAsync(
            "/api/payroll/v1/periods/1/close",
            CloseRequest($"accounting-disabled-{expectedGate}-{Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var assessment = (await response.Content.ReadFromJsonAsync<PayrollPeriodCloseResponse>())!;
        Assert.Contains(assessment.Gates, gate => gate.Code == "accounting_posting" && gate.Passed);
        Assert.Contains(assessment.Gates,
            gate => gate.Code == "accounting_settlement_posting" && gate.Passed);
        Assert.Contains(assessment.Gates, gate => gate.Code == expectedGate && !gate.Passed);
    }

    [Fact]
    public async Task TestOnlyExceptionPolicyRequiresPermissionReasonReferenceAndPersistsEvidence()
    {
        using var factory = new SqliteRoleGovernanceApiFactory
        {
            PayrollPeriodCloseExceptionPolicy =
                new TestCloseExceptionPolicy(1, ["held_payment_exception"])
        };
        using var client = factory.CreateClient();
        await SeedCompleteCloseablePayrollAsync(factory.Services, netPay: 0m, heldPayment: true);
        var exception = Exception("held_payment_exception", 1);
        var request = new ClosePayrollPeriodRequest(
            "test-policy-close", "Approved isolated exception", 3, 1, [exception]);

        Authorize(client, "payroll.period.close");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/payroll/v1/periods/1/close", request)).StatusCode);

        SetPermissions(client, "payroll.period.close,payroll.period.close_with_exception");
        var response = await client.PostAsJsonAsync("/api/payroll/v1/periods/1/close", request);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Exception close failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var close = (await response.Content.ReadFromJsonAsync<PayrollPeriodCloseResponse>())!;
        Assert.True(close.Closed);
        Assert.Contains(close.Gates, gate => gate.Code == "held_payment_exception" && gate.Passed);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var evidence = (await db.PayrollPeriodCloseAssessments.SingleAsync()).ExceptionsJson;
        Assert.Contains("\"policyVersion\":1", evidence);
        Assert.Contains("held_payment_exception", evidence);
        var auditEvent = await db.AuditEvents.SingleAsync(row =>
            row.ActionType == "payroll.period.closed_with_exception");
        var genericAuditPayload = $"{auditEvent.ActionSummary} {auditEvent.Metadata}";
        Assert.DoesNotContain("1000", genericAuditPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST-001", genericAuditPayload, StringComparison.Ordinal);
        Assert.DoesNotContain("1000", evidence);
        Assert.DoesNotContain("account", evidence, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProductionDenyByDefaultPolicyCannotCloseWithException()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        await SeedCompleteCloseablePayrollAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            await db.PayrollPayments.ExecuteUpdateAsync(update =>
                update.SetProperty(row => row.Status, PayrollPaymentStatuses.Held));
        }
        await AssertCloseBlockedAsync(factory, "held_payment_exception",
            [Exception("held_payment_exception", 1)]);
    }

    [Theory]
    [InlineData("", "TEST-APPROVAL-1")]
    [InlineData("Reason required", "")]
    public async Task TestOnlyExceptionRequiresReasonAndApprovalReference(
        string reason, string approvalReference)
    {
        using var factory = new SqliteRoleGovernanceApiFactory
        {
            PayrollPeriodCloseExceptionPolicy =
                new TestCloseExceptionPolicy(1, ["held_payment_exception"])
        };
        await SeedCompleteCloseablePayrollAsync(factory.Services, netPay: 0m, heldPayment: true);
        using var client = factory.CreateClient();
        Authorize(client, "payroll.period.close,payroll.period.close_with_exception");
        var response = await client.PostAsJsonAsync("/api/payroll/v1/periods/1/close",
            new ClosePayrollPeriodRequest("invalid-test-exception", "Invalid exception", 3, 1,
            [
                new PayrollPeriodCloseExceptionRequest(
                    "held_payment_exception", "1", reason, approvalReference)
            ]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(PayrollPeriodStatuses.Approved,
            (await db.PayrollPeriods.SingleAsync(row => row.Id == 1)).Status);
    }

    [Fact]
    public async Task ExceptionForHeldDoesNotBypassUnpaidLiabilityAndViceVersa()
    {
        using (var factory = new SqliteRoleGovernanceApiFactory
               {
                   PayrollPeriodCloseExceptionPolicy =
                       new TestCloseExceptionPolicy(1, ["held_payment_exception"])
               })
        {
            await SeedCompleteCloseablePayrollAsync(factory.Services);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                await db.PayrollPayments.ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.Status, PayrollPaymentStatuses.Held)
                    .SetProperty(row => row.PaidAmount, 0m)
                    .SetProperty(row => row.OutstandingAmount, 1000m));
            }

            await AssertCloseBlockedAsync(factory, "unpaid_partial_liability",
                [Exception("held_payment_exception", 1)]);
        }

        using (var factory = new SqliteRoleGovernanceApiFactory
               {
                   PayrollPeriodCloseExceptionPolicy =
                       new TestCloseExceptionPolicy(1, ["unpaid_partial_liability"])
               })
        {
            await SeedCompleteCloseablePayrollAsync(factory.Services);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
                await db.PayrollPayments.ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.Status, PayrollPaymentStatuses.Held)
                    .SetProperty(row => row.PaidAmount, 0m)
                    .SetProperty(row => row.OutstandingAmount, 1000m));
            }

            await AssertCloseBlockedAsync(factory, "held_payment_exception",
                [Exception("unpaid_partial_liability", 1)]);
        }
    }

    [Theory]
    [InlineData("uncertain-accounting", "accounting_posting")]
    [InlineData("conflicting-accounting", "accounting_posting")]
    [InlineData("unknown-settlement-truth", "processing_payment")]
    [InlineData("unresolved-correction", "unresolved_correction")]
    public async Task TestOnlyExceptionCannotBypassNonExceptionEligibleGates(
        string scenario, string gateCode)
    {
        using var factory = new SqliteRoleGovernanceApiFactory
        {
            PayrollPeriodCloseExceptionPolicy =
                new TestCloseExceptionPolicy(1, [gateCode])
        };
        await SeedCompleteCloseablePayrollAsync(factory.Services);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            switch (scenario)
            {
                case "uncertain-accounting":
                    await UpdatePostingAsync(
                        factory.Services, PayrollAccountingPostingTypes.PayrollExpense,
                        PayrollAccountingPostingStatuses.Processing);
                    break;
                case "conflicting-accounting":
                    await UpdatePostingAsync(
                        factory.Services, PayrollAccountingPostingTypes.PayrollExpense,
                        PayrollAccountingPostingStatuses.Conflict);
                    break;
                case "unknown-settlement-truth":
                    await db.PayrollPayments.ExecuteUpdateAsync(update =>
                        update.SetProperty(row => row.Status, PayrollPaymentStatuses.Processing));
                    break;
                case "unresolved-correction":
                {
                    var original = await db.PayrollRuns.SingleAsync();
                    db.PayrollRuns.Add(new PayrollRun
                    {
                        BusinessId = 1,
                        PayrollPeriodId = 1,
                        RunNo = 2,
                        RunType = PayrollRunTypes.Correction,
                        OriginalPayrollRunId = original.Id,
                        OriginalCalculationVersion = original.CalculationVersion,
                        Status = PayrollRunStatuses.Approved,
                        CalculationVersion = 1,
                        CalculationInputHash = new string('D', 64),
                        ApprovalEvidenceHash = new string('E', 64),
                        IdempotencyKey = "nonbypassable-unresolved-correction",
                        IdempotencyPayloadHash = new string('F', 64),
                        CreatedByUserId = 101,
                        CreatedAtUtc = DateTime.UtcNow
                    });
                    await db.SaveChangesAsync();
                    break;
                }
                default:
                    throw new InvalidOperationException(
                        $"Unsupported non-bypassable gate scenario: {scenario}.");
            }
        }

        await AssertCloseBlockedAsync(factory, gateCode, [Exception(gateCode, 1)]);
    }

    [Fact]
    public async Task ExceptionAuditFailureRollsBackCloseAndExceptionEvidence()
    {
        using var factory = new SqliteRoleGovernanceApiFactory
        {
            PayrollPeriodCloseExceptionPolicy =
                new TestCloseExceptionPolicy(1, ["held_payment_exception"])
        };
        using var client = factory.CreateClient();
        await SeedCompleteCloseablePayrollAsync(factory.Services, netPay: 0m, heldPayment: true);
        Authorize(client, "payroll.period.close,payroll.period.close_with_exception");
        factory.RoleAuditFailureInterceptor.FailAuditActionType =
            "payroll.period.closed_with_exception";

        var response = await client.PostAsJsonAsync("/api/payroll/v1/periods/1/close",
            new ClosePayrollPeriodRequest("audit-failure-exception",
                "Audit failure rollback", 3, 1, [Exception("held_payment_exception", 1)]));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(PayrollPeriodStatuses.Approved,
            (await db.PayrollPeriods.SingleAsync(row => row.Id == 1)).Status);
        Assert.Empty(await db.PayrollPeriodCloseAssessments.ToArrayAsync());
        Assert.Empty(await db.AuditEvents.Where(row =>
            row.ActionType == "payroll.period.closed_with_exception").ToArrayAsync());
    }

    private static CreatePayrollPeriodRequest Period(string code, DateOnly start, DateOnly end) =>
        new(code, start, end, null);

    private static PayrollPeriodCloseExceptionRequest Exception(string gateCode, long periodId) =>
        new(gateCode, periodId.ToString(), "Approved test exception", "TEST-APPROVAL-1");

    private static async Task AssertCloseBlockedAsync(
        SqliteRoleGovernanceApiFactory factory,
        string expectedGate,
        IReadOnlyList<PayrollPeriodCloseExceptionRequest>? exceptions = null)
    {
        using var client = factory.CreateClient();
        Authorize(client, exceptions is null
            ? "payroll.period.close"
            : "payroll.period.close,payroll.period.close_with_exception");
        var response = await client.PostAsJsonAsync("/api/payroll/v1/periods/1/close",
            new ClosePayrollPeriodRequest(
                $"blocked-{expectedGate}-{Guid.NewGuid():N}", "Acceptance gate test", 3, 1, exceptions));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var assessment = (await response.Content.ReadFromJsonAsync<PayrollPeriodCloseResponse>())!;
        Assert.False(assessment.Closed);
        Assert.Contains(assessment.Gates, gate => gate.Code == expectedGate && !gate.Passed);
    }

    private static async Task UpdatePostingAsync(
        IServiceProvider services, string postingType, string status)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.PayrollAccountingPostings.Where(row => row.PostingType == postingType)
            .ExecuteUpdateAsync(update => update
                .SetProperty(row => row.Status, status)
                .SetProperty(row => row.AccountingJournalId,
                    status == PayrollAccountingPostingStatuses.Posted ? 901L : (long?)null));
    }

    private static async Task SeedCompleteCloseablePayrollAsync(
        IServiceProvider services, decimal netPay = 1000m, bool heldPayment = false)
    {
        await SeedCloseablePeriodAsync(services, accountingConfigured: true);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.BusinessFeatureSettings
            .Where(row => row.BusinessId == 1 && row.FeatureKey == "accounting.enabled")
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsEnabled, true));
        var now = DateTime.UtcNow;
        var snapshotJson = """{"gross":1000,"deductions":0,"net":1000}""";
        var snapshotHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));
        var requestHash = new string('2', 64);
        var componentSnapshot = """[{"code":"BASE","amount":1000}]""";

        await HrRosterPersistenceTests.Seed(db, businessExists: true);
        db.PayrollSalaryStructures.Add(new PayrollSalaryStructure
        {
            Id = 1,
            BusinessId = 1,
            Code = "TEST",
            Name = "Test salary",
            Version = 1
        });
        db.ApprovalPolicies.Add(new ApprovalPolicy
        {
            Id = 1,
            BusinessId = 1,
            Code = "TEST-SALARY",
            ProcessType = ApprovalProcessType.SalarySetup,
            Name = "Test salary approval",
            VersionNo = 1,
            ScopeType = ApprovalScopeType.Business,
            EffectiveFrom = new DateOnly(2026, 1, 1),
            IsActive = true
        });
        await db.SaveChangesAsync();
        db.ApprovalInstances.Add(new ApprovalInstance
        {
            Id = 1,
            BusinessId = 1,
            PolicyId = 1,
            PolicyCodeSnapshot = "TEST-SALARY",
            PolicyVersionSnapshot = 1,
            ProcessType = ApprovalProcessType.SalarySetup,
            TargetType = "PayrollSalarySetup",
            TargetId = "1",
            RequestedByUserId = 101,
            RequestedAtUtc = now,
            Status = ApprovalInstanceStatus.Approved,
            CorrelationId = Guid.NewGuid(),
            ScopeType = ApprovalScopeType.Business,
            TargetVersion = 1,
            RequestHash = requestHash
        });
        db.PayrollSalarySetupRequests.Add(new PayrollSalarySetupRequest
        {
            Id = 1,
            BusinessId = 1,
            EmployeeId = 1,
            SalaryStructureId = 1,
            SalaryStructureVersion = 1,
            EffectiveFrom = new DateOnly(2026, 10, 1),
            OperationalLocationProjectionId = 10,
            StructureSnapshotJson = "{}",
            ComponentSnapshotJson = componentSnapshot,
            RequestHash = requestHash,
            IdempotencyPayloadHash = requestHash,
            IdempotencyKey = "test-salary-setup",
            Reason = "Test fixture",
            Status = "Approved",
            CreatedByUserId = 101,
            CreatedAtUtc = now,
            DecidedByUserId = 202,
            DecidedAtUtc = now
        });
        await db.SaveChangesAsync();
        db.PayrollEmployeeSalaryVersions.Add(new PayrollEmployeeSalaryVersion
        {
            Id = 1,
            BusinessId = 1,
            EmployeeId = 1,
            SalarySetupRequestId = 1,
            SalaryStructureId = 1,
            SalaryStructureVersion = 1,
            EffectiveFrom = new DateOnly(2026, 10, 1),
            OperationalLocationProjectionId = 10,
            StructureSnapshotJson = "{}",
            ComponentSnapshotJson = componentSnapshot,
            ApprovalInstanceId = 1,
            ApprovedByUserId = 202,
            ApprovedAtUtc = now,
            CreatedAtUtc = now
        });
        await db.SaveChangesAsync();
        var run = await db.PayrollRuns.SingleAsync(row => row.PayrollPeriodId == 1);
        db.PayrollRunEmployeeSelections.Add(new PayrollRunEmployeeSelection
        {
            BusinessId = 1,
            PayrollRunId = run.Id,
            EmployeeId = 1,
            EmployeeCodeSnapshot = "TEST-001",
            EmployeeNameSnapshot = "Test Employee",
            LocationIdSnapshot = 10,
            EligibilityDate = new DateOnly(2026, 10, 31),
            EmploymentStatusSnapshot = "Active",
            SalaryVersionId = 1,
            SalaryEffectiveFromSnapshot = new DateOnly(2026, 10, 1),
            IsIncluded = true,
            SelectedAtUtc = now
        });
        var result = new PayrollEmployeeResult
        {
            BusinessId = 1,
            PayrollRunId = run.Id,
            CalculationVersion = 1,
            CalculationInputHash = run.CalculationInputHash!,
            CalculationSnapshotJson = snapshotJson,
            CalculationSnapshotHash = snapshotHash,
            EmployeeId = 1,
            EmployeeCodeSnapshot = "TEST-001",
            EmployeeNameSnapshot = "Test Employee",
            LocationIdSnapshot = 10,
            SalaryVersionId = 1,
            SalaryEffectiveFromSnapshot = new DateOnly(2026, 10, 1),
            GrossEarnings = netPay == 0m ? 0m : 1000m,
            TotalDeductions = netPay == 0m ? 0m : 0m,
            NetPay = netPay,
            CalculatedAtUtc = now
        };
        db.PayrollEmployeeResults.Add(result);
        await db.SaveChangesAsync();

        var payment = new PayrollPayment
        {
            BusinessId = 1,
            PayrollPeriodId = 1,
            PayrollRunId = run.Id,
            PayrollEmployeeResultId = result.Id,
            EmployeeId = 1,
            PayableAmount = netPay > 0m ? netPay : 1m,
            PaidAmount = netPay > 0m ? netPay : 1m,
            OutstandingAmount = 0m,
            CurrencyCode = "BDT",
            Status = heldPayment ? PayrollPaymentStatuses.Held : PayrollPaymentStatuses.Paid,
            Version = 1,
            IdempotencyKey = "test-payment",
            IdempotencyPayloadHash = requestHash,
            AttemptNo = 1,
            CreatedByUserId = 101,
            CreatedAtUtc = now,
            PaidAtUtc = now
        };
        db.PayrollPayments.Add(payment);
        await db.SaveChangesAsync();
        var allocation = new PayrollPaymentAllocation
        {
            BusinessId = 1,
            PayrollPaymentId = payment.Id,
            AllocationNo = 1,
            Method = PayrollPaymentMethods.Cash,
            Amount = payment.PayableAmount,
            SettledAmount = payment.PayableAmount,
            State = PayrollPaymentAllocationStates.Settled
        };
        db.PayrollPaymentAllocations.Add(allocation);
        await db.SaveChangesAsync();
        var evidence = new PayrollPaymentEvidence
        {
            BusinessId = 1,
            PayrollPaymentId = payment.Id,
            Type = PayrollPaymentEvidenceTypes.Settlement,
            Amount = payment.PayableAmount,
            EvidenceDate = new DateOnly(2026, 10, 31),
            EvidenceReference = "TEST-SETTLEMENT",
            AllocationEvidenceJson = $$"""[{"AllocationNo":1,"Method":"Cash","Amount":{{payment.PayableAmount}}}]""",
            IdempotencyKey = "test-settlement-evidence",
            PayloadHash = requestHash,
            VerifiedByUserId = 202,
            VerifiedAtUtc = now
        };
        db.PayrollPaymentEvidence.Add(evidence);
        var reconciliation = new PayrollPaymentReconciliation
        {
            BusinessId = 1,
            PayrollPaymentId = payment.Id,
            PayrollPaymentAllocationId = allocation.Id,
            Version = 1,
            PaymentVersion = 1,
            ExpectedAmount = allocation.Amount,
            SettledAmount = allocation.SettledAmount,
            Difference = 0m,
            Status = PayrollPaymentReconciliationStatuses.Reconciled,
            SettlementDate = new DateOnly(2026, 10, 31),
            EvidenceSource = PayrollPaymentMethods.Cash,
            IdempotencyKey = "test-reconciliation",
            PayloadHash = requestHash,
            ReconciledByUserId = 202,
            ReconciledAtUtc = now
        };
        db.PayrollPaymentReconciliations.Add(reconciliation);
        await db.SaveChangesAsync();

        db.PayrollAccountingPostings.AddRange(
            new PayrollAccountingPosting
            {
                BusinessId = 1,
                PayrollPeriodId = 1,
                PayrollRunId = run.Id,
                RunRevision = 1,
                PostingType = PayrollAccountingPostingTypes.PayrollExpense,
                PostingReference = "TEST-EXPENSE",
                IdempotencyKey = "test-expense-posting",
                RequestPayloadHash = requestHash,
                AccountingJournalId = 901,
                Status = PayrollAccountingPostingStatuses.Posted,
                RequestedByUserId = 101,
                RequestedAtUtc = now,
                PostedAtUtc = now
            },
            new PayrollAccountingPosting
            {
                BusinessId = 1,
                PayrollPeriodId = 1,
                PayrollRunId = run.Id,
                RunRevision = 1,
                PostingType = PayrollAccountingPostingTypes.SalaryPayment,
                PayrollPaymentId = payment.Id,
                PayrollPaymentAllocationId = allocation.Id,
                PayrollPaymentEvidenceId = evidence.Id,
                PayrollPaymentReconciliationId = reconciliation.Id,
                PostingReference = "TEST-SETTLEMENT",
                IdempotencyKey = "test-settlement-posting",
                RequestPayloadHash = requestHash,
                AccountingJournalId = 902,
                Status = PayrollAccountingPostingStatuses.Posted,
                RequestedByUserId = 101,
                RequestedAtUtc = now,
                PostedAtUtc = now
            });
        await db.SaveChangesAsync();
    }

    private static async Task SeedAsync(
        IServiceProvider services, bool hrEnabled = true, bool payrollEnabled = true)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.AddRange(new Business { Id = 1, Name = "Payroll" },
            new Business { Id = 2, Name = "Other payroll" });
        foreach (var businessId in new long[] { 1, 2 })
        {
            db.BusinessFeatureSettings.AddRange(
                new BusinessFeatureSetting
                {
                    BusinessId = businessId,
                    FeatureKey = "hr.enabled",
                    IsEnabled = businessId != 1 || hrEnabled
                },
                new BusinessFeatureSetting
                {
                    BusinessId = businessId,
                    FeatureKey = "payroll.enabled",
                    IsEnabled = businessId != 1 || payrollEnabled
                });
        }
        await db.SaveChangesAsync();
    }

    private static ClosePayrollPeriodRequest CloseRequest(string key) =>
        new(key, "Payroll finalized", 3, 1, []);

    private static async Task SeedCloseablePeriodAsync(IServiceProvider services, bool accountingConfigured)
    {
        await SeedAsync(services);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.PayrollPeriods.Add(new PayrollPeriod
        {
            Id = 1,
            BusinessId = 1,
            PeriodCode = "2026-10",
            PeriodStart = new(2026, 10, 1),
            PeriodEnd = new(2026, 10, 31),
            Status = PayrollPeriodStatuses.Approved,
            Version = 3,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = 101,
            OpenedAtUtc = DateTime.UtcNow,
            OpenedByUserId = 101,
            ApprovedAtUtc = DateTime.UtcNow,
            ApprovedByUserId = 202
        });
        db.PayrollRuns.Add(new PayrollRun
        {
            BusinessId = 1,
            PayrollPeriodId = 1,
            RunNo = 1,
            RunType = PayrollRunTypes.Regular,
            ScopeType = PayrollRunScopeTypes.Business,
            ScopeKey = PayrollRunScopeTypes.BusinessKey,
            Status = PayrollRunStatuses.Approved,
            Version = 4,
            CalculationVersion = 1,
            CalculationInputHash = new string('A', 64),
            ApprovalEvidenceHash = new string('B', 64),
            IdempotencyKey = "approved-close-test",
            IdempotencyPayloadHash = new string('C', 64),
            CreatedByUserId = 101,
            CreatedAtUtc = DateTime.UtcNow
        });
        if (accountingConfigured)
            db.BusinessFeatureSettings.Add(new BusinessFeatureSetting
            {
                BusinessId = 1,
                FeatureKey = "accounting.enabled",
                IsEnabled = false
            });
        await db.SaveChangesAsync();
    }

    private static void Authorize(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "HR");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }

    private static void SetPermissions(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Remove("X-Test-Permissions");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }

    private sealed class TestCloseExceptionPolicy(
        int version,
        IReadOnlyList<string> allowedGateCodes) : IPayrollPeriodCloseExceptionPolicy
    {
        public Task<PayrollPeriodCloseExceptionPolicySnapshot?> GetApprovedPolicyAsync(
            long businessId, CancellationToken cancellationToken) =>
            Task.FromResult<PayrollPeriodCloseExceptionPolicySnapshot?>(
                new PayrollPeriodCloseExceptionPolicySnapshot(
                    version, allowedGateCodes, RequiresApprovalReference: true));
    }
}
