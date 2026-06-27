import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { StockTransferService } from '../../services/stock-transfer.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { ProductImageService } from '../../services/product-image.service';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { VariantSearchItem, VariantSearchPickerComponent } from '../../components/variant-search-picker/variant-search-picker.component';
import { CreateStockTransferDto } from '../../models/stock-transfer.model';
import { findVariantIndexById } from '../../utils/variant-selection.util';

interface TransferLineItem {
  variantId: number;
  variantSku: string;
  productName: string;
  productCode?: string;
  attributes?: string;
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
  imports: [CommonModule, FormsModule, VariantSearchPickerComponent],
  templateUrl: './transfer-create.component.html',
  styleUrls: ['./transfer-create.component.css']
})
export class TransferCreateComponent implements OnInit {
  private imageService = inject(ProductImageService);

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
    private errorHandler: ErrorHandlerService
  ) {}

  ngOnInit(): void {
    this.loadSupportingData();
  }

  loadSupportingData(): void {
    this.isLoadingData.set(true);

    this.userOutletAccess.load().subscribe({
      next: (response: any) => {
        const auth = response?.data;
        this.sourceOutlets.set(auth?.outlets || []);
        this.sourceWarehouses.set(auth?.warehouses || []);
        this.destinationOutlets.set(auth?.destinationOutlets || auth?.outlets || []);
        this.destinationWarehouses.set(auth?.destinationWarehouses || auth?.warehouses || []);

        if (auth?.defaultLocationType === 'warehouse') {
          this.fromLocationType.set('warehouse');
        }

        if (auth?.defaultLocationId) {
          this.fromLocationId.set(auth.defaultLocationId);
        }
        this.isLoadingData.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoadingData.set(false);
      }
    });
  }

  getFromLocations(): any[] {
    return this.fromLocationType() === 'outlet' ? this.sourceOutlets() : this.sourceWarehouses();
  }

  getToLocations(): any[] {
    return this.toLocationType() === 'outlet' ? this.destinationOutlets() : this.destinationWarehouses();
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

  resolveImage(path?: string | null): string {
    return this.imageService.resolveUrl(path);
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
        productName: variant.productName,
        productCode: variant.productCode,
        attributes: variant.attributes,
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
        const transferId = this.extractTransferId(res);

        if (!transferId || action === 'draft') {
          this.alertService.success('Stock transfer saved successfully');
          this.router.navigate(['/stock-transfers']);
          return;
        }

        this.transferService.submit(transferId).subscribe({
          next: () => {
            if (action !== 'submit_dispatch') {
              this.alertService.success('Stock transfer submitted successfully');
              this.router.navigate(['/stock-transfers', transferId]);
              return;
            }

            this.transferService.send(transferId).subscribe({
              next: () => {
                this.alertService.success('Stock transfer submitted and dispatched successfully');
                this.router.navigate(['/stock-transfers', transferId]);
              },
              error: (dispatchErr) => {
                this.alertService.error(`Transfer submitted, but dispatch failed: ${this.errorHandler.extractErrorMessage(dispatchErr)}`);
                this.isSubmitting.set(false);
              }
            });
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
