import { UiDateInputDirective } from '../shared/ui/ui-date-input.directive';
import { Component, OnInit, AfterViewInit, inject, signal, computed, ViewChild } from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, debounceTime } from 'rxjs';
import { PurchaseOrderService } from '../../services/purchase-order.service';
import { SupplierService } from '../../services/supplier.service';
import { UserOutletAccessService } from '../../services/user-outlet-access.service';
import { ProductService } from '../../services/product.service';
import { ProductImageService } from '../../services/product-image.service';
import { AuthService } from '../../services/auth.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreatePurchaseAndReceiveRequest, CreatePurchaseOrderRequest, CreatePurchaseOrderItem, PurchaseLocationType, getPurchaseLocationTypeLabel } from '../../models/purchase-order.model';
import { SearchableDropdownComponent, DropdownOption } from '../searchable-dropdown/searchable-dropdown.component';
import { Supplier } from '../../models/supplier.model';
import { AuthorizedLocationDto, AuthorizedOutletsDto } from '../../models/report.model';
import { UiButtonComponent, UiCardComponent, UiControlDirective, UiFormFieldComponent, UiStateComponent } from '../shared/ui';
import { CurrencyService } from '../../services/currency.service';
import { VariationService } from '../../services/variation.service';
import { Variation } from '../../models/variation.model';
import { findVariantIndexById, isVariantAlreadyInItems } from '../../utils/variant-selection.util';

interface ProductVariantOption {
  id: number;
  productId: number;
  productCode?: string;
  productName: string;
  name: string;
  sku?: string;
  barcode?: string;
  attributes?: string;
  variantDisplay?: string;
  finalPrice: number;
  costPrice?: number;
  stockQuantity?: number;
  primaryImageThumb?: string;
}

type VariationMeta = {
  name: string;
  displayOrder: number;
  options: string[];
};

@Component({
  selector: 'app-po-form',
  standalone: true,
  imports: [UiDateInputDirective, CommonModule, FormsModule, SearchableDropdownComponent, UiCardComponent, UiFormFieldComponent, UiControlDirective, UiStateComponent, UiButtonComponent],
  templateUrl: './po-form.component.html',
  styleUrl: './po-form.component.css'
})
export class PoFormComponent implements OnInit, AfterViewInit {
  @ViewChild('supplierDropdown') supplierDropdown!: SearchableDropdownComponent;

  private imageSvc = inject(ProductImageService);
  private auth = inject(AuthService);
  private locationAccess = inject(UserOutletAccessService);
  canViewCost = computed(() => this.auth.hasPermission('products.view_cost'));
  canPurchaseAndReceive = computed(() => this.auth.hasPermission('purchases.receive'));

  resolveImage(path?: string | null): string { return this.imageSvc.resolveUrl(path); }

  isEditMode = signal(false);
  poId = signal<number | null>(null);
  isLoading = signal(false);
  isSaving = signal(false);

  // Form fields
  supplierId = signal<number | null>(null);
  locationType = signal<PurchaseLocationType | null>(null);
  locationId = signal<number | null>(null);
  orderDate = signal<string>(this.formatDateForInput(new Date()));
  expectedDeliveryDate = signal<string>('');
  notes = signal<string>('');
  
  items = signal<any[]>([]);

  // Computed helpers used in template
  hasSelectedItems = computed(() => this.items().some((i: any) => i.variantId && i.variantId !== 0));
  selectedItems = computed(() => this.items().filter((i: any) => i.variantId && i.variantId !== 0));
  selectedItemCount = computed(() => this.selectedItems().length);
  totalQuantity = computed(() => this.selectedItems().reduce((sum: number, item: any) => sum + (item.quantity || 0), 0));
  setupChecklist = computed(() => {
    const hasSupplier = !!this.supplierId();
    const hasDestination = !!this.locationType() && !!this.locationId();
    const hasDate = !!this.orderDate();
    const hasItems = this.selectedItemCount() > 0;
    const hasValue = this.calculateGrandTotal() > 0;

    return [
      { label: 'Supplier selected', complete: hasSupplier },
      { label: 'Destination selected', complete: hasDestination },
      { label: 'Order date selected', complete: hasDate },
      { label: 'At least one line item', complete: hasItems },
      { label: 'Order value confirmed', complete: hasValue },
    ];
  });
  setupPercent = computed(() => {
    const checklist = this.setupChecklist();
    if (checklist.length === 0) return 0;
    const done = checklist.filter(item => item.complete).length;
    return Math.round((done / checklist.length) * 100);
  });
  // 7 columns: #, Product / Variant, Product Code, Qty, Unit Price, Total, Action
  searchRowColspan = computed(() => this.hasSelectedItems() ? 7 : 1);

