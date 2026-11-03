using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuestPDF.Infrastructure;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Application.HR.Payroll;
using RetailPOS.Application.ConfidentialFiles;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Payroll;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ConfidentialFiles;
using RetailPOS.Tests.Identity;
using RetailPOS.Tests.Infrastructure;
using UglyToad.PdfPig;

namespace RetailPOS.IntegrationTests.HR;

public sealed partial class PayrollRunApiTests
{
    [Fact]
    public async Task PayrollReportAndRunExportQueryMeasurementsAreCaptured()
    {
        var measurements = new DashboardQueryMeasurementInterceptor();
        using var factory = new SqliteRoleGovernanceApiFactory
        {
            AdditionalDbCommandInterceptor = measurements
        };
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client);
        SetIdentity(client, 101, "payroll.report.view,payroll.salary.view,payroll.export");

        measurements.Reset();
        var reportTimer = Stopwatch.StartNew();
        using var report = await client.GetAsync(
            $"/api/payroll/v1/reports/advanced?kind=PayrollRegister&from=2026-10-01&to=2026-10-31&runId={approved.RunId}");
        reportTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var reportBytes = await report.Content.ReadAsByteArrayAsync();
        WriteMeasurement("Payroll advanced report", measurements.Snapshot(), reportTimer.Elapsed,
            reportBytes.Length);

