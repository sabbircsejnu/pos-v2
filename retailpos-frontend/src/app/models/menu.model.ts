import { HrPayrollCapability } from '../services/hr-payroll-capability.store';

export interface MenuItem {
  id: string;
  label: string;
  icon: string;
  route?: string;
  permission?: string | string[];
  permissionMode?: 'all' | 'any';
  role?: string | string[];
  disabled?: boolean;
  capability?: HrPayrollCapability;
  children?: MenuItem[];
}

export interface MenuSection {
  id: string;
  label: string;
  items: MenuItem[];
}

// Section-first navigation model designed to scale for 100+ screens.
export const NAV_SECTIONS: MenuSection[] = [
  {
    id: 'owner-dashboards',
    label: 'DASHBOARD',
    items: [
      { id: 'dashboard-sales-finance', label: 'Sales & Finance', icon: 'fas fa-chart-line',
        route: '/dashboard/sales-finance', permission: 'dashboard.sales_finance.view' },
      { id: 'dashboard-inventory-intelligence', label: 'Inventory Intelligence', icon: 'fas fa-boxes-stacked',
        route: '/dashboard/inventory-intelligence', permission: 'dashboard.inventory_intelligence.view' }
    ]
  },
  {
    id: 'hr-payroll',
    label: 'HR AND PAYROLL',
    items: [
      { id: 'hr-dashboard', label: 'HR Dashboard', icon: 'fas fa-chart-pie',
        route: '/hr/dashboard', permission: 'hr.dashboard.view', capability: 'hr.enabled' },
      {
        id: 'hr-group-employee-management', label: 'Employee Management', icon: 'fas fa-address-book',
        children: [
          { id: 'hr-employees', label: 'Employees', icon: 'fas fa-users',
            route: '/hr/employees', permission: 'hr.employee.view', capability: 'hr.enabled' },
          { id: 'hr-organisation', label: 'Organisation', icon: 'fas fa-sitemap',
            route: '/hr/org-units', permission: 'hr.organisation.view', capability: 'hr.enabled' },
          { id: 'hr-positions', label: 'Positions', icon: 'fas fa-user-tie',
            route: '/hr/positions', permission: 'hr.position.view', capability: 'hr.enabled' },
          { id: 'hr-locations', label: 'Operational Locations', icon: 'fas fa-location-dot',
            route: '/hr/operational-locations', permission: 'hr.location.view', capability: 'hr.enabled' }
        ]
      },
      {
        id: 'hr-group-attendance', label: 'Attendance', icon: 'fas fa-clock',
        children: [
          { id: 'hr-shifts', label: 'Shifts', icon: 'fas fa-clock',
            route: '/hr/shifts', permission: 'hr.shift.view', capability: 'attendance.enabled' },
          { id: 'hr-holidays', label: 'Holidays', icon: 'fas fa-calendar',
            route: '/hr/holidays', permission: 'hr.holiday.view', capability: 'attendance.enabled' },
          { id: 'hr-weekly-off', label: 'Weekly Off', icon: 'fas fa-calendar-week',
            route: '/hr/weekly-off', permission: 'hr.weekly_off.view', capability: 'attendance.enabled' },
          { id: 'hr-rosters', label: 'Rosters', icon: 'fas fa-calendar-alt',
            route: '/hr/rosters', permission: 'hr.roster.view', capability: 'attendance.enabled' },
          { id: 'hr-devices', label: 'Attendance Devices', icon: 'fas fa-fingerprint',
            route: '/hr/attendance/devices', permission: 'hr.device.view', capability: 'deviceintegration.enabled' }
        ]
      },
      {
        id: 'hr-group-leave-overtime', label: 'Leave & Overtime', icon: 'fas fa-calendar-check',
        children: [
          { id: 'hr-leave', label: 'Leave Requests & Policies', icon: 'fas fa-calendar-check',
            route: '/hr/leave', permission: [
              'hr.leave.view', 'hr.leave.request', 'hr.leave.approve', 'hr.leave.admin',
              'hr.leave_balance.view', 'hr.leave_year.close', 'hr.leave_year.reopen'
            ],
            capability: 'hr.enabled' },
          { id: 'hr-overtime', label: 'Overtime', icon: 'fas fa-hourglass-half',
            route: '/hr/overtime', permission: 'hr.overtime.view', capability: 'attendance.enabled' }
        ]
      },
      {
        id: 'hr-group-employee-exit', label: 'Employee Exit', icon: 'fas fa-user-slash',
        children: [
          { id: 'hr-exits', label: 'Exit & Clearance', icon: 'fas fa-user-slash',
            route: '/hr/exits', permission: 'hr.exit.view', capability: 'hr.enabled' }
        ]
      },
      {
        id: 'payroll-group', label: 'Payroll', icon: 'fas fa-money-bill-wave',
        children: [
          { id: 'payroll-dashboard', label: 'Payroll Dashboard', icon: 'fas fa-money-bill',
            route: '/payroll/dashboard', permission: ['payroll.dashboard.view', 'payroll.salary.view'],
            permissionMode: 'all', capability: 'payroll.enabled' },
          { id: 'payroll-salary-setup', label: 'Salary Setup', icon: 'fas fa-money-bill-wave',
            route: '/payroll/salary-setup', permission: 'payroll.salary.view', capability: 'payroll.enabled' },
          { id: 'payroll-employee-loans', label: 'Loans & Advances', icon: 'fas fa-hand-holding-usd',
            route: '/payroll/loans', permission: 'payroll.loan.view', capability: 'payroll.enabled' },
          { id: 'payroll-validation', label: 'Payroll Validation', icon: 'fas fa-clipboard-check',
            route: '/payroll/validation', permission: 'payroll.salary.view', capability: 'payroll.enabled' },
          { id: 'payroll-periods', label: 'Payroll Periods', icon: 'fas fa-calendar-check',
            route: '/payroll/periods', permission: 'payroll.period.view', capability: 'payroll.enabled' },
          { id: 'payroll-payments', label: 'Payments & Bank Files', icon: 'fas fa-credit-card',
            route: '/payroll/payments', permission: ['payroll.payment.view', 'payroll.payment.process'],
            capability: 'payroll.enabled' }
        ]
      },
      {
        id: 'hr-group-imports', label: 'Imports', icon: 'fas fa-file-import',
        children: [
          { id: 'hr-opening-imports', label: 'Opening Employee Import', icon: 'fas fa-file-import',
            route: '/hr/opening-imports', permission: 'hr.import.view', capability: 'hr.enabled' },
          { id: 'payroll-opening-imports', label: 'Opening Payroll Import', icon: 'fas fa-file-import',
            route: '/payroll/opening-imports', permission: 'payroll.import.view', capability: 'payroll.enabled' }
        ]
      },
      {
        id: 'hr-group-reports', label: 'Reports', icon: 'fas fa-chart-line',
        children: [
          { id: 'hr-core-reports', label: 'Core HR Reports', icon: 'fas fa-chart-pie',
            route: '/hr/reports', permission: 'hr.report.view', capability: 'hr.enabled' },
          { id: 'hr-attendance-reports', label: 'Attendance Reports', icon: 'fas fa-chart-line',
            route: '/hr/attendance/reports', permission: 'hr.attendance.report', capability: 'attendance.enabled' },
          { id: 'payroll-reports', label: 'Payroll Reports', icon: 'fas fa-file-invoice-dollar',
            route: '/payroll/reports', permission: ['payroll.report.view', 'payroll.salary.view'],
            permissionMode: 'all', capability: 'payroll.enabled' }
        ]
      }
    ]
  },
  {
    id: 'catalog',
    label: 'CATALOG',
    items: [
      { id: 'products', label: 'Products', icon: 'fas fa-box', route: '/products', permission: 'products.view' },
      {
        id: 'barcode-labels',
        label: 'Barcode Labels',
        icon: 'fas fa-barcode',
        route: '/barcode-labels',
        permission: ['barcode.view', 'barcode.print', 'barcode.bulk_print', 'barcode.template_manage']
      },
      { id: 'categories', label: 'Categories', icon: 'fas fa-tags', route: '/categories', permission: 'categories.view' },
      { id: 'brands', label: 'Brands', icon: 'fas fa-award', permission: 'products.view', disabled: true },
      { id: 'variations', label: 'Variations', icon: 'fas fa-sliders-h', route: '/variations', permission: 'variations.view' },
    ]
  },
  {
    id: 'inventory',
    label: 'INVENTORY',
    items: [
      { id: 'inventory', label: 'Inventory', icon: 'fas fa-boxes', route: '/inventory', permission: 'inventory.view' },
      { id: 'stock-counts', label: 'Stock Counts', icon: 'fas fa-clipboard-check', route: '/stock-counts', permission: ['StockCount.ViewOwn', 'StockCount.ViewAll'] },
      { id: 'stock-adjustment', label: 'Stock Adjustment', icon: 'fas fa-sliders-h', route: '/stock-adjustments', permission: 'stock_adjustments.view' },
      { id: 'stock-transfer', label: 'Stock Transfer', icon: 'fas fa-truck-moving', route: '/stock-transfers', permission: 'stock_transfers.view' },
      { id: 'low-stock', label: 'Low Stock Alerts', icon: 'fas fa-triangle-exclamation', route: '/inventory/low-stock', permission: 'low_stock_alerts.view' },
    ]
  },
  {
    id: 'procurement',
    label: 'PROCUREMENT',
    items: [
      { id: 'purchase-orders', label: 'Purchase Orders', icon: 'fas fa-file-invoice', route: '/purchase-orders', permission: 'purchases.view' },
      { id: 'grn', label: 'Goods Received Notes (GRN)', icon: 'fas fa-box-open', route: '/grn', permission: 'grn.view' },
      { id: 'suppliers', label: 'Suppliers', icon: 'fas fa-truck', route: '/suppliers', permission: 'suppliers.view' },
    ]
  },
  {
    id: 'sales',
    label: 'SALES',
    items: [
      { id: 'pos-sales', label: 'POS Sales', icon: 'fas fa-cash-register', route: '/pos', permission: 'sales.create' },
      { id: 'cash-sessions', label: 'Cash Sessions', icon: 'fas fa-wallet', route: '/cash-sessions', permission: 'cash_sessions.view' },
      { id: 'sales', label: 'Sales', icon: 'fas fa-receipt', route: '/sales', permission: 'sales.view' },
      { id: 'customers', label: 'Customers', icon: 'fas fa-user-friends', route: '/customers', permission: 'customers.view' },
    ]
  },
  {
    id: 'finance',
    label: 'FINANCE',
    items: [
      { id: 'chart-of-accounts', label: 'Chart of Accounts', icon: 'fas fa-sitemap', route: '/accounting/chart-of-accounts', permission: 'accounting.chart_accounts.view' },
      { id: 'accounting-config', label: 'Accounting Configuration', icon: 'fas fa-sliders', route: '/settings/accounting', permission: 'accounting.configuration.view' },
      { id: 'financial-years', label: 'Financial Years / Periods', icon: 'fas fa-calendar-alt', route: '/financial-years', permission: ['accounting.financial_years.view', 'accounting.periods.view'] },
      { id: 'opening-balance', label: 'Opening Balance', icon: 'fas fa-balance-scale', route: '/opening-balance', permission: ['accounting.opening_balances.post', 'accounting.opening_balances.adjust'] },
      { id: 'expenses', label: 'Expenses', icon: 'fas fa-receipt', route: '/expenses', permission: 'accounting.expenses.view' },
      { id: 'expense-categories', label: 'Expense Categories', icon: 'fas fa-tags', route: '/expense-categories', permission: ['accounting.expenses.view', 'accounting.expense_categories.manage'] },
      { id: 'supplier-payments', label: 'Supplier Payments', icon: 'fas fa-file-invoice-dollar', route: '/supplier-payments', permission: ['accounting.supplier_payments.view', 'accounting.supplier_advances.view'] },
      { id: 'other-income', label: 'Other Income', icon: 'fas fa-hand-holding-dollar', route: '/incomes', permission: 'accounting.other_income.view' },
      { id: 'financial-postings', label: 'Sale GL Postings', icon: 'fas fa-arrows-rotate', route: '/financial-postings', permission: 'sales.financial_postings.view' },
      { id: 'manual-journals', label: 'Manual Journals', icon: 'fas fa-pen-to-square', route: '/manual-journals', permission: 'accounting.manual_journals.view' },
      { id: 'bank-reconciliation', label: 'Bank Reconciliation', icon: 'fas fa-building-columns', route: '/accounting/reconciliation', permission: 'accounting.reconciliation.view' },
      { id: 'accounting-close-workbench', label: 'Close Workbench', icon: 'fas fa-lock', route: '/accounting/close-workbench', permission: 'accounting.close.audit.view' },
    ]
  },
  {
    id: 'reporting',
    label: 'REPORTING',
    items: [
      { id: 'reports', label: 'Sales Reports', icon: 'fas fa-chart-line', route: '/reports/sales', permission: 'reports.sales' },
      { id: 'inventory-reports', label: 'Inventory Reports', icon: 'fas fa-boxes-stacked', route: '/reports/inventory', permission: 'reports.inventory' },
      { id: 'general-ledger', label: 'General Ledger', icon: 'fas fa-file-invoice-dollar', route: '/reports/general-ledger', permission: 'accounting.reports.general_ledger' },
      { id: 'trial-balance', label: 'Trial Balance', icon: 'fas fa-scale-balanced', route: '/reports/trial-balance', permission: 'accounting.reports.trial_balance' },
      { id: 'profit-loss', label: 'Profit & Loss', icon: 'fas fa-chart-pie', route: '/reports/profit-loss', permission: 'accounting.reports.profit_loss' },
      { id: 'profitability-report', label: 'Profitability', icon: 'fas fa-chart-simple', route: '/reports/profitability', permission: 'accounting.reports.profitability' },
      { id: 'cogs-report', label: 'Cost of Goods Sold', icon: 'fas fa-receipt', route: '/reports/cogs', permission: 'accounting.reports.cogs' },
      { id: 'inventory-valuation-report', label: 'Inventory Valuation', icon: 'fas fa-boxes-stacked', route: '/reports/inventory-valuation', permission: 'accounting.reports.inventory_valuation' },
      { id: 'cash-book-report', label: 'Cash Book', icon: 'fas fa-money-bill-transfer', route: '/reports/cash-book', permission: 'accounting.reports.cash_book' },
      { id: 'cash-flow-report', label: 'Cash Flow', icon: 'fas fa-water', route: '/reports/cash-flow', permission: 'accounting.reports.cash_flow' },
      { id: 'balance-sheet-report', label: 'Balance Sheet', icon: 'fas fa-scale-balanced', route: '/reports/balance-sheet', permission: 'accounting.reports.balance_sheet' },
      { id: 'enhanced-general-ledger', label: 'Enhanced General Ledger', icon: 'fas fa-book-open', route: '/reports/enhanced-general-ledger', permission: 'accounting.reports.general_ledger' },
      { id: 'ap-subledger', label: 'AP Subledger', icon: 'fas fa-file-invoice', route: '/reports/ap-subledger', permission: 'accounting.reports.ap_subledger' },
      { id: 'ap-aging', label: 'AP Aging', icon: 'fas fa-hourglass-half', route: '/reports/ap-aging', permission: 'accounting.reports.ap_aging' },
      { id: 'supplier-statement', label: 'Supplier Statement', icon: 'fas fa-list-check', route: '/reports/supplier-statement', permission: 'accounting.reports.supplier_statement' },
      { id: 'ap-reconciliation', label: 'AP Reconciliation', icon: 'fas fa-arrows-rotate', route: '/reports/ap-reconciliation', permission: 'accounting.reports.ap_reconciliation' },
      { id: 'vat-summary', label: 'VAT Summary', icon: 'fas fa-percent', route: '/reports/vat-summary', permission: 'accounting.reports.vat' },
      { id: 'vat-details', label: 'VAT Details', icon: 'fas fa-receipt', route: '/reports/vat-details', permission: 'accounting.reports.vat' },
      { id: 'vat-reconciliation', label: 'VAT Reconciliation', icon: 'fas fa-scale-balanced', route: '/reports/vat-reconciliation', permission: 'accounting.reports.vat' },
      { id: 'vat-exceptions', label: 'VAT Exceptions', icon: 'fas fa-triangle-exclamation', route: '/reports/vat-exceptions', permission: 'accounting.reports.vat' },
      { id: 'dashboard', label: 'Dashboard', icon: 'fas fa-gauge-high', route: '/dashboard' },
    ]
  },
  {
    id: 'administration',
    label: 'ADMINISTRATION',
    items: [
      { id: 'users', label: 'Users', icon: 'fas fa-users', route: '/users', permission: 'users.view' },
      { id: 'roles', label: 'Roles', icon: 'fas fa-user-tag', route: '/roles', permission: 'roles.view' },
      { id: 'outlets', label: 'Outlets', icon: 'fas fa-store', route: '/outlets', permission: 'outlets.view' },
      { id: 'warehouses', label: 'Warehouses', icon: 'fas fa-warehouse', route: '/warehouses', permission: 'warehouses.view' },
      { id: 'settings', label: 'Settings', icon: 'fas fa-cog', route: '/settings', permission: 'settings.edit' },
      { id: 'discount-settings', label: 'Discount Limit', icon: 'fas fa-percent', route: '/settings/discounts', permission: 'sales.discount.configure' },
      { id: 'audit-logs', label: 'Audit Logs', icon: 'fas fa-history', route: '/audit-logs', permission: 'audit.view' },
      { id: 'businesses', label: 'Businesses', icon: 'fas fa-building', route: '/businesses', role: 'Super Admin' },
    ]
  },
];
