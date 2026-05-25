import { Component, OnInit, AfterViewInit, inject, signal, computed, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, debounceTime } from 'rxjs';
import { PurchaseOrderService } from '../../services/purchase-order.service';
import { SupplierService } from '../../services/supplier.service';
import { WarehouseService } from '../../services/warehouse.service';
import { ProductService } from '../../services/product.service';
import { ProductImageService } from '../../services/product-image.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CreatePurchaseOrderRequest, CreatePurchaseOrderItem } from '../../models/purchase-order.model';
import { SearchableDropdownComponent, DropdownOption } from '../searchable-dropdown/searchable-dropdown.component';
import { Supplier } from '../../models/supplier.model';
import { Warehouse } from '../../models/warehouse.model';
import { CurrencyService } from '../../services/currency.service';

interface ProductVariantOption {
  id: number;
  productId: number;
  productName: string;
  name: string;
  sku?: string;
  barcode?: string;
  attributes?: string;
  finalPrice: number;
  costPrice?: number;
  stockQuantity?: number;
  primaryImageThumb?: string;
}

@Component({
  selector: 'app-po-form',
  standalone: true,
  imports: [CommonModule, FormsModule, SearchableDropdownComponent],
  templateUrl: './po-form.component.html',
  styleUrl: './po-form.component.css'
})
export class PoFormComponent implements OnInit, AfterViewInit {
  @ViewChild('supplierDropdown') supplierDropdown!: SearchableDropdownComponent;
  @ViewChild('warehouseDropdown') warehouseDropdown!: SearchableDropdownComponent;

  private imageSvc = inject(ProductImageService);
  resolveImage(path?: string | null): string { return this.imageSvc.resolveUrl(path); }

  isEditMode = signal(false);
  poId = signal<number | null>(null);
  isLoading = signal(false);
  isSaving = signal(false);

  // Form fields
  supplierId = signal<number | null>(null);
  warehouseId = signal<number | null>(null);
  orderDate = signal<string>(this.formatDateForInput(new Date()));
  expectedDeliveryDate = signal<string>('');
  
  items = signal<any[]>([]);

  // Computed helpers used in template
  hasSelectedItems = computed(() => this.items().some((i: any) => i.variantId && i.variantId !== 0));
  searchRowColspan = computed(() => this.hasSelectedItems() ? 7 : 1);

  // Data
  suppliers = signal<Supplier[]>([]);
  warehouses = signal<Warehouse[]>([]);

