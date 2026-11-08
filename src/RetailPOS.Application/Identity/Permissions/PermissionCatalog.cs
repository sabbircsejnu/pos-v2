namespace RetailPOS.Application.Identity.Permissions;

public static class PermissionCatalog
{
    /// <summary>Configure a user's context switching with mapped roles and selected locations.</summary>
    public const string UserContextAccessManage = "users.context_access.manage";

    /// <summary>Additionally grant All Business Roles or All Business Locations.</summary>
    public const string UserContextAccessGrantAll = "users.context_access.grant_all";

    /// <summary>Read the whole Sales &amp; Finance dashboard of the current Business. Grants no change to any transaction.</summary>
    public const string DashboardSalesFinanceView = "dashboard.sales_finance.view";

    /// <summary>Read the whole Inventory Intelligence dashboard of the current Business. Grants no change to any transaction.</summary>
    public const string DashboardInventoryIntelligenceView = "dashboard.inventory_intelligence.view";

    /// <summary>
    /// Business-wide Expense Category configuration, including which ledger account each category debits. Deliberately
    /// has no alias: entering or editing expenses does not imply it.
    /// </summary>
    public const string ExpenseCategoriesManage = "accounting.expense_categories.manage";

    /// <summary>Read the unit of measurement catalog.</summary>
    public const string UomView = "uom.view";

    /// <summary>Create, edit and deactivate units of measurement.</summary>
    public const string UomManage = "uom.manage";

    /// <summary>Read sealed, loose and prepared stock.</summary>
    public const string PackStateView = "inventory.pack_state.view";

    /// <summary>Open sealed packs at an authorized location.</summary>
    public const string PackStateManage = "inventory.pack_state.manage";

    /// <summary>Read repack jobs.</summary>
    public const string RepackingView = "inventory.repacking.view";

    /// <summary>Create, execute, cancel and reverse repack jobs at an authorized location.</summary>
    public const string RepackingExecute = "inventory.repacking.execute";

    /// <summary>Start, look up, preview and confirm a product exchange (Exchange EX-16).</summary>
    public const string ExchangeCreate = "pos.exchange.create";

    /// <summary>Confirm an exchange that pays a refund, or approve such a refund as supervisor (EX-16, OC-03).</summary>
    public const string ExchangeRefund = "pos.exchange.refund";

    /// <summary>Supervisor approval of an expired exchange window, and of a high-value exchange (EX-16, C-10).</summary>
    public const string ExchangeOverrideWindow = "pos.exchange.override_window";

    /// <summary>Supervisor approval of a replacement-chain window or count exception (EX-16, OC-02).</summary>
    public const string ExchangeOverrideChain = "pos.exchange.override_chain";

    /// <summary>Reverse a completed exchange with compensating effects (EX-16, EX-14).</summary>
    public const string ExchangeReverse = "pos.exchange.reverse";

    /// <summary>Exactly the five approved exchange permissions (EX-16, C-01); there is no sixth.</summary>
    public static readonly string[] ExchangePermissions =
    {
        ExchangeCreate, ExchangeRefund, ExchangeOverrideWindow, ExchangeOverrideChain, ExchangeReverse
    };

