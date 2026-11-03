-- =============================================================
-- POS v2 Role Seeder
-- Run this against the live database to add the canonical v2 roles.
-- Safe to run multiple times (ON CONFLICT DO NOTHING).
-- =============================================================

INSERT INTO roles (name, permissions, created_at, updated_at)
VALUES
    -- -------------------------------------------------------
    -- BusinessOwner
    -- Full control over own business: all modules
    -- -------------------------------------------------------
    ('BusinessOwner', '[
        "users.view", "users.create", "users.edit", "users.delete",
        "roles.view",
        "products.view", "products.create", "products.edit", "products.delete",
        "products.view_cost",
        "variations.view", "variations.create", "variations.edit", "variations.delete",
        "barcode.view", "barcode.print", "barcode.bulk_print", "barcode.template_manage",
        "categories.view", "categories.create", "categories.edit", "categories.delete",
        "inventory.view", "inventory.create", "inventory.edit", "inventory.delete",
        "stock_adjustments.view", "stock_adjustments.create", "stock_adjustments.edit", "stock_adjustments.delete", "stock_adjustments.approve", "stock_adjustments.cancel",
        "StockCount.ViewOwn", "StockCount.ViewAll", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Approve", "StockCount.Reject", "StockCount.Reopen",
        "stock_transfers.view", "stock_transfers.view_all_locations", "stock_transfers.create", "stock_transfers.edit", "stock_transfers.delete", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "low_stock_alerts.view", "low_stock_alerts.create", "low_stock_alerts.edit", "low_stock_alerts.delete",
        "sales.view", "sales.create",
        "purchases.view", "purchases.create", "purchases.edit", "purchases.delete",
        "customers.view", "customers.create", "customers.edit", "customers.delete",
        "suppliers.view", "suppliers.create", "suppliers.edit", "suppliers.delete",
        "outlets.view", "outlets.create", "outlets.edit", "outlets.delete",
        "warehouses.view", "warehouses.create", "warehouses.edit", "warehouses.delete",
        "accounting.dashboard.view",
        "accounting.chart_accounts.view", "accounting.chart_accounts.create", "accounting.chart_accounts.edit",
        "accounting.configuration.view", "accounting.configuration.edit",
        "accounting.journals.view", "accounting.journals.create", "accounting.journals.update", "accounting.journals.cancel", "accounting.journals.post", "accounting.journals.reverse", "accounting.settlements.view", "accounting.settlements.create", "accounting.settlements.post", "accounting.settlements.reverse", "accounting.liquidity_transfers.view", "accounting.liquidity_transfers.create", "accounting.liquidity_transfers.post", "accounting.liquidity_transfers.reverse", "accounting.statement_import.view", "accounting.statement_import.create", "accounting.reconciliation.view", "accounting.reconciliation.create", "accounting.reconciliation.match", "accounting.reconciliation.unmatch", "accounting.reconciliation.close", "accounting.reconciliation.reopen", "accounting.adjustments.propose", "accounting.adjustments.approve", "accounting.adjustments.reverse", "accounting.exceptions.manage",
        "accounting.manual_journals.view", "accounting.manual_journals.create", "accounting.manual_journals.update", "accounting.manual_journals.submit", "accounting.manual_journals.approve", "accounting.manual_journals.reject", "accounting.manual_journals.cancel", "accounting.manual_journals.post", "accounting.manual_journals.reverse",
        "accounting.expenses.view", "accounting.expenses.create", "accounting.expenses.edit", "accounting.expenses.submit", "accounting.expenses.approve", "accounting.expenses.reject", "accounting.expenses.cancel",
        "accounting.supplier_payments.view", "accounting.supplier_payments.create", "accounting.supplier_payments.submit", "accounting.supplier_payments.approve", "accounting.supplier_payments.reject", "accounting.supplier_payments.cancel",
        "accounting.supplier_advances.view", "accounting.supplier_advances.create", "accounting.supplier_advances.submit", "accounting.supplier_advances.approve", "accounting.supplier_advances.reject", "accounting.supplier_advances.cancel",
        "accounting.other_income.view", "accounting.other_income.create", "accounting.other_income.edit", "accounting.other_income.submit", "accounting.other_income.approve", "accounting.other_income.reject", "accounting.other_income.cancel",
        "accounting.financial_years.view", "accounting.financial_years.manage", "accounting.financial_years.reopen",
        "accounting.periods.view", "accounting.periods.close", "accounting.periods.reopen", "accounting.close.prepare", "accounting.close.approve", "accounting.close.execute", "accounting.close.override_exception", "accounting.close.audit.view",
        "accounting.opening_balances.post",
        "accounting.opening_balances.adjust",
        "accounting.reports.general_ledger", "accounting.reports.trial_balance", "accounting.reports.profit_loss", "accounting.reports.cash_flow", "accounting.reports.profitability", "accounting.reports.ap_subledger", "accounting.reports.ap_aging", "accounting.reports.supplier_statement", "accounting.reports.ap_reconciliation", "accounting.reports.vat",
        "reports.sales", "reports.inventory", "reports.financial", "reports.export",
        "settings.view", "settings.edit"
    ]'::jsonb, NOW(), NOW()),

    -- -------------------------------------------------------
    -- BusinessAdmin
    -- Similar to BusinessOwner but cannot delete users or roles
    -- -------------------------------------------------------
    ('BusinessAdmin', '[
        "users.view", "users.create", "users.edit",
        "roles.view",
        "products.view", "products.create", "products.edit", "products.delete",
        "products.view_cost",
        "variations.view", "variations.create", "variations.edit", "variations.delete",
        "barcode.view", "barcode.print", "barcode.bulk_print", "barcode.template_manage",
        "categories.view", "categories.create", "categories.edit", "categories.delete",
        "inventory.view", "inventory.create", "inventory.edit", "inventory.delete",
        "stock_adjustments.view", "stock_adjustments.create", "stock_adjustments.edit", "stock_adjustments.delete", "stock_adjustments.approve", "stock_adjustments.cancel",
        "StockCount.ViewOwn", "StockCount.ViewAll", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Approve", "StockCount.Reject", "StockCount.Reopen",
        "stock_transfers.view", "stock_transfers.create", "stock_transfers.edit", "stock_transfers.delete", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "low_stock_alerts.view", "low_stock_alerts.create", "low_stock_alerts.edit", "low_stock_alerts.delete",
        "sales.view", "sales.create",
        "purchases.view", "purchases.create", "purchases.edit", "purchases.delete",
        "customers.view", "customers.create", "customers.edit", "customers.delete",
        "suppliers.view", "suppliers.create", "suppliers.edit", "suppliers.delete",
        "outlets.view", "outlets.create", "outlets.edit",
        "warehouses.view", "warehouses.create", "warehouses.edit",
        "accounting.dashboard.view",
        "accounting.chart_accounts.view", "accounting.chart_accounts.create", "accounting.chart_accounts.edit",
        "accounting.configuration.view", "accounting.configuration.edit",
        "accounting.journals.view", "accounting.journals.create", "accounting.journals.update", "accounting.journals.cancel", "accounting.journals.post", "accounting.journals.reverse",
        "accounting.manual_journals.view", "accounting.manual_journals.create", "accounting.manual_journals.update", "accounting.manual_journals.submit", "accounting.manual_journals.approve", "accounting.manual_journals.reject", "accounting.manual_journals.cancel", "accounting.manual_journals.post", "accounting.manual_journals.reverse",
        "accounting.expenses.view", "accounting.expenses.create", "accounting.expenses.edit", "accounting.expenses.submit", "accounting.expenses.approve", "accounting.expenses.reject", "accounting.expenses.cancel",
        "accounting.supplier_payments.view", "accounting.supplier_payments.create", "accounting.supplier_payments.submit", "accounting.supplier_payments.approve", "accounting.supplier_payments.reject", "accounting.supplier_payments.cancel",
        "accounting.supplier_advances.view", "accounting.supplier_advances.create", "accounting.supplier_advances.submit", "accounting.supplier_advances.approve", "accounting.supplier_advances.reject", "accounting.supplier_advances.cancel",
        "accounting.other_income.view", "accounting.other_income.create", "accounting.other_income.edit", "accounting.other_income.submit", "accounting.other_income.approve", "accounting.other_income.reject", "accounting.other_income.cancel",
        "accounting.financial_years.view", "accounting.financial_years.manage", "accounting.financial_years.reopen",
        "accounting.periods.view", "accounting.periods.close", "accounting.periods.reopen", "accounting.close.prepare", "accounting.close.approve", "accounting.close.execute", "accounting.close.override_exception", "accounting.close.audit.view",
        "accounting.opening_balances.post",
        "accounting.opening_balances.adjust",
        "accounting.reports.general_ledger", "accounting.reports.trial_balance", "accounting.reports.profit_loss", "accounting.reports.ap_subledger", "accounting.reports.ap_aging", "accounting.reports.supplier_statement", "accounting.reports.ap_reconciliation",
        "reports.sales", "reports.inventory", "reports.financial", "reports.export",
        "settings.view"
    ]'::jsonb, NOW(), NOW()),

    -- -------------------------------------------------------
    -- AccountsAdmin
    -- Business-level financial data access only
    -- -------------------------------------------------------
    ('AccountsAdmin', '[
        "accounting.dashboard.view",
        "accounting.chart_accounts.view", "accounting.chart_accounts.create", "accounting.chart_accounts.edit",
        "accounting.configuration.view", "accounting.configuration.edit",
        "accounting.journals.view", "accounting.journals.create", "accounting.journals.update", "accounting.journals.cancel", "accounting.journals.post", "accounting.journals.reverse",
        "accounting.manual_journals.view", "accounting.manual_journals.create", "accounting.manual_journals.update", "accounting.manual_journals.submit", "accounting.manual_journals.approve", "accounting.manual_journals.reject", "accounting.manual_journals.cancel", "accounting.manual_journals.post", "accounting.manual_journals.reverse",
        "accounting.expenses.view", "accounting.expenses.create", "accounting.expenses.edit", "accounting.expenses.submit", "accounting.expenses.approve", "accounting.expenses.reject", "accounting.expenses.cancel",
        "accounting.other_income.view", "accounting.other_income.create", "accounting.other_income.edit", "accounting.other_income.submit", "accounting.other_income.approve", "accounting.other_income.reject", "accounting.other_income.cancel",
        "accounting.financial_years.view", "accounting.financial_years.manage", "accounting.financial_years.reopen",
        "accounting.periods.view", "accounting.periods.close", "accounting.close.prepare", "accounting.close.approve", "accounting.close.execute", "accounting.close.override_exception", "accounting.close.audit.view", "accounting.periods.reopen",
        "accounting.opening_balances.post",
        "accounting.opening_balances.adjust",
        "accounting.reports.general_ledger", "accounting.reports.trial_balance", "accounting.reports.profit_loss", "accounting.reports.ap_subledger", "accounting.reports.ap_aging", "accounting.reports.supplier_statement", "accounting.reports.ap_reconciliation",
        "reports.export"
    ]'::jsonb, NOW(), NOW()),

    -- -------------------------------------------------------
    -- Accounting Manager
    -- Accounting maker/reviewer role; approval/reversal/reopen duties remain segregated
    -- -------------------------------------------------------
    ('Accounting Manager', '["suppliers.view", "accounting.dashboard.view", "accounting.chart_accounts.view", "accounting.chart_accounts.create", "accounting.chart_accounts.edit", "accounting.configuration.view", "accounting.journals.view", "accounting.manual_journals.view", "accounting.manual_journals.create", "accounting.manual_journals.update", "accounting.manual_journals.submit", "accounting.manual_journals.cancel", "accounting.expenses.view", "accounting.expenses.create", "accounting.expenses.edit", "accounting.expenses.submit", "accounting.expenses.cancel", "accounting.supplier_payments.view", "accounting.supplier_payments.create", "accounting.supplier_payments.submit", "accounting.supplier_payments.cancel", "accounting.supplier_advances.view", "accounting.supplier_advances.create", "accounting.supplier_advances.submit", "accounting.supplier_advances.cancel", "accounting.other_income.view", "accounting.other_income.create", "accounting.other_income.edit", "accounting.other_income.submit", "accounting.other_income.cancel", "accounting.settlements.view", "accounting.settlements.create", "accounting.liquidity_transfers.view", "accounting.liquidity_transfers.create", "accounting.statement_import.view", "accounting.statement_import.create", "accounting.reconciliation.view", "accounting.reconciliation.create", "accounting.reconciliation.match", "accounting.reconciliation.unmatch", "accounting.adjustments.propose", "accounting.exceptions.manage", "accounting.financial_years.view", "accounting.periods.view", "accounting.close.prepare", "accounting.close.audit.view", "accounting.reports.general_ledger", "accounting.reports.trial_balance", "accounting.reports.profit_loss", "accounting.reports.balance_sheet", "accounting.reports.cash_book", "accounting.reports.cash_flow", "accounting.reports.profitability", "accounting.reports.cogs", "accounting.reports.inventory_valuation", "accounting.reports.ap_subledger", "accounting.reports.ap_aging", "accounting.reports.supplier_statement", "accounting.reports.ap_reconciliation", "accounting.reports.vat", "reports.export"]'::jsonb, NOW(), NOW()),

    -- -------------------------------------------------------
    -- OutletManager
    -- Assigned to one outlet; manages that outlet operations
    -- -------------------------------------------------------
    ('OutletManager', '[
        "products.view", "products.create", "products.edit",
        "variations.view",
        "barcode.view", "barcode.print", "barcode.bulk_print",
        "categories.view",
        "inventory.view", "inventory.create", "inventory.edit",
        "stock_adjustments.view", "stock_adjustments.create",
        "StockCount.ViewOwn", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Reject", "StockCount.Reopen",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "stock_transfers.view", "stock_transfers.create", "stock_transfers.approve", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create",
        "low_stock_alerts.view",
        "sales.view", "sales.create",
        "customers.view", "customers.create", "customers.edit",
        "suppliers.view",
        "outlets.view",
        "reports.sales", "reports.inventory"
    ]'::jsonb, NOW(), NOW()),

    -- -------------------------------------------------------
    -- SalesPerson
    -- Assigned to one outlet; sales operations only
    -- -------------------------------------------------------
    ('SalesPerson', '[
        "products.view",
        "variations.view",
        "barcode.view", "barcode.print",
        "inventory.view",
        "stock_transfers.view",
        "low_stock_alerts.view",
        "sales.view", "sales.create",
        "customers.view", "customers.create"
    ]'::jsonb, NOW(), NOW()),

    -- -------------------------------------------------------
    -- WarehouseManager
    -- Assigned to one or more warehouses; warehouse operations
    -- -------------------------------------------------------
    ('WarehouseManager', '[
        "products.view", "products.view_cost",
        "variations.view",
        "barcode.view", "barcode.print", "barcode.bulk_print",
        "categories.view",
        "inventory.view", "inventory.create", "inventory.edit", "inventory.delete",
        "stock_adjustments.view", "stock_adjustments.create", "stock_adjustments.edit", "stock_adjustments.delete", "stock_adjustments.approve", "stock_adjustments.cancel",
        "StockCount.ViewOwn", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Reject", "StockCount.Reopen",
        "stock_transfers.view", "stock_transfers.view_all_locations", "stock_transfers.create", "stock_transfers.edit", "stock_transfers.delete", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "low_stock_alerts.view", "low_stock_alerts.edit",
        "purchases.view", "purchases.create", "purchases.edit", "purchases.receive",
        "suppliers.view", "suppliers.create", "suppliers.edit",
        "warehouses.view",
        "reports.inventory"
    ]'::jsonb, NOW(), NOW())

ON CONFLICT (name) DO NOTHING;

-- Verify
SELECT id, name FROM roles ORDER BY id;
