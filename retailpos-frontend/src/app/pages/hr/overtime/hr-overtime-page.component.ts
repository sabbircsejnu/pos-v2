import { CompanyDateService } from '../../../services/company-date.service';
import { inject as injectCompanyDates } from '@angular/core';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UI_KIT, UiBadgeVariant, UiTableColumn } from '../../../components/shared/ui';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import {
  HrOvertimeService,
  OvertimeEntry,
  OvertimeFilters,
  OvertimePolicy,
  OvertimePolicyRequest,
  OvertimePolicyScopeType,
  OvertimeRateType,
  OvertimeRoundingMethod
} from '../../../services/hr-overtime.service';

export interface OvertimePolicyRow {
  id: number;
  policy: string;
  rateType: string;
  isActive: boolean;
  scope: string;
  effective: string;
  effectiveFrom: string;
  eligibility: string;
}

export interface OvertimeEntryRow {
  id: number;
  entry: OvertimeEntry;
  employee: string;
  workDate: string;
  context: string;
  calculation: string;
  workflow: string;
}

@Component({
  selector: 'app-hr-overtime-page',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  styles: [`
    .ot-page { display: flex; flex-direction: column; gap: var(--rp-space-4); }
    .ot-grid { display: grid; gap: var(--rp-form-gap); grid-template-columns: repeat(auto-fit, minmax(13rem, 1fr)); }
    .ot-checks { display: grid; gap: var(--rp-space-3); grid-template-columns: repeat(auto-fit, minmax(13rem, 1fr)); margin: var(--rp-space-4) 0 0; padding: 0; border: 0; min-inline-size: 0; }
    .ot-form-actions { display: flex; flex-wrap: wrap; align-items: center; gap: var(--rp-space-2); }
    .ot-actions { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); }
    .ot-secondary { display: block; color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); line-height: var(--rp-text-helper-line); }
    .ot-nowrap { white-space: nowrap; }
    @media (max-width: 639.98px) {
      .ot-form-actions > button { flex: 1 1 100%; }
    }
  `],
  template: `<main class="mx-auto max-w-7xl px-4 py-4">
    <app-ui-page-header title="Overtime"
      description="Overtime is calculated from frozen attendance and policy evidence. Approval records eligibility for future Payroll; no pay amount is calculated here." />

    <div class="ot-page">
      @if (error()) {
        <app-ui-alert variant="danger">{{ error() }}</app-ui-alert>
      }
      @if (notice()) {
        <app-ui-alert variant="success">{{ notice() }}</app-ui-alert>
      }
      @if (busy()) {
        <app-ui-state kind="loading" title="Loading overtime..." [compact]="true" />
      }

      @if (canManagePolicy) {
        <form (ngSubmit)="createPolicy()">
          <app-ui-card title="Create policy version">
            <div class="ot-grid">
              <app-ui-form-field label="Code" [required]="true">
                <input appUiControl name="code" required maxlength="100" [(ngModel)]="policy.code">
              </app-ui-form-field>
              <app-ui-form-field label="Name" [required]="true">
                <input appUiControl name="name" required maxlength="150" [(ngModel)]="policy.name">
              </app-ui-form-field>
              <app-ui-form-field label="Scope">
                <select appUiControl name="scopeType" [(ngModel)]="policy.scopeType" (ngModelChange)="scopeChanged()">
                  <option value="Business">Business</option><option value="LocationType">Location type</option>
                  <option value="Location">Location</option><option value="Employee">Employee</option>
                </select>
              </app-ui-form-field>
              @if (policy.scopeType !== 'Business') {
                <app-ui-form-field label="Scope reference" [required]="true">
                  <input appUiControl name="scopeReference" required [(ngModel)]="policy.scopeReference">
                </app-ui-form-field>
              }
              <app-ui-form-field label="Version number" [required]="true">
                <input appUiControl name="versionNo" type="number" min="1" required [(ngModel)]="policy.versionNo">
              </app-ui-form-field>
              <app-ui-form-field label="Effective from" [required]="true">
                <input appUiControl name="effectiveFrom" type="date" appCompanyDate required [(ngModel)]="policy.effectiveFrom">
              </app-ui-form-field>
              <app-ui-form-field label="Effective to">
                <input appUiControl name="effectiveTo" type="date" appCompanyDate [(ngModel)]="policy.effectiveTo">
              </app-ui-form-field>
              <app-ui-form-field label="Minimum minutes">
                <input appUiControl name="minimum" type="number" min="0" max="1440" [(ngModel)]="policy.minimumEligibleMinutes">
              </app-ui-form-field>
              <app-ui-form-field label="Maximum minutes">
                <input appUiControl name="maximum" type="number" min="0" max="2880" [(ngModel)]="policy.maximumEligibleMinutes">
              </app-ui-form-field>
              <app-ui-form-field label="Rounding unit">
                <input appUiControl name="roundingUnit" type="number" min="1" max="1440" [(ngModel)]="policy.roundingUnitMinutes">
              </app-ui-form-field>
              <app-ui-form-field label="Rounding">
                <select appUiControl name="rounding" [(ngModel)]="policy.roundingMethod">
                  <option value="Down">Down</option><option value="Nearest">Nearest</option><option value="Up">Up</option>
                </select>
              </app-ui-form-field>
              <app-ui-form-field label="Rate type">
                <select appUiControl name="rateType" [(ngModel)]="policy.rateType">
                  <option value="Multiplier">Multiplier</option><option value="FixedHourly">Fixed hourly</option>
                </select>
              </app-ui-form-field>
              <app-ui-form-field label="Normal day rate">
                <input appUiControl name="normalRate" type="number" min="0" step="0.01" [(ngModel)]="policy.normalDayRate">
              </app-ui-form-field>
              <app-ui-form-field label="Weekly-off rate">
                <input appUiControl name="weeklyRate" type="number" min="0" step="0.01" [(ngModel)]="policy.weeklyOffRate">
              </app-ui-form-field>
              <app-ui-form-field label="Holiday rate">
                <input appUiControl name="holidayRate" type="number" min="0" step="0.01" [(ngModel)]="policy.holidayRate">
              </app-ui-form-field>
              <app-ui-form-field label="Special-day rate">
                <input appUiControl name="specialRate" type="number" min="0" step="0.01" [(ngModel)]="policy.specialDayRate">
              </app-ui-form-field>
            </div>
            <fieldset class="ot-checks">
              <legend class="sr-only">Policy options</legend>
              <label class="ui-check"><input appUiControl name="includePre" type="checkbox" [(ngModel)]="policy.includePreShift"><span>Include pre-shift work</span></label>
              <label class="ui-check"><input appUiControl name="includePost" type="checkbox" [(ngModel)]="policy.includePostShift"><span>Include post-shift work</span></label>
              <label class="ui-check"><input appUiControl name="normalDay" type="checkbox" [(ngModel)]="policy.allowNormalWorkingDay"><span>Normal working day</span></label>
              <label class="ui-check"><input appUiControl name="weeklyOff" type="checkbox" [(ngModel)]="policy.allowWeeklyOff"><span>Weekly off</span></label>
              <label class="ui-check"><input appUiControl name="holiday" type="checkbox" [(ngModel)]="policy.allowHoliday"><span>Holiday</span></label>
              <label class="ui-check"><input appUiControl name="specialDay" type="checkbox" [(ngModel)]="policy.allowSpecialDay"><span>Special day</span></label>
              <label class="ui-check"><input appUiControl name="isActive" type="checkbox" [(ngModel)]="policy.isActive"><span>Active</span></label>
            </fieldset>
            <div uiCardFooter class="ot-form-actions">
              <button appUiButton type="submit" [disabled]="busy()">Create version</button>
            </div>
          </app-ui-card>
        </form>
      }

      @if (canManagePolicy) {
        <app-ui-card title="Policy versions">
          <app-ui-data-table
            ariaLabel="Overtime policy versions"
            density="compact"
            [columns]="policyColumns"
            [rows]="policyRows()"
            rowKey="id"
            searchLabel="Search policy versions"
            searchPlaceholder="Search policy versions…"
            emptyTitle="No overtime policy versions are configured.">
            <ng-template uiTableCell="policy" let-row>
              <strong>{{ row.policy }}</strong>
              <span class="ot-secondary">{{ row.rateType }} ·
                <app-ui-badge [variant]="row.isActive ? 'success' : 'neutral'">{{ row.isActive ? 'Active' : 'Inactive' }}</app-ui-badge></span>
            </ng-template>
          </app-ui-data-table>
        </app-ui-card>
      }

      <form (ngSubmit)="search()">
        <app-ui-card title="Calculation and approval workbench">
          <div class="ot-grid">
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
                <option value="">All statuses</option><option>Calculated</option><option>PendingApproval</option>
                <option>PayrollEligible</option><option>Rejected</option><option>Cancelled</option><option>Superseded</option>
              </select>
            </app-ui-form-field>
            <app-ui-form-field label="Decision reason">
              <input appUiControl name="decisionReason" maxlength="1000" [(ngModel)]="decisionReason">
            </app-ui-form-field>
          </div>
          <div uiCardFooter class="ot-form-actions">
            @if (canCalculate) {
              <button appUiButton type="button" variant="outline" [disabled]="busy()" (click)="calculate()">Calculate / reprocess</button>
            }
            <button appUiButton type="submit" [disabled]="busy()">Search</button>
          </div>
        </app-ui-card>
      </form>

      <app-ui-data-table
        ariaLabel="Overtime entries"
        density="compact"
        [columns]="entryColumns"
        [rows]="entryRows()"
        rowKey="id"
        [loading]="busy()"
        loadingLabel="Loading overtime..."
        searchLabel="Search overtime entries"
        searchPlaceholder="Search employee, date, status…"
        emptyTitle="No overtime entries match these filters.">
        <ng-template uiTableCell="employee" let-row>
          <strong>{{ row.employee }}</strong>
        </ng-template>
        <ng-template uiTableCell="calculation" let-row>
          {{ row.entry.eligibleMinutes }} eligible · {{ row.entry.calculatedMinutes }} calculated · {{ row.entry.approvedMinutes ?? '—' }} approved
          <span class="ot-secondary">Calculation v{{ row.entry.calculationVersion }} · Policy v{{ row.entry.policyVersionNo }}</span>
        </ng-template>
        <ng-template uiTableCell="context" let-row>
          {{ row.entry.dayCategory }} · location {{ row.entry.operationalLocationProjectionId }}
          <span class="ot-secondary">Attendance {{ row.entry.dailyAttendanceId }} / v{{ row.entry.attendanceProcessingVersion }}</span>
        </ng-template>
        <ng-template uiTableCell="workflow" let-row>
          <app-ui-badge [variant]="statusVariant(row.entry.status)">{{ row.entry.status }}</app-ui-badge>
          @if (row.entry.approvalStatus) {
            <span class="ot-secondary">approval {{ row.entry.approvalStatus }} step {{ row.entry.currentApprovalStep ?? '—' }}</span>
          }
          <span class="ot-secondary">Evidence versions {{ evidenceVersions(row.entry) }}</span>
        </ng-template>
        <ng-template uiTableRowActions let-row>
          <div class="ot-actions">
            @if (canSubmit && row.entry.status === 'Calculated' && row.entry.calculatedMinutes > 0) {
              <button appUiButton size="sm" type="button" [disabled]="busy()" (click)="submit(row.entry)">Submit</button>
            }
            @if (canCancel && row.entry.status === 'PendingApproval') {
              <button appUiButton size="sm" variant="outline" type="button" [disabled]="busy()" (click)="cancel(row.entry)">Cancel</button>
            }
            @if (canApprove && row.entry.status === 'PendingApproval' && row.entry.approvalInstanceId) {
              <button appUiButton size="sm" type="button" [disabled]="busy()" (click)="decide(row.entry, 'approve')">Approve</button>
              <button appUiButton size="sm" variant="danger" type="button" [disabled]="busy()" (click)="decide(row.entry, 'reject')">Reject</button>
            }
          </div>
        </ng-template>
      </app-ui-data-table>
    </div>
  </main>`
})
export class HrOvertimePageComponent implements OnInit {
  private readonly companyDates = injectCompanyDates(CompanyDateService);
  private readonly api = inject(HrOvertimeService);
  private readonly auth = inject(AuthService);
  private readonly capability = inject(HrPayrollCapabilityStore);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly notice = signal<string | null>(null);
  readonly policies = signal<OvertimePolicy[]>([]);
  readonly entries = signal<OvertimeEntry[]>([]);
  private readonly today = new Date().toISOString().slice(0, 10);
  readonly filters: OvertimeFilters = {
    from: this.today, to: this.today, employeeId: null, locationProjectionId: null, status: ''
  };
  policy: OvertimePolicyRequest = this.newPolicy();
  decisionReason = '';
  readonly scopes: OvertimePolicyScopeType[] = ['Business', 'LocationType', 'Location', 'Employee'];
  readonly roundingMethods: OvertimeRoundingMethod[] = ['Down', 'Nearest', 'Up'];
  readonly rateTypes: OvertimeRateType[] = ['Multiplier', 'FixedHourly'];
  readonly canManagePolicy = this.auth.hasPermission('hr.overtime_policy.manage');
  readonly canCalculate = this.auth.hasPermission('hr.overtime.calculate');
  readonly canSubmit = this.auth.hasPermission('hr.overtime.submit');
  readonly canCancel = this.auth.hasPermission('hr.overtime.submit');
  readonly canApprove = this.auth.hasPermission('hr.overtime.approve');
  readonly canView = this.auth.hasPermission('hr.overtime.view') || this.canApprove;

