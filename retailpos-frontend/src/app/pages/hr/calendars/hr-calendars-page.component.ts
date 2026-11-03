import { CompanyDateService } from '../../../services/company-date.service';
import { inject as injectCompanyDates } from '@angular/core';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Observable } from 'rxjs';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import { HrOperationalLocation, HrOrganisationService } from '../../../services/hr-organisation.service';
import { CalendarData, CalendarScope, ConfigurationRow, DayClassification, HolidayData, HolidayRow,
  HrCalendarService, ShiftData, WeeklyOffData, WeeklyOffRow } from '../../../services/hr-calendar.service';
import { UI_KIT, UiBreadcrumb, UiTableColumn } from '../../../components/shared/ui';

const ACTIVE_STATUS_MAP = {
  Active: { label: 'Active', variant: 'success' },
  Inactive: { label: 'Inactive', variant: 'neutral' }
} as const;

@Component({
  selector: 'app-hr-calendars-page', standalone: true, imports: [FormsModule, ...UI_KIT],
  template: `
    <main class="container mx-auto px-4 py-3">
      <app-ui-page-header
        [title]="title"
        description="Calendar changes are future-only (UTC). Closed weekly-off versions remain available for historical lookup."
        [breadcrumbs]="breadcrumbs" />
      @if (error() || (busy() && primaryCount() > 0)) {
        <div class="cal-alerts">
          @if (error()) {
            <app-ui-alert variant="danger">{{ error() }}</app-ui-alert>
          }
          @if (busy() && primaryCount() > 0) {
            <app-ui-state kind="loading" [compact]="true" title="Loading / saving..." />
          }
        </div>
      }
      @if (mode === 'shifts' && canManage()) {
        <app-ui-card class="cal-card" [title]="shiftEdit ? 'Edit shift' : 'Create shift'">
          <form (ngSubmit)="saveShift()" class="cal-grid" aria-label="Shift details">
            <app-ui-form-field label="Code" [required]="true">
              <input appUiControl name="code" required maxlength="50" [(ngModel)]="shift.code">
            </app-ui-form-field>
            <app-ui-form-field label="Name" [required]="true">
              <input appUiControl name="name" required maxlength="100" [(ngModel)]="shift.name">
            </app-ui-form-field>
            <app-ui-form-field label="Start local time" [required]="true">
              <input appUiControl name="start" type="time" required [(ngModel)]="shift.startTimeLocal">
            </app-ui-form-field>
            <app-ui-form-field label="End local time" [required]="true">
              <input appUiControl name="end" type="time" required [(ngModel)]="shift.endTimeLocal">
            </app-ui-form-field>
            <label class="ui-check cal-check">
              <input appUiControl name="midnight" type="checkbox" [(ngModel)]="shift.crossesMidnight">
              <span>Crosses midnight (WorkDate = start date)</span>
            </label>
            <app-ui-form-field label="Minimum net work minutes" [required]="true">
              <input appUiControl name="min" type="number" min="1" required [(ngModel)]="shift.minimumWorkMinutes">
            </app-ui-form-field>
            <app-ui-form-field label="Maximum allowed minutes">
              <input appUiControl name="max" type="number" min="1" max="1439" [(ngModel)]="shift.maximumAllowedWorkMinutes">
            </app-ui-form-field>
            <app-ui-form-field label="Early punch buffer" [required]="true">
              <input appUiControl name="early" type="number" min="0" required [(ngModel)]="shift.allowedEarlyPunchMinutes">
            </app-ui-form-field>
            <app-ui-form-field label="Late punch buffer" [required]="true">
              <input appUiControl name="late" type="number" min="0" required [(ngModel)]="shift.allowedLatePunchMinutes">
            </app-ui-form-field>
            <app-ui-form-field label="Minimum rest minutes">
              <input appUiControl name="rest" type="number" min="0" [(ngModel)]="shift.minimumRestBeforeNextShiftMinutes">
            </app-ui-form-field>
            <app-ui-form-field label="Late arrival grace minutes" [required]="true">
              <input appUiControl name="graceIn" type="number" min="0" required [(ngModel)]="shift.graceInMinutes">
            </app-ui-form-field>
            <app-ui-form-field label="Early departure grace minutes" [required]="true">
              <input appUiControl name="graceOut" type="number" min="0" required [(ngModel)]="shift.graceOutMinutes">
            </app-ui-form-field>
            <label class="ui-check cal-check">
              <input appUiControl name="overtime" type="checkbox" [(ngModel)]="shift.overtimeEligible">
              <span>Overtime eligible (no rates)</span>
            </label>
            <fieldset class="cal-fieldset cal-wide"><legend>Ordered duration-based breaks</legend>
              @for (b of shift.breaks; track b; let i = $index) {
                <div class="cal-break">
                  <app-ui-form-field label="Minutes" [required]="true">
                    <input appUiControl [name]="'break'+i" type="number" min="1" required [(ngModel)]="b.durationMinutes">
                  </app-ui-form-field>
                  <label class="ui-check">
                    <input appUiControl [name]="'paid'+i" type="checkbox" [(ngModel)]="b.isPaid">
                    <span>Paid</span>
                  </label>
                  <button appUiButton type="button" variant="outline" size="sm" [attr.aria-label]="'Remove break ' + (i + 1)" (click)="removeBreak(i)">Remove</button>
                </div>
              }
              <div>
                <button appUiButton type="button" variant="outline" size="sm" (click)="shift.breaks.push({sequence: shift.breaks.length+1, durationMinutes: 30, isPaid: false})">Add break</button>
              </div>
            </fieldset>
            <div class="cal-actions cal-wide">
              <button appUiButton type="submit" [disabled]="busy()">Save shift</button>
              <button appUiButton type="button" variant="outline" (click)="reset()">Cancel / reset</button>
            </div>
          </form>
        </app-ui-card>
      }
      @if (mode === 'shifts') {
        <app-ui-data-table
          ariaLabel="Shifts"
          density="compact"
          rowKey="id"
          [columns]="shiftColumns"
          [rows]="shiftRows()"
          [loading]="busy()"
          loadingLabel="Loading / saving..."
          emptyTitle="No shifts found."
          searchLabel="Search shifts"
          searchPlaceholder="Search shifts…"
          [initialPageSize]="25">
          <ng-template uiTableRowActions let-row>
            @if (canManage() && row.row.isActive) {
              <div class="cal-row-actions">
                <button appUiButton type="button" variant="ghost" size="sm" [attr.aria-label]="'Edit shift ' + row.shift" (click)="editShift(row.row)">Edit</button>
                <button appUiButton type="button" variant="ghost" size="sm" class="cal-danger" [attr.aria-label]="'Deactivate shift ' + row.shift" (click)="disableShift(row.row)">Deactivate</button>
              </div>
            }
          </ng-template>
        </app-ui-data-table>
      }
      @if (mode !== 'shifts' && canManage()) {
        <app-ui-card class="cal-card" [title]="mode === 'holidays' ? 'Holiday calendar' : (weeklyEdit ? 'New policy version' : 'Create weekly-off policy')">
          <form (ngSubmit)="saveScoped()" class="cal-grid" aria-label="Calendar configuration">
            @if (mode === 'holidays') {
              <app-ui-form-field label="Calendar name" [required]="true">
                <input appUiControl name="calendarName" required maxlength="150" [(ngModel)]="calendar.name">
              </app-ui-form-field>
            }
            <app-ui-form-field label="Scope">
              <select appUiControl name="scope" [(ngModel)]="scope.scopeType" [disabled]="!!weeklyEdit || !!calendarEdit" (ngModelChange)="scopeChanged()">
                <option value="Company">Company</option>
                <option value="LocationType">Location type</option>
                <option value="Location">Specific location</option>
              </select>
            </app-ui-form-field>
            @if (scope.scopeType === 'LocationType') {
              <app-ui-form-field label="Location type" [required]="true">
                <select appUiControl name="locationType" [(ngModel)]="scope.locationType" [disabled]="!!weeklyEdit || !!calendarEdit" required>
                  <option value="Outlet">Outlet</option>
                  <option value="Warehouse">Warehouse</option>
                  <option value="Factory">Factory (future source)</option>
                </select>
              </app-ui-form-field>
            }
            @if (scope.scopeType === 'Location') {
              <app-ui-form-field label="HR operational location" [required]="true">
                <select appUiControl name="location" [(ngModel)]="scope.operationalLocationProjectionId" [disabled]="!!weeklyEdit || !!calendarEdit" required>
                  <option [ngValue]="null">Select location</option>
                  @for (l of locations(); track l.id) {
                    <option [ngValue]="l.id">{{l.sourceEntityType}} - {{l.name}}</option>
                  }
                </select>
              </app-ui-form-field>
            }
            <app-ui-form-field label="Effective from" [required]="true">
              <input appUiControl name="from" type="date" appCompanyDate required [(ngModel)]="effectiveFrom">
            </app-ui-form-field>
            <app-ui-form-field label="Effective to (inclusive)">
              <input appUiControl name="to" type="date" appCompanyDate [(ngModel)]="effectiveTo">
            </app-ui-form-field>
            @if (mode === 'weekly-off') {
              <fieldset class="cal-fieldset"><legend>Weekly-off days</legend>
                <div class="cal-days">
                  @for (d of dayNames; track d; let i = $index) {
                    <label class="ui-check">
                      <input appUiControl type="checkbox" [name]="'day'+i" [ngModel]="days.includes(i)" (ngModelChange)="toggleDay(i, $event)">
                      <span>{{d}}</span>
                    </label>
                  }
                </div>
              </fieldset>
            }
            <div class="cal-actions cal-wide">
              <button appUiButton type="submit" [disabled]="busy()">Save {{weeklyEdit ? 'new version' : 'configuration'}}</button>
              <button appUiButton type="button" variant="outline" (click)="reset()">Cancel / reset</button>
            </div>
          </form>
        </app-ui-card>
      }
      @if (mode !== 'shifts' && canManage()) {
        <div class="cal-closure">
          <app-ui-form-field label="Closure effective end (UTC date, inclusive)">
            <input appUiControl type="date" appCompanyDate [(ngModel)]="closeDate">
          </app-ui-form-field>
        </div>
      }
      @if (mode === 'weekly-off') {
        <app-ui-data-table
          ariaLabel="Weekly-off policies"
          density="compact"
          rowKey="id"
          [columns]="weeklyColumns"
          [rows]="weeklyRows()"
          [loading]="busy()"
          loadingLabel="Loading / saving..."
          emptyTitle="No weekly-off policies found."
          searchLabel="Search weekly-off policies"
          searchPlaceholder="Search scope, days…"
          [initialPageSize]="25">
          <ng-template uiTableRowActions let-row>
            @if (canManage() && row.row.isActive) {
              <div class="cal-row-actions">
                <button appUiButton type="button" variant="ghost" size="sm" [attr.aria-label]="'New version of ' + row.scope" (click)="editWeekly(row.row)">New version</button>
                <button appUiButton type="button" variant="ghost" size="sm" class="cal-danger" [attr.aria-label]="'Close ' + row.scope + ' at date'" (click)="disableWeekly(row.row)">Close at date</button>
              </div>
            }
          </ng-template>
        </app-ui-data-table>
      }
      @if (mode === 'holidays') {
        <app-ui-data-table
          ariaLabel="Holiday calendars"
          density="compact"
          rowKey="id"
          [columns]="calendarColumns"
          [rows]="calendarRows()"
          [loading]="busy()"
          loadingLabel="Loading / saving..."
          emptyTitle="No holiday calendars found."
          searchLabel="Search holiday calendars"
          searchPlaceholder="Search calendars…"
          [initialPageSize]="25">
          <ng-template uiTableRowActions let-row>
            <div class="cal-row-actions">
              <button appUiButton type="button" variant="ghost" size="sm" [attr.aria-label]="'View holidays of ' + row.name" (click)="selectCalendar(row.row)">View holidays</button>
              @if (canManage() && row.row.isActive) {
                <button appUiButton type="button" variant="ghost" size="sm" [attr.aria-label]="'Edit future calendar ' + row.name" (click)="editCalendar(row.row)">Edit future calendar</button>
                <button appUiButton type="button" variant="ghost" size="sm" class="cal-danger" [attr.aria-label]="'Close ' + row.name + ' at date'" (click)="disableCalendar(row.row)">Close at date</button>
              }
            </div>
          </ng-template>
        </app-ui-data-table>
        @if (selectedCalendar; as selected) {
          <app-ui-card class="cal-card cal-card--spaced" [title]="'Holidays: ' + selected.data.name">
            @if (selected.isActive && canManage()) {
              <form (ngSubmit)="saveHoliday()" class="cal-grid cal-holiday-form" aria-label="Holiday details">
                <app-ui-form-field label="Date" [required]="true">
                  <input appUiControl name="holidayDate" type="date" appCompanyDate required [(ngModel)]="holiday.holidayDate">
                </app-ui-form-field>
                <app-ui-form-field label="Code" [required]="true">
                  <input appUiControl name="holidayCode" required maxlength="50" [(ngModel)]="holiday.code">
                </app-ui-form-field>
                <app-ui-form-field label="Name" [required]="true">
                  <input appUiControl name="holidayName" required maxlength="150" [(ngModel)]="holiday.name">
                </app-ui-form-field>
                <app-ui-form-field label="Classification" [required]="true">
                  <input appUiControl name="holidayType" required maxlength="50" [(ngModel)]="holiday.holidayType">
                </app-ui-form-field>
                <label class="ui-check cal-check">
                  <input appUiControl name="holidayPaid" type="checkbox" [(ngModel)]="holiday.isPaid">
                  <span>Paid holiday</span>
                </label>
                <app-ui-form-field label="Notes" class="cal-wide">
                  <textarea appUiControl name="notes" maxlength="1000" rows="3" [(ngModel)]="holiday.notes"></textarea>
                </app-ui-form-field>
                <div class="cal-actions cal-wide">
                  <button appUiButton type="submit" [disabled]="busy()">Save holiday</button>
                </div>
              </form>
            }
            <app-ui-data-table
              ariaLabel="Holidays"
              density="compact"
              rowKey="id"
              [columns]="holidayColumns"
              [rows]="holidayRows()"
              emptyTitle="No holidays found."
              searchLabel="Search holidays"
              searchPlaceholder="Search holidays…"
              [initialPageSize]="25">
              <ng-template uiTableRowActions let-row>
                @if (canManage() && row.row.isActive) {
                  <div class="cal-row-actions">
                    <button appUiButton type="button" variant="ghost" size="sm" [attr.aria-label]="'Edit future holiday ' + row.holiday" (click)="editHoliday(row.row)">Edit future holiday</button>
                    <button appUiButton type="button" variant="ghost" size="sm" class="cal-danger" [attr.aria-label]="'Deactivate holiday ' + row.holiday" (click)="disableHoliday(row.row)">Deactivate</button>
                  </div>
                }
              </ng-template>
            </app-ui-data-table>
          </app-ui-card>
        }
      }
      @if (mode !== 'shifts' && canClassify()) {
        <app-ui-card class="cal-card cal-card--spaced" title="Effective day classification">
          <div class="cal-lookup">
            <app-ui-form-field label="Date">
              <input appUiControl type="date" appCompanyDate [(ngModel)]="lookupDate">
            </app-ui-form-field>
            <app-ui-form-field label="Location">
              <select appUiControl [(ngModel)]="lookupLocation">
                <option [ngValue]="null">Select location</option>
                @for (l of locations(); track l.id) {
                  <option [ngValue]="l.id">{{l.name}}</option>
                }
              </select>
            </app-ui-form-field>
            <div class="cal-lookup__action">
              <button appUiButton type="button" [disabled]="busy()" (click)="lookup()">Resolve</button>
            </div>
          </div>
          @if (classification(); as c) {
            <app-ui-alert variant="info">{{ (c.workDate | appDate) }}: {{c.classification}} ({{c.source}}).
              Holiday: {{c.isHoliday}}; Weekly off: {{c.isWeeklyOff}}; policy version: {{c.weeklyOffPolicy?.versionNo || 'none'}}.
              {{c.holidays.length}} holiday evidence row(s). No benefits are calculated.</app-ui-alert>
          }
        </app-ui-card>
      }
    </main>
  `,
  styles: [`
    .cal-alerts { display: grid; gap: var(--rp-space-3); margin-bottom: var(--rp-space-4); }
    .cal-card { display: block; margin-bottom: var(--rp-space-6); }
    .cal-card--spaced { margin-top: var(--rp-space-6); }
    .cal-grid { display: grid; gap: var(--rp-form-gap); grid-template-columns: minmax(0, 1fr); }
    .cal-holiday-form { margin-bottom: var(--rp-space-4); }
    .cal-actions { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); }
    .cal-check { align-self: center; }
    .cal-fieldset { min-width: 0; margin: 0; padding: var(--rp-space-3) var(--rp-space-4);
      border: 1px solid var(--rp-color-border); border-radius: var(--rp-radius-md); }
    .cal-fieldset legend { padding: 0 var(--rp-space-2); color: var(--rp-color-text-primary);
      font-size: var(--rp-text-label-size); font-weight: var(--rp-font-weight-medium); }
    .cal-break { display: grid; gap: var(--rp-space-3); align-items: end; margin-bottom: var(--rp-space-3);
      grid-template-columns: minmax(0, 1fr); }
    .cal-days { display: flex; flex-wrap: wrap; gap: var(--rp-space-3) var(--rp-space-5); }
    .cal-closure { margin-bottom: var(--rp-space-4); max-width: 24rem; }
    .cal-row-actions { display: inline-flex; flex-wrap: wrap; align-items: center; justify-content: center; gap: var(--rp-space-1); }
    .cal-danger { color: var(--rp-color-status-danger-fg); }
    .cal-lookup { display: grid; gap: var(--rp-form-gap); grid-template-columns: minmax(0, 1fr); align-items: end; margin-bottom: var(--rp-space-3); }
    .cal-lookup__action { display: flex; }
    @media (min-width: 768px) {
      .cal-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .cal-wide { grid-column: 1 / -1; }
      .cal-break { grid-template-columns: minmax(0, 12rem) auto auto; }
      .cal-lookup { grid-template-columns: minmax(0, 1fr) minmax(0, 1fr) auto; }
    }
  `]
})
export class HrCalendarsPageComponent implements OnInit {
  private readonly companyDates = injectCompanyDates(CompanyDateService);
  private readonly api = inject(HrCalendarService);
  private readonly organisation = inject(HrOrganisationService);
  readonly auth = inject(AuthService);
  private readonly capabilities = inject(HrPayrollCapabilityStore);
  readonly mode: 'shifts' | 'holidays' | 'weekly-off' = inject(ActivatedRoute).snapshot.data['calendarMode'];
  readonly title = this.mode === 'shifts' ? 'Shift management' : this.mode === 'holidays' ? 'Holiday calendars' : 'Weekly-off policies';
  readonly shifts = signal<ConfigurationRow<ShiftData>[]>([]);
  readonly calendars = signal<ConfigurationRow<CalendarData>[]>([]);
  readonly weekly = signal<WeeklyOffRow[]>([]); readonly holidays = signal<HolidayRow[]>([]);
  readonly locations = signal<HrOperationalLocation[]>([]);
  readonly error = signal<string | null>(null); readonly busy = signal(false);
  readonly classification = signal<DayClassification | null>(null);
  readonly dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
  readonly breadcrumbs: UiBreadcrumb[] = [{ label: 'Dashboard', link: '/dashboard' }, { label: this.title }];
  readonly primaryCount = computed(() => this.mode === 'shifts' ? this.shifts().length
    : this.mode === 'holidays' ? this.calendars().length : this.weekly().length);
  readonly shiftColumns: UiTableColumn[] = [
    { key: 'shift', header: 'Shift', sortable: true },
    { key: 'window', header: 'Local window' },
    { key: 'limits', header: 'Minimum / maximum' },
    { key: 'status', header: 'Status', type: 'status', sortable: true, statusMap: ACTIVE_STATUS_MAP },
    { key: 'version', header: 'Version', type: 'number', decimals: 0, sortable: true }
  ];
  readonly weeklyColumns: UiTableColumn[] = [
    { key: 'scope', header: 'Scope', sortable: true },
    { key: 'days', header: 'Days' },
    { key: 'effective', header: 'Effective period', sortable: true, sortField: 'row.data.effectiveFrom' },
    { key: 'version', header: 'Version', type: 'number', decimals: 0, sortable: true },
    {
      key: 'state', header: 'State', type: 'status', sortable: true,
      statusMap: { Open: { label: 'Open', variant: 'success' }, Closed: { label: 'Closed (history retained)', variant: 'neutral' } }
    }
  ];
  readonly calendarColumns: UiTableColumn[] = [
    { key: 'name', header: 'Calendar', sortable: true },
    { key: 'scope', header: 'Scope', sortable: true },
    { key: 'effective', header: 'Effective period', sortable: true, sortField: 'row.data.effectiveFrom' },
    { key: 'version', header: 'Version', type: 'number', decimals: 0, sortable: true },
    {
      key: 'state', header: 'State', type: 'status', sortable: true,
      statusMap: { Open: { label: 'Open', variant: 'success' }, Closed: { label: 'Closed', variant: 'neutral' } }
    }
  ];
  readonly holidayColumns: UiTableColumn[] = [
    { key: 'date', type: 'date', header: 'Date', sortable: true },
    { key: 'holiday', header: 'Holiday', sortable: true },
    { key: 'kind', header: 'Classification / pay' },
    { key: 'status', header: 'Status', type: 'status', sortable: true, statusMap: ACTIVE_STATUS_MAP },
    { key: 'version', header: 'Version', type: 'number', decimals: 0, sortable: true }
  ];
  readonly shiftRows = computed(() => this.shifts().map(r => ({
    id: r.id, row: r,
    shift: `${r.data.code} - ${r.data.name}`,
    window: `${r.data.startTimeLocal} - ${r.data.endTimeLocal}${r.data.crossesMidnight ? ' (next day)' : ''}`,
    limits: `${r.data.minimumWorkMinutes} / ${r.data.maximumAllowedWorkMinutes ?? 'unspecified'}`,
    status: r.isActive ? 'Active' : 'Inactive',
    version: r.version
  })));
  readonly weeklyRows = computed(() => this.weekly().map(r => ({
    id: r.id, row: r,
    scope: this.scopeLabel(r.data.scope),
    days: this.dayLabel(r.data.days),
    effective: `${this.companyDates.format(r.data.effectiveFrom, false)} - ${this.companyDates.format(r.data.effectiveTo, false) || 'open'}`,
    version: r.versionNo,
    state: r.isActive ? 'Open' : 'Closed'
  })));
  readonly calendarRows = computed(() => this.calendars().map(r => ({
    id: r.id, row: r,
    name: r.data.name,
    scope: this.scopeLabel(r.data.scope),
    effective: `${this.companyDates.format(r.data.effectiveFrom, false)} - ${this.companyDates.format(r.data.effectiveTo, false) || 'open'}`,
    version: r.version,
    state: r.isActive ? 'Open' : 'Closed'
  })));
  readonly holidayRows = computed(() => this.holidays().map(r => ({
    id: r.id, row: r,
    date: r.data.holidayDate,
    holiday: `${r.data.code} - ${r.data.name}`,
    kind: `${r.data.holidayType} / ${r.data.isPaid ? 'Paid' : 'Unpaid'}`,
    status: r.isActive ? 'Active' : 'Inactive',
    version: r.version
  })));
  shiftEdit: ConfigurationRow<ShiftData> | null = null; calendarEdit: ConfigurationRow<CalendarData> | null = null;
  weeklyEdit: WeeklyOffRow | null = null; holidayEdit: HolidayRow | null = null;
  selectedCalendar: ConfigurationRow<CalendarData> | null = null;
  shift = this.emptyShift(); scope: CalendarScope = { scopeType: 'Company', locationType: null, operationalLocationProjectionId: null };
  calendar = { name: '' }; effectiveFrom = ''; effectiveTo = ''; closeDate = ''; days: number[] = [];
  holiday: HolidayData = { holidayDate: '', code: '', name: '', holidayType: 'Public', isPaid: true, notes: null };
  lookupDate = ''; lookupLocation: number | null = null;

