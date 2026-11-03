-- =============================================================
-- Add Phase 1 accounting permissions to v2 roles
-- Safe to run multiple times (deduplicates permission arrays)
-- =============================================================

INSERT INTO roles (name, permissions, created_at, updated_at)
VALUES ('Accounting Manager', '["suppliers.view", "accounting.dashboard.view", "accounting.chart_accounts.view", "accounting.chart_accounts.create", "accounting.chart_accounts.edit", "accounting.configuration.view", "accounting.journals.view", "accounting.manual_journals.view", "accounting.manual_journals.create", "accounting.manual_journals.update", "accounting.manual_journals.submit", "accounting.manual_journals.cancel", "accounting.expenses.view", "accounting.expenses.create", "accounting.expenses.edit", "accounting.expenses.submit", "accounting.expenses.cancel", "accounting.supplier_payments.view", "accounting.supplier_payments.create", "accounting.supplier_payments.submit", "accounting.supplier_payments.cancel", "accounting.supplier_advances.view", "accounting.supplier_advances.create", "accounting.supplier_advances.submit", "accounting.supplier_advances.cancel", "accounting.other_income.view", "accounting.other_income.create", "accounting.other_income.edit", "accounting.other_income.submit", "accounting.other_income.cancel", "accounting.settlements.view", "accounting.settlements.create", "accounting.liquidity_transfers.view", "accounting.liquidity_transfers.create", "accounting.statement_import.view", "accounting.statement_import.create", "accounting.reconciliation.view", "accounting.reconciliation.create", "accounting.reconciliation.match", "accounting.reconciliation.unmatch", "accounting.adjustments.propose", "accounting.exceptions.manage", "accounting.financial_years.view", "accounting.periods.view", "accounting.close.prepare", "accounting.close.audit.view", "accounting.reports.general_ledger", "accounting.reports.trial_balance", "accounting.reports.profit_loss", "accounting.reports.balance_sheet", "accounting.reports.cash_book", "accounting.reports.cash_flow", "accounting.reports.profitability", "accounting.reports.cogs", "accounting.reports.inventory_valuation", "accounting.reports.ap_subledger", "accounting.reports.ap_aging", "accounting.reports.supplier_statement", "accounting.reports.ap_reconciliation", "accounting.reports.vat", "reports.export"]'::jsonb, NOW(), NOW())
ON CONFLICT (name) DO NOTHING;

