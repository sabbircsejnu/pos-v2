import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { AppDatePipe } from '../../pipes/app-date.pipe';
import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { BusinessService } from '../../services/business.service';
import { AuthService } from '../../services/auth.service';
import { UiBadgeComponent } from '../../components/shared/ui/ui-badge.component';
import { UiButtonComponent } from '../../components/shared/ui/ui-button.component';
import { BusinessReceiptFooterFieldsComponent } from './business-receipt-footer-fields.component';
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
  imports: [AppDatePipe, UiDateInputDirective, CommonModule, FormsModule, BusinessReceiptFooterFieldsComponent, UiButtonComponent, UiBadgeComponent],
  templateUrl: './business-setup.component.html'
})
export class BusinessSetupComponent implements OnInit {
  private readonly authService = inject(AuthService);

  businessId = signal<number>(0);
  business = signal<BusinessSummaryDto | null>(null);
  activeTab = signal<SetupTab>('info');
  isLoading = signal(false);

  // Settings tab: platform-controlled receipt footer
  showPoweredBy = signal(false);
  poweredByText = signal('');
  receiptFooterSaving = signal(false);

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
          this.showPoweredBy.set(!!res.data.showPoweredByOnReceipt);
          this.poweredByText.set(res.data.poweredByText ?? '');
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
        this.features.set(this.withHrPayrollCapabilities(res.data ?? []));
        this.settingsLoading.set(false);
      },
      error: (err) => {
        this.settingsLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  /** From the last saved values, not the form: what receipts print right now. */
  receiptFooterPrints(): boolean {
    const saved = this.business();
    return !!saved?.showPoweredByOnReceipt && !!saved.poweredByText?.trim();
  }

  get canManagePoweredBy(): boolean {
    return this.authService.isSuperAdmin();
  }

  saveReceiptFooter(): void {
    if (this.receiptFooterSaving() || !this.canManagePoweredBy) return;
    this.receiptFooterSaving.set(true);
    this.businessService.updateReceiptFooter(this.businessId(), {
      showPoweredByOnReceipt: this.showPoweredBy(),
      poweredByText: this.poweredByText().trim() || null
    }).subscribe({
      next: (res) => {
        this.receiptFooterSaving.set(false);
        this.business.set(res.data ?? null);
        this.showPoweredBy.set(!!res.data?.showPoweredByOnReceipt);
        this.poweredByText.set(res.data?.poweredByText ?? '');
        this.alertService.success(this.receiptFooterPrints()
          ? 'Receipt footer saved. It now prints on receipts.'
          : 'Receipt footer saved. It does not print on receipts.');
      },
      error: (err) => {
        this.receiptFooterSaving.set(false);
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
    if (this.settingsSubmitting()) return;
    if (!this.canToggleFeature(feature)) {
      this.alertService.error(this.featureToggleMessage(feature));
      return;
    }

    const previous = this.features();
    const updated = this.features().map(f =>
      f.featureKey === feature.featureKey ? { ...f, isEnabled: !f.isEnabled } : f
    );
    this.features.set(updated);
    this.businessService.upsertFeatureSettings(this.businessId(), {
      features: updated.map(f => ({ featureKey: f.featureKey, isEnabled: f.isEnabled, limitValue: f.limitValue }))
    }).subscribe({
      next: (res) => {
        this.features.set(this.withHrPayrollCapabilities(res.data ?? []));
        this.alertService.success('Feature settings saved');
      },
      error: (err) => {
        this.features.set(previous);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  canToggleFeature(feature: BusinessFeatureSettingDto): boolean {
    const key = feature.featureKey.trim().toLowerCase();
    const hrEnabled = this.features().some(item =>
      item.featureKey.toLowerCase() === 'hr.enabled' && item.isEnabled);
    const payrollEnabled = this.features().some(item =>
      item.featureKey.toLowerCase() === 'payroll.enabled' && item.isEnabled);

    if (key === 'payroll.enabled' && !feature.isEnabled && !hrEnabled) return false;
    if (key === 'hr.enabled' && feature.isEnabled && payrollEnabled) return false;
    return true;
  }

  featureToggleMessage(feature: BusinessFeatureSettingDto): string {
    const key = feature.featureKey.trim().toLowerCase();
    if (key === 'payroll.enabled' && !feature.isEnabled) {
      return 'Enable HR before enabling Payroll.';
    }
    if (key === 'hr.enabled' && feature.isEnabled) {
      return 'Disable Payroll before disabling HR.';
    }
    return 'This feature cannot be changed right now.';
  }

  private withHrPayrollCapabilities(features: BusinessFeatureSettingDto[]): BusinessFeatureSettingDto[] {
    const result = [...features];
    for (const featureKey of ['hr.enabled', 'payroll.enabled']) {
      if (!result.some(feature => feature.featureKey.toLowerCase() === featureKey)) {
        result.push({ featureKey, isEnabled: false });
      }
    }
    return result;
  }

  setSubscriptionField(field: keyof UpdateBusinessSubscriptionDto, value: string | number | undefined): void {
    this.subscriptionForm.set({ ...this.subscriptionForm(), [field]: value });
  }
}
