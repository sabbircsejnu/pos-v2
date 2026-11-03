import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface AttendanceReportFilters {
  from: string;
  to: string;
  employeeId: number | null;
  locationProjectionId: number | null;
  status: string;
  exceptionsOnly: boolean;
  page: number;
  pageSize: number;
}

export interface AttendanceReportPunch {
  punchTimeLocal: string;
  punchTimeUtc: string | null;
  directionCode: string | null;
  sourceType: string;
}

export interface AttendanceReportRow {
  id: number;
  employeeId: number;
  employeeCode: string;
  employeeName: string;
  workDate: string;
  locationProjectionId: number;
  locationName: string;
  shiftId: number;
  shiftCode: string;
  shiftName: string;
  shiftStartTimeLocal: string;
  shiftEndTimeLocal: string;
  shiftCrossesMidnight: boolean;
  dayClassification: string;
  holidayEvidence: { code: string; name: string; holidayType: string }[];
  weeklyOffEvidence: { versionNo: number; days: string[] } | null;
  firstInAtLocal: string | null;
  lastOutAtLocal: string | null;
  workedMinutes: number;
  breakMinutes: number;
  lateMinutes: number;
  earlyLeaveMinutes: number;
  status: string;
  isCorrected: boolean;
  processingVersion: number;
  lastProcessedAtUtc: string;
  correctionCount: number;
  latestCorrectionSequence: number | null;
  latestCorrectionStatus: string | null;
  punches: AttendanceReportPunch[];
}

export interface AttendanceReportPage {
  items: AttendanceReportRow[];
  page: number;
  pageSize: number;
  totalCount: number;
  lateCount: number;
  earlyLeaveCount: number;
  absentCount: number;
  exceptionCount: number;
}

@Injectable({ providedIn: 'root' })
export class HrAttendanceReportService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/hr/v1/attendance/reports`;

  query(filters: AttendanceReportFilters) {
    let params = new HttpParams()
      .set('from', filters.from)
      .set('to', filters.to)
      .set('exceptionsOnly', filters.exceptionsOnly)
      .set('page', filters.page)
      .set('pageSize', filters.pageSize);
    if (filters.employeeId !== null) params = params.set('employeeId', filters.employeeId);
    if (filters.locationProjectionId !== null) params = params.set('locationProjectionId', filters.locationProjectionId);
    if (filters.status) params = params.set('status', filters.status);
    return this.http.get<AttendanceReportPage>(this.base, { params });
  }
}
