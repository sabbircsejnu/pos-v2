using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Data;

using RetailPOS.Tests.Infrastructure;

namespace RetailPOS.Tests.Accounting;

public class JournalEntryCommandTests : IDisposable
{
    private readonly RetailPOSDbContext _db;

    public JournalEntryCommandTests()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new RetailPOSDbContext(options);
        Seed();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task CreateManualAsync_RejectsUnbalancedJournal()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = DateTime.UtcNow,
            PostingDate = DateTime.UtcNow,
            ReferenceType = "Manual",
            ReferenceId = "M-1",
            Description = "Unbalanced manual journal",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 100 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 90 }
            ]
        }, 901));

        Assert.Contains("not balanced", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateManualAsync_CreatesDistinctDrafts_ForRepeatedManualReference()
    {
        var service = CreateService();

        var dto = new CreateJournalEntryDto
        {
            TransactionDate = DateTime.UtcNow,
            PostingDate = DateTime.UtcNow,
            ReferenceType = "Manual",
            ReferenceId = "M-100",
            Description = "Idempotent test",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 100 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 100 }
            ]
        };

        var first = await service.CreateManualAsync(dto, 901);
        var second = await service.CreateManualAsync(dto, 901);

        Assert.Null(first.JournalNumber);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await _db.JournalEntries.CountAsync(x => x.BusinessId == 1));
    }

    [Fact]
    public async Task CreateOrGetSystemDraftAsync_IsIdempotentByTypeAndReference()
    {
        var service = CreateService();

        var dto = new CreateJournalEntryDto
        {
            JournalType = "SalesInvoice",
            TransactionDate = DateTime.UtcNow,
            PostingDate = DateTime.UtcNow,
            ReferenceType = "SalesInvoice",
            ReferenceId = "SI-1001",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 125 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 125 }
            ]
        };

        var first = await service.CreateOrGetSystemDraftAsync(dto, 901);
        var second = await service.CreateOrGetSystemDraftAsync(dto, 902);

        Assert.Equal(first.Id, second.Id);
        Assert.True(first.IsSystemGenerated);
        Assert.Equal(1, await _db.JournalEntries.CountAsync(x => x.BusinessId == 1 && x.IsSystemGenerated));
    }

    [Fact]
    public async Task PostAsync_RejectsLockedPeriod()
    {
        var service = CreateService();

        var postingDate = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);
        SeedYearAndPeriod(3001, "FY26", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), isClosed: false,
            3101, 8, "Aug", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc), AccountingPeriodStatuses.Locked);
        await _db.SaveChangesAsync();

        var draft = await service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = postingDate,
            PostingDate = postingDate,
            ReferenceType = "Manual",
            ReferenceId = "M-LCK-1",
            Description = "Locked period manual journal",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 50 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 50 }
            ]
        }, 901);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(draft.Id, 901));
        Assert.Contains("status 'Locked'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAsync_RejectsWhenNoMatchingFinancialYear()
    {
        var service = CreateService();

        var postingDate = new DateTime(2028, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var draft = await service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = postingDate,
            PostingDate = postingDate,
            ReferenceType = "Manual",
            ReferenceId = "M-NOFY-1",
            Description = "No financial year manual journal",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 55 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 55 }
            ]
        }, 901);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(draft.Id, 901));
        Assert.Contains("does not match any financial year", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAsync_RejectsWhenNoMatchingAccountingPeriod()
    {
        var service = CreateService();

        SeedYearAndPeriod(3301, "FY26-MissingPeriod", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), isClosed: false,
            3302, 1, "January", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc), AccountingPeriodStatuses.Open);
        await _db.SaveChangesAsync();

        var postingDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var draft = await service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = postingDate,
            PostingDate = postingDate,
            ReferenceType = "Manual",
            ReferenceId = "M-NOPERIOD-1",
            Description = "No accounting period manual journal",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 35 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 35 }
            ]
        }, 901);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(draft.Id, 901));
        Assert.Contains("does not match any accounting period", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAsync_RejectsClosedFinancialYear()
    {
        var service = CreateService();

        SeedYearAndPeriod(3401, "FY26-Closed", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), isClosed: true,
            3402, 7, "July", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc), AccountingPeriodStatuses.Open);
        await _db.SaveChangesAsync();

        var postingDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var draft = await service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = postingDate,
            PostingDate = postingDate,
            ReferenceType = "Manual",
            ReferenceId = "M-CLOSED-YEAR-1",
            Description = "Closed year manual journal",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 45 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 45 }
            ]
        }, 901);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(draft.Id, 901));
        Assert.Contains("belongs to closed financial year", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAsync_RejectsClosedPeriod()
    {
        var service = CreateService();

        SeedYearAndPeriod(3501, "FY26-ClosedPeriod", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), isClosed: false,
            3502, 7, "July", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc), AccountingPeriodStatuses.Closed);
        await _db.SaveChangesAsync();

        var postingDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var draft = await service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = postingDate,
            PostingDate = postingDate,
            ReferenceType = "Manual",
            ReferenceId = "M-CLOSED-PERIOD-1",
            Description = "Closed period manual journal",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 85 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 85 }
            ]
        }, 901);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(draft.Id, 901));
        Assert.Contains("status 'Closed'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAsync_RejectsWhenBusinessDateFallsInLockedPeriod()
    {
        var service = CreateService();

        _db.FinancialYears.Add(new FinancialYear
        {
            Id = 3002,
            BusinessId = 1,
            YearCode = "FY26-B",
            Name = "FY26-B",
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            IsCurrent = true,
            IsClosed = false
        });
        _db.AccountingPeriods.AddRange(
            new AccountingPeriod
            {
                Id = 3201,
                FinancialYearId = 3002,
                PeriodNumber = 1,
                Name = "Jan",
                StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
                Status = AccountingPeriodStatuses.Open
            },
            new AccountingPeriod
            {
                Id = 3202,
                FinancialYearId = 3002,
                PeriodNumber = 2,
                Name = "Feb",
                StartDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc),
                Status = AccountingPeriodStatuses.Locked
            });
        await _db.SaveChangesAsync();

        var draft = await service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            PostingDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            BusinessDate = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc),
            ReferenceType = "Manual",
            ReferenceId = "M-BIZ-LCK-1",
            Description = "Business date locked period journal",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 50 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 50 }
            ]
        }, 901);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(draft.Id, 901));
        Assert.Contains("Business date", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status 'Locked'", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAsync_SucceedsWhenMatchingOpenPeriodExists()
    {
        var service = CreateService();

        SeedYearAndPeriod(3601, "FY26-Open", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), isClosed: false,
            3602, 4, "April", new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc), AccountingPeriodStatuses.Open);
        await _db.SaveChangesAsync();

        var postingDate = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);
        var draft = await service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = postingDate,
            PostingDate = postingDate,
            BusinessDate = postingDate,
            ReferenceType = "Manual",
            ReferenceId = "M-OPEN-1",
            Description = "Open period manual journal",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 125 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 125 }
            ]
        }, 901);

        var posted = await service.PostAsync(draft.Id, 901);
        Assert.Equal(JournalEntryStatuses.Posted, posted.Status);
        Assert.Equal(3601, posted.FinancialYearId);
        Assert.Equal(3602, posted.AccountingPeriodId);
    }

    [Fact]
    public async Task ReverseAsync_CreatesReversal_AndMarksOriginal()
    {
        var service = CreateService();

        var effectiveDate = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        SeedYearAndPeriod(3701, "FY26-Reversal", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), isClosed: false,
            3702, 7, "July", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc), AccountingPeriodStatuses.Open);
        await _db.SaveChangesAsync();

        var draft = await service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = effectiveDate,
            PostingDate = effectiveDate,
            ReferenceType = "Manual",
            ReferenceId = "M-REV-1",
            Description = "Reversal source manual journal",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 70 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 70 }
            ]
        }, 901);

        var posted = await service.PostAsync(draft.Id, 901);
        Assert.Equal("JR-2026-000001", posted.JournalNumber);
        var reversal = await service.ReverseAsync(posted.Id, new ReverseJournalEntryDto
        {
            Reason = "Correction",
            PostingDate = effectiveDate,
            BusinessDate = effectiveDate
        }, 901);

        Assert.Equal(JournalEntryStatuses.Posted, reversal.Status);
        Assert.Equal("JournalReversal", reversal.JournalType);
        Assert.Equal("JR-2026-000001", posted.JournalNumber);
        Assert.Equal(posted.Id, reversal.ReversalOfJournalEntryId);

        var original = await _db.JournalEntries.FirstAsync(x => x.Id == posted.Id);
        Assert.Equal(JournalEntryStatuses.Reversed, original.Status);
        Assert.Equal(reversal.Id, original.ReversedByJournalEntryId);
        Assert.NotNull(original.ReversedAt);
        Assert.Equal(901, original.ReversedBy);

        var reversedLines = reversal.Lines;
        Assert.Equal(2, reversedLines.Count);
        Assert.Contains(reversedLines, x => x.CreditAmount == 70);
        Assert.Contains(reversedLines, x => x.DebitAmount == 70);
    }

    [Fact]
    public async Task UpdateDraftAsync_UpdatesHeaderAndLines_ForDraftOnly()
    {
        var service = CreateService();

        var draft = await service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = DateTime.UtcNow,
            PostingDate = DateTime.UtcNow,
            ReferenceType = "Manual",
            ReferenceId = "M-UPD-1",
            Description = "Before update",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 40 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 40 }
            ]
        }, 901);

        var updated = await service.UpdateDraftAsync(draft.Id, new UpdateJournalEntryDto
        {
            JournalType = "ManualJournal",
            TransactionDate = draft.TransactionDate.AddDays(1),
            PostingDate = draft.PostingDate.AddDays(1),
            BusinessDate = draft.PostingDate.AddDays(1),
            ReferenceType = "Manual",
            ReferenceId = "M-UPD-1",
            ReferenceNumber = "R-001",
            Description = "After update",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 55 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 55 }
            ]
        }, 902);

        Assert.Equal("After update", updated.Description);
        Assert.Equal("R-001", updated.ReferenceNumber);
        Assert.Equal(2, updated.Lines.Count);
        Assert.Contains(updated.Lines, x => x.DebitAmount == 55);
        Assert.Contains(updated.Lines, x => x.CreditAmount == 55);
        Assert.Equal(902, updated.UpdatedBy);
    }

    [Fact]
    public async Task CancelDraftAsync_CancelsDraft_AndRejectsPosting()
    {
        var service = CreateService();

        var draft = await service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = DateTime.UtcNow,
            PostingDate = DateTime.UtcNow,
            ReferenceType = "Manual",
            ReferenceId = "M-CXL-1",
            Description = "Cancellable manual journal",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 60 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 60 }
            ]
        }, 901);

        var cancelled = await service.CancelDraftAsync(draft.Id, "No longer needed", 901);
        Assert.Equal(JournalEntryStatuses.Cancelled, cancelled.Status);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostAsync(draft.Id, 901));
        Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManualJournalLifecycle_AuditPayloadsIncludeStatusReferencesActorsAndJournalLinks()
    {
        var auditEvents = new List<AuditEventInput>();
        var audit = new Mock<IAuditService>();
        audit
            .Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEventInput, CancellationToken>((input, _) => auditEvents.Add(input))
            .Returns(Task.CompletedTask);
        audit
            .Setup(x => x.RecordFailureAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(audit.Object);
        var effectiveDate = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        SeedYearAndPeriod(3901, "FY26-Audit", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc), false,
            3902, 7, "July", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc), AccountingPeriodStatuses.Open);
        _db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            ManualJournalWorkflowMode = ManualJournalWorkflowModes.ApprovalRequired,
            ManualJournalAllowSelfApproval = true,
            JournalNumberPrefix = "JR",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var draft = await service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = effectiveDate,
            PostingDate = effectiveDate,
            ReferenceType = "Manual",
            ReferenceId = "M-AUD-1",
            ReferenceNumber = "REF-1",
            Description = "Manual audit source",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 80 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 80 }
            ]
        }, 901);

        var updated = await service.UpdateDraftAsync(draft.Id, new UpdateJournalEntryDto
        {
            JournalType = "ManualJournal",
            TransactionDate = effectiveDate,
            PostingDate = effectiveDate,
            BusinessDate = effectiveDate,
            ReferenceType = "Manual",
            ReferenceId = "M-AUD-2",
            ReferenceNumber = "REF-2",
            Description = "Manual audit updated",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 90 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 90 }
            ]
        }, 902);
        await service.SubmitAsync(updated.Id, 903);
        await service.ApproveAsync(updated.Id, 904);
        var posted = await service.PostAsync(updated.Id, 905);
        var reversal = await service.ReverseAsync(posted.Id, new ReverseJournalEntryDto { Reason = "Audit correction", PostingDate = effectiveDate, BusinessDate = effectiveDate }, 906);

        var createAudit = Assert.Single(auditEvents, x => x.Summary.Contains("Journal draft created", StringComparison.Ordinal));
        Assert.Equal(draft.Id, createAudit.Metadata?["journalId"]);
        Assert.Equal("Manual", createAudit.Metadata?["referenceType"]);
        Assert.Equal("M-AUD-1", createAudit.Metadata?["referenceId"]);
        Assert.Equal(901L, createAudit.Metadata?["createdBy"]);
        Assert.Contains(createAudit.Entities.Single().Fields, x => x.FieldName == "Status" && Equals(x.NewValue, JournalEntryStatuses.Draft));

        var updateAudit = Assert.Single(auditEvents, x => x.Summary.Contains("Journal draft updated", StringComparison.Ordinal));
        Assert.Equal(902L, updateAudit.Metadata?["updatedBy"]);
        Assert.Contains(updateAudit.Entities.Single().Fields, x => x.FieldName == "ReferenceId" && Equals(x.OldValue, "M-AUD-1") && Equals(x.NewValue, "M-AUD-2"));
        Assert.Contains(updateAudit.Entities.Single().Fields, x => x.FieldName == "DebitTotal" && Equals(x.OldValue, 80m) && Equals(x.NewValue, 90m));

        var submitAudit = Assert.Single(auditEvents, x => x.Summary.Contains("Journal submitted", StringComparison.Ordinal));
        Assert.Contains(submitAudit.Entities.Single().Fields, x => x.FieldName == "Status" && Equals(x.OldValue, JournalEntryStatuses.Draft) && Equals(x.NewValue, JournalEntryStatuses.Submitted));
        Assert.Equal(903L, submitAudit.Metadata?["submittedBy"]);

        var approveAudit = Assert.Single(auditEvents, x => x.Summary.Contains("Journal approved", StringComparison.Ordinal));
        Assert.Contains(approveAudit.Entities.Single().Fields, x => x.FieldName == "Status" && Equals(x.OldValue, JournalEntryStatuses.Submitted) && Equals(x.NewValue, JournalEntryStatuses.Approved));
        Assert.Equal(904L, approveAudit.Metadata?["approvedBy"]);

        var postAudit = Assert.Single(auditEvents, x => x.Summary.Contains("Journal posted", StringComparison.Ordinal));
        Assert.Contains(postAudit.Entities.Single().Fields, x => x.FieldName == "Status" && Equals(x.OldValue, JournalEntryStatuses.Approved) && Equals(x.NewValue, JournalEntryStatuses.Posted));
        Assert.Equal(905L, postAudit.Metadata?["postedBy"]);
        Assert.Equal(posted.Id, postAudit.Metadata?["journalId"]);

        var reverseAudit = Assert.Single(auditEvents, x => x.Summary.Contains("Journal reversed", StringComparison.Ordinal));
        Assert.Equal(posted.Id, reverseAudit.Metadata?["originalJournalId"]);
        Assert.Equal(reversal.Id, reverseAudit.Metadata?["reversalJournalId"]);
        Assert.Contains(reverseAudit.Entities.Single().Fields, x => x.FieldName == "Reason" && Equals(x.NewValue, "Audit correction"));

        var cancelDraft = await service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = effectiveDate,
            PostingDate = effectiveDate,
            ReferenceType = "Manual",
            ReferenceId = "M-AUD-CANCEL",
            Description = "Manual cancellation audit",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 40 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 40 }
            ]
        }, 901);
        await service.CancelDraftAsync(cancelDraft.Id, "No longer needed", 907);

        var cancelAudit = Assert.Single(auditEvents, x => x.Summary.Contains($"Journal draft cancelled: {cancelDraft.Id}", StringComparison.Ordinal));
        Assert.Equal(907L, cancelAudit.Metadata?["cancelledBy"]);
        Assert.Contains(cancelAudit.Entities.Single().Fields, x => x.FieldName == "CancellationReason" && Equals(x.NewValue, "No longer needed"));

        var rejectDraft = await service.CreateManualAsync(new CreateJournalEntryDto
        {
            TransactionDate = effectiveDate,
            PostingDate = effectiveDate,
            ReferenceType = "Manual",
            ReferenceId = "M-AUD-REJECT",
            ReferenceNumber = "REF-REJECT",
            Description = "Manual rejection audit",
            Lines =
            [
                new CreateJournalEntryLineDto { AccountId = 1001, DebitAmount = 25 },
                new CreateJournalEntryLineDto { AccountId = 2001, CreditAmount = 25 }
            ]
        }, 901);
        await service.SubmitAsync(rejectDraft.Id, 908);
        var rejected = await service.RejectAsync(rejectDraft.Id, "Supporting detail required", 909);

        var rejectAudit = Assert.Single(auditEvents, x => x.Summary == $"Journal rejected: {rejected.Id}");
        Assert.Equal(AuditActionType.Reject, rejectAudit.ActionType);
        Assert.Equal(1L, rejectAudit.Metadata?["businessId"]);
        Assert.Equal("M-AUD-REJECT", rejectAudit.Metadata?["referenceId"]);
        Assert.Equal(909L, rejectAudit.Metadata?["rejectedBy"]);
        Assert.Equal("Supporting detail required", rejectAudit.Metadata?["reason"]);
        Assert.Contains(rejectAudit.Entities.Single().Fields, x => x.FieldName == "Status" && Equals(x.OldValue, JournalEntryStatuses.Submitted) && Equals(x.NewValue, JournalEntryStatuses.Rejected));
        Assert.Contains(rejectAudit.Entities.Single().Fields, x => x.FieldName == "RejectionReason" && Equals(x.NewValue, "Supporting detail required"));
    }

    private JournalEntryCommandTestHarness CreateService(IAuditService? auditOverride = null)
    {
        var journalNumbers = new Mock<IJournalNumberAllocator>();
        var audit = new Mock<IAuditService>();
        journalNumbers
            .Setup(x => x.AllocateNextJournalNumberAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<DateTime>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("JR-2026-000001");

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

        return new JournalEntryCommandTestHarness(
            _db,
            auditOverride ?? audit.Object,
            journalNumbers.Object);
    }

    private sealed class JournalEntryCommandTestHarness(
        RetailPOSDbContext db,
        IAuditService audit,
        IJournalNumberAllocator journalNumbers)
    {
        private readonly ManualJournalDraftStore _store = new(
            db,
            audit,
            journalNumbers,
            NullLogger<ManualJournalDraftStore>.Instance);

        public async Task<JournalEntryDto> CreateManualAsync(
            CreateJournalEntryDto input,
            long actorUserId)
        {
            var id = await new CreateManualJournalDraftCommandHandler(_store).Handle(
                new CreateManualJournalDraftCommand(1, actorUserId, Map(input)),
                CancellationToken.None);
            return await LoadAsync(id);
        }

        public async Task<JournalEntryDto> CreateOrGetSystemDraftAsync(
            CreateJournalEntryDto input,
            long actorUserId,
            string? idempotencyDiscriminator = null)
        {
            var id = await new CreateOrGetSystemJournalDraftCommandHandler(_store).Handle(
                new CreateOrGetSystemJournalDraftCommand(
                    1, actorUserId, Map(input), idempotencyDiscriminator),
                CancellationToken.None);
            return await LoadAsync(id);
        }

        public async Task<JournalEntryDto> UpdateDraftAsync(
            long journalId,
            UpdateJournalEntryDto input,
            long actorUserId)
        {
            var id = await new UpdateManualJournalDraftCommandHandler(_store).Handle(
                new UpdateManualJournalDraftCommand(
                    1, actorUserId, journalId, Map(input)),
                CancellationToken.None);
            return await LoadAsync(id);
        }

        public async Task<JournalEntryDto> SubmitAsync(long journalId, long actorUserId)
        {
            var id = await new SubmitManualJournalCommandHandler(_store).Handle(
                new SubmitManualJournalCommand(1, actorUserId, journalId),
                CancellationToken.None);
            return await LoadAsync(id);
        }

        public async Task<JournalEntryDto> ApproveAsync(long journalId, long actorUserId)
        {
            var id = await new ApproveManualJournalCommandHandler(_store).Handle(
                new ApproveManualJournalCommand(1, actorUserId, journalId, false),
                CancellationToken.None);
            return await LoadAsync(id);
        }

        public async Task<JournalEntryDto> RejectAsync(
            long journalId,
            string reason,
            long actorUserId)
        {
            var id = await new RejectManualJournalCommandHandler(_store).Handle(
                new RejectManualJournalCommand(1, actorUserId, journalId, reason),
                CancellationToken.None);
            return await LoadAsync(id);
        }

        public async Task<JournalEntryDto> CancelDraftAsync(
            long journalId,
            string? reason,
            long actorUserId)
        {
            var id = await new CancelManualJournalCommandHandler(_store).Handle(
                new CancelManualJournalCommand(1, actorUserId, journalId, reason),
                CancellationToken.None);
            return await LoadAsync(id);
        }

        public async Task<JournalEntryDto> PostAsync(long journalId, long actorUserId)
        {
            var id = await new PostJournalEntryCommandHandler(_store).Handle(
                new PostJournalEntryCommand(1, actorUserId, journalId),
                CancellationToken.None);
            return await LoadAsync(id);
        }

        public async Task<JournalEntryDto> ReverseAsync(
            long journalId,
            ReverseJournalEntryDto input,
            long actorUserId)
        {
            var id = await new ReverseJournalEntryCommandHandler(_store, TestCompanySettings.Provider()).Handle(
                new ReverseJournalEntryCommand(
                    1,
                    actorUserId,
                    journalId,
                    new ManualJournalReversalInput(
                        input.Reason, input.PostingDate, input.BusinessDate)),
                CancellationToken.None);
            return await LoadAsync(id);
        }

        private static ManualJournalDraftInput Map(CreateJournalEntryDto input) =>
            new(
                input.JournalType,
                input.TransactionDate,
                input.PostingDate,
                input.BusinessDate,
                input.ReferenceType,
                input.ReferenceId,
                input.ReferenceNumber,
                input.Description,
                input.OutletId,
                input.WarehouseId,
                input.TerminalId,
                input.CashSessionId,
                input.Lines.Select(Map).ToArray());

        private static ManualJournalDraftUpdateInput Map(UpdateJournalEntryDto input) =>
            new(
                input.JournalType,
                input.TransactionDate,
                input.PostingDate,
                input.BusinessDate,
                input.ReferenceType,
                input.ReferenceId,
                input.ReferenceNumber,
                input.Description,
                input.OutletId,
                input.WarehouseId,
                input.TerminalId,
                input.CashSessionId,
                input.Lines.Select(Map).ToArray());

        private static ManualJournalDraftLineInput Map(CreateJournalEntryLineDto input) =>
            new(
                input.AccountId,
                input.Description,
                input.DebitAmount,
                input.CreditAmount,
                input.OutletId,
                input.WarehouseId,
                input.SupplierId,
                input.CustomerId,
                input.PaymentMethodId,
                input.CostCenterId);

        private async Task<JournalEntryDto> LoadAsync(long id)
        {
            var journal = await db.JournalEntries
                .Include(entry => entry.Lines)
                .SingleAsync(entry => entry.Id == id);
            return new JournalEntryDto
            {
                Id = journal.Id,
                BusinessId = journal.BusinessId,
                JournalNumber = journal.JournalNumber,
                JournalType = journal.JournalType,
                BusinessDate = journal.BusinessDate,
                TransactionDate = journal.TransactionDate,
                PostingDate = journal.PostingDate,
                FinancialYearId = journal.FinancialYearId,
                AccountingPeriodId = journal.AccountingPeriodId,
                ReferenceType = journal.ReferenceType,
                ReferenceId = journal.ReferenceId,
                ReferenceNumber = journal.ReferenceNumber,
                Description = journal.Description,
                OutletId = journal.OutletId,
                WarehouseId = journal.WarehouseId,
                TerminalId = journal.TerminalId,
                CashSessionId = journal.CashSessionId,
                Status = journal.Status,
                IsSystemGenerated = journal.IsSystemGenerated,
                ReversalOfJournalEntryId = journal.ReversalOfJournalEntryId,
                ReversedByJournalEntryId = journal.ReversedByJournalEntryId,
                SubmittedAt = journal.SubmittedAt,
                SubmittedBy = journal.SubmittedBy,
                ApprovedAt = journal.ApprovedAt,
                ApprovedBy = journal.ApprovedBy,
                RejectedAt = journal.RejectedAt,
                RejectedBy = journal.RejectedBy,
                RejectionReason = journal.RejectionReason,
                CreatedAt = journal.CreatedAt,
                CreatedBy = journal.CreatedBy,
                UpdatedAt = journal.UpdatedAt,
                UpdatedBy = journal.UpdatedBy,
                PostedAt = journal.PostedAt,
                PostedBy = journal.PostedBy,
                CancelledAt = journal.CancelledAt,
                CancelledBy = journal.CancelledBy,
                CancellationReason = journal.CancellationReason,
                ReversedAt = journal.ReversedAt,
                ReversedBy = journal.ReversedBy,
                Lines = journal.Lines.Select(line => new JournalEntryLineDto
                {
                    Id = line.Id,
                    AccountId = line.AccountId,
                    Description = line.Description,
                    DebitAmount = line.DebitAmount,
                    CreditAmount = line.CreditAmount,
                    OutletId = line.OutletId,
                    WarehouseId = line.WarehouseId,
                    SupplierId = line.SupplierId,
                    CustomerId = line.CustomerId,
                    PaymentMethodId = line.PaymentMethodId,
                    CostCenterId = line.CostCenterId
                }).ToList()
            };
        }
    }

    private void Seed()
    {
        _db.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Journal Business",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.ChartAccounts.AddRange(
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
                AccountName = "Payable",
                AccountType = ChartAccountTypes.Liability,
                NormalBalanceType = NormalBalanceTypes.Credit,
                AllowManualPosting = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

        _db.SaveChanges();
    }

    private void SeedYearAndPeriod(
        long yearId,
        string yearName,
        DateTime yearStart,
        DateTime yearEnd,
        bool isClosed,
        long periodId,
        int periodNumber,
        string periodName,
        DateTime periodStart,
        DateTime periodEnd,
        string periodStatus)
    {
        _db.FinancialYears.Add(new FinancialYear
        {
            Id = yearId,
            BusinessId = 1,
            YearCode = yearName,
            Name = yearName,
            StartDate = yearStart,
            EndDate = yearEnd,
            IsCurrent = true,
            IsClosed = isClosed,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.AccountingPeriods.Add(new AccountingPeriod
        {
            Id = periodId,
            FinancialYearId = yearId,
            PeriodNumber = periodNumber,
            Name = periodName,
            StartDate = periodStart,
            EndDate = periodEnd,
            Status = periodStatus,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }
}
