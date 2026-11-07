import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { RoleService } from '../../services/role.service';
import { CreateRoleDto, UpdateRoleDto } from '../../models/role.model';
import { HR_PAYROLL_PERMISSION_CATEGORIES } from '../../models/hr-payroll-permissions';

interface PermissionCategory {
  name: string;
  permissions: string[];
}

/** Readable labels for permissions whose raw key is not self-explanatory. */
export const PERMISSION_DETAILS: Record<string, { label: string; description: string }> = {
  'dashboard.sales_finance.view': {
    label: 'View Sales & Finance Dashboard',
    description: 'See all sales, purchase, expense, profit and cash figures of the business, for every outlet. Does not allow changing any transaction.'
  },
  'dashboard.inventory_intelligence.view': {
    label: 'View Inventory Intelligence Dashboard',
    description: 'See all stock, stock value and transfer recommendations of the business, for every location. Creating a transfer still needs the Stock Transfer permissions.'
  },
  'sales.discount.apply': {
    label: 'Apply Manual Discounts',
    description: 'Apply discounts at the POS, up to the maximum manual discount percentage.'
  },
  'sales.discount.override_limit': {
    label: 'Override Discount Limit',
    description: 'Apply discounts above the maximum manual discount percentage.'
  },
  'sales.discount.configure': {
    label: 'Configure Discount Limit',
    description: 'Change the maximum manual discount percentage in POS settings.'
  },
  'outlets.terminals.manage': {
    label: 'Manage POS Terminals',
    description: 'Add, rename, activate, deactivate and delete POS terminals when editing an outlet.'
  },
  'cash_sessions.view': { label: 'View Cash Sessions', description: 'See cash session history, details and reconciliation for authorized outlets.' },
  'cash_sessions.open': { label: 'Open Cash Sessions', description: 'Open a cash session on an active terminal of an authorized outlet.' },
  'cash_sessions.close': { label: 'Close Cash Sessions', description: 'Count the drawer and close a cash session, including an overdue one.' },
  'cash_sessions.handover.confirm': { label: 'Confirm Cash Handovers', description: 'Confirm a cash handover or a bank deposit recorded at close.' },
  'cash_sessions.attachments.upload': { label: 'Attach Closing Evidence', description: 'Upload photos or PDFs to a cash session.' },
  'cash_sessions.attachments.download': { label: 'Download Closing Evidence', description: 'Download cash session attachments.' },
  'cash_sessions.variance.view': { label: 'View Cash Differences', description: 'See cash differences awaiting a decision.' },
  'cash_sessions.variance.approve': { label: 'Approve Cash Differences', description: 'Approve or reject a closing cash difference (never your own close).' },
  'cash_sessions.variance.retry_posting': { label: 'Retry Cash Difference Posting', description: 'Retry the accounting posting of an approved cash difference.' },
  'sales.financial_postings.view': { label: 'View Sale Financial Postings', description: 'See pending/failed sale and reversal GL postings and the posting worker health for the Business.' },
  'sales.financial_postings.retry': { label: 'Retry Sale Financial Posting', description: 'Re-run a pending or failed sale/reversal GL posting. Never changes its amounts or accounting date.' },
  'cash_sessions.expenses.pay': { label: 'Pay Drawer Expenses', description: 'Record the cash payout of an approved POS Drawer expense from an open session.' },
  'expenses.self_approve': { label: 'Owner Expense Self-Approval', description: 'Business Owner exception to approve own POS Drawer/Petty Cash expenses. Needs the Business setting and is never implied by the wildcard.' },
  'uom.view': { label: 'View Units of Measurement', description: 'See the units of measurement and their standard conversions.' },
  'uom.manage': { label: 'Manage Units of Measurement', description: 'Add, edit and deactivate the units of this business. System units stay read-only.' },
  'inventory.pack_state.view': { label: 'View Pack State', description: 'See how the stock of an item splits into sealed packs, loose quantity and prepared packs.' },
  'inventory.pack_state.manage': { label: 'Open Sealed Packs', description: 'Open sealed packs into loose stock at an authorized location.' },
  'inventory.repacking.view': { label: 'View Repacking', description: 'See repack jobs, including their content and packaging values.' },
  'inventory.repacking.execute': { label: 'Run Repacking', description: 'Create, execute, cancel and reverse repack jobs at an authorized location.' }
};

