import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { Component, HostListener, OnInit, computed, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { forkJoin } from 'rxjs';
import { StockTransferService } from '../../services/stock-transfer.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ProductImageService } from '../../services/product-image.service';
import { VariantSearchItem, VariantSearchPickerComponent } from '../../components/variant-search-picker/variant-search-picker.component';
import { CreateStockTransferDto } from '../../models/stock-transfer.model';
import { findVariantIndexById } from '../../utils/variant-selection.util';
import { UnsavedChangesAware, UnsavedChangesService } from '../../services/unsaved-changes.service';

interface TransferLineItem {
  variantId: number;
  variantSku: string;
  variantName?: string;
  productName: string;
  productCode?: string;
  attributes?: string;
  variantAttributes?: string;
  variantDisplay?: string;
  primaryImageThumb?: string;
  availableStock: number | null;
  destinationStock: number | null;
  requestedQuantity: number;
  transferQuantity: number;
}

type TransferCreateAction = 'draft' | 'submit' | 'submit_dispatch';

@Component({
  selector: 'app-transfer-create',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, FormsModule, VariantSearchPickerComponent],
  templateUrl: './transfer-create.component.html',
  styleUrls: ['./transfer-create.component.css']
})
export class TransferCreateComponent implements OnInit, UnsavedChangesAware {
  transferType = signal<string>('direct');
  fromLocationType = signal<string>('outlet');
  fromLocationId = signal<number | null>(null);
  toLocationType = signal<string>('outlet');
  toLocationId = signal<number | null>(null);
  transferDate = signal<string>(new Date().toISOString().substring(0, 10));
  notes = signal<string>('');

  lineItems = signal<TransferLineItem[]>([]);

  sourceOutlets = signal<any[]>([]);
  sourceWarehouses = signal<any[]>([]);
  destinationOutlets = signal<any[]>([]);
  destinationWarehouses = signal<any[]>([]);
  duplicateLineItemIndex = signal<number | null>(null);

  isLoadingData = signal(false);
  isSubmitting = signal(false);
  private initialSnapshot = '';

  existingVariantIds = computed(() => this.lineItems().map(i => i.variantId));
  canSelectFromLocation = computed(() => this.auth.hasPermission('stock_transfers.transfer_from_any_location'));

  isRequisitionTransfer = computed(() => this.transferType() === 'requisition');

  hasBothLocationsResolved = computed(() =>
    !!this.fromLocationType() &&
    !!this.fromLocationId() &&
    !!this.toLocationType() &&
    !!this.toLocationId()
  );

  canSearchProducts = computed(() => this.hasBothLocationsResolved() && !this.sameLocationSelected());

  productSearchHelperText = computed(() => {
    if (this.sameLocationSelected()) {
      return 'Source and destination cannot be the same.';
    }

    if (!this.fromLocationId() || !this.fromLocationType()) {
      return 'Source location is being resolved from your assigned/default location.';
    }

    if (!this.toLocationId() || !this.toLocationType()) {
      return 'Select destination location before adding products.';
    }

    return '';
  });

  totals = computed(() => {
    const lines = this.lineItems();
    return {
      totalProducts: lines.length,
      totalTransferQuantity: lines.reduce((sum, i) => sum + (Number(i.transferQuantity) || 0), 0),
    };
  });

  hasInvalidLineItems = computed(() =>
    this.lineItems().some(item => this.getTransferQuantityError(item) !== null)
  );

  sameLocationSelected = computed(() =>
    this.fromLocationType() === this.toLocationType() &&
    !!this.fromLocationId() &&
    !!this.toLocationId() &&
    this.fromLocationId() === this.toLocationId()
  );

