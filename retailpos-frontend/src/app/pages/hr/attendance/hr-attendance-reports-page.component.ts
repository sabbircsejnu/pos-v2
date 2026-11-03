import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import {
  AttendanceReportFilters,
  AttendanceReportPage,
  HrAttendanceReportService
} from '../../../services/hr-attendance-report.service';
import { UI_KIT, UiBadgeVariant, UiTableColumn } from '../../../components/shared/ui';

@Component({
  selector: 'app-hr-attendance-reports-page',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  styles: [`
    :host { display: block; }
    .ar-page { display: flex; flex-direction: column; gap: var(--rp-space-4); padding: var(--rp-space-4); }
    .ar-form { display: grid; gap: var(--rp-form-gap); align-items: end; grid-template-columns: minmax(0, 1fr); }
    .ar-check { min-height: var(--rp-control-height); }
    .ar-results { display: flex; flex-direction: column; gap: var(--rp-space-3); }
    .ar-count { margin: 0; color: var(--rp-color-text-secondary); font-weight: var(--rp-font-weight-medium); }
    .ar-pager { display: flex; flex-wrap: wrap; align-items: center; gap: var(--rp-space-3); }
    .ar-pager-label { color: var(--rp-color-text-secondary); }
    .ar-stack { display: flex; flex-direction: column; gap: var(--rp-space-1); min-width: 11rem; }
    .ar-inline { display: flex; flex-wrap: wrap; align-items: center; gap: var(--rp-space-2); }
    .ar-muted { color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); }
    @media (min-width: 640px) {
      .ar-form { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }
    @media (min-width: 1024px) {
      .ar-page { padding: var(--rp-space-6); }
      .ar-form { grid-template-columns: repeat(4, minmax(0, 1fr)); }
    }
  `],
  template: `<main class="ar-page">
    <app-ui-page-header title="Attendance reports"
      description="WorkDate is the shift start date. Frozen shift, location, calendar, punch and correction evidence are shown where available." />
    @if (error()) {
      <app-ui-alert variant="danger">{{error()}}</app-ui-alert>
    }
    @if (busy()) {
      <app-ui-state kind="loading" title="Loading attendance report..." [compact]="true" />
    }
    <app-ui-card title="Filters" subtitle="Narrow the report by period, employee, location or status.">
      <form (ngSubmit)="search()" class="ar-form" aria-label="Attendance report filters">
        <app-ui-form-field label="From" [required]="true">
          <input appUiControl name="from" type="date" appCompanyDate required [(ngModel)]="filters.from">
        </app-ui-form-field>
        <app-ui-form-field label="To" [required]="true">
          <input appUiControl name="to" type="date" appCompanyDate required [(ngModel)]="filters.to">
        </app-ui-form-field>
        <app-ui-form-field label="Employee ID">
          <input appUiControl name="employee" type="number" min="1" [(ngModel)]="filters.employeeId">
        </app-ui-form-field>
        <app-ui-form-field label="Location projection ID">
          <input appUiControl name="location" type="number" min="1" [(ngModel)]="filters.locationProjectionId">
        </app-ui-form-field>
        <app-ui-form-field label="Status">
          <select appUiControl name="status" [(ngModel)]="filters.status">
            <option value="">All statuses</option><option>Present</option><option>Late</option><option>HalfDay</option>
            <option>MissingPunch</option><option>Absent</option><option>Holiday</option><option>WeeklyOff</option>
          </select>
        </app-ui-form-field>
        <label class="ui-check ar-check"><input appUiControl name="exceptionsOnly" type="checkbox" [(ngModel)]="filters.exceptionsOnly"><span>Exceptions only</span></label>
        <div>
          <button appUiButton type="submit" [disabled]="busy()" [loading]="busy()">Search</button>
        </div>
      </form>
    </app-ui-card>
    @if (report(); as result) {
      <section class="ar-results" aria-label="Report results">
        <p class="ar-count" aria-live="polite">{{result.totalCount}} matching rows · {{result.exceptionCount}} exceptions · {{result.lateCount}} late ·
          {{result.earlyLeaveCount}} early leave · {{result.absentCount}} absent</p>
        <app-ui-data-table
          [columns]="columns" [rows]="result.items" [searchable]="false" [paginated]="false"
          ariaLabel="Attendance report results" density="compact"
          emptyTitle="No attendance rows match these filters.">
          <ng-template uiTableCell="employee" let-row>
            <div class="ar-stack">
              <span>{{row.employeeCode}} — {{row.employeeName}} ({{row.employeeId}})</span>
              <span class="ar-muted">{{ (row.workDate | appDate) }}</span>
            </div>
          </ng-template>
          <ng-template uiTableCell="shift" let-row>
            <div class="ar-stack">
              <span>{{row.shiftCode}} — {{row.shiftName}}</span>
              <span>{{row.shiftStartTimeLocal}}–{{row.shiftEndTimeLocal}}@if (row.shiftCrossesMidnight) {<span> (next day; one WorkDate)</span>}</span>
              <span class="ar-muted">{{row.locationName}}</span>
            </div>
          </ng-template>
          <ng-template uiTableCell="result" let-row>
            <div class="ar-stack">
              <span class="ar-inline">
                <app-ui-badge [variant]="statusVariant(row.status)">{{row.status}}</app-ui-badge>&ngsp;
                @if (row.isCorrected) {
                  <app-ui-badge variant="info">Corrected</app-ui-badge>
                }
              </span>
              <span>Late {{row.lateMinutes}}m · Early leave {{row.earlyLeaveMinutes}}m</span>
              <span>Worked {{row.workedMinutes}}m · Break {{row.breakMinutes}}m</span>
            </div>
          </ng-template>
          <ng-template uiTableCell="calendar" let-row>
            <div class="ar-stack">
              <span>{{row.dayClassification}}</span>
              @for (holiday of row.holidayEvidence; track $index) {
                <span class="ar-muted">{{holiday.code}} {{holiday.name}}</span>
              }
              @if (row.weeklyOffEvidence) {
                <span class="ar-muted">Weekly off v{{row.weeklyOffEvidence.versionNo}} ({{row.weeklyOffEvidence.days.join(', ')}})</span>
              }
            </div>
          </ng-template>
          <ng-template uiTableCell="processed" let-row>
            <div class="ar-stack">
              <span>Processing v{{row.processingVersion}} · {{ (row.lastProcessedAtUtc | appDate:'medium') }}</span>
              @if (row.correctionCount) {
                <span class="ar-muted">{{row.correctionCount}} correction(s), latest {{row.latestCorrectionStatus}}</span>
              }
            </div>
          </ng-template>
          <ng-template uiTableCell="punches" let-row>
            <div class="ar-stack">
              @for (punch of row.punches; track $index) {
                <span>{{punch.directionCode || 'Punch'}} {{punch.punchTimeLocal}} ({{punch.sourceType}})</span>
              }
              @if (!row.punches.length) {
                <span>No linked punch</span>
              }
            </div>
          </ng-template>
        </app-ui-data-table>
        @if (result.totalCount) {
          <nav class="ar-pager" aria-label="Report pagination">
            <button appUiButton variant="outline" size="sm" type="button" [disabled]="busy() || filters.page <= 1" (click)="changePage(-1)">Previous</button>
            <span class="ar-pager-label">Page {{result.page}} of {{pageCount(result)}}</span>
            <button appUiButton variant="outline" size="sm" type="button" [disabled]="busy() || result.page >= pageCount(result)" (click)="changePage(1)">Next</button>
          </nav>
        }
      </section>
    }
  </main>`
})
export class HrAttendanceReportsPageComponent implements OnInit {
  private readonly api = inject(HrAttendanceReportService);
  private readonly auth = inject(AuthService);
  private readonly capability = inject(HrPayrollCapabilityStore);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly report = signal<AttendanceReportPage | null>(null);
  private readonly today = new Date().toISOString().slice(0, 10);
  filters: AttendanceReportFilters = {
    from: this.today, to: this.today, employeeId: null, locationProjectionId: null,
    status: '', exceptionsOnly: false, page: 1, pageSize: 50
  };

