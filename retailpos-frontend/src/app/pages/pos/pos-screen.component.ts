import { UiDateInputDirective } from '../../components/shared/ui/ui-date-input.directive';
import { UiAlertComponent } from '../../components/shared/ui/ui-alert.component';
import { AppDatePipe } from '../../pipes/app-date.pipe';
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
import { CashSessionDto, CartPriceResultItem, EligibleSalespersonDto, PosTerminalDto } from '../../services/pos.service';
import { CashSessionService } from '../../services/cash-session.service';
import { Router } from '@angular/router';
import { PosDiscountAuditRequest } from '../../services/pos.service';
import { CustomerService } from '../../services/customer.service';
import { SettingsService } from '../../services/settings.service';
import { AlertService } from '../../services/alert.service';
import { ErrorHandlerService } from '../../services/error-handler.service';
import {
  CartItem, CreateSaleDto, CreateSalePaymentDto, HeldSaleDto, PosBusinessSettingsDto, PosProduct, ReceiptDocumentDto, ReceiptPrintAttemptDto
} from '../../models/sale.model';
import { CustomerDto } from '../../models/customer.model';
import { ProductImageService } from '../../services/product-image.service';
import { environment } from '../../../environments/environment';
import { ReceiptSettings } from '../../models/settings.model';
import { calculateTax, effectiveLineTaxRate, roundMoney } from './pos-tax';
import { percentToDiscountAmount, validateManualDiscount } from './pos-discount';
import {
  CART_UPDATED_MESSAGE, DISCOUNT_NOT_PERMITTED_MESSAGE, NO_ACTIVE_TERMINAL_MESSAGE, TERMINAL_REQUIRED_MESSAGE,
  discountLimitMessage, mapPosSaleError
} from './pos-sale-errors';
import { PosTerminalAvailability, resolveTerminalSelection } from './pos-terminals';

type TerminalsState = PosTerminalAvailability | 'loading' | 'error';

