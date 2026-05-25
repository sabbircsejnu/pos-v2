import {
  Component, OnInit, OnDestroy, signal, computed, inject
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
  selectedCategory = signal<number | null>(null);
  isLoadingProducts = signal(false);

  // Barcode scan
  barcodeInput = signal('');
  isBarcodeScanning = signal(false);

  // Cart state
  cartItems = signal<CartItem[]>([]);
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

  // Hold/park state
  showHeldSalesPanel = signal(false);
  heldSales = this.posService.heldSales;
  isLoadingHeld = this.posService.isLoadingHeld;

  // Processing
  isProcessing = signal(false);
  showReceiptModal = signal(false);
  completedSale = signal<any>(null);

  // Computed
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

  change = computed(() =>
    Math.max(0, Math.round((this.cashTendered() - this.totalAmount()) * 100) / 100)
  );

  splitTotal = computed(() =>
    this.splitPayments().reduce((s, p) => s + (p.amount || 0), 0)
  );

  splitRemaining = computed(() =>
    Math.max(0, Math.round((this.totalAmount() - this.splitTotal()) * 100) / 100)
  );

  private customerSearchSubject = new Subject<string>();
  private subs: Subscription[] = [];

  ngOnInit(): void {
    this.loadCategories();
    this.loadProducts();
    this.loadTaxSettings();
    const user = this.authService.getUserValue();
    if (user?.outletId) {
      this.posService.loadHeldSales(user.outletId);
    }
    this.subs.push(
      this.customerSearchSubject.pipe(
        debounceTime(400),
        distinctUntilChanged()
      ).subscribe(q => this.doCustomerSearch(q))
    );
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
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

  selectCategory(id: number | null): void {
    this.selectedCategory.set(id);
  }

  addToCart(product: PosProduct): void {
    if (product.stockQty <= 0) {
      this.alertService.error('This product is out of stock');
      return;
    }
    const items = this.cartItems();
    const idx = items.findIndex(i => i.variantId === product.variantId);
    if (idx >= 0) {
      const updated = [...items];
      updated[idx] = {
        ...updated[idx],
        quantity: updated[idx].quantity + 1,
        subtotal: (updated[idx].quantity + 1) * updated[idx].unitPrice
      };
      this.cartItems.set(updated);
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
    this.selectedCustomer.set(null);
    this.customerSearchQuery.set('');
    this.paymentMethod.set('cash');
    this.splitPayments.set([]);
    this.useSplitPayment.set(false);
  }

  onCustomerSearch(query: string): void {
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
    this.selectedCustomer.set(null);
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

    if (this.paymentMethod() === 'cash' && !this.useSplitPayment() && this.cashTendered() < this.totalAmount()) {
      this.alertService.error('Cash tendered is less than total amount');
      return;
    }

    if (this.useSplitPayment()) {
      if (Math.abs(this.splitTotal() - this.totalAmount()) > 0.01) {
        this.alertService.error(`Split payment total (${this.currencyService.format(this.splitTotal())}) must equal sale total (${this.currencyService.format(this.totalAmount())})`);
        return;
      }
    }

    // Generate one-time idempotency key per checkout attempt to prevent double-submit
    const idempotencyKey = crypto.randomUUID();

    const dto: CreateSaleDto = {
      outletId: user.outletId!,
      cashierId: user.id,
      customerId: this.selectedCustomer()?.id,
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
  }

  setPaymentMethod(method: string): void {
    this.paymentMethod.set(method);
  }

  onDiscountChange(val: string): void {
    const n = parseFloat(val);
    this.discountPercent.set(isNaN(n) ? 0 : Math.min(100, Math.max(0, n)));
  }

  onCashTenderedChange(val: string): void {
    const n = parseFloat(val);
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

    const user = this.authService.getUserValue();
    if (!user?.outletId) return;

    this.isBarcodeScanning.set(true);
    this.posService.lookup(code, user.outletId, 'barcode').subscribe({
      next: (res) => {
        const result = res?.data ?? res;
        if (!result) {
          this.alertService.error(`Product not found for barcode: ${code}`);
          this.isBarcodeScanning.set(false);
          return;
        }
        // Inject into cart using the lookup result
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

        const items = this.cartItems();
        const idx = items.findIndex(i => i.variantId === product.variantId);
        if (idx >= 0) {
          const updated = [...items];
          updated[idx] = {
            ...updated[idx],
            quantity: updated[idx].quantity + 1,
            subtotal: (updated[idx].quantity + 1) * updated[idx].unitPrice
          };
          this.cartItems.set(updated);
        } else {
          this.cartItems.set([...items, {
            variantId: product.variantId,
            productName: product.name,
            variantSku: product.sku,
            quantity: 1,
            unitPrice: product.price,
            discountAmount: 0,
            subtotal: product.price,
            appliedRuleName: result.appliedRuleName,
            primaryImageThumb: product.primaryImageThumb,
          }]);
        }

        this.barcodeInput.set('');
        this.isBarcodeScanning.set(false);
      },
      error: () => {
        this.alertService.error(`No product found for: ${code}`);
        this.isBarcodeScanning.set(false);
      }
    });
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
      customerId: this.selectedCustomer()?.id,
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
