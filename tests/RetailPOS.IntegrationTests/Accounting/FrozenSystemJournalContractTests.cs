using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Accounting;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AuditEvents;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.IntegrationTests.Accounting;

public sealed class FrozenSystemJournalContractTests
{
    private static readonly DateTime Date = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public Task SameFrozenIdentityRepeatsSuccess_ChangedPayloadConflicts() => WithDatabase(async state =>
    {
        var request = Request();
        var first = await state.Store.PostAsync(request, 901, default);
        Assert.Equal(first, await state.Store.PostAsync(request, 902, default));
        Assert.Equal(first, await state.Store.ResolveExistingAsync(request, default));
        Assert.Null(await state.Store.ResolveExistingAsync(Request(reference: "foundation:missing"), default));
        await Assert.ThrowsAsync<FrozenJournalConflictException>(
            () => state.Store.ResolveExistingAsync(Request(11), default));
        await Assert.ThrowsAsync<FrozenJournalConflictException>(() => state.Store.PostAsync(Request(11), 901, default));
        Assert.Equal(1, await state.Db.JournalEntries.CountAsync());
        Assert.Equal(1, await state.Db.Set<FrozenSystemJournalOperation>().CountAsync());
        Assert.Equal(JournalEntryStatuses.Posted, (await state.Db.JournalEntries.SingleAsync()).Status);
    });

    [Fact]
    public Task SuccessfulReversalRetryResolvesCorrection_ChangedCorrectionConflicts() => WithDatabase(async state =>
    {
        var originalId = await state.Store.PostAsync(Request(), 901, default);
        var correction = FrozenSystemJournalRequest.ForReversal(1, "foundation:correction:1",
            originalId, new("Approved correction", Date.AddDays(1), Date.AddDays(1)));
        var correctedId = await state.Store.ReverseAsync(correction, 901, default);
        Assert.Equal(correctedId, await state.Store.ReverseAsync(correction, 902, default));
        await Assert.ThrowsAsync<FrozenJournalConflictException>(() => state.Store.ReverseAsync(
            FrozenSystemJournalRequest.ForReversal(1, "foundation:correction:1", originalId,
                new("Changed reason", Date.AddDays(1), Date.AddDays(1))), 901, default));
        await Assert.ThrowsAsync<FrozenJournalConflictException>(() => state.Store.ReverseAsync(
            FrozenSystemJournalRequest.ForReversal(1, "foundation:correction:2", originalId,
                new("Second correction", Date.AddDays(1), Date.AddDays(1))), 901, default));
        Assert.Equal(2, await state.Db.JournalEntries.CountAsync());
        Assert.Equal(correctedId, (await state.Db.JournalEntries.SingleAsync(row => row.Id == originalId)).ReversedByJournalEntryId);
        Assert.Equal(originalId, (await state.Db.JournalEntries.SingleAsync(row => row.Id == correctedId)).ReversalOfJournalEntryId);
    });

    [Fact]
    public Task ConcurrentSameCorrectionReversalResolvesOneJournal() => WithDatabase(async state =>
    {
        var originalId = await state.Store.PostAsync(Request(), 901, default);
        var correction = FrozenSystemJournalRequest.ForReversal(1, "foundation:correction:concurrent",
            originalId, new("Concurrent correction", Date.AddDays(1), Date.AddDays(1)));
        var connection = state.Db.Database.GetConnectionString()!;
        var first = CreateState(connection);
        var second = CreateState(connection);
        await using var firstDb = first.Db;
        await using var secondDb = second.Db;

        var results = await Task.WhenAll(
            first.Store.ReverseAsync(correction, 901, default),
            second.Store.ReverseAsync(correction, 902, default));

        Assert.Equal(results[0], results[1]);
        Assert.Equal(2, await state.Db.JournalEntries.CountAsync());
        Assert.Single(await state.Db.Set<FrozenSystemJournalOperation>()
            .ToArrayAsync(), row => row.PostingReference == "foundation:correction:concurrent");
    });

