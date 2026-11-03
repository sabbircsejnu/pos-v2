using RetailPOS.Application.AccountingJournals.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingSettlements.Abstractions;
using RetailPOS.Application.AccountingSettlements.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.AccountingSettlements;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Accounting;

public sealed class P306SettlementTransferRelationalAtomicityTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ThrowAfterJournalPersistenceInterceptor _interceptor = new();

    public P306SettlementTransferRelationalAtomicityTests()
    {
        _connection.Open();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA ignore_check_constraints = 1;";
            command.ExecuteNonQuery();
        }
        using var context = CreateContext();
        context.Database.EnsureCreated();
        Seed(context);
    }

    [Fact]
    public async Task SettlementFailureRollsBackBatchJournalAndLines_ThenRetryPostsExactlyOnce()
    {
        _interceptor.FailSettlementCompletion = true;
        await using (var failed = CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostSettlementAsync(CreateSettlementStore(failed), Settlement("settle-atomic", 20, 0)));
        }

        await using (var verify = CreateContext())
        {
            Assert.Empty(await verify.SettlementBatches.ToListAsync());
            Assert.Empty(await verify.JournalEntries.Where(x => x.ReferenceType == "SettlementBatch").ToListAsync());
            Assert.Single(await verify.JournalEntryLines.ToListAsync());
        }

        _interceptor.FailSettlementCompletion = false;
        await using (var retry = CreateContext())
        {
            var input = Settlement("settle-atomic", 20, 0);
            var result = await PostSettlementAsync(
                CreateSettlementStore(retry),
                input);
            var duplicate = await PostSettlementAsync(CreateSettlementStore(retry), input);
            Assert.Equal(SettlementBatchStatuses.Posted, result.Status);
            Assert.Equal(result.Id, duplicate.Id);
        }

        await using var final = CreateContext();
        Assert.Single(await final.SettlementBatches.Where(x => x.IdempotencyKey == "settle-atomic" && x.Status == SettlementBatchStatuses.Posted).ToListAsync());
        Assert.Single(await final.JournalEntries.Where(x => x.ReferenceType == "SettlementBatch" && x.Status == JournalEntryStatuses.Posted).ToListAsync());
        Assert.Equal(3, await final.JournalEntryLines.CountAsync());
    }

    [Fact]
    public async Task TransferFailureRollsBackTransferJournalAndLines_ThenRetryPostsExactlyOnce()
    {
        _interceptor.FailTransferCompletion = true;
        await using (var failed = CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PostTransferAsync(
                    CreateSettlementStore(failed),
                    new CreateLiquidityTransferDto { TransferType = LiquidityTransferTypes.CashToBank, DestinationBankAccountId = 501, Amount = 15, PostingDate = new DateTime(2026, 9, 6), IdempotencyKey = "transfer-atomic" }));
        }

        await using (var verify = CreateContext())
        {
            Assert.Empty(await verify.LiquidityTransfers.ToListAsync());
            Assert.Empty(await verify.JournalEntries.Where(x => x.ReferenceType == "LiquidityTransfer").ToListAsync());
            Assert.Single(await verify.JournalEntryLines.ToListAsync());
        }

        _interceptor.FailTransferCompletion = false;
        await using (var retry = CreateContext())
        {
            var result = await PostTransferAsync(
                CreateSettlementStore(retry),
                new CreateLiquidityTransferDto { TransferType = LiquidityTransferTypes.CashToBank, DestinationBankAccountId = 501, Amount = 15, PostingDate = new DateTime(2026, 9, 6), IdempotencyKey = "transfer-atomic" });
            Assert.Equal(LiquidityTransferStatuses.Posted, result.Status);
        }

        await using var final = CreateContext();
        Assert.Single(await final.LiquidityTransfers.Where(x => x.IdempotencyKey == "transfer-atomic" && x.Status == LiquidityTransferStatuses.Posted).ToListAsync());
        Assert.Single(await final.JournalEntries.Where(x => x.ReferenceType == "LiquidityTransfer" && x.Status == JournalEntryStatuses.Posted).ToListAsync());
        Assert.Equal(3, await final.JournalEntryLines.CountAsync());
    }

    [Fact]
    public async Task SettlementReversal_LaterOpenPeriodRestoresEligibility_AndRepeatIsIdempotent()
    {
        await using var context = CreateContext();
        var posted = await PostSettlementAsync(
            CreateSettlementStore(context),
            Settlement("settle-reverse-open", 20, 0));
        var store = CreateSettlementStore(context);
        var reversed = await ReverseSettlementAsync(store, posted.Id, new ReverseSettlementBatchDto { Reason = "later correction", PostingDate = new DateTime(2026, 10, 1) });
        var retry = await ReverseSettlementAsync(store, posted.Id, new ReverseSettlementBatchDto { Reason = "retry", PostingDate = new DateTime(2026, 10, 1) });

        Assert.Equal(SettlementBatchStatuses.Reversed, reversed.Status);
        Assert.Equal(reversed.ReversalJournalEntryId, retry.ReversalJournalEntryId);
        Assert.Equal(100, await context.JournalEntryLines.Where(x => x.AccountId == 103).SumAsync(x => x.DebitAmount - x.CreditAmount));
        Assert.Single(await context.JournalEntries.Where(x => x.ReversalOfJournalEntryId == posted.JournalEntryId).ToListAsync());
        Assert.Single(await context.JournalEntries.Where(x => x.ReferenceType == "Sale" && x.ReferenceId == "1").ToListAsync());
    }

    [Fact]
    public async Task SettlementReversal_ClosedPeriodIsRejected_AndOriginalStateRemainsPosted()
    {
        await using var context = CreateContext();
        var posted = await PostSettlementAsync(
            CreateSettlementStore(context),
            Settlement("settle-reverse-closed", 20, 0));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReverseSettlementAsync(
                CreateSettlementStore(context),
                posted.Id,
                new ReverseSettlementBatchDto { Reason = "closed period", PostingDate = new DateTime(2026, 11, 1) }));

        await using var verify = CreateContext();
        var batch = await verify.SettlementBatches.SingleAsync(x => x.Id == posted.Id);
        Assert.Equal(SettlementBatchStatuses.Posted, batch.Status);
        Assert.Null(batch.ReversalJournalEntryId);
        Assert.Empty(await verify.JournalEntries.Where(x => x.ReversalOfJournalEntryId == posted.JournalEntryId).ToListAsync());
    }

    [Theory]
    [InlineData(LiquidityTransferTypes.CashToBank, 101L, 102L)]
    [InlineData(LiquidityTransferTypes.BankToCash, 102L, 101L)]
    [InlineData(LiquidityTransferTypes.BankToBank, 102L, 106L)]
    public async Task TransferReversal_LaterOpenPeriodPreservesOriginalDirection(string type, long sourceAccountId, long destinationAccountId)
    {
        await using var context = CreateContext();
        var posted = await PostTransferAsync(
            CreateSettlementStore(context),
            new CreateLiquidityTransferDto { TransferType = type, SourceBankAccountId = type == LiquidityTransferTypes.CashToBank ? null : 501, DestinationBankAccountId = type == LiquidityTransferTypes.BankToCash ? null : type == LiquidityTransferTypes.BankToBank ? 502 : 501, Amount = 15, PostingDate = new DateTime(2026, 9, 6), IdempotencyKey = $"transfer-reverse-{type}" });
        var reversed = await ReverseTransferAsync(
            CreateSettlementStore(context),
            posted.Id,
            new ReverseLiquidityTransferDto { Reason = "later correction", PostingDate = new DateTime(2026, 10, 1) });
        var retry = await ReverseTransferAsync(
            CreateSettlementStore(context),
            posted.Id,
            new ReverseLiquidityTransferDto { Reason = "retry", PostingDate = new DateTime(2026, 10, 1) });

        Assert.Equal(LiquidityTransferStatuses.Reversed, reversed.Status);
        Assert.Equal(reversed.ReversalJournalEntryId, retry.ReversalJournalEntryId);
        var original = await context.JournalEntries.Include(x => x.Lines).SingleAsync(x => x.Id == posted.JournalEntryId);
        var reversal = await context.JournalEntries.Include(x => x.Lines).SingleAsync(x => x.Id == reversed.ReversalJournalEntryId);
        Assert.Contains(original.Lines, x => x.AccountId == sourceAccountId && x.CreditAmount == 15);
        Assert.Contains(original.Lines, x => x.AccountId == destinationAccountId && x.DebitAmount == 15);
        Assert.Contains(reversal.Lines, x => x.AccountId == sourceAccountId && x.DebitAmount == 15);
        Assert.Contains(reversal.Lines, x => x.AccountId == destinationAccountId && x.CreditAmount == 15);
    }

    [Fact]
    public async Task TransferReversal_ClosedPeriodIsRejected_AndOriginalStateRemainsPosted()
    {
        await using var context = CreateContext();
        var posted = await PostTransferAsync(
            CreateSettlementStore(context),
            new CreateLiquidityTransferDto { TransferType = LiquidityTransferTypes.BankToCash, SourceBankAccountId = 501, Amount = 15, PostingDate = new DateTime(2026, 9, 6), IdempotencyKey = "transfer-reverse-closed" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReverseTransferAsync(
                CreateSettlementStore(context),
                posted.Id,
                new ReverseLiquidityTransferDto { Reason = "closed period", PostingDate = new DateTime(2026, 11, 1) }));

        await using var verify = CreateContext();
        var transfer = await verify.LiquidityTransfers.SingleAsync(x => x.Id == posted.Id);
        Assert.Equal(LiquidityTransferStatuses.Posted, transfer.Status);
        Assert.Null(transfer.ReversalJournalEntryId);
        Assert.Empty(await verify.JournalEntries.Where(x => x.ReversalOfJournalEntryId == posted.JournalEntryId).ToListAsync());
    }

    private SettlementTransferCommandStore CreateSettlementStore(RetailPOSDbContext context)
    {
        var tenant = new Mock<ITenantAccessService>();
        tenant.Setup(x => x.RequireBusinessId()).Returns(1);
        tenant.Setup(x => x.EffectiveBusinessId).Returns(1);
        tenant.Setup(x => x.IsSuperAdmin).Returns(false);
        var configuration = AccountingConfigurationServiceTestFactory.Create(
            context,
            tenant.Object,
            NullLogger<AccountingConfigurationService>.Instance);
        var numbers = new Mock<IJournalNumberAllocator>();
        numbers.Setup(x => x.AllocateNextJournalNumberAsync(
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<DateTime>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"JR-{Guid.NewGuid():N}");
        var audit = Mock.Of<IAuditService>(
            x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>())
                == Task.CompletedTask);
        var journalStore = new ManualJournalDraftStore(
            context,
            audit,
            numbers.Object,
            NullLogger<ManualJournalDraftStore>.Instance);
        var journalCommands = SupplierPaymentCreationTestHarness.CreateJournalCommandSender(journalStore);
        return new SettlementTransferCommandStore(
            context,
            new SettlementPostingAccountResolver(configuration, tenant.Object),
            new SettlementJournalPoster(journalCommands, tenant.Object),
            new SettlementJournalReverser(journalCommands, tenant.Object));
    }

    private static Task<SettlementBatchReadModel> PostSettlementAsync(
        SettlementTransferCommandStore store,
        CreateSettlementBatchDto dto) =>
        store.PostSettlementAsync(
            1,
            9,
            new SettlementPostingInput(
                dto.SettlementType,
                dto.ClearingAccountId,
                dto.BankAccountId,
                dto.SettlementDate,
                dto.GrossClearingAmount,
                dto.SettlementFeeAmount,
                dto.ExternalReference,
                dto.IdempotencyKey),
            CancellationToken.None);

    private static Task<SettlementBatchReadModel> ReverseSettlementAsync(
        SettlementTransferCommandStore store,
        long id,
        ReverseSettlementBatchDto dto) =>
        store.ReverseSettlementAsync(
            1,
            9,
            id,
            new SettlementReversalInput(dto.Reason, dto.PostingDate),
            CancellationToken.None);

    private static Task<LiquidityTransferReadModel> PostTransferAsync(
        SettlementTransferCommandStore store,
        CreateLiquidityTransferDto dto) =>
        store.PostTransferAsync(
            1,
            9,
            new LiquidityTransferPostingInput(
                dto.TransferType,
                dto.SourceBankAccountId,
                dto.DestinationBankAccountId,
                dto.Amount,
                dto.PostingDate,
                dto.Reference,
                dto.IdempotencyKey),
            CancellationToken.None);

    private static Task<LiquidityTransferReadModel> ReverseTransferAsync(
        SettlementTransferCommandStore store,
        long id,
        ReverseLiquidityTransferDto dto) =>
        store.ReverseTransferAsync(
            1,
            9,
            id,
            new LiquidityTransferReversalInput(dto.Reason, dto.PostingDate),
            CancellationToken.None);

    private RetailPOSDbContext CreateContext() => new(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(_connection).AddInterceptors(_interceptor).Options);
    private static CreateSettlementBatchDto Settlement(string key, decimal gross, decimal fee) => new() { SettlementType = "card", ClearingAccountId = 103, BankAccountId = 501, SettlementDate = new DateTime(2026, 9, 6), GrossClearingAmount = gross, SettlementFeeAmount = fee, IdempotencyKey = key };

    private static void Seed(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Atomicity Business" });
        db.ChartAccounts.AddRange(Account(101, "Cash", ChartAccountTypes.Asset), Account(102, "Bank", ChartAccountTypes.Asset), Account(103, "Card Clearing", ChartAccountTypes.Asset), Account(104, "Mobile Clearing", ChartAccountTypes.Asset), Account(105, "Settlement Fee", ChartAccountTypes.Expense), Account(106, "Second Bank", ChartAccountTypes.Asset));
        db.BankAccounts.AddRange(new BankAccount { Id = 501, BusinessId = 1, ChartAccountId = 102, DisplayName = "Main Bank" }, new BankAccount { Id = 502, BusinessId = 1, ChartAccountId = 106, DisplayName = "Second Bank" });
        db.AccountingConfigurations.Add(new AccountingConfiguration { BusinessId = 1, CashAccountId = 101, DefaultBankAccountId = 102, CardClearingAccountId = 103, MobileMfsClearingAccountId = 104, SettlementFeeExpenseAccountId = 105, JournalNumberPrefix = "JR", JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear });
        db.FinancialYears.Add(new FinancialYear { Id = 1, BusinessId = 1, YearCode = "FY26", Name = "FY26", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true });
        db.AccountingPeriods.Add(new AccountingPeriod { Id = 1, FinancialYearId = 1, PeriodNumber = 9, Name = "September", StartDate = new DateTime(2026, 9, 1), EndDate = new DateTime(2026, 9, 30), Status = AccountingPeriodStatuses.Open });
        db.AccountingPeriods.Add(new AccountingPeriod { Id = 2, FinancialYearId = 1, PeriodNumber = 10, Name = "October", StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 10, 31), Status = AccountingPeriodStatuses.Open });
        db.AccountingPeriods.Add(new AccountingPeriod { Id = 3, FinancialYearId = 1, PeriodNumber = 11, Name = "November", StartDate = new DateTime(2026, 11, 1), EndDate = new DateTime(2026, 11, 30), Status = AccountingPeriodStatuses.Closed, ClosedAt = DateTime.UtcNow });
        db.JournalEntries.Add(new JournalEntry { Id = 600, BusinessId = 1, JournalType = "SalePosting", TransactionDate = new DateTime(2026, 9, 5), PostingDate = new DateTime(2026, 9, 5), BusinessDate = new DateTime(2026, 9, 5), Status = JournalEntryStatuses.Posted, ReferenceType = "Sale", ReferenceId = "1", JournalNumber = "JR-SEED", Lines = [new JournalEntryLine { AccountId = 103, DebitAmount = 100, CreditAmount = 0 }] });
        db.SaveChanges();
    }

    private static ChartAccount Account(long id, string name, string type) => new() { Id = id, BusinessId = 1, AccountCode = id.ToString(), AccountName = name, AccountType = type, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true };

    public void Dispose() => _connection.Dispose();

    private sealed class ThrowAfterJournalPersistenceInterceptor : SaveChangesInterceptor
    {
        public bool FailSettlementCompletion { get; set; }
        public bool FailTransferCompletion { get; set; }
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            var context = eventData.Context;
            if (context != null && ((FailSettlementCompletion && context.ChangeTracker.Entries<SettlementBatch>().Any(x => x.Entity.Status == SettlementBatchStatuses.Posted && x.State == EntityState.Modified)) || (FailTransferCompletion && context.ChangeTracker.Entries<LiquidityTransfer>().Any(x => x.Entity.Status == LiquidityTransferStatuses.Posted && x.State == EntityState.Modified))))
                throw new InvalidOperationException("Injected operational completion failure.");
            return result;
        }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => new(SavingChanges(eventData, result));
    }
}
