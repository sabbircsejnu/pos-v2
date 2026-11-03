using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Grns;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Infrastructure.SupplierPayments;

namespace RetailPOS.Tests.Accounting;

public class P302RelationalFaultInjectionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ThrowOnPayableDocumentSaveInterceptor _interceptor = new();

    public P302RelationalFaultInjectionTests()
    {
        _connection.Open();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA ignore_check_constraints = 1;";
            command.ExecuteNonQuery();
        }
        using var db = CreateContext();
        db.Database.EnsureCreated();
        Seed(db);
    }

    [Fact]
    public async Task CompleteAsync_RollsBackJournalAndGrn_WhenPayableProjectionPersistenceFails_ThenRetriesSafely()
    {
        await using (var failedContext = CreateContext())
        {
            _interceptor.Enabled = true;
            var handler = CreateGrnHandler(failedContext);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.Handle(new CompleteGrnCommand(40, 101), CancellationToken.None));
        }

        await using (var verifyContext = CreateContext())
        {
            Assert.Empty(await verifyContext.JournalEntries.Where(x => x.ReferenceType == "Grn" && x.ReferenceId == "40").ToListAsync());
            Assert.Empty(await verifyContext.JournalEntryLines.ToListAsync());
            Assert.Empty(await verifyContext.SupplierPayableDocuments.ToListAsync());
            Assert.Empty(await verifyContext.StockLedgers.ToListAsync());
            var inventory = await verifyContext.Inventories.SingleAsync(x => x.Id == 1);
            Assert.Equal(0, inventory.Quantity);
            Assert.Equal("full", (await verifyContext.Grns.SingleAsync(x => x.Id == 40)).Status);
        }

        _interceptor.Enabled = false;
        await using (var retryContext = CreateContext())
        {
            var result = await CreateGrnHandler(retryContext).Handle(
                new CompleteGrnCommand(40, 101),
                CancellationToken.None);
            Assert.Equal("completed", result.Status);
        }

        await using var finalContext = CreateContext();
        Assert.Single(await finalContext.JournalEntries.Where(x => x.ReferenceType == "Grn" && x.ReferenceId == "40" && x.Status == JournalEntryStatuses.Posted).ToListAsync());
        Assert.Equal(2, await finalContext.JournalEntryLines.CountAsync());
        Assert.Single(await finalContext.SupplierPayableDocuments.Where(x => x.SourceDocumentId == 40).ToListAsync());
        Assert.Single(await finalContext.Inventories.ToListAsync());
        Assert.Single(await finalContext.StockLedgers.ToListAsync());
    }

    private RetailPOSDbContext CreateContext() => new(new DbContextOptionsBuilder<RetailPOSDbContext>()
        .UseSqlite(_connection).AddInterceptors(_interceptor).Options);

    private CompleteGrnCommandHandler CreateGrnHandler(RetailPOSDbContext db)
    {
        var tenant = new Mock<ITenantAccessService>();
        tenant.Setup(x => x.RequireBusinessId()).Returns(1L);
        tenant.Setup(x => x.IsSuperAdmin).Returns(false);
        var numbers = new Mock<IJournalNumberAllocator>();
        numbers.Setup(x => x.AllocateNextJournalNumberAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<DateTime>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).ReturnsAsync("JR-001");
        var audit = Mock.Of<IAuditService>(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);
        var config = AccountingConfigurationServiceTestFactory.Create(db, tenant.Object,  NullLogger<AccountingConfigurationService>.Instance);
        var journalStore = new ManualJournalDraftStore(
            db,
            audit,
            numbers.Object,
            NullLogger<ManualJournalDraftStore>.Instance);
        var stockLedger = new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
            db,
            NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance);
        var store = new GrnCompletionStore(
            db,
            new GrnCompletionStockLedgerWriterAdapter(stockLedger),
            new GrnCompletionAccountingPosterAdapter(
                SupplierPaymentCreationTestHarness.CreateJournalCommandSender(
                    journalStore,
                    new JournalEntryReadStore(db)),
                config,
                new SupplierPayableRecognitionWriter(db)),
            new GrnCompletionAuditWriterAdapter(audit),
            NullLogger<GrnCompletionStore>.Instance);
        var authorizer = new Mock<IGrnCompletionAuthorizer>();
        authorizer.Setup(x => x.AuthorizeAsync(1, 30, "warehouse", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return new CompleteGrnCommandHandler(store, authorizer.Object);
    }

    private static void Seed(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "B" });
        db.Users.Add(new User { Id = 101, BusinessId = 1, Name = "Completer", Email = "grn-completer@example.test", PasswordHash = "test", IsActive = true });
        db.Suppliers.Add(new Supplier { Id = 10, BusinessId = 1, Name = "S" });
        db.Warehouses.Add(new Warehouse { Id = 30, BusinessId = 1, Name = "W", Address = "A" });
        db.Categories.Add(new Category { Id = 1, BusinessId = 1, Name = "C" });
        db.Products.Add(new Product { Id = 1, BusinessId = 1, Name = "P", CategoryId = 1 });
        db.ProductVariants.Add(new ProductVariant { Id = 1, BusinessId = 1, ProductId = 1, Name = "V", Sku = "V" });
        db.PurchaseOrders.Add(new PurchaseOrder { Id = 20, PoNumber = "PO-20", SupplierId = 10, WarehouseId = 30, Status = "approved", Items = [new PurchaseOrderItem { Id = 21, VariantId = 1, Quantity = 5, UnitPrice = 10m }] });
        db.Grns.Add(new Grn { Id = 40, PoId = 20, ReceivedDate = new DateTime(2026, 3, 10), Status = "full", Items = [new GrnItem { Id = 41, PoItemId = 21, ReceivedQty = 5, UnitCost = 10m }] });
        db.ChartAccounts.AddRange(Account(100, "Inventory", ChartAccountTypes.Asset), Account(200, "Payable", ChartAccountTypes.Liability), Account(300, "Advance", ChartAccountTypes.Liability));
        db.AccountingConfigurations.Add(new AccountingConfiguration { BusinessId = 1, InventoryAccountId = 100, SupplierPayableAccountId = 200, SupplierAdvanceAccountId = 300, JournalNumberPrefix = "JR", JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear });
        db.FinancialYears.Add(new FinancialYear { Id = 1, BusinessId = 1, YearCode = "FY26", Name = "FY26", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true });
        db.AccountingPeriods.Add(new AccountingPeriod { Id = 1, FinancialYearId = 1, PeriodNumber = 3, Name = "Mar", StartDate = new DateTime(2026, 3, 1), EndDate = new DateTime(2026, 3, 31), Status = AccountingPeriodStatuses.Open });
        db.SaveChanges();
        db.Database.ExecuteSqlRaw("INSERT INTO inventories (id, variant_id, location_id, location_type, quantity, low_stock_threshold, xmin) VALUES (1, 1, 30, 'warehouse', 0, 10, 1)");
    }

    private static ChartAccount Account(long id, string name, string type) => new() { Id = id, BusinessId = 1, AccountCode = id.ToString(), AccountName = name, AccountType = type, NormalBalanceType = type == ChartAccountTypes.Asset ? NormalBalanceTypes.Debit : NormalBalanceTypes.Credit, IsActive = true };

    public void Dispose() => _connection.Dispose();

    private sealed class ThrowOnPayableDocumentSaveInterceptor : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (Enabled && eventData.Context?.ChangeTracker.Entries<SupplierPayableDocument>().Any(x => x.State == EntityState.Added) == true)
                throw new InvalidOperationException("Injected payable projection persistence failure.");
            return result;
        }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => new(SavingChanges(eventData, result));
    }
}