  ngOnInit() {
    this.load();
    if (this.mode !== 'shifts' && this.auth.hasPermission('hr.location.view')) {
      this.organisation.getOperationalLocations().subscribe({
        next: rows => this.locations.set(rows.filter(l => l.isSourceActive && !l.isSourceDeleted && l.syncStatus === 'Current')),
        error: () => this.error.set('Unable to load authorized locations. Location-scoped changes cannot be configured.')
      });
    }
  }
  canManage() { return this.capabilities.isEnabled('attendance.enabled') && this.auth.hasPermission(this.permission('manage')); }
  canClassify() { return this.capabilities.isEnabled('attendance.enabled') && this.auth.hasPermission('hr.holiday.view') && this.auth.hasPermission('hr.weekly_off.view'); }
  private permission(action: 'manage' | 'view') { return `hr.${this.mode === 'shifts' ? 'shift' : this.mode === 'holidays' ? 'holiday' : 'weekly_off'}.${action}`; }
  load() {
    if (!this.capabilities.isEnabled('attendance.enabled') || !this.auth.hasPermission(this.permission('view'))) {
      this.error.set('HR/Attendance capability or view permission is unavailable.'); return;
    }
    this.busy.set(true);
    const failed = () => { this.busy.set(false); this.error.set('Unable to load configuration. Check Business, capabilities and permissions.'); };
    if (this.mode === 'shifts') this.api.shifts().subscribe({ next: r => { this.shifts.set(r); this.busy.set(false); }, error: failed });
    else if (this.mode === 'holidays') this.api.calendars().subscribe({ next: r => {
      this.calendars.set(r); this.busy.set(false);
      if (this.selectedCalendar) this.selectCalendar(r.find(c => c.id === this.selectedCalendar?.id) ?? this.selectedCalendar);
    }, error: failed });
    else this.api.weeklyOff().subscribe({ next: r => { this.weekly.set(r); this.busy.set(false); }, error: failed });
  }
  private write<T>(request: Observable<T>) {
    if (!this.canManage() || this.busy()) { this.error.set('Manage access is unavailable or a request is already running.'); return; }
    this.error.set(null); this.busy.set(true);
    request.subscribe({ next: () => { this.busy.set(false); this.reset(); this.classification.set(null); this.load(); },
      error: (e: { error?: { detail?: string } }) => { this.busy.set(false); this.error.set(e.error?.detail || 'Configuration was not saved. Check access, dates, conflicts and concurrency version.'); } });
  }
  saveShift() { this.write(this.api.saveShift({ ...this.shift, breaks: this.shift.breaks.map((b, i) => ({ ...b, sequence: i + 1 })) }, this.shiftEdit)); }
  editShift(r: ConfigurationRow<ShiftData>) { if (!this.canManage()) return; this.shiftEdit = r; this.shift = structuredClone(r.data); }
  disableShift(r: ConfigurationRow<ShiftData>) { this.write(this.api.deactivateShift(r)); }
  removeBreak(i: number) { this.shift.breaks.splice(i, 1); }
  scopeChanged() { this.scope.locationType = this.scope.scopeType === 'LocationType' ? 'Outlet' : null; this.scope.operationalLocationProjectionId = null; }
  saveScoped() {
    if (this.mode === 'holidays') this.write(this.api.saveCalendar({
      name: this.calendar.name, scope: { ...this.scope }, effectiveFrom: this.effectiveFrom, effectiveTo: this.effectiveTo || null
    }, this.calendarEdit));
    else this.write(this.api.saveWeeklyOff({ scope: { ...this.scope }, effectiveFrom: this.effectiveFrom, effectiveTo: this.effectiveTo || null, days: [...this.days] }, this.weeklyEdit));
  }
  editCalendar(r: ConfigurationRow<CalendarData>) { if (!this.canManage()) return; this.reset(); this.calendarEdit = r; this.calendar.name = r.data.name; this.scope = { ...r.data.scope }; this.effectiveFrom = r.data.effectiveFrom; this.effectiveTo = r.data.effectiveTo || ''; }
  editWeekly(r: WeeklyOffRow) { if (!this.canManage()) return; this.reset(); this.weeklyEdit = r; this.scope = { ...r.data.scope }; this.days = [...r.data.days]; this.effectiveTo = r.data.effectiveTo || ''; }
  disableCalendar(r: ConfigurationRow<CalendarData>) { if (!this.closeDate) { this.error.set('Select a future closure date.'); return; } this.write(this.api.deactivateCalendar(r, this.closeDate)); }
  disableWeekly(r: WeeklyOffRow) { if (!this.closeDate) { this.error.set('Select a future closure date.'); return; } this.write(this.api.deactivateWeeklyOff(r, this.closeDate)); }
  selectCalendar(r: ConfigurationRow<CalendarData>) {
    this.selectedCalendar = r; this.holidays.set([]); this.holidayEdit = null;
    this.api.holidays(r.id).subscribe({ next: rows => { if (this.selectedCalendar?.id === r.id) this.holidays.set(rows); },
      error: () => this.error.set('Unable to load holidays for this calendar.') });
  }
  saveHoliday() { if (this.selectedCalendar) this.write(this.api.saveHoliday(this.selectedCalendar.id, { ...this.holiday }, this.holidayEdit)); }
  editHoliday(r: HolidayRow) { if (!this.canManage()) return; this.holidayEdit = r; this.holiday = { ...r.data }; }
  disableHoliday(r: HolidayRow) { this.write(this.api.deactivateHoliday(r)); }
  toggleDay(day: number, checked: boolean) { this.days = checked ? [...new Set([...this.days, day])] : this.days.filter(d => d !== day); }
  lookup() {
    if (!this.canClassify() || !this.lookupDate || !this.lookupLocation) { this.error.set('Select a date and location with holiday and weekly-off view access.'); return; }
    this.classification.set(null);
    this.api.classify(this.lookupDate, this.lookupLocation).subscribe({ next: r => this.classification.set(r), error: () => this.error.set('Unable to resolve the effective day classification.') });
  }
  scopeLabel(s: CalendarScope) { return s.scopeType === 'Location' ? `Location ${s.operationalLocationProjectionId}` : s.scopeType === 'LocationType' ? `${s.locationType} type` : 'Company'; }
  dayLabel(days: number[]) { return days.map(d => this.dayNames[d]).join(', '); }
  reset() { this.shiftEdit = null; this.calendarEdit = null; this.weeklyEdit = null; this.holidayEdit = null;
    this.shift = this.emptyShift(); this.calendar = { name: '' }; this.effectiveFrom = ''; this.effectiveTo = ''; this.days = [];
    this.scope = { scopeType: 'Company', locationType: null, operationalLocationProjectionId: null };
    this.holiday = { holidayDate: '', code: '', name: '', holidayType: 'Public', isPaid: true, notes: null }; }
  private emptyShift(): ShiftData { return { code: '', name: '', startTimeLocal: '09:00', endTimeLocal: '17:00', crossesMidnight: false,
    minimumWorkMinutes: 420, maximumAllowedWorkMinutes: null, allowedEarlyPunchMinutes: 0, allowedLatePunchMinutes: 0,
    minimumRestBeforeNextShiftMinutes: null, graceInMinutes: 0, graceOutMinutes: 0, overtimeEligible: false, breaks: [] }; }
}
