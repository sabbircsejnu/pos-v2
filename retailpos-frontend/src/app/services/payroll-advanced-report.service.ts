import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export type PayrollAdvancedReportKind =
  | 'PayrollRegister'
  | 'PayrollComponents'
  | 'PayrollVariance'
  | 'CorrectionOffCycle'
  | 'SalaryHistory'
  | 'LoanRecovery'
  | 'RunStatus'
  | 'PaymentOutstanding'
  | 'CostByLocation';

export interface PayrollAdvancedReportFilters {
  kind: PayrollAdvancedReportKind;
  from: string;
  to: string;
  employeeId: number | null;
  locationId: number | null;
  runId: number | null;
  compareRunId: number | null;
  page: number;
  pageSize: number;
}

export interface PayrollAdvancedReportRow {
  employeeId: number | null;
  employeeCode: string | null;
  employeeName: string | null;
  periodId: number | null;
  periodCode: string | null;
  runId: number | null;
  runRevision: number | null;
  runType: string | null;
  originalRunId: number | null;
  category: string;
  componentCode: string | null;
  componentName: string | null;
  gross: number | null;
  deductions: number | null;
  net: number | null;
  status: string;
  locationId: number | null;
  locationName: string | null;
  effectiveDate: string | null;
  effectiveTo: string | null;
  salaryVersionId: number | null;
  opening: number | null;
  recovered: number | null;
  outstanding: number | null;
  detail: string | null;
}

export interface PayrollAdvancedReportPage {
  kind: PayrollAdvancedReportKind;
  items: PayrollAdvancedReportRow[];
  page: number;
  pageSize: number;
  totalCount: number;
}

@Injectable({ providedIn: 'root' })
export class PayrollAdvancedReportService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/payroll/v1/reports/advanced`;

  query(filters: PayrollAdvancedReportFilters) {
    return this.http.get<PayrollAdvancedReportPage>(this.url, {
      params: this.params(filters).set('page', filters.page).set('pageSize', filters.pageSize)
    });
  }

  export(filters: PayrollAdvancedReportFilters, format: 'csv' | 'xlsx') {
    return this.http.get(`${this.url}/export/${format}`, {
      params: this.params(filters),
      responseType: 'blob'
    });
  }

  private params(filters: PayrollAdvancedReportFilters) {
    let params = new HttpParams()
      .set('kind', filters.kind)
      .set('from', filters.from)
      .set('to', filters.to);
    if (filters.employeeId !== null) params = params.set('employeeId', filters.employeeId);
    if (filters.locationId !== null) params = params.set('locationId', filters.locationId);
    if (filters.runId !== null) params = params.set('runId', filters.runId);
    if (filters.compareRunId !== null) params = params.set('compareRunId', filters.compareRunId);
    return params;
  }
}
