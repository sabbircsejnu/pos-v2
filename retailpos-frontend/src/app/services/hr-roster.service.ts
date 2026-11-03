import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';
import { DayClassification, ShiftData } from './hr-calendar.service';

export interface RosterData {
  employeeId: number; workDate: string; shiftId: number; operationalLocationProjectionId: number;
  source: 'Manual' | 'Bulk' | 'Template'; workKind: 'Normal' | 'Extended' | 'Special';
  dayClassification: 'NormalWorkingDay' | 'Holiday' | 'WeeklyOff';
}
export interface RosterRow {
  id: number; businessId: number; data: RosterData; status: 'Draft' | 'Published' | 'Cancelled'; version: number;
  publishedAtUtc: string | null; publishedShift: ShiftData | null;
  publishedLocation: { projectionId: number; name: string; sourceEntityType: string } | null;
  publishedClassification: DayClassification | null;
}
@Injectable({ providedIn: 'root' })
export class HrRosterService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/hr/v1/rosters`;
  list(from: string, to: string) { return this.http.get<RosterRow[]>(this.base, { params: { from, to } }); }
  save(data: RosterData, row: RosterRow | null) {
    return row ? this.http.put<RosterRow>(`${this.base}/${row.id}`, { expectedVersion: row.version, data })
      : this.http.post<RosterRow>(this.base, data);
  }
  bulk(rows: RosterData[]) { return this.http.post<RosterRow[]>(`${this.base}/bulk`, { rows }); }
  transition(row: RosterRow, action: 'publish' | 'cancel') {
    return this.http.post<RosterRow>(`${this.base}/${row.id}/${action}`, { expectedVersion: row.version });
  }
}
