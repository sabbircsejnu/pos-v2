import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import { HrDashboardService, HrDashboardSummary } from '../../../services/hr-dashboard.service';
import { UI_KIT } from '../../../components/shared/ui';

@Component({
  selector: 'app-hr-dashboard-page',
  standalone: true,
  imports: [FormsModule, RouterLink, ...UI_KIT],
  styles: [`
    :host { display: block; }
    .hd-page { display: flex; flex-direction: column; gap: var(--rp-space-4); padding: var(--rp-space-4); }
    .hd-form { display: grid; gap: var(--rp-form-gap); align-items: end; grid-template-columns: minmax(0, 1fr); }
    .hd-grid { display: grid; gap: var(--rp-space-4); grid-template-columns: minmax(0, 1fr); }
    .hd-kpi { display: flex; flex-direction: column; gap: var(--rp-space-1); margin: 0; }
    .hd-kpi__label { color: var(--rp-color-text-secondary); font-size: var(--rp-text-label-size); font-weight: var(--rp-font-weight-medium); }
    .hd-kpi__value { margin: 0; color: var(--rp-color-text-primary); font-size: var(--rp-text-page-title-size); font-weight: var(--rp-font-weight-semibold); line-height: var(--rp-text-page-title-line); }
    .hd-kpi-link { margin-top: var(--rp-space-2); }
    .hd-metrics { display: flex; flex-direction: column; gap: var(--rp-space-2); margin: 0; padding: 0; list-style: none; }
    .hd-metrics li { display: flex; flex-wrap: wrap; align-items: center; gap: var(--rp-space-2); }
    .hd-card-link { margin-top: var(--rp-space-3); }
    @media (min-width: 640px) {
      .hd-form { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .hd-grid--sections { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .hd-grid--kpis { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }
    @media (min-width: 1024px) {
      .hd-page { padding: var(--rp-space-6); }
      .hd-form { grid-template-columns: repeat(5, minmax(0, 1fr)); }
      .hd-grid--kpis { grid-template-columns: repeat(3, minmax(0, 1fr)); }
    }
  `],
  template: `<main class="hd-page">
    <app-ui-page-header title="HR dashboard"
      description="Aggregated workforce, attendance, leave, overtime, document-expiry and exit indicators. No salary, bank, payment, or individual document data is shown." />
    @if (error()) {
      <app-ui-alert variant="danger">{{error()}}</app-ui-alert>
    }
    @if (busy()) {
      <app-ui-state kind="loading" title="Loading HR dashboard..." [compact]="true" />
    }
    <app-ui-card title="Filters" subtitle="Choose the period and location scope to summarise.">
      <form (ngSubmit)="search()" class="hd-form" aria-label="HR dashboard filters">
        <app-ui-form-field label="From" [required]="true">
          <input appUiControl name="from" type="date" appCompanyDate required [(ngModel)]="from">
        </app-ui-form-field>
        <app-ui-form-field label="To" [required]="true">
          <input appUiControl name="to" type="date" appCompanyDate required [(ngModel)]="to">
        </app-ui-form-field>
        <app-ui-form-field label="Location projection ID">
          <input appUiControl name="location" type="number" min="1" [(ngModel)]="locationProjectionId">
        </app-ui-form-field>
        <app-ui-form-field label="Location type">
          <select appUiControl name="locationType" [(ngModel)]="locationType">
            <option [ngValue]="null">All types</option>
            <option value="Outlet">Outlet</option>
            <option value="Warehouse">Warehouse</option>
          </select>
        </app-ui-form-field>
        <div>
          <button appUiButton type="submit" [disabled]="busy()" [loading]="busy()">Refresh</button>
        </div>
      </form>
    </app-ui-card>
    @if (summary(); as data) {
      <section class="hd-grid hd-grid--kpis" aria-label="HR overview">
        @for (kpi of kpis(data); track kpi.label) {
          <app-ui-card>
            <dl class="hd-kpi">
              <dt class="hd-kpi__label">{{kpi.label}}</dt>
              <dd class="hd-kpi__value">{{kpi.value}}</dd>
            </dl>
            @if (kpi.link) {
              <a appUiButton variant="ghost" size="sm" class="hd-kpi-link" [routerLink]="kpi.link">{{kpi.linkLabel}}</a>
            }
          </app-ui-card>
        }
      </section>
      @if (!data.totalEmployees) {
        <app-ui-alert variant="info">No employees are in this scope.</app-ui-alert>
      }
      <div class="hd-grid hd-grid--sections">
        @for (section of sections(data); track section.title) {
          <app-ui-card [title]="section.title">
            @if (!section.metrics.length) {
              <app-ui-state title="No data for this period." [compact]="true" />
            }
            @if (section.metrics.length) {
              <ul class="hd-metrics">
                @for (metric of section.metrics; track $index) {
                  <li>
                    <span>{{metric.label}}: {{metric.count}}</span>&ngsp;
                    @if (metric.minutes !== null) {
                      <app-ui-badge variant="neutral">{{metric.minutes}} minutes</app-ui-badge>
                    }
                  </li>
                }
              </ul>
            }
            @if (section.link) {
              <a appUiButton variant="outline" size="sm" class="hd-card-link" [routerLink]="section.link">Open related view</a>
            }
          </app-ui-card>
        }
      </div>
    }
  </main>`
})
export class HrDashboardPageComponent implements OnInit {
  private readonly api = inject(HrDashboardService);
  private readonly auth = inject(AuthService);
  private readonly capability = inject(HrPayrollCapabilityStore);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly summary = signal<HrDashboardSummary | null>(null);
  private readonly today = new Date().toISOString().slice(0, 10);
  to = this.today;
  from = this.daysBefore(30);
  locationProjectionId: number | null = null;
  locationType: string | null = null;

