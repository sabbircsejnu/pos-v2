using System.Data;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

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

            var allocator = new JournalNumberService(context);
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

            var allocator = new JournalNumberService(commitContext);
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
        private readonly string _databaseName;
        private readonly string _databaseConnectionString;
        private readonly string _adminConnectionString;

        private RelationalHarness(string databaseName, string databaseConnectionString, string adminConnectionString)
        {
            _databaseName = databaseName;
            _databaseConnectionString = databaseConnectionString;
            _adminConnectionString = adminConnectionString;
        }

        public static async Task<RelationalHarness> CreateAsync()
        {
            var baseConnectionString = ResolveBaseConnectionString();
            var baseBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString);
            var dbName = $"retailpos_acc_seq_{Guid.NewGuid():N}";

            var adminBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString)
            {
                Database = string.IsNullOrWhiteSpace(baseBuilder.Database) ? "postgres" : baseBuilder.Database
            };

            await using (var adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString))
            {
                await adminConnection.OpenAsync();
                await using var createCmd = adminConnection.CreateCommand();
                createCmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
                await createCmd.ExecuteNonQueryAsync();
            }

            var dbBuilder = new NpgsqlConnectionStringBuilder(baseBuilder.ConnectionString)
            {
                Database = dbName
            };

            var harness = new RelationalHarness(dbName, dbBuilder.ConnectionString, adminBuilder.ConnectionString);
            await harness.MigrateAndSeedBaseAsync();
            return harness;
        }

        public RetailPOSDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
                .UseNpgsql(_databaseConnectionString)
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

            var tenant = new Mock<ITenantAccessService>();
            var audit = new Mock<IAuditService>();
            tenant.Setup(x => x.IsSuperAdmin).Returns(false);
            tenant.Setup(x => x.EffectiveBusinessId).Returns(1);
            tenant.Setup(x => x.RequireBusinessId()).Returns(1);

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

            var service = new JournalEntryService(
                context,
                tenant.Object,
                new JournalNumberService(context),
                audit.Object,
                NullLogger<JournalEntryService>.Instance);

            var posted = await service.PostAsync(journalId, 9001);
            return posted.JournalNumber ?? string.Empty;
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
        {
            await using var adminConnection = new NpgsqlConnection(_adminConnectionString);
            await adminConnection.OpenAsync();

            await using (var terminate = adminConnection.CreateCommand())
            {
                terminate.CommandText = @"
SELECT pg_terminate_backend(pid)
FROM pg_stat_activity
WHERE datname = @dbName AND pid <> pg_backend_pid();";
                terminate.Parameters.AddWithValue("dbName", _databaseName);
                await terminate.ExecuteNonQueryAsync();
            }

            await using (var drop = adminConnection.CreateCommand())
            {
                drop.CommandText = $"DROP DATABASE IF EXISTS \"{_databaseName}\"";
                await drop.ExecuteNonQueryAsync();
            }
        }

        private static string ResolveBaseConnectionString()
        {
            var fromEnv = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                return fromEnv;
            }

            return "Host=127.0.0.1;Port=5435;Database=retailpos_db;Username=admin;Password=589123Qwe";
        }
    }
}
