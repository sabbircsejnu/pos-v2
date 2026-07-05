import {
  Component, OnInit, OnDestroy, signal, computed, inject, HostListener
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { AppCurrencyPipe } from '../../pipes/app-currency.pipe';
import { CurrencyService } from '../../services/currency.service';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription, debounceTime, distinctUntilChanged } from 'rxjs';
import { HttpClient, HttpParams } from '@angular/common/http';

import { AuthService } from '../../services/auth.service';
import { SaleService } from '../../services/sale.service';
import { PosService } from '../../services/pos.service';
import { PosTerminalDto } from '../../services/pos.service';
import { CustomerService } from '../../services/customer.service';
import { SettingsService } from '../../services/settings.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import {
  CartItem, CreateSaleDto, CreateSalePaymentDto, HeldSaleDto, PosProduct
} from '../../models/sale.model';
import { CustomerDto } from '../../models/customer.model';
import { ProductImageService } from '../../services/product-image.service';
import { environment } from '../../../environments/environment';

interface Category {
  id: number;
  name: string;
}

interface QuickCustomerForm {
  salutation: string;
  firstName: string;
  lastName: string;
  displayName: string;
  email: string;
  phone: string;
  mobile: string;
  address: string;
}

@Component({
  selector: 'app-pos-screen',
  standalone: true,
  imports: [CommonModule, FormsModule, AppCurrencyPipe],
  templateUrl: './pos-screen.component.html',
  styleUrls: ['./pos-screen.component.css']
})
export class PosScreenComponent implements OnInit, OnDestroy {
  private authService = inject(AuthService);
  private saleService = inject(SaleService);
  private posService  = inject(PosService);
  private customerService = inject(CustomerService);
  private settingsService = inject(SettingsService);
  private alertService = inject(AlertService);
  private errorHandler = inject(ErrorHandlerService);
  private http = inject(HttpClient);
  private imageSvc = inject(ProductImageService);
  private currencyService = inject(CurrencyService);

  resolveImage(path?: string | null): string {
    return this.imageSvc.resolveUrl(path);
  }

  // Tax rate loaded from settings (default 10%)
  taxRate = signal<number>(0.10);

  // Product/category state
  allProducts = signal<PosProduct[]>([]);
  categories = signal<Category[]>([]);
  searchQuery = signal('');
  smartSearchQuery = signal('');
  showSmartSearchDropdown = signal(false);
  showProductBrowser = signal(false);
  selectedCategory = signal<number | null>(null);
  isLoadingProducts = signal(false);

  // Barcode scan
  barcodeInput = signal('');
  isBarcodeScanning = signal(false);

  // Cart state
  cartItems = signal<CartItem[]>([]);
  duplicateCartVariantId = signal<number | null>(null);
  discountPercent = signal<number>(0);
  paymentMethod = signal<string>('cash');
  cashTendered = signal<number>(0);

  // Split payment state
  splitPayments = signal<CreateSalePaymentDto[]>([]);
  useSplitPayment = signal(false);

  // Customer state
  selectedCustomer = signal<CustomerDto | null>(null);
  customerSearchQuery = signal('');
  customerResults = signal<CustomerDto[]>([]);
  showCustomerDropdown = signal(false);
  showQuickCustomerModal = signal(false);
  isSavingCustomer = signal(false);
  quickCustomerForm = signal<QuickCustomerForm>({
    salutation: '',
    firstName: '',
    lastName: '',
    displayName: '',
    email: '',
    phone: '',
    mobile: '',
    address: ''
  });

  // POS header context
  shiftStartedAt = signal<Date>(new Date());
  now = signal<Date>(new Date());
  salesOrderDate = signal<string>('');
  saleSequence = signal(1);
  currentUser = signal<any | null>(null);

  // Hold/park state
  showHeldSalesPanel = signal(false);
  heldSales = this.posService.heldSales;
  isLoadingHeld = this.posService.isLoadingHeld;

  terminals = signal<PosTerminalDto[]>([]);
  selectedTerminalId = signal<number | null>(null);

