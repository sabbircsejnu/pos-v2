// NEW ─────────────────────────────────────────────────────────────────────────
// Dedicated Angular service for POS screen interactions.
// Wraps the fast /api/pos/* lookup endpoints and the /api/held-sales/* hold/park
// endpoints.  All heavy product browsing stays in SaleService.
// ─────────────────────────────────────────────────────────────────────────────
import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { SUPPRESS_ERROR_NOTICE } from '../interceptors/error.interceptor';
import {
  HeldSaleDto,
  HoldSaleDto,
  PosBusinessSettingsDto,
  PosProductLookupDto,
  PosStockHintDto,
  TaxClassification,
} from '../models/sale.model';

/** Batch item sent to POST /api/pos/cart-prices */
export interface CartPriceRequestItem {
  productVariantId: number;
  quantity: number;
  outletId: number;
  /** A pack or custom option; the line is then priced from the option using `enteredQuantity`. */
  sellingOptionId?: number;
  /** Pack count or measured quantity of `sellingOptionId`. */
  enteredQuantity?: number;
}

/** Per-item result from POST /api/pos/cart-prices (per-unit amounts). */
export interface CartPriceResultItem {
  productVariantId: number;
  basePrice?: number;
  /** Manual-discount-free rule discount per unit; already reflected in `finalPrice`. */
  discountAmount: number;
  /** Unit price after automatic price rules: the price the sale line is submitted with. */
  finalPrice: number;
  /** Effective tax rate in percent. */
  taxRate: number;
  taxClassification?: TaxClassification;
  /** Tax per unit (informational); the sale tax comes from the checkout algorithm. */
  taxAmount: number;
  finalPriceWithTax: number;
  pricesIncludeTax?: boolean;
  appliedRuleId?: number;
  appliedRuleName?: string;
  /** Option lines: the whole line amount after automatic rules, before manual discount and tax-exclusive VAT. */
  lineGross?: number | null;
  /** Option lines: rules that match but cannot be applied to a measured quantity. */
  rulesNotApplied?: string[] | null;
}

export interface PosApiResponse<T> {
  success?: boolean;
  message?: string;
  data: T;
}

/** Sellable variant from GET /api/pos/catalogue. `listPrice` is a display hint; the cart re-prices via cart-prices. */
export interface PosCatalogueItemDto {
  variantId: number;
  productId: number;
  productName: string;
  variantName: string;
  productCode?: string | null;
  sku: string;
  barcode?: string | null;
  attributes?: string | null;
  categoryId: number;
  categoryName?: string | null;
  listPrice: number;
  primaryImageThumb?: string | null;
  /** Quantity at the requested outlet; null when no outlet was sent. */
  stockQty?: number | null;
}

/** Active category from GET /api/pos/categories. */
export interface PosCategoryDto {
  id: number;
  name: string;
  parentCategoryId?: number | null;
  displayOrder: number;
}

/** Minimal customer from GET /api/pos/customers/search (no email, loyalty or purchase history). */
export interface PosCustomerLookupDto {
  id: number;
  name: string;
  phone?: string | null;
}

export interface PosTerminalDto {
  id: number;
  name: string;
  code: string;
  /** Meaningless since terminals must be chosen explicitly; never used for selection. */
  isDefault?: boolean;
}

export interface EligibleSalespersonDto {
  userId: number;
  name: string;
  /** True when this outlet is the user's primary outlet (listed first). */
  primaryOutlet: boolean;
}

export interface SalespersonOptionsDto {
  salespersons: EligibleSalespersonDto[];
  defaultSalespersonUserId?: number | null;
  /** session_opener | current_user | none */
  defaultSource: string;
}

export interface PosDiscountAuditRequest {
  action: string;
  fromType?: string;
  toType?: string;
  outletId?: number | null;
  terminalId?: number | null;
  payload?: Record<string, unknown>;
}

export interface CashSessionDto {
  id: number;
  businessId?: number;
  outletId: number;
  terminalId: number;
  businessDate: string;
  openedAt: string;
  closedAt?: string;
  openingCashAmount: number;
  expectedClosingCashAmount?: number;
  actualClosingCashAmount?: number;
  differenceAmount?: number;
  status: string;
  openedByUserId: number;
  closedByUserId?: number;
  openingNotes?: string;
  closingNotes?: string;
  isOverdue: boolean;
  maxDurationHours: number;
  elapsedSeconds: number;
  timeZoneId?: string;
  cutoffAtUtc?: string;
  /** none | warning | urgent | overdue — derived by the server from the snapshotted duration. */
  warningLevel?: 'none' | 'warning' | 'urgent' | 'overdue';
  /** open | overdue | closed. Overdue is derived and never persisted. */
  state?: 'open' | 'overdue' | 'closed';
  /** Server clock at response time; elapsed time must never be computed from the browser clock. */
  serverNowUtc?: string;
  varianceReason?: string;
}

