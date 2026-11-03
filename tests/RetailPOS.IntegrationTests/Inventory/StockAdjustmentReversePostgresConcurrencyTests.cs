using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Commands;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ReadStores;
using RetailPOS.Tests.Infrastructure;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Inventory;

public sealed class StockAdjustmentReversePostgresConcurrencyTests
{
    static StockAdjustmentReversePostgresConcurrencyTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task ConcurrentReversals_KeepOneInventoryLedgerAndAudit_AndLedgerFailureRollsBack()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.StockAdjustmentReverse,
            async database =>
            {
                await using (var seed = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    await SeedAsync(seed);
                }

                using var barrier = new Barrier(2);
                await using var firstContext = CreateContext(database.ConnectionString);
                await using var secondContext = CreateContext(database.ConnectionString);
                var firstAudit = CreateAuditWriter();
                var secondAudit = CreateAuditWriter();
                var firstHandler = CreateHandler(firstContext, firstAudit, barrier);
                var secondHandler = CreateHandler(secondContext, secondAudit, barrier);

                var outcomes = await Task.WhenAll(
                    Task.Run(() => ReverseAsync(firstHandler)),
                    Task.Run(() => ReverseAsync(secondHandler)));

                Assert.Single(outcomes, outcome => outcome.Exception is null);
                Assert.Single(outcomes, outcome => outcome.Exception is DbUpdateException);
                firstAudit.Verify(writer => writer.RecordReversedAsync(
                    It.IsAny<StockAdjustmentReadModel>(),
                    "Correction",
                    106,
                    null,
                    null,
                    It.IsAny<CancellationToken>()), Times.AtMostOnce);
                secondAudit.Verify(writer => writer.RecordReversedAsync(
                    It.IsAny<StockAdjustmentReadModel>(),
                    "Correction",
                    106,
                    null,
                    null,
                    It.IsAny<CancellationToken>()), Times.AtMostOnce);
                Assert.Equal(
                    1,
                    firstAudit.Invocations.Count(invocation =>
                        invocation.Method.Name == nameof(IStockAdjustmentAuditWriter.RecordReversedAsync))
                    + secondAudit.Invocations.Count(invocation =>
                        invocation.Method.Name == nameof(IStockAdjustmentAuditWriter.RecordReversedAsync)));

                await using (var verify = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    Assert.Equal(
                        StockAdjustment.StatusReversed,
                        await verify.StockAdjustments
                            .Where(row => row.Id == 501)
                            .Select(row => row.Status)
                            .SingleAsync());
                    Assert.Equal(
                        10,
                        await verify.Inventories
                            .Where(row => row.VariantId == 301
                                && row.LocationId == 11
                                && row.LocationType == "outlet")
                            .Select(row => row.Quantity)
                            .SingleAsync());
                    var ledgerRows = await verify.StockLedgers
                        .Where(row => row.ReferenceType == StockLedgerReferenceType.StockAdjustment
                            && row.ReferenceId == 501)
                        .ToArrayAsync();
                    Assert.Single(ledgerRows);
                    Assert.Equal(2, ledgerRows[0].QtyOut);
                    Assert.Equal(10, ledgerRows[0].BalanceAfter);

                    verify.StockAdjustments.Add(CreateApprovedAdjustment(502));
                    await verify.SaveChangesAsync();
                    await verify.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE stock_ledgers ADD CONSTRAINT ck_stock_adjustment_reverse_test_failure CHECK (qty_in < 0) NOT VALID");
                }

                await using (var rollbackContext = CreateContext(database.ConnectionString))
                {
                    var rollbackAudit = CreateAuditWriter();
                    var rollbackHandler = CreateHandler(rollbackContext, rollbackAudit);
                    await Assert.ThrowsAsync<DbUpdateException>(() =>
                        rollbackHandler.Handle(
                            new ReverseStockAdjustmentCommand(502, 106, "Correction"),
                            CancellationToken.None));
                    rollbackAudit.Verify(writer => writer.RecordReversedAsync(
                        It.IsAny<StockAdjustmentReadModel>(),
                        It.IsAny<string>(),
                        It.IsAny<long>(),
                        It.IsAny<long?>(),
                        It.IsAny<long?>(),
                        It.IsAny<CancellationToken>()), Times.Never);
                }

                await using var final = await CreateMigratedContextAsync(database.ConnectionString);
                Assert.Equal(
                    StockAdjustment.StatusApproved,
                    await final.StockAdjustments
                        .Where(row => row.Id == 502)
                        .Select(row => row.Status)
                        .SingleAsync());
                Assert.Equal(
                    10,
                    await final.Inventories
                        .Where(row => row.VariantId == 301
                            && row.LocationId == 11
                            && row.LocationType == "outlet")
                        .Select(row => row.Quantity)
                        .SingleAsync());
                Assert.Empty(await final.StockLedgers
                    .Where(row => row.ReferenceType == StockLedgerReferenceType.StockAdjustment
                        && row.ReferenceId == 502)
                    .ToArrayAsync());
            });
    }

    private static ReverseStockAdjustmentCommandHandler CreateHandler(
        RetailPOSDbContext dbContext,
        Mock<IStockAdjustmentAuditWriter> auditWriter,
        Barrier? barrier = null)
    {
        var context = new StockAdjustmentCreateContext(
            new StockAdjustmentReadScope(
                1,
                [new StockAdjustmentLocationScope(11, "outlet")]),
            AllowNegativeStock: false);
        var contextProvider = new Mock<IStockAdjustmentCreateContextProvider>(MockBehavior.Strict);
        contextProvider
            .Setup(provider => provider.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);
        var sender = new Mock<ISender>(MockBehavior.Strict);
        IStockLedgerWriter stockLedger = new StockLedgerWriter(
            dbContext,
            NullLogger<StockLedgerWriter>.Instance);
        if (barrier is not null)
        {
            stockLedger = new ReverseBarrierStockLedgerWriter(stockLedger, barrier);
        }

        var reversalStore = new StockAdjustmentReversalStore(
            dbContext,
            stockLedger,
            sender.Object,
            NullLogger<StockAdjustmentReversalStore>.Instance);
        return new ReverseStockAdjustmentCommandHandler(
            contextProvider.Object,
            reversalStore,
            new StockAdjustmentReadStore(dbContext),
            auditWriter.Object);
    }

    private static Mock<IStockAdjustmentAuditWriter> CreateAuditWriter()
    {
        var auditWriter = new Mock<IStockAdjustmentAuditWriter>(MockBehavior.Strict);
        auditWriter
            .Setup(writer => writer.RecordReversedAsync(
                It.IsAny<StockAdjustmentReadModel>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return auditWriter;
    }

    private static async Task<ReverseOutcome> ReverseAsync(
        ReverseStockAdjustmentCommandHandler handler)
    {
        try
        {
            return new ReverseOutcome(
                await handler.Handle(
                    new ReverseStockAdjustmentCommand(501, 106, "Correction"),
                    CancellationToken.None),
                null);
        }
        catch (Exception exception)
        {
            return new ReverseOutcome(null, exception);
        }
    }

    private static async Task<RetailPOSDbContext> CreateMigratedContextAsync(string connectionString)
    {
        var dbContext = CreateContext(connectionString);
        return await PostgresTestDatabase.InitializeResourceAsync(
            dbContext, context => context.Database.MigrateAsync());
    }

    private static RetailPOSDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString)
            .ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new RetailPOSDbContext(options);
    }

    private static async Task SeedAsync(RetailPOSDbContext dbContext)
    {
        dbContext.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Stock Adjustment Reverse Test Business",
            IsActive = true
        });
        dbContext.Users.AddRange(
            new User
            {
                Id = 101,
                BusinessId = 1,
                Name = "Adjustment Creator",
                Email = "adjustment-reverse-creator@example.test",
                PasswordHash = "test",
                IsActive = true
            },
            new User
            {
                Id = 106,
                BusinessId = 1,
                Name = "Adjustment Reverser",
                Email = "adjustment-reverse-actor@example.test",
                PasswordHash = "test",
                IsActive = true
            });
        dbContext.Categories.Add(new Category
        {
            Id = 201,
            BusinessId = 1,
            Name = "Stock Adjustment Reverse Test Category"
        });
        dbContext.Outlets.Add(new Outlet
        {
            Id = 11,
            BusinessId = 1,
            Name = "Stock Adjustment Reverse Test Outlet",
            Address = "Test"
        });
        dbContext.Products.Add(new Product
        {
            Id = 300,
            BusinessId = 1,
            Name = "Stock Adjustment Reverse Test Product",
            ProductCode = "SAR-001",
            CategoryId = 201,
            BasePrice = 10m,
            CostPrice = 0m,
            HasVariants = true,
            Status = ProductStatus.Active
        });
        dbContext.ProductVariants.Add(new ProductVariant
        {
            Id = 301,
            BusinessId = 1,
            ProductId = 300,
            Name = "Stock Adjustment Reverse Test Variant",
            Sku = "SAR-001-DEF",
            Attributes = "{}"
        });
        dbContext.Inventories.Add(new InventoryEntity
        {
            Id = 401,
            VariantId = 301,
            LocationId = 11,
            LocationType = "outlet",
            Quantity = 12
        });
        dbContext.StockAdjustments.Add(CreateApprovedAdjustment(501));
        await dbContext.SaveChangesAsync();
    }

    private static StockAdjustment CreateApprovedAdjustment(long id) => new()
    {
        Id = id,
        AdjustmentNumber = $"ADJ-2026-{id:D5}",
        Status = StockAdjustment.StatusApproved,
        LocationId = 11,
        LocationType = "outlet",
        AdjustedBy = 101,
        AdjustmentDate = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        ApprovedBy = 101,
        ApprovedAt = DateTime.UtcNow,
        Lines =
        [
            new StockAdjustmentLine
            {
                VariantId = 301,
                PreviousQuantity = 10,
                QuantityChange = 2,
                NewQuantity = 12,
                Reason = "Found",
                UnitCostSnapshot = 5m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        ]
    };

    private static string GetPostgresConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

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

    private sealed record ReverseOutcome(
        StockAdjustmentReadModel? Adjustment,
        Exception? Exception);

    private sealed class ReverseBarrierStockLedgerWriter(
        IStockLedgerWriter inner,
        Barrier barrier) : IStockLedgerWriter
    {
        public void WriteEntry(
            long variantId,
            long locationId,
            string locationType,
            string transactionType,
            int qtyIn,
            int qtyOut,
            int balanceAfter,
            string referenceType,
            long referenceId,
            string? remarks = null,
            long? createdBy = null,
            DateTime? createdAt = null,
            decimal? unitCost = null,
            decimal? movementValue = null,
            DateTime? businessDate = null,
            long? movementSequence = null,
            long? reversalOfMovementId = null) =>
            inner.WriteEntry(
                variantId,
                locationId,
                locationType,
                transactionType,
                qtyIn,
                qtyOut,
                balanceAfter,
                referenceType,
                referenceId,
                remarks,
                createdBy,
                createdAt,
                unitCost,
                movementValue,
                businessDate,
                movementSequence,
                reversalOfMovementId);

        public void WriteEntryWithValuation(
            long variantId,
            long locationId,
            string locationType,
            string transactionType,
            int qtyIn,
            int qtyOut,
            int balanceAfter,
            string referenceType,
            long referenceId,
            string? remarks = null,
            long? createdBy = null,
            DateTime? createdAt = null,
            decimal? unitCost = null,
            decimal? movementValue = null,
            DateTime? businessDate = null,
            long? movementSequence = null,
            long? reversalOfMovementId = null)
        {
            inner.WriteEntryWithValuation(
                variantId,
                locationId,
                locationType,
                transactionType,
                qtyIn,
                qtyOut,
                balanceAfter,
                referenceType,
                referenceId,
                remarks,
                createdBy,
                createdAt,
                unitCost,
                movementValue,
                businessDate,
                movementSequence,
                reversalOfMovementId);
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException(
                    "Both stock adjustment reversals did not reach the inventory compensation barrier.");
            }
        }
    }
}
