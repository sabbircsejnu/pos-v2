using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MediatR;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.AccountingSettlements.Abstractions;
using RetailPOS.Application.AccountingSettlements.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.AccountingSettlements;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Accounting;

public sealed class P306SettlementTransferTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly Mock<ITenantAccessService> _tenant = new();
    private readonly Mock<ISender> _journalCommands = new();
    private readonly AccountingConfigurationService _configuration;
    private readonly List<CreateOrGetSystemJournalDraftCommand> _journalDrafts = [];

    public P306SettlementTransferTests()
    {
        _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        _tenant.Setup(x => x.RequireBusinessId()).Returns(1);
        _tenant.Setup(x => x.EffectiveBusinessId).Returns(1);
        _tenant.Setup(x => x.IsSuperAdmin).Returns(false);
        _db.Businesses.Add(new Business { Id = 1, Name = "Settlement Business" });
        _db.ChartAccounts.AddRange(Account(101, "Cash", ChartAccountTypes.Asset), Account(102, "Bank", ChartAccountTypes.Asset), Account(103, "Card Clearing", ChartAccountTypes.Asset), Account(104, "Mobile Clearing", ChartAccountTypes.Asset), Account(105, "Settlement Fee", ChartAccountTypes.Expense));
        _db.BankAccounts.Add(new BankAccount { Id = 501, BusinessId = 1, ChartAccountId = 102, DisplayName = "Main Bank" });
        _db.AccountingConfigurations.Add(new AccountingConfiguration { BusinessId = 1, CashAccountId = 101, DefaultBankAccountId = 102, CardClearingAccountId = 103, MobileMfsClearingAccountId = 104, SettlementFeeExpenseAccountId = 105 });
        _db.JournalEntries.AddRange(new JournalEntry { Id = 601, BusinessId = 1, Status = JournalEntryStatuses.Posted, JournalType = "SalePosting", PostingDate = DateTime.UtcNow, TransactionDate = DateTime.UtcNow, BusinessDate = DateTime.UtcNow.Date, Lines = [new JournalEntryLine { AccountId = 103, DebitAmount = 100, CreditAmount = 0 }] }, new JournalEntry { Id = 602, BusinessId = 1, Status = JournalEntryStatuses.Posted, JournalType = "SalePosting", PostingDate = DateTime.UtcNow, TransactionDate = DateTime.UtcNow, BusinessDate = DateTime.UtcNow.Date, Lines = [new JournalEntryLine { AccountId = 104, DebitAmount = 100, CreditAmount = 0 }] });
        _db.SaveChanges();
        _configuration = AccountingConfigurationServiceTestFactory.Create(_db, _tenant.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<AccountingConfigurationService>.Instance);
        _journalCommands.Setup(x => x.Send(It.IsAny<CreateOrGetSystemJournalDraftCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<long>, CancellationToken>((request, _) =>
                _journalDrafts.Add(Assert.IsType<CreateOrGetSystemJournalDraftCommand>(request)))
            .ReturnsAsync(700);
        _journalCommands.Setup(x => x.Send(It.IsAny<PostJournalEntryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(700);
        _journalCommands.Setup(x => x.Send(It.IsAny<ReverseJournalEntryCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(701);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CardSettlementWithoutFee_PostsBankDebitAndClearingCredit()
    {
        var result = await PostSettlementAsync(new CreateSettlementBatchDto { SettlementType = "card", ClearingAccountId = 103, BankAccountId = 501, SettlementDate = DateTime.UtcNow, GrossClearingAmount = 40, IdempotencyKey = "card-1" });
        var lines = _journalDrafts.Single().Journal.Lines;
        Assert.Equal(SettlementBatchStatuses.Posted, result.Status);
        Assert.Equal(40, result.NetBankAmount);
        Assert.Contains(lines, x => x.AccountId == 102 && x.DebitAmount == 40);
        Assert.Contains(lines, x => x.AccountId == 103 && x.CreditAmount == 40);
        Assert.DoesNotContain(lines, x => x.AccountId is 4000 or 5000);
    }

    [Fact]
    public async Task MobileSettlementWithFee_BalancesGrossNetAndFee()
    {
        var result = await PostSettlementAsync(new CreateSettlementBatchDto { SettlementType = "mobile", ClearingAccountId = 104, BankAccountId = 501, SettlementDate = DateTime.UtcNow, GrossClearingAmount = 40, SettlementFeeAmount = 2, IdempotencyKey = "mobile-1" });
        Assert.Equal(38, result.NetBankAmount);
        Assert.Contains(_journalDrafts.Single().Journal.Lines, x => x.AccountId == 102 && x.DebitAmount == 38);
        Assert.Contains(_journalDrafts.Single().Journal.Lines, x => x.AccountId == 105 && x.DebitAmount == 2);
        Assert.Contains(_journalDrafts.Single().Journal.Lines, x => x.AccountId == 104 && x.CreditAmount == 40);
    }

    [Fact]
    public async Task SettlementRejectsInsufficientClearingAndDuplicateIdempotencyKey()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => PostSettlementAsync(new CreateSettlementBatchDto { SettlementType = "card", ClearingAccountId = 103, BankAccountId = 501, SettlementDate = DateTime.UtcNow, GrossClearingAmount = 101, IdempotencyKey = "too-much" }));
        await PostSettlementAsync(new CreateSettlementBatchDto { SettlementType = "card", ClearingAccountId = 103, BankAccountId = 501, SettlementDate = DateTime.UtcNow, GrossClearingAmount = 20, IdempotencyKey = "same" });
        var retry = await PostSettlementAsync(new CreateSettlementBatchDto { SettlementType = "card", ClearingAccountId = 103, BankAccountId = 501, SettlementDate = DateTime.UtcNow, GrossClearingAmount = 20, IdempotencyKey = "same" });
        Assert.Single(await _db.SettlementBatches.ToListAsync());
        Assert.Single(_journalDrafts);
        Assert.Equal(1, retry.Id);
    }

    [Theory]
    [InlineData(LiquidityTransferTypes.CashToBank, 101, 102)]
    [InlineData(LiquidityTransferTypes.BankToCash, 102, 101)]
    [InlineData(LiquidityTransferTypes.BankToBank, 102, 103)]
    public async Task TransferPostsOnlySourceAndDestinationLines(string type, long expectedSource, long expectedDestination)
    {
        if (type == LiquidityTransferTypes.BankToBank)
        {
            _db.BankAccounts.Add(new BankAccount { Id = 502, BusinessId = 1, ChartAccountId = 103, DisplayName = "Second Bank" });
            await _db.SaveChangesAsync();
        }
        var result = await PostTransferAsync(new CreateLiquidityTransferDto { TransferType = type, SourceBankAccountId = type == LiquidityTransferTypes.CashToBank ? null : 501, DestinationBankAccountId = type == LiquidityTransferTypes.BankToCash ? null : type == LiquidityTransferTypes.BankToBank ? 502 : 501, Amount = 10, PostingDate = DateTime.UtcNow, IdempotencyKey = Guid.NewGuid().ToString() });
        Assert.Equal(LiquidityTransferStatuses.Posted, result.Status);
        Assert.Contains(_journalDrafts.Single().Journal.Lines, x => x.AccountId == expectedDestination && x.DebitAmount == 10);
        Assert.Contains(_journalDrafts.Single().Journal.Lines, x => x.AccountId == expectedSource && x.CreditAmount == 10);
    }

    [Fact]
    public async Task TransferRejectsNonPositiveAndSameAccountRequests()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => PostTransferAsync(new CreateLiquidityTransferDto { TransferType = LiquidityTransferTypes.CashToBank, DestinationBankAccountId = 501, Amount = 0, IdempotencyKey = "zero" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PostTransferAsync(new CreateLiquidityTransferDto { TransferType = LiquidityTransferTypes.BankToBank, SourceBankAccountId = 501, DestinationBankAccountId = 501, Amount = 10, IdempotencyKey = "same-account" }));
    }

    [Fact]
    public async Task SettlementReversalUsesJournalEngineAndIsIdempotent()
    {
        var created = await PostSettlementAsync(new CreateSettlementBatchDto { SettlementType = "card", ClearingAccountId = 103, BankAccountId = 501, SettlementDate = DateTime.UtcNow, GrossClearingAmount = 20, IdempotencyKey = "reverse-1" });
        var reversed = await ReverseSettlementAsync(created.Id, new ReverseSettlementBatchDto { Reason = "correction", PostingDate = DateTime.UtcNow });
        var retry = await ReverseSettlementAsync(created.Id, new ReverseSettlementBatchDto { Reason = "retry", PostingDate = DateTime.UtcNow });
        Assert.Equal(SettlementBatchStatuses.Reversed, reversed.Status);
        Assert.Equal(reversed.ReversalJournalEntryId, retry.ReversalJournalEntryId);
        _journalCommands.Verify(x => x.Send(
            It.Is<ReverseJournalEntryCommand>(command =>
                command.BusinessId == 1
                && command.ActorUserId == 9
                && command.JournalId == 700
                && command.Reversal.Reason == "correction"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private SettlementTransferCommandStore CreateSettlementStore() =>
        new(
            _db,
            new SettlementPostingAccountResolver(_configuration, _tenant.Object),
            new SettlementJournalPoster(_journalCommands.Object, _tenant.Object),
            new SettlementJournalReverser(_journalCommands.Object, _tenant.Object));

    private Task<SettlementBatchReadModel> PostSettlementAsync(CreateSettlementBatchDto dto) =>
        CreateSettlementStore().PostSettlementAsync(
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

    private Task<SettlementBatchReadModel> ReverseSettlementAsync(
        long settlementId,
        ReverseSettlementBatchDto dto) =>
        CreateSettlementStore().ReverseSettlementAsync(
            1,
            9,
            settlementId,
            new SettlementReversalInput(dto.Reason, dto.PostingDate),
            CancellationToken.None);

    private Task<LiquidityTransferReadModel> PostTransferAsync(CreateLiquidityTransferDto dto) =>
        CreateSettlementStore().PostTransferAsync(
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

    private static ChartAccount Account(long id, string name, string type) => new() { Id = id, BusinessId = 1, AccountCode = id.ToString(), AccountName = name, AccountType = type, NormalBalanceType = type == ChartAccountTypes.Expense ? NormalBalanceTypes.Debit : NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true };
}
