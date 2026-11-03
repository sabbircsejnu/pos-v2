using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.StockCounts;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

public sealed class StockCountAdjustmentGenerationPostgresConcurrencyTests
{
    static StockCountAdjustmentGenerationPostgresConcurrencyTests()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    [Fact]
    public async Task ConcurrentDraftInsert_AfterBothRequestsObserveNoDraft_PersistsOneSourceAdjustment()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.StockCountAdjustmentGeneration,
            async database =>
            {
                long userId;
                long outletId;
                long stockCountId;
                await using (var setup = CreateDbContext(database.ConnectionString))
                {
                    await setup.Database.MigrateAsync();
                    await DbSeeder.SeedAsync(setup);
                    userId = await setup.Users.OrderBy(user => user.Id).Select(user => user.Id).FirstAsync();
                    outletId = await setup.Outlets.OrderBy(outlet => outlet.Id).Select(outlet => outlet.Id).FirstAsync();
                    var businessId = await setup.Businesses.OrderBy(business => business.Id)
                        .Select(business => business.Id).FirstAsync();
                    var stockCount = new StockCount
                    {
                        StockCountNo = $"SC-{Guid.NewGuid():N}",
                        BusinessId = businessId,
                        LocationId = outletId,
                        LocationType = "outlet",
                        StockCountDate = new DateTime(2026, 10, 5),
                        Status = StockCount.StatusApproved,
                        TotalItems = 1,
                        CreatedBy = userId,
                        CreatedAt = DateTime.UtcNow,
                        ApprovedBy = userId,
                        ApprovedAt = DateTime.UtcNow
                    };
                    setup.StockCounts.Add(stockCount);
                    await setup.SaveChangesAsync();
                    stockCountId = stockCount.Id;
                }

                var barrier = new ExistingDraftObservationBarrier();
                async Task<(bool Inserted, bool UniqueConflict)> TryInsertAsync()
                {
                    await using var context = CreateDbContext(database.ConnectionString, barrier);
                    Assert.False(await context.StockAdjustments
                        .AnyAsync(adjustment => adjustment.SourceStockCountId == stockCountId));
                    context.StockAdjustments.Add(new StockAdjustment
                    {
                        AdjustmentNumber = $"ADJ-{Guid.NewGuid():N}"[..30],
                        Status = StockAdjustment.StatusDraft,
                        LocationId = outletId,
                        LocationType = "outlet",
                        SourceStockCountId = stockCountId,
                        AdjustedBy = userId,
                        AdjustmentDate = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });

                    try
                    {
                        await context.SaveChangesAsync();
                        return (true, false);
                    }
                    catch (DbUpdateException exception)
                    {
                        var postgresException = FindPostgresException(exception);
                        return (false, postgresException?.SqlState == PostgresErrorCodes.UniqueViolation
                            && postgresException.ConstraintName == "IX_stock_adjustments_source_stock_count_id");
                    }
                }

                var results = await Task.WhenAll(TryInsertAsync(), TryInsertAsync());
                Assert.True(barrier.Observations >= 2);
                Assert.Single(results, result => result.Inserted);
                Assert.Single(results, result => result.UniqueConflict);

                await using var verify = CreateDbContext(database.ConnectionString);
                Assert.Single(await verify.StockAdjustments
                    .Where(adjustment => adjustment.SourceStockCountId == stockCountId)
                    .ToListAsync());
            });
    }

    [Fact]
    public async Task ConcurrentGenerationRequests_CreateOneDraftAndTransitionTheSourceOnce()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.StockCountAdjustmentGeneration,
            async database =>
            {
                long userId;
                long outletId;
                long stockCountId;
                int currentQuantity;
                await using (var setup = CreateDbContext(database.ConnectionString))
                {
                    await setup.Database.MigrateAsync();
                    await DbSeeder.SeedAsync(setup);
                    userId = await setup.Users.OrderBy(user => user.Id).Select(user => user.Id).FirstAsync();
                    outletId = await setup.Outlets.OrderBy(outlet => outlet.Id).Select(outlet => outlet.Id).FirstAsync();
                    var businessId = await setup.Businesses.OrderBy(business => business.Id)
                        .Select(business => business.Id).FirstAsync();
                    var inventory = await setup.Inventories.FirstAsync(
                        item => item.LocationId == outletId && item.LocationType == "outlet");
                    currentQuantity = inventory.Quantity;
                    var variant = await setup.ProductVariants
                        .Include(item => item.Product)
                        .SingleAsync(item => item.Id == inventory.VariantId);
                    var now = DateTime.UtcNow;
                    var stockCount = new StockCount
                    {
                        StockCountNo = $"SC-{Guid.NewGuid():N}",
                        BusinessId = businessId,
                        LocationId = outletId,
                        LocationType = "outlet",
                        StockCountDate = new DateTime(2026, 10, 5),
                        Status = StockCount.StatusApproved,
                        TotalItems = 1,
                        CreatedBy = userId,
                        CreatedAt = now,
                        ApprovedBy = userId,
                        ApprovedAt = now,
                        Lines =
                        [
                            new StockCountLine
                            {
                                ProductId = variant.ProductId,
                                VariantId = variant.Id,
                                ProductName = variant.Product.Name,
                                ProductCode = "PG-ADJUSTMENT-TEST",
                                VariantName = variant.Name,
                                CurrentStock = inventory.Quantity,
                                PhysicalStock = inventory.Quantity + 1,
                                Difference = 1
                            }
                        ]
                    };
                    setup.StockCounts.Add(stockCount);
                    await setup.SaveChangesAsync();
                    stockCountId = stockCount.Id;
                }

                async Task<(bool Generated, string? Error)> GenerateAsync()
                {
                    await using var context = CreateDbContext(database.ConnectionString);
                    var store = new StockCountAdjustmentDraftStore(
                        context,
                        NullLogger<StockCountAdjustmentDraftStore>.Instance);
                    try
                    {
                        await store.GenerateAsync(
                            stockCountId,
                            await context.StockCounts.Where(item => item.Id == stockCountId)
                                .Select(item => item.BusinessId).SingleAsync(),
                            outletId,
                            "outlet",
                            userId,
                            CancellationToken.None);
                        return (true, null);
                    }
                    catch (InvalidOperationException exception)
                    {
                        return (false, exception.Message);
                    }
                }

                async Task SetDifferenceAsync(decimal difference)
                {
                    await using var context = CreateDbContext(database.ConnectionString);
                    var line = await context.StockCountLines.SingleAsync(
                        item => item.StockCountId == stockCountId);
                    line.Difference = difference;
                    await context.SaveChangesAsync();
                }

                await SetDifferenceAsync(1.5m);
                var fractionalResult = await GenerateAsync();
                Assert.False(fractionalResult.Generated);
                Assert.Contains("fractional quantity", fractionalResult.Error, StringComparison.OrdinalIgnoreCase);

                await SetDifferenceAsync(-(currentQuantity + 1m));
                var negativeResult = await GenerateAsync();
                Assert.False(negativeResult.Generated);
                Assert.Contains("inventory negative", negativeResult.Error, StringComparison.OrdinalIgnoreCase);

                await SetDifferenceAsync(1m);
                var results = await Task.WhenAll(GenerateAsync(), GenerateAsync());
                Assert.Single(results, result => result.Generated);
                Assert.Equal(
                    "Stock adjustment draft can only be generated from Approved stock counts.",
                    Assert.Single(results, result => !result.Generated).Error);

                await using var verify = CreateDbContext(database.ConnectionString);
                var persistedCount = await verify.StockCounts.SingleAsync(item => item.Id == stockCountId);
                Assert.Equal(StockCount.StatusAdjustmentGenerated, persistedCount.Status);
                var adjustment = await verify.StockAdjustments
                    .Include(item => item.Lines)
                    .SingleAsync(item => item.SourceStockCountId == stockCountId);
                var line = Assert.Single(adjustment.Lines);
                Assert.Equal(1, line.QuantityChange);
                Assert.Equal("StockCountCorrection", line.Reason);
            });
    }

    private static RetailPOSDbContext CreateDbContext(
        string connectionString,
        ExistingDraftObservationBarrier? interceptor = null)
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

    private static PostgresException? FindPostgresException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgresException)
                return postgresException;
        }

        return null;
    }

    private sealed class ExistingDraftObservationBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _observations;

        public int Observations => Volatile.Read(ref _observations);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("stock_adjustments", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("source_stock_count_id", StringComparison.OrdinalIgnoreCase))
            {
                var observation = Interlocked.Increment(ref _observations);
                if (observation == 2)
                    _bothObserved.TrySetResult();

                if (observation <= 2)
                    await _bothObserved.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }

            return result;
        }
    }
}