  // Data
  suppliers = signal<Supplier[]>([]);
  // Destination locations: Business-scoped and access-filtered by the backend (/me/outlets).
  authorizedOutlets = signal<AuthorizedLocationDto[]>([]);
  authorizedWarehouses = signal<AuthorizedLocationDto[]>([]);
  locationsLoading = signal(true);
  locationsError = signal('');
  availableLocations = computed<AuthorizedLocationDto[]>(() => {
    switch (this.locationType()) {
      case 'outlet': return this.authorizedOutlets();
      case 'warehouse': return this.authorizedWarehouses();
      default: return [];
    }
  });
  locationTypeOptions = computed(() => [
    { value: 'outlet' as PurchaseLocationType, label: 'Outlet', count: this.authorizedOutlets().length },
    { value: 'warehouse' as PurchaseLocationType, label: 'Warehouse', count: this.authorizedWarehouses().length }
  ]);
  hasAnyLocation = computed(() => this.authorizedOutlets().length + this.authorizedWarehouses().length > 0);

  /** Purchase & Receive follows the GRN location rules, which are narrower than the PO destination list. */
  private receivableLocations = signal<AuthorizedOutletsDto | null>(null);
  canReceiveAtDestination = computed(() => {
    const access = this.receivableLocations();
    const id = this.locationId();
    if (!access || !id) {
      return true;
    }
    const locations = this.locationType() === 'outlet' ? access.outlets : access.warehouses;
    return locations.some(location => location.id === id);
  });
  selectedLocationTypeLabel = computed(() => getPurchaseLocationTypeLabel(this.locationType()));
  selectedLocationName = computed(() =>
    this.availableLocations().find(location => location.id === this.locationId())?.name ?? '');
  private loadedDestination: { type: PurchaseLocationType; id: number; name: string } | null = null;

