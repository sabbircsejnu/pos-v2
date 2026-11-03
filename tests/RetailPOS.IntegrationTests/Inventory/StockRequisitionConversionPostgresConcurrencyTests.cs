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
using RetailPOS.Infrastructure.StockTransfers;
using RetailPOS.Infrastructure.StockRequisitions;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Inventory;

public sealed class StockRequisitionConversionPostgresConcurrencyTests
{
    static StockRequisitionConversionPostgresConcurrencyTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task ConcurrentConversions_LeaveOneTransferAndRejectTheDuplicate()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.StockRequisitionConversion,
            async database =>
            {
                await using (var seed = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    SeedApprovedRequisition(seed);
                    await seed.SaveChangesAsync();
                }

                using var barrier = new Barrier(2);
                await using var firstContext = CreateContext(database.ConnectionString);
                await using var secondContext = CreateContext(database.ConnectionString);
                var firstHandler = CreateHandler(firstContext, barrier);
                var secondHandler = CreateHandler(secondContext, barrier);

                var outcomes = await Task.WhenAll(
                    Task.Run(() => AttemptConversionAsync(firstHandler, 501)),
                    Task.Run(() => AttemptConversionAsync(secondHandler, 501)));

                Assert.Single(outcomes, outcome => outcome.Exception is null);
                Assert.IsType<InvalidOperationException>(
                    Assert.Single(outcomes, outcome => outcome.Exception is not null).Exception);
                Assert.Contains(
                    "Requisition in status 'fully_fulfilled' cannot be converted to transfer.",
                    Assert.Single(outcomes, outcome => outcome.Exception is InvalidOperationException).Exception!.Message);

                await using var verify = await CreateMigratedContextAsync(database.ConnectionString);
                var requisition = await verify.StockRequisitions
                    .Include(row => row.Lines)
                    .SingleAsync(row => row.Id == 501);
                Assert.Equal(StockRequisition.StatusFullyFulfilled, requisition.Status);
                Assert.Equal(5, Assert.Single(requisition.Lines).FulfilledQuantity);
                var transfer = await verify.StockTransfers
                    .Include(row => row.Items)
                    .SingleAsync(row => row.RelatedRequisitionId == 501);
                Assert.Equal(StockTransfer.StatusDraft, transfer.Status);
                Assert.Equal(501, transfer.RelatedRequisitionId);
                Assert.Equal(3, Assert.Single(transfer.Items).TransferQuantity);
            });
    }

    [Fact]
    public async Task ConversionFailure_RollsBackTransferAndRequisitionFulfillment()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.StockRequisitionConversion,
            async database =>
            {
                await using (var seed = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    SeedApprovedRequisition(seed);
                    await seed.SaveChangesAsync();
                    await seed.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE stock_transfers ADD CONSTRAINT ck_requisition_conversion_test_failure CHECK (transfer_type <> 'requisition')");
                }

                await using (var conversionContext = CreateContext(database.ConnectionString))
                {
                    using var barrier = new Barrier(1);
                    var handler = CreateHandler(conversionContext, barrier);
                    await Assert.ThrowsAsync<DbUpdateException>(() =>
                        handler.Handle(
                            new ConvertStockRequisitionToTransferCommand(501, 101),
                            CancellationToken.None));
                }

                await using var verify = await CreateMigratedContextAsync(database.ConnectionString);
                var requisition = await verify.StockRequisitions
                    .Include(row => row.Lines)
                    .SingleAsync(row => row.Id == 501);
                Assert.Equal(StockRequisition.StatusApproved, requisition.Status);
                Assert.Equal(2, Assert.Single(requisition.Lines).FulfilledQuantity);
                Assert.Empty(verify.StockTransfers);
            });
    }

    [Fact]
    public async Task ConversionFromAnotherBusiness_IsRejectedWithoutEffects()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.StockRequisitionConversion,
            async database =>
            {
                await using (var seed = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    SeedApprovedRequisition(seed);
                    await seed.SaveChangesAsync();
                }

                await using (var conversionContext = CreateContext(database.ConnectionString))
                {
                    using var barrier = new Barrier(1);
                    var handler = CreateHandler(conversionContext, barrier, businessId: 2);
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                        handler.Handle(
                            new ConvertStockRequisitionToTransferCommand(501, 101),
                            CancellationToken.None));
                }

                await using var verify = await CreateMigratedContextAsync(database.ConnectionString);
                var requisition = await verify.StockRequisitions
                    .Include(row => row.Lines)
                    .SingleAsync(row => row.Id == 501);
                Assert.Equal(StockRequisition.StatusApproved, requisition.Status);
                Assert.Equal(2, Assert.Single(requisition.Lines).FulfilledQuantity);
                Assert.Empty(verify.StockTransfers);
            });
    }

    private static ConvertStockRequisitionToTransferCommandHandler CreateHandler(
        RetailPOSDbContext dbContext,
        Barrier barrier,
        long businessId = 1)
    {
        var scopeProvider = new Mock<IStockTransferReadScopeProvider>(MockBehavior.Strict);
        scopeProvider.Setup(provider => provider.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
                    throw new TimeoutException(
                        "Timed out waiting for concurrent Stock Requisition conversion requests.");
                return Task.FromResult(businessId);
            });

        return new ConvertStockRequisitionToTransferCommandHandler(
            scopeProvider.Object,
            new StockRequisitionTransferStore(
                dbContext,
                NullLogger<StockRequisitionTransferStore>.Instance),
            new StockTransferReadStore(dbContext));
    }

    private static async Task<ConversionOutcome> AttemptConversionAsync(
        ConvertStockRequisitionToTransferCommandHandler handler,
        long requisitionId)
    {
        try
        {
            return new ConversionOutcome(
                await handler.Handle(
                    new ConvertStockRequisitionToTransferCommand(requisitionId, 101),
                    CancellationToken.None),
                null);
        }
        catch (Exception exception)
        {
            return new ConversionOutcome(null, exception);
        }
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

    private static void SeedApprovedRequisition(RetailPOSDbContext dbContext)
    {
        dbContext.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Stock Requisition Conversion Test Business",
            IsActive = true
        });
        dbContext.Users.Add(new User
        {
            Id = 101,
            BusinessId = 1,
            Name = "Requisition Test User",
            Email = "stock-requisition-conversion@example.test",
            PasswordHash = "test",
            IsActive = true
        });
        dbContext.Categories.Add(new Category
        {
            Id = 201,
            BusinessId = 1,
            Name = "Stock Requisition Conversion Category"
        });
        dbContext.Outlets.Add(new Outlet
        {
            Id = 11,
            BusinessId = 1,
            Name = "Stock Requisition Requesting Outlet",
            Address = "Test"
        });
        dbContext.Warehouses.Add(new Warehouse
        {
            Id = 21,
            BusinessId = 1,
            Name = "Stock Requisition Source Warehouse",
            Address = "Test"
        });
        dbContext.Products.Add(new Product
        {
            Id = 300,
            BusinessId = 1,
            Name = "Stock Requisition Product",
            ProductCode = "SR-001",
            CategoryId = 201,
            BasePrice = 10m,
            CostPrice = 10m,
            HasVariants = true,
            Status = ProductStatus.Active
        });
        dbContext.ProductVariants.Add(new ProductVariant
        {
            Id = 301,
            BusinessId = 1,
            ProductId = 300,
            Name = "Stock Requisition Variant",
            Sku = "SR-001-DEF",
            Attributes = "{}"
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
            Status = StockRequisition.StatusApproved,
            CreatedAt = DateTime.UtcNow,
            Lines =
            [
                new StockRequisitionLine
                {
                    Id = 601,
                    VariantId = 301,
                    RequestedQuantity = 5,
                    FulfilledQuantity = 2,
                    Remarks = "pending quantity"
                }
            ]
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

    private sealed record ConversionOutcome(
        RetailPOS.Application.Inventory.StockTransfers.StockTransferReadModel? Transfer,
        Exception? Exception);
}