  formChecklist = computed(() => {
    const allItemsValid = this.lineItems().length > 0 && !this.hasInvalidLineItems();

    return [
      { label: 'Transfer type selected', complete: !!this.transferType() },
      { label: 'Transfer date selected', complete: !!this.transferDate() },
      { label: 'Source location selected', complete: !!this.fromLocationType() && !!this.fromLocationId() },
      { label: 'Destination location selected', complete: !!this.toLocationType() && !!this.toLocationId() },
      { label: 'Source and destination are different', complete: !this.sameLocationSelected() },
      { label: 'At least one valid line item', complete: allItemsValid },
    ];
  });

  checklistPercent = computed(() => {
    const checklist = this.formChecklist();
    const complete = checklist.filter(item => item.complete).length;
    return Math.round((complete / checklist.length) * 100);
  });

  canSubmit = computed(() => {
    if (this.isSubmitting() || this.isLoadingData()) {
      return false;
    }

    return this.formChecklist().every(item => item.complete) && !this.hasInvalidLineItems();
  });

  constructor(
    private transferService: StockTransferService,
    private userOutletAccess: UserOutletAccessService,
    public auth: AuthService,
    private router: Router,
    private location: Location,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private imageService: ProductImageService,
    private unsavedChangesService: UnsavedChangesService
  ) {}

  @HostListener('window:beforeunload', ['$event'])
  onBeforeUnload(event: BeforeUnloadEvent): void {
    this.unsavedChangesService.applyBeforeUnload(event, this.hasUnsavedChanges());
  }

  hasUnsavedChanges(): boolean {
    if (!this.initialSnapshot) {
      return false;
    }

    return this.initialSnapshot !== this.buildSnapshot();
  }

  getUnsavedChangesMessage(): string {
    return this.unsavedChangesService.defaultMessage;
  }

  formatVariantText(item: TransferLineItem): string {
    const attributesText = this.formatFromAttributes(item.variantAttributes || item.attributes);
    if (attributesText) {
      return attributesText;
    }

    if (item.variantDisplay?.trim()) {
      return this.normalizeVariantDisplay(item.variantDisplay.trim());
    }

    if (item.variantName?.trim()) {
      return this.normalizeVariantDisplay(item.variantName.trim());
    }

    return item.variantSku?.trim() || '';
  }

