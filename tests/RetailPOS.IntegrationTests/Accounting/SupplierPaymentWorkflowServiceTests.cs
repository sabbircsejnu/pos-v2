using RetailPOS.Application.AccountingJournals.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.DTOs.SupplierPayment;
using RetailPOS.API.Services;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.Commands;
using RetailPOS.Application.SupplierPayments.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Audit;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.SupplierPayments;

namespace RetailPOS.Tests.Accounting;

public class SupplierPaymentWorkflowServiceTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly Mock<ITenantAccessService> _tenant = new();
    private readonly Mock<IJournalNumberAllocator> _journalNumbers = new();
    private readonly Mock<IAuditService> _audit = new();
    private readonly List<AuditEventInput> _auditEvents = new();

    public SupplierPaymentWorkflowServiceTests()
    {
        var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new RetailPOSDbContext(options);
        Seed();

        _tenant.Setup(x => x.IsSuperAdmin).Returns(false);
        _tenant.Setup(x => x.EffectiveBusinessId).Returns(1);
        _tenant.Setup(x => x.RequireBusinessId()).Returns(1);

        _journalNumbers
            .Setup(x => x.AllocateNextJournalNumberAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<DateTime>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("JR-2026-000001");

        _audit
            .Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
            .Callback<AuditEventInput, CancellationToken>((input, _) => _auditEvents.Add(input))
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
    public async Task SubmitCommand_PersistsActorTimestampsAndOneStatusAudit()
    {
        var created = await SupplierPaymentCreationTestHarness.CreateAsync(_db, new CreateSupplierPaymentDto
        {
            SupplierId = 10,
            PaymentDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            PaymentMethod = "bank",
            PaymentAccountId = 1021,
            Amount = 50m,
            OutletId = 11,
            ReferenceNumber = "PAY-SUBMIT"
        }, actorUserId: 101, _audit.Object);
        _auditEvents.Clear();

        var submitted = await SupplierPaymentCreationTestHarness.SubmitAsync(
            _db,
            created.Id,
            actorUserId: 102,
            _audit.Object);
        var persisted = await _db.SupplierPayments.SingleAsync(payment => payment.Id == created.Id);

        Assert.Equal("submitted", submitted.Status);
        Assert.Equal("submitted", persisted.Status);
        Assert.Equal(102L, persisted.SubmittedBy);
        Assert.NotEqual(default, persisted.SubmittedAt);
        Assert.Equal(102L, persisted.UpdatedBy);
        Assert.NotEqual(default, persisted.UpdatedAt);
        var audit = Assert.Single(_auditEvents);
        Assert.Equal(AuditActionType.Submit, audit.ActionType);
        Assert.Equal(102L, audit.Metadata!["actorUserId"]);
        Assert.Equal("draft", audit.Entities.Single().Fields.Single().OldValue);
        Assert.Equal("submitted", audit.Entities.Single().Fields.Single().NewValue);
    }

    [Fact]
    public async Task ApproveAsync_PostsPaymentJournal_AndCreatesAdvanceForOverpayment()
    {
        var created = await SupplierPaymentCreationTestHarness.CreateAsync(_db, new CreateSupplierPaymentDto
        {
            SupplierId = 10,
            PaymentDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
            PaymentMethod = "bank",
            PaymentAccountId = 1021,
            Amount = 100m,
            OutletId = 11,
            ReferenceNumber = "PAY-001",
            Allocations =
            [
                new SupplierPaymentAllocationLineDto { BillId = 1001, Amount = 75m }
            ]
        }, actorUserId: 101, _audit.Object);
        await SupplierPaymentCreationTestHarness.SubmitAsync(_db, created.Id, 101, _audit.Object);
        var approved = await ApproveAsync(created.Id, 101);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ApproveAsync(created.Id, 101));

        Assert.Equal("posted", approved.Status);
        Assert.Equal(75m, approved.AllocatedAmount);
        Assert.Equal(25m, approved.AdvanceAmount);

        var advance = await _db.SupplierAdvances.SingleOrDefaultAsync(x => x.SourcePaymentId == created.Id);
        Assert.NotNull(advance);
        Assert.Equal(25m, advance.RemainingAmount);
        Assert.Equal("posted", advance.Status);

        var journalCount = await _db.JournalEntries.CountAsync(x => x.ReferenceType == "SupplierPayment" || x.ReferenceType == "SupplierAdvance");
        Assert.Equal(2, journalCount);

        var paymentJournal = await _db.JournalEntries.FirstAsync(x => x.ReferenceType == "SupplierPayment");
        Assert.Equal(JournalEntryStatuses.Posted, paymentJournal.Status);
        Assert.Contains(paymentJournal.Lines, x => x.AccountId == 2001 && x.DebitAmount == 75m);
        Assert.Contains(paymentJournal.Lines, x => x.AccountId == 1021 && x.CreditAmount == 75m);

        var paymentAuditEvents = _auditEvents
            .Where(x => x.PrimaryEntity?.Type == "SupplierPayment" && x.PrimaryEntity?.Id == created.Id.ToString())
            .ToList();
        Assert.Contains(paymentAuditEvents, x => x.ActionType == AuditActionType.Create && Equals(x.Metadata?["actorUserId"], 101L));
        Assert.Contains(paymentAuditEvents, x => x.ActionType == AuditActionType.Submit && Equals(x.Metadata?["actorUserId"], 101L));
        var approvalAudit = Assert.Single(
            paymentAuditEvents,
            auditEvent => auditEvent.ActionType == AuditActionType.Approve);
        Assert.Equal(approved.JournalEntryId, approvalAudit.Metadata?["journalEntryId"]);
        Assert.Contains(approvalAudit.Entities.Single().Fields, field => field.FieldName == "AllocationSummary");
    }

    [Fact]
    public async Task CreateCommand_RejectsAllocationTotalGreaterThanPaymentAmountAfterDraftPersistence()
    {
        var dto = new CreateSupplierPaymentDto
        {
            SupplierId = 10,
            PaymentDate = new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc),
            PaymentMethod = "bank",
            PaymentAccountId = 1021,
            Amount = 100m,
            OutletId = 11,
            ReferenceNumber = "PAY-002",
            Allocations =
            [
                new SupplierPaymentAllocationLineDto { BillId = 1001, Amount = 75m },
                new SupplierPaymentAllocationLineDto { BillId = 1001, Amount = 40m }
            ]
        };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SupplierPaymentCreationTestHarness.CreateAsync(_db, dto, 101, _audit.Object));

        Assert.Contains("Allocation total cannot exceed", ex.Message);
        Assert.Equal("draft", await _db.SupplierPayments.Select(payment => payment.Status).SingleAsync());
    }

    [Fact]
    public async Task CreateAdvanceAsync_AndApproveAdvance_PostsAdvanceJournal()
    {
        var created = await SupplierPaymentCreationTestHarness.CreateAdvanceAsync(_db, new CreateSupplierAdvanceDto
        {
            SupplierId = 10,
            PaymentDate = new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc),
            PaymentMethod = "cash",
            PaymentAccountId = 1001,
            Amount = 50m,
            OutletId = 11,
            ReferenceNumber = "ADV-001"
        }, actorUserId: 101, _audit.Object);

        var persisted = await _db.SupplierAdvances.SingleAsync(x => x.Id == created.Id);
        Assert.Equal(new DateTime(2026, 3, 16), persisted.BusinessDate);
        Assert.Equal(new DateTime(2026, 3, 16), persisted.PostingDate);
        Assert.Equal("1:10:20260316:ADV-001", persisted.IdempotencyKey);
        Assert.Equal(50m, persisted.RemainingAmount);
        Assert.Equal(0m, persisted.AppliedAmount);

        var submitted = await SupplierPaymentCreationTestHarness.SubmitAdvanceAsync(
            _db, created.Id, 101, _audit.Object);
        Assert.Equal("submitted", submitted.Status);
        var submittedRow = await _db.SupplierAdvances.SingleAsync(x => x.Id == created.Id);
        Assert.Equal(101L, submittedRow.SubmittedBy);
        Assert.NotEqual(default, submittedRow.SubmittedAt);
        Assert.Equal(101L, submittedRow.UpdatedBy);
        Assert.NotEqual(default, submittedRow.UpdatedAt);
        var submissionAudit = Assert.Single(
            _auditEvents,
            auditEvent => auditEvent.PrimaryEntity?.Type == "SupplierAdvance"
                && auditEvent.PrimaryEntity?.Id == created.Id.ToString()
                && auditEvent.ActionType == AuditActionType.Submit);
        Assert.Equal(101L, submissionAudit.Metadata!["actorUserId"]);
        Assert.Equal("draft", submissionAudit.Entities.Single().Fields.Single().OldValue);
        Assert.Equal("submitted", submissionAudit.Entities.Single().Fields.Single().NewValue);
        var approved = await ApproveAdvanceAsync(created.Id, 101);

        Assert.Equal("posted", approved.Status);
        Assert.Equal(50m, approved.Amount);

        var journal = await _db.JournalEntries.SingleAsync(x => x.ReferenceType == "SupplierAdvance");
        Assert.Equal(JournalEntryStatuses.Posted, journal.Status);
        Assert.Contains(journal.Lines, x => x.AccountId == 3002 && x.DebitAmount == 50m);
        Assert.Contains(journal.Lines, x => x.AccountId == 1001 && x.CreditAmount == 50m);

        var advanceAuditEvents = _auditEvents
            .Where(x => x.PrimaryEntity?.Type == "SupplierAdvance" && x.PrimaryEntity?.Id == created.Id.ToString())
            .ToList();
        Assert.Contains(advanceAuditEvents, x => x.ActionType == AuditActionType.Create && Equals(x.Metadata?["actorUserId"], 101L));
        Assert.Contains(advanceAuditEvents, x => x.ActionType == AuditActionType.Submit && Equals(x.Metadata?["actorUserId"], 101L));
        Assert.Contains(advanceAuditEvents, x => x.ActionType == AuditActionType.Approve
            && Equals(x.Metadata?["journalEntryId"], approved.JournalEntryId)
            && x.Entities.Single().Fields.Any(f => f.FieldName == "JournalEntryId"));
    }

    [Fact]
    public async Task ApplyAdvanceAsync_AndReverseUtilization_RecordAuditWithJournalLinkage()
    {
        _db.SupplierPayableDocuments.Add(new SupplierPayableDocument
        {
            Id = 200,
            BusinessId = 1,
            SupplierId = 10,
            OriginalPayableAmount = 40m,
            Status = SupplierPayableDocumentStatuses.Open,
            SourceDocumentType = "GRN",
            SourceDocumentId = 400,
            SourceReference = "GRN-400",
            SourceJournalEntryId = 500,
            RecognitionDate = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
            DueDate = new DateTime(2026, 4, 9, 0, 0, 0, DateTimeKind.Utc)
        });
        await _db.SaveChangesAsync();

        var advance = await SupplierPaymentCreationTestHarness.CreateAdvanceAsync(_db, new CreateSupplierAdvanceDto
        {
            SupplierId = 10,
            PaymentDate = new DateTime(2026, 3, 16, 0, 0, 0, DateTimeKind.Utc),
            PaymentMethod = "cash",
            PaymentAccountId = 1001,
            Amount = 40m,
            OutletId = 11,
            ReferenceNumber = "ADV-UTIL-001"
        }, actorUserId: 101, _audit.Object);
        await SupplierPaymentCreationTestHarness.SubmitAdvanceAsync(
            _db, advance.Id, 101, _audit.Object);
        await ApproveAdvanceAsync(advance.Id, 101);

        await ApplyAdvanceAsync(advance.Id, new ApplySupplierAdvanceDto
        {
            SupplierPayableDocumentId = 200,
            Amount = 30m,
            EffectiveDate = new DateTime(2026, 3, 18, 0, 0, 0, DateTimeKind.Utc),
            ReferenceNumber = "UTIL-001",
            Notes = "Apply advance"
        }, 102);

        var utilization = await _db.SupplierAdvanceUtilizations.SingleAsync(x => x.Status == "effective");
        Assert.Contains(_auditEvents, x => x.PrimaryEntity?.Type == "SupplierAdvanceUtilization"
            && x.PrimaryEntity?.Id == utilization.Id.ToString()
            && x.ActionType == AuditActionType.Approve
            && Equals(x.Metadata?["journalEntryId"], utilization.JournalEntryId)
            && Equals(x.Metadata?["actorUserId"], 102L));

        await ReverseAdvanceUtilizationAsync(utilization.Id, new ReverseSupplierAdvanceUtilizationDto
        {
            PostingDate = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc),
            Reason = "Supplier credit corrected"
        }, 103);

        Assert.Contains(_auditEvents, x => x.PrimaryEntity?.Type == "SupplierAdvanceUtilization"
            && x.PrimaryEntity?.Id == utilization.Id.ToString()
            && x.ActionType == AuditActionType.Reverse
            && Equals(x.Metadata?["actorUserId"], 103L)
            && x.Entities.Single().Fields.Any(f => f.FieldName == "ReversalJournalEntryId" && f.NewValue != null));
    }

    [Fact]
    public async Task ReverseAsync_ReversesPostedPaymentAndEffectiveAllocationWithoutDeletingHistory()
    {
        var payment = await SupplierPaymentCreationTestHarness.CreateAsync(_db, new CreateSupplierPaymentDto
        {
            SupplierId = 10, PaymentDate = new DateTime(2026, 3, 15), PaymentMethod = "bank", PaymentAccountId = 1021,
            Amount = 75m, OutletId = 11, ReferenceNumber = "PAY-REV-1", Allocations = [new SupplierPaymentAllocationLineDto { BillId = 1001, Amount = 75m }]
        }, 101, _audit.Object);
        var pending = await _db.SupplierPaymentAllocations.SingleAsync(x => x.SupplierPaymentId == payment.Id);
        Assert.Equal("pending", pending.Status);
        await SupplierPaymentCreationTestHarness.SubmitAsync(_db, payment.Id, 101, _audit.Object);
        await ApproveAsync(payment.Id, 101);
        Assert.Equal("effective", (await _db.SupplierPaymentAllocations.SingleAsync(x => x.SupplierPaymentId == payment.Id)).Status);

        var reverseRequest = new ReverseSupplierPaymentDto
        {
            PostingDate = new DateTime(2026, 3, 20),
            Reason = "Duplicate payment"
        };
        var reversed = await ReverseAsync(payment.Id, reverseRequest, 102);
        var allocation = await _db.SupplierPaymentAllocations.SingleAsync(x => x.SupplierPaymentId == payment.Id);

        Assert.Equal("reversed", reversed.Status);
        Assert.True(allocation.IsReversed);
        Assert.Equal("reversed", allocation.Status);
        Assert.NotNull(allocation.JournalEntryId);
        Assert.Equal(new DateTime(2026, 3, 15), allocation.EffectiveDate);
        Assert.Equal(new DateTime(2026, 3, 20), allocation.ReversedEffectiveDate);
        Assert.NotNull(allocation.ReversalJournalEntryId);
        Assert.Equal(2, await _db.JournalEntries.CountAsync(x => x.ReferenceType == "SupplierPayment" || x.ReversalOfJournalEntryId == reversed.JournalEntryId));

        var replay = await ReverseAsync(payment.Id, reverseRequest, 102);
        Assert.Equal("reversed", replay.Status);
        Assert.Equal(2, await _db.JournalEntries.CountAsync(x => x.ReferenceType == "SupplierPayment" || x.ReversalOfJournalEntryId == reversed.JournalEntryId));
        Assert.Single(_auditEvents, x => x.PrimaryEntity?.Type == "SupplierPayment" && x.PrimaryEntity?.Id == payment.Id.ToString() && x.ActionType == AuditActionType.Reverse);
    }

    [Fact]
    public async Task ReverseAsync_RejectsMissingReasonAndClosedReversalPeriod()
    {
        var payment = await SupplierPaymentCreationTestHarness.CreateAsync(_db, new CreateSupplierPaymentDto
        {
            SupplierId = 10, PaymentDate = new DateTime(2026, 3, 15), PaymentMethod = "bank", PaymentAccountId = 1021,
            Amount = 10m, OutletId = 11, ReferenceNumber = "PAY-REV-2", Allocations = [new SupplierPaymentAllocationLineDto { BillId = 1001, Amount = 10m }]
        }, 101, _audit.Object);
        await SupplierPaymentCreationTestHarness.SubmitAsync(_db, payment.Id, 101, _audit.Object);
        await ApproveAsync(payment.Id, 101);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReverseAsync(payment.Id, new ReverseSupplierPaymentDto
            {
                PostingDate = new DateTime(2026, 3, 20)
            }, 102));
        var period = await _db.AccountingPeriods.SingleAsync();
        period.Status = AccountingPeriodStatuses.Closed;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReverseAsync(payment.Id, new ReverseSupplierPaymentDto
            {
                PostingDate = new DateTime(2026, 3, 20),
                Reason = "Closed"
            }, 102));
    }

    private Task ReverseAdvanceUtilizationAsync(
        long utilizationId,
        ReverseSupplierAdvanceUtilizationDto dto,
        long actorUserId)
    {
        var store = new SupplierAdvanceUtilizationStore(
            _db,
            Mock.Of<ISupplierAdvanceUtilizationJournalPoster>(),
            new SupplierPaymentReversalJournalPoster(
                new ManualJournalDraftStore(
                    _db,
                    _audit.Object,
                    _journalNumbers.Object,
                    NullLogger<ManualJournalDraftStore>.Instance)),
            new SupplierPaymentCreationAuditWriter(_audit.Object));
        var handler = new ReverseSupplierAdvanceUtilizationCommandHandler(
            store,
            new SupplierPaymentCreationAuthorizer(_tenant.Object));
        return handler.Handle(
            new ReverseSupplierAdvanceUtilizationCommand(
                utilizationId,
                dto.PostingDate,
                dto.Reason,
                actorUserId),
            CancellationToken.None);
    }

    private Task<SupplierPaymentReadModel> ApproveAsync(long paymentId, long actorUserId)
    {
        var configurationService = AccountingConfigurationServiceTestFactory.Create(
            _db,
            _tenant.Object,
            NullLogger<AccountingConfigurationService>.Instance);
        return SupplierPaymentCreationTestHarness.ApproveAsync(
            _db,
            paymentId,
            actorUserId,
            _audit.Object,
            configurationService,
            _journalNumbers.Object);
    }

    private Task<SupplierPaymentReadModel> ReverseAsync(
        long paymentId,
        ReverseSupplierPaymentDto dto,
        long actorUserId)
    {
        return SupplierPaymentCreationTestHarness.ReverseAsync(
            _db,
            paymentId,
            dto,
            actorUserId,
            _audit.Object,
            _journalNumbers.Object);
    }

    private Task<SupplierAdvanceReadModel> ApproveAdvanceAsync(long advanceId, long actorUserId)
    {
        var configurationService = AccountingConfigurationServiceTestFactory.Create(
            _db,
            _tenant.Object,
            NullLogger<AccountingConfigurationService>.Instance);
        return SupplierPaymentCreationTestHarness.ApproveAdvanceAsync(
            _db,
            advanceId,
            actorUserId,
            _audit.Object,
            configurationService,
            _journalNumbers.Object);
    }

    private Task ApplyAdvanceAsync(
        long advanceId,
        ApplySupplierAdvanceDto dto,
        long actorUserId)
    {
        var configurationService = AccountingConfigurationServiceTestFactory.Create(
            _db,
            _tenant.Object,
            NullLogger<AccountingConfigurationService>.Instance);
        return SupplierPaymentCreationTestHarness.ApplyAdvanceAsync(
            _db,
            advanceId,
            dto,
            actorUserId,
            _audit.Object,
            configurationService,
            _journalNumbers.Object);
    }

    private void Seed()
    {
        _db.Businesses.Add(new Business
        {
            Id = 1,
            Name = "Supplier Payment Business",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.Outlets.Add(new Outlet
        {
            Id = 11,
            BusinessId = 1,
            Name = "Main Outlet",
            Address = "X",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.Suppliers.Add(new Supplier
        {
            Id = 10,
            BusinessId = 1,
            Name = "Acme Supplier",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.Bills.Add(new Bill
        {
            Id = 1001,
            SupplierId = 10,
            AmountDue = 100m,
            DueDate = new DateTime(2026, 3, 30, 0, 0, 0, DateTimeKind.Utc),
            Status = "unpaid"
        });

        _db.ChartAccounts.AddRange(
            Account(1001, "1000", "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(1021, "1020", "Bank", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(2001, "2000", "Supplier Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(3001, "3000", "Owner Capital", ChartAccountTypes.Equity, NormalBalanceTypes.Credit),
            Account(3002, "3001", "Supplier Advance", ChartAccountTypes.Liability, NormalBalanceTypes.Credit)
        );

        _db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            CashAccountId = 1001,
            PettyCashAccountId = 1001,
            DefaultBankAccountId = 1021,
            InventoryAccountId = 1001,
            SalesAccountId = 1001,
            SalesDiscountAccountId = 1001,
            SalesReturnAccountId = 1001,
            CogsAccountId = 1001,
            InventoryShortageAccountId = 1001,
            InventoryGainAccountId = 1001,
            SupplierPayableAccountId = 2001,
            SupplierAdvanceAccountId = 3002,
            CustomerAdvanceAccountId = 2001,
            OtherIncomeAccountId = 1001,
            OwnerCapitalAccountId = 3001,
            OwnerDrawingsAccountId = 3001,
            RoundingDifferenceAccountId = 1001,
            CashDifferenceGainAccountId = 1001,
            CashDifferenceLossAccountId = 1001,
            JournalNumberPrefix = "JR",
            JournalNumberPadding = 6,
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.FinancialYears.Add(new FinancialYear
        {
            Id = 9001,
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
            Id = 9002,
            FinancialYearId = 9001,
            PeriodNumber = 3,
            Name = "March",
            StartDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = AccountingPeriodStatuses.Open,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        _db.SaveChanges();
    }

    private static ChartAccount Account(long id, string code, string name, string type, string normalBalance)
    {
        return new ChartAccount
        {
            Id = id,
            BusinessId = 1,
            AccountCode = code,
            AccountName = name,
            AccountType = type,
            NormalBalanceType = normalBalance,
            IsSystemAccount = false,
            IsControlAccount = false,
            AllowManualPosting = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
