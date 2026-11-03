import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface HrClearanceItem {
  id: number;
  sequence: number;
  code: string;
  name: string;
  responsibleRole: string | null;
  responsibleReference: string | null;
  isMandatory: boolean;
  status: string;
  clearedByUserId: number | null;
  clearedAtUtc: string | null;
  comment: string | null;
  evidenceReference: string | null;
  version: number;
}

export interface HrExitEvent {
  sequence: number;
  eventType: string;
  previousStatus: string | null;
  newStatus: string;
  actualLastWorkingDate: string | null;
  reason: string | null;
  actorUserId: number;
  occurredAtUtc: string;
}

export interface HrExitCase {
  id: number;
  employeeId: number;
  employeeCode: string;
  employeeName: string;
  exitType: string;
  initiatedDate: string;
  proposedLastWorkingDate: string;
  actualLastWorkingDate: string | null;
  reason: string;
  status: string;
  approvalInstanceId: number | null;
  primaryLocationProjectionId: number | null;
  locationType: string | null;
  clearanceTemplateId: number;
  clearanceTemplateVersionNo: number;
  version: number;
  clearanceItems: HrClearanceItem[];
  events: HrExitEvent[];
}

export interface HrExitFilters {
  from: string;
  to: string;
  employeeId: number | null;
  status: string;
}

export interface HrExitRequest {
  employeeId: number;
  exitType: string;
  initiatedDate: string;
  proposedLastWorkingDate: string;
  reason: string;
}

export interface HrExitCompletionSettlement {
  settlementId: number;
  revisionNo: number;
  status: string;
  completionMode: 'PositiveSettlement' | 'ZeroSettlement' | 'NegativeSettlement';
  netSettlement: number;
  currencyCode: string;
  submittedEvidenceHash: string | null;
  paymentRequired: boolean;
  activePaymentCount: number;
  outstandingAmount: number;
  paymentsSatisfied: boolean;
  reconciliationRequired: boolean;
  reconciliationSatisfied: boolean;
  accountingRequired: boolean;
  requiredPostingType: string | null;
  accountingSatisfied: boolean;
  requiredPaymentAccountingCount: number;
  postedPaymentAccountingCount: number;
}

export interface HrExitCompletionPreview {
  exitCaseId: number;
  exitVersion: number;
  payrollEnabled: boolean;
  canComplete: boolean;
  pendingMandatoryClearanceCount: number;
  reasonlessNotApplicableCount: number;
  blockers: string[];
  settlement: HrExitCompletionSettlement | null;
}

export interface PayrollFinalSettlementLine {
  lineNo: number;
  componentCode: string;
  componentName: string;
  lineType: 'Earning' | 'Deduction' | 'NotApplicable';
  amount: number;
  isApplicable: boolean;
  notApplicableReason: string | null;
  sourceType: string;
  sourceId: string | null;
  evidenceSnapshotJson: string;
}

export interface PayrollFinalSettlement {
  id: number;
  exitCaseId: number;
  employeeId: number;
  revisionNo: number;
  settlementDate: string;
  currencyCode: string;
  decimalPrecision: number;
  roundingMode: string;
  grossDue: number;
  totalDeductions: number;
  netSettlement: number;
  calculationInputHash: string;
  snapshotHash: string;
  snapshotJson: string;
  calculatedAtUtc: string;
  calculatedByUserId: number;
  lines: PayrollFinalSettlementLine[];
  isReplay: boolean;
  status: string;
  approvalInstanceId: number | null;
  requestedByUserId: number | null;
  requestedAtUtc: string | null;
  approvalPolicyCode: string | null;
  approvalPolicyVersion: number | null;
  submittedEvidenceHash: string | null;
  approvalHistory: PayrollFinalSettlementApprovalDecision[] | null;
}

export interface PayrollFinalSettlementApprovalDecision {
  decision: string;
  actorUserId: number;
  occurredAtUtc: string;
  reason: string | null;
  instanceStatusAfterDecision: string;
}

export interface PayrollFinalSettlementAccountingStatus {
  id: number;
  revisionNo: number;
  postingType: string;
  postingReference: string;
  status: string;
  accountingJournalId: number | null;
  accountingReference: string | null;
  errorMessage: string | null;
  attemptCount: number;
  version: number;
  postedAtUtc: string | null;
}

export interface PayrollFinalSettlementExecutionPayment {
  id: number;
  payableAmount: number;
  paidAmount: number;
  outstandingAmount: number;
  currencyCode: string;
  method: string;
  status: string;
  attemptNo: number;
  evidence: {
    id: number;
    type: string;
    amount: number;
    evidenceDate: string;
    evidenceReference: string;
  }[];
}

export interface PayrollFinalSettlementExecution {
  finalSettlementId: number;
  revisionNo: number;
  settlementStatus: string;
  approvalInstanceId: number | null;
  payments: PayrollFinalSettlementExecutionPayment[];
  accountingPostings: PayrollFinalSettlementAccountingStatus[];
}

export interface FinalSettlementPaymentRequest {
  revisionNo: number;
  idempotencyKey: string;
  allocations: { method: 'Cash' | 'Bank' | 'Mobile'; amount: number }[];
  notes?: string | null;
}

export interface ApprovalDecisionResult {
  instanceId: number;
  status: string;
  currentStepSequence: number | null;
  isReplay: boolean;
  targetStatus: string | null;
}

export interface HrClearanceItemInput {
  sequence: number;
  code: string;
  name: string;
  responsibleRole: string | null;
  responsibleReference: string | null;
  isMandatory: boolean;
}

