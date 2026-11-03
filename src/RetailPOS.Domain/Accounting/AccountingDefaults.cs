using RetailPOS.Core.Entities;

namespace RetailPOS.Core.Accounting;

public sealed class DefaultChartAccountDefinition
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string AccountType { get; init; }
    public required string NormalBalanceType { get; init; }
    public bool IsControlAccount { get; init; }
    public bool AllowManualPosting { get; init; } = true;
    public int DisplayOrder { get; init; }
}

public static class AccountingDefaults
{
    public const string CashOnHandCode = "1000";
    public const string PettyCashCode = "1010";
    public const string BankCode = "1020";
    public const string InventoryCode = "1100";
    public const string SupplierPayableCode = "2000";
    public const string SupplierAdvanceCode = "2010";
    public const string CustomerAdvanceCode = "2100";
    public const string VatReceivableCode = "1200";
    public const string CardClearingCode = "1030";
    public const string MobileMfsClearingCode = "1040";
    public const string SettlementFeeExpenseCode = "6300";
    public const string BankFeeExpenseCode = "6310";
    public const string BankInterestIncomeCode = "6010";
    public const string OwnerCapitalCode = "3000";
    public const string OwnerDrawingsCode = "3100";
    public const string RetainedEarningsCode = "3200";
    public const string SalesCode = "4000";
    public const string SalesDiscountCode = "4010";
    public const string SalesReturnCode = "4020";
    public const string CogsCode = "5000";
    public const string InventoryShortageCode = "5100";
    public const string InventoryGainCode = "5200";
    public const string OtherIncomeCode = "6000";
    public const string RoundingDifferenceCode = "6100";
    public const string CashDifferenceGainCode = "6200";
    public const string CashDifferenceLossCode = "6210";

    public static readonly IReadOnlyList<DefaultChartAccountDefinition> ChartOfAccounts =
    [
        new() { Code = CashOnHandCode, Name = "Cash on Hand", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = true, DisplayOrder = 10 },
        new() { Code = PettyCashCode, Name = "Petty Cash", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = false, DisplayOrder = 20 },
        new() { Code = BankCode, Name = "Bank Account", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = true, DisplayOrder = 30 },
        new() { Code = InventoryCode, Name = "Inventory", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = true, AllowManualPosting = false, DisplayOrder = 40 },
        new() { Code = SupplierPayableCode, Name = "Supplier Payables", AccountType = ChartAccountTypes.Liability, NormalBalanceType = NormalBalanceTypes.Credit, IsControlAccount = true, DisplayOrder = 50 },
        new() { Code = SupplierAdvanceCode, Name = "Supplier Advances", AccountType = ChartAccountTypes.Liability, NormalBalanceType = NormalBalanceTypes.Credit, IsControlAccount = true, DisplayOrder = 55 },
        new() { Code = CustomerAdvanceCode, Name = "Customer Advances", AccountType = ChartAccountTypes.Liability, NormalBalanceType = NormalBalanceTypes.Credit, IsControlAccount = true, DisplayOrder = 60 },
        new() { Code = VatReceivableCode, Name = "Input VAT Receivable", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = true, DisplayOrder = 65 },
        new() { Code = CardClearingCode, Name = "Card Clearing", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = true, DisplayOrder = 35 },
        new() { Code = MobileMfsClearingCode, Name = "Mobile/MFS Clearing", AccountType = ChartAccountTypes.Asset, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = true, DisplayOrder = 36 },
        new() { Code = SettlementFeeExpenseCode, Name = "Settlement Fee Expense", AccountType = ChartAccountTypes.Expense, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = false, DisplayOrder = 67 },
        new() { Code = BankFeeExpenseCode, Name = "Bank Fee Expense", AccountType = ChartAccountTypes.Expense, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = false, DisplayOrder = 68 },
        new() { Code = BankInterestIncomeCode, Name = "Bank Interest Income", AccountType = ChartAccountTypes.Income, NormalBalanceType = NormalBalanceTypes.Credit, IsControlAccount = false, DisplayOrder = 151 },
        new() { Code = OwnerCapitalCode, Name = "Owner Capital", AccountType = ChartAccountTypes.Equity, NormalBalanceType = NormalBalanceTypes.Credit, IsControlAccount = false, DisplayOrder = 70 },
        new() { Code = OwnerDrawingsCode, Name = "Owner Drawings", AccountType = ChartAccountTypes.Equity, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = false, DisplayOrder = 80 },
        new() { Code = RetainedEarningsCode, Name = "Retained Earnings", AccountType = ChartAccountTypes.Equity, NormalBalanceType = NormalBalanceTypes.Credit, IsControlAccount = false, AllowManualPosting = false, DisplayOrder = 85 },
        new() { Code = SalesCode, Name = "Sales Revenue", AccountType = ChartAccountTypes.Income, NormalBalanceType = NormalBalanceTypes.Credit, IsControlAccount = false, AllowManualPosting = false, DisplayOrder = 90 },
        new() { Code = SalesDiscountCode, Name = "Sales Discounts", AccountType = ChartAccountTypes.Expense, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = false, AllowManualPosting = false, DisplayOrder = 100 },
        new() { Code = SalesReturnCode, Name = "Sales Returns", AccountType = ChartAccountTypes.Expense, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = false, AllowManualPosting = false, DisplayOrder = 110 },
        new() { Code = CogsCode, Name = "Cost of Goods Sold", AccountType = ChartAccountTypes.CostOfGoodsSold, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = false, AllowManualPosting = false, DisplayOrder = 120 },
        new() { Code = InventoryShortageCode, Name = "Inventory Shortage", AccountType = ChartAccountTypes.Expense, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = false, DisplayOrder = 130 },
        new() { Code = InventoryGainCode, Name = "Inventory Gain", AccountType = ChartAccountTypes.Income, NormalBalanceType = NormalBalanceTypes.Credit, IsControlAccount = false, DisplayOrder = 140 },
        new() { Code = OtherIncomeCode, Name = "Other Income", AccountType = ChartAccountTypes.Income, NormalBalanceType = NormalBalanceTypes.Credit, IsControlAccount = false, DisplayOrder = 150 },
        new() { Code = RoundingDifferenceCode, Name = "Rounding Difference", AccountType = ChartAccountTypes.Expense, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = false, DisplayOrder = 160 },
        new() { Code = CashDifferenceGainCode, Name = "Cash Difference Gain", AccountType = ChartAccountTypes.Income, NormalBalanceType = NormalBalanceTypes.Credit, IsControlAccount = false, DisplayOrder = 170 },
        new() { Code = CashDifferenceLossCode, Name = "Cash Difference Loss", AccountType = ChartAccountTypes.Expense, NormalBalanceType = NormalBalanceTypes.Debit, IsControlAccount = false, DisplayOrder = 180 }
    ];
}