  ngOnInit() { this.search(); }

  search() {
    if (!this.allowed()) return;
    this.busy.set(true);
    this.error.set(null);
    this.api.query(this.from, this.to, this.locationProjectionId, this.locationType).subscribe({
      next: result => { this.summary.set(result); this.busy.set(false); },
      error: error => {
        this.busy.set(false);
        this.error.set(error.error?.detail ?? 'Unable to load the authorized HR dashboard.');
      }
    });
  }

  kpis(data: HrDashboardSummary) {
    return [
      { label: 'Total employees', value: data.totalEmployees, link: '/hr/employees', linkLabel: 'View employees' },
      { label: 'Joiners', value: data.joiners, link: null, linkLabel: '' },
      { label: 'Exits', value: data.exits, link: null, linkLabel: '' },
      { label: 'Documents expiring', value: data.documentsExpiring, link: null, linkLabel: '' },
      { label: 'Open exit cases', value: data.openExitCases, link: '/hr/exits', linkLabel: 'View exits' },
      {
        label: 'Pending mandatory clearance', value: data.pendingMandatoryClearanceItems,
        link: null, linkLabel: ''
      }
    ];
  }

  sections(data: HrDashboardSummary) {
    return [
      { title: 'Employment status', metrics: data.employmentStatuses, link: '/hr/employees' },
      { title: 'Employment type', metrics: data.employmentTypes, link: '/hr/employees' },
      { title: 'Employees by location type', metrics: data.locationTypes, link: '/hr/operational-locations' },
      { title: 'Employees by location', metrics: data.locations, link: '/hr/employees' },
      { title: 'Employees by organisation unit', metrics: data.orgUnits, link: '/hr/reports' },
      { title: 'Employees by position', metrics: data.positions, link: '/hr/employees' },
      { title: 'Processed attendance', metrics: data.attendance, link: '/hr/attendance/reports' },
      { title: 'Leave requests', metrics: data.leaveRequests, link: '/hr/leave' },
      { title: 'Overtime', metrics: data.overtime, link: '/hr/overtime' }
    ];
  }

  private allowed() {
    if (!this.capability.isEnabled('hr.enabled') || !this.auth.hasPermission('hr.dashboard.view')) {
      this.error.set('HR capability or dashboard permission unavailable.');
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