export interface OpenCashSessionDto {
  outletId: number;
  /** Required only when the outlet has several active terminals. */
  terminalId?: number;
  openingCashAmount: number;
  openingNotes?: string;
  idempotencyKey?: string;
}

export interface CashDenominationInput {
  value: number;
  count: number;
}

export interface CashHandoverInput {
  method: 'cash_to_manager' | 'cash_to_owner' | 'bank_deposit';
  amount: number;
  recipientUserId?: number;
  recipientName?: string;
  bankName?: string;
  bankAccountReference?: string;
  depositReference?: string;
  depositDate?: string;
  notes?: string;
}

export interface CloseCashSessionDto {
  actualClosingCashAmount: number;
  closingNotes?: string;
  varianceReason?: string;
  denominations?: CashDenominationInput[];
  handovers?: CashHandoverInput[];
}

export interface CashSessionCloseSummaryDto {
  sessionId: number;
  outletId: number;
  terminalId: number;
  businessDate: string;
  openedAt: string;
  closingAt: string;
  openDurationSeconds: number;
  openingCash: number;
  /** Net cash retained in the drawer (D14) — never the tendered amount. */
  cashSales: number;
  cashTendered: number;
  changeGiven: number;
  cardSales: number;
  mobileSales: number;
  otherNonCashSales: number;
  drawerExpenses: number;
  cashRefunds: number;
  corrections: number;
  expectedClosingCash: number;
  saleCount: number;
  isOverdue: boolean;
}

@Injectable({ providedIn: 'root' })
export class PosService {
  private posUrl       = `${environment.apiUrl}/pos`;
  private heldSalesUrl = `${environment.apiUrl}/held-sales`;
  private posAuditUrl  = `${environment.apiUrl}/pos-audit`;

  /** Held sales list state — populated by loadHeldSales() */
  heldSales  = signal<HeldSaleDto[]>([]);
  isLoadingHeld = signal(false);

  constructor(private http: HttpClient) {}

  // ── Catalogue ─────────────────────────────────────────────────────────────

  /**
   * Sellable variants for the product browser, authorized by the POS sale permission (not products.view).
   * The global error notice is suppressed: the POS screen reports the failure with its real cause.
   */
  getCatalogue(outletId?: number | null, limit: number = 200): Observable<PosApiResponse<PosCatalogueItemDto[]>> {
    let params = new HttpParams().set('limit', limit.toString());
    if (outletId) params = params.set('outletId', outletId.toString());
    return this.http.get<PosApiResponse<PosCatalogueItemDto[]>>(`${this.posUrl}/catalogue`, {
      params,
      context: new HttpContext().set(SUPPRESS_ERROR_NOTICE, true)
    });
  }

  /** Active categories for the POS filter, authorized by the POS sale permission (not categories.view). */
  getCategories(): Observable<PosApiResponse<PosCategoryDto[]>> {
    return this.http.get<PosApiResponse<PosCategoryDto[]>>(`${this.posUrl}/categories`, {
      context: new HttpContext().set(SUPPRESS_ERROR_NOTICE, true)
    });
  }

  /** Checkout customer picker, authorized by the POS sale permission (not customers.view). */
  searchCustomers(query: string): Observable<PosApiResponse<PosCustomerLookupDto[]>> {
    return this.http.get<PosApiResponse<PosCustomerLookupDto[]>>(`${this.posUrl}/customers/search`, {
      params: new HttpParams().set('q', query)
    });
  }

  /**
   * Quick Customer from checkout, authorized by the POS sale permission (not customers.create). A phone already used in
   * the Business returns 409 CUSTOMER_PHONE_EXISTS with the existing customer's id, name and phone.
   */
  createCustomer(dto: { name: string; phone: string }): Observable<PosApiResponse<PosCustomerLookupDto>> {
    return this.http.post<PosApiResponse<PosCustomerLookupDto>>(`${this.posUrl}/customers`, dto);
  }

  // ── Lookup ────────────────────────────────────────────────────────────────

  /**
   * Resolve a barcode, SKU or variantId to a full POS product card.
   * @param q      The barcode string, SKU, or numeric variant ID as a string
   * @param outletId  Current outlet for pricing + stock hint
   * @param mode   'barcode' | 'sku' | 'variant'  (default 'barcode')
   */
  lookup(q: string, outletId: number, mode: 'barcode' | 'sku' | 'variant' = 'barcode'): Observable<any> {
    const params = new HttpParams()
      .set('q', q)
      .set('outletId', outletId.toString())
      .set('mode', mode);
    return this.http.get<any>(`${this.posUrl}/lookup`, { params });
  }

