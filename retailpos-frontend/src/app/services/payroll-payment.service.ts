import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface PayrollPaymentAllocationInput {
  method: 'Cash' | 'Bank' | 'Mobile';
  amount: number;
  externalReference: string | null;
}

export interface PayrollPaymentAllocation {
  id: number;
  allocationNo: number;
  method: string;
  amount: number;
  settledAmount: number;
  state: string;
  externalReference: string | null;
  paymentDestinationId: number | null;
  paymentDestinationVersionNo: number | null;
  bankName: string | null;
  accountName: string | null;
  accountNumber: string | null;
  mobileProvider: string | null;
  mobileAccountName: string | null;
  mobileAccountNumber: string | null;
}

export interface PayrollPayment {
  id: number;
  businessId: number;
  payrollPeriodId: number;
  payrollRunId: number;
  payrollEmployeeResultId: number;
  employeeId: number;
  locationIdSnapshot: number | null;
  payableAmount: number;
  paidAmount: number;
  outstandingAmount: number;
  currencyCode: string;
  method: string;
  status: string;
  version: number;
  attemptNo: number;
  originalPaymentId: number | null;
  idempotencyKey: string;
  createdByUserId: number;
  createdAtUtc: string;
  notes: string | null;
  allocations: PayrollPaymentAllocation[];
  paidAtUtc: string | null;
  externalReference: string | null;
  failureReturnReason: string | null;
  heldByUserId: number | null;
  heldAtUtc: string | null;
  holdReason: string | null;
  releasedByUserId: number | null;
  releasedAtUtc: string | null;
  releaseReason: string | null;
  resolutionStatus: string;
  resolutionEvidenceReference: string | null;
  evidence: PayrollPaymentEvidence[];
  paymentBatchId: number | null;
  isReplay: boolean;
}

export interface PayrollPaymentEvidence {
  id: number;
  type: 'Settlement' | 'Return';
  amount: number;
  evidenceDate: string;
  evidenceReference: string;
  verifiedByUserId: number;
  verifiedAtUtc: string;
  allocations: { allocationNo: number; method: string; amount: number }[];
}

export interface PayrollAccountingPostingResponse {
  posting: {
    status: string;
    accountingJournalId: number | null;
    errorMessage: string | null;
  } | null;
  message: string | null;
}

export interface PayrollPaymentReconciliation {
  id: number;
  businessId: number;
  payrollPaymentId: number;
  paymentBatchId: number | null;
  employeeId: number;
  payrollPeriodId: number;
  allocationNo: number;
  expectedAmount: number;
  settledAmount: number;
  difference: number;
  status: 'Unreconciled' | 'PartiallyReconciled' | 'Reconciled' | 'Exception';
  settlementDate: string | null;
  evidenceSource: string;
  externalReference: string | null;
  notes: string | null;
  version: number;
  paymentVersion: number;
  reconciledByUserId: number;
  reconciledAtUtc: string;
  isReplay: boolean;
}

export interface ReconcilePayrollPaymentAllocationRequest {
  expectedPaymentVersion: number;
  expectedReconciliationVersion: number;
  idempotencyKey: string;
  allocationNo: number;
  settledAmount: number;
  settlementDate: string | null;
  evidenceSource: string;
  externalReference: string | null;
  notes: string | null;
  isException: boolean;
}

export interface ReconcilePayrollPaymentBatchItemRequest {
  paymentId: number;
  expectedPaymentVersion: number;
  allocationNo: number;
  expectedReconciliationVersion: number;
  settledAmount: number;
  settlementDate: string | null;
  evidenceSource: string;
  externalReference: string | null;
  notes: string | null;
  isException: boolean;
}

export interface PayrollPaymentReconciliationOperation {
  items: PayrollPaymentReconciliation[];
  isReplay: boolean;
}

export interface CreatePayrollPaymentRequest {
  payrollEmployeeResultId: number;
  expectedRunVersion: number;
  idempotencyKey: string;
  allocations: PayrollPaymentAllocationInput[];
  notes: string | null;
}

export interface PayrollPaymentStateChangeRequest {
  expectedVersion: number;
  reason: string;
}

export interface PayrollPaymentFailureRequest extends PayrollPaymentStateChangeRequest {
  externalReference: string | null;
}

export interface PayrollPaymentEvidenceRequest {
  expectedVersion: number;
  idempotencyKey: string;
  evidenceDate: string;
  evidenceReference: string;
  reason: string | null;
  allocations: { allocationNo: number; amount: number }[];
}

export interface PayrollPaymentBatchItem {
  paymentId: number;
  expectedVersion: number;
}

export interface CreatePayrollPaymentBatchRequest {
  batchReference: string;
  idempotencyKey: string;
  payments: PayrollPaymentBatchItem[];
}

