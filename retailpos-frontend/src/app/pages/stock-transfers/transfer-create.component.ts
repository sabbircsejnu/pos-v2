import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { Component, DestroyRef, HostListener, Injector, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';
import { StockTransferService } from '../../services/stock-transfer.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { ProductImageService } from '../../services/product-image.service';
import { ProductService } from '../../services/product.service';
import { VariantSearchItem, VariantSearchPickerComponent } from '../../components/variant-search-picker/variant-search-picker.component';
import { CreateStockTransferDto, CreateStockTransferItemDto } from '../../models/stock-transfer.model';
import { PackStateBalanceRow, PackStateView, TransferAllocation, UOM_ERROR_MESSAGES, VariantUomConfig } from '../../models/uom.model';
import { UomService, formatUnitQuantity } from '../../services/uom.service';
import { UnitQuantityEntryComponent } from '../../components/shared/unit-quantity-entry.component';
import { UiControlDirective } from '../../components/shared/ui/ui-control.directive';
import { UnitEntry, entryError, findStockOption, looseOnHand, roundQuantity, toBaseQuantity, unitTotalLabel } from '../../utils/uom-quantity.util';
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
  /** Always in base units. For a configured item they preview `enteredQuantity`; the server converts. */
  requestedQuantity: number;
  transferQuantity: number;
  uomStatus?: 'loading' | 'ready' | 'failed';
  /** Null or absent means plain pcs, which behave as they always did. */
  uomConfig?: VariantUomConfig | null;
  unitOptionId?: number | null;
  enteredQuantity?: number | null;
  packStatus?: 'loading' | 'ready' | 'failed';
  packView?: PackStateView | null;
  /** Whole packs chosen per balance row, keyed by `packKey`. */
  packCounts?: Record<string, number>;
}

function packKey(row: Pick<PackStateBalanceRow, 'state' | 'packCode' | 'baseQtyPerPack'>): string {
  return `${row.state}|${row.packCode}|${row.baseQtyPerPack}`;
}

type TransferCreateAction = 'draft' | 'submit' | 'submit_dispatch';