  // Processing
  isProcessing = signal(false);
  showReceiptModal = signal(false);
  completedSale = signal<any>(null);

  // Computed
  smartSearchResults = computed(() => {
    const q = this.smartSearchQuery().toLowerCase().trim();
    if (!q) return [];

    return this.allProducts()
      .filter((p) =>
        p.name.toLowerCase().includes(q) ||
        p.sku.toLowerCase().includes(q) ||
        (p.categoryName ?? '').toLowerCase().includes(q)
      )
      .slice(0, 8);
  });

  filteredProducts = computed(() => {
    let products = this.allProducts();
    const q = this.searchQuery().toLowerCase().trim();
    const cat = this.selectedCategory();

    if (q) {
      products = products.filter(p =>
        p.name.toLowerCase().includes(q) || p.sku.toLowerCase().includes(q)
      );
    }
    if (cat !== null) {
      products = products.filter(p => p.categoryId === cat);
    }
    return products;
  });

  subtotal = computed(() =>
    this.cartItems().reduce((sum, item) => sum + item.subtotal, 0)
  );

  discountAmount = computed(() =>
    Math.round(this.subtotal() * this.discountPercent() / 100 * 100) / 100
  );

  taxAmount = computed(() =>
    Math.round((this.subtotal() - this.discountAmount()) * this.taxRate() * 100) / 100
  );

  totalAmount = computed(() =>
    Math.round((this.subtotal() - this.discountAmount() + this.taxAmount()) * 100) / 100
  );

  roundOff = computed(() => 0);

  grandTotal = computed(() =>
    Math.round((this.totalAmount() + this.roundOff()) * 100) / 100
  );

  payableAmount = computed(() => this.grandTotal());

  itemCount = computed(() => this.cartItems().length);

  quantityCount = computed(() =>
    this.cartItems().reduce((sum, item) => sum + item.quantity, 0)
  );

  dueAmount = computed(() => {
    if (this.useSplitPayment()) {
      return Math.max(0, Math.round((this.payableAmount() - this.splitTotal()) * 100) / 100);
    }
    if (this.paymentMethod() === 'cash') {
      return Math.max(0, Math.round((this.payableAmount() - this.cashTendered()) * 100) / 100);
    }
    return 0;
  });

  headerOutlet = computed(() => this.currentUser()?.outletName ?? 'Outlet not assigned');
  headerCashier = computed(() => this.currentUser()?.fullName ?? this.currentUser()?.username ?? 'Cashier');
  headerTerminal = computed(() => {
    const selectedId = this.selectedTerminalId();
    const terminal = this.terminals().find((t) => t.id === selectedId);
    return terminal ? `${terminal.name} (${terminal.code})` : 'No terminal selected';
  });

  canViewCustomers = computed(() =>
    this.authService.hasPermission('customers.view') || this.authService.hasPermission('*')
  );

  canCreateCustomers = computed(() =>
    this.authService.hasPermission('customers.create') || this.authService.hasPermission('*')
  );

  canBackdateSale = computed(() =>
    this.authService.hasPermission('sales.backdate') || this.authService.hasPermission('*')
  );

  saleOrderNumber = computed(() => {
    const now = this.now();
    const y = now.getFullYear();
    const m = String(now.getMonth() + 1).padStart(2, '0');
    const d = String(now.getDate()).padStart(2, '0');
    const seq = String(this.saleSequence()).padStart(3, '0');
    return `SO-${y}${m}${d}-${seq}`;
  });

  shiftDuration = computed(() => {
    const ms = this.now().getTime() - this.shiftStartedAt().getTime();
    const totalSeconds = Math.max(0, Math.floor(ms / 1000));
    const hours = Math.floor(totalSeconds / 3600);
    const minutes = Math.floor((totalSeconds % 3600) / 60);
    const seconds = totalSeconds % 60;
    return `${String(hours).padStart(2, '0')}h ${String(minutes).padStart(2, '0')}m ${String(seconds).padStart(2, '0')}s`;
  });

