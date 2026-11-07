using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MediatR;
using Moq;
using Npgsql;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.Commands;
using RetailPOS.Application.Inventory.StockAdjustments;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ReadStores;
using RetailPOS.Tests.Infrastructure;
using InventoryEntity = RetailPOS.Core.Entities.Inventory;

namespace RetailPOS.Tests.Inventory;

public sealed class StockAdjustmentApprovalPostgresConcurrencyTests
{
    static StockAdjustmentApprovalPostgresConcurrencyTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task ConcurrentApprovals_KeepOneInventoryLedgerAndPostCommitOutcome_AndRollbackLedgerFailure()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.StockAdjustmentApproval,
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
                var cacheInvalidator = new Mock<IStockAdjustmentApprovalCacheInvalidator>(MockBehavior.Strict);
                cacheInvalidator
                    .Setup(invalidator => invalidator.InvalidateAsync(
                        11,
                        It.Is<IReadOnlyList<long>>(ids => ids.SequenceEqual(new long[] { 301 })),
                        It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);

                var firstHandler = CreateHandler(firstContext, firstAudit, cacheInvalidator.Object, barrier);
                var secondHandler = CreateHandler(secondContext, secondAudit, cacheInvalidator.Object, barrier);
                var outcomes = await Task.WhenAll(
                    Task.Run(() => ApproveAsync(firstHandler)),
                    Task.Run(() => ApproveAsync(secondHandler)));

                Assert.Single(outcomes, outcome => outcome.Exception is null);
                Assert.Single(outcomes, outcome =>
                    outcome.Exception is DbUpdateConcurrencyException);
                Assert.Equal(
                    1,
                    firstAudit.Invocations.Count(invocation =>
                        invocation.Method.Name == nameof(IStockAdjustmentAuditWriter.RecordApprovedAsync))
                    + secondAudit.Invocations.Count(invocation =>
                        invocation.Method.Name == nameof(IStockAdjustmentAuditWriter.RecordApprovedAsync)));
                firstAudit.Verify(writer => writer.RecordApprovedAsync(
                    It.IsAny<StockAdjustmentReadModel>(),
                    105,
                    null,
                    It.IsAny<CancellationToken>()), Times.AtMostOnce);
                secondAudit.Verify(writer => writer.RecordApprovedAsync(
                    It.IsAny<StockAdjustmentReadModel>(),
                    105,
                    null,
                    It.IsAny<CancellationToken>()), Times.AtMostOnce);
                cacheInvalidator.Verify(invalidator => invalidator.InvalidateAsync(
                    11,
                    It.Is<IReadOnlyList<long>>(ids => ids.SequenceEqual(new long[] { 301 })),
                    It.IsAny<CancellationToken>()), Times.Once);

                await using (var duplicateContext = CreateContext(database.ConnectionString))
                {
                    var duplicateHandler = CreateHandler(
                        duplicateContext,
                        CreateAuditWriter(),
                        cacheInvalidator.Object);
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        duplicateHandler.Handle(
                            new ApproveStockAdjustmentCommand(501, 105),
                            CancellationToken.None));
                }

