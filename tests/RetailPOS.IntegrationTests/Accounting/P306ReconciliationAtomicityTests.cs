using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.BankReconciliation;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Accounting;

public sealed class P306ReconciliationAtomicityTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FailMatchPersistenceInterceptor _interceptor = new();

    public P306ReconciliationAtomicityTests()
    {
        _connection.Open(); using var command = _connection.CreateCommand(); command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA ignore_check_constraints = 1;"; command.ExecuteNonQuery();
        using var db = CreateContext(); db.Database.EnsureCreated(); Seed(db);
    }

    [Fact]
    public async Task MatchFailureRollsBackGroupAndLinks_ThenRetryCreatesOneEffectiveMatch()
    {
        _interceptor.FailMatchLines = true;
        await using (var failed = CreateContext()) await Assert.ThrowsAsync<InvalidOperationException>(() => MatchStore(failed).CreateAsync(1, 9, new CreateBankReconciliationMatchInput(700, [1001], [2001]), CancellationToken.None));
        await using (var verify = CreateContext()) { Assert.Empty(await verify.BankReconciliationMatches.ToListAsync()); Assert.Empty(await verify.BankReconciliationMatchLines.ToListAsync()); }
        _interceptor.FailMatchLines = false;
        await using (var retry = CreateContext()) await MatchStore(retry).CreateAsync(1, 9, new CreateBankReconciliationMatchInput(700, [1001], [2001]), CancellationToken.None);
        await using var final = CreateContext(); Assert.Single(await final.BankReconciliationMatches.Where(x => x.Status == BankReconciliationMatchStatuses.Effective).ToListAsync()); Assert.Equal(2, await final.BankReconciliationMatchLines.CountAsync());
    }

    [Fact]
    public async Task UnmatchFailureLeavesEffectiveMatch_ThenRetryUnmatchesOnce()
    {
        await using (var setup = CreateContext()) await MatchStore(setup).CreateAsync(1, 9, new CreateBankReconciliationMatchInput(700, [1001], [2001]), CancellationToken.None);
        _interceptor.FailUnmatch = true;
        await using (var failed = CreateContext()) await Assert.ThrowsAsync<InvalidOperationException>(() => MatchStore(failed).UnmatchAsync(1, 9, new UnmatchBankReconciliationInput(1, "fail"), CancellationToken.None));
        await using (var verify = CreateContext()) Assert.Equal(BankReconciliationMatchStatuses.Effective, (await verify.BankReconciliationMatches.SingleAsync()).Status);
        _interceptor.FailUnmatch = false;
        await using (var retry = CreateContext()) await MatchStore(retry).UnmatchAsync(1, 9, new UnmatchBankReconciliationInput(1, "retry"), CancellationToken.None);
        await using var final = CreateContext(); Assert.Equal(BankReconciliationMatchStatuses.Unmatched, (await final.BankReconciliationMatches.SingleAsync()).Status); Assert.All(await final.BankReconciliationMatchLines.ToListAsync(), x => Assert.False(x.IsActive));
    }

    [Fact]
    public async Task CloseFailureRollsBackReadinessTransition_ThenRetryAndDuplicateCloseRemainExactlyOnce()
    {
        await using (var setup = CreateContext())
        {
            await MatchStore(setup).CreateAsync(
                1,
                9,
                new CreateBankReconciliationMatchInput(700, [1001], [2001]),
                CancellationToken.None);
            await CloseStore(setup).PrepareForCloseAsync(1, 9, 700, CancellationToken.None);
        }

        _interceptor.FailClose = true;
        await using (var failed = CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CloseStore(failed).CloseAsync(
                    1,
                    9,
                    700,
                    "Finance approved",
                    CancellationToken.None));
        await using (var verify = CreateContext())
            Assert.Equal(
                BankReconciliationSessionStatuses.ReadyForClose,
                (await verify.BankReconciliationSessions.SingleAsync()).Status);

        _interceptor.FailClose = false;
        await using (var retry = CreateContext())
            await CloseStore(retry).CloseAsync(
                1,
                9,
                700,
                "Finance approved",
                CancellationToken.None);
        await using (var duplicate = CreateContext())
            await CloseStore(duplicate).CloseAsync(
                1,
                10,
                700,
                "different retry",
                CancellationToken.None);

        await using var final = CreateContext();
        var session = await final.BankReconciliationSessions.SingleAsync();
        Assert.Equal(BankReconciliationSessionStatuses.Closed, session.Status);
        Assert.Equal(9, session.ClosedBy);
        Assert.Equal("Finance approved", session.CloseResultReference);
        Assert.Equal(0m, session.ClosedDifference);
    }

    [Fact]
    public async Task ReopenFailureLeavesClosedSession_ThenRetryAndDuplicateRemainExactlyOnce()
    {
        await using (var setup = CreateContext())
        {
            await MatchStore(setup).CreateAsync(
                1,
                9,
                new CreateBankReconciliationMatchInput(700, [1001], [2001]),
                CancellationToken.None);
            var closeStore = CloseStore(setup);
            await closeStore.PrepareForCloseAsync(1, 9, 700, CancellationToken.None);
            await closeStore.CloseAsync(
                1,
                9,
                700,
                "Finance approved",
                CancellationToken.None);
        }

        _interceptor.FailReopen = true;
        await using (var failed = CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => CloseStore(failed).ReopenAsync(
                    1,
                    9,
                    700,
                    "correction",
                    CancellationToken.None));
        await using (var verify = CreateContext())
        {
            var session = await verify.BankReconciliationSessions.SingleAsync();
            Assert.Equal(BankReconciliationSessionStatuses.Closed, session.Status);
            Assert.Null(session.ReopenReason);
        }

        _interceptor.FailReopen = false;
        await using (var retry = CreateContext())
            await CloseStore(retry).ReopenAsync(
                1,
                9,
                700,
                "correction",
                CancellationToken.None);
        await using (var duplicate = CreateContext())
            await CloseStore(duplicate).ReopenAsync(
                1,
                10,
                700,
                "different retry",
                CancellationToken.None);

        await using var final = CreateContext();
        var reopened = await final.BankReconciliationSessions.SingleAsync();
        Assert.Equal(BankReconciliationSessionStatuses.InProgress, reopened.Status);
        Assert.Equal(9, reopened.ReopenedBy);
        Assert.Equal("correction", reopened.ReopenReason);
    }

    private static BankReconciliationMatchCommandStore MatchStore(RetailPOSDbContext db) => new(db);
    private static BankReconciliationCloseCommandStore CloseStore(RetailPOSDbContext db) =>
        new(db, new BankReconciliationReadStore(db));
    private RetailPOSDbContext CreateContext() => new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(_connection).AddInterceptors(_interceptor).Options);
    private static void Seed(RetailPOSDbContext db) { db.Businesses.Add(new Business { Id = 1, Name = "B" }); db.ChartAccounts.Add(new ChartAccount { Id = 101, BusinessId = 1, AccountCode = "101", AccountName = "Bank", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true }); db.BankAccounts.Add(new BankAccount { Id = 501, BusinessId = 1, ChartAccountId = 101, DisplayName = "Bank" }); db.BankReconciliationSessions.Add(new BankReconciliationSession { Id = 700, BusinessId = 1, BankAccountId = 501, PeriodStart = new DateTime(2026, 9, 1), PeriodEnd = new DateTime(2026, 9, 30), StatementOpeningBalance = 0, StatementClosingBalance = 10, GlOpeningBalance = 0, Status = BankReconciliationSessionStatuses.Open }); db.BankStatementLines.Add(new BankStatementLine { Id = 1001, BusinessId = 1, BankAccountId = 501, BankStatementImportId = 1, SourceRowNumber = 1, TransactionDate = new DateTime(2026, 9, 10), Description = "A", SignedAmount = 10 }); db.BankStatementImports.Add(new BankStatementImport { Id = 1, BusinessId = 1, BankAccountId = 501, SourceFileName = "a", SourceFileChecksum = "a", Status = BankStatementImportStatuses.Imported }); db.JournalEntries.Add(new JournalEntry { Id = 200, BusinessId = 1, Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 9, 10), TransactionDate = new DateTime(2026, 9, 10), Lines = [new JournalEntryLine { Id = 2001, AccountId = 101, DebitAmount = 10 }] }); db.SaveChanges(); }
    public void Dispose() => _connection.Dispose();
    private sealed class FailMatchPersistenceInterceptor : SaveChangesInterceptor { public bool FailMatchLines { get; set; } public bool FailUnmatch { get; set; } public bool FailClose { get; set; } public bool FailReopen { get; set; } public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) { var context = eventData.Context; if (context != null && ((FailMatchLines && context.ChangeTracker.Entries<BankReconciliationMatchLine>().Any(x => x.State == EntityState.Added)) || (FailUnmatch && context.ChangeTracker.Entries<BankReconciliationMatchLine>().Any(x => x.State == EntityState.Modified)) || (FailClose && context.ChangeTracker.Entries<BankReconciliationSession>().Any(x => x.State == EntityState.Modified && x.Entity.Status == BankReconciliationSessionStatuses.Closed)) || (FailReopen && context.ChangeTracker.Entries<BankReconciliationSession>().Any(x => x.State == EntityState.Modified && x.Entity.ReopenedAt.HasValue)))) throw new InvalidOperationException("Injected reconciliation persistence failure."); return result; } public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => new(SavingChanges(eventData, result)); }
}
