namespace RetailPOS.Core.Entities;

public static class AccountingSequenceTypes
{
    public const string Journal = "Journal";
    public const string ExpenseReference = "ExpenseReference";
}

public static class AccountingSequenceResetPolicies
{
    public const string Continuous = "Continuous";
    public const string PerFinancialYear = "PerFinancialYear";
    public const string PerBusinessDay = "PerBusinessDay";
}

public class AccountingNumberSequence
{
    public long Id { get; set; }
    public long BusinessId { get; set; }
    public string SequenceType { get; set; } = AccountingSequenceTypes.Journal;
    public long? FinancialYearId { get; set; }
    public string ScopeKey { get; set; } = string.Empty;
    public string Prefix { get; set; } = "JR";
    public int Padding { get; set; } = 6;
    public long LastIssuedNumber { get; set; }
    public string ResetPolicy { get; set; } = AccountingSequenceResetPolicies.Continuous;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long? UpdatedBy { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual FinancialYear? FinancialYear { get; set; }
}