                await using (var verify = await CreateMigratedContextAsync(database.ConnectionString))
                {
                    Assert.Equal(
                        StockAdjustment.StatusApproved,
                        await verify.StockAdjustments
                            .Where(row => row.Id == 501)
                            .Select(row => row.Status)
                            .SingleAsync());
                    Assert.Equal(
                        11,
                        await verify.Inventories
                            .Where(row => row.VariantId == 301
                                && row.LocationId == 11
                                && row.LocationType == "outlet")
                            .Select(row => row.Quantity)
                            .SingleAsync());
                    var ledger = await verify.StockLedgers
                        .Where(row => row.ReferenceType == StockLedgerReferenceType.StockAdjustment
                            && row.ReferenceId == 501)
                        .ToArrayAsync();
                    Assert.Single(ledger);
                    Assert.Empty(await verify.JournalEntries
                        .Where(row => row.ReferenceType == "StockAdjustment"
                            && row.ReferenceId == "501")
                        .ToArrayAsync());

                    verify.StockAdjustments.Add(new StockAdjustment
                    {
                        Id = 502,
                        AdjustmentNumber = "ADJ-2026-00502",
                        Status = StockAdjustment.StatusPendingApproval,
                        LocationId = 11,
                        LocationType = "outlet",
                        AdjustedBy = 101,
                        AdjustmentDate = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        SubmittedAt = DateTime.UtcNow,
                        Lines =
                        [
                            new StockAdjustmentLine
                            {
                                VariantId = 301,
                                PreviousQuantity = 11,
                                QuantityChange = 1,
                                NewQuantity = 12,
                                Reason = "Found",
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            }
                        ]
                    });
                    await verify.SaveChangesAsync();
                    await verify.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE stock_ledgers ADD CONSTRAINT ck_stock_adjustment_approval_test_failure CHECK (qty_in < 0) NOT VALID");
                }

                await using var rollbackContext = CreateContext(database.ConnectionString);
                var rollbackHandler = CreateHandler(
                    rollbackContext,
                    CreateAuditWriter(),
                    cacheInvalidator.Object);
                await Assert.ThrowsAsync<DbUpdateException>(() =>
                    rollbackHandler.Handle(
                        new ApproveStockAdjustmentCommand(502, 105),
                        CancellationToken.None));

