import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiEnvelope } from './cash-session.service';

export interface SaleFinancialPosting {
  id: number;
  saleId: number;
  kind: 'sale' | 'reversal' | string;
  reversalType?: string | null;
  sourceKey: string;
  status: 'pending' | 'posted' | 'failed' | string;
  outletId: number;
  businessDate: string;
  attempts: number;
  lastErrorCode?: string | null;
  lastError?: string | null;
  nextAttemptAtUtc?: string | null;
  lastAttemptAtUtc?: string | null;
  journalEntryId?: number | null;
  postedAtUtc?: string | null;
  createdAtUtc: string;
}

export interface FinancialPostingWorkerLiveness {
  worker: string;
  instanceId: string;
  hostName: string;
  hostRole: string;
  lastRunCompletedAtUtc?: string | null;
  consecutiveRunFailures: number;
  stale: boolean;
}

export interface FinancialPostingSummary {
  sales: { pending: number; failed: number; posted: number; awaitingSalePosting: number; oldestUnpostedCreatedAtUtc?: string | null };
  cashDifferencesDue: number;
  workers: FinancialPostingWorkerLiveness[];
}

/** Client for the D10 reconciliation API (Phase 3.2 permissions `sales.financial_postings.view/retry`). */
@Injectable({ providedIn: 'root' })
export class FinancialPostingService {
  private readonly url = `${environment.apiUrl}/financial-postings`;

  constructor(private readonly http: HttpClient) {}

  summary(): Observable<ApiEnvelope<FinancialPostingSummary>> {
    return this.http.get<ApiEnvelope<FinancialPostingSummary>>(`${this.url}/summary`);
  }

  list(status: string | null, take = 50): Observable<ApiEnvelope<SaleFinancialPosting[]>> {
    let params = new HttpParams().set('take', take);
    if (status) params = params.set('status', status);
    return this.http.get<ApiEnvelope<SaleFinancialPosting[]>>(`${this.url}/sales`, { params });
  }

  /** Retries one posting as is: no amount, date or snapshot can be overridden (audited on the server). */
  retry(postingId: number): Observable<ApiEnvelope<{ outcome: string; posting: SaleFinancialPosting }>> {
    return this.http.post<ApiEnvelope<{ outcome: string; posting: SaleFinancialPosting }>>(`${this.url}/sales/${postingId}/retry`, {});
  }
}
