import { Routes } from '@angular/router';
import { LoginComponent } from './components/login/login.component';
import { RegisterComponent } from './components/register/register.component';
import { LayoutComponent } from './components/layout/layout.component';
import { HrOrganisationPageComponent } from './pages/hr/organisation/hr-organisation-page.component';
import { HrOperationalLocationsPageComponent } from './pages/hr/operational-locations/hr-operational-locations-page.component';
import { HrPositionsPageComponent } from './pages/hr/positions/hr-positions-page.component';
import { HrEmployeesPageComponent } from './pages/hr/employees/hr-employees-page.component';
import { OpeningEmployeeImportComponent } from './pages/hr/employees/opening-employee-import.component';
import { HrCalendarsPageComponent } from './pages/hr/calendars/hr-calendars-page.component';
import { HrRostersPageComponent } from './pages/hr/attendance/hr-rosters-page.component';
import { HrDevicesPageComponent } from './pages/hr/attendance/hr-devices-page.component';
import { HrAttendanceReportsPageComponent } from './pages/hr/attendance/hr-attendance-reports-page.component';
import { HrLeavePageComponent } from './pages/hr/leave/hr-leave-page.component';
import { HrOvertimePageComponent } from './pages/hr/overtime/hr-overtime-page.component';
import { HrExitPageComponent } from './pages/hr/exit/hr-exit-page.component';
import { HrCoreReportsPageComponent } from './pages/hr/reports/hr-core-reports-page.component';
import { HrDashboardPageComponent } from './pages/hr/dashboard/hr-dashboard-page.component';
import { PayrollSalarySetupPageComponent } from './pages/payroll/payroll-salary-setup-page.component';
import { PayrollLoansPageComponent } from './pages/payroll/payroll-loans-page.component';
import { PayrollRunValidationPageComponent } from './pages/payroll/payroll-run-validation-page.component';
import { PayrollPeriodControlPageComponent } from './pages/payroll/payroll-period-control-page.component';
import { PayrollPaymentsPageComponent } from './pages/payroll/payroll-payments-page.component';
import { PayrollDashboardPageComponent } from './pages/payroll/payroll-dashboard-page.component';
import { PayrollAdvancedReportsPageComponent } from './pages/payroll/payroll-advanced-reports-page.component';
import { hrPayrollCapabilityGuard } from './guards/hr-payroll-capability.guard';
import { DashboardComponent } from './components/dashboard/dashboard.component';
import { UserListComponent } from './components/users/user-list.component';
import { UserFormComponent } from './components/users/user-form.component';
import { UserDetailsComponent } from './components/users/user-details.component';
import { RoleListComponent } from './components/roles/role-list.component';
import { RoleFormComponent } from './components/roles/role-form.component';
import { OutletListComponent } from './components/outlets/outlet-list.component';
import { OutletFormComponent } from './components/outlets/outlet-form.component';
import { OutletDetailsComponent } from './components/outlets/outlet-details.component';
import { WarehouseListComponent } from './components/warehouses/warehouse-list.component';
import { WarehouseFormComponent } from './components/warehouses/warehouse-form.component';
import { WarehouseDetailsComponent } from './components/warehouses/warehouse-details.component';
import { CategoryListComponent } from './components/categories/category-list.component';
import { CategoryFormComponent } from './components/categories/category-form.component';
import { ProductList } from './components/product-list/product-list';
import { ProductForm } from './components/product-form/product-form';
import { ProductDetailComponent } from './components/product-detail/product-detail.component';
import { VariationsComponent } from './pages/variations/variations.component';
import { InventoryListComponent } from './components/inventory/inventory-list.component';
import { LowStockAlertsComponent } from './components/inventory/low-stock-alerts.component';
import { SupplierListComponent } from './pages/suppliers/supplier-list.component';
import { SupplierFormComponent } from './pages/suppliers/supplier-form.component';
import { SupplierDetailsComponent } from './pages/suppliers/supplier-details.component';
import { PoListComponent } from './pages/purchase-orders/po-list.component';
import { PoFormComponent } from './components/po-form/po-form.component';
import { PoDetailsComponent } from './components/po-details/po-details.component';
import { GrnListComponent } from './pages/grn/grn-list.component';
import { GrnCreateComponent } from './pages/grn/grn-create.component';
import { GrnDetailsComponent } from './pages/grn/grn-details.component';
import { CustomerListComponent } from './pages/customers/customer-list.component';
import { CustomerFormComponent } from './pages/customers/customer-form.component';
import { CustomerDetailsComponent } from './pages/customers/customer-details.component';
import { PosScreenComponent } from './pages/pos/pos-screen.component';
import { CashSessionListComponent } from './pages/cash-sessions/cash-session-list.component';
import { CashSessionOpenComponent } from './pages/cash-sessions/cash-session-open.component';
import { CashSessionCloseComponent } from './pages/cash-sessions/cash-session-close.component';
import { CashSessionDetailComponent } from './pages/cash-sessions/cash-session-detail.component';
import { SalesListComponent } from './pages/sales/sales-list.component';
import { SaleDetailsComponent } from './pages/sales/sale-details.component';
import { TransferListComponent } from './pages/stock-transfers/transfer-list.component';
import { TransferCreateComponent } from './pages/stock-transfers/transfer-create.component';
import { TransferDetailsComponent } from './pages/stock-transfers/transfer-details.component';
import { AdjustmentListComponent } from './pages/stock-adjustments/adjustment-list.component';
import { AdjustmentCreateComponent } from './pages/stock-adjustments/adjustment-create.component';
import { AdjustmentDetailsComponent } from './pages/stock-adjustments/adjustment-details.component';
import { AdjustmentEditComponent } from './pages/stock-adjustments/adjustment-edit.component';
import { StockCountListComponent } from './pages/stock-counts/stock-count-list.component';
import { StockCountCreateComponent } from './pages/stock-counts/stock-count-create.component';
import { StockCountDetailsComponent } from './pages/stock-counts/stock-count-details.component';
import { AccountsListComponent } from './pages/accounting/accounts-list.component';
import { ChartOfAccountsComponent } from './pages/accounting/chart-of-accounts.component';
import { TransactionsListComponent } from './pages/accounting/transactions-list.component';
import { ExpensesListComponent } from './pages/accounting/expenses-list.component';
import { ExpenseFormComponent } from './pages/accounting/expense-form.component';
import { ManualJournalListComponent } from './pages/accounting/manual-journal-list.component';
import { ManualJournalFormComponent } from './pages/accounting/manual-journal-form.component';
import { ManualJournalDetailComponent } from './pages/accounting/manual-journal-detail.component';
import { OtherIncomeListComponent } from './pages/accounting/other-income-list.component';
import { OtherIncomeFormComponent } from './pages/accounting/other-income-form.component';
import { AccountingPlaceholderComponent } from './pages/accounting/accounting-placeholder.component';
import { FinancialYearsComponent } from './pages/accounting/financial-years.component';
import { FinancialPostingsComponent } from './pages/accounting/financial-postings.component';
import { OpeningBalanceComponent } from './pages/accounting/opening-balance.component';
import { AccountingReportsComponent } from './pages/accounting/accounting-reports.component';
import { ValuationReportsComponent } from './pages/accounting/valuation-reports.component';
import { CashBookReportComponent } from './pages/accounting/cash-book-report.component';
import { CashFlowReportComponent } from './pages/accounting/cash-flow-report.component';
import { ProfitabilityReportComponent } from './pages/accounting/profitability-report.component';
import { AccountingConfigurationComponent } from './pages/accounting/accounting-configuration.component';
import { BalanceSheetReportComponent } from './pages/accounting/balance-sheet-report.component';
import { EnhancedGeneralLedgerComponent } from './pages/accounting/enhanced-general-ledger.component';
import { ExpenseCategoriesComponent } from './pages/accounting/expense-categories.component';
import { SupplierPaymentsComponent } from './pages/accounting/supplier-payments.component';
import { ApReportComponent } from './pages/accounting/ap-report.component';
import { VatReportComponent } from './pages/accounting/vat-report.component';
import { BillsListComponent } from './pages/accounting/bills-list.component';
import { BankReconciliationComponent } from './pages/accounting/bank-reconciliation.component';
import { AccountingCloseWorkbenchComponent } from './pages/accounting/accounting-close-workbench.component';
import { SalesReportComponent } from './pages/reports/sales-report.component';
import { InventoryReportComponent } from './pages/reports/inventory-report.component';
import { PurchaseReportComponent } from './pages/reports/purchase-report.component';
import { StockTransactionReportComponent } from './pages/reports/stock-transaction-report.component';
import { CurrentStockReportComponent } from './pages/reports/current-stock-report.component';
import { ProductLedgerReportComponent } from './pages/reports/product-ledger-report.component';
import { StockMovementReportComponent } from './pages/reports/stock-movement-report.component';
import { StockValuationReportComponent } from './pages/reports/stock-valuation-report.component';
import { OutletWiseStockReportComponent } from './pages/reports/outlet-wise-stock-report.component';
import { LowStockReportComponent } from './pages/reports/low-stock-report.component';
import { OutOfStockReportComponent } from './pages/reports/out-of-stock-report.component';
import { NegativeStockReportComponent } from './pages/reports/negative-stock-report.component';
import { StockAdjustmentReportComponent } from './pages/reports/stock-adjustment-report.component';
import { StockTransferReportComponent } from './pages/reports/stock-transfer-report.component';
import { SettingsPageComponent } from './pages/settings/settings-page.component';
import { PosDiscountSettingsComponent } from './pages/settings/pos-discount-settings.component';
import { AuditListComponent } from './pages/audit/audit-list.component';
import { BusinessListComponent } from './pages/businesses/business-list.component';
import { BusinessFormComponent } from './pages/businesses/business-form.component';
import { BusinessSetupComponent } from './pages/businesses/business-setup.component';
import { GlobalSearchComponent } from './pages/global-search/global-search.component';
import { BarcodeLabelsComponent } from './pages/barcodes/barcode-labels.component';
import { BarcodeTemplatesComponent } from './pages/barcodes/barcode-templates.component';
import { authGuard, loginGuard, roleGuard, routePermissionGuard } from './guards/auth.guard';
import { unsavedChangesGuard } from './guards/unsaved-changes.guard';

