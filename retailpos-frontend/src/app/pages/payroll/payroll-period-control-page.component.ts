import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { UI_KIT, UiBadgeVariant, UiTableColumn } from '../../components/shared/ui';
import { AuthService } from '../../services/auth.service';
import {
  PayrollPeriod,
  PayrollPeriodCloseResponse,
  PayrollPeriodService
} from '../../services/payroll-period.service';
import {
  PayrollRunCorrectionService,
  PayrollSpecialRunResponse
} from '../../services/payroll-run-correction.service';

@Component({
  selector: 'app-payroll-period-control-page',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  styles: [`
    :host {
      display: block;
    }

    .pc-page {
      display: flex;
      flex-direction: column;
      gap: var(--rp-space-4);
    }

    .pc-load {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: var(--rp-space-3);
    }

    .pc-load app-ui-form-field {
      flex: 1 1 12rem;
      max-width: 20rem;
    }

    .pc-summary {
      margin: 0;
      color: var(--rp-color-text-secondary);
    }

    .pc-stack {
      display: flex;
      flex-direction: column;
      gap: var(--rp-space-4);
    }

    .pc-fields {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
      gap: var(--rp-space-4);
      align-items: start;
      max-width: 48rem;
    }

    .pc-actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--rp-space-3);
    }

    .pc-reason {
      display: block;
      max-width: 32rem;
    }
  `],
  template: `
    <main class="container mx-auto px-4 py-3">
      <app-ui-page-header
        title="Payroll Period Control"
        description="Lock, reopen, and assess close gates for a Payroll period."
        [breadcrumbs]="breadcrumbs" />

      <div class="pc-page">
        @if (error()) {
          <app-ui-alert variant="danger">{{ error() }}</app-ui-alert>
        }
        @if (busy()) {
          <app-ui-state kind="loading" [compact]="true" title="Loading Payroll period..." />
        }

        <app-ui-card title="Find a period">
          <form class="pc-load" (ngSubmit)="load()">
            <app-ui-form-field label="Payroll period ID" [required]="true">
              <input appUiControl name="periodId" type="number" min="1" required [(ngModel)]="periodId">
            </app-ui-form-field>
            <button appUiButton type="submit" [disabled]="busy()">Load</button>
          </form>
        </app-ui-card>

        @if (period(); as p) {
          <app-ui-card [title]="p.periodCode">
            <div class="pc-stack">
              <p class="pc-summary">{{ (p.periodStart | appDate) }} – {{ (p.periodEnd | appDate) }} ·
                <app-ui-badge [variant]="periodStatusVariant(p.status)">{{ p.status }}</app-ui-badge>
                · Version {{ p.version }}</p>
              @if (canReopen()) {
                <app-ui-form-field label="Reason" class="pc-reason">
                  <input appUiControl name="reason" [(ngModel)]="reason" maxlength="1000">
                </app-ui-form-field>
              }
              <div class="pc-actions">
                @if (p.status === 'Processing' && canLock()) {
                  <button appUiButton type="button" [disabled]="busy()" (click)="lock()">Lock period</button>
                }
                @if (p.status === 'Processing' && canReopen()) {
                  <button appUiButton variant="outline" type="button" [disabled]="busy() || !reason.trim()" (click)="returnToOpen()">
                    Return unused period to Open</button>
                }
                @if ((p.status === 'Locked' || p.status === 'Approved') && canReopen()) {
                  <button appUiButton variant="outline" type="button" [disabled]="busy() || !reason.trim()" (click)="reopen()">Reopen period</button>
                }
                @if (p.status === 'Approved' && canClose()) {
                  <button appUiButton type="button" [disabled]="busy() || !reason.trim()" (click)="close()">Assess and close</button>
                }
              </div>
            </div>
          </app-ui-card>

          @if (canCreateCorrection() && canCorrectPeriod()) {
            <app-ui-card title="Payroll correction" subtitle="Create a linked correction run; the approved run remains unchanged.">
              <div class="pc-stack">
                <div class="pc-fields">
                  <app-ui-form-field label="Original run ID">
                    <input appUiControl name="originalRunId" type="number" min="1" [(ngModel)]="originalRunId">
                  </app-ui-form-field>
                  <app-ui-form-field label="Original run version">
                    <input appUiControl name="originalRunVersion" type="number" min="1" [(ngModel)]="originalRunVersion">
                  </app-ui-form-field>
                  <app-ui-form-field label="Correction type">
                    <select appUiControl name="correctionType" [(ngModel)]="correctionType">
                      <option value="SalaryInput">Salary input</option>
                      <option value="Attendance">Attendance</option>
                      <option value="Leave">Leave</option>
                      <option value="Overtime">Overtime</option>
                      <option value="ManualAdjustment">Manual adjustment</option>
                      <option value="LoanRecovery">Loan recovery</option>
                      <option value="Recalculation">Recalculation</option>
                    </select>
                  </app-ui-form-field>
                  <app-ui-form-field label="Affected employee IDs (comma-separated)">
                    <input appUiControl name="correctionEmployees" [(ngModel)]="employeeIdsText">
                  </app-ui-form-field>
                  <app-ui-form-field label="Correction reference">
                    <input appUiControl name="correctionReference" [(ngModel)]="correctionReference">
                  </app-ui-form-field>
                  <app-ui-form-field label="Correction reason">
                    <input appUiControl name="correctionReason" [(ngModel)]="correctionReason" maxlength="1000">
                  </app-ui-form-field>
                </div>
                <div class="pc-actions">
                  <button appUiButton type="button" [disabled]="busy() || !correctionReady()"
                    (click)="createCorrection()">Create correction run</button>
                </div>
              </div>
            </app-ui-card>
          }
          @if (canCreateOffCycle() && p.status === 'Processing') {
            <app-ui-card title="Off-Cycle Payroll">
              <div class="pc-stack">
                <div class="pc-fields">
                  <app-ui-form-field label="Off-Cycle reference">
                    <input appUiControl name="offCycleReference" [(ngModel)]="offCycleReference">
                  </app-ui-form-field>
                  <app-ui-form-field label="Off-Cycle reason">
                    <input appUiControl name="offCycleReason" [(ngModel)]="offCycleReason" maxlength="1000">
                  </app-ui-form-field>
                </div>
                <div class="pc-actions">
                  <button appUiButton type="button" [disabled]="busy() || !offCycleReady()"
                    (click)="createOffCycle()">Create Off-Cycle run</button>
                </div>
              </div>
            </app-ui-card>
          }
          @if (specialRun(); as run) {
            <app-ui-alert variant="info">
              {{ run.runType }} run {{ run.id }} created as
              {{ run.status }} (version {{ run.version }}).
              @if (run.downstreamReversalRequired) {
                <span>
                  Downstream payment/Accounting correction is required separately.
                </span>
              }
            </app-ui-alert>
          }
          @if (closeResult(); as result) {
            <app-ui-card [title]="result.closed ? 'Period closed' : 'Close is blocked'">
              <div aria-live="polite">
                <app-ui-data-table
                  ariaLabel="Close gates"
                  density="compact"
                  rowKey="code"
                  [columns]="gateColumns"
                  [rows]="result.gates"
                  [searchable]="false"
                  [paginated]="false"
                  emptyTitle="No close gates were returned.">
                  <ng-template uiTableCell="result" let-gate>
                    <app-ui-badge [variant]="gate.passed ? 'success' : 'danger'">{{ gate.passed ? 'Passed' : 'Blocked' }}</app-ui-badge>
                  </ng-template>
                </app-ui-data-table>
              </div>
            </app-ui-card>
          }
        }
      </div>
    </main>
  `
})
export class PayrollPeriodControlPageComponent {
  private readonly auth = inject(AuthService);
  private readonly service = inject(PayrollPeriodService);
  private readonly correctionService = inject(PayrollRunCorrectionService);