WITH additions AS (
    SELECT *
    FROM (VALUES
        ('BusinessOwner', 'accounting.dashboard.view'),
        ('BusinessOwner', 'accounting.chart_accounts.view'),
        ('BusinessOwner', 'accounting.chart_accounts.create'),
        ('BusinessOwner', 'accounting.chart_accounts.edit'),
        ('BusinessOwner', 'accounting.configuration.view'),
        ('BusinessOwner', 'accounting.configuration.edit'),
        ('BusinessOwner', 'accounting.journals.view'),
        ('BusinessOwner', 'accounting.journals.create'),
        ('BusinessOwner', 'accounting.journals.update'),
        ('BusinessOwner', 'accounting.journals.cancel'),
        ('BusinessOwner', 'accounting.journals.post'),
        ('BusinessOwner', 'accounting.journals.reverse'),
        ('BusinessOwner', 'accounting.settlements.view'), ('BusinessOwner', 'accounting.settlements.create'), ('BusinessOwner', 'accounting.settlements.post'), ('BusinessOwner', 'accounting.settlements.reverse'),
        ('BusinessOwner', 'accounting.liquidity_transfers.view'), ('BusinessOwner', 'accounting.liquidity_transfers.create'), ('BusinessOwner', 'accounting.liquidity_transfers.post'), ('BusinessOwner', 'accounting.liquidity_transfers.reverse'),
        ('BusinessOwner', 'accounting.manual_journals.view'),
        ('BusinessOwner', 'accounting.manual_journals.create'),
        ('BusinessOwner', 'accounting.manual_journals.update'),
        ('BusinessOwner', 'accounting.manual_journals.submit'),
        ('BusinessOwner', 'accounting.manual_journals.approve'),
        ('BusinessOwner', 'accounting.manual_journals.reject'),
        ('BusinessOwner', 'accounting.manual_journals.cancel'),
        ('BusinessOwner', 'accounting.manual_journals.post'),
        ('BusinessOwner', 'accounting.manual_journals.reverse'),
        ('BusinessOwner', 'accounting.expenses.view'),
        ('BusinessOwner', 'accounting.expenses.create'),
        ('BusinessOwner', 'accounting.expenses.edit'),
        ('BusinessOwner', 'accounting.expenses.submit'),
        ('BusinessOwner', 'accounting.expenses.approve'),
        ('BusinessOwner', 'accounting.expenses.reject'),
        ('BusinessOwner', 'accounting.expenses.cancel'),
        ('BusinessOwner', 'accounting.financial_years.view'),
        ('BusinessOwner', 'accounting.financial_years.manage'),
        ('BusinessOwner', 'accounting.financial_years.reopen'),
        ('BusinessOwner', 'accounting.periods.view'),
        ('BusinessOwner', 'accounting.periods.close'),
        ('BusinessOwner', 'accounting.periods.reopen'),
        ('BusinessOwner', 'accounting.close.prepare'), ('BusinessOwner', 'accounting.close.approve'), ('BusinessOwner', 'accounting.close.execute'), ('BusinessOwner', 'accounting.close.override_exception'), ('BusinessOwner', 'accounting.close.audit.view'),
        ('BusinessOwner', 'accounting.opening_balances.post'),
        ('BusinessOwner', 'accounting.opening_balances.adjust'),
        ('BusinessOwner', 'accounting.other_income.view'),
        ('BusinessOwner', 'accounting.other_income.create'),
        ('BusinessOwner', 'accounting.other_income.edit'),
        ('BusinessOwner', 'accounting.other_income.submit'),
        ('BusinessOwner', 'accounting.other_income.approve'),
        ('BusinessOwner', 'accounting.other_income.reject'),
        ('BusinessOwner', 'accounting.other_income.cancel'),
        ('BusinessOwner', 'accounting.reports.general_ledger'),
        ('BusinessOwner', 'accounting.reports.trial_balance'),
        ('BusinessOwner', 'accounting.reports.profit_loss'),
        ('BusinessOwner', 'accounting.reports.balance_sheet'),
        ('BusinessOwner', 'accounting.reports.cash_book'),
        ('BusinessOwner', 'accounting.reports.cash_flow'),
        ('BusinessOwner', 'accounting.reports.profitability'),
        ('BusinessOwner', 'accounting.reports.expense'),
        ('BusinessOwner', 'accounting.reports.income'),
        ('BusinessOwner', 'accounting.reports.cogs'),
        ('BusinessOwner', 'accounting.reports.inventory_valuation'),
        ('BusinessOwner', 'accounting.reports.cash_closing'),
        ('BusinessOwner', 'accounting.reports.ap_subledger'),
        ('BusinessOwner', 'accounting.reports.ap_aging'),
        ('BusinessOwner', 'accounting.reports.supplier_statement'),
        ('BusinessOwner', 'accounting.reports.ap_reconciliation'),
        ('BusinessOwner', 'accounting.reports.vat'),

        ('BusinessAdmin', 'accounting.dashboard.view'),
        ('BusinessAdmin', 'accounting.chart_accounts.view'),
        ('BusinessAdmin', 'accounting.chart_accounts.create'),
        ('BusinessAdmin', 'accounting.chart_accounts.edit'),
        ('BusinessAdmin', 'accounting.configuration.view'),
        ('BusinessAdmin', 'accounting.configuration.edit'),
        ('BusinessAdmin', 'accounting.journals.view'),
        ('BusinessAdmin', 'accounting.journals.create'),
        ('BusinessAdmin', 'accounting.journals.update'),
        ('BusinessAdmin', 'accounting.journals.cancel'),
        ('BusinessAdmin', 'accounting.journals.post'),
        ('BusinessAdmin', 'accounting.journals.reverse'),
        ('BusinessAdmin', 'accounting.settlements.view'), ('BusinessAdmin', 'accounting.settlements.create'), ('BusinessAdmin', 'accounting.settlements.post'), ('BusinessAdmin', 'accounting.settlements.reverse'),
        ('BusinessAdmin', 'accounting.liquidity_transfers.view'), ('BusinessAdmin', 'accounting.liquidity_transfers.create'), ('BusinessAdmin', 'accounting.liquidity_transfers.post'), ('BusinessAdmin', 'accounting.liquidity_transfers.reverse'),
        ('BusinessAdmin', 'accounting.manual_journals.view'),
        ('BusinessAdmin', 'accounting.manual_journals.create'),
        ('BusinessAdmin', 'accounting.manual_journals.update'),
        ('BusinessAdmin', 'accounting.manual_journals.submit'),
        ('BusinessAdmin', 'accounting.manual_journals.approve'),
        ('BusinessAdmin', 'accounting.manual_journals.reject'),
        ('BusinessAdmin', 'accounting.manual_journals.cancel'),
        ('BusinessAdmin', 'accounting.manual_journals.post'),
        ('BusinessAdmin', 'accounting.manual_journals.reverse'),
        ('BusinessAdmin', 'accounting.expenses.view'),
        ('BusinessAdmin', 'accounting.expenses.create'),
        ('BusinessAdmin', 'accounting.expenses.edit'),
        ('BusinessAdmin', 'accounting.expenses.submit'),
        ('BusinessAdmin', 'accounting.expenses.approve'),
        ('BusinessAdmin', 'accounting.expenses.reject'),
        ('BusinessAdmin', 'accounting.expenses.cancel'),
        ('BusinessAdmin', 'accounting.financial_years.view'),
        ('BusinessAdmin', 'accounting.financial_years.manage'),
        ('BusinessAdmin', 'accounting.financial_years.reopen'),
        ('BusinessAdmin', 'accounting.periods.view'),
        ('BusinessAdmin', 'accounting.periods.close'),
        ('BusinessAdmin', 'accounting.periods.reopen'),
        ('BusinessAdmin', 'accounting.close.prepare'), ('BusinessAdmin', 'accounting.close.approve'), ('BusinessAdmin', 'accounting.close.execute'), ('BusinessAdmin', 'accounting.close.override_exception'), ('BusinessAdmin', 'accounting.close.audit.view'),
        ('BusinessAdmin', 'accounting.opening_balances.post'),
        ('BusinessAdmin', 'accounting.opening_balances.adjust'),
        ('BusinessAdmin', 'accounting.other_income.view'),
        ('BusinessAdmin', 'accounting.other_income.create'),
        ('BusinessAdmin', 'accounting.other_income.edit'),
        ('BusinessAdmin', 'accounting.other_income.submit'),
        ('BusinessAdmin', 'accounting.other_income.approve'),
        ('BusinessAdmin', 'accounting.other_income.reject'),
        ('BusinessAdmin', 'accounting.other_income.cancel'),
        ('BusinessAdmin', 'accounting.reports.general_ledger'),
        ('BusinessAdmin', 'accounting.reports.trial_balance'),
        ('BusinessAdmin', 'accounting.reports.profit_loss'),
        ('BusinessAdmin', 'accounting.reports.balance_sheet'),
        ('BusinessAdmin', 'accounting.reports.cash_book'),
        ('BusinessAdmin', 'accounting.reports.cash_flow'),
        ('BusinessAdmin', 'accounting.reports.profitability'),
        ('BusinessAdmin', 'accounting.reports.expense'),
        ('BusinessAdmin', 'accounting.reports.income'),
        ('BusinessAdmin', 'accounting.reports.cogs'),
        ('BusinessAdmin', 'accounting.reports.inventory_valuation'),
        ('BusinessAdmin', 'accounting.reports.cash_closing'),
        ('BusinessAdmin', 'accounting.reports.ap_subledger'),
        ('BusinessAdmin', 'accounting.reports.ap_aging'),
        ('BusinessAdmin', 'accounting.reports.supplier_statement'),
        ('BusinessAdmin', 'accounting.reports.ap_reconciliation'),
        ('BusinessAdmin', 'accounting.reports.vat'),

        ('AccountsAdmin', 'accounting.dashboard.view'),
        ('AccountsAdmin', 'accounting.chart_accounts.view'),
        ('AccountsAdmin', 'accounting.chart_accounts.create'),
        ('AccountsAdmin', 'accounting.chart_accounts.edit'),
        ('AccountsAdmin', 'accounting.configuration.view'),
        ('AccountsAdmin', 'accounting.configuration.edit'),
        ('AccountsAdmin', 'accounting.journals.view'),
        ('AccountsAdmin', 'accounting.journals.create'),
        ('AccountsAdmin', 'accounting.journals.update'),
        ('AccountsAdmin', 'accounting.journals.cancel'),
        ('AccountsAdmin', 'accounting.journals.post'),
        ('AccountsAdmin', 'accounting.journals.reverse'),
        ('AccountsAdmin', 'accounting.settlements.view'), ('AccountsAdmin', 'accounting.settlements.create'), ('AccountsAdmin', 'accounting.settlements.post'), ('AccountsAdmin', 'accounting.settlements.reverse'),
        ('AccountsAdmin', 'accounting.liquidity_transfers.view'), ('AccountsAdmin', 'accounting.liquidity_transfers.create'), ('AccountsAdmin', 'accounting.liquidity_transfers.post'), ('AccountsAdmin', 'accounting.liquidity_transfers.reverse'),
        ('AccountsAdmin', 'accounting.statement_import.view'), ('AccountsAdmin', 'accounting.statement_import.create'),
        ('AccountsAdmin', 'accounting.reconciliation.view'), ('AccountsAdmin', 'accounting.reconciliation.create'), ('AccountsAdmin', 'accounting.reconciliation.match'), ('AccountsAdmin', 'accounting.reconciliation.unmatch'),
        ('AccountsAdmin', 'accounting.manual_journals.view'),
        ('AccountsAdmin', 'accounting.manual_journals.create'),
        ('AccountsAdmin', 'accounting.manual_journals.update'),
        ('AccountsAdmin', 'accounting.manual_journals.submit'),
        ('AccountsAdmin', 'accounting.manual_journals.approve'),
        ('AccountsAdmin', 'accounting.manual_journals.reject'),
        ('AccountsAdmin', 'accounting.manual_journals.cancel'),
        ('AccountsAdmin', 'accounting.manual_journals.post'),
        ('AccountsAdmin', 'accounting.manual_journals.reverse'),
        ('AccountsAdmin', 'accounting.expenses.view'),
        ('AccountsAdmin', 'accounting.expenses.create'),
        ('AccountsAdmin', 'accounting.expenses.edit'),
        ('AccountsAdmin', 'accounting.expenses.submit'),
        ('AccountsAdmin', 'accounting.expenses.approve'),
        ('AccountsAdmin', 'accounting.expenses.reject'),
        ('AccountsAdmin', 'accounting.expenses.cancel'),
        ('AccountsAdmin', 'accounting.financial_years.view'),
        ('AccountsAdmin', 'accounting.financial_years.manage'),
        ('AccountsAdmin', 'accounting.financial_years.reopen'),
        ('AccountsAdmin', 'accounting.periods.view'),
        ('AccountsAdmin', 'accounting.periods.close'),
        ('AccountsAdmin', 'accounting.periods.reopen'),
        ('AccountsAdmin', 'accounting.close.prepare'), ('AccountsAdmin', 'accounting.close.approve'), ('AccountsAdmin', 'accounting.close.execute'), ('AccountsAdmin', 'accounting.close.override_exception'), ('AccountsAdmin', 'accounting.close.audit.view'),
        ('AccountsAdmin', 'accounting.opening_balances.post'),
        ('AccountsAdmin', 'accounting.opening_balances.adjust'),
        ('AccountsAdmin', 'accounting.other_income.view'),
        ('AccountsAdmin', 'accounting.other_income.create'),
        ('AccountsAdmin', 'accounting.other_income.edit'),
        ('AccountsAdmin', 'accounting.other_income.submit'),
        ('AccountsAdmin', 'accounting.other_income.approve'),
        ('AccountsAdmin', 'accounting.other_income.reject'),
        ('AccountsAdmin', 'accounting.other_income.cancel'),
        ('AccountsAdmin', 'accounting.reports.general_ledger'),
        ('AccountsAdmin', 'accounting.reports.trial_balance'),
        ('AccountsAdmin', 'accounting.reports.profit_loss'),
        ('AccountsAdmin', 'accounting.reports.balance_sheet'),
        ('AccountsAdmin', 'accounting.reports.cash_book'),
        ('AccountsAdmin', 'accounting.reports.cash_flow'),
        ('AccountsAdmin', 'accounting.reports.profitability'),
        ('AccountsAdmin', 'accounting.reports.expense'),
        ('AccountsAdmin', 'accounting.reports.income'),
        ('AccountsAdmin', 'accounting.reports.cogs'),
        ('AccountsAdmin', 'accounting.reports.inventory_valuation'),
        ('AccountsAdmin', 'accounting.reports.cash_closing'),
        ('AccountsAdmin', 'accounting.reports.ap_subledger'),
        ('AccountsAdmin', 'accounting.reports.ap_aging'),
        ('AccountsAdmin', 'accounting.reports.supplier_statement'),
        ('AccountsAdmin', 'accounting.reports.ap_reconciliation'),
        ('AccountsAdmin', 'accounting.reports.vat')
    ) AS v(role_name, permission)
),
existing_permissions AS (
    SELECT r.id, r.name AS role_name, p.permission
    FROM roles r
    JOIN additions a ON a.role_name = r.name
    CROSS JOIN LATERAL jsonb_array_elements_text(COALESCE(r.permissions, '[]'::jsonb)) p(permission)
),
combined_permissions AS (
    SELECT r.id, a.role_name, a.permission
    FROM roles r
    JOIN additions a ON a.role_name = r.name

    UNION

    SELECT ep.id, ep.role_name, ep.permission
    FROM existing_permissions ep
),
deduplicated AS (
    SELECT
        id,
        jsonb_agg(permission ORDER BY permission) AS permissions
    FROM (
        SELECT DISTINCT id, permission
        FROM combined_permissions
    ) x
    GROUP BY id
)
UPDATE roles r
SET permissions = d.permissions,
    updated_at = NOW()
FROM deduplicated d
WHERE r.id = d.id;

-- Verification
SELECT name, permissions
FROM roles
WHERE name IN ('BusinessOwner', 'BusinessAdmin', 'AccountsAdmin', 'Accounting Manager')
ORDER BY name;