  private formatFromAttributes(attributes?: string): string {
    if (!attributes?.trim()) {
      return '';
    }

    try {
      const parsed = JSON.parse(attributes);
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
        return '';
      }

      const entries = Object.entries(parsed as Record<string, unknown>)
        .map(([key, value]) => ({ label: this.humanizeKey(key), value: String(value ?? '').trim() }))
        .filter(x => !!x.label && !!x.value && x.value.toLowerCase() !== 'null' && x.value.toLowerCase() !== 'undefined');

      if (!entries.length) {
        return '';
      }

      const preferredOrder = ['color', 'size', 'fabric', 'febric'];
      entries.sort((a, b) => {
        const ai = preferredOrder.indexOf(a.label.toLowerCase());
        const bi = preferredOrder.indexOf(b.label.toLowerCase());
        const left = ai === -1 ? Number.MAX_SAFE_INTEGER : ai;
        const right = bi === -1 ? Number.MAX_SAFE_INTEGER : bi;
        if (left !== right) return left - right;
        return a.label.localeCompare(b.label);
      });

      return entries.map(x => `${x.label}: ${x.value}`).join(' / ');
    } catch {
      return '';
    }
  }

  private normalizeVariantDisplay(display: string): string {
    const parts = display.split('/').map(x => x.trim()).filter(Boolean);
    if (!parts.length) {
      return display;
    }

    return parts
      .map(part => {
        if (part.includes(':')) {
          const [rawLabel, ...rest] = part.split(':');
          const label = this.humanizeKey(rawLabel.trim());
          const value = rest.join(':').trim();
          return value ? `${label}: ${value}` : label;
        }

        const firstSpace = part.indexOf(' ');
        if (firstSpace <= 0) {
          return part;
        }

        const label = this.humanizeKey(part.substring(0, firstSpace).trim());
        const value = part.substring(firstSpace + 1).trim();
        return value ? `${label}: ${value}` : label;
      })
      .join(' / ');
  }

  private humanizeKey(key: string): string {
    return key
      .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
      .replace(/[_-]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim()
      .replace(/^./, c => c.toUpperCase());
  }

  resolveImage(path?: string | null): string {
    return this.imageService.resolveUrl(path);
  }

  ngOnInit(): void {
    this.loadSupportingData();
  }

  private markAsPristine(): void {
    this.initialSnapshot = this.buildSnapshot();
  }

  private markAsSaved(): void {
    this.initialSnapshot = this.buildSnapshot();
  }

  private buildSnapshot(): string {
    const itemState = this.lineItems().map(item => ({
      variantId: item.variantId,
      requestedQuantity: Number(item.requestedQuantity) || 0,
      transferQuantity: Number(item.transferQuantity) || 0
    }));

    return JSON.stringify({
      transferType: this.transferType(),
      transferDate: this.transferDate(),
      notes: this.notes(),
      fromLocationType: this.fromLocationType(),
      fromLocationId: this.canSelectFromLocation() ? this.fromLocationId() : null,
      toLocationType: this.toLocationType(),
      toLocationId: this.toLocationId(),
      items: itemState
    });
  }

  loadSupportingData(): void {
    this.isLoadingData.set(true);

    // `defaults` carries the user's default (pinned) source; `source` carries the Step 5 source locations and
    // the business-wide destination lookup (StockTransferDestination), which are independent of each other.
    forkJoin({
      defaults: this.userOutletAccess.load(),
      source: this.userOutletAccess.load('StockTransferSource')
    }).subscribe({
      next: ({ defaults, source }: any) => {
        const defaultAuth = defaults?.data;
        const auth = source?.data;
        this.sourceOutlets.set(auth?.outlets || []);
        this.sourceWarehouses.set(auth?.warehouses || []);
        this.destinationOutlets.set(auth?.destinationOutlets || []);
        this.destinationWarehouses.set(auth?.destinationWarehouses || []);

        const defaultType = defaultAuth?.defaultLocationType;
        const defaultId = defaultAuth?.defaultLocationId;
        const defaultIsSource = !!defaultId
          && (defaultType === 'warehouse' ? this.sourceWarehouses() : this.sourceOutlets())
            .some((location: any) => location.id === defaultId);

        if (defaultIsSource && defaultType === 'warehouse') {
          this.fromLocationType.set('warehouse');
        }

        if (defaultIsSource) {
          this.fromLocationId.set(defaultId);
        }
        if (this.sameLocationSelected()) {
          this.onToLocationChange(null);
        }
        this.markAsPristine();
        this.isLoadingData.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.markAsPristine();
        this.isLoadingData.set(false);
      }
    });
  }

  getFromLocations(): any[] {
    const locations = this.fromLocationType() === 'outlet' ? this.sourceOutlets() : this.sourceWarehouses();
    return this.canSelectFromLocation() && this.fromLocationType() === this.toLocationType()
      ? locations.filter(loc => loc.id !== this.toLocationId())
      : locations;
  }

  getToLocations(): any[] {
    const locations = this.toLocationType() === 'outlet' ? this.destinationOutlets() : this.destinationWarehouses();
    return this.toLocationType() === this.fromLocationType()
      ? locations.filter(loc => loc.id !== this.fromLocationId())
      : locations;
  }

  getFromLocationName(): string {
    const selectedId = this.fromLocationId();
    if (!selectedId) {
      return '-';
    }

    const selected = this.getFromLocations().find(loc => loc.id === selectedId);
    return selected?.name || '-';
  }

  getToLocationName(): string {
    const selectedId = this.toLocationId();
    if (!selectedId) {
      return '-';
    }

    const selected = this.getToLocations().find(loc => loc.id === selectedId);
    return selected?.name || '-';
  }

  onFromTypeChange(): void {
    if (!this.canSelectFromLocation()) {
      return;
    }

    if (this.lineItems().length > 0) {
      const confirmed = window.confirm('Changing source location type will clear transfer items. Continue?');
      if (!confirmed) {
        return;
      }
      this.lineItems.set([]);
    }
    this.fromLocationId.set(null);
  }

  onToTypeChange(): void {
    this.toLocationId.set(null);
    this.lineItems.update(items => items.map(item => ({ ...item, destinationStock: null })));
  }

  onToLocationChange(nextId: number | null): void {
    this.toLocationId.set(nextId);
    if (this.sameLocationSelected()) {
      this.onFromLocationChange(null);
      if (this.sameLocationSelected()) {
        this.toLocationId.set(null);
      }
    }
    this.lineItems.update(items => items.map(item => ({ ...item, destinationStock: null })));
  }

  onTransferTypeChange(nextType: string): void {
    this.transferType.set(nextType);

    if (nextType === 'direct') {
      this.lineItems.update(items =>
        items.map(item => ({
          ...item,
          requestedQuantity: item.transferQuantity
        }))
      );
    }
  }

  onFromLocationChange(nextId: number | null): void {
    if (!this.canSelectFromLocation()) {
      return;
    }

    const currentId = this.fromLocationId();
    if (currentId && nextId !== currentId && this.lineItems().length > 0) {
      const confirmed = window.confirm('Changing source location will clear transfer items. Continue?');
      if (!confirmed) {
        return;
      }
      this.lineItems.set([]);
    }
    this.fromLocationId.set(nextId);
    if (this.sameLocationSelected()) {
      this.onToLocationChange(null);
    }
  }

  removeItem(index: number): void {
    this.lineItems.update(items => items.filter((_, i) => i !== index));
  }

  highlightDuplicateRow(index: number): void {
    this.duplicateLineItemIndex.set(index);
    setTimeout(() => this.duplicateLineItemIndex.set(null), 1500);
  }

  onVariantSelected(variant: VariantSearchItem): void {
    if (!this.fromLocationId()) {
      this.alertService.warning('Select source location first before adding products.');
      return;
    }

    const duplicateIndex = findVariantIndexById(this.lineItems(), variant.id);
    if (duplicateIndex !== -1) {
      this.alertService.warning('This variant is already added in another line');
      this.highlightDuplicateRow(duplicateIndex);
      return;
    }

    this.lineItems.update(items => [
      ...items,
      {
        variantId: variant.id,
        variantSku: variant.sku || '-',
        variantName: variant.name,
        productName: variant.productName,
        productCode: variant.productCode,
        attributes: variant.attributes || variant.variantAttributes,
        variantAttributes: variant.variantAttributes || variant.attributes,
        variantDisplay: variant.variantDisplay,
        primaryImageThumb: variant.primaryImageThumb,
        availableStock: variant.stockQuantity ?? null,
        destinationStock: null,
        requestedQuantity: 1,
        transferQuantity: 1
      }
    ]);
  }

  updateTransferQuantity(index: number, qty: number): void {
    this.lineItems.update(items => {
      const updated = [...items];
      const transfer = Number.isFinite(qty) ? qty : 0;

      const requested = this.transferType() === 'direct'
        ? transfer
        : (updated[index].requestedQuantity < transfer ? transfer : updated[index].requestedQuantity);

      updated[index] = {
        ...updated[index],
        transferQuantity: transfer,
        requestedQuantity: requested
      };
      return updated;
    });
  }

  getTransferQuantityError(item: TransferLineItem): string | null {
    if (!item.transferQuantity || item.transferQuantity <= 0) {
      return 'Transfer quantity must be greater than 0.';
    }

    if (item.availableStock !== null && item.transferQuantity > item.availableStock) {
      return 'Transfer quantity cannot exceed available stock at source.';
    }

    if (this.isRequisitionTransfer() && item.transferQuantity > item.requestedQuantity) {
      return 'Transfer quantity cannot exceed requested quantity.';
    }

    return null;
  }

  onSubmit(): void {
    this.submit();
  }

  saveDraft(): void {
    this.submitCreate('draft');
  }

  submit(): void {
    this.submitCreate('submit');
  }

  submitAndDispatch(): void {
    if (!this.auth.hasPermission('stock_transfers.dispatch')) {
      this.alertService.error('You do not have permission to dispatch stock transfers');
      return;
    }

    this.submitCreate('submit_dispatch');
  }

  private submitCreate(action: TransferCreateAction): void {
    if (!this.fromLocationId()) {
      this.alertService.error('Please select a from location');
      return;
    }
    if (!this.toLocationId()) {
      this.alertService.error('Please select a to location');
      return;
    }
    if (
      this.fromLocationType() === this.toLocationType() &&
      this.fromLocationId() === this.toLocationId()
    ) {
      this.alertService.error('From and to locations cannot be the same');
      return;
    }
    if (this.lineItems().length === 0) {
      this.alertService.error('Please add at least one item');
      return;
    }
    if (this.lineItems().some(i => !i.variantId)) {
      this.alertService.error('Please select a product variant for all items');
      return;
    }
    if (this.lineItems().some(i => i.transferQuantity <= 0)) {
      this.alertService.error('Transfer quantity must be greater than 0 for all items');
      return;
    }
    if (this.lineItems().some(i => i.availableStock !== null && i.transferQuantity > i.availableStock)) {
      this.alertService.error('Transfer quantity cannot exceed available stock at source');
      return;
    }
    if (this.lineItems().some(i => i.requestedQuantity < i.transferQuantity)) {
      this.alertService.error('Requested quantity cannot be less than transfer quantity');
      return;
    }

    const dto: CreateStockTransferDto = {
      transferType: this.transferType(),
      fromLocationId: this.fromLocationId()!,
      fromLocationType: this.fromLocationType(),
      toLocationId: this.toLocationId()!,
      toLocationType: this.toLocationType(),
      transferDate: this.transferDate(),
      notes: this.notes().trim() || undefined,
      items: this.lineItems().map(i => ({
        variantId: i.variantId,
        quantity: i.transferQuantity,
        requestedQuantity: i.requestedQuantity,
        transferQuantity: i.transferQuantity,
        unitCost: 0
      }))
    };

    this.isSubmitting.set(true);
    this.transferService.create(dto).subscribe({
      next: (res) => {
        this.markAsSaved();
        const transferId = this.extractTransferId(res);

        if (!transferId || action === 'draft') {
          this.alertService.success('Stock transfer saved successfully');
          this.router.navigate(['/stock-transfers']);
          return;
        }

        if (action === 'submit_dispatch') {
          this.transferService.submitAndDispatch(transferId).subscribe({
            next: () => {
              this.alertService.success('Stock transfer submitted and dispatched successfully');
              this.router.navigate(['/stock-transfers', transferId]);
            },
            error: (dispatchErr) => {
              this.alertService.error(`Transfer created, but submit and dispatch failed: ${this.errorHandler.extractErrorMessage(dispatchErr)}`);
              this.isSubmitting.set(false);
            }
          });
          return;
        }

        this.transferService.submit(transferId).subscribe({
          next: () => {
            this.alertService.success('Stock transfer submitted successfully');
            this.router.navigate(['/stock-transfers', transferId]);
          },
          error: (submitErr) => {
            this.alertService.error(`Transfer created, but submit failed: ${this.errorHandler.extractErrorMessage(submitErr)}`);
            this.isSubmitting.set(false);
          }
        });
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSubmitting.set(false);
      }
    });
  }

  private extractTransferId(response: any): number | null {
    const id = response?.data?.id ?? response?.data?.transfer?.id ?? response?.id;
    return typeof id === 'number' ? id : null;
  }

  cancel(): void {
    this.location.back();
  }
}
