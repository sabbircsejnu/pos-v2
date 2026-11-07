import { Component, DestroyRef, Injector, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { forkJoin } from 'rxjs';
import { StockAdjustmentService } from '../../services/stock-adjustment.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { ProductImageService } from '../../services/product-image.service';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { SettingsService } from '../../services/settings.service';
import {
  CreateStockAdjustmentBatchDto,
  ADJUSTMENT_REASONS,
  UpdateStockAdjustmentDto,
  StockAdjustmentDto,
  StockAdjustmentCreateAction
} from '../../models/stock-adjustment.model';
import { VariantSearchItem, VariantSearchPickerComponent } from '../../components/variant-search-picker/variant-search-picker.component';
import { findVariantIndexById, isVariantAlreadyInItems } from '../../utils/variant-selection.util';
import { UomService, formatUnitQuantity } from '../../services/uom.service';
import { UnitEntry, formatUnitTotals, roundQuantity, sumByUnit } from '../../utils/uom-quantity.util';
import { AdjustmentUomFieldsComponent } from './adjustment-uom-fields.component';
import { AdjustmentUomEntry, LOOSE_CHOICE, adjustmentEntryError, adjustmentUomPayload, signedBaseChange } from './adjustment-uom.logic';

interface AdjustmentLineItem extends AdjustmentUomEntry {
  variantId: number;
  productName: string;
  variantName: string;
  sku?: string;
  productCode?: string;
  attributes?: string;
  primaryImageThumb?: string;
  /** In base units. For a configured item it previews `enteredQuantity`; the server converts. */
  currentStock: number;
  quantityChange: number;
  reason?: string;
}

@Component({
  selector: 'app-adjustment-create',
  standalone: true,
  imports: [CommonModule, FormsModule, VariantSearchPickerComponent, AdjustmentUomFieldsComponent],
  templateUrl: './adjustment-create.component.html',
  styleUrls: ['./adjustment-create.component.css']
})
export class AdjustmentCreateComponent implements OnInit {
  private imageService = inject(ProductImageService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  readonly ADJUSTMENT_REASONS = ADJUSTMENT_REASONS;
  allowNegativeStock = signal(false);
  isEditMode = signal(false);
  editingAdjustmentId = signal<number | null>(null);
  originalAdjustment = signal<StockAdjustmentDto | null>(null);

  locationType = signal<string>('outlet');
  locationId = signal<number | null>(null);
  reason = signal<string>('');
  notes = signal<string>('');

  outlets = signal<any[]>([]);
  warehouses = signal<any[]>([]);
  items = signal<AdjustmentLineItem[]>([]);
  loadingLineStocks = signal<Set<number>>(new Set());

  isLoadingData = signal(false);
  isSubmitting = signal(false);

  private previousLocationType = 'outlet';

  existingVariantIds = computed(() => this.items().map(i => i.variantId));

  totals = computed(() => {
    const lines = this.items();
    const totalIncrease = lines.filter(i => i.quantityChange > 0).reduce((sum, i) => sum + i.quantityChange, 0);
    const totalDecrease = lines.filter(i => i.quantityChange < 0).reduce((sum, i) => sum + Math.abs(i.quantityChange), 0);
    return {
      totalProducts: lines.length,
      totalIncrease,
      totalDecrease,
      netAdjustment: totalIncrease - totalDecrease,
    };
  });

  /** Whether the lines involve anything but pcs, in which case their quantities cannot be added into one figure. */
  hasNonPcsLines = computed(() => this.items().some(line => this.lineUnit(line) !== 'pcs'));

  /** One figure per unit, for when plain totals would add unlike units. `sign` filters to increases or decreases; none gives the net. */
  totalLabel(sign?: 1 | -1): string {
    const rows = this.items()
      .filter(line => !sign || Math.sign(line.quantityChange) === sign)
      .map(line => ({ unitCode: this.lineUnit(line), quantity: sign ? Math.abs(line.quantityChange) : line.quantityChange }));
    return formatUnitTotals(sumByUnit(rows));
  }

  lineUnit(line: AdjustmentLineItem): string {
    return line.uomConfig?.baseUnit.code ?? 'pcs';
  }

  /** A configured line says its unit; a plain pcs line is shown as it always was. */
  lineStockLabel(line: AdjustmentLineItem, quantity: number): string {
    return line.uomConfig ? formatUnitQuantity(quantity, this.lineUnit(line)) : String(quantity);
  }

  isFormValid = computed(() => {
    return !!this.locationType()
      && !!this.locationId()
      && this.items().length > 0
      && !!this.reason()
      && !this.hasInvalidRows();
  });

  formChecklist = computed(() => {
    return [
      { label: 'Location type selected', complete: !!this.locationType() },
      { label: 'Location selected', complete: !!this.locationId() },
      { label: 'At least one line item', complete: this.items().length > 0 },
      { label: 'All line quantities are non-zero', complete: this.items().every(i => i.quantityChange !== 0) },
      { label: 'Reason selected', complete: !!this.reason() },
      { label: 'Resulting stock is valid', complete: !this.hasInvalidRows() },
    ];
  });

  checklistPercent = computed(() => {
    const checklist = this.formChecklist();
    const complete = checklist.filter(item => item.complete).length;
    return Math.round((complete / checklist.length) * 100);
  });

  canSubmit = computed(() => {
    return !this.isSubmitting() && !this.isLoadingData() && this.isFormValid();
  });

  constructor(
    private adjustmentService: StockAdjustmentService,
    private userOutletAccess: UserOutletAccessService,
    private settingsService: SettingsService,
    public auth: AuthService,
    private route: ActivatedRoute,
    private router: Router,
    private location: Location,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    const idParam = this.route.snapshot.paramMap.get('id');
    if (idParam) {
      this.isEditMode.set(true);
      this.editingAdjustmentId.set(Number(idParam));
    }

    this.loadSupportingData();
    this.previousLocationType = this.locationType();
  }

  resolveImage(path?: string | null): string {
    return this.imageService.resolveUrl(path);
  }

  loadSupportingData(): void {
    this.isLoadingData.set(true);

    forkJoin({
      authorizedLocations: this.userOutletAccess.load(),
      inventorySettings: this.settingsService.getInventorySettings(),
    }).subscribe({
      next: (responses) => {
        const auth = responses.authorizedLocations.data;
        this.outlets.set(auth?.destinationOutlets || auth?.outlets || []);
        this.warehouses.set(auth?.destinationWarehouses || auth?.warehouses || []);

        if (auth?.defaultLocationType === 'warehouse') {
          this.locationType.set('warehouse');
          this.previousLocationType = 'warehouse';
        }

        if (auth?.defaultLocationId) {
          this.locationId.set(auth.defaultLocationId);
        }

        this.allowNegativeStock.set(!!responses.inventorySettings.data?.allowNegativeStock);
        if (this.isEditMode() && this.editingAdjustmentId()) {
          this.loadAdjustment(this.editingAdjustmentId()!);
        }
        this.isLoadingData.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoadingData.set(false);
      }
    });
  }

  private loadAdjustment(id: number): void {
    this.adjustmentService.getById(id).subscribe({
      next: (res) => {
        const adjustment = res.data as StockAdjustmentDto;
        this.originalAdjustment.set(adjustment);
        this.locationType.set(adjustment.locationType);
        this.previousLocationType = adjustment.locationType;
        this.locationId.set(adjustment.locationId);
        this.reason.set(adjustment.reason === 'MultiLineAdjustment' ? '' : adjustment.reason);
        this.notes.set(adjustment.notes || '');
        this.items.set(adjustment.lines.map(line => ({
          variantId: line.variantId,
          productName: line.productName,
          variantName: line.variantSku,
          sku: line.variantSku,
          productCode: line.productCode,
          currentStock: line.previousQuantity,
          quantityChange: line.quantityChange,
          reason: line.reason,
        })));
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  getLocations(): any[] {
    return this.locationType() === 'outlet' ? this.outlets() : this.warehouses();
  }

  getSelectedLocationName(): string {
    const selectedId = this.locationId();
    if (!selectedId) {
      return '-';
    }

    const selected = this.getLocations().find(loc => loc.id === selectedId);
    return selected?.name || '-';
  }

  onLocationTypeChange(nextType: string): void {
    if (this.items().length > 0) {
      const confirmed = window.confirm('Changing location type will clear all current line items. Continue?');
      if (!confirmed) {
        this.locationType.set(this.previousLocationType);
        return;
      }
    }

    this.locationType.set(nextType);
    this.previousLocationType = nextType;
    this.locationId.set(null);
    this.items.set([]);
  }

  onLocationChange(nextLocationId: number | null): void {
    const currentLocationId = this.locationId();
    if (currentLocationId && nextLocationId !== currentLocationId && this.items().length > 0) {
      const confirmed = window.confirm('Changing location will clear all current line items. Continue?');
      if (!confirmed) {
        return;
      }
    }

    this.locationId.set(nextLocationId);
    this.items.set([]);
  }

  onVariantSelected(variant: VariantSearchItem): void {
    if (!this.locationId()) {
      this.alertService.warning('Select location first before adding products.');
      return;
    }

    if (this.isEditMode() && this.items().length > 0) {
      this.alertService.warning('Draft edit supports one adjustment line at a time. Remove the current line to replace it.');
      return;
    }

    const existingIndex = findVariantIndexById(this.items(), variant.id);
    if (existingIndex !== -1) {
      this.alertService.warning(`${variant.productName} - ${variant.name} is already added`);
      return;
    }

    const lineIndex = this.items().length;
    this.loadingLineStocks.update(s => {
      const next = new Set(s);
      next.add(lineIndex);
      return next;
    });

    const lineItem: AdjustmentLineItem = {
      variantId: variant.id,
      productName: variant.productName,
      variantName: variant.name,
      sku: variant.sku,
      productCode: variant.productCode,
      attributes: variant.attributes,
      primaryImageThumb: variant.primaryImageThumb,
      currentStock: variant.stockQuantity ?? 0,
      quantityChange: 0,
      reason: ''
    };

    this.items.update(items => [...items, { ...lineItem, uomStatus: 'loading' }]);
    this.loadLineConfig(variant.id);
    this.loadingLineStocks.update(s => {
      const next = new Set(s);
      next.delete(lineIndex);
      return next;
    });
  }

  private loadLineConfig(variantId: number): void {
    this.injector.get(UomService).getVariantConfigs([variantId])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: configs => {
          const config = configs.find(candidate => candidate.variantId === variantId) ?? null;
          this.patchLine(variantId, line => ({
            ...line,
            uomStatus: 'ready',
            uomConfig: config,
            ...(config ? { unitOptionId: null, enteredQuantity: line.quantityChange, packChoice: LOOSE_CHOICE } : {})
          }));
          if (config?.packStateTrackingEnabled) {
            this.loadPackState(variantId);
          }
        },
        error: () => this.patchLine(variantId, line => ({ ...line, uomStatus: 'failed' }))
      });
  }

  /** Shows which sealed packs are held. Choosing a form still works without it, and the server checks the stock. */
  private loadPackState(variantId: number): void {
    const locationId = this.locationId();
    if (!locationId) {
      return;
    }
    this.injector.get(UomService).getPackState(variantId, locationId, this.locationType())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: view => this.patchLine(variantId, line => ({ ...line, packView: view })),
        error: () => this.patchLine(variantId, line => ({ ...line, packView: null }))
      });
  }

  private patchLine(variantId: number, change: (line: AdjustmentLineItem) => AdjustmentLineItem): void {
    this.items.update(items => items.map(line => (line.variantId === variantId ? change(line) : line)));
  }

  updateLineEntry(index: number, entry: UnitEntry): void {
    this.items.update(items => {
      const next = [...items];
      const config = next[index].uomConfig;
      if (!config) {
        return items;
      }
      next[index] = {
        ...next[index],
        unitOptionId: entry.unitOptionId,
        enteredQuantity: entry.quantity,
        quantityChange: signedBaseChange(config, entry)
      };
      return next;
    });
  }

  updateLinePackChoice(index: number, packChoice: string | null): void {
    this.items.update(items => {
      const next = [...items];
      next[index] = { ...next[index], packChoice };
      return next;
    });
  }

  removeLine(index: number): void {
    this.items.update(items => items.filter((_, i) => i !== index));
  }

  updateLineQuantity(index: number, value: number): void {
    this.items.update(items => {
      const next = [...items];
      next[index] = { ...next[index], quantityChange: Number.isFinite(value) ? value : 0 };
      return next;
    });
  }

  updateLineReason(index: number, value: string): void {
    this.items.update(items => {
      const next = [...items];
      next[index] = { ...next[index], reason: value };
      return next;
    });
  }

  getLineNewStock(line: AdjustmentLineItem): number {
    return roundQuantity(line.currentStock + line.quantityChange);
  }

  isLineNegativeInvalid(line: AdjustmentLineItem): boolean {
    if (this.allowNegativeStock()) {
      return false;
    }

    if (line.quantityChange >= 0) {
      return false;
    }

    return this.getLineNewStock(line) < 0;
  }

  isLineInvalid(line: AdjustmentLineItem): boolean {
    if (!line.variantId) {
      return true;
    }

    if (line.uomStatus === 'loading' || adjustmentEntryError(line)) {
      return true;
    }

    if (line.quantityChange === 0) {
      return true;
    }

    return this.isLineNegativeInvalid(line);
  }

  hasInvalidRows(): boolean {
    const items = this.items();
    if (items.length === 0) {
      return true;
    }

    return items.some(i => this.isLineInvalid(i));
  }

  isDuplicateVariant(variantId: number): boolean {
    return isVariantAlreadyInItems(this.items(), variantId);
  }

  onSubmit(): void {
    if (this.isEditMode()) {
      this.updateDraft();
      return;
    }

    this.saveDraft();
  }

  saveDraft(): void {
    this.submitCreate('Draft');
  }

  submitForApproval(): void {
    this.submitCreate('Submit');
  }

  submitAndApprove(): void {
    if (!this.auth.hasPermission('stock_adjustments.approve')) {
      this.alertService.error('You do not have permission to approve stock adjustments');
      return;
    }

    this.submitCreate('SubmitAndApprove');
  }

  private submitCreate(action: StockAdjustmentCreateAction): void {
    if (!this.locationId()) {
      this.alertService.error('Please select a location');
      return;
    }

    if (this.items().length === 0) {
      this.alertService.error('Please add at least one product line');
      return;
    }

    if (this.isEditMode() && this.items().length === 0) {
      this.alertService.error('Please keep at least one adjustment line');
      return;
    }

    if (!this.reason()) {
      this.alertService.error('Please select a default reason');
      return;
    }

    if (this.hasInvalidRows()) {
      this.alertService.error('Please fix invalid line items before submitting');
      return;
    }

    this.isSubmitting.set(true);

    const dto: CreateStockAdjustmentBatchDto = {
      action,
      locationId: this.locationId()!,
      locationType: this.locationType(),
      reason: this.reason(),
      notes: this.notes() || undefined,
      items: this.items().map(item => ({
        variantId: item.variantId,
        quantityChange: item.quantityChange,
        reason: item.reason || undefined,
        ...adjustmentUomPayload(item)
      }))
    };

    this.adjustmentService.createBatch(dto).subscribe({
      next: (res) => {
        const created = res.data as StockAdjustmentDto | undefined;
        const adjustmentNumber = created?.adjustmentNumber || 'Adjustment';
        const status = created?.status || action;
        this.alertService.success(`Stock adjustment ${adjustmentNumber} saved as ${status}`);
        this.router.navigate(['/stock-adjustments']);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSubmitting.set(false);
      }
    });
  }

  private updateDraft(): void {
    if (!this.locationId()) {
      this.alertService.error('Please select a location');
      return;
    }

    if (this.items().length === 0) {
      this.alertService.error('Please add at least one product line');
      return;
    }

    if (this.hasInvalidRows()) {
      this.alertService.error('Please fix invalid line items before submitting');
      return;
    }

    this.isSubmitting.set(true);

    const dto: UpdateStockAdjustmentDto = {
      locationId: this.locationId()!,
      locationType: this.locationType(),
      items: this.items().map(item => ({
        variantId: item.variantId,
        quantityChange: item.quantityChange,
        reason: item.reason || this.reason() || 'Other',
        notes: this.notes() || undefined,
        ...adjustmentUomPayload(item)
      })),
    };

    this.adjustmentService.update(this.editingAdjustmentId()!, dto).subscribe({
      next: (res) => {
        const updated = res.data as StockAdjustmentDto | undefined;
        this.alertService.success(`Stock adjustment ${updated?.adjustmentNumber || 'draft'} updated successfully`);
        this.router.navigate(['/stock-adjustments']);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSubmitting.set(false);
      }
    });
  }

  cancel(): void {
    this.location.back();
  }
}
