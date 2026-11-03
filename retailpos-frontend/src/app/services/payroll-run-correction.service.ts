import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface PayrollSpecialRunResponse {
  id: number;
  runType: string;
  status: string;
  version: number;
  runReference: string | null;
  correctionReference: string | null;
  originalPayrollRunId: number | null;
  originalCalculationVersion: number | null;
  originalEvidenceHash: string | null;
  correctionType: string | null;
  correctionReason: string | null;
  downstreamReversalRequired: boolean;
  paymentCorrectionRequired: boolean;
  accountingCorrectionRequired: boolean;
}

@Injectable({ providedIn: 'root' })
export class PayrollRunCorrectionService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/payroll/v1/periods`;

  createCorrection(periodId: number, originalRunId: number, request: {
    correctionReference: string;
    correctionType: string;
    reason: string;
    employeeIds: number[];
    idempotencyKey: string;
    expectedPeriodVersion: number;
    expectedOriginalRunVersion: number;
  }) {
    return this.http.post<PayrollSpecialRunResponse>(
      `${this.base}/${periodId}/runs/${originalRunId}/corrections`, request
    );
  }

  createOffCycle(periodId: number, request: {
    runReference: string;
    reason: string;
    scopeType: 'Business';
    scopeId: null;
    employeeIds: number[];
    idempotencyKey: string;
    expectedPeriodVersion: number;
  }) {
    return this.http.post<PayrollSpecialRunResponse>(
      `${this.base}/${periodId}/runs/off-cycle`, request
    );
  }
}
