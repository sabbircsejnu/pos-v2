namespace RetailPOS.Core.Entities;

public class Business
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? SubscriptionPlan { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }
    public int? MaxOutlets { get; set; }
    public int? MaxUsers { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Platform-controlled: whether receipts print <see cref="PoweredByText"/>. Only a Platform Super Admin may change it.</summary>
    public bool ShowPoweredByOnReceipt { get; set; }

    /// <summary>Platform-controlled receipt footer text, e.g. "Powered by: Irani Mart Technology".</summary>
    public string? PoweredByText { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<User> Users { get; set; } = new List<User>();
    public virtual ICollection<Outlet> Outlets { get; set; } = new List<Outlet>();
    public virtual ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
    public virtual ICollection<Account> Accounts { get; set; } = new List<Account>();
    public virtual ICollection<ChartAccount> ChartAccounts { get; set; } = new List<ChartAccount>();
    public virtual ICollection<AccountingConfiguration> AccountingConfigurations { get; set; } = new List<AccountingConfiguration>();
    public virtual ICollection<AccountingOutletConfiguration> AccountingOutletConfigurations { get; set; } = new List<AccountingOutletConfiguration>();
    public virtual ICollection<AccountingNumberSequence> AccountingNumberSequences { get; set; } = new List<AccountingNumberSequence>();
    public virtual ICollection<FinancialYear> FinancialYears { get; set; } = new List<FinancialYear>();
    public virtual ICollection<ExpenseCategory> ExpenseCategories { get; set; } = new List<ExpenseCategory>();
    public virtual ICollection<Expense> Expenses { get; set; } = new List<Expense>();
    public virtual ICollection<JournalEntry> JournalEntries { get; set; } = new List<JournalEntry>();
    public virtual ICollection<OtherIncomeEntry> OtherIncomeEntries { get; set; } = new List<OtherIncomeEntry>();
    public virtual ICollection<BusinessFeatureSetting> FeatureSettings { get; set; } = new List<BusinessFeatureSetting>();
    public virtual ICollection<BusinessReminderNotification> ReminderNotifications { get; set; } = new List<BusinessReminderNotification>();
}