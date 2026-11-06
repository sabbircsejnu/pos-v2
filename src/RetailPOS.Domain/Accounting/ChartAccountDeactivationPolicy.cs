namespace RetailPOS.Core.Accounting;

/// <summary>Everything that keeps an account in use, counted within the account's own Business.</summary>
public sealed record ChartAccountDependencies(
    int ActiveExpenseCategories = 0,
    int ActiveChildAccounts = 0,
    bool MappedInConfiguration = false,
    bool ReferencedByJournals = false,
    int ActiveBankAccounts = 0,
    int UnapprovedExpenses = 0,
    int UnapprovedOtherIncome = 0,
    int UnapprovedSupplierPayments = 0);

/// <summary>D5: the one rule for moving an account from Active to Inactive, used by every path that can do so.</summary>
public static class ChartAccountDeactivationPolicy
{
    /// <returns>A message naming every blocking dependency, or null when the account can be deactivated.</returns>
    public static string? RefusalReason(ChartAccountDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);

        var reasons = new List<string>();
        if (dependencies.ActiveExpenseCategories > 0)
            reasons.Add($"it is used by {Count(dependencies.ActiveExpenseCategories, "active expense category", "active expense categories")}");
        if (dependencies.ActiveChildAccounts > 0)
            reasons.Add($"it has {Count(dependencies.ActiveChildAccounts, "active child account", "active child accounts")}");
        if (dependencies.MappedInConfiguration)
            reasons.Add("it is mapped in accounting configuration");
        if (dependencies.ReferencedByJournals)
            reasons.Add("it is referenced by journal transactions");
        if (dependencies.ActiveBankAccounts > 0)
            reasons.Add($"it is linked to {Count(dependencies.ActiveBankAccounts, "active bank account", "active bank accounts")}");
        if (dependencies.UnapprovedExpenses > 0)
            reasons.Add($"it would strand {Count(dependencies.UnapprovedExpenses, "unapproved expense", "unapproved expenses")}");
        if (dependencies.UnapprovedOtherIncome > 0)
            reasons.Add($"it would strand {Count(dependencies.UnapprovedOtherIncome, "unapproved other income entry", "unapproved other income entries")}");
        if (dependencies.UnapprovedSupplierPayments > 0)
            reasons.Add($"it would strand {Count(dependencies.UnapprovedSupplierPayments, "unapproved supplier payment", "unapproved supplier payments")}");

        return reasons.Count == 0
            ? null
            : $"Cannot deactivate this account because {Join(reasons)}.";
    }

    private static string Count(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private static string Join(List<string> reasons) =>
        reasons.Count == 1
            ? reasons[0]
            : $"{string.Join(", ", reasons.Take(reasons.Count - 1))} and {reasons[^1]}";
}
