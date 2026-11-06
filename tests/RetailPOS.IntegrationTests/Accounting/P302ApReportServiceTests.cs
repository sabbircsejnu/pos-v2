using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.ApReports.Abstractions;
using RetailPOS.Application.ApReports.Queries;
using RetailPOS.API.DTOs.Reports;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.ApReports;
using Xunit;

using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class P302ApReportServiceTests
{
    [Fact]
    public async Task Subledger_ReconstructsBeforeBetweenAndAfterReversalDates()
    {
        await using var db = CreateDb();
        SeedHistory(db);

        var beforePayment = await GetSubledgerAsync(db, new ApReportFilterDto { AsOfDate = new DateTime(2026, 3, 10) });
        var between = await GetSubledgerAsync(db, new ApReportFilterDto { AsOfDate = new DateTime(2026, 3, 25) });
        var afterReversal = await GetSubledgerAsync(db, new ApReportFilterDto { AsOfDate = new DateTime(2026, 4, 25) });

        Assert.Equal(100m, beforePayment.TotalOutstanding);
        Assert.Equal(50m, between.TotalOutstanding);
        Assert.Equal(SupplierPayableDocumentStatuses.Partial, between.Rows.Single().Status);
        Assert.Equal(100m, afterReversal.TotalOutstanding);
        Assert.Equal(SupplierPayableDocumentStatuses.Open, afterReversal.Rows.Single().Status);
        Assert.Equal(40m, afterReversal.Rows.Single().ReversedPaymentAllocations);
        Assert.Equal(10m, afterReversal.Rows.Single().ReversedAdvanceUtilisations);
    }

    [Fact]
    public async Task Aging_UsesPersistedDueDateAndReturnsEveryApprovedBucketAndDisputedAmount()
    {
        await using var db = CreateDb();
        SeedAging(db);
        var result = await GetAgingAsync(db, new ApReportFilterDto { AsOfDate = new DateTime(2026, 6, 30) });

        Assert.Equal(150m, result.TotalOutstanding);
        Assert.Equal(10m, result.CurrentNotDue);
        Assert.Equal(20m, result.Overdue1To30);
        Assert.Equal(30m, result.Overdue31To60);
        Assert.Equal(40m, result.Overdue61To90);
        Assert.Equal(50m, result.Overdue90Plus);
        Assert.Equal(50m, result.DisputedAmount);
        Assert.Equal(5, result.Rows.Count);

        var overdueOnly = await GetAgingAsync(db, new ApReportFilterDto { AsOfDate = new DateTime(2026, 6, 30), AgingBucket = "31–60 days overdue" });
        Assert.Equal(30m, overdueOnly.TotalOutstanding);
        Assert.Single(overdueOnly.Rows);
    }

    [Fact]
    public async Task SupplierStatement_OpeningPlusMovementsEqualsClosingWithDeterministicOrdering()
    {
        await using var db = CreateDb();
        SeedHistory(db);
        var result = await GetSupplierStatementAsync(
            db,
            new ApReportFilterDto
            {
                SupplierId = 10,
                DateFrom = new DateTime(2026, 3, 1),
                DateTo = new DateTime(2026, 4, 30)
            });

        Assert.Equal(0m, result.OpeningBalance);
        Assert.Equal(100m, result.NewPayables);
        Assert.Equal(40m, result.SupplierPayments);
        Assert.Equal(10m, result.SupplierAdvanceApplications);
        Assert.Equal(50m, result.Reversals);
        Assert.Equal(result.OpeningBalance + result.NewPayables - result.SupplierPayments - result.SupplierAdvanceApplications + result.Reversals, result.ClosingBalance);
        Assert.Equal(result.Rows.OrderBy(x => x.Date).ThenBy(x => x.PayableDocumentId).ThenBy(x => x.EventType).Select(x => x.EventType), result.Rows.Select(x => x.EventType));
    }

    [Fact]
    public async Task Reconciliation_ComparesIndependentSubledgerAndGlAndSurfacesDifference()
    {
        await using var db = CreateDb();
        SeedHistory(db);
        var clean = await GetApReconciliationAsync(db, new ApReportFilterDto { AsOfDate = new DateTime(2026, 3, 25) });
        Assert.Equal(0m, clean.ReconciliationDifference);

        db.JournalEntryLines.Add(new JournalEntryLine { Id = 99, JournalEntryId = 100, AccountId = 201, CreditAmount = 5m, DebitAmount = 0m });
        await db.SaveChangesAsync();
        var difference = await GetApReconciliationAsync(db, new ApReportFilterDto { AsOfDate = new DateTime(2026, 3, 25) });
        Assert.Equal(-5m, difference.ReconciliationDifference);
        Assert.NotEqual(0m, difference.GeneralLedgerBalance - difference.SubledgerTotal);
    }

    [Fact]
    public async Task Subledger_ExcludesOtherBusinessesAndReportsLegacyBillsSeparately()
    {
        await using var db = CreateDb();
        SeedHistory(db);
        db.Businesses.Add(new Business { Id = 2, Name = "Other" });
        db.Suppliers.Add(new Supplier { Id = 20, BusinessId = 2, Name = "Other supplier" });
        db.SupplierPayableDocuments.Add(new SupplierPayableDocument { Id = 20, BusinessId = 2, SupplierId = 20, OriginalPayableAmount = 999, RecognitionDate = new DateTime(2026, 3, 1), DueDate = new DateTime(2026, 3, 31), SourceReference = "OTHER", SourceDocumentType = "GRN", SourceDocumentId = 20, SourceJournalEntryId = 100 });
        db.Bills.Add(new Bill { Id = 77, SupplierId = 10, AmountDue = 25, DueDate = new DateTime(2026, 3, 31), Status = "unpaid" });
        await db.SaveChangesAsync();

        var result = await GetSubledgerAsync(db, new ApReportFilterDto { AsOfDate = new DateTime(2026, 3, 25) });
        Assert.DoesNotContain(result.Rows, x => x.SupplierId == 20);
        Assert.Equal(1, result.Exceptions.LegacyUnreconciledCount);
        Assert.Contains("legacy Bill", result.Exceptions.Details.Single());
    }

    [Fact]
    public async Task HistoricalStatus_DerivesOpenPartialPaidFromDatedEventsNotCurrentStatus()
    {
        await using var db = CreateDb();
        SeedHistory(db);
        db.SupplierPaymentAllocations.Add(new SupplierPaymentAllocation { Id = 2, SupplierPaymentId = 2, SupplierPayableDocumentId = 1, PayableDocumentId = 1, AllocatedAmount = 50, Status = "effective", EffectiveDate = new DateTime(2026, 3, 30), JournalEntryId = 105 });
        await db.SaveChangesAsync();

        Assert.Equal(SupplierPayableDocumentStatuses.Open, (await GetSubledgerAsync(db, new ApReportFilterDto { AsOfDate = new DateTime(2026, 3, 10) })).Rows.Single().Status);
        Assert.Equal(SupplierPayableDocumentStatuses.Partial, (await GetSubledgerAsync(db, new ApReportFilterDto { AsOfDate = new DateTime(2026, 3, 25) })).Rows.Single().Status);
        Assert.Equal(SupplierPayableDocumentStatuses.Paid, (await GetSubledgerAsync(db, new ApReportFilterDto { AsOfDate = new DateTime(2026, 3, 31) })).Rows.Single().Status);
    }

    [Fact]
    public async Task Aging_UsesExactDueDateBoundariesAndPersistedSnapshot()
    {
        await using var db = CreateDb();
        SeedAgingBoundaries(db);
        var supplier = await db.Suppliers.SingleAsync(x => x.Id == 10);
        supplier.PaymentTermsDays = 999;
        await db.SaveChangesAsync();
        var result = await GetAgingAsync(db, new ApReportFilterDto { AsOfDate = new DateTime(2026, 6, 30) });

        Assert.Equal(10m, result.CurrentNotDue);
        Assert.Equal(20m, result.Overdue1To30);
        Assert.Equal(20m, result.Overdue31To60);
        Assert.Equal(20m, result.Overdue61To90);
        Assert.Equal(20m, result.Overdue90Plus);
        Assert.Contains(result.Rows, x => x.DueDate == new DateTime(2026, 5, 30));
    }

    [Fact]
    public async Task AdvanceReconciliation_ReconstructsUnappliedBalanceAndSurfacesMismatch()
    {
        await using var db = CreateDb();
        SeedAdvanceReconciliation(db);
        var beforeReversals = await GetSupplierAdvanceReconciliationAsync(
            db,
            new ApReportFilterDto { AsOfDate = new DateTime(2026, 3, 31) });
        Assert.Equal(160m, beforeReversals.SubledgerTotal);
        Assert.Equal(160m, beforeReversals.GeneralLedgerBalance);
        Assert.Equal(0m, beforeReversals.ReconciliationDifference);

        var afterReversals = await GetSupplierAdvanceReconciliationAsync(
            db,
            new ApReportFilterDto { AsOfDate = new DateTime(2026, 4, 30) });
        Assert.Equal(150m, afterReversals.SubledgerTotal);
        Assert.Equal(150m, afterReversals.GeneralLedgerBalance);
        Assert.Equal(0m, afterReversals.ReconciliationDifference);

        db.JournalEntryLines.Add(new JournalEntryLine { Id = 999, JournalEntryId = 500, AccountId = 202, CreditAmount = 5, DebitAmount = 0 });
        await db.SaveChangesAsync();
        var mismatch = await GetSupplierAdvanceReconciliationAsync(
            db,
            new ApReportFilterDto { AsOfDate = new DateTime(2026, 3, 31) });
        Assert.Equal(-5m, mismatch.ReconciliationDifference);
    }

    private static RetailPOSDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RetailPOSDbContext(options);
    }

    private static Task<RetailPOS.Application.ApReports.ReadModels.ApReconciliationReadModel> GetApReconciliationAsync(
        RetailPOSDbContext db,
        ApReportFilterDto filter)
    {
        var handler = new GetApReconciliationQueryHandler(
            new ApPayablesReportReadStore(db, TestCompanySettings.Provider()),
            new TestApReportTenantScope(1));
        return handler.Handle(
            new GetApReconciliationQuery(ApReportReadModelMapper.ToApplicationFilter(filter)),
            CancellationToken.None);
    }

    private static Task<RetailPOS.Application.ApReports.ReadModels.ApReconciliationReadModel> GetSupplierAdvanceReconciliationAsync(
        RetailPOSDbContext db,
        ApReportFilterDto filter)
    {
        var handler = new GetSupplierAdvanceReconciliationQueryHandler(
            new ApPayablesReportReadStore(db, TestCompanySettings.Provider()),
            new TestApReportTenantScope(1));
        return handler.Handle(
            new GetSupplierAdvanceReconciliationQuery(ApReportReadModelMapper.ToApplicationFilter(filter)),
            CancellationToken.None);
    }

    private static Task<RetailPOS.Application.ApReports.ReadModels.ApSubledgerReadModel> GetSubledgerAsync(
        RetailPOSDbContext db,
        ApReportFilterDto filter)
    {
        var handler = new GetApSubledgerQueryHandler(
            new ApPayablesReportReadStore(db, TestCompanySettings.Provider()),
            new TestApReportTenantScope(1));
        return handler.Handle(
            new GetApSubledgerQuery(ApReportReadModelMapper.ToApplicationFilter(filter)),
            CancellationToken.None);
    }

    private static Task<RetailPOS.Application.ApReports.ReadModels.ApAgingReadModel> GetAgingAsync(
        RetailPOSDbContext db,
        ApReportFilterDto filter)
    {
        var handler = new GetApAgingQueryHandler(
            new ApPayablesReportReadStore(db, TestCompanySettings.Provider()),
            new TestApReportTenantScope(1));
        return handler.Handle(
            new GetApAgingQuery(ApReportReadModelMapper.ToApplicationFilter(filter)),
            CancellationToken.None);
    }

    private static Task<RetailPOS.Application.ApReports.ReadModels.SupplierStatementReadModel> GetSupplierStatementAsync(
        RetailPOSDbContext db,
        ApReportFilterDto filter)
    {
        var handler = new GetSupplierStatementQueryHandler(
            new ApPayablesReportReadStore(db, TestCompanySettings.Provider()),
            new TestApReportTenantScope(1));
        return handler.Handle(
            new GetSupplierStatementQuery(ApReportReadModelMapper.ToApplicationFilter(filter)),
            CancellationToken.None);
    }

    private sealed class TestApReportTenantScope(long businessId) : IApReportTenantScope
    {
        public Task<long> GetBusinessIdAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(businessId);
        }
    }

    private static void SeedHistory(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Business" });
        db.Suppliers.Add(new Supplier { Id = 10, BusinessId = 1, Name = "Supplier" });
        db.ChartAccounts.AddRange(Account(101, "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit), Account(201, "Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit), Account(202, "Advance", ChartAccountTypes.Liability, NormalBalanceTypes.Credit));
        db.AccountingConfigurations.Add(new AccountingConfiguration { Id = 1, BusinessId = 1, SupplierPayableAccountId = 201, SupplierAdvanceAccountId = 202 });
        db.JournalEntries.AddRange(
            Journal(100, "GRN", new DateTime(2026, 3, 1), 1, 201, 0, 100),
            Journal(101, "Payment", new DateTime(2026, 3, 15), 1, 201, 40, 0),
            Journal(102, "AdvanceUtil", new DateTime(2026, 3, 20), 1, 201, 10, 0),
            Journal(103, "PaymentReversal", new DateTime(2026, 4, 15), 2, 201, 40, 0),
            Journal(104, "AdvanceUtilReversal", new DateTime(2026, 4, 20), 3, 201, 10, 0));
        db.SupplierPayableDocuments.Add(new SupplierPayableDocument { Id = 1, BusinessId = 1, SupplierId = 10, OriginalPayableAmount = 100, RecognitionDate = new DateTime(2026, 3, 1), DueDate = new DateTime(2026, 3, 31), SourceDocumentType = "GRN", SourceDocumentId = 1, SourceReference = "GRN-1", SourceJournalEntryId = 100, Status = "partial" });
        db.SupplierPaymentAllocations.Add(new SupplierPaymentAllocation { Id = 1, SupplierPaymentId = 1, SupplierPayableDocumentId = 1, PayableDocumentId = 1, AllocatedAmount = 40, Status = "reversed", EffectiveDate = new DateTime(2026, 3, 15), ReversedEffectiveDate = new DateTime(2026, 4, 15), JournalEntryId = 101, ReversalJournalEntryId = 103 });
        db.SupplierAdvanceUtilizations.AddRange(
            new SupplierAdvanceUtilization { Id = 1, BusinessId = 1, SupplierAdvanceId = 1, SupplierPayableDocumentId = 1, Amount = 10, EffectiveDate = new DateTime(2026, 3, 20), JournalEntryId = 102, Status = "reversed" },
            new SupplierAdvanceUtilization { Id = 2, BusinessId = 1, SupplierAdvanceId = 1, SupplierPayableDocumentId = 1, Amount = 10, EffectiveDate = new DateTime(2026, 4, 20), JournalEntryId = 104, Status = "reversal", ReversalOfUtilizationId = 1 });
        db.SaveChanges();
    }

    private static void SeedAging(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Business" });
        db.Suppliers.Add(new Supplier { Id = 10, BusinessId = 1, Name = "Supplier" });
        db.ChartAccounts.Add(Account(201, "Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit));
        db.AccountingConfigurations.Add(new AccountingConfiguration { Id = 1, BusinessId = 1, SupplierPayableAccountId = 201 });
        db.JournalEntries.AddRange(Enumerable.Range(1, 5).Select(id => Journal(id, "GRN", new DateTime(2026, 1, 1), 1, 201, 0, 0)));
        for (var i = 0; i < 5; i++)
            db.SupplierPayableDocuments.Add(new SupplierPayableDocument { Id = i + 1, BusinessId = 1, SupplierId = 10, OriginalPayableAmount = i == 4 ? 50 : 10 * (i + 1), RecognitionDate = new DateTime(2026, 1, 1), DueDate = new DateTime(2026, 6, 30).AddDays(i == 0 ? 0 : -(i == 1 ? 15 : i == 2 ? 45 : i == 3 ? 75 : 100)), SourceDocumentType = "GRN", SourceDocumentId = i + 1, SourceReference = $"GRN-{i + 1}", SourceJournalEntryId = 0, IsDisputed = i == 4 });
        db.SaveChanges();
    }

    private static void SeedAgingBoundaries(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Business" });
        db.Suppliers.Add(new Supplier { Id = 10, BusinessId = 1, Name = "Supplier" });
        db.ChartAccounts.Add(Account(201, "Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit));
        db.AccountingConfigurations.Add(new AccountingConfiguration { Id = 1, BusinessId = 1, SupplierPayableAccountId = 201 });
        var dueDates = new[] { 0, -1, -30, -31, -60, -61, -90, -91 };
        db.JournalEntries.AddRange(Enumerable.Range(1, dueDates.Length).Select(id => Journal(id, "GRN", new DateTime(2026, 1, 1), 1, 201, 0, 10)));
        for (var i = 0; i < dueDates.Length; i++)
            db.SupplierPayableDocuments.Add(new SupplierPayableDocument { Id = i + 1, BusinessId = 1, SupplierId = 10, OriginalPayableAmount = i == 7 ? 20 : 10, RecognitionDate = new DateTime(2026, 1, 1), DueDate = new DateTime(2026, 6, 30).AddDays(dueDates[i]), SourceDocumentType = "GRN", SourceDocumentId = i + 1, SourceReference = $"BOUNDARY-{i + 1}", SourceJournalEntryId = i + 1 });
        db.SaveChanges();
    }

    private static void SeedAdvanceReconciliation(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Business" });
        db.Suppliers.Add(new Supplier { Id = 10, BusinessId = 1, Name = "Supplier" });
        db.ChartAccounts.Add(Account(202, "Advance", ChartAccountTypes.Liability, NormalBalanceTypes.Credit));
        db.AccountingConfigurations.Add(new AccountingConfiguration { Id = 1, BusinessId = 1, SupplierAdvanceAccountId = 202 });
        db.JournalEntries.AddRange(
            Journal(500, "Advance", new DateTime(2026, 3, 1), 1, 202, 0, 100),
            Journal(501, "Advance", new DateTime(2026, 3, 1), 1, 202, 0, 50),
            Journal(502, "Advance", new DateTime(2026, 3, 1), 1, 202, 0, 40),
            Journal(503, "Utilisation", new DateTime(2026, 3, 15), 1, 202, 30, 0),
            Journal(504, "UtilisationReversal", new DateTime(2026, 4, 15), 1, 202, 0, 30),
            Journal(505, "AdvanceReversal", new DateTime(2026, 4, 20), 502, 202, 40, 0));
        db.SupplierAdvances.AddRange(
            new SupplierAdvance { Id = 1, BusinessId = 1, SupplierId = 10, Amount = 100, RemainingAmount = 70, AppliedAmount = 30, Status = "posted", PostingDate = new DateTime(2026, 3, 1), JournalEntryId = 500 },
            new SupplierAdvance { Id = 2, BusinessId = 1, SupplierId = 10, Amount = 50, RemainingAmount = 50, AppliedAmount = 0, Status = "posted", PostingDate = new DateTime(2026, 3, 1), JournalEntryId = 501 },
            new SupplierAdvance { Id = 3, BusinessId = 1, SupplierId = 10, Amount = 40, RemainingAmount = 0, AppliedAmount = 0, Status = "reversed", PostingDate = new DateTime(2026, 3, 1), JournalEntryId = 502 });
        db.SupplierAdvanceUtilizations.AddRange(
            new SupplierAdvanceUtilization { Id = 1, BusinessId = 1, SupplierAdvanceId = 1, SupplierPayableDocumentId = 1, Amount = 30, EffectiveDate = new DateTime(2026, 3, 15), JournalEntryId = 503, Status = "reversed" },
            new SupplierAdvanceUtilization { Id = 2, BusinessId = 1, SupplierAdvanceId = 1, SupplierPayableDocumentId = 1, Amount = 30, EffectiveDate = new DateTime(2026, 4, 15), JournalEntryId = 504, Status = "reversal", ReversalOfUtilizationId = 1 });
        db.SaveChanges();
    }

    private static JournalEntry Journal(long id, string reference, DateTime date, long reversalOf, long accountId, decimal debit, decimal credit) => new() { Id = id, BusinessId = 1, JournalNumber = $"J-{id}", JournalType = "system", Status = JournalEntryStatuses.Posted, TransactionDate = date, PostingDate = date, ReferenceType = reference, ReferenceId = id.ToString(), ReversalOfJournalEntryId = reversalOf == 1 ? null : reversalOf, Lines = [new JournalEntryLine { Id = id * 10, AccountId = accountId, DebitAmount = debit, CreditAmount = credit }] };
    private static ChartAccount Account(long id, string name, string type, string normal) => new() { Id = id, BusinessId = 1, AccountCode = id.ToString(), AccountName = name, AccountType = type, NormalBalanceType = normal, IsActive = true };
}