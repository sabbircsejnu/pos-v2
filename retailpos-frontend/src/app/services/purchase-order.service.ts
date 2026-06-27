import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  PurchaseOrder,
  CreatePurchaseOrderRequest,
  CreatePurchaseAndReceiveRequest,
  UpdatePurchaseOrderRequest,
  PurchaseOrderSearchRequest,
  PurchaseOrderListResponse,
  UpdatePurchaseOrderStatusRequest
} from '../models/purchase-order.model';

@Injectable({
  providedIn: 'root'
})
export class PurchaseOrderService {
  private apiUrl = `${environment.apiUrl}/purchase-orders`;

  // Signals
  purchaseOrders = signal<PurchaseOrder[]>([]);
  totalCount = signal<number>(0);
  isLoading = signal<boolean>(false);
  error = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  /**
   * Get all purchase orders with optional filters
   */
  getAll(status?: string, supplierId?: number, warehouseId?: number): Observable<any> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    if (supplierId) params = params.set('supplierId', supplierId.toString());
    if (warehouseId) params = params.set('warehouseId', warehouseId.toString());

    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(`${this.apiUrl}`, { params }).pipe(
      tap({
        next: (response) => {
          this.purchaseOrders.set(response.data || []);
          this.totalCount.set(response.data?.length || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load purchase orders');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Search purchase orders with pagination
   */
  search(searchRequest: PurchaseOrderSearchRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.apiUrl}/search`, searchRequest).pipe(
      tap({
        next: (response) => {
          this.purchaseOrders.set(response.data.purchaseOrders || []);
          this.totalCount.set(response.data.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to search purchase orders');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Get purchase order by ID
   */
  getById(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(`${this.apiUrl}/${id}`).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to load purchase order');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Get purchase orders awaiting approval
   */
  getPendingApprovals(): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.get<any>(`${this.apiUrl}/pending-approvals`).pipe(
      tap({
        next: (response) => {
          this.purchaseOrders.set(response.data || []);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.error.set(err.message || 'Failed to load pending approvals');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Get total amount for purchase orders
   */
  getTotalAmount(status?: string, startDate?: Date, endDate?: Date): Observable<any> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    if (startDate) params = params.set('startDate', startDate.toISOString());
    if (endDate) params = params.set('endDate', endDate.toISOString());

    return this.http.get<any>(`${this.apiUrl}/total-amount`, { params });
  }

  /**
   * Create a new purchase order
   */
  create(request: CreatePurchaseOrderRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(this.apiUrl, request).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to create purchase order');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Create PO and immediately receive it with auto-generated GRN
   */
  purchaseAndReceive(request: CreatePurchaseAndReceiveRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.apiUrl}/purchase-receive`, request).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to complete purchase and receive');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Update an existing purchase order
   */
  update(id: number, request: UpdatePurchaseOrderRequest): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.put<any>(`${this.apiUrl}/${id}`, request).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to update purchase order');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Delete a purchase order
   */
  delete(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.delete<any>(`${this.apiUrl}/${id}`).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to delete purchase order');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Submit purchase order for approval (Draft → Pending)
   */
  submit(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.apiUrl}/${id}/submit`, {}).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to submit purchase order');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Approve purchase order (Pending → Approved)
   */
  approve(id: number): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    return this.http.post<any>(`${this.apiUrl}/${id}/approve`, {}).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to approve purchase order');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Reject purchase order
   */
  reject(id: number, reason?: string): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    const request: UpdatePurchaseOrderStatusRequest = {
      status: 'rejected',
      reason
    };

    return this.http.post<any>(`${this.apiUrl}/${id}/reject`, request).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to reject purchase order');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Cancel purchase order
   */
  cancel(id: number, reason?: string): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    const request: UpdatePurchaseOrderStatusRequest = {
      status: 'cancelled',
      reason
    };

    return this.http.post<any>(`${this.apiUrl}/${id}/cancel`, request).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to cancel purchase order');
          this.isLoading.set(false);
        }
      })
    );
  }

  /**
   * Send purchase order back for correction (Pending → Sent Back)
   */
  sendBack(id: number, reason: string): Observable<any> {
    this.isLoading.set(true);
    this.error.set(null);

    const request: UpdatePurchaseOrderStatusRequest = {
      status: 'sent_back',
      reason
    };

    return this.http.post<any>(`${this.apiUrl}/${id}/send-back`, request).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err.message || 'Failed to send back purchase order');
          this.isLoading.set(false);
        }
      })
    );
  }
}