    [Fact]
    public Task ConcurrentDifferentCorrectionsForOneOriginalHaveOneWinner() => WithDatabase(async state =>
    {
        var originalId = await state.Store.PostAsync(Request(), 901, default);
        var firstCorrection = FrozenSystemJournalRequest.ForReversal(1, "foundation:correction:first",
            originalId, new("First correction", Date.AddDays(1), Date.AddDays(1)));
        var secondCorrection = FrozenSystemJournalRequest.ForReversal(1, "foundation:correction:second",
            originalId, new("Second correction", Date.AddDays(1), Date.AddDays(1)));
        var connection = state.Db.Database.GetConnectionString()!;
        var first = CreateState(connection);
        var second = CreateState(connection);
        await using var firstDb = first.Db;
        await using var secondDb = second.Db;

        var results = await Task.WhenAll(
            CaptureReversalAsync(first.Store, firstCorrection),
            CaptureReversalAsync(second.Store, secondCorrection));

        Assert.Single(results, result => result.JournalId.HasValue);
        Assert.Single(results, result => result.Error is FrozenJournalConflictException);
        Assert.Equal(2, await state.Db.JournalEntries.CountAsync());
        Assert.Equal(2, await state.Db.Set<FrozenSystemJournalOperation>().CountAsync());
    });

    [Fact]
    public Task AuditFailureRollsBackJournalBindingAndNumberAllocation() => WithDatabase(async state =>
    {
        state.Failure.Fail = true;
        await Assert.ThrowsAsync<IOException>(() => state.Store.PostAsync(Request(), 901, default));
        Assert.Equal(0, await state.Db.JournalEntries.CountAsync());
        Assert.Equal(0, await state.Db.Set<FrozenSystemJournalOperation>().CountAsync());
        Assert.Equal(0, await state.Db.AccountingNumberSequences.CountAsync());
        state.Failure.Fail = false;
        await state.Store.PostAsync(Request(), 901, default);
        Assert.Equal(1, await state.Db.JournalEntries.CountAsync());
    });

    [Fact]
    public Task LegacyIdentityIsNeverAdopted_AndLegacyRepeatBehaviorIsUnchanged() => WithDatabase(async state =>
    {
        var input = Request().ReadFrozenPayload<ManualJournalDraftInput>();
        var legacy = await state.Legacy.CreateOrGetSystemDraftAsync(1, 901, input, null, default);
        Assert.Equal(legacy, await state.Legacy.CreateOrGetSystemDraftAsync(1, 901,
            Request(11).ReadFrozenPayload<ManualJournalDraftInput>(), null, default));
        await Assert.ThrowsAsync<FrozenJournalConflictException>(() => state.Store.PostAsync(Request(), 901, default));
        Assert.Equal(0, await state.Db.Set<FrozenSystemJournalOperation>().CountAsync());
        Assert.Equal(JournalEntryStatuses.Draft, (await state.Db.JournalEntries.SingleAsync()).Status);
    });