export interface HrClearanceTemplate {
  id: number;
  code: string;
  name: string;
  scopeType: 'Business' | 'LocationType' | 'Location';
  scopeReference: string | null;
  versionNo: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  isActive: boolean;
  items: HrClearanceItemInput[];
}

export interface HrClearanceTemplateRequest extends Omit<HrClearanceTemplate, 'id'> {}

@Injectable({ providedIn: 'root' })
export class HrExitService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/hr/v1/exits`;

  cases(filters: HrExitFilters) {
    let params = new HttpParams();
    if (filters.from) params = params.set('from', filters.from);
    if (filters.to) params = params.set('to', filters.to);
    if (filters.employeeId !== null) params = params.set('employeeId', filters.employeeId);
    if (filters.status) params = params.set('status', filters.status);
    return this.http.get<HrExitCase[]>(this.base, { params });
  }

  templates() {
    return this.http.get<HrClearanceTemplate[]>(`${this.base}/templates`);
  }

  createTemplate(request: HrClearanceTemplateRequest) {
    return this.http.post<HrClearanceTemplate>(`${this.base}/templates`, request);
  }

  initiate(request: HrExitRequest) {
    return this.http.post<HrExitCase>(this.base, request);
  }

  submit(row: HrExitCase) {
    return this.http.post(`${this.base}/${row.id}/submit`, { expectedVersion: row.version });
  }

  cancel(row: HrExitCase, reason: string) {
    return this.http.post(`${this.base}/${row.id}/cancel`, {
      expectedVersion: row.version,
      decisionKey: crypto.randomUUID(),
      reason
    });
  }

  updateClearance(
    item: HrClearanceItem,
    status: string,
    comment: string | null,
    evidenceReference: string | null
  ) {
    return this.http.put<HrExitCase>(`${this.base}/clearance-items`, {
      itemId: item.id,
      expectedVersion: item.version,
      status,
      comment,
      evidenceReference
    });
  }

  complete(row: HrExitCase, actualLastWorkingDate: string, reason: string) {
    return this.http.post<HrExitCase>(`${this.base}/${row.id}/complete`, {
      expectedVersion: row.version,
      actualLastWorkingDate,
      reason
    });
  }

  completionPreview(row: HrExitCase) {
    return this.http.get<HrExitCompletionPreview>(`${this.base}/${row.id}/completion-preview`);
  }

  calculateFinalSettlement(row: HrExitCase, idempotencyKey: string) {
    return this.http.post<PayrollFinalSettlement>(
      `${this.base}/${row.id}/final-settlement/calculate`,
      { expectedExitVersion: row.version, idempotencyKey }
    );
  }

  finalSettlementForExit(row: HrExitCase) {
    return this.http.get<PayrollFinalSettlement>(`${this.base}/${row.id}/final-settlement`);
  }

  finalSettlementExecution(settlement: PayrollFinalSettlement) {
    return this.http.get<PayrollFinalSettlementExecution>(
      `${environment.apiUrl}/hr/v1/final-settlements/${settlement.id}/execution`
    );
  }

  createFinalSettlementPayment(
    settlement: PayrollFinalSettlement,
    request: FinalSettlementPaymentRequest
  ) {
    return this.http.post(
      `${environment.apiUrl}/hr/v1/final-settlements/${settlement.id}/payments`,
      request
    );
  }

  postFinalSettlementAccounting(
    settlement: PayrollFinalSettlement,
    idempotencyKey: string
  ) {
    return this.http.post<PayrollFinalSettlementAccountingStatus>(
      `${environment.apiUrl}/hr/v1/final-settlements/${settlement.id}/accounting`,
      { revisionNo: settlement.revisionNo, idempotencyKey }
    );
  }

  postFinalSettlementPaymentAccounting(
    paymentId: number,
    evidenceId: number
  ) {
    return this.http.post<PayrollFinalSettlementAccountingStatus>(
      `${environment.apiUrl}/payroll/v1/final-settlement-payments/${paymentId}` +
        `/settlement-evidence/${evidenceId}/post-accounting`,
      {}
    );
  }

  submitFinalSettlement(settlement: PayrollFinalSettlement, idempotencyKey: string) {
    return this.http.post<ApprovalDecisionResult>(
      `${environment.apiUrl}/hr/v1/final-settlements/${settlement.id}/submit`,
      { revisionNo: settlement.revisionNo, idempotencyKey }
    );
  }

  approveFinalSettlement(settlement: PayrollFinalSettlement, reason: string) {
    return this.http.post<ApprovalDecisionResult>(
      `${environment.apiUrl}/hr/v1/final-settlements/${settlement.id}/approve`,
      { revisionNo: settlement.revisionNo, decisionKey: crypto.randomUUID(), reason }
    );
  }

  rejectFinalSettlement(settlement: PayrollFinalSettlement, reason: string) {
    return this.http.post<ApprovalDecisionResult>(
      `${environment.apiUrl}/hr/v1/final-settlements/${settlement.id}/reject`,
      { revisionNo: settlement.revisionNo, decisionKey: crypto.randomUUID(), reason }
    );
  }

  cancelFinalSettlement(settlement: PayrollFinalSettlement, reason: string) {
    return this.http.post<ApprovalDecisionResult>(
      `${environment.apiUrl}/hr/v1/final-settlements/${settlement.id}/cancel`,
      { revisionNo: settlement.revisionNo, decisionKey: crypto.randomUUID(), reason }
    );
  }
}