  readonly policyColumns: UiTableColumn[] = [
    { key: 'policy', header: 'Policy', field: 'policy', sortable: true },
    { key: 'scope', header: 'Scope', field: 'scope', sortable: true },
    { key: 'effective', header: 'Effective', field: 'effective', sortable: true, sortField: 'effectiveFrom' },
    { key: 'eligibility', header: 'Eligibility', field: 'eligibility' }
  ];
  readonly entryColumns: UiTableColumn[] = [
    { key: 'employee', header: 'Employee', field: 'employee', sortable: true },
    { key: 'workDate', type: 'date', header: 'Work date', field: 'workDate', sortable: true },
    { key: 'context', header: 'Attendance / context', field: 'context' },
    { key: 'calculation', header: 'Calculation', field: 'calculation' },
    { key: 'workflow', header: 'Workflow', field: 'workflow', sortable: true }
  ];

  // Presentation-only projections of the loaded data; the API rows themselves are never modified.
  readonly policyRows = computed<OvertimePolicyRow[]>(() => this.policies().map(row => ({
    effectiveFrom: row.effectiveFrom,
    id: row.id,
    policy: `${row.code} v${row.versionNo} — ${row.name}`,
    rateType: row.rateType,
    isActive: row.isActive,
    scope: row.scopeReference ? `${row.scopeType} · ${row.scopeReference}` : row.scopeType,
    effective: `${this.companyDates.format(row.effectiveFrom, false)} – ${this.companyDates.format(row.effectiveTo, false) || 'open'}`,
    eligibility: `Min ${row.minimumEligibleMinutes}m · cap ${row.maximumEligibleMinutes ?? 'none'}m · round ${row.roundingMethod}/${row.roundingUnitMinutes}m`
  })));
  readonly entryRows = computed<OvertimeEntryRow[]>(() => this.entries().map(entry => ({
    id: entry.id,
    entry,
    employee: `${entry.employeeCode} — ${entry.employeeName} (${entry.employeeId})`,
    workDate: entry.workDate,
    context: `${entry.dayCategory} · location ${entry.operationalLocationProjectionId} Attendance ${entry.dailyAttendanceId} / v${entry.attendanceProcessingVersion}`,
    calculation: `${entry.eligibleMinutes} eligible · ${entry.calculatedMinutes} calculated · ${entry.approvedMinutes ?? '—'} approved Calculation v${entry.calculationVersion} · Policy v${entry.policyVersionNo}`,
    workflow: `${entry.status}${entry.approvalStatus ? ` · approval ${entry.approvalStatus} step ${entry.currentApprovalStep ?? '—'}` : ''} Evidence versions ${this.evidenceVersions(entry)}`
  })));

