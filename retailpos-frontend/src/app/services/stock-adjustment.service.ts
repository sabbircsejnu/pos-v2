import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  StockAdjustmentDto,
  CreateStockAdjustmentDto,
  CreateStockAdjustmentBatchDto,
  StockAdjustmentSearchRequest,
  UpdateStockAdjustmentDto,
  RejectStockAdjustmentDto
} from '../models/stock-adjustment.model';

@Injectable({
  providedIn: 'root'
})
export class StockAdjustmentService {
  private apiUrl = `${environment.apiUrl}/stock-adjustments`;

  adjustments = signal<StockAdjustmentDto[]>([]);
  totalCount = signal<number>(0);
  isLoading = signal<boolean>(false);
  error = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  /** Get all stock adjustments */
  getAll(request?: Partial<StockAdjustmentSearchRequest>): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    let params = new HttpParams();
    if (request?.locationId != null) params = params.set('locationId', request.locationId.toString());
    if (request?.locationType) params = params.set('locationType', request.locationType);
    if (request?.variantId != null) params = params.set('variantId', request.variantId.toString());
    if (request?.status) params = params.set('status', request.status);

    return this.http.get<any>(this.apiUrl, { params }).pipe(
      tap({
        next: (response) => {
          this.adjustments.set(response.data?.stockAdjustments || response.data || []);
          this.totalCount.set(response.data?.totalCount || response.data?.length || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load stock adjustments');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Search stock adjustments with pagination */
  search(request: StockAdjustmentSearchRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    return this.http.post<any>(`${this.apiUrl}/search`, request).pipe(
      tap({
        next: (response) => {
          this.adjustments.set(response.data?.stockAdjustments || []);
          this.totalCount.set(response.data?.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to search stock adjustments');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get stock adjustment by ID */
  getById(id: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}`);
  }

  update(id: number, dto: UpdateStockAdjustmentDto): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    return this.http.put<any>(`${this.apiUrl}/${id}`, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to update stock adjustment');
          this.isLoading.set(false);
        }
      })
    );
  }

  delete(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    return this.http.delete<any>(`${this.apiUrl}/${id}`).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to delete stock adjustment');
          this.isLoading.set(false);
        }
      })
    );
  }

  submit(id: number): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/${id}/submit`, {});
  }

  approve(id: number): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/${id}/approve`, {});
  }

  reject(id: number, dto: RejectStockAdjustmentDto): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/${id}/reject`, dto);
  }

  cancel(id: number): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/${id}/cancel`, {});
  }

  /** Create a new stock adjustment */
  create(dto: CreateStockAdjustmentDto): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    return this.http.post<any>(this.apiUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to create stock adjustment');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Create a stock adjustment header with multiple line items */
  createBatch(dto: CreateStockAdjustmentBatchDto): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    return this.http.post<any>(`${this.apiUrl}/batch`, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to create stock adjustment');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get adjustment history for a variant at a location */
  getHistory(variantId: number, locationId: number): Observable<any> {
    const params = new HttpParams()
      .set('variantId', variantId.toString())
      .set('locationId', locationId.toString());
    return this.http.get<any>(`${this.apiUrl}/history`, { params });
  }
}