    [Fact]
    public Task ScopeMismatchAndFrozenEvidenceMutationAreRejected() => WithDatabase(async state =>
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => state.Store.PostAsync(
            FrozenSystemJournalRequest.ForPosting(2, "foundation:1", Request().ReadFrozenPayload<ManualJournalDraftInput>()), 901, default));
        await state.Store.PostAsync(Request(), 901, default);
        var evidence = await state.Db.Set<FrozenSystemJournalOperation>().SingleAsync();
        evidence.PayloadHash = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => state.Db.SaveChangesAsync());
    });

    [Fact]
    public Task ConcurrentIdenticalRequestsResolveOneCommittedJournal() => WithDatabase(async state =>
    {
        var first = CreateState(state.Db.Database.GetConnectionString()!);
        var second = CreateState(state.Db.Database.GetConnectionString()!);
        await using var firstDb = first.Db;
        await using var secondDb = second.Db;
        var request = Request();
        var results = await Task.WhenAll(first.Store.PostAsync(request, 901, default),
            second.Store.PostAsync(request, 902, default));
        Assert.Equal(results[0], results[1]);
        Assert.Equal(1, await state.Db.JournalEntries.CountAsync());
        Assert.Equal(1, await state.Db.Set<FrozenSystemJournalOperation>().CountAsync());
    });

    private static FrozenSystemJournalRequest Request(decimal amount = 10, string reference = "foundation:1") =>
        FrozenSystemJournalRequest.ForPosting(1, reference, new("SalesInvoice", Date, Date, Date,
            "SalesInvoice", reference, null, "Foundation contract", null, null, null, null,
            [new(1001, null, amount, 0, null, null, null, null, null, null),
                new(2001, null, 0, amount, null, null, null, null, null, null)]));

    private static async Task<(long? JournalId, Exception? Error)> CaptureReversalAsync(
        FrozenSystemJournalStore store, FrozenSystemJournalRequest request)
    {
        try
        {
            return (await store.ReverseAsync(request, 901, default), null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }

    private sealed record State(RetailPOSDbContext Db, FrozenSystemJournalStore Store,
        ManualJournalDraftStore Legacy, FailAuditInterceptor Failure);

    private static State CreateState(string connection)
    {
        var audit = new AuditContext();
        var failure = new FailAuditInterceptor();
        var db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseNpgsql(connection)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .AddInterceptors(new AuditChangeTrackerInterceptor(audit, NullLogger<AuditChangeTrackerInterceptor>.Instance), failure).Options);
        audit.Initialize(AuditContext.ForBusinessSystemJob("frozen-journal-test", 1));
        var service = new AuditService(db, audit, Mock.Of<IServiceProvider>(), NullLogger<AuditService>.Instance);
        var boundary = new RequiredAuditTransaction(db, audit, service, NullLogger<RequiredAuditTransaction>.Instance);
        var legacy = new ManualJournalDraftStore(db, service, new JournalNumberAllocator(db), NullLogger<ManualJournalDraftStore>.Instance);
        return new(db, new FrozenSystemJournalStore(db, legacy, boundary, audit), legacy, failure);
    }

    private static async Task WithDatabase(Func<State, Task> action)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var connection = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connection))
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "RetailPOS.API", "appsettings.json"));
            connection = new ConfigurationBuilder().AddJsonFile(path).Build().GetConnectionString("DefaultConnection");
        }
        await PostgresTestDatabase.RunAsync(connection!, PostgresTestDatabasePrefix.AccountingSequence, async database =>
        {
            await using (var seed = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(database.ConnectionString)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)).Options))
            {
                await seed.Database.MigrateAsync();
                seed.Businesses.Add(new Business { Id = 1, Name = "Foundation" });
                seed.AccountingConfigurations.Add(new AccountingConfiguration { BusinessId = 1 });
                seed.FinancialYears.Add(new FinancialYear { Id = 2026, BusinessId = 1, YearCode = "2026",
                    Name = "FY2026", StartDate = Date.AddMonths(-7), EndDate = Date.AddMonths(4), IsCurrent = true });
                seed.AccountingPeriods.Add(new AccountingPeriod { Id = 8026, FinancialYearId = 2026,
                    PeriodNumber = 8, Name = "August", StartDate = Date.AddDays(-14),
                    EndDate = Date.AddDays(16), Status = AccountingPeriodStatuses.Open });
                seed.ChartAccounts.AddRange(
                    new ChartAccount { Id = 1001, BusinessId = 1, AccountCode = "1000", AccountName = "Cash",
                        AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true },
                    new ChartAccount { Id = 2001, BusinessId = 1, AccountCode = "2000", AccountName = "Income",
                        AccountType = ChartAccountTypes.Income, NormalBalanceType = NormalBalanceTypes.Credit, IsActive = true });
                await seed.SaveChangesAsync();
            }
            var state = CreateState(database.ConnectionString);
            await using var db = state.Db;
            await action(state);
        });
    }

    private sealed class FailAuditInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fail && eventData.Context!.ChangeTracker.Entries<AuditEvent>().Any(entry => entry.State == EntityState.Added))
                throw new IOException("Injected audit failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
