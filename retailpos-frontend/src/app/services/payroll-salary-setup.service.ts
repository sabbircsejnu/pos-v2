import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface PayrollSalaryComponentData {
  code: string;
  name: string;
  componentType: 'Earning' | 'Deduction';
  valueType: 'Fixed' | 'Percentage' | 'Formula' | 'Calculated';
  taxTreatmentCode: string | null;
  isRecurring: boolean;
  calculationOrder: number;
  formulaExpression: string | null;
}

export interface PayrollSalaryComponentRow {
  id: number;
  businessId: number;
  data: PayrollSalaryComponentData;
  isActive: boolean;
  version: number;
}

export interface PayrollSalaryStructureLineData {
  salaryComponentId: number;
  defaultValue: number | null;
  formulaOverride: string | null;
  calculationOrder: number;
}

export interface PayrollSalaryStructureData {
  code: string;
  name: string;
  lines: PayrollSalaryStructureLineData[];
}

export interface PayrollSalaryStructureRow {
  id: number;
  businessId: number;
  data: PayrollSalaryStructureData;
  isActive: boolean;
  version: number;
}

export interface PayrollSalarySetupComponentInput {
  salaryComponentId: number;
  value: number | null;
}

export interface PayrollSalarySetupComponent {
  salaryComponentId: number;
  code: string;
  name: string;
  componentType: string;
  valueType: string;
  value: number | null;
}

export interface PayrollSalaryApprovalStep {
  sequence: number;
  decision: string;
  decidedByUserId: number | null;
  decidedAtUtc: string | null;
  comment: string | null;
}

export interface PayrollSalaryDecision {
  decision: string;
  actorUserId: number;
  occurredAtUtc: string;
  reason: string | null;
}

export interface PayrollSalarySetupRequest {
  id: number;
  businessId: number;
  employeeId: number;
  salaryStructureId: number;
  salaryStructureVersion: number;
  effectiveFrom: string;
  operationalLocationProjectionId: number;
  structureCode: string;
  structureName: string;
  components: PayrollSalarySetupComponent[];
  reason: string;
  status: string;
  approvalInstanceId: number | null;
  createdByUserId: number;
  createdAtUtc: string;
  submittedByUserId: number | null;
  submittedAtUtc: string | null;
  decidedByUserId: number | null;
  decidedAtUtc: string | null;
  decisionReason: string | null;
  version: number;
  approvalSteps: PayrollSalaryApprovalStep[];
  approvalHistory: PayrollSalaryDecision[];
  isReplay: boolean;
}

export interface PayrollSalaryRevisionRequest {
  id: number;
  businessId: number;
  employeeId: number;
  sourceSalaryVersionId: number;
  effectiveFrom: string;
  operationalLocationProjectionId: number;
  currentComponents: PayrollSalarySetupComponent[];
  proposedComponents: PayrollSalarySetupComponent[];
  reason: string;
  status: string;
  approvalInstanceId: number | null;
  createdByUserId: number;
  createdAtUtc: string;
  submittedByUserId: number | null;
  submittedAtUtc: string | null;
  decidedByUserId: number | null;
  decidedAtUtc: string | null;
  decisionReason: string | null;
  version: number;
  approvalSteps: PayrollSalaryApprovalStep[];
  approvalHistory: PayrollSalaryDecision[];
  isReplay: boolean;
}

export interface PayrollEmployeeSalary {
  id: number;
  businessId: number;
  employeeId: number;
  salarySetupRequestId: number;
  salaryStructureId: number;
  salaryStructureVersion: number;
  effectiveFrom: string;
  operationalLocationProjectionId: number;
  structureCode: string;
  structureName: string;
  components: PayrollSalarySetupComponent[];
  approvalInstanceId: number;
  approvedByUserId: number;
  approvedAtUtc: string;
  salaryRevisionRequestId?: number | null;
  predecessorSalaryVersionId?: number | null;
}

export interface PayrollEmployeeAdjustment {
  id: number;
  businessId: number;
  employeeId: number;
  adjustmentIdentity: string;
  version: number;
  predecessorVersionId: number | null;
  salaryComponentId: number;
  componentCode: string;
  componentName: string;
  componentType: string;
  valueType: string;
  adjustmentKind: 'RecurringAllowance' | 'RecurringDeduction' | 'OneTimeEarning' | 'OneTimeDeduction';
  classification: 'Manual' | 'Statutory' | 'Incentive' | 'Bonus';
  amount: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  operationalLocationProjectionId: number;
  reason: string;
  reference: string | null;
  status: 'Active' | 'Inactive';
  createdByUserId: number;
  createdAtUtc: string;
  isReplay: boolean;
}

export interface PayrollEmployeeAdjustmentRequest {
  adjustmentIdentity?: string | null;
  expectedVersion?: number | null;
  employeeId: number;
  salaryComponentId: number;
  adjustmentKind: PayrollEmployeeAdjustment['adjustmentKind'];
  classification: PayrollEmployeeAdjustment['classification'];
  amount: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  reason: string;
  reference: string | null;
  idempotencyKey: string;
}

