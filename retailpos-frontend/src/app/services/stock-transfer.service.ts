import { Injectable, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  StockTransferDto,
  CreateStockTransferDto,
  TransferStatusUpdateDto,
  StockTransferSearchRequest
} from '../models/stock-transfer.model';

@Injectable({
  providedIn: 'root'
})
export class StockTransferService {
  private apiUrl = `${environment.apiUrl}/stock-transfers`;

  transfers = signal<StockTransferDto[]>([]);
  totalCount = signal<number>(0);
  isLoading = signal<boolean>(false);
  error = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  /** Get all stock transfers */
  getAll(): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    return this.http.get<any>(this.apiUrl).pipe(
      tap({
        next: (response) => {
          this.transfers.set(response.data?.stockTransfers || response.data || []);
          this.totalCount.set(response.data?.totalCount || response.data?.length || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load stock transfers');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Search stock transfers with pagination */
  search(request: StockTransferSearchRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    return this.http.post<any>(`${this.apiUrl}/search`, request).pipe(
      tap({
        next: (response) => {
          this.transfers.set(response.data?.transfers || response.data || []);
          this.totalCount.set(response.data?.totalCount || response.data?.length || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to search stock transfers');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Get stock transfer by ID */
  getById(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    return this.http.get<any>(`${this.apiUrl}/${id}`).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to load stock transfer');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Create a new stock transfer */
  create(dto: CreateStockTransferDto): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);
    return this.http.post<any>(this.apiUrl, dto).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to create stock transfer');
          this.isLoading.set(false);
        }
      })
    );
  }

  /** Approve a stock transfer */
  approve(id: number, dto?: TransferStatusUpdateDto): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/${id}/approve`, dto || {});
  }

  /** Reject a stock transfer */
  reject(id: number, dto: TransferStatusUpdateDto): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/${id}/reject`, dto);
  }

  /** Mark as in-transit */
  send(id: number): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/${id}/send`, {});
  }

  /** Receive and update stock */
  receive(id: number): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/${id}/receive`, {});
  }

  /** Cancel a stock transfer */
  cancel(id: number, dto: TransferStatusUpdateDto): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/${id}/cancel`, dto);
  }
}
