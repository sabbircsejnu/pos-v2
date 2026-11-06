import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  CashSessionCloseSummaryDto,
  CashSessionDto,
  CloseCashSessionDto,
  OpenCashSessionDto
} from './pos.service';

export interface ApiEnvelope<T> {
  success: boolean;
  message?: string;
  data: T;
  errorCode?: string;
}

export type CashSessionState = 'open' | 'overdue' | 'closed';

export interface CashSessionListItem {
  id: number;
  outletId: number;
  outletName: string;
  terminalId: number;
  terminalName: string;
  businessDate: string;
  openedAt: string;
  closedAt?: string;
  openingCash: number;
  expectedCash?: number;
  countedCash?: number;
  variance?: number;
  status: string;
  state: CashSessionState;
  durationHours: number;
  openedByUserId: number;
  openedByName?: string;
  closedByUserId?: number;
  closedByName?: string;
  varianceStatus?: string;
}

export interface CashSessionListPage {
  items: CashSessionListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
  serverNowUtc?: string;
}

export interface CashSessionListFilter {
  outletId: number;
  terminalId?: number | null;
  from?: string | null;
  to?: string | null;
  state?: string | null;
  openedByUserId?: number | null;
  page?: number;
  pageSize?: number;
}

export interface CashSessionTerminalOption {
  id: number;
  name: string;
  code: string;
  hasOpenSession: boolean;
}

export interface CashSessionOpenContext {
  outletId: number;
  outletName: string;
  terminals: CashSessionTerminalOption[];
  autoSelectedTerminalId?: number | null;
  timeZoneId: string;
  businessDate: string;
  serverNowUtc: string;
  maxDurationHours: number;
}

export interface CashHandoverDetail {
  id: number;
  method: string;
  amount: number;
  status: string;
  givenByName?: string;
  recipientName?: string;
  bankName?: string;
  bankAccountReference?: string;
  depositReference?: string;
  notes?: string;
  createdAtUtc: string;
  confirmedAtUtc?: string;
}

export interface CashMovementDetail {
  id: number;
  movementType: string;
  cashEffect: number;
  sourceType: string;
  sourceId?: number;
  originalSaleId?: number;
  originalCashSessionId?: number;
  notes?: string;
  recordedAtUtc: string;
  recordedByUserId: number;
}

export interface CashAttachmentDetail {
  id: number;
  handoverId?: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  sha256: string;
  uploadedAtUtc: string;
}

export interface CashSessionSaleDetail {
  saleId: number;
  saleNumber: string;
  status: string;
  totalAmount: number;
  cashRetained: number;
  nonCash: number;
  createdAt: string;
  cashierName?: string;
}

export interface CashDifferenceApproval {
  id: number;
  cashSessionId: number;
  varianceAmount: number;
  reason: string;
  status: 'pending' | 'approved' | 'rejected';
  postingStatus: 'not_required' | 'pending' | 'posted' | 'failed';
  postingAttempts: number;
  lastPostingError?: string;
  journalEntryId?: number;
  createdAtUtc: string;
  reviewedAtUtc?: string;
  reviewNotes?: string;
  postedAtUtc?: string;
}

export interface CashSessionCloseSnapshot {
  openingCash: number;
  cashSalesRetained: number;
  cashTendered: number;
  changeGiven: number;
  cardTotal: number;
  mobileTotal: number;
  drawerExpenses: number;
  cashRefunds: number;
  corrections: number;
  expectedCash: number;
  countedCash: number;
  variance: number;
  varianceReason?: string;
  handedOverTotal: number;
  retainedAtOutlet: number;
  denominationsJson?: string;
  notes?: string;
  closedAtUtc: string;
  wasOverdueAtClose: boolean;
  snapshotHash: string;
}

export interface CashSessionTimelineEntry {
  atUtc: string;
  kind: string;
  title: string;
  detail?: string;
  actorUserId?: number;
}

export interface CashSessionDetail {
  session: CashSessionDto;
  outletName: string;
  terminalName: string;
  openedByName?: string;
  closedByName?: string;
  summary: CashSessionCloseSummaryDto;
  closeSnapshot?: CashSessionCloseSnapshot;
  handovers: CashHandoverDetail[];
  movements: CashMovementDetail[];
  attachments: CashAttachmentDetail[];
  sales: CashSessionSaleDetail[];
  variance?: CashDifferenceApproval;
  timeline: CashSessionTimelineEntry[];
}

/** Daily Sales for one outlet business date (Phase 4). Completed sales are totalled; voided/refunded are counted only. */
/** Target against net sales excluding VAT (FR-TARGET-04). `variance` is signed: actual minus target. */
export interface BusinessDayTarget {
  outletId: number;
  businessDate: string;
  netSales: number;
  /** null when not configured; zero on a scheduled off day. */
  target: number | null;
  variance: number | null;
  achievementPercent: number | null;
  status: 'ok' | 'not_configured' | 'off_day';
  serverNowUtc: string;
}