  // Product variant search per row
  variantSearchResults = signal<{ [index: number]: ProductVariantOption[] }>({});
  searchingVariants = signal<{ [index: number]: boolean }>({});
  private variantSearchSubjects: { [index: number]: Subject<string> } = {};
  variantSearchQuery = signal<{ [index: number]: string }>({});
  currentSearchPage = signal<{ [index: number]: number }>({});
  hasMoreVariants = signal<{ [index: number]: boolean }>({});
  variantInputFocused = signal<{ [index: number]: boolean }>({});
  dropdownPosition = signal<{ [index: number]: { top: number; left: number; width: number } }>({});

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private poService: PurchaseOrderService,
    private supplierService: SupplierService,
    private warehouseService: WarehouseService,
    private productService: ProductService,
    private alertService: AlertService,
    private errorHandler: ErrorHandlerService,
    private currencyService: CurrencyService
  ) {}

  ngOnInit(): void {
    // Load suppliers and warehouses immediately
    this.loadSuppliers();
    this.loadWarehouses();
    
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.isEditMode.set(true);
      this.poId.set(+id);
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
    
    if (this.warehouses().length > 0 && this.warehouseDropdown) {
      const warehouseOptions = this.warehouses().map((w: Warehouse) => ({ 
        id: w.id, 
        name: w.name, 
        address: w.address 
      }));
      this.warehouseDropdown.setOptions(warehouseOptions, false);
      this.warehouseDropdown.setHasMore(false);
    }
    
    // If data not loaded yet, try again
    if (this.suppliers().length === 0 || this.warehouses().length === 0) {
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
  
  loadWarehouses(): void {
    this.warehouseService.getAllWarehouses().subscribe({
      next: (response) => {
        if (response.data) {
          this.warehouses.set(response.data);
          // Initialize dropdown after data loaded
          if (this.warehouseDropdown) {
            const warehouseOptions = this.warehouses().map((w: Warehouse) => ({ 
              id: w.id, 
              name: w.name, 
              address: w.address 
            }));
            this.warehouseDropdown.setOptions(warehouseOptions, false);
            this.warehouseDropdown.setHasMore(false);
          }
        }
      },
      error: (err: any) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
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

  onWarehouseSearch(event: { query: string, page: number, pageSize: number }): void {
    const warehouses: DropdownOption[] = this.warehouses().map((w: Warehouse) => ({
      id: w.id,
      name: w.name,
      address: w.address
    }));
    
    // Filter by query if provided
    const filtered = event.query 
      ? warehouses.filter(w => w.name.toLowerCase().includes(event.query.toLowerCase()))
      : warehouses;
    
    this.warehouseDropdown?.setOptions(filtered, false);
    this.warehouseDropdown?.setHasMore(false);
  }

  onWarehouseLoadMore(event: { query: string, page: number, pageSize: number }): void {
    // Not needed for warehouses as we load all
  }

  onWarehouseSelect(option: DropdownOption): void {
    this.warehouseId.set(option.id);
  }

  loadPurchaseOrder(id: number): void {
    this.isLoading.set(true);
    this.poService.getById(id).subscribe({
      next: (response) => {
        const po = response.data;
        this.supplierId.set(po.supplierId);
        this.warehouseId.set(po.warehouseId);
        this.orderDate.set(this.formatDateForInput(new Date(po.orderDate)));
        if (po.expectedDelivery) {
          this.expectedDeliveryDate.set(this.formatDateForInput(new Date(po.expectedDelivery)));
        }
        
        // Convert items
        const itemsData: any[] = po.items.map((item: any) => ({
          variantId: item.variantId,
          quantity: item.quantity,
          unitPrice: item.unitPrice,
          sku: item.sku || '',
          productName: item.productName || '',
          variantName: item.variantName || ''
        }));
        this.items.set(itemsData);
        // Add empty search row at end for adding more items
        this.addItem();
        this.isLoading.set(false);
      },
      error: (err: any) => {
        this.isLoading.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.router.navigate(['/purchase-orders']);
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
    if (currentItem.variantId && value !== this.getVariantDisplayText(currentItem)) {
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
      if (!currentItem.variantId) {
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
        const variants: ProductVariantOption[] = response.data || [];
        
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

  selectVariant(variant: ProductVariantOption, index: number): void {
    // Check if this variant is already selected in another row
    const currentItems = this.items();
    const isDuplicate = currentItems.some((item: any, idx: number) => 
      idx !== index && item.variantId === variant.id
    );
    
    if (isDuplicate) {
      this.alertService.error(`${variant.productName} - ${variant.name} is already added to this order`);
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
      unitPrice: variant.costPrice || variant.finalPrice || 0,
      productName: variant.productName,
      variantName: variant.name,
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
    const items = this.items();
    items[index].quantity = quantity;
    this.items.set([...items]);
  }

  updateItemPrice(index: number, price: number): void {
    const items = this.items();
    items[index].unitPrice = price;
    this.items.set([...items]);
  }

  calculateItemTotal(item: any): number {
    return (item.quantity || 0) * (item.unitPrice || 0);
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

  calculateGrandTotal(): number {
    return this.items().reduce((sum, item) => sum + this.calculateItemTotal(item), 0);
  }

  validateForm(): boolean {
    if (!this.supplierId()) {
      this.alertService.error('Please select a supplier');
      return false;
    }
    
    if (!this.warehouseId()) {
      this.alertService.error('Please select a warehouse');
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
      warehouseId: this.warehouseId()!,
      orderDate: this.orderDate(),
      expectedDelivery: this.expectedDeliveryDate() || undefined,
      items: this.items()
        .filter((item: any) => item.variantId && item.variantId !== 0)
        .map((item: any) => ({
          variantId: item.variantId,
          quantity: item.quantity,
          unitPrice: item.unitPrice
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
              this.router.navigate(['/purchase-orders']);
            },
            error: (err: any) => {
              this.isSaving.set(false);
              this.alertService.error(this.errorHandler.extractErrorMessage(err));
            }
          });
        } else {
          this.isSaving.set(false);
          this.alertService.success(`Purchase order ${this.isEditMode() ? 'updated' : 'created'} successfully`);
          this.router.navigate(['/purchase-orders']);
        }
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
    this.router.navigate(['/purchase-orders']);
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
    if (item.productName && item.variantName) {
      return `${item.productName} - ${item.variantName}`;
    }
    return '';
  }
}
