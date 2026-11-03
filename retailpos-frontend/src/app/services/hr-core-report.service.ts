import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export type HrCoreReportKind =
  | 'EmployeeMaster'
  | 'EmployeeRoster'
  | 'Lifecycle'
  | 'LifecycleHistory'
  | 'PositionAssignmentHistory'
  | 'LocationAssignmentHistory'
  | 'AttendanceSummary'
  | 'AttendanceExceptions'
  | 'AttendanceCorrections'
  | 'LeaveSummary'
  | 'LeaveBalance'
  | 'OvertimeSummary'
  | 'DocumentExpiry'
  | 'ExitClearance';

export type HrCoreReportSortBy =
  | 'employeeCode'
  | 'employeeName'
  | 'effectiveDate'
  | 'category'
  | 'status'
  | 'locationName';

export interface HrCoreReportFilters {
  kind: HrCoreReportKind;
  from: string;
  to: string;
  employeeId: number | null;
  locationProjectionId: number | null;
  status: string;
  page: number;
  pageSize: number;
  asOfDate?: string;
  employmentTypeId?: number | null;
  employmentStatus?: string;
  positionId?: number | null;
  departmentId?: number | null;
  sortBy?: HrCoreReportSortBy;
  descending?: boolean;
}

export interface HrCoreReportMeasures {
  scheduledDays: number | null;
  presentDays: number | null;
  absentDays: number | null;
  lateDays: number | null;
  holidayDays: number | null;
  weeklyOffDays: number | null;
  approvedLeaveUnits: number | null;
  workedMinutes: number | null;
  approvedOvertimeEntries: number | null;
  opening: number | null;
  accrued: number | null;
  used: number | null;
  restored: number | null;
  adjusted: number | null;
  carriedForward: number | null;
  expired: number | null;
  balance: number | null;
  mandatoryClearanceItems: number | null;
  completedMandatoryClearanceItems: number | null;
}

export interface HrCoreReportRow {
  employeeId: number;
  employeeCode: string;
  employeeName: string;
  category: string;
  effectiveDate: string | null;
  status: string;
  locationName: string | null;
  detail: string | null;
  metric: number | null;
  employmentType: string | null;
  department: string | null;
  position: string | null;
  locationType: string | null;
  effectiveTo: string | null;
  isCurrent: boolean | null;
  actorUserId: number | null;
  occurredAtUtc: string | null;
  quantity: number | null;
  completionMode: string | null;
  payrollEnabled: boolean | null;
  measures: HrCoreReportMeasures | null;
}

export interface HrCoreReportPage {
  kind: HrCoreReportKind;
  items: HrCoreReportRow[];
  page: number;
  pageSize: number;
  totalCount: number;
}

@Injectable({ providedIn: 'root' })
export class HrCoreReportService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/hr/v1/reports/core`;

  query(filters: HrCoreReportFilters) {
    let params = new HttpParams()
      .set('kind', filters.kind)
      .set('from', filters.from)
      .set('to', filters.to)
      .set('page', filters.page)
      .set('pageSize', filters.pageSize);
    if (filters.employeeId !== null) params = params.set('employeeId', filters.employeeId);
    if (filters.locationProjectionId !== null) {
      params = params.set('locationProjectionId', filters.locationProjectionId);
    }
    if (filters.status) params = params.set('status', filters.status);
    if (filters.asOfDate) params = params.set('asOfDate', filters.asOfDate);
    if (filters.employmentTypeId != null) {
      params = params.set('employmentTypeId', filters.employmentTypeId);
    }
    if (filters.employmentStatus) params = params.set('employmentStatus', filters.employmentStatus);
    if (filters.positionId != null) params = params.set('positionId', filters.positionId);
    if (filters.departmentId != null) params = params.set('departmentId', filters.departmentId);
    if (filters.sortBy) params = params.set('sortBy', filters.sortBy);
    if (filters.descending != null) params = params.set('descending', filters.descending);
    return this.http.get<HrCoreReportPage>(this.url, { params });
  }

  export(filters: HrCoreReportFilters, format: 'csv' | 'xlsx') {
    let params = new HttpParams()
      .set('kind', filters.kind)
      .set('from', filters.from)
      .set('to', filters.to)
      .set('descending', filters.descending ?? true);
    if (filters.employeeId !== null) params = params.set('employeeId', filters.employeeId);
    if (filters.locationProjectionId !== null) {
      params = params.set('locationProjectionId', filters.locationProjectionId);
    }
    if (filters.status) params = params.set('status', filters.status);
    if (filters.asOfDate) params = params.set('asOfDate', filters.asOfDate);
    if (filters.employmentTypeId != null) {
      params = params.set('employmentTypeId', filters.employmentTypeId);
    }
    if (filters.employmentStatus) params = params.set('employmentStatus', filters.employmentStatus);
    if (filters.positionId != null) params = params.set('positionId', filters.positionId);
    if (filters.departmentId != null) params = params.set('departmentId', filters.departmentId);
    if (filters.sortBy) params = params.set('sortBy', filters.sortBy);
    return this.http.get(`${this.url}/export/${format}`, { params, responseType: 'blob' });
  }
}