        measurements.Reset();
        var advancedExportTimer = Stopwatch.StartNew();
        using var advancedExport = await client.GetAsync(
            $"/api/payroll/v1/reports/advanced/export/csv?kind=PayrollRegister&from=2026-10-01&to=2026-10-31&runId={approved.RunId}");
        advancedExportTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, advancedExport.StatusCode);
        var advancedExportBytes = await advancedExport.Content.ReadAsByteArrayAsync();
        WriteMeasurement("Payroll advanced CSV export", measurements.Snapshot(),
            advancedExportTimer.Elapsed, advancedExportBytes.Length);

        measurements.Reset();
        var exportTimer = Stopwatch.StartNew();
        using var export = await client.GetAsync($"/api/payroll/v1/runs/{approved.RunId}/exports/csv");
        exportTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exportBytes = await export.Content.ReadAsByteArrayAsync();
        WriteMeasurement("Payroll run CSV export", measurements.Snapshot(), exportTimer.Elapsed,
            exportBytes.Length);
    }

    [Fact]
    public async Task PostgresPayrollReportAndExportQueryMeasurementsAreCaptured()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API",
            "appsettings.json"))).Build();
        var baseConnectionString = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
            ?? configuration.GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(baseConnectionString));

        await using var database = await PostgresTestDatabase.CreateAsync(
            baseConnectionString!, PostgresTestDatabasePrefix.PayrollAdjustment);
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString).Options;
        await using (var migrationDb = new RetailPOSDbContext(options))
            await migrationDb.Database.MigrateAsync();

        var measurements = new DashboardQueryMeasurementInterceptor();
        using var factory = new PostgresMeasurementApiFactory(
            database.ConnectionString, measurements);
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client);
        SetIdentity(client, 101, "payroll.dashboard.view,payroll.salary.view");

        measurements.Reset();
        var dashboardTimer = Stopwatch.StartNew();
        using var dashboard = await client.GetAsync($"{DashboardRoute}?periodId=1");
        dashboardTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        var dashboardBytes = await dashboard.Content.ReadAsByteArrayAsync();
        WriteMeasurement("Payroll dashboard PostgreSQL", measurements.Snapshot(),
            dashboardTimer.Elapsed, dashboardBytes.Length, "PostgreSQL");

        SetPermissions(client, "payroll.report.view,payroll.salary.view,payroll.export");

        measurements.Reset();
        var reportTimer = Stopwatch.StartNew();
        using var report = await client.GetAsync(
            $"/api/payroll/v1/reports/advanced?kind=PayrollRegister&from=2026-10-01&to=2026-10-31&runId={approved.RunId}");
        reportTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var reportBytes = await report.Content.ReadAsByteArrayAsync();
        WriteMeasurement("Payroll advanced report PostgreSQL", measurements.Snapshot(),
            reportTimer.Elapsed, reportBytes.Length, "PostgreSQL");

        measurements.Reset();
        var advancedExportTimer = Stopwatch.StartNew();
        using var advancedExport = await client.GetAsync(
            $"/api/payroll/v1/reports/advanced/export/csv?kind=PayrollRegister&from=2026-10-01&to=2026-10-31&runId={approved.RunId}");
        advancedExportTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, advancedExport.StatusCode);
        var advancedExportBytes = await advancedExport.Content.ReadAsByteArrayAsync();
        WriteMeasurement("Payroll advanced CSV export PostgreSQL", measurements.Snapshot(),
            advancedExportTimer.Elapsed, advancedExportBytes.Length, "PostgreSQL");

        measurements.Reset();
        var exportTimer = Stopwatch.StartNew();
        using var export = await client.GetAsync($"/api/payroll/v1/runs/{approved.RunId}/exports/csv");
        exportTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exportBytes = await export.Content.ReadAsByteArrayAsync();
        WriteMeasurement("Payroll run CSV export PostgreSQL", measurements.Snapshot(),
            exportTimer.Elapsed, exportBytes.Length, "PostgreSQL");
    }

    private void WriteMeasurement(
        string requestName,
        (long CommandCount, TimeSpan Cumulative, TimeSpan SlowestDataQuery, string? SlowestDataCommand) snapshot,
        TimeSpan elapsed,
        int? responseBytes,
        string provider = "SQLite")
    {
        output.WriteLine(
            $"{requestName}: provider={provider}; SQL commands={snapshot.CommandCount}; "
            + $"cumulative SQL={snapshot.Cumulative.TotalMilliseconds:F2} ms; "
            + $"slowest meaningful query={snapshot.SlowestDataQuery.TotalMilliseconds:F2} ms; "
            + $"HTTP={elapsed.TotalMilliseconds:F2} ms; response bytes={responseBytes?.ToString() ?? "n/a"}.");
        output.WriteLine($"Slowest SQL: {snapshot.SlowestDataCommand}");
    }

    [Fact]
    public async Task PayslipEndpointsRequireTheirDedicatedHttpPermissions()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        Authorize(client, "");

        const string route = "/api/payroll/v1/runs/1/payslips/1";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(route, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"{route}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"{route}/print")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(
            $"{route}/email",
            new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json")
            {
                Headers = { { "Idempotency-Key", "unauthorized-email" } }
            })).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.PayrollArtifactVersions.ToArrayAsync());
        Assert.Empty(await db.PayrollPayslipDeliveries.ToArrayAsync());
    }

    [Fact]
    public async Task ProductionPayslipAdaptersFailClosedWithoutReturningStorageReferences()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var blobs = services.GetRequiredService<IPrivateBlobStore>();
        var encryption = services.GetRequiredService<IConfidentialFileEncryption>();
        var inspector = services.GetRequiredService<IConfidentialContentInspector>();
        var email = services.GetRequiredService<IPayrollPayslipEmailTransport>();

        Assert.IsType<UnconfiguredPrivateBlobStore>(blobs);
        Assert.IsType<UnconfiguredConfidentialFileEncryption>(encryption);
        Assert.IsType<UnconfiguredConfidentialContentInspector>(inspector);
        Assert.IsType<UnconfiguredPayrollPayslipEmailTransport>(email);

        var storageFailure = await Assert.ThrowsAsync<PayrollPayslipInfrastructureUnavailableException>(
            () => blobs.StageAsync(1, "ciphertext"u8.ToArray(), CancellationToken.None));
        Assert.DoesNotContain("StorageKey", storageFailure.Message, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<PayrollPayslipInfrastructureUnavailableException>(
            () => encryption.EncryptAsync("salary data"u8.ToArray(), CancellationToken.None));
        await Assert.ThrowsAsync<PayrollPayslipInfrastructureUnavailableException>(
            () => inspector.ScanAsync("salary data"u8.ToArray(), CancellationToken.None));
        await Assert.ThrowsAsync<PayrollPayslipEmailNotConfiguredException>(
            () => email.DeliverAsync(1, 42, "employee@example.invalid", Guid.NewGuid(),
                CancellationToken.None));
    }

    [Fact]
    public async Task RunEndpointsEnforcePermissionAndBusinessIsolation()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        Authorize(client, "");

        var route = "/api/payroll/v1/periods/1/runs";
        var request = new CreatePayrollRunRequest("Business", null, "run-api-key", 2);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, request)).StatusCode);

        SetPermissions(client, "payroll.run.create");
        var createdResponse = await client.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content.ReadFromJsonAsync<PayrollRunResponse>())!;
        Assert.Equal("Draft", created.Status);
        Assert.Equal(0, created.IncludedEmployeeCount);
        Assert.Equal(1, created.ExcludedEmployeeCount);

        var replayResponse = await client.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<PayrollRunResponse>())!;
        Assert.True(replay.IsReplay);
        Assert.Equal(created.Id, replay.Id);

        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(route,
                request with { IdempotencyKey = "different-key" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"{route}/{created.Id}")).StatusCode);

        SetPermissions(client, "payroll.run.view");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{route}/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"{route}/{created.Id}/eligibility?page=1&pageSize=20")).StatusCode);

        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"{route}/{created.Id}")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Single(await db.PayrollRuns.Where(row => row.BusinessId == 1).ToListAsync());
        Assert.Empty(await db.PayrollRuns.Where(row => row.BusinessId == 2).ToListAsync());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task RunCreationRequiresHrAndPayrollCapabilities(bool hrEnabled, bool payrollEnabled)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, hrEnabled, payrollEnabled);
        Authorize(client, "payroll.run.create");

        var response = await client.PostAsJsonAsync(
            "/api/payroll/v1/periods/1/runs",
            new CreatePayrollRunRequest("Business", null, $"disabled-{hrEnabled}-{payrollEnabled}", 2));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.PayrollRuns.ToListAsync());
    }

    [Fact]
    public async Task RunCreationRequiresBusinessMonetaryConfigurationEffectiveForWholePeriod()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services, includeMonetaryConfiguration: false);
        Authorize(client, "payroll.run.create");

        var route = "/api/payroll/v1/periods/1/runs";
        var request = new CreatePayrollRunRequest("Business", null, "no-money-config", 2);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(route, request)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.PayrollRuns.ToArrayAsync());
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            db.PayrollMonetaryConfigurationVersions.Add(new PayrollMonetaryConfigurationVersion
            {
                BusinessId = 1,
                VersionNo = 1,
                EffectiveFrom = new(2020, 1, 1),
                CurrencyCode = "BDT",
                DecimalPrecision = 2,
                RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = 101
            });
            db.PayrollMonetaryConfigurationVersions.Add(new PayrollMonetaryConfigurationVersion
            {
                BusinessId = 1,
                VersionNo = 2,
                EffectiveFrom = new(2026, 10, 15),
                CurrencyCode = "JPY",
                DecimalPrecision = 0,
                RoundingMode = MidpointRounding.ToEven.ToString(),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = 101
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(
                route, request with { IdempotencyKey = "mid-period-money-config" })).StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await verify.PayrollRuns.ToArrayAsync());
    }

    [Fact]
    public async Task RunSubmissionRequiresPermissionAndBlocksUncalculatedRunWithoutMutation()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        Authorize(client, "payroll.run.create");
        const string runsRoute = "/api/payroll/v1/periods/1/runs";
        var createdResponse = await client.PostAsJsonAsync(
            runsRoute, new CreatePayrollRunRequest("Business", null, "submit-auth", 2));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var run = (await createdResponse.Content.ReadFromJsonAsync<PayrollRunResponse>())!;

        SetPermissions(client, "payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"{runsRoute}/{run.Id}/submit",
                new SubmitPayrollRunRequest(run.Version))).StatusCode);

        SetPermissions(client, "payroll.run.submit,payroll.salary.view");
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"{runsRoute}/{run.Id}/submit",
                new SubmitPayrollRunRequest(run.Version))).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persisted = await db.PayrollRuns.SingleAsync(row => row.Id == run.Id);
        Assert.Equal(PayrollRunStatuses.Draft, persisted.Status);
        Assert.Null(persisted.ApprovalInstanceId);
        Assert.Null(persisted.SubmittedAtUtc);
        Assert.Empty(await db.ApprovalInstances.ToArrayAsync());
    }

    [Theory]
    [InlineData("approve", ApprovalInstanceStatus.Approved, PayrollRunStatuses.Approved)]
    [InlineData("reject", ApprovalInstanceStatus.Rejected, PayrollRunStatuses.Rejected)]
    [InlineData("cancel", ApprovalInstanceStatus.Cancelled, PayrollRunStatuses.Cancelled)]
    public async Task ReadyRunUsesApprovalKernelOverHttpAndFreezesEvidenceThroughDecision(
        string action, ApprovalInstanceStatus expectedApprovalStatus, string expectedRunStatus)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        await SeedReadyRunApprovalAsync(factory.Services);
        Authorize(client, "payroll.run.create");
        const string runsRoute = "/api/payroll/v1/periods/1/runs";
        var create = await client.PostAsJsonAsync(
            runsRoute, new CreatePayrollRunRequest("Business", null, "ready-approval-http", 2));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var run = (await create.Content.ReadFromJsonAsync<PayrollRunResponse>())!;

        SetPermissions(client, "payroll.run.calculate");
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"{runsRoute}/{run.Id}/calculate", null)).StatusCode);
        SetPermissions(client, "payroll.run.submit,payroll.salary.view");
        var submit = await client.PostAsJsonAsync($"{runsRoute}/{run.Id}/submit",
            new SubmitPayrollRunRequest(run.Version + 1));
        Assert.True(submit.StatusCode == HttpStatusCode.OK,
            $"Expected OK but received {(int)submit.StatusCode}: {await submit.Content.ReadAsStringAsync()}");
        var approval = (await submit.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!;
        Assert.True(approval.InstanceId > 0);
        Assert.Equal(ApprovalInstanceStatus.Pending, approval.Status);
        var submissionReplay = await client.PostAsJsonAsync(
            $"{runsRoute}/{run.Id}/submit", new SubmitPayrollRunRequest(run.Version + 1));
        Assert.Equal(HttpStatusCode.OK, submissionReplay.StatusCode);
        var replayedApproval = (await submissionReplay.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!;
        Assert.True(replayedApproval.IsReplay);
        Assert.Equal(approval.InstanceId, replayedApproval.InstanceId);

        using (var pendingScope = factory.Services.CreateScope())
        {
            var pendingDb = pendingScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var pending = await pendingDb.PayrollRuns.SingleAsync(row => row.Id == run.Id);
            Assert.Equal(PayrollRunStatuses.PendingApproval, pending.Status);
            Assert.Equal(approval.InstanceId, pending.ApprovalInstanceId);
            Assert.NotNull(pending.ApprovalEvidenceHash);
            Assert.Null(pending.DecidedAtUtc);
        }

        SetPermissions(client, "payroll.period.lock,payroll.period.view");
        var locked = await client.PostAsJsonAsync(
            "/api/payroll/v1/periods/1/lock", new PayrollPeriodLifecycleRequest(2));
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Equal(PayrollPeriodStatuses.Locked,
            (await locked.Content.ReadFromJsonAsync<PayrollPeriodResponse>())!.Status);

        var actorId = action == "cancel" ? 101 : 202;
        SetIdentity(client, actorId, action == "cancel" ? "payroll.run.submit" : "payroll.run.approve");
        var decision = await client.PostAsJsonAsync(
            $"/api/payroll/v1/approvals/runs/{approval.InstanceId}/{action}",
            new PayrollRunApprovalDecisionRequest($"{action}-ready-run", "Reviewed"));
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        var recordedDecision = (await decision.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!;
        Assert.Equal(expectedApprovalStatus, recordedDecision.Status);
        var decisionReplay = await client.PostAsJsonAsync(
            $"/api/payroll/v1/approvals/runs/{approval.InstanceId}/{action}",
            new PayrollRunApprovalDecisionRequest($"{action}-ready-run", "Reviewed"));
        Assert.Equal(HttpStatusCode.OK, decisionReplay.StatusCode);
        Assert.True((await decisionReplay.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!.IsReplay);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var approved = await verifyDb.PayrollRuns.AsNoTracking().SingleAsync(row => row.Id == run.Id);
        Assert.Equal(expectedRunStatus, approved.Status);
        Assert.Equal(actorId, approved.DecidedByUserId);
        Assert.Equal("Reviewed", approved.DecisionReason);
        Assert.NotNull(approved.ApprovalEvidenceHash);
        Assert.Single(await verifyDb.ApprovalDecisions.ToArrayAsync());
        Assert.Single(await verifyDb.PayrollEmployeeResults.ToArrayAsync());
        Assert.Contains(await verifyDb.AuditEvents.Select(row => row.ActionType).ToArrayAsync(),
            action => action == "hr.approval.submitted");
        Assert.Contains(await verifyDb.AuditEvents.Select(row => row.ActionType).ToArrayAsync(),
            eventAction => eventAction == (action == "cancel"
                ? "hr.approval.cancelled" : "hr.approval.decided"));
        var period = await verifyDb.PayrollPeriods.SingleAsync(row => row.Id == 1);
        Assert.Equal(action == "approve" ? PayrollPeriodStatuses.Approved : PayrollPeriodStatuses.Locked,
            period.Status);
    }

    [Fact]
    public async Task AuditFailureRollsBackPayrollRunApprovalSubmissionAndDecision()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        await SeedReadyRunApprovalAsync(factory.Services);
        Authorize(client, "payroll.run.create");
        const string runsRoute = "/api/payroll/v1/periods/1/runs";
        var create = await client.PostAsJsonAsync(
            runsRoute, new CreatePayrollRunRequest("Business", null, "approval-audit-rollback", 2));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var run = (await create.Content.ReadFromJsonAsync<PayrollRunResponse>())!;
        SetPermissions(client, "payroll.run.calculate");
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"{runsRoute}/{run.Id}/calculate", null)).StatusCode);

        SetPermissions(client, "payroll.run.submit,payroll.salary.view");
        factory.RoleAuditFailureInterceptor.FailApprovalSubmissionAudit = true;
        var submitRoute = $"{runsRoute}/{run.Id}/submit";
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await client.PostAsJsonAsync(submitRoute,
                new SubmitPayrollRunRequest(run.Version + 1))).StatusCode);
        factory.RoleAuditFailureInterceptor.FailApprovalSubmissionAudit = false;

        using (var rollbackScope = factory.Services.CreateScope())
        {
            var rollbackDb = rollbackScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var rolledBack = await rollbackDb.PayrollRuns.SingleAsync(row => row.Id == run.Id);
            Assert.Equal(PayrollRunStatuses.Draft, rolledBack.Status);
            Assert.Null(rolledBack.ApprovalInstanceId);
            Assert.Null(rolledBack.SubmittedAtUtc);
            Assert.Empty(await rollbackDb.ApprovalInstances
                .Where(row => row.ProcessType == ApprovalProcessType.PayrollRun).ToArrayAsync());
        }

        var submit = await client.PostAsJsonAsync(submitRoute,
            new SubmitPayrollRunRequest(run.Version + 1));
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var approval = (await submit.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!;
        SetPermissions(client, "payroll.period.lock");
        long periodVersion;
        using (var periodScope = factory.Services.CreateScope())
        {
            var periodDb = periodScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            periodVersion = await periodDb.PayrollPeriods.Where(row => row.Id == 1)
                .Select(row => row.Version).SingleAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/payroll/v1/periods/1/lock",
            new PayrollPeriodLifecycleRequest(periodVersion))).StatusCode);
        SetIdentity(client, 202, "payroll.run.approve");
        var decisionRoute = $"/api/payroll/v1/approvals/runs/{approval.InstanceId}/approve";
        var decisionRequest = new PayrollRunApprovalDecisionRequest("audit-retry-approval", "Reviewed");
        factory.RoleAuditFailureInterceptor.FailApprovalDecisionAudit = true;
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await client.PostAsJsonAsync(decisionRoute, decisionRequest)).StatusCode);
        factory.RoleAuditFailureInterceptor.FailApprovalDecisionAudit = false;

        using (var decisionRollbackScope = factory.Services.CreateScope())
        {
            var decisionDb = decisionRollbackScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Equal(PayrollRunStatuses.PendingApproval,
                (await decisionDb.PayrollRuns.SingleAsync(row => row.Id == run.Id)).Status);
            Assert.Equal(ApprovalInstanceStatus.Pending,
                (await decisionDb.ApprovalInstances.SingleAsync(row => row.Id == approval.InstanceId)).Status);
            Assert.Empty(await decisionDb.ApprovalDecisions.ToArrayAsync());
        }

        var retry = await client.PostAsJsonAsync(decisionRoute, decisionRequest);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(PayrollRunStatuses.Approved,
            (await verifyDb.PayrollRuns.SingleAsync(row => row.Id == run.Id)).Status);
        Assert.Single(await verifyDb.ApprovalDecisions.ToArrayAsync());
    }

    [Fact]
    public async Task CalculationAndConfidentialResultReadsUseActualHttpPermissionChecks()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        Authorize(client, "payroll.run.create");

        var route = "/api/payroll/v1/periods/1/runs";
        var create = await client.PostAsJsonAsync(
            route, new CreatePayrollRunRequest("Business", null, "calculate-http", 2));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var run = (await create.Content.ReadFromJsonAsync<PayrollRunResponse>())!;

        SetPermissions(client, "payroll.run.calculate");
        factory.RoleAuditFailureInterceptor.FailPayrollRunCalculationAudit = true;
        var failedCalculation = await client.PostAsync($"{route}/{run.Id}/calculate", null);
        Assert.Equal(HttpStatusCode.InternalServerError, failedCalculation.StatusCode);
        using (var failedScope = factory.Services.CreateScope())
        {
            var failedDb = failedScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await failedDb.PayrollEmployeeResults.ToArrayAsync());
        }

        factory.RoleAuditFailureInterceptor.FailPayrollRunCalculationAudit = false;
        var calculate = await client.PostAsync($"{route}/{run.Id}/calculate", null);
        Assert.Equal(HttpStatusCode.OK, calculate.StatusCode);
        var calculation = (await calculate.Content.ReadFromJsonAsync<PayrollRunCalculationResponse>())!;
        Assert.Equal(0, calculation.CalculatedEmployeeCount);
        Assert.Equal(0, calculation.ExceptionEmployeeCount);
        var replay = await client.PostAsync($"{route}/{run.Id}/calculate", null);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<PayrollRunCalculationResponse>())!.IsReplay);

        SetPermissions(client, "payroll.run.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"{route}/{run.Id}/results")).StatusCode);
        SetPermissions(client, "payroll.run.view,payroll.salary.view");
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync($"{route}/{run.Id}/results")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Single(await db.AuditEvents.Where(row =>
            row.ActionType == "payroll.run.calculated").ToArrayAsync());
        Assert.Empty(await db.PayrollEmployeeResults.ToArrayAsync());
    }

    [Fact]
    public async Task ValidationWorkbenchRequiresConfidentialReadPermissionsAndAuditsFindings()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        Authorize(client, "payroll.run.create");

        var runsRoute = "/api/payroll/v1/periods/1/runs";
        var createdResponse = await client.PostAsJsonAsync(
            runsRoute, new CreatePayrollRunRequest("Business", null, "validation-api", 2));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var run = (await createdResponse.Content.ReadFromJsonAsync<PayrollRunResponse>())!;
        var validationRoute = $"{runsRoute}/{run.Id}/validation";

        SetPermissions(client, "");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(validationRoute)).StatusCode);
        SetPermissions(client, "payroll.run.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(validationRoute)).StatusCode);

        SetPermissions(client, "payroll.run.view,payroll.salary.view");
        var response = await client.GetAsync(validationRoute);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("GrossEarnings", body, StringComparison.Ordinal);
        Assert.DoesNotContain("NetPay", body, StringComparison.Ordinal);
        var result = System.Text.Json.JsonSerializer.Deserialize<PayrollRunValidationResponse>(
            body, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })!;
        Assert.Contains(result.Findings, row => row.Code == "NoApprovedSalary"
            && row.Severity == PayrollValidationSeverities.BlockingError);
        Assert.Contains(result.Findings, row => row.Code == "CalculationRequired");

        factory.RoleAuditFailureInterceptor.FailPayrollRunValidationAudit = true;
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await client.GetAsync(validationRoute)).StatusCode);
        factory.RoleAuditFailureInterceptor.FailPayrollRunValidationAudit = false;

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(1, await db.AuditEvents.CountAsync(row =>
            row.BusinessId == 1 && row.ActionType == "payroll.run.validation_accessed"));
    }

    [Fact]
    public async Task ValidationWorkbenchDoesNotEmitUnavailablePaymentBlockerWithoutIncludedEmployees()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        Authorize(client, "payroll.run.create");

        const string runsRoute = "/api/payroll/v1/periods/1/runs";
        var createdResponse = await client.PostAsJsonAsync(
            runsRoute, new CreatePayrollRunRequest("Business", null, "payment-validation", 2));
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var run = (await createdResponse.Content.ReadFromJsonAsync<PayrollRunResponse>())!;

        SetPermissions(client, "payroll.run.view,payroll.salary.view");
        var response = await client.GetAsync($"{runsRoute}/{run.Id}/validation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<PayrollRunValidationResponse>())!;
        Assert.False(result.CanSubmit);
        Assert.DoesNotContain(result.Findings, row =>
            row.Code == "PaymentDetailsValidationUnavailable");
        Assert.Contains(result.Findings, row => row.Code == "NoIncludedEmployees");
    }

    [Fact]
    public async Task OffCycleRunUsesAuthorizedEmployeeSubsetAndIsIdempotentOverHttp()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        await SeedReadyRunApprovalAsync(factory.Services);
        Authorize(client, "payroll.run.create");
        const string route = "/api/payroll/v1/periods/1/runs/off-cycle";
        var request = new CreateOffCyclePayrollRunRequest(
            "OFF-API-001", "Approved supplemental payroll", "Business", null,
            [1], "offcycle-api-key", 2);

        var createdResponse = await client.PostAsJsonAsync(route, request);
        Assert.True(createdResponse.StatusCode == HttpStatusCode.Created,
            $"Expected Created but got {(int)createdResponse.StatusCode}: {await createdResponse.Content.ReadAsStringAsync()}");
        var created = (await createdResponse.Content.ReadFromJsonAsync<PayrollRunResponse>())!;
        Assert.Equal(PayrollRunTypes.OffCycle, created.RunType);
        Assert.Equal("OFF-API-001", created.RunReference);
        SetPermissions(client, "payroll.run.view");
        var detailResponse = await client.GetAsync(
            $"/api/payroll/v1/periods/1/runs/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        SetPermissions(client, "payroll.run.create");
        var replayResponse = await client.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<PayrollRunResponse>())!;
        Assert.True(replay.IsReplay);
        Assert.Equal(created.Id, replay.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            route, request with { Reason = "Different payload" })).StatusCode);

        SetPermissions(client, "payroll.run.calculate");
        var calculation = await client.PostAsync(
            $"/api/payroll/v1/periods/1/runs/{created.Id}/calculate", null);
        Assert.Equal(HttpStatusCode.OK, calculation.StatusCode);
        SetPermissions(client, "payroll.run.submit,payroll.salary.view");
        var submission = await client.PostAsJsonAsync(
            $"/api/payroll/v1/periods/1/runs/{created.Id}/submit",
            new SubmitPayrollRunRequest(created.Version + 1));
        Assert.Equal(HttpStatusCode.OK, submission.StatusCode);
        var approval = (await submission.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!;
        SetPermissions(client, "payroll.period.lock,payroll.period.view");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/payroll/v1/periods/1/lock",
            new PayrollPeriodLifecycleRequest(2))).StatusCode);
        SetIdentity(client, 202, "payroll.run.approve");
        var approvalResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/approvals/runs/{approval.InstanceId}/approve",
            new PayrollRunApprovalDecisionRequest("approve-off-cycle-run", "Approved"));
        Assert.Equal(HttpStatusCode.OK, approvalResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var persisted = await db.PayrollRuns.AsNoTracking()
            .SingleAsync(row => row.Id == created.Id);
        Assert.Equal(PayrollRunTypes.OffCycle, persisted.RunType);
        Assert.Equal(PayrollRunStatuses.Approved, persisted.Status);
        Assert.Equal("[1]", persisted.AffectedEmployeeIdsSnapshotJson);
        Assert.Single(await db.PayrollRunEmployeeSelections
            .Where(row => row.PayrollRunId == created.Id && row.IsIncluded).ToArrayAsync());
        var result = await db.PayrollEmployeeResults.AsNoTracking()
            .SingleAsync(row => row.PayrollRunId == created.Id && row.EmployeeId == 1);
        var payslip = await scope.ServiceProvider.GetRequiredService<IPayrollPayslipStore>()
            .GetSourceAsync(1, created.Id, 1, CancellationToken.None);
        var exportStore = scope.ServiceProvider.GetRequiredService<IPayrollExportStore>();
        var exportRun = await exportStore.FindRunAsync(1, created.Id, CancellationToken.None);
        Assert.NotNull(exportRun);
        var exportEmployees = await exportStore.GetEmployeesAsync(
            1, created.Id, exportRun!.Revision, null, false, CancellationToken.None);
        var exportedEmployee = Assert.Single(exportEmployees);
        Assert.Equal(result.EmployeeCodeSnapshot, payslip.EmployeeCode);
        Assert.Equal(result.EmployeeNameSnapshot, payslip.EmployeeName);
        Assert.Equal(result.GrossEarnings, payslip.GrossEarnings);
        Assert.Equal(result.TotalDeductions, payslip.TotalDeductions);
        Assert.Equal(result.NetPay, payslip.NetPayable);
        Assert.Equal(result.EmployeeCodeSnapshot, exportedEmployee.EmployeeCode);
        Assert.Equal(result.EmployeeNameSnapshot, exportedEmployee.EmployeeName);
        Assert.Equal(result.GrossEarnings, exportedEmployee.GrossEarnings);
        Assert.Equal(result.TotalDeductions, exportedEmployee.TotalDeductions);
        Assert.Equal(result.NetPay, exportedEmployee.NetPayable);
    }

    [Fact]
    public async Task CorrectionCreatesLinkedRunAndReopensPeriodWithoutChangingApprovedRun()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory.Services);
        await SeedReadyRunApprovalAsync(factory.Services);
        Authorize(client, "payroll.run.create");
        const string runsRoute = "/api/payroll/v1/periods/1/runs";
        var createResponse = await client.PostAsJsonAsync(runsRoute,
            new CreatePayrollRunRequest("Business", null, "correction-original", 2));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var original = (await createResponse.Content.ReadFromJsonAsync<PayrollRunResponse>())!;
        SetPermissions(client, "payroll.run.calculate");
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"{runsRoute}/{original.Id}/calculate", null)).StatusCode);
        SetPermissions(client, "payroll.run.submit,payroll.salary.view");
        var submission = await client.PostAsJsonAsync($"{runsRoute}/{original.Id}/submit",
            new SubmitPayrollRunRequest(original.Version + 1));
        Assert.Equal(HttpStatusCode.OK, submission.StatusCode);
        var approval = (await submission.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!;
        SetPermissions(client, "payroll.period.lock,payroll.period.view");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/payroll/v1/periods/1/lock", new PayrollPeriodLifecycleRequest(2))).StatusCode);
        SetIdentity(client, 202, "payroll.run.approve");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/approvals/runs/{approval.InstanceId}/approve",
            new PayrollRunApprovalDecisionRequest(
                "approve-original-for-correction", "Approved"))).StatusCode);

        long originalVersion;
        string originalStatus;
        string? originalInputHash;
        long periodVersion;
        PayrollResultEvidence[] beforeResults;
        using (var beforeScope = factory.Services.CreateScope())
        {
            var beforeDb = beforeScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var beforeOriginal = await beforeDb.PayrollRuns.AsNoTracking()
                .SingleAsync(row => row.Id == original.Id);
            originalVersion = beforeOriginal.Version;
            originalStatus = beforeOriginal.Status;
            originalInputHash = beforeOriginal.CalculationInputHash;
            periodVersion = (await beforeDb.PayrollPeriods.AsNoTracking()
                .SingleAsync(row => row.Id == 1)).Version;
            beforeResults = await beforeDb.PayrollEmployeeResults.AsNoTracking()
                .Where(row => row.PayrollRunId == original.Id)
                .Select(row => new PayrollResultEvidence(
                    row.CalculationSnapshotHash, row.CalculationInputHash, row.NetPay))
                .ToArrayAsync();
        }

        SetIdentity(client, 101, "payroll.run.reopen,payroll.run.create");
        var correctionRequest = new CreatePayrollCorrectionRequest(
            "CORR-API-001", "Recalculation", "Correct approved attendance evidence", [1],
            "correction-api-key", periodVersion, originalVersion);
        SetPermissions(client, "payroll.run.create");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(
                $"{runsRoute}/{original.Id}/corrections", correctionRequest)).StatusCode);
        using (var deniedScope = factory.Services.CreateScope())
        {
            var deniedDb = deniedScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Single(await deniedDb.PayrollRuns.ToArrayAsync());
            Assert.Empty(await deniedDb.PayrollPeriodReopenEvents.ToArrayAsync());
        }
        SetPermissions(client, "payroll.run.reopen,payroll.run.create");
        factory.RoleAuditFailureInterceptor.FailPayrollRunCorrectionAudit = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await client.PostAsJsonAsync(
            $"{runsRoute}/{original.Id}/corrections", correctionRequest)).StatusCode);
        factory.RoleAuditFailureInterceptor.FailPayrollRunCorrectionAudit = false;
        using (var auditFailureScope = factory.Services.CreateScope())
        {
            var auditFailureDb = auditFailureScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Single(await auditFailureDb.PayrollRuns.ToArrayAsync());
            Assert.Empty(await auditFailureDb.PayrollPeriodReopenEvents.ToArrayAsync());
        }
        var correctionResponse = await client.PostAsJsonAsync(
            $"{runsRoute}/{original.Id}/corrections", correctionRequest);
        Assert.Equal(HttpStatusCode.Created, correctionResponse.StatusCode);
        var correction = (await correctionResponse.Content.ReadFromJsonAsync<PayrollRunResponse>())!;
        Assert.Equal(PayrollRunTypes.Correction, correction.RunType);
        Assert.Equal(original.Id, correction.OriginalPayrollRunId);
        Assert.True(correction.OriginalEvidenceHash is { Length: 64 });
        var correctionReplayResponse = await client.PostAsJsonAsync(
            $"{runsRoute}/{original.Id}/corrections", correctionRequest);
        Assert.Equal(HttpStatusCode.Created, correctionReplayResponse.StatusCode);
        var correctionReplay =
            (await correctionReplayResponse.Content.ReadFromJsonAsync<PayrollRunResponse>())!;
        Assert.True(correctionReplay.IsReplay);
        Assert.Equal(correction.Id, correctionReplay.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            $"{runsRoute}/{original.Id}/corrections",
            correctionRequest with { Reason = "Different correction payload" })).StatusCode);

        SetPermissions(client, "payroll.run.calculate");
        var correctionCalculation = await client.PostAsync(
            $"{runsRoute}/{correction.Id}/calculate", null);
        Assert.Equal(HttpStatusCode.OK, correctionCalculation.StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var unchangedOriginal = await verifyDb.PayrollRuns.AsNoTracking()
            .SingleAsync(row => row.Id == original.Id);
        Assert.Equal(originalVersion, unchangedOriginal.Version);
        Assert.Equal(originalStatus, unchangedOriginal.Status);
        Assert.Equal(originalInputHash, unchangedOriginal.CalculationInputHash);
        var afterResults = await verifyDb.PayrollEmployeeResults.AsNoTracking()
            .Where(row => row.PayrollRunId == original.Id)
            .Select(row => new PayrollResultEvidence(
                row.CalculationSnapshotHash, row.CalculationInputHash, row.NetPay))
            .ToArrayAsync();
        Assert.Equal(beforeResults, afterResults);
        var correctionResult = await verifyDb.PayrollEmployeeResults.AsNoTracking()
            .SingleAsync(row => row.PayrollRunId == correction.Id);
        Assert.Equal(1, correctionResult.CalculationVersion);
        Assert.False(string.IsNullOrWhiteSpace(correctionResult.CalculationSnapshotJson));
        Assert.False(string.IsNullOrWhiteSpace(correctionResult.CalculationSnapshotHash));
        Assert.Equal(PayrollPeriodStatuses.Processing,
            (await verifyDb.PayrollPeriods.SingleAsync(row => row.Id == 1)).Status);
        var reopen = await verifyDb.PayrollPeriodReopenEvents.SingleAsync();
        Assert.Equal(original.Id, reopen.OriginalPayrollRunId);
        Assert.Equal(correction.Id, reopen.CorrectionPayrollRunId);
        Assert.Equal("Approved", reopen.PreviousStatus);
        Assert.Single(await verifyDb.AuditEvents
            .Where(row => row.ActionType == "payroll.run.correction_created").ToArrayAsync());
        SetPermissions(client, "payroll.run.view");
        var correctionRead = await client.GetAsync(
            $"{runsRoute}/{correction.Id}/correction");
        Assert.Equal(HttpStatusCode.OK, correctionRead.StatusCode);
        Assert.Single(await verifyDb.AuditEvents
            .Where(row => row.ActionType == "payroll.run.correction_accessed").ToArrayAsync());
    }

    [Fact]
    public async Task PayrollAccountingEndpointEnforcesPermissionBusinessAndPayrollScope()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client);
        var route = $"/api/payroll/v1/runs/{approved.RunId}/post-accounting";

        using var baselineScope = factory.Services.CreateScope();
        var baselineDb = baselineScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var auditCount = await baselineDb.AuditEvents.CountAsync();
        var postingCount = await baselineDb.PayrollAccountingPostings.CountAsync();
        var journalCount = await baselineDb.JournalEntries.CountAsync();

        SetPermissions(client, "payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync(route, null)).StatusCode);
        using (var deniedScope = factory.Services.CreateScope())
        {
            var deniedDb = deniedScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Equal(postingCount, await deniedDb.PayrollAccountingPostings.CountAsync());
            Assert.Equal(journalCount, await deniedDb.JournalEntries.CountAsync());
            Assert.Equal(auditCount, await deniedDb.AuditEvents.CountAsync());
        }

        SetPermissions(client, "payroll.accounting.post,payroll.salary.view");
        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(route, null)).StatusCode);
        using (var crossBusinessScope = factory.Services.CreateScope())
        {
            var crossBusinessDb = crossBusinessScope.ServiceProvider
                .GetRequiredService<RetailPOSDbContext>();
            Assert.Equal(postingCount, await crossBusinessDb.PayrollAccountingPostings.CountAsync());
            Assert.Equal(journalCount, await crossBusinessDb.JournalEntries.CountAsync());
            Assert.Equal(auditCount, await crossBusinessDb.AuditEvents.CountAsync());
        }

        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        await AddAdditionalEmployeeLocationAndScopeUserAsync(factory.Services);
        using (var locationUserScope = factory.Services.CreateScope())
        {
            var locationUserDb = locationUserScope.ServiceProvider
                .GetRequiredService<RetailPOSDbContext>();
            locationUserDb.Users.Add(new User
            {
                Id = 303,
                BusinessId = 1,
                Name = "Additional-location-only user",
                Email = "payroll-accounting-location@example.invalid",
                PasswordHash = "test",
                IsActive = true,
                OutletId = 20,
                InventoryLocationAccessScope = User.InventoryAccessAssignedOnly
            });
            locationUserDb.UserOutletAssignments.Add(new UserOutletAssignment
            {
                UserId = 303,
                BusinessId = 1,
                OutletId = 20,
                IsPrimary = true
            });
            await locationUserDb.SaveChangesAsync();
        }
        SetRole(client, "HR");
        SetIdentity(client, 303, "payroll.accounting.post,payroll.salary.view");
        var locationDeniedResponse = await client.PostAsync(route, null);
        Assert.True(locationDeniedResponse.StatusCode == HttpStatusCode.Forbidden,
            await locationDeniedResponse.Content.ReadAsStringAsync());
        using (var locationScope = factory.Services.CreateScope())
        {
            var locationDb = locationScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Equal(postingCount, await locationDb.PayrollAccountingPostings.CountAsync());
            Assert.Equal(journalCount, await locationDb.JournalEntries.CountAsync());
            Assert.Equal(auditCount, await locationDb.AuditEvents.CountAsync());
        }

        using var authorizedClient = factory.CreateClient();
        Authorize(authorizedClient, "payroll.accounting.post,payroll.salary.view");
        var authorizedResponse = await authorizedClient.PostAsync(route, null);
        Assert.Equal(HttpStatusCode.BadRequest, authorizedResponse.StatusCode);
        Assert.Contains("requires PostgreSQL",
            await authorizedResponse.Content.ReadAsStringAsync());
        using var authorizedScope = factory.Services.CreateScope();
        var authorizedDb = authorizedScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(postingCount, await authorizedDb.PayrollAccountingPostings.CountAsync());
        Assert.Equal(journalCount, await authorizedDb.JournalEntries.CountAsync());
        Assert.Equal(auditCount + 1, await authorizedDb.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task PayrollPaymentUsesApprovedPayableAndIsIdempotentAndAudited()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client);
        var request = new CreatePayrollPaymentRequest(
            approved.ResultId, approved.RunVersion, "payment-cash-001",
            [new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, approved.Payable)]);

        await ScopeUserToOutletAsync(factory.Services, 101, 10);
        SetRole(client, "HR");
        SetIdentity(client, 101, "payroll.payment.process,payroll.salary.view");
        var createdResponse = await client.PostAsJsonAsync("/api/payroll/v1/payments", request);
        Assert.True(createdResponse.StatusCode == HttpStatusCode.Created,
            $"Expected Created but received {(int)createdResponse.StatusCode}: " +
            await createdResponse.Content.ReadAsStringAsync());
        var created = (await createdResponse.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.Equal(approved.Payable, created.PayableAmount);
        Assert.Equal(0m, created.PaidAmount);
        Assert.Equal(approved.Payable, created.OutstandingAmount);
        Assert.Equal(PayrollPaymentStatuses.Pending, created.Status);
        Assert.Equal(PayrollPaymentMethods.Cash, created.Method);
        Assert.Equal(approved.Payable, created.Allocations.Sum(row => row.Amount));
        Assert.All(created.Allocations, allocation =>
        {
            Assert.Equal(0m, allocation.SettledAmount);
            Assert.Equal(PayrollPaymentAllocationStates.Pending, allocation.State);
            Assert.Null(allocation.AccountNumber);
        });

        var replayResponse = await client.PostAsJsonAsync("/api/payroll/v1/payments", request);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.True(replay.IsReplay);
        Assert.Equal(created.Id, replay.Id);

        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/payroll/v1/payments",
                request with
                {
                    Allocations =
                    [
                        new PayrollPaymentAllocationInput(
                            PayrollPaymentMethods.Cash, approved.Payable - 1m)
                    ]
                })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/payroll/v1/payments",
                request with { ExpectedRunVersion = approved.RunVersion + 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync("/api/payroll/v1/payments",
                request with { IdempotencyKey = "payment-cash-duplicate" })).StatusCode);

        SetPermissions(client, "payroll.payment.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/payroll/v1/payments/{created.Id}")).StatusCode);
        SetPermissions(client, "payroll.payment.view,payroll.salary.view");
        var detailResponse = await client.GetAsync($"/api/payroll/v1/payments/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/payroll/v1/payments/{created.Id}")).StatusCode);
        SetPermissions(client, "payroll.payment.process,payroll.salary.view");
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync("/api/payroll/v1/payments",
                request with { IdempotencyKey = "cross-business-result" })).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var payment = await db.PayrollPayments.Include(row => row.Allocations)
            .SingleAsync(row => row.Id == created.Id);
        Assert.Empty(await db.PayrollPayments.Where(row => row.BusinessId == 2).ToArrayAsync());
        Assert.Equal(approved.Payable, payment.PayableAmount);
        Assert.Equal(0m, payment.PaidAmount);
        Assert.Equal(approved.Payable, payment.OutstandingAmount);
        Assert.Single(payment.Allocations);
        var resultAfter = await db.PayrollEmployeeResults.AsNoTracking()
            .SingleAsync(row => row.Id == approved.ResultId);
        Assert.Equal(approved.SnapshotHash, resultAfter.CalculationSnapshotHash);
        Assert.Equal(approved.Payable, resultAfter.NetPay);
        Assert.Equal(1, await db.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.created"));
        Assert.Equal(1, await db.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.accessed"));
    }

    [Fact]
    public async Task PayrollPaymentHoldReleaseFailureAndRetryAreVersionedAndAudited()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client);
        await ScopeUserToOutletAsync(factory.Services, 101, 10);
        SetIdentity(client, 101, "payroll.payment.process,payroll.salary.view,payroll.payment.view");
        var create = new CreatePayrollPaymentRequest(
            approved.ResultId, approved.RunVersion, "payment-lifecycle-001",
            [new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, approved.Payable)]);
        var createResponse = await client.PostAsJsonAsync("/api/payroll/v1/payments", create);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var payment = (await createResponse.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;

        SetPermissions(client, "payroll.payment.view,payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/payroll/v1/payments/{payment.Id}/hold",
                new PayrollPaymentStateChangeRequest(payment.Version, "risk review"))).StatusCode);

        SetPermissions(client,
            "payroll.payment.hold,payroll.payment.view,payroll.salary.view");
        factory.RoleAuditFailureInterceptor.FailAuditActionType = "payroll.payment.held";
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await client.PostAsJsonAsync($"/api/payroll/v1/payments/{payment.Id}/hold",
                new PayrollPaymentStateChangeRequest(payment.Version, "risk review"))).StatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        var unchanged = (await (await client.GetAsync(
            $"/api/payroll/v1/payments/{payment.Id}"))
            .Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.Equal(PayrollPaymentStatuses.Pending, unchanged.Status);
        Assert.Equal(payment.Version, unchanged.Version);

        var hold = await client.PostAsJsonAsync($"/api/payroll/v1/payments/{payment.Id}/hold",
            new PayrollPaymentStateChangeRequest(payment.Version, "risk review"));
        Assert.Equal(HttpStatusCode.OK, hold.StatusCode);
        payment = (await hold.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.Equal(PayrollPaymentStatuses.Held, payment.Status);
        Assert.Equal(101, payment.HeldByUserId);
        Assert.Equal("risk review", payment.HoldReason);

        SetPermissions(client, "payroll.payment.view,payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/payroll/v1/payments/{payment.Id}/release",
                new PayrollPaymentStateChangeRequest(payment.Version, "review complete"))).StatusCode);
        SetPermissions(client,
            "payroll.payment.release,payroll.payment.view,payroll.salary.view");
        var release = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/release",
            new PayrollPaymentStateChangeRequest(payment.Version, "review complete"));
        Assert.Equal(HttpStatusCode.OK, release.StatusCode);
        payment = (await release.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.Equal(PayrollPaymentStatuses.Ready, payment.Status);
        Assert.Equal(101, payment.ReleasedByUserId);
        Assert.Equal("review complete", payment.ReleaseReason);

        using (var scope = factory.Services.CreateScope())
        {
            var stateDb = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var persisted = await stateDb.PayrollPayments.SingleAsync(row => row.Id == payment.Id);
            persisted.Status = PayrollPaymentStatuses.Processing;
            await stateDb.SaveChangesAsync();
        }
        SetPermissions(client, "payroll.payment.process,payroll.payment.view,payroll.salary.view");
        var failure = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/fail",
            new PayrollPaymentFailureRequest(
                payment.Version, "bank rejected instruction", "provider-rejection-1"));
        Assert.Equal(HttpStatusCode.OK, failure.StatusCode);
        var failed = (await failure.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.Equal(PayrollPaymentStatuses.Failed, failed.Status);
        Assert.Equal("bank rejected instruction", failed.FailureReturnReason);
        Assert.Equal("provider-rejection-1", failed.ExternalReference);
        Assert.Equal(PayrollPaymentResolutionStatuses.PendingInvestigation, failed.ResolutionStatus);
        Assert.Equal(0m, failed.PaidAmount);
        Assert.Equal(approved.Payable, failed.OutstandingAmount);

        var retryRequest = new RetryPayrollPaymentRequest(
            failed.Version, "payment-lifecycle-retry-001", "provider-confirmed-no-settlement",
            [new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, approved.Payable)]);
        SetPermissions(client, "payroll.payment.view,payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(
                $"/api/payroll/v1/payments/{payment.Id}/retry", retryRequest)).StatusCode);
        SetPermissions(client, "payroll.payment.retry,payroll.payment.view,payroll.salary.view");
        var retryResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/retry", retryRequest);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var retried = (await retryResponse.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.Equal(PayrollPaymentStatuses.Ready, retried.Status);
        Assert.Equal(2, retried.AttemptNo);
        Assert.Equal(payment.Id, retried.OriginalPaymentId);
        Assert.Equal(approved.Payable, retried.PayableAmount);
        Assert.False(retried.IsReplay);

        var replayResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/retry", retryRequest);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.True(replay.IsReplay);
        Assert.Equal(retried.Id, replay.Id);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync($"/api/payroll/v1/payments/{payment.Id}/retry",
                retryRequest with
                {
                    Allocations =
                    [
                        new PayrollPaymentAllocationInput(
                            PayrollPaymentMethods.Cash, approved.Payable - 1m)
                    ]
                })).StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var original = await verifyDb.PayrollPayments.SingleAsync(row => row.Id == payment.Id);
        Assert.Equal(PayrollPaymentResolutionStatuses.ResolvedReissue, original.ResolutionStatus);
        Assert.Equal("provider-confirmed-no-settlement", original.ResolutionEvidenceReference);
        Assert.Equal(2, await verifyDb.PayrollPayments.CountAsync());
        Assert.Equal(1, await verifyDb.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.held"));
        Assert.Equal(1, await verifyDb.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.released"));
        Assert.Equal(1, await verifyDb.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.failed"));
        Assert.Equal(1, await verifyDb.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.retried"));
    }

    [Fact]
    public async Task PayrollPaymentSettlementAndReturnEvidenceGateRetry()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client);
        await ScopeUserToOutletAsync(factory.Services, 101, 10);
        SetIdentity(client, 101,
            "payroll.payment.process,payroll.payment.view,payroll.salary.view,payroll.payment.retry");
        var create = new CreatePayrollPaymentRequest(
            approved.ResultId, approved.RunVersion, "payment-settlement-001",
            [new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, approved.Payable)]);
        var createdResponse = await client.PostAsJsonAsync("/api/payroll/v1/payments", create);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var payment = (await createdResponse.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var stateDb = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var persisted = await stateDb.PayrollPayments.SingleAsync(row => row.Id == payment.Id);
            persisted.Status = PayrollPaymentStatuses.Processing;
            await stateDb.SaveChangesAsync();
        }

        var part = 1m;
        Assert.True(approved.Payable > part);
        var firstEvidence = new PayrollPaymentEvidenceRequest(
            payment.Version, "settlement-evidence-001", new DateOnly(2026, 10, 11),
            "cash-settlement-001", [new PayrollPaymentAllocationEvidenceInput(1, part)]);
        var settlementResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/settlements", firstEvidence);
        Assert.Equal(HttpStatusCode.OK, settlementResponse.StatusCode);
        var partiallySettled =
            (await settlementResponse.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.Equal(PayrollPaymentStatuses.PartiallyPaid, partiallySettled.Status);
        Assert.Equal(part, partiallySettled.PaidAmount);
        Assert.Equal(approved.Payable - part, partiallySettled.OutstandingAmount);
        Assert.Equal(PayrollPaymentAllocationStates.PartiallySettled,
            Assert.Single(partiallySettled.Allocations).State);
        var settlementReplay = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/settlements", firstEvidence);
        Assert.Equal(HttpStatusCode.OK, settlementReplay.StatusCode);
        Assert.True((await settlementReplay.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!
            .IsReplay);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(
                $"/api/payroll/v1/payments/{payment.Id}/settlements",
                firstEvidence with
                {
                    Allocations = [new PayrollPaymentAllocationEvidenceInput(1, part + 1m)]
                })).StatusCode);

        var completeEvidence = new PayrollPaymentEvidenceRequest(
            partiallySettled.Version, "settlement-evidence-002", new DateOnly(2026, 10, 12),
            "cash-settlement-002",
            [new PayrollPaymentAllocationEvidenceInput(1, approved.Payable - part)]);
        var completeResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/settlements", completeEvidence);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var paid = (await completeResponse.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.Equal(PayrollPaymentStatuses.Paid, paid.Status);
        Assert.Equal(approved.Payable, paid.PaidAmount);
        Assert.Equal(0m, paid.OutstandingAmount);
        Assert.NotNull(paid.PaidAtUtc);
        Assert.Equal(2, paid.Evidence.Count);

        var firstReturn = new PayrollPaymentEvidenceRequest(
            paid.Version, "return-evidence-001", new DateOnly(2026, 10, 13),
            "cash-return-001", [new PayrollPaymentAllocationEvidenceInput(1, part)],
            "partial return");
        var returnResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/returns", firstReturn);
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);
        var partiallyReturned =
            (await returnResponse.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.Equal(PayrollPaymentStatuses.Returned, partiallyReturned.Status);
        Assert.Equal(approved.Payable - part, partiallyReturned.PaidAmount);
        Assert.Equal(part, partiallyReturned.OutstandingAmount);
        Assert.Equal(PayrollPaymentResolutionStatuses.PendingInvestigation,
            partiallyReturned.ResolutionStatus);
        var retryRequest = new RetryPayrollPaymentRequest(
            partiallyReturned.Version, "payment-return-retry-001", "return-confirmed",
            [new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, part)]);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(
                $"/api/payroll/v1/payments/{payment.Id}/retry", retryRequest)).StatusCode);

        var remainingReturn = new PayrollPaymentEvidenceRequest(
            partiallyReturned.Version, "return-evidence-002", new DateOnly(2026, 10, 14),
            "cash-return-002",
            [new PayrollPaymentAllocationEvidenceInput(1, approved.Payable - part)],
            "remaining funds returned");
        var fullReturnResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/returns", remainingReturn);
        Assert.Equal(HttpStatusCode.OK, fullReturnResponse.StatusCode);
        var fullyReturned =
            (await fullReturnResponse.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.Equal(0m, fullyReturned.PaidAmount);
        Assert.Equal(approved.Payable, fullyReturned.OutstandingAmount);
        Assert.Equal(PayrollPaymentResolutionStatuses.ResolvedReturn,
            fullyReturned.ResolutionStatus);

        retryRequest = retryRequest with
        {
            ExpectedVersion = fullyReturned.Version,
            Allocations = [new PayrollPaymentAllocationInput(
                PayrollPaymentMethods.Cash, approved.Payable)]
        };
        var retryResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/retry", retryRequest);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        var retried = (await retryResponse.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.Equal(PayrollPaymentStatuses.Ready, retried.Status);
        Assert.Equal(2, retried.AttemptNo);
        Assert.Equal(approved.Payable, retried.PayableAmount);
        Assert.Empty(retried.Evidence);

        using var verifyScope = factory.Services.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Equal(4, await db.PayrollPaymentEvidence.CountAsync());
        Assert.Equal(2, await db.PayrollPayments.CountAsync());
        Assert.Equal(2, await db.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.settled"));
        Assert.Equal(2, await db.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.returned"));
        Assert.Equal(1, await db.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.retried"));
    }

    [Fact]
    public async Task PaymentReconciliationIsAuthorizedAuditedVersionedAndIdempotentOverHttp()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client);
        await ScopeUserToOutletAsync(factory.Services, 101, 10);
        SetRole(client, "HR");
        SetIdentity(client, 101,
            "payroll.payment.process,payroll.payment.view,payroll.salary.view");
        var createResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/payments",
            new CreatePayrollPaymentRequest(
                approved.ResultId, approved.RunVersion, "reconciliation-payment-001",
                [new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, approved.Payable)]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var payment = (await createResponse.Content
            .ReadFromJsonAsync<PayrollPaymentResponse>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var persisted = await db.PayrollPayments.SingleAsync(row => row.Id == payment.Id);
            persisted.Status = PayrollPaymentStatuses.Processing;
            await db.SaveChangesAsync();
        }

        var settlementResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/settlements",
            new PayrollPaymentEvidenceRequest(
                payment.Version, "reconciliation-settlement-001",
                new DateOnly(2026, 10, 11), "cash-settlement-reconcile-001",
                [new PayrollPaymentAllocationEvidenceInput(1, approved.Payable)]));
        Assert.Equal(HttpStatusCode.OK, settlementResponse.StatusCode);
        var settled = (await settlementResponse.Content
            .ReadFromJsonAsync<PayrollPaymentResponse>())!;
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider
                .GetRequiredService<IPayrollPaymentReconciliationStore>();
            Assert.Equal(10, await store.GetPaymentLocationIdAsync(
                1, payment.Id, CancellationToken.None));
            Assert.Empty(await store.ListLatestAsync(
                1, payment.Id, null, CancellationToken.None));
        }
        var request = new ReconcilePayrollPaymentRequest(
            settled.Version, 0, "reconciliation-idempotency-001", 1, approved.Payable,
            new DateOnly(2026, 10, 11), PayrollPaymentMethods.Cash,
            "cash-settlement-reconcile-001");
        var route = $"/api/payroll/v1/payments/{payment.Id}/reconcile";

        var beforePermissionCheck = await client.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.Forbidden, beforePermissionCheck.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.PayrollPaymentReconciliations.ToArrayAsync());
        }

        SetPermissions(client,
            "payroll.payment.reconcile,payroll.payment.view,payroll.salary.view");
        factory.RoleAuditFailureInterceptor.FailAuditActionType = "payroll.payment.reconciled";
        var failedAuditResponse = await client.PostAsJsonAsync(route, request);
        Assert.True(failedAuditResponse.StatusCode == HttpStatusCode.InternalServerError,
            $"Expected audit failure to return 500, received {(int)failedAuditResponse.StatusCode}: " +
            await failedAuditResponse.Content.ReadAsStringAsync());
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.PayrollPaymentReconciliations.ToArrayAsync());
            var persisted = await db.PayrollPayments.SingleAsync(row => row.Id == payment.Id);
            Assert.Equal(settled.Version, persisted.Version);
            Assert.Equal(approved.Payable, persisted.PaidAmount);
        }

        var response = await client.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content
            .ReadFromJsonAsync<PayrollPaymentReconciliationOperationResponse>())!;
        var reconciliation = Assert.Single(result.Items);
        Assert.Equal(PayrollPaymentReconciliationStatuses.Reconciled, reconciliation.Status);
        Assert.Equal(approved.Payable, reconciliation.ExpectedAmount);
        Assert.Equal(approved.Payable, reconciliation.SettledAmount);
        Assert.Equal(1, reconciliation.Version);
        Assert.Equal(settled.Version, reconciliation.PaymentVersion);
        Assert.False(result.IsReplay);

        var replayResponse = await client.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replay = (await replayResponse.Content
            .ReadFromJsonAsync<PayrollPaymentReconciliationOperationResponse>())!;
        Assert.True(replay.IsReplay);
        Assert.Equal(reconciliation.Id, Assert.Single(replay.Items).Id);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            route, request with
            {
                SettledAmount = approved.Payable - 1m,
                Notes = "different evidence"
            })).StatusCode);

        var listResponse = await client.GetAsync(
            $"/api/payroll/v1/payment-reconciliation?paymentId={payment.Id}");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listed = (await listResponse.Content
            .ReadFromJsonAsync<PayrollPaymentReconciliationResponse[]>())!;
        Assert.Equal(reconciliation.Id, Assert.Single(listed).Id);

        await AddAdditionalEmployeeLocationAndScopeUserAsync(factory.Services);
        var additionalLocationRequest = request with
        {
            ExpectedReconciliationVersion = 1,
            IdempotencyKey = "additional-location-denied",
            ExternalReference = "additional-location-reference"
        };
        var additionalLocationResponse = await client.PostAsJsonAsync(
            route, additionalLocationRequest);
        Assert.Equal(HttpStatusCode.Forbidden, additionalLocationResponse.StatusCode);
        var additionalLocationBody = await additionalLocationResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(approved.Payable.ToString(
            System.Globalization.CultureInfo.InvariantCulture), additionalLocationBody);
        Assert.DoesNotContain("cash-settlement-reconcile-001", additionalLocationBody);

        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        var crossBusinessResponse = await client.PostAsJsonAsync(
            route, additionalLocationRequest with
            {
                ExpectedReconciliationVersion = 1,
                IdempotencyKey = "cross-business-denied"
            });
        Assert.Equal(HttpStatusCode.NotFound, crossBusinessResponse.StatusCode);
        var crossBusinessBody = await crossBusinessResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(approved.Payable.ToString(
            System.Globalization.CultureInfo.InvariantCulture), crossBusinessBody);
        Assert.DoesNotContain("cash-settlement-reconcile-001", crossBusinessBody);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var unchangedPayment = await verifyDb.PayrollPayments.SingleAsync(row => row.Id == payment.Id);
        Assert.Equal(settled.Version, unchangedPayment.Version);
        Assert.Equal(approved.Payable, unchangedPayment.PaidAmount);
        Assert.Equal(approved.Payable, unchangedPayment.PayableAmount);
        Assert.Equal(1, await verifyDb.PayrollPaymentReconciliations.CountAsync());
        Assert.Equal(1, await verifyDb.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.reconciled"));
        Assert.Equal(1, await verifyDb.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.reconciliation_accessed"));
    }

    [Fact]
    public async Task ElectronicPaymentReconciliationRequiresSensitiveViewPermission()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(
            factory, client, PayrollPaymentMethods.Bank);
        await ScopeUserToOutletAsync(factory.Services, 101, 10);
        SetRole(client, "HR");
        SetIdentity(client, 101,
            "payroll.payment.process,payroll.payment.view,payroll.salary.view," +
            "payroll.payment.view_sensitive");
        var createResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/payments",
            new CreatePayrollPaymentRequest(
                approved.ResultId, approved.RunVersion, "bank-reconcile-permission",
                [new PayrollPaymentAllocationInput(
                    PayrollPaymentMethods.Bank, approved.Payable, "bank-attempt-reference")]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var payment = (await createResponse.Content
            .ReadFromJsonAsync<PayrollPaymentResponse>())!;
        var route = $"/api/payroll/v1/payments/{payment.Id}/reconcile";
        var request = new ReconcilePayrollPaymentRequest(
            payment.Version, 0, "bank-reconcile-key", 1, 0m, null,
            PayrollPaymentMethods.Bank, null);
        SetPermissions(client,
            "payroll.payment.reconcile,payroll.payment.view,payroll.salary.view");
        var deniedResponse = await client.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        Assert.DoesNotContain("00123456789",
            await deniedResponse.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.PayrollPaymentReconciliations.ToArrayAsync());
        }

        SetPermissions(client,
            "payroll.payment.reconcile,payroll.payment.view,payroll.salary.view," +
            "payroll.payment.view_sensitive");
        var allowedResponse = await client.PostAsJsonAsync(route, request);
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
        var result = (await allowedResponse.Content
            .ReadFromJsonAsync<PayrollPaymentReconciliationOperationResponse>())!;
        Assert.Equal(PayrollPaymentReconciliationStatuses.Unreconciled,
            Assert.Single(result.Items).Status);
        Assert.DoesNotContain("00123456789", await allowedResponse.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(PayrollPaymentMethods.Bank)]
    [InlineData(PayrollPaymentMethods.Mobile)]
    public async Task MixedPartialPaymentFreezesSensitiveDestinationEvidence(string method)
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client, method);
        var request = new CreatePayrollPaymentRequest(
            approved.ResultId, approved.RunVersion, $"payment-mixed-{method}",
            [
                new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, approved.Payable / 4m),
                new PayrollPaymentAllocationInput(method, approved.Payable / 4m, "attempt-reference")
            ]);
        SetIdentity(client, 101,
            "payroll.payment.process,payroll.salary.view");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/payroll/v1/payments", request)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.PayrollPayments.ToArrayAsync());
        }
        SetPermissions(client,
            "payroll.payment.process,payroll.payment.view_sensitive,payroll.salary.view");

        var response = await client.PostAsJsonAsync("/api/payroll/v1/payments", request);
        Assert.True(response.StatusCode == HttpStatusCode.Created,
            $"Expected Created but received {(int)response.StatusCode}: " +
            await response.Content.ReadAsStringAsync());
        var payment = (await response.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.Equal(PayrollPaymentMethods.Mixed, payment.Method);
        Assert.Equal(0m, payment.PaidAmount);
        Assert.Equal(approved.Payable, payment.OutstandingAmount);
        Assert.Equal(approved.Payable / 2m, payment.Allocations.Sum(row => row.Amount));
        var electronic = Assert.Single(payment.Allocations, row => row.Method == method);
        Assert.True(electronic.PaymentDestinationId > 0);
        Assert.NotNull(electronic.PaymentDestinationVersionNo);
        if (method == PayrollPaymentMethods.Bank)
        {
            Assert.Equal("Bank One", electronic.BankName);
            Assert.Equal("Payroll Account", electronic.AccountName);
            Assert.Equal("00123456789", electronic.AccountNumber);
            Assert.Null(electronic.MobileAccountNumber);
        }
        else
        {
            Assert.Equal("Mobile One", electronic.MobileProvider);
            Assert.Equal("Payroll Wallet", electronic.MobileAccountName);
            Assert.Equal("01700000000", electronic.MobileAccountNumber);
            Assert.Null(electronic.AccountNumber);
        }

        long destinationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            destinationId = await db.PayrollEmployeePaymentDestinations
                .Where(row => row.BusinessId == 1 && row.EmployeeId == 1)
                .Select(row => row.Id).SingleAsync();
        }
        SetPermissions(client,
            "payroll.payment_destination.manage,payroll.payment.view,payroll.payment.view_sensitive,payroll.salary.view");
        var successorData = new PayrollEmployeePaymentDestinationData(
            PayrollPaymentMethods.Cash, new(2026, 11, 1), null, null, null, null, null, null);
        var versionResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/employees/1/payment-destinations/{destinationId}/versions",
            new VersionPayrollEmployeePaymentDestinationRequest(1, successorData));
        Assert.Equal(HttpStatusCode.OK, versionResponse.StatusCode);

        SetPermissions(client, "payroll.payment.view,payroll.salary.view");
        var detailResponse = await client.GetAsync($"/api/payroll/v1/payments/{payment.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var masked = (await detailResponse.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        var maskedAllocation = Assert.Single(masked.Allocations, row => row.Method == method);
        Assert.Null(maskedAllocation.BankName);
        Assert.Null(maskedAllocation.AccountNumber);
        Assert.Null(maskedAllocation.MobileProvider);
        Assert.Null(maskedAllocation.MobileAccountNumber);

        SetPermissions(client,
            "payroll.payment.view,payroll.payment.view_sensitive,payroll.salary.view");
        detailResponse = await client.GetAsync($"/api/payroll/v1/payments/{payment.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var historical = (await detailResponse.Content.ReadFromJsonAsync<PayrollPaymentResponse>())!;
        var frozen = Assert.Single(historical.Allocations, row => row.Method == method);
        Assert.Equal(electronic.PaymentDestinationVersionNo, frozen.PaymentDestinationVersionNo);
        Assert.Equal(electronic.AccountNumber, frozen.AccountNumber);
        Assert.Equal(electronic.MobileAccountNumber, frozen.MobileAccountNumber);
        Assert.Equal(0m, historical.PaidAmount);
        Assert.Equal(approved.Payable, historical.OutstandingAmount);
    }

    [Fact]
    public async Task PayrollPaymentRejectsAdditionalOnlyLocationScope()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client);
        await AddAdditionalEmployeeLocationAndScopeUserAsync(factory.Services);
        SetRole(client, "HR");
        SetIdentity(client, 101, "payroll.payment.process,payroll.salary.view");
        var request = new CreatePayrollPaymentRequest(
            approved.ResultId, approved.RunVersion, "payment-additional-only",
            [new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, approved.Payable)]);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/payroll/v1/payments", request)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await db.PayrollPayments.ToArrayAsync());
        Assert.Empty(await db.PayrollPaymentAllocations.ToArrayAsync());
        Assert.Empty(await db.AuditEvents.Where(row =>
            row.ActionType == "payroll.payment.created").ToArrayAsync());
    }

    [Fact]
    public async Task PayrollPaymentRequiresPermissionsAndPayrollCapabilities()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client);
        var request = new CreatePayrollPaymentRequest(
            approved.ResultId, approved.RunVersion, "payment-capability-check",
            [new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, approved.Payable)]);

        SetIdentity(client, 101, "payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/payroll/v1/payments", request)).StatusCode);
        SetPermissions(client, "payroll.payment.process");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/payroll/v1/payments", request)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var payroll = await db.BusinessFeatureSettings.SingleAsync(row =>
                row.BusinessId == 1 && row.FeatureKey == "payroll.enabled");
            payroll.IsEnabled = false;
            await db.SaveChangesAsync();
        }
        SetPermissions(client, "payroll.payment.process,payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/payroll/v1/payments", request)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var payroll = await db.BusinessFeatureSettings.SingleAsync(row =>
                row.BusinessId == 1 && row.FeatureKey == "payroll.enabled");
            var hr = await db.BusinessFeatureSettings.SingleAsync(row =>
                row.BusinessId == 1 && row.FeatureKey == "hr.enabled");
            payroll.IsEnabled = true;
            hr.IsEnabled = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/payroll/v1/payments", request)).StatusCode);

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Empty(await verifyDb.PayrollPayments.ToArrayAsync());
        Assert.Empty(await verifyDb.AuditEvents.Where(row =>
            row.ActionType == "payroll.payment.created").ToArrayAsync());
    }

    [Fact]
    public async Task PayrollPaymentOverpaymentAndAuditFailureDoNotCreatePartialState()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client);
        var request = new CreatePayrollPaymentRequest(
            approved.ResultId, approved.RunVersion, "payment-audit-failure",
            [new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, approved.Payable + 1m)]);
        SetIdentity(client, 101, "payroll.payment.process,payroll.salary.view");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/payroll/v1/payments", request)).StatusCode);

        var valid = request with
        {
            Allocations =
            [
                new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, approved.Payable)
            ]
        };
        factory.RoleAuditFailureInterceptor.FailAuditActionType = "payroll.payment.created";
        Assert.Equal(HttpStatusCode.InternalServerError,
            (await client.PostAsJsonAsync("/api/payroll/v1/payments", valid)).StatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;
        var retry = await client.PostAsJsonAsync("/api/payroll/v1/payments", valid);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        Assert.Single(await db.PayrollPayments.ToArrayAsync());
        Assert.Single(await db.PayrollPaymentAllocations.ToArrayAsync());
        Assert.Equal(1, await db.AuditEvents.CountAsync(row =>
            row.ActionType == "payroll.payment.created"));
    }

    [Fact]
    public void PayrollPaymentRoutesAppearInOpenApi()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        var paths = factory.Services.GetRequiredService<Swashbuckle.AspNetCore.Swagger.ISwaggerProvider>()
            .GetSwagger("v1").Paths.Keys;
        Assert.Contains("/api/payroll/v1/payments", paths);
        Assert.Contains("/api/payroll/v1/payments/{paymentId}", paths);
        Assert.Contains("/api/payroll/v1/payment-batches/{id}/submit", paths);
        Assert.Contains("/api/payroll/v1/payments/{paymentId}/reconcile", paths);
        Assert.Contains("/api/payroll/v1/payment-batches/{batchId}/reconcile", paths);
        Assert.Contains("/api/payroll/v1/payment-reconciliation", paths);
    }

    [Fact]
    public async Task PayrollPaymentBatchSubmissionIsAuditedVersionedAndIdempotentOverHttp()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client);
        await ScopeUserToOutletAsync(factory.Services, 101, 10);
        SetRole(client, "HR");
        SetIdentity(client, 101, "payroll.payment.process,payroll.payment.view," +
            "payroll.salary.view");

        var paymentResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/payments",
            new CreatePayrollPaymentRequest(
                approved.ResultId, approved.RunVersion, "batch-payment-cash",
                [new PayrollPaymentAllocationInput(
                    PayrollPaymentMethods.Cash, approved.Payable)]));
        Assert.Equal(HttpStatusCode.Created, paymentResponse.StatusCode);
        var payment = (await paymentResponse.Content
            .ReadFromJsonAsync<PayrollPaymentResponse>())!;

        var batchResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/runs/1/payment-batches",
            new CreatePayrollPaymentBatchRequest(
                "batch-cash-001", "batch-create-001",
                [new PayrollPaymentBatchItemInput(payment.Id, payment.Version)]));
        Assert.True(batchResponse.StatusCode == HttpStatusCode.Created,
            $"Expected Created but received {(int)batchResponse.StatusCode}: " +
            await batchResponse.Content.ReadAsStringAsync());
        var batch = (await batchResponse.Content
            .ReadFromJsonAsync<PayrollPaymentBatchResponse>())!;
        Assert.Equal(PayrollPaymentBatchStatuses.Ready, batch.Status);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var beforeSubmit = await db.PayrollPayments.SingleAsync(row => row.Id == payment.Id);
            Assert.Equal(PayrollPaymentStatuses.Ready, beforeSubmit.Status);
            Assert.Equal(0m, beforeSubmit.PaidAmount);
        }

        var submission = new SubmitPayrollPaymentBatchRequest(
            batch.Version, "batch-submit-001", "bank-upload-001", "Uploaded to bank portal");
        var submitResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payment-batches/{batch.Id}/submit", submission);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var submitted = (await submitResponse.Content
            .ReadFromJsonAsync<PayrollPaymentBatchResponse>())!;
        Assert.Equal(PayrollPaymentBatchStatuses.Processing, submitted.Status);
        Assert.NotNull(submitted.SubmittedAtUtc);
        Assert.Equal(101, submitted.SubmittedByUserId);
        Assert.Equal(PayrollPaymentStatuses.Processing, Assert.Single(submitted.Payments).Status);
        Assert.Equal(PayrollPaymentMethods.Cash, submitted.Payments[0].Method);

        var replayResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payment-batches/{batch.Id}/submit", submission);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.True((await replayResponse.Content
            .ReadFromJsonAsync<PayrollPaymentBatchResponse>())!.IsReplay);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/payment-batches/{batch.Id}/submit",
            submission with { ExternalReference = "different-reference" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/payment-batches/{batch.Id}/submit",
            submission with
            {
                ExpectedVersion = submitted.Version,
                IdempotencyKey = "second-submission-key"
            })).StatusCode);

        var settlementResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/settlements",
            new PayrollPaymentEvidenceRequest(
                submitted.Payments[0].Version,
                "batch-payment-settlement-001",
                new DateOnly(2026, 10, 11),
                "cash-receipt-001",
                [new PayrollPaymentAllocationEvidenceInput(1, approved.Payable)],
                "Cash paid"));
        Assert.Equal(HttpStatusCode.OK, settlementResponse.StatusCode);
        var completedBatchResponse = await client.GetAsync(
            $"/api/payroll/v1/payment-batches/{batch.Id}");
        Assert.Equal(HttpStatusCode.OK, completedBatchResponse.StatusCode);
        var completedBatch = (await completedBatchResponse.Content
            .ReadFromJsonAsync<PayrollPaymentBatchResponse>())!;
        Assert.Equal(PayrollPaymentBatchStatuses.Completed, completedBatch.Status);
        var settledPayment = (await settlementResponse.Content
            .ReadFromJsonAsync<PayrollPaymentResponse>())!;
        SetPermissions(client,
            "payroll.payment.reconcile,payroll.payment.view,payroll.salary.view");
        var reconciliationResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payment-batches/{batch.Id}/reconcile",
            new ReconcilePayrollPaymentBatchRequest(
                completedBatch.Version, "batch-reconciliation-001",
                [new ReconcilePayrollPaymentBatchItemRequest(
                    payment.Id, settledPayment.Version, 1, 0, approved.Payable,
                    new DateOnly(2026, 10, 11), PayrollPaymentMethods.Cash,
                    "cash-reconciliation-001")]));
        Assert.Equal(HttpStatusCode.OK, reconciliationResponse.StatusCode);
        var reconciliation = (await reconciliationResponse.Content
            .ReadFromJsonAsync<PayrollPaymentReconciliationOperationResponse>())!;
        Assert.Equal(PayrollPaymentReconciliationStatuses.Reconciled,
            Assert.Single(reconciliation.Items).Status);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            var persistedBatch = await db.PayrollPaymentBatches.SingleAsync(row => row.Id == batch.Id);
            var persistedPayment = await db.PayrollPayments.SingleAsync(row => row.Id == payment.Id);
            Assert.Equal(PayrollPaymentBatchStatuses.Completed, persistedBatch.Status);
            Assert.Equal("bank-upload-001", persistedBatch.SubmissionExternalReference);
            Assert.Equal("batch-submit-001", persistedBatch.SubmissionIdempotencyKey);
            Assert.Equal(PayrollPaymentStatuses.Paid, persistedPayment.Status);
            Assert.Equal(2, await db.AuditEvents.CountAsync(row =>
                row.ActionType == "payroll.payment_batch.submitted"));
            Assert.Equal(1, await db.PayrollPaymentReconciliations.CountAsync());
            Assert.Equal(1, await db.AuditEvents.CountAsync(row =>
                row.ActionType == "payroll.payment.reconciled"));
        }
    }

    [Fact]
    public async Task BankPaymentBatchRequiresVersionedFileAndSubmissionIsAuditedOverHttp()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client, PayrollPaymentMethods.Bank);
        await ScopeUserToOutletAsync(factory.Services, 101, 10);
        SetRole(client, "HR");
        SetIdentity(client, 101,
            "payroll.payment.process,payroll.payment.view,payroll.salary.view," +
            "payroll.payment.view_sensitive");
        var paymentResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/payments",
            new CreatePayrollPaymentRequest(
                approved.ResultId, approved.RunVersion, "batch-payment-bank",
                [new PayrollPaymentAllocationInput(
                    PayrollPaymentMethods.Bank, approved.Payable, "bank-attempt-001")]));
        Assert.Equal(HttpStatusCode.Created, paymentResponse.StatusCode);
        var payment = (await paymentResponse.Content
            .ReadFromJsonAsync<PayrollPaymentResponse>())!;
        var batchResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/runs/1/payment-batches",
            new CreatePayrollPaymentBatchRequest(
                "batch-bank-001", "batch-bank-create-001",
                [new PayrollPaymentBatchItemInput(payment.Id, payment.Version)]));
        Assert.Equal(HttpStatusCode.Created, batchResponse.StatusCode);
        var batch = (await batchResponse.Content
            .ReadFromJsonAsync<PayrollPaymentBatchResponse>())!;

        SetPermissions(client, "payroll.payment.process,payroll.payment.view," +
            "payroll.salary.view,payroll.payment.view_sensitive");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/payment-batches/{batch.Id}/submit",
            new SubmitPayrollPaymentBatchRequest(
                batch.Version, "submit-before-file", "bank-upload", "Uploaded"))).StatusCode);

        var fileRequest = new PayrollPaymentBankFileRequest(
            batch.Version, "batch-bank-file-001", "Initial payment file");
        SetPermissions(client, "payroll.payment.view,payroll.salary.view," +
            "payroll.payment.view_sensitive");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(
                $"/api/payroll/v1/payment-batches/{batch.Id}/generate-bank-file",
                fileRequest)).StatusCode);

        SetPermissions(client, "payroll.bank_file.generate,payroll.payment.view," +
            "payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(
                $"/api/payroll/v1/payment-batches/{batch.Id}/generate-bank-file",
                fileRequest)).StatusCode);

        SetPermissions(client, "payroll.bank_file.generate,payroll.payment.view," +
            "payroll.salary.view,payroll.payment.view_sensitive");
        factory.RoleAuditFailureInterceptor.FailAuditActionType = "payroll.bank_file.generated";
        var failedAuditResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payment-batches/{batch.Id}/generate-bank-file", fileRequest);
        Assert.False(failedAuditResponse.IsSuccessStatusCode);
        factory.RoleAuditFailureInterceptor.FailAuditActionType = null;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.PayrollPaymentBankFileVersions.ToArrayAsync());
            Assert.Equal(batch.Version,
                (await db.PayrollPaymentBatches.SingleAsync(row => row.Id == batch.Id)).Version);
        }

        var generatedResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payment-batches/{batch.Id}/generate-bank-file", fileRequest);
        Assert.Equal(HttpStatusCode.OK, generatedResponse.StatusCode);
        Assert.Contains("private", generatedResponse.Headers.CacheControl?.ToString());
        Assert.Contains("no-store", generatedResponse.Headers.CacheControl?.ToString());
        var generatedContent = await generatedResponse.Content.ReadAsStringAsync();
        Assert.Contains("00123456789", generatedContent);
        Assert.Contains(approved.Payable.ToString(CultureInfo.InvariantCulture), generatedContent);
        Assert.DoesNotContain("StorageKey", generatedContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("public path", generatedContent, StringComparison.OrdinalIgnoreCase);

        var replayResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payment-batches/{batch.Id}/generate-bank-file", fileRequest);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(generatedContent, await replayResponse.Content.ReadAsStringAsync());

        var secondFileResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payment-batches/{batch.Id}/generate-bank-file",
            new PayrollPaymentBankFileRequest(
                batch.Version + 1, "batch-bank-file-002", "Regenerated payment file"));
        Assert.Equal(HttpStatusCode.OK, secondFileResponse.StatusCode);
        SetPermissions(client,
            "payroll.payment.process,payroll.payment.view,payroll.salary.view," +
            "payroll.payment.view_sensitive");
        var submitResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payment-batches/{batch.Id}/submit",
            new SubmitPayrollPaymentBatchRequest(
                batch.Version + 2, "batch-bank-submit-001", "bank-upload-001", "Uploaded"));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var submitted = (await submitResponse.Content
            .ReadFromJsonAsync<PayrollPaymentBatchResponse>())!;
        Assert.Equal(PayrollPaymentBatchStatuses.Processing, submitted.Status);
        Assert.Equal(2, submitted.BankFileVersions.Count);
        Assert.Equal(101, submitted.SubmittedByUserId);

        SetPermissions(client,
            "payroll.payment.process,payroll.payment.view,payroll.salary.view," +
            "payroll.payment.view_sensitive");
        var paymentDetailResponse = await client.GetAsync(
            $"/api/payroll/v1/payments/{payment.Id}");
        Assert.Equal(HttpStatusCode.OK, paymentDetailResponse.StatusCode);
        var paymentBeforeSettlement = (await paymentDetailResponse.Content
            .ReadFromJsonAsync<PayrollPaymentResponse>())!;
        var settlementResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/settlements",
            new PayrollPaymentEvidenceRequest(
                paymentBeforeSettlement.Version, "cross-artifact-settlement",
                new DateOnly(2026, 10, 11), "cross-artifact-bank-settlement",
                [new PayrollPaymentAllocationEvidenceInput(1, approved.Payable)]));
        Assert.Equal(HttpStatusCode.OK, settlementResponse.StatusCode);
        var settled = (await settlementResponse.Content
            .ReadFromJsonAsync<PayrollPaymentResponse>())!;
        Assert.Equal(approved.Payable, settled.PaidAmount);
        Assert.Equal(0m, settled.OutstandingAmount);
        Assert.Equal(PayrollPaymentStatuses.Paid, settled.Status);

        SetPermissions(client,
            "payroll.payment.reconcile,payroll.payment.view,payroll.salary.view," +
            "payroll.payment.view_sensitive");
        var reconciliationResponse = await client.PostAsJsonAsync(
            $"/api/payroll/v1/payments/{payment.Id}/reconcile",
            new ReconcilePayrollPaymentRequest(
                settled.Version, 0, "cross-artifact-reconciliation", 1,
                approved.Payable, new DateOnly(2026, 10, 11), PayrollPaymentMethods.Bank,
                "cross-artifact-bank-settlement"));
        Assert.Equal(HttpStatusCode.OK, reconciliationResponse.StatusCode);
        var reconciliation = (await reconciliationResponse.Content
            .ReadFromJsonAsync<PayrollPaymentReconciliationOperationResponse>())!;
        Assert.Equal(PayrollPaymentReconciliationStatuses.Reconciled,
            Assert.Single(reconciliation.Items).Status);

        QuestPDF.Settings.License = LicenseType.Community;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Equal(2, await db.PayrollPaymentBankFileVersions.CountAsync(
                row => row.PaymentBatchId == batch.Id));
            Assert.Equal(3, await db.AuditEvents.CountAsync(
                row => row.ActionType == "payroll.bank_file.generated"));
            Assert.Equal(1, await db.AuditEvents.CountAsync(
                row => row.ActionType == "payroll.payment_batch.submitted"));

            var result = await db.PayrollEmployeeResults.AsNoTracking()
                .SingleAsync(row => row.Id == approved.ResultId);
            var persistedPayment = await db.PayrollPayments.AsNoTracking()
                .SingleAsync(row => row.Id == payment.Id);
            var bankFile = await db.PayrollPaymentBankFileVersions.AsNoTracking()
                .SingleAsync(row => row.PaymentBatchId == batch.Id && row.VersionNo == 1);
            var persistedReconciliation = await db.PayrollPaymentReconciliations.AsNoTracking()
                .SingleAsync(row => row.PayrollPaymentId == payment.Id);
            Assert.Equal(result.NetPay, persistedPayment.PayableAmount);
            Assert.Equal(result.NetPay, bankFile.ExpectedAmount);
            Assert.Equal(persistedPayment.PaidAmount, persistedReconciliation.SettledAmount);
            Assert.Equal(result.NetPay, persistedReconciliation.ExpectedAmount);

            var payslipStore = scope.ServiceProvider.GetRequiredService<IPayrollPayslipStore>();
            var payslipRenderer = scope.ServiceProvider.GetRequiredService<IPayrollPayslipRenderer>();
            var payslipSource = await payslipStore.GetSourceAsync(
                1, approved.RunId, 1, CancellationToken.None);
            Assert.Equal(result.EmployeeCodeSnapshot, payslipSource.EmployeeCode);
            Assert.Equal(result.EmployeeNameSnapshot, payslipSource.EmployeeName);
            Assert.Equal(result.GrossEarnings, payslipSource.GrossEarnings);
            Assert.Equal(result.TotalDeductions, payslipSource.TotalDeductions);
            Assert.Equal(result.NetPay, payslipSource.NetPayable);
            Assert.Equal(persistedPayment.PaidAmount, payslipSource.Payment?.PaidAmount);
            Assert.Equal(persistedPayment.OutstandingAmount, payslipSource.Payment?.OutstandingAmount);
            Assert.Contains(PayrollPaymentReconciliationStatuses.Reconciled,
                payslipSource.Payment!.ReconciliationStatuses);

            var payslipPdf = payslipRenderer.Render(payslipSource, "payslip-v1");
            using var payslipDocument = PdfDocument.Open(payslipPdf);
            var payslipText = string.Join('\n',
                payslipDocument.GetPages().Select(page => page.Text));
            var formattedAmount = $"BDT {approved.Payable.ToString("N2", CultureInfo.InvariantCulture)}";
            Assert.Contains($"Net payable: {formattedAmount}", payslipText);
            Assert.Contains($"Paid: {formattedAmount}", payslipText);
            Assert.Contains("Outstanding: BDT 0.00", payslipText);
            Assert.Contains("Reconciliation: Reconciled", payslipText);

            var exportStore = scope.ServiceProvider.GetRequiredService<IPayrollExportStore>();
            var exportRenderer = scope.ServiceProvider.GetRequiredService<IPayrollExportRenderer>();
            var exportRun = await exportStore.FindRunAsync(1, approved.RunId, CancellationToken.None);
            Assert.NotNull(exportRun);
            var exportEmployees = await exportStore.GetEmployeesAsync(
                1, approved.RunId, exportRun!.Revision, null, true, CancellationToken.None);
            var exportEmployee = Assert.Single(exportEmployees);
            Assert.Equal(result.EmployeeCodeSnapshot, exportEmployee.EmployeeCode);
            Assert.Equal(result.EmployeeNameSnapshot, exportEmployee.EmployeeName);
            Assert.Equal(result.GrossEarnings, exportEmployee.GrossEarnings);
            Assert.Equal(result.TotalDeductions, exportEmployee.TotalDeductions);
            Assert.Equal(result.NetPay, exportEmployee.NetPayable);
            Assert.Equal(persistedPayment.PaidAmount, exportEmployee.Payment?.PaidAmount);
            Assert.Equal(persistedPayment.OutstandingAmount, exportEmployee.Payment?.OutstandingAmount);
            Assert.Contains(PayrollPaymentReconciliationStatuses.Reconciled,
                exportEmployee.Payment!.ReconciliationStatuses);
            var exportSource = new PayrollExportSource(
                exportRun,
                exportEmployees,
                true,
                exportEmployees.Sum(row => row.GrossEarnings),
                exportEmployees.Sum(row => row.TotalDeductions),
                exportEmployees.Sum(row => row.NetPayable),
                exportEmployees.Sum(row => row.Payment?.PaidAmount ?? 0m),
                exportEmployees.Sum(row => row.Payment?.OutstandingAmount ?? 0m),
                exportEmployees.Where(row => row.Payment is not null)
                    .GroupBy(row => row.Payment!.Status, StringComparer.Ordinal)
                    .Select(group => new PayrollExportStatusCount(group.Key, group.Count()))
                    .ToArray(),
                exportEmployees.Where(row => row.Payment is not null)
                    .SelectMany(row => row.Payment!.ReconciliationStatuses)
                    .GroupBy(status => status, StringComparer.Ordinal)
                    .Select(group => new PayrollExportStatusCount(group.Key, group.Count()))
                    .ToArray());
            Assert.Equal(result.GrossEarnings, exportSource.GrossTotal);
            Assert.Equal(result.TotalDeductions, exportSource.TotalDeductions);
            Assert.Equal(result.NetPay, exportSource.NetPayable);

            var renderer = exportRenderer;
            var csv = System.Text.Encoding.UTF8.GetString(renderer.RenderCsv(exportSource));
            Assert.Contains(approved.Payable.ToString(CultureInfo.InvariantCulture), csv);
            using var workbook = new XLWorkbook(new MemoryStream(renderer.RenderExcel(exportSource)));
            var sheet = workbook.Worksheet("Payroll Register");
            var netPayColumn = sheet.Row(1).CellsUsed()
                .Single(cell => cell.GetString() == "Net Payable").Address.ColumnNumber;
            Assert.Equal(approved.Payable, sheet.Cell(2, netPayColumn).GetValue<decimal>());
            Assert.Equal(approved.Payable,
                sheet.Cell(3, netPayColumn).GetValue<decimal>());

            var summaryPdf = renderer.RenderPdfSummary(exportSource);
            using var summaryDocument = PdfDocument.Open(summaryPdf);
            var summaryText = string.Join('\n',
                summaryDocument.GetPages().Select(page => page.Text));
            Assert.Contains("Net Payable", summaryText);
            Assert.Contains("Paid Total", summaryText);
            Assert.Equal(3, summaryText.Split(formattedAmount).Length - 1);
            Assert.Contains("Reconciled: 1", summaryText);

            var historicalCode = result.EmployeeCodeSnapshot;
            var historicalName = result.EmployeeNameSnapshot;
            var employee = await db.HrEmployees.SingleAsync(row => row.Id == result.EmployeeId);
            employee.EmployeeCode = "MUTATED-AFTER-APPROVAL";
            employee.DisplayName = "Changed after approval";
            await db.SaveChangesAsync();

            var payslipAfterMasterChange = await payslipStore.GetSourceAsync(
                1, approved.RunId, result.EmployeeId, CancellationToken.None);
            var exportAfterMasterChange = await exportStore.GetEmployeesAsync(
                1, approved.RunId, exportRun.Revision, null, true, CancellationToken.None);
            Assert.Equal(historicalCode, payslipAfterMasterChange.EmployeeCode);
            Assert.Equal(historicalName, payslipAfterMasterChange.EmployeeName);
            Assert.Equal(historicalCode, Assert.Single(exportAfterMasterChange).EmployeeCode);
            Assert.Equal(historicalName, Assert.Single(exportAfterMasterChange).EmployeeName);
            Assert.Equal(approved.Payable, payslipAfterMasterChange.NetPayable);
            Assert.Equal(approved.Payable, Assert.Single(exportAfterMasterChange).NetPayable);
        }
    }

    [Fact]
    public async Task PaymentBatchCreationAndReadEnforceApprovalBusinessAndPrimaryLocationOverHttp()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        var approved = await CreateApprovedRunAsync(factory, client);
        await ScopeUserToOutletAsync(factory.Services, 101, 10);
        SetRole(client, "HR");
        SetIdentity(client, 101, "payroll.payment.process,payroll.payment.view," +
            "payroll.salary.view");
        var paymentResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/payments",
            new CreatePayrollPaymentRequest(
                approved.ResultId, approved.RunVersion, "batch-scope-payment",
                [new PayrollPaymentAllocationInput(PayrollPaymentMethods.Cash, approved.Payable)]));
        Assert.Equal(HttpStatusCode.Created, paymentResponse.StatusCode);
        var payment = (await paymentResponse.Content
            .ReadFromJsonAsync<PayrollPaymentResponse>())!;
        var request = new CreatePayrollPaymentBatchRequest(
            "batch-scope-001", "batch-scope-key-001",
            [new PayrollPaymentBatchItemInput(payment.Id, payment.Version)]);
        SetPermissions(client, "payroll.payment.view,payroll.salary.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(
                "/api/payroll/v1/runs/1/payment-batches", request)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.PayrollPaymentBatches.ToArrayAsync());
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE payroll_runs SET status = {PayrollRunStatuses.Draft} WHERE id = {1}");
        }
        SetPermissions(client,
            "payroll.payment.process,payroll.payment.view,payroll.salary.view");
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsJsonAsync(
                "/api/payroll/v1/runs/1/payment-batches", request)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Empty(await db.PayrollPaymentBatches.ToArrayAsync());
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE payroll_runs SET status = {PayrollRunStatuses.Approved} WHERE id = {1}");
        }

        var createdResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/runs/1/payment-batches", request);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var created = (await createdResponse.Content
            .ReadFromJsonAsync<PayrollPaymentBatchResponse>())!;
        var replayResponse = await client.PostAsJsonAsync(
            "/api/payroll/v1/runs/1/payment-batches", request);
        Assert.Equal(HttpStatusCode.Created, replayResponse.StatusCode);
        Assert.True((await replayResponse.Content
            .ReadFromJsonAsync<PayrollPaymentBatchResponse>())!.IsReplay);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            "/api/payroll/v1/runs/1/payment-batches",
            request with { BatchReference = "different-reference" })).StatusCode);

        await AddAdditionalEmployeeLocationAndScopeUserAsync(factory.Services);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/payroll/v1/payment-batches/{created.Id}")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-BusinessId");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "2");
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/payroll/v1/payment-batches/{created.Id}")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
            Assert.Single(await db.PayrollPaymentBatches.ToArrayAsync());
            Assert.Equal(PayrollPaymentStatuses.Ready,
                (await db.PayrollPayments.SingleAsync(row => row.Id == payment.Id)).Status);
        }
    }

    private static async Task<ApprovedPayrollResult> CreateApprovedRunAsync(
        WebApplicationFactory<Program> factory,
        HttpClient client,
        string paymentDestinationMethod = PayrollPaymentMethods.Cash,
        string scopeType = "Business",
        long? scopeId = null)
    {
        await SeedAsync(factory.Services);
        await SeedReadyRunApprovalAsync(factory.Services, paymentDestinationMethod);
        Authorize(client, "payroll.run.create");
        const string route = "/api/payroll/v1/periods/1/runs";
        var create = await client.PostAsJsonAsync(
            route, new CreatePayrollRunRequest(scopeType, scopeId,
                $"payment-run-{paymentDestinationMethod}", 2));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var run = (await create.Content.ReadFromJsonAsync<PayrollRunResponse>())!;

        SetPermissions(client, "payroll.run.calculate");
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsync($"{route}/{run.Id}/calculate", null)).StatusCode);
        SetPermissions(client, "payroll.run.submit,payroll.salary.view");
        var submit = await client.PostAsJsonAsync(
            $"{route}/{run.Id}/submit", new SubmitPayrollRunRequest(run.Version + 1));
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var approval = (await submit.Content.ReadFromJsonAsync<ApprovalDecisionResult>())!;

        SetPermissions(client, "payroll.period.lock,payroll.period.view");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/payroll/v1/periods/1/lock", new PayrollPeriodLifecycleRequest(2))).StatusCode);
        SetIdentity(client, 202, "payroll.run.approve");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/payroll/v1/approvals/runs/{approval.InstanceId}/approve",
            new PayrollRunApprovalDecisionRequest(
                $"approve-payment-run-{paymentDestinationMethod}", "Approved"))).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var approvedRun = await db.PayrollRuns.AsNoTracking().SingleAsync(row => row.Id == run.Id);
        var result = await db.PayrollEmployeeResults.AsNoTracking()
            .SingleAsync(row => row.PayrollRunId == run.Id && row.EmployeeId == 1);
        return new ApprovedPayrollResult(
            run.Id, result.Id, approvedRun.Version, result.NetPay!.Value,
            result.CalculationSnapshotHash!);
    }

    private sealed record ApprovedPayrollResult(
        long RunId, long ResultId, long RunVersion, decimal Payable, string SnapshotHash);

    private static async Task ScopeUserToOutletAsync(
        IServiceProvider services, long userId, long outletId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var user = await db.Users.SingleAsync(row => row.Id == userId);
        user.OutletId = outletId;
        user.InventoryLocationAccessScope = User.InventoryAccessAssignedOnly;
        db.UserOutletAssignments.Add(new UserOutletAssignment
        {
            UserId = userId,
            BusinessId = user.BusinessId!.Value,
            OutletId = outletId,
            IsPrimary = true
        });
        await db.SaveChangesAsync();
    }

    private static async Task AddAdditionalEmployeeLocationAndScopeUserAsync(
        IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        db.Outlets.Add(new Outlet { Id = 20, BusinessId = 1, Name = "Additional outlet" });
        db.OperationalLocationProjections.Add(new OperationalLocationProjection
        {
            Id = 21,
            BusinessId = 1,
            SourceSystem = "RetailPOS",
            SourceEntityType = "Outlet",
            SourceEntityId = "20",
            Name = "Additional outlet",
            SourceVersion = 1
        });
        db.HrEmployeeLocationAssignments.Add(new HrEmployeeLocationAssignment
        {
            BusinessId = 1,
            EmployeeId = 1,
            OperationalLocationProjectionId = 21,
            AssignmentType = "Additional",
            EffectiveFrom = new(2026, 1, 1),
            LocationNameSnapshot = "Additional outlet",
            SourceSystemSnapshot = "RetailPOS",
            SourceEntityTypeSnapshot = "Outlet",
            SourceEntityIdSnapshot = "20",
            LocationSourceVersionSnapshot = 1,
            RequestedByUserId = 101,
            RequestedAtUtc = DateTime.UtcNow
        });
        var user = await db.Users.SingleAsync(row => row.Id == 101);
        user.OutletId = 20;
        user.InventoryLocationAccessScope = User.InventoryAccessAssignedOnly;
        db.UserOutletAssignments.Add(new UserOutletAssignment
        {
            UserId = user.Id,
            BusinessId = 1,
            OutletId = 20,
            IsPrimary = true
        });
        await db.SaveChangesAsync();
    }

    private static void SetRole(HttpClient client, string role)
    {
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
    }

    private static async Task SeedAsync(
        IServiceProvider services,
        bool hrEnabled = true,
        bool payrollEnabled = true,
        bool includeMonetaryConfiguration = true)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureCreatedAsync();
        await HrRosterPersistenceTests.Seed(db);
        if (includeMonetaryConfiguration)
            db.PayrollMonetaryConfigurationVersions.Add(new PayrollMonetaryConfigurationVersion
            {
                BusinessId = 1,
                VersionNo = 1,
                EffectiveFrom = new(2020, 1, 1),
                CurrencyCode = "BDT",
                DecimalPrecision = 2,
                RoundingMode = MidpointRounding.AwayFromZero.ToString(),
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByUserId = 101
            });
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
        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Actor",
            Email = "payroll-run@example.invalid",
            PasswordHash = "test",
            IsActive = true,
            InventoryLocationAccessScope = User.InventoryAccessAll
        });
        db.PayrollPeriods.Add(new PayrollPeriod
        {
            Id = 1, BusinessId = 1, PeriodCode = "2026-10",
            PeriodStart = new(2026, 10, 1), PeriodEnd = new(2026, 10, 31),
            Status = PayrollPeriodStatuses.Processing, Version = 2,
            CreatedAtUtc = DateTime.UtcNow, CreatedByUserId = 101,
            OpenedAtUtc = DateTime.UtcNow, OpenedByUserId = 101,
            ProcessingAtUtc = DateTime.UtcNow, ProcessingByUserId = 101
        });
        await db.SaveChangesAsync();
    }

    private sealed record PayrollResultEvidence(
        string? SnapshotHash, string InputHash, decimal? NetPay);

    private static async Task SeedReadyRunApprovalAsync(
        IServiceProvider services,
        string paymentDestinationMethod = PayrollPaymentDestinationMethods.Cash)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        var component = new PayrollSalaryComponent
        {
            BusinessId = 1, Code = "BASE_READY_RUN", Name = "Base salary",
            ComponentType = "Earning", ValueType = "Fixed", IsRecurring = true
        };
        var structure = new PayrollSalaryStructure
        {
            BusinessId = 1, Code = "STRUCT-READY-RUN", Name = "Standard"
        };
        var policy = new ApprovalPolicy
        {
            BusinessId = 1, Code = "PAYROLL-RUN-API", ProcessType = ApprovalProcessType.PayrollRun,
            Name = "Payroll run approval", VersionNo = 1, ScopeType = ApprovalScopeType.Business,
            EffectiveFrom = new(2020, 1, 1), IsActive = true
        };
        policy.Steps.Add(new ApprovalPolicyStep
        {
            Sequence = 1,
            ApproverType = ApprovalApproverType.User,
            ApproverReference = "202",
            RequiredApprovals = 1,
            CanReject = true,
            CanCancel = true
        });
        db.PayrollSalaryComponents.Add(component);
        db.PayrollSalaryStructures.Add(structure);
        db.ApprovalPolicies.Add(policy);
        db.Users.Add(new User
        {
            Id = 202, BusinessId = 1, Name = "Approver",
            Email = "payroll-approver@example.invalid", PasswordHash = "test", IsActive = true,
            InventoryLocationAccessScope = User.InventoryAccessAll
        });
        await db.SaveChangesAsync();

        var salaryApproval = new ApprovalInstance
        {
            BusinessId = 1,
            PolicyId = policy.Id,
            PolicyCodeSnapshot = policy.Code,
            PolicyVersionSnapshot = policy.VersionNo,
            ProcessType = ApprovalProcessType.SalarySetup,
            TargetType = "PayrollSalarySetupRequest",
            TargetId = "1",
            RequestedByUserId = 101,
            RequestedAtUtc = DateTime.UtcNow,
            Status = ApprovalInstanceStatus.Approved,
            CorrelationId = Guid.NewGuid(),
            ScopeType = ApprovalScopeType.Business,
            TargetVersion = 1,
            RequestHash = new string('A', 64)
        };
        db.ApprovalInstances.Add(salaryApproval);
        await db.SaveChangesAsync();

        var componentSnapshot = System.Text.Json.JsonSerializer.Serialize(
            new[]
            {
                new PayrollSalarySetupComponentSnapshot(
                    component.Id, component.Code, component.Name, component.ComponentType,
                    component.ValueType, null, true, 1, null, 1000m)
            },
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var salaryRequest = new PayrollSalarySetupRequest
        {
            BusinessId = 1,
            EmployeeId = 1,
            SalaryStructureId = structure.Id,
            SalaryStructureVersion = 1,
            EffectiveFrom = new(2026, 1, 1),
            OperationalLocationProjectionId = 10,
            StructureSnapshotJson = "{}",
            ComponentSnapshotJson = componentSnapshot,
            RequestHash = new string('B', 64),
            IdempotencyPayloadHash = new string('C', 64),
            IdempotencyKey = "ready-run-salary",
            Reason = "Approved test salary",
            Status = "Approved",
            ApprovalInstanceId = salaryApproval.Id,
            CreatedByUserId = 101,
            CreatedAtUtc = DateTime.UtcNow,
            DecidedByUserId = 202,
            DecidedAtUtc = DateTime.UtcNow,
            Version = 2
        };
        db.PayrollSalarySetupRequests.Add(salaryRequest);
        await db.SaveChangesAsync();
        db.PayrollEmployeeSalaryVersions.Add(new PayrollEmployeeSalaryVersion
        {
            BusinessId = 1,
            EmployeeId = 1,
            SalarySetupRequestId = salaryRequest.Id,
            SalaryStructureId = structure.Id,
            SalaryStructureVersion = 1,
            EffectiveFrom = new(2026, 1, 1),
            OperationalLocationProjectionId = 10,
            StructureSnapshotJson = "{}",
            ComponentSnapshotJson = componentSnapshot,
            ApprovalInstanceId = salaryApproval.Id,
            ApprovedByUserId = 202,
            ApprovedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        db.PayrollEmployeePaymentDestinations.Add(new PayrollEmployeePaymentDestination
        {
            BusinessId = 1, EmployeeId = 1, VersionNo = 1, Version = 1,
            OperationalLocationProjectionId = 10, Method = paymentDestinationMethod,
            IsPrimary = true, IsActive = true, EffectiveFrom = new(2026, 1, 1),
            BankName = paymentDestinationMethod == PayrollPaymentDestinationMethods.Bank
                ? "Bank One" : null,
            AccountName = paymentDestinationMethod == PayrollPaymentDestinationMethods.Bank
                ? "Payroll Account" : null,
            AccountNumber = paymentDestinationMethod == PayrollPaymentDestinationMethods.Bank
                ? "00123456789" : null,
            MobileProvider = paymentDestinationMethod == PayrollPaymentDestinationMethods.Mobile
                ? "Mobile One" : null,
            MobileAccountName = paymentDestinationMethod == PayrollPaymentDestinationMethods.Mobile
                ? "Payroll Wallet" : null,
            MobileAccountNumber = paymentDestinationMethod == PayrollPaymentDestinationMethods.Mobile
                ? "01700000000" : null,
            CreatedByUserId = 101, CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static void Authorize(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "Super Admin");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }

    private static void SetIdentity(HttpClient client, long userId, string permissions)
    {
        client.DefaultRequestHeaders.Remove("X-Test-UserId");
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
        SetPermissions(client, permissions);
    }

    private static void SetPermissions(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Remove("X-Test-Permissions");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }
}