  canCompleteSale = computed(() => {
    if (this.cartItems().length === 0 || this.isProcessing() || !this.selectedTerminalId()) {
      return false;
    }

    if (this.useSplitPayment()) {
      return Math.abs(this.splitTotal() - this.payableAmount()) <= 0.01;
    }

    if (this.paymentMethod() === 'cash') {
      return this.cashTendered() >= this.payableAmount();
    }

    return true;
  });

  cashBreakdown = computed(() => {
    if (this.useSplitPayment()) {
      return this.splitPayments()
        .filter((p) => p.method === 'cash')
        .reduce((sum, p) => sum + (p.amount || 0), 0);
    }
    return this.paymentMethod() === 'cash' ? this.payableAmount() : 0;
  });

  cardBreakdown = computed(() => {
    if (this.useSplitPayment()) {
      return this.splitPayments()
        .filter((p) => p.method === 'card')
        .reduce((sum, p) => sum + (p.amount || 0), 0);
    }
    return this.paymentMethod() === 'card' ? this.payableAmount() : 0;
  });

  mobileBreakdown = computed(() => {
    if (this.useSplitPayment()) {
      return this.splitPayments()
        .filter((p) => p.method === 'mobile')
        .reduce((sum, p) => sum + (p.amount || 0), 0);
    }
    return this.paymentMethod() === 'mobile' ? this.payableAmount() : 0;
  });

  change = computed(() =>
    Math.max(0, Math.round((this.cashTendered() - this.payableAmount()) * 100) / 100)
  );

  splitTotal = computed(() =>
    this.splitPayments().reduce((s, p) => s + (p.amount || 0), 0)
  );

  splitRemaining = computed(() =>
    Math.max(0, Math.round((this.payableAmount() - this.splitTotal()) * 100) / 100)
  );

  private customerSearchSubject = new Subject<string>();
  private subs: Subscription[] = [];

  private readonly walkInCustomer: CustomerDto = {
    id: 0,
    name: 'Walk-in Customer',
    loyaltyPoints: 0,
    createdAt: new Date(0).toISOString()
  };

  ngOnInit(): void {
    this.currentUser.set(this.authService.getUserValue());
    this.salesOrderDate.set(this.formatDateForInput(new Date()));
    this.selectedCustomer.set(this.walkInCustomer);
    this.loadCategories();
    this.loadProducts();
    this.loadTaxSettings();
    const user = this.authService.getUserValue();
    if (user?.outletId) {
      this.posService.loadHeldSales(user.outletId);
      this.loadTerminals(user.outletId);
    }
    this.subs.push(
      this.customerSearchSubject.pipe(
        debounceTime(400),
        distinctUntilChanged()
      ).subscribe(q => this.doCustomerSearch(q))
    );

    const intervalId = window.setInterval(() => {
      this.now.set(new Date());
    }, 1000);

    this.subs.push(
      new Subscription(() => clearInterval(intervalId))
    );
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
  }

