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
import { AccountsListComponent } from './pages/accounting/accounts-list.component';
import { TransactionsListComponent } from './pages/accounting/transactions-list.component';
import { ExpensesListComponent } from './pages/accounting/expenses-list.component';
import { ExpenseFormComponent } from './pages/accounting/expense-form.component';
import { BillsListComponent } from './pages/accounting/bills-list.component';
import { SalesReportComponent } from './pages/reports/sales-report.component';
import { InventoryReportComponent } from './pages/reports/inventory-report.component';
import { PurchaseReportComponent } from './pages/reports/purchase-report.component';
import { StockTransactionReportComponent } from './pages/reports/stock-transaction-report.component';
import { SettingsPageComponent } from './pages/settings/settings-page.component';
import { AuditListComponent } from './pages/audit/audit-list.component';
import { authGuard, loginGuard } from './guards/auth.guard';

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
      
      // User Management Routes
      { path: 'users', component: UserListComponent },
      { path: 'users/create', component: UserFormComponent },
      { path: 'users/edit/:id', component: UserFormComponent },
      { path: 'users/:id', component: UserDetailsComponent },
      
      // Role Management Routes
      { path: 'roles', component: RoleListComponent },
      { path: 'roles/create', component: RoleFormComponent },
      { path: 'roles/edit/:id', component: RoleFormComponent },
      
      // Outlet Management Routes
      { path: 'outlets', component: OutletListComponent },
      { path: 'outlets/create', component: OutletFormComponent },
      { path: 'outlets/edit/:id', component: OutletFormComponent },
      { path: 'outlets/:id', component: OutletDetailsComponent },
      
      // Warehouse Management Routes
      { path: 'warehouses', component: WarehouseListComponent },
      { path: 'warehouses/create', component: WarehouseFormComponent },
      { path: 'warehouses/edit/:id', component: WarehouseFormComponent },
      { path: 'warehouses/:id', component: WarehouseDetailsComponent },
      
      // Category Management Routes
      { path: 'categories', component: CategoryListComponent },
      { path: 'categories/create', component: CategoryFormComponent },
      { path: 'categories/edit/:id', component: CategoryFormComponent },
      
      // Variations Management Routes
      { path: 'variations', component: VariationsComponent },
      
      // Product Management Routes
      { path: 'products', component: ProductList },
      { path: 'products/create', component: ProductForm },
      { path: 'products/edit/:id', component: ProductForm },
      
      // Inventory Management Routes
      { path: 'inventory', component: InventoryListComponent },
      { path: 'inventory/low-stock', component: LowStockAlertsComponent },
      
      // Supplier Management Routes
      { path: 'suppliers', component: SupplierListComponent },
      { path: 'suppliers/create', component: SupplierFormComponent },
      { path: 'suppliers/edit/:id', component: SupplierFormComponent },
      { path: 'suppliers/:id', component: SupplierDetailsComponent },
      
      // Purchase Order Management Routes
      { path: 'purchase-orders', component: PoListComponent },
      { path: 'purchase-orders/create', component: PoFormComponent },
      { path: 'purchase-orders/edit/:id', component: PoFormComponent },
      { path: 'purchase-orders/:id', component: PoDetailsComponent },

      // GRN Routes
      { path: 'grn', component: GrnListComponent },
      { path: 'grn/create', component: GrnCreateComponent },
      { path: 'grn/:id', component: GrnDetailsComponent },

      // Customer Routes
      { path: 'customers', component: CustomerListComponent },
      { path: 'customers/create', component: CustomerFormComponent },
      { path: 'customers/edit/:id', component: CustomerFormComponent },
      { path: 'customers/:id', component: CustomerDetailsComponent },

      // POS & Sales Routes
      { path: 'pos', component: PosScreenComponent },
      { path: 'sales', component: SalesListComponent },
      { path: 'sales/:id', component: SaleDetailsComponent },

      // Stock Transfer Routes
      { path: 'stock-transfers', component: TransferListComponent },
      { path: 'stock-transfers/create', component: TransferCreateComponent },
      { path: 'stock-transfers/:id', component: TransferDetailsComponent },

      // Stock Adjustment Routes
      { path: 'stock-adjustments', component: AdjustmentListComponent },
      { path: 'stock-adjustments/create', component: AdjustmentCreateComponent },

      // Reports Routes
      { path: 'reports/sales', component: SalesReportComponent },
      { path: 'reports/inventory', component: InventoryReportComponent },
      { path: 'reports/purchases', component: PurchaseReportComponent },
      { path: 'reports/stock-transactions', component: StockTransactionReportComponent },

      // Audit Log Route
      { path: 'audit-logs', component: AuditListComponent },

      // Settings Routes
      { path: 'settings', component: SettingsPageComponent },
      { path: 'settings/company', component: SettingsPageComponent },

      // Accounting Routes
      { path: 'accounts', component: AccountsListComponent },
      { path: 'transactions', component: TransactionsListComponent },
      { path: 'expenses', component: ExpensesListComponent },
      { path: 'expenses/create', component: ExpenseFormComponent },
      { path: 'expenses/edit/:id', component: ExpenseFormComponent },
      { path: 'bills', component: BillsListComponent },
    ]
  },
  
  { path: '**', redirectTo: '/dashboard' }
];