    public static readonly string[] All = new[]
    {
        ExchangeCreate, ExchangeRefund, ExchangeOverrideWindow, ExchangeOverrideChain, ExchangeReverse,
        UomView, UomManage, PackStateView, PackStateManage, RepackingView, RepackingExecute,
        "users.view", "users.create", "users.edit", "users.delete",
        UserContextAccessManage, UserContextAccessGrantAll,
        "roles.view", "roles.create", "roles.edit", "roles.delete",
        "products.view", "products.create", "products.edit", "products.delete",
        "products.view_cost",
        "variations.view", "variations.create", "variations.edit", "variations.delete",
        "barcode.view", "barcode.print", "barcode.bulk_print", "barcode.template_manage",
        "categories.view", "categories.create", "categories.edit", "categories.delete",
        "inventory.view", "inventory.create", "inventory.edit", "inventory.delete",
        "stock_adjustments.view", "stock_adjustments.create", "stock_adjustments.edit", "stock_adjustments.delete", "stock_adjustments.approve", "stock_adjustments.reject", "stock_adjustments.cancel",
        "stock_transfers.view", "stock_transfers.view_all_locations", "stock_transfers.create", "stock_transfers.edit", "stock_transfers.delete", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "StockCount.ViewOwn", "StockCount.ViewAll", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Approve", "StockCount.Reject", "StockCount.Reopen",
        "low_stock_alerts.view", "low_stock_alerts.create", "low_stock_alerts.edit", "low_stock_alerts.delete",
        "sales.view", "sales.create", "sales.backdate", "sales.void", "sales.refund",
        "sales.discount.apply", "sales.discount.override_limit", "sales.discount.configure",
        "sales.financial_postings.view", "sales.financial_postings.retry",
        "cash_sessions.view", "cash_sessions.open", "cash_sessions.close",
        "cash_sessions.handover.confirm", "cash_sessions.attachments.upload", "cash_sessions.attachments.download",
        "cash_sessions.variance.view", "cash_sessions.variance.approve", "cash_sessions.variance.retry_posting",
        "cash_sessions.expenses.pay", "expenses.self_approve",
        "purchases.view", "purchases.create", "purchases.edit", "purchases.approve", "purchases.receive",
        "grn.view", "grn.create", "grn.receive",
        "customers.view", "customers.create", "customers.edit", "customers.delete",
        "suppliers.view", "suppliers.create", "suppliers.edit", "suppliers.delete",
        "reports.sales", "reports.inventory", "reports.financial", "reports.export",
        "settings.view", "settings.edit",
        "audit.view",
        "outlets.view", "outlets.create", "outlets.edit", "outlets.delete", "outlets.terminals.manage",
        "warehouses.view", "warehouses.create", "warehouses.edit", "warehouses.delete",
        "accounts.view", "accounts.create", "accounts.edit", "accounts.delete",
        "transactions.view", "transactions.create", "transactions.edit", "transactions.delete",
        "expenses.view", "expenses.create", "expenses.edit", "expenses.delete",
        DashboardSalesFinanceView, DashboardInventoryIntelligenceView,
        "accounting.dashboard.view",
        "accounting.chart_accounts.view", "accounting.chart_accounts.create", "accounting.chart_accounts.edit",
        "accounting.configuration.view", "accounting.configuration.edit",
        "accounting.journals.view", "accounting.journals.create", "accounting.journals.update", "accounting.journals.cancel", "accounting.journals.post", "accounting.journals.reverse",
        "accounting.settlements.view", "accounting.settlements.create", "accounting.settlements.post", "accounting.settlements.reverse",
        "accounting.liquidity_transfers.view", "accounting.liquidity_transfers.create", "accounting.liquidity_transfers.post", "accounting.liquidity_transfers.reverse",
        "accounting.statement_import.view", "accounting.statement_import.create",
        "accounting.reconciliation.view", "accounting.reconciliation.create", "accounting.reconciliation.match", "accounting.reconciliation.unmatch",
        "accounting.reconciliation.close", "accounting.reconciliation.reopen", "accounting.adjustments.propose", "accounting.adjustments.approve", "accounting.adjustments.reverse", "accounting.exceptions.manage",
        "accounting.manual_journals.view", "accounting.manual_journals.create", "accounting.manual_journals.update", "accounting.manual_journals.submit", "accounting.manual_journals.approve", "accounting.manual_journals.reject", "accounting.manual_journals.cancel", "accounting.manual_journals.post", "accounting.manual_journals.reverse",
        "accounting.expenses.view", "accounting.expenses.create", "accounting.expenses.edit", "accounting.expenses.submit", "accounting.expenses.approve", "accounting.expenses.reject", "accounting.expenses.cancel",
        ExpenseCategoriesManage,
        "accounting.supplier_payments.view", "accounting.supplier_payments.create", "accounting.supplier_payments.submit", "accounting.supplier_payments.approve", "accounting.supplier_payments.reject", "accounting.supplier_payments.cancel",
        "accounting.supplier_advances.view", "accounting.supplier_advances.create", "accounting.supplier_advances.submit", "accounting.supplier_advances.approve", "accounting.supplier_advances.reject", "accounting.supplier_advances.cancel",
        "accounting.financial_years.view", "accounting.financial_years.manage",
        "accounting.periods.view", "accounting.periods.close", "accounting.periods.reopen",
        "accounting.close.prepare", "accounting.close.approve", "accounting.close.execute", "accounting.close.override_exception", "accounting.close.audit.view",
        "accounting.financial_years.reopen",
        "accounting.opening_balances.post",
        "accounting.opening_balances.adjust",
        "accounting.other_income.view", "accounting.other_income.create", "accounting.other_income.edit", "accounting.other_income.submit", "accounting.other_income.approve", "accounting.other_income.reject", "accounting.other_income.cancel",
        "accounting.reports.general_ledger", "accounting.reports.trial_balance", "accounting.reports.profit_loss", "accounting.reports.balance_sheet", "accounting.reports.cash_book", "accounting.reports.cash_flow", "accounting.reports.profitability", "accounting.reports.expense", "accounting.reports.income", "accounting.reports.cogs", "accounting.reports.inventory_valuation", "accounting.reports.cash_closing", "accounting.reports.ap_subledger", "accounting.reports.ap_aging", "accounting.reports.supplier_statement", "accounting.reports.ap_reconciliation", "accounting.reports.vat"
    }.Concat(HrPayrollPermissionCatalog.All).ToArray();