  // Product variant search per row
  variantSearchResults = signal<{ [index: number]: ProductVariantOption[] }>({});
  searchingVariants = signal<{ [index: number]: boolean }>({});
  private variantSearchSubjects: { [index: number]: Subject<string> } = {};
  variantSearchQuery = signal<{ [index: number]: string }>({});
  currentSearchPage = signal<{ [index: number]: number }>({});
  hasMoreVariants = signal<{ [index: number]: boolean }>({});
  variantInputFocused = signal<{ [index: number]: boolean }>({});
  dropdownPosition = signal<{ [index: number]: { top: number; left: number; width: number } }>({});
  duplicateHighlightedVariantId = signal<number | null>(null);
  variationMetadata = signal<VariationMeta[]>([]);

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private location: Location,
    private poService: PurchaseOrderService,
    private supplierService: SupplierService,
    private productService: ProductService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService,
    private variationService: VariationService
  ) {}

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.poId.set(+id);
    }

    // Load suppliers and the user's authorized destination locations immediately
    this.loadSuppliers();
    this.loadLocations();
    this.loadReceivableLocations();
    this.loadVariationMetadata();

    if (id) {
      this.loadPurchaseOrder(+id);
    } else {
      // Create mode - add one empty item
      this.addItem();
    }
  }
  
  ngAfterViewInit(): void {
    // Trigger initial load for dropdowns after view is initialized
    // Need to wait for async data to load
    setTimeout(() => {
      this.initializeDropdowns();
    }, 500);
  }
  
  initializeDropdowns(): void {
    if (this.suppliers().length > 0 && this.supplierDropdown) {
      const supplierOptions = this.suppliers().map((s: Supplier) => ({ 
        id: s.id, 
        name: s.name, 
        contact: s.contact 
      }));
      this.supplierDropdown.setOptions(supplierOptions, false);
      this.supplierDropdown.setHasMore(false);
    }
    
    // If data not loaded yet, try again
    if (this.suppliers().length === 0) {
      setTimeout(() => this.initializeDropdowns(), 300);
    }
  }
  
  loadSuppliers(): void {
    this.supplierService.getAll().subscribe({  
      next: (response) => {
        if (response.data) {
          this.suppliers.set(response.data);
          // Initialize dropdown after data loaded
          if (this.supplierDropdown) {
            const supplierOptions = this.suppliers().map((s: Supplier) => ({ 
              id: s.id, 
              name: s.name, 
              contact: s.contact 
            }));
            this.supplierDropdown.setOptions(supplierOptions, false);
            this.supplierDropdown.setHasMore(false);
          }
        }
      },
      error: (err: any) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }
  
  loadLocations(): void {
    this.locationsLoading.set(true);
    this.locationsError.set('');
    this.locationAccess.load().subscribe({
      next: (response) => {
        const access = response.data;
        this.authorizedOutlets.set(access.outlets);
        this.authorizedWarehouses.set(access.warehouses);
        this.locationsLoading.set(false);
        this.applyLoadedDestination();
        if (!this.isEditMode()) {
          this.applyDefaultDestination(access);
        }
      },
      error: (err: any) => {
        this.locationsLoading.set(false);
        this.locationsError.set(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  private loadReceivableLocations(): void {
    if (!this.canPurchaseAndReceive() || this.isEditMode()) {
      return;
    }
    // If this fails the button stays available and the server still enforces the rule.
    this.locationAccess.load('GrnView').subscribe({
      next: (response) => this.receivableLocations.set(response.data),
      error: () => this.receivableLocations.set(null)
    });
  }

  onLocationTypeChange(type: PurchaseLocationType | null): void {
    this.locationType.set(type);
    const options = this.availableLocations();
    if (!options.some(location => location.id === this.locationId())) {
      this.locationId.set(options.length === 1 ? options[0].id : null);
    }
  }

  onLocationChange(id: number | string | null): void {
    const parsed = id === null || id === '' ? null : Number(id);
    this.locationId.set(parsed && !Number.isNaN(parsed) ? parsed : null);
  }

  /** Pre-selects the user's default location (or the only available one) for new orders. */
  private applyDefaultDestination(access: AuthorizedOutletsDto | null): void {
    if (this.locationType()) {
      return;
    }

    const outlets = this.authorizedOutlets();
    const warehouses = this.authorizedWarehouses();
    const preferred = access?.defaultLocationType;
    const type: PurchaseLocationType | null =
      preferred === 'outlet' && outlets.length > 0 ? 'outlet'
        : preferred === 'warehouse' && warehouses.length > 0 ? 'warehouse'
          : outlets.length > 0 && warehouses.length === 0 ? 'outlet'
            : warehouses.length > 0 && outlets.length === 0 ? 'warehouse'
              : null;
    if (!type) {
      return;
    }

    this.locationType.set(type);
    const options = this.availableLocations();
    const defaultId = access?.defaultLocationType === type ? access?.defaultLocationId : null;
    const match = options.find(location => location.id === defaultId);
    this.locationId.set(match?.id ?? (options.length === 1 ? options[0].id : null));
  }

  /**
   * Keeps an existing order's destination selectable while editing, even when it is not in the
   * current user's list; the backend still enforces location access on save.
   */
  private applyLoadedDestination(): void {
    const destination = this.loadedDestination;
    if (!destination || this.locationsLoading()) {
      return;
    }

    const list = destination.type === 'outlet' ? this.authorizedOutlets : this.authorizedWarehouses;
    if (!list().some(location => location.id === destination.id)) {
      list.update(locations => [...locations, { id: destination.id, name: destination.name, type: destination.type }]);
    }
    this.locationType.set(destination.type);
    this.locationId.set(destination.id);
  }
  onSupplierSearch(event: { query: string, page: number, pageSize: number }): void {
    const suppliers: DropdownOption[] = this.suppliers().map((s: Supplier) => ({
      id: s.id,
      name: s.name,
      contact: s.contact
    }));
    
    // Filter by query if provided
    const filtered = event.query 
      ? suppliers.filter(s => s.name.toLowerCase().includes(event.query.toLowerCase()))
      : suppliers;
    
    this.supplierDropdown?.setOptions(filtered, false);
    this.supplierDropdown?.setHasMore(false);
  }

  onSupplierLoadMore(event: { query: string, page: number, pageSize: number }): void {
    // Not needed for suppliers as we load all
  }

  onSupplierSelect(option: DropdownOption): void {
    this.supplierId.set(option.id);
  }

  loadPurchaseOrder(id: number): void {
    this.isLoading.set(true);
    this.poService.getById(id).subscribe({
      next: (response) => {
        const po = response.data;
        this.supplierId.set(po.supplierId);
        const loadedType: PurchaseLocationType = po.locationType === 'outlet' ? 'outlet' : 'warehouse';
        const loadedId = po.locationId || (loadedType === 'outlet' ? po.outletId : po.warehouseId) || 0;
        if (loadedId) {
          this.loadedDestination = {
            type: loadedType,
            id: loadedId,
            name: po.locationName || (loadedType === 'outlet' ? po.outletName : po.warehouseName) || ''
          };
          this.applyLoadedDestination();
        }
        this.orderDate.set(this.formatDateForInput(new Date(po.orderDate)));
        if (po.expectedDelivery) {
          this.expectedDeliveryDate.set(this.formatDateForInput(new Date(po.expectedDelivery)));
        }
        
        this.notes.set(po.notes || '');

        // Convert items
        const itemsData: any[] = po.items.map((item: any) => ({
          variantId: item.variantId,
          quantity: item.quantity,
          unitPrice: item.unitPrice,
          discount: item.discount ?? 0,
          tax: item.tax ?? 0,
          unit: item.unit || '',
          sku: item.sku || '',
          productCode: item.productCode || item.mainProductCode || '',
          productName: item.productName || '',
          variantName: item.variantName || '',
          variantAttributes: item.variantAttributes || ''
        }));
        this.items.set(itemsData);
        // Add empty search row at end for adding more items
        this.addItem();
        this.isLoading.set(false);
      },
      error: (err: any) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.location.back();
      }
    });
  }

  addItem(): void {
    const newIndex = this.items().length;
    this.items.update(items => [
      ...items,
      {
        variantId: 0,
        quantity: 1,
        unitPrice: 0,
        discount: 0,
        tax: 0,
        unit: '',
        productName: '',
        variantName: ''
      }
    ]);
    
    // Initialize search subject for this row
    if (!this.variantSearchSubjects[newIndex]) {
      this.variantSearchSubjects[newIndex] = new Subject<string>();
      this.variantSearchSubjects[newIndex].pipe(
        debounceTime(300)
      ).subscribe(query => {
        this.searchVariantsForRow(newIndex, query, 1);
      });
    }
  }

  removeItem(index: number): void {
    this.items.update(items => items.filter((_, i) => i !== index));
    
    // Clean up search data
    delete this.variantSearchResults()[index];
    delete this.searchingVariants()[index];
    delete this.variantSearchSubjects[index];
    delete this.variantSearchQuery()[index];
  }

  onVariantSearchInput(value: string, index: number): void {
    // Update query
    this.variantSearchQuery.update(q => ({ ...q, [index]: value }));
    
    // Clear selection if user is typing (they're searching for something new)
    const currentItem = this.items()[index];
    if (currentItem?.variantId && value !== this.getVariantDisplayText(currentItem)) {
      const items = [...this.items()];
      items[index] = {
        ...items[index],
        variantId: 0,
        productName: '',
        variantName: '',
        unitPrice: 0
      };
      this.items.set(items);
    }
    
    // Initialize search subject if needed
    if (!this.variantSearchSubjects[index]) {
      this.variantSearchSubjects[index] = new Subject<string>();
      this.variantSearchSubjects[index].pipe(
        debounceTime(300)
      ).subscribe(query => {
        this.searchVariantsForRow(index, query, 1);
      });
    }
    
    // Trigger debounced search
    this.variantSearchSubjects[index].next(value);
  }

  onVariantInputFocus(index: number): void {
    this.variantInputFocused.update(f => ({ ...f, [index]: true }));

    // Calculate and store dropdown position
    setTimeout(() => {
      const inputElement = document.querySelector(`#variant-input-${index}`) as HTMLElement;
      if (inputElement) {
        const rect = inputElement.getBoundingClientRect();
        this.dropdownPosition.update(pos => ({
          ...pos,
          [index]: {
            top: rect.bottom + window.scrollY + 4,
            left: rect.left + window.scrollX,
            width: inputElement.offsetWidth
          }
        }));
      }
    }, 0);
  }

  onVariantInputBlur(index: number): void {
    // Delay to allow click on dropdown items
    setTimeout(() => {
      this.variantInputFocused.update(f => ({ ...f, [index]: false }));
      // Clear dropdown position
      this.dropdownPosition.update(pos => {
        const newPos = { ...pos };
        delete newPos[index];
        return newPos;
      });
      // If no selection was made, clear the query and results
      const currentItem = this.items()[index];
        if (!currentItem?.variantId) {
        this.variantSearchQuery.update(q => ({ ...q, [index]: '' }));
        this.variantSearchResults.update(r => ({ ...r, [index]: [] }));
      }
    }, 200);
  }

  getVariantInputValue(item: any, index: number): string {
    // If input is focused, show the search query; otherwise show the selected item
    if (this.variantInputFocused()[index]) {
      return this.variantSearchQuery()[index] || '';
    }
    return this.getVariantDisplayText(item);
  }

  searchVariantsForRow(index: number, query: string, page: number): void {
    if (!query || query.length < 2) {
      this.variantSearchResults.update(r => ({ ...r, [index]: [] }));
      return;
    }
    
    this.searchingVariants.update(s => ({ ...s, [index]: true }));
    this.currentSearchPage.update(p => ({ ...p, [index]: page }));
    
    this.productService.searchVariants(query, page, 20).subscribe({
      next: (response) => {
        const variants: ProductVariantOption[] = (response.data || []).map((variant: ProductVariantOption) => ({
          ...variant,
          variantDisplay: this.resolveVariantDisplay(variant)
        }));
        
        if (page === 1) {
          this.variantSearchResults.update(r => ({ ...r, [index]: variants }));
        } else {
          const current = this.variantSearchResults()[index] || [];
          this.variantSearchResults.update(r => ({ ...r, [index]: [...current, ...variants] }));
        }
        
        this.hasMoreVariants.update(h => ({ ...h, [index]: variants.length === 20 }));
        this.searchingVariants.update(s => ({ ...s, [index]: false }));
      },
      error: (err: any) => {
        this.searchingVariants.update(s => ({ ...s, [index]: false }));
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  loadMoreVariants(index: number): void {
    const query = this.variantSearchQuery()[index] || '';
    const currentPage = this.currentSearchPage()[index] || 1;
    this.searchVariantsForRow(index, query, currentPage + 1);
  }

  isVariantAlreadyAdded(variantId: number, currentIndex: number): boolean {
    return isVariantAlreadyInItems(this.items(), variantId, currentIndex);
  }

  onVariantResultClick(variant: ProductVariantOption, index: number): void {
    if (this.isVariantAlreadyAdded(variant.id, index)) {
      this.alertService.warning(`${variant.productName} - ${variant.name} is already added`);
      this.highlightDuplicateVariant(variant.id);
      return;
    }

    this.selectVariant(variant, index);
  }

  highlightDuplicateVariant(variantId: number): void {
    this.duplicateHighlightedVariantId.set(variantId);
    setTimeout(() => this.duplicateHighlightedVariantId.set(null), 1500);
  }

  selectVariant(variant: ProductVariantOption, index: number): void {
    const currentItems = this.items();
    const duplicateIndex = findVariantIndexById(currentItems, variant.id, index);
    
    if (duplicateIndex !== -1) {
      this.alertService.error(`${variant.productName} - ${variant.name} is already added to this order`);
      this.highlightDuplicateVariant(variant.id);
      // Clear search state but don't select
      this.variantSearchResults.update(r => ({ ...r, [index]: [] }));
      this.variantSearchQuery.update(q => ({ ...q, [index]: '' }));
      this.searchingVariants.update(s => ({ ...s, [index]: false }));
      this.variantInputFocused.update(f => ({ ...f, [index]: false }));
      return;
    }
    
    const updatedItems: any[] = [...currentItems];
    updatedItems[index] = {
      ...updatedItems[index],
      variantId: variant.id,
      sku: variant.sku || '',
      productCode: variant.productCode || '',
      unitPrice: variant.costPrice || variant.finalPrice || 0,
      productName: variant.productName,
      variantName: variant.name,
      variantAttributes: variant.attributes || '',
      variantDisplay: variant.variantDisplay || this.resolveVariantDisplay(variant),
      primaryImageThumb: variant.primaryImageThumb,
    };
    this.items.set(updatedItems);
    
    // Clear search state
    this.variantSearchResults.update(r => ({ ...r, [index]: [] }));
    this.variantSearchQuery.update(q => ({ ...q, [index]: '' }));
    this.searchingVariants.update(s => ({ ...s, [index]: false }));
    this.variantInputFocused.update(f => ({ ...f, [index]: false }));
    // Clear dropdown position
    this.dropdownPosition.update(pos => {
      const newPos = { ...pos };
      delete newPos[index];
      return newPos;
    });

    // Auto-add a new empty search row if there isn't one already
    const hasEmptyRow = this.items().some((item: any) => !item.variantId || item.variantId === 0);
    if (!hasEmptyRow) {
      this.addItem();
    }
  }

  updateItemQuantity(index: number, quantity: number): void {
    const items = [...this.items()];
    items[index] = { ...items[index], quantity };
    this.items.set(items);
  }

  updateItemPrice(index: number, price: number): void {
    const items = [...this.items()];
    items[index] = { ...items[index], unitPrice: price };
    this.items.set(items);
  }

  updateItemDiscount(index: number, discount: number): void {
    const items = [...this.items()];
    items[index] = { ...items[index], discount };
    this.items.set(items);
  }

  updateItemTax(index: number, tax: number): void {
    const items = [...this.items()];
    items[index] = { ...items[index], tax };
    this.items.set(items);
  }

  updateItemUnit(index: number, unit: string): void {
    const items = [...this.items()];
    items[index] = { ...items[index], unit };
    this.items.set(items);
  }

  calculateItemTotal(item: any): number {
    const qty = item.quantity || 0;
    const price = item.unitPrice || 0;
    const disc = item.discount || 0;
    const tax = item.tax || 0;
    return Math.round(qty * price * (1 - disc / 100) * (1 + tax / 100) * 100) / 100;
  }

  /** Returns 1-based position counting only selected (non-empty) items */
  getSelectedItemNumber(index: number): number {
    let count = 0;
    for (let i = 0; i <= index; i++) {
      const item = this.items()[i];
      if (item.variantId && item.variantId !== 0) count++;
    }
    return count;
  }

  getSearchRowIndex(): number {
    return Math.max(this.items().length - 1, 0);
  }

  calculateGrandTotal(): number {
    return this.items().reduce((sum, item) => sum + this.calculateItemTotal(item), 0);
  }

  validateForm(): boolean {
    if (this.locationsLoading() || this.locationsError()) {
      this.alertService.error(this.locationsError() || 'Please wait for locations to load');
      return false;
    }

    if (!this.supplierId()) {
      this.alertService.error('Please select a supplier');
      return false;
    }
    
    if (!this.locationType()) {
      this.alertService.error('Please select a location type');
      return false;
    }

    if (!this.locationId()) {
      this.alertService.error(`Please select ${this.locationType() === 'outlet' ? 'an outlet' : 'a warehouse'}`);
      return false;
    }
    
    if (!this.orderDate()) {
      this.alertService.error('Please select an order date');
      return false;
    }
    
    if (this.items().length === 0) {
      this.alertService.error('Please add at least one item');
      return false;
    }

    const selectedItems = this.items().filter((item: any) => item.variantId && item.variantId !== 0);
    if (selectedItems.length === 0) {
      this.alertService.error('Please add at least one item');
      return false;
    }
    
    for (let i = 0; i < selectedItems.length; i++) {
      const item: any = selectedItems[i];
      if (!item.variantId || item.variantId === 0) {
        this.alertService.error(`Item ${i + 1}: Please select a product variant`);
        return false;
      }
      if (item.quantity <= 0) {
        this.alertService.error(`Item ${i + 1}: Quantity must be greater than 0`);
        return false;
      }
      if (item.unitPrice < 0) {
        this.alertService.error(`Item ${i + 1}: Unit price cannot be negative`);
        return false;
      }
      if (item.discount < 0 || item.discount > 100) {
        this.alertService.error(`Item ${i + 1}: Discount must be between 0 and 100`);
        return false;
      }
      if (item.tax < 0 || item.tax > 100) {
        this.alertService.error(`Item ${i + 1}: Tax must be between 0 and 100`);
        return false;
      }
    }
    
    return true;
  }

  save(submit: boolean = false): void {
    if (!this.validateForm()) {
      return;
    }
    
    this.isSaving.set(true);

    const request: any = {
      supplierId: this.supplierId()!,
      locationType: this.locationType()!,
      locationId: this.locationId()!,
      orderDate: this.orderDate(),
      expectedDelivery: this.expectedDeliveryDate() || undefined,
      notes: this.notes() || undefined,
      items: this.items()
        .filter((item: any) => item.variantId && item.variantId !== 0)
        .map((item: any) => ({
          variantId: item.variantId,
          quantity: item.quantity,
          unitPrice: item.unitPrice,
          discount: item.discount ?? 0,
          tax: item.tax ?? 0,
          unit: item.unit || undefined
        }))
    };

    const saveObservable = this.isEditMode()
      ? this.poService.update(this.poId()!, request)
      : this.poService.create(request);

    saveObservable.subscribe({
      next: (response) => {
        const poId = this.isEditMode() ? this.poId() : response.data?.id;
        
        if (submit) {
          // Submit for approval
          this.poService.submit(poId!).subscribe({
            next: () => {
              this.isSaving.set(false);
              this.alertService.success('Purchase order created and submitted for approval');
              this.location.back();
            },
            error: (err: any) => {
              this.isSaving.set(false);
              this.alertService.error(this.errorHandler.extractErrorMessage(err));
            }
          });
        } else {
          this.isSaving.set(false);
          this.alertService.success(`Purchase order ${this.isEditMode() ? 'updated' : 'created'} successfully`);
          this.location.back();
        }
      },
      error: (err: any) => {
        this.isSaving.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  purchaseAndReceive(): void {
    if (this.isEditMode()) {
      this.alertService.error('Purchase & Receive is only available for new purchase orders');
      return;
    }

    if (this.isSaving()) {
      return;
    }

    if (!this.validateForm()) {
      return;
    }

    if (!this.canReceiveAtDestination()) {
      this.alertService.error('You are not assigned to this location, so you cannot receive goods into it.');
      return;
    }

    this.alertService.confirm(
      `This will create the purchase order, automatically generate a GRN, and add the products to the stock of ${this.selectedLocationTypeLabel().toLowerCase()} "${this.selectedLocationName()}". Do you want to continue?`,
      () => this.executePurchaseAndReceive(),
      'Confirm Purchase & Receive',
      'Continue',
      'Cancel'
    );
  }

  private executePurchaseAndReceive(): void {
    if (this.isSaving()) {
      return;
    }

    this.isSaving.set(true);

    const request: CreatePurchaseAndReceiveRequest = {
      supplierId: this.supplierId()!,
      locationType: this.locationType()!,
      locationId: this.locationId()!,
      orderDate: new Date(this.orderDate()),
      expectedDelivery: this.expectedDeliveryDate() ? new Date(this.expectedDeliveryDate()) : undefined,
      notes: this.notes() || undefined,
      idempotencyKey: crypto.randomUUID(),
      items: this.items()
        .filter((item: any) => item.variantId && item.variantId !== 0)
        .map((item: any) => ({
          variantId: item.variantId,
          quantity: item.quantity,
          unitPrice: item.unitPrice,
          discount: item.discount ?? 0,
          tax: item.tax ?? 0,
          unit: item.unit || undefined
        }))
    };

    this.poService.purchaseAndReceive(request).subscribe({
      next: (response) => {
        const result = response.data;
        this.isSaving.set(false);
        this.alertService.success(`Purchase & Receive completed successfully. Auto-generated ${result?.grnNumber || 'GRN'}.`);

        const poId = result?.purchaseOrder?.id;
        if (poId) {
          this.router.navigate(['/purchase-orders', poId]);
          return;
        }

        const grnId = result?.grnId;
        if (grnId) {
          this.router.navigate(['/grn', grnId]);
          return;
        }

        this.location.back();
      },
      error: (err: any) => {
        this.isSaving.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  saveDraft(): void {
    this.save(false);
  }

  submitForApproval(): void {
    this.save(true);
  }

  cancel(): void {
    this.location.back();
  }

  formatDateForInput(date: Date): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  formatCurrency(amount: number): string {
    return this.currencyService.format(amount);
  }

  onVariantScroll(event: any, index: number): void {
    const element = event.target;
    const threshold = 50;
    
    if (element.scrollHeight - element.scrollTop - element.clientHeight < threshold) {
      if (this.hasMoreVariants()[index] && !this.searchingVariants()[index]) {
        this.loadMoreVariants(index);
      }
    }
  }

  getVariantDisplayText(item: any): string {
    if (item.productName) {
      const variantText = this.resolveVariantText(item.variantAttributes, item.variantDisplay || item.variantName);
      return variantText ? `${item.productName} (${variantText})` : item.productName;
    }
    return '';
  }

  formatVariantAttributes(attributes?: string | null, fallbackName?: string | null): string {
    return this.resolveVariantText(attributes, fallbackName);
  }

  getSearchVariantText(variant: ProductVariantOption): string {
    return this.resolveVariantText(variant.attributes, variant.variantDisplay || variant.name);
  }

  private resolveVariantText(attributes?: string | null, fallbackName?: string | null): string {
    const formattedAttributes = this.formatFromAttributes(attributes);
    if (formattedAttributes) {
      return formattedAttributes;
    }

    const fallback = (fallbackName || '').trim();
    if (!fallback) {
      return '';
    }

    const normalized = this.normalizeVariantDisplay(fallback);
    return this.formatFromHyphenValues(normalized) || normalized;
  }

  private resolveVariantDisplay(variant: ProductVariantOption): string {
    const explicit = (variant.variantDisplay || '').trim();
    if (explicit) {
      return this.resolveVariantText(variant.attributes, explicit);
    }

    return this.resolveVariantText(variant.attributes, variant.name);
  }

  private formatFromAttributes(attributes?: string | null): string {
    if (!attributes?.trim()) {
      return '';
    }

    try {
      const parsed = JSON.parse(attributes);
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
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
      }
    } catch {
      // Payload may already be formatted text.
    }

    return '';
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

  private formatFromHyphenValues(raw: string): string {
    if (!raw || raw.includes(':') || raw.includes('/')) {
      return '';
    }

    const variations = this.variationMetadata();
    if (!variations.length) {
      return '';
    }

    const parts = raw
      .split(/\s+-\s+/)
      .map(x => x.trim())
      .filter(Boolean);

    if (parts.length < 2) {
      return '';
    }

    const usedVariations = new Set<string>();
    const mappedByVariation = new Map<string, { label: string; value: string; order: number }>();

    for (const part of parts) {
      const token = part.trim().toLowerCase();
      if (!token) {
        continue;
      }

      const matched = variations.find(variation => {
        if (usedVariations.has(variation.name)) {
          return false;
        }

        return variation.options.some(option => option.toLowerCase() === token);
      });

      if (!matched) {
        continue;
      }

      usedVariations.add(matched.name);
      mappedByVariation.set(matched.name, {
        label: this.humanizeKey(matched.name),
        value: part,
        order: matched.displayOrder
      });
    }

    if (!mappedByVariation.size) {
      return '';
    }

    return Array.from(mappedByVariation.values())
      .sort((a, b) => a.order - b.order)
      .map(entry => `${entry.label}: ${entry.value}`)
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

  private loadVariationMetadata(): void {
    this.variationService.getAllVariations().subscribe({
      next: (variations: Variation[]) => {
        const prepared = (variations || [])
          .filter(v => v.isActive)
          .sort((a, b) => a.displayOrder - b.displayOrder)
          .map(v => ({
            name: v.name,
            displayOrder: v.displayOrder,
            options: (v.options || [])
              .filter(o => o.isActive)
              .sort((a, b) => a.displayOrder - b.displayOrder)
              .map(o => o.name)
          }));

        this.variationMetadata.set(prepared);
      },
      error: () => {
        this.variationMetadata.set([]);
      }
    });
  }
}
