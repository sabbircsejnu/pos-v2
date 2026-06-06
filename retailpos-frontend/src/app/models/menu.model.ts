export interface MenuItem {
  id: string;
  label: string;
  icon: string;
  route?: string;
  children?: MenuItem[];
  permission?: string | string[];
}

export const MENU_ITEMS: MenuItem[] = [
  {
    id: 'dashboard',
    label: 'Dashboard',
    icon: 'fas fa-home',
    route: '/dashboard'
  },
  {
    id: 'admin',
    label: 'Administration',
    icon: 'fas fa-user-shield',
    children: [
      {
        id: 'users',
        label: 'Users',
        icon: 'fas fa-users',
        route: '/users',
        permission: 'users.view'
      },
      {
        id: 'roles',
        label: 'Roles',
        icon: 'fas fa-user-tag',
        route: '/roles',
        permission: 'roles.view'
      },
      {
        id: 'audit-logs',
        label: 'Audit Log',
        icon: 'fas fa-history',
        route: '/audit-logs',
        permission: 'audit.view'
      }
    ]
  },
  {
    id: 'master-data',
    label: 'Master Data',
    icon: 'fas fa-database',
    children: [
      {
        id: 'outlets',
        label: 'Outlets',
        icon: 'fas fa-store',
        route: '/outlets',
        permission: 'outlets.view'
      },
      {
        id: 'warehouses',
        label: 'Warehouses',
        icon: 'fas fa-warehouse',
        route: '/warehouses',
        permission: 'warehouses.view'
      },
      {
        id: 'categories',
        label: 'Categories',
        icon: 'fas fa-tags',
        route: '/categories',
        permission: 'products.view'
      },
      {
        id: 'suppliers',
        label: 'Suppliers',
        icon: 'fas fa-truck',
        route: '/suppliers',
        permission: 'suppliers.view'
      }
    ]
  },
  {
    id: 'products',
    label: 'Products',
    icon: 'fas fa-box',
    children: [
      {
        id: 'product-list',
        label: 'Product List',
        icon: 'fas fa-list',
        route: '/products',
        permission: 'products.view'
      },
      {
        id: 'variations',
        label: 'Variations',
        icon: 'fas fa-sliders-h',
        route: '/variations',
        permission: 'products.view'
      },
      {
        id: 'inventory',
        label: 'Inventory',
        icon: 'fas fa-boxes',
        route: '/inventory',
        permission: 'inventory.view'
      },
      {
        id: 'low-stock',
        label: 'Low Stock Alerts',
        icon: 'fas fa-exclamation-triangle',
        route: '/inventory/low-stock',
        permission: 'inventory.view'
      }
    ]
  },
  {
    id: 'procurement',
    label: 'Procurement',
    icon: 'fas fa-shopping-basket',
    children: [
      {
        id: 'purchase-orders',
        label: 'Purchase Orders',
        icon: 'fas fa-file-invoice',
        route: '/purchase-orders',
        permission: 'purchases.view'
      },
      {
        id: 'grn',
        label: 'Goods Received (GRN)',
        icon: 'fas fa-boxes',
        route: '/grn',
        permission: 'grn.view'
      }
    ]
  },
  {
    id: 'stock-movement',
    label: 'Stock Movement',
    icon: 'fas fa-exchange-alt',
    children: [
      {
        id: 'stock-transfers',
        label: 'Stock Transfers',
        icon: 'fas fa-truck-moving',
        route: '/stock-transfers',
        permission: 'inventory.transfer'
      },
      {
        id: 'stock-adjustments',
        label: 'Stock Adjustments',
        icon: 'fas fa-sliders-h',
        route: '/stock-adjustments',
        permission: 'inventory.adjust'
      }
    ]
  },
  {
    id: 'accounting',
    label: 'Accounting',
    icon: 'fas fa-calculator',
    children: [
      {
        id: 'accounts',
        label: 'Chart of Accounts',
        icon: 'fas fa-book',
        route: '/accounts',
        permission: 'accounts.view'
      },
      {
        id: 'transactions',
        label: 'Transactions',
        icon: 'fas fa-exchange-alt',
        route: '/transactions',
        permission: 'transactions.view'
      },
      {
        id: 'expenses',
        label: 'Expenses',
        icon: 'fas fa-receipt',
        route: '/expenses',
        permission: 'accounts.view'
      },
      {
        id: 'bills',
        label: 'Bills Payable',
        icon: 'fas fa-file-invoice-dollar',
        route: '/bills',
        permission: 'accounts.view'
      }
    ]
  },
  {
    id: 'sales',
    label: 'Sales',
    icon: 'fas fa-cash-register',
    children: [
      {
        id: 'pos',
        label: 'POS',
        icon: 'fas fa-shopping-cart',
        route: '/pos',
        permission: 'sales.create'
      },
      {
        id: 'sales-list',
        label: 'Sales List',
        icon: 'fas fa-receipt',
        route: '/sales',
        permission: 'sales.view'
      },
      {
        id: 'customers',
        label: 'Customers',
        icon: 'fas fa-user-friends',
        route: '/customers',
        permission: 'customers.view'
      }
    ]
  },
  {
    id: 'reports',
    label: 'Reports',
    icon: 'fas fa-chart-line',
    children: [
      {
        id: 'sales-reports',
        label: 'Sales Reports',
        icon: 'fas fa-chart-bar',
        route: '/reports/sales',
        permission: 'reports.sales'
      },
      {
        id: 'inventory-reports',
        label: 'Inventory Reports',
        icon: 'fas fa-chart-pie',
        route: '/reports/inventory',
        permission: 'reports.inventory'
      },
      {
        id: 'stock-transaction-report',
        label: 'Stock Transactions',
        icon: 'fas fa-exchange-alt',
        route: '/reports/stock-transactions',
        permission: 'reports.inventory'
      },
      {
        id: 'purchase-reports',
        label: 'Purchase Reports',
        icon: 'fas fa-truck',
        route: '/reports/purchases',
        permission: 'purchases.view'
      },
      {
        id: 'financial-reports',
        label: 'Financial Reports',
        icon: 'fas fa-file-invoice-dollar',
        route: '/reports/financial',
        permission: 'reports.financial'
      }
    ]
  },
  {
    id: 'settings',
    label: 'Settings',
    icon: 'fas fa-cog',
    children: [
      {
        id: 'company-settings',
        label: 'Company & System Settings',
        icon: 'fas fa-cog',
        route: '/settings',
        permission: 'settings.edit'
      }
    ]
  }
];
