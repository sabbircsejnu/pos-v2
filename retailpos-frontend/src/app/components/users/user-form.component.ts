import { Component, OnInit, signal, inject, computed } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { Router, ActivatedRoute } from '@angular/router';
import { UserService } from '../../services/user.service';
import { RoleService } from '../../services/role.service';
import { OutletService } from '../../services/outlet.service';
import { WarehouseService } from '../../services/warehouse.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreateUserDto, UpdateUserDto, InventoryLocationAccessScope } from '../../models/user.model';
import { Role } from '../../models/role.model';
import { Outlet } from '../../models/outlet.model';
import { Warehouse } from '../../models/warehouse.model';

@Component({
  selector: 'app-user-form',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './user-form.component.html',
  styleUrls: ['./user-form.component.css']
})
export class UserFormComponent implements OnInit {
  private fb = inject(FormBuilder);
  private router = inject(Router);
  private location = inject(Location);
  private route = inject(ActivatedRoute);
  private userService = inject(UserService);
  private roleService = inject(RoleService);
  private outletService = inject(OutletService);
  private warehouseService = inject(WarehouseService);
  private errorHandler = inject(ErrorHandlerService);

  userForm!: FormGroup;
  isEditMode = signal(false);
  userId = signal<number | null>(null);
  isLoading = signal(false);
  error = signal<string | null>(null);
  successMessage = signal<string | null>(null);
  roles = signal<Role[]>([]);
  outlets = signal<Outlet[]>([]);
  warehouses = signal<Warehouse[]>([]);
  showPassword = signal(false);
  showConfirmPassword = signal(false);
  selectedInventoryScope = signal<InventoryLocationAccessScope>('assigned_only');
  editingBusinessId = signal<number | null>(null);

  isAssignedOnlyScope = computed(() => this.selectedInventoryScope() === 'assigned_only');
  isSpecificScope = computed(() => this.selectedInventoryScope() === 'specific_locations');
  isAllScope = computed(() => this.selectedInventoryScope() === 'all_locations');

  ngOnInit(): void {
    this.initializeForm();
    this.loadRoles();
    this.loadOutlets();
    this.loadWarehouses();
    
    // Watch for role changes to update signal and validators
    if (this.userForm) {
      this.userForm.get('inventoryLocationAccessScope')?.valueChanges.subscribe((value: InventoryLocationAccessScope) => {
        this.selectedInventoryScope.set(value || 'assigned_only');
        this.updateFieldValidators();
      });

      this.userForm.get('roleId')?.valueChanges.subscribe(() => {
        this.updateFieldValidators();
      });
    }
    
    // Check if edit mode
    this.route.params.subscribe(params => {
      if (params['id']) {
        this.isEditMode.set(true);
        this.userId.set(+params['id']);
        this.loadUser(+params['id']);
      }
    });
  }

