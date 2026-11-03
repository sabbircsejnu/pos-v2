using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.PurchaseOrders.Abstractions;
using RetailPOS.Application.PurchaseOrders.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.PurchaseOrders;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Tests.Accounting;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Purchase;

public sealed class PurchaseAndReceiveRelationalTests
{
    static PurchaseAndReceiveRelationalTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task PurchaseAndReceive_PostgresCommitsJournalAndInventoryInOneTransaction()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.PurchaseReceive,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db, 1);
                var today = DateTime.UtcNow.Date;
                var periodStart = new DateTime(today.Year, today.Month, 1);
                var periodEnd = periodStart.AddMonths(1).AddDays(-1);
                db.ChartAccounts.AddRange(
                    new ChartAccount
                    {
                        Id = 6101,
                        BusinessId = 1,
                        AccountCode = "6101",
                        AccountName = "Inventory",
                        AccountType = ChartAccountTypes.Asset,
                        NormalBalanceType = NormalBalanceTypes.Debit,
                        IsActive = true
                    },
                    new ChartAccount
                    {
                        Id = 6102,
                        BusinessId = 1,
                        AccountCode = "6102",
                        AccountName = "Supplier Payable",
                        AccountType = ChartAccountTypes.Liability,
                        NormalBalanceType = NormalBalanceTypes.Credit,
                        IsActive = true
                    });
                db.FinancialYears.Add(new FinancialYear
                {
                    Id = 6201,
                    BusinessId = 1,
                    YearCode = $"FY-{today.Year}-PUR",
                    Name = $"FY {today.Year}",
                    StartDate = new DateTime(today.Year, 1, 1),
                    EndDate = new DateTime(today.Year, 12, 31),
                    IsCurrent = true
                });
                db.AccountingPeriods.Add(new AccountingPeriod
                {
                    Id = 6202,
                    FinancialYearId = 6201,
                    PeriodNumber = today.Month,
                    Name = today.ToString("MMMM"),
                    StartDate = periodStart,
                    EndDate = periodEnd,
                    Status = AccountingPeriodStatuses.Open
                });
                await db.SaveChangesAsync();

                var audit = Mock.Of<IAuditService>(service =>
                    service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()) == Task.CompletedTask);
                var journalNumbers = new Mock<IJournalNumberAllocator>();
                journalNumbers.Setup(service => service.AllocateNextJournalNumberAsync(
                        It.IsAny<long>(),
                        It.IsAny<long?>(),
                        It.IsAny<DateTime>(),
                        It.IsAny<long?>(),
                        It.IsAny<CancellationToken>()))
                    .ReturnsAsync("PUR-RECEIPT-1");
                var journalStore = new ManualJournalDraftStore(
                    db,
                    audit,
                    journalNumbers.Object,
                    NullLogger<ManualJournalDraftStore>.Instance);
                var configuration = new Mock<IAccountingConfigurationService>();
                configuration.Setup(service => service.ResolvePostingAccountAsync(
                        PostingAccountSlots.Inventory,
                        null))
                    .ReturnsAsync(new PostingAccountResolutionDto
                    {
                        Slot = PostingAccountSlots.Inventory,
                        AccountId = 6101
                    });
                configuration.Setup(service => service.ResolvePostingAccountAsync(
                        PostingAccountSlots.SupplierPayable,
                        null))
                    .ReturnsAsync(new PostingAccountResolutionDto
                    {
                        Slot = PostingAccountSlots.SupplierPayable,
                        AccountId = 6102
                    });
                var poster = new PurchaseReceiptJournalPoster(
                    SupplierPaymentCreationTestHarness.CreateJournalCommandSender(journalStore),
                    configuration.Object);
                var ledger = new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                    db,
                    NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance);
                var handler = new PurchaseAndReceiveCommandHandler(
                    new PurchaseAndReceiveStore(
                        db,
                        new PurchaseAndReceiveStockLedgerWriterAdapter(ledger),
                        poster,
                        Mock.Of<ILogger<PurchaseAndReceiveStore>>()),
                    new PurchaseOrderReadStore(db),
                    new TestPurchaseOrderTenantScope(1),
                    Mock.Of<IPurchaseOrderLocationAuthorizer>());

                var result = await handler.Handle(
                    BuildCommand("purchase-receipt-journal"),
                    CancellationToken.None);

                var journal = await db.JournalEntries
                    .Include(entry => entry.Lines)
                    .SingleAsync(entry =>
                        entry.BusinessId == 1
                        && entry.ReferenceType == "Grn"
                        && entry.ReferenceId == result.GrnId.ToString());
                Assert.Equal(JournalEntryStatuses.Posted, journal.Status);
                Assert.Equal("PurchaseReceiptPosting", journal.JournalType);
                Assert.Equal(1101, journal.WarehouseId);
                Assert.Equal("GRN-" + result.GrnId.ToString("D6"), journal.ReferenceNumber);
                Assert.Equal(37.5m, journal.Lines.Sum(line => line.DebitAmount));
                Assert.Equal(37.5m, journal.Lines.Sum(line => line.CreditAmount));
                Assert.Contains(journal.Lines, line =>
                    line.AccountId == 6101
                    && line.DebitAmount == 37.5m
                    && line.CreditAmount == 0
                    && line.WarehouseId == 1101);
                Assert.Contains(journal.Lines, line =>
                    line.AccountId == 6102
                    && line.DebitAmount == 0
                    && line.CreditAmount == 37.5m
                    && line.WarehouseId == 1101);
                Assert.Equal("completed", result.PurchaseOrder.Status, ignoreCase: true);
                Assert.Single(await db.StockLedgers.ToListAsync());
                Assert.Equal(3, await db.Inventories.Select(item => item.Quantity).SingleAsync());
            });
    }

    [Fact]
    public async Task PurchaseAndReceive_PostgresPersistsReceiptAndSequentialDuplicateDoesNotRepeatEffects()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.PurchaseReceive,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db, 1);
                var subject = CreateSubject(db, 1);

                var first = await subject.Handler.Handle(BuildCommand("  immediate-receive  "), CancellationToken.None);

                Assert.False(first.IsDuplicateRequest);
                Assert.Equal("completed", first.PurchaseOrder.Status, ignoreCase: true);
                Assert.Equal(35.44m, first.PurchaseOrder.TotalAmount);
                Assert.Single(first.PurchaseOrder.Items);
                Assert.True(first.GrnId > 0);
                var persistedOrder = await db.PurchaseOrders.Include(order => order.Items).SingleAsync();
                Assert.Equal("immediate-receive", persistedOrder.IdempotencyKey);
                Assert.Equal(77, persistedOrder.CreatedBy);
                var grn = await db.Grns.Include(receipt => receipt.Items).SingleAsync();
                Assert.Equal("completed", grn.Status, ignoreCase: true);
                Assert.Equal(3, Assert.Single(grn.Items).ReceivedQty);
                Assert.Equal(3, await db.Inventories.Select(item => item.Quantity).SingleAsync());
                var ledger = await db.StockLedgers.SingleAsync();
                Assert.Equal(3, ledger.QtyIn);
                Assert.Equal(grn.Id, ledger.ReferenceId);
                Assert.Equal(77, ledger.CreatedBy);
                subject.Journal.Verify(
                    poster => poster.PostAsync(
                        It.Is<Grn>(receipt =>
                            receipt.Id == grn.Id
                            && receipt.Items.Sum(item => item.ReceivedQty * item.UnitCost) == 37.5m),
                        It.Is<PurchaseOrder>(order =>
                            order.Id == persistedOrder.Id
                            && order.WarehouseId == persistedOrder.WarehouseId),
                        1,
                        It.IsAny<CancellationToken>()),
                    Times.Once);

                var duplicate = await subject.Handler.Handle(
                    BuildCommand("immediate-receive", includeItems: false),
                    CancellationToken.None);

                Assert.True(duplicate.IsDuplicateRequest);
                Assert.Equal(first.PurchaseOrder.Id, duplicate.PurchaseOrder.Id);
                Assert.Equal(first.GrnId, duplicate.GrnId);
                Assert.Equal(3, await db.Inventories.Select(item => item.Quantity).SingleAsync());
                Assert.Single(await db.StockLedgers.ToListAsync());
                Assert.Single(await db.Grns.ToListAsync());
                subject.Journal.Verify(
                    poster => poster.PostAsync(
                        It.IsAny<Grn>(),
                        It.IsAny<PurchaseOrder>(),
                        It.IsAny<long>(),
                        It.IsAny<CancellationToken>()),
                    Times.Once);

                var failed = CreateSubject(db, 1);
                failed.Journal
                    .Setup(poster => poster.PostAsync(
                        It.IsAny<Grn>(),
                        It.IsAny<PurchaseOrder>(),
                        It.IsAny<long>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<Grn, PurchaseOrder, long, CancellationToken>(async (_, _, _, cancellationToken) =>
                    {
                        await db.SaveChangesAsync(cancellationToken);
                        throw new InvalidOperationException("Journal posting failed");
                    });

                var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => failed.Handler.Handle(BuildCommand("rollback-receive"), CancellationToken.None));

                Assert.Equal("Journal posting failed", exception.Message);
                await using var verify = await CreateMigratedContextAsync(database.ConnectionString);
                Assert.Single(await verify.PurchaseOrders.ToListAsync());
                Assert.Single(await verify.Grns.ToListAsync());
                Assert.Equal(3, await verify.Inventories.Select(item => item.Quantity).SingleAsync());
                Assert.Single(await verify.StockLedgers.ToListAsync());
            });
    }

    [Fact]
    public async Task PurchaseAndReceive_PostgresConcurrentSameKeyCreatesOneReceipt()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.PurchaseReceive,
            async database =>
            {
                await using (var setup = await CreateMigratedContextAsync(database.ConnectionString))
                    await SeedAsync(setup, 1);

                var barrier = new InitialIdempotencyLookupBarrier();
                await using var firstContext = await CreateMigratedContextAsync(database.ConnectionString, barrier);
                await using var secondContext = await CreateMigratedContextAsync(database.ConnectionString, barrier);
                var first = CreateSubject(firstContext, 1);
                var second = CreateSubject(secondContext, 1);

                var results = await Task.WhenAll(
                    first.Handler.Handle(BuildCommand("concurrent-receive"), CancellationToken.None),
                    second.Handler.Handle(BuildCommand("concurrent-receive"), CancellationToken.None));

                Assert.Equal(2, barrier.InitialLookupCount);
                Assert.Single(results, result => !result.IsDuplicateRequest);
                Assert.Single(results, result => result.IsDuplicateRequest);
                Assert.Equal(results[0].PurchaseOrder.Id, results[1].PurchaseOrder.Id);
                Assert.Equal(results[0].GrnId, results[1].GrnId);
                first.Journal.Verify(
                    poster => poster.PostAsync(
                        It.IsAny<Grn>(),
                        It.IsAny<PurchaseOrder>(),
                        It.IsAny<long>(),
                        It.IsAny<CancellationToken>()),
                    Times.AtMostOnce);
                second.Journal.Verify(
                    poster => poster.PostAsync(
                        It.IsAny<Grn>(),
                        It.IsAny<PurchaseOrder>(),
                        It.IsAny<long>(),
                        It.IsAny<CancellationToken>()),
                    Times.AtMostOnce);

                await using var verify = await CreateMigratedContextAsync(database.ConnectionString);
                Assert.Single(await verify.PurchaseOrders.ToListAsync());
                Assert.Single(await verify.Grns.ToListAsync());
                Assert.Single(await verify.GrnItems.ToListAsync());
                Assert.Equal(3, await verify.Inventories.Select(item => item.Quantity).SingleAsync());
                Assert.Single(await verify.StockLedgers.ToListAsync());
                Assert.Equal(
                    1,
                    first.Journal.Invocations.Count(invocation =>
                        invocation.Method.Name == nameof(IPurchaseReceiptJournalPoster.PostAsync))
                    + second.Journal.Invocations.Count(invocation =>
                        invocation.Method.Name == nameof(IPurchaseReceiptJournalPoster.PostAsync)));
            });
    }

    [Fact]
    public async Task PurchaseAndReceive_ValidatesIdempotencyBeforeItemsAndDeniesOtherBusinessWarehouse()
    {
        await PostgresTestDatabase.RunAsync(
            GetPostgresConnectionString(),
            PostgresTestDatabasePrefix.PurchaseReceive,
            async database =>
            {
                await using var db = await CreateMigratedContextAsync(database.ConnectionString);
                await SeedAsync(db, 1);
                await SeedAsync(db, 2);
                var subject = CreateSubject(db, 1);

                var missingKey = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => subject.Handler.Handle(
                        BuildCommand(null, includeItems: false),
                        CancellationToken.None));
                Assert.Equal("Idempotency key is required for purchase and receive action", missingKey.Message);

                var emptyItems = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => subject.Handler.Handle(
                        BuildCommand("valid-key", includeItems: false),
                        CancellationToken.None));
                Assert.Equal("Purchase order must have at least one item", emptyItems.Message);

                var denied = await Assert.ThrowsAsync<KeyNotFoundException>(
                    () => subject.Handler.Handle(
                        BuildCommand("other-business", warehouseId: 2101, supplierId: 2001),
                        CancellationToken.None));
                Assert.Equal("Warehouse with ID 2101 not found", denied.Message);

                Assert.Empty(await db.PurchaseOrders.ToListAsync());
                Assert.Empty(await db.Grns.ToListAsync());
                Assert.Empty(await db.Inventories.ToListAsync());
                Assert.Empty(await db.StockLedgers.ToListAsync());
            });
    }

    private static async Task<RetailPOSDbContext> CreateMigratedContextAsync(
        string connectionString,
        DbCommandInterceptor? interceptor = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString)
            .ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        if (interceptor is not null)
            optionsBuilder.AddInterceptors(interceptor);
        var db = new RetailPOSDbContext(optionsBuilder.Options);
        return await PostgresTestDatabase.InitializeResourceAsync(
            db, context => context.Database.MigrateAsync());
    }

    private static async Task SeedAsync(RetailPOSDbContext db, long businessId)
    {
        var warehouseId = businessId == 1 ? 1101 : 2101;
        var supplierId = businessId == 1 ? 1001 : 2001;
        var categoryId = businessId + 3000;
        var productId = businessId + 4000;
        var variantId = businessId + 5000;
        var userId = businessId + 76;
        db.Businesses.Add(new Business
        {
            Id = businessId,
            Name = $"Business {businessId}",
            IsActive = true
        });
        db.Categories.Add(new Category { Id = categoryId, BusinessId = businessId, Name = $"Category {businessId}" });
        db.Warehouses.Add(new Warehouse
        {
            Id = warehouseId,
            BusinessId = businessId,
            Name = $"Warehouse {businessId}"
        });
        db.Suppliers.Add(new Supplier
        {
            Id = supplierId,
            BusinessId = businessId,
            Name = $"Supplier {businessId}"
        });
        db.Users.Add(new User
        {
            Id = userId,
            BusinessId = businessId,
            Name = $"User {businessId}",
            Email = $"user{businessId}@example.test",
            PasswordHash = "test"
        });
        db.Products.Add(new Product
        {
            Id = productId,
            BusinessId = businessId,
            Name = $"Product {businessId}",
            CategoryId = categoryId,
            CostPrice = 10m
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = variantId,
            BusinessId = businessId,
            ProductId = productId,
            Sku = $"SKU-{businessId}",
            Name = $"Variant {businessId}"
        });
        await db.SaveChangesAsync();
    }

    private static PurchaseAndReceiveSubject CreateSubject(RetailPOSDbContext db, long businessId)
    {
        var journal = new Mock<IPurchaseReceiptJournalPoster>();
        var ledger = new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
            db,
            NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance);
        var store = new PurchaseAndReceiveStore(
            db,
            new PurchaseAndReceiveStockLedgerWriterAdapter(ledger),
            journal.Object,
            Mock.Of<ILogger<PurchaseAndReceiveStore>>());
        var readStore = new PurchaseOrderReadStore(db);
        var tenantScope = new TestPurchaseOrderTenantScope(businessId);
        return new PurchaseAndReceiveSubject(
            new PurchaseAndReceiveCommandHandler(store, readStore, tenantScope, Mock.Of<IPurchaseOrderLocationAuthorizer>()),
            journal);
    }

    private static PurchaseAndReceiveCommand BuildCommand(
        string? key,
        bool includeItems = true,
        long warehouseId = 1101,
        long supplierId = 1001) =>
        new(
            supplierId,
            new PurchaseOrderDestination("warehouse", warehouseId),
            new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc),
            null,
            "Immediate purchase",
            includeItems
                ? [new PurchaseAndReceiveItemCommand(
                    supplierId == 1001 ? 5001 : 5002,
                    3,
                    12.5m,
                    10m,
                    5m,
                    "pcs")]
                : [],
            key,
            77);

    private static string GetPostgresConnectionString()
    {
        var configuredConnection = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(configuredConnection))
            return configuredConnection;

        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        return new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build()
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("RetailPOS PostgreSQL test connection is not configured.");
    }

    private sealed record PurchaseAndReceiveSubject(
        PurchaseAndReceiveCommandHandler Handler,
        Mock<IPurchaseReceiptJournalPoster> Journal);

    private sealed class TestPurchaseOrderTenantScope(long businessId) : IPurchaseOrderTenantScope
    {
        public Task<long?> GetBusinessIdAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<long?>(businessId);
        }
    }

    private sealed class InitialIdempotencyLookupBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothInitialLookupsReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _keyLookupCount;
        private int _initialLookupCount;

        public int InitialLookupCount => Volatile.Read(ref _initialLookupCount);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            var whereIndex = command.CommandText.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
            var whereClause = whereIndex < 0 ? string.Empty : command.CommandText[whereIndex..];
            if (command.CommandText.Contains("purchase_orders", StringComparison.OrdinalIgnoreCase)
                && whereClause.Contains("idempotency_key", StringComparison.OrdinalIgnoreCase)
                && Interlocked.Increment(ref _keyLookupCount) <= 2)
            {
                var initialLookupCount = Interlocked.Increment(ref _initialLookupCount);
                if (initialLookupCount == 2)
                    _bothInitialLookupsReached.TrySetResult();
                await _bothInitialLookupsReached.Task.WaitAsync(
                    TimeSpan.FromSeconds(20),
                    cancellationToken);
            }

            return result;
        }
    }
}