@Component({
  selector: 'app-role-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './role-form.component.html',
  styleUrls: ['./role-form.component.css']
})
export class RoleFormComponent implements OnInit {
  private fb = inject(FormBuilder);
  private router = inject(Router);
  private location = inject(Location);
  private route = inject(ActivatedRoute);
  private roleService = inject(RoleService);

  roleForm!: FormGroup;
  isEditMode = signal(false);
  roleId = signal<number | null>(null);
  isLoading = signal(false);
  error = signal<string | null>(null);
  successMessage = signal<string | null>(null);
  allPermissions = signal<string[]>([]);
  selectedPermissions = signal<Set<string>>(new Set());
  useWildcard = signal(false);

  permissionCategories: PermissionCategory[] = [
    ...HR_PAYROLL_PERMISSION_CATEGORIES,
    {
      name: 'User Management',
      permissions: ['users.view', 'users.create', 'users.edit', 'users.delete', 'users.context_access.manage', 'users.context_access.grant_all']
    },
    {
      name: 'Role Management',
      permissions: ['roles.view', 'roles.create', 'roles.edit', 'roles.delete']
    },
    {
      name: 'Product Management',
      permissions: ['products.view', 'products.view_cost', 'products.create', 'products.edit', 'products.delete']
    },
    {
      name: 'Variation Management',
      permissions: ['variations.view', 'variations.create', 'variations.edit', 'variations.delete']
    },
    {
      name: 'Barcode Labels',
      permissions: ['barcode.view', 'barcode.print', 'barcode.bulk_print', 'barcode.template_manage']
    },
    {
      name: 'Category Management',
      permissions: ['categories.view', 'categories.create', 'categories.edit', 'categories.delete']
    },
    {
      name: 'Inventory',
      permissions: ['inventory.view', 'inventory.create', 'inventory.edit', 'inventory.delete']
    },
    {
      name: 'Units of Measurement',
      permissions: ['uom.view', 'uom.manage']
    },
    {
      name: 'Pack State & Repacking',
      permissions: ['inventory.pack_state.view', 'inventory.pack_state.manage', 'inventory.repacking.view', 'inventory.repacking.execute']
    },
    {
      name: 'Stock Adjustment',
      permissions: [
        'stock_adjustments.view',
        'stock_adjustments.create',
        'stock_adjustments.edit',
        'stock_adjustments.delete',
        'stock_adjustments.approve',
        'stock_adjustments.reject',
        'stock_adjustments.cancel'
      ]
    },
    {
      name: 'Stock Transfer',
      permissions: [
        'stock_transfers.view',
        'stock_transfers.view_all_locations',
        'stock_transfers.create',
        'stock_transfers.edit',
        'stock_transfers.delete',
        'stock_transfers.approve',
        'stock_transfers.cancel',
        'stock_transfers.dispatch',
        'stock_transfers.receive',
        'stock_transfers.reject_receive',
        'stock_transfers.return_create',
        'stock_transfers.transfer_from_any_location'
      ]
    },
    {
      name: 'Stock Count',
      permissions: [
        'StockCount.ViewOwn',
        'StockCount.ViewAll',
        'StockCount.Create',
        'StockCount.Download',
        'StockCount.Print',
        'StockCount.Upload',
        'StockCount.Submit',
        'StockCount.Approve',
        'StockCount.Reject',
        'StockCount.Reopen'
      ]
    },
    {
      name: 'Stock Requisition',
      permissions: [
        'stock_requisitions.view',
        'stock_requisitions.create',
        'stock_requisitions.edit',
        'stock_requisitions.approve',
        'stock_requisitions.reject',
        'stock_requisitions.convert_to_transfer'
      ]
    },
    {
      name: 'Low Stock Alerts',
      permissions: ['low_stock_alerts.view', 'low_stock_alerts.create', 'low_stock_alerts.edit', 'low_stock_alerts.delete']
    },
    {
      name: 'Sales Management',
      permissions: ['sales.view', 'sales.create', 'sales.backdate', 'sales.void', 'sales.refund', 'sales.discount.apply', 'sales.discount.override_limit', 'sales.discount.configure', 'sales.financial_postings.view', 'sales.financial_postings.retry']
    },
    {
      name: 'Cash Sessions',
      permissions: [
        'cash_sessions.view', 'cash_sessions.open', 'cash_sessions.close',
        'cash_sessions.handover.confirm', 'cash_sessions.attachments.upload', 'cash_sessions.attachments.download',
        'cash_sessions.variance.view', 'cash_sessions.variance.approve', 'cash_sessions.variance.retry_posting',
        'cash_sessions.expenses.pay', 'expenses.self_approve'
      ]
    },
    {
      name: 'Purchase Management',
      permissions: ['purchases.view', 'purchases.create', 'purchases.edit', 'purchases.approve', 'purchases.receive']
    },
    {
      name: 'GRN Management',
      permissions: ['grn.view', 'grn.create', 'grn.receive']
    },
    {
      name: 'Customer Management',
      permissions: ['customers.view', 'customers.create', 'customers.edit', 'customers.delete']
    },
    {
      name: 'Supplier Management',
      permissions: ['suppliers.view', 'suppliers.create', 'suppliers.edit', 'suppliers.delete']
    },
    {
      name: 'Dashboards',
      permissions: ['dashboard.sales_finance.view', 'dashboard.inventory_intelligence.view']
    },
    {
      name: 'Reports',
      permissions: ['reports.sales', 'reports.inventory', 'reports.financial', 'reports.export']
    },
    {
      name: 'Settings',
      permissions: ['settings.view', 'settings.edit']
    },
    {
      name: 'Audit Logs',
      permissions: ['audit.view']
    },
    {
      name: 'Outlet Management',
      permissions: ['outlets.view', 'outlets.create', 'outlets.edit', 'outlets.delete', 'outlets.terminals.manage']
    },
    {
      name: 'Warehouse Management',
      permissions: ['warehouses.view', 'warehouses.create', 'warehouses.edit', 'warehouses.delete']
    },
    {
      name: 'Finance - Chart of Accounts',
      permissions: ['accounting.chart_accounts.view', 'accounting.chart_accounts.create', 'accounting.chart_accounts.edit']
    },
    {
      name: 'Finance - Accounting Configuration',
      permissions: ['accounting.configuration.view', 'accounting.configuration.edit']
    },
    {
      name: 'Finance - Journals and Manual Journals',
      permissions: ['accounting.journals.view', 'accounting.journals.create', 'accounting.journals.update', 'accounting.journals.cancel', 'accounting.journals.post', 'accounting.journals.reverse', 'accounting.manual_journals.view', 'accounting.manual_journals.create', 'accounting.manual_journals.update', 'accounting.manual_journals.submit', 'accounting.manual_journals.approve', 'accounting.manual_journals.reject', 'accounting.manual_journals.cancel', 'accounting.manual_journals.post', 'accounting.manual_journals.reverse']
    },
    {
      name: 'Finance - Expenses and Expense Categories',
      permissions: ['accounting.expenses.view', 'accounting.expenses.create', 'accounting.expenses.edit', 'accounting.expenses.submit', 'accounting.expenses.approve', 'accounting.expenses.reject', 'accounting.expenses.cancel', 'accounting.expense_categories.manage']
    },
    {
      name: 'Finance - Supplier Payments and Advances',
      permissions: ['accounting.supplier_payments.view', 'accounting.supplier_payments.create', 'accounting.supplier_payments.submit', 'accounting.supplier_payments.approve', 'accounting.supplier_payments.reject', 'accounting.supplier_payments.cancel', 'accounting.supplier_advances.view', 'accounting.supplier_advances.create', 'accounting.supplier_advances.submit', 'accounting.supplier_advances.approve', 'accounting.supplier_advances.reject', 'accounting.supplier_advances.cancel']
    },
    {
      name: 'Finance - Other Income',
      permissions: ['accounting.other_income.view', 'accounting.other_income.create', 'accounting.other_income.edit', 'accounting.other_income.submit', 'accounting.other_income.approve', 'accounting.other_income.reject', 'accounting.other_income.cancel']
    },
    {
      name: 'Finance - Financial Years, Periods and Opening Balances',
      permissions: ['accounting.financial_years.view', 'accounting.financial_years.manage', 'accounting.financial_years.reopen', 'accounting.periods.view', 'accounting.periods.close', 'accounting.periods.reopen', 'accounting.opening_balances.post', 'accounting.opening_balances.adjust']
    },
    {
      name: 'Finance - Bank Statements, Settlements and Transfers',
      permissions: ['accounting.statement_import.view', 'accounting.statement_import.create', 'accounting.settlements.view', 'accounting.settlements.create', 'accounting.settlements.post', 'accounting.settlements.reverse', 'accounting.liquidity_transfers.view', 'accounting.liquidity_transfers.create', 'accounting.liquidity_transfers.post', 'accounting.liquidity_transfers.reverse']
    },
    {
      name: 'Finance - Bank Reconciliation and Adjustments',
      permissions: ['accounting.reconciliation.view', 'accounting.reconciliation.create', 'accounting.reconciliation.match', 'accounting.reconciliation.unmatch', 'accounting.reconciliation.close', 'accounting.reconciliation.reopen', 'accounting.adjustments.propose', 'accounting.adjustments.approve', 'accounting.adjustments.reverse', 'accounting.exceptions.manage']
    },
    {
      name: 'Finance - Period Close',
      permissions: ['accounting.close.prepare', 'accounting.close.approve', 'accounting.close.execute', 'accounting.close.override_exception', 'accounting.close.audit.view']
    },
    {
      name: 'Finance - Reports',
      permissions: ['accounting.reports.general_ledger', 'accounting.reports.trial_balance', 'accounting.reports.profit_loss', 'accounting.reports.balance_sheet', 'accounting.reports.cash_book', 'accounting.reports.cash_flow', 'accounting.reports.profitability', 'accounting.reports.expense', 'accounting.reports.income', 'accounting.reports.cogs', 'accounting.reports.inventory_valuation', 'accounting.reports.cash_closing', 'accounting.reports.ap_subledger', 'accounting.reports.ap_aging', 'accounting.reports.supplier_statement', 'accounting.reports.ap_reconciliation', 'accounting.reports.vat']
    }
  ];

