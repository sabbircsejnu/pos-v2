using RetailPOS.Application.AccountingJournals.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.DTOs.SupplierPayment;
using RetailPOS.API.Services;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using Xunit;

namespace RetailPOS.Tests.Accounting;

internal enum SupplierPaymentReversalFailureBoundary
{
    None,
    ReversalJournal,
    OriginalJournalState,
    AllocationRestoration,
    AdvanceReversal
}

internal sealed class ThrowOnSupplierPaymentReversalPersistenceInterceptor : SaveChangesInterceptor
{
    public SupplierPaymentReversalFailureBoundary Boundary { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null && ShouldThrow(eventData.Context))
            throw new InvalidOperationException($"Injected supplier payment reversal failure at {Boundary} boundary.");

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private bool ShouldThrow(DbContext context)
    {
        var entries = context.ChangeTracker;
        var matches = Boundary switch
        {
            SupplierPaymentReversalFailureBoundary.ReversalJournal => entries.Entries<JournalEntry>().Any(x => x.State == EntityState.Added && x.Entity.JournalType == AccountingJournalTypes.JournalReversal),
            SupplierPaymentReversalFailureBoundary.OriginalJournalState => entries.Entries<JournalEntry>().Any(x => x.State == EntityState.Modified && x.Entity.Status == JournalEntryStatuses.Reversed),
            SupplierPaymentReversalFailureBoundary.AllocationRestoration => entries.Entries<SupplierPaymentAllocation>().Any(x => x.State == EntityState.Modified && x.Entity.IsReversed),
            SupplierPaymentReversalFailureBoundary.AdvanceReversal => entries.Entries<SupplierAdvance>().Any(x => x.State == EntityState.Modified && x.Entity.Status == "reversed"),
            _ => false
        };

        if (matches)
        {
            Boundary = SupplierPaymentReversalFailureBoundary.None;
            return true;
        }

        return false;
    }
}

internal sealed class SqliteSupplierPaymentReversalDbContext(DbContextOptions<RetailPOSDbContext> options) : RetailPOSDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<JournalEntryLine>().Property(x => x.DebitAmount).HasConversion<double>();
        modelBuilder.Entity<JournalEntryLine>().Property(x => x.CreditAmount).HasConversion<double>();
    }
}

