using Microsoft.EntityFrameworkCore;
using Moq;
using RetailPOS.Application.AccountingReports.Abstractions;
using RetailPOS.Application.VatReports.Queries;
using RetailPOS.Application.VatReports.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.AccountingReports;
using RetailPOS.Infrastructure.Data;
using Xunit;

using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class P305VatReportAcceptanceTests
{
    [Fact]
    public async Task Reconciliation_CleanAndControlledMismatchesRemainIndependent()
    {
        await using var db = CreateDb();
        SeedVat(db);
        var filter = Filter();
        var clean = await GetReconciliationAsync(db, filter);
        Assert.Equal(10m, clean.ReportOutputVat);
        Assert.Equal(10m, clean.GlOutputVat);
        Assert.Equal(5m, clean.ReportInputVat);
    Assert.Equal(5m, clean.GlInputVat);
        Assert.Equal(0m, clean.OutputDifference);
        Assert.Equal(0m, clean.InputDifference);
        Assert.Equal(0m, clean.OverallDifference);

        db.JournalEntryLines.Add(new JournalEntryLine { Id = 99, JournalEntryId = 10, AccountId = 100, CreditAmount = 2m, DebitAmount = 0m });
        await db.SaveChangesAsync();
        var mismatch = await GetReconciliationAsync(db, filter);
        Assert.Equal(-2m, mismatch.OutputDifference);
        Assert.Equal(0m, mismatch.InputDifference);
        Assert.Equal(-2m, mismatch.OverallDifference);
    }

    [Fact]
    public async Task ReversalAppearsInItsPostingPeriodAndOriginalPeriodRemainsUnchanged()
    {
        await using var db = CreateDb();
        SeedVat(db);
        var original = await GetSummaryAsync(
            db,
            new VatReportFilter(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31)));
        var reversal = await GetSummaryAsync(
            db,
            new VatReportFilter(new DateTime(2026, 4, 1), new DateTime(2026, 4, 30)));
        Assert.Equal(10m, original.TotalOutputVat);
        Assert.Equal(-10m, reversal.TotalOutputVat);
        var detail = await GetTransactionsAsync(
            db,
            new VatReportFilter(new DateTime(2026, 4, 1), new DateTime(2026, 4, 30)));
        Assert.Contains(detail, x => x.SourceType == "SaleRefund" && x.VatAmount == -10m && x.OriginalJournalEntryId == 10);
    }

    [Fact]
    public async Task AReceiptAndSaleJustAfterCompanyMidnight_FallInTheCompanyMonth()
    {
        await using var db = CreateDb();
        SeedVat(db);
        // 18:30 UTC on 31 March is 00:30 on 1 April in the Company zone, Asia/Dhaka.
        var justAfterMidnight = new DateTime(2026, 3, 31, 18, 30, 0, DateTimeKind.Utc);
        db.Sales.Add(new Sale { Id = 5, SaleNumber = "SALE-5", OutletId = 1, SaleDate = justAfterMidnight, Tax = 3m, TotalAmount = 33m, PaymentMethod = "cash", Status = "completed", CashierId = 1, Items = [new SaleItem { SaleId = 5, VariantId = 1, Quantity = 1, UnitPrice = 30, Subtotal = 30, TaxableAmount = 30, VatRate = 10, VatAmount = 3, TaxCode = "VAT", TaxInclusive = false }] });
        db.PurchaseOrders.Add(new PurchaseOrder { Id = 6, PoNumber = "PO-6", SupplierId = 1, OutletId = 1, OrderDate = justAfterMidnight, Status = "received" });
        db.PurchaseOrderItems.Add(new PurchaseOrderItem { Id = 6, PoId = 6, VariantId = 1, Quantity = 1, UnitPrice = 20 });
        db.Grns.Add(new Grn { Id = 6, PoId = 6, ReceivedDate = justAfterMidnight, Status = "completed", Items = [new GrnItem { Id = 6, GrnId = 6, PoItemId = 6, ReceivedQty = 1, UnitCost = 20, TaxableAmount = 20, VatRate = 10, VatAmount = 2, TaxCode = "VAT" }] });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var march = await GetTransactionsAsync(db, new VatReportFilter(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31)));
        var april = await GetTransactionsAsync(db, new VatReportFilter(new DateTime(2026, 4, 1), new DateTime(2026, 4, 30)));

        Assert.DoesNotContain(march, row => row.VatAmount is 3m or 2m);
        Assert.Contains(april, row => row.VatAmount == 3m);
        Assert.Contains(april, row => row.VatAmount == 2m);
    }

    [Fact]
    public async Task LegacyIncompleteSnapshotIsExcludedAndSurfaced()
    {
        await using var db = CreateDb();
        SeedVat(db);
        db.Sales.Add(new Sale { Id = 2, SaleNumber = "LEGACY-1", OutletId = 1, SaleDate = new DateTime(2026, 3, 20), Tax = 7m, TotalAmount = 107m, PaymentMethod = "cash", Status = "completed", CashierId = 1, Items = [new SaleItem { SaleId = 2, VariantId = 1, Quantity = 1, UnitPrice = 100, Subtotal = 100 }] });
        await db.SaveChangesAsync();
        var summary = await GetSummaryAsync(db, Filter());
        var exceptions = await GetExceptionsAsync(db, Filter());
        Assert.Equal(10m, summary.TotalOutputVat);
        Assert.Contains(exceptions, x => x.SourceReference == "LEGACY-1");
    }

    [Fact]
    public async Task LocationFilterChangesDetailsAndSummaryAndDoesNotLeakOtherBusiness()
    {
        await using var db = CreateDb();
        SeedVat(db);
        db.Outlets.Add(new Outlet { Id = 2, BusinessId = 1, Name = "Other", Address = "x" });
        db.Sales.Add(new Sale { Id = 3, SaleNumber = "OUTLET-2", OutletId = 2, SaleDate = new DateTime(2026, 3, 20), Tax = 4m, TotalAmount = 104m, PaymentMethod = "cash", Status = "completed", CashierId = 1, Items = [new SaleItem { SaleId = 3, VariantId = 1, Quantity = 1, UnitPrice = 100, Subtotal = 100, TaxableAmount = 100, VatRate = 4, VatAmount = 4, TaxCode = "VAT", TaxInclusive = false }] });
        await db.SaveChangesAsync();
        var outletOne = await GetSummaryAsync(db, Filter(1));
        var outletTwo = await GetSummaryAsync(db, Filter(2));
        Assert.Equal(10m, outletOne.TotalOutputVat);
        Assert.Equal(4m, outletTwo.TotalOutputVat);
        Assert.All(await GetTransactionsAsync(db, Filter(1)), x => Assert.Equal(1, x.OutletId));
    }

    private static VatReportFilter Filter(long? outletId = null) =>
        new(new DateTime(2026, 3, 1), new DateTime(2026, 3, 31), OutletId: outletId);

    private static Task<VatSummaryReadModel> GetSummaryAsync(
        RetailPOSDbContext db,
        VatReportFilter filter) =>
        new GetVatSummaryQueryHandler(new VatReportReadStore(db, TestCompanySettings.Provider()), TenantScope())
            .Handle(new GetVatSummaryQuery(filter), CancellationToken.None);

    private static Task<VatReconciliationReadModel> GetReconciliationAsync(
        RetailPOSDbContext db,
        VatReportFilter filter) =>
        new GetVatReconciliationQueryHandler(new VatReportReadStore(db, TestCompanySettings.Provider()), TenantScope())
            .Handle(new GetVatReconciliationQuery(filter), CancellationToken.None);

    private static Task<IReadOnlyList<VatTransactionReadModel>> GetTransactionsAsync(
        RetailPOSDbContext db,
        VatReportFilter filter) =>
        new GetVatTransactionsQueryHandler(new VatReportReadStore(db, TestCompanySettings.Provider()), TenantScope())
            .Handle(new GetVatTransactionsQuery(filter), CancellationToken.None);

    private static Task<IReadOnlyList<VatExceptionReadModel>> GetExceptionsAsync(
        RetailPOSDbContext db,
        VatReportFilter filter) =>
        new GetVatExceptionsQueryHandler(new VatReportReadStore(db, TestCompanySettings.Provider()), TenantScope())
            .Handle(new GetVatExceptionsQuery(filter), CancellationToken.None);

    private static IAccountingReportTenantScope TenantScope()
    {
        var tenantScope = new Mock<IAccountingReportTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);
        return tenantScope.Object;
    }

    private static RetailPOSDbContext CreateDb() => new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static void SeedVat(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "B" });
        db.Outlets.Add(new Outlet { Id = 1, BusinessId = 1, Name = "Main", Address = "x" });
        db.Suppliers.Add(new Supplier { Id = 1, BusinessId = 1, Name = "S" });
        db.ChartAccounts.AddRange(Account(100, "VAT Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit), Account(101, "VAT Receivable", ChartAccountTypes.Asset, NormalBalanceTypes.Debit));
        db.AccountingConfigurations.Add(new AccountingConfiguration { Id = 1, BusinessId = 1, VatPayableAccountId = 100, VatReceivableAccountId = 101 });
        var saleJournal = new JournalEntry { Id = 10, BusinessId = 1, JournalNumber = "J-10", JournalType = "SalePosting", Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 3, 20), TransactionDate = new DateTime(2026, 3, 20), ReferenceType = "Sale", ReferenceId = "1", Lines = [new JournalEntryLine { Id = 11, AccountId = 100, CreditAmount = 10, DebitAmount = 0 }] };
        var reversalJournal = new JournalEntry { Id = 20, BusinessId = 1, JournalNumber = "J-20", JournalType = AccountingJournalTypes.JournalReversal, Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 4, 5), TransactionDate = new DateTime(2026, 4, 5), ReferenceType = "Sale", ReferenceId = "1", ReversalOfJournalEntryId = 10, Lines = [new JournalEntryLine { Id = 21, AccountId = 100, CreditAmount = 0, DebitAmount = 10 }] };
        var inputJournal = new JournalEntry { Id = 30, BusinessId = 1, JournalNumber = "J-30", JournalType = "System", Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 3, 21), TransactionDate = new DateTime(2026, 3, 21), ReferenceType = "Expense", ReferenceId = "1", Lines = [new JournalEntryLine { Id = 31, AccountId = 101, DebitAmount = 5, CreditAmount = 0 }] };
        db.JournalEntries.AddRange(saleJournal, reversalJournal, inputJournal);
        db.Sales.Add(new Sale { Id = 1, SaleNumber = "SALE-1", OutletId = 1, SaleDate = new DateTime(2026, 3, 20), Tax = 10, TotalAmount = 110, PaymentMethod = "cash", Status = "completed", CashierId = 1, Items = [new SaleItem { SaleId = 1, VariantId = 1, Quantity = 1, UnitPrice = 100, Subtotal = 100, TaxableAmount = 100, VatRate = 10, VatAmount = 10, TaxCode = "VAT", TaxInclusive = false }] });
        db.Expenses.Add(new Expense { Id = 1, BusinessId = 1, Amount = 55, TaxableAmount = 50, VatRate = 10, VatAmount = 5, TaxCode = "VAT", TaxInclusive = false, ExpenseDate = new DateTime(2026, 3, 21), Status = ExpenseStatuses.Approved, PaymentAccountId = 101 });
        db.SaveChanges();
        Assert.Single(db.Sales);
        Assert.Equal(3, db.JournalEntries.Count());
        Assert.Contains(1L, db.Outlets.Select(x => x.Id));
        Assert.Equal(10m, db.Sales.Single().Items.Single().VatAmount);
    }
    private static ChartAccount Account(long id, string name, string type, string normal) => new() { Id = id, BusinessId = 1, AccountCode = id.ToString(), AccountName = name, AccountType = type, NormalBalanceType = normal, IsActive = true };
}
