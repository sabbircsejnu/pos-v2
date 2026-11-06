using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using System.Data.Common;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingSettlements.Abstractions;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.AccountingSettlements;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public sealed class SettlementPostingPostgresConcurrencyTests
{
    static SettlementPostingPostgresConcurrencyTests() =>
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

    [Fact]
    public async Task ConcurrentSameKeyPostingAfterBothPreflightReadsProducesOneBatchAndJournal()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.SettlementPosting,
            PostgresTestDatabaseStrategy.FreshMigration,
            async database =>
            {
                await using (var setup = CreateContext(database.ConnectionString))
                {
                    await setup.Database.EnsureCreatedAsync();
                    Seed(setup);
                    await setup.SaveChangesAsync();
                }

                var barrier = new SettlementPostingPreflightBarrier();
                var tenant = new Mock<ITenantAccessService>();
                tenant.Setup(access => access.RequireBusinessId()).Returns(1);
                var audit = new Mock<IAuditService>();
                var postedAuditCount = 0;
                audit.Setup(service => service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<AuditEventInput, CancellationToken>((input, _) =>
                    {
                        if (input.Summary.StartsWith("Journal posted:", StringComparison.Ordinal))
                            Interlocked.Increment(ref postedAuditCount);
                    })
                    .Returns(Task.CompletedTask);

                async Task<Exception?> PostAsync()
                {
                    await using var context = CreateContext(database.ConnectionString);
                    var journalCommands = CreateJournalCommandSender(context, audit.Object);
                    var store = new SettlementTransferCommandStore(
                        context,
                        new SettlementPostingPreflightResolver(barrier),
                        new SettlementJournalPoster(journalCommands, tenant.Object),
                        Mock.Of<ISettlementJournalReverser>());

                    try
                    {
                        await store.PostSettlementAsync(
                            1,
                            101,
                            new SettlementPostingInput(
                                "card",
                                103,
                                501,
                                new DateTime(2026, 9, 6),
                                20m,
                                0m,
                                null,
                                "concurrent-settlement"),
                            CancellationToken.None);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }

                var outcomes = await Task.WhenAll(PostAsync(), PostAsync());
                Assert.Single(outcomes, outcome => outcome is null);
                var duplicateFailure = Assert.IsType<DbUpdateException>(
                    Assert.Single(outcomes, outcome => outcome is not null));
                var postgresFailure = Assert.IsType<PostgresException>(duplicateFailure.InnerException);
                Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresFailure.SqlState);

                await using var verify = CreateContext(database.ConnectionString);
                var batch = await verify.SettlementBatches
                    .SingleAsync(value => value.BusinessId == 1
                        && value.IdempotencyKey == "concurrent-settlement");
                Assert.Equal(SettlementBatchStatuses.Posted, batch.Status);
                Assert.NotNull(batch.JournalEntryId);
                Assert.Single(await verify.JournalEntries
                    .Where(journal => journal.BusinessId == 1
                        && journal.ReferenceType == "SettlementBatch"
                        && journal.ReferenceId == batch.Id.ToString()
                        && journal.Status == JournalEntryStatuses.Posted)
                    .ToListAsync());
                Assert.Equal(3, await verify.JournalEntryLines.CountAsync());
                Assert.Equal(1, postedAuditCount);

                await using var retryContext = CreateContext(database.ConnectionString);
                var retryCommands = CreateJournalCommandSender(retryContext, audit.Object);
                var retryStore = new SettlementTransferCommandStore(
                    retryContext,
                    new SettlementPostingPreflightResolver(barrier),
                    new SettlementJournalPoster(retryCommands, tenant.Object),
                    Mock.Of<ISettlementJournalReverser>());
                var retry = await retryStore.PostSettlementAsync(
                    1,
                    101,
                    new SettlementPostingInput(
                        "card",
                        103,
                        501,
                        new DateTime(2026, 9, 6),
                        20m,
                        0m,
                        null,
                        "concurrent-settlement"),
                    CancellationToken.None);
                Assert.Equal(batch.Id, retry.Id);
                Assert.Equal(1, postedAuditCount);
            });
    }

    [Fact]
    public async Task ConcurrentSameKeyTransferAfterBothPreflightReadsProducesOneTransferAndJournal()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.SettlementPosting,
            PostgresTestDatabaseStrategy.FreshMigration,
            async database =>
            {
                await using (var setup = CreateContext(database.ConnectionString))
                {
                    await setup.Database.EnsureCreatedAsync();
                    Seed(setup);
                    await setup.SaveChangesAsync();
                }

                var barrier = new SettlementPostingPreflightBarrier();
                var tenant = new Mock<ITenantAccessService>();
                tenant.Setup(access => access.RequireBusinessId()).Returns(1);
                var audit = new Mock<IAuditService>();
                var postedAuditCount = 0;
                audit.Setup(service => service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<AuditEventInput, CancellationToken>((input, _) =>
                    {
                        if (input.Summary.StartsWith("Journal posted:", StringComparison.Ordinal))
                            Interlocked.Increment(ref postedAuditCount);
                    })
                    .Returns(Task.CompletedTask);

                async Task<Exception?> PostAsync()
                {
                    await using var context = CreateContext(database.ConnectionString);
                    var journalCommands = CreateJournalCommandSender(context, audit.Object);
                    var store = new SettlementTransferCommandStore(
                        context,
                        new TransferPostingPreflightResolver(barrier),
                        new SettlementJournalPoster(journalCommands, tenant.Object),
                        Mock.Of<ISettlementJournalReverser>());

                    try
                    {
                        await store.PostTransferAsync(
                            1,
                            101,
                            new LiquidityTransferPostingInput(
                                LiquidityTransferTypes.CashToBank,
                                null,
                                501,
                                15m,
                                new DateTime(2026, 9, 6),
                                "concurrent transfer",
                                "concurrent-transfer"),
                            CancellationToken.None);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }

                var outcomes = await Task.WhenAll(PostAsync(), PostAsync());
                Assert.Single(outcomes, outcome => outcome is null);
                var duplicateFailure = Assert.IsType<DbUpdateException>(
                    Assert.Single(outcomes, outcome => outcome is not null));
                var postgresFailure = Assert.IsType<PostgresException>(duplicateFailure.InnerException);
                Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresFailure.SqlState);

                await using var verify = CreateContext(database.ConnectionString);
                var transfer = await verify.LiquidityTransfers
                    .SingleAsync(value => value.BusinessId == 1
                        && value.IdempotencyKey == "concurrent-transfer");
                Assert.Equal(LiquidityTransferStatuses.Posted, transfer.Status);
                Assert.NotNull(transfer.JournalEntryId);
                var journal = await verify.JournalEntries
                    .SingleAsync(value => value.BusinessId == 1
                        && value.ReferenceType == "LiquidityTransfer"
                        && value.ReferenceId == transfer.Id.ToString()
                        && value.Status == JournalEntryStatuses.Posted);
                Assert.Equal(2, await verify.JournalEntryLines
                    .CountAsync(line => line.JournalEntryId == journal.Id));
                Assert.Equal(3, await verify.JournalEntryLines.CountAsync());
                Assert.Equal(1, postedAuditCount);

                await using var retryContext = CreateContext(database.ConnectionString);
                var retryCommands = CreateJournalCommandSender(retryContext, audit.Object);
                var retryStore = new SettlementTransferCommandStore(
                    retryContext,
                    new TransferPostingPreflightResolver(barrier),
                    new SettlementJournalPoster(retryCommands, tenant.Object),
                    Mock.Of<ISettlementJournalReverser>());
                var retry = await retryStore.PostTransferAsync(
                    1,
                    101,
                    new LiquidityTransferPostingInput(
                        LiquidityTransferTypes.CashToBank,
                        null,
                        501,
                        15m,
                        new DateTime(2026, 9, 6),
                        "concurrent transfer",
                        "concurrent-transfer"),
                    CancellationToken.None);
                Assert.Equal(transfer.Id, retry.Id);
                Assert.Equal(1, postedAuditCount);
            });
    }

    [Fact]
    public async Task ConcurrentDifferentKeysCannotSettleTheSameClearingBalanceTwice()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.SettlementPosting,
            PostgresTestDatabaseStrategy.FreshMigration,
            async database =>
            {
                await using (var setup = CreateContext(database.ConnectionString))
                {
                    await setup.Database.EnsureCreatedAsync();
                    Seed(setup);
                    await setup.SaveChangesAsync();
                }

                var readBarrier = new SettlementEligibilityReadBarrier();
                var tenant = new Mock<ITenantAccessService>();
                tenant.Setup(access => access.RequireBusinessId()).Returns(1);
                var audit = Mock.Of<IAuditService>(
                    service => service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()) == Task.CompletedTask);

                async Task<Exception?> PostAsync(string key)
                {
                    await using var context = CreateContext(database.ConnectionString, readBarrier);
                    var journalCommands = CreateJournalCommandSender(context, audit);
                    var store = new SettlementTransferCommandStore(
                        context,
                        new FixedSettlementPostingResolver(),
                        new SettlementJournalPoster(journalCommands, tenant.Object),
                        Mock.Of<ISettlementJournalReverser>());

                    try
                    {
                        await store.PostSettlementAsync(
                            1,
                            101,
                            new SettlementPostingInput(
                                "card",
                                103,
                                501,
                                new DateTime(2026, 9, 6),
                                60m,
                                0m,
                                null,
                                key),
                            CancellationToken.None);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }

                var outcomes = await Task.WhenAll(
                    PostAsync("concurrent-settlement-a"),
                    PostAsync("concurrent-settlement-b"));
                Assert.Single(outcomes, outcome => outcome is null);
                var rejected = Assert.Single(outcomes, outcome => outcome is not null)!;
                Assert.IsType<PostgresException>(rejected.GetBaseException());
                Assert.Equal(
                    PostgresErrorCodes.SerializationFailure,
                    ((PostgresException)rejected.GetBaseException()).SqlState);

                await using var verify = CreateContext(database.ConnectionString);
                Assert.Single(await verify.SettlementBatches
                    .Where(batch => batch.BusinessId == 1
                        && batch.Status == SettlementBatchStatuses.Posted)
                    .ToListAsync());
                var remainingClearingBalance = await verify.JournalEntryLines
                    .Where(line => line.JournalEntry.BusinessId == 1
                        && line.JournalEntry.Status == JournalEntryStatuses.Posted
                        && line.AccountId == 103)
                    .SumAsync(line => line.DebitAmount - line.CreditAmount);
                Assert.Equal(40m, remainingClearingBalance);
            });
    }

    [Fact]
    public async Task ConcurrentSettlementReversalProducesOneLinkedReversal()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.SettlementPosting,
            PostgresTestDatabaseStrategy.FreshMigration,
            async database =>
            {
                await using (var setup = CreateContext(database.ConnectionString))
                {
                    await setup.Database.EnsureCreatedAsync();
                    Seed(setup);
                    SeedPostedSettlement(setup);
                    await setup.SaveChangesAsync();
                }

                var barrier = new SettlementReversalPreflightBarrier();
                var tenant = new Mock<ITenantAccessService>();
                tenant.Setup(access => access.RequireBusinessId()).Returns(1);
                var audit = new Mock<IAuditService>();
                var reversalAuditCount = 0;
                audit.Setup(service => service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<AuditEventInput, CancellationToken>((input, _) =>
                    {
                        if (input.Summary == "Journal reversed: 601")
                            Interlocked.Increment(ref reversalAuditCount);
                    })
                    .Returns(Task.CompletedTask);

                async Task<Exception?> ReverseAsync()
                {
                    await using var context = CreateContext(database.ConnectionString);
                    var journalCommands = CreateJournalCommandSender(context, audit.Object);
                    var store = new SettlementTransferCommandStore(
                        context,
                        new FixedSettlementPostingResolver(),
                        Mock.Of<ISettlementJournalPoster>(),
                        new SettlementReversalPreflightBarrierReverser(
                            barrier,
                            new SettlementJournalReverser(journalCommands, tenant.Object)));

                    try
                    {
                        await store.ReverseSettlementAsync(
                            1,
                            101,
                            2001,
                            new SettlementReversalInput(
                                "concurrent correction",
                                new DateTime(2026, 9, 15)),
                            CancellationToken.None);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }

                var outcomes = await Task.WhenAll(ReverseAsync(), ReverseAsync());
                Assert.Single(outcomes, outcome => outcome is null);
                Assert.IsType<InvalidOperationException>(
                    Assert.Single(outcomes, outcome => outcome is not null));

                await using var verify = CreateContext(database.ConnectionString);
                var settlement = await verify.SettlementBatches.SingleAsync(batch => batch.Id == 2001);
                Assert.Equal(SettlementBatchStatuses.Reversed, settlement.Status);
                Assert.NotNull(settlement.ReversalJournalEntryId);
                var reversal = await verify.JournalEntries.SingleAsync(
                    journal => journal.ReversalOfJournalEntryId == settlement.JournalEntryId);
                Assert.Equal(JournalEntryStatuses.Posted, reversal.Status);
                Assert.Equal(settlement.ReversalJournalEntryId, reversal.Id);
                Assert.Equal(1, reversalAuditCount);
            });
    }

    [Fact]
    public async Task ConcurrentLiquidityTransferReversalProducesOneLinkedReversal()
    {
        await PostgresTestDatabase.RunAsync(
            ResolveBaseConnectionString(),
            PostgresTestDatabasePrefix.SettlementPosting,
            PostgresTestDatabaseStrategy.FreshMigration,
            async database =>
            {
                await using (var setup = CreateContext(database.ConnectionString))
                {
                    await setup.Database.EnsureCreatedAsync();
                    Seed(setup);
                    SeedPostedTransfer(setup);
                    await setup.SaveChangesAsync();
                }

                var barrier = new SettlementReversalPreflightBarrier();
                var tenant = new Mock<ITenantAccessService>();
                tenant.Setup(access => access.RequireBusinessId()).Returns(1);
                var audit = new Mock<IAuditService>();
                var reversalAuditCount = 0;
                audit.Setup(service => service.RecordAsync(
                        It.IsAny<AuditEventInput>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<AuditEventInput, CancellationToken>((input, _) =>
                    {
                        if (input.Summary == "Journal reversed: 602")
                            Interlocked.Increment(ref reversalAuditCount);
                    })
                    .Returns(Task.CompletedTask);

                async Task<Exception?> ReverseAsync()
                {
                    await using var context = CreateContext(database.ConnectionString);
                    var journalCommands = CreateJournalCommandSender(context, audit.Object);
                    var store = new SettlementTransferCommandStore(
                        context,
                        new FixedSettlementPostingResolver(),
                        Mock.Of<ISettlementJournalPoster>(),
                        new SettlementReversalPreflightBarrierReverser(
                            barrier,
                            new SettlementJournalReverser(journalCommands, tenant.Object)));

                    try
                    {
                        await store.ReverseTransferAsync(
                            1,
                            101,
                            2002,
                            new LiquidityTransferReversalInput(
                                "concurrent transfer correction",
                                new DateTime(2026, 9, 15)),
                            CancellationToken.None);
                        return null;
                    }
                    catch (Exception exception)
                    {
                        return exception;
                    }
                }

                var outcomes = await Task.WhenAll(ReverseAsync(), ReverseAsync());
                Assert.Single(outcomes, outcome => outcome is null);
                Assert.IsType<InvalidOperationException>(
                    Assert.Single(outcomes, outcome => outcome is not null));

                await using var verify = CreateContext(database.ConnectionString);
                var transfer = await verify.LiquidityTransfers.SingleAsync(value => value.Id == 2002);
                Assert.Equal(LiquidityTransferStatuses.Reversed, transfer.Status);
                Assert.NotNull(transfer.ReversalJournalEntryId);
                var reversal = await verify.JournalEntries.SingleAsync(
                    journal => journal.ReversalOfJournalEntryId == transfer.JournalEntryId);
                Assert.Equal(JournalEntryStatuses.Posted, reversal.Status);
                Assert.Equal(transfer.ReversalJournalEntryId, reversal.Id);
                Assert.Equal(1, reversalAuditCount);
            });
    }

    private static RetailPOSDbContext CreateContext(
        string connectionString,
        DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseNpgsql(connectionString);
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        return new RetailPOSDbContext(options.Options);
    }

    private static MediatR.ISender CreateJournalCommandSender(
        RetailPOSDbContext context,
        IAuditService audit)
    {
        var journalStore = new ManualJournalDraftStore(
            context,
            audit,
            new JournalNumberAllocator(context),
            NullLogger<ManualJournalDraftStore>.Instance);
        return SupplierPaymentCreationTestHarness.CreateJournalCommandSender(journalStore);
    }

    private static void Seed(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Settlement concurrency" });
        db.ChartAccounts.AddRange(
            Account(101, "Cash", ChartAccountTypes.Asset),
            Account(102, "Bank", ChartAccountTypes.Asset),
            Account(103, "Card clearing", ChartAccountTypes.Asset));
        db.BankAccounts.Add(new BankAccount
        {
            Id = 501,
            BusinessId = 1,
            ChartAccountId = 102,
            DisplayName = "Settlement bank"
        });
        db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            CashAccountId = 101,
            DefaultBankAccountId = 102,
            CardClearingAccountId = 103,
            JournalNumberPrefix = "JR",
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear
        });
        db.FinancialYears.Add(new FinancialYear
        {
            Id = 1,
            BusinessId = 1,
            YearCode = "FY26",
            Name = "FY26",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31),
            IsCurrent = true
        });
        db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 1,
            FinancialYearId = 1,
            PeriodNumber = 9,
            Name = "September",
            StartDate = new DateTime(2026, 9, 1),
            EndDate = new DateTime(2026, 9, 30),
            Status = AccountingPeriodStatuses.Open
        });
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 600,
            BusinessId = 1,
            JournalType = "SalePosting",
            TransactionDate = new DateTime(2026, 9, 5),
            PostingDate = new DateTime(2026, 9, 5),
            BusinessDate = new DateTime(2026, 9, 5),
            Status = JournalEntryStatuses.Posted,
            ReferenceType = "Sale",
            ReferenceId = "1",
            JournalNumber = "JR-SEED",
            Lines = [new JournalEntryLine { AccountId = 103, DebitAmount = 100m }]
        });
    }

    private static void SeedPostedSettlement(RetailPOSDbContext db)
    {
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 601,
            BusinessId = 1,
            JournalType = "BankSettlement",
            TransactionDate = new DateTime(2026, 9, 6),
            PostingDate = new DateTime(2026, 9, 6),
            BusinessDate = new DateTime(2026, 9, 6),
            FinancialYearId = 1,
            AccountingPeriodId = 1,
            Status = JournalEntryStatuses.Posted,
            IsSystemGenerated = true,
            ReferenceType = "SettlementBatch",
            ReferenceId = "2001",
            JournalNumber = "JR-SETTLEMENT",
            Lines =
            [
                new JournalEntryLine { AccountId = 102, DebitAmount = 20m },
                new JournalEntryLine { AccountId = 103, CreditAmount = 20m }
            ]
        });
        db.SettlementBatches.Add(new SettlementBatch
        {
            Id = 2001,
            BusinessId = 1,
            SettlementType = "card",
            ClearingAccountId = 103,
            BankAccountId = 501,
            SettlementDate = new DateTime(2026, 9, 6),
            GrossClearingAmount = 20m,
            NetBankAmount = 20m,
            IdempotencyKey = "settlement-reversal-race",
            Status = SettlementBatchStatuses.Posted,
            JournalEntryId = 601,
            CreatedAt = new DateTime(2026, 9, 6),
            CreatedBy = 101,
            UpdatedAt = new DateTime(2026, 9, 6),
            UpdatedBy = 101
        });
    }

    private static void SeedPostedTransfer(RetailPOSDbContext db)
    {
        db.JournalEntries.Add(new JournalEntry
        {
            Id = 602,
            BusinessId = 1,
            JournalType = "LiquidityTransfer",
            TransactionDate = new DateTime(2026, 9, 6),
            PostingDate = new DateTime(2026, 9, 6),
            BusinessDate = new DateTime(2026, 9, 6),
            FinancialYearId = 1,
            AccountingPeriodId = 1,
            Status = JournalEntryStatuses.Posted,
            IsSystemGenerated = true,
            ReferenceType = "LiquidityTransfer",
            ReferenceId = "2002",
            JournalNumber = "JR-TRANSFER",
            Lines =
            [
                new JournalEntryLine { AccountId = 102, DebitAmount = 20m },
                new JournalEntryLine { AccountId = 101, CreditAmount = 20m }
            ]
        });
        db.LiquidityTransfers.Add(new LiquidityTransfer
        {
            Id = 2002,
            BusinessId = 1,
            TransferType = LiquidityTransferTypes.CashToBank,
            SourceAccountId = 101,
            DestinationAccountId = 102,
            Amount = 20m,
            PostingDate = new DateTime(2026, 9, 6),
            IdempotencyKey = "transfer-reversal-race",
            Status = LiquidityTransferStatuses.Posted,
            JournalEntryId = 602,
            CreatedAt = new DateTime(2026, 9, 6),
            CreatedBy = 101,
            UpdatedAt = new DateTime(2026, 9, 6),
            UpdatedBy = 101
        });
    }

    private static ChartAccount Account(long id, string name, string type) =>
        new()
        {
            Id = id,
            BusinessId = 1,
            AccountCode = id.ToString(),
            AccountName = name,
            AccountType = type,
            NormalBalanceType = NormalBalanceTypes.Debit,
            IsActive = true,
            AllowManualPosting = true
        };

    private static string ResolveBaseConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("RETAILPOS_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var settingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "RetailPOS.API", "appsettings.json"));
        return new ConfigurationBuilder()
            .AddJsonFile(settingsPath, optional: false)
            .Build()
            .GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "DefaultConnection is not configured for PostgreSQL integration tests.");
    }

    private sealed class SettlementPostingPreflightBarrier
    {
        private readonly TaskCompletionSource _bothArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _bothArrived.TrySetResult();
            await _bothArrived.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class SettlementPostingPreflightResolver(
        SettlementPostingPreflightBarrier barrier) : ISettlementPostingAccountResolver
    {
        public async Task<long> ResolveAsync(
            long businessId,
            string slot,
            CancellationToken cancellationToken)
        {
            if (slot == "card_clearing")
                await barrier.WaitAsync(cancellationToken);
            return slot switch
            {
                "card_clearing" => 103,
                "settlement_fee_expense" => throw new InvalidOperationException(
                    "No fee account is expected for this test."),
                _ => throw new InvalidOperationException($"Unexpected posting slot '{slot}'.")
            };
        }
    }

    private sealed class TransferPostingPreflightResolver(
        SettlementPostingPreflightBarrier barrier) : ISettlementPostingAccountResolver
    {
        public async Task<long> ResolveAsync(
            long businessId,
            string slot,
            CancellationToken cancellationToken)
        {
            if (slot == "cash")
                await barrier.WaitAsync(cancellationToken);
            return slot == "cash"
                ? 101
                : throw new InvalidOperationException($"Unexpected posting slot '{slot}'.");
        }
    }

    private sealed class FixedSettlementPostingResolver : ISettlementPostingAccountResolver
    {
        public Task<long> ResolveAsync(
            long businessId,
            string slot,
            CancellationToken cancellationToken) =>
            Task.FromResult(slot switch
            {
                "card_clearing" => 103L,
                _ => throw new InvalidOperationException($"Unexpected posting slot '{slot}'.")
            });
    }

    private sealed class SettlementEligibilityReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothReadersReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("SUM(", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("journal_entry_lines", StringComparison.OrdinalIgnoreCase))
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                    _bothReadersReady.TrySetResult();
                await _bothReadersReady.Task.WaitAsync(cancellationToken);
            }

            return await base.ReaderExecutedAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }
    }

    private sealed class SettlementReversalPreflightBarrier
    {
        private readonly TaskCompletionSource _bothArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrivals) == 2)
                _bothArrived.TrySetResult();
            await _bothArrived.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class SettlementReversalPreflightBarrierReverser(
        SettlementReversalPreflightBarrier barrier,
        ISettlementJournalReverser inner) : ISettlementJournalReverser
    {
        public async Task<long> ReverseAsync(
            long businessId,
            long actorUserId,
            long journalEntryId,
            SettlementJournalReversalInput input,
            CancellationToken cancellationToken)
        {
            await barrier.WaitAsync(cancellationToken);
            return await inner.ReverseAsync(
                businessId,
                actorUserId,
                journalEntryId,
                input,
                cancellationToken);
        }
    }
}
