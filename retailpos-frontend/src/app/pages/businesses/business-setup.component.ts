import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { BusinessService } from '../../services/business.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import {
  BusinessSummaryDto,
  BusinessFeatureSettingDto,
  UpdateBusinessSubscriptionDto,
} from '../../models/business.model';
import { Outlet, CreateOutletRequest } from '../../models/outlet.model';
import { Warehouse, CreateWarehouseRequest } from '../../models/warehouse.model';

type SetupTab = 'info' | 'owner' | 'outlets' | 'warehouses' | 'settings';

@Component({
  selector: 'app-business-setup',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './business-setup.component.html'
})
export class BusinessSetupComponent implements OnInit {
  businessId = signal<number>(0);
  business = signal<BusinessSummaryDto | null>(null);
  activeTab = signal<SetupTab>('info');
  isLoading = signal(false);

  // Owner tab
  resetToken = signal<string | null>(null);
  isResettingAccess = signal(false);

  // Outlets tab
  outlets = signal<Outlet[]>([]);
  outletsLoading = signal(false);
  showOutletForm = signal(false);
  editingOutlet = signal<Outlet | null>(null);
  outletForm = signal<CreateOutletRequest>({ name: '', address: '' });
  outletSubmitting = signal(false);

  // Warehouses tab
  warehouses = signal<Warehouse[]>([]);
  warehousesLoading = signal(false);
  showWarehouseForm = signal(false);
  editingWarehouse = signal<Warehouse | null>(null);
  warehouseForm = signal<CreateWarehouseRequest>({ name: '', address: '' });
  warehouseSubmitting = signal(false);

