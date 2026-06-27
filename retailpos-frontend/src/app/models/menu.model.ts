export interface MenuItem {
  id: string;
  label: string;
  icon: string;
  route?: string;
  permission?: string | string[];
  role?: string | string[];
  disabled?: boolean;
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
      { id: 'variations', label: 'Variations', icon: 'fas fa-sliders-h', route: '/variations', permission: 'products.view' },
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
      { id: 'sales-orders', label: 'Sales Orders', icon: 'fas fa-receipt', route: '/sales', permission: 'sales.view' },
      { id: 'customers', label: 'Customers', icon: 'fas fa-user-friends', route: '/customers', permission: 'customers.view' },
    ]
  },
  {
    id: 'finance',
    label: 'FINANCE',
    items: [
      { id: 'accounting', label: 'Accounting', icon: 'fas fa-calculator', route: '/accounts', permission: 'accounts.view' },
      { id: 'payments', label: 'Payments', icon: 'fas fa-credit-card', route: '/transactions', permission: 'transactions.view' },
    ]
  },
  {
    id: 'reporting',
    label: 'REPORTING',
    items: [
      { id: 'reports', label: 'Reports', icon: 'fas fa-chart-line', route: '/reports/sales', permission: ['reports.sales', 'reports.inventory'] },
      { id: 'dashboard', label: 'Dashboard', icon: 'fas fa-gauge-high', route: '/dashboard' },
    ]
  },
  {
    id: 'administration',
    label: 'ADMINISTRATION',
    items: [
      { id: 'users', label: 'Users', icon: 'fas fa-users', route: '/users', permission: 'users.view' },
      { id: 'roles', label: 'Roles', icon: 'fas fa-user-tag', route: '/roles', permission: 'roles.view' },
      { id: 'outlets', label: 'Outlets', icon: 'fas fa-store', route: '/outlets', permission: 'outlets.view', role: ['Business Owner / Admin', 'Outlet Manager', 'Warehouse Manager', 'Sales Person', 'Accounts Admin'] },
      { id: 'warehouses', label: 'Warehouses', icon: 'fas fa-warehouse', route: '/warehouses', permission: 'warehouses.view', role: ['Business Owner / Admin', 'Outlet Manager', 'Warehouse Manager', 'Sales Person', 'Accounts Admin'] },
      { id: 'settings', label: 'Settings', icon: 'fas fa-cog', route: '/settings', permission: 'settings.edit' },
      { id: 'audit-logs', label: 'Audit Logs', icon: 'fas fa-history', route: '/audit-logs', permission: 'audit.view' },
      { id: 'businesses', label: 'Businesses', icon: 'fas fa-building', route: '/businesses', role: 'Super Admin' },
    ]
  },
];
