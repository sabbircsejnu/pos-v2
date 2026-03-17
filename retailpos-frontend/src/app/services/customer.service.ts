import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  CustomerDto,
  CreateCustomerDto,
  UpdateCustomerDto,
  CustomerListResponse
} from '../models/customer.model';

@Injectable({
  providedIn: 'root'
})
export class CustomerService {
  private apiUrl = `${environment.apiUrl}/customers`;

  customers = signal<CustomerDto[]>([]);
  totalCount = signal<number>(0);
  isLoading = signal<boolean>(false);
  error = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  /** Get all customers with optional search query and pagination */
  getAll(query?: string, page: number = 1, size: number = 10): Observable<any> {
    let params = new HttpParams()
      .set('pageNumber', page.toString())
      .set('pageSize', size.toString());
    if (query) params = params.set('query', query);

    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(this.apiUrl, { params }).pipe(
      tap({
        next: (response) => {
          const data: CustomerListResponse = response.data;
          this.customers.set(data?.customers || []);
          this.totalCount.set(data?.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load customers');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get customer by ID */
  getById(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(`${this.apiUrl}/${id}`).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to load customer');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Create a new customer */
  create(dto: CreateCustomerDto): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(this.apiUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to create customer');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Update an existing customer */
  update(id: number, dto: UpdateCustomerDto): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.put<any>(`${this.apiUrl}/${id}`, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to update customer');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Delete a customer */
  delete(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.delete<any>(`${this.apiUrl}/${id}`).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to delete customer');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Quick search for POS (returns up to 10 results) */
  quickSearch(q: string): Observable<any> {
    const params = new HttpParams().set('q', q);
    return this.http.get<any>(`${this.apiUrl}/search`, { params });
  }

  /** Add loyalty points to a customer */
  addPoints(id: number, points: number): Observable<any> {
    const params = new HttpParams().set('points', points.toString());
    return this.http.post<any>(`${this.apiUrl}/${id}/add-points`, {}, { params });
  }

  /** Redeem loyalty points from a customer */
  redeemPoints(id: number, points: number): Observable<any> {
    const params = new HttpParams().set('points', points.toString());
    return this.http.post<any>(`${this.apiUrl}/${id}/redeem-points`, {}, { params });
  }
}
