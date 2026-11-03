using Moq;
using RetailPOS.Application.AccountingConfiguration.Abstractions;
using RetailPOS.Application.AccountingConfiguration.Queries;
using RetailPOS.Core.Entities;
using AccountingConfigurationEntity = RetailPOS.Core.Entities.AccountingConfiguration;

namespace RetailPOS.UnitTests.AccountingConfiguration;

public sealed class ValidateRequiredAccountingMappingsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsUnmappedErrorsInLegacyOrderWithoutLoadingAccounts()
    {
        var tenantScope = new Mock<IAccountingConfigurationTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(CancellationToken.None))
            .ReturnsAsync(7);
        var initializationStore = new Mock<IAccountingConfigurationInitializationStore>();
        initializationStore.Setup(store => store.EnsureInitializedAsync(7, null, CancellationToken.None))
            .ReturnsAsync(new AccountingConfigurationEntity { BusinessId = 7 });
        var readStore = new Mock<IAccountingPostingAccountReadStore>();
        var handler = new ValidateRequiredAccountingMappingsQueryHandler(
            tenantScope.Object,
            initializationStore.Object,
            readStore.Object);

        var errors = await handler.Handle(
            new ValidateRequiredAccountingMappingsQuery(),
            CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "slot 'cash' is not mapped.",
                "slot 'petty_cash' is not mapped.",
                "slot 'bank' is not mapped.",
                "slot 'inventory' is not mapped.",
                "slot 'sales' is not mapped.",
                "slot 'sales_discount' is not mapped.",
                "slot 'sales_return' is not mapped.",
                "slot 'cogs' is not mapped.",
                "slot 'inventory_shortage' is not mapped.",
                "slot 'inventory_gain' is not mapped.",
                "slot 'supplier_payable' is not mapped.",
                "slot 'supplier_advance' is not mapped.",
                "slot 'customer_advance' is not mapped.",
                "slot 'other_income' is not mapped.",
                "slot 'owner_capital' is not mapped.",
                "slot 'owner_drawings' is not mapped.",
                "slot 'retained_earnings' is not mapped.",
                "slot 'rounding_difference' is not mapped.",
                "slot 'cash_difference_gain' is not mapped.",
                "slot 'cash_difference_loss' is not mapped."
            },
            errors);
        readStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_BalanceSheetOnlyChecksRequiredAccountsInLegacyOrder()
    {
        var tenantScope = new Mock<IAccountingConfigurationTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(CancellationToken.None))
            .ReturnsAsync(7);
        var initializationStore = new Mock<IAccountingConfigurationInitializationStore>();
        initializationStore.Setup(store => store.EnsureInitializedAsync(7, null, CancellationToken.None))
            .ReturnsAsync(new AccountingConfigurationEntity
            {
                BusinessId = 7,
                CashAccountId = 1,
                PettyCashAccountId = 2,
                DefaultBankAccountId = 3,
                InventoryAccountId = 4,
                SupplierPayableAccountId = 5,
                CustomerAdvanceAccountId = 6,
                OwnerCapitalAccountId = 7,
                OwnerDrawingsAccountId = 8,
                SalesAccountId = 99
            });
        var readStore = new Mock<IAccountingPostingAccountReadStore>();
        readStore.Setup(store => store.GetChartAccountsForBusinessAsync(
                7,
                It.Is<IReadOnlyCollection<long>>(ids => ids.SequenceEqual(new long[] { 1, 2, 3, 4, 5, 6, 7, 8 })),
                CancellationToken.None))
            .ReturnsAsync(
            [
                Account(1, "Cash", ChartAccountTypes.Asset, isActive: false),
                Account(2, "Petty", ChartAccountTypes.Income),
                Account(3, "Bank", ChartAccountTypes.Asset),
                Account(4, "Inventory", ChartAccountTypes.Asset),
                Account(5, "Payable", ChartAccountTypes.Liability),
                Account(6, "Customer Advance", ChartAccountTypes.Liability),
                Account(7, "Owner Capital", ChartAccountTypes.Equity),
                Account(8, "Owner Drawings", ChartAccountTypes.Equity)
            ]);
        var handler = new ValidateRequiredAccountingMappingsQueryHandler(
            tenantScope.Object,
            initializationStore.Object,
            readStore.Object);

        var errors = await handler.Handle(
            new ValidateRequiredAccountingMappingsQuery(BalanceSheetOnly: true),
            CancellationToken.None);

        Assert.Equal(
            new[]
            {
                "slot 'cash' uses inactive account 'Cash' (1).",
                "slot 'petty_cash' requires account type(s) [Asset] but account 'Petty' (2) is 'Income'."
            },
            errors);
    }

    [Fact]
    public async Task Handle_PropagatesCancellationFromTenantScope()
    {
        using var cancellation = new CancellationTokenSource();
        var tenantScope = new Mock<IAccountingConfigurationTenantScope>();
        tenantScope.Setup(scope => scope.GetBusinessIdAsync(cancellation.Token))
            .ThrowsAsync(new OperationCanceledException(cancellation.Token));
        var initializationStore = new Mock<IAccountingConfigurationInitializationStore>();
        var readStore = new Mock<IAccountingPostingAccountReadStore>();
        var handler = new ValidateRequiredAccountingMappingsQueryHandler(
            tenantScope.Object,
            initializationStore.Object,
            readStore.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.Handle(new ValidateRequiredAccountingMappingsQuery(), cancellation.Token));

        initializationStore.VerifyNoOtherCalls();
        readStore.VerifyNoOtherCalls();
    }

    private static ChartAccount Account(
        long id,
        string name,
        string type,
        bool isActive = true) =>
        new()
        {
            Id = id,
            BusinessId = 7,
            AccountCode = name,
            AccountName = name,
            AccountType = type,
            IsActive = isActive
        };
}
