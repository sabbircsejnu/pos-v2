using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Tests.Identity;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;
using Xunit.Abstractions;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrDashboardApiTests
{
    private readonly ITestOutputHelper output;

    public HrDashboardApiTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public async Task DashboardRequiresPermissionAndHrCapabilityButNotPayrollOrAttendance()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await Seed(factory.Services, hrEnabled: true);
        Auth(client, "");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/hr/v1/dashboard?from=2026-10-01&to=2026-10-31")).StatusCode);

        client.DefaultRequestHeaders.Remove("X-Test-Permissions");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", "hr.dashboard.view");
        var response = await client.GetAsync("/api/hr/v1/dashboard?from=2026-10-01&to=2026-10-31");
        var payload = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, payload);
        Assert.Contains("\"totalEmployees\":1", payload);
        Assert.Contains("\"attendance\":[]", payload);
        Assert.DoesNotContain("salary", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bank", payload, StringComparison.OrdinalIgnoreCase);

        using var disabledFactory = new SqliteRoleGovernanceApiFactory();
        using var disabledClient = disabledFactory.CreateClient();
        await Seed(disabledFactory.Services, hrEnabled: false);
        Auth(disabledClient, "hr.dashboard.view");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await disabledClient.GetAsync("/api/hr/v1/dashboard?from=2026-10-01&to=2026-10-31")).StatusCode);
    }

    [Fact]
    public async Task DashboardRejectsInvalidDateRange()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await Seed(factory.Services, hrEnabled: true);
        Auth(client, "hr.dashboard.view");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/hr/v1/dashboard?from=2026-11-01&to=2026-10-01")).StatusCode);
    }

    [Fact]
    public async Task DashboardAggregatesOnlyAuthorizedLocationScope()
    {
        using var factory = new SqliteRoleGovernanceApiFactory();
        using var client = factory.CreateClient();
        await SeedScopedLocations(factory.Services);
        Auth(client, "hr.dashboard.view");

        var response = await client.GetAsync("/api/hr/v1/dashboard?from=2026-10-01&to=2026-10-31");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"totalEmployees\":1", payload);
        Assert.Contains("Outlet: Outlet 10", payload);
        Assert.DoesNotContain("Outlet 20", payload);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            "/api/hr/v1/dashboard?from=2026-10-01&to=2026-10-31&locationProjectionId=20")).StatusCode);

        var typeFiltered = await client.GetAsync(
            "/api/hr/v1/dashboard?from=2026-10-01&to=2026-10-31&locationType=Warehouse");
        Assert.Equal(HttpStatusCode.OK, typeFiltered.StatusCode);
        Assert.Contains("\"totalEmployees\":0", await typeFiltered.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DashboardAggregateQueryCountAndSqlExecutionTimeAreMeasured()
    {
        var measurements = new DashboardQueryMeasurementInterceptor();
        using var factory = new SqliteRoleGovernanceApiFactory
        {
            AdditionalDbCommandInterceptor = measurements
        };
        using var client = factory.CreateClient();
        await Seed(factory.Services, hrEnabled: true);
        Auth(client, "hr.dashboard.view");
        measurements.Reset();

        var requestTimer = Stopwatch.StartNew();
        var response = await client.GetAsync("/api/hr/v1/dashboard?from=2026-10-01&to=2026-10-31");
        requestTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var snapshot = measurements.Snapshot();
        output.WriteLine(
            $"Dashboard SQL: {snapshot.CommandCount} commands; cumulative execution {snapshot.Cumulative.TotalMilliseconds:F2} ms; "
            + $"slowest HR data query {snapshot.SlowestDataQuery.TotalMilliseconds:F2} ms; "
            + $"HTTP {requestTimer.Elapsed.TotalMilliseconds:F2} ms.");
        output.WriteLine($"Slowest HR data SQL: {snapshot.SlowestDataCommand}");
        Assert.InRange(snapshot.CommandCount, 1, 25);
        Assert.NotNull(snapshot.SlowestDataCommand);
    }

    [Fact]
    public async Task CoreHrReportAndExportQueryMeasurementsAreCaptured()
    {
        var measurements = new DashboardQueryMeasurementInterceptor();
        using var factory = new SqliteRoleGovernanceApiFactory
        {
            AdditionalDbCommandInterceptor = measurements
        };
        using var client = factory.CreateClient();
        await Seed(factory.Services, hrEnabled: true);
        Auth(client, "hr.report.view");
        const string query =
            "kind=EmployeeRoster&from=2026-01-01&to=2026-12-31";

        measurements.Reset();
        var queryTimer = Stopwatch.StartNew();
        using var report = await client.GetAsync($"/api/hr/v1/reports/core?{query}");
        queryTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var reportBytes = await report.Content.ReadAsByteArrayAsync();
        WriteMeasurement("HR core report", measurements.Snapshot(), queryTimer.Elapsed,
            reportBytes.Length);

        measurements.Reset();
        var exportTimer = Stopwatch.StartNew();
        using var export = await client.GetAsync($"/api/hr/v1/reports/core/export/csv?{query}");
        exportTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exportBytes = await export.Content.ReadAsByteArrayAsync();
        WriteMeasurement("HR core CSV export", measurements.Snapshot(), exportTimer.Elapsed,
            exportBytes.Length);
    }

    [Fact]
    public async Task PostgresDashboardAndCoreReportQueryMeasurementsAreCaptured()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API",
            "appsettings.json"))).Build();
        var baseConnectionString = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES")
            ?? configuration.GetConnectionString("DefaultConnection");
        Assert.False(string.IsNullOrWhiteSpace(baseConnectionString));

        await using var database = await PostgresTestDatabase.CreateAsync(
            baseConnectionString!, PostgresTestDatabasePrefix.MigrationBaseline);
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString).Options;
        await using (var migrationDb = new RetailPOSDbContext(options))
            await migrationDb.Database.MigrateAsync();

        var measurements = new DashboardQueryMeasurementInterceptor();
        using var factory = new PostgresMeasurementApiFactory(
            database.ConnectionString, measurements);
        using var client = factory.CreateClient();
        await Seed(factory.Services, hrEnabled: true);
        Auth(client, "hr.dashboard.view,hr.report.view");

        measurements.Reset();
        var dashboardTimer = Stopwatch.StartNew();
        using var dashboard = await client.GetAsync(
            "/api/hr/v1/dashboard?from=2026-01-01&to=2026-12-31");
        dashboardTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        var dashboardBytes = await dashboard.Content.ReadAsByteArrayAsync();
        WriteMeasurement("HR dashboard PostgreSQL", measurements.Snapshot(),
            dashboardTimer.Elapsed, dashboardBytes.Length, "PostgreSQL");

        measurements.Reset();
        var reportTimer = Stopwatch.StartNew();
        using var report = await client.GetAsync(
            "/api/hr/v1/reports/core?kind=EmployeeRoster&from=2026-01-01&to=2026-12-31");
        reportTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var reportBytes = await report.Content.ReadAsByteArrayAsync();
        WriteMeasurement("HR core report PostgreSQL", measurements.Snapshot(),
            reportTimer.Elapsed, reportBytes.Length, "PostgreSQL");

        measurements.Reset();
        var exportTimer = Stopwatch.StartNew();
        using var export = await client.GetAsync(
            "/api/hr/v1/reports/core/export/csv?kind=EmployeeRoster&from=2026-01-01&to=2026-12-31");
        exportTimer.Stop();
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var exportBytes = await export.Content.ReadAsByteArrayAsync();
        WriteMeasurement("HR core CSV export PostgreSQL", measurements.Snapshot(),
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

    private static void Auth(HttpClient client, string permissions)
    {
        client.DefaultRequestHeaders.Add("X-Test-UserId", "101");
        client.DefaultRequestHeaders.Add("X-Test-BusinessId", "1");
        client.DefaultRequestHeaders.Add("X-Test-Role", "HR");
        client.DefaultRequestHeaders.Add("X-Test-Permissions", permissions);
    }

    private static async Task Seed(IServiceProvider services, bool hrEnabled)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.Add(new Business { Id = 1, Name = "Dashboard" });
        db.BusinessFeatureSettings.AddRange(
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "hr.enabled", IsEnabled = hrEnabled },
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "attendance.enabled", IsEnabled = false },
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "payroll.enabled", IsEnabled = false });
        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Actor",
            Email = "actor@example.invalid",
            PasswordHash = "test",
            IsActive = true,
            InventoryLocationAccessScope = User.InventoryAccessAll
        });
        db.HrEmploymentTypes.Add(new HrEmploymentType
        {
            Id = 1, BusinessId = 1, Code = "PERM", Name = "Permanent"
        });
        db.HrEmployees.Add(new HrEmployee
        {
            Id = 1,
            BusinessId = 1,
            EmployeeCode = "EMP-1",
            FirstName = "Test",
            DisplayName = "Test Employee",
            CurrentEmploymentTypeId = 1,
            JoiningDate = new DateOnly(2026, 1, 1)
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedScopedLocations(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Businesses.Add(new Business { Id = 1, Name = "Dashboard" });
        db.BusinessFeatureSettings.AddRange(
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "hr.enabled", IsEnabled = true },
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "attendance.enabled", IsEnabled = false },
            new BusinessFeatureSetting { BusinessId = 1, FeatureKey = "payroll.enabled", IsEnabled = false });
        db.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Actor",
            Email = "actor@example.invalid",
            PasswordHash = "test",
            IsActive = true,
            InventoryLocationAccessScope = User.InventoryAccessSpecific
        });
        db.Outlets.AddRange(
            new Outlet { Id = 10, BusinessId = 1, Name = "Outlet 10" },
            new Outlet { Id = 20, BusinessId = 1, Name = "Outlet 20" });
        db.OperationalLocationProjections.AddRange(
            Projection(10, "Outlet 10"),
            Projection(20, "Outlet 20"));
        db.UserOutletAssignments.Add(new UserOutletAssignment
        {
            BusinessId = 1, UserId = 101, OutletId = 10, IsActive = true
        });
        db.HrEmploymentTypes.Add(new HrEmploymentType
        {
            Id = 1, BusinessId = 1, Code = "PERM", Name = "Permanent"
        });
        db.HrEmployees.AddRange(
            new HrEmployee
            {
                Id = 1, BusinessId = 1, EmployeeCode = "EMP-10", FirstName = "Visible",
                DisplayName = "Visible Employee", CurrentEmploymentTypeId = 1,
                CurrentPrimaryLocationProjectionId = 10, JoiningDate = new DateOnly(2026, 1, 1)
            },
            new HrEmployee
            {
                Id = 2, BusinessId = 1, EmployeeCode = "EMP-20", FirstName = "Hidden",
                DisplayName = "Hidden Employee", CurrentEmploymentTypeId = 1,
                CurrentPrimaryLocationProjectionId = 20, JoiningDate = new DateOnly(2026, 1, 1)
            });
        await db.SaveChangesAsync();
    }

    private static OperationalLocationProjection Projection(long id, string name) => new()
    {
        Id = id, BusinessId = 1, SourceSystem = "RetailPOS", SourceEntityType = "Outlet",
        SourceEntityId = id.ToString(), Name = name, SourceVersion = 1
    };
}

