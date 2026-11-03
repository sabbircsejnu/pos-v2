using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class JournalNumberRelationalConcurrencyTests
{
    static JournalNumberRelationalConcurrencyTests()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    [Fact]
    public async Task ConcurrentPosting_AllocatesUniqueSequentialNumbers()
    {
        await using var harness = await RelationalHarness.CreateAsync();
        await harness.SeedDraftJournalsAsync(count: 24);

        var draftIds = await harness.GetDraftJournalIdsAsync();

        var postTasks = draftIds.Select(id => harness.PostDraftJournalAsync(id)).ToArray();
        var numbers = await Task.WhenAll(postTasks);

        Assert.Equal(draftIds.Count, numbers.Length);
        Assert.Equal(numbers.Length, numbers.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(numbers, number => Assert.Matches(@"^JR-2026-\d{6}$", number));

        var sequenceValues = numbers
            .Select(ExtractSequenceValue)
            .OrderBy(x => x)
            .ToList();

        for (var i = 0; i < sequenceValues.Count; i++)
        {
            Assert.Equal(i + 1, sequenceValues[i]);
        }

        await using var verifyContext = harness.CreateDbContext();
        var persistedNumbers = await verifyContext.JournalEntries
            .Where(x => x.BusinessId == 1 && x.Status == JournalEntryStatuses.Posted)
            .Select(x => x.JournalNumber)
            .ToListAsync();

        Assert.DoesNotContain(persistedNumbers, x => string.IsNullOrWhiteSpace(x));
        Assert.Equal(numbers.Length, persistedNumbers.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var sequence = await verifyContext.AccountingNumberSequences
            .SingleAsync(x => x.BusinessId == 1 && x.SequenceType == AccountingSequenceTypes.Journal && x.ScopeKey == "fy:2026");

        Assert.Equal(numbers.Length, sequence.LastIssuedNumber);
    }

    [Fact]
    public async Task RolledBackAllocation_DoesNotPersistSequenceMutation()
    {
        await using var harness = await RelationalHarness.CreateAsync();

        await using (var context = harness.CreateDbContext())
        {
            await context.Database.OpenConnectionAsync();
            await using var tx = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var allocator = new JournalNumberAllocator(context);
            var allocated = await allocator.AllocateNextJournalNumberAsync(
                businessId: 1,
                financialYearId: 2026,
                postingDate: new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc),
                actorUserId: 9001);

            Assert.Equal("JR-2026-000001", allocated);
            await tx.RollbackAsync();
        }

        await using (var verifyContext = harness.CreateDbContext())
        {
            var sequenceExists = await verifyContext.AccountingNumberSequences
                .AnyAsync(x => x.BusinessId == 1 && x.SequenceType == AccountingSequenceTypes.Journal && x.ScopeKey == "fy:2026");

            Assert.False(sequenceExists);
        }

        await using (var commitContext = harness.CreateDbContext())
        {
            await commitContext.Database.OpenConnectionAsync();
            await using var tx = await commitContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var allocator = new JournalNumberAllocator(commitContext);
            var allocated = await allocator.AllocateNextJournalNumberAsync(
                businessId: 1,
                financialYearId: 2026,
                postingDate: new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc),
                actorUserId: 9001);

            Assert.Equal("JR-2026-000001", allocated);
            await commitContext.SaveChangesAsync();
            await tx.CommitAsync();
        }

        await using var finalContext = harness.CreateDbContext();
        var sequence = await finalContext.AccountingNumberSequences
            .SingleAsync(x => x.BusinessId == 1 && x.SequenceType == AccountingSequenceTypes.Journal && x.ScopeKey == "fy:2026");

        Assert.Equal(1, sequence.LastIssuedNumber);
    }

    [Fact]
    public async Task ManualReferences_CanRepeat_WhileSystemReferences_RemainUnique()
    {
        await using var harness = await RelationalHarness.CreateAsync();
        var now = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);

        await using (var context = harness.CreateDbContext())
        {
            context.JournalEntries.Add(CreateDraftJournal(
                referenceId: "REF-SHARED-1",
                isSystemGenerated: false,
                description: "Manual A",
                now: now));

            context.JournalEntries.Add(CreateDraftJournal(
                referenceId: "REF-SHARED-1",
                isSystemGenerated: false,
                description: "Manual B",
                now: now.AddMinutes(1)));

            await context.SaveChangesAsync();
        }

        await using (var verifyManual = harness.CreateDbContext())
        {
            var manualCount = await verifyManual.JournalEntries
                .CountAsync(x => x.BusinessId == 1 && !x.IsSystemGenerated && x.ReferenceType == "Manual" && x.ReferenceId == "REF-SHARED-1");

            Assert.Equal(2, manualCount);
        }

        await using (var context = harness.CreateDbContext())
        {
            context.JournalEntries.Add(CreateDraftJournal(
                referenceId: "SI-2001",
                isSystemGenerated: true,
                description: "System A",
                journalType: "SalesInvoice",
                referenceType: "SalesInvoice",
                now: now.AddMinutes(2)));
            await context.SaveChangesAsync();

            context.JournalEntries.Add(CreateDraftJournal(
                referenceId: "SI-2001",
                isSystemGenerated: true,
                description: "System B",
                journalType: "SalesInvoice",
                referenceType: "SalesInvoice",
                now: now.AddMinutes(3)));

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task ConcurrentJournalCancellation_TransitionsOnceAndRejectsTheDuplicate()
    {
        await using var harness = await RelationalHarness.CreateAsync();
        await harness.SeedDraftJournalsAsync(count: 1);
        var journalId = Assert.Single(await harness.GetDraftJournalIdsAsync());
        var firstAudit = CreateAuditMock();
        var secondAudit = CreateAuditMock();

        var outcomes = await Task.WhenAll(
            harness.CancelDraftJournalAsync(journalId, 9001, firstAudit.Object),
            harness.CancelDraftJournalAsync(journalId, 9002, secondAudit.Object));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.IsType<InvalidOperationException>(Assert.Single(outcomes, outcome => outcome is not null));
        Assert.Equal(
            1,
            new[] { firstAudit, secondAudit }.Sum(audit => audit.Invocations.Count(invocation =>
                invocation.Method.Name == nameof(IAuditService.RecordAsync))));
        Assert.Equal(
            1,
            new[] { firstAudit, secondAudit }.Sum(audit => audit.Invocations.Count(invocation =>
                invocation.Method.Name == nameof(IAuditService.RecordFailureAsync))));

        await using var verifyContext = harness.CreateDbContext();
        var cancelled = await verifyContext.JournalEntries.SingleAsync(entry => entry.Id == journalId);
        Assert.Equal(JournalEntryStatuses.Cancelled, cancelled.Status);
        Assert.Contains(cancelled.CancelledBy, new long?[] { 9001, 9002 });
    }

    [Fact]
    public async Task JournalCancellationPersistenceFailure_RollsBackTheOwnedTransaction()
    {
        await using var harness = await RelationalHarness.CreateAsync();
        await harness.SeedDraftJournalsAsync(count: 1);
        var journalId = Assert.Single(await harness.GetDraftJournalIdsAsync());
        await using (var setupContext = harness.CreateDbContext())
        {
            await setupContext.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION retailpos_reject_journal_cancel() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.status = 'Cancelled' THEN
                        RAISE EXCEPTION 'Rejected journal cancellation by test trigger';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER retailpos_reject_journal_cancel
                BEFORE UPDATE ON journal_entries
                FOR EACH ROW EXECUTE FUNCTION retailpos_reject_journal_cancel();
                """);
        }

        var audit = CreateAuditMock();
        var exception = await harness.CancelDraftJournalAsync(journalId, 9001, audit.Object);
        var updateException = Assert.IsType<DbUpdateException>(exception);
        Assert.Contains("Rejected journal cancellation", updateException.ToString(), StringComparison.Ordinal);
        audit.Verify(
            service => service.RecordFailureAsync(
                AuditActionType.Cancel,
                It.IsAny<string>(),
                It.IsAny<string>(),
                "JournalEntry",
                journalId.ToString(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        await using var verifyContext = harness.CreateDbContext();
        var journal = await verifyContext.JournalEntries.SingleAsync(entry => entry.Id == journalId);
        Assert.Equal(JournalEntryStatuses.Draft, journal.Status);
        Assert.Null(journal.CancelledAt);
        Assert.Null(journal.CancelledBy);
    }

    [Fact]
    public async Task ConcurrentDuplicatePosting_ProducesOnePostedTransitionAndJournalNumber()
    {
        await using var harness = await RelationalHarness.CreateAsync();
        await harness.SeedDraftJournalsAsync(count: 1);
        var journalId = Assert.Single(await harness.GetDraftJournalIdsAsync());
        var audit = CreateAuditMock();

        var results = await Task.WhenAll(
            harness.PostDraftJournalThroughStoreAsync(journalId, 9001, audit.Object),
            harness.PostDraftJournalThroughStoreAsync(journalId, 9002, audit.Object));

        Assert.Equal(new[] { journalId, journalId }, results);
        audit.Verify(
            service => service.RecordAsync(
                It.Is<AuditEventInput>(input => input.Summary == $"Journal posted: {journalId}"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        audit.Verify(
            service => service.RecordFailureAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        await using var verifyContext = harness.CreateDbContext();
        var posted = await verifyContext.JournalEntries.SingleAsync(entry => entry.Id == journalId);
        Assert.Equal(JournalEntryStatuses.Posted, posted.Status);
        Assert.NotNull(posted.JournalNumber);
        Assert.Contains(posted.PostedBy, new long?[] { 9001, 9002 });
        var sequence = await verifyContext.AccountingNumberSequences.SingleAsync(
            entry => entry.BusinessId == 1
                && entry.SequenceType == AccountingSequenceTypes.Journal
                && entry.ScopeKey == "fy:2026");
        Assert.Equal(1, sequence.LastIssuedNumber);
    }

    [Fact]
    public async Task JournalPostingPersistenceFailure_RollsBackJournalAndNumberAllocation()
    {
        await using var harness = await RelationalHarness.CreateAsync();
        await harness.SeedDraftJournalsAsync(count: 1);
        var journalId = Assert.Single(await harness.GetDraftJournalIdsAsync());
        await using (var setupContext = harness.CreateDbContext())
        {
            await setupContext.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION retailpos_reject_journal_post() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.status = 'Posted' THEN
                        RAISE EXCEPTION 'Rejected journal posting by test trigger';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER retailpos_reject_journal_post
                BEFORE UPDATE ON journal_entries
                FOR EACH ROW EXECUTE FUNCTION retailpos_reject_journal_post();
                """);
        }

        var audit = CreateAuditMock();
        var updateException = await Assert.ThrowsAsync<DbUpdateException>(
            () => harness.PostDraftJournalThroughStoreAsync(
                journalId,
                9001,
                audit.Object));
        Assert.Contains("Rejected journal posting", updateException.ToString(), StringComparison.Ordinal);
        audit.Verify(
            service => service.RecordFailureAsync(
                AuditActionType.Reject,
                It.IsAny<string>(),
                $"Journal posting rejected: {journalId}",
                "JournalEntry",
                journalId.ToString(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        await using var verifyContext = harness.CreateDbContext();
        var journal = await verifyContext.JournalEntries.SingleAsync(entry => entry.Id == journalId);
        Assert.Equal(JournalEntryStatuses.Draft, journal.Status);
        Assert.Null(journal.JournalNumber);
        Assert.Null(journal.PostedAt);
        Assert.Null(journal.PostedBy);
        Assert.False(await verifyContext.AccountingNumberSequences.AnyAsync(
            entry => entry.BusinessId == 1
                && entry.SequenceType == AccountingSequenceTypes.Journal
                && entry.ScopeKey == "fy:2026"));
    }

    [Fact]
    public async Task ConcurrentDuplicateReversal_CreatesOneLinkedPostedReversal()
    {
        await using var harness = await RelationalHarness.CreateAsync();
        await harness.SeedDraftJournalsAsync(count: 1);
        var journalId = Assert.Single(await harness.GetDraftJournalIdsAsync());
        await harness.PostDraftJournalThroughStoreAsync(
            journalId,
            9001,
            CreateAuditMock().Object);
        var firstAudit = CreateAuditMock();
        var secondAudit = CreateAuditMock();

        var first = harness.ReverseJournalThroughStoreAsync(
            journalId,
            9002,
            "Concurrent reversal",
            firstAudit.Object);
        var second = harness.ReverseJournalThroughStoreAsync(
            journalId,
            9003,
            "Concurrent reversal",
            secondAudit.Object);
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await Task.WhenAll(first, second));

        Assert.Equal(
            1,
            new[] { firstAudit, secondAudit }.Sum(audit => audit.Invocations.Count(invocation =>
                invocation.Method.Name == nameof(IAuditService.RecordAsync))));
        Assert.Equal(
            1,
            new[] { firstAudit, secondAudit }.Sum(audit => audit.Invocations.Count(invocation =>
                invocation.Method.Name == nameof(IAuditService.RecordFailureAsync))));

        await using var verifyContext = harness.CreateDbContext();
        var original = await verifyContext.JournalEntries.SingleAsync(entry => entry.Id == journalId);
        Assert.Equal(JournalEntryStatuses.Reversed, original.Status);
        Assert.NotNull(original.ReversedByJournalEntryId);
        var reversal = await verifyContext.JournalEntries
            .Include(entry => entry.Lines)
            .SingleAsync(entry => entry.ReversalOfJournalEntryId == journalId);
        Assert.Equal(JournalEntryStatuses.Posted, reversal.Status);
        Assert.Equal(original.ReversedByJournalEntryId, reversal.Id);
        Assert.Equal(2, reversal.Lines.Count);
        Assert.Contains(reversal.Lines, line => line.DebitAmount == 10 && line.CreditAmount == 0);
        Assert.Contains(reversal.Lines, line => line.CreditAmount == 10 && line.DebitAmount == 0);
        var sequence = await verifyContext.AccountingNumberSequences.SingleAsync(
            entry => entry.BusinessId == 1
                && entry.SequenceType == AccountingSequenceTypes.Journal
                && entry.ScopeKey == "fy:2026");
        Assert.Equal(2, sequence.LastIssuedNumber);
    }

    [Fact]
    public async Task JournalReversalPersistenceFailure_RollsBackReversalAndNumberAllocation()
    {
        await using var harness = await RelationalHarness.CreateAsync();
        await harness.SeedDraftJournalsAsync(count: 1);
        var journalId = Assert.Single(await harness.GetDraftJournalIdsAsync());
        await harness.PostDraftJournalThroughStoreAsync(
            journalId,
            9001,
            CreateAuditMock().Object);
        await using (var setupContext = harness.CreateDbContext())
        {
            await setupContext.Database.ExecuteSqlRawAsync(
                """
                CREATE FUNCTION retailpos_reject_journal_reversal() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.status = 'Reversed' THEN
                        RAISE EXCEPTION 'Rejected original journal reversal by test trigger';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER retailpos_reject_journal_reversal
                BEFORE UPDATE ON journal_entries
                FOR EACH ROW EXECUTE FUNCTION retailpos_reject_journal_reversal();
                """);
        }

        var audit = CreateAuditMock();
        var updateException = await Assert.ThrowsAsync<DbUpdateException>(
            () => harness.ReverseJournalThroughStoreAsync(
                journalId,
                9002,
                "Rollback test",
                audit.Object));
        Assert.Contains(
            "Rejected original journal reversal",
            updateException.ToString(),
            StringComparison.Ordinal);
        audit.Verify(
            service => service.RecordFailureAsync(
                AuditActionType.Reject,
                It.IsAny<string>(),
                $"Journal reversal rejected: {journalId}",
                "JournalEntry",
                journalId.ToString(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        await using var verifyContext = harness.CreateDbContext();
        var original = await verifyContext.JournalEntries.SingleAsync(entry => entry.Id == journalId);
        Assert.Equal(JournalEntryStatuses.Posted, original.Status);
        Assert.Null(original.ReversedByJournalEntryId);
        Assert.Empty(await verifyContext.JournalEntries
            .Where(entry => entry.ReversalOfJournalEntryId == journalId)
            .ToListAsync());
        var sequence = await verifyContext.AccountingNumberSequences.SingleAsync(
            entry => entry.BusinessId == 1
                && entry.SequenceType == AccountingSequenceTypes.Journal
                && entry.ScopeKey == "fy:2026");
        Assert.Equal(1, sequence.LastIssuedNumber);
    }

    private static Mock<IAuditService> CreateAuditMock()
    {
        var audit = new Mock<IAuditService>();
        audit
            .Setup(service => service.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        audit
            .Setup(service => service.RecordFailureAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return audit;
    }

    private static int ExtractSequenceValue(string number)
    {
        var match = Regex.Match(number, @"(\d+)$");
        Assert.True(match.Success);
        return int.Parse(match.Value);
    }

    private static JournalEntry CreateDraftJournal(
        string referenceId,
        bool isSystemGenerated,
        string description,
        DateTime now,
        string journalType = "ManualJournal",
        string referenceType = "Manual")
    {
        return new JournalEntry
        {
            BusinessId = 1,
            JournalType = journalType,
            TransactionDate = now,
            PostingDate = now,
            BusinessDate = now.Date,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Description = description,
            Status = JournalEntryStatuses.Draft,
            IsSystemGenerated = isSystemGenerated,
            CreatedAt = now,
            CreatedBy = 101,
            UpdatedAt = now,
            UpdatedBy = 101,
            Lines =
            [
                new JournalEntryLine
                {
                    AccountId = 1001,
                    DebitAmount = 10,
                    CreditAmount = 0,
                    CreatedAt = now,
                    CreatedBy = 101
                },
                new JournalEntryLine
                {
                    AccountId = 2001,
                    DebitAmount = 0,
                    CreditAmount = 10,
                    CreatedAt = now,
                    CreatedBy = 101
                }
            ]
        };
    }

    private sealed class RelationalHarness : IAsyncDisposable
    {
        private readonly PostgresTestDatabase _database;

        private RelationalHarness(PostgresTestDatabase database)
        {
            _database = database;
        }

        public static async Task<RelationalHarness> CreateAsync()
        {
            var baseConnectionString = ResolveBaseConnectionString();
            var database = await PostgresTestDatabase.CreateAsync(
                baseConnectionString,
                PostgresTestDatabasePrefix.AccountingSequence);
            try
            {
                var harness = new RelationalHarness(database);
                await harness.MigrateAndSeedBaseAsync();
                return harness;
            }
            catch (Exception setupException)
            {
                try
                {
                    await database.DisposeAsync();
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(
                        $"Accounting PostgreSQL harness setup failed and database {database.Name} could not be removed.",
                        setupException,
                        cleanupException);
                }

                throw;
            }
        }

        public RetailPOSDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(_database.ConnectionString)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;

            return new RetailPOSDbContext(options);
        }

        public async Task SeedDraftJournalsAsync(int count)
        {
            await using var context = CreateDbContext();
            var now = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

            for (var i = 1; i <= count; i++)
            {
                context.JournalEntries.Add(new JournalEntry
                {
                    BusinessId = 1,
                    JournalType = "ManualJournal",
                    TransactionDate = now,
                    PostingDate = now,
                    BusinessDate = now.Date,
                    ReferenceType = "Manual",
                    ReferenceId = $"C-{i}",
                    Description = $"Concurrent draft {i}",
                    Status = JournalEntryStatuses.Draft,
                    IsSystemGenerated = false,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = 101,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedBy = 101,
                    Lines =
                    [
                        new JournalEntryLine
                        {
                            AccountId = 1001,
                            DebitAmount = 10,
                            CreditAmount = 0,
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = 101
                        },
                        new JournalEntryLine
                        {
                            AccountId = 2001,
                            DebitAmount = 0,
                            CreditAmount = 10,
                            CreatedAt = DateTime.UtcNow,
                            CreatedBy = 101
                        }
                    ]
                });
            }

            await context.SaveChangesAsync();
        }

        public async Task<List<long>> GetDraftJournalIdsAsync()
        {
            await using var context = CreateDbContext();
            return await context.JournalEntries
                .Where(x => x.BusinessId == 1 && x.Status == JournalEntryStatuses.Draft)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();
        }

        public async Task<string> PostDraftJournalAsync(long journalId)
        {
            await using var context = CreateDbContext();

            var audit = new Mock<IAuditService>();
            audit
                .Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            audit
                .Setup(x => x.RecordFailureAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var store = new ManualJournalDraftStore(
                context,
                audit.Object,
                new JournalNumberAllocator(context),
                NullLogger<ManualJournalDraftStore>.Instance);

            await store.PostAsync(1, 9001, journalId, CancellationToken.None);
            return await context.JournalEntries
                .Where(x => x.BusinessId == 1 && x.Id == journalId)
                .Select(x => x.JournalNumber)
                .SingleAsync() ?? string.Empty;
        }

        public async Task<long> PostDraftJournalThroughStoreAsync(
            long journalId,
            long actorUserId,
            IAuditService auditService)
        {
            await using var context = CreateDbContext();
            var store = new ManualJournalDraftStore(
                context,
                auditService,
                new JournalNumberAllocator(context),
                NullLogger<ManualJournalDraftStore>.Instance);
            return await store.PostAsync(
                businessId: 1,
                actorUserId,
                journalId,
                CancellationToken.None);
        }

        public async Task<long> ReverseJournalThroughStoreAsync(
            long journalId,
            long actorUserId,
            string reason,
            IAuditService auditService)
        {
            await using var context = CreateDbContext();
            var store = new ManualJournalDraftStore(
                context,
                auditService,
                new JournalNumberAllocator(context),
                NullLogger<ManualJournalDraftStore>.Instance);
            return await store.ReverseAsync(
                businessId: 1,
                actorUserId,
                journalId,
                new ManualJournalReversalInput(
                    reason,
                    new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc)),
                CancellationToken.None);
        }

        public async Task<Exception?> CancelDraftJournalAsync(
            long journalId,
            long actorUserId,
            IAuditService auditService)
        {
            await using var context = CreateDbContext();
            var store = new ManualJournalDraftStore(
                context,
                auditService,
                new JournalNumberAllocator(context),
                NullLogger<ManualJournalDraftStore>.Instance);
            try
            {
                await store.CancelAsync(
                    businessId: 1,
                    actorUserId,
                    journalId,
                    reason: "Relational cancellation test",
                    CancellationToken.None);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private async Task MigrateAndSeedBaseAsync()
        {
            await using var context = CreateDbContext();
            await context.Database.MigrateAsync();

            context.Businesses.Add(new Business
            {
                Id = 1,
                Name = "Concurrency Business",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            context.AccountingConfigurations.Add(new AccountingConfiguration
            {
                BusinessId = 1,
                JournalNumberPrefix = "JR",
                JournalNumberPadding = 6,
                JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            context.FinancialYears.Add(new FinancialYear
            {
                Id = 2026,
                BusinessId = 1,
                YearCode = "2026",
                Name = "FY 2026",
                StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                IsCurrent = true,
                IsClosed = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            context.AccountingPeriods.Add(new AccountingPeriod
            {
                Id = 8026,
                FinancialYearId = 2026,
                PeriodNumber = 8,
                Name = "August",
                StartDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
                Status = AccountingPeriodStatuses.Open,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            context.ChartAccounts.AddRange(
                new ChartAccount
                {
                    Id = 1001,
                    BusinessId = 1,
                    AccountCode = "1000",
                    AccountName = "Cash",
                    AccountType = ChartAccountTypes.Asset,
                    NormalBalanceType = NormalBalanceTypes.Debit,
                    AllowManualPosting = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new ChartAccount
                {
                    Id = 2001,
                    BusinessId = 1,
                    AccountCode = "2000",
                    AccountName = "Revenue",
                    AccountType = ChartAccountTypes.Income,
                    NormalBalanceType = NormalBalanceTypes.Credit,
                    AllowManualPosting = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });

            await context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
            => await _database.DisposeAsync();

        private static string ResolveBaseConnectionString()
        {
            return RetailPOS.Tests.Infrastructure.InfrastructureTestSettings.RequireIsolatedPostgres();
        }
    }
}