  statusVariant(status: string): UiBadgeVariant {
    switch (status) {
      case 'Calculated': return 'info';
      case 'PendingApproval': return 'warning';
      case 'PayrollEligible': return 'success';
      case 'Rejected': return 'danger';
      default: return 'neutral';
    }
  }

  ngOnInit() {
    if (!this.capability.isEnabled('hr.enabled') || !this.capability.isEnabled('attendance.enabled')) {
      this.error.set('HR or Attendance capability is disabled.');
      return;
    }
    if (this.canManagePolicy) this.loadPolicies();
    if (this.canView) this.search();
  }

  scopeChanged() {
    if (this.policy.scopeType === 'Business') this.policy.scopeReference = null;
  }

  search() {
    if (this.busy() || !this.canView || !this.hasAnyAccess()) return;
    this.busy.set(true);
    this.clearMessages();
    this.api.entries(this.filters).subscribe({
      next: rows => { this.entries.set(rows); this.busy.set(false); },
      error: error => this.fail(error, 'Unable to load authorized overtime entries.')
    });
  }

  calculate() {
    if (this.busy() || !this.canCalculate || !this.capabilitiesEnabled()) return;
    this.busy.set(true);
    this.clearMessages();
    this.api.calculate(this.filters).subscribe({
      next: rows => {
        this.entries.set(rows);
        this.notice.set('Overtime calculation completed.');
        this.busy.set(false);
      },
      error: error => this.fail(error, 'Overtime calculation failed.')
    });
  }