  ngOnInit() { this.search(); }

  readonly columns: UiTableColumn[] = [
    { key: 'employee', header: 'Employee / WorkDate', type: 'custom' },
    { key: 'shift', header: 'Shift / Location', type: 'custom' },
    { key: 'result', header: 'Result', type: 'custom' },
    { key: 'calendar', header: 'Calendar evidence', type: 'custom' },
    { key: 'processed', header: 'Processed evidence', type: 'custom' },
    { key: 'punches', header: 'Punches', type: 'custom' }
  ];

  statusVariant(status: string): UiBadgeVariant {
    switch (status) {
      case 'Present': return 'success';
      case 'Late':
      case 'HalfDay': return 'warning';
      case 'MissingPunch':
      case 'Absent': return 'danger';
      case 'Holiday':
      case 'WeeklyOff': return 'info';
      default: return 'neutral';
    }
  }

  search() {
    if (!this.allowed()) return;
    this.filters.page = 1;
    this.load();
  }

  changePage(amount: number) {
    if (!this.allowed()) return;
    this.filters.page += amount;
    this.load();
  }

  pageCount(page: AttendanceReportPage) {
    return Math.max(1, Math.ceil(page.totalCount / page.pageSize));
  }

  private allowed() {
    if (!this.capability.isEnabled('attendance.enabled') || !this.auth.hasPermission('hr.attendance.report')) {
      this.error.set('HR/Attendance capability or attendance report permission unavailable.');
      return false;
    }
    return !this.busy();
  }

  private load() {
    this.busy.set(true);
    this.error.set(null);
    this.api.query(this.filters).subscribe({
      next: result => { this.report.set(result); this.busy.set(false); },
      error: error => {
        this.busy.set(false);
        this.error.set(error.error?.detail ?? 'Unable to load authorized attendance report.');
      }
    });
  }
}
