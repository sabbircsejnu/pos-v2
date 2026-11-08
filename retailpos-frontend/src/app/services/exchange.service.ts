import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { SUPPRESS_ERROR_NOTICE } from '../interceptors/error.interceptor';
import {
  ConfirmExchangeRequest,
  ExchangeApiResponse,
  ExchangeApprovalToken,
  ExchangeDetail,
  ExchangeListFilters,
  ExchangeListItem,
  ExchangeListPage,
  ExchangeMetrics,
  ExchangeMetricsFilters,
  ExchangePolicy,
  ExchangePreview,
  ExchangeReceiptDocument,
  ExchangeReceiptPrintAttempt,
  ExchangeRequest,
  ExchangeResult,
  ExchangeReversalEligibility,
  ExchangeReversalResult,
  ExchangeSourceInvoice,
  GrantExchangeApprovalsRequest,
  ReverseExchangeRequest,
  SaveExchangePolicyRequest
} from '../models/exchange.model';

/**
 * Product Exchange API client (`/api/pos/exchanges`). It only transports requests: the server owns every permission,
 * scope, amount and eligibility decision. Supervisor credentials are passed straight through and never stored here.
 */
@Injectable({ providedIn: 'root' })
export class ExchangeService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/pos/exchanges`;

  getPolicy(): Observable<ExchangeApiResponse<ExchangePolicy>> {
    return this.http.get<ExchangeApiResponse<ExchangePolicy>>(`${this.baseUrl}/policy`);
  }

  savePolicy(body: SaveExchangePolicyRequest): Observable<ExchangeApiResponse<ExchangePolicy>> {
    return this.http.put<ExchangeApiResponse<ExchangePolicy>>(`${this.baseUrl}/policy`, body);
  }

  findSourceInvoice(invoiceNumber: string): Observable<ExchangeApiResponse<ExchangeSourceInvoice>> {
    const params = new HttpParams().set('invoiceNumber', invoiceNumber.trim());
    return this.http.get<ExchangeApiResponse<ExchangeSourceInvoice>>(`${this.baseUrl}/source-invoice`, { params });
  }

  preview(body: ExchangeRequest): Observable<ExchangeApiResponse<ExchangePreview>> {
    return this.http.post<ExchangeApiResponse<ExchangePreview>>(`${this.baseUrl}/preview`, body);
  }

  grantApprovals(body: GrantExchangeApprovalsRequest): Observable<ExchangeApiResponse<ExchangeApprovalToken[]>> {
    return this.http.post<ExchangeApiResponse<ExchangeApprovalToken[]>>(`${this.baseUrl}/approvals`, body, { context: this.ownErrorDisplay() });
  }

  confirm(body: ConfirmExchangeRequest): Observable<ExchangeApiResponse<ExchangeResult>> {
    return this.http.post<ExchangeApiResponse<ExchangeResult>>(this.baseUrl, body, { context: this.ownErrorDisplay() });
  }

  list(filters: ExchangeListFilters = {}): Observable<ExchangeApiResponse<ExchangeListPage>> {
    let params = new HttpParams();
    if (filters.search?.trim()) params = params.set('search', filters.search.trim());
    if (filters.from) params = params.set('from', filters.from);
    if (filters.to) params = params.set('to', filters.to);
    if (filters.outletId) params = params.set('outletId', String(filters.outletId));
    if (filters.status) params = params.set('status', filters.status);
    if (filters.page) params = params.set('page', String(filters.page));
    if (filters.pageSize) params = params.set('pageSize', String(filters.pageSize));
    return this.http.get<ExchangeApiResponse<ExchangeListPage>>(this.baseUrl, { params });
  }

  getMetrics(filters: ExchangeMetricsFilters = {}): Observable<ExchangeApiResponse<ExchangeMetrics>> {
    let params = new HttpParams();
    if (filters.from) params = params.set('from', filters.from);
    if (filters.to) params = params.set('to', filters.to);
    if (filters.outletId) params = params.set('outletId', String(filters.outletId));
    return this.http.get<ExchangeApiResponse<ExchangeMetrics>>(`${this.baseUrl}/metrics`, { params });
  }

  getById(id: number): Observable<ExchangeApiResponse<ExchangeDetail>> {
    return this.http.get<ExchangeApiResponse<ExchangeDetail>>(`${this.baseUrl}/${id}`);
  }

  listBySale(saleId: number): Observable<ExchangeApiResponse<ExchangeListItem[]>> {
    return this.http.get<ExchangeApiResponse<ExchangeListItem[]>>(`${this.baseUrl}/by-sale/${saleId}`);
  }

  getReceiptDocument(id: number): Observable<ExchangeApiResponse<ExchangeReceiptDocument>> {
    return this.http.get<ExchangeApiResponse<ExchangeReceiptDocument>>(`${this.baseUrl}/${id}/receipt-document`);
  }

  getReceiptPdf(id: number, mode: 'thermal' | 'a4' = 'thermal'): Observable<Blob> {
    const params = new HttpParams().set('mode', mode);
    return this.http.get(`${this.baseUrl}/${id}/receipt-pdf`, { params, responseType: 'blob' });
  }

  recordReprint(id: number): Observable<ExchangeApiResponse<unknown>> {
    return this.http.post<ExchangeApiResponse<unknown>>(`${this.baseUrl}/${id}/reprint`, {});
  }

  recordPrintAttempt(id: number, body: ExchangeReceiptPrintAttempt): Observable<ExchangeApiResponse<unknown>> {
    return this.http.post<ExchangeApiResponse<unknown>>(`${this.baseUrl}/${id}/receipt-print-attempt`, body);
  }

  getReversalEligibility(id: number): Observable<ExchangeApiResponse<ExchangeReversalEligibility>> {
    return this.http.get<ExchangeApiResponse<ExchangeReversalEligibility>>(`${this.baseUrl}/${id}/reversal-eligibility`);
  }

  reverse(id: number, body: ReverseExchangeRequest): Observable<ExchangeApiResponse<ExchangeReversalResult>> {
    return this.http.post<ExchangeApiResponse<ExchangeReversalResult>>(`${this.baseUrl}/${id}/reverse`, body, { context: this.ownErrorDisplay() });
  }

  /**
   * The approval dialog, Confirm and the reversal dialog show the specific exchange error themselves (for example
   * "the supervisor has no access to this outlet"); a global "You do not have permission" notice would cover and
   * contradict it. 403 still never signs the user out.
   */
  private ownErrorDisplay(): HttpContext {
    return new HttpContext().set(SUPPRESS_ERROR_NOTICE, true);
  }
}
