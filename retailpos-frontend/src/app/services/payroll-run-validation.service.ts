import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface PayrollRunValidationFinding {
  code: string;
  category: string;
  severity: 'BlockingError' | 'Warning';
  employeeId: number | null;
  relatedRunId: number | null;
  message: string;
}

export interface PayrollRunValidationResponse {
  payrollRunId: number;
  payrollPeriodId: number;
  runStatus: string;
  calculationVersion: number;
  isCalculated: boolean;
  canSubmit: boolean;
  blockingErrorCount: number;
  warningCount: number;
  page: number;
  pageSize: number;
  totalFindings: number;
  findings: PayrollRunValidationFinding[];
}

export interface PayrollRunResponse {
  id: number;
  status: string;
  version: number;
  approvalInstanceId: number | null;
  submittedByUserId: number | null;
  submittedAtUtc: string | null;
  decidedByUserId: number | null;
  decidedAtUtc: string | null;
  decisionReason: string | null;
}

export interface PayrollRunApprovalResult {
  instanceId: number;
  status: string;
  currentStepSequence: number | null;
  isReplay: boolean;
  targetStatus: string | null;
}

export interface PayrollAccountingPosting {
  id: number;
  businessId: number;
  payrollPeriodId: number;
  payrollRunId: number;
  runRevision: number;
  postingType: string;
  postingReference: string;
  idempotencyKey: string;
  requestPayloadHash: string | null;
  conflictPayloadHash: string | null;
  accountingJournalId: number | null;
  accountingReference: string | null;
  status: string;
  errorMessage: string | null;
  attemptCount: number;
  version: number;
  postedAtUtc: string | null;
}

export interface PayrollAccountingMapping {
  componentType: string;
  componentCode: string;
  accountId: number;
  accountCode: string;
  accountName: string;
  isActive: boolean;
  version: number;
}

@Injectable({ providedIn: 'root' })
export class PayrollRunValidationService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/payroll/v1/periods`;

  get(periodId: number, runId: number, page = 1, pageSize = 50) {
    return this.http.get<PayrollRunValidationResponse>(
      `${this.base}/${periodId}/runs/${runId}/validation`,
      { params: { page, pageSize } }
    );
  }

  getRun(periodId: number, runId: number) {
    return this.http.get<PayrollRunResponse>(`${this.base}/${periodId}/runs/${runId}`);
  }

  submit(periodId: number, runId: number, expectedVersion: number) {
    return this.http.post<PayrollRunApprovalResult>(
      `${this.base}/${periodId}/runs/${runId}/submit`, { expectedVersion }
    );
  }

  decide(instanceId: number, decision: 'approve' | 'reject') {
    return this.http.post<PayrollRunApprovalResult>(
      `${environment.apiUrl}/payroll/v1/approvals/runs/${instanceId}/${decision}`,
      { decisionKey: crypto.randomUUID(), reason: null }
    );
  }

  postAccounting(runId: number) {
    return this.http.post<{ posting: PayrollAccountingPosting | null; message: string | null }>(
      `${environment.apiUrl}/payroll/v1/runs/${runId}/post-accounting`, {}
    );
  }

  getAccountingPosting(runId: number) {
    return this.http.get<{ posting: PayrollAccountingPosting | null; message: string | null }>(
      `${environment.apiUrl}/payroll/v1/runs/${runId}/accounting-posting`
    );
  }

  getAccountingMappings() {
    return this.http.get<PayrollAccountingMapping[]>(
      `${environment.apiUrl}/payroll/v1/accounting-mappings`
    );
  }

  upsertAccountingMapping(componentType: string, componentCode: string, accountId: number) {
    return this.http.put<PayrollAccountingMapping>(
      `${environment.apiUrl}/payroll/v1/accounting-mappings`,
      { componentType, componentCode, accountId }
    );
  }
}
