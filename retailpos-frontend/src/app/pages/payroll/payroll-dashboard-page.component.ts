import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../services/hr-payroll-capability.store';
import {
  PayrollDashboardFilters,
  PayrollDashboardService,
  PayrollDashboardSummary
} from '../../services/payroll-dashboard.service';
import { UI_KIT, UiTableColumn } from '../../components/shared/ui';

@Component({
  selector: 'app-payroll-dashboard-page',
  standalone: true,
  imports: [FormsModule, RouterLink, ...UI_KIT],
  styles: [`
    :host { display: block; }
    .pd-page { display: flex; flex-direction: column; gap: var(--rp-space-4); padding: var(--rp-space-4); }
    .pd-form { display: grid; gap: var(--rp-form-gap); align-items: end; grid-template-columns: minmax(0, 1fr); }
    .pd-grid { display: grid; gap: var(--rp-space-4); grid-template-columns: minmax(0, 1fr); }
    .pd-kpi { display: flex; flex-direction: column; gap: var(--rp-space-1); margin: 0; }
    .pd-kpi__label { color: var(--rp-color-text-secondary); font-size: var(--rp-text-label-size); font-weight: var(--rp-font-weight-medium); }
    .pd-kpi__value { margin: 0; color: var(--rp-color-text-primary); font-size: var(--rp-text-page-title-size); font-weight: var(--rp-font-weight-semibold); line-height: var(--rp-text-page-title-line); }
    .pd-facts { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: var(--rp-space-2) var(--rp-space-4); margin: 0; }
    .pd-facts dt { color: var(--rp-color-text-secondary); }
    .pd-facts dd { margin: 0; color: var(--rp-color-text-primary); font-weight: var(--rp-font-weight-semibold); text-align: end; }
    .pd-note { margin: 0 0 var(--rp-space-3); color: var(--rp-color-text-secondary); }
    .pd-card-link { margin-top: var(--rp-space-3); }
    @media (min-width: 640px) {
      .pd-form { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .pd-grid--sections, .pd-grid--kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }
    @media (min-width: 1024px) {
      .pd-page { padding: var(--rp-space-6); }
      .pd-form { grid-template-columns: repeat(4, minmax(0, 1fr)); }
      .pd-grid--kpis { grid-template-columns: repeat(3, minmax(0, 1fr)); }
    }
  `],
  template: `<main class="pd-page">
    <app-ui-page-header title="Payroll dashboard"
      description="Read-only summary of persisted Payroll, payment, reconciliation, Accounting, and Final Settlement evidence." />
    @if (error()) {
      <app-ui-alert variant="danger">{{error()}}</app-ui-alert>
    }
    @if (busy()) {
      <app-ui-state kind="loading" title="Loading Payroll dashboard..." [compact]="true" />
    }
    <app-ui-card title="Filters" subtitle="Choose the period, run and location scope to summarise.">
      <form (ngSubmit)="search()" class="pd-form" aria-label="Payroll dashboard filters">
        <app-ui-form-field label="From" [required]="true">
          <input appUiControl name="fromDate" type="date" appCompanyDate required [(ngModel)]="fromDate">
        </app-ui-form-field>
        <app-ui-form-field label="To" [required]="true">
          <input appUiControl name="toDate" type="date" appCompanyDate required [(ngModel)]="toDate">
        </app-ui-form-field>
        <app-ui-form-field label="Payroll period ID">
          <input appUiControl name="periodId" type="number" min="1" [(ngModel)]="periodId">
        </app-ui-form-field>
        <app-ui-form-field label="Payroll run ID">
          <input appUiControl name="runId" type="number" min="1" [(ngModel)]="runId">
        </app-ui-form-field>
        <app-ui-form-field label="Location ID">
          <input appUiControl name="locationId" type="number" min="1" [(ngModel)]="locationId">
        </app-ui-form-field>
        <app-ui-form-field label="Run type">
          <select appUiControl name="runType" [(ngModel)]="runType">
            <option [ngValue]="null">All types</option>
            <option value="Regular">Regular</option>
            <option value="Correction">Correction</option>
            <option value="OffCycle">Off-cycle</option>
          </select>
        </app-ui-form-field>
        <div>
          <button appUiButton type="submit" [disabled]="busy()" [loading]="busy()">Refresh</button>
        </div>
      </form>
    </app-ui-card>
    @if (summary(); as data) {
      @if (!data.period.id) {
        <app-ui-alert variant="info">No Payroll period is available in this scope.</app-ui-alert>
      }
      <section class="pd-grid pd-grid--kpis" aria-label="Payroll totals">
        @for (kpi of kpis(data); track kpi.label) {
          <app-ui-card>
            <dl class="pd-kpi">
              <dt class="pd-kpi__label">{{kpi.label}}</dt>
              <dd class="pd-kpi__value">{{kpi.value}}</dd>
            </dl>
          </app-ui-card>
        }
      </section>
      @if (!data.run.id) {
        <app-ui-alert variant="info">No Payroll run matches these filters.</app-ui-alert>
      }
      <div class="pd-grid pd-grid--sections">
        <app-ui-card title="Payment states">
          @if (!hasCounts(data.payments.states)) {
            <p class="pd-note">No payment data for this run.</p>
          }
          @if (data.payments.states.length) {
            <app-ui-data-table
              [columns]="paymentStateColumns" [rows]="data.payments.states" rowKey="status"
              [searchable]="false" [paginated]="false" density="compact" ariaLabel="Payment states" />
          }
          <a appUiButton variant="outline" size="sm" class="pd-card-link" routerLink="/payroll/payments">Open payments</a>
        </app-ui-card>
        <app-ui-card title="Reconciliation">
          @if (!data.reconciliation.applies) {
            <p class="pd-note">Reconciliation is not applicable for this run.</p>
          }
          @if (data.reconciliation.applies) {
            <dl class="pd-facts">
              <dt>Reconciled</dt><dd>{{data.reconciliation.reconciledCount}}</dd>
              <dt>Partial</dt><dd>{{data.reconciliation.partiallyReconciledCount}}</dd>
              <dt>Unreconciled</dt><dd>{{data.reconciliation.unreconciledCount}}</dd>
              <dt>Exceptions</dt><dd>{{data.reconciliation.exceptionCount}}</dd>
            </dl>
          }
          <a appUiButton variant="outline" size="sm" class="pd-card-link" routerLink="/payroll/payments">Review payment evidence</a>
        </app-ui-card>
        <app-ui-card title="Accounting postings">
          @if (!data.accounting.enabled) {
            <p class="pd-note">Accounting is not enabled for this Business.</p>
          }
          @if (data.accounting.enabled) {
            <dl class="pd-facts">
              <dt>Pending, failed, or conflicting postings</dt><dd>{{data.accounting.blockerCount}}</dd>
            </dl>
          }
        </app-ui-card>
        <app-ui-card title="Final Settlements">
          <dl class="pd-facts">
            <dt>Approved positive</dt>
            <dd>{{data.finalSettlements.approvedPositiveCount}} · {{data.finalSettlements.approvedPositiveAmount}}</dd>
            <dt>Paid</dt><dd>{{data.finalSettlements.paidAmount}}</dd>
            <dt>Outstanding</dt><dd>{{data.finalSettlements.outstandingAmount}}</dd>
            <dt>Receivables</dt>
            <dd>{{data.finalSettlements.negativeReceivableCount}} · {{data.finalSettlements.negativeReceivableAmount}}</dd>
            <dt>Unresolved financial items</dt><dd>{{data.finalSettlements.unresolvedFinancialCount}}</dd>
          </dl>
          <a appUiButton variant="outline" size="sm" class="pd-card-link" routerLink="/hr/exits">Open exit cases</a>
        </app-ui-card>
        <app-ui-card title="Operational blockers">
          <dl class="pd-facts">
            <dt>Unresolved payments</dt><dd>{{data.blockers.unresolvedPaymentCount}}</dd>
            <dt>Partial payments</dt><dd>{{data.blockers.partialPaymentCount}}</dd>
            <dt>Pending reconciliation</dt><dd>{{data.blockers.reconciliationPendingCount}}</dd>
            <dt>Accounting pending or failed</dt><dd>{{data.blockers.accountingPendingOrFailedCount}}</dd>
            <dt>Correction runs pending</dt><dd>{{data.blockers.correctionPendingCount}}</dd>
          </dl>
          <a appUiButton variant="outline" size="sm" class="pd-card-link" routerLink="/payroll/periods">Open Payroll periods</a>
        </app-ui-card>
      </div>
    }
  </main>`
})
export class PayrollDashboardPageComponent implements OnInit {
  private readonly api = inject(PayrollDashboardService);
  private readonly auth = inject(AuthService);
  private readonly capability = inject(HrPayrollCapabilityStore);

  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly summary = signal<PayrollDashboardSummary | null>(null);
  private readonly today = new Date().toISOString().slice(0, 10);
  toDate = this.today;
  fromDate = this.daysBefore(30);
  periodId: number | null = null;
  runId: number | null = null;
  locationId: number | null = null;
  runType: string | null = null;

