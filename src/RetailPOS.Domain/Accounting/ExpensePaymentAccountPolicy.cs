using RetailPOS.Core.Entities;

namespace RetailPOS.Core.Accounting;

/// <summary>
/// How the current Business classifies its funding accounts for one expense location. No account subtype exists, so this
/// comes from Accounting Configuration and the bank register. A Cash Session has no ledger account of its own: the POS
/// drawer uses the outlet's posting cash account, the same outlet-override-then-business rule the posting resolver uses.
/// </summary>
public sealed record ExpensePaymentAccountMap(
    long? DrawerCashAccountId,
    IReadOnlySet<long> CashAccountIds,
    IReadOnlySet<long> BankAccountIds);

/// <summary>
/// D4: the Payment Source says where the money comes from and the Payment Account is the ledger credited, so they must
/// agree. A row with no source keeps the documented legacy meaning of Bank.
/// </summary>
public static class ExpensePaymentAccountPolicy
{
    public static IReadOnlySet<long> AllowedAccountIds(string? paymentSource, ExpensePaymentAccountMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return paymentSource switch
        {
            ExpensePaymentSources.PosDrawer => map.DrawerCashAccountId is { } drawer ? new HashSet<long> { drawer } : new HashSet<long>(),
            ExpensePaymentSources.PettyCash => map.CashAccountIds,
            _ => map.BankAccountIds
        };
    }

    /// <returns>Why the account cannot pay from this source, or null when it can.</returns>
    public static string? RefusalReason(string? paymentSource, long accountId, string accountCode, ExpensePaymentAccountMap map)
    {
        if (AllowedAccountIds(paymentSource, map).Contains(accountId))
            return null;

        return paymentSource switch
        {
            ExpensePaymentSources.PosDrawer when map.DrawerCashAccountId is null =>
                "A POS Drawer expense needs an outlet with a cash account mapped in Accounting Configuration.",
            ExpensePaymentSources.PosDrawer =>
                $"A POS Drawer expense must credit the outlet's cash account; account '{accountCode}' is not that account.",
            ExpensePaymentSources.PettyCash =>
                $"A Petty Cash expense must credit a cash or petty cash account; account '{accountCode}' is not mapped as one for this location in Accounting Configuration.",
            _ =>
                $"A Bank expense must credit a bank account; account '{accountCode}' is not mapped as one for this location in Accounting Configuration or the bank register."
        };
    }
}