  private formatDateForInput(date: Date): string {
    const y = date.getFullYear();
    const m = String(date.getMonth() + 1).padStart(2, '0');
    const d = String(date.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }

  private buildRequestedSalesDate(): string | undefined {
    if (!this.canBackdateSale()) {
      return undefined;
    }

    const date = this.salesOrderDate().trim();
    if (!date) {
      return undefined;
    }

    // Keep date-only value without timezone conversion drift.
    return `${date}T00:00:00`;
  }

  @HostListener('window:keydown', ['$event'])
  handleKeyboardShortcuts(event: KeyboardEvent): void {
    switch (event.key) {
      case 'F2':
        event.preventDefault();
        this.focusSmartSearch();
        break;
      case 'F4':
        event.preventDefault();
        this.openProductBrowser();
        break;
      case 'F6':
        event.preventDefault();
        this.holdSale();
        break;
      case 'F7':
        event.preventDefault();
        this.toggleHeldSalesPanel();
        break;
      case 'F9':
        event.preventDefault();
        this.completeSale();
        break;
      case '1':
        if (event.ctrlKey) {
          event.preventDefault();
          this.useSplitPayment.set(false);
          this.setPaymentMethod('cash');
        }
        break;
      case '2':
        if (event.ctrlKey) {
          event.preventDefault();
          this.useSplitPayment.set(false);
          this.setPaymentMethod('card');
        }
        break;
      case '3':
        if (event.ctrlKey) {
          event.preventDefault();
          this.useSplitPayment.set(false);
          this.setPaymentMethod('mobile');
        }
        break;
      default:
        break;
    }
  }

  private loadTaxSettings(): void {
    this.settingsService.getTaxSettings().subscribe({
      next: (res) => {
        const data = res?.data;
        if (data && data.taxEnabled && data.defaultTaxRate > 0) {
          this.taxRate.set(data.defaultTaxRate / 100);
        } else if (data && !data.taxEnabled) {
          this.taxRate.set(0);
        }
      }
    });
  }

  private loadCategories(): void {
    this.http.get<any>(`${environment.apiUrl}/categories`).subscribe({
      next: (res) => this.categories.set(res.data || res || []),
      error: () => {}
    });
  }

  private loadProducts(): void {
    this.isLoadingProducts.set(true);
    const params = new HttpParams()
      .set('pageSize', '200')
      .set('isActive', 'true');

    this.http.get<any>(`${environment.apiUrl}/products`, { params }).subscribe({
      next: (res) => {
        const raw = res.data?.products || res.data || res || [];
        const mapped: PosProduct[] = [];
        for (const p of raw) {
          if (p.variants && p.variants.length > 0) {
            for (const v of p.variants) {
              mapped.push({
                variantId: v.id,
                productId: p.id,
                name: p.name + (v.name ? ` - ${v.name}` : ''),
                sku: v.sku || p.sku || '',
                price: v.sellingPrice ?? v.price ?? p.sellingPrice ?? 0,
                stockQty: v.stockQuantity ?? v.stock ?? 0,
                categoryId: p.categoryId,
                categoryName: p.categoryName,
                primaryImageThumb: p.primaryImageThumb,
              });
            }
          } else {
            mapped.push({
              variantId: p.defaultVariantId ?? p.id,
              productId: p.id,
              name: p.name,
              sku: p.sku || '',
              price: p.sellingPrice ?? p.price ?? 0,
              stockQty: p.stockQuantity ?? p.stock ?? 0,
              categoryId: p.categoryId,
              categoryName: p.categoryName,
              primaryImageThumb: p.primaryImageThumb,
            });
          }
        }
        this.allProducts.set(mapped);
        this.isLoadingProducts.set(false);
      },
      error: () => this.isLoadingProducts.set(false)
    });
  }

  private loadTerminals(outletId: number): void {
    this.posService.getTerminals(outletId).subscribe({
      next: (res) => {
        const data = (res?.data ?? res ?? []) as PosTerminalDto[];
        this.terminals.set(data);
        const preferred = data.find(t => t.isDefault) ?? data[0];
        this.selectedTerminalId.set(preferred?.id ?? null);
      },
      error: () => {
        this.terminals.set([]);
        this.selectedTerminalId.set(null);
      }
    });
  }

  onTerminalChange(value: string): void {
    const id = Number(value);
    this.selectedTerminalId.set(Number.isFinite(id) && id > 0 ? id : null);
  }

  focusSmartSearch(): void {
    const element = document.getElementById('smart-pos-search') as HTMLInputElement | null;
    element?.focus();
    element?.select();
  }

  openProductBrowser(): void {
    this.showProductBrowser.set(true);
  }

  closeProductBrowser(): void {
    this.showProductBrowser.set(false);
  }

  selectCategory(id: number | null): void {
    this.selectedCategory.set(id);
  }

  isVariantInCart(variantId: number): boolean {
    return this.cartItems().some(item => item.variantId === variantId);
  }

  highlightCartVariant(variantId: number): void {
    this.duplicateCartVariantId.set(variantId);
    setTimeout(() => this.duplicateCartVariantId.set(null), 1500);
  }

  onProductCardClick(product: PosProduct): void {
    if (product.stockQty <= 0) {
      this.alertService.error('This product is out of stock');
      return;
    }

    this.addToCart(product);
  }

  addToCart(product: PosProduct): void {
    if (product.stockQty <= 0) {
      this.alertService.error('This product is out of stock');
      return;
    }
    const items = this.cartItems();
    const idx = items.findIndex(i => i.variantId === product.variantId);
    if (idx >= 0) {
      const nextQty = items[idx].quantity + 1;
      const updated = [...items];
      updated[idx] = {
        ...updated[idx],
        quantity: nextQty,
        subtotal: nextQty * updated[idx].unitPrice
      };
      this.cartItems.set(updated);
      this.highlightCartVariant(product.variantId);
      return;
    } else {
      this.cartItems.set([...items, {
        variantId: product.variantId,
        productName: product.name,
        variantSku: product.sku,
        quantity: 1,
        unitPrice: product.price,
        discountAmount: 0,
        subtotal: product.price,
        primaryImageThumb: product.primaryImageThumb,
      }]);
    }

    this.showSmartSearchDropdown.set(false);
  }

  onSmartSearchChange(value: string): void {
    this.smartSearchQuery.set(value);
    this.showSmartSearchDropdown.set(!!value.trim());
  }

  onSmartSearchSubmit(): void {
    const query = this.smartSearchQuery().trim();
    if (!query) return;

    const firstMatch = this.smartSearchResults()[0];
    if (firstMatch && !this.looksLikeCode(query)) {
      this.addToCart(firstMatch);
      this.smartSearchQuery.set('');
      this.showSmartSearchDropdown.set(false);
      return;
    }

    this.lookupAndAddProduct(query);
  }

  selectSmartSearchResult(product: PosProduct): void {
    this.addToCart(product);
    this.smartSearchQuery.set('');
    this.showSmartSearchDropdown.set(false);
    this.focusSmartSearch();
  }

  private looksLikeCode(value: string): boolean {
    const q = value.trim();
    return /^\d{5,}$/.test(q) || /^[A-Za-z0-9\-]{4,}$/.test(q);
  }

  private lookupAndAddProduct(code: string): void {
    const user = this.authService.getUserValue();
    if (!user?.outletId) return;

    this.isBarcodeScanning.set(true);
    this.posService.lookup(code, user.outletId, 'barcode').subscribe({
      next: (res) => {
        const result = res?.data ?? res;
        if (!result) {
          this.trySkuLookup(code, user.outletId!);
          return;
        }

        this.addLookupResultToCart(result);
      },
      error: () => this.trySkuLookup(code, user.outletId!)
    });
  }

  private trySkuLookup(code: string, outletId: number): void {
    this.posService.lookup(code, outletId, 'sku').subscribe({
      next: (res) => {
        const result = res?.data ?? res;
        if (!result) {
          this.alertService.error(`No product found for: ${code}`);
          this.isBarcodeScanning.set(false);
          return;
        }

        this.addLookupResultToCart(result);
      },
      error: () => {
        this.alertService.error(`No product found for: ${code}`);
        this.isBarcodeScanning.set(false);
      }
    });
  }

  private addLookupResultToCart(result: any): void {
    const product: PosProduct = {
      variantId: result.variantId,
      productId: result.productId,
      name: result.productName + (result.variantName ? ` - ${result.variantName}` : ''),
      sku: result.sku,
      price: result.effectivePrice,
      stockQty: result.stockQty,
      primaryImageThumb: result.imageUrl,
    };

    if (!result.inStock) {
      this.alertService.error(`${product.name} is out of stock`);
      this.isBarcodeScanning.set(false);
      return;
    }

    this.addToCart(product);
    this.smartSearchQuery.set('');
    this.barcodeInput.set('');
    this.showSmartSearchDropdown.set(false);
    this.isBarcodeScanning.set(false);
  }

  quickAddCustomer(): void {
    if (!this.canCreateCustomers()) {
      this.alertService.error('You do not have permission to create customers.');
      return;
    }

    this.quickCustomerForm.set({
      salutation: '',
      firstName: '',
      lastName: '',
      displayName: '',
      email: '',
      phone: '',
      mobile: '',
      address: ''
    });
    this.showQuickCustomerModal.set(true);
  }

  closeQuickCustomerModal(): void {
    if (this.isSavingCustomer()) return;
    this.showQuickCustomerModal.set(false);
  }

  updateQuickCustomerField(field: keyof QuickCustomerForm, value: string): void {
    this.quickCustomerForm.update((form) => ({
      ...form,
      [field]: value,
    }));
  }

  saveQuickCustomer(): void {
    if (!this.canCreateCustomers()) {
      this.alertService.error('You do not have permission to create customers.');
      return;
    }

    const form = this.quickCustomerForm();
    const name = form.displayName.trim() || `${form.firstName} ${form.lastName}`.trim();
    const mobile = form.mobile.trim();

    if (!name) {
      this.alertService.error('Display Name is required.');
      return;
    }

    if (!mobile) {
      this.alertService.error('Mobile is required.');
      return;
    }

    this.isSavingCustomer.set(true);

    this.customerService.create({
      name,
      phone: mobile,
      email: form.email.trim() || undefined,
    }).subscribe({
      next: (res) => {
        const created = (res?.data ?? res) as CustomerDto;
        if (created?.id) {
          this.selectCustomer(created);
          this.alertService.success('Customer created and selected.');
        }
        this.isSavingCustomer.set(false);
        this.showQuickCustomerModal.set(false);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isSavingCustomer.set(false);
      }
    });
  }

  removeFromCart(index: number): void {
    const updated = this.cartItems().filter((_, i) => i !== index);
    this.cartItems.set(updated);
  }

  increaseQty(index: number): void {
    const updated = [...this.cartItems()];
    updated[index] = {
      ...updated[index],
      quantity: updated[index].quantity + 1,
      subtotal: (updated[index].quantity + 1) * updated[index].unitPrice
    };
    this.cartItems.set(updated);
  }

  decreaseQty(index: number): void {
    const items = this.cartItems();
    if (items[index].quantity <= 1) {
      this.removeFromCart(index);
      return;
    }
    const updated = [...items];
    updated[index] = {
      ...updated[index],
      quantity: updated[index].quantity - 1,
      subtotal: (updated[index].quantity - 1) * updated[index].unitPrice
    };
    this.cartItems.set(updated);
  }

  clearCart(): void {
    this.cartItems.set([]);
    this.discountPercent.set(0);
    this.cashTendered.set(0);
    this.selectedCustomer.set(this.walkInCustomer);
    this.customerSearchQuery.set('');
    this.paymentMethod.set('cash');
    this.splitPayments.set([]);
    this.useSplitPayment.set(false);
  }

  onCustomerSearch(query: string): void {
    if (!this.canViewCustomers()) {
      this.alertService.error('You do not have permission to view customers.');
      return;
    }

    this.customerSearchQuery.set(query);
    if (!query.trim()) {
      this.customerResults.set([]);
      this.showCustomerDropdown.set(false);
      return;
    }
    this.customerSearchSubject.next(query);
  }

  private doCustomerSearch(query: string): void {
    const params = new HttpParams().set('q', query);
    this.http.get<any>(`${environment.apiUrl}/customers/search`, { params }).subscribe({
      next: (res) => {
        this.customerResults.set(res.data || res || []);
        this.showCustomerDropdown.set(true);
      },
      error: () => {}
    });
  }

  selectCustomer(customer: CustomerDto): void {
    this.selectedCustomer.set(customer);
    this.customerSearchQuery.set(customer.name);
    this.showCustomerDropdown.set(false);
    this.customerResults.set([]);
  }

  clearCustomer(): void {
    this.selectedCustomer.set(this.walkInCustomer);
    this.customerSearchQuery.set('');
    this.customerResults.set([]);
    this.showCustomerDropdown.set(false);
  }

  completeSale(): void {
    if (this.cartItems().length === 0) {
      this.alertService.error('Cart is empty');
      return;
    }
    if (this.isProcessing()) return;

    const user = this.authService.getUserValue();
    if (!user) {
      this.alertService.error('User not authenticated');
      return;
    }

    if (!user.outletId) {
      this.alertService.error('Your account is not assigned to an outlet. Please contact an administrator.');
      return;
    }

    if (!this.selectedTerminalId()) {
      this.alertService.error('Please select a POS terminal before completing sale.');
      return;
    }

    if (this.paymentMethod() === 'cash' && !this.useSplitPayment() && this.cashTendered() < this.payableAmount()) {
      this.alertService.error('Cash tendered is less than total amount');
      return;
    }

    if (this.useSplitPayment()) {
      if (Math.abs(this.splitTotal() - this.payableAmount()) > 0.01) {
        this.alertService.error(`Split payment total (${this.currencyService.format(this.splitTotal())}) must equal sale total (${this.currencyService.format(this.payableAmount())})`);
        return;
      }
    }

    // Generate one-time idempotency key per checkout attempt to prevent double-submit
    const idempotencyKey = crypto.randomUUID();

    const dto: CreateSaleDto = {
      outletId: user.outletId!,
      terminalId: this.selectedTerminalId()!,
      cashierId: user.id,
      customerId: (this.selectedCustomer()?.id ?? 0) > 0 ? this.selectedCustomer()!.id : undefined,
      salesDate: this.buildRequestedSalesDate(),
      items: this.cartItems().map(i => ({
        variantId: i.variantId,
        quantity: i.quantity,
        unitPrice: i.unitPrice,
        discountAmount: i.discountAmount,
        appliedRuleName: i.appliedRuleName
      })),
      discount: this.discountAmount(),
      tax: this.taxAmount(),
      paymentMethod: this.paymentMethod(),
      idempotencyKey,
      payments: this.useSplitPayment() ? this.splitPayments() : undefined
    };

    this.isProcessing.set(true);
    this.saleService.createSale(dto).subscribe({
      next: (res) => {
        this.completedSale.set(res.data || res);
        this.showReceiptModal.set(true);
        this.isProcessing.set(false);
        this.saleSequence.update((v) => v + 1);
        // Refresh held sales count after a successful sale
        this.posService.loadHeldSales(user.outletId!);
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isProcessing.set(false);
      }
    });
  }

  printReceipt(): void {
    window.print();
  }

  startNewSale(): void {
    this.clearCart();
    this.showReceiptModal.set(false);
    this.completedSale.set(null);
    this.saleSequence.update((v) => v + 1);
    this.focusSmartSearch();
  }

  setPaymentMethod(method: string): void {
    this.paymentMethod.set(method);
  }

  onDiscountChange(val: string): void {
    const n = parseFloat(String(val));
    this.discountPercent.set(isNaN(n) ? 0 : Math.min(100, Math.max(0, n)));
  }

  onCashTenderedChange(val: string): void {
    const n = parseFloat(String(val));
    this.cashTendered.set(isNaN(n) ? 0 : n);
  }

  // ── Barcode Scan ──────────────────────────────────────────────────────────

  onBarcodeInputChange(val: string): void {
    this.barcodeInput.set(val);
  }

  /** Called when user presses Enter in the barcode input field. */
  onBarcodeScan(): void {
    const code = this.barcodeInput().trim();
    if (!code) return;
    this.lookupAndAddProduct(code);
  }

  // ── Hold / Park ───────────────────────────────────────────────────────────

  toggleHeldSalesPanel(): void {
    const user = this.authService.getUserValue();
    if (user?.outletId) {
      this.posService.loadHeldSales(user.outletId);
    }
    this.showHeldSalesPanel.update(v => !v);
  }

  holdSale(): void {
    if (this.cartItems().length === 0) {
      this.alertService.error('Cart is empty — nothing to hold');
      return;
    }
    const user = this.authService.getUserValue();
    if (!user?.outletId) return;

    this.posService.holdSale({
      outletId: user.outletId,
      cashierId: user.id,
      customerId: (this.selectedCustomer()?.id ?? 0) > 0 ? this.selectedCustomer()!.id : undefined,
      items: this.cartItems().map(i => ({
        variantId: i.variantId,
        productName: i.productName,
        variantSku: i.variantSku,
        quantity: i.quantity,
        unitPrice: i.unitPrice,
        discountAmount: i.discountAmount,
        appliedRuleName: i.appliedRuleName
      })),
      discountPercent: this.discountPercent(),
      paymentMethod: this.paymentMethod()
    }).subscribe({
      next: () => {
        this.alertService.success('Sale held — cart cleared');
        this.clearCart();
        this.posService.loadHeldSales(user.outletId!);
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  recallHeldSale(held: HeldSaleDto): void {
    this.posService.recallHeldSale(held.id).subscribe({
      next: (res) => {
        const data: HeldSaleDto = res?.data ?? res;
        // Restore cart from held sale
        this.cartItems.set(data.items.map(i => ({
          variantId: i.variantId,
          productName: i.productName,
          variantSku: i.variantSku,
          quantity: i.quantity,
          unitPrice: i.unitPrice,
          discountAmount: i.discountAmount,
          subtotal: i.quantity * i.unitPrice - i.discountAmount,
          appliedRuleName: i.appliedRuleName
        })));
        this.discountPercent.set(data.discountPercent);
        this.paymentMethod.set(data.paymentMethod);
        this.selectedCustomer.set(
          data.customerId && data.customerId > 0
            ? {
                id: data.customerId,
                name: data.customerName ?? `Customer #${data.customerId}`,
                loyaltyPoints: 0,
                createdAt: new Date(0).toISOString()
              }
            : this.walkInCustomer
        );
        this.showHeldSalesPanel.set(false);

        // Delete the held sale now that it's been recalled
        this.posService.discardHeldSale(held.id).subscribe();
        const user = this.authService.getUserValue();
        if (user?.outletId) this.posService.loadHeldSales(user.outletId);
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  discardHeldSale(held: HeldSaleDto): void {
    this.posService.discardHeldSale(held.id).subscribe({
      next: () => {
        const user = this.authService.getUserValue();
        if (user?.outletId) this.posService.loadHeldSales(user.outletId);
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  // ── Split Payment ─────────────────────────────────────────────────────────

  toggleSplitPayment(): void {
    this.useSplitPayment.update(v => !v);
    if (this.useSplitPayment()) {
      // Seed with single full payment as a starting point
      this.splitPayments.set([{ method: this.paymentMethod(), amount: this.totalAmount() }]);
    } else {
      this.splitPayments.set([]);
    }
  }

  addSplitRow(): void {
    const remaining = this.splitRemaining();
    this.splitPayments.update(rows => [...rows, { method: 'cash', amount: remaining }]);
  }

  removeSplitRow(index: number): void {
    this.splitPayments.update(rows => rows.filter((_, i) => i !== index));
  }

  updateSplitMethod(index: number, method: string): void {
    this.splitPayments.update(rows => {
      const updated = [...rows];
      updated[index] = { ...updated[index], method };
      return updated;
    });
  }

  updateSplitAmount(index: number, val: string): void {
    const n = parseFloat(val);
    this.splitPayments.update(rows => {
      const updated = [...rows];
      updated[index] = { ...updated[index], amount: isNaN(n) ? 0 : n };
      return updated;
    });
  }

  updateSplitTendered(index: number, val: string): void {
    const n = parseFloat(val);
    this.splitPayments.update(rows => {
      const updated = [...rows];
      updated[index] = { ...updated[index], tendered: isNaN(n) ? undefined : n };
      return updated;
    });
  }
}
