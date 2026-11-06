using RetailPOS.Core.Accounting;
using RetailPOS.Core.Entities;

namespace RetailPOS.UnitTests.Accounting;

public sealed class ExpenseAccountEligibilityPolicyTests
{
    private static ExpenseAccountFacts Account(
        string code = "7100",
        string type = ChartAccountTypes.Expense,
        bool active = true,
        bool system = false,
        bool control = false,
        bool manual = true,
        bool children = false) =>
        new(code, type, active, system, control, manual, children);

    [Fact]
    public void OrdinaryCustomExpenseAccount_IsEligible() =>
        Assert.True(ExpenseAccountEligibilityPolicy.IsEligible(Account()));

    [Fact]
    public void BankFeeSystemAccount_IsTheApprovedException() =>
        Assert.True(ExpenseAccountEligibilityPolicy.IsEligible(Account(code: AccountingDefaults.BankFeeExpenseCode, system: true)));

    [Theory]
    [InlineData(AccountingDefaults.SalesDiscountCode)]
    [InlineData(AccountingDefaults.SalesReturnCode)]
    [InlineData(AccountingDefaults.InventoryShortageCode)]
    [InlineData(AccountingDefaults.RoundingDifferenceCode)]
    [InlineData(AccountingDefaults.CashDifferenceLossCode)]
    [InlineData(AccountingDefaults.SettlementFeeExpenseCode)]
    public void OtherSystemExpenseAccounts_AreRefused(string code)
    {
        var reason = ExpenseAccountEligibilityPolicy.RefusalReason(Account(code: code, system: true));

        Assert.Contains("reserved for automatic postings", reason);
    }

    [Fact]
    public void AllowlistCode_OnACustomAccountStaysSubjectToEveryOtherRule() =>
        Assert.False(ExpenseAccountEligibilityPolicy.IsEligible(Account(code: AccountingDefaults.BankFeeExpenseCode, system: true, control: true)));

    [Fact]
    public void InactiveAccount_IsRefused() =>
        Assert.Contains("inactive", ExpenseAccountEligibilityPolicy.RefusalReason(Account(active: false)));

    [Theory]
    [InlineData(ChartAccountTypes.Asset)]
    [InlineData(ChartAccountTypes.Liability)]
    [InlineData(ChartAccountTypes.Equity)]
    [InlineData(ChartAccountTypes.Income)]
    [InlineData(ChartAccountTypes.CostOfGoodsSold)]
    public void NonExpenseTypes_AreRefused(string type) =>
        Assert.Contains("not a valid expense posting account", ExpenseAccountEligibilityPolicy.RefusalReason(Account(type: type)));

    [Fact]
    public void ControlAccount_IsRefused() =>
        Assert.Contains("control account", ExpenseAccountEligibilityPolicy.RefusalReason(Account(control: true)));

    [Fact]
    public void ParentWithActiveChildren_IsRefused() =>
        Assert.Contains("grouping account", ExpenseAccountEligibilityPolicy.RefusalReason(Account(children: true)));

    [Fact]
    public void AccountThatRefusesDirectPostings_IsRefused() =>
        Assert.Contains("direct postings", ExpenseAccountEligibilityPolicy.RefusalReason(Account(manual: false)));
}

public sealed class ExpensePaymentAccountPolicyTests
{
    private const long Cash = 1000;
    private const long Petty = 1010;
    private const long Bank = 1020;
    private const long Other = 1500;

    private static readonly ExpensePaymentAccountMap Map = new(Cash, new HashSet<long> { Cash, Petty }, new HashSet<long> { Bank });

    [Theory]
    [InlineData(ExpensePaymentSources.PosDrawer, Cash, true)]
    [InlineData(ExpensePaymentSources.PosDrawer, Petty, false)]
    [InlineData(ExpensePaymentSources.PosDrawer, Bank, false)]
    [InlineData(ExpensePaymentSources.PosDrawer, Other, false)]
    [InlineData(ExpensePaymentSources.PettyCash, Cash, true)]
    [InlineData(ExpensePaymentSources.PettyCash, Petty, true)]
    [InlineData(ExpensePaymentSources.PettyCash, Bank, false)]
    [InlineData(ExpensePaymentSources.Bank, Bank, true)]
    [InlineData(ExpensePaymentSources.Bank, Cash, false)]
    [InlineData(ExpensePaymentSources.Bank, Other, false)]
    public void Source_MustAgreeWithTheAccount(string source, long accountId, bool allowed)
    {
        var reason = ExpensePaymentAccountPolicy.RefusalReason(source, accountId, "X", Map);

        Assert.Equal(allowed, reason is null);
    }

    [Theory]
    [InlineData(Bank, true)]
    [InlineData(Cash, false)]
    public void MissingSource_KeepsTheLegacyMeaningOfBank(long accountId, bool allowed) =>
        Assert.Equal(allowed, ExpensePaymentAccountPolicy.RefusalReason(null, accountId, "X", Map) is null);

    [Fact]
    public void PosDrawer_WithoutAMappedCashAccount_SaysWhatIsMissing()
    {
        var map = new ExpensePaymentAccountMap(null, new HashSet<long>(), new HashSet<long> { Bank });

        Assert.Contains("needs an outlet", ExpensePaymentAccountPolicy.RefusalReason(ExpensePaymentSources.PosDrawer, Bank, "X", map));
        Assert.Empty(ExpensePaymentAccountPolicy.AllowedAccountIds(ExpensePaymentSources.PosDrawer, map));
    }
}

public sealed class ChartAccountDeactivationPolicyTests
{
    [Fact]
    public void NoDependencies_AllowsDeactivation() =>
        Assert.Null(ChartAccountDeactivationPolicy.RefusalReason(new ChartAccountDependencies()));

    [Fact]
    public void SingleDependency_ReadsAsAWholeSentenceWithItsCount() =>
        Assert.Equal(
            "Cannot deactivate this account because it is used by 2 active expense categories.",
            ChartAccountDeactivationPolicy.RefusalReason(new ChartAccountDependencies(ActiveExpenseCategories: 2)));

    [Fact]
    public void EveryDependencyType_IsNamed()
    {
        var reason = ChartAccountDeactivationPolicy.RefusalReason(new ChartAccountDependencies(1, 1, true, true, 1, 1, 1, 1));

        Assert.Contains("1 active expense category", reason);
        Assert.Contains("1 active child account", reason);
        Assert.Contains("accounting configuration", reason);
        Assert.Contains("journal transactions", reason);
        Assert.Contains("1 active bank account", reason);
        Assert.Contains("1 unapproved expense", reason);
        Assert.Contains("1 unapproved other income entry", reason);
        Assert.Contains("1 unapproved supplier payment", reason);
    }
}