                await using var final = await CreateMigratedContextAsync(database.ConnectionString);
                Assert.Equal(
                    StockAdjustment.StatusPendingApproval,
                    await final.StockAdjustments
                        .Where(row => row.Id == 502)
                        .Select(row => row.Status)
                        .SingleAsync());
                Assert.Equal(
                    11,
                    await final.Inventories
                        .Where(row => row.VariantId == 301
                            && row.LocationId == 11
                            && row.LocationType == "outlet")
                        .Select(row => row.Quantity)
                        .SingleAsync());
                Assert.Single(await final.StockLedgers
                    .Where(row => row.ReferenceType == StockLedgerReferenceType.StockAdjustment)
                    .ToArrayAsync());
            });
    }

    private static ApproveStockAdjustmentCommandHandler CreateHandler(
        RetailPOSDbContext dbContext,
        Mock<IStockAdjustmentAuditWriter> auditWriter,
        IStockAdjustmentApprovalCacheInvalidator cacheInvalidator,
        Barrier? barrier = null)
    {
        var scope = new StockAdjustmentCreateContext(
            new StockAdjustmentReadScope(1, [new StockAdjustmentLocationScope(11, "outlet")]),
            AllowNegativeStock: false);
        var contextProvider = new Mock<IStockAdjustmentCreateContextProvider>(MockBehavior.Strict);
        contextProvider
            .Setup(provider => provider.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(scope);

        var sender = new Mock<ISender>(MockBehavior.Strict);
        var postingAccountResolver = new Mock<IStockAdjustmentPostingAccountResolver>(MockBehavior.Strict);
        IStockLedgerWriter stockLedger = new StockLedgerWriter(
            dbContext,
            NullLogger<StockLedgerWriter>.Instance);
        if (barrier is not null)
        {
            stockLedger = new ApprovalBarrierStockLedgerWriter(stockLedger, barrier);
        }

        var approvalStore = new StockAdjustmentApprovalStore(
            dbContext,
            stockLedger,
            postingAccountResolver.Object,
            sender.Object);
        return new ApproveStockAdjustmentCommandHandler(
            contextProvider.Object,
            approvalStore,
            new StockAdjustmentReadStore(dbContext),
            auditWriter.Object,
            cacheInvalidator);
    }

    private static Mock<IStockAdjustmentAuditWriter> CreateAuditWriter()
    {
        var auditWriter = new Mock<IStockAdjustmentAuditWriter>(MockBehavior.Strict);
        auditWriter
            .Setup(writer => writer.RecordApprovedAsync(
                It.IsAny<StockAdjustmentReadModel>(),
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return auditWriter;
    }

    private static async Task<ApprovalOutcome> ApproveAsync(
        ApproveStockAdjustmentCommandHandler handler)
    {
        try
        {
            return new ApprovalOutcome(
                await handler.Handle(
                    new ApproveStockAdjustmentCommand(501, 105),
                    CancellationToken.None),
                null);
        }
        catch (Exception exception)
        {
            return new ApprovalOutcome(null, exception);
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
            Name = "Stock Adjustment Approval Test Business",
            IsActive = true
        });
        dbContext.Users.AddRange(
            new User
            {
                Id = 101,
                BusinessId = 1,
                Name = "Adjustment Creator",
                Email = "adjustment-creator@example.test",
                PasswordHash = "test",
                IsActive = true
            },
            new User
            {
                Id = 105,
                BusinessId = 1,
                Name = "Adjustment Approver",
                Email = "adjustment-approver@example.test",
                PasswordHash = "test",
                IsActive = true
            });
        dbContext.Categories.Add(new Category
        {
            Id = 201,
            BusinessId = 1,
            Name = "Stock Adjustment Test Category"
        });
        dbContext.Outlets.Add(new Outlet
        {
            Id = 11,
            BusinessId = 1,
            Name = "Stock Adjustment Test Outlet",
            Address = "Test"
        });
        dbContext.Products.Add(new Product
        {
            Id = 300,
            BusinessId = 1,
            Name = "Stock Adjustment Test Product",
            ProductCode = "SAT-001",
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
            Name = "Stock Adjustment Test Variant",
            Sku = "SAT-001-DEF",
            Attributes = "{}"
        });
        dbContext.Inventories.Add(new InventoryEntity
        {
            Id = 401,
            VariantId = 301,
            LocationId = 11,
            LocationType = "outlet",
            Quantity = 10
        });
        dbContext.StockAdjustments.Add(new StockAdjustment
        {
            Id = 501,
            AdjustmentNumber = "ADJ-2026-00501",
            Status = StockAdjustment.StatusPendingApproval,
            LocationId = 11,
            LocationType = "outlet",
            AdjustedBy = 101,
            AdjustmentDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            SubmittedAt = DateTime.UtcNow,
            Lines =
            [
                new StockAdjustmentLine
                {
                    VariantId = 301,
                    PreviousQuantity = 10,
                    QuantityChange = 1,
                    NewQuantity = 11,
                    Reason = "Found",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            ]
        });
        await dbContext.SaveChangesAsync();
    }

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

    private sealed record ApprovalOutcome(
        StockAdjustmentReadModel? Adjustment,
        Exception? Exception);

    private sealed class ApprovalBarrierStockLedgerWriter(
        IStockLedgerWriter inner,
        Barrier barrier) : IStockLedgerWriter
    {
        public void WriteVerifiedZeroCostEntry(
            long variantId, long locationId, string locationType, string transactionType, decimal qtyIn, decimal qtyOut,
            decimal balanceAfter, string referenceType, long referenceId, string? remarks = null, long? createdBy = null,
            DateTime? createdAt = null) =>
            inner.WriteVerifiedZeroCostEntry(variantId, locationId, locationType, transactionType, qtyIn, qtyOut,
                balanceAfter, referenceType, referenceId, remarks, createdBy, createdAt);

        public void WriteEntry(
            long variantId,
            long locationId,
            string locationType,
            string transactionType,
            decimal qtyIn,
            decimal qtyOut,
            decimal balanceAfter,
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
            decimal qtyIn,
            decimal qtyOut,
            decimal balanceAfter,
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
                    "Both stock adjustment approvals did not reach the inventory-apply barrier.");
            }
        }
    }
}
