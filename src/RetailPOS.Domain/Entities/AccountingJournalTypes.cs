namespace RetailPOS.Core.Entities;

public static class AccountingJournalTypes
{
    public const string ManualJournal = "ManualJournal";
    public const string SystemJournal = "SystemJournal";
    public const string JournalReversal = "JournalReversal";
    public const string OpeningBalance = "OpeningBalance";
    public const string OpeningBalanceAdjustment = "OpeningBalanceAdjustment";
    public const string YearEndClosing = "YearEndClosing";
}

public static class AccountingReferenceTypes
{
    public const string OpeningBalance = "OpeningBalance";
    public const string OpeningBalanceAdjustment = "OpeningBalanceAdjustment";
    public const string FinancialYearClose = "FinancialYearClose";
}