  readonly breadcrumbs = [{ label: 'Payroll' }, { label: 'Period control' }];
  readonly gateColumns: UiTableColumn[] = [
    { key: 'result', header: 'Result', type: 'custom' },
    { key: 'code', header: 'Gate', sortable: true },
    { key: 'message', header: 'Message' }
  ];

  readonly period = signal<PayrollPeriod | null>(null);
  readonly closeResult = signal<PayrollPeriodCloseResponse | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly specialRun = signal<PayrollSpecialRunResponse | null>(null);
  periodId: number | null = null;
  reason = '';
  originalRunId: number | null = null;
  originalRunVersion: number | null = null;
  employeeIdsText = '';
  correctionReference = '';
  correctionType = 'Recalculation';
  correctionReason = '';
  offCycleReference = '';
  offCycleReason = '';

  periodStatusVariant(status: string): UiBadgeVariant {
    switch (status) {
      case 'Approved':
      case 'Posted':
        return 'success';
      case 'Closed':
        return 'info';
      case 'Processing':
      case 'Locked':
        return 'warning';
      default:
        return 'neutral';
    }
  }

  canLock(): boolean {
    return this.auth.hasPermission('payroll.period.lock');
  }

  canReopen(): boolean {
    return this.auth.hasPermission('payroll.period.reopen');
  }

  canClose(): boolean {
    return this.auth.hasPermission('payroll.period.close');
  }

  canCreateCorrection(): boolean {
    return this.auth.hasPermission('payroll.run.reopen')
      && this.auth.hasPermission('payroll.run.create');
  }

  canCreateOffCycle(): boolean {
    return this.auth.hasPermission('payroll.run.create');
  }

  canCorrectPeriod(): boolean {
    return ['Locked', 'Approved', 'Posted', 'Closed'].includes(this.period()?.status ?? '');
  }

