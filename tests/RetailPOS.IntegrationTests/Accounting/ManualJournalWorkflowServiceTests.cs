using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Tests.Accounting;

public class ManualJournalCommandWorkflowTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly Mock<IJournalNumberAllocator> _journalNumbers = new();
    private readonly Mock<IAuditService> _audit = new();

    public ManualJournalCommandWorkflowTests()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new RetailPOSDbContext(options);
        Seed();

        _journalNumbers
            .Setup(x => x.AllocateNextJournalNumberAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<DateTime>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("JR-2026-000001");

        _audit
            .Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _audit
            .Setup(x => x.RecordFailureAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task CreateManualAsync_RejectsMissingNarration()
    {
        var date = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CreateManualJournalDraftCommandHandler(CreateStore()).Handle(
                new CreateManualJournalDraftCommand(1, 901, new ManualJournalDraftInput(
                    null, date, date, date, "Manual", null, null, null, null,
                    null, null, null,
                    [new(1001, null, 40, 0, null, null, null, null, null, null),
                     new(2001, null, 0, 40, null, null, null, null, null, null)])),
                CancellationToken.None));

        Assert.Contains("Description is required", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateManualAsync_RejectsMoreThan200Lines()
    {
        var lines = new List<ManualJournalDraftLineInput>();
        for (var index = 0; index < 100; index++)
        {
            lines.Add(new(1001, null, 1, 0, null, null, null, null, null, null));
            lines.Add(new(2001, null, 0, 1, null, null, null, null, null, null));
        }
        lines.Add(new(1001, null, 1, 0, null, null, null, null, null, null));
        var date = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new CreateManualJournalDraftCommandHandler(CreateStore()).Handle(
                new CreateManualJournalDraftCommand(1, 901, new ManualJournalDraftInput(
                    null, date, date, date, "Manual", null, null, "Too many lines",
                    null, null, null, null, lines)),
                CancellationToken.None));

        Assert.Contains("cannot exceed 200 lines", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ModeA_PostsDirectlyFromDraft()
    {
        var date = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc);
        var journalId = await CreateDraftAsync(date, "Direct post journal", 25m);
        await new PostJournalEntryCommandHandler(CreateStore()).Handle(
            new PostJournalEntryCommand(1, 901, journalId), CancellationToken.None);
        Assert.Equal(JournalEntryStatuses.Posted, (await _db.JournalEntries.SingleAsync(x => x.Id == journalId)).Status);
    }

    [Fact]
    public async Task ModeB_RequiresSubmitApproveThenPost()
    {
        SetManualConfig(ManualJournalWorkflowModes.ApprovalRequired, allowSelfApproval: false);
        var journalId = await CreateDraftAsync(new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc), "Approval flow journal", 50m);

        var postEx = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PostJournalEntryCommandHandler(CreateStore()).Handle(
                new PostJournalEntryCommand(1, 901, journalId), CancellationToken.None));
        Assert.Contains("Approved", postEx.Message, StringComparison.OrdinalIgnoreCase);

        await new SubmitManualJournalCommandHandler(CreateStore()).Handle(
            new SubmitManualJournalCommand(1, 901, journalId), CancellationToken.None);
        Assert.Equal(JournalEntryStatuses.Submitted, (await _db.JournalEntries.SingleAsync(x => x.Id == journalId)).Status);

        await new ApproveManualJournalCommandHandler(CreateStore()).Handle(
            new ApproveManualJournalCommand(1, 902, journalId, false), CancellationToken.None);
        Assert.Equal(JournalEntryStatuses.Approved, (await _db.JournalEntries.SingleAsync(x => x.Id == journalId)).Status);

        await new PostJournalEntryCommandHandler(CreateStore()).Handle(
            new PostJournalEntryCommand(1, 902, journalId), CancellationToken.None);
        Assert.Equal(JournalEntryStatuses.Posted, (await _db.JournalEntries.SingleAsync(x => x.Id == journalId)).Status);
    }

    [Fact]
    public async Task ModeB_RejectsSelfApproval_WhenDisabled()
    {
        SetManualConfig(ManualJournalWorkflowModes.ApprovalRequired, allowSelfApproval: false);
        var journalId = await CreateDraftAsync(new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc), "Self approval blocked", 10m);
        await new SubmitManualJournalCommandHandler(CreateStore()).Handle(
            new SubmitManualJournalCommand(1, 901, journalId), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ApproveManualJournalCommandHandler(CreateStore()).Handle(
                new ApproveManualJournalCommand(1, 901, journalId, false), CancellationToken.None));
        Assert.Contains("cannot approve their own", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ModeB_AllowsBusinessOwnerSelfApproval_WhenEnabled()
    {
        SetManualConfig(ManualJournalWorkflowModes.ApprovalRequired, allowSelfApproval: true);
        var journalId = await CreateDraftAsync(new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc), "Owner self approval", 15m);
        await new SubmitManualJournalCommandHandler(CreateStore()).Handle(
            new SubmitManualJournalCommand(1, 901, journalId), CancellationToken.None);
        await new ApproveManualJournalCommandHandler(CreateStore()).Handle(
            new ApproveManualJournalCommand(1, 901, journalId, true), CancellationToken.None);
        Assert.Equal(JournalEntryStatuses.Approved, (await _db.JournalEntries.SingleAsync(x => x.Id == journalId)).Status);
    }

    private ManualJournalDraftStore CreateStore() => new(
        _db,
        _audit.Object,
        _journalNumbers.Object,
        NullLogger<ManualJournalDraftStore>.Instance);

    private async Task<long> CreateDraftAsync(DateTime date, string description, decimal amount)
    {
        var command = new CreateManualJournalDraftCommand(
            1,
            901,
            new ManualJournalDraftInput(
                null,
                date,
                date,
                date,
                "Manual",
                null,
                null,
                description,
                null,
                null,
                null,
                null,
                [
                    new ManualJournalDraftLineInput(1001, null, amount, 0, null, null, null, null, null, null),
                    new ManualJournalDraftLineInput(2001, null, 0, amount, null, null, null, null, null, null)
                ]));
        return await new CreateManualJournalDraftCommandHandler(CreateStore()).Handle(
            command, CancellationToken.None);
    }

    private void SetManualConfig(string mode, bool allowSelfApproval)
    {
        var config = _db.AccountingConfigurations.First(x => x.BusinessId == 1);
        config.ManualJournalWorkflowMode = mode;
        config.ManualJournalAllowSelfApproval = allowSelfApproval;
        config.UpdatedAt = DateTime.UtcNow;
        _db.SaveChanges();
    }

    private void Seed()
    {
        _db.Businesses.Add(new Business { Id = 1, Name = "Manual Journal Business", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        _db.ChartAccounts.AddRange(
            Account(1001, "1000", "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2001, "2000", "Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(9991, "9991", "Blocked Manual", ChartAccountTypes.Asset, NormalBalanceTypes.Debit, allowManualPosting: false)
        );

        _db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            JournalNumberPrefix = "JR",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear,
            ManualJournalWorkflowMode = ManualJournalWorkflowModes.DraftPost,
            ManualJournalAllowSelfApproval = false,
            ManualJournalAttachmentMaxBytes = 10 * 1024 * 1024,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.FinancialYears.Add(new FinancialYear
        {
            Id = 7001,
            BusinessId = 1,
            YearCode = "FY26",
            Name = "FY26",
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            IsCurrent = true,
            IsClosed = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = 7002,
            FinancialYearId = 7001,
            PeriodNumber = 7,
            Name = "July",
            StartDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = AccountingPeriodStatuses.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();
    }

    private static ChartAccount Account(long id, string code, string name, string type, string normalBalance, bool allowManualPosting = true)
    {
        return new ChartAccount
        {
            Id = id,
            BusinessId = 1,
            AccountCode = code,
            AccountName = name,
            AccountType = type,
            NormalBalanceType = normalBalance,
            AllowManualPosting = allowManualPosting,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}