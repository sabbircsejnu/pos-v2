using RetailPOS.Core.Entities;

namespace RetailPOS.Core.Accounting;

/// <summary>What the eligibility rule needs to know about an account; the caller loads it for the current Business.</summary>
public sealed record ExpenseAccountFacts(
    string AccountCode,
    string AccountType,
    bool IsActive,
    bool IsSystemAccount,
    bool IsControlAccount,
    bool AllowManualPosting,
    bool HasActiveChildren);

/// <summary>
/// D2: which ledger account may be newly chosen to receive an expense debit. It is the single rule behind the expense
/// account lookup, category create/remap and the approval override. An unchanged category mapping is not re-judged
/// here, so an older mapping keeps working until someone changes it.
/// </summary>
public static class ExpenseAccountEligibilityPolicy
{
    /// <summary>
    /// System accounts mostly carry operational postings (Sales Discounts, Sales Returns, Inventory Shortage…), so one
    /// may receive ordinary expenses only when it is named here.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedSystemAccountCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AccountingDefaults.BankFeeExpenseCode
    };

    public static bool IsEligible(ExpenseAccountFacts account) => RefusalReason(account) is null;

    /// <returns>Why the account cannot be chosen, or null when it can.</returns>
    public static string? RefusalReason(ExpenseAccountFacts account)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!account.IsActive)
            return $"Expense account '{account.AccountCode}' is inactive.";
        if (!string.Equals(account.AccountType, ChartAccountTypes.Expense, StringComparison.OrdinalIgnoreCase))
            return $"Account '{account.AccountCode}' is not a valid expense posting account.";
        if (account.IsControlAccount)
            return $"Account '{account.AccountCode}' is a control account and cannot receive expenses directly.";
        if (account.HasActiveChildren)
            return $"Account '{account.AccountCode}' is a grouping account with active child accounts. Choose one of its child accounts.";
        if (account.IsSystemAccount && !AllowedSystemAccountCodes.Contains(account.AccountCode))
            return $"System account '{account.AccountCode}' is reserved for automatic postings and cannot be used for expense categories.";
        if (!account.AllowManualPosting)
            return $"Account '{account.AccountCode}' does not allow direct postings.";
        return null;
    }
}
