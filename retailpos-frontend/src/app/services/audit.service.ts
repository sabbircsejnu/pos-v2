import { Injectable, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import {
  AuditEventDetails,
  AuditEventListResponse,
  AuditExportRequest,
  AuditListFilter,
} from '../models/audit.model';

@Injectable({ providedIn: 'root' })
export class AuditService {
  private apiUrl = `${environment.apiUrl}/audit-events`;

  isLoading = signal(false);
  error = signal<string | null>(null);

  constructor(private http: HttpClient) {}

  list(filter: AuditListFilter = {}): Observable<AuditEventListResponse> {
    let params = new HttpParams()
      .set('page', String(filter.page ?? 1))
      .set('pageSize', String(filter.pageSize ?? 50));

    if (filter.search) params = params.set('search', filter.search);
    if (filter.fromDate) params = params.set('fromDate', filter.fromDate);
    if (filter.toDate) params = params.set('toDate', filter.toDate);
    if (filter.userId) params = params.set('userId', String(filter.userId));
    if (filter.outletId) params = params.set('outletId', String(filter.outletId));
    if (filter.status) params = params.set('status', filter.status);
    (filter.actionType ?? []).forEach((a) => (params = params.append('actionType', a)));
    (filter.module ?? []).forEach((m) => (params = params.append('module', m)));
    (filter.source ?? []).forEach((s) => (params = params.append('source', s)));

    this.isLoading.set(true);
    this.error.set(null);
    return this.http.get<AuditEventListResponse>(this.apiUrl, { params }).pipe(
      tap({
        next: () => this.isLoading.set(false),
        error: (err) => {
          this.error.set(err?.message ?? 'Failed to load audit events');
          this.isLoading.set(false);
        },
      })
    );
  }

  details(id: number): Observable<AuditEventDetails> {
    return this.http.get<AuditEventDetails>(`${this.apiUrl}/${id}`);
  }

  export(req: AuditExportRequest): Observable<Blob> {
    return this.http.post(`${this.apiUrl}/export`, req, { responseType: 'blob' });
  }
}