  createPolicy() {
    if (this.busy() || !this.canManagePolicy || !this.capabilitiesEnabled()) return;
    this.busy.set(true);
    this.clearMessages();
    const request = {
      ...this.policy,
      scopeReference: this.policy.scopeType === 'Business' ? null : this.policy.scopeReference?.trim() || null
    };
    this.api.createPolicy(request).subscribe({
      next: () => {
        this.notice.set('Overtime policy version created.');
        this.policy = this.newPolicy();
        this.busy.set(false);
        this.loadPolicies();
      },
      error: error => this.fail(error, 'Unable to create overtime policy version.')
    });
  }

  submit(row: OvertimeEntry) {
    this.action(row, () => this.api.submit(row), 'Overtime submitted for approval.');
  }

  cancel(row: OvertimeEntry) {
    this.action(row, () => this.api.cancel(row, 'Cancelled from the overtime workbench.'),
      'Overtime approval cancelled.');
  }

  decide(row: OvertimeEntry, decision: 'approve' | 'reject') {
    if (!row.approvalInstanceId) return;
    const reason = this.decisionReason.trim();
    if (decision === 'reject' && !reason) {
      this.error.set('A reason is required to reject overtime.');
      return;
    }
    this.action(row, () => this.api.decide(row.approvalInstanceId!, decision, reason),
      decision === 'approve' ? 'Overtime approved.' : 'Overtime rejected.');
  }

