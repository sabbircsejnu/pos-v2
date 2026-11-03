import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import {
  HrCoreReportFilters,
  HrCoreReportPage,
  HrCoreReportRow,
  HrCoreReportService
} from '../../../services/hr-core-report.service';
import { UI_KIT, UiTableColumn } from '../../../components/shared/ui';

@Component({
  selector: 'app-hr-core-reports-page',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  styles: [`
    :host { display: block; }
    .cr-page { display: flex; flex-direction: column; gap: var(--rp-space-4); padding: var(--rp-space-4); }
    .cr-form { display: grid; gap: var(--rp-form-gap); align-items: end; grid-template-columns: minmax(0, 1fr); }
    .cr-results { display: flex; flex-direction: column; gap: var(--rp-space-3); }
    .cr-summary { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: var(--rp-space-3); }
    .cr-count { margin: 0; color: var(--rp-color-text-secondary); font-weight: var(--rp-font-weight-medium); }
    .cr-actions, .cr-pager { display: flex; flex-wrap: wrap; align-items: center; gap: var(--rp-space-3); }
    .cr-pager-label { color: var(--rp-color-text-secondary); }
    .cr-details { display: inline-block; min-width: 16rem; }
    @media (min-width: 640px) {
      .cr-form { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }
    @media (min-width: 1024px) {
      .cr-page { padding: var(--rp-space-6); }
      .cr-form { grid-template-columns: repeat(4, minmax(0, 1fr)); }
    }
  `],
  template: `<main class="cr-page">
    <app-ui-page-header title="Core HR reports"
      description="Reports are restricted to employees whose primary location is within your authorized scope. Personal identifiers, contact details, document numbers, file data, and exit reasons are excluded." />
    @if (error()) {
      <app-ui-alert variant="danger">{{error()}}</app-ui-alert>
    }
    @if (busy()) {
      <app-ui-state kind="loading" title="Loading report..." [compact]="true" />
    }
    @if (exportBusy()) {
      <app-ui-state kind="loading" title="Preparing export..." [compact]="true" />
    }
    <app-ui-card title="Filters" subtitle="Choose a report and narrow it by period, employee, location or status.">
      <form (ngSubmit)="search()" class="cr-form" aria-label="Core HR report filters">
        <app-ui-form-field label="Report">
          <select appUiControl name="kind" [(ngModel)]="filters.kind">
            <option value="EmployeeMaster">Employee master</option>
            <option value="EmployeeRoster">Employee roster / headcount</option>
            <option value="LifecycleHistory">Lifecycle history</option>
            <option value="PositionAssignmentHistory">Position assignment history</option>
            <option value="LocationAssignmentHistory">Location assignment history</option>
            <option value="AttendanceSummary">Attendance summary</option>
            <option value="AttendanceExceptions">Attendance exceptions</option>
            <option value="AttendanceCorrections">Attendance corrections</option>
            <option value="LeaveSummary">Leave summary</option>
            <option value="LeaveBalance">Leave balance</option>
            <option value="OvertimeSummary">Overtime summary</option>
            <option value="DocumentExpiry">Document expiry</option>
            <option value="ExitClearance">Exit / clearance</option>
          </select>
        </app-ui-form-field>
        <app-ui-form-field label="From" [required]="true">
          <input appUiControl name="from" type="date" appCompanyDate required [(ngModel)]="filters.from">
        </app-ui-form-field>
        <app-ui-form-field label="To" [required]="true">
          <input appUiControl name="to" type="date" appCompanyDate required [(ngModel)]="filters.to">
        </app-ui-form-field>
        <app-ui-form-field label="Employee ID">
          <input appUiControl name="employeeId" type="number" min="1" [(ngModel)]="filters.employeeId">
        </app-ui-form-field>
        <app-ui-form-field label="Location projection ID">
          <input appUiControl name="locationId" type="number" min="1" [(ngModel)]="filters.locationProjectionId">
        </app-ui-form-field>
        <app-ui-form-field label="Status">
          <input appUiControl name="status" [(ngModel)]="filters.status" maxlength="40">
        </app-ui-form-field>
        <app-ui-form-field label="As of date">
          <input appUiControl name="asOfDate" type="date" appCompanyDate [(ngModel)]="filters.asOfDate">
        </app-ui-form-field>
        <app-ui-form-field label="Employment type ID">
          <input appUiControl name="employmentTypeId" type="number" min="1" [(ngModel)]="filters.employmentTypeId">
        </app-ui-form-field>
        <app-ui-form-field label="Employment status">
          <input appUiControl name="employmentStatus" [(ngModel)]="filters.employmentStatus" maxlength="40">
        </app-ui-form-field>
        <app-ui-form-field label="Position ID">
          <input appUiControl name="positionId" type="number" min="1" [(ngModel)]="filters.positionId">
        </app-ui-form-field>
        <app-ui-form-field label="Department ID">
          <input appUiControl name="departmentId" type="number" min="1" [(ngModel)]="filters.departmentId">
        </app-ui-form-field>
        <app-ui-form-field label="Sort by">
          <select appUiControl name="sortBy" [(ngModel)]="filters.sortBy">
            <option [ngValue]="undefined">Default</option><option value="employeeCode">Employee code</option><option value="employeeName">Employee name</option>
            <option value="effectiveDate">Effective date</option><option value="category">Category</option>
            <option value="status">Status</option><option value="locationName">Location</option>
          </select>
        </app-ui-form-field>
        <app-ui-form-field label="Order">
          <select appUiControl name="descending" [(ngModel)]="filters.descending">
            <option [ngValue]="true">Descending</option><option [ngValue]="false">Ascending</option>
          </select>
        </app-ui-form-field>
        <div>
          <button appUiButton type="submit" [disabled]="busy()" [loading]="busy()">Search</button>
        </div>
      </form>
    </app-ui-card>
    @if (report(); as result) {
      <section class="cr-results" aria-label="Report results">
        <div class="cr-summary">
          <p class="cr-count" aria-live="polite">{{result.totalCount}} matching records</p>
          @if (result.totalCount) {
            <div class="cr-actions">
              <button appUiButton variant="outline" type="button" [disabled]="busy() || exportBusy()" (click)="download('csv')">Export CSV</button>
              <button appUiButton variant="outline" type="button" [disabled]="busy() || exportBusy()" (click)="download('xlsx')">Export XLSX</button>
            </div>
          }
        </div>
        <app-ui-data-table
          [columns]="columns" [rows]="result.items" [searchable]="false" [paginated]="false"
          ariaLabel="Core HR report results" density="compact"
          emptyTitle="No records match these filters.">
          <ng-template uiTableCell="employee" let-row>{{row.employeeCode}} — {{row.employeeName}} ({{row.employeeId}})</ng-template>
          <ng-template uiTableCell="details" let-row>
            <span class="cr-details">{{row.employmentType || ''}} {{row.department || ''}} {{row.position || ''}}
              {{row.locationType || ''}} {{row.detail || ''}} {{row.completionMode || ''}}
              @if (row.payrollEnabled !== null) {
                <span> · Payroll {{row.payrollEnabled ? 'enabled' : 'disabled'}}</span>
              }
              @if (row.effectiveTo) {
                <span> · through {{ (row.effectiveTo | appDate) }}</span>
              }
              @if (row.quantity !== null) {
                <span> · quantity {{row.quantity}}</span>
              }
              @if (row.isCurrent !== null) {
                <span> · {{row.isCurrent ? 'Current' : 'Historical'}}</span>
              }
              @if (row.actorUserId !== null) {
                <span> · Actor {{row.actorUserId}}</span>
              }
              @if (row.occurredAtUtc) {
                <span> · {{ (row.occurredAtUtc | appDate:'medium') }}</span>
              }
              @if (measureSummary(row); as summary) {
                <span> · {{summary}}</span>
              }
              @if (row.metric !== null) {
                <span> · {{row.metric}} pending mandatory items</span>
              }
              @if (!row.employmentType && !row.department && !row.position && !row.locationType
                && !row.detail && row.metric === null && !row.measures && row.quantity === null) {
                <span>—</span>
              }
            </span>
          </ng-template>
        </app-ui-data-table>
        @if (result.totalCount) {
          <nav class="cr-pager" aria-label="Report pagination">
            <button appUiButton variant="outline" size="sm" type="button" [disabled]="busy() || filters.page <= 1" (click)="changePage(-1)">Previous</button>
            <span class="cr-pager-label">Page {{result.page}} of {{pageCount(result)}}</span>
            <button appUiButton variant="outline" size="sm" type="button" [disabled]="busy() || result.page >= pageCount(result)" (click)="changePage(1)">Next</button>
          </nav>
        }
      </section>
    }
  </main>`
})
export class HrCoreReportsPageComponent implements OnInit {
  private readonly api = inject(HrCoreReportService);
  private readonly auth = inject(AuthService);
  private readonly capability = inject(HrPayrollCapabilityStore);
  readonly busy = signal(false);
  readonly exportBusy = signal(false);
  readonly error = signal<string | null>(null);
  readonly report = signal<HrCoreReportPage | null>(null);
  private readonly today = new Date().toISOString().slice(0, 10);
  filters: HrCoreReportFilters = {
    kind: 'EmployeeRoster', from: this.today, to: this.today, employeeId: null,
    locationProjectionId: null, status: '', page: 1, pageSize: 50,
    employmentTypeId: null, employmentStatus: '', positionId: null, departmentId: null,
    sortBy: undefined, descending: true
  };

