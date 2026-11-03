using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using RetailPOS.API.Controllers.HR;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Identity;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed partial class PayrollRunApiTests
{
    [Fact]
    public async Task FinalSettlementApprovalRacesAreSerializedOnPostgres()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        await using var database = await PostgresTestDatabase.CreateAsync(
            GetPostgresConnectionString(), PostgresTestDatabasePrefix.PayrollAdjustment);
        await using (var migrationDb = CreatePostgresDb(database.ConnectionString))
            await migrationDb.Database.MigrateAsync();

        using var factory = new FinalSettlementPostgresApiFactory(database.ConnectionString);
        await SeedAsync(factory.Services);
        await SeedFinalSettlementApprovalPolicyAsync(factory.Services);

        using var submitClientA = factory.CreateClient();
        using var submitClientB = factory.CreateClient();
        Authorize(submitClientA, "payroll.final_settlement.submit");
        Authorize(submitClientB, "payroll.final_settlement.submit");
        var (submitSettlementId, _) = await CreateFinalSettlementAsync(factory.Services);
        var submitRoute = $"/api/hr/v1/final-settlements/{submitSettlementId}/submit";
        var submitBody = new FinalSettlementSubmitRequest(1, "pg-concurrent-submit");
        var submitResponses = await Task.WhenAll(
            submitClientA.PostAsJsonAsync(submitRoute, submitBody),
            submitClientB.PostAsJsonAsync(submitRoute, submitBody));
        var submitErrors = await Task.WhenAll(submitResponses.Select(async response =>
            response.StatusCode == HttpStatusCode.OK
                ? ""
                : $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}"));
        Assert.True(submitErrors.All(string.IsNullOrEmpty),
            string.Join(Environment.NewLine, submitErrors.Concat(factory.Logs)));
        var submitResults = await Task.WhenAll(submitResponses.Select(
            response => response.Content.ReadFromJsonAsync<ApprovalDecisionResult>()));
        Assert.All(submitResults, result => Assert.NotNull(result));
        Assert.Equal(submitResults[0]!.InstanceId, submitResults[1]!.InstanceId);
        Assert.Equal(2, submitResults.Select(result => result!.IsReplay).Distinct().Count());
        Assert.Contains(submitResults, result => result!.IsReplay);
        Assert.Contains(submitResults, result => !result!.IsReplay);
        await AssertSubmissionStateAsync(factory.Services, submitSettlementId,
            ApprovalInstanceStatus.Pending, expectedDecisionCount: 0);

        using var approveClientA = factory.CreateClient();
        using var approveClientB = factory.CreateClient();
        Authorize(approveClientA, "payroll.final_settlement.approve");
        Authorize(approveClientB, "payroll.final_settlement.approve");
        SetIdentity(approveClientA, 202, "payroll.final_settlement.approve");
        SetIdentity(approveClientB, 202, "payroll.final_settlement.approve");
        var approveRoute = $"/api/hr/v1/final-settlements/{submitSettlementId}/approve";
        var approveBody = new FinalSettlementDecisionRequest(1, "pg-concurrent-approve", "Reviewed");
        var approveResponses = await Task.WhenAll(
            approveClientA.PostAsJsonAsync(approveRoute, approveBody),
            approveClientB.PostAsJsonAsync(approveRoute, approveBody));
        var approveErrors = await Task.WhenAll(approveResponses.Select(async response =>
            response.StatusCode == HttpStatusCode.OK
                ? ""
                : $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}"));
        Assert.True(approveErrors.All(string.IsNullOrEmpty),
            string.Join(Environment.NewLine, approveErrors));
        var approveResults = await Task.WhenAll(approveResponses.Select(
            response => response.Content.ReadFromJsonAsync<ApprovalDecisionResult>()));
        Assert.All(approveResults, result => Assert.NotNull(result));
        Assert.Equal(approveResults[0]!.InstanceId, approveResults[1]!.InstanceId);
        await AssertSubmissionStateAsync(factory.Services, submitSettlementId,
            ApprovalInstanceStatus.Approved, expectedDecisionCount: 1);

        var (rejectSettlementId, _) = await CreateFinalSettlementAsync(factory.Services);
        await SubmitFinalSettlementAsync(submitClientA, rejectSettlementId, "pg-approve-reject-submit");
        using var raceApproveClient = factory.CreateClient();
        using var raceRejectClient = factory.CreateClient();
        Authorize(raceApproveClient, "payroll.final_settlement.approve");
        Authorize(raceRejectClient, "payroll.final_settlement.reject");
        SetIdentity(raceApproveClient, 202, "payroll.final_settlement.approve");
        SetIdentity(raceRejectClient, 202, "payroll.final_settlement.reject");
        var rejectRace = await Task.WhenAll(
            raceApproveClient.PostAsJsonAsync(
                $"/api/hr/v1/final-settlements/{rejectSettlementId}/approve",
                new FinalSettlementDecisionRequest(1, "pg-approve-reject-approve", "Reviewed")),
            raceRejectClient.PostAsJsonAsync(
                $"/api/hr/v1/final-settlements/{rejectSettlementId}/reject",
                new FinalSettlementDecisionRequest(1, "pg-approve-reject-reject", "Not supported")));
        AssertRaceWinnerAndConflict(rejectRace);
        var rejectRaceState = await ReadSubmissionStateAsync(factory.Services, rejectSettlementId);
        Assert.Contains(rejectRaceState.Status,
            new[] { ApprovalInstanceStatus.Approved, ApprovalInstanceStatus.Rejected });
        Assert.Equal(1, rejectRaceState.DecisionCount);

        var (cancelSettlementId, _) = await CreateFinalSettlementAsync(factory.Services);
        await SubmitFinalSettlementAsync(submitClientA, cancelSettlementId, "pg-approve-cancel-submit");
        using var raceCancelClient = factory.CreateClient();
        Authorize(raceCancelClient, "payroll.final_settlement.cancel");
        SetIdentity(raceCancelClient, 101, "payroll.final_settlement.cancel");
        var cancelRace = await Task.WhenAll(
            raceApproveClient.PostAsJsonAsync(
                $"/api/hr/v1/final-settlements/{cancelSettlementId}/approve",
                new FinalSettlementDecisionRequest(1, "pg-approve-cancel-approve", "Reviewed")),
            raceCancelClient.PostAsJsonAsync(
                $"/api/hr/v1/final-settlements/{cancelSettlementId}/cancel",
                new FinalSettlementDecisionRequest(1, "pg-approve-cancel-cancel", "Withdrawn")));
        AssertRaceWinnerAndConflict(cancelRace);
        var cancelRaceState = await ReadSubmissionStateAsync(factory.Services, cancelSettlementId);
        Assert.Contains(cancelRaceState.Status,
            new[] { ApprovalInstanceStatus.Approved, ApprovalInstanceStatus.Cancelled });
        Assert.Equal(1, cancelRaceState.DecisionCount);

        var (recalculateSettlementId, exitVersion) =
            await CreateFinalSettlementAsync(factory.Services);
        await SubmitFinalSettlementAsync(
            submitClientA, recalculateSettlementId, "pg-approve-recalculate-submit");
        using var recalculateClient = factory.CreateClient();
        Authorize(recalculateClient,
            "payroll.final_settlement.view,payroll.final_settlement.calculate");
        var recalculateRace = await Task.WhenAll(
            raceApproveClient.PostAsJsonAsync(
                $"/api/hr/v1/final-settlements/{recalculateSettlementId}/approve",
                new FinalSettlementDecisionRequest(1, "pg-approve-recalculate-approve", "Reviewed")),
            recalculateClient.PostAsJsonAsync(
                $"/api/hr/v1/exits/{(await ReadExitCaseIdAsync(factory.Services, recalculateSettlementId))}/final-settlement/calculate",
                new HrFinalSettlementCalculateRequest(exitVersion, "pg-approve-recalculate")));
        Assert.All(recalculateRace, response =>
            Assert.Contains(response.StatusCode,
                new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Contains(recalculateRace, response => response.StatusCode == HttpStatusCode.OK);
        var recalculationState = await ReadSubmissionStateAsync(factory.Services, recalculateSettlementId);
        Assert.Equal(1, recalculationState.DecisionCount);
        await AssertSubmissionEvidenceUnchangedAsync(factory.Services, recalculateSettlementId);

        await AssertSafeAuditAndNoPaymentSideEffectsAsync(factory.Services);
    }

    [Fact]
    public async Task FinalSettlementRejectAndCancelApisPersistImmutableDecisions()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var submitClient = factory.CreateClient();
        await SeedAsync(factory.Services);
        await SeedFinalSettlementApprovalPolicyAsync(factory.Services);
        Authorize(submitClient, "payroll.final_settlement.submit");

        var (rejectSettlementId, _) = await CreateFinalSettlementAsync(factory.Services);
        await SubmitFinalSettlementAsync(submitClient, rejectSettlementId, "reject-api-submit");
        SetIdentity(submitClient, 202, "payroll.final_settlement.reject");
        var rejectResponse = await submitClient.PostAsJsonAsync(
            $"/api/hr/v1/final-settlements/{rejectSettlementId}/reject",
            new FinalSettlementDecisionRequest(1, "reject-api-decision", "Evidence needs correction"));
        Assert.Equal(HttpStatusCode.OK, rejectResponse.StatusCode);
        var rejected = (await rejectResponse.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!;
        Assert.Equal(ApprovalInstanceStatus.Rejected, rejected.Status);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var submission = await db.PayrollFinalSettlementApprovalSubmissions.SingleAsync(
                row => row.FinalSettlementId == rejectSettlementId);
            var instance = await db.ApprovalInstances
                .SingleAsync(row => row.Id == submission.ApprovalInstanceId);
            var decisions = await db.ApprovalDecisions
                .Where(row => row.ApprovalInstanceId == instance.Id).ToArrayAsync();
            Assert.Equal(submission.SubmittedEvidenceHash, instance.RequestHash);
            var decision = Assert.Single(decisions);
            Assert.Equal(ApprovalDecisionType.Reject, decision.Decision);
            Assert.Equal("Evidence needs correction", decision.Reason);
            Assert.Contains(await db.AuditEvents.ToArrayAsync(), audit =>
                audit.ActionType == "hr.approval.decided");
            Assert.Empty(await db.PayrollPayments.ToArrayAsync());
            Assert.Empty(await db.PayrollAccountingPostings.ToArrayAsync());
        }

        var (approvedSettlementId, _) = await CreateFinalSettlementAsync(factory.Services);
        SetIdentity(submitClient, 101, "payroll.final_settlement.submit");
        await SubmitFinalSettlementAsync(submitClient, approvedSettlementId, "cancel-approved-submit");
        SetIdentity(submitClient, 202, "payroll.final_settlement.approve");
        using (var approveResponse = await submitClient.PostAsJsonAsync(
            $"/api/hr/v1/final-settlements/{approvedSettlementId}/approve",
            new FinalSettlementDecisionRequest(1, "cancel-approved-approve", "Reviewed")))
            Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        SetPermissions(submitClient, "payroll.final_settlement.cancel");
        using var cancelApprovedResponse = await submitClient.PostAsJsonAsync(
            $"/api/hr/v1/final-settlements/{approvedSettlementId}/cancel",
            new FinalSettlementDecisionRequest(1, "cancel-approved-late-cancel", "Too late"));
        Assert.Equal(HttpStatusCode.Conflict, cancelApprovedResponse.StatusCode);
        var approvedState = await ReadSubmissionStateAsync(factory.Services, approvedSettlementId);
        Assert.Equal(ApprovalInstanceStatus.Approved, approvedState.Status);
        Assert.Equal(1, approvedState.DecisionCount);

        SetPermissions(submitClient, "payroll.final_settlement.approve");
        var approveRejected = await submitClient.PostAsJsonAsync(
            $"/api/hr/v1/final-settlements/{rejectSettlementId}/approve",
            new FinalSettlementDecisionRequest(1, "reject-api-late-approve", "Reviewed"));
        Assert.Equal(HttpStatusCode.Conflict, approveRejected.StatusCode);

        var (cancelSettlementId, _) = await CreateFinalSettlementAsync(factory.Services);
        SetIdentity(submitClient, 101, "payroll.final_settlement.submit");
        await SubmitFinalSettlementAsync(submitClient, cancelSettlementId, "cancel-api-submit");
        SetPermissions(submitClient, "payroll.final_settlement.cancel");
        var cancelResponse = await submitClient.PostAsJsonAsync(
            $"/api/hr/v1/final-settlements/{cancelSettlementId}/cancel",
            new FinalSettlementDecisionRequest(1, "cancel-api-decision", "Request withdrawn"));
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        var cancelled = (await cancelResponse.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!;
        Assert.Equal(ApprovalInstanceStatus.Cancelled, cancelled.Status);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var submission = await db.PayrollFinalSettlementApprovalSubmissions.SingleAsync(
                row => row.FinalSettlementId == cancelSettlementId);
            var instance = await db.ApprovalInstances
                .SingleAsync(row => row.Id == submission.ApprovalInstanceId);
            var decisions = await db.ApprovalDecisions
                .Where(row => row.ApprovalInstanceId == instance.Id).ToArrayAsync();
            Assert.Equal(submission.SubmittedEvidenceHash, instance.RequestHash);
            var decision = Assert.Single(decisions);
            Assert.Equal(ApprovalDecisionType.Cancel, decision.Decision);
            Assert.Equal("Request withdrawn", decision.Reason);
            Assert.Contains(await db.AuditEvents.ToArrayAsync(), audit =>
                audit.ActionType == "hr.approval.cancelled");
            Assert.Empty(await db.PayrollPayments.ToArrayAsync());
            Assert.Empty(await db.PayrollAccountingPostings.ToArrayAsync());
        }
    }

    [Fact]
    public async Task FinalSettlementRejectAndCancelPermissionDenialsHaveNoDecisionSideEffects()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var submitClient = factory.CreateClient();
        await SeedAsync(factory.Services);
        await SeedFinalSettlementApprovalPolicyAsync(factory.Services);
        Authorize(submitClient, "payroll.final_settlement.submit");

        var (rejectSettlementId, _) = await CreateFinalSettlementAsync(factory.Services);
        await SubmitFinalSettlementAsync(submitClient, rejectSettlementId, "reject-denial-submit");
        SetIdentity(submitClient, 202, "payroll.final_settlement.view");
        var rejectResponse = await submitClient.PostAsJsonAsync(
            $"/api/hr/v1/final-settlements/{rejectSettlementId}/reject",
            new FinalSettlementDecisionRequest(1, "reject-denied", "Not allowed"));
        Assert.Equal(HttpStatusCode.Forbidden, rejectResponse.StatusCode);

        var (cancelSettlementId, _) = await CreateFinalSettlementAsync(factory.Services);
        SetIdentity(submitClient, 101, "payroll.final_settlement.submit");
        await SubmitFinalSettlementAsync(submitClient, cancelSettlementId, "cancel-denial-submit");
        SetPermissions(submitClient, "payroll.final_settlement.view");
        var cancelResponse = await submitClient.PostAsJsonAsync(
            $"/api/hr/v1/final-settlements/{cancelSettlementId}/cancel",
            new FinalSettlementDecisionRequest(1, "cancel-denied", "Not allowed"));
        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.All(await db.ApprovalInstances.ToArrayAsync(),
            instance => Assert.Equal(ApprovalInstanceStatus.Pending, instance.Status));
        Assert.Empty(await db.ApprovalDecisions.ToArrayAsync());
        Assert.Empty(await db.AuditEvents.Where(row =>
            row.ActionType == "hr.approval.decided"
            || row.ActionType == "hr.approval.cancelled").ToArrayAsync());
    }

    [Fact]
    public async Task FinalSettlementRejectAndCancelEnforceBusinessAndLocationScope()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var submitClient = factory.CreateClient();
        await SeedAsync(factory.Services);
        await SeedFinalSettlementApprovalPolicyAsync(factory.Services);
        await SeedOutletAsync(factory.Services, 20);
        await SeedAdditionalLocationUserAsync(factory.Services);
        Authorize(submitClient, "payroll.final_settlement.submit");

        var (settlementId, _) = await CreateFinalSettlementAsync(factory.Services);
        await SubmitFinalSettlementAsync(submitClient, settlementId, "scope-denial-submit");
        var route = $"/api/hr/v1/final-settlements/{settlementId}";

        using var crossBusinessClient = factory.CreateClient();
        Authorize(crossBusinessClient, "payroll.final_settlement.reject");
        crossBusinessClient.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        crossBusinessClient.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        using var crossBusinessResponse = await crossBusinessClient.PostAsJsonAsync(
            $"{route}/reject",
            new FinalSettlementDecisionRequest(1, "scope-cross-business-reject", "Denied"));
        Assert.Contains(crossBusinessResponse.StatusCode,
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
        var crossBusinessBody = await crossBusinessResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("1000", crossBusinessBody, StringComparison.Ordinal);

        using var locationRejectClient = factory.CreateClient();
        Authorize(locationRejectClient, "payroll.final_settlement.reject");
        SetRole(locationRejectClient, "HR");
        SetIdentity(locationRejectClient, 303, "payroll.final_settlement.reject");
        using var locationRejectResponse = await locationRejectClient.PostAsJsonAsync(
            $"{route}/reject",
            new FinalSettlementDecisionRequest(1, "scope-location-reject", "Denied"));
        Assert.Equal(HttpStatusCode.Forbidden, locationRejectResponse.StatusCode);

        using var locationCancelClient = factory.CreateClient();
        Authorize(locationCancelClient, "payroll.final_settlement.cancel");
        SetRole(locationCancelClient, "HR");
        SetIdentity(locationCancelClient, 303, "payroll.final_settlement.cancel");
        using var locationCancelResponse = await locationCancelClient.PostAsJsonAsync(
            $"{route}/cancel",
            new FinalSettlementDecisionRequest(1, "scope-location-cancel", "Denied"));
        Assert.Equal(HttpStatusCode.Forbidden, locationCancelResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(ApprovalInstanceStatus.Pending,
            (await db.ApprovalInstances.SingleAsync()).Status);
        Assert.Empty(await db.ApprovalDecisions.ToArrayAsync());
        Assert.Empty(await db.AuditEvents.Where(row =>
            row.ActionType == "hr.approval.decided"
            || row.ActionType == "hr.approval.cancelled").ToArrayAsync());
    }

    private static async Task<(long SettlementId, long ExitVersion)> CreateFinalSettlementAsync(
        IServiceProvider services)
    {
        var (exitCaseId, exitVersion) = await SeedFinalSettlementExitAsync(services);
        var settlementId = await SeedCalculatedFinalSettlementAsync(services, exitCaseId);
        return (settlementId, exitVersion);
    }

    private static async Task SubmitFinalSettlementAsync(
        HttpClient client, long settlementId, string idempotencyKey)
    {
        using var response = await client.PostAsJsonAsync(
            $"/api/hr/v1/final-settlements/{settlementId}/submit",
            new FinalSettlementSubmitRequest(1, idempotencyKey));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static void AssertRaceWinnerAndConflict(HttpResponseMessage[] responses)
    {
        Assert.Equal(2, responses.Length);
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.DoesNotContain(responses, response => (int)response.StatusCode >= 500);
    }

    private static async Task AssertSubmissionStateAsync(
        IServiceProvider services,
        long settlementId,
        ApprovalInstanceStatus status,
        int expectedDecisionCount)
    {
        var state = await ReadSubmissionStateAsync(services, settlementId);
        Assert.Equal(status, state.Status);
        Assert.Equal(expectedDecisionCount, state.DecisionCount);
    }

    private static async Task<(ApprovalInstanceStatus Status, int DecisionCount)> ReadSubmissionStateAsync(
        IServiceProvider services, long settlementId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var submission = await db.PayrollFinalSettlementApprovalSubmissions.AsNoTracking()
            .SingleAsync(row => row.FinalSettlementId == settlementId);
        var instance = await db.ApprovalInstances.AsNoTracking().SingleAsync(
            row => row.Id == submission.ApprovalInstanceId);
        var decisions = await db.ApprovalDecisions.CountAsync(row =>
            row.ApprovalInstanceId == instance.Id);
        return (instance.Status, decisions);
    }

    private static async Task<long> ReadExitCaseIdAsync(IServiceProvider services, long settlementId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        return await db.PayrollFinalSettlements.AsNoTracking()
            .Where(row => row.Id == settlementId).Select(row => row.ExitCaseId).SingleAsync();
    }

    private static async Task AssertSubmissionEvidenceUnchangedAsync(
        IServiceProvider services, long settlementId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var submission = await db.PayrollFinalSettlementApprovalSubmissions.AsNoTracking()
            .SingleAsync(row => row.FinalSettlementId == settlementId);
        var instance = await db.ApprovalInstances.AsNoTracking()
            .SingleAsync(row => row.Id == submission.ApprovalInstanceId);
        Assert.Equal(submission.SubmittedEvidenceHash, instance.RequestHash);
    }

    private static async Task AssertSafeAuditAndNoPaymentSideEffectsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(4, await db.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.final_settlement.requested"));
        foreach (var audit in await db.AuditEvents.ToArrayAsync())
        {
            Assert.Null(audit.Metadata);
            Assert.DoesNotContain("1000", audit.ActionSummary, StringComparison.Ordinal);
            Assert.DoesNotContain("1200", audit.ActionSummary, StringComparison.Ordinal);
        }
        Assert.Empty(await db.PayrollPayments.ToArrayAsync());
        Assert.Empty(await db.PayrollAccountingPostings.ToArrayAsync());
        Assert.Equal(4, await db.PayrollFinalSettlements.CountAsync());
        Assert.True(await db.PayrollFinalSettlementRevisions.CountAsync() >= 4);
        Assert.Equal(4, await db.PayrollFinalSettlementApprovalSubmissions.CountAsync());
        Assert.Equal(4, await db.ApprovalInstances.CountAsync(
            row => row.ProcessType == ApprovalProcessType.FinalSettlement));
    }

    private static RetailPOSDbContext CreatePostgresDb(string connectionString) =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(connectionString).Options);

    private static string GetPostgresConnectionString()
    {
        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        var connectionString = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
            ?? new ConfigurationBuilder().AddJsonFile(settingsPath, optional: false)
                .Build().GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));
        return connectionString!;
    }

    private sealed class FinalSettlementPostgresApiFactory(string connectionString)
        : WebApplicationFactory<Program>
    {
        public ConcurrentQueue<string> Logs { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.AddProvider(new CaptureLoggerProvider(Logs)));
            TestJwtConfiguration.Configure(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<RetailPOSDbContext>>();
                services.RemoveAll<RetailPOSDbContext>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<RetailPOSDbContext>));
                services.AddDbContext<RetailPOSDbContext>((serviceProvider, options) =>
                    options.UseNpgsql(connectionString).AddInterceptors(
                        serviceProvider.GetRequiredService<AuditChangeTrackerInterceptor>()));
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });
            });
        }
    }

    private sealed class CaptureLoggerProvider(ConcurrentQueue<string> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, entries);
        public void Dispose() { }
    }

    private sealed class CaptureLogger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                entries.Enqueue($"{category}: {formatter(state, exception)} {exception}");
        }
    }
}