internal sealed class PostgresMeasurementApiFactory(
    string connectionString,
    IInterceptor measurementInterceptor) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        TestJwtConfiguration.Configure(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<RetailPOSDbContext>>();
            services.RemoveAll<RetailPOSDbContext>();
            services.RemoveAll(typeof(IDbContextOptionsConfiguration<RetailPOSDbContext>));
            services.AddDbContext<RetailPOSDbContext>((serviceProvider, options) =>
                options.UseNpgsql(connectionString)
                    .AddInterceptors(
                        serviceProvider.GetRequiredService<AuditChangeTrackerInterceptor>(),
                        measurementInterceptor));
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                TestAuthHandler.SchemeName, _ => { });
        });
    }
}

internal sealed class DashboardQueryMeasurementInterceptor : DbCommandInterceptor
{
    private readonly ConcurrentDictionary<DbCommand, long> startedAt = new();
    private readonly object gate = new();
    private long commandCount;
    private long cumulativeTimestampTicks;
    private long slowestDataTimestampTicks;
    private string? slowestDataCommand;

    public void Reset()
    {
        startedAt.Clear();
        lock (gate)
        {
            commandCount = 0;
            cumulativeTimestampTicks = 0;
            slowestDataTimestampTicks = 0;
            slowestDataCommand = null;
        }
    }

