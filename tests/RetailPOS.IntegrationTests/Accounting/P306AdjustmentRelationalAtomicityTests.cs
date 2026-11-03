using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.BankReconciliation;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Accounting;

public sealed class P306AdjustmentRelationalAtomicityTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AdjustmentFailureInterceptor _interceptor = new();
    public P306AdjustmentRelationalAtomicityTests() { _connection.Open(); using var command = _connection.CreateCommand(); command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA ignore_check_constraints = 1;"; command.ExecuteNonQuery(); using var db = CreateContext(); db.Database.EnsureCreated(); Seed(db); }

    [Fact]
    public async Task ApprovalFailureAfterJournalPersistenceRollsBackAndRetryIsExactlyOnce()
    {
        _interceptor.FailApprovalLinkage = true;
        await using (var failed = CreateContext()) { await Assert.ThrowsAsync<InvalidOperationException>(() => ApprovalStore(failed).ApproveAsync(1, 9, 1, new DateTime(2026, 9, 10), CancellationToken.None)); }
        await using (var verify = CreateContext()) { var row = await verify.BankStatementAdjustments.SingleAsync(); Assert.Equal(BankStatementAdjustmentStatuses.Proposed, row.Status); Assert.Null(row.JournalEntryId); Assert.Empty(await verify.JournalEntries.Where(x => x.ReferenceType == "BankStatementAdjustment").ToListAsync()); }
        _interceptor.FailApprovalLinkage = false;
        await using (var retry = CreateContext()) { await ApprovalStore(retry).ApproveAsync(1, 9, 1, new DateTime(2026, 9, 10), CancellationToken.None); }
        await using var final = CreateContext(); Assert.Single(await final.BankStatementAdjustments.Where(x => x.Status == BankStatementAdjustmentStatuses.Approved).ToListAsync()); Assert.Single(await final.JournalEntries.Where(x => x.ReferenceType == "BankStatementAdjustment" && x.Status == JournalEntryStatuses.Posted).ToListAsync()); Assert.Equal(2, await final.JournalEntryLines.CountAsync(x => x.JournalEntry.ReferenceType == "BankStatementAdjustment"));
    }

    [Fact]
    public async Task ReversalFailureAfterJournalPersistenceRollsBackAndRetryIsExactlyOnce()
    {
        await using (var setup = CreateContext()) await ApprovalStore(setup).ApproveAsync(1, 9, 1, new DateTime(2026, 9, 10), CancellationToken.None);
        _interceptor.FailReversalLinkage = true;
        await using (var failed = CreateContext()) { await Assert.ThrowsAsync<InvalidOperationException>(() => ReversalStore(failed).ReverseAsync(1, 9, 1, "reverse", new DateTime(2026, 10, 1), CancellationToken.None)); }
        await using (var verify = CreateContext()) { var row = await verify.BankStatementAdjustments.SingleAsync(); Assert.Equal(BankStatementAdjustmentStatuses.Approved, row.Status); Assert.Null(row.ReversalJournalEntryId); Assert.Empty(await verify.JournalEntries.Where(x => x.ReversalOfJournalEntryId == row.JournalEntryId).ToListAsync()); }
        _interceptor.FailReversalLinkage = false;
        await using (var retry = CreateContext()) await ReversalStore(retry).ReverseAsync(1, 9, 1, "retry", new DateTime(2026, 10, 1), CancellationToken.None);
        await using var final = CreateContext(); var reversedAdjustment = await final.BankStatementAdjustments.SingleAsync(); Assert.Equal(BankStatementAdjustmentStatuses.Reversed, reversedAdjustment.Status); Assert.Equal(9, reversedAdjustment.ReversedBy); var reversal = await final.JournalEntries.SingleAsync(x => x.ReversalOfJournalEntryId == reversedAdjustment.JournalEntryId); Assert.Equal(reversal.Id, reversedAdjustment.ReversalJournalEntryId); Assert.Equal(9, reversal.CreatedBy);
    }

    [Fact]
    public async Task ClosedPeriodApprovalAndReversalAreRejectedWithoutStateChange()
    {
        await using (var failed = CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => ApprovalStore(failed).ApproveAsync(1, 9, 1, new DateTime(2026, 11, 1), CancellationToken.None));
        }
        await using (var verify = CreateContext()) Assert.Equal(BankStatementAdjustmentStatuses.Proposed, (await verify.BankStatementAdjustments.SingleAsync()).Status);
        await using (var open = CreateContext()) await ApprovalStore(open).ApproveAsync(1, 9, 1, new DateTime(2026, 9, 10), CancellationToken.None);
        await using (var failedReverse = CreateContext()) await Assert.ThrowsAsync<InvalidOperationException>(() => ReversalStore(failedReverse).ReverseAsync(1, 9, 1, "closed", new DateTime(2026, 11, 1), CancellationToken.None));
        await using var final = CreateContext(); Assert.Equal(BankStatementAdjustmentStatuses.Approved, (await final.BankStatementAdjustments.SingleAsync()).Status);
    }

    private BankStatementAdjustmentApprovalStore ApprovalStore(RetailPOSDbContext db) { var tenant = Tenant(); var config = AccountingConfigurationServiceTestFactory.Create(db, tenant.Object,  NullLogger<AccountingConfigurationService>.Instance); return new BankStatementAdjustmentApprovalStore(db, new BankStatementAdjustmentJournalPoster(JournalSender(db), config)); }
    private BankStatementAdjustmentReversalStore ReversalStore(RetailPOSDbContext db) { var tenant = Tenant(); var config = AccountingConfigurationServiceTestFactory.Create(db, tenant.Object,  NullLogger<AccountingConfigurationService>.Instance); return new BankStatementAdjustmentReversalStore(db, new BankStatementAdjustmentJournalPoster(JournalSender(db), config)); }
    private static Mock<ITenantAccessService> Tenant() { var tenant = new Mock<ITenantAccessService>(); tenant.Setup(x => x.RequireBusinessId()).Returns(1); tenant.Setup(x => x.EffectiveBusinessId).Returns(1); tenant.Setup(x => x.IsSuperAdmin).Returns(false); return tenant; }
    private static MediatR.ISender JournalSender(RetailPOSDbContext db) { var numbers = new Mock<IJournalNumberAllocator>(); numbers.Setup(x => x.AllocateNextJournalNumberAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<DateTime>(), It.IsAny<long?>(), It.IsAny<CancellationToken>())).ReturnsAsync(() => Guid.NewGuid().ToString()); var audit = Mock.Of<IAuditService>(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()) == Task.CompletedTask); return SupplierPaymentCreationTestHarness.CreateJournalCommandSender(new ManualJournalDraftStore(db, audit, numbers.Object, NullLogger<ManualJournalDraftStore>.Instance)); }
    private RetailPOSDbContext CreateContext() => new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(_connection).AddInterceptors(_interceptor).Options);
    private static void Seed(RetailPOSDbContext db) { db.Businesses.Add(new Business { Id = 1, Name = "B" }); db.ChartAccounts.AddRange(Account(101, ChartAccountTypes.Asset), Account(601, ChartAccountTypes.Expense), Account(602, ChartAccountTypes.Income)); db.BankAccounts.Add(new BankAccount { Id = 501, BusinessId = 1, ChartAccountId = 101, DisplayName = "Bank" }); db.AccountingConfigurations.Add(new AccountingConfiguration { BusinessId = 1, CashAccountId = 101, DefaultBankAccountId = 101, BankFeeExpenseAccountId = 601, BankInterestIncomeAccountId = 602, JournalNumberPrefix = "JR", JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear }); db.FinancialYears.Add(new FinancialYear { Id = 1, BusinessId = 1, YearCode = "FY26", Name = "FY26", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true }); db.AccountingPeriods.AddRange(new AccountingPeriod { Id = 1, FinancialYearId = 1, PeriodNumber = 9, Name = "Sep", StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 9, 30), Status = AccountingPeriodStatuses.Open }, new AccountingPeriod { Id = 2, FinancialYearId = 1, PeriodNumber = 10, Name = "Oct", StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31), Status = AccountingPeriodStatuses.Open }, new AccountingPeriod { Id = 3, FinancialYearId = 1, PeriodNumber = 11, Name = "Nov", StartDate = new DateTime(2026, 11, 1), EndDate = new DateTime(2026, 11, 30), Status = AccountingPeriodStatuses.Closed, ClosedAt = DateTime.UtcNow }); db.BankStatementImports.Add(new BankStatementImport { Id = 100, BusinessId = 1, BankAccountId = 501, SourceFileName = "s", SourceFileChecksum = "s", Status = BankStatementImportStatuses.Imported }); db.BankStatementLines.Add(new BankStatementLine { Id = 1001, BusinessId = 1, BankAccountId = 501, BankStatementImportId = 100, SourceRowNumber = 1, TransactionDate = new DateTime(2026, 9, 10), Description = "fee", SignedAmount = -5, DebitAmount = 5 }); db.BankStatementAdjustments.Add(new BankStatementAdjustment { Id = 1, BusinessId = 1, BankAccountId = 501, BankStatementLineId = 1001, AdjustmentType = BankStatementAdjustmentTypes.BankFee, Amount = 2, IdempotencyKey = "a1", ProposedBy = 9, Reason = "fee" }); db.SaveChanges(); }
    private static ChartAccount Account(long id, string type) => new() { Id = id, BusinessId = 1, AccountCode = id.ToString(), AccountName = "A" + id, AccountType = type, NormalBalanceType = type == ChartAccountTypes.Income ? NormalBalanceTypes.Credit : NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true };
    public void Dispose() => _connection.Dispose();
    private sealed class AdjustmentFailureInterceptor : SaveChangesInterceptor { public bool FailApprovalLinkage { get; set; } public bool FailReversalLinkage { get; set; } public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) { var context = eventData.Context; if (context != null && ((FailApprovalLinkage && context.ChangeTracker.Entries<BankStatementAdjustment>().Any(x => x.Entity.Status == BankStatementAdjustmentStatuses.Approved && x.Entity.JournalEntryId.HasValue && x.State == EntityState.Modified)) || (FailReversalLinkage && context.ChangeTracker.Entries<BankStatementAdjustment>().Any(x => x.Entity.Status == BankStatementAdjustmentStatuses.Reversed && x.Entity.ReversalJournalEntryId.HasValue && x.State == EntityState.Modified)))) throw new InvalidOperationException("Injected adjustment linkage failure."); return result; } public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => new(SavingChanges(eventData, result)); }
}