export const routes: Routes = [
  { path: '', redirectTo: '/dashboard', pathMatch: 'full' },
  { path: 'login', component: LoginComponent, canActivate: [loginGuard] },
  { path: 'register', component: RegisterComponent, canActivate: [loginGuard] },

  // Protected routes with layout
  {
    path: '',
    component: LayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', component: DashboardComponent },
      {
        path: 'dashboard/sales-finance',
        loadComponent: () => import('./pages/owner-dashboard/sales-finance-page.component')
          .then(page => page.SalesFinancePageComponent),
        canActivate: [routePermissionGuard],
        data: { permissions: ['dashboard.sales_finance.view'] }
      },
      {
        path: 'dashboard/inventory-intelligence',
        loadComponent: () => import('./pages/owner-dashboard/inventory-intelligence-page.component')
          .then(page => page.InventoryIntelligencePageComponent),
        canActivate: [routePermissionGuard],
        data: { permissions: ['dashboard.inventory_intelligence.view'] }
      },
      { path: 'search', component: GlobalSearchComponent },
      {
        path: 'hr/dashboard',
        component: HrDashboardPageComponent,
        canActivate: [hrPayrollCapabilityGuard('hr.enabled'), routePermissionGuard],
        data: { permissions: ['hr.dashboard.view'] }
      },
      {
        path: 'payroll/dashboard',
        component: PayrollDashboardPageComponent,
        canActivate: [
          hrPayrollCapabilityGuard('payroll.enabled'),
          hrPayrollCapabilityGuard('hr.enabled'),
          routePermissionGuard
        ],
        data: {
          permissions: ['payroll.dashboard.view', 'payroll.salary.view'],
          permissionMode: 'all'
        }
      },
      {
        path: 'payroll/reports',
        component: PayrollAdvancedReportsPageComponent,
        canActivate: [
          hrPayrollCapabilityGuard('payroll.enabled'),
          hrPayrollCapabilityGuard('hr.enabled'),
          routePermissionGuard
        ],
        data: {
          permissions: ['payroll.report.view', 'payroll.salary.view'],
          permissionMode: 'all'
        }
      },
      {
        path: 'hr/org-units',
        component: HrOrganisationPageComponent,
        canActivate: [hrPayrollCapabilityGuard('hr.enabled'), routePermissionGuard],
        data: { permissions: ['hr.organisation.view'] }
      },
      {
        path: 'hr/operational-locations',
        component: HrOperationalLocationsPageComponent,
        canActivate: [hrPayrollCapabilityGuard('hr.enabled'), routePermissionGuard],
        data: { permissions: ['hr.location.view'] }
      },
      {
        path: 'hr/employees',
        component: HrEmployeesPageComponent,
        canActivate: [hrPayrollCapabilityGuard('hr.enabled'), routePermissionGuard],
        data: { permissions: ['hr.employee.view'] }
      },
      {
        path: 'hr/employee/new',
        component: HrEmployeesPageComponent,
        canActivate: [hrPayrollCapabilityGuard('hr.enabled'), routePermissionGuard],
        data: { permissions: ['hr.employee.create'] }
      },
      {
        path: 'hr/employee/:employeeId/view',
        component: HrEmployeesPageComponent,
        canActivate: [hrPayrollCapabilityGuard('hr.enabled'), routePermissionGuard],
        data: { permissions: ['hr.employee.view'] }
      },
      {
        path: 'hr/employee/:employeeId/edit',
        component: HrEmployeesPageComponent,
        canActivate: [hrPayrollCapabilityGuard('hr.enabled'), routePermissionGuard],
        data: { permissions: ['hr.employee.view', 'hr.employee.edit'], permissionMode: 'all' }
      },
      {
        path: 'hr/positions',
        component: HrPositionsPageComponent,
        canActivate: [hrPayrollCapabilityGuard('hr.enabled'), routePermissionGuard],
        data: { permissions: ['hr.position.view'] }
      },

      {
        path: 'hr/opening-imports',
        component: OpeningEmployeeImportComponent,
        canActivate: [hrPayrollCapabilityGuard('hr.enabled'), routePermissionGuard],
        data: { permissions: ['hr.import.view'] }
      },
      {
        path: 'payroll/opening-imports',
        component: OpeningEmployeeImportComponent,
        canActivate: [hrPayrollCapabilityGuard('payroll.enabled'), routePermissionGuard],
        data: { permissions: ['payroll.import.view'] }
      },
      {
        path: 'hr/shifts', component: HrCalendarsPageComponent,
        canActivate: [hrPayrollCapabilityGuard('attendance.enabled'), routePermissionGuard],
        data: { permissions: ['hr.shift.view'], calendarMode: 'shifts' }
      },
      {
        path: 'hr/holidays', component: HrCalendarsPageComponent,
        canActivate: [hrPayrollCapabilityGuard('attendance.enabled'), routePermissionGuard],
        data: { permissions: ['hr.holiday.view'], calendarMode: 'holidays' }
      },
      {
        path: 'hr/weekly-off', component: HrCalendarsPageComponent,
        canActivate: [hrPayrollCapabilityGuard('attendance.enabled'), routePermissionGuard],
        data: { permissions: ['hr.weekly_off.view'], calendarMode: 'weekly-off' }
      },

      // User Management Routes
      {
        path: 'hr/rosters', component: HrRostersPageComponent,
        canActivate: [hrPayrollCapabilityGuard('attendance.enabled'), routePermissionGuard],
        data: { permissions: ['hr.roster.view'] }
      },
      {
        path: 'hr/attendance/reports', component: HrAttendanceReportsPageComponent,
        canActivate: [hrPayrollCapabilityGuard('attendance.enabled'), routePermissionGuard],
        data: { permissions: ['hr.attendance.report'] }
      },
      {
        path: 'hr/reports', component: HrCoreReportsPageComponent,
        canActivate: [hrPayrollCapabilityGuard('hr.enabled'), routePermissionGuard],
        data: { permissions: ['hr.report.view'] }
      },
      {
        path: 'hr/leave', component: HrLeavePageComponent,
        canActivate: [hrPayrollCapabilityGuard('hr.enabled'), routePermissionGuard],
        data: {
          permissions: [
            'hr.leave.view', 'hr.leave.request', 'hr.leave.approve', 'hr.leave.admin',
            'hr.leave_balance.view', 'hr.leave_year.close', 'hr.leave_year.reopen'
          ],
          permissionMode: 'any'
        }
      },
      {
        path: 'hr/exits', component: HrExitPageComponent,
        canActivate: [hrPayrollCapabilityGuard('hr.enabled'), routePermissionGuard],
        data: {
          permissions: ['hr.exit.view', 'hr.exit.initiate', 'hr.exit.clearance', 'hr.exit.approve'],
          permissionMode: 'any'
        }
      },
      {
        path: 'hr/overtime', component: HrOvertimePageComponent,
        canActivate: [
          hrPayrollCapabilityGuard('hr.enabled'),
          hrPayrollCapabilityGuard('attendance.enabled'),
          routePermissionGuard
        ],
        data: {
          permissions: [
            'hr.overtime.view', 'hr.overtime.calculate', 'hr.overtime.submit',
            'hr.overtime.approve', 'hr.overtime_policy.manage'
          ],
          permissionMode: 'any'
        }
      },
      {
        path: 'hr/attendance/devices', component: HrDevicesPageComponent,
        canActivate: [hrPayrollCapabilityGuard('deviceintegration.enabled'), routePermissionGuard],
        data: { permissions: ['hr.device.view'] }
      },
      {
        path: 'payroll/salary-setup', component: PayrollSalarySetupPageComponent,
        canActivate: [
          hrPayrollCapabilityGuard('payroll.enabled'),
          hrPayrollCapabilityGuard('hr.enabled'),
          routePermissionGuard
        ],
        data: { permissions: ['payroll.salary.view'] }
      },
      {
        path: 'payroll/loans', component: PayrollLoansPageComponent,
        canActivate: [
          hrPayrollCapabilityGuard('payroll.enabled'),
          hrPayrollCapabilityGuard('hr.enabled'),
          routePermissionGuard
        ],
        data: { permissions: ['payroll.loan.view'] }
      },
      {
        path: 'payroll/validation', component: PayrollRunValidationPageComponent,
        canActivate: [
          hrPayrollCapabilityGuard('payroll.enabled'),
          hrPayrollCapabilityGuard('hr.enabled'),
          routePermissionGuard
        ],
        data: { permissions: ['payroll.run.view', 'payroll.salary.view'] }
      },
      {
        path: 'payroll/payments', component: PayrollPaymentsPageComponent,
        canActivate: [
          hrPayrollCapabilityGuard('payroll.enabled'),
          hrPayrollCapabilityGuard('hr.enabled'),
          routePermissionGuard
        ],
        data: { permissions: ['payroll.payment.view', 'payroll.payment.process'] }
      },
      {
        path: 'payroll/periods', component: PayrollPeriodControlPageComponent,
        canActivate: [
          hrPayrollCapabilityGuard('payroll.enabled'),
          hrPayrollCapabilityGuard('hr.enabled'),
          routePermissionGuard
        ],
        data: { permissions: ['payroll.period.view'] }
      },

      { path: 'businesses', component: BusinessListComponent, canActivate: [roleGuard(['Super Admin'])] },
      { path: 'businesses/create', component: BusinessFormComponent, canActivate: [roleGuard(['Super Admin'])] },
      { path: 'businesses/:id/setup', component: BusinessSetupComponent, canActivate: [roleGuard(['Super Admin'])] },

      { path: 'users', component: UserListComponent, canActivate: [routePermissionGuard], data: { permissions: ['users.view'] } },
      { path: 'users/create', component: UserFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['users.create'] } },
      { path: 'users/edit/:id', component: UserFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['users.edit'] } },
      { path: 'users/:id', component: UserDetailsComponent, canActivate: [routePermissionGuard], data: { permissions: ['users.view'] } },

      // Role Management Routes
      { path: 'roles', component: RoleListComponent, canActivate: [routePermissionGuard], data: { permissions: ['roles.view'] } },
      { path: 'roles/create', component: RoleFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['roles.create'] } },
      { path: 'roles/edit/:id', component: RoleFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['roles.edit'] } },

      // Outlet Management Routes
      { path: 'outlets', component: OutletListComponent, canActivate: [routePermissionGuard], data: { permissions: ['outlets.view'] } },
      { path: 'outlets/create', component: OutletFormComponent, canActivate: [routePermissionGuard], canDeactivate: [unsavedChangesGuard], data: { permissions: ['outlets.create'] } },
      { path: 'outlets/edit/:id', component: OutletFormComponent, canActivate: [routePermissionGuard], canDeactivate: [unsavedChangesGuard], data: { permissions: ['outlets.edit'] } },
      { path: 'outlets/:id', component: OutletDetailsComponent, canActivate: [routePermissionGuard], data: { permissions: ['outlets.view'] } },

      // Warehouse Management Routes
      { path: 'warehouses', component: WarehouseListComponent, canActivate: [routePermissionGuard], data: { permissions: ['warehouses.view'] } },
      { path: 'warehouses/create', component: WarehouseFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['warehouses.create'] } },
      { path: 'warehouses/edit/:id', component: WarehouseFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['warehouses.edit'] } },
      { path: 'warehouses/:id', component: WarehouseDetailsComponent, canActivate: [routePermissionGuard], data: { permissions: ['warehouses.view'] } },

      // Category Management Routes
      { path: 'categories', component: CategoryListComponent, canActivate: [routePermissionGuard], data: { permissions: ['categories.view'] } },
      { path: 'categories/create', component: CategoryFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['categories.create'] } },
      { path: 'categories/edit/:id', component: CategoryFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['categories.edit'] } },

      // Variations Management Routes
      { path: 'variations', component: VariationsComponent, canActivate: [routePermissionGuard], data: { permissions: ['variations.view'] } },

      // Product Management Routes
      { path: 'products', component: ProductList, canActivate: [routePermissionGuard], data: { permissions: ['products.view'] } },
      { path: 'products/create', component: ProductForm, canActivate: [routePermissionGuard], data: { permissions: ['products.create'] } },
      { path: 'products/edit/:id', component: ProductForm, canActivate: [routePermissionGuard], data: { permissions: ['products.edit'] } },
      { path: 'products/:id', component: ProductDetailComponent, canActivate: [routePermissionGuard], data: { permissions: ['products.view'] } },
      {
        path: 'barcode-labels',
        component: BarcodeLabelsComponent,
        canActivate: [routePermissionGuard],
        data: { permissions: ['barcode.view', 'barcode.print', 'barcode.bulk_print', 'barcode.template_manage'] }
      },
      { path: 'barcode-templates', component: BarcodeTemplatesComponent, canActivate: [routePermissionGuard], data: { permissions: ['barcode.template_manage'] } },

      // Inventory Management Routes
      { path: 'inventory', component: InventoryListComponent, canActivate: [routePermissionGuard], data: { permissions: ['inventory.view'] } },
      { path: 'inventory/low-stock', component: LowStockAlertsComponent, canActivate: [routePermissionGuard], data: { permissions: ['low_stock_alerts.view'] } },

      // Supplier Management Routes
      { path: 'suppliers', component: SupplierListComponent, canActivate: [routePermissionGuard], data: { permissions: ['suppliers.view'] } },
      { path: 'suppliers/create', component: SupplierFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['suppliers.create'] } },
      { path: 'suppliers/edit/:id', component: SupplierFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['suppliers.edit'] } },
      { path: 'suppliers/:id', component: SupplierDetailsComponent, canActivate: [routePermissionGuard], data: { permissions: ['suppliers.view'] } },

      // Purchase Order Management Routes
      { path: 'purchase-orders', component: PoListComponent, canActivate: [routePermissionGuard], data: { permissions: ['purchases.view'] } },
      { path: 'purchase-orders/create', component: PoFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['purchases.create'] } },
      { path: 'purchase-orders/edit/:id', component: PoFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['purchases.edit'] } },
      { path: 'purchase-orders/:id', component: PoDetailsComponent, canActivate: [routePermissionGuard], data: { permissions: ['purchases.view'] } },

      // GRN Routes
      { path: 'grn', component: GrnListComponent, canActivate: [routePermissionGuard], data: { permissions: ['grn.view'] } },
      { path: 'grn/create', component: GrnCreateComponent, canActivate: [routePermissionGuard], data: { permissions: ['grn.create'] } },
      { path: 'grn/:id', component: GrnDetailsComponent, canActivate: [routePermissionGuard], data: { permissions: ['grn.view'] } },

      // Customer Routes
      { path: 'customers', component: CustomerListComponent, canActivate: [routePermissionGuard], data: { permissions: ['customers.view'] } },
      { path: 'customers/create', component: CustomerFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['customers.create'] } },
      { path: 'customers/edit/:id', component: CustomerFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['customers.edit'] } },
      { path: 'customers/:id', component: CustomerDetailsComponent, canActivate: [routePermissionGuard], data: { permissions: ['customers.view'] } },

      // POS & Sales Routes
      { path: 'pos', component: PosScreenComponent, canActivate: [routePermissionGuard], data: { permissions: ['sales.create'] } },
  // Cash Sessions (Phase 3): dedicated routes; the API re-checks every permission and the Business/Outlet scope.
  { path: 'cash-sessions', component: CashSessionListComponent, canActivate: [routePermissionGuard], data: { permissions: ['cash_sessions.view'] } },
  { path: 'cash-sessions/open', component: CashSessionOpenComponent, canActivate: [routePermissionGuard], data: { permissions: ['cash_sessions.open'] } },
  { path: 'cash-sessions/:id/close', component: CashSessionCloseComponent, canActivate: [routePermissionGuard], data: { permissions: ['cash_sessions.close'] } },
  { path: 'cash-sessions/:id', component: CashSessionDetailComponent, canActivate: [routePermissionGuard], data: { permissions: ['cash_sessions.view'] } },
      { path: 'sales', component: SalesListComponent, canActivate: [routePermissionGuard], data: { permissions: ['sales.view'] } },
      { path: 'sales/:id', component: SaleDetailsComponent, canActivate: [routePermissionGuard], data: { permissions: ['sales.view'] } },

      // Stock Transfer Routes
      { path: 'stock-transfers', component: TransferListComponent, canActivate: [routePermissionGuard], data: { permissions: ['stock_transfers.view'] } },
      { path: 'stock-transfers/create', component: TransferCreateComponent, canActivate: [routePermissionGuard], canDeactivate: [unsavedChangesGuard], data: { permissions: ['stock_transfers.create'] } },
      { path: 'stock-transfers/:id', component: TransferDetailsComponent, canActivate: [routePermissionGuard], data: { permissions: ['stock_transfers.view'] } },

      // Stock Adjustment Routes
      { path: 'stock-adjustments', component: AdjustmentListComponent, canActivate: [routePermissionGuard], data: { permissions: ['stock_adjustments.view'] } },
      { path: 'stock-adjustments/create', component: AdjustmentCreateComponent, canActivate: [routePermissionGuard], data: { permissions: ['stock_adjustments.create'] } },
      { path: 'stock-adjustments/:id/edit', component: AdjustmentEditComponent, canActivate: [routePermissionGuard], data: { permissions: ['stock_adjustments.edit'] } },
      { path: 'stock-adjustments/:id', component: AdjustmentDetailsComponent, canActivate: [routePermissionGuard], data: { permissions: ['stock_adjustments.view'] } },

      // Stock Count Routes
      { path: 'stock-counts', component: StockCountListComponent, canActivate: [routePermissionGuard], data: { permissions: ['StockCount.ViewOwn', 'StockCount.ViewAll'] } },
      { path: 'stock-counts/create', component: StockCountCreateComponent, canActivate: [routePermissionGuard], data: { permissions: ['StockCount.Create'] } },
      { path: 'stock-counts/:id', component: StockCountDetailsComponent, canActivate: [routePermissionGuard], data: { permissions: ['StockCount.ViewOwn', 'StockCount.ViewAll'] } },

      // Reports Routes
      { path: 'reports/sales', component: SalesReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['reports.sales'] } },
      { path: 'reports/inventory', component: InventoryReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['reports.inventory'] } },
      { path: 'reports/current-stock', component: CurrentStockReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['reports.inventory'] } },
      { path: 'reports/product-ledger', component: ProductLedgerReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['reports.inventory'] } },
      { path: 'reports/stock-movement', component: StockMovementReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['reports.inventory'] } },
      { path: 'reports/stock-valuation', component: StockValuationReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['reports.inventory'] } },
      { path: 'reports/outlet-wise-stock', component: OutletWiseStockReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['reports.inventory'] } },
      { path: 'reports/low-stock', component: LowStockReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['reports.inventory'] } },
      { path: 'reports/out-of-stock', component: OutOfStockReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['reports.inventory'] } },
      { path: 'reports/negative-stock', component: NegativeStockReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['reports.inventory'] } },
      { path: 'reports/stock-adjustments', component: StockAdjustmentReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['reports.inventory'] } },
      { path: 'reports/stock-transfers-report', component: StockTransferReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['reports.inventory'] } },
      { path: 'reports/purchases', component: PurchaseReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['purchases.view'] } },
      { path: 'reports/stock-transactions', component: StockTransactionReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['reports.inventory'] } },

      // Audit Log Route
      { path: 'audit-logs', component: AuditListComponent, canActivate: [routePermissionGuard], data: { permissions: ['audit.view'] } },

      // Settings Routes
      { path: 'settings', component: SettingsPageComponent, canActivate: [routePermissionGuard], data: { permissions: ['settings.edit'] } },
      { path: 'settings/company', component: SettingsPageComponent, canActivate: [routePermissionGuard], data: { permissions: ['settings.edit'] } },
      { path: 'settings/discounts', component: PosDiscountSettingsComponent, canActivate: [routePermissionGuard], data: { permissions: ['sales.discount.configure'], title: 'Discount Limit' } },

      // Accounting Routes
      { path: 'accounts', component: AccountsListComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.chart_accounts.view'] } },
      { path: 'accounting/chart-of-accounts', component: ChartOfAccountsComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.chart_accounts.view'], title: 'Chart of Accounts' } },
      { path: 'settings/accounting', component: AccountingConfigurationComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.configuration.view'] } },
      { path: 'accounting/reconciliation', component: BankReconciliationComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reconciliation.view'] } },
      { path: 'accounting/close-workbench', component: AccountingCloseWorkbenchComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.close.audit.view'], title: 'Close Workbench' } },
      { path: 'financial-postings', component: FinancialPostingsComponent, canActivate: [routePermissionGuard], data: { permissions: ['sales.financial_postings.view'], title: 'Sale GL Postings' } },
      { path: 'financial-years', component: FinancialYearsComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.financial_years.view', 'accounting.periods.view'], title: 'Financial Years / Accounting Periods' } },
      { path: 'opening-balance', component: OpeningBalanceComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.opening_balances.post', 'accounting.opening_balances.adjust'], title: 'Opening Balance' } },
      { path: 'expense-categories', component: ExpenseCategoriesComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.expenses.view', 'accounting.expense_categories.manage'], title: 'Expense Categories' } },
      { path: 'supplier-payments', component: SupplierPaymentsComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.supplier_payments.view', 'accounting.supplier_advances.view'], title: 'Supplier Payments & Advances' } },
      { path: 'transactions', component: TransactionsListComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.journals.view'] } },
      { path: 'manual-journals', component: ManualJournalListComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.manual_journals.view'] } },
      { path: 'manual-journals/create', component: ManualJournalFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.manual_journals.create'] } },
      { path: 'manual-journals/:id/edit', component: ManualJournalFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.manual_journals.update'] } },
      { path: 'manual-journals/:id', component: ManualJournalDetailComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.manual_journals.view'] } },
      { path: 'expenses', component: ExpensesListComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.expenses.view'] } },
      { path: 'expenses/create', component: ExpenseFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.expenses.create'] } },
      { path: 'expenses/edit/:id', component: ExpenseFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.expenses.edit'] } },
      { path: 'incomes', component: OtherIncomeListComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.other_income.view'] } },
      { path: 'incomes/create', component: OtherIncomeFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.other_income.create'] } },
      { path: 'incomes/edit/:id', component: OtherIncomeFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.other_income.edit'] } },
      { path: 'reports/general-ledger', component: AccountingReportsComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.general_ledger'], title: 'General Ledger' } },
      { path: 'reports/trial-balance', component: AccountingReportsComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.trial_balance'], title: 'Trial Balance' } },
      { path: 'reports/profit-loss', component: AccountingReportsComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.profit_loss'], title: 'Profit & Loss' } },
      { path: 'reports/profitability', component: ProfitabilityReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.profitability'], title: 'Profitability' } },
      { path: 'reports/cogs', component: ValuationReportsComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.cogs'], valuationMode: 'cogs', title: 'Cost of Goods Sold' } },
      { path: 'reports/inventory-valuation', component: ValuationReportsComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.inventory_valuation'], valuationMode: 'inventory', title: 'Inventory Valuation' } },
      { path: 'reports/cash-book', component: CashBookReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.cash_book'], title: 'Cash Book' } },
      { path: 'reports/cash-flow', component: CashFlowReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.cash_flow'], title: 'Cash Flow' } },
      { path: 'reports/balance-sheet', component: BalanceSheetReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.balance_sheet'], title: 'Balance Sheet' } },
      { path: 'reports/enhanced-general-ledger', component: EnhancedGeneralLedgerComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.general_ledger'], title: 'Enhanced General Ledger' } },
      { path: 'reports/ap-subledger', component: ApReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.ap_subledger'], reportType: 'subledger', title: 'AP Subledger' } },
      { path: 'reports/ap-aging', component: ApReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.ap_aging'], reportType: 'aging', title: 'AP Aging' } },
      { path: 'reports/supplier-statement', component: ApReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.supplier_statement'], reportType: 'statement', title: 'Supplier Statement' } },
      { path: 'reports/ap-reconciliation', component: ApReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.ap_reconciliation'], reportType: 'reconciliation', title: 'AP Reconciliation' } },
      { path: 'reports/supplier-advance-reconciliation', component: ApReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.ap_reconciliation'], reportType: 'advance-reconciliation', title: 'Supplier Advance Reconciliation' } },
      { path: 'reports/vat-summary', component: VatReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.vat'], reportType: 'summary', title: 'VAT Summary' } },
      { path: 'reports/vat-details', component: VatReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.vat'], reportType: 'details', title: 'VAT Details' } },
      { path: 'reports/vat-reconciliation', component: VatReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.vat'], reportType: 'reconciliation', title: 'VAT Reconciliation' } },
      { path: 'reports/vat-exceptions', component: VatReportComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.reports.vat'], reportType: 'exceptions', title: 'VAT Exceptions' } },
      { path: 'bills', component: BillsListComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounting.chart_accounts.view'] } },
    ]
  },

  { path: '**', redirectTo: '/dashboard' }
];
