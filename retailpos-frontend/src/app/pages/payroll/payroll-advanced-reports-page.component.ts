import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../services/hr-payroll-capability.store';
import {
  PayrollAdvancedReportFilters,
  PayrollAdvancedReportPage,
  PayrollAdvancedReportService
} from '../../services/payroll-advanced-report.service';
import { UI_KIT, UiTableColumn } from '../../components/shared/ui';

@Component({
  selector: 'app-payroll-advanced-reports-page',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  styles: [`
    :host { display: block; }
    .pr-page { display: flex; flex-direction: column; gap: var(--rp-space-4); padding: var(--rp-space-4); }
    .pr-form { display: grid; gap: var(--rp-form-gap); align-items: end; grid-template-columns: minmax(0, 1fr); }
    .pr-results { display: flex; flex-direction: column; gap: var(--rp-space-3); }
    .pr-summary { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: var(--rp-space-3); }
    .pr-count { margin: 0; color: var(--rp-color-text-secondary); font-weight: var(--rp-font-weight-medium); }
    .pr-actions, .pr-pager { display: flex; flex-wrap: wrap; align-items: center; gap: var(--rp-space-3); }
    .pr-pager-label { color: var(--rp-color-text-secondary); }
    .pr-nowrap { white-space: nowrap; }
    .pr-muted { color: var(--rp-color-text-secondary); }
    .pr-wide { display: inline-block; min-width: 12rem; }
    @media (min-width: 640px) {
      .pr-form { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }
    @media (min-width: 1024px) {
      .pr-page { padding: var(--rp-space-6); }
      .pr-form { grid-template-columns: repeat(4, minmax(0, 1fr)); }
    }
  `],
  template: `<main class="pr-page">
    <app-ui-page-header title="Payroll reports"
      description="Reports use persisted Payroll evidence and are restricted to your authorized Business and location scope." />
    @if (error()) {
      <app-ui-alert variant="danger">{{error()}}</app-ui-alert>
    }
    @if (busy()) {
      <app-ui-state kind="loading" title="Loading Payroll report..." [compact]="true" />
    }
    @if (exportBusy()) {
      <app-ui-state kind="loading" title="Preparing Payroll report export..." [compact]="true" />
    }
    <app-ui-card title="Filters" subtitle="Choose a report and narrow it by period, employee, location or run.">
      <form (ngSubmit)="search()" class="pr-form" aria-label="Payroll report filters">
        <app-ui-form-field label="Report">
          <select appUiControl name="kind" [(ngModel)]="filters.kind" (ngModelChange)="reportKindChanged()">
            <option value="PayrollRegister">Payroll register</option>
            <option value="PayrollComponents">Payroll components</option>
            <option value="PayrollVariance">Payroll variance</option>
            <option value="CorrectionOffCycle">Correction / off-cycle</option>
            <option value="SalaryHistory">Salary history</option>
            <option value="LoanRecovery">Loan recovery</option>
            <option value="RunStatus">Run status</option>
            <option value="PaymentOutstanding">Payment / outstanding / reconciliation</option>
            <option value="CostByLocation">Payroll cost by location / type</option>
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
        <app-ui-form-field label="Location ID">
          <input appUiControl name="locationId" type="number" min="1" [(ngModel)]="filters.locationId">
        </app-ui-form-field>
        <app-ui-form-field label="Run ID">
          <input appUiControl name="runId" type="number" min="1" [(ngModel)]="filters.runId">
        </app-ui-form-field>
        @if (filters.kind === 'PayrollVariance') {
          <app-ui-form-field label="Compare run ID" [required]="true">
            <input appUiControl name="compareRunId" type="number" min="1" required [(ngModel)]="filters.compareRunId">
          </app-ui-form-field>
        }
        <div>
          <button appUiButton type="submit" [disabled]="busy()" [loading]="busy()">Search</button>
        </div>
      </form>
    </app-ui-card>
    @if (report(); as result) {
      <section class="pr-results" aria-label="Report results">
        <div class="pr-summary">
          <p class="pr-count" aria-live="polite">{{result.totalCount}} matching records</p>
          @if (result.totalCount && canExport()) {
            <div class="pr-actions">
              <button appUiButton variant="outline" type="button" [disabled]="busy() || exportBusy()" (click)="download('csv')">Export CSV</button>
              <button appUiButton variant="outline" type="button" [disabled]="busy() || exportBusy()" (click)="download('xlsx')">Export XLSX</button>
            </div>
          }
        </div>
        <app-ui-data-table
          [columns]="columns" [rows]="result.items" [searchable]="false" [paginated]="false"
          ariaLabel="Payroll report results" density="compact"
          emptyTitle="No records match these filters.">
          <ng-template uiTableCell="employee" let-row>
            <span class="pr-wide">{{row.employeeCode || '—'}} — {{row.employeeName || '—'}}@if (row.employeeId) {<span> ({{row.employeeId}})</span>}</span>
          </ng-template>
          <ng-template uiTableCell="periodRun" let-row>
            <span class="pr-nowrap">{{row.periodCode || row.periodId || '—'}} / {{row.runId || '—'}}
              @if (row.runRevision !== null) {
                <span> · rev {{row.runRevision}}</span>
              }
              @if (row.runType) {
                <span> · {{row.runType}}</span>
              }
            </span>
          </ng-template>
          <ng-template uiTableCell="component" let-row>{{row.componentCode || '—'}}@if (row.componentName) {<span> · {{row.componentName}}</span>}</ng-template>
          <ng-template uiTableCell="gross" let-row>{{value(row.gross)}}</ng-template>
          <ng-template uiTableCell="deductions" let-row>{{value(row.deductions)}}</ng-template>
          <ng-template uiTableCell="net" let-row>{{value(row.net)}}</ng-template>
          <ng-template uiTableCell="location" let-row>{{row.locationName || row.locationId || '—'}}</ng-template>
          <ng-template uiTableCell="effective" let-row>
            <span class="pr-nowrap">{{ (row.effectiveDate | appDate) || '—' }}@if (row.effectiveTo) {<span> – {{ (row.effectiveTo | appDate) }}</span>}</span>
          </ng-template>
          <ng-template uiTableCell="loanRecovery" let-row>
            @if (row.opening !== null || row.recovered !== null || row.outstanding !== null) {
              <span class="pr-wide">Opening {{value(row.opening)}} · recovered {{value(row.recovered)}} · outstanding {{value(row.outstanding)}}</span>
            }
            @if (row.opening === null && row.recovered === null && row.outstanding === null) {
              <span>—</span>
            }
          </ng-template>
        </app-ui-data-table>
        @if (result.totalCount) {
          <nav class="pr-pager" aria-label="Report pagination">
            <button appUiButton variant="outline" size="sm" type="button" [disabled]="busy() || filters.page <= 1" (click)="changePage(-1)">Previous</button>
            <span class="pr-pager-label">Page {{result.page}} of {{pageCount(result)}}</span>
            <button appUiButton variant="outline" size="sm" type="button" [disabled]="busy() || result.page >= pageCount(result)" (click)="changePage(1)">Next</button>
          </nav>
        }
      </section>
    }
  </main>`
})
export class PayrollAdvancedReportsPageComponent implements OnInit {
  private readonly api = inject(PayrollAdvancedReportService);
  private readonly auth = inject(AuthService);
  private readonly capability = inject(HrPayrollCapabilityStore);
  readonly busy = signal(false);
  readonly exportBusy = signal(false);
  readonly error = signal<string | null>(null);
  readonly report = signal<PayrollAdvancedReportPage | null>(null);
  private readonly today = new Date().toISOString().slice(0, 10);
  filters: PayrollAdvancedReportFilters = {
    kind: 'PayrollRegister', from: this.daysBefore(30), to: this.today,
    employeeId: null, locationId: null, runId: null, compareRunId: null,
    page: 1, pageSize: 50
  };

