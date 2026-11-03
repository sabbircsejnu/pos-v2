import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { environment } from '../../environments/environment';

export interface CalendarScope {
  scopeType: 'Company' | 'LocationType' | 'Location';
  locationType: 'Outlet' | 'Warehouse' | 'Factory' | null;
  operationalLocationProjectionId: number | null;
}
export interface ShiftData {
  code: string; name: string; startTimeLocal: string; endTimeLocal: string; crossesMidnight: boolean;
  minimumWorkMinutes: number; maximumAllowedWorkMinutes: number | null;
  allowedEarlyPunchMinutes: number; allowedLatePunchMinutes: number;
  minimumRestBeforeNextShiftMinutes: number | null; graceInMinutes: number; graceOutMinutes: number;
  overtimeEligible: boolean; breaks: { sequence: number; durationMinutes: number; isPaid: boolean }[];
}
export interface CalendarData {
  name: string; scope: CalendarScope; effectiveFrom: string; effectiveTo: string | null;
}
export interface HolidayData {
  holidayDate: string; code: string; name: string; holidayType: string; isPaid: boolean; notes: string | null;
}
export interface WeeklyOffData {
  scope: CalendarScope; effectiveFrom: string; effectiveTo: string | null; days: number[];
}
export interface ConfigurationRow<T> {
  id: number; data: T; version: number; isActive: boolean;
}
export interface WeeklyOffRow extends ConfigurationRow<WeeklyOffData> { versionNo: number }
export interface HolidayRow extends ConfigurationRow<HolidayData> { calendarId: number }
export interface DayClassification {
  workDate: string; classification: string; source: string; isHoliday: boolean; isWeeklyOff: boolean;
  holidays: HolidayRow[]; weeklyOffPolicy: WeeklyOffRow | null;
}

@Injectable({ providedIn: 'root' })
export class HrCalendarService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.apiUrl}/hr/v1`;
  shifts() { return this.http.get<ConfigurationRow<ShiftData>[]>(`${this.base}/shifts`); }
  saveShift(data: ShiftData, row: ConfigurationRow<ShiftData> | null) {
    return row ? this.http.put<ConfigurationRow<ShiftData>>(`${this.base}/shifts/${row.id}`, { expectedVersion: row.version, data })
      : this.http.post<ConfigurationRow<ShiftData>>(`${this.base}/shifts`, data);
  }
  deactivateShift(row: ConfigurationRow<ShiftData>) {
    return this.http.post<ConfigurationRow<ShiftData>>(`${this.base}/shifts/${row.id}/deactivate`, { expectedVersion: row.version });
  }
  calendars() { return this.http.get<ConfigurationRow<CalendarData>[]>(`${this.base}/holiday-calendars`); }
  saveCalendar(data: CalendarData, row: ConfigurationRow<CalendarData> | null) {
    return row ? this.http.put<ConfigurationRow<CalendarData>>(`${this.base}/holiday-calendars/${row.id}`, { expectedVersion: row.version, data })
      : this.http.post<ConfigurationRow<CalendarData>>(`${this.base}/holiday-calendars`, data);
  }
  deactivateCalendar(row: ConfigurationRow<CalendarData>, effectiveTo: string) {
    return this.http.post<ConfigurationRow<CalendarData>>(`${this.base}/holiday-calendars/${row.id}/deactivate`, { expectedVersion: row.version, effectiveTo });
  }
  holidays(calendarId: number) { return this.http.get<HolidayRow[]>(`${this.base}/holiday-calendars/${calendarId}/holidays`); }
  saveHoliday(calendarId: number, data: HolidayData, row: HolidayRow | null) {
    const url = `${this.base}/holiday-calendars/${calendarId}/holidays`;
    return row ? this.http.put<HolidayRow>(`${url}/${row.id}`, { expectedVersion: row.version, data })
      : this.http.post<HolidayRow>(url, data);
  }
  deactivateHoliday(row: HolidayRow) {
    return this.http.post<HolidayRow>(`${this.base}/holiday-calendars/${row.calendarId}/holidays/${row.id}/deactivate`, { expectedVersion: row.version });
  }
  weeklyOff() { return this.http.get<WeeklyOffRow[]>(`${this.base}/weekly-off-policies`); }
  saveWeeklyOff(data: WeeklyOffData, previous: WeeklyOffRow | null) {
    return previous ? this.http.post<WeeklyOffRow>(`${this.base}/weekly-off-policies/${previous.id}/versions`, { expectedVersion: previous.version, data })
      : this.http.post<WeeklyOffRow>(`${this.base}/weekly-off-policies`, data);
  }
  deactivateWeeklyOff(row: WeeklyOffRow, effectiveTo: string) {
    return this.http.post<WeeklyOffRow>(`${this.base}/weekly-off-policies/${row.id}/deactivate`, { expectedVersion: row.version, effectiveTo });
  }
  classify(workDate: string, locationId: number) {
    return this.http.get<DayClassification>(`${this.base}/day-classification`, { params: { workDate, locationId } });
  }
}