  // Settings tab
  features = signal<BusinessFeatureSettingDto[]>([]);
  settingsLoading = signal(false);
  subscriptionForm = signal<UpdateBusinessSubscriptionDto>({});
  settingsSubmitting = signal(false);

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private businessService: BusinessService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    const id = Number(this.route.snapshot.paramMap.get('id'));
    if (!id) {
      this.router.navigate(['/businesses']);
      return;
    }
    this.businessId.set(id);
    this.loadBusiness();
  }

  loadBusiness(): void {
    this.isLoading.set(true);
    this.businessService.getBusinessById(this.businessId()).subscribe({
      next: (res) => {
        this.business.set(res.data ?? null);
        if (res.data) {
          this.subscriptionForm.set({
            subscriptionPlan: res.data.subscriptionPlan,
            trialEndsAt: res.data.trialEndsAt,
            subscriptionEndsAt: res.data.subscriptionEndsAt,
            maxOutlets: res.data.maxOutlets,
            maxUsers: res.data.maxUsers,
          });
        }
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  setTab(tab: string): void {
    this.activeTab.set(tab as SetupTab);
    if (tab === 'outlets' && this.outlets().length === 0) {
      this.loadOutlets();
    } else if (tab === 'warehouses' && this.warehouses().length === 0) {
      this.loadWarehouses();
    } else if (tab === 'settings' && this.features().length === 0) {
      this.loadFeatures();
    }
  }

  back(): void {
    this.router.navigate(['/businesses']);
  }

  toggleBusinessStatus(): void {
    const b = this.business();
    if (!b) return;
    const next = !b.isActive;
    this.alertService.confirm(
      `Are you sure you want to ${next ? 'activate' : 'deactivate'} this business?`,
      () => {
        this.businessService.updateBusinessStatus(b.businessId, { isActive: next }).subscribe({
          next: (res) => {
            this.business.set(res.data ?? null);
            this.alertService.success(`Business ${next ? 'activated' : 'deactivated'}`);
          },
          error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
        });
      }
    );
  }

  // ── Owner ──────────────────────────────────────────────────────────────────

  resetOwnerAccess(): void {
    this.alertService.confirm('Send a password reset invitation to the Business Owner?', () => {
      this.isResettingAccess.set(true);
      this.businessService.resetOwnerAccess(this.businessId()).subscribe({
        next: (res) => {
          this.isResettingAccess.set(false);
          this.resetToken.set(res.data?.invitationToken ?? null);
          this.alertService.success('Invitation issued. Share the token with the owner.');
        },
        error: (err) => {
          this.isResettingAccess.set(false);
          this.alertService.error(this.errorHandler.extractErrorMessage(err));
        }
      });
    });
  }

  // ── Outlets ────────────────────────────────────────────────────────────────

  loadOutlets(): void {
    this.outletsLoading.set(true);
    this.businessService.getSetupOutlets(this.businessId()).subscribe({
      next: (res) => {
        this.outlets.set(res.data ?? []);
        this.outletsLoading.set(false);
      },
      error: (err) => {
        this.outletsLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  openCreateOutlet(): void {
    this.editingOutlet.set(null);
    this.outletForm.set({ name: '', address: '' });
    this.showOutletForm.set(true);
  }

  openEditOutlet(outlet: Outlet): void {
    this.editingOutlet.set(outlet);
    this.outletForm.set({ name: outlet.name, address: outlet.address, contactNumber: outlet.contactNumber });
    this.showOutletForm.set(true);
  }

  cancelOutletForm(): void {
    this.showOutletForm.set(false);
    this.editingOutlet.set(null);
  }

  submitOutlet(): void {
    if (this.outletSubmitting()) return;
    const form = this.outletForm();
    if (!form.name.trim() || !form.address.trim()) {
      this.alertService.error('Name and address are required');
      return;
    }

    this.outletSubmitting.set(true);
    const editing = this.editingOutlet();

    const obs = editing
      ? this.businessService.updateSetupOutlet(this.businessId(), editing.id, form)
      : this.businessService.createSetupOutlet(this.businessId(), form);

    obs.subscribe({
      next: () => {
        this.outletSubmitting.set(false);
        this.showOutletForm.set(false);
        this.editingOutlet.set(null);
        this.alertService.success(editing ? 'Outlet updated' : 'Outlet created');
        this.loadOutlets();
      },
      error: (err) => {
        this.outletSubmitting.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  deleteOutlet(outlet: Outlet): void {
    this.alertService.confirm(`Delete outlet "${outlet.name}"? This cannot be undone.`, () => {
      this.businessService.deleteSetupOutlet(this.businessId(), outlet.id).subscribe({
        next: () => {
          this.alertService.success('Outlet deleted');
          this.loadOutlets();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  setOutletFormField(field: keyof CreateOutletRequest, value: string): void {
    this.outletForm.set({ ...this.outletForm(), [field]: value });
  }

  // ── Warehouses ─────────────────────────────────────────────────────────────

  loadWarehouses(): void {
    this.warehousesLoading.set(true);
    this.businessService.getSetupWarehouses(this.businessId()).subscribe({
      next: (res) => {
        this.warehouses.set(res.data ?? []);
        this.warehousesLoading.set(false);
      },
      error: (err) => {
        this.warehousesLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  openCreateWarehouse(): void {
    this.editingWarehouse.set(null);
    this.warehouseForm.set({ name: '', address: '' });
    this.showWarehouseForm.set(true);
  }

  openEditWarehouse(warehouse: Warehouse): void {
    this.editingWarehouse.set(warehouse);
    this.warehouseForm.set({ name: warehouse.name, address: warehouse.address, capacity: warehouse.capacity });
    this.showWarehouseForm.set(true);
  }

  cancelWarehouseForm(): void {
    this.showWarehouseForm.set(false);
    this.editingWarehouse.set(null);
  }

  submitWarehouse(): void {
    if (this.warehouseSubmitting()) return;
    const form = this.warehouseForm();
    if (!form.name.trim() || !form.address.trim()) {
      this.alertService.error('Name and address are required');
      return;
    }

    this.warehouseSubmitting.set(true);
    const editing = this.editingWarehouse();

    const obs = editing
      ? this.businessService.updateSetupWarehouse(this.businessId(), editing.id, form)
      : this.businessService.createSetupWarehouse(this.businessId(), form);

    obs.subscribe({
      next: () => {
        this.warehouseSubmitting.set(false);
        this.showWarehouseForm.set(false);
        this.editingWarehouse.set(null);
        this.alertService.success(editing ? 'Warehouse updated' : 'Warehouse created');
        this.loadWarehouses();
      },
      error: (err) => {
        this.warehouseSubmitting.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  deleteWarehouse(warehouse: Warehouse): void {
    this.alertService.confirm(`Delete warehouse "${warehouse.name}"? This cannot be undone.`, () => {
      this.businessService.deleteSetupWarehouse(this.businessId(), warehouse.id).subscribe({
        next: () => {
          this.alertService.success('Warehouse deleted');
          this.loadWarehouses();
        },
        error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
      });
    });
  }

  setWarehouseFormField(field: keyof CreateWarehouseRequest, value: string | number | undefined): void {
    this.warehouseForm.set({ ...this.warehouseForm(), [field]: value });
  }

  // ── Settings ───────────────────────────────────────────────────────────────

  loadFeatures(): void {
    this.settingsLoading.set(true);
    this.businessService.getFeatureSettings(this.businessId()).subscribe({
      next: (res) => {
        this.features.set(res.data ?? []);
        this.settingsLoading.set(false);
      },
      error: (err) => {
        this.settingsLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  saveSubscription(): void {
    if (this.settingsSubmitting()) return;
    this.settingsSubmitting.set(true);
    this.businessService.updateSubscription(this.businessId(), this.subscriptionForm()).subscribe({
      next: (res) => {
        this.settingsSubmitting.set(false);
        this.business.set(res.data ?? null);
        this.alertService.success('Subscription settings saved');
      },
      error: (err) => {
        this.settingsSubmitting.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  toggleFeature(feature: BusinessFeatureSettingDto): void {
    const updated = this.features().map(f =>
      f.featureKey === feature.featureKey ? { ...f, isEnabled: !f.isEnabled } : f
    );
    this.features.set(updated);
    this.businessService.upsertFeatureSettings(this.businessId(), {
      features: updated.map(f => ({ featureKey: f.featureKey, isEnabled: f.isEnabled, limitValue: f.limitValue }))
    }).subscribe({
      next: () => this.alertService.success('Feature settings saved'),
      error: (err) => {
        this.features.set(this.features()); // revert optimistic
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  setSubscriptionField(field: keyof UpdateBusinessSubscriptionDto, value: string | number | undefined): void {
    this.subscriptionForm.set({ ...this.subscriptionForm(), [field]: value });
  }
}
