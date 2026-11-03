import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface PayrollDashboardFilters {
  periodId: number | null;
  runId: number | null;
  locationId: number | null;
  fromDate: string;
  toDate: string;
  runType: string | null;
}

export interface PayrollDashboardMetric {
  status: string;
  count: number;
  amount: number;
}

export interface PayrollDashboardSummary {
  period: { id: number | null; code: string | null; status: string | null; start: string | null; end: string | null };
  run: {
    id: number | null;
    status: string | null;
    runType: string | null;
    employeeCount: number;
    gross: number;
    totalDeductions: number;
    netPayable: number;
    approvedAmount: number;
    processingStates: PayrollDashboardMetric[];
    runTypes: PayrollDashboardMetric[];
    unresolvedCorrectionLineageCount: number;
  };
  payments: {
    totalPayable: number;
    totalPaid: number;
    totalOutstanding: number;
    states: PayrollDashboardMetric[];
    heldCount: number;
    processingCount: number;
    failedCount: number;
    returnedCount: number;
    completedCount: number;
    methods: { method: string; settledAmount: number }[];
  };
  reconciliation: {
    applies: boolean;
    reconciledCount: number;
    partiallyReconciledCount: number;
    unreconciledCount: number;
    exceptionCount: number;
  };
  accounting: {
    enabled: boolean;
    expensePostings: { category: string; status: string; count: number }[];
    settlementPostings: { category: string; status: string; count: number }[];
    blockerCount: number;
  };
  finalSettlements: {
    approvedPositiveCount: number;
    approvedPositiveAmount: number;
    paidAmount: number;
    outstandingAmount: number;
    negativeReceivableCount: number;
    negativeReceivableAmount: number;
    financiallyCompletedCount: number;
    unresolvedFinancialCount: number;
  };
  blockers: {
    unresolvedPaymentCount: number;
    partialPaymentCount: number;
    reconciliationPendingCount: number;
    accountingPendingOrFailedCount: number;
    correctionPendingCount: number;
  };
}

@Injectable({ providedIn: 'root' })
export class PayrollDashboardService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/payroll/v1/dashboard`;

  query(filters: PayrollDashboardFilters) {
    let params = new HttpParams()
      .set('fromDate', filters.fromDate)
      .set('toDate', filters.toDate);
    if (filters.periodId !== null) params = params.set('periodId', filters.periodId);
    if (filters.runId !== null) params = params.set('runId', filters.runId);
    if (filters.locationId !== null) params = params.set('locationId', filters.locationId);
    if (filters.runType !== null) params = params.set('runType', filters.runType);
    return this.http.get<PayrollDashboardSummary>(this.url, { params });
  }
}
