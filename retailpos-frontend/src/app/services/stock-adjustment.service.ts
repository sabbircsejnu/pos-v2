import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  StockAdjustmentDto,
  CreateStockAdjustmentDto,
  StockAdjustmentSearchRequest
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
  getAll(): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    return this.http.get<any>(this.apiUrl).pipe(
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
          this.adjustments.set(response.data?.adjustments || response.data || []);
          this.totalCount.set(response.data?.totalCount || response.data?.length || 0);
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

  /** Get adjustment history for a variant at a location */
  getHistory(variantId: number, locationId: number): Observable<any> {
    const params = new HttpParams()
      .set('variantId', variantId.toString())
      .set('locationId', locationId.toString());
    return this.http.get<any>(`${this.apiUrl}/history`, { params });
  }
}
