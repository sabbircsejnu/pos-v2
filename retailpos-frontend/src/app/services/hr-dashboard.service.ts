import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface HrDashboardMetric {
  label: string;
  count: number;
  minutes: number | null;
}

export interface HrDashboardSummary {
  from: string;
  to: string;
  totalEmployees: number;
  employmentStatuses: HrDashboardMetric[];
  employmentTypes: HrDashboardMetric[];
  locationTypes: HrDashboardMetric[];
  locations: HrDashboardMetric[];
  orgUnits: HrDashboardMetric[];
  positions: HrDashboardMetric[];
  joiners: number;
  exits: number;
  attendance: HrDashboardMetric[];
  leaveRequests: HrDashboardMetric[];
  overtime: HrDashboardMetric[];
  documentsExpiring: number;
  openExitCases: number;
  pendingMandatoryClearanceItems: number;
}

@Injectable({ providedIn: 'root' })
export class HrDashboardService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/hr/v1/dashboard`;

  query(from: string, to: string, locationProjectionId: number | null, locationType: string | null) {
    let params = new HttpParams().set('from', from).set('to', to);
    if (locationProjectionId !== null) {
      params = params.set('locationProjectionId', locationProjectionId);
    }
    if (locationType !== null) params = params.set('locationType', locationType);
    return this.http.get<HrDashboardSummary>(this.url, { params });
  }
}
