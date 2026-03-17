import {
  Component, OnInit, OnDestroy, signal, computed, inject
} from '@angular/core';
import { CommonModule, CurrencyPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, Subscription, debounceTime, distinctUntilChanged } from 'rxjs';
import { HttpClient, HttpParams } from '@angular/common/http';

import { AuthService } from '../../services/auth.service';
import { SaleService } from '../../services/sale.service';
import { SettingsService } from '../../services/settings.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import { CartItem, CreateSaleDto, PosProduct } from '../../models/sale.model';
import { CustomerDto } from '../../models/customer.model';
import { environment } from '../../../environments/environment';

interface Category {
  id: number;
  name: string;
}

@Component({
  selector: 'app-pos-screen',
  standalone: true,
  imports: [CommonModule, FormsModule, CurrencyPipe],
  templateUrl: './pos-screen.component.html',
  styleUrls: ['./pos-screen.component.css']
})
export class PosScreenComponent implements OnInit, OnDestroy {
  private authService = inject(AuthService);
  private saleService = inject(SaleService);
  private settingsService = inject(SettingsService);
  private alertService = inject(AlertService);
  private errorHandler = inject(ErrorHandlerService);
  private http = inject(HttpClient);

  // Tax rate loaded from settings (default 10%)
  taxRate = signal<number>(0.10);

  // Product/category state
  allProducts = signal<PosProduct[]>([]);
  categories = signal<Category[]>([]);
  searchQuery = signal('');
  selectedCategory = signal<number | null>(null);
  isLoadingProducts = signal(false);

  // Cart state
  cartItems = signal<CartItem[]>([]);
  discountPercent = signal<number>(0);
  paymentMethod = signal<string>('cash');
  cashTendered = signal<number>(0);

  // Customer state
  selectedCustomer = signal<CustomerDto | null>(null);
  customerSearchQuery = signal('');
  customerResults = signal<CustomerDto[]>([]);
  showCustomerDropdown = signal(false);

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

  private customerSearchSubject = new Subject<string>();
  private subs: Subscription[] = [];

  ngOnInit(): void {
    this.loadCategories();
    this.loadProducts();
    this.loadTaxSettings();
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
                categoryName: p.categoryName
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
              categoryName: p.categoryName
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
        subtotal: product.price
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

    if (this.paymentMethod() === 'cash' && this.cashTendered() < this.totalAmount()) {
      this.alertService.error('Cash tendered is less than total amount');
      return;
    }

    const dto: CreateSaleDto = {
      outletId: user.outletId!,
      cashierId: user.id,
      customerId: this.selectedCustomer()?.id,
      items: this.cartItems().map(i => ({
        variantId: i.variantId,
        quantity: i.quantity,
        unitPrice: i.unitPrice
      })),
      discount: this.discountAmount(),
      tax: this.taxAmount(),
      paymentMethod: this.paymentMethod()
    };

    this.isProcessing.set(true);
    this.saleService.createSale(dto).subscribe({
      next: (res) => {
        this.completedSale.set(res.data || res);
        this.showReceiptModal.set(true);
        this.isProcessing.set(false);
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
}