export interface PayrollSalaryDecisionResult {
  instanceId: number;
  status: string;
  currentStepSequence: number | null;
  isReplay: boolean;
  targetStatus: string | null;
}

export interface PayrollLoanScheduleLineInput {
  dueDate: string;
  plannedAmount: number;
}

export interface PayrollLoanScheduleLine {
  installmentNo: number;
  dueDate: string;
  plannedAmount: number;
  actualRecoveredAmount: number;
  status: string;
}

export interface PayrollLoanScheduleVersion {
  id: number;
  versionNo: number;
  recoveryStartDate: string;
  recoveryMode: string;
  fixedInstallment: number | null;
  status: string;
  createdByUserId: number;
  createdAtUtc: string;
  lines: PayrollLoanScheduleLine[];
}

export interface PayrollLoanRepayment {
  id: number;
  amount: number;
  paymentDate: string;
  reference: string | null;
  reason: string;
  recordedByUserId: number;
  recordedAtUtc: string;
  isReplay: boolean;
}

export interface PayrollEmployeeLoan {
  id: number;
  businessId: number;
  employeeId: number;
  operationalLocationProjectionId: number;
  loanType: 'Loan' | 'Advance';
  principalAmount: number;
  approvedAmount: number | null;
  disbursedDate: string | null;
  recoveryStartDate: string;
  recoveryMode: 'FixedMonthly' | 'Manual' | 'CustomSchedule';
  fixedInstallment: number | null;
  outstandingAmount: number;
  status: 'Draft' | 'PendingApproval' | 'Active' | 'Rejected' | 'Cancelled' | 'Settled';
  reason: string;
  reference: string | null;
  approvalInstanceId: number | null;
  createdByUserId: number;
  createdAtUtc: string;
  submittedByUserId: number | null;
  submittedAtUtc: string | null;
  decidedByUserId: number | null;
  decidedAtUtc: string | null;
  decisionReason: string | null;
  version: number;
  approvalSteps: PayrollSalaryApprovalStep[];
  approvalHistory: PayrollSalaryDecision[];
  scheduleHistory: PayrollLoanScheduleVersion[];
  repayments: PayrollLoanRepayment[];
  isReplay: boolean;
}

