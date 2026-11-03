using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using MediatR;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.BankReconciliation;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Accounting;

public sealed class P306CloseAdjustmentTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly Mock<ITenantAccessService> _tenant = new();
    private readonly BankReconciliationReadStore _readStore;
    private readonly BankReconciliationSessionCommandStore _sessionStore;
    private readonly BankReconciliationMatchCommandStore _matchStore;
    private readonly BankReconciliationCloseCommandStore _closeStore;
    private readonly BankStatementAdjustmentCommandStore _adjustmentCommandStore;
    private readonly BankStatementAdjustmentApprovalStore _approvalStore;
    private readonly BankStatementAdjustmentReversalStore _reversalStore;
    private readonly BankStatementAdjustmentJournalPoster _journalPoster;
    private readonly Mock<ISender> _journalCommands = new();
    private readonly Mock<IAccountingConfigurationService> _config = new();

    public P306CloseAdjustmentTests()
    {
        _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options);
        _tenant.Setup(x => x.RequireBusinessId()).Returns(1); _tenant.Setup(x => x.EffectiveBusinessId).Returns(1); _tenant.Setup(x => x.IsSuperAdmin).Returns(false);
        _db.Businesses.Add(new Business { Id = 1, Name = "One" }); _db.ChartAccounts.AddRange(Account(101, ChartAccountTypes.Asset), Account(601, ChartAccountTypes.Expense), Account(602, ChartAccountTypes.Income)); _db.BankAccounts.Add(new BankAccount { Id = 501, BusinessId = 1, ChartAccountId = 101, DisplayName = "Bank", ReconciliationStartDate = new DateTime(2026, 9, 1) }); _db.BankStatementImports.Add(new BankStatementImport { Id = 100, BusinessId = 1, BankAccountId = 501, SourceFileName = "s", SourceFileChecksum = "s", Status = BankStatementImportStatuses.Imported, OpeningBalance = 100, ClosingBalance = 110 }); _db.BankStatementLines.Add(new BankStatementLine { Id = 1001, BusinessId = 1, BankAccountId = 501, BankStatementImportId = 100, SourceRowNumber = 1, TransactionDate = new DateTime(2026, 9, 10), Description = "movement", SignedAmount = 10, CreditAmount = 10 }); _db.JournalEntries.AddRange(new JournalEntry { Id = 199, BusinessId = 1, JournalNumber = "OPEN", JournalType = "Opening", ReferenceType = "Opening", ReferenceId = "0", Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 8, 31), TransactionDate = new DateTime(2026, 8, 31), Lines = [new JournalEntryLine { Id = 1991, AccountId = 101, DebitAmount = 100, Description = "opening" }] }, new JournalEntry { Id = 200, BusinessId = 1, JournalNumber = "JR", JournalType = "Sale", ReferenceType = "Sale", ReferenceId = "1", Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 9, 10), TransactionDate = new DateTime(2026, 9, 10), Lines = [new JournalEntryLine { Id = 2001, AccountId = 101, DebitAmount = 10, Description = "movement" }] }); _db.SaveChanges();
        _readStore = new BankReconciliationReadStore(_db);
        _sessionStore = new BankReconciliationSessionCommandStore(_db);
        _matchStore = new BankReconciliationMatchCommandStore(_db);
        _closeStore = new BankReconciliationCloseCommandStore(
            _db,
            new BankReconciliationReadStore(_db));
        _adjustmentCommandStore = new BankStatementAdjustmentCommandStore(_db);
        _journalPoster = new BankStatementAdjustmentJournalPoster(
            _journalCommands.Object,
            _config.Object);
        _approvalStore = new BankStatementAdjustmentApprovalStore(_db, _journalPoster);
        _reversalStore = new BankStatementAdjustmentReversalStore(_db, _journalPoster);
        _config.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.BankFeeExpense, null)).ReturnsAsync(new PostingAccountResolutionDto { AccountId = 601, Slot = PostingAccountSlots.BankFeeExpense });
        _config.Setup(x => x.ResolvePostingAccountAsync(PostingAccountSlots.BankInterestIncome, null)).ReturnsAsync(new PostingAccountResolutionDto { AccountId = 602, Slot = PostingAccountSlots.BankInterestIncome });
        _journalCommands.Setup(x => x.Send(It.IsAny<CreateOrGetSystemJournalDraftCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(700L);
        _journalCommands.Setup(x => x.Send(It.IsAny<PostJournalEntryCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(700L);
        _journalCommands.Setup(x => x.Send(It.IsAny<ReverseJournalEntryCommand>(), It.IsAny<CancellationToken>())).ReturnsAsync(701L);
    }
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ReadyForCloseRequiresEvidenceMatchesAndNoExceptions_ThenCloseAndReopenAreIdempotent()
    {
        var session = await CreateSession(); await MatchAsync(session.Id); var ready = await _closeStore.PrepareForCloseAsync(1, 9, session.Id, CancellationToken.None); var closed = await _closeStore.CloseAsync(1, 9, session.Id, "Finance approved", CancellationToken.None); var closeRetry = await _closeStore.CloseAsync(1, 9, session.Id, "retry", CancellationToken.None); Assert.Equal(BankReconciliationSessionStatuses.ReadyForClose, ready.Status); Assert.Equal(BankReconciliationSessionStatuses.Closed, closed.Status); Assert.Equal(closed.Id, closeRetry.Id); Assert.Equal("Finance approved", (await _db.BankReconciliationSessions.SingleAsync(row => row.Id == session.Id)).CloseResultReference); await Assert.ThrowsAsync<InvalidOperationException>(() => MatchAsync(session.Id)); var reopened = await _closeStore.ReopenAsync(1, 9, session.Id, "correction", CancellationToken.None); var reopenRetry = await _closeStore.ReopenAsync(1, 9, session.Id, "retry", CancellationToken.None); Assert.Equal(BankReconciliationSessionStatuses.InProgress, reopened.Status); Assert.Equal(reopened.Id, reopenRetry.Id); Assert.Equal("correction", (await _db.BankReconciliationSessions.SingleAsync(row => row.Id == session.Id)).ReopenReason);
    }

    [Fact]
    public async Task UnresolvedExceptionPreventsClose()
    {
        var session = await CreateSession(); await MatchAsync(session.Id); _db.BankStatementExceptions.Add(new BankStatementException { BusinessId = 1, BankAccountId = 501, BankStatementLineId = 1001, Classification = BankStatementExceptionClassifications.Chargeback, Reason = "review", CreatedBy = 9 }); await _db.SaveChangesAsync(); await Assert.ThrowsAsync<InvalidOperationException>(() => _closeStore.PrepareForCloseAsync(1, 9, session.Id, CancellationToken.None));
    }

    [Fact]
    public async Task BankFeeAdjustmentPostsExpenseDebitAndBankCredit()
    {
        var proposed = await ProposeAsync(BankStatementAdjustmentTypes.BankFee, 2, "monthly fee", "fee-1"); var approved = await _approvalStore.ApproveAsync(1, 9, proposed.Id, new DateTime(2026, 9, 10), CancellationToken.None); var draft = Assert.IsType<CreateOrGetSystemJournalDraftCommand>(_journalCommands.Invocations.Single(x => x.Arguments[0] is CreateOrGetSystemJournalDraftCommand).Arguments[0]); Assert.Contains(draft.Journal.Lines, x => x.AccountId == 601 && x.DebitAmount == 2); Assert.Contains(draft.Journal.Lines, x => x.AccountId == 101 && x.CreditAmount == 2); Assert.Equal(BankStatementAdjustmentStatuses.Approved, approved.Status);
    }

    [Fact]
    public async Task InterestAdjustmentPostsBankDebitAndIncomeCredit()
    {
        var proposed = await ProposeAsync(BankStatementAdjustmentTypes.BankInterest, 2, "interest", "interest-1"); await _approvalStore.ApproveAsync(1, 9, proposed.Id, new DateTime(2026, 9, 10), CancellationToken.None); var draft = Assert.IsType<CreateOrGetSystemJournalDraftCommand>(_journalCommands.Invocations.Single(x => x.Arguments[0] is CreateOrGetSystemJournalDraftCommand).Arguments[0]); Assert.Contains(draft.Journal.Lines, x => x.AccountId == 101 && x.DebitAmount == 2); Assert.Contains(draft.Journal.Lines, x => x.AccountId == 602 && x.CreditAmount == 2);
    }

    [Fact]
    public async Task AdjustmentReversalIsLinkedAndIdempotent()
    {
        var proposed = await ProposeAsync(BankStatementAdjustmentTypes.BankFee, 2, "fee", "reverse-fee"); var approved = await _approvalStore.ApproveAsync(1, 9, proposed.Id, new DateTime(2026, 9, 10), CancellationToken.None); var reversed = await _reversalStore.ReverseAsync(1, 9, approved.Id, " correction ", new DateTime(2026, 10, 1), CancellationToken.None); var retry = await _reversalStore.ReverseAsync(1, 9, approved.Id, "retry", new DateTime(2026, 10, 1), CancellationToken.None); Assert.Equal(BankStatementAdjustmentStatuses.Reversed, reversed.Status); Assert.Equal(reversed.ReversalJournalEntryId, retry.ReversalJournalEntryId); _journalCommands.Verify(x => x.Send(It.Is<ReverseJournalEntryCommand>(command => command.BusinessId == 1 && command.ActorUserId == 9 && command.JournalId == 700 && command.Reversal.Reason == "correction" && command.Reversal.PostingDate == new DateTime(2026, 10, 1) && command.Reversal.BusinessDate == new DateTime(2026, 10, 1)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdjustmentProposalTrimsFieldsAndReturnsTheOriginalProposalForSequentialRetry()
    {
        var proposed = await ProposeAsync(
            BankStatementAdjustmentTypes.BankFee,
            2,
            " monthly fee ",
            " fee-retry ");
        var retry = await _adjustmentCommandStore.ProposeAsync(
            1,
            99,
            new ProposeBankStatementAdjustmentInput(
                9999,
                "unsupported",
                999m,
                "different retry",
                "fee-retry"),
            CancellationToken.None);

        Assert.Equal(proposed.Id, retry.Id);
        Assert.Equal(BankStatementAdjustmentStatuses.Proposed, retry.Status);
        var persisted = await _db.BankStatementAdjustments.SingleAsync();
        Assert.Equal("monthly fee", persisted.Reason);
        Assert.Equal("fee-retry", persisted.IdempotencyKey);
        Assert.Equal(9, persisted.ProposedBy);
        Assert.Equal(9, persisted.CreatedBy);
    }

    [Fact]
    public async Task ApprovedFeeAndInterestAdjustmentsAreNotDoubleCountedInReconciliation()
    {
        _db.BankStatementLines.AddRange(new BankStatementLine { Id = 1002, BusinessId = 1, BankAccountId = 501, BankStatementImportId = 100, SourceRowNumber = 2, TransactionDate = new DateTime(2026, 9, 11), Description = "fee", SignedAmount = -2, DebitAmount = 2 }, new BankStatementLine { Id = 1003, BusinessId = 1, BankAccountId = 501, BankStatementImportId = 100, SourceRowNumber = 3, TransactionDate = new DateTime(2026, 9, 12), Description = "interest", SignedAmount = 3, CreditAmount = 3 });
        _db.JournalEntries.AddRange(new JournalEntry { Id = 201, BusinessId = 1, JournalNumber = "FEE", JournalType = "BankStatementAdjustment", ReferenceType = "BankStatementAdjustment", ReferenceId = "1", Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 9, 11), TransactionDate = new DateTime(2026, 9, 11), Lines = [new JournalEntryLine { Id = 2011, AccountId = 101, CreditAmount = 2 }, new JournalEntryLine { Id = 2012, AccountId = 601, DebitAmount = 2 }] }, new JournalEntry { Id = 202, BusinessId = 1, JournalNumber = "INT", JournalType = "BankStatementAdjustment", ReferenceType = "BankStatementAdjustment", ReferenceId = "2", Status = JournalEntryStatuses.Posted, PostingDate = new DateTime(2026, 9, 12), TransactionDate = new DateTime(2026, 9, 12), Lines = [new JournalEntryLine { Id = 2021, AccountId = 101, DebitAmount = 3 }, new JournalEntryLine { Id = 2022, AccountId = 602, CreditAmount = 3 }] });
        _db.BankStatementAdjustments.AddRange(new BankStatementAdjustment { Id = 11, BusinessId = 1, BankAccountId = 501, BankStatementLineId = 1002, AdjustmentType = BankStatementAdjustmentTypes.BankFee, Amount = 2, Status = BankStatementAdjustmentStatuses.Approved, JournalEntryId = 201, IdempotencyKey = "fee", ProposedBy = 9 }, new BankStatementAdjustment { Id = 12, BusinessId = 1, BankAccountId = 501, BankStatementLineId = 1003, AdjustmentType = BankStatementAdjustmentTypes.BankInterest, Amount = 3, Status = BankStatementAdjustmentStatuses.Approved, JournalEntryId = 202, IdempotencyKey = "interest", ProposedBy = 9 });
        _db.BankStatementImports.Single(x => x.Id == 100).ClosingBalance = 111;
        await _db.SaveChangesAsync();
        var session = await CreateSession(); var calculation = await _readStore.GetCalculationAsync(1, session.Id, CancellationToken.None);
        Assert.Equal(11m, calculation.StatementMovement); Assert.Equal(11m, calculation.GlMovement); Assert.Equal(111m, calculation.GlClosingBalance); Assert.Equal(0m, calculation.ReconciliationDifference); Assert.Equal(2, await _db.BankStatementAdjustments.CountAsync(x => x.Status == BankStatementAdjustmentStatuses.Approved));
    }

    private Task<BankReconciliationSessionReadModel> CreateSession() =>
        _sessionStore.CreateAsync(
            1,
            9,
            new CreateBankReconciliationSessionInput(
                501,
                new DateTime(2026, 9, 1),
                new DateTime(2026, 9, 30)),
            CancellationToken.None);

    private Task<StatementAdjustmentReadModel> ProposeAsync(
        string adjustmentType,
        decimal amount,
        string reason,
        string idempotencyKey) =>
        _adjustmentCommandStore.ProposeAsync(
            1,
            9,
            new ProposeBankStatementAdjustmentInput(
                1001,
                adjustmentType,
                amount,
                reason,
                idempotencyKey),
            CancellationToken.None);

    private Task<ReconciliationMatchReadModel> MatchAsync(long sessionId) =>
        _matchStore.CreateAsync(
            1,
            9,
            new CreateBankReconciliationMatchInput(sessionId, [1001], [2001]),
            CancellationToken.None);

    private static ChartAccount Account(long id, string type) => new() { Id = id, BusinessId = 1, AccountCode = id.ToString(), AccountName = $"Account {id}", AccountType = type, NormalBalanceType = type == ChartAccountTypes.Income ? NormalBalanceTypes.Credit : NormalBalanceTypes.Debit, IsActive = true, AllowManualPosting = true };
}
