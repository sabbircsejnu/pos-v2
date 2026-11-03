using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using RetailPOS.Application.Inventory.StockCounts;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.StockCounts;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

public sealed class StockCountCreatePostgresConcurrencyTests
{
    static StockCountCreatePostgresConcurrencyTests()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    [Fact]
    public async Task ConcurrentCreate_AfterBothRequestsObserveNoActiveCount_PersistsOneHeaderAndSnapshot()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.StockCountCreate,
            async database =>
            {
            long businessId;
            long outletId;
            long userId;
            await using (var setup = CreateDbContext(database.ConnectionString))
            {
                await setup.Database.MigrateAsync();
                await DbSeeder.SeedAsync(setup);
                businessId = await setup.Businesses
                    .OrderBy(business => business.Id)
                    .Select(business => business.Id)
                    .FirstAsync();
                outletId = await setup.Outlets
                    .OrderBy(outlet => outlet.Id)
                    .Select(outlet => outlet.Id)
                    .FirstAsync();
                userId = await setup.Users
                    .OrderBy(user => user.Id)
                    .Select(user => user.Id)
                    .FirstAsync();
            }

            var interceptor = new StockCountCreateBarrierInterceptor();

            async Task<(string? Error, long? Id)> CreateAsync()
            {
                await using var context = CreateDbContext(database.ConnectionString, interceptor);
                var store = new StockCountCreationStore(context);
                try
                {
                    var id = await store.CreateAsync(
                        new StockCountCreateInput(
                            businessId,
                            outletId,
                            "outlet",
                            new DateTime(2026, 10, 5),
                            null,
                            userId),
                        CancellationToken.None);
                    return (null, id);
                }
                catch (InvalidOperationException exception)
                {
                    return (exception.Message, null);
                }
            }

            var outcomes = await Task.WhenAll(CreateAsync(), CreateAsync());
            Assert.True(interceptor.ActiveCountObservations >= 2);
            Assert.True(interceptor.NumberCountObservations >= 2);
            _ = Assert.Single(outcomes, outcome => outcome.Id.HasValue);
            var failedOutcome = Assert.Single(outcomes, outcome => outcome.Error is not null);
            Assert.Equal("An active stock count already exists for this location.", failedOutcome.Error);

            await using var verify = CreateDbContext(database.ConnectionString);
            var persisted = await verify.StockCounts
                .Include(stockCount => stockCount.Lines)
                .Where(stockCount =>
                    stockCount.BusinessId == businessId
                    && stockCount.LocationId == outletId
                    && stockCount.LocationType == "outlet"
                    && stockCount.Status == StockCount.StatusDraft)
                .ToListAsync();
            var stockCount = Assert.Single(persisted);
            Assert.Equal(stockCount.Lines.Count, stockCount.TotalItems);
            Assert.NotEmpty(stockCount.Lines);
            Assert.Single(persisted.Select(item => item.StockCountNo).Distinct(StringComparer.Ordinal));
            });
    }

    private static RetailPOSDbContext CreateDbContext(
        string connectionString,
        StockCountCreateBarrierInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString)
            .AddInterceptors(interceptor is null ? [] : [interceptor])
            .ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new RetailPOSDbContext(options);
    }

    private static string ResolveBaseConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        return new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build()
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("RetailPOS test PostgreSQL connection string is not configured.");
    }

    private sealed class StockCountCreateBarrierInterceptor : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _activeReaders = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _numberReaders = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeCountObservations;
        private int _numberCountObservations;

        public int ActiveCountObservations => Volatile.Read(ref _activeCountObservations);
        public int NumberCountObservations => Volatile.Read(ref _numberCountObservations);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            var sql = command.CommandText;
            if (sql.Contains("EXISTS", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("stock_counts", StringComparison.OrdinalIgnoreCase))
            {
                var observation = Interlocked.Increment(ref _activeCountObservations);
                if (observation == 2)
                {
                    _activeReaders.TrySetResult();
                }

                if (observation <= 2)
                {
                    await _activeReaders.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                }
            }
            else if (sql.Contains("COUNT", StringComparison.OrdinalIgnoreCase)
                && sql.Contains("stock_count_no", StringComparison.OrdinalIgnoreCase))
            {
                var observation = Interlocked.Increment(ref _numberCountObservations);
                if (observation == 2)
                {
                    _numberReaders.TrySetResult();
                }

                if (observation <= 2)
                {
                    await _numberReaders.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                }
            }

            return result;
        }
    }
}
