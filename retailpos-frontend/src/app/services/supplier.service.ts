import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  Supplier,
  CreateSupplierDto,
  UpdateSupplierDto,
  SupplierPerformanceDto,
  SupplierSearchRequest,
  SupplierListResponse
} from '../models/supplier.model';

interface ApiResponse<T> {
  success: boolean;
  message?: string;
  data?: T;
}

@Injectable({
  providedIn: 'root'
})
export class SupplierService {
  private apiUrl = `${environment.apiUrl}/suppliers`;

  // Signals for reactive state
  suppliers = signal<Supplier[]>([]);
  totalCount = signal(0);
  isLoading = signal(false);
  error = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  /**
   * Search suppliers with filters and pagination
   */
  search(searchRequest: SupplierSearchRequest): Observable<ApiResponse<SupplierListResponse>> {
    this.isLoading.set(true);
    this.error.set(null);
    
    return this.http.post<ApiResponse<SupplierListResponse>>(`${this.apiUrl}/search`, searchRequest).pipe(
      tap({
        next: (response) => {
          this.suppliers.set(response.data?.suppliers || []);
          this.totalCount.set(response.data?.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.error?.message || 'Failed to load suppliers');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Load all suppliers
   */
  loadSuppliers(): void {
    this.isLoading.set(true);
    this.getAll().subscribe({
      next: (response) => {
        this.suppliers.set(response.data || []);
        this.totalCount.set(response.data?.length || 0);
        this.isLoading.set(false);
      },
      error: () => this.isLoading.set(false)
    });
  }

  /**
   * Get all suppliers
   */
  getAll(): Observable<ApiResponse<Supplier[]>> {
    return this.http.get<ApiResponse<Supplier[]>>(this.apiUrl);
  }

  /**
   * Get supplier by ID
   */
  getById(id: number): Observable<ApiResponse<Supplier>> {
    return this.http.get<ApiResponse<Supplier>>(`${this.apiUrl}/${id}`);
  }

  /**
   * Create new supplier
   */
  create(dto: CreateSupplierDto): Observable<ApiResponse<Supplier>> {
    return this.http.post<ApiResponse<Supplier>>(this.apiUrl, dto).pipe(
      tap(() => this.loadSuppliers())
    );
  }

  /**
   * Update supplier
   */
  update(id: number, dto: UpdateSupplierDto): Observable<ApiResponse<Supplier>> {
    return this.http.put<ApiResponse<Supplier>>(`${this.apiUrl}/${id}`, dto).pipe(
      tap(() => this.loadSuppliers())
    );
  }

  /**
   * Delete supplier
   */
  delete(id: number): Observable<ApiResponse<void>> {
    return this.http.delete<ApiResponse<void>>(`${this.apiUrl}/${id}`).pipe(
      tap(() => this.loadSuppliers())
    );
  }

  /**
   * Get supplier performance metrics
   */
  getPerformance(id: number): Observable<ApiResponse<SupplierPerformanceDto>> {
    return this.http.get<ApiResponse<SupplierPerformanceDto>>(`${this.apiUrl}/${id}/performance`);
  }
}
