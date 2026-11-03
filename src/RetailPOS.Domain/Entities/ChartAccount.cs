namespace RetailPOS.Core.Entities;

public static class ChartAccountTypes
{
    public const string Asset = "Asset";
    public const string Liability = "Liability";
    public const string Equity = "Equity";
    public const string Income = "Income";
    public const string CostOfGoodsSold = "CostOfGoodsSold";
    public const string Expense = "Expense";
}

public static class NormalBalanceTypes
{
    public const string Debit = "Debit";
    public const string Credit = "Credit";
}

public class ChartAccount
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public long? ParentAccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AccountType { get; set; } = ChartAccountTypes.Asset;
    public string NormalBalanceType { get; set; } = NormalBalanceTypes.Debit;
    public bool IsSystemAccount { get; set; }
    public bool IsControlAccount { get; set; }
    public bool AllowManualPosting { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual ChartAccount? ParentAccount { get; set; }
    public virtual ICollection<ChartAccount> ChildAccounts { get; set; } = new List<ChartAccount>();
    public virtual ICollection<ExpenseCategory> ExpenseCategories { get; set; } = new List<ExpenseCategory>();
    public virtual ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();
}