export interface BusinessDaySales {
  outletId: number;
  outletName: string;
  businessDate: string;
  timeZoneId: string;
  serverNowUtc: string;
  completedCount: number;
  completedTotal: number;
  completedTax: number;
  completedNetOfTax: number;
  completedDiscount: number;
  voidedCount: number;
  refundedCount: number;
  refundedOriginalTotal: number;
}

/** Client for the dedicated Cash Session API (`/api/cash-sessions`). Authorization stays on the server. */
@Injectable({ providedIn: 'root' })
export class CashSessionService {
  private readonly url = `${environment.apiUrl}/cash-sessions`;

  constructor(private readonly http: HttpClient) {}

  list(filter: CashSessionListFilter): Observable<ApiEnvelope<CashSessionListPage>> {
    let params = new HttpParams().set('outletId', filter.outletId);
    if (filter.terminalId) params = params.set('terminalId', filter.terminalId);
    if (filter.from) params = params.set('from', filter.from);
    if (filter.to) params = params.set('to', filter.to);
    if (filter.state) params = params.set('state', filter.state);
    if (filter.openedByUserId) params = params.set('openedByUserId', filter.openedByUserId);
    params = params.set('page', filter.page ?? 1).set('pageSize', filter.pageSize ?? 20);
    return this.http.get<ApiEnvelope<CashSessionListPage>>(this.url, { params });
  }

  openContext(outletId: number): Observable<ApiEnvelope<CashSessionOpenContext>> {
    return this.http.get<ApiEnvelope<CashSessionOpenContext>>(`${this.url}/open-context`, {
      params: new HttpParams().set('outletId', outletId)
    });
  }

  businessDaySales(outletId: number, businessDate?: string | null): Observable<ApiEnvelope<BusinessDaySales>> {
    let params = new HttpParams().set('outletId', outletId);
    if (businessDate) params = params.set('businessDate', businessDate);
    return this.http.get<ApiEnvelope<BusinessDaySales>>(`${this.url}/business-day-sales`, { params });
  }

  businessDayTarget(outletId: number, businessDate?: string | null): Observable<ApiEnvelope<BusinessDayTarget>> {
    let params = new HttpParams().set('outletId', outletId);
    if (businessDate) params = params.set('businessDate', businessDate);
    return this.http.get<ApiEnvelope<BusinessDayTarget>>(`${this.url}/business-day-target`, { params });
  }

  /** Closing summary PDF from the immutable close snapshot (closed sessions only). */
  closingSummaryPdf(id: number): Observable<Blob> {
    return this.http.get(`${this.url}/${id}/closing-summary.pdf`, { responseType: 'blob' });
  }

  detail(id: number): Observable<ApiEnvelope<CashSessionDetail>> {
    return this.http.get<ApiEnvelope<CashSessionDetail>>(`${this.url}/${id}`);
  }

  summary(id: number): Observable<ApiEnvelope<CashSessionCloseSummaryDto>> {
    return this.http.get<ApiEnvelope<CashSessionCloseSummaryDto>>(`${this.url}/${id}/summary`);
  }

  open(dto: OpenCashSessionDto): Observable<ApiEnvelope<CashSessionDto>> {
    return this.http.post<ApiEnvelope<CashSessionDto>>(`${this.url}/open`, dto);
  }

  close(id: number, dto: CloseCashSessionDto): Observable<ApiEnvelope<CashSessionDto>> {
    return this.http.post<ApiEnvelope<CashSessionDto>>(`${this.url}/${id}/close`, dto);
  }

  confirmHandover(sessionId: number, handoverId: number): Observable<ApiEnvelope<CashHandoverDetail>> {
    return this.http.post<ApiEnvelope<CashHandoverDetail>>(`${this.url}/${sessionId}/handovers/${handoverId}/confirm`, {});
  }

  uploadAttachment(sessionId: number, file: File, handoverId?: number): Observable<ApiEnvelope<CashAttachmentDetail>> {
    const body = new FormData();
    body.append('file', file);
    let params = new HttpParams();
    if (handoverId) params = params.set('handoverId', handoverId);
    return this.http.post<ApiEnvelope<CashAttachmentDetail>>(`${this.url}/${sessionId}/attachments`, body, { params });
  }

  downloadAttachment(sessionId: number, attachmentId: number): Observable<Blob> {
    return this.http.get(`${this.url}/${sessionId}/attachments/${attachmentId}`, { responseType: 'blob' });
  }

  reviewVariance(approvalId: number, approve: boolean, notes?: string): Observable<ApiEnvelope<CashDifferenceApproval>> {
    return this.http.post<ApiEnvelope<CashDifferenceApproval>>(`${this.url}/variances/${approvalId}/review`, { approve, notes });
  }

  retryVariancePosting(approvalId: number): Observable<ApiEnvelope<CashDifferenceApproval>> {
    return this.http.post<ApiEnvelope<CashDifferenceApproval>>(`${this.url}/variances/${approvalId}/post`, {});
  }
}
