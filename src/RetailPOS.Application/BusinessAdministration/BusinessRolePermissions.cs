namespace RetailPOS.Application.BusinessAdministration;

public static class BusinessRolePermissions
{
    public static readonly IReadOnlyList<string> BusinessOwner = ["*"];

    public static readonly IReadOnlyList<string> OutletManager =
    [
        "users.view",
        "products.view", "products.create", "products.edit",
        "categories.view",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "stock_transfers.view", "stock_transfers.create", "stock_transfers.approve", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create",
        "stock_adjustments.view", "stock_adjustments.create",
        "sales.view", "sales.create",
        "reports.sales"
    ];

    public static readonly IReadOnlyList<string> SalesPerson =
    [
        "products.view",
        "stock_requisitions.view",
        "stock_transfers.view",
        "sales.view", "sales.create",
        "customers.view", "customers.create"
    ];

    public static readonly IReadOnlyList<string> AccountsAdmin =
    [
        "accounting.chart_accounts.view", "accounting.chart_accounts.create", "accounting.chart_accounts.edit",
        "accounting.configuration.view", "accounting.configuration.edit",
        "accounting.journals.view", "accounting.journals.create", "accounting.journals.update", "accounting.journals.cancel", "accounting.journals.post", "accounting.journals.reverse",
        "accounting.settlements.view", "accounting.settlements.create", "accounting.settlements.post", "accounting.settlements.reverse",
        "accounting.liquidity_transfers.view", "accounting.liquidity_transfers.create", "accounting.liquidity_transfers.post", "accounting.liquidity_transfers.reverse",
        "accounting.statement_import.view", "accounting.statement_import.create",
        "accounting.reconciliation.view", "accounting.reconciliation.create", "accounting.reconciliation.match", "accounting.reconciliation.unmatch",
        "accounting.reconciliation.close", "accounting.reconciliation.reopen", "accounting.adjustments.propose", "accounting.adjustments.approve", "accounting.adjustments.reverse", "accounting.exceptions.manage",
        "accounting.manual_journals.create", "accounting.manual_journals.post",
        "accounting.expenses.view", "accounting.expenses.create", "accounting.expenses.edit", "accounting.expenses.submit", "accounting.expenses.approve", "accounting.expenses.reject", "accounting.expenses.cancel",
        "accounting.other_income.view", "accounting.other_income.create", "accounting.other_income.edit", "accounting.other_income.submit", "accounting.other_income.approve", "accounting.other_income.cancel",
        "accounting.financial_years.view", "accounting.financial_years.manage", "accounting.financial_years.reopen",
        "accounting.periods.view", "accounting.periods.close", "accounting.periods.reopen", "accounting.close.prepare", "accounting.close.approve", "accounting.close.execute", "accounting.close.override_exception", "accounting.close.audit.view",
        "accounting.opening_balances.post",
        "accounting.opening_balances.adjust",
        "accounting.reports.general_ledger", "accounting.reports.trial_balance", "accounting.reports.profit_loss"
    ];

    public static readonly IReadOnlyList<string> WarehouseManager =
    [
        "inventory.view", "inventory.edit",
        "stock_adjustments.view", "stock_adjustments.create",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "stock_transfers.view", "stock_transfers.view_all_locations", "stock_transfers.create", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
        "low_stock_alerts.view",
        "warehouses.view",
        "purchases.view", "purchases.create", "purchases.receive"
    ];
}