  /** Categories filtered to only include permissions the backend supports. */
  get visiblePermissionCategories(): PermissionCategory[] {
    const supported = this.allPermissions();
    if (supported.length === 0) {
      // API permissions not yet loaded — show all to avoid empty UI
      return this.permissionCategories;
    }
    return this.permissionCategories
      .map(cat => ({
        name: cat.name,
        permissions: cat.permissions.filter(p => supported.includes(p))
      }))
      .filter(cat => cat.permissions.length > 0);
  }

  ngOnInit(): void {
    this.initializeForm();
    this.loadPermissions();

    // Check if edit mode
    this.route.params.subscribe(params => {
      if (params['id']) {
        this.isEditMode.set(true);
        this.roleId.set(+params['id']);
        this.loadRole(+params['id']);
      }
    });
  }

  initializeForm(): void {
    this.roleForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2)]]
    });
  }

  loadPermissions(): void {
    this.roleService.getAllPermissions().subscribe({
      next: (permissions) => {
        this.allPermissions.set(permissions);
      },
      error: (err) => {
        console.error('Error loading permissions:', err);
      }
    });
  }

  loadRole(id: number): void {
    this.isLoading.set(true);
    this.roleService.getRoleById(id).subscribe({
      next: (role) => {
        this.roleForm.patchValue({
          name: role.name
        });

        // Check if wildcard permission
        if (role.permissions.includes('*')) {
          this.useWildcard.set(true);
          this.selectedPermissions.set(new Set(['*']));
        } else {
          this.selectedPermissions.set(new Set(role.permissions));
        }

        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Error loading role:', err);
        this.error.set('Failed to load role');
        this.isLoading.set(false);
      }
    });
  }

  toggleWildcard(): void {
    this.useWildcard.set(!this.useWildcard());
    if (this.useWildcard()) {
      this.selectedPermissions.set(new Set(['*']));
    } else {
      this.selectedPermissions.set(new Set());
    }
  }

  togglePermission(permission: string): void {
    if (this.useWildcard()) return;

    const current = new Set(this.selectedPermissions());
    if (current.has(permission)) {
      current.delete(permission);
    } else {
      current.add(permission);
    }
    this.selectedPermissions.set(current);
  }

  toggleCategory(category: PermissionCategory): void {
    if (this.useWildcard()) return;

    const current = new Set(this.selectedPermissions());
    const allSelected = category.permissions.every(p => current.has(p));

    if (allSelected) {
      category.permissions.forEach(p => current.delete(p));
    } else {
      category.permissions.forEach(p => current.add(p));
    }
    this.selectedPermissions.set(current);
  }

  isCategorySelected(category: PermissionCategory): boolean {
    return category.permissions.every(p => this.selectedPermissions().has(p));
  }

  isCategoryPartiallySelected(category: PermissionCategory): boolean {
    const selected = category.permissions.filter(p => this.selectedPermissions().has(p));
    return selected.length > 0 && selected.length < category.permissions.length;
  }

  getSelectedPermissionsCount(category: PermissionCategory): number {
    return category.permissions.filter(p => this.selectedPermissions().has(p)).length;
  }

  onSubmit(): void {
    if (this.roleForm.invalid) {
      this.roleForm.markAllAsTouched();
      return;
    }

    if (this.selectedPermissions().size === 0) {
      this.error.set('Please select at least one permission or enable full access');
      return;
    }

    this.isLoading.set(true);
    this.error.set(null);

    if (this.isEditMode() && this.roleId()) {
      this.updateRole();
    } else {
      this.createRole();
    }
  }

  createRole(): void {
    const dto: CreateRoleDto = {
      name: this.roleForm.value.name,
      permissions: Array.from(this.selectedPermissions())
    };

    this.roleService.createRole(dto).subscribe({
      next: () => {
        this.successMessage.set('Role created successfully!');
        setTimeout(() => {
          this.location.back();
        }, 1500);
      },
      error: (err) => {
        console.error('Error creating role:', err);
        this.error.set(err.error?.error || err.error?.message || 'Failed to create role');
        this.isLoading.set(false);
      }
    });
  }

  updateRole(): void {
    const dto: UpdateRoleDto = {
      name: this.roleForm.value.name,
      permissions: Array.from(this.selectedPermissions())
    };

    this.roleService.updateRole(this.roleId()!, dto).subscribe({
      next: () => {
        this.successMessage.set('Role updated successfully!');
        setTimeout(() => {
          this.location.back();
        }, 1500);
      },
      error: (err) => {
        console.error('Error updating role:', err);
        this.error.set(err.error?.error || err.error?.message || 'Failed to update role');
        this.isLoading.set(false);
      }
    });
  }

  cancel(): void {
    this.location.back();
  }

  getPermissionDisplayName(permission: string): string {
    const expenseCategoryHints: Record<string, string> = {
      'accounting.expenses.view': 'accounting.expenses.view (Expenses and Expense Categories list)',
      'accounting.expenses.edit': 'accounting.expenses.edit (edit Expenses, create/edit Expense Categories)'
    };
    if (expenseCategoryHints[permission]) {
      return expenseCategoryHints[permission];
    }

    if (permission === 'stock_transfers.view_all_locations') {
      return 'View Transfers Across All Locations';
    }

    if (permission === 'stock_transfers.transfer_from_any_location') {
      return 'Transfer From Any Location';
    }

    return PERMISSION_DETAILS[permission]?.label ?? permission;
  }

  getPermissionDescription(permission: string): string {
    return PERMISSION_DETAILS[permission]?.description ?? '';
  }
}
