using RetailPOS.Application.AccountingJournals.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.DTOs.SupplierPayment;
using RetailPOS.API.Services;
using RetailPOS.Application.SupplierPayments.Abstractions;
using RetailPOS.Application.SupplierPayments.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.AccountingJournals;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.SupplierPayments;
using Xunit;

namespace RetailPOS.Tests.Accounting;

public sealed class ThrowOnAdvanceUtilisationPersistenceInterceptor : SaveChangesInterceptor
{
    public bool Enabled { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null && eventData.Context.ChangeTracker.Entries<JournalEntryLine>().Any())
        {
            Assert.All(eventData.Context.ChangeTracker.Entries<JournalEntryLine>(), entry =>
            {
                var line = entry.Entity;
                Assert.True(
                    (line.DebitAmount > 0m && line.CreditAmount == 0m) ||
                    (line.CreditAmount > 0m && line.DebitAmount == 0m),
                    $"Invalid runtime JournalEntryLine: JournalEntryId={line.JournalEntryId}, AccountId={line.AccountId}, DebitAmount={line.DebitAmount}, CreditAmount={line.CreditAmount}, State={entry.State}, CreatedBy=JournalEntryService");
            });
        }
        if (Enabled && eventData.Context != null &&
            (eventData.Context.ChangeTracker.Entries<SupplierAdvanceUtilization>().Any(x => x.State is EntityState.Added or EntityState.Modified) ||
             eventData.Context.ChangeTracker.Entries<SupplierAdvance>().Any(x => x.State == EntityState.Modified)))
        {
            Enabled = false;
            throw new InvalidOperationException("Intentional advance-utilisation persistence failure.");
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

internal sealed class SqliteSupplierAdvanceDbContext(DbContextOptions<RetailPOSDbContext> options) : RetailPOSDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<JournalEntryLine>().Property(x => x.DebitAmount).HasConversion<double>();
        modelBuilder.Entity<JournalEntryLine>().Property(x => x.CreditAmount).HasConversion<double>();
    }
}

