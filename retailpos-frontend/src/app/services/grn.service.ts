import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  Grn,
  GrnVariance,
  PurchaseOrderForGrn,
  CreateGrnDto,
  GrnSearchRequest,
  GrnListResponse
} from '../models/grn.model';

@Injectable({
  providedIn: 'root'
})
export class GrnService {
  private apiUrl = `${environment.apiUrl}/grns`;

  grns = signal<Grn[]>([]);
  totalCount = signal<number>(0);
  isLoading = signal<boolean>(false);
  error = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  /** Get list of approved POs awaiting receipt */
  getPendingPOs(): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/pending-pos`);
  }

  /** Search GRNs with pagination */
  search(searchRequest: GrnSearchRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.apiUrl}/search`, searchRequest).pipe(
      tap({
        next: (response) => {
          const data: GrnListResponse = response.data;
          this.grns.set(data?.grns || []);
          this.totalCount.set(data?.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load GRNs');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get GRN by ID */
  getById(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(`${this.apiUrl}/${id}`).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to load GRN');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Create a new GRN */
  create(dto: CreateGrnDto): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(this.apiUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to create GRN');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Complete a GRN and update stock */
  complete(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.apiUrl}/${id}/complete`, {}).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to complete GRN');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get variance report for a GRN */
  getVariance(id: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/${id}/variance`);
  }
}
