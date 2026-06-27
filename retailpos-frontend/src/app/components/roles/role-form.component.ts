import { Component, OnInit, signal, inject } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { RoleService } from '../../services/role.service';
import { CreateRoleDto, UpdateRoleDto } from '../../models/role.model';

interface PermissionCategory {
  name: string;
  permissions: string[];
}

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
    {
      name: 'User Management',
      permissions: ['users.view', 'users.create', 'users.edit', 'users.delete']
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
      name: 'Stock Adjustment',
      permissions: [
        'stock_adjustments.view',
        'stock_adjustments.create',
        'stock_adjustments.edit',
        'stock_adjustments.delete',
        'stock_adjustments.approve',
        'stock_adjustments.reject'
      ]
    },
    {
      name: 'Stock Transfer',
      permissions: [
        'stock_transfers.view',
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
      permissions: ['sales.view', 'sales.create', 'sales.void', 'sales.refund']
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
      permissions: ['outlets.view', 'outlets.create', 'outlets.edit', 'outlets.delete']
    },
    {
      name: 'Warehouse Management',
      permissions: ['warehouses.view', 'warehouses.create', 'warehouses.edit', 'warehouses.delete']
    },
    {
      name: 'Accounts Management',
      permissions: ['accounts.view', 'accounts.create', 'accounts.edit', 'accounts.delete']
    },
    {
      name: 'Transactions Management',
      permissions: ['transactions.view', 'transactions.create', 'transactions.edit', 'transactions.delete']
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
    if (permission === 'stock_transfers.transfer_from_any_location') {
      return 'Transfer From Any Location';
    }

    return permission;
  }
}
