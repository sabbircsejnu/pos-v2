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
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;

namespace RetailPOS.Tests.Accounting;

public class P302SupplierPaymentAllocationAtomicityTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ThrowOnAllocationActivationInterceptor _interceptor = new();

    public P302SupplierPaymentAllocationAtomicityTests()
    {
        _connection.Open();
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA ignore_check_constraints = 1;";
            command.ExecuteNonQuery();
        }
        using var db = CreateContext();
        db.Database.EnsureCreated();
        Seed(db);
    }

    [Fact]
    public async Task ApproveAsync_RollsBackJournalAndAllocations_WhenAllocationActivationPersistenceFails_ThenRetriesSafely()
    {
        // Create and submit a payment with one allocation to a payable document.
        long paymentId;
        using (var ctx = CreateContext())
        {
            var createDto = new CreateSupplierPaymentDto
            {
                SupplierId = 10,
                PaymentDate = new DateTime(2026, 3, 15),
                PaymentMethod = "cash",
                Amount = 50m,
                PaymentAccountId = 401,
                Allocations = [new SupplierPaymentAllocationLineDto { SupplierPayableDocumentId = 100, Amount = 50m }]
            };
            var created = await SupplierPaymentCreationTestHarness.CreateAsync(
                ctx,
                createDto,
                1,
                Mock.Of<IAuditService>());
            paymentId = created.Id;
            await SupplierPaymentCreationTestHarness.SubmitAsync(
                ctx,
                paymentId,
                1,
                Mock.Of<IAuditService>());
        }

        // Inject failure and attempt approval, which should roll back everything.
        using (var failedContext = CreateContext())
        {
            _interceptor.Enabled = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ApproveSupplierPaymentAsync(failedContext, paymentId, 1));
        }

        // Fresh context verifies complete rollback.
        using (var verifyContext = CreateContext())
        {
            var payment = await verifyContext.SupplierPayments.SingleAsync(x => x.Id == paymentId);
            Assert.Equal("submitted", payment.Status);
            Assert.Null(payment.JournalEntryId);
            Assert.Null(payment.ApprovedAt);

            Assert.Empty(await verifyContext.JournalEntries.Where(x => x.ReferenceType == "SupplierPayment" && x.ReferenceId == paymentId.ToString()).ToListAsync());
            var paymentLines = await verifyContext.JournalEntryLines.Where(x => x.JournalEntry.ReferenceType == "SupplierPayment").ToListAsync();
            Assert.Empty(paymentLines);

            var allocations = await verifyContext.SupplierPaymentAllocations.Where(x => x.SupplierPaymentId == paymentId).ToListAsync();
            Assert.NotEmpty(allocations);
            foreach (var alloc in allocations)
            {
                Assert.Equal("pending", alloc.Status);
                Assert.Null(alloc.EffectiveDate);
                Assert.Null(alloc.JournalEntryId);
            }

            var payable = await verifyContext.SupplierPayableDocuments.SingleAsync(x => x.Id == 100);
            Assert.Equal(50m, payable.OriginalPayableAmount);
        }

        // Disable fault and retry; should succeed exactly once.
        _interceptor.Enabled = false;
        using (var retryContext = CreateContext())
        {
            var result = await ApproveSupplierPaymentAsync(retryContext, paymentId, 1);
            Assert.Equal("posted", result.Status);
        }

        // Fresh context verifies complete success with exact counts.
        using var finalContext = CreateContext();
        var finalPayment = await finalContext.SupplierPayments.SingleAsync(x => x.Id == paymentId);
        Assert.Equal("posted", finalPayment.Status);
        Assert.NotNull(finalPayment.JournalEntryId);
        Assert.NotNull(finalPayment.ApprovedAt);

        var paymentJournal = await finalContext.JournalEntries.SingleAsync(x => x.ReferenceType == "SupplierPayment" && x.ReferenceId == paymentId.ToString() && x.Status == JournalEntryStatuses.Posted);
        Assert.Equal(2, await finalContext.JournalEntryLines.CountAsync(x => x.JournalEntryId == paymentJournal.Id));

        var finalAllocations = await finalContext.SupplierPaymentAllocations.Where(x => x.SupplierPaymentId == paymentId).ToListAsync();
        Assert.NotEmpty(finalAllocations);
        foreach (var alloc in finalAllocations)
        {
            Assert.Equal("effective", alloc.Status);
            Assert.Equal(new DateTime(2026, 3, 15), alloc.EffectiveDate);
            Assert.Equal(paymentJournal.Id, alloc.JournalEntryId);
        }
    }

    [Fact]
    public async Task ApproveAsync_RollsBackBothAllocations_WhenMultiplePayablesAndActivationFails()
    {
        // Create and submit a payment with two allocations across two payable documents.
        long paymentId;
        using (var ctx = CreateContext())
        {
            var createDto = new CreateSupplierPaymentDto
            {
                SupplierId = 10,
                PaymentDate = new DateTime(2026, 3, 15),
                PaymentMethod = "cash",
                Amount = 100m,
                PaymentAccountId = 401,
                Allocations =
                [
                    new SupplierPaymentAllocationLineDto { SupplierPayableDocumentId = 100, Amount = 50m },
                    new SupplierPaymentAllocationLineDto { SupplierPayableDocumentId = 101, Amount = 50m }
                ]
            };
            var created = await SupplierPaymentCreationTestHarness.CreateAsync(
                ctx,
                createDto,
                1,
                Mock.Of<IAuditService>());
            paymentId = created.Id;
            await SupplierPaymentCreationTestHarness.SubmitAsync(
                ctx,
                paymentId,
                1,
                Mock.Of<IAuditService>());
        }

        // Inject failure; both allocations should remain pending after rollback.
        using (var failedContext = CreateContext())
        {
            _interceptor.Enabled = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ApproveSupplierPaymentAsync(failedContext, paymentId, 1));
        }

        // Verify both allocations are still pending.
        using (var verifyContext = CreateContext())
        {
            var allocations = await verifyContext.SupplierPaymentAllocations.Where(x => x.SupplierPaymentId == paymentId).OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(2, allocations.Count);
            foreach (var alloc in allocations)
            {
                Assert.Equal("pending", alloc.Status);
                Assert.Null(alloc.EffectiveDate);
                Assert.Null(alloc.JournalEntryId);
            }

            var paymentLines = await verifyContext.JournalEntryLines.Where(x => x.JournalEntry.ReferenceType == "SupplierPayment").ToListAsync();
            Assert.Empty(paymentLines);
        }

        // Retry without fault; both should activate together.
        _interceptor.Enabled = false;
        using (var retryContext = CreateContext())
        {
            var result = await ApproveSupplierPaymentAsync(retryContext, paymentId, 1);
            Assert.Equal("posted", result.Status);
        }

        // Verify both allocations are now effective with same journal linkage.
        using var finalContext = CreateContext();
        var finalAllocations = await finalContext.SupplierPaymentAllocations.Where(x => x.SupplierPaymentId == paymentId).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, finalAllocations.Count);
        
        var journalId = finalAllocations[0].JournalEntryId;
        foreach (var alloc in finalAllocations)
        {
            Assert.Equal("effective", alloc.Status);
            Assert.Equal(journalId, alloc.JournalEntryId);
            Assert.Equal(new DateTime(2026, 3, 15), alloc.EffectiveDate);
        }
    }

    [Fact]
    public async Task ApproveAsync_RollsBackPartialPayment_WhenActivationFails()
    {
        // Create a payable with 50, allocate only 30 from payment of 100 (leaving 70 as advance).
        long paymentId;
        using (var ctx = CreateContext())
        {
            var createDto = new CreateSupplierPaymentDto
            {
                SupplierId = 10,
                PaymentDate = new DateTime(2026, 3, 15),
                PaymentMethod = "cash",
                Amount = 100m,
                PaymentAccountId = 401,
                Allocations = [new SupplierPaymentAllocationLineDto { SupplierPayableDocumentId = 100, Amount = 30m }]
            };
            var created = await SupplierPaymentCreationTestHarness.CreateAsync(
                ctx,
                createDto,
                1,
                Mock.Of<IAuditService>());
            paymentId = created.Id;
            await SupplierPaymentCreationTestHarness.SubmitAsync(
                ctx,
                paymentId,
                1,
                Mock.Of<IAuditService>());
        }

        // Verify pre-rollback state: payable is 50.
        using (var preCtx = CreateContext())
        {
            var payable = await preCtx.SupplierPayableDocuments.SingleAsync(x => x.Id == 100);
            Assert.Equal(50m, payable.OriginalPayableAmount);
        }

        // Inject failure and attempt approval.
        using (var failedContext = CreateContext())
        {
            _interceptor.Enabled = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ApproveSupplierPaymentAsync(failedContext, paymentId, 1));
        }

        // Fresh context verifies allocation is still pending and payable outstanding unchanged.
        using (var verifyContext = CreateContext())
        {
            var allocation = await verifyContext.SupplierPaymentAllocations.SingleAsync(x => x.SupplierPaymentId == paymentId);
            Assert.Equal("pending", allocation.Status);
            Assert.Null(allocation.EffectiveDate);
            Assert.Null(allocation.JournalEntryId);

            var payable = await verifyContext.SupplierPayableDocuments.SingleAsync(x => x.Id == 100);
            Assert.Equal(50m, payable.OriginalPayableAmount);

            var paymentLines = await verifyContext.JournalEntryLines.Where(x => x.JournalEntry.ReferenceType == "SupplierPayment").ToListAsync();
            Assert.Empty(paymentLines);
        }

        // Retry and confirm partial settlement works.
        _interceptor.Enabled = false;
        using (var retryContext = CreateContext())
        {
            var result = await ApproveSupplierPaymentAsync(retryContext, paymentId, 1);
            Assert.Equal("posted", result.Status);
        }

        // Verify allocation is effective.
        using var finalContext = CreateContext();
        var finalAllocation = await finalContext.SupplierPaymentAllocations.SingleAsync(x => x.SupplierPaymentId == paymentId);
        Assert.Equal("effective", finalAllocation.Status);
        Assert.Equal(30m, finalAllocation.AllocatedAmount);
        Assert.Equal(new DateTime(2026, 3, 15), finalAllocation.EffectiveDate);
    }

    private RetailPOSDbContext CreateContext() => new(new DbContextOptionsBuilder<RetailPOSDbContext>()
        .UseSqlite(_connection).AddInterceptors(_interceptor).Options);

    private Task<RetailPOS.Application.SupplierPayments.ReadModels.SupplierPaymentReadModel> ApproveSupplierPaymentAsync(
        RetailPOSDbContext db,
        long paymentId,
        long actorUserId)
    {
        var tenant = new Mock<ITenantAccessService>();
        tenant.Setup(x => x.RequireBusinessId()).Returns(1L);
        tenant.Setup(x => x.IsSuperAdmin).Returns(false);

        var numbers = new Mock<IJournalNumberAllocator>();
        // Use a static counter to ensure global uniqueness across all test instances sharing one SQLite connection
        numbers.Setup(x => x.AllocateNextJournalNumberAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<DateTime>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult($"JP-{System.Threading.Interlocked.Increment(ref s_globalJournalCounter):D4}"));

        var audit = Mock.Of<IAuditService>(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()) == Task.CompletedTask);

        var config = AccountingConfigurationServiceTestFactory.Create(db, tenant.Object,  NullLogger<AccountingConfigurationService>.Instance);
        return SupplierPaymentCreationTestHarness.ApproveAsync(
            db,
            paymentId,
            actorUserId,
            audit,
            config,
            numbers.Object);
    }

    private static int s_globalJournalCounter = 0;

    private static void Seed(RetailPOSDbContext db)
    {
        db.Businesses.Add(new Business { Id = 1, Name = "B" });
        db.Suppliers.Add(new Supplier { Id = 10, BusinessId = 1, Name = "S" });
        db.Warehouses.Add(new Warehouse { Id = 30, BusinessId = 1, Name = "W", Address = "A" });

        // Seed products and variants for potential inventory needs.
        db.Categories.Add(new Category { Id = 1, BusinessId = 1, Name = "C" });
        db.Products.Add(new Product { Id = 1, BusinessId = 1, Name = "P", CategoryId = 1 });
        db.ProductVariants.Add(new ProductVariant { Id = 1, BusinessId = 1, ProductId = 1, Name = "V", Sku = "V" });

        // Create GRNs for payable document projection (no actual journal entries needed for this test).
        db.PurchaseOrders.Add(new PurchaseOrder { Id = 20, PoNumber = "PO-20", SupplierId = 10, WarehouseId = 30, Status = "approved", Items = [new PurchaseOrderItem { Id = 21, VariantId = 1, Quantity = 5, UnitPrice = 10m }] });
        db.Grns.Add(new Grn { Id = 40, PoId = 20, ReceivedDate = new DateTime(2026, 3, 10), Status = "full", Items = [new GrnItem { Id = 41, PoItemId = 21, ReceivedQty = 5, UnitCost = 10m }] });
        db.Grns.Add(new Grn { Id = 50, PoId = 20, ReceivedDate = new DateTime(2026, 3, 11), Status = "full", Items = [new GrnItem { Id = 42, PoItemId = 21, ReceivedQty = 5, UnitCost = 10m }] });

        // Seed minimal journal entries to anchor payable documents without polluting test assertions.
        db.JournalEntries.AddRange(
            new JournalEntry { Id = 1000, BusinessId = 1, JournalNumber = "RCP-001", Status = JournalEntryStatuses.Posted, JournalType = "receipt", TransactionDate = new DateTime(2026, 3, 10), PostingDate = new DateTime(2026, 3, 10), ReferenceType = "Grn", ReferenceId = "40" },
            new JournalEntry { Id = 1001, BusinessId = 1, JournalNumber = "RCP-002", Status = JournalEntryStatuses.Posted, JournalType = "receipt", TransactionDate = new DateTime(2026, 3, 11), PostingDate = new DateTime(2026, 3, 11), ReferenceType = "Grn", ReferenceId = "50" }
        );

        db.JournalEntryLines.AddRange(
            new JournalEntryLine { Id = 1000, JournalEntryId = 1000, AccountId = 100, DebitAmount = 50, CreditAmount = 0, Description = "Inventory" },
            new JournalEntryLine { Id = 1001, JournalEntryId = 1000, AccountId = 200, DebitAmount = 0, CreditAmount = 50, Description = "Supplier Payable" },
            new JournalEntryLine { Id = 1002, JournalEntryId = 1001, AccountId = 100, DebitAmount = 50, CreditAmount = 0, Description = "Inventory" },
            new JournalEntryLine { Id = 1003, JournalEntryId = 1001, AccountId = 200, DebitAmount = 0, CreditAmount = 50, Description = "Supplier Payable" }
        );

        // Seed payable documents that can be allocated against.
        db.SupplierPayableDocuments.AddRange(
            new SupplierPayableDocument { Id = 100, BusinessId = 1, SupplierId = 10, OriginalPayableAmount = 50m, Status = "open", SourceDocumentId = 40, SourceDocumentType = "Grn", SourceReference = "GRN-40", SourceJournalEntryId = 1000, RecognitionDate = new DateTime(2026, 3, 10), PaymentTermsDays = 30, DueDate = new DateTime(2026, 4, 9) },
            new SupplierPayableDocument { Id = 101, BusinessId = 1, SupplierId = 10, OriginalPayableAmount = 50m, Status = "open", SourceDocumentId = 50, SourceDocumentType = "Grn", SourceReference = "GRN-50", SourceJournalEntryId = 1001, RecognitionDate = new DateTime(2026, 3, 11), PaymentTermsDays = 30, DueDate = new DateTime(2026, 4, 10) }
        );

        // Seed chart accounts and configuration.
        db.ChartAccounts.AddRange(
            Account(100, "Inventory", ChartAccountTypes.Asset),
            Account(200, "Payable", ChartAccountTypes.Liability),
            Account(300, "Advance", ChartAccountTypes.Liability),
            Account(401, "Cash", ChartAccountTypes.Asset)
        );

        db.AccountingConfigurations.Add(new AccountingConfiguration
        {
            BusinessId = 1,
            InventoryAccountId = 100,
            SupplierPayableAccountId = 200,
            SupplierAdvanceAccountId = 300,
            JournalNumberPrefix = "JP",
            JournalSequenceResetPolicy = AccountingSequenceResetPolicies.PerFinancialYear
        });

        db.FinancialYears.Add(new FinancialYear { Id = 1, BusinessId = 1, YearCode = "FY26", Name = "FY26", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsCurrent = true });
        db.AccountingPeriods.Add(new AccountingPeriod { Id = 1, FinancialYearId = 1, PeriodNumber = 3, Name = "Mar", StartDate = new DateTime(2026, 3, 1), EndDate = new DateTime(2026, 3, 31), Status = AccountingPeriodStatuses.Open });

        db.SaveChanges();
    }

    private static ChartAccount Account(long id, string name, string type) => new()
    {
        Id = id,
        BusinessId = 1,
        AccountCode = id.ToString(),
        AccountName = name,
        AccountType = type,
        NormalBalanceType = type == ChartAccountTypes.Asset ? NormalBalanceTypes.Debit : NormalBalanceTypes.Credit,
        IsActive = true
    };

    public void Dispose() => _connection.Dispose();

    private sealed class ThrowOnAllocationActivationInterceptor : SaveChangesInterceptor
    {
        public bool Enabled { get; set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (Enabled)
            {
                var allocations = eventData.Context?.ChangeTracker.Entries<SupplierPaymentAllocation>()
                    .Where(x => x.State == EntityState.Modified || x.State == EntityState.Added)
                    .Where(x => x.Entity.Status == "effective" || x.Entity.EffectiveDate.HasValue)
                    .ToList();
                
                if (allocations?.Any() == true)
                {
                    throw new InvalidOperationException("Injected allocation activation persistence failure.");
                }
            }
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => new(SavingChanges(eventData, result));
    }
}