interface CheckoutNotice {
  variant: 'danger' | 'warning' | 'info';
  text: string;
}

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
  imports: [AppDatePipe, UiDateInputDirective, UiAlertComponent, CommonModule, FormsModule, AppCurrencyPipe],
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
  private router = inject(Router);
  private cashSessions = inject(CashSessionService);

  resolveImage(path?: string | null): string {
    return this.imageSvc.resolveUrl(path);
  }

  // Business POS settings (GET /api/settings/pos). The server is authoritative for tax and discount limits.
  posSettings = signal<PosBusinessSettingsDto | null>(null);
  pricesIncludeTax = signal(false);
  checkoutNotice = signal<CheckoutNotice | null>(null);
  completedTaxInclusive = signal(false);

  // Product/category state
  allProducts = signal<PosProduct[]>([]);
  categories = signal<Category[]>([]);
  searchQuery = signal('');
  smartSearchQuery = signal('');
  showSmartSearchDropdown = signal(false);
  showProductBrowser = signal(false);
  selectedCategory = signal<number | null>(null);
  isLoadingProducts = signal(false);
  lowStockThreshold = signal<number>(5);
  outletStockByVariant = signal<Record<number, number>>({});
  stockLoadingByVariant = signal<Record<number, boolean>>({});

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
  sessionBusinessDate = signal<Date | null>(null);
  now = signal<Date>(new Date());
  readonly todayForInput = computed(() => this.formatDateForInput(this.now()));
  salesOrderDate = signal<string>('');
  saleSequence = signal(1);
  currentUser = signal<any | null>(null);

  // Hold/park state
  showHeldSalesPanel = signal(false);
  heldSales = this.posService.heldSales;
  isLoadingHeld = this.posService.isLoadingHeld;

  terminals = signal<PosTerminalDto[]>([]);
  selectedTerminalId = signal<number | null>(null);
  terminalsState = signal<TerminalsState>('loading');

  /** Why checkout is blocked on terminal grounds; empty when a terminal is selected or terminals are still loading. */
  terminalPrompt = computed(() => {
    if (this.selectedTerminalId()) return '';
    switch (this.terminalsState()) {
      case 'none': return NO_ACTIVE_TERMINAL_MESSAGE;
      case 'multiple': return 'Select the POS terminal for this sale before completing it.';
      case 'error': return 'Terminals could not be loaded. Reload the page to try again.';
      default: return '';
    }
  });
  activeCashSession = signal<CashSessionDto | null>(null);
  showOpenCashModal = signal(false);
  openingCashAmount = signal<number>(0);
  openingNotes = signal<string>('');
  isLoadingSession = signal(false);
  /** Server-derived opening context for the Open Cash dialog: the business date is never taken from the browser clock. */
  openContextBusinessDate = signal<string | null>(null);
  openContextMaxHours = signal<number | null>(null);

  // Salesperson (Phase 4): eligible outlet users; the server re-validates the choice when the sale is created.
  salespersons = signal<EligibleSalespersonDto[]>([]);
  selectedSalespersonId = signal<number | null>(null);
  private defaultSalespersonId: number | null = null;

  isFullscreen = signal(false);
  /** Prints of the receipt currently shown; the first is recorded as Printed, later ones as Reprinted. */
  receiptPrintCount = signal(0);

  // Processing
  isProcessing = signal(false);
  private checkoutAttempt: { signature: string; key: string } | null = null;
  showReceiptModal = signal(false);
  completedSale = signal<any>(null);
  receiptDocument = signal<ReceiptDocumentDto | null>(null);
  receiptOutputMode = signal<'80mm' | 'A4'>('80mm');
  receiptCopies = signal<number>(1);
  receiptSettings = signal<ReceiptSettings>({
    header: 'Thank you for shopping!',
    footer: 'Please come again',
    showLogo: true,
    showBarcode: false,
    showQr: false,
    defaultReceiptSize: '80mm',
    paperSize: '80mm',
    autoPrintAfterSale: true,
    receiptCopies: 1,
    thankYouMessage: 'Thank you for your purchase!',
    returnPolicy: 'Items can be returned according to store policy.'
  });

  // Computed
  smartSearchResults = computed(() => {
    const q = this.smartSearchQuery().toLowerCase().trim();
    if (!q) return [];

    return this.allProducts()
      .filter((p) => this.matchesSmartSearch(p, q))
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

  taxEnabled = computed(() => this.posSettings()?.taxEnabled ?? true);
  taxName = computed(() => this.posSettings()?.taxName?.trim() || 'Tax');

  canApplyDiscount = computed(() =>
    this.posSettings()?.canApplyDiscount ?? this.authService.hasPermission('sales.discount.apply')
  );

  canOverrideDiscountLimit = computed(() =>
    this.posSettings()?.canOverrideDiscountLimit ?? this.authService.hasPermission('sales.discount.override_limit')
  );

  /** Business manual-discount cap in percent; null while the settings are unknown (the server then decides). */
  discountLimitPercent = computed<number | null>(() => {
    const limit = this.posSettings()?.maxManualDiscountPercent;
    return limit === undefined || limit === null ? null : Number(limit);
  });

  discountLimitText = computed(() => {
    const limit = this.discountLimitPercent();
    if (limit === null) return '';
    return limit > 0 ? `Manual discount limit: ${Number(limit.toFixed(2))}%` : 'Manual discounts are not enabled';
  });

  discountAmount = computed(() => {
    const percent = this.discountPercent();
    if (percent <= 0) return 0;
    return percentToDiscountAmount(this.subtotal(), percent, this.discountLimitPercent(), this.canOverrideDiscountLimit());
  });

  manualDiscountCheck = computed(() => validateManualDiscount({
    lines: this.cartItems().map(item => ({
      quantity: item.quantity,
      unitPrice: item.unitPrice,
      lineDiscount: item.discountAmount,
    })),
    invoiceDiscount: this.discountAmount(),
    canApply: this.canApplyDiscount(),
    canOverrideLimit: this.canOverrideDiscountLimit(),
    limitPercent: this.discountLimitPercent(),
  }));

  /** Cashier-facing reason the cart cannot be submitted because of its manual discount; empty when it can. */
  discountBlockMessage = computed(() => {
    const check = this.manualDiscountCheck();
    if (check.status === 'not_permitted') return DISCOUNT_NOT_PERMITTED_MESSAGE;
    if (check.status === 'over_limit') return discountLimitMessage(check.limitPercent, check.effectivePercent);
    return '';
  });

  discountOverrideNotice = computed(() => {
    const check = this.manualDiscountCheck();
    return check.status === 'override'
      ? `This discount exceeds the ${Number((check.limitPercent ?? 0).toFixed(2))}% manual discount limit and is allowed by your override permission.`
      : '';
  });

  /** Server-authoritative tax algorithm applied to the cart (see pos-tax.ts). */
  taxCalculation = computed(() => {
    const settings = this.posSettings();
    const taxEnabled = this.taxEnabled();
    // A line the server has not priced yet is estimated with the Business default rate until the next price refresh.
    const fallbackRate = settings?.taxEnabled ? Number(settings.defaultTaxRate || 0) : 0;
    return calculateTax({
      lines: this.cartItems().map(item => ({
        quantity: item.quantity,
        unitPrice: item.unitPrice,
        lineDiscount: item.discountAmount,
        taxRate: effectiveLineTaxRate(item.taxRate ?? fallbackRate, item.taxClassification, taxEnabled),
      })),
      invoiceDiscount: this.discountAmount(),
      pricesIncludeTax: this.pricesIncludeTax(),
    });
  });

  taxAmount = computed(() => this.taxCalculation().tax);

  /** "VAT", or "incl. VAT" when the prices already contain the tax. */
  taxLabel = computed(() => (this.taxCalculation().inclusive ? `incl. ${this.taxName()}` : this.taxName()));

  receiptTaxLabel = computed(() => (this.completedTaxInclusive() ? `incl. ${this.taxName()}` : this.taxName()));

  totalAmount = computed(() => this.taxCalculation().total);

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

  /** The server-assigned business date of the active session; never derived from the browser clock. */
  headerBusinessDate = computed(() => this.sessionBusinessDate());

  canViewCustomers = computed(() =>
    this.authService.hasPermission('customers.view') || this.authService.hasPermission('*')
  );

  canCreateCustomers = computed(() =>
    this.authService.hasPermission('customers.create') || this.authService.hasPermission('*')
  );

  canBackdateSale = computed(() =>
    this.authService.hasPermission('sales.backdate')
      || this.authService.hasPermission('pos.sale.backdate')
      || this.authService.hasPermission('*')
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

  sessionMaxDurationHours = computed(() => this.activeCashSession()?.maxDurationHours ?? 20);

  /** Server clock minus browser clock, captured when the session was loaded. */
  serverClockOffsetMs = signal(0);

  sessionElapsedSeconds = computed(() => {
    const opened = this.shiftStartedAt();
    const ms = this.now().getTime() + this.serverClockOffsetMs() - opened.getTime();
    return Math.max(0, Math.floor(ms / 1000));
  });

  sessionIsOverdue = computed(() => {
    const active = this.activeCashSession();
    if (!active) return false;
    const maxSeconds = this.sessionMaxDurationHours() * 3600;
    return this.sessionElapsedSeconds() >= maxSeconds || !!active.isOverdue;
  });

  sessionWarningMessage = computed(() => {
    if (!this.activeCashSession()) return '';

    const elapsed = this.sessionElapsedSeconds();
    const max = this.sessionMaxDurationHours() * 3600;
    const remaining = max - elapsed;

    if (remaining <= 0) {
      return `This cash session reached its ${this.sessionMaxDurationHours()}-hour limit. Please close the current cash session before continuing.`;
    }
    if (remaining <= 60 * 60) {
      return 'Cash session will reach its time limit in less than 1 hour.';
    }
    if (remaining <= 2 * 60 * 60) {
      return 'Cash session will reach its time limit in less than 2 hours.';
    }
    return '';
  });

  canCompleteSale = computed(() => {
    if (this.cartItems().length === 0 || this.isProcessing() || !this.selectedTerminalId()) {
      return false;
    }

    if (this.discountBlockMessage()) {
      return false;
    }

    if (!this.activeCashSession() || this.sessionIsOverdue()) {
      return false;
    }

    if (this.useSplitPayment()) {
      return Math.abs(this.splitTotal() - this.payableAmount()) <= 0.01 && !this.splitTenderError();
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

  change = computed(() => {
    if (this.useSplitPayment()) {
      return this.roundMoney(this.splitPayments()
        .filter((p) => p.method === 'cash')
        .reduce((sum, p) => sum + Math.max(0, (p.tendered ?? p.amount ?? 0) - (p.amount || 0)), 0));
    }
    if (this.paymentMethod() !== 'cash') return 0;
    return Math.max(0, Math.round((this.cashTendered() - this.payableAmount()) * 100) / 100);
  });

  /** Cash that stays in the drawer for this sale: the cash allocation, never the tendered amount. */
  netCashRetained = computed(() => this.roundMoney(this.cashBreakdown()));

  /** Cash handed over by the customer across all cash payments. */
  cashTenderedTotal = computed(() => {
    if (this.useSplitPayment()) {
      return this.roundMoney(this.splitPayments()
        .filter((p) => p.method === 'cash')
        .reduce((sum, p) => sum + (p.tendered ?? p.amount ?? 0), 0));
    }
    return this.paymentMethod() === 'cash' ? this.cashTendered() : 0;
  });

  splitTenderError = computed(() => {
    const short = this.splitPayments().some((p) => p.method === 'cash' && p.tendered !== undefined && p.tendered !== null && p.tendered < (p.amount || 0) - 0.004);
    return short ? 'Cash tendered on a cash line cannot be less than its amount.' : '';
  });

  splitTotal = computed(() =>
    this.splitPayments().reduce((s, p) => s + (p.amount || 0), 0)
  );

  splitRemaining = computed(() =>
    Math.max(0, Math.round((this.payableAmount() - this.splitTotal()) * 100) / 100)
  );

  private customerSearchSubject = new Subject<string>();
  private subs: Subscription[] = [];
  private discountAuditTrail = signal<Array<Record<string, unknown>>>([]);

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
    this.loadPosSettings();
    this.loadInventorySettings();
    this.loadReceiptSettings();
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
    document.body.classList.remove('pos-fullscreen');
    if (document.fullscreenElement) {
      document.exitFullscreen?.().catch(() => {});
    }
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
      case 'F3':
        event.preventDefault();
        this.focusCustomerSearch();
        break;
      case 'F4':
        event.preventDefault();
        this.openProductBrowser();
        break;
      case 'Escape':
        this.closeTopmostPanel();
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

  @HostListener('document:fullscreenchange')
  onFullscreenChange(): void {
    const active = !!document.fullscreenElement;
    this.isFullscreen.set(active);
    document.body.classList.toggle('pos-fullscreen', active);
  }

  /** Fullscreen hides the browser chrome only; the cart, session and payment state are untouched. */
  toggleFullscreen(): void {
    if (document.fullscreenElement) {
      document.exitFullscreen?.().catch(() => {});
      return;
    }
    const shell = document.documentElement;
    if (!shell.requestFullscreen) {
      this.alertService.warning('Fullscreen is not supported by this browser.');
      return;
    }
    shell.requestFullscreen().catch(() => this.alertService.warning('The browser refused fullscreen mode.'));
  }

  focusCustomerSearch(): void {
    (document.getElementById('pos-customer-search') as HTMLInputElement | null)?.focus();
  }

  private closeTopmostPanel(): void {
    if (this.showQuickCustomerModal()) { this.closeQuickCustomerModal(); return; }
    if (this.showProductBrowser()) { this.closeProductBrowser(); return; }
    if (this.showHeldSalesPanel()) { this.showHeldSalesPanel.set(false); return; }
    if (this.showSmartSearchDropdown()) { this.showSmartSearchDropdown.set(false); return; }
    if (this.showCustomerDropdown()) { this.showCustomerDropdown.set(false); }
  }

  private loadPosSettings(): void {
    this.posService.getPosSettings().subscribe({
      next: (res) => this.applyPosSettings(res?.data ?? null),
      error: () => {}
    });
  }

  private applyPosSettings(settings: PosBusinessSettingsDto | null): void {
    if (!settings) return;
    this.posSettings.set(settings);
    this.pricesIncludeTax.set(!!settings.pricesIncludeTax);
  }

  private loadInventorySettings(): void {
    this.settingsService.getInventorySettings().subscribe({
      next: (res) => {
        const data = res?.data;
        const threshold = Number(data?.lowStockThreshold);
        if (Number.isFinite(threshold) && threshold >= 0) {
          this.lowStockThreshold.set(threshold);
        }
      },
      error: () => {}
    });
  }

  private loadReceiptSettings(): void {
    this.settingsService.getReceiptSettings().subscribe({
      next: (res) => {
        const data = res?.data;
        if (!data) return;
        this.receiptSettings.set({ ...this.receiptSettings(), ...data });
        const mode = String(data.defaultReceiptSize || data.paperSize || '80mm').toLowerCase() === 'a4' ? 'A4' : '80mm';
        this.receiptOutputMode.set(mode);
        const copies = Number(data.receiptCopies ?? 1);
        this.receiptCopies.set(Number.isFinite(copies) && copies > 0 ? Math.floor(copies) : 1);
      },
      error: () => {}
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
              const basePrice = Number(p.basePrice ?? p.sellingPrice ?? p.price ?? 0);
              const variantPrice = Number(
                v.finalPrice
                  ?? (Number.isFinite(basePrice) ? basePrice + Number(v.priceAdjustment ?? 0) : undefined)
                  ?? v.sellingPrice
                  ?? v.price
                  ?? p.basePrice
                  ?? p.sellingPrice
                  ?? p.price
                  ?? 0
              );

              mapped.push({
                variantId: v.id,
                productId: p.id,
                productName: p.name,
                variantName: v.name,
                name: p.name + (v.name ? ` - ${v.name}` : ''),
                productCode: p.productCode || v.productCode || '',
                sku: v.sku || p.sku || '',
                barcode: v.barcode || p.barcode || '',
                attributes: v.attributes || '',
                price: Number.isFinite(variantPrice) ? variantPrice : 0,
                stockQty: v.stockQuantity ?? v.stock ?? 0,
                categoryId: p.categoryId,
                categoryName: p.categoryName,
                primaryImageThumb: p.primaryImageThumb,
              });
            }
          } else {
            const basePrice = Number(p.basePrice ?? p.sellingPrice ?? p.price ?? 0);
            mapped.push({
              variantId: p.defaultVariantId ?? p.id,
              productId: p.id,
              productName: p.name,
              variantName: '',
              name: p.name,
              productCode: p.productCode || '',
              sku: p.sku || '',
              barcode: p.barcode || '',
              attributes: '',
              price: Number.isFinite(basePrice) ? basePrice : 0,
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
        const selection = resolveTerminalSelection(data, this.selectedTerminalId());
        this.terminalsState.set(selection.availability);
        this.selectedTerminalId.set(selection.selectedId);
        this.loadActiveCashSession();
      },
      error: () => {
        this.terminals.set([]);
        this.terminalsState.set('error');
        this.selectedTerminalId.set(null);
        this.activeCashSession.set(null);
      }
    });
  }

  onTerminalChange(value: string | number | null): void {
    const id = Number(value);
    this.selectedTerminalId.set(Number.isFinite(id) && id > 0 ? id : null);
    this.loadActiveCashSession();
  }

  onSalespersonChange(value: string | number | null): void {
    const id = Number(value);
    this.selectedSalespersonId.set(Number.isFinite(id) && id > 0 ? id : null);
  }

  /** Eligible salespersons and the server's default (terminal session opener when eligible, otherwise the cashier). */
  private loadSalespersons(): void {
    const outletId = this.currentUser()?.outletId;
    if (!outletId || typeof this.posService.getSalespersons !== 'function') return;
    this.posService.getSalespersons(outletId, this.selectedTerminalId()).subscribe({
      next: (res) => {
        const options = res?.data;
        const list = options?.salespersons ?? [];
        this.salespersons.set(list);
        this.defaultSalespersonId = options?.defaultSalespersonUserId ?? null;
        const current = this.selectedSalespersonId();
        if (current === null || !list.some(person => person.userId === current)) {
          this.selectedSalespersonId.set(this.defaultSalespersonId);
        }
      },
      error: () => {
        this.salespersons.set([]);
        this.defaultSalespersonId = null;
        this.selectedSalespersonId.set(null);
      }
    });
  }

  /** Shows the Open Cash dialog with the server's business date and session limit for this outlet. */
  private promptOpenCashSession(): void {
    this.showOpenCashModal.set(true);
    const outletId = this.currentUser()?.outletId;
    if (!outletId) return;
    this.openContextBusinessDate.set(null);
    this.openContextMaxHours.set(null);
    this.cashSessions.openContext(outletId).subscribe({
      next: (res) => {
        this.openContextBusinessDate.set(res?.data?.businessDate ?? null);
        this.openContextMaxHours.set(res?.data?.maxDurationHours ?? null);
      },
      error: () => {}
    });
  }

  openCashSession(): void {
    const user = this.currentUser();
    const terminalId = this.selectedTerminalId();
    if (!user?.outletId || !terminalId) {
      this.alertService.error('Please select an outlet and terminal before opening cash.');
      return;
    }

    const openingCashAmount = Number(this.openingCashAmount() || 0);
    if (!Number.isFinite(openingCashAmount) || openingCashAmount < 0) {
      this.alertService.error('Opening cash is required.');
      return;
    }

    this.isLoadingSession.set(true);
    this.posService.openCashSession({
      outletId: user.outletId,
      terminalId,
      openingCashAmount,
      openingNotes: this.openingNotes().trim() || undefined,
    }).subscribe({
      next: (res) => {
        const session = (res?.data ?? null) as CashSessionDto | null;
        if (session) {
          this.bindSession(session);
        }
        this.showOpenCashModal.set(false);
        this.openingCashAmount.set(0);
        this.openingNotes.set('');
        this.isLoadingSession.set(false);
        this.loadSalespersons();

        if (this.cartItems().length > 0) {
          this.revalidateCartForSession();
        }
      },
      error: (err) => {
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
        this.isLoadingSession.set(false);
      }
    });
  }

  /**
   * Closing happens on the dedicated Close Cash Session page (reconciliation, variance reason, denominations and handover,
   * all re-validated by the server). A cart in progress is held first so it can be recalled after the next session opens.
   */
  requestCloseCashSession(): void {
    const session = this.activeCashSession();
    if (!session) {
      return;
    }

    const goToClose = () => this.router.navigate(['/cash-sessions', session.id, 'close']);
    if (this.cartItems().length === 0) {
      goToClose();
      return;
    }

    this.alertService.confirm(
      `The cart has ${this.cartItems().length} item(s). It will be held so you can recall it after the session is closed and a new one is opened.`,
      () => this.holdSale(goToClose),
      'Close Cash Session',
      'Hold Cart & Continue',
      'Cancel'
    );
  }

  closeCashModals(): void {
    if (this.isLoadingSession()) return;
    this.showOpenCashModal.set(false);
  }

  private loadActiveCashSession(): void {
    const user = this.currentUser();
    const terminalId = this.selectedTerminalId();

    if (!user?.outletId || !terminalId) {
      this.activeCashSession.set(null);
      return;
    }

    this.isLoadingSession.set(true);
    this.posService.getActiveCashSession(user.outletId, terminalId).subscribe({
      next: (res) => {
        const session = (res?.data ?? null) as CashSessionDto | null;
        if (session) {
          this.bindSession(session);
          this.showOpenCashModal.set(false);
        } else {
          this.activeCashSession.set(null);
          this.promptOpenCashSession();
        }
        this.isLoadingSession.set(false);
        this.loadSalespersons();
      },
      error: (err) => {
        this.activeCashSession.set(null);
        this.promptOpenCashSession();
        this.isLoadingSession.set(false);
        this.alertService.error(this.errorHandler.extractErrorMessage(err));
      }
    });
  }

  private bindSession(session: CashSessionDto): void {
    this.activeCashSession.set(session);
    this.shiftStartedAt.set(new Date(session.openedAt));
    this.sessionBusinessDate.set(new Date(session.businessDate));
    // Elapsed time and the cutoff follow the server clock, never the browser clock.
    this.serverClockOffsetMs.set(
      session.serverNowUtc ? new Date(session.serverNowUtc).getTime() - Date.now() : 0
    );
  }

  private revalidateCartForSession(): void {
    const items = this.cartItems();
    if (items.length === 0) return;

    this.refreshCartPrices();

    this.refreshSmartSearchStock(items.map(i => ({
      variantId: i.variantId,
      productId: 0,
      productName: i.productName,
      name: i.productName,
      sku: i.variantSku,
      barcode: '',
      variantName: '',
      attributes: '',
      price: i.unitPrice,
      stockQty: 0,
      categoryId: 0,
      categoryName: ''
    })));
  }

  /** Re-prices the cart with the server: final unit price, effective tax rate and classification per line. */
  private refreshCartPrices(onDone?: (succeeded: boolean) => void): void {
    const outletId = this.authService.getUserValue()?.outletId;
    const items = this.cartItems();
    if (items.length === 0) {
      onDone?.(true);
      return;
    }
    if (!outletId) {
      onDone?.(false);
      return;
    }

    this.posService.cartPrices(items.map(i => ({ productVariantId: i.variantId, quantity: i.quantity, outletId }))).subscribe({
      next: (res) => {
        this.applyCartPrices((res?.data ?? []) as CartPriceResultItem[]);
        onDone?.(true);
      },
      error: () => onDone?.(false)
    });
  }

  private applyCartPrices(rows: CartPriceResultItem[]): void {
    const byVariant = new Map<number, CartPriceResultItem>();
    rows.forEach(row => byVariant.set(Number(row.productVariantId), row));

    const includesTax = rows.find(row => typeof row.pricesIncludeTax === 'boolean')?.pricesIncludeTax;
    if (includesTax !== undefined) {
      this.pricesIncludeTax.set(includesTax);
    }

    this.cartItems.update(items => items.map(item => {
      const priced = byVariant.get(item.variantId);
      const unitPrice = Number(priced?.finalPrice);
      if (!priced || !Number.isFinite(unitPrice)) return item;
      return this.repriceLine(item, {
        unitPrice,
        taxRate: Number(priced.taxRate ?? 0),
        taxClassification: priced.taxClassification ?? item.taxClassification,
        appliedRuleName: priced.appliedRuleName ?? undefined,
      });
    }));
  }

  /** Applies changes to a cart line and recalculates its manual (percentage) discount and subtotal. */
  private repriceLine(item: CartItem, changes: Partial<CartItem>): CartItem {
    const next = { ...item, ...changes };
    const gross = this.roundMoney(next.quantity * next.unitPrice);
    const percent = Number(next.discountPercent || 0);
    const discountAmount = percent > 0
      ? percentToDiscountAmount(gross, percent, this.discountLimitPercent(), this.canOverrideDiscountLimit())
      : 0;
    return { ...next, discountAmount, subtotal: this.roundMoney(gross - discountAmount) };
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
    const outletStock = this.getOutletStockQty(product);
    if (outletStock <= 0) {
      this.alertService.error('This product is out of stock');
      return;
    }

    this.addToCart(product);
  }

  addToCart(product: PosProduct): void {
    const outletStock = this.getOutletStockQty(product);
    if (outletStock <= 0) {
      this.alertService.error('This product is out of stock');
      return;
    }
    const items = this.cartItems();
    const idx = items.findIndex(i => i.variantId === product.variantId);
    if (idx >= 0) {
      this.setQuantity(idx, items[idx].quantity + 1);
      this.highlightCartVariant(product.variantId);
      return;
    } else {
      this.cartItems.set([...items, {
        variantId: product.variantId,
        productName: product.name,
        variantSku: product.sku,
        barcode: product.barcode || undefined,
        variantLabel: this.getVariantDetails(product),
        knownStock: this.getKnownOutletStockQty(product) ?? (Number.isFinite(Number(product.stockQty)) ? Number(product.stockQty) : null),
        quantity: 1,
        unitPrice: product.price,
        discountPercent: 0,
        discountAmount: 0,
        subtotal: product.price,
        primaryImageThumb: product.primaryImageThumb,
        taxRate: product.taxRate,
        taxClassification: product.taxClassification,
      }]);

      // Products from the catalogue list carry no tax details; ask the server for them.
      if (product.taxRate === undefined) {
        this.refreshCartPrices();
      }
    }

    this.showSmartSearchDropdown.set(false);
  }

  onSmartSearchChange(value: string): void {
    this.smartSearchQuery.set(value);
    const hasQuery = !!value.trim();
    this.showSmartSearchDropdown.set(hasQuery);
    if (hasQuery) {
      this.refreshSmartSearchStock(this.smartSearchResults());
    }
  }

  onSmartSearchFocus(): void {
    const hasQuery = !!this.smartSearchQuery().trim();
    this.showSmartSearchDropdown.set(hasQuery);
    if (hasQuery) {
      this.refreshSmartSearchStock(this.smartSearchResults());
    }
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
      productName: result.productName,
      variantName: result.variantName,
      name: result.productName + (result.variantName ? ` - ${result.variantName}` : ''),
      productCode: result.productCode || '',
      sku: result.sku,
      barcode: result.barcode || '',
      price: result.effectivePrice,
      stockQty: result.stockQty,
      attributes: result.attributes || '',
      primaryImageThumb: result.imageUrl,
      taxRate: result.taxRate === undefined || result.taxRate === null ? undefined : Number(result.taxRate),
      taxClassification: result.taxClassification,
    };

    if (typeof result.pricesIncludeTax === 'boolean') {
      this.pricesIncludeTax.set(result.pricesIncludeTax);
    }

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

  getPrimaryName(product: PosProduct): string {
    return (product.productName || product.name || '').trim();
  }

  getVariantDetails(product: PosProduct): string {
    const fromAttributes = this.formatVariantAttributes(product.attributes);
    if (fromAttributes) {
      return fromAttributes;
    }

    const variantName = (product.variantName || '').trim();
    if (variantName && variantName.toLowerCase() !== 'default') {
      return variantName;
    }

    return 'Default Variant';
  }

  getOutletStockQty(product: PosProduct): number {
    const stock = this.outletStockByVariant()[product.variantId];
    if (stock === undefined || stock === null) {
      return Number(product.stockQty || 0);
    }

    return Number(stock || 0);
  }

  private getKnownOutletStockQty(product: PosProduct): number | null {
    const stock = this.outletStockByVariant()[product.variantId];
    if (stock === undefined || stock === null) {
      return null;
    }

    return Number(stock || 0);
  }

  getStockStatusClass(product: PosProduct): string {
    const qty = this.getKnownOutletStockQty(product);
    if (qty === null) return 'stock-unknown';
    if (qty <= 0) return 'stock-out';
    if (qty <= this.lowStockThreshold()) return 'stock-low';
    return 'stock-in';
  }

  getStockStatusText(product: PosProduct): string {
    const qty = this.getKnownOutletStockQty(product);
    if (qty === null) return 'Stock unavailable for selected outlet';
    if (qty <= 0) return 'Out of Stock';
    if (qty <= this.lowStockThreshold()) return `Current Stock: ${qty} (Low Stock)`;
    return `Current Stock: ${qty}`;
  }

  isStockLoading(product: PosProduct): boolean {
    return !!this.stockLoadingByVariant()[product.variantId];
  }

  private matchesSmartSearch(product: PosProduct, query: string): boolean {
    const haystack = [
      product.name,
      product.productName,
      product.variantName,
      product.productCode,
      product.sku,
      product.barcode,
      product.categoryName,
      this.formatVariantAttributes(product.attributes)
    ]
      .filter(Boolean)
      .join(' ')
      .toLowerCase();

    return haystack.includes(query);
  }

  private refreshSmartSearchStock(products: PosProduct[]): void {
    const outletId = this.currentUser()?.outletId;
    if (!outletId || products.length === 0) {
      return;
    }

    for (const product of products) {
      this.stockLoadingByVariant.update(map => ({ ...map, [product.variantId]: true }));
      this.posService.stockHint(product.variantId, outletId, 1).subscribe({
        next: (res) => {
          const data = res?.data ?? res;
          const qty = Number(data?.availableQty ?? 0);
          this.outletStockByVariant.update(map => ({ ...map, [product.variantId]: qty }));
          this.stockLoadingByVariant.update(map => ({ ...map, [product.variantId]: false }));
        },
        error: () => {
          this.stockLoadingByVariant.update(map => ({ ...map, [product.variantId]: false }));
        }
      });
    }
  }

  private formatVariantAttributes(raw?: string): string {
    if (!raw || !raw.trim()) {
      return '';
    }

    try {
      const parsed = JSON.parse(raw);

      if (Array.isArray(parsed)) {
        return parsed
          .filter((row: any) => this.isAttributeRowActive(row))
          .map((row: any) => {
            const label = this.humanizeLabel(String(row.displayName || row.attributeName || row.name || row.key || '').trim());
            const value = String(row.displayValue ?? row.value ?? '').trim();
            return label && value ? `${label}: ${value}` : '';
          })
          .filter(Boolean)
          .join(' | ');
      }

      if (parsed && typeof parsed === 'object') {
        return Object.entries(parsed)
          .filter(([key, value]) => this.isAttributeEntryActive(key, value))
          .map(([key, value]) => `${this.humanizeLabel(key)}: ${String(value).trim()}`)
          .join(' | ');
      }
    } catch {
      // Keep graceful fallback for older plain-text stored attributes.
    }

    return raw
      .replace(/[{}"]+/g, '')
      .replace(/\s*,\s*/g, ' | ')
      .replace(/\s*\/\s*/g, ' | ')
      .trim();
  }

  private isAttributeRowActive(row: any): boolean {
    if (!row) return false;

    const status = String(row.status ?? '').toLowerCase();
    if (status === 'inactive' || status === 'deleted') return false;

    if (row.isDeleted === true || row.deleted === true) return false;
    if (row.isActive === false || row.active === false) return false;

    const value = String(row.displayValue ?? row.value ?? '').trim();
    return !!value;
  }

  private isAttributeEntryActive(key: string, value: unknown): boolean {
    if (value === null || value === undefined) return false;

    const normalizedKey = key.trim().toLowerCase();
    if (['isactive', 'active', 'isdeleted', 'deleted', 'status'].includes(normalizedKey)) {
      return false;
    }

    const text = String(value).trim();
    if (!text) return false;

    const normalizedValue = text.toLowerCase();
    if (['inactive', 'deleted', 'false', 'null', 'undefined'].includes(normalizedValue)) {
      return false;
    }

    return true;
  }

  private humanizeLabel(value: string): string {
    return value
      .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
      .replace(/[_-]+/g, ' ')
      .replace(/\s+/g, ' ')
      .trim()
      .replace(/^./, (c) => c.toUpperCase());
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
    this.setQuantity(index, this.cartItems()[index].quantity + 1);
  }

  /**
   * Typed or stepped quantity: whole units of at least 1. The last known outlet stock is a hint only (the server
   * re-checks stock atomically at checkout), so an over-stock quantity is capped with a warning, never silently sold.
   */
  setQuantity(index: number, raw: string | number | null, input?: HTMLInputElement): void {
    this.applyQuantity(index, raw);
    // A refused or capped value leaves the bound quantity unchanged, so the field is reset explicitly.
    if (input) input.value = String(this.cartItems()[index]?.quantity ?? '');
  }

  private applyQuantity(index: number, raw: string | number | null): void {
    const items = this.cartItems();
    const line = items[index];
    if (!line) return;

    const parsed = Math.floor(Number(raw));
    if (!Number.isFinite(parsed) || parsed < 1) {
      this.alertService.warning('Quantity must be a whole number of at least 1. Use the remove button to delete the line.');
      this.cartItems.set([...items]);
      return;
    }

    const known = this.outletStockByVariant()[line.variantId] ?? line.knownStock;
    let quantity = parsed;
    if (known !== undefined && known !== null && Number.isFinite(Number(known)) && quantity > Number(known)) {
      quantity = Math.max(1, Number(known));
      this.alertService.warning(`Only ${known} in stock at this outlet for ${line.productName}.`);
    }

    const updated = [...items];
    updated[index] = this.repriceLine(line, { quantity });
    this.cartItems.set(updated);
  }

  requestClearCart(): void {
    if (this.cartItems().length === 0) return;
    this.alertService.confirm(
      'Remove all items, discounts, payments and the selected customer from this sale?',
      () => this.clearCart(),
      'Clear Cart',
      'Clear Cart',
      'Cancel'
    );
  }

  decreaseQty(index: number): void {
    const items = this.cartItems();
    if (items[index].quantity <= 1) {
      this.removeFromCart(index);
      return;
    }
    const updated = [...items];
    updated[index] = this.repriceLine(updated[index], { quantity: updated[index].quantity - 1 });
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
    this.checkoutNotice.set(null);
    this.selectedSalespersonId.set(this.defaultSalespersonId);
  }

  private roundMoney(value: number): number {
    return roundMoney(value);
  }

  private hasProductWiseDiscounts(): boolean {
    return this.cartItems().some(i => Number(i.discountAmount || 0) > 0 || Number(i.discountPercent || 0) > 0);
  }

  private applyProductWiseDiscount(index: number, percent: number): void {
    const updated = [...this.cartItems()];
    const current = updated[index];
    updated[index] = this.repriceLine(current, { discountPercent: percent });

    this.cartItems.set(updated);
    this.auditDiscountChange('ApplyProductWiseDiscount', {
      variantId: current.variantId,
      productName: current.productName,
      percent,
      removedOverallDiscountPercent: this.discountPercent()
    });
  }

  private clearAllProductWiseDiscounts(): void {
    this.cartItems.update(items => items.map(item => this.repriceLine(item, { discountPercent: 0 })));
  }

  private refuseDiscountWithoutPermission(): boolean {
    if (this.canApplyDiscount()) return false;
    this.alertService.error(DISCOUNT_NOT_PERMITTED_MESSAGE);
    return true;
  }

  onItemDiscountChange(index: number, val: string): void {
    if (this.refuseDiscountWithoutPermission()) return;

    const parsed = parseFloat(String(val));
    const nextPercent = isNaN(parsed) ? 0 : Math.min(100, Math.max(0, parsed));

    // Applying item-wise discount while overall discount exists requires confirmation.
    if (nextPercent > 0 && this.discountPercent() > 0) {
      this.alertService.confirm(
        'An overall invoice discount is already applied. Product-wise discounts cannot be used together with an overall discount.\n\nDo you want to remove the overall discount and continue?',
        () => {
          const previousOverall = this.discountPercent();
          this.discountPercent.set(0);
          this.applyProductWiseDiscount(index, nextPercent);
          this.auditDiscountChange('SwitchToProductWiseDiscount', {
            removedOverallDiscountPercent: previousOverall,
            appliedLineIndex: index,
            appliedLinePercent: nextPercent
          }, 'overall', 'product-wise');
        },
        'Switch Discount Type',
        'Yes, Remove Overall Discount',
        'Cancel'
      );
      return;
    }

    this.applyProductWiseDiscount(index, nextPercent);
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
      const message = this.terminalsState() === 'none' ? NO_ACTIVE_TERMINAL_MESSAGE : TERMINAL_REQUIRED_MESSAGE;
      this.checkoutNotice.set({ variant: 'danger', text: message });
      this.alertService.error(message);
      return;
    }

    if (!this.activeCashSession()) {
      this.promptOpenCashSession();
      this.alertService.error('No active cash session found for this terminal. Please open cash before completing sale.');
      return;
    }

    if (this.sessionIsOverdue()) {
      this.alertService.error(`This cash session reached its ${this.sessionMaxDurationHours()}-hour limit.\n\nPlease close the current cash session before continuing.`);
      return;
    }

    if (this.rejectBlockedDiscount()) return;

    const paymentError = this.paymentError();
    if (paymentError) {
      this.alertService.error(paymentError);
      return;
    }

    // Always re-price right before submitting: tax rates and prices may have changed since the lines were added.
    this.checkoutNotice.set(null);
    this.isProcessing.set(true);
    const before = this.checkoutSnapshot();
    this.refreshCartPrices((succeeded) => {
      if (!succeeded) {
        this.isProcessing.set(false);
        this.alertService.error('Could not refresh prices and tax from the server. Please try again.');
        return;
      }

      if (this.checkoutSnapshot() !== before) {
        this.isProcessing.set(false);
        this.notifyCartUpdated();
        return;
      }

      this.submitSale(user.outletId!);
    });
  }

  private checkoutSnapshot(): string {
    return JSON.stringify([
      this.cartItems().map(i => [i.variantId, i.unitPrice]),
      this.pricesIncludeTax(),
      this.taxAmount(),
      this.grandTotal(),
    ]);
  }

  private notifyCartUpdated(): void {
    this.checkoutNotice.set({ variant: 'warning', text: CART_UPDATED_MESSAGE });
    this.alertService.warning(CART_UPDATED_MESSAGE);
  }

  private rejectBlockedDiscount(): boolean {
    const message = this.discountBlockMessage();
    if (!message) return false;
    this.checkoutNotice.set({ variant: 'danger', text: message });
    return true;
  }

  private paymentError(): string | null {
    if (this.paymentMethod() === 'cash' && !this.useSplitPayment() && this.cashTendered() < this.payableAmount()) {
      return 'Cash tendered is less than total amount';
    }

    if (this.useSplitPayment() && Math.abs(this.splitTotal() - this.payableAmount()) > 0.01) {
      return `Split payment total (${this.currencyService.format(this.splitTotal())}) must equal sale total (${this.currencyService.format(this.payableAmount())})`;
    }

    if (this.useSplitPayment() && this.splitTenderError()) {
      return this.splitTenderError();
    }

    return null;
  }

  private submitSale(outletId: number): void {
    const paymentError = this.paymentError();
    if (paymentError) {
      this.isProcessing.set(false);
      this.alertService.error(paymentError);
      return;
    }

    if (this.rejectBlockedDiscount()) {
      this.isProcessing.set(false);
      return;
    }

    const inclusiveTax = this.taxCalculation().inclusive;
    // One idempotency key per distinct checkout payload: a retry of the same cart (timeout, double click) reuses it so the
    // server returns the original sale, while any edit to the cart produces a new key.
    const attemptPayload = {
      outletId,
      terminalId: this.selectedTerminalId()!,
      customerId: (this.selectedCustomer()?.id ?? 0) > 0 ? this.selectedCustomer()!.id : undefined,
      items: this.cartItems().map(i => ({ variantId: i.variantId, quantity: i.quantity, unitPrice: i.unitPrice, discountAmount: i.discountAmount })),
      discount: this.discountAmount(),
      tax: this.taxAmount(),
      paymentMethod: this.paymentMethod(),
      cashTendered: this.paymentMethod() === 'cash' && !this.useSplitPayment() ? this.cashTendered() : undefined,
      payments: this.useSplitPayment() ? this.splitPayments() : undefined,
      salespersonUserId: this.selectedSalespersonId() ?? undefined
    };
    const signature = JSON.stringify(attemptPayload);
    if (!this.checkoutAttempt || this.checkoutAttempt.signature !== signature) {
      this.checkoutAttempt = { signature, key: crypto.randomUUID() };
    }
    const idempotencyKey = this.checkoutAttempt.key;

    // The server derives the cashier from the authenticated user and re-prices and re-taxes the cart; the tax sent is the
    // output of the same algorithm (pos-tax.ts) and is rejected with SALE_TAX_MISMATCH when it differs.
    const dto: CreateSaleDto = {
      outletId,
      terminalId: this.selectedTerminalId()!,
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
      cashTendered: attemptPayload.cashTendered,
      idempotencyKey,
      payments: this.useSplitPayment() ? this.splitPayments() : undefined,
      salespersonUserId: attemptPayload.salespersonUserId
    };

    this.isProcessing.set(true);
    this.saleService.createSale(dto).subscribe({
      next: (res) => {
        this.checkoutAttempt = null;
        const createdSale = res?.data || res;
        this.completedSale.set(createdSale);
        this.receiptPrintCount.set(0);
        this.completedTaxInclusive.set(inclusiveTax);
        this.checkoutNotice.set(null);
        this.showReceiptModal.set(true);
        this.isProcessing.set(false);
        this.saleSequence.update((v) => v + 1);
        // Refresh held sales count after a successful sale
        this.posService.loadHeldSales(outletId);

        const saleId = Number(createdSale?.id || 0);
        if (saleId > 0) {
          this.handlePostSaleReceiptFlow(saleId);
        }
      },
      error: (err) => {
        this.handleSaleError(err, outletId);
        this.isProcessing.set(false);
      }
    });
  }

  private handleSaleError(err: any, outletId: number): void {
    const mapped = mapPosSaleError(err);
    if (!mapped) {
      this.alertService.error(this.errorHandler.extractErrorMessage(err));
      return;
    }

    switch (mapped.kind) {
      case 'price_changed':
        if (Array.isArray(mapped.data)) {
          this.applyServerPrices(mapped.data);
        }
        this.refreshAfterServerRejection();
        this.notifyCartUpdated();
        break;
      case 'tax_mismatch':
        this.refreshAfterServerRejection();
        this.notifyCartUpdated();
        break;
      case 'no_active_terminal':
      case 'terminal_required':
        this.loadTerminals(outletId);
        this.checkoutNotice.set({ variant: 'danger', text: mapped.message });
        break;
      default:
        this.checkoutNotice.set({ variant: 'danger', text: mapped.message });
        break;
    }
  }

  /** After a 409 the Business settings and the cart prices may be stale: reload both and adopt the server values. */
  private refreshAfterServerRejection(): void {
    this.posService.getPosSettings().subscribe({
      next: (res) => {
        this.applyPosSettings(res?.data ?? null);
        this.refreshCartPrices();
      },
      error: () => this.refreshCartPrices()
    });
  }

  /** Adopts the prices the server says are current; discounts expressed as a percentage are recalculated. */
  private applyServerPrices(changes: Array<{ variantId: number; currentUnitPrice: number }>): void {
    const byVariant = new Map(changes.map(change => [change.variantId, change.currentUnitPrice]));
    this.cartItems.update(items => items.map(item => {
      const price = byVariant.get(item.variantId);
      if (price === undefined) return item;
      return this.repriceLine(item, { unitPrice: price });
    }));
  }

  printReceipt(): void {
    const saleId = Number(this.completedSale()?.id || 0);
    if (saleId > 0) {
      this.recordReceiptAttempt(saleId, {
        action: this.receiptPrintCount() > 0 ? 'Reprinted' : 'Printed',
        outputMode: this.receiptOutputMode()
      });
    }
    this.receiptPrintCount.update(count => count + 1);

    this.printWithCopies(this.receiptCopies());
  }

  private async handlePostSaleReceiptFlow(saleId: number): Promise<void> {
    const mode = this.receiptOutputMode();
    await this.loadReceiptDocument(saleId, mode, true);

    if (!this.receiptSettings().autoPrintAfterSale) {
      return;
    }

    const printerState = await this.detectPrinterAvailability();
    if (printerState === 'unavailable') {
      this.recordReceiptAttempt(saleId, {
        action: 'PrintFailed',
        outputMode: mode,
        message: 'No receipt printer was detected.'
      });
      this.showNoPrinterDialog(saleId, mode);
      return;
    }

    window.setTimeout(() => {
      this.printWithCopies(this.receiptCopies());
      this.recordReceiptAttempt(saleId, { action: 'Printed', outputMode: mode });
      this.receiptPrintCount.update(count => count + 1);
    }, 150);
  }

  private printWithCopies(copies: number): void {
    const safeCopies = Math.max(1, Math.floor(Number(copies || 1)));
    let remaining = safeCopies;

    const run = () => {
      if (remaining <= 0) {
        return;
      }
      window.print();
      remaining -= 1;
      if (remaining > 0) {
        window.setTimeout(run, 250);
      }
    };

    run();
  }

  onReceiptModeChange(mode: string): void {
    const resolved: '80mm' | 'A4' = String(mode).toLowerCase() === 'a4' ? 'A4' : '80mm';
    this.receiptOutputMode.set(resolved);

    const saleId = Number(this.completedSale()?.id || 0);
    if (saleId > 0) {
      this.loadReceiptDocument(saleId, resolved, true);
    }
  }

  saveReceiptAsPdf(): void {
    const saleId = Number(this.completedSale()?.id || 0);
    if (saleId <= 0) {
      this.alertService.error('No completed sale is available for PDF export.');
      return;
    }

    this.downloadReceiptPdf(saleId, this.receiptOutputMode());
  }

  private async loadReceiptDocument(saleId: number, mode: string, silent: boolean = false): Promise<void> {
    await new Promise<void>((resolve) => {
      this.saleService.getReceiptDocument(saleId, mode).subscribe({
        next: (res) => {
          const doc = res?.data;
          if (doc) {
            this.receiptDocument.set(doc);
          }
          resolve();
        },
        error: () => {
          if (!silent) {
            this.alertService.warning('Unable to load receipt document details. Showing basic receipt view.');
          }
          resolve();
        }
      });
    });
  }

  private async detectPrinterAvailability(): Promise<'available' | 'unavailable' | 'unknown'> {
    const navAny = navigator as any;

    try {
      if (typeof navAny.usb?.getDevices === 'function') {
        const usbDevices = await navAny.usb.getDevices();
        if (Array.isArray(usbDevices) && usbDevices.length > 0) {
          return 'available';
        }
      }
    } catch {}

    try {
      if (typeof navAny.serial?.getPorts === 'function') {
        const serialPorts = await navAny.serial.getPorts();
        if (Array.isArray(serialPorts) && serialPorts.length > 0) {
          return 'available';
        }
      }
    } catch {}

    if (typeof navAny.usb?.getDevices === 'function' || typeof navAny.serial?.getPorts === 'function') {
      return 'unavailable';
    }

    return 'unknown';
  }

  private showNoPrinterDialog(saleId: number, mode: string): void {
    this.alertService.confirm(
      'No receipt printer was detected.',
      () => {
        this.downloadReceiptPdf(saleId, mode);
      },
      'Printer Not Available',
      'Save Receipt as PDF',
      'Cancel',
      () => {
        this.recordReceiptAttempt(saleId, {
          action: 'PrintFailed',
          outputMode: mode,
          message: 'Cashier canceled after no printer detection.'
        });
      }
    );
  }

  private downloadReceiptPdf(saleId: number, mode: string): void {
    this.saleService.getReceiptPdf(saleId, mode).subscribe({
      next: (blob) => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `receipt-${saleId}-${new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-')}.pdf`;
        document.body.appendChild(a);
        a.click();
        a.remove();
        window.URL.revokeObjectURL(url);

        this.recordReceiptAttempt(saleId, {
          action: 'SavedAsPdf',
          outputMode: mode
        });
      },
      error: () => {
        this.alertService.error('Failed to generate receipt PDF. Please try printing again.');
        this.recordReceiptAttempt(saleId, {
          action: 'PrintFailed',
          outputMode: mode,
          message: 'PDF generation failed.'
        });
      }
    });
  }

  private recordReceiptAttempt(saleId: number, attempt: ReceiptPrintAttemptDto): void {
    this.saleService.registerReceiptPrintAttempt(saleId, attempt).subscribe({
      next: () => {},
      error: () => {}
    });
  }

  startNewSale(): void {
    this.clearCart();
    this.showReceiptModal.set(false);
    this.completedSale.set(null);
    this.receiptDocument.set(null);
    this.receiptPrintCount.set(0);
    this.saleSequence.update((v) => v + 1);
    this.focusSmartSearch();
  }

  setPaymentMethod(method: string): void {
    this.paymentMethod.set(method);
  }

  onDiscountChange(val: string): void {
    if (this.refuseDiscountWithoutPermission()) return;

    const parsed = parseFloat(String(val));
    const nextPercent = isNaN(parsed) ? 0 : Math.min(100, Math.max(0, parsed));

    // Applying overall invoice discount while line discounts exist requires confirmation.
    if (nextPercent > 0 && this.hasProductWiseDiscounts()) {
      this.alertService.confirm(
        'One or more product-wise discounts are already applied. Overall invoice discount cannot be used together with product-wise discounts.\n\nDo you want to remove all product-wise discounts and continue?',
        () => {
          this.clearAllProductWiseDiscounts();
          this.discountPercent.set(nextPercent);
          this.auditDiscountChange('SwitchToOverallDiscount', {
            appliedOverallDiscountPercent: nextPercent,
            removedProductWiseDiscounts: true
          }, 'product-wise', 'overall');
        },
        'Switch Discount Type',
        'Yes, Remove Product Discounts',
        'Cancel'
      );
      return;
    }

    this.discountPercent.set(nextPercent);
    this.auditDiscountChange('ApplyOverallDiscount', {
      overallDiscountPercent: nextPercent,
      hasProductWiseDiscounts: this.hasProductWiseDiscounts()
    });
  }

  private auditDiscountChange(
    action: string,
    payload: Record<string, unknown>,
    fromType?: 'overall' | 'product-wise',
    toType?: 'overall' | 'product-wise'
  ): void {
    const event = {
      at: new Date().toISOString(),
      action,
      userId: this.currentUser()?.id ?? null,
      outletId: this.currentUser()?.outletId ?? null,
      terminalId: this.selectedTerminalId(),
      payload,
    };

    this.discountAuditTrail.update(list => [event, ...list].slice(0, 200));
    this.reportDiscountAudit({
      action,
      fromType,
      toType,
      outletId: this.currentUser()?.outletId ?? null,
      terminalId: this.selectedTerminalId(),
      payload,
    });
    console.info('[POS Discount Audit]', event);
  }

  private reportDiscountAudit(event: PosDiscountAuditRequest): void {
    this.posService.reportDiscountAudit(event).subscribe({
      next: () => {},
      error: () => {
        // Do not interrupt cashier flow if auditing endpoint is temporarily unavailable.
      }
    });
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

  holdSale(onHeld?: () => void): void {
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
        onHeld?.();
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  recallHeldSale(held: HeldSaleDto): void {
    if (this.cartItems().length > 0) {
      this.alertService.confirm(
        `The current cart has ${this.cartItems().length} item(s). Recalling this invoice replaces it. Hold the current cart first if you need it.`,
        () => this.doRecallHeldSale(held),
        'Replace Current Cart?',
        'Replace Cart',
        'Cancel'
      );
      return;
    }
    this.doRecallHeldSale(held);
  }

  private doRecallHeldSale(held: HeldSaleDto): void {
    this.posService.recallHeldSale(held.id).subscribe({
      next: (res) => {
        const data: HeldSaleDto = res?.data ?? res;
        // Restore cart from held sale
        this.cartItems.set(data.items.map(i => {
          const gross = i.quantity * i.unitPrice;
          return {
            variantId: i.variantId,
            productName: i.productName,
            variantSku: i.variantSku,
            quantity: i.quantity,
            unitPrice: i.unitPrice,
            discountPercent: gross > 0 ? (i.discountAmount / gross) * 100 : 0,
            discountAmount: i.discountAmount,
            subtotal: this.roundMoney(gross - i.discountAmount),
            appliedRuleName: i.appliedRuleName
          };
        }));
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
        // A held cart carries no tax details; the current prices and tax rates come from the server.
        this.refreshCartPrices();

        // Delete the held sale now that it's been recalled
        this.posService.discardHeldSale(held.id).subscribe();
        const user = this.authService.getUserValue();
        if (user?.outletId) this.posService.loadHeldSales(user.outletId);
      },
      error: (err) => this.alertService.error(this.errorHandler.extractErrorMessage(err))
    });
  }

  discardHeldSale(held: HeldSaleDto): void {
    this.alertService.confirm(
      `Discard the held invoice of ${held.items.length} item(s)? It cannot be recalled afterwards.`,
      () => this.doDiscardHeldSale(held),
      'Discard Held Invoice',
      'Discard',
      'Cancel'
    );
  }

  private doDiscardHeldSale(held: HeldSaleDto): void {
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
