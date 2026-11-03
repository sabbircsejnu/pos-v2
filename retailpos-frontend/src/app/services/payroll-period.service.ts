import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface PayrollPeriod {
  id: number;
  periodCode: string;
  periodStart: string;
  periodEnd: string;
  status: string;
  version: number;
  closedReason: string | null;
}

export interface PayrollPeriodCloseGate {
  code: string;
  passed: boolean;
  blocking: boolean;
  message: string;
  referenceId: string | null;
}

export interface PayrollPeriodCloseResponse {
  payrollPeriodId: number;
  status: string;
  version: number;
  assessmentId: number;
  policyVersion: number;
  gates: PayrollPeriodCloseGate[];
  closed: boolean;
  isReplay: boolean;
}

@Injectable({ providedIn: 'root' })
export class PayrollPeriodService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/payroll/v1/periods`;

  list() {
    return this.http.get<PayrollPeriod[]>(this.base);
  }

  get(id: number) {
    return this.http.get<PayrollPeriod>(`${this.base}/${id}`);
  }

  lock(id: number, expectedVersion: number) {
    return this.http.post<PayrollPeriod>(`${this.base}/${id}/lock`, { expectedVersion });
  }

  reopen(id: number, expectedVersion: number, reason: string) {
    return this.http.post<PayrollPeriod>(`${this.base}/${id}/reopen`, { expectedVersion, reason });
  }

  returnToOpen(id: number, expectedVersion: number, reason: string) {
    return this.http.post<PayrollPeriod>(
      `${this.base}/${id}/return-to-open`,
      { expectedVersion, reason }
    );
  }

  close(id: number, request: {
    idempotencyKey: string;
    reason: string;
    expectedVersion: number;
    policyVersion: number;
    exceptions: [];
  }) {
    return this.http.post<PayrollPeriodCloseResponse>(`${this.base}/${id}/close`, request);
  }
}
