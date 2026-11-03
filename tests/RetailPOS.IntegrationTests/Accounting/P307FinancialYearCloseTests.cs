using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using RetailPOS.Infrastructure.FinancialYearCloseRuns;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Application.FinancialYearCloseRuns.Abstractions;

namespace RetailPOS.Tests.Accounting;

public sealed class P307FinancialYearCloseTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly Mock<IAccountingFinancialYearCloseJournalPosting> _journalPosting = new();
    private readonly Mock<IAuditService> _audit = new();

    public P307FinancialYearCloseTests()
    {
        _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options); _audit.Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask); Seed();
    }

    [Fact]
    public async Task ApprovedFinalPeriodRun_ClosesYearWithDirectTemporaryAndSeparateDrawingsEntries_AndRetries()
    {
        FinancialYearCloseJournalDraft? closing = null;
        _journalPosting.Setup(x => x.CreateOrGetSystemDraftAsync(
                It.IsAny<long>(),
                It.IsAny<FinancialYearCloseJournalDraft>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<long, FinancialYearCloseJournalDraft, long, string, CancellationToken>(
                (_, draft, _, _, _) => closing = draft)
            .ReturnsAsync(900);
        _journalPosting.Setup(x => x.PostApprovedYearEndClosingAsync(
                It.IsAny<long>(),
                900,
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(900);
        var prepared = await PrepareRunAsync(1, 10);
        await ApproveRunAsync(prepared, 11);
        var approved = await _db.AccountingFinancialYearCloseRuns.SingleAsync();
        await ExecuteRunAsync(approved.Id, 12);
        var executed = await _db.AccountingFinancialYearCloseRuns.SingleAsync();
        Assert.Equal(11, approved.ApprovedBy);
        Assert.Equal(AccountingFinancialYearCloseRunStatuses.Executed, executed.Status); Assert.True((await _db.FinancialYears.FindAsync(1L))!.IsClosed); Assert.Equal(AccountingPeriodStatuses.Closed, (await _db.AccountingPeriods.FindAsync(10L))!.Status);
        Assert.NotNull(closing); Assert.Equal(AccountingJournalTypes.YearEndClosing, closing!.JournalType); Assert.Equal(AccountingReferenceTypes.FinancialYearClose, closing.ReferenceType); Assert.Equal("1", closing.ReferenceId); Assert.Equal(0m, closing.Lines.Sum(x => x.DebitAmount) - closing.Lines.Sum(x => x.CreditAmount));
        Assert.Contains(closing.Lines, x => x.AccountId == 20 && x.DebitAmount == 100m); Assert.Contains(closing.Lines, x => x.AccountId == 21 && x.CreditAmount == 40m); Assert.Contains(closing.Lines, x => x.AccountId == 3 && x.CreditAmount == 60m); Assert.Contains(closing.Lines, x => x.AccountId == 5 && x.CreditAmount == 20m); Assert.Contains(closing.Lines, x => x.AccountId == 4 && x.DebitAmount == 20m);
        await ExecuteRunAsync(executed.Id, 12);
        _journalPosting.Verify(x => x.PostApprovedYearEndClosingAsync(
            1,
            900,
            executed.Id,
            12,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FinancialYearClose_RequiresApprovedLockedFinalPeriodRun()
    {
        var finalRun = await _db.AccountingPeriodCloseRuns.SingleAsync(); finalRun.Status = AccountingPeriodCloseRunStatuses.Prepared; await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PrepareRunAsync(1, 10));
    }

    [Fact]
    public async Task FinancialYearCloseRunReadStore_PreservesProjectionFilterAndBusinessScope()
    {
        var created = await PrepareRunAsync(1, 10);
        var run = await _db.AccountingFinancialYearCloseRuns.SingleAsync();
        run.YearEndJournalEntryId = 900;
        var journal = await _db.JournalEntries.SingleAsync(entry => entry.Id == 900);
        journal.JournalNumber = "YE-001";
        await _db.SaveChangesAsync();

        var store = new AccountingFinancialYearCloseRunReadStore(_db);
        var fetched = await store.GetAsync(1, created, CancellationToken.None);
        var listed = await store.ListAsync(1, 1, CancellationToken.None);
        var filteredOut = await store.ListAsync(1, 999, CancellationToken.None);

        Assert.Equal(created, fetched.Id);
        Assert.Equal(1, fetched.FinancialYearId);
        Assert.Equal(10, fetched.FinalAccountingPeriodId);
        Assert.Equal(100, fetched.FinalPeriodCloseRunId);
        Assert.Equal("YE-001", fetched.YearEndJournalNumber);
        Assert.Equal(created, Assert.Single(listed).Id);
        Assert.Empty(filteredOut);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            store.GetAsync(2, created, CancellationToken.None));
    }

    [Fact]
    public async Task FinancialYearCloseReopen_ReversesPostedJournalAndPersistsTrimmedReason()
    {
        _journalPosting.Setup(x => x.CreateOrGetSystemDraftAsync(
                It.IsAny<long>(),
                It.IsAny<FinancialYearCloseJournalDraft>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(900);
        _journalPosting.Setup(x => x.PostApprovedYearEndClosingAsync(
                It.IsAny<long>(),
                900,
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(900);
        _journalPosting.Setup(x => x.ReverseYearEndJournalAsync(
                1,
                900,
                "period correction",
                new DateTime(2027, 1, 15),
                14,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(901);
        var runId = await PrepareRunAsync(1, 10);
        await ApproveRunAsync(runId, 11);
        await ExecuteRunAsync(runId, 12);
        var original = await _db.JournalEntries.SingleAsync(value => value.Id == 900);
        original.Status = JournalEntryStatuses.Posted;
        await _db.SaveChangesAsync();

        await ReopenRunAsync(
            runId,
            "  period correction  ",
            new DateTime(2027, 1, 15, 10, 30, 0),
            14);

        var run = await _db.AccountingFinancialYearCloseRuns.SingleAsync(
            value => value.Id == runId);
        Assert.Equal(AccountingFinancialYearCloseRunStatuses.Reopened, run.Status);
        Assert.Equal("period correction", run.ReopenReason);
        Assert.Equal(901, run.ReversalJournalEntryId);
        Assert.Equal(14, run.ReopenedBy);
        var year = await _db.FinancialYears.SingleAsync(value => value.Id == 1);
        Assert.False(year.IsClosed);
        Assert.Null(year.ClosedAt);
        _journalPosting.Verify(posting => posting.ReverseYearEndJournalAsync(
            1,
            900,
            "period correction",
            new DateTime(2027, 1, 15),
            14,
            It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(audit => audit.RecordAsync(
            It.Is<AuditEventInput>(input =>
                input.Summary == "Financial year close reopened with year-end reversal"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private async Task<long> PrepareRunAsync(long financialYearId, long actorUserId)
    {
        var store = new AccountingFinancialYearCloseRunCommandStore(_db, _audit.Object, _journalPosting.Object);
        var runId = await store.PrepareAsync(1, financialYearId, actorUserId, CancellationToken.None);
        return runId;
    }
    private Task ApproveRunAsync(long runId, long actorUserId)
    {
        var store = new AccountingFinancialYearCloseRunCommandStore(_db, _audit.Object, _journalPosting.Object);
        return store.ApproveAsync(1, runId, actorUserId, CancellationToken.None);
    }
    private Task ExecuteRunAsync(long runId, long actorUserId)
    {
        var store = new AccountingFinancialYearCloseRunCommandStore(_db, _audit.Object, _journalPosting.Object);
        return store.ExecuteAsync(1, runId, actorUserId, CancellationToken.None);
    }
    private Task ReopenRunAsync(
        long runId,
        string? reason,
        DateTime postingDate,
        long actorUserId)
    {
        var store = new AccountingFinancialYearCloseRunCommandStore(
            _db,
            _audit.Object,
            _journalPosting.Object);
        return store.ReopenAsync(
            1,
            runId,
            reason,
            postingDate,
            actorUserId,
            CancellationToken.None);
    }
    private void Seed()
    {
        var date = new DateTime(2026, 12, 31); _db.Businesses.Add(new Business { Id = 1, Name = "Test", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        _db.FinancialYears.Add(new FinancialYear { Id = 1, BusinessId = 1, YearCode = "FY26", Name = "FY26", StartDate = new DateTime(2026, 1, 1), EndDate = date, IsCurrent = true });
        _db.AccountingPeriods.Add(new AccountingPeriod { Id = 10, FinancialYearId = 1, PeriodNumber = 12, Name = "December", StartDate = new DateTime(2026, 12, 1), EndDate = date, Status = AccountingPeriodStatuses.Locked });
        _db.ChartAccounts.AddRange(new ChartAccount { Id = 3, BusinessId = 1, AccountCode = "3200", AccountName = "Retained", AccountType = ChartAccountTypes.Equity, NormalBalanceType = NormalBalanceTypes.Credit }, new ChartAccount { Id = 4, BusinessId = 1, AccountCode = "3000", AccountName = "Capital", AccountType = ChartAccountTypes.Equity, NormalBalanceType = NormalBalanceTypes.Credit }, new ChartAccount { Id = 5, BusinessId = 1, AccountCode = "3100", AccountName = "Drawings", AccountType = ChartAccountTypes.Equity, NormalBalanceType = NormalBalanceTypes.Debit }, new ChartAccount { Id = 20, BusinessId = 1, AccountCode = "4000", AccountName = "Sales", AccountType = ChartAccountTypes.Income, NormalBalanceType = NormalBalanceTypes.Credit }, new ChartAccount { Id = 21, BusinessId = 1, AccountCode = "5000", AccountName = "COGS", AccountType = ChartAccountTypes.CostOfGoodsSold, NormalBalanceType = NormalBalanceTypes.Debit });
        _db.AccountingConfigurations.Add(new AccountingConfiguration { BusinessId = 1, RetainedEarningsAccountId = 3, OwnerCapitalAccountId = 4, OwnerDrawingsAccountId = 5 });
        _db.AccountingPeriodCloseRuns.Add(new AccountingPeriodCloseRun { Id = 100, BusinessId = 1, AccountingPeriodId = 10, RunNumber = 1, Status = AccountingPeriodCloseRunStatuses.Approved, EvaluationSequence = 1, PreparedBy = 1, CreatedBy = 1 });
        _db.JournalEntries.AddRange(new JournalEntry { Id = 1000, BusinessId = 1, FinancialYearId = 1, AccountingPeriodId = 10, JournalType = AccountingJournalTypes.SystemJournal, ReferenceType = "sale", ReferenceId = "1", TransactionDate = date, PostingDate = date, Status = JournalEntryStatuses.Posted, Lines = new List<JournalEntryLine> { new() { AccountId = 3, DebitAmount = 100m }, new() { AccountId = 20, CreditAmount = 100m } } }, new JournalEntry { Id = 1001, BusinessId = 1, FinancialYearId = 1, AccountingPeriodId = 10, JournalType = AccountingJournalTypes.SystemJournal, ReferenceType = "cogs", ReferenceId = "2", TransactionDate = date, PostingDate = date, Status = JournalEntryStatuses.Posted, Lines = new List<JournalEntryLine> { new() { AccountId = 21, DebitAmount = 40m }, new() { AccountId = 5, DebitAmount = 20m }, new() { AccountId = 3, CreditAmount = 60m } } }, new JournalEntry { Id = 900, BusinessId = 1, JournalType = AccountingJournalTypes.YearEndClosing, ReferenceType = AccountingReferenceTypes.FinancialYearClose, ReferenceId = "1", TransactionDate = date, PostingDate = date, Status = JournalEntryStatuses.Draft, IsSystemGenerated = true });
        _db.SaveChanges();
    }
    public void Dispose() => _db.Dispose();
}