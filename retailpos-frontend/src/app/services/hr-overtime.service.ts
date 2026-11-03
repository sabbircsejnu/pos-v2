import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export type OvertimePolicyScopeType = 'Business' | 'LocationType' | 'Location' | 'Employee';
export type OvertimeRoundingMethod = 'Down' | 'Up' | 'Nearest';
export type OvertimeRateType = 'Multiplier' | 'FixedHourly';

export interface OvertimePolicy {
  id: number;
  code: string;
  name: string;
  scopeType: OvertimePolicyScopeType;
  scopeReference: string | null;
  versionNo: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  minimumEligibleMinutes: number;
  maximumEligibleMinutes: number | null;
  roundingUnitMinutes: number;
  roundingMethod: OvertimeRoundingMethod;
  includePreShift: boolean;
  includePostShift: boolean;
  allowNormalWorkingDay: boolean;
  allowWeeklyOff: boolean;
  allowHoliday: boolean;
  allowSpecialDay: boolean;
  rateType: OvertimeRateType;
  normalDayRate: number;
  weeklyOffRate: number;
  holidayRate: number;
  specialDayRate: number;
  isActive: boolean;
}

export interface OvertimeCalculationEvidence {
  calculationVersion: number;
  attendanceProcessingVersion: number;
  policyVersionId: number;
  eligibleMinutes: number;
  calculatedMinutes: number;
  dayCategory: string;
  calculationFingerprint: string;
  policySnapshotJson: string;
  attendanceSnapshotJson: string;
  calculationInputsJson: string;
  createdAtUtc: string;
  createdByUserId: number;
}

export interface OvertimeEntry {
  id: number;
  employeeId: number;
  employeeCode: string;
  employeeName: string;
  workDate: string;
  dailyAttendanceId: number;
  operationalLocationProjectionId: number;
  dayCategory: string;
  attendanceProcessingVersion: number;
  policyVersionId: number;
  policyVersionNo: number;
  eligibleMinutes: number;
  calculatedMinutes: number;
  approvedMinutes: number | null;
  status: string;
  calculationVersion: number;
  approvalInstanceId: number | null;
  approvalStatus: string | null;
  currentApprovalStep: number | null;
  version: number;
  evidence: OvertimeCalculationEvidence[];
}

export interface OvertimePolicyRequest {
  code: string;
  name: string;
  scopeType: OvertimePolicyScopeType;
  scopeReference: string | null;
  versionNo: number;
  effectiveFrom: string;
  effectiveTo: string | null;
  minimumEligibleMinutes: number;
  maximumEligibleMinutes: number | null;
  roundingUnitMinutes: number;
  roundingMethod: OvertimeRoundingMethod;
  includePreShift: boolean;
  includePostShift: boolean;
  allowNormalWorkingDay: boolean;
  allowWeeklyOff: boolean;
  allowHoliday: boolean;
  allowSpecialDay: boolean;
  rateType: OvertimeRateType;
  normalDayRate: number;
  weeklyOffRate: number;
  holidayRate: number;
  specialDayRate: number;
  isActive: boolean;
}

export interface OvertimeFilters {
  from: string;
  to: string;
  employeeId: number | null;
  locationProjectionId: number | null;
  status: string;
}

@Injectable({ providedIn: 'root' })
export class HrOvertimeService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/hr/v1/overtime`;

  policies() {
    return this.http.get<OvertimePolicy[]>(`${this.base}/policies`);
  }

  createPolicy(request: OvertimePolicyRequest) {
    return this.http.post<OvertimePolicy>(`${this.base}/policies`, request);
  }

  entries(filters: OvertimeFilters) {
    let params = new HttpParams().set('from', filters.from).set('to', filters.to);
    if (filters.employeeId !== null) params = params.set('employeeId', filters.employeeId);
    if (filters.locationProjectionId !== null) {
      params = params.set('locationProjectionId', filters.locationProjectionId);
    }
    if (filters.status) params = params.set('status', filters.status);
    return this.http.get<OvertimeEntry[]>(`${this.base}/entries`, { params });
  }

  calculate(filters: OvertimeFilters) {
    return this.http.post<OvertimeEntry[]>(`${this.base}/calculate`, {
      from: filters.from,
      to: filters.to,
      employeeId: filters.employeeId,
      locationProjectionId: filters.locationProjectionId
    });
  }

  submit(entry: OvertimeEntry) {
    return this.http.post(`${this.base}/entries/${entry.id}/submit`, { expectedVersion: entry.version });
  }

  cancel(entry: OvertimeEntry, reason: string) {
    return this.http.post(`${this.base}/entries/${entry.id}/cancel`, {
      decisionKey: crypto.randomUUID(),
      reason
    });
  }

  decide(instanceId: number, decision: 'approve' | 'reject', reason: string) {
    return this.http.post(
      `${this.base}/approvals/${instanceId}/${decision}`,
      { decisionKey: crypto.randomUUID(), reason }
    );
  }
}