    private static readonly IReadOnlyDictionary<string, string[]> Aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["accounting.chart_accounts.view"] = ["accounts.view"],
        ["accounting.chart_accounts.create"] = ["accounts.create"],
        ["accounting.chart_accounts.edit"] = ["accounts.edit", "accounts.delete"],
        ["accounting.journals.view"] = ["transactions.view", "accounting.manual_journals.view"],
        ["accounting.journals.create"] = ["transactions.create"],
        ["accounting.journals.update"] = ["transactions.edit"],
        ["accounting.journals.cancel"] = ["transactions.delete"],
        ["accounting.journals.post"] = ["transactions.create", "transactions.edit"],
        ["accounting.journals.reverse"] = ["transactions.edit", "transactions.delete"],
        ["accounting.manual_journals.view"] = ["accounting.journals.view", "transactions.view"],
        ["accounting.manual_journals.create"] = ["accounting.journals.create", "transactions.create"],
        ["accounting.manual_journals.update"] = ["accounting.journals.update", "transactions.edit"],
        ["accounting.manual_journals.submit"] = ["accounting.journals.update", "transactions.edit"],
        ["accounting.manual_journals.approve"] = ["accounting.journals.post", "transactions.edit"],
        ["accounting.manual_journals.reject"] = ["accounting.journals.update", "transactions.edit"],
        ["accounting.manual_journals.cancel"] = ["accounting.journals.cancel", "transactions.delete"],
        ["accounting.manual_journals.post"] = ["accounting.journals.post", "transactions.create", "transactions.edit"],
        ["accounting.manual_journals.reverse"] = ["accounting.journals.reverse", "transactions.edit", "transactions.delete"],
        ["accounting.expenses.view"] = ["expenses.view"],
        ["accounting.expenses.create"] = ["expenses.create"],
        ["accounting.expenses.edit"] = ["expenses.edit"],
        ["accounting.expenses.submit"] = ["expenses.create", "expenses.edit"],
        ["accounting.expenses.approve"] = ["expenses.edit"],
        ["accounting.expenses.reject"] = ["expenses.edit"],
        ["accounting.expenses.cancel"] = ["expenses.delete"],
        ["accounting.supplier_payments.view"] = ["suppliers.view"],
        ["accounting.supplier_payments.create"] = ["suppliers.edit"],
        ["accounting.supplier_payments.submit"] = ["suppliers.edit"],
        ["accounting.supplier_payments.approve"] = ["suppliers.edit"],
        ["accounting.supplier_payments.reject"] = ["suppliers.edit"],
        ["accounting.supplier_payments.cancel"] = ["suppliers.edit"],
        ["accounting.supplier_advances.view"] = ["suppliers.view"],
        ["accounting.supplier_advances.create"] = ["suppliers.edit"],
        ["accounting.supplier_advances.submit"] = ["suppliers.edit"],
        ["accounting.supplier_advances.approve"] = ["suppliers.edit"],
        ["accounting.supplier_advances.reject"] = ["suppliers.edit"],
        ["accounting.supplier_advances.cancel"] = ["suppliers.edit"],
        ["accounting.opening_balances.post"] = ["transactions.create", "transactions.edit"],
        ["accounting.opening_balances.adjust"] = ["transactions.edit"],
        ["accounts.view"] = ["accounting.chart_accounts.view"],
        ["accounts.create"] = ["accounting.chart_accounts.create"],
        ["accounts.edit"] = ["accounting.chart_accounts.edit"],
        ["accounts.delete"] = ["accounting.chart_accounts.edit"],
        ["transactions.view"] = ["accounting.journals.view", "accounting.manual_journals.view"],
        ["transactions.create"] = ["accounting.journals.create", "accounting.manual_journals.create", "accounting.journals.post", "accounting.manual_journals.post", "accounting.opening_balances.post"],
        ["transactions.edit"] = ["accounting.journals.update", "accounting.journals.post", "accounting.journals.reverse", "accounting.manual_journals.update", "accounting.manual_journals.submit", "accounting.manual_journals.approve", "accounting.manual_journals.reject", "accounting.manual_journals.post", "accounting.manual_journals.reverse", "accounting.opening_balances.post", "accounting.opening_balances.adjust"],
        ["transactions.delete"] = ["accounting.journals.cancel", "accounting.journals.reverse", "accounting.manual_journals.cancel", "accounting.manual_journals.reverse"],
        ["expenses.view"] = ["accounting.expenses.view"],
        ["expenses.create"] = ["accounting.expenses.create", "accounting.expenses.submit"],
        ["expenses.edit"] = ["accounting.expenses.edit", "accounting.expenses.approve", "accounting.expenses.reject"],
        ["expenses.delete"] = ["accounting.expenses.cancel"]
    };

    public static IReadOnlyList<string> ExpandClaims(string permission)
    {
        var claims = new List<string> { permission };
        if (Aliases.TryGetValue(permission, out var aliases))
        {
            claims.AddRange(aliases);
        }

        return claims;
    }

    /// <summary>
    /// The Business-level wildcard. It satisfies any permission of the catalog for the caller's own
    /// Business only; it does not widen Business/Outlet scope, feature entitlements, platform
    /// (Super Admin role) operations or domain approval rules, which are enforced separately.
    /// </summary>
    public const string Wildcard = "*";

    /// <summary>
    /// The single authoritative permission check: true when <paramref name="grantedPermissions"/>
    /// contains the wildcard, the required permission or one of its accepted aliases (case-insensitive).
    /// </summary>
    public static bool IsGranted(IEnumerable<string> grantedPermissions, string requiredPermission)
    {
        ArgumentNullException.ThrowIfNull(grantedPermissions);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredPermission);

        var accepted = ExpandClaims(requiredPermission);
        foreach (var granted in grantedPermissions)
        {
            if (string.Equals(granted, Wildcard, StringComparison.Ordinal)
                || accepted.Contains(granted, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
