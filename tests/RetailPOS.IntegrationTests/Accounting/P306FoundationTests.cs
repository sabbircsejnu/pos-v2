using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MediatR;
using Moq;
using Microsoft.Data.Sqlite;
using RetailPOS.API.DTOs.Accounting;
using RetailPOS.API.DTOs.Sale;
using RetailPOS.API.Services;
using RetailPOS.Application.AccountingJournals.Abstractions;
using RetailPOS.Application.AccountingJournals.Commands;
using RetailPOS.Application.BankReconciliation.Abstractions;
using RetailPOS.Application.BankReconciliation.ReadModels;
using RetailPOS.Infrastructure.BankReconciliation;
using RetailPOS.Infrastructure.AccountingConfigurations;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.FinancialPostings;
using RetailPOS.Application.Sales.Commands;
using RetailPOS.Core.Entities;
using RetailPOS.Infrastructure.Audit;
using RetailPOS.Infrastructure.Data;
using RetailPOS.Infrastructure.Repositories;
using RetailPOS.Infrastructure.Sales;
using RetailPOS.Tests.Sales;

namespace RetailPOS.Tests.Accounting;

public sealed class P306FoundationTests : IDisposable
{
    private readonly RetailPOSDbContext _db;
    private readonly Mock<ITenantAccessService> _tenant = new();

