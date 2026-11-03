using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.DTOs.Pos;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.Commands;
using RetailPOS.Application.Pos.Abstractions;
using RetailPOS.Application.Pos.Commands;
using RetailPOS.Infrastructure.Pos;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Grns;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Infrastructure.SupplierPayments;

namespace RetailPOS.Tests.Accounting;

public sealed class P308RetryAtomicityTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public P308RetryAtomicityTests()
    {
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA ignore_check_constraints = 1;";
        command.ExecuteNonQuery();
        using var db = CreateContext();
        db.Database.EnsureCreated();
        Seed(db);
    }

    [Fact]
    public async Task GrnCompletionRetry_DoesNotDuplicateInventoryOrAuthoritativeJournal()
    {
        await using (var context = CreateContext())
        {
            var handler = CreateGrnHandler(context);
            var first = await handler.Handle(
                new CompleteGrnCommand(40, 10),
                CancellationToken.None);
            Assert.Equal("completed", first.Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                handler.Handle(new CompleteGrnCommand(40, 10), CancellationToken.None));
        }

        await using var verify = CreateContext();
        Assert.Equal("completed", await verify.Grns.Where(x => x.Id == 40).Select(x => x.Status).SingleAsync());
        Assert.Equal(5, await verify.Inventories.Where(x => x.VariantId == 1 && x.LocationId == 30 && x.LocationType == "warehouse").Select(x => x.Quantity).SingleAsync());
        var postedJournal = await verify.JournalEntries.SingleAsync(x => x.ReferenceType == "Grn" && x.ReferenceId == "40" && x.Status == JournalEntryStatuses.Posted);
        Assert.Equal(10L, postedJournal.CreatedBy);
        Assert.Equal(10L, postedJournal.PostedBy);
        Assert.Equal(2, await verify.JournalEntryLines.Where(x => x.JournalEntry.ReferenceType == "Grn" && x.JournalEntry.ReferenceId == "40").CountAsync());
        Assert.Single(await verify.SupplierPayableDocuments.Where(x => x.SourceDocumentId == 40).ToListAsync());
        Assert.Single(await verify.StockLedgers.ToListAsync());
    }

    [Fact]
    public async Task CashSessionCloseCommandRetry_ReturnsClosedSessionAndCreatesNoJournalOrSecondSnapshot()
    {
        await using (var context = CreateContext())
        {
            var handler = CreateCashSessionCloseHandler(context);
            var first = await handler.Handle(new CloseCashSessionCommand(80, 120m, "Surplus counted", 9), CancellationToken.None);
            var retry = await handler.Handle(new CloseCashSessionCommand(80, 120m, "Duplicate retry", 9), CancellationToken.None);
            Assert.Equal("closed", first.Status);
            Assert.Equal("closed", retry.Status);
        }

        await using var verify = CreateContext();
        var session = await verify.CashSessions.SingleAsync(x => x.Id == 80);
        Assert.Equal("closed", session.Status);
        Assert.Equal(20m, session.DifferenceAmount);
        // D2: closing never posts the variance journal; it only records one pending approval fact.
        Assert.Empty(await verify.JournalEntries.Where(x => x.ReferenceType == "CashSessionClose").ToListAsync());
        Assert.Single(await verify.CashSessionCloseSnapshots.Where(x => x.CashSessionId == 80).ToListAsync());
        var approval = Assert.Single(await verify.CashDifferenceApprovals.Where(x => x.CashSessionId == 80).ToListAsync());
        Assert.Equal(CashDifferenceStatuses.Pending, approval.Status);
        Assert.Equal(CashDifferencePostingStatuses.NotRequired, approval.PostingStatus);
    }

    [Fact]
    public async Task CashSessionCloseCommand_RollsBackSnapshotAndApprovalWhenClosePersistenceFails()
    {
        await using (var context = CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_cash_session_close
                BEFORE UPDATE OF status ON cash_sessions
                WHEN OLD.id = 80
                BEGIN
                    SELECT RAISE(ABORT, 'cash session close fault');
                END;
                """);

            var handler = CreateCashSessionCloseHandler(context);
            await Assert.ThrowsAnyAsync<Exception>(() => handler.Handle(
                new CloseCashSessionCommand(80, 120m, "Surplus counted", 9),
                CancellationToken.None));
        }

        await using var verify = CreateContext();
        Assert.Equal("open", await verify.CashSessions.Where(session => session.Id == 80)
            .Select(session => session.Status)
            .SingleAsync());
        Assert.Empty(await verify.CashSessionCloseSnapshots.ToListAsync());
        Assert.Empty(await verify.CashDifferenceApprovals.ToListAsync());
    }

    private CompleteGrnCommandHandler CreateGrnHandler(RetailPOSDbContext db)
    {
        var tenant = Tenant();
        var numbers = new Mock<IJournalNumberAllocator>();
        numbers.Setup(service => service.AllocateNextJournalNumberAsync(
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<DateTime>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"GRN-{Guid.NewGuid():N}");
        var audit = Audit();
        var config = Configuration();
        var journalStore = new ManualJournalDraftStore(
            db,
            audit.Object,
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
                config.Object,
                new SupplierPayableRecognitionWriter(db)),
            new GrnCompletionAuditWriterAdapter(audit.Object),
            NullLogger<GrnCompletionStore>.Instance);
        var authorizer = new Mock<IGrnCompletionAuthorizer>();
        authorizer.Setup(x => x.AuthorizeAsync(1, 30, "warehouse", It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return new CompleteGrnCommandHandler(store, authorizer.Object);
    }

    private CloseCashSessionCommandHandler CreateCashSessionCloseHandler(RetailPOSDbContext db)
    {
        var authorizer = new Mock<ICashSessionWriteOutletAuthorizer>();
        authorizer.Setup(port => port.AuthorizeWriteAsync(11, It.IsAny<CancellationToken>()))
            .ReturnsAsync(11);

        return new CloseCashSessionCommandHandler(
            new CashSessionStore(db),
            authorizer.Object,
            Mock.Of<ICashSessionCloseAuditWriter>(),
            Mock.Of<ICashSessionClosedPublisher>(),
            new RetailPOS.IntegrationTests.Infrastructure.NullCashSessionEvents(),
            TimeProvider.System);
    }

    private static Mock<ITenantAccessService> Tenant()
    {
        var tenant = new Mock<ITenantAccessService>();
        tenant.Setup(x => x.RequireBusinessId()).Returns(1L);
        tenant.Setup(x => x.EffectiveBusinessId).Returns(1L);
        tenant.Setup(x => x.IsSuperAdmin).Returns(false);
        return tenant;
    }

    private static Mock<IJournalNumberAllocator> JournalNumbers()
    {
        var numbers = new Mock<IJournalNumberAllocator>();
        numbers.Setup(x => x.AllocateNextJournalNumberAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<DateTime>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => $"JR-{Guid.NewGuid():N}");
        return numbers;
    }

    private static Mock<IAccountingConfigurationService> Configuration()
    {
        var config = new Mock<IAccountingConfigurationService>();
        config.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Cash, It.IsAny<long?>())).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Cash, AccountId = 100, Source = "Test" });
        config.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.Inventory, It.IsAny<long?>())).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.Inventory, AccountId = 101, Source = "Test" });
        config.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.SupplierPayable, It.IsAny<long?>())).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.SupplierPayable, AccountId = 102, Source = "Test" });
        config.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.CashDifferenceGain, It.IsAny<long?>())).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.CashDifferenceGain, AccountId = 103, Source = "Test" });
        config.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.CashDifferenceLoss, It.IsAny<long?>())).ReturnsAsync(new PostingAccountResolutionDto { Slot = PostingAccountSlots.CashDifferenceLoss, AccountId = 104, Source = "Test" });
        config.Setup(x => x.GetBusinessConfigurationAsync()).ReturnsAsync(new AccountingConfigurationDto { CashAccountId = 100, InventoryAccountId = 101, SupplierPayableAccountId = 102, CashDifferenceGainAccountId = 103, CashDifferenceLossAccountId = 104, DefaultSupplierPaymentTermsDays = 30 });
        return config;
    }

    private static Mock<IAuditService> Audit()
    {
        var audit = new Mock<IAuditService>();
        audit.Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        audit.Setup(x => x.RecordFailureAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return audit;
    }

    private RetailPOSDbContext CreateContext() => new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(_connection).Options);

    private static void Seed(RetailPOSDbContext db)
    {
        var today = DateTime.UtcNow.Date;
        db.Businesses.Add(new Business { Id = 1, Name = "Retry Atomicity" });
        db.Suppliers.Add(new Supplier { Id = 10, BusinessId = 1, Name = "Supplier" });
        db.Warehouses.Add(new Warehouse { Id = 30, BusinessId = 1, Name = "Warehouse", Address = "A" });
        db.Outlets.Add(new Outlet { Id = 11, BusinessId = 1, Name = "Outlet", Address = "A" });
        db.Users.Add(new User { Id = 9, BusinessId = 1, Name = "Actor", Email = "actor@example.test", PasswordHash = "test", OutletId = 11, IsActive = true });
        db.Users.Add(new User { Id = 10, BusinessId = 1, Name = "GRN Completer", Email = "grn-completer@example.test", PasswordHash = "test", OutletId = 11, IsActive = true });
        db.PosTerminals.Add(new PosTerminal { Id = 12, OutletId = 11, Name = "Terminal", Code = "T-1", IsActive = true });
        db.Categories.Add(new Category { Id = 1, BusinessId = 1, Name = "Category" });
        db.Products.Add(new Product { Id = 1, BusinessId = 1, Name = "Product", CategoryId = 1 });
        db.ProductVariants.Add(new ProductVariant { Id = 1, BusinessId = 1, ProductId = 1, Name = "Variant", Sku = "V-1" });
        db.PurchaseOrders.Add(new PurchaseOrder { Id = 20, PoNumber = "PO-20", SupplierId = 10, WarehouseId = 30, Status = "approved", Items = [new PurchaseOrderItem { Id = 21, VariantId = 1, Quantity = 5, UnitPrice = 10m }] });
        db.Grns.Add(new Grn { Id = 40, PoId = 20, ReceivedDate = today, Status = "full", CreatedBy = 9, Items = [new GrnItem { Id = 41, PoItemId = 21, ReceivedQty = 5, UnitCost = 10m }] });
        db.CashSessions.Add(new CashSession { Id = 80, BusinessId = 1, OutletId = 11, TerminalId = 12, BusinessDate = today, OpenedAt = today.AddHours(8), OpeningCashAmount = 100m, Status = "open", OpenedByUserId = 9, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.ChartAccounts.AddRange(
            Account(100, "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(101, "Inventory", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(102, "Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(103, "Cash Difference Gain", ChartAccountTypes.Income, NormalBalanceTypes.Credit),
            Account(104, "Cash Difference Loss", ChartAccountTypes.Expense, NormalBalanceTypes.Debit));
        db.AccountingConfigurations.Add(new AccountingConfiguration { BusinessId = 1, CashAccountId = 100, InventoryAccountId = 101, SupplierPayableAccountId = 102, CashDifferenceGainAccountId = 103, CashDifferenceLossAccountId = 104, JournalNumberPrefix = "JR", JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear });
        db.FinancialYears.Add(new FinancialYear { Id = 1, BusinessId = 1, YearCode = "FY", Name = "FY", StartDate = today.AddDays(-30), EndDate = today.AddDays(30), IsCurrent = true });
        db.AccountingPeriods.Add(new AccountingPeriod { Id = 1, FinancialYearId = 1, PeriodNumber = 1, Name = "Open", StartDate = today.AddDays(-30), EndDate = today.AddDays(30), Status = AccountingPeriodStatuses.Open });
        db.SaveChanges();
        db.Database.ExecuteSqlRaw("INSERT INTO inventories (id, variant_id, location_id, location_type, quantity, low_stock_threshold, xmin) VALUES (1, 1, 30, 'warehouse', 0, 10, 1)");
    }

    private static ChartAccount Account(long id, string name, string type, string normalBalance) => new() { Id = id, BusinessId = 1, AccountCode = id.ToString(), AccountName = name, AccountType = type, NormalBalanceType = normalBalance, IsActive = true, AllowManualPosting = true };

    public void Dispose() => _connection.Dispose();
}
