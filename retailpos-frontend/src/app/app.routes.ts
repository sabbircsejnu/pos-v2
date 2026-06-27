import { Routes } from '@angular/router';
import { LoginComponent } from './components/login/login.component';
import { RegisterComponent } from './components/register/register.component';
import { LayoutComponent } from './components/layout/layout.component';
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
import { TransactionsListComponent } from './pages/accounting/transactions-list.component';
import { ExpensesListComponent } from './pages/accounting/expenses-list.component';
import { ExpenseFormComponent } from './pages/accounting/expense-form.component';
import { BillsListComponent } from './pages/accounting/bills-list.component';
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
import { AuditListComponent } from './pages/audit/audit-list.component';
import { BusinessListComponent } from './pages/businesses/business-list.component';
import { BusinessFormComponent } from './pages/businesses/business-form.component';
import { BusinessSetupComponent } from './pages/businesses/business-setup.component';
import { GlobalSearchComponent } from './pages/global-search/global-search.component';
import { BarcodeLabelsComponent } from './pages/barcodes/barcode-labels.component';
import { BarcodeTemplatesComponent } from './pages/barcodes/barcode-templates.component';
import { authGuard, loginGuard, roleGuard, routePermissionGuard } from './guards/auth.guard';

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
      { path: 'search', component: GlobalSearchComponent },
      
      // User Management Routes
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
      { path: 'outlets/create', component: OutletFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['outlets.create'] } },
      { path: 'outlets/edit/:id', component: OutletFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['outlets.edit'] } },
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
      { path: 'variations', component: VariationsComponent, canActivate: [routePermissionGuard], data: { permissions: ['products.view'] } },
      
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
      { path: 'sales', component: SalesListComponent, canActivate: [routePermissionGuard], data: { permissions: ['sales.view'] } },
      { path: 'sales/:id', component: SaleDetailsComponent, canActivate: [routePermissionGuard], data: { permissions: ['sales.view'] } },

      // Stock Transfer Routes
      { path: 'stock-transfers', component: TransferListComponent, canActivate: [routePermissionGuard], data: { permissions: ['stock_transfers.view'] } },
      { path: 'stock-transfers/create', component: TransferCreateComponent, canActivate: [routePermissionGuard], data: { permissions: ['stock_transfers.create'] } },
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

      // Accounting Routes
      { path: 'accounts', component: AccountsListComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounts.view'] } },
      { path: 'transactions', component: TransactionsListComponent, canActivate: [routePermissionGuard], data: { permissions: ['transactions.view'] } },
      { path: 'expenses', component: ExpensesListComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounts.view'] } },
      { path: 'expenses/create', component: ExpenseFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounts.create'] } },
      { path: 'expenses/edit/:id', component: ExpenseFormComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounts.edit'] } },
      { path: 'bills', component: BillsListComponent, canActivate: [routePermissionGuard], data: { permissions: ['accounts.view'] } },
    ]
  },
  
  { path: '**', redirectTo: '/dashboard' }
];