    public P306FoundationTests()
    {
        _db = new RetailPOSDbContext(new DbContextOptionsBuilder<RetailPOSDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        _db.Businesses.AddRange(
            new Business { Id = 1, Name = "Business One", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Business { Id = 2, Name = "Business Two", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        _db.Outlets.AddRange(
            new Outlet { Id = 11, BusinessId = 1, Name = "Outlet One", Address = "A", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new Outlet { Id = 22, BusinessId = 2, Name = "Outlet Two", Address = "B", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        _db.ChartAccounts.AddRange(
            Account(101, 1, "Bank One", ChartAccountTypes.Asset),
            Account(102, 1, "Card Clearing", ChartAccountTypes.Asset),
            Account(103, 1, "Liability", ChartAccountTypes.Liability),
            Account(201, 2, "Bank Two", ChartAccountTypes.Asset));
        _db.SaveChanges();
        SetBusiness(1);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task BankAccount_IsBusinessOwned_AndRejectsCrossBusinessChartAccountAndOutlet()
    {
        var created = await CreateBankAccount("Main Bank", 101, 11);

        Assert.Equal(1, (await _db.BankAccounts.SingleAsync(row => row.Id == created.Id)).BusinessId);
        Assert.Equal(11, created.OutletId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateBankAccount("Wrong Bank", 201));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateBankAccount("Wrong Outlet", 102, 22));
    }

    [Fact]
    public async Task BankAccount_RequiresActiveAssetChartAccount()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateBankAccount("Liability", 103));
    }

    [Fact]
    public async Task BankAccount_CrossBusinessAccessIsRejected()
    {
        await CreateBankAccount("Main Bank", 101);
        SetBusiness(2);
        Assert.Empty(await _db.BankAccounts.Where(x => x.BusinessId == 2).ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateBankAccount("Other Bank", 101));
    }

    [Fact]
    public async Task ClearingMappings_AcceptOnlySameBusinessAssetAccounts()
    {
        var dto = new AccountingConfiguration
        {
            CardClearingAccountId = 102,
            MobileMfsClearingAccountId = 102,
            BankClearingActivationDate = new DateTime(2026, 9, 6)
        };
        var saved = await CreateAccountingConfigurationWriteStore()
            .UpdateBusinessConfigurationAsync(1, dto, null, CancellationToken.None);
        Assert.Equal(dto.CardClearingAccountId, saved.CardClearingAccountId);
        Assert.Equal(dto.BankClearingActivationDate, saved.BankClearingActivationDate);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateAccountingConfigurationWriteStore().UpdateBusinessConfigurationAsync(
                1, new AccountingConfiguration { CardClearingAccountId = 103 }, null, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateAccountingConfigurationWriteStore().UpdateBusinessConfigurationAsync(
                1, new AccountingConfiguration { CardClearingAccountId = 201 }, null, CancellationToken.None));
    }

    [Fact]
    public async Task CutoverConfiguration_IsBusinessScoped()
    {
        await CreateAccountingConfigurationWriteStore().UpdateBusinessConfigurationAsync(
            1,
            new AccountingConfiguration
            {
                CardClearingAccountId = 102,
                BankClearingActivationDate = new DateTime(2026, 9, 6)
            },
            null,
            CancellationToken.None);

        SetBusiness(2);
        var businessTwo = await CreateAccountingConfigurationWriteStore().UpdateBusinessConfigurationAsync(
            2,
            new AccountingConfiguration
            {
                CardClearingAccountId = 201,
                BankClearingActivationDate = new DateTime(2026, 10, 1)
            },
            null,
            CancellationToken.None);
        Assert.Equal(new DateTime(2026, 10, 1), businessTwo.BankClearingActivationDate);
        Assert.Equal(2, await _db.AccountingConfigurations.CountAsync());
        Assert.Equal(new DateTime(2026, 9, 6), await _db.AccountingConfigurations.Where(x => x.BusinessId == 1).Select(x => x.BankClearingActivationDate).SingleAsync());
    }

    [Theory]
    [InlineData("cash", "cash")]
    [InlineData("bank", "bank")]
    [InlineData("card", "bank")]
    [InlineData("mobile", "bank")]
    [InlineData("mfs", "bank")]
    public void Cutover_BeforeDate_UsesLegacyLiquiditySlots(string method, string expected)
        => Assert.Equal(expected, PaymentPostingPolicy.ResolveSlot(method, new DateTime(2026, 9, 5), new DateTime(2026, 9, 6)));

    [Theory]
    [InlineData("cash", "cash")]
    [InlineData("bank", "bank")]
    [InlineData("card", "card_clearing")]
    [InlineData("mobile", "mobile_mfs_clearing")]
    [InlineData("mfs", "mobile_mfs_clearing")]
    public void Cutover_OnDateAndAfter_UsesStableConfiguredSlots(string method, string expected)
    {
        Assert.Equal(expected, PaymentPostingPolicy.ResolveSlot(method, new DateTime(2026, 9, 6), new DateTime(2026, 9, 6)));
        Assert.Equal(expected, PaymentPostingPolicy.ResolveSlot(method, new DateTime(2026, 9, 7), new DateTime(2026, 9, 6)));
    }

    [Fact]
    public void SplitPayments_ResolveEachComponentIndependently()
    {
        var cutover = new DateTime(2026, 9, 6);
        var methods = new[] { "cash", "card", "mobile", "mfs", "bank" };
        var slots = methods.Select(method => PaymentPostingPolicy.ResolveSlot(method, cutover, cutover)).ToArray();
        Assert.Equal(new[] { PostingAccountSlots.Cash, PostingAccountSlots.CardClearing, PostingAccountSlots.MobileMfsClearing, PostingAccountSlots.MobileMfsClearing, PostingAccountSlots.Bank }, slots);
    }

    [Fact]
    public async Task SalePayment_PaymentAccountSnapshot_IsPersistedAndNullRemainsLegacyCompatible()
    {
        var sale = new Sale { Id = 501, OutletId = 11, SaleNumber = "S-501", SaleDate = new DateTime(2026, 9, 6), TotalAmount = 10, PaymentMethod = "card", Status = "completed" };
        _db.Sales.Add(sale);
        _db.SalePayments.Add(new SalePayment { SaleId = 501, Method = "card", Amount = 10, PaymentAccountId = 102 });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        Assert.Equal(102, (await _db.SalePayments.SingleAsync()).PaymentAccountId);
        _db.SalePayments.Add(new SalePayment { SaleId = 501, Method = "card", Amount = 10 });
        await _db.SaveChangesAsync();
        Assert.Null(await _db.SalePayments.OrderBy(x => x.Id).LastAsync().ContinueWith(x => x.Result.PaymentAccountId));
    }

    [Fact]
    public async Task Refund_UsesOriginalPaymentAccountAfterCutoverConfigurationChanges()
    {
        var journal = new Mock<ISender>();
        CreateJournalMock(journal, 701);
        var accountConfiguration = new Mock<IAccountingConfigurationService>();
        accountConfiguration.Setup(x => x.ResolvePostingAccountAsync(It.IsAny<string>(), 11)).ReturnsAsync((string slot, long _) => new PostingAccountResolutionDto { Slot = slot, AccountId = slot == PostingAccountSlots.Bank ? 101 : 999, Source = "test" });
        accountConfiguration.Setup(x => x.GetBusinessConfigurationAsync()).ReturnsAsync(new AccountingConfigurationDto { CardClearingAccountId = 102, BankClearingActivationDate = new DateTime(2026, 9, 6) });

        var sale = CreateAccountingSale(701, "card", 101);
        var captured = await RefundWithAccountingPosterAsync(
            sale,
            journal,
            accountConfiguration,
            [new SaleRefundLine(sale.Items.Single().Id, 100, 1)]);

        Assert.Contains(captured.Lines, line => line.AccountId == 101 && line.CreditAmount > 0);
    }

    [Fact]
    public async Task Void_UsesOriginalPaymentAccountAfterCutoverConfigurationChanges()
    {
        var journal = new Mock<ISender>();
        CreateJournalMock(journal, 702);
        var accountConfiguration = new Mock<IAccountingConfigurationService>();
        accountConfiguration.Setup(x => x.ResolvePostingAccountAsync(It.IsAny<string>(), 11)).ReturnsAsync((string slot, long _) => new PostingAccountResolutionDto { Slot = slot, AccountId = slot == PostingAccountSlots.Bank ? 101 : 999, Source = "test" });
        accountConfiguration.Setup(x => x.GetBusinessConfigurationAsync()).ReturnsAsync(new AccountingConfigurationDto { CardClearingAccountId = 102, BankClearingActivationDate = new DateTime(2026, 9, 6) });

        var sale = CreateAccountingSale(702, "card", 101);
        // VAT-inclusive line: the recorded allocation is 9.50 net + 0.50 VAT of the 10.00 paid (P3.2-D01).
        sale.Tax = 0.5m;
        sale.Items.Single().TaxableAmount = 9.5m;
        sale.Items.Single().VatAmount = 0.5m;
        var captured = await VoidWithServiceAsync(sale, journal, accountConfiguration);

        Assert.Contains(captured.Lines, line => line.AccountId == 101 && line.CreditAmount > 0);
        Assert.Contains(captured.Lines, line => line.Description == "VAT payable reversal" && line.DebitAmount == 0.5m);
        Assert.Contains(captured.Lines, line => line.Description == "Inventory restoration" && line.DebitAmount == 5m);
        Assert.Contains(captured.Lines, line => line.Description == "COGS reversal" && line.CreditAmount == 5m);
        Assert.Equal("SalesReturnPosting", captured.JournalType);
        Assert.Equal("SaleRefund", captured.ReferenceType);
        Assert.Equal(sale.Id.ToString(), captured.ReferenceId);
        Assert.Equal($"sale:{sale.Id}:reversal:v1", GetDraftCommand(journal).IdempotencyDiscriminator);
    }

    private async Task<ManualJournalDraftInput> RefundWithAccountingPosterAsync(
        Sale sale,
        Mock<ISender> journal,
        Mock<IAccountingConfigurationService> configuration,
        IReadOnlyList<SaleRefundLine> lines)
    {
        SetBusiness(1);
        var intent = SaleFinancialIntentFactory.ForReversal(
            sale, 1, lines, SaleFinancialPostingReversalTypes.Refund, 5, DateTime.UtcNow);
        await new SaleFinancialJournalPoster(journal.Object, configuration.Object)
            .PostReversalAsync(
                SaleFinancialIntentFactory.ReadReversalSnapshot(intent),
                sale.Payments.ToDictionary(payment => payment.Id, payment => payment.PaymentAccountId),
                CancellationToken.None);
        return GetDraftCommand(journal).Journal;
    }

    private async Task<ManualJournalDraftInput> VoidWithServiceAsync(Sale sale, Mock<ISender> journal, Mock<IAccountingConfigurationService> configuration)
    {
        await using var connection = new SqliteConnection(
            $"Data Source=p306-void-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Foreign Keys=False");
        await connection.OpenAsync();
        await using var db = new RetailPOSDbContext(
            new DbContextOptionsBuilder<RetailPOSDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        var product = new Product { Id = 200, BusinessId = 1, Name = "Widget", Sku = "W1", CostPrice = 5m };
        db.Businesses.Add(new Business { Id = 1, Name = "Business" });
        db.Outlets.Add(new Outlet
        {
            Id = sale.OutletId,
            BusinessId = 1,
            Name = "Outlet",
            Address = "Address"
        });
        db.Users.Add(new User
        {
            Id = sale.CashierId,
            BusinessId = 1,
            Name = "Cashier",
            Email = "cashier@example.test"
        });
        db.Products.Add(product);
        db.ProductVariants.Add(new ProductVariant
        {
            Id = 100,
            BusinessId = 1,
            ProductId = product.Id,
            Product = product,
            Name = "Widget",
            Sku = "W1"
        });
        db.Sales.Add(sale);
        await db.SaveChangesAsync();

        var cache = new Mock<IPosCacheService>();
        cache.Setup(x => x.InvalidateStockAsync(It.IsAny<long>(), It.IsAny<long>()))
            .Returns(Task.CompletedTask);
        var handler = new VoidSaleCommandHandler(
            new SaleVoidStore(db),
            new SaleReadStore(db),
            CreateOutletAuthorizer(),
            RetailPOS.Tests.Sales.SaleCreationCommandTestFactory.FinancialPostings(db),
            new SaleVoidStockLedgerWriter(new RetailPOS.Infrastructure.ReadStores.StockLedgerWriter(
                db,
                NullLogger<RetailPOS.Infrastructure.ReadStores.StockLedgerWriter>.Instance)),
            new SaleVoidAuditWriter(Mock.Of<IAuditService>()),
            new SaleVoidCacheInvalidator(cache.Object),
            new SaleVoidLogger(NullLogger<SaleVoidLogger>.Instance),
            new RetailPOS.IntegrationTests.Infrastructure.NoCashRefunds());
        await handler.Handle(new VoidSaleCommand(sale.Id, "void", 5), CancellationToken.None);

        // D10: the void committed only the reversal intent; the worker's poster produces the journal from it.
        Assert.Empty(journal.Invocations);
        var intent = await db.SaleFinancialPostings.AsNoTracking().SingleAsync(row => row.SaleId == sale.Id);
        Assert.Equal(SaleFinancialPostingKinds.Reversal, intent.Kind);
        Assert.Equal(SaleFinancialPostingReversalTypes.Void, intent.ReversalType);
        await new SaleFinancialJournalPoster(journal.Object, configuration.Object)
            .PostReversalAsync(
                SaleFinancialIntentFactory.ReadReversalSnapshot(intent),
                await new RetailPOS.Infrastructure.Sales.SaleFinancialPostingStore(db)
                    .GetPaymentAccountsAsync(sale.Id, CancellationToken.None),
                CancellationToken.None);
        return GetDraftCommand(journal).Journal;
    }

    private Sale CreateAccountingSale(long id, string method, long paymentAccountId)
    {
        var product = new Product { Id = 200, BusinessId = 1, Name = "Widget", CostPrice = 5m };
        var variant = new ProductVariant { Id = 100, BusinessId = 1, ProductId = 200, Product = product, Sku = "W1", Name = "Widget" };
        _db.Products.Add(product);
        _db.ProductVariants.Add(variant);
        _db.Inventories.Add(new RetailPOS.Core.Entities.Inventory { VariantId = 100, LocationId = 11, LocationType = "outlet", Quantity = 10 });
        var item = new SaleItem { VariantId = 100, Quantity = 1, UnitPrice = 10m, Subtotal = 10m, DiscountAmount = 0m, TaxableAmount = 10m, VatAmount = 0m };
        item.CaptureHistoricalUnitCost(5m);
        return new Sale { Id = id, OutletId = 11, CashierId = 5, Status = "completed", TotalAmount = 10m, SaleDate = new DateTime(2026, 9, 6), Items = [item], Payments = [new SalePayment { Method = method, Amount = 10m, PaymentAccountId = paymentAccountId }] };
    }

    private static CreateOrGetSystemJournalDraftCommand GetDraftCommand(Mock<ISender> journal) =>
        journal.Invocations.Single(invocation =>
                invocation.Arguments[0] is CreateOrGetSystemJournalDraftCommand)
            .Arguments[0] as CreateOrGetSystemJournalDraftCommand
        ?? throw new InvalidOperationException("The journal draft command was not sent.");

    private static void CreateJournalMock(Mock<ISender> journal, long id)
    {
        journal.Setup(x => x.Send(
                It.IsAny<CreateOrGetSystemJournalDraftCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(id);
        journal.Setup(x => x.Send(
                It.Is<PostJournalEntryCommand>(command => command.JournalId == id),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(id);
    }

    private static ISaleMutationOutletAuthorizer CreateOutletAuthorizer()
    {
        var authorizer = new Mock<ISaleMutationOutletAuthorizer>();
        authorizer.Setup(service => service.AuthorizeAsync(
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return authorizer.Object;
    }

    private void SetBusiness(long businessId)
    {
        _tenant.Setup(x => x.RequireBusinessId()).Returns(businessId);
        _tenant.Setup(x => x.EffectiveBusinessId).Returns(businessId);
        _tenant.Setup(x => x.IsSuperAdmin).Returns(false);
    }

    private AccountingConfigurationWriteStore CreateAccountingConfigurationWriteStore() =>
        new(_db, null, NullLogger<AccountingConfigurationWriteStore>.Instance);

    private Task<BankAccountReadModel> CreateBankAccount(
        string displayName,
        long chartAccountId,
        long? outletId = null) =>
        new BankAccountCommandStore(_db).CreateAsync(
            _tenant.Object.RequireBusinessId(),
            null,
            new CreateBankAccountInput(
                displayName,
                chartAccountId,
                outletId,
                null,
                true,
                null,
                null),
            CancellationToken.None);

    private static ChartAccount Account(long id, long businessId, string name, string type) => new()
    {
        Id = id, BusinessId = businessId, AccountCode = id.ToString(), AccountName = name,
        AccountType = type, NormalBalanceType = type == ChartAccountTypes.Asset ? NormalBalanceTypes.Debit : NormalBalanceTypes.Credit,
        IsActive = true, IsControlAccount = false, AllowManualPosting = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

}
