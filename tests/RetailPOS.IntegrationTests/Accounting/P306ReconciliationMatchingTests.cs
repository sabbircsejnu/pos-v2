using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.BankReconciliation;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Accounting;

public sealed class P306ReconciliationMatchingTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly BankReconciliationSessionCommandStore _sessionStore;
    private readonly BankReconciliationReadStore _readStore;
    private readonly BankReconciliationMatchCommandStore _matchStore;

    public P306ReconciliationMatchingTests()
    {
        _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        _db.Businesses.AddRange(new Business { Id = 1, Name = "One" }, new Business { Id = 2, Name = "Two" });
        _db.ChartAccounts.AddRange(Account(101, 1), Account(201, 2));
        _db.BankAccounts.AddRange(new BankAccount { Id = 501, BusinessId = 1, ChartAccountId = 101, DisplayName = "Bank One", ReconciliationStartDate = new DateTime(2026, 9, 1) }, new BankAccount { Id = 502, BusinessId = 2, ChartAccountId = 201, DisplayName = "Bank Two" });
        _db.BankStatementImports.Add(new BankStatementImport { Id = 100, BusinessId = 1, BankAccountId = 501, SourceFileName = "s.csv", SourceFileChecksum = "s", Status = BankStatementImportStatuses.Imported, OpeningBalance = 100, ClosingBalance = 125, ImportedAt = DateTime.UtcNow });
        _db.BankStatementLines.AddRange(Line(1001, 100, 10, "A"), Line(1002, 100, 20, "B"), Line(1003, 100, -5, "C"));
        _db.JournalEntries.AddRange(new JournalEntry { Id = 199, BusinessId = 1, JournalNumber = "JR-OPEN", JournalType = "Opening", ReferenceType = "Opening", ReferenceId = "0", Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 8, 31), TransactionDate = new DateTime(2026, 8, 31), BusinessDate = new DateTime(2026, 8, 31), Lines = [new JournalEntryLine { Id = 1991, AccountId = 101, DebitAmount = 100, CreditAmount = 0, Description = "Opening" }] }, new JournalEntry { Id = 200, BusinessId = 1, JournalNumber = "JR-1", JournalType = "Sale", ReferenceType = "Sale", ReferenceId = "1", Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 9, 10), TransactionDate = new DateTime(2026, 9, 10), BusinessDate = new DateTime(2026, 9, 10), Lines = [new JournalEntryLine { Id = 2001, AccountId = 101, DebitAmount = 10, CreditAmount = 0, Description = "A" }, new JournalEntryLine { Id = 2002, AccountId = 101, DebitAmount = 15, CreditAmount = 0, Description = "B1" }, new JournalEntryLine { Id = 2003, AccountId = 101, DebitAmount = 0, CreditAmount = 5, Description = "C" }, new JournalEntryLine { Id = 2004, AccountId = 101, DebitAmount = 5, CreditAmount = 0, Description = "B2" }] });
        _db.SaveChanges();
        _sessionStore = new BankReconciliationSessionCommandStore(_db);
        _readStore = new BankReconciliationReadStore(_db);
        _matchStore = new BankReconciliationMatchCommandStore(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SessionEnforcesStartDateAndOverlappingActivePeriods()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSession(new CreateBankReconciliationSessionDto { BankAccountId = 501, PeriodStart = new DateTime(2026, 9, 30), PeriodEnd = new DateTime(2026, 9, 1) }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSession(new CreateBankReconciliationSessionDto { BankAccountId = 501, PeriodStart = new DateTime(2026, 8, 1), PeriodEnd = new DateTime(2026, 8, 31) }));
        var session = await CreateSession(new CreateBankReconciliationSessionDto { BankAccountId = 501, PeriodStart = new DateTime(2026, 9, 1), PeriodEnd = new DateTime(2026, 9, 30) });
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSession(new CreateBankReconciliationSessionDto { BankAccountId = 501, PeriodStart = new DateTime(2026, 9, 15), PeriodEnd = new DateTime(2026, 10, 15) }));
        Assert.Equal(BankReconciliationSessionStatuses.Open, session.Status);
        Assert.Equal(100m, session.StatementOpeningBalance);
        Assert.Equal(125m, session.StatementClosingBalance);
        Assert.Equal(100m, session.GlOpeningBalance);
    }

    [Fact]
    public async Task CandidatesUsePostedBankActivityAndExactSignConvention()
    {
        var session = await CreateSession(new CreateBankReconciliationSessionDto { BankAccountId = 501, PeriodStart = new DateTime(2026, 9, 1), PeriodEnd = new DateTime(2026, 9, 30) });
        var statements = await _readStore.FindStatementCandidatesAsync(1, session.Id, CancellationToken.None);
        var gl = await _readStore.FindGlCandidatesAsync(1, session.Id, CancellationToken.None);
        Assert.Contains(statements, x => x.SignedAmount == 10); Assert.Contains(gl, x => x.SignedAmount == 10); Assert.Contains(gl, x => x.JournalNumber == "JR-1");
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _readStore.FindStatementCandidatesAsync(2, session.Id, CancellationToken.None));
        await MatchAsync(new CreateBankReconciliationMatchDto
        {
            SessionId = session.Id,
            StatementLineIds = [1001],
            JournalEntryLineIds = [2001]
        }, 9);
        Assert.DoesNotContain(
            await _readStore.FindStatementCandidatesAsync(1, session.Id, CancellationToken.None),
            candidate => candidate.BankStatementLineId == 1001);
        Assert.DoesNotContain(
            await _readStore.FindGlCandidatesAsync(1, session.Id, CancellationToken.None),
            candidate => candidate.JournalEntryLineId == 2001);
    }

    [Fact]
    public async Task SupportsOneToOneOneToManyAndManyToOneWithExactZeroTolerance()
    {
        var session = await CreateSession(new CreateBankReconciliationSessionDto { BankAccountId = 501, PeriodStart = new DateTime(2026, 9, 1), PeriodEnd = new DateTime(2026, 9, 30) });
        var one = await MatchAsync(new CreateBankReconciliationMatchDto { SessionId = session.Id, StatementLineIds = [1001], JournalEntryLineIds = [2001] }, 9);
        var oneMany = await MatchAsync(new CreateBankReconciliationMatchDto { SessionId = session.Id, StatementLineIds = [1002], JournalEntryLineIds = [2002, 2004] }, 9);
        var manyOne = await MatchAsync(new CreateBankReconciliationMatchDto { SessionId = session.Id, StatementLineIds = [1003], JournalEntryLineIds = [2003] }, 9);
        Assert.Equal(one.StatementTotal, one.GlTotal); Assert.Equal(oneMany.StatementTotal, oneMany.GlTotal); Assert.Equal(manyOne.StatementTotal, manyOne.GlTotal);
    }

    [Fact]
    public async Task RejectsAlreadyMatchedAndUnequalTotals_AndUnmatchPreservesHistory()
    {
        var session = await CreateSession(new CreateBankReconciliationSessionDto { BankAccountId = 501, PeriodStart = new DateTime(2026, 9, 1), PeriodEnd = new DateTime(2026, 9, 30) });
        var match = await MatchAsync(new CreateBankReconciliationMatchDto { SessionId = session.Id, StatementLineIds = [1001], JournalEntryLineIds = [2001] }, 9);
        await Assert.ThrowsAsync<InvalidOperationException>(() => MatchAsync(new CreateBankReconciliationMatchDto { SessionId = session.Id, StatementLineIds = [1001], JournalEntryLineIds = [2002] }, 9));
        await Assert.ThrowsAsync<InvalidOperationException>(() => MatchAsync(new CreateBankReconciliationMatchDto { SessionId = session.Id, StatementLineIds = [1002], JournalEntryLineIds = [2001] }, 9));
        var unmatched = await _matchStore.UnmatchAsync(
            1,
            9,
            new UnmatchBankReconciliationInput(match.Id, "review"),
            CancellationToken.None);
        var retry = await _matchStore.UnmatchAsync(
            1,
            9,
            new UnmatchBankReconciliationInput(match.Id, "retry"),
            CancellationToken.None);
        Assert.Equal(BankReconciliationMatchStatuses.Unmatched, unmatched.Status); Assert.Equal(unmatched.Id, retry.Id); Assert.Equal(2, await _db.BankReconciliationMatchLines.CountAsync()); Assert.All(await _db.BankReconciliationMatchLines.ToListAsync(), x => Assert.False(x.IsActive));
    }

    [Fact]
    public async Task CalculationIsIndependentAndShowsMatchedAndUnmatchedAmounts()
    {
        var session = await CreateSession(new CreateBankReconciliationSessionDto { BankAccountId = 501, PeriodStart = new DateTime(2026, 9, 1), PeriodEnd = new DateTime(2026, 9, 30) });
        await MatchAsync(new CreateBankReconciliationMatchDto { SessionId = session.Id, StatementLineIds = [1001], JournalEntryLineIds = [2001] }, 9);
        var calculation = await _readStore.GetCalculationAsync(
            1,
            session.Id,
            CancellationToken.None);
        Assert.Equal(25, calculation.StatementMovement); Assert.Equal(25, calculation.GlMovement); Assert.Equal(10, calculation.MatchedStatementAmount); Assert.Equal(15, calculation.UnmatchedStatementAmount); Assert.Equal(15, calculation.UnmatchedGlAmount); Assert.Equal(0, calculation.ReconciliationDifference);
        Assert.Equal(1, calculation.MatchedStatementCount);
        Assert.Equal(1, calculation.MatchedGlCount);
        Assert.Equal(2, calculation.UnmatchedStatementCount);
        Assert.Equal(3, calculation.UnmatchedGlCount);
    }

    private Task<BankReconciliationSessionReadModel> CreateSession(
        CreateBankReconciliationSessionDto request) =>
        _sessionStore.CreateAsync(
            1,
            9,
            new CreateBankReconciliationSessionInput(
                request.BankAccountId,
                request.PeriodStart,
                request.PeriodEnd),
            CancellationToken.None);

    private Task<ReconciliationMatchReadModel> MatchAsync(
        CreateBankReconciliationMatchDto request,
        long actorUserId) =>
        _matchStore.CreateAsync(
            1,
            actorUserId,
            new CreateBankReconciliationMatchInput(
                request.SessionId,
                request.StatementLineIds,
                request.JournalEntryLineIds),
            CancellationToken.None);

    private static BankStatementLine Line(long id, long importId, decimal signed, string description) => new() { Id = id, BusinessId = 1, BankAccountId = 501, BankStatementImportId = importId, SourceRowNumber = (int)(id - 1000), TransactionDate = new DateTime(2026, 9, 10), Description = description, DebitAmount = signed < 0 ? Math.Abs(signed) : 0, CreditAmount = signed > 0 ? signed : 0, SignedAmount = signed };
    private static ChartAccount Account(long id, long businessId) => new() { Id = id, BusinessId = businessId, AccountCode = id.ToString(), AccountName = $"Bank {id}", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true };
}
