namespace RetailPOS.Application.Authorization;

public static class AccountingManagerPermissions
{
    public const string RoleName = "Accounting Manager";

    public static readonly IReadOnlyList<string> All = new[]
    {
        "suppliers.view",
        "accounting.dashboard.view",
        "accounting.chart_accounts.view", "accounting.chart_accounts.create", "accounting.chart_accounts.edit",
        "accounting.configuration.view",
        "accounting.journals.view",
        "accounting.manual_journals.view", "accounting.manual_journals.create", "accounting.manual_journals.update", "accounting.manual_journals.submit", "accounting.manual_journals.cancel",
        "accounting.expenses.view", "accounting.expenses.create", "accounting.expenses.edit", "accounting.expenses.submit", "accounting.expenses.cancel",
        "accounting.supplier_payments.view", "accounting.supplier_payments.create", "accounting.supplier_payments.submit", "accounting.supplier_payments.cancel",
        "accounting.supplier_advances.view", "accounting.supplier_advances.create", "accounting.supplier_advances.submit", "accounting.supplier_advances.cancel",
        "accounting.other_income.view", "accounting.other_income.create", "accounting.other_income.edit", "accounting.other_income.submit", "accounting.other_income.cancel",
        "accounting.settlements.view", "accounting.settlements.create",
        "accounting.liquidity_transfers.view", "accounting.liquidity_transfers.create",
        "accounting.statement_import.view", "accounting.statement_import.create",
        "accounting.reconciliation.view", "accounting.reconciliation.create", "accounting.reconciliation.match", "accounting.reconciliation.unmatch",
        "accounting.adjustments.propose", "accounting.exceptions.manage",
        "accounting.financial_years.view", "accounting.periods.view",
        "accounting.close.prepare", "accounting.close.audit.view",
        "accounting.reports.general_ledger", "accounting.reports.trial_balance", "accounting.reports.profit_loss", "accounting.reports.balance_sheet",
        "accounting.reports.cash_book", "accounting.reports.cash_flow", "accounting.reports.profitability",
        "accounting.reports.cogs", "accounting.reports.inventory_valuation",
        "accounting.reports.ap_subledger", "accounting.reports.ap_aging", "accounting.reports.supplier_statement", "accounting.reports.ap_reconciliation", "accounting.reports.vat",
        "reports.export"
    };
}