  readonly columns: UiTableColumn[] = [
    { key: 'employee', header: 'Employee', type: 'custom' },
    { key: 'periodRun', header: 'Period / run', type: 'custom' },
    { key: 'category', header: 'Category' },
    { key: 'component', header: 'Component', type: 'custom' },
    { key: 'gross', header: 'Gross', type: 'custom', align: 'end' },
    { key: 'deductions', header: 'Deductions', type: 'custom', align: 'end' },
    { key: 'net', header: 'Net', type: 'custom', align: 'end' },
    { key: 'status', header: 'Status', type: 'status' },
    { key: 'location', header: 'Location', type: 'custom' },
    { key: 'effective', header: 'Effective', type: 'custom' },
    { key: 'loanRecovery', header: 'Loan recovery', type: 'custom' },
    { key: 'detail', header: 'Details' }
  ];

  ngOnInit() {
    this.search();
  }

  search() {
    if (!this.allowed()) return;
    if (this.filters.kind === 'PayrollVariance' && this.filters.compareRunId === null) {
      this.error.set('A comparison run is required for Payroll variance.');
      return;
    }
    this.filters.page = 1;
    this.load();
  }

  reportKindChanged() {
    if (this.filters.kind !== 'PayrollVariance') this.filters.compareRunId = null;
  }

  changePage(amount: number) {
    if (!this.allowed()) return;
    this.filters.page += amount;
    this.load();
  }

  download(format: 'csv' | 'xlsx') {
    if (!this.allowed() || this.exportBusy()) return;
    if (!this.canExport()) {
      this.error.set('Payroll export permission is unavailable.');
      return;
    }
    this.exportBusy.set(true);
    this.error.set(null);
    this.api.export(this.filters, format).subscribe({
      next: blob => {
        const objectUrl = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = objectUrl;
        link.download = `payroll-${this.filters.kind}.${format}`;
        link.click();
        URL.revokeObjectURL(objectUrl);
        this.exportBusy.set(false);
      },
      error: error => {
        this.exportBusy.set(false);
        this.error.set(error.error?.detail ?? 'Unable to export the authorized Payroll report.');
      }
    });
  }

  value(amount: number | null) {
    return amount === null ? '—' : amount;
  }

  pageCount(result: PayrollAdvancedReportPage) {
    return Math.max(1, Math.ceil(result.totalCount / result.pageSize));
  }

  canExport() {
    return this.auth.hasPermission('payroll.export');
  }

  private load() {
    this.busy.set(true);
    this.error.set(null);
    this.api.query(this.filters).subscribe({
      next: result => {
        this.report.set(result);
        this.busy.set(false);
      },
      error: error => {
        this.busy.set(false);
        this.error.set(error.error?.detail ?? 'Unable to load the authorized Payroll report.');
      }
    });
  }

  private allowed() {
    if (!this.capability.isEnabled('hr.enabled')
      || !this.capability.isEnabled('payroll.enabled')
      || !this.auth.hasPermission('payroll.report.view')
      || !this.auth.hasPermission('payroll.salary.view')) {
      this.error.set('Payroll capability or confidential report permissions are unavailable.');
      return false;
    }
    return !this.busy();
  }

  private daysBefore(days: number) {
    const date = new Date(`${this.today}T00:00:00Z`);
    date.setUTCDate(date.getUTCDate() - days);
    return date.toISOString().slice(0, 10);
  }
}
