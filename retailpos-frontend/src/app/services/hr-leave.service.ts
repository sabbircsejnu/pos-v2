import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface HrLeaveType {
  id: number;
  code: string;
  name: string;
  isPaid: boolean;
  requiresDocument: boolean;
  isActive: boolean;
  version: number;
}

export interface HrLeavePolicy {
  id: number;
  leaveTypeId: number;
  code: string;
  name: string;
  scopeType: number;
  scopeReference: string | null;
  versionNo: number;
  accrualMethod: number;
  annualEntitlement: number;
  carryForwardAllowed: boolean;
  carryForwardCap: number | null;
  carryForwardExpiryMonths: number | null;
  maximumBalance: number | null;
  negativeBalanceAllowed: boolean;
  payrollImpactCode: string | null;
  leaveYearStartMonth: number;
  leaveYearStartDay: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  isActive: boolean;
}

export interface HrLeaveBalance {
  employeeId: number;
  leaveTypeId: number;
  leaveTypeCode: string;
  leaveTypeName: string;
  leaveYearStart: string;
  leaveYearEnd: string;
  policyVersionNo: number;
  accrualMethod: number;
  opening: number;
  accrued: number;
  used: number;
  restored: number;
  adjusted: number;
  carriedForward: number;
  expired: number;
  remaining: number;
  version: number;
  status: 'Open' | 'ClosingReview' | 'Closed';
  closeVersion: number;
  closingBalance: number | null;
  closeCarryForward: number | null;
  closeExpired: number | null;
  carryForwardExpiresOn: string | null;
  lastLifecycleAtUtc: string | null;
  lastLifecycleAction: string | null;
  lastLifecycleReason: string | null;
}

export interface HrLeaveYearLifecycle {
  employeeId: number;
  leaveTypeId: number;
  leaveYearStart: string;
  leaveYearEnd: string;
  status: 'Open' | 'ClosingReview' | 'Closed';
  closeVersion: number;
  version: number;
  policyVersionNo: number;
  balanceBeforeClose: number;
  carryForwardAmount: number;
  expiredAmount: number;
  carryForwardExpiresOn: string | null;
  occurredAtUtc: string;
  action: string;
  reason: string;
  isReplay: boolean;
}

export interface HrLeaveLedgerEntry {
  id: number;
  employeeId: number;
  leaveTypeId: number;
  leaveYearStart: string;
  transactionType: number;
  quantity: number;
  effectiveDate: string;
  sourceType: string;
  sourceId: string;
  reason: string;
  actorRealUserId: number;
  createdAtUtc: string;
}

export interface HrLeaveRequestDay {
  workDate: string;
  rosterId: number | null;
  operationalLocationProjectionId: number;
  dayClassification: string;
  units: number;
  policyVersionNo: number;
  shiftSnapshotJson: string;
  calendarEvidenceJson: string;
}

export interface HrLeaveRequest {
  id: number;
  employeeId: number;
  employeeName: string;
  leaveTypeId: number;
  leaveTypeCode: string;
  leaveTypeName: string;
  startDate: string;
  endDate: string;
  requestedUnits: number;
  reason: string;
  status: string;
  scopeLocationProjectionId: number;
  requestedByUserId: number;
  requestedAtUtc: string;
  decidedByUserId: number | null;
  decidedAtUtc: string | null;
  decisionReason: string | null;
  approvalInstanceId: number | null;
  approvalStatus: number | null;
  currentApprovalStep: number | null;
  attachmentVersionId: string | null;
  version: number;
  days: HrLeaveRequestDay[];
}

export interface HrLeaveRequestAttachment {
  id: string;
  versionNo: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  scanStatus: number;
  uploadedByUserId: number;
  uploadedAtUtc: string;
}

export interface HrLeavePolicyInput extends Omit<HrLeavePolicy, 'id' | 'versionNo' | 'leaveTypeId' | 'accrualMethod'> {
  versionNo: number;
  leaveTypeId: number;
  accrualMethod: number;
}

