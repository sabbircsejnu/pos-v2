using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using MediatR;
using Moq;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Application.Sales.ReadModels;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Infrastructure.Sales;

namespace RetailPOS.Tests.Sales;

public sealed class SaleCreationRelationalBaselineTests : IDisposable
{
    private readonly string _connectionString =
        $"Data Source=sale-create-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=True;Default Timeout=10";
    private readonly SqliteConnection _anchorConnection;
    private readonly ThrowOnSaleItemUpdateInterceptor _interceptor = new();
    private readonly Customer _walkIn;
    private readonly CashSession _cashSession;

    public SaleCreationRelationalBaselineTests()
    {
        _anchorConnection = new SqliteConnection(_connectionString);
        _anchorConnection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();

        db.Businesses.Add(new Business { Id = 1, Name = "Test business", IsActive = true });
        db.Outlets.Add(new Outlet { Id = 5001, BusinessId = 1, Name = "Main outlet", Address = "Dhaka" });
        db.Users.Add(new User
        {
            Id = 7001,
            BusinessId = 1,
            Name = "Cashier",
            Email = "cashier@example.test",
            PasswordHash = "test",
            OutletId = 5001,
            IsActive = true
        });
        _walkIn = new Customer
        {
            Id = 6001,
            BusinessId = 1,
            Name = Customer.WalkInCustomerName,
            CustomerCode = Customer.WalkInCustomerCode,
            IsSystem = true,
            IsActive = true
        };
        db.Customers.Add(_walkIn);
        db.Categories.Add(new Category { Id = 3001, BusinessId = 1, Name = "General" });
        db.ChartAccounts.Add(new ChartAccount
        {
            Id = 1,
            BusinessId = 1,
            AccountCode = "1000",
            AccountName = "Cash",
            AccountType = ChartAccountTypes.Asset,
            NormalBalanceType = NormalBalanceTypes.Debit,
            IsActive = true
        });
        db.Products.Add(new Product
        {
            Id = 3101,
            BusinessId = 1,
            Name = "Test product",
            ProductCode = "P-3101",
            CategoryId = 3001,
            BasePrice = 100m,
            CostPrice = 70m,
            Status = ProductStatus.Active,
            HasVariants = true
        });
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 3201,
            BusinessId = 1,
            ProductId = 3101,
            Name = "Default",
            Sku = "SKU-3201",
            Barcode = "BC-3201",
            Attributes = "{}"
        });
        db.PosTerminals.Add(new PosTerminal
        {
            Id = 8001,
            OutletId = 5001,
            Name = "Counter",
            Code = "T-1",
            IsActive = true,
            IsDefault = true
        });
        _cashSession = new CashSession
        {
            Id = 8101,
            BusinessId = 1,
            OutletId = 5001,
            TerminalId = 8001,
            BusinessDate = DateTime.UtcNow.Date,
            OpenedAt = DateTime.UtcNow,
            OpeningCashAmount = 1000m,
            Status = "open",
            OpenedByUserId = 7001
        };
        db.CashSessions.Add(_cashSession);
        db.SaveChanges();
        db.Database.ExecuteSqlRaw(
            "INSERT INTO inventories (id, variant_id, location_id, location_type, quantity, low_stock_threshold, xmin) " +
            "VALUES (9001, 3201, 5001, 'outlet', 50, 5, 1)");
    }

    [Fact]
    public async Task CreateAsync_RollsBackPersistedSaleAndStockDeduction_WhenLaterSaleItemSaveFails()
    {
        await using (var context = CreateContext())
        {
            var cache = new Mock<IPosCacheService>();
            var events = new Mock<ISaleEventPublisher>();
            var service = CreateHandler(context, cache, events);
            _interceptor.ThrowOnModifiedSaleItem = true;

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.Handle(
                    ToCommand(CreateRequest("rollback-after-sale-save")),
                    CancellationToken.None));

            cache.Verify(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
            events.Verify(x => x.PublishSaleCompletedAsync(It.IsAny<SaleCompletedEvent>()), Times.Never);
        }

        await using var verify = CreateContext();
        Assert.Empty(await verify.Sales.ToListAsync());
        Assert.Empty(await verify.SaleItems.ToListAsync());
        Assert.Empty(await verify.SalePayments.ToListAsync());
        Assert.Empty(await verify.StockLedgers.ToListAsync());
        Assert.Equal(50, (await verify.Inventories.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task CreateAsync_SequentialIdempotencyRetryReturnsOriginalSaleWithoutRepeatingEffects()
    {
        await using (var context = CreateContext())
        {
            var cache = new Mock<IPosCacheService>();
            cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>()))
                .Returns(Task.CompletedTask);
            var events = new Mock<ISaleEventPublisher>();
            events.Setup(x => x.PublishSaleCompletedAsync(It.IsAny<SaleCompletedEvent>()))
                .Returns(Task.CompletedTask);
            var service = CreateHandler(context, cache, events);
            var request = CreateRequest("sequential-retry");

            var command = ToCommand(request);
            var first = await service.Handle(command, CancellationToken.None);
            var retry = await service.Handle(command, CancellationToken.None);

            Assert.Equal(first.Id, retry.Id);
            cache.Verify(x => x.InvalidateStockAsync(3201, 5001), Times.Once);
            events.Verify(x => x.PublishSaleCompletedAsync(It.IsAny<SaleCompletedEvent>()), Times.Once);
        }

        await using var verify = CreateContext();
        Assert.Single(await verify.Sales.Where(x => x.IdempotencyKey == "sequential-retry").ToListAsync());
        Assert.Single(await verify.SaleItems.ToListAsync());
        Assert.Single(await verify.SalePayments.ToListAsync());
        Assert.Single(await verify.StockLedgers.ToListAsync());
        Assert.Equal(48, (await verify.Inventories.SingleAsync()).Quantity);
        var idempotencyIndex = verify.Model.FindEntityType(typeof(Sale))!
            .GetIndexes()
            .Single(index => index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(Sale.OutletId), nameof(Sale.IdempotencyKey)]));
        Assert.True(idempotencyIndex.IsUnique);
        Assert.Contains("idempotency_key IS NOT NULL", idempotencyIndex.GetFilter());
    }

    [Fact]
    public async Task CreateAsync_DoesNotEarnLegacyLoyaltyPointsForACustomerSale()
    {
        await using (var seed = CreateContext())
        {
            seed.Customers.Add(new Customer
            {
                Id = 6003,
                BusinessId = 1,
                Name = "Existing balance",
                CustomerCode = "LOYALTY-NO-EARN",
                IsSystem = false,
                IsActive = true,
                LoyaltyPoints = 40
            });
            await seed.SaveChangesAsync();
        }

        var request = CreateRequest("no-legacy-earning");
        request.CustomerId = 6003;
        await using (var context = CreateContext())
        {
            var cache = new Mock<IPosCacheService>();
            cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>())).Returns(Task.CompletedTask);
            var events = new Mock<ISaleEventPublisher>();
            events.Setup(x => x.PublishSaleCompletedAsync(It.IsAny<SaleCompletedEvent>())).Returns(Task.CompletedTask);

            await CreateHandler(context, cache, events).Handle(ToCommand(request), CancellationToken.None);

            events.Verify(
                x => x.PublishSaleCompletedAsync(It.Is<SaleCompletedEvent>(published => published.LoyaltyPointsAwarded == 0)),
                Times.Once);
        }

        await using var verify = CreateContext();
        Assert.Equal(40, (await verify.Customers.SingleAsync(customer => customer.Id == 6003)).LoyaltyPoints);
        Assert.Equal(6003, (await verify.Sales.SingleAsync(sale => sale.IdempotencyKey == "no-legacy-earning")).CustomerId);
    }

    [Fact]
    public async Task CreateAsync_InvalidatesAndPublishesOnlyAfterSaleAndInventoryCommit()
    {
        var effects = new List<string>();
        var cache = new Mock<IPosCacheService>();
        cache.Setup(x => x.InvalidateStockAsync(3201, 5001))
            .Returns(async () =>
            {
                await using var observer = CreateContext();
                Assert.Single(await observer.Sales.ToListAsync());
                Assert.Single(await observer.StockLedgers.ToListAsync());
                Assert.Equal(48, (await observer.Inventories.SingleAsync()).Quantity);
                effects.Add("cache");
            });
        var events = new Mock<ISaleEventPublisher>();
        events.Setup(x => x.PublishSaleCompletedAsync(It.IsAny<SaleCompletedEvent>()))
            .Callback(() => effects.Add("event"))
            .Returns(Task.CompletedTask);

        await using (var context = CreateContext())
        {
            var handler = CreateHandler(context, cache, events);
            await handler.Handle(
                ToCommand(CreateRequest("post-commit-effects")),
                CancellationToken.None);
        }

        Assert.Equal(["cache", "event"], effects);
    }

    [Fact]
    public async Task VoidCommand_RequiredAuditFailureRollsBackStatusHistoryInventoryAndLedger()
    {
        await SeedCompletedSaleAsync(9101);
        var cache = new Mock<IPosCacheService>();
        var audit = new Mock<IAuditService>();
        audit.Setup(service => service.RecordAsync(
                It.IsAny<AuditEventInput>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Injected required void audit failure."));

        await using (var context = CreateContext())
        {
            var handler = CreateVoidHandler(context, cache, audit.Object);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => handler.Handle(new VoidSaleCommand(9101, "customer request", 7001), CancellationToken.None));
        }

        await using var verify = CreateContext();
        Assert.Equal("completed", (await verify.Sales.SingleAsync(sale => sale.Id == 9101)).Status);
        Assert.Empty(await verify.SaleVoids.ToListAsync());
        Assert.Empty(await verify.StockLedgers.ToListAsync());
        Assert.Equal(50, (await verify.Inventories.SingleAsync()).Quantity);
        Assert.Equal(250, (await verify.Customers.SingleAsync(customer => customer.Id == 6002)).LoyaltyPoints);
        cache.Verify(cacheService => cacheService.InvalidateStockAsync(
            It.IsAny<long>(),
            It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task VoidCommand_SequentialDuplicateIsRejectedWithoutRepeatingReversalEffects()
    {
        await SeedCompletedSaleAsync(9102);
        var cache = new Mock<IPosCacheService>();
        cache.Setup(service => service.InvalidateStockAsync(3201, 5001))
            .Returns(async () =>
            {
                await using var observer = CreateContext();
                Assert.Equal("voided", (await observer.Sales.SingleAsync(sale => sale.Id == 9102)).Status);
                Assert.Single(await observer.SaleVoids.ToListAsync());
                Assert.Single(await observer.StockLedgers.ToListAsync());
                Assert.Equal(52, (await observer.Inventories.SingleAsync()).Quantity);
            });

        await using (var context = CreateContext())
        {
            var handler = CreateVoidHandler(context, cache);
            await handler.Handle(
                new VoidSaleCommand(9102, "customer request", 7001),
                CancellationToken.None);
        }

        await using (var context = CreateContext())
        {
            var handler = CreateVoidHandler(context, cache);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => handler.Handle(
                    new VoidSaleCommand(9102, "retry", 7001),
                    CancellationToken.None));
            Assert.Equal("Sale is already voided", exception.Message);
        }

        await using var verify = CreateContext();
        Assert.Equal("voided", (await verify.Sales.SingleAsync(sale => sale.Id == 9102)).Status);
        Assert.Single(await verify.SaleVoids.ToListAsync());
        Assert.Single(await verify.StockLedgers.ToListAsync());
        var reversal = await verify.StockLedgers.SingleAsync();
        Assert.Equal(70m, reversal.UnitCost);
        Assert.Contains("historical unit cost 70.0000", reversal.Remarks);
        Assert.Equal(52, (await verify.Inventories.SingleAsync()).Quantity);
        // Phase 2.1: voiding no longer touches the legacy integer loyalty balance.
        Assert.Equal(250, (await verify.Customers.SingleAsync(customer => customer.Id == 6002)).LoyaltyPoints);
        cache.Verify(cacheService => cacheService.InvalidateStockAsync(3201, 5001), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_RollsBackSaleStockLedgerPostingIntentAndOutbox_WhenRequiredAuditFails()
    {
        await using (var context = CreateContext())
        {
            var cache = new Mock<IPosCacheService>();
            var events = new Mock<ISaleEventPublisher>();
            var audit = new Mock<IAuditService>();
            audit.Setup(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Injected required audit failure."));
            var service = CreateHandler(context, cache, events, audit.Object);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.Handle(ToCommand(CreateRequest("rollback-after-intent")), CancellationToken.None));

            audit.Verify(x => x.RecordAsync(It.IsAny<AuditEventInput>(), It.IsAny<CancellationToken>()), Times.Once);
            cache.Verify(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>()), Times.Never);
            events.Verify(x => x.PublishSaleCompletedAsync(It.IsAny<SaleCompletedEvent>()), Times.Never);
        }

        // D10: an uncommitted sale leaves neither a posting intent nor its outbox event, and never a journal.
        await using var verify = CreateContext();
        Assert.Empty(await verify.Sales.ToListAsync());
        Assert.Empty(await verify.SaleItems.ToListAsync());
        Assert.Empty(await verify.SalePayments.ToListAsync());
        Assert.Empty(await verify.StockLedgers.ToListAsync());
        Assert.Empty(await verify.SaleFinancialPostings.ToListAsync());
        Assert.Empty(await verify.Set<RetailPOS.Core.Entities.Integration.IntegrationOutboxMessage>().ToListAsync());
        Assert.Empty(await verify.JournalEntries.ToListAsync());
        Assert.Equal(50, (await verify.Inventories.SingleAsync()).Quantity);
    }

    [Fact]
    public async Task CreateAsync_ConcurrentDuplicateRequestsCannotCommitTwoSalesOrDeductStockTwice()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = CreateRequest("concurrent-retry");
        async Task<(SaleReadModel? Sale, Exception? Error)> CreateAfterStartAsync()
        {
            await start.Task;
            try
            {
                await using var context = CreateContext();
                var service = CreateHandler(
                    context,
                    new Mock<IPosCacheService>(),
                    new Mock<ISaleEventPublisher>());
                return (await service.Handle(ToCommand(request), CancellationToken.None), null);
            }
            catch (Exception exception)
            {
                return (null, exception);
            }
        }

        var attempts = new[]
        {
            CreateAfterStartAsync(),
            CreateAfterStartAsync()
        };
        start.SetResult();
        var outcomes = await Task.WhenAll(attempts);
        var successfulIds = outcomes
            .Where(outcome => outcome.Sale is not null)
            .Select(outcome => outcome.Sale!.Id)
            .Distinct()
            .ToArray();

        Assert.NotEmpty(successfulIds);
        Assert.Single(successfulIds);

        await using var verify = CreateContext();
        var created = await verify.Sales.Where(x => x.IdempotencyKey == "concurrent-retry").ToListAsync();
        Assert.Single(created);
        Assert.Single(await verify.SaleItems.ToListAsync());
        Assert.Single(await verify.SalePayments.ToListAsync());
        Assert.Single(await verify.StockLedgers.ToListAsync());
        Assert.Equal(48, (await verify.Inventories.SingleAsync()).Quantity);
        Assert.Contains(outcomes, outcome => outcome.Error is null);
    }

    [Fact]
    public async Task RefundCommand_PartialRefundPostsOneReversalAndInvalidatesCacheAfterCommit()
    {
        await SeedCompletedSaleAsync(9201);
        var accounting = new Mock<ISaleFinancialPostingWriter>();
        accounting.Setup(service => service.AppendReversalAsync(
                It.IsAny<Sale>(),
                It.IsAny<IReadOnlyList<SaleRefundLine>>(),
                It.IsAny<string>(),
                7001,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaleFinancialPosting());
        var audit = new Mock<ISaleRefundAuditWriter>();
        audit.Setup(service => service.RecordRefundedAsync(
                It.IsAny<Sale>(),
                "completed",
                "partial return",
                1,
                (long?)null,
                7001,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var cache = new Mock<ISaleRefundCacheInvalidator>();
        cache.Setup(service => service.InvalidateAsync(3201, 5001, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await using var observer = CreateContext();
                Assert.Equal("refunded", await observer.Sales.Where(sale => sale.Id == 9201)
                    .Select(sale => sale.Status).SingleAsync());
                Assert.Equal(51, (await observer.Inventories.SingleAsync()).Quantity);
                Assert.Single(await observer.StockLedgers.Where(entry => entry.ReferenceId == 9201).ToListAsync());
            });

        SaleReadModel result;
        await using (var context = CreateContext())
        {
            result = await CreateRefundHandler(context, accounting.Object, audit.Object, cache.Object)
                .Handle(
                    new RefundSaleCommand(
                        9201,
                        [new RefundSaleLine(3201, 1)],
                        "partial return",
                        7001),
                    CancellationToken.None);
        }

        Assert.Equal("refunded", result.Status);
        Assert.Equal("VOID-9201", result.SaleNumber);
        Assert.Equal(5001, result.OutletId);
        Assert.Single(result.Items);
        Assert.Equal(2, result.Items.Single().Quantity);
        accounting.Verify(service => service.AppendReversalAsync(
            It.Is<Sale>(sale => sale.Id == 9201),
            It.Is<IReadOnlyList<SaleRefundLine>>(lines =>
                lines.Count == 1 && lines[0].SaleItemId > 0 &&
                lines[0].VariantId == 3201 && lines[0].Quantity == 1),
            It.IsAny<string>(),
            7001,
            It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(service => service.RecordRefundedAsync(
            It.IsAny<Sale>(),
            "completed",
            "partial return",
            1,
            (long?)null,
            7001,
            It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(service => service.InvalidateAsync(3201, 5001, It.IsAny<CancellationToken>()), Times.Once);

        await using var verify = CreateContext();
        var ledger = await verify.StockLedgers.SingleAsync(entry => entry.ReferenceId == 9201);
        Assert.Equal(1, ledger.QtyIn);
        Assert.Equal(70m, ledger.UnitCost);
    }

    [Fact]
    public async Task RefundCommand_RequiredAuditFailureRollsBackEveryDatabaseEffect()
    {
        await SeedCompletedSaleAsync(9202);
        var audit = new Mock<ISaleRefundAuditWriter>();
        audit.Setup(service => service.RecordRefundedAsync(
                It.IsAny<Sale>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Injected required refund audit failure."));
        var accounting = new Mock<ISaleFinancialPostingWriter>();
        var cache = new Mock<ISaleRefundCacheInvalidator>();

        await using (var context = CreateContext())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateRefundHandler(context, accounting.Object, audit.Object, cache.Object)
                    .Handle(
                        new RefundSaleCommand(9202, [new RefundSaleLine(3201, 1)], "return", 7001),
                        CancellationToken.None));
        }

        await using var verify = CreateContext();
        Assert.Equal("completed", await verify.Sales.Where(sale => sale.Id == 9202)
            .Select(sale => sale.Status).SingleAsync());
        Assert.Equal(50, (await verify.Inventories.SingleAsync()).Quantity);
        Assert.Empty(await verify.StockLedgers.Where(entry => entry.ReferenceId == 9202).ToListAsync());
        cache.Verify(service => service.InvalidateAsync(
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefundCommand_RejectsSequentialDuplicateWithoutRepeatingEffects()
    {
        await SeedCompletedSaleAsync(9203);
        var accounting = new Mock<ISaleFinancialPostingWriter>();
        var audit = new Mock<ISaleRefundAuditWriter>();
        audit.Setup(service => service.RecordRefundedAsync(
                It.IsAny<Sale>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int>(),
                It.IsAny<long?>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var cache = new Mock<ISaleRefundCacheInvalidator>();
        cache.Setup(service => service.InvalidateAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var request = new RefundSaleCommand(
            9203,
            [new RefundSaleLine(3201, 1)],
            "partial return",
            7001);

        await using (var context = CreateContext())
            await CreateRefundHandler(context, accounting.Object, audit.Object, cache.Object)
                .Handle(request, CancellationToken.None);

        await using (var context = CreateContext())
        {
            var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateRefundHandler(context, accounting.Object, audit.Object, cache.Object)
                    .Handle(request, CancellationToken.None));
            Assert.Equal("Sale is already refunded", duplicate.Message);
        }

        await using var verify = CreateContext();
        Assert.Equal(51, (await verify.Inventories.SingleAsync()).Quantity);
        Assert.Single(await verify.StockLedgers.Where(entry => entry.ReferenceId == 9203).ToListAsync());
        accounting.Verify(service => service.AppendReversalAsync(
            It.IsAny<Sale>(),
            It.IsAny<IReadOnlyList<SaleRefundLine>>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(service => service.RecordRefundedAsync(
            It.IsAny<Sale>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<int>(),
            It.IsAny<long?>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(service => service.InvalidateAsync(
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefundCommand_RejectsDuplicateVariantOverRefundBeforeMutation()
    {
        await SeedCompletedSaleAsync(9204);
        var accounting = new Mock<ISaleFinancialPostingWriter>();
        var audit = new Mock<ISaleRefundAuditWriter>();
        var cache = new Mock<ISaleRefundCacheInvalidator>();

        await using (var context = CreateContext())
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateRefundHandler(context, accounting.Object, audit.Object, cache.Object)
                    .Handle(
                        new RefundSaleCommand(
                            9204,
                            [new RefundSaleLine(3201, 2), new RefundSaleLine(3201, 1)],
                            "duplicate line",
                            7001),
                        CancellationToken.None));
            Assert.Equal(
                "Refund quantity 3 exceeds sold quantity 2 for variant ID 3201",
                exception.Message);
        }

        await using var verify = CreateContext();
        Assert.Equal("completed", await verify.Sales.Where(sale => sale.Id == 9204)
            .Select(sale => sale.Status).SingleAsync());
        Assert.Equal(50, (await verify.Inventories.SingleAsync()).Quantity);
        Assert.Empty(await verify.StockLedgers.Where(entry => entry.ReferenceId == 9204).ToListAsync());
        accounting.Verify(service => service.AppendReversalAsync(
            It.IsAny<Sale>(),
            It.IsAny<IReadOnlyList<SaleRefundLine>>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefundCommand_OutletDenialPrecedesAllMutationAndAccountingEffects()
    {
        await SeedCompletedSaleAsync(9205);
        var authorizer = new Mock<ISaleMutationOutletAuthorizer>();
        authorizer.Setup(service => service.AuthorizeAsync(5001, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Outlet access denied."));
        var accounting = new Mock<ISaleFinancialPostingWriter>();
        var audit = new Mock<ISaleRefundAuditWriter>();
        var cache = new Mock<ISaleRefundCacheInvalidator>();

        await using (var context = CreateContext())
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                CreateRefundHandler(context, accounting.Object, audit.Object, cache.Object, authorizer.Object)
                    .Handle(
                        new RefundSaleCommand(9205, [new RefundSaleLine(3201, 1)], "return", 7001),
                        CancellationToken.None));
        }

        await using var verify = CreateContext();
        Assert.Equal("completed", await verify.Sales.Where(sale => sale.Id == 9205)
            .Select(sale => sale.Status).SingleAsync());
        Assert.Equal(50, (await verify.Inventories.SingleAsync()).Quantity);
        Assert.Empty(await verify.StockLedgers.Where(entry => entry.ReferenceId == 9205).ToListAsync());
        accounting.Verify(service => service.AppendReversalAsync(
            It.IsAny<Sale>(),
            It.IsAny<IReadOnlyList<SaleRefundLine>>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
        audit.Verify(service => service.RecordRefundedAsync(
            It.IsAny<Sale>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<int>(),
            It.IsAny<long?>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
        cache.Verify(service => service.InvalidateAsync(
            It.IsAny<long>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0, 0, "Refund must include at least one item")]
    [InlineData(1, 9999, "Variant ID 9999 not found in this sale")]
    [InlineData(3, 3201, "Refund quantity 3 exceeds sold quantity 2 for variant ID 3201")]
    [InlineData(0, 3201, "Refund quantity must be greater than zero for variant ID 3201")]
    [InlineData(-1, 3201, "Refund quantity must be greater than zero for variant ID 3201")]
    public async Task RefundCommand_PreservesValidationErrorsWithoutSideEffects(
        int quantity,
        long variantId,
        string expectedMessage)
    {
        const long saleId = 9206;
        await SeedCompletedSaleAsync(saleId);
        var accounting = new Mock<ISaleFinancialPostingWriter>();
        var audit = new Mock<ISaleRefundAuditWriter>();
        var cache = new Mock<ISaleRefundCacheInvalidator>();
        IReadOnlyList<RefundSaleLine> items = quantity == 0 && variantId == 0
            ? []
            : [new RefundSaleLine(variantId, quantity)];

        await using (var context = CreateContext())
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CreateRefundHandler(context, accounting.Object, audit.Object, cache.Object)
                    .Handle(
                        new RefundSaleCommand(saleId, items, "invalid", 7001),
                        CancellationToken.None));
            Assert.Equal(expectedMessage, exception.Message);
        }

        await using var verify = CreateContext();
        Assert.Equal("completed", await verify.Sales.Where(sale => sale.Id == saleId)
            .Select(sale => sale.Status).SingleAsync());
        Assert.Equal(50, (await verify.Inventories.SingleAsync()).Quantity);
        Assert.Empty(await verify.StockLedgers.Where(entry => entry.ReferenceId == saleId).ToListAsync());
        accounting.Verify(service => service.AppendReversalAsync(
            It.IsAny<Sale>(),
            It.IsAny<IReadOnlyList<SaleRefundLine>>(),
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefundCommand_PropagatesCancellationBeforeOpeningTransaction()
    {
        await SeedCompletedSaleAsync(9207);
        var accounting = new Mock<ISaleFinancialPostingWriter>();
        var audit = new Mock<ISaleRefundAuditWriter>();
        var cache = new Mock<ISaleRefundCacheInvalidator>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await using (var context = CreateContext())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                CreateRefundHandler(context, accounting.Object, audit.Object, cache.Object)
                    .Handle(
                        new RefundSaleCommand(9207, [new RefundSaleLine(3201, 1)], "return", 7001),
                        cancellation.Token));
        }

        await using var verify = CreateContext();
        Assert.Equal("completed", await verify.Sales.Where(sale => sale.Id == 9207)
            .Select(sale => sale.Status).SingleAsync());
        Assert.Equal(50, (await verify.Inventories.SingleAsync()).Quantity);
        Assert.Empty(await verify.StockLedgers.Where(entry => entry.ReferenceId == 9207).ToListAsync());
    }

    private RetailPOSDbContext? activeContext;

    private RetailPOSDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseSqlite(_connectionString)
            .AddInterceptors(_interceptor)
            .Options);

    private async Task SeedCompletedSaleAsync(long saleId)
    {
        await using var context = CreateContext();
        context.Customers.Add(new Customer
        {
            Id = 6002,
            BusinessId = 1,
            Name = "Loyalty customer",
            CustomerCode = $"LOYALTY-{saleId}",
            IsSystem = false,
            IsActive = true,
            LoyaltyPoints = 250
        });
        var saleItem = new SaleItem
        {
            VariantId = 3201,
            Quantity = 2,
            UnitPrice = 100m,
            Subtotal = 200m,
            DiscountAmount = 0m
        };
        saleItem.CaptureHistoricalUnitCost(70m);
        context.Sales.Add(new Sale
        {
            Id = saleId,
            SaleNumber = $"VOID-{saleId}",
            OutletId = 5001,
            TerminalId = 8001,
            CashSessionId = 8101,
            CustomerId = 6002,
            CashierId = 7001,
            SaleDate = DateTime.UtcNow,
            SaleDateTime = DateTime.UtcNow,
            BusinessDate = DateTime.UtcNow.Date,
            Status = "completed",
            TotalAmount = 200m,
            PaymentMethod = "cash",
            Items = [saleItem],
            Payments = [new SalePayment { Method = "cash", Amount = 200m, Tendered = 200m }]
        });
        await context.SaveChangesAsync();
    }

    private VoidSaleCommandHandler CreateVoidHandler(
        RetailPOSDbContext context,
        Mock<IPosCacheService> cache,
        IAuditService? audit = null) =>
        new(
            new SaleVoidStore(context),
            new SaleReadStore(context),
            CreateOutletAuthorizer(),
            SaleCreationCommandTestFactory.FinancialPostings(context),
            new SaleVoidStockLedgerWriter(new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                context,
                NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance)),
            new SaleVoidAuditWriter(audit ?? Mock.Of<IAuditService>()),
            new SaleVoidCacheInvalidator(cache.Object),
            new SaleVoidLogger(NullLogger<SaleVoidLogger>.Instance),
            new RetailPOS.IntegrationTests.Infrastructure.NoCashRefunds());

    private static ISaleMutationOutletAuthorizer CreateOutletAuthorizer()
    {
        var authorizer = new Mock<ISaleMutationOutletAuthorizer>();
        authorizer.Setup(service => service.AuthorizeAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return authorizer.Object;
    }

    private RefundSaleCommandHandler CreateRefundHandler(
        RetailPOSDbContext context,
        ISaleFinancialPostingWriter accounting,
        ISaleRefundAuditWriter audit,
        ISaleRefundCacheInvalidator cache,
        ISaleMutationOutletAuthorizer? authorizer = null) =>
        new(
            new SaleVoidStore(context),
            new SaleReadStore(context),
            authorizer ?? CreateOutletAuthorizer(),
            accounting,
            new SaleRefundStockLedgerWriter(new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                context,
                NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance)),
            audit,
            cache,
            Mock.Of<ISaleRefundLogger>(),
            new RetailPOS.IntegrationTests.Infrastructure.NoCashRefunds());

    private CreateSaleCommandHandler CreateHandler(
        RetailPOSDbContext context,
        Mock<IPosCacheService> cache,
        Mock<ISaleEventPublisher> events,
        IAuditService? auditService = null)
    {
        var cashSessions = new Mock<ISaleCashSessionValidator>();
        cashSessions
            .Setup(x => x.ValidateActiveForSaleAsync(
                5001,
                8001,
                7001,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_cashSession);
        var invoiceNumberAllocator = new Mock<ISaleInvoiceNumberAllocator>();
        invoiceNumberAllocator.Setup(x => x.AllocateAsync(
                It.IsAny<long>(),
                It.IsAny<long>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => TestInvoices.Issue($"SALE-{Guid.NewGuid():N}"));

        return new CreateSaleCommandHandler(
            new SaleCreationStore(context, new SaleReadStore(context)),
            cashSessions.Object,
            invoiceNumberAllocator.Object,
            SaleCreationCommandTestFactory.FinancialPostings(context),
            new SaleCreationAuditWriter(auditService ?? Mock.Of<IAuditService>()),
            new SaleStockLedgerWriter(new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                context,
                NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance)),
            new SaleStockCacheInvalidator(cache.Object),
            new SaleCompletedPublisher(events.Object),
            new SaleCreationLogger(NullLogger<SaleCreationLogger>.Instance),
            SaleCreationCommandTestFactory.PriceCalculator(context),
            SaleCreationCommandTestFactory.TaxPolicy());
    }

    private static CreateSaleCommand ToCommand(CreateSaleDto dto) =>
        new(
            dto.OutletId,
            dto.TerminalId,
            dto.CustomerId,
            dto.Items.Select(item => new CreateSaleLine(
                item.VariantId,
                item.Quantity,
                item.UnitPrice,
                item.DiscountAmount,
                item.AppliedRuleId,
                item.AppliedRuleName)).ToArray(),
            dto.Discount,
            dto.Tax,
            dto.PaymentMethod,
            dto.Payments.Select(payment => new CreateSalePayment(
                payment.Method,
                payment.Amount,
                payment.Tendered)).ToArray(),
            dto.CashierId ?? 0,
            dto.OriginalSaleId,
            dto.SalesDate,
            dto.IdempotencyKey,
            dto.CashTendered);

    private static CreateSaleDto CreateRequest(string idempotencyKey) => new()
    {
        OutletId = 5001,
        TerminalId = 8001,
        CashierId = 7001,
        PaymentMethod = "cash",
        CashTendered = 200m,
        IdempotencyKey = idempotencyKey,
        Items =
        [
            new CreateSaleItemDto
            {
                VariantId = 3201,
                Quantity = 2,
                UnitPrice = 100m
            }
        ]
    };

    public void Dispose() => _anchorConnection.Dispose();

    private sealed class ThrowOnSaleItemUpdateInterceptor : SaveChangesInterceptor
    {
        public bool ThrowOnModifiedSaleItem { get; set; }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            if (ThrowOnModifiedSaleItem &&
                eventData.Context?.ChangeTracker.Entries<SaleItem>()
                    .Any(entry => entry.State == EntityState.Modified) == true)
            {
                throw new InvalidOperationException("Injected SaleItem persistence failure.");
            }

            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            new(SavingChanges(eventData, result));
    }
}
