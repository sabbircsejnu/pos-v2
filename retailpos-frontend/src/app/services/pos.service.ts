// NEW ─────────────────────────────────────────────────────────────────────────
// Dedicated Angular service for POS screen interactions.
// Wraps the fast /api/pos/* lookup endpoints and the /api/held-sales/* hold/park
// endpoints.  All heavy product browsing stays in SaleService.
// ─────────────────────────────────────────────────────────────────────────────
import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  HeldSaleDto,
  HoldSaleDto,
  PosProductLookupDto,
  PosStockHintDto,
} from '../models/sale.model';

/** Batch item sent to POST /api/pos/cart-prices */
export interface CartPriceRequestItem {
  variantId: number;
  quantity: number;
  outletId: number;
}

/** Per-item result from POST /api/pos/cart-prices */
export interface CartPriceResultItem {
  variantId: number;
  effectivePrice: number;
  discountAmount: number;
  appliedRuleId?: number;
  appliedRuleName?: string;
}

@Injectable({ providedIn: 'root' })
export class PosService {
  private posUrl       = `${environment.apiUrl}/pos`;
  private heldSalesUrl = `${environment.apiUrl}/held-sales`;

  /** Held sales list state — populated by loadHeldSales() */
  heldSales  = signal<HeldSaleDto[]>([]);
  isLoadingHeld = signal(false);

  constructor(private http: HttpClient) {}

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
   * Returns effective prices + discount amounts + rule names in one round-trip.
   */
  cartPrices(items: CartPriceRequestItem[]): Observable<any> {
    return this.http.post<any>(`${this.posUrl}/cart-prices`, { items });
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
        this.heldSales.set(res?.data ?? res ?? []);
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
}
