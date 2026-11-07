import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable, of } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  CreateRepackJob,
  PackStateView,
  RepackJob,
  RepackJobPage,
  SaveUomUnit,
  StockCountEntriesResult,
  StockCountEntryInput,
  UnitSaleQuote,
  UomCatalog,
  UomUnit,
  VariantUomConfig,
  VariantUomConfigInput,
  VariantUomConfigView,
} from '../models/uom.model';

interface ApiEnvelope<T> {
  data: T;
}

@Injectable({ providedIn: 'root' })
export class UomService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/v1/uom`;

  getCatalog(options: { activeOnly?: boolean; search?: string } = {}): Observable<UomCatalog> {
    let params = new HttpParams();
    if (options.activeOnly) params = params.set('activeOnly', 'true');
    if (options.search?.trim()) params = params.set('search', options.search.trim());
    return this.http.get<ApiEnvelope<UomCatalog>>(`${this.baseUrl}/units`, { params }).pipe(map(r => r.data));
  }

  createUnit(unit: SaveUomUnit): Observable<UomUnit> {
    return this.http.post<ApiEnvelope<UomUnit>>(`${this.baseUrl}/units`, unit).pipe(map(r => r.data));
  }

  updateUnit(id: number, unit: SaveUomUnit): Observable<UomUnit> {
    return this.http.put<ApiEnvelope<UomUnit>>(`${this.baseUrl}/units/${id}`, unit).pipe(map(r => r.data));
  }

  getVariantConfig(variantId: number): Observable<VariantUomConfigView> {
    return this.http.get<ApiEnvelope<VariantUomConfigView>>(`${this.baseUrl}/variants/${variantId}/config`).pipe(map(r => r.data));
  }

  /** Current configurations only: a variant missing from the result is plain pcs. */
  getVariantConfigs(variantIds: readonly number[]): Observable<VariantUomConfig[]> {
    const ids = [...new Set(variantIds)].filter(id => id > 0);
    if (ids.length === 0) return of([]);
    return this.http
      .post<ApiEnvelope<VariantUomConfig[]>>(`${this.baseUrl}/variants/configs`, { variantIds: ids })
      .pipe(map(r => r.data));
  }

  saveVariantConfig(variantId: number, expectedVersion: number | null, config: VariantUomConfigInput): Observable<VariantUomConfigView> {
    return this.http
      .put<ApiEnvelope<VariantUomConfigView>>(`${this.baseUrl}/variants/${variantId}/config`, { expectedVersion, config })
      .pipe(map(r => r.data));
  }

  /** A preview only. Checkout recomputes everything on the server. */
  quote(variantId: number, sellingOptionId: number, enteredQuantity: number, expectedConfigVersionId: number | null): Observable<UnitSaleQuote> {
    return this.http
      .post<ApiEnvelope<UnitSaleQuote>>(`${this.baseUrl}/quote`, { variantId, sellingOptionId, enteredQuantity, expectedConfigVersionId })
      .pipe(map(r => r.data));
  }

  getPackState(variantId: number, locationId: number, locationType: string): Observable<PackStateView> {
    const params = new HttpParams().set('variantId', variantId).set('locationId', locationId).set('locationType', locationType);
    return this.http.get<ApiEnvelope<PackStateView>>(`${this.baseUrl}/pack-state`, { params }).pipe(map(r => r.data));
  }

  openSealedPack(body: {
    variantId: number;
    locationId: number;
    locationType: string;
    packCode: string;
    baseQtyPerPack: number;
    count: number;
    idempotencyKey: string;
  }): Observable<PackStateView> {
    return this.http.post<ApiEnvelope<PackStateView>>(`${this.baseUrl}/pack-state/open`, body).pipe(map(r => r.data));
  }

  getRepackJobs(filter: { locationId?: number; locationType?: string; status?: string; page?: number; pageSize?: number } = {}): Observable<RepackJobPage> {
    let params = new HttpParams();
    Object.entries(filter).forEach(([key, value]) => {
      if (value !== undefined && value !== null && value !== '') params = params.set(key, value);
    });
    return this.http.get<ApiEnvelope<RepackJobPage>>(`${this.baseUrl}/repacking`, { params }).pipe(map(r => r.data));
  }

  createRepackJob(body: CreateRepackJob): Observable<RepackJob> {
    return this.http.post<ApiEnvelope<RepackJob>>(`${this.baseUrl}/repacking`, body).pipe(map(r => r.data));
  }

  executeRepackJob(id: number): Observable<RepackJob> {
    return this.http.post<ApiEnvelope<RepackJob>>(`${this.baseUrl}/repacking/${id}/execute`, {}).pipe(map(r => r.data));
  }

  cancelRepackJob(id: number): Observable<RepackJob> {
    return this.http.post<ApiEnvelope<RepackJob>>(`${this.baseUrl}/repacking/${id}/cancel`, {}).pipe(map(r => r.data));
  }

  reverseRepackJob(id: number): Observable<RepackJob> {
    return this.http.post<ApiEnvelope<RepackJob>>(`${this.baseUrl}/repacking/${id}/reverse`, {}).pipe(map(r => r.data));
  }

  saveCountEntries(stockCountId: number, lineId: number, entries: StockCountEntryInput[]): Observable<StockCountEntriesResult> {
    return this.http
      .put<ApiEnvelope<StockCountEntriesResult>>(`${this.baseUrl}/stock-counts/${stockCountId}/lines/${lineId}/entries`, entries)
      .pipe(map(r => r.data));
  }
}

/** Formats a quantity with its unit: `488 ml`, `2.5 kg`. A missing unit is pcs. */
export function formatUnitQuantity(quantity: number | null | undefined, unitCode?: string | null): string {
  const value = Number(quantity ?? 0);
  const text = value.toLocaleString('en-US', { maximumFractionDigits: 6 });
  return `${text} ${unitCode || 'pcs'}`;
}

/** The `step` attribute for a quantity input at a unit's precision. */
export function quantityStep(precision: number | null | undefined): string {
  const digits = Math.max(0, Math.min(precision ?? 0, 6));
  return digits === 0 ? '1' : (1 / 10 ** digits).toFixed(digits);
}

/** Whether a quantity fits a unit's precision. Mirrors the server rule for instant feedback only. */
export function fitsPrecision(quantity: number, precision: number): boolean {
  if (!Number.isFinite(quantity)) return false;
  const text = String(quantity);
  const fraction = text.includes('.') ? text.split('.')[1].length : 0;
  return fraction <= precision;
}

export function newIdempotencyKey(prefix: string): string {
  return `${prefix}-${crypto.randomUUID()}`;
}