@Component({
  selector: 'app-transfer-create',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, FormsModule, VariantSearchPickerComponent, UnitQuantityEntryComponent, UiControlDirective],
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
  private readonly route = inject(ActivatedRoute, { optional: true });
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

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

  /** The plain sum for pcs, one figure per unit otherwise. Unlike units are never added. */
  totalTransferLabel = computed(() => unitTotalLabel(this.lineItems().map(line => ({
    unitCode: this.lineUnit(line),
    quantity: Number(line.transferQuantity) || 0
  }))));

  hasConfiguredLines = computed(() => this.lineItems().some(line => !!line.uomConfig));

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
      transferQuantity: Number(item.transferQuantity) || 0,
      ...(item.uomConfig ? { unitOptionId: item.unitOptionId ?? null, enteredQuantity: item.enteredQuantity ?? null, packCounts: item.packCounts ?? {} } : {})
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
        this.applyRecommendationPrefill(defaultIsSource ? defaultId : null);
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

  /**
   * A dashboard recommendation arrives as query parameters. Each value is checked against the lists this form already
   * offers, the form stays unsubmitted and editable, and the API validates the transfer again on submit.
   */
  private applyRecommendationPrefill(pinnedSourceId: number | null): void {
    const params = this.route?.snapshot?.queryParamMap;
    if (!params?.has('variantId')) {
      return;
    }

    const fromId = Number(params.get('fromLocationId'));
    const toId = Number(params.get('toLocationId'));
    const variantId = Number(params.get('variantId'));
    const quantity = Number(params.get('quantity'));
    const sku = params.get('sku') ?? '';
    const sourceAllowed = this.sourceOutlets().some((location: any) => location.id === fromId)
      && (this.canSelectFromLocation() || pinnedSourceId === fromId);
    const destinationAllowed = fromId !== toId
      && this.destinationOutlets().some((location: any) => location.id === toId);
    if (params.get('fromLocationType') !== 'outlet' || params.get('toLocationType') !== 'outlet'
      || !sourceAllowed || !destinationAllowed || !Number.isInteger(variantId) || variantId <= 0
      || !Number.isFinite(quantity) || quantity <= 0 || !sku) {
      this.alertService.warning('This recommendation could not be applied to your transfer locations.');
      return;
    }

    this.fromLocationType.set('outlet');
    this.fromLocationId.set(fromId);
    this.toLocationType.set('outlet');
    this.toLocationId.set(toId);

    // Live stock at the source comes from the same lookup the product search uses, not from the recommendation.
    // The quantity is in base units, so it may be fractional only for an item whose base unit allows it.
    forkJoin({
      search: this.injector.get(ProductService).searchVariants(sku, 1, 20, fromId, 'outlet'),
      configs: this.injector.get(UomService).getVariantConfigs([variantId]).pipe(catchError(() => of(null)))
    }).subscribe({
      next: ({ search, configs }: { search: { data?: VariantSearchItem[] } | null; configs: VariantUomConfig[] | null }) => {
        const match = (search?.data || []).find(variant => variant.id === variantId);
        if (!match) {
          this.alertService.warning('The recommended variant is no longer available at the source.');
          this.markAsPristine();
          return;
        }

        const config = configs?.find(candidate => candidate.variantId === variantId) ?? null;
        if (!config && !Number.isInteger(quantity)) {
          this.alertService.warning('This recommendation could not be applied to your transfer locations.');
          this.markAsPristine();
          return;
        }

        const added = this.addLine({
          id: match.id,
          productCode: match.productCode,
          sku: match.sku,
          barcode: match.barcode,
          productName: match.productName,
          name: match.name,
          attributes: match.attributes,
          variantAttributes: match.variantAttributes || match.attributes,
          variantDisplay: match.variantDisplay,
          stockQuantity: match.stockQuantity,
          primaryImageThumb: match.primaryImageThumb
        });
        if (added) {
          this.applyLineConfig(variantId, config, configs === null);
          this.updateTransferQuantity(0, quantity);
        }
        this.markAsPristine();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
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
    if (this.addLine(variant)) {
      this.loadLineConfig(variant.id);
    }
  }

  private addLine(variant: VariantSearchItem): boolean {
    if (!this.fromLocationId()) {
      this.alertService.warning('Select source location first before adding products.');
      return false;
    }

    const duplicateIndex = findVariantIndexById(this.lineItems(), variant.id);
    if (duplicateIndex !== -1) {
      this.alertService.warning('This variant is already added in another line');
      this.highlightDuplicateRow(duplicateIndex);
      return false;
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
        transferQuantity: 1,
        uomStatus: 'loading'
      }
    ]);
    return true;
  }

  private loadLineConfig(variantId: number): void {
    this.injector.get(UomService).getVariantConfigs([variantId])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: configs => this.applyLineConfig(variantId, configs.find(config => config.variantId === variantId) ?? null, false),
        error: () => this.applyLineConfig(variantId, null, true)
      });
  }

  /** The line keeps its base quantity: the base unit stays selected until the user chooses another. */
  private applyLineConfig(variantId: number, config: VariantUomConfig | null, failed: boolean): void {
    this.patchLine(variantId, line => ({
      ...line,
      uomStatus: failed ? 'failed' : 'ready',
      uomConfig: config,
      ...(config ? { unitOptionId: null, enteredQuantity: line.transferQuantity } : {}),
      ...(config?.packStateTrackingEnabled ? { packCounts: {} } : {})
    }));
    if (config?.packStateTrackingEnabled) {
      this.loadPackState(variantId);
    }
  }

  loadPackState(variantId: number): void {
    const locationId = this.fromLocationId();
    if (!locationId) {
      return;
    }
    this.patchLine(variantId, line => ({ ...line, packStatus: 'loading' }));
    this.injector.get(UomService).getPackState(variantId, locationId, this.fromLocationType())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: view => this.patchLine(variantId, line => ({ ...line, packStatus: 'ready', packView: view })),
        error: () => this.patchLine(variantId, line => ({ ...line, packStatus: 'failed', packView: null }))
      });
  }

  private patchLine(variantId: number, change: (line: TransferLineItem) => TransferLineItem): void {
    this.lineItems.update(items => items.map(line => (line.variantId === variantId ? change(line) : line)));
  }

  /** Requested follows transfer on a direct transfer, and never falls below it otherwise. */
  private withTransferQuantity(line: TransferLineItem, transfer: number): TransferLineItem {
    const requested = this.transferType() === 'direct'
      ? transfer
      : (line.requestedQuantity < transfer ? transfer : line.requestedQuantity);
    return { ...line, transferQuantity: transfer, requestedQuantity: requested };
  }

  updateTransferQuantity(index: number, qty: number): void {
    const current = this.lineItems()[index];
    if (current?.uomConfig) {
      this.updateTransferEntry(index, { unitOptionId: current.unitOptionId ?? null, quantity: qty });
      return;
    }

    this.lineItems.update(items => {
      const updated = [...items];
      updated[index] = this.withTransferQuantity(updated[index], Number.isFinite(qty) ? qty : 0);
      return updated;
    });
  }

  updateTransferEntry(index: number, entry: UnitEntry): void {
    this.lineItems.update(items => {
      const updated = [...items];
      const line = updated[index];
      const config = line.uomConfig;
      if (!config) {
        return items;
      }
      const base = entry.quantity && entry.quantity > 0 ? toBaseQuantity(config, entry.unitOptionId, entry.quantity) ?? 0 : 0;
      updated[index] = {
        ...this.withTransferQuantity(line, base),
        unitOptionId: entry.unitOptionId,
        enteredQuantity: entry.quantity,
        packCounts: this.defaultPackCounts(line, entry)
      };
      return updated;
    });
  }

  /** Entering a sealed purchase unit means those sealed packs move, until the user says otherwise. */
  private defaultPackCounts(line: TransferLineItem, entry: UnitEntry): Record<string, number> | undefined {
    const option = line.uomConfig ? findStockOption(line.uomConfig, entry.unitOptionId) : null;
    const row = option?.isSealedPack
      ? line.packView?.balances.find(balance => balance.state === 'sealed' && balance.packCode === option.code && balance.baseQtyPerPack === option.baseQuantityPerUnit)
      : undefined;
    return row && Number.isInteger(entry.quantity) ? { [packKey(row)]: entry.quantity as number } : line.packCounts;
  }

  updatePackCount(index: number, row: PackStateBalanceRow, value: number | null): void {
    this.lineItems.update(items => {
      const updated = [...items];
      updated[index] = {
        ...updated[index],
        packCounts: { ...updated[index].packCounts, [packKey(row)]: Number(value) || 0 }
      };
      return updated;
    });
  }

  lineUnit(line: TransferLineItem): string {
    return line.uomConfig?.baseUnit.code ?? 'pcs';
  }

  isPackTracked(line: TransferLineItem): boolean {
    return !!line.uomConfig?.packStateTrackingEnabled;
  }

  /** Sealed and prepared stock at the source that this line can move. */
  packRows(line: TransferLineItem): PackStateBalanceRow[] {
    return (line.packView?.balances ?? []).filter(row => row.state !== 'loose' && row.packCount > 0);
  }

  packCount(line: TransferLineItem, row: PackStateBalanceRow): number {
    return line.packCounts?.[packKey(row)] ?? 0;
  }

  /** What is left of the moved quantity after whole packs. It moves as loose stock. */
  looseRemainder(line: TransferLineItem): number {
    const packed = this.packRows(line).reduce((sum, row) => sum + this.packCount(line, row) * row.baseQtyPerPack, 0);
    return roundQuantity(line.transferQuantity - packed);
  }

  formatBase(line: TransferLineItem, quantity: number): string {
    return formatUnitQuantity(quantity, this.lineUnit(line));
  }

  /** The allocations sent with a pack-tracked line. They add up to the moved quantity when the line is valid. */
  buildAllocations(line: TransferLineItem): TransferAllocation[] {
    const allocations: TransferAllocation[] = this.packRows(line)
      .filter(row => this.packCount(line, row) > 0)
      .map(row => ({
        state: row.state,
        packCode: row.packCode,
        baseQtyPerPack: row.baseQtyPerPack,
        packCount: this.packCount(line, row),
        baseQuantity: roundQuantity(this.packCount(line, row) * row.baseQtyPerPack)
      }));
    const loose = this.looseRemainder(line);
    if (loose > 0) {
      allocations.push({ state: 'loose', packCode: null, baseQtyPerPack: 0, packCount: 0, baseQuantity: loose });
    }
    return allocations;
  }

  getAllocationError(line: TransferLineItem): string | null {
    if (!this.isPackTracked(line)) {
      return null;
    }
    if (line.packStatus === 'loading') {
      return 'Checking sealed and loose stock at the source...';
    }
    if (line.packStatus === 'failed' || !line.packView) {
      return 'Sealed and loose stock could not be loaded.';
    }
    for (const row of this.packRows(line)) {
      const count = this.packCount(line, row);
      if (!Number.isInteger(count) || count < 0) {
        return 'Sealed and prepared stock moves in whole packs.';
      }
      if (count > row.packCount) {
        return `Only ${row.packCount} ${row.state} ${row.packCode} available.`;
      }
    }
    const loose = this.looseRemainder(line);
    if (loose < 0) {
      return 'The packs selected are more than the quantity being moved.';
    }
    if (loose > looseOnHand(line.packView)) {
      return UOM_ERROR_MESSAGES['UOM_INSUFFICIENT_LOOSE_STOCK'];
    }
    return null;
  }

  getTransferQuantityError(item: TransferLineItem): string | null {
    if (item.uomStatus === 'loading') {
      return 'Checking the units of this item...';
    }

    if (item.uomConfig) {
      const error = entryError(item.uomConfig, item.unitOptionId, item.enteredQuantity);
      if (error) {
        return error;
      }
    }

    if (!item.transferQuantity || item.transferQuantity <= 0) {
      return 'Transfer quantity must be greater than 0.';
    }

    if (!item.uomConfig && !Number.isInteger(item.transferQuantity)) {
      return 'Transfer quantity must be a whole number.';
    }

    if (item.availableStock !== null && item.transferQuantity > item.availableStock) {
      return 'Transfer quantity cannot exceed available stock at source.';
    }

    if (this.isRequisitionTransfer() && item.transferQuantity > item.requestedQuantity) {
      return 'Transfer quantity cannot exceed requested quantity.';
    }

    return this.getAllocationError(item);
  }

  private toItemDto(line: TransferLineItem): CreateStockTransferItemDto {
    const dto: CreateStockTransferItemDto = {
      variantId: line.variantId,
      quantity: line.transferQuantity,
      requestedQuantity: line.requestedQuantity,
      transferQuantity: line.transferQuantity,
      unitCost: 0
    };
    if (!line.uomConfig) {
      return dto;
    }
    return {
      ...dto,
      enteredQuantity: line.enteredQuantity ?? undefined,
      unitOptionId: line.unitOptionId ?? null,
      expectedConfigVersionId: line.uomConfig.configId,
      ...(this.isPackTracked(line) ? { allocations: this.buildAllocations(line) } : {})
    };
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

    if (this.hasInvalidLineItems()) {
      this.alertService.error('Please fix the invalid transfer lines');
      return;
    }

    if (this.isSubmitting()) {
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
      items: this.lineItems().map(line => this.toItemDto(line))
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