  evidenceVersions(row: OvertimeEntry) {
    return row.evidence.map(item => `v${item.calculationVersion}`).join(', ') || 'none';
  }

  private action(row: OvertimeEntry, request: () => ReturnType<HrOvertimeService['submit']>, message: string) {
    if (this.busy() || !this.capabilitiesEnabled()) return;
    this.busy.set(true);
    this.clearMessages();
    request().subscribe({
      next: () => {
        this.notice.set(message);
        this.busy.set(false);
        this.search();
      },
      error: error => this.fail(error, `Unable to update overtime entry ${row.id}.`)
    });
  }

  private loadPolicies() {
    this.api.policies().subscribe({
      next: rows => this.policies.set(rows),
      error: error => this.error.set(error.error?.detail ?? 'Unable to load overtime policies.')
    });
  }

  private hasAnyAccess() {
    if (!this.capabilitiesEnabled()
      || !(this.canManagePolicy || this.canCalculate || this.canSubmit || this.canApprove
        || this.canView)) {
      this.error.set('HR/Attendance capability or overtime permission unavailable.');
      return false;
    }
    return true;
  }

  private capabilitiesEnabled() {
    return this.capability.isEnabled('hr.enabled') && this.capability.isEnabled('attendance.enabled');
  }

  private clearMessages() {
    this.error.set(null);
    this.notice.set(null);
  }

  private fail(error: any, fallback: string) {
    this.busy.set(false);
    this.error.set(error.error?.detail ?? fallback);
  }

  private newPolicy(): OvertimePolicyRequest {
    return {
      code: '', name: '', scopeType: 'Business', scopeReference: null, versionNo: 1,
      effectiveFrom: this.today, effectiveTo: null,
      minimumEligibleMinutes: 1, maximumEligibleMinutes: null, roundingUnitMinutes: 1,
      roundingMethod: 'Down', includePreShift: false, includePostShift: false,
      allowNormalWorkingDay: true, allowWeeklyOff: false, allowHoliday: false, allowSpecialDay: false,
      rateType: 'Multiplier', normalDayRate: 1, weeklyOffRate: 1, holidayRate: 1, specialDayRate: 1,
      isActive: true
    };
  }
}
