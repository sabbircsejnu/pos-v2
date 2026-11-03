import { CompanyDateService } from '../../../services/company-date.service';
import { inject as injectCompanyDates } from '@angular/core';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import { HrRosterService, RosterData, RosterRow } from '../../../services/hr-roster.service';
import { UI_KIT, UiBreadcrumb, UiTableColumn } from '../../../components/shared/ui';

@Component({
  selector: 'app-hr-rosters-page', standalone: true, imports: [FormsModule, ...UI_KIT],
  template: `<main class="container mx-auto px-4 py-3">
    <app-ui-page-header
      title="Employee rosters"
      description="WorkDate is the shift START date, including overnight shifts. Drafts do not classify attendance. Publication freezes shift, location and calendar evidence. Historical published rows cannot be edited."
      [breadcrumbs]="breadcrumbs" />
    @if (error() || (busy() && rows().length > 0)) {
      <div class="ro-alerts">
        @if (error()) {
          <app-ui-alert variant="danger">{{error()}}</app-ui-alert>
        }
        @if (busy() && rows().length > 0) {
          <app-ui-state kind="loading" [compact]="true" title="Loading / saving..." />
        }
      </div>
    }
    <app-ui-card class="ro-card" title="Roster period">
      <form (ngSubmit)="load()" class="ro-period" aria-label="Roster period">
        <app-ui-form-field label="From" [required]="true">
          <input appUiControl name="from" type="date" appCompanyDate required [(ngModel)]="from">
        </app-ui-form-field>
        <app-ui-form-field label="To" [required]="true">
          <input appUiControl name="to" type="date" appCompanyDate required [(ngModel)]="to">
        </app-ui-form-field>
        <div class="ro-period__action"><button appUiButton type="submit" [disabled]="busy()">Load</button></div>
      </form>
    </app-ui-card>
    @if (canManage()) {
      <app-ui-card class="ro-card" [title]="editing ? 'Edit draft' : 'New draft'">
        <form (ngSubmit)="save()" class="ro-grid" aria-label="Roster draft">
          <app-ui-form-field label="Employee ID" [required]="true">
            <input appUiControl name="employee" type="number" min="1" required [(ngModel)]="data.employeeId">
          </app-ui-form-field>
          <app-ui-form-field label="Shift ID" [required]="true">
            <input appUiControl name="shift" type="number" min="1" required [(ngModel)]="data.shiftId">
          </app-ui-form-field>
          <app-ui-form-field label="HR operational location projection ID" [required]="true">
            <input appUiControl name="location" type="number" min="1" required [(ngModel)]="data.operationalLocationProjectionId">
          </app-ui-form-field>
          <app-ui-form-field label="WorkDate (start date)" [required]="true">
            <input appUiControl name="date" type="date" appCompanyDate required [(ngModel)]="data.workDate">
          </app-ui-form-field>
          <app-ui-form-field label="Work kind">
            <select appUiControl name="kind" [(ngModel)]="data.workKind">
              <option>Normal</option>
              <option>Extended</option>
              <option>Special</option>
            </select>
          </app-ui-form-field>
          <app-ui-form-field label="Explicit day classification">
            <select appUiControl name="classification" [(ngModel)]="data.dayClassification">
              <option value="NormalWorkingDay">Working day</option>
              <option value="Holiday">Holiday</option>
              <option value="WeeklyOff">Weekly off</option>
            </select>
          </app-ui-form-field>
          <div class="ro-actions ro-wide">
            <button appUiButton type="submit" [disabled]="busy()">Save draft</button>
            <button appUiButton type="button" variant="outline" (click)="reset()">Reset</button>
          </div>
        </form>
      </app-ui-card>
      <app-ui-card class="ro-card" title="Bulk draft assignment" subtitle="Enter comma-separated employee IDs. Uses the shift, date and HR location above. Atomic; maximum 100 rows.">
        <div class="ro-bulk">
          <app-ui-form-field label="Bulk employee IDs">
            <input appUiControl aria-label="Bulk employee IDs" [(ngModel)]="bulkEmployees">
          </app-ui-form-field>
          <div class="ro-bulk__action"><button appUiButton type="button" [disabled]="busy()" (click)="bulk()">Create drafts</button></div>
        </div>
      </app-ui-card>
    }
    <app-ui-data-table
      ariaLabel="Employee rosters"
      density="compact"
      rowKey="id"
      [columns]="columns"
      [rows]="tableRows()"
      [loading]="busy()"
      loadingLabel="Loading / saving..."
      emptyTitle="No rosters found."
      emptyMessage="No rosters exist for the selected period."
      searchLabel="Search rosters"
      searchPlaceholder="Search employee, shift, status…"
      [initialPageSize]="25">
      <ng-template uiTableCell="frozen" let-row>
        @let r = row.row;
        @if (r.publishedShift || r.publishedLocation || r.publishedClassification) {
          <div>{{r.publishedShift?.startTimeLocal}} - {{r.publishedShift?.endTimeLocal}} {{r.publishedShift?.crossesMidnight ? '(next day)' : ''}}
            {{r.publishedLocation?.name}} {{r.publishedClassification?.classification}}</div>
        }
        @if (r.publishedClassification?.isHoliday) {
          <app-ui-badge variant="info">Holiday evidence retained</app-ui-badge>
        }
        @if (r.publishedClassification?.weeklyOffPolicy) {
          <app-ui-badge variant="info">Weekly-off policy evidence retained</app-ui-badge>
        }
      </ng-template>
      <ng-template uiTableRowActions let-row>
        @if (canManage()) {
          <div class="ro-row-actions">
            @if (row.row.status === 'Draft') {
              <button appUiButton type="button" variant="ghost" size="sm" [attr.aria-label]="'Edit roster ' + row.employee" (click)="edit(row.row)">Edit</button>
              <button appUiButton type="button" variant="ghost" size="sm" [attr.aria-label]="'Publish roster ' + row.employee" [disabled]="busy()" (click)="transition(row.row,'publish')">Publish</button>
            }
            @if (row.row.status !== 'Cancelled' && row.row.data.workDate > today) {
              <button appUiButton type="button" variant="ghost" size="sm" class="ro-danger" [attr.aria-label]="'Cancel future roster ' + row.employee" [disabled]="busy()" (click)="transition(row.row,'cancel')">Cancel future roster</button>
            }
          </div>
        }
      </ng-template>
    </app-ui-data-table>
  </main>`,
  styles: [`
    .ro-alerts { display: grid; gap: var(--rp-space-3); margin-bottom: var(--rp-space-4); }
    .ro-card { display: block; margin-bottom: var(--rp-space-6); }
    .ro-period, .ro-bulk { display: grid; gap: var(--rp-form-gap); grid-template-columns: minmax(0, 1fr); align-items: end; }
    .ro-period__action, .ro-bulk__action { display: flex; }
    .ro-grid { display: grid; gap: var(--rp-form-gap); grid-template-columns: minmax(0, 1fr); }
    .ro-actions { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); }
    .ro-row-actions { display: inline-flex; flex-wrap: wrap; align-items: center; justify-content: center; gap: var(--rp-space-1); }
    .ro-danger { color: var(--rp-color-status-danger-fg); }
    @media (min-width: 768px) {
      .ro-period { grid-template-columns: minmax(0, 12rem) minmax(0, 12rem) auto; }
      .ro-bulk { grid-template-columns: minmax(0, 1fr) auto; }
      .ro-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .ro-wide { grid-column: 1 / -1; }
    }
  `]
})
export class HrRostersPageComponent implements OnInit {
  private readonly companyDates = injectCompanyDates(CompanyDateService);
  private readonly api = inject(HrRosterService);
  private readonly auth = inject(AuthService);
  private readonly capability = inject(HrPayrollCapabilityStore);
  readonly rows = signal<RosterRow[]>([]);
  readonly busy = signal(false); readonly error = signal<string | null>(null);
  readonly today = new Date().toISOString().slice(0, 10);
  from = this.today; to = this.today; bulkEmployees = '';
  data: RosterData = this.empty(); editing: RosterRow | null = null;
  readonly breadcrumbs: UiBreadcrumb[] = [{ label: 'Dashboard', link: '/dashboard' }, { label: 'Employee rosters' }];
  readonly columns: UiTableColumn[] = [
    { key: 'employee', header: 'Employee / WorkDate', sortable: true, sortField: 'employeeSort' },
    { key: 'shiftLocation', header: 'Shift / Location', sortable: true },
    {
      key: 'status', header: 'Status', type: 'status', sortable: true,
      statusMap: {
        Draft: { label: 'Draft', variant: 'warning' },
        Published: { label: 'Published', variant: 'success' },
        Cancelled: { label: 'Cancelled', variant: 'neutral' }
      }
    },
    { key: 'version', header: 'Version', type: 'number', decimals: 0, sortable: true },
    { key: 'published', type: 'date', header: 'Published (UTC)' },
    { key: 'frozen', header: 'Frozen context', type: 'custom' }
  ];
  readonly tableRows = computed(() => this.rows().map(r => ({
    id: r.id, row: r,
    employee: `${r.data.employeeId} / ${this.companyDates.format(r.data.workDate, false)} / ${r.data.workKind}`,
    employeeSort: `${r.data.employeeId} / ${r.data.workDate} / ${r.data.workKind}`,
    shiftLocation: `${r.data.shiftId} / ${r.data.operationalLocationProjectionId}`,
    status: r.status,
    version: r.version,
    published: r.publishedAtUtc || 'Not published'
  })));
  ngOnInit() { this.load(); }
  canManage() { return this.capability.isEnabled('attendance.enabled') && this.auth.hasPermission('hr.roster.manage'); }
  load() {
    if (!this.capability.isEnabled('attendance.enabled') || !this.auth.hasPermission('hr.roster.view')) {
      this.error.set('HR/Attendance capability or roster view permission unavailable.'); return;
    }
    this.busy.set(true); this.api.list(this.from, this.to).subscribe({
      next: rows => { this.rows.set(rows); this.busy.set(false); },
      error: e => { this.busy.set(false); this.error.set(e.error?.detail ?? 'Unable to load authorized rosters.'); }
    });
  }
  save() { if (this.allowed()) this.write(this.api.save(this.data, this.editing)); }
  bulk() {
    if (!this.allowed()) return;
    const ids = this.bulkEmployees.split(',').map(s => Number(s.trim()));
    if (ids.length > 100 || ids.some(id => !Number.isSafeInteger(id) || id <= 0) || new Set(ids).size !== ids.length) {
      this.error.set('Provide 1-100 unique positive employee IDs.'); return;
    }
    this.write(this.api.bulk(ids.map(employeeId => ({ ...this.data, employeeId, source: 'Bulk' }))));
  }
  transition(row: RosterRow, action: 'publish' | 'cancel') {
    if (this.allowed()) this.write(this.api.transition(row, action));
  }
  edit(row: RosterRow) { if (this.allowed() && row.status === 'Draft') { this.editing = row; this.data = { ...row.data }; } }
  reset() { this.editing = null; this.data = this.empty(); }
  private allowed() {
    if (!this.canManage()) { this.error.set('Roster manage access required.'); return false; }
    return !this.busy();
  }
  private write<T>(request: Observable<T>) {
    this.busy.set(true); this.error.set(null); request.subscribe({
      next: () => { this.busy.set(false); this.reset(); this.load(); },
      error: e => { this.busy.set(false); this.error.set(e.error?.detail ?? 'Roster operation failed.'); }
    });
  }
  private empty(): RosterData { return { employeeId: 0, shiftId: 0, workDate: this.today,
    operationalLocationProjectionId: 0, source: 'Manual', workKind: 'Normal', dayClassification: 'NormalWorkingDay' }; }
}