    public (long CommandCount, TimeSpan Cumulative, TimeSpan SlowestDataQuery, string? SlowestDataCommand) Snapshot()
    {
        lock (gate)
        {
            return (
                commandCount,
                TimeSpan.FromSeconds(cumulativeTimestampTicks / (double)Stopwatch.Frequency),
                TimeSpan.FromSeconds(slowestDataTimestampTicks / (double)Stopwatch.Frequency),
                slowestDataCommand);
        }
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Begin(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        End(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Begin(command);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        End(command);
        return ValueTask.FromResult(result);
    }

    private void Begin(DbCommand command) => startedAt[command] = Stopwatch.GetTimestamp();

    private void End(DbCommand command)
    {
        if (!startedAt.TryRemove(command, out var start))
            return;
        var elapsed = Stopwatch.GetTimestamp() - start;
        lock (gate)
        {
            commandCount++;
            cumulativeTimestampTicks += elapsed;
            if (IsDashboardDataQuery(command.CommandText) && elapsed > slowestDataTimestampTicks)
            {
                slowestDataTimestampTicks = elapsed;
                var normalizedCommand = string.Join(" ", command.CommandText.Split(
                    (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                slowestDataCommand = normalizedCommand.Length <= 240
                    ? normalizedCommand
                    : normalizedCommand[..240];
            }
        }
    }

    private static bool IsDashboardDataQuery(string commandText) =>
        commandText.Contains("payroll_", StringComparison.OrdinalIgnoreCase)
        || commandText.Contains("Payroll", StringComparison.OrdinalIgnoreCase)
        || commandText.Contains("hr_employees", StringComparison.Ordinal)
        || commandText.Contains("hr_daily_attendance", StringComparison.Ordinal)
        || commandText.Contains("hr_leave_requests", StringComparison.Ordinal)
        || commandText.Contains("hr_overtime_entries", StringComparison.Ordinal)
        || commandText.Contains("hr_employee_documents", StringComparison.Ordinal)
        || commandText.Contains("hr_exit_cases", StringComparison.Ordinal)
        || commandText.Contains("hr_clearance_items", StringComparison.Ordinal);
}