@Injectable({ providedIn: 'root' })
export class HrLeaveService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/hr/v1/leave`;

  getTypes() {
    return this.http.get<HrLeaveType[]>(`${this.base}/types`);
  }

  getMyRequests() {
    return this.http.get<HrLeaveRequest[]>(`${this.base}/requests/mine`);
  }

  getRequests(status: string | null = null) {
    let params = new HttpParams();
    if (status !== null) params = params.set('status', status);
    return this.http.get<HrLeaveRequest[]>(`${this.base}/requests`, { params });
  }

  createRequest(input: { leaveTypeId: number; startDate: string; endDate: string; reason: string }) {
    return this.http.post<HrLeaveRequest>(`${this.base}/requests`, input);
  }

  submitRequest(request: HrLeaveRequest) {
    return this.http.post(`${this.base}/requests/${request.id}/submit`, { expectedVersion: request.version });
  }

  cancelRequest(requestId: number, decisionKey: string, reason: string | null) {
    return this.http.post<HrLeaveRequest>(`${this.base}/requests/${requestId}/cancel`, { decisionKey, reason });
  }

  getRequestAttachments(requestId: number) {
    return this.http.get<HrLeaveRequestAttachment[]>(`${this.base}/requests/${requestId}/attachments`);
  }

  uploadRequestAttachment(requestId: number, file: File) {
    const data = new FormData();
    data.append('file', file, file.name);
    return this.http.post<HrLeaveRequestAttachment>(
      `${this.base}/requests/${requestId}/attachments`, data);
  }

  downloadRequestAttachment(requestId: number, versionId: string) {
    return this.http.get(
      `${this.base}/requests/${requestId}/attachments/${versionId}`, { responseType: 'blob' });
  }

  decideRequest(instanceId: number, decision: 'approve' | 'reject', decisionKey: string, reason: string | null) {
    return this.http.post(`${this.base}/approvals/${instanceId}/${decision}`, { decisionKey, reason });
  }

  createType(input: { code: string; name: string; isPaid: boolean; requiresDocument: boolean }) {
    return this.http.post<HrLeaveType>(`${this.base}/types`, input);
  }

  getPolicies(leaveTypeId: number | null) {
    let params = new HttpParams();
    if (leaveTypeId !== null) params = params.set('leaveTypeId', leaveTypeId);
    return this.http.get<HrLeavePolicy[]>(`${this.base}/policies`, { params });
  }

  createPolicy(input: HrLeavePolicyInput) {
    return this.http.post<HrLeavePolicy>(`${this.base}/policies`, input);
  }

  getBalances(employeeId: number, leaveTypeId: number | null) {
    let params = new HttpParams();
    if (leaveTypeId !== null) params = params.set('leaveTypeId', leaveTypeId);
    return this.http.get<HrLeaveBalance[]>(`${this.base}/employees/${employeeId}/balances`, { params });
  }

  getLedger(employeeId: number, leaveTypeId: number | null) {
    let params = new HttpParams();
    if (leaveTypeId !== null) params = params.set('leaveTypeId', leaveTypeId);
    return this.http.get<HrLeaveLedgerEntry[]>(`${this.base}/employees/${employeeId}/ledger`, { params });
  }

  postLedgerEntry(input: {
    employeeId: number; leaveTypeId: number; transactionType: 0 | 4; quantity: number;
    effectiveDate: string; idempotencyKey: string; reason: string;
  }) {
    return this.http.post<{ entries: HrLeaveLedgerEntry[]; isReplay: boolean }>(
      `${this.base}/employees/${input.employeeId}/ledger`, {
        leaveTypeId: input.leaveTypeId, transactionType: input.transactionType,
        quantity: input.quantity, effectiveDate: input.effectiveDate,
        idempotencyKey: input.idempotencyKey, reason: input.reason
      });
  }

  accrue(input: { employeeId: number; leaveTypeId: number; throughDate: string }) {
    return this.http.post<{ entries: HrLeaveLedgerEntry[]; isReplay: boolean }>(
      `${this.base}/employees/${input.employeeId}/accruals`,
      { leaveTypeId: input.leaveTypeId, throughDate: input.throughDate });
  }

  reviewLeaveYearClose(balance: HrLeaveBalance, idempotencyKey: string) {
    return this.http.post<HrLeaveYearLifecycle>(
      `${this.base}/employees/${balance.employeeId}/leave-years/${balance.leaveYearStart}/review-close`,
      { leaveTypeId: balance.leaveTypeId, expectedVersion: balance.version, idempotencyKey });
  }

  closeLeaveYear(balance: HrLeaveBalance, idempotencyKey: string) {
    return this.http.post<HrLeaveYearLifecycle>(
      `${this.base}/employees/${balance.employeeId}/leave-years/${balance.leaveYearStart}/close`,
      { leaveTypeId: balance.leaveTypeId, expectedVersion: balance.version, idempotencyKey });
  }

  reopenLeaveYear(balance: HrLeaveBalance, idempotencyKey: string, reason: string) {
    return this.http.post<HrLeaveYearLifecycle>(
      `${this.base}/employees/${balance.employeeId}/leave-years/${balance.leaveYearStart}/reopen`,
      { leaveTypeId: balance.leaveTypeId, expectedVersion: balance.version, idempotencyKey, reason });
  }
}
