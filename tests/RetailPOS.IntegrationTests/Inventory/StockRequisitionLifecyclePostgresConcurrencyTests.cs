using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ReadStores;
using RetailPOS.Infrastructure.StockRequisitions;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

public sealed class StockRequisitionLifecyclePostgresConcurrencyTests
{
    static StockRequisitionLifecyclePostgresConcurrencyTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task ConcurrentSubmissions_AfterBothObserveDraft_CommitExactlyOneTransition()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.StockRequisitionLifecycle,
            async database =>
            {
                await using (var seed = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    SeedDraftRequisition(seed);
                    await seed.SaveChangesAsync();
                }

                using var barrier = new Barrier(2);
                await using var firstContext = CreateContext(database.ConnectionString);
                await using var secondContext = CreateContext(database.ConnectionString);
                var firstHandler = CreateHandler(firstContext);
                var secondHandler = CreateHandler(secondContext);

                var outcomes = await Task.WhenAll(
                    Task.Run(() => ObserveAndSubmitAsync(firstContext, firstHandler, barrier)),
                    Task.Run(() => ObserveAndSubmitAsync(secondContext, secondHandler, barrier)));

                Assert.Single(outcomes, outcome => outcome.Exception is null);
                var rejected = Assert.Single(outcomes, outcome => outcome.Exception is InvalidOperationException);
                Assert.Contains(
                    "Can only submit draft requisitions. Current status: submitted",
                    rejected.Exception!.Message);

                await using var verify = await CreateMigratedContextAsync(database.ConnectionString);
                var persisted = await verify.StockRequisitions
                    .SingleAsync(requisition => requisition.Id == 501);
                Assert.Equal(StockRequisition.StatusSubmitted, persisted.Status);
                Assert.Equal(101, persisted.SubmittedBy);
                Assert.NotNull(persisted.SubmittedAt);
                Assert.Equal(101, persisted.UpdatedBy);
                Assert.NotNull(persisted.UpdatedAt);
                Assert.Empty(verify.StockTransfers);
            });
    }

    private static async Task<TransitionOutcome> ObserveAndSubmitAsync(
        RetailPOSDbContext dbContext,
        SubmitStockRequisitionCommandHandler handler,
        Barrier barrier)
    {
        try
        {
            var observedDraft = await dbContext.StockRequisitions
                .AnyAsync(requisition =>
                    requisition.Id == 501 && requisition.Status == StockRequisition.StatusDraft);
            if (!observedDraft)
                throw new InvalidOperationException("Concurrent test did not observe the initial Draft state.");

            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("Timed out waiting for concurrent requisition submissions.");

            await handler.Handle(
                new SubmitStockRequisitionCommand(501, 101),
                CancellationToken.None);
            return new TransitionOutcome(null);
        }
        catch (Exception exception)
        {
            return new TransitionOutcome(exception);
        }
    }

    private static SubmitStockRequisitionCommandHandler CreateHandler(RetailPOSDbContext dbContext)
    {
        var scopeProvider = new Mock<IStockRequisitionBusinessScopeProvider>(MockBehavior.Strict);
        scopeProvider.Setup(provider => provider.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        return new SubmitStockRequisitionCommandHandler(
            scopeProvider.Object,
            new StockRequisitionLifecycleStore(
                dbContext,
                NullLogger<StockRequisitionLifecycleStore>.Instance),
            new StockRequisitionReadStore(dbContext));
    }

    private static async Task<RetailPOSDbContext> CreateMigratedContextAsync(string connectionString)
    {
        var context = CreateContext(connectionString);
        return await PostgresTestDatabase.InitializeResourceAsync(
            context, value => value.Database.MigrateAsync());
    }

    private static RetailPOSDbContext CreateContext(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString)
            .ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        return new RetailPOSDbContext(builder.Options);
    }

    private static void SeedDraftRequisition(RetailPOSDbContext dbContext)
    {
        dbContext.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Stock Requisition Lifecycle Test Business",
            IsActive = true
        });
        dbContext.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Requisition Lifecycle Test User",
            Email = "stock-requisition-lifecycle@example.test",
            PasswordHash = "test",
            IsActive = true
        });
        dbContext.Outlets.Add(new Outlet
        {
            Id = 11,
            BusinessId = 1,
            Name = "Requisition Lifecycle Outlet",
            Address = "Test"
        });
        dbContext.Warehouses.Add(new Warehouse
        {
            Id = 21,
            BusinessId = 1,
            Name = "Requisition Lifecycle Warehouse",
            Address = "Test"
        });
        dbContext.StockRequisitions.Add(new StockRequisition
        {
            Id = 501,
            RequisitionNo = "REQ-TEST-501",
            RequestingLocationId = 11,
            RequestingLocationType = "outlet",
            SourceLocationId = 21,
            SourceLocationType = "warehouse",
            RequestedBy = 101,
            RequestDate = DateTime.UtcNow,
            Status = StockRequisition.StatusDraft,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static string GetPostgresConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        return new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build()
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "RetailPOS PostgreSQL test connection is not configured.");
    }

    private sealed record TransitionOutcome(Exception? Exception);
}
