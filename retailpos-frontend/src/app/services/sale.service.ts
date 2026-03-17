import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { CreateSaleDto, SaleDto, SaleListDto, SaleSummaryDto, VoidSaleDto } from '../models/sale.model';

@Injectable({
  providedIn: 'root'
})
export class SaleService {
  private apiUrl = `${environment.apiUrl}/sales`;

  sales = signal<SaleListDto[]>([]);
  totalCount = signal<number>(0);
  isLoading = signal<boolean>(false);
  error = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  /** Create a new sale */
  createSale(dto: CreateSaleDto): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    return this.http.post<any>(this.apiUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to create sale');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Search/list sales with filters */
  searchSales(filters: {
    searchQuery?: string;
    status?: string;
    startDate?: string;
    endDate?: string;
    pageNumber?: number;
    pageSize?: number;
  }): Observable<any> {
    let params = new HttpParams();
    if (filters.searchQuery) params = params.set('searchQuery', filters.searchQuery);
    if (filters.status) params = params.set('status', filters.status);
    if (filters.startDate) params = params.set('startDate', filters.startDate);
    if (filters.endDate) params = params.set('endDate', filters.endDate);
    if (filters.pageNumber) params = params.set('pageNumber', filters.pageNumber.toString());
    if (filters.pageSize) params = params.set('pageSize', filters.pageSize.toString());

    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(`${this.apiUrl}`, { params }).pipe(
      tap({
        next: (response) => {
          const data = response.data;
          this.sales.set(data?.sales || data || []);
          this.totalCount.set(data?.totalCount || (Array.isArray(data) ? data.length : 0));
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load sales');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get sale by ID */
  getSaleById(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    return this.http.get<any>(`${this.apiUrl}/${id}`).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to load sale');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Void a sale */
  voidSale(id: number, dto: VoidSaleDto): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/${id}/void`, dto);
  }

  /** Get today's sales summary */
  getTodaySummary(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/today/summary`);
  }

  /** Get sale receipt */
  getSaleReceipt(id: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}/receipt`);
  }
}