public sealed class P302SupplierAdvanceUtilisationAtomicityTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ThrowOnAdvanceUtilisationPersistenceInterceptor _interceptor = new();
    private static int s_journalNumber;

    public P302SupplierAdvanceUtilisationAtomicityTests()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
        Seed(db);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task ApproveAdvanceAsync_RollsBackJournalAndAdvance_WhenPersistenceFails_ThenRetriesSafely()
    {
        using (var seed = CreateContext())
        {
            seed.SupplierAdvances.Add(new SupplierAdvance
            {
                Id = 303,
                BusinessId = 1,
                SupplierId = 10,
                PaymentDate = new DateTime(2026, 3, 15),
                BusinessDate = new DateTime(2026, 3, 15),
                PostingDate = new DateTime(2026, 3, 15),
                PaymentMethod = "cash",
                PaymentAccountId = 101,
                Amount = 75m,
                RemainingAmount = 75m,
                AppliedAmount = 0m,
                Status = "submitted",
                ReferenceNumber = "ADV-APPROVE-ROLLBACK"
            });
            await seed.SaveChangesAsync();
        }

        _interceptor.Enabled = true;
        using (var db = CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ApproveAdvanceCommand(db, 303, 101));

        using (var verify = CreateContext())
        {
            var advance = await verify.SupplierAdvances.SingleAsync(x => x.Id == 303);
            Assert.Equal("submitted", advance.Status);
            Assert.Null(advance.JournalEntryId);
            Assert.Equal(75m, advance.RemainingAmount);
            Assert.Equal(0m, advance.AppliedAmount);
            Assert.Equal(0, await verify.JournalEntries.CountAsync(x =>
                x.ReferenceType == "SupplierAdvance" && x.ReferenceId == "303"));
        }

        _interceptor.Enabled = false;
        using (var retry = CreateContext())
        {
            var approved = await ApproveAdvanceCommand(retry, 303, 101);
            Assert.Equal("posted", approved.Status);
            Assert.Equal(75m, approved.Amount);
            Assert.NotNull(approved.JournalEntryId);
        }

        using var final = CreateContext();
        var persisted = await final.SupplierAdvances.SingleAsync(x => x.Id == 303);
        Assert.Equal("posted", persisted.Status);
        Assert.Equal(101L, persisted.ApprovedBy);
        Assert.Equal(101L, persisted.PostedBy);
        Assert.NotNull(persisted.ApprovedAt);
        Assert.NotNull(persisted.PostedAt);
        var journal = await final.JournalEntries
            .Include(x => x.Lines)
            .SingleAsync(x => x.ReferenceType == "SupplierAdvance" && x.ReferenceId == "303");
        Assert.Equal(JournalEntryStatuses.Posted, journal.Status);
        Assert.Contains(journal.Lines, line =>
            line.AccountId == 202 && line.DebitAmount == 75m && line.CreditAmount == 0m);
        Assert.Contains(journal.Lines, line =>
            line.AccountId == 101 && line.DebitAmount == 0m && line.CreditAmount == 75m);
    }

    [Fact]
    public async Task ApplyAdvanceAsync_PartialUtilisation_RollsBackWhenPersistenceFails_ThenRetriesSafely()
    {
        var date = new DateTime(2026, 3, 15);
        var dto = ApplyDto(50, date, "UTIL-PARTIAL");
        _interceptor.Enabled = true;
        using (var db = CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() => ApplyAdvanceCommand(db, 300, dto, 1));

        Assert.Equal((150m, 0m, "open"), await AdvanceState(300, 200));
        _interceptor.Enabled = false;
        using (var db = CreateContext())
            await ApplyAdvanceCommand(db, 300, dto, 1);

        var state = await AdvanceState(300, 200);
        Assert.Equal((100m, 50m, "partial"), state);
        using var verify = CreateContext();
        Assert.Equal(1, await verify.SupplierAdvanceUtilizations.CountAsync(x => x.SupplierAdvanceId == 300));
    }

    [Fact]
    public async Task ApplyAdvanceAsync_FullUtilisationOfAdvance_WhenAdvanceFullyApplied()
    {
        using var db = CreateContext();
        await ApplyAdvanceCommand(db, 300, ApplyDto(150, new DateTime(2026, 3, 15), "UTIL-FULL-ADV"), 1);
        Assert.Equal((0m, 150m, "paid"), await AdvanceState(300, 200));
    }

    [Fact]
    public async Task ApplyAdvanceAsync_FullUtilisationOfPayable_WhenPayableFullySettled()
    {
        using var db = CreateContext();
        var dto = new ApplySupplierAdvanceDto { SupplierPayableDocumentId = 201, Amount = 100, EffectiveDate = new DateTime(2026, 3, 15), ReferenceNumber = "UTIL-FULL-PAY", Notes = "UTIL-FULL-PAY" };
        await ApplyAdvanceCommand(db, 301, dto, 1);
        Assert.Equal((0m, 100m, "paid"), await AdvanceState(301, 201));
    }

    [Fact]
    public async Task ReverseAdvanceUtilizationAsync_RollsBackReversal_WhenPersistenceFails_ThenRetriesSafely()
    {
        var date = new DateTime(2026, 3, 15);
        long utilizationId;
        using (var db = CreateContext())
        {
            await ApplyAdvanceCommand(db, 300, ApplyDto(50, date, "UTIL-REVERSE"), 1);
            utilizationId = await db.SupplierAdvanceUtilizations.Where(x => x.SupplierAdvanceId == 300).Select(x => x.Id).SingleAsync();
        }

        _interceptor.Enabled = true;
        using (var db = CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() => ReverseAdvanceUtilizationCommand(
                db,
                utilizationId,
                new ReverseSupplierAdvanceUtilizationDto { PostingDate = date, Reason = "retry" },
                1));

        Assert.Equal((100m, 50m, "partial"), await AdvanceState(300, 200));
        _interceptor.Enabled = false;
        using (var db = CreateContext())
            await ReverseAdvanceUtilizationCommand(
                db,
                utilizationId,
                new ReverseSupplierAdvanceUtilizationDto { PostingDate = date, Reason = "retry" },
                1);

        Assert.Equal((150m, 0m, "open"), await AdvanceState(300, 200));
    }

    [Fact]
    public async Task ReverseAdvanceUtilizationAsync_LaterPeriodReversal_WhenReversalInDifferentPeriod()
    {
        var march = new DateTime(2026, 3, 15);
        long utilizationId;
        using (var db = CreateContext())
        {
            await ApplyAdvanceCommand(db, 300, ApplyDto(30, march, "UTIL-LATER"), 1);
            utilizationId = await db.SupplierAdvanceUtilizations.Where(x => x.SupplierAdvanceId == 300).Select(x => x.Id).SingleAsync();
        }
        using (var db = CreateContext())
            await ReverseAdvanceUtilizationCommand(
                db,
                utilizationId,
                new ReverseSupplierAdvanceUtilizationDto { PostingDate = new DateTime(2026, 4, 15), Reason = "later" },
                1);

        using var verify = CreateContext();
        var reversal = await verify.SupplierAdvanceUtilizations.SingleAsync(x => x.ReversalOfUtilizationId == utilizationId);
        Assert.Equal("reversal", reversal.Status);
        Assert.Equal(new DateTime(2026, 4, 15), reversal.EffectiveDate);
    }

    [Fact]
    public async Task ReverseAdvanceUtilizationAsync_ClosedPeriodRejection_WhenReversalIntoClosedPeriod()
    {
        var march = new DateTime(2026, 3, 15);
        long utilizationId;
        using (var db = CreateContext())
        {
            await ApplyAdvanceCommand(db, 300, ApplyDto(25, march, "UTIL-CLOSED"), 1);
            utilizationId = await db.SupplierAdvanceUtilizations.Where(x => x.SupplierAdvanceId == 300).Select(x => x.Id).SingleAsync();
        }
        using (var db = CreateContext())
            await Assert.ThrowsAsync<InvalidOperationException>(() => ReverseAdvanceUtilizationCommand(
                db,
                utilizationId,
                new ReverseSupplierAdvanceUtilizationDto { PostingDate = new DateTime(2026, 2, 28), Reason = "closed" },
                1));

        Assert.Equal((125m, 25m, "partial"), await AdvanceState(300, 200));
    }

    [Fact]
    public async Task ReverseAdvanceUtilizationAsync_DuplicateReversal_WhenReversalRepeated_ThenIdempotent()
    {
        var date = new DateTime(2026, 3, 15);
        long utilizationId;
        using (var db = CreateContext())
        {
            await ApplyAdvanceCommand(db, 300, ApplyDto(40, date, "UTIL-IDEMPOTENT"), 1);
            utilizationId = await db.SupplierAdvanceUtilizations.Where(x => x.SupplierAdvanceId == 300).Select(x => x.Id).SingleAsync();
            await ReverseAdvanceUtilizationCommand(
                db,
                utilizationId,
                new ReverseSupplierAdvanceUtilizationDto { PostingDate = date, Reason = "first" },
                1);
        }
        using (var db = CreateContext())
            await Assert.ThrowsAsync<KeyNotFoundException>(() => ReverseAdvanceUtilizationCommand(
                db,
                utilizationId,
                new ReverseSupplierAdvanceUtilizationDto { PostingDate = date, Reason = "second" },
                1));

        using var verify = CreateContext();
        Assert.Equal(1, await verify.SupplierAdvanceUtilizations.CountAsync(x => x.ReversalOfUtilizationId == utilizationId));
    }

    [Fact]
    public async Task ApplyAdvanceAsync_CrossSupplierRejection_WhenAdvanceFromDifferentSupplier()
    {
        using var db = CreateContext();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ApplyAdvanceCommand(db, 302, ApplyDto(25, new DateTime(2026, 3, 15), "UTIL-CROSS"), 1));
        Assert.Equal((50m, 0m, "open"), await AdvanceState(302, 200));
    }

    private static ApplySupplierAdvanceDto ApplyDto(decimal amount, DateTime date, string reference) => new() { SupplierPayableDocumentId = 200, Amount = amount, EffectiveDate = date, ReferenceNumber = reference, Notes = reference };

    private async Task<(decimal Remaining, decimal Applied, string Status)> AdvanceState(long advanceId, long payableId)
    {
        using var db = CreateContext();
        var advance = await db.SupplierAdvances.SingleAsync(x => x.Id == advanceId);
        var payable = await db.SupplierPayableDocuments.SingleAsync(x => x.Id == payableId);
        return (advance.RemainingAmount, advance.AppliedAmount, payable.Status);
    }

    private RetailPOSDbContext CreateContext() => new SqliteSupplierAdvanceDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(_connection).AddInterceptors(_interceptor).Options);

    private static Task<RetailPOS.Application.SupplierPayments.ReadModels.SupplierAdvanceReadModel> ApproveAdvanceCommand(
        RetailPOSDbContext db,
        long advanceId,
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
            .Returns(() => Task.FromResult($"JP-{Interlocked.Increment(ref s_journalNumber)}"));
        var audit = Mock.Of<IAuditService>(value =>
            value.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);
        var configuration = AccountingConfigurationServiceTestFactory.Create(
            db,
            tenant.Object,
            NullLogger<AccountingConfigurationService>.Instance);
        return SupplierPaymentCreationTestHarness.ApproveAdvanceAsync(
            db,
            advanceId,
            actorUserId,
            audit,
            configuration,
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
            .Returns(() => Task.FromResult($"JP-{Interlocked.Increment(ref s_journalNumber)}"));
        var audit = Mock.Of<IAuditService>(value =>
            value.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);
        var configuration = AccountingConfigurationServiceTestFactory.Create(
            db,
            tenant.Object,
            NullLogger<AccountingConfigurationService>.Instance);
        return SupplierPaymentCreationTestHarness.ApplyAdvanceAsync(
            db,
            advanceId,
            dto,
            actorUserId,
            audit,
            configuration,
            numbers.Object);
    }

    private Task ReverseAdvanceUtilizationCommand(
        RetailPOSDbContext db,
        long utilizationId,
        ReverseSupplierAdvanceUtilizationDto dto,
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
            .Returns(() => Task.FromResult($"JP-{Interlocked.Increment(ref s_journalNumber)}"));
        var audit = Mock.Of<IAuditService>(value =>
            value.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);
        var store = new SupplierAdvanceUtilizationStore(
            db,
            Mock.Of<ISupplierAdvanceUtilizationJournalPoster>(),
            new SupplierPaymentReversalJournalPoster(
                new ManualJournalDraftStore(
                    db,
                    audit,
                    numbers.Object,
                    NullLogger<ManualJournalDraftStore>.Instance)),
            new SupplierPaymentCreationAuditWriter(audit));
        var handler = new ReverseSupplierAdvanceUtilizationCommandHandler(
            store,
            new SupplierPaymentCreationAuthorizer(tenant.Object));
        return handler.Handle(
            new ReverseSupplierAdvanceUtilizationCommand(
                utilizationId,
                dto.PostingDate,
                dto.Reason,
                actorUserId),
            CancellationToken.None);
    }

    private static void Seed(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "Test", IsActive = true });
        db.Suppliers.AddRange(new Supplier { Id = 10, BusinessId = 1, Name = "Supplier A" }, new Supplier { Id = 11, BusinessId = 1, Name = "Supplier B" });
        db.Warehouses.Add(new Warehouse { Id = 1, BusinessId = 1, Name = "Warehouse", Address = "Test" });
        db.ChartAccounts.AddRange(
            new ChartAccount { Id = 101, BusinessId = 1, AccountCode = "1001", AccountName = "Cash", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsActive = true },
            new ChartAccount { Id = 201, BusinessId = 1, AccountCode = "2001", AccountName = "Payable", AccountType = ChartAccountTypes.Liability, NormalBalanceType = NormalBalanceTypes.Credit, IsActive = true },
            new ChartAccount { Id = 202, BusinessId = 1, AccountCode = "2010", AccountName = "Advance", AccountType = ChartAccountTypes.Liability, NormalBalanceType = NormalBalanceTypes.Credit, IsActive = true });
        db.FinancialYears.Add(new FinancialYear { Id = 1, BusinessId = 1, YearCode = "FY26", Name = "2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true });
        db.AccountingPeriods.AddRange(new AccountingPeriod { Id = 1, FinancialYearId = 1, PeriodNumber = 2, Name = "Feb", StartDate = new DateTime(2026, 2, 1), EndDate = new DateTime(2026, 2, 28), Status = AccountingPeriodStatuses.Closed }, new AccountingPeriod { Id = 2, FinancialYearId = 1, PeriodNumber = 3, Name = "Mar", StartDate = new DateTime(2026, 3, 1), EndDate = new DateTime(2026, 3, 31), Status = AccountingPeriodStatuses.Open }, new AccountingPeriod { Id = 3, FinancialYearId = 1, PeriodNumber = 4, Name = "Apr", StartDate = new DateTime(2026, 4, 1), EndDate = new DateTime(2026, 4, 30), Status = AccountingPeriodStatuses.Open });
        db.AccountingConfigurations.Add(new AccountingConfiguration { Id = 1, BusinessId = 1, SupplierPayableAccountId = 201, SupplierAdvanceAccountId = 202 });
        db.SaveChanges();

        db.JournalEntries.AddRange(
            new JournalEntry { Id = 1000, BusinessId = 1, JournalNumber = "GRN-1", JournalType = "system", Status = JournalEntryStatuses.Posted, TransactionDate = new DateTime(2026, 3, 1), PostingDate = new DateTime(2026, 3, 1), ReferenceType = "GRN", ReferenceId = "1" },
            new JournalEntry { Id = 1001, BusinessId = 1, JournalNumber = "GRN-2", JournalType = "system", Status = JournalEntryStatuses.Posted, TransactionDate = new DateTime(2026, 3, 1), PostingDate = new DateTime(2026, 3, 1), ReferenceType = "GRN", ReferenceId = "2" });
        db.SaveChanges();

        db.JournalEntryLines.AddRange(
            new JournalEntryLine { Id = 1, JournalEntryId = 1000, AccountId = 101, DebitAmount = 100m, CreditAmount = 0m, Description = "Cash" },
            new JournalEntryLine { Id = 2, JournalEntryId = 1000, AccountId = 201, DebitAmount = 0m, CreditAmount = 100m, Description = "Supplier payable" },
            new JournalEntryLine { Id = 3, JournalEntryId = 1001, AccountId = 101, DebitAmount = 100m, CreditAmount = 0m, Description = "Cash" },
            new JournalEntryLine { Id = 4, JournalEntryId = 1001, AccountId = 201, DebitAmount = 0m, CreditAmount = 100m, Description = "Supplier payable" });
        Assert.All(db.ChangeTracker.Entries<JournalEntryLine>(), entry =>
        {
            var line = entry.Entity;
            Assert.True(
                (line.DebitAmount > 0m && line.CreditAmount == 0m) ||
                (line.CreditAmount > 0m && line.DebitAmount == 0m),
                $"Invalid JournalEntryLine: JournalEntryId={line.JournalEntryId}, AccountId={line.AccountId}, DebitAmount={line.DebitAmount}, CreditAmount={line.CreditAmount}, State={entry.State}, CreatedBy=Seed");
        });
        db.SaveChanges();

        db.SupplierPayableDocuments.AddRange(new SupplierPayableDocument { Id = 200, BusinessId = 1, SupplierId = 10, OriginalPayableAmount = 150, Status = "open", SourceDocumentId = 1, SourceDocumentType = "GRN", SourceReference = "GRN-1", SourceJournalEntryId = 1000, RecognitionDate = new DateTime(2026, 3, 1), PaymentTermsDays = 30, DueDate = new DateTime(2026, 3, 31) }, new SupplierPayableDocument { Id = 201, BusinessId = 1, SupplierId = 10, OriginalPayableAmount = 100, Status = "open", SourceDocumentId = 2, SourceDocumentType = "GRN", SourceReference = "GRN-2", SourceJournalEntryId = 1001, RecognitionDate = new DateTime(2026, 3, 1), PaymentTermsDays = 30, DueDate = new DateTime(2026, 3, 31) });
        db.SaveChanges();

        db.SupplierAdvances.AddRange(new SupplierAdvance { Id = 300, BusinessId = 1, SupplierId = 10, PaymentDate = new DateTime(2026, 3, 1), PostingDate = new DateTime(2026, 3, 1), BusinessDate = new DateTime(2026, 3, 1), PaymentMethod = "cash", PaymentAccountId = 101, Amount = 150, RemainingAmount = 150, AppliedAmount = 0, Status = "posted", ReferenceNumber = "ADV-1" }, new SupplierAdvance { Id = 301, BusinessId = 1, SupplierId = 10, PaymentDate = new DateTime(2026, 3, 1), PostingDate = new DateTime(2026, 3, 1), BusinessDate = new DateTime(2026, 3, 1), PaymentMethod = "cash", PaymentAccountId = 101, Amount = 100, RemainingAmount = 100, AppliedAmount = 0, Status = "posted", ReferenceNumber = "ADV-2" }, new SupplierAdvance { Id = 302, BusinessId = 1, SupplierId = 11, PaymentDate = new DateTime(2026, 3, 1), PostingDate = new DateTime(2026, 3, 1), BusinessDate = new DateTime(2026, 3, 1), PaymentMethod = "cash", PaymentAccountId = 101, Amount = 50, RemainingAmount = 50, AppliedAmount = 0, Status = "posted", ReferenceNumber = "ADV-3" });
        db.SaveChanges();
    }
}