public sealed class P302SupplierPaymentReversalAtomicityTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ThrowOnSupplierPaymentReversalPersistenceInterceptor _interceptor = new();
    private static int s_journalNumber;

    public P302SupplierPaymentReversalAtomicityTests()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
        Seed(db);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task ReverseAsync_ReversalJournalBoundaryFailure_RollsBackEverything_ThenRetriesWithoutDuplicate()
    {
        var paymentId = await CreatePostedPaymentAsync(60, [(200, 60)]);
        var originalJournalId = await GetPaymentJournalId(paymentId);
        _interceptor.Boundary = SupplierPaymentReversalFailureBoundary.ReversalJournal;

        using (var db = CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReversePaymentAsync(db, paymentId, ReverseDto(new DateTime(2026, 4, 15), "journal failure"), 1));

        await AssertUnchangedAfterFailedReversal(paymentId, originalJournalId, 1, 200);

        _interceptor.Boundary = SupplierPaymentReversalFailureBoundary.None;
        using (var db = CreateContext())
            await ReversePaymentAsync(db, paymentId, ReverseDto(new DateTime(2026, 4, 15), "retry"), 1);

        await AssertReversedAsync(paymentId, originalJournalId, [200], new DateTime(2026, 4, 15));
        using (var db = CreateContext())
            await ReversePaymentAsync(db, paymentId, ReverseDto(new DateTime(2026, 4, 15), "duplicate"), 1);
        await AssertReversedAsync(paymentId, originalJournalId, [200], new DateTime(2026, 4, 15));
    }

    [Fact]
    public async Task ReverseAsync_OriginalJournalStateBoundaryFailure_RollsBackJournalAndPaymentState()
    {
        var paymentId = await CreatePostedPaymentAsync(60, [(200, 60)]);
        var originalJournalId = await GetPaymentJournalId(paymentId);
        _interceptor.Boundary = SupplierPaymentReversalFailureBoundary.OriginalJournalState;

        using (var db = CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReversePaymentAsync(db, paymentId, ReverseDto(new DateTime(2026, 4, 15), "original state failure"), 1));

        await AssertUnchangedAfterFailedReversal(paymentId, originalJournalId, 1, 200);
    }

    [Fact]
    public async Task ReverseAsync_MultiAllocationRestorationBoundaryFailure_RollsBackBothAllocationsAndPayables_ThenRetries()
    {
        var paymentId = await CreatePostedPaymentAsync(100, [(200, 50), (201, 50)]);
        var originalJournalId = await GetPaymentJournalId(paymentId);
        _interceptor.Boundary = SupplierPaymentReversalFailureBoundary.AllocationRestoration;

        using (var db = CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReversePaymentAsync(db, paymentId, ReverseDto(new DateTime(2026, 4, 15), "allocation failure"), 1));

        await AssertUnchangedAfterFailedReversal(paymentId, originalJournalId, 2, 200, 201);
        _interceptor.Boundary = SupplierPaymentReversalFailureBoundary.None;
        using (var db = CreateContext())
            await ReversePaymentAsync(db, paymentId, ReverseDto(new DateTime(2026, 4, 15), "retry"), 1);
        await AssertReversedAsync(paymentId, originalJournalId, [200, 201], new DateTime(2026, 4, 15));
    }

    [Fact]
    public async Task ReverseAsync_PartialPaymentAgainstSharedPayable_ReversesOnlySelectedPayment()
    {
        var firstPaymentId = await CreatePostedPaymentAsync(50, [(200, 50)]);
        var secondPaymentId = await CreatePostedPaymentAsync(30, [(200, 30)]);
        var firstJournalId = await GetPaymentJournalId(firstPaymentId);
        using (var db = CreateContext())
            await ReversePaymentAsync(db, firstPaymentId, ReverseDto(new DateTime(2026, 4, 15), "partial"), 1);

        using var verify = CreateContext();
        var first = await verify.SupplierPaymentAllocations.SingleAsync(x => x.SupplierPaymentId == firstPaymentId);
        var second = await verify.SupplierPaymentAllocations.SingleAsync(x => x.SupplierPaymentId == secondPaymentId);
        Assert.Equal("reversed", first.Status);
        Assert.Equal("effective", second.Status);
        Assert.Equal(firstJournalId, first.JournalEntryId);
        Assert.NotNull(first.ReversalJournalEntryId);
        Assert.Equal(200, first.SupplierPayableDocumentId);
        Assert.Equal(SupplierPayableDocumentStatuses.Partial, (await verify.SupplierPayableDocuments.SingleAsync(x => x.Id == 200)).Status);
    }

    [Fact]
    public async Task ReverseAsync_AdvanceReversalBoundaryFailure_RollsBackPaymentAllocationAdvanceAndJournals_ThenRetries()
    {
        var paymentId = await CreatePostedPaymentAsync(100, [(200, 60)]);
        var advanceId = await GetAdvanceId(paymentId);
        var originalJournalId = await GetPaymentJournalId(paymentId);
        _interceptor.Boundary = SupplierPaymentReversalFailureBoundary.AdvanceReversal;

        using (var db = CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReversePaymentAsync(db, paymentId, ReverseDto(new DateTime(2026, 4, 15), "advance failure"), 1));

        await AssertUnchangedAfterFailedReversal(paymentId, originalJournalId, 1, 200);
        using (var db = CreateContext())
        {
            var advance = await db.SupplierAdvances.SingleAsync(x => x.Id == advanceId);
            Assert.Equal("posted", advance.Status);
            Assert.Equal(40m, advance.RemainingAmount);
            Assert.Equal(0m, advance.AppliedAmount);
            Assert.Empty(await db.JournalEntries.Where(x => x.ReversalOfJournalEntryId == advance.JournalEntryId).ToListAsync());
        }

        _interceptor.Boundary = SupplierPaymentReversalFailureBoundary.None;
        using (var db = CreateContext())
            await ReversePaymentAsync(db, paymentId, ReverseDto(new DateTime(2026, 4, 15), "retry"), 1);

        await AssertReversedAsync(paymentId, originalJournalId, [200], new DateTime(2026, 4, 15));
        using (var db = CreateContext())
        {
            var advance = await db.SupplierAdvances.SingleAsync(x => x.Id == advanceId);
            Assert.Equal("reversed", advance.Status);
            Assert.Equal(0m, advance.RemainingAmount);
            Assert.Single(await db.JournalEntries.Where(x => x.ReversalOfJournalEntryId == advance.JournalEntryId).ToListAsync());
        }
    }

    [Fact]
    public async Task ReverseAsync_DependentAdvanceUtilisation_IsRejectedBeforeAnyMutation()
    {
        var paymentId = await CreatePostedPaymentAsync(100, [(200, 60)]);
        var advanceId = await GetAdvanceId(paymentId);
        using (var db = CreateContext())
            await ApplyAdvanceCommand(db, advanceId, new ApplySupplierAdvanceDto
            {
                SupplierPayableDocumentId = 201,
                Amount = 10,
                EffectiveDate = new DateTime(2026, 3, 20),
                ReferenceNumber = "DEPENDENT-1",
                Notes = "dependent"
            }, 1);
        var originalJournalId = await GetPaymentJournalId(paymentId);

        using (var db = CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReversePaymentAsync(db, paymentId, ReverseDto(new DateTime(2026, 4, 15), "dependent"), 1));

        await AssertUnchangedAfterFailedReversal(paymentId, originalJournalId, 1, 200);
        using var verify = CreateContext();
        var advance = await verify.SupplierAdvances.SingleAsync(x => x.Id == advanceId);
        Assert.Equal("posted", advance.Status);
        Assert.Empty(await verify.JournalEntries.Where(x => x.ReversalOfJournalEntryId == originalJournalId || x.ReversalOfJournalEntryId == advance.JournalEntryId).ToListAsync());
        Assert.Equal("effective", (await verify.SupplierAdvanceUtilizations.SingleAsync(x => x.SupplierAdvanceId == advanceId)).Status);
    }

    [Fact]
    public async Task ReverseAsync_LaterOpenPeriodPreservesOriginalDate_AndClosedPeriodRejectsWithoutMutation()
    {
        var paymentId = await CreatePostedPaymentAsync(60, [(200, 60)]);
        var originalJournalId = await GetPaymentJournalId(paymentId);
        using (var db = CreateContext())
            await ReversePaymentAsync(db, paymentId, ReverseDto(new DateTime(2026, 4, 15), "later period"), 1);

        using (var verify = CreateContext())
        {
            var original = await verify.JournalEntries.SingleAsync(x => x.Id == originalJournalId);
            var reversal = await verify.JournalEntries.SingleAsync(x => x.ReversalOfJournalEntryId == originalJournalId);
            Assert.Equal(new DateTime(2026, 3, 15), original.PostingDate);
            Assert.Equal(new DateTime(2026, 4, 15), reversal.PostingDate);
        }

        var closedPaymentId = await CreatePostedPaymentAsync(20, [(201, 20)]);
        var closedJournalId = await GetPaymentJournalId(closedPaymentId);
        using (var db = CreateContext())
        {
            var april = await db.AccountingPeriods.SingleAsync(x => x.Id == 3);
            april.Status = AccountingPeriodStatuses.Closed;
            await db.SaveChangesAsync();
        }
        using (var db = CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ReversePaymentAsync(db, closedPaymentId, ReverseDto(new DateTime(2026, 4, 15), "closed"), 1));
        await AssertUnchangedAfterFailedReversal(closedPaymentId, closedJournalId, 1, 201);
    }

    private async Task<long> CreatePostedPaymentAsync(decimal amount, IReadOnlyCollection<(long PayableId, decimal Amount)> allocations)
    {
        using var db = CreateContext();
        var created = await SupplierPaymentCreationTestHarness.CreateAsync(db, new CreateSupplierPaymentDto
        {
            SupplierId = 10, PaymentDate = new DateTime(2026, 3, 15), PaymentMethod = "cash", PaymentAccountId = 101,
            Amount = amount, ReferenceNumber = $"PAY-{Interlocked.Increment(ref s_journalNumber)}",
            Allocations = allocations.Select(x => new SupplierPaymentAllocationLineDto { SupplierPayableDocumentId = x.PayableId, Amount = x.Amount }).ToList()
        }, 1, Mock.Of<IAuditService>());
        await SupplierPaymentCreationTestHarness.SubmitAsync(db, created.Id, 1, Mock.Of<IAuditService>());
        await ApproveSupplierPaymentAsync(db, created.Id, 1);
        var payableIds = allocations.Select(x => x.PayableId).Distinct().ToArray();
        foreach (var payableId in payableIds)
        {
            var payable = await db.SupplierPayableDocuments.SingleAsync(x => x.Id == payableId);
            var effectiveAmount = await db.SupplierPaymentAllocations
                .Where(x => x.SupplierPayableDocumentId == payableId && x.Status == "effective" && !x.IsReversed)
                .SumAsync(x => x.AllocatedAmount);
            payable.Status = effectiveAmount == 0m
                ? SupplierPayableDocumentStatuses.Open
                : effectiveAmount >= payable.OriginalPayableAmount
                    ? SupplierPayableDocumentStatuses.Paid
                    : SupplierPayableDocumentStatuses.Partial;
        }
        await db.SaveChangesAsync();
        return created.Id;
    }

    private async Task<long> GetPaymentJournalId(long paymentId)
    {
        using var db = CreateContext();
        return (await db.SupplierPayments.SingleAsync(x => x.Id == paymentId)).JournalEntryId!.Value;
    }

    private async Task<long> GetAdvanceId(long paymentId)
    {
        using var db = CreateContext();
        return (await db.SupplierAdvances.SingleAsync(x => x.SourcePaymentId == paymentId)).Id;
    }

    private async Task AssertUnchangedAfterFailedReversal(long paymentId, long originalJournalId, int allocationCount, params long[] payableIds)
    {
        using var db = CreateContext();
        var payment = await db.SupplierPayments.SingleAsync(x => x.Id == paymentId);
        Assert.Equal("posted", payment.Status);
        Assert.Null(payment.ReversalJournalEntryId);
        var original = await db.JournalEntries.SingleAsync(x => x.Id == originalJournalId);
        Assert.Equal(JournalEntryStatuses.Posted, original.Status);
        Assert.Null(original.ReversedByJournalEntryId);
        Assert.Empty(await db.JournalEntries.Where(x => x.ReversalOfJournalEntryId == originalJournalId).ToListAsync());
        var allocations = await db.SupplierPaymentAllocations.Where(x => x.SupplierPaymentId == paymentId).ToListAsync();
        Assert.Equal(allocationCount, allocations.Count);
        Assert.All(allocations, allocation =>
        {
            Assert.Equal("effective", allocation.Status);
            Assert.False(allocation.IsReversed);
            Assert.Null(allocation.ReversedEffectiveDate);
            Assert.Null(allocation.ReversalJournalEntryId);
        });
        foreach (var payableId in payableIds)
            Assert.Equal(SupplierPayableDocumentStatuses.Partial, (await db.SupplierPayableDocuments.SingleAsync(x => x.Id == payableId)).Status);
    }

    private async Task AssertReversedAsync(long paymentId, long originalJournalId, IReadOnlyCollection<long> payableIds, DateTime reversalDate)
    {
        using var db = CreateContext();
        var payment = await db.SupplierPayments.SingleAsync(x => x.Id == paymentId);
        Assert.Equal("reversed", payment.Status);
        Assert.Single(await db.JournalEntries.Where(x => x.ReversalOfJournalEntryId == originalJournalId).ToListAsync());
        var allocations = await db.SupplierPaymentAllocations.Where(x => x.SupplierPaymentId == paymentId).ToListAsync();
        Assert.All(allocations, allocation =>
        {
            Assert.Equal("reversed", allocation.Status);
            Assert.True(allocation.IsReversed);
            Assert.Equal(new DateTime(2026, 3, 15), allocation.EffectiveDate);
            Assert.Equal(reversalDate, allocation.ReversedEffectiveDate);
            Assert.NotNull(allocation.ReversalJournalEntryId);
        });
        foreach (var payableId in payableIds)
            Assert.Equal(SupplierPayableDocumentStatuses.Open, (await db.SupplierPayableDocuments.SingleAsync(x => x.Id == payableId)).Status);
    }

    private static ReverseSupplierPaymentDto ReverseDto(DateTime date, string reason) => new() { PostingDate = date, Reason = reason };

    private RetailPOSDbContext CreateContext() => new SqliteSupplierPaymentReversalDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(_connection).AddInterceptors(_interceptor).Options);

    private Task<RetailPOS.Application.SupplierPayments.ReadModels.SupplierPaymentReadModel> ReversePaymentAsync(
        RetailPOSDbContext db,
        long paymentId,
        ReverseSupplierPaymentDto dto,
        long actorUserId)
    {
        var tenant = new Mock<ITenantAccessService>();
        tenant.Setup(x => x.RequireBusinessId()).Returns(1L);
        tenant.Setup(x => x.IsSuperAdmin).Returns(false);
        var numbers = new Mock<IJournalNumberAllocator>();
        numbers.Setup(x => x.AllocateNextJournalNumberAsync(
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<DateTime>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult($"REV-{Interlocked.Increment(ref s_journalNumber)}"));
        var audit = Mock.Of<IAuditService>(x =>
            x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);
        return SupplierPaymentCreationTestHarness.ReverseAsync(
            db,
            paymentId,
            dto,
            actorUserId,
            audit,
            numbers.Object);
    }

    private Task<RetailPOS.Application.SupplierPayments.ReadModels.SupplierPaymentReadModel> ApproveSupplierPaymentAsync(
        RetailPOSDbContext db,
        long paymentId,
        long actorUserId)
    {
        var tenant = new Mock<ITenantAccessService>();
        tenant.Setup(x => x.RequireBusinessId()).Returns(1L);
        tenant.Setup(x => x.IsSuperAdmin).Returns(false);
        var numbers = new Mock<IJournalNumberAllocator>();
        numbers.Setup(x => x.AllocateNextJournalNumberAsync(
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<DateTime>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult($"REV-{Interlocked.Increment(ref s_journalNumber)}"));
        var audit = Mock.Of<IAuditService>(x =>
            x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);
        var config = AccountingConfigurationServiceTestFactory.Create(
            db,
            tenant.Object,
            NullLogger<AccountingConfigurationService>.Instance);
        return SupplierPaymentCreationTestHarness.ApproveAsync(
            db,
            paymentId,
            actorUserId,
            audit,
            config,
            numbers.Object);
    }

    private Task ApplyAdvanceCommand(
        RetailPOSDbContext db,
        long advanceId,
        ApplySupplierAdvanceDto dto,
        long actorUserId)
    {
        var tenant = new Mock<ITenantAccessService>();
        tenant.Setup(value => value.RequireBusinessId()).Returns(1L);
        var numbers = new Mock<IJournalNumberAllocator>();
        numbers.Setup(value => value.AllocateNextJournalNumberAsync(
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<DateTime>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult($"REV-{Interlocked.Increment(ref s_journalNumber)}"));
        var audit = Mock.Of<IAuditService>(value =>
            value.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);
        var config = AccountingConfigurationServiceTestFactory.Create(
            db,
            tenant.Object,
            NullLogger<AccountingConfigurationService>.Instance);
        return SupplierPaymentCreationTestHarness.ApplyAdvanceAsync(
            db,
            advanceId,
            dto,
            actorUserId,
            audit,
            config,
            numbers.Object);
    }

    private static void Seed(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Reversal test business", IsActive = true });
        db.Suppliers.Add(new Supplier { Id = 10, BusinessId = 1, Name = "Supplier" });
        db.Warehouses.Add(new Warehouse { Id = 1, BusinessId = 1, Name = "Warehouse", Address = "Test" });
        db.ChartAccounts.AddRange(
            Account(101, "Cash", ChartAccountTypes.Asset, NormalBalanceTypes.Debit),
            Account(201, "Payable", ChartAccountTypes.Liability, NormalBalanceTypes.Credit),
            Account(202, "Advance", ChartAccountTypes.Liability, NormalBalanceTypes.Credit));
        db.AccountingConfigurations.Add(new AccountingConfiguration { Id = 1, BusinessId = 1, SupplierPayableAccountId = 201, SupplierAdvanceAccountId = 202 });
        db.FinancialYears.Add(new FinancialYear { Id = 1, BusinessId = 1, YearCode = "FY26", Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true });
        db.AccountingPeriods.AddRange(
            new AccountingPeriod { Id = 2, FinancialYearId = 1, PeriodNumber = 3, Name = "March", StartDate = new DateTime(2026, 3, 1), EndDate = new DateTime(2026, 3, 31), Status = AccountingPeriodStatuses.Open },
            new AccountingPeriod { Id = 3, FinancialYearId = 1, PeriodNumber = 4, Name = "April", StartDate = new DateTime(2026, 4, 1), EndDate = new DateTime(2026, 4, 30), Status = AccountingPeriodStatuses.Open });
        db.JournalEntries.AddRange(
            new JournalEntry { Id = 1000, BusinessId = 1, JournalNumber = "GRN-1", JournalType = "system", Status = JournalEntryStatuses.Posted, TransactionDate = new DateTime(2026, 3, 1), PostingDate = new DateTime(2026, 3, 1), ReferenceType = "GRN", ReferenceId = "1" },
            new JournalEntry { Id = 1001, BusinessId = 1, JournalNumber = "GRN-2", JournalType = "system", Status = JournalEntryStatuses.Posted, TransactionDate = new DateTime(2026, 3, 1), PostingDate = new DateTime(2026, 3, 1), ReferenceType = "GRN", ReferenceId = "2" });
        db.SaveChanges();
        db.JournalEntryLines.AddRange(
            new JournalEntryLine { Id = 1, JournalEntryId = 1000, AccountId = 101, DebitAmount = 100, CreditAmount = 0, Description = "Cash" },
            new JournalEntryLine { Id = 2, JournalEntryId = 1000, AccountId = 201, DebitAmount = 0, CreditAmount = 100, Description = "Payable" },
            new JournalEntryLine { Id = 3, JournalEntryId = 1001, AccountId = 101, DebitAmount = 100, CreditAmount = 0, Description = "Cash" },
            new JournalEntryLine { Id = 4, JournalEntryId = 1001, AccountId = 201, DebitAmount = 0, CreditAmount = 100, Description = "Payable" });
        db.SupplierPayableDocuments.AddRange(
            new SupplierPayableDocument { Id = 200, BusinessId = 1, SupplierId = 10, OriginalPayableAmount = 100, Status = "open", SourceDocumentId = 1, SourceDocumentType = "GRN", SourceReference = "GRN-1", SourceJournalEntryId = 1000, RecognitionDate = new DateTime(2026, 3, 1), PaymentTermsDays = 30, DueDate = new DateTime(2026, 3, 31) },
            new SupplierPayableDocument { Id = 201, BusinessId = 1, SupplierId = 10, OriginalPayableAmount = 100, Status = "open", SourceDocumentId = 2, SourceDocumentType = "GRN", SourceReference = "GRN-2", SourceJournalEntryId = 1001, RecognitionDate = new DateTime(2026, 3, 1), PaymentTermsDays = 30, DueDate = new DateTime(2026, 3, 31) });
        db.SaveChanges();
    }

    private static ChartAccount Account(long id, string name, string type, string normalBalance) => new() { Id = id, BusinessId = 1, AccountCode = id.ToString(), AccountName = name, AccountType = type, NormalBalanceType = normalBalance, IsActive = true };
}