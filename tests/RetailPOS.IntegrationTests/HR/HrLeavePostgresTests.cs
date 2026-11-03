using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RetailPOS.Application.HR.Leave;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.HR.Leave;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.HR;

public sealed class HrLeavePostgresTests
{
    [Fact]
    public async Task ConcurrentDuplicateLedgerWritesCreateOneImmutableEntry()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var configured = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured))
        {
            var settings = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API", "appsettings.json"));
            configured = new ConfigurationBuilder().AddJsonFile(settings, optional: false)
                .Build().GetConnectionString("DefaultConnection");
        }
        Assert.False(string.IsNullOrWhiteSpace(configured));
        await using var database = await PostgresTestDatabase.CreateAsync(
            configured!, PostgresTestDatabasePrefix.MigrationBaseline);
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString).Options;
        long leaveTypeId;
        HrLeavePolicyVersion policy;
        await using (var setup = new RetailPOSDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Businesses.Add(new Business { Id = 1, Name = "Leave concurrency" });
            setup.HrEmploymentTypes.Add(new HrEmploymentType
            {
                Id = 1, BusinessId = 1, Code = "PERM", Name = "Permanent"
            });
            setup.HrEmployees.Add(new HrEmployee
            {
                Id = 1, BusinessId = 1, EmployeeCode = "E-1", FirstName = "A",
                DisplayName = "A", CurrentEmploymentTypeId = 1, JoiningDate = new DateOnly(2025, 1, 1)
            });
            await setup.SaveChangesAsync();
            var store = new HrLeaveStore(setup);
            var type = await store.CreateTypeAsync(1, new("ANNUAL", "Annual", true, false), default);
            leaveTypeId = type.Id;
            var policyModel = await store.CreatePolicyVersionAsync(1,
                new(type.Id, "STANDARD", "Standard", HrLeavePolicyScopeType.Business, null, 1,
                    HrLeaveAccrualMethod.AnnualGrantAtLeaveYearStart, 12m, false, null, null,
                    null, false, null, 1, 1, new DateOnly(2025, 1, 1), null, true), default);
            policy = await setup.HrLeavePolicyVersions.AsNoTracking()
                .SingleAsync(row => row.Id == policyModel.Id);
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HrLeaveLedgerWriteResult> PostAsync()
        {
            await using var context = new RetailPOSDbContext(options);
            var store = new HrLeaveStore(context);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await start.Task;
            var result = await store.PostLedgerEntriesAsync(1, 1, leaveTypeId,
                new(policy, new DateOnly(2025, 1, 1), null, null), [OpeningWrite()], 101, default);
            await transaction.CommitAsync();
            return result;
        }

        var first = PostAsync();
        var second = PostAsync();
        start.SetResult();
        var outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, result => !result.IsReplay);
        Assert.Single(outcomes, result => result.IsReplay);
        Assert.Equal(outcomes[0].Entries.Single().Id, outcomes[1].Entries.Single().Id);
        await using var verify = new RetailPOSDbContext(options);
        Assert.Single(await verify.HrLeaveLedgerEntries.AsNoTracking().ToListAsync());
        Assert.Equal(2, await verify.HrLeaveEntitlements.AsNoTracking()
            .Select(row => row.Version).SingleAsync());
    }

    [Fact]
    public async Task ConcurrentLeaveYearReviewAndCloseReplayWithoutDuplicateEvidence()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var configured = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(configured))
        {
            var settings = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RetailPOS.API", "appsettings.json"));
            configured = new ConfigurationBuilder().AddJsonFile(settings, optional: false)
                .Build().GetConnectionString("DefaultConnection");
        }
        Assert.False(string.IsNullOrWhiteSpace(configured));
        await using var database = await PostgresTestDatabase.CreateAsync(
            configured!, PostgresTestDatabasePrefix.MigrationBaseline);
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(database.ConnectionString).Options;
        const long businessId = 1;
        const long employeeId = 1;
        const long actorId = 101;
        var leaveYearStart = new DateOnly(2025, 1, 1);
        long leaveTypeId;
        long entitlementVersion;

        await using (var setup = new RetailPOSDbContext(options))
        {
            await setup.Database.MigrateAsync();
            setup.Businesses.Add(new Business { Id = businessId, Name = "Leave close concurrency" });
            setup.HrEmploymentTypes.Add(new HrEmploymentType
            {
                Id = 1, BusinessId = businessId, Code = "PERM", Name = "Permanent"
            });
            setup.HrEmployees.Add(new HrEmployee
            {
                Id = employeeId, BusinessId = businessId, EmployeeCode = "E-1",
                FirstName = "A", DisplayName = "A", CurrentEmploymentTypeId = 1,
                JoiningDate = new DateOnly(2020, 1, 1)
            });
            await setup.SaveChangesAsync();
            var store = new HrLeaveStore(setup);
            var type = await store.CreateTypeAsync(
                businessId, new("ANNUAL", "Annual", true, false), default);
            leaveTypeId = type.Id;
            await store.CreatePolicyVersionAsync(businessId,
                new(type.Id, "STANDARD", "Standard", HrLeavePolicyScopeType.Business, null, 1,
                    HrLeaveAccrualMethod.AnnualGrantAtLeaveYearStart, 12m,
                    true, 3m, 6, null, false, null, 1, 1,
                    new DateOnly(2025, 1, 1), null, true), default);
            var context = await store.ResolvePolicyAsync(
                businessId, employeeId, leaveTypeId, leaveYearStart, default);
            await using (var transaction = await setup.Database.BeginTransactionAsync())
            {
                await store.PostLedgerEntriesAsync(
                    businessId, employeeId, leaveTypeId, context,
                    [OpeningWrite(leaveYearStart)], actorId, default);
                await transaction.CommitAsync();
            }
            entitlementVersion = (await store.GetBalancesAsync(
                businessId, employeeId, leaveTypeId, default)).Single().Version;
        }

        static async Task<T[]> RunConcurrently<T>(Func<Task<T>> operation)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<T> Run()
            {
                await start.Task;
                return await operation();
            }
            var first = Run();
            var second = Run();
            start.SetResult();
            return await Task.WhenAll(first, second);
        }

        async Task<HrLeaveYearLifecycleModel> ReviewAsync()
        {
            await using var context = new RetailPOSDbContext(options);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var result = await new HrLeaveStore(context).ReviewLeaveYearCloseAsync(
                businessId, employeeId, leaveTypeId, leaveYearStart,
                entitlementVersion, "concurrent-review", actorId, default);
            await transaction.CommitAsync();
            return result;
        }

        var reviews = await RunConcurrently(ReviewAsync);
        var review = Assert.Single(reviews, result => !result.IsReplay);
        Assert.Single(reviews, result => result.IsReplay);

        async Task<HrLeaveYearLifecycleModel> CloseAsync()
        {
            await using var context = new RetailPOSDbContext(options);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var result = await new HrLeaveStore(context).CloseLeaveYearAsync(
                businessId, employeeId, leaveTypeId, leaveYearStart,
                review.Version, "concurrent-close", actorId, default);
            await transaction.CommitAsync();
            return result;
        }

        var closes = await RunConcurrently(CloseAsync);
        Assert.Single(closes, result => !result.IsReplay);
        Assert.Single(closes, result => result.IsReplay);
        await using var verify = new RetailPOSDbContext(options);
        Assert.Equal(2, await verify.HrLeaveYearLifecycleEvents.AsNoTracking().CountAsync());
        Assert.Single(await verify.HrLeaveLedgerEntries.AsNoTracking()
            .Where(row => row.TransactionType == HrLeaveLedgerTransactionType.CarryForward).ToListAsync());
        Assert.Equal("Closed", (await new HrLeaveStore(verify).GetBalancesAsync(
            businessId, employeeId, leaveTypeId, default)).Single(row => row.LeaveYearStart == leaveYearStart).Status);
    }

    private static HrLeaveLedgerWrite OpeningWrite(DateOnly? effectiveDate = null)
    {
        const string key = "leave-open-1";
        var date = effectiveDate ?? new DateOnly(2026, 1, 1);
        var payload = $"Opening|5|{date:yyyy-MM-dd}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(payload)));
        return new(HrLeaveLedgerTransactionType.Opening, 5m, date,
            "Manual", key, key, hash, "Opening balance");
    }
}