@Injectable({ providedIn: 'root' })
export class PayrollSalarySetupService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/payroll/v1`;

  components() {
    return this.http.get<PayrollSalaryComponentRow[]>(`${this.base}/salary-components`);
  }

  saveComponent(data: PayrollSalaryComponentData, row: PayrollSalaryComponentRow | null) {
    return row
      ? this.http.put<PayrollSalaryComponentRow>(`${this.base}/salary-components/${row.id}`, {
          expectedVersion: row.version,
          data
        })
      : this.http.post<PayrollSalaryComponentRow>(`${this.base}/salary-components`, data);
  }

  deactivateComponent(row: PayrollSalaryComponentRow) {
    return this.http.post<PayrollSalaryComponentRow>(
      `${this.base}/salary-components/${row.id}/deactivate`,
      { expectedVersion: row.version }
    );
  }

  structures() {
    return this.http.get<PayrollSalaryStructureRow[]>(`${this.base}/salary-structures`);
  }

  saveStructure(data: PayrollSalaryStructureData, row: PayrollSalaryStructureRow | null) {
    return row
      ? this.http.put<PayrollSalaryStructureRow>(`${this.base}/salary-structures/${row.id}`, {
          expectedVersion: row.version,
          data
        })
      : this.http.post<PayrollSalaryStructureRow>(`${this.base}/salary-structures`, data);
  }

  deactivateStructure(row: PayrollSalaryStructureRow) {
    return this.http.post<PayrollSalaryStructureRow>(
      `${this.base}/salary-structures/${row.id}/deactivate`,
      { expectedVersion: row.version }
    );
  }

  createInitialSalarySetup(data: {
    employeeId: number;
    salaryStructureId: number;
    effectiveFrom: string;
    components: PayrollSalarySetupComponentInput[];
    reason: string;
    idempotencyKey: string;
  }) {
    return this.http.post<PayrollSalarySetupRequest>(`${this.base}/salary-setup-requests`, data);
  }

  employeeSalarySetupRequests(employeeId: number) {
    return this.http.get<PayrollSalarySetupRequest[]>(
      `${this.base}/employees/${employeeId}/salary-setup-requests`
    );
  }

  createSalaryRevision(data: {
    employeeId: number;
    sourceSalaryVersionId: number;
    effectiveFrom: string;
    components: PayrollSalarySetupComponentInput[];
    reason: string;
    idempotencyKey: string;
  }) {
    return this.http.post<PayrollSalaryRevisionRequest>(`${this.base}/salary-revision-requests`, data);
  }

  employeeSalaryRevisionRequests(employeeId: number) {
    return this.http.get<PayrollSalaryRevisionRequest[]>(
      `${this.base}/employees/${employeeId}/salary-revision-requests`
    );
  }

  submitSalaryRevision(request: PayrollSalaryRevisionRequest) {
    return this.http.post<PayrollSalaryDecisionResult>(
      `${this.base}/salary-revision-requests/${request.id}/submit`,
      { expectedVersion: request.version }
    );
  }

  decideSalaryRevision(
    request: PayrollSalaryRevisionRequest,
    decision: 'approve' | 'reject' | 'cancel',
    reason: string
  ) {
    if (request.approvalInstanceId === null) {
      throw new Error('The salary revision request has no approval instance.');
    }
    return this.http.post<PayrollSalaryDecisionResult>(
      `${this.base}/salary-revision-approvals/${request.approvalInstanceId}/${decision}`,
      { decisionKey: crypto.randomUUID(), reason: reason || null }
    );
  }

  employeeSalaryHistory(employeeId: number) {
    return this.http.get<PayrollEmployeeSalary[]>(
      `${this.base}/employees/${employeeId}/salary-history`
    );
  }

  employeeAdjustments(employeeId: number, asOf: string) {
    return this.http.get<PayrollEmployeeAdjustment[]>(
      `${this.base}/employees/${employeeId}/adjustments`,
      { params: { asOf } }
    );
  }

  adjustmentHistory(adjustmentIdentity: string) {
    return this.http.get<PayrollEmployeeAdjustment[]>(
      `${this.base}/adjustments/${adjustmentIdentity}/history`
    );
  }

  saveAdjustment(data: PayrollEmployeeAdjustmentRequest) {
    return data.adjustmentIdentity
      ? this.http.put<PayrollEmployeeAdjustment>(
          `${this.base}/adjustments/${data.adjustmentIdentity}`, data
        )
      : this.http.post<PayrollEmployeeAdjustment>(`${this.base}/adjustments`, data);
  }

  endAdjustment(adjustment: PayrollEmployeeAdjustment, effectiveFrom: string, reason: string) {
    return this.http.post<PayrollEmployeeAdjustment>(
      `${this.base}/adjustments/${adjustment.adjustmentIdentity}/end`,
      {
        expectedVersion: adjustment.version,
        effectiveFrom,
        reason,
        idempotencyKey: crypto.randomUUID()
      }
    );
  }

  submitInitialSalarySetup(request: PayrollSalarySetupRequest) {
    return this.http.post<PayrollSalaryDecisionResult>(
      `${this.base}/salary-setup-requests/${request.id}/submit`,
      { expectedVersion: request.version }
    );
  }

  decideInitialSalarySetup(
    request: PayrollSalarySetupRequest,
    decision: 'approve' | 'reject' | 'cancel',
    reason: string
  ) {
    if (request.approvalInstanceId === null) {
      throw new Error('The salary setup request has no approval instance.');
    }
    return this.http.post<PayrollSalaryDecisionResult>(
      `${this.base}/salary-setup-approvals/${request.approvalInstanceId}/${decision}`,
      { decisionKey: crypto.randomUUID(), reason: reason || null }
    );
  }

  employeeLoans(employeeId: number) {
    return this.http.get<PayrollEmployeeLoan[]>(`${this.base}/loans`, {
      params: { employeeId }
    });
  }

  createEmployeeLoan(data: {
    employeeId: number;
    loanType: 'Loan' | 'Advance';
    principalAmount: number;
    recoveryStartDate: string;
    recoveryMode: 'FixedMonthly' | 'Manual' | 'CustomSchedule';
    fixedInstallment: number | null;
    schedule: PayrollLoanScheduleLineInput[];
    reason: string;
    reference: string | null;
    idempotencyKey: string;
  }) {
    return this.http.post<PayrollEmployeeLoan>(`${this.base}/loans`, data);
  }

  submitEmployeeLoan(loan: PayrollEmployeeLoan) {
    return this.http.post<PayrollSalaryDecisionResult>(
      `${this.base}/loans/${loan.id}/submit`,
      { expectedVersion: loan.version }
    );
  }

  decideEmployeeLoan(
    loan: PayrollEmployeeLoan,
    decision: 'approve' | 'reject' | 'cancel',
    reason: string
  ) {
    if (loan.approvalInstanceId === null) {
      throw new Error('The employee loan has no approval instance.');
    }
    return this.http.post<PayrollSalaryDecisionResult>(
      `${this.base}/loans/approvals/${loan.approvalInstanceId}/${decision}`,
      { decisionKey: crypto.randomUUID(), reason: reason || null }
    );
  }

  updateEmployeeLoanSchedule(loan: PayrollEmployeeLoan, data: {
    recoveryStartDate: string;
    recoveryMode: 'FixedMonthly' | 'Manual' | 'CustomSchedule';
    fixedInstallment: number | null;
    schedule: PayrollLoanScheduleLineInput[];
    idempotencyKey: string;
  }) {
    return this.http.put<PayrollEmployeeLoan>(
      `${this.base}/loans/${loan.id}/schedule`,
      { ...data, expectedLoanVersion: loan.version }
    );
  }

  recordEmployeeLoanRepayment(loan: PayrollEmployeeLoan, data: {
    amount: number;
    paymentDate: string;
    reason: string;
    reference: string | null;
    idempotencyKey: string;
  }) {
    return this.http.post<PayrollEmployeeLoan>(
      `${this.base}/loans/${loan.id}/repayments`,
      data
    );
  }
}