  /**
   * Lightweight stock availability hint for a single variant at an outlet.
   * ⚠ DISPLAY HINT ONLY — not the authoritative stock check gate.
   */
  stockHint(variantId: number, outletId: number, qty: number = 1): Observable<any> {
    const params = new HttpParams()
      .set('variantId', variantId.toString())
      .set('outletId',  outletId.toString())
      .set('qty',       qty.toString());
    return this.http.get<any>(`${this.posUrl}/stock-hint`, { params });
  }

  /**
   * Batch price calculation for all current cart lines.
   * Returns the per-unit final price, effective tax rate/classification and rule names in one round-trip.
   */
  cartPrices(items: CartPriceRequestItem[]): Observable<PosApiResponse<CartPriceResultItem[]>> {
    return this.http.post<PosApiResponse<CartPriceResultItem[]>>(`${this.posUrl}/cart-prices`, { items });
  }

  /** Business POS settings (tax, discount limit, ...) plus the caller's discount permissions. */
  getPosSettings(): Observable<PosApiResponse<PosBusinessSettingsDto>> {
    return this.http.get<PosApiResponse<PosBusinessSettingsDto>>(`${environment.apiUrl}/settings/pos`);
  }

  /** Eligible salespersons for the outlet and the default for the terminal's open session (Phase 4). */
  getSalespersons(outletId: number, terminalId?: number | null): Observable<{ data: SalespersonOptionsDto }> {
    let params = new HttpParams().set('outletId', outletId.toString());
    if (terminalId) params = params.set('terminalId', terminalId.toString());
    return this.http.get<{ data: SalespersonOptionsDto }>(`${this.posUrl}/salespersons`, { params });
  }

  getTerminals(outletId: number): Observable<any> {
    const params = new HttpParams().set('outletId', outletId.toString());
    return this.http.get<any>(`${this.posUrl}/terminals`, { params });
  }

  getActiveCashSession(outletId: number, terminalId: number): Observable<any> {
    const params = new HttpParams()
      .set('outletId', outletId.toString())
      .set('terminalId', terminalId.toString());
    return this.http.get<any>(`${this.posUrl}/cash-sessions/active`, { params });
  }

  openCashSession(dto: OpenCashSessionDto): Observable<any> {
    return this.http.post<any>(`${this.posUrl}/cash-sessions/open`, dto);
  }

  getCashSessionCloseSummary(sessionId: number): Observable<any> {
    return this.http.get<any>(`${this.posUrl}/cash-sessions/${sessionId}/summary`);
  }

  closeCashSession(sessionId: number, dto: CloseCashSessionDto): Observable<any> {
    return this.http.post<any>(`${this.posUrl}/cash-sessions/${sessionId}/close`, dto);
  }

  // ── Hold / Park ───────────────────────────────────────────────────────────

  /** Park the current cart without finalising the sale. */
  holdSale(dto: HoldSaleDto): Observable<any> {
    return this.http.post<any>(this.heldSalesUrl, dto);
  }

  /** Load all held sales for an outlet into the heldSales signal. */
  loadHeldSales(outletId: number): void {
    this.isLoadingHeld.set(true);
    this.http.get<any>(`${this.heldSalesUrl}/outlet/${outletId}`).subscribe({
      next: (res) => {
        // GET /held-sales/outlet/{id} returns { heldSales, totalCount }; keep accepting a bare array as well.
        const data = res?.data ?? res;
        this.heldSales.set(Array.isArray(data) ? data : Array.isArray(data?.heldSales) ? data.heldSales : []);
        this.isLoadingHeld.set(false);
      },
      error: () => this.isLoadingHeld.set(false)
    });
  }

  /** Recall a single held sale by ID (returns full items list). */
  recallHeldSale(id: number): Observable<any> {
    return this.http.get<any>(`${this.heldSalesUrl}/${id}`);
  }

  /** Discard a held sale (after recall or manual delete). */
  discardHeldSale(id: number): Observable<any> {
    return this.http.delete<any>(`${this.heldSalesUrl}/${id}`);
  }

  /** Persist POS discount mode/amount changes for audit trail visibility. */
  reportDiscountAudit(dto: PosDiscountAuditRequest): Observable<any> {
    return this.http.post<any>(`${this.posAuditUrl}/discount-change`, dto);
  }
}