  initializeForm(): void {
    this.userForm = this.fb.group({
      name: ['', [Validators.required, Validators.minLength(2)]],
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(6)]],
      confirmPassword: ['', [Validators.required]],
      roleId: [null, Validators.required],
      outletId: [null],
      outletIds: [[]],
      warehouseIds: [[]], // Multi-select for warehouses
      defaultLocationType: ['outlet'],
      defaultLocationId: [null],
      inventoryLocationAccessScope: ['assigned_only', Validators.required],
      isActive: [true]
    });
  }

  updateFieldValidators(): void {
    const outletControl = this.userForm.get('outletId');
    const outletIdsControl = this.userForm.get('outletIds');
    const warehouseControl = this.userForm.get('warehouseIds');

    outletControl?.clearValidators();
    outletIdsControl?.clearValidators();
    warehouseControl?.clearValidators();

    if (this.isAllScope()) {
      outletControl?.setValue(null);

      outletIdsControl?.setValue([]);

      if (!this.isWarehouseRequiredForSelectedRole()) {
        warehouseControl?.setValue([]);
      }
    }

    outletControl?.updateValueAndValidity();
    outletIdsControl?.updateValueAndValidity();

    warehouseControl?.updateValueAndValidity();
  }

  loadRoles(): void {
    this.roleService.getAllRoles().subscribe({
      next: (roles) => {
        this.roles.set(roles);
      },
      error: (err) => {
        console.error('Error loading roles:', err);
        this.error.set('Failed to load roles');
      }
    });
  }

  loadOutlets(): void {
    this.outletService.getAllOutlets().subscribe({
      next: (response) => {
        // Extract data array from response
        const outletsData = response?.data || [];
        this.outlets.set(outletsData);
      },
      error: (err) => {
        console.error('Error loading outlets:', err);
        this.outlets.set([]);
        // Don't show error in main message as it's optional data
      }
    });
  }

  loadWarehouses(): void {
    this.warehouseService.getAllWarehouses().subscribe({
      next: (response) => {
        // Extract data array from response
        const warehousesData = response?.data || [];
        this.warehouses.set(warehousesData);
      },
      error: (err) => {
        console.error('Error loading warehouses:', err);
        this.warehouses.set([]);
        // Don't show error in main message as it's optional data
      }
    });
  }

  loadUser(id: number): void {
    this.isLoading.set(true);
    this.userService.getUserById(id).subscribe({
      next: (user) => {
        // Remove password validators in edit mode
        this.userForm.get('password')?.clearValidators();
        this.userForm.get('confirmPassword')?.clearValidators();
        this.userForm.get('password')?.updateValueAndValidity();
        this.userForm.get('confirmPassword')?.updateValueAndValidity();
        
        const warehouseIds = user.warehouseAssignments?.map(w => w.warehouseId) || [];
        this.editingBusinessId.set(user.businessId ?? null);
        
        this.userForm.patchValue({
          name: user.name,
          email: user.email,
          roleId: user.roleId,
          outletId: user.outletId,
          outletIds: user.outletAssignments?.map(o => o.outletId) || [],
          warehouseIds: warehouseIds,
          defaultLocationType: user.defaultLocationType || (user.outletId ? 'outlet' : (warehouseIds.length > 0 ? 'warehouse' : 'outlet')),
          defaultLocationId: user.defaultLocationId || user.outletId || warehouseIds[0] || null,
          inventoryLocationAccessScope: user.inventoryLocationAccessScope || 'assigned_only',
          isActive: user.isActive
        });

        this.selectedInventoryScope.set((user.inventoryLocationAccessScope as InventoryLocationAccessScope) || 'assigned_only');
        this.updateFieldValidators();
        this.isLoading.set(false);
      },
      error: (err) => {
        console.error('Error loading user:', err);
        this.error.set('Failed to load user');
        this.isLoading.set(false);
      }
    });
  }

  onSubmit(): void {
    if (this.userForm.invalid) {
      Object.keys(this.userForm.controls).forEach(key => {
        this.userForm.get(key)?.markAsTouched();
      });
      return;
    }

    // Validate passwords match in create mode
    if (!this.isEditMode()) {
      const password = this.userForm.get('password')?.value;
      const confirmPassword = this.userForm.get('confirmPassword')?.value;
      if (password !== confirmPassword) {
        this.error.set('Passwords do not match');
        return;
      }
    }

    const outletIds: number[] = this.userForm.get('outletIds')?.value || [];
    const warehouseIds: number[] = this.userForm.get('warehouseIds')?.value || [];
    const defaultLocationType: 'outlet' | 'warehouse' = this.userForm.get('defaultLocationType')?.value || 'outlet';
    const defaultLocationId: number | null = this.userForm.get('defaultLocationId')?.value;
    const outletId: number | null = defaultLocationType === 'outlet' ? defaultLocationId : null;
    const accessScope: InventoryLocationAccessScope = this.userForm.get('inventoryLocationAccessScope')?.value || 'assigned_only';

    if (!defaultLocationId) {
      this.error.set('Default inventory location is required.');
      return;
    }

    if (accessScope === 'assigned_only' && defaultLocationType === 'warehouse' && warehouseIds.length === 0) {
      this.error.set('Assigned Location Only requires at least one assigned warehouse when default location type is warehouse.');
      return;
    }

    if (accessScope === 'specific_locations' && outletIds.length === 0 && warehouseIds.length === 0) {
      this.error.set('Specific Outlets/Warehouses requires at least one outlet or warehouse selection.');
      return;
    }

    if (accessScope === 'specific_locations') {
      const defaultInAssigned = defaultLocationType === 'outlet'
        ? outletIds.includes(defaultLocationId)
        : warehouseIds.includes(defaultLocationId);

      if (!defaultInAssigned) {
        this.error.set('For this scope, default inventory location should be one of the assigned locations.');
        return;
      }
    }

    this.isLoading.set(true);
    this.error.set(null);

    if (this.isEditMode() && this.userId()) {
      this.updateUser();
    } else {
      this.createUser();
    }
  }

  private isWarehouseRequiredForSelectedRole(): boolean {
    const roleName = this.getSelectedRoleName();
    return roleName.toLowerCase() === 'warehousemanager';
  }

  private normalizeAssignmentCollections(
    accessScope: InventoryLocationAccessScope,
    outletIds: number[],
    warehouseIds: number[]
  ): { normalizedOutletIds?: number[]; normalizedWarehouseIds?: number[] } {
    const normalizedOutletIds = [...new Set(outletIds.filter(id => id > 0))];
    const normalizedWarehouseIds = [...new Set(warehouseIds.filter(id => id > 0))];

    if (accessScope === 'all_locations') {
      return {
        normalizedOutletIds: undefined,
        normalizedWarehouseIds: this.isWarehouseRequiredForSelectedRole()
          ? (normalizedWarehouseIds.length > 0 ? normalizedWarehouseIds : undefined)
          : undefined
      };
    }

    return {
      normalizedOutletIds: normalizedOutletIds.length > 0 ? normalizedOutletIds : undefined,
      normalizedWarehouseIds: normalizedWarehouseIds.length > 0 ? normalizedWarehouseIds : undefined
    };
  }

  private getSelectedRoleName(): string {
    const selectedRoleId = Number(this.userForm.get('roleId')?.value);
    if (!selectedRoleId) {
      return '';
    }

    return this.roles().find(r => r.id === selectedRoleId)?.name ?? '';
  }

  createUser(): void {
    const formValue = this.userForm.value;
    const assignmentPayload = this.normalizeAssignmentCollections(
      formValue.inventoryLocationAccessScope,
      formValue.outletIds || [],
      formValue.warehouseIds || []
    );

    const dto: CreateUserDto = {
      name: formValue.name,
      email: formValue.email,
      password: formValue.password,
      roleId: formValue.roleId,
      outletId: formValue.defaultLocationType === 'outlet' ? (formValue.defaultLocationId || undefined) : undefined,
      outletIds: assignmentPayload.normalizedOutletIds,
      warehouseIds: assignmentPayload.normalizedWarehouseIds,
      defaultLocationType: formValue.defaultLocationType,
      defaultLocationId: formValue.defaultLocationId || undefined,
      inventoryLocationAccessScope: formValue.inventoryLocationAccessScope,
      isActive: formValue.isActive
    };

    this.userService.createUser(dto).subscribe({
      next: (user) => {
        this.successMessage.set('User created successfully!');
        setTimeout(() => {
          this.location.back();
        }, 1500);
      },
      error: (err) => {
        console.error('Error creating user:', err);
        this.error.set(this.errorHandler.getUserFriendlyMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  updateUser(): void {
    const formValue = this.userForm.value;
    const assignmentPayload = this.normalizeAssignmentCollections(
      formValue.inventoryLocationAccessScope,
      formValue.outletIds || [],
      formValue.warehouseIds || []
    );

    const dto: UpdateUserDto = {
      name: formValue.name,
      email: formValue.email,
      roleId: formValue.roleId,
      businessId: this.editingBusinessId() || undefined,
      outletId: formValue.defaultLocationType === 'outlet' ? (formValue.defaultLocationId || undefined) : undefined,
      outletIds: assignmentPayload.normalizedOutletIds,
      warehouseIds: assignmentPayload.normalizedWarehouseIds,
      defaultLocationType: formValue.defaultLocationType,
      defaultLocationId: formValue.defaultLocationId || undefined,
      inventoryLocationAccessScope: formValue.inventoryLocationAccessScope,
      isActive: formValue.isActive
    };

    this.userService.updateUser(this.userId()!, dto).subscribe({
      next: (user) => {
        this.successMessage.set('User updated successfully!');
        setTimeout(() => {
          this.location.back();
        }, 1500);
      },
      error: (err) => {
        console.error('Error updating user:', err);
        this.error.set(this.errorHandler.getUserFriendlyMessage(err));
        this.isLoading.set(false);
      }
    });
  }

  cancel(): void {
    this.location.back();
  }

  togglePasswordVisibility(): void {
    this.showPassword.set(!this.showPassword());
  }

  toggleConfirmPasswordVisibility(): void {
    this.showConfirmPassword.set(!this.showConfirmPassword());
  }

  toggleWarehouseSelection(warehouseId: number): void {
    const warehouseIds = this.userForm.get('warehouseIds')?.value || [];
    const index = warehouseIds.indexOf(warehouseId);
    
    if (index > -1) {
      warehouseIds.splice(index, 1);
    } else {
      warehouseIds.push(warehouseId);
    }
    
    this.userForm.get('warehouseIds')?.setValue([...warehouseIds]);
    this.userForm.get('warehouseIds')?.markAsTouched();
  }

  isWarehouseSelected(warehouseId: number): boolean {
    const warehouseIds = this.userForm.get('warehouseIds')?.value || [];
    return warehouseIds.includes(warehouseId);
  }

  toggleOutletSelection(outletId: number): void {
    const outletIds = this.userForm.get('outletIds')?.value || [];
    const index = outletIds.indexOf(outletId);

    if (index > -1) {
      outletIds.splice(index, 1);
    } else {
      outletIds.push(outletId);
    }

    this.userForm.get('outletIds')?.setValue([...outletIds]);
    this.userForm.get('outletIds')?.markAsTouched();
  }

  isOutletSelected(outletId: number): boolean {
    const outletIds = this.userForm.get('outletIds')?.value || [];
    return outletIds.includes(outletId);
  }

  onDefaultLocationTypeChange(): void {
    this.userForm.get('defaultLocationId')?.setValue(null);
  }

  getFieldError(fieldName: string): string {
    const field = this.userForm.get(fieldName);
    if (field?.hasError('required')) return `${fieldName} is required`;
    if (field?.hasError('email')) return 'Invalid email format';
    if (field?.hasError('minlength')) {
      const minLength = field.errors?.['minlength'].requiredLength;
      return `Minimum ${minLength} characters required`;
    }
    return '';
  }

}