  readonly paymentStateColumns: UiTableColumn[] = [
    { key: 'status', header: 'Status', type: 'status' },
    { key: 'count', header: 'Count', align: 'end' },
    { key: 'amount', header: 'Outstanding', align: 'end' }
  ];

  ngOnInit() {
    this.search();
  }

  kpis(data: PayrollDashboardSummary) {
    return [
      { label: 'Gross earnings', value: data.run.gross },
      { label: 'Deductions', value: data.run.totalDeductions },
      { label: 'Net payable', value: data.run.netPayable },
      { label: 'Paid', value: data.payments.totalPaid },
      { label: 'Outstanding', value: data.payments.totalOutstanding },
      { label: 'Employees in run', value: data.run.employeeCount }
    ];
  }

  search() {
    if (!this.allowed()) return;
    this.busy.set(true);
    this.error.set(null);
    const filters: PayrollDashboardFilters = {
      periodId: this.periodId,
      runId: this.runId,
      locationId: this.locationId,
      fromDate: this.fromDate,
      toDate: this.toDate,
      runType: this.runType
    };
    this.api.query(filters).subscribe({
      next: result => {
        this.summary.set(result);
        this.busy.set(false);
      },
      error: error => {
        this.busy.set(false);
        this.error.set(error.error?.detail ?? 'Unable to load the authorized Payroll dashboard.');
      }
    });
  }

  hasCounts(metrics: { count: number }[]) {
    return metrics.some(metric => metric.count > 0);
  }

  private allowed() {
    if (!this.capability.isEnabled('hr.enabled')
      || !this.capability.isEnabled('payroll.enabled')
      || !this.auth.hasPermission('payroll.dashboard.view')
      || !this.auth.hasPermission('payroll.salary.view')) {
      this.error.set('Payroll capability or confidential dashboard permissions are unavailable.');
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