  ngOnInit() { this.search(); }

  readonly columns: UiTableColumn[] = [
    { key: 'employee', header: 'Employee', type: 'custom' },
    { key: 'category', header: 'Report item' },
    { key: 'effectiveDate', type: 'date', header: 'Date' },
    { key: 'status', header: 'Status', type: 'status' },
    { key: 'locationName', header: 'Location' },
    { key: 'details', header: 'Details', type: 'custom' }
  ];

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

  download(format: 'csv' | 'xlsx') {
    if (!this.allowed() || this.exportBusy()) return;
    this.exportBusy.set(true);
    this.error.set(null);
    this.api.export(this.filters, format).subscribe({
      next: blob => {
        const objectUrl = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = objectUrl;
        link.download = `hr-${this.filters.kind}.${format}`;
        link.click();
        URL.revokeObjectURL(objectUrl);
        this.exportBusy.set(false);
      },
      error: error => {
        this.exportBusy.set(false);
        this.error.set(error.error?.detail ?? 'Unable to export the authorized HR report.');
      }
    });
  }

  pageCount(page: HrCoreReportPage) {
    return Math.max(1, Math.ceil(page.totalCount / page.pageSize));
  }

  measureSummary(row: HrCoreReportRow) {
    const measures = row.measures;
    if (!measures) return '';
    const values: string[] = [];
    if (measures.scheduledDays !== null) values.push(`Scheduled days: ${measures.scheduledDays}`);
    if (measures.presentDays !== null) values.push(`Present days: ${measures.presentDays}`);
    if (measures.absentDays !== null) values.push(`Absent days: ${measures.absentDays}`);
    if (measures.lateDays !== null) values.push(`Late days: ${measures.lateDays}`);
    if (measures.holidayDays !== null) values.push(`Holiday days: ${measures.holidayDays}`);
    if (measures.weeklyOffDays !== null) values.push(`Weekly-off days: ${measures.weeklyOffDays}`);
    if (measures.approvedLeaveUnits !== null) values.push(`Approved leave units: ${measures.approvedLeaveUnits}`);
    if (measures.workedMinutes !== null) values.push(`Worked minutes: ${measures.workedMinutes}`);
    if (measures.approvedOvertimeEntries !== null) {
      values.push(`Approved overtime entries: ${measures.approvedOvertimeEntries}`);
    }
    if (measures.opening !== null) values.push(`Opening: ${measures.opening}`);
    if (measures.accrued !== null) values.push(`Accrued: ${measures.accrued}`);
    if (measures.used !== null) values.push(`Used: ${measures.used}`);
    if (measures.restored !== null) values.push(`Restored: ${measures.restored}`);
    if (measures.adjusted !== null) values.push(`Adjusted: ${measures.adjusted}`);
    if (measures.carriedForward !== null) values.push(`Carried forward: ${measures.carriedForward}`);
    if (measures.expired !== null) values.push(`Expired: ${measures.expired}`);
    if (measures.balance !== null) values.push(`Balance: ${measures.balance}`);
    if (measures.mandatoryClearanceItems !== null) {
      values.push(`Mandatory clearance items: ${measures.mandatoryClearanceItems}`);
    }
    if (measures.completedMandatoryClearanceItems !== null) {
      values.push(`Completed mandatory clearance items: ${measures.completedMandatoryClearanceItems}`);
    }
    return values.join(' · ');
  }

  private allowed() {
    if (!this.capability.isEnabled('hr.enabled') || !this.auth.hasPermission('hr.report.view')) {
      this.error.set('HR capability or report permission unavailable.');
      return false;
    }
    if (this.isAttendanceReport() &&
      (!this.capability.isEnabled('attendance.enabled') || !this.auth.hasPermission('hr.attendance.report'))) {
      this.error.set('Attendance capability or attendance report permission unavailable.');
      return false;
    }
    return !this.busy();
  }

  private isAttendanceReport() {
    return this.filters.kind === 'AttendanceSummary' || this.filters.kind === 'AttendanceExceptions'
      || this.filters.kind === 'AttendanceCorrections';
  }

  private load() {
    this.busy.set(true);
    this.error.set(null);
    this.api.query(this.filters).subscribe({
      next: result => { this.report.set(result); this.busy.set(false); },
      error: error => {
        this.busy.set(false);
        this.error.set(error.error?.detail ?? 'Unable to load the authorized HR report.');
      }
    });
  }
}