  correctionReady(): boolean {
    return this.originalRunId !== null && this.originalRunId > 0
      && this.originalRunVersion !== null && this.originalRunVersion > 0
      && this.employeeIds() !== null && !!this.correctionReference.trim()
      && !!this.correctionReason.trim();
  }

  offCycleReady(): boolean {
    return this.period()?.status === 'Processing' && this.employeeIds() !== null
      && !!this.offCycleReference.trim()
      && !!this.offCycleReason.trim();
  }

  load(): void {
    if (!this.periodId || this.periodId <= 0) {
      this.error.set('Enter a valid Payroll period ID.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.closeResult.set(null);
    this.service.get(this.periodId).subscribe({
      next: period => {
        this.period.set(period);
        this.busy.set(false);
      },
      error: () => {
        this.error.set('Unable to load this Payroll period.');
        this.period.set(null);
        this.busy.set(false);
      }
    });
  }

  lock(): void {
    const period = this.period();
    if (!period || !this.canLock() || period.status !== 'Processing') return;
    this.runAction(this.service.lock(period.id, period.version));
  }

  reopen(): void {
    const period = this.period();
    if (!period || !this.canReopen() || !this.reason.trim()) return;
    this.runAction(this.service.reopen(period.id, period.version, this.reason.trim()));
  }

  returnToOpen(): void {
    const period = this.period();
    if (!period || period.status !== 'Processing' || !this.canReopen() || !this.reason.trim()) return;
    this.runAction(this.service.returnToOpen(period.id, period.version, this.reason.trim()));
  }

  close(): void {
    const period = this.period();
    if (!period || !this.canClose() || !this.reason.trim() || period.status !== 'Approved') return;
    this.busy.set(true);
    this.error.set('');
    this.service.close(period.id, {
      idempotencyKey: crypto.randomUUID(),
      reason: this.reason.trim(),
      expectedVersion: period.version,
      policyVersion: 1,
      exceptions: []
    }).subscribe({
      next: result => {
        this.closeResult.set(result);
        this.load();
      },
      error: (response: { error?: unknown }) => {
        if (this.isCloseResponse(response.error)) this.closeResult.set(response.error);
        else this.error.set('Close assessment failed. Reload the period and retry.');
        this.busy.set(false);
      }
    });
  }

  createCorrection(): void {
    const period = this.period();
    if (!period || !this.canCreateCorrection() || !this.correctionReady()
      || !this.canCorrectPeriod()) return;
    this.runSpecialAction(this.correctionService.createCorrection(
      period.id,
      this.originalRunId!,
      {
        correctionReference: this.correctionReference.trim(),
        correctionType: this.correctionType,
        reason: this.correctionReason.trim(),
        employeeIds: this.employeeIds()!,
        idempotencyKey: crypto.randomUUID(),
        expectedPeriodVersion: period.version,
        expectedOriginalRunVersion: this.originalRunVersion!
      }
    ));
  }

  createOffCycle(): void {
    const period = this.period();
    if (!period || !this.auth.hasPermission('payroll.run.create')
      || !this.offCycleReady() || period.status !== 'Processing') return;
    this.runSpecialAction(this.correctionService.createOffCycle(period.id, {
      runReference: this.offCycleReference.trim(),
      reason: this.offCycleReason.trim(),
      scopeType: 'Business',
      scopeId: null,
      employeeIds: this.employeeIds()!,
      idempotencyKey: crypto.randomUUID(),
      expectedPeriodVersion: period.version
    }));
  }

  private employeeIds(): number[] | null {
    const tokens = this.employeeIdsText.split(',').map(value => value.trim());
    if (tokens.some(value => !/^[1-9]\d*$/.test(value))) return null;
    const ids = tokens.map(Number);
    if (ids.some(value => !Number.isSafeInteger(value)) || new Set(ids).size !== ids.length)
      return null;
    return ids;
  }

  private runSpecialAction(action: Observable<PayrollSpecialRunResponse>): void {
    this.busy.set(true);
    this.error.set('');
    action.subscribe({
      next: run => {
        this.specialRun.set(run);
        this.busy.set(false);
        this.load();
      },
      error: () => {
        this.error.set('Payroll special run creation failed. Reload the period and retry.');
        this.busy.set(false);
      }
    });
  }

  private runAction(action: Observable<PayrollPeriod>): void {
    this.busy.set(true);
    this.error.set('');
    action.subscribe({
      next: period => {
        this.period.set(period);
        this.busy.set(false);
      },
      error: () => {
        this.error.set('Payroll period action failed. Reload the period and retry.');
        this.busy.set(false);
      }
    });
  }

  private isCloseResponse(value: unknown): value is PayrollPeriodCloseResponse {
    if (typeof value !== 'object' || value === null) return false;
    const result = value as Partial<PayrollPeriodCloseResponse>;
    return typeof result.payrollPeriodId === 'number' && Array.isArray(result.gates);
  }
}