export interface PayrollPaymentBatch {
  id: number;
  businessId: number;
  payrollRunId: number;
  payrollPeriodId: number;
  batchReference: string;
  method: string;
  currencyCode: string;
  expectedAmount: number;
  status: string;
  version: number;
  createdByUserId: number;
  createdAtUtc: string;
  payments: {
    paymentId: number;
    employeeId: number;
    amount: number;
    status: string;
    version: number;
    method: string;
  }[];
  bankFileVersions: {
    versionNo: number;
    contentHash: string;
    rowCount: number;
    expectedAmount: number;
    fileName: string;
    generatedByUserId: number;
    generatedAtUtc: string;
    generationReason: string;
  }[];
  isReplay: boolean;
  submittedAtUtc: string | null;
  submittedByUserId: number | null;
}

export interface RetryPayrollPaymentRequest {
  expectedVersion: number;
  idempotencyKey: string;
  resolutionEvidenceReference: string;
  allocations: PayrollPaymentAllocationInput[];
  notes: string | null;
}

export interface PayrollPaymentBankFileRequest {
  expectedVersion: number;
  idempotencyKey: string;
  reason: string;
}

export interface SubmitPayrollPaymentBatchRequest {
  expectedVersion: number;
  idempotencyKey: string;
  externalReference: string;
  reason: string;
}

@Injectable({ providedIn: 'root' })
export class PayrollPaymentService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/payroll/v1/payments`;
  private readonly batches = `${environment.apiUrl}/payroll/v1/payment-batches`;
  private readonly reconciliation =
    `${environment.apiUrl}/payroll/v1/payment-reconciliation`;

  create(request: CreatePayrollPaymentRequest) {
    return this.http.post<PayrollPayment>(this.base, request);
  }

  get(paymentId: number) {
    return this.http.get<PayrollPayment>(`${this.base}/${paymentId}`);
  }

  hold(paymentId: number, request: PayrollPaymentStateChangeRequest) {
    return this.http.post<PayrollPayment>(`${this.base}/${paymentId}/hold`, request);
  }

  release(paymentId: number, request: PayrollPaymentStateChangeRequest) {
    return this.http.post<PayrollPayment>(`${this.base}/${paymentId}/release`, request);
  }

  fail(paymentId: number, request: PayrollPaymentFailureRequest) {
    return this.http.post<PayrollPayment>(`${this.base}/${paymentId}/fail`, request);
  }

  settle(paymentId: number, request: PayrollPaymentEvidenceRequest) {
    return this.http.post<PayrollPayment>(`${this.base}/${paymentId}/settlements`, request);
  }

  return(paymentId: number, request: PayrollPaymentEvidenceRequest) {
    return this.http.post<PayrollPayment>(`${this.base}/${paymentId}/returns`, request);
  }

  postSettlementAccounting(paymentId: number, allocationId: number, evidenceId: number) {
    return this.http.post<PayrollAccountingPostingResponse>(
      `${environment.apiUrl}/payroll/v1/payments/${paymentId}/allocations/${allocationId}` +
      `/settlement-evidence/${evidenceId}/post-accounting`, {}
    );
  }

  retry(paymentId: number, request: RetryPayrollPaymentRequest) {
    return this.http.post<PayrollPayment>(`${this.base}/${paymentId}/retry`, request);
  }

  listBatches(payrollRunId?: number) {
    const url = payrollRunId
      ? `${this.batches}?payrollRunId=${payrollRunId}`
      : this.batches;
    return this.http.get<PayrollPaymentBatch[]>(url);
  }

  getBatch(batchId: number) {
    return this.http.get<PayrollPaymentBatch>(`${this.batches}/${batchId}`);
  }

  createBatch(payrollRunId: number, request: CreatePayrollPaymentBatchRequest) {
    return this.http.post<PayrollPaymentBatch>(
      `${environment.apiUrl}/payroll/v1/runs/${payrollRunId}/payment-batches`,
      request
    );
  }

  generateBankFile(batchId: number, request: PayrollPaymentBankFileRequest) {
    return this.http.post(`${this.batches}/${batchId}/generate-bank-file`, request, {
      responseType: 'blob',
      observe: 'response'
    });
  }

  submitBatch(batchId: number, request: SubmitPayrollPaymentBatchRequest) {
    return this.http.post<PayrollPaymentBatch>(`${this.batches}/${batchId}/submit`, request);
  }

  listReconciliations(paymentId?: number, paymentBatchId?: number) {
    const params = new URLSearchParams();
    if (paymentId) params.set('paymentId', String(paymentId));
    if (paymentBatchId) params.set('paymentBatchId', String(paymentBatchId));
    const query = params.toString();
    return this.http.get<PayrollPaymentReconciliation[]>(
      query ? `${this.reconciliation}?${query}` : this.reconciliation
    );
  }

  reconcilePayment(paymentId: number, request: ReconcilePayrollPaymentAllocationRequest) {
    return this.http.post<PayrollPaymentReconciliationOperation>(
      `${this.base}/${paymentId}/reconcile`, request
    );
  }

  reconcileBatch(
    batchId: number,
    request: {
      expectedBatchVersion: number;
      idempotencyKey: string;
      items: ReconcilePayrollPaymentBatchItemRequest[];
    }
  ) {
    return this.http.post<PayrollPaymentReconciliationOperation>(
      `${this.batches}/${batchId}/reconcile`, request
    );
  }
}
