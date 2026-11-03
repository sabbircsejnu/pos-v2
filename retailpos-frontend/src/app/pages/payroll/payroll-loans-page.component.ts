import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { UI_KIT, UiBadgeVariant, UiTableColumn } from '../../components/shared/ui';
import { AuthService } from '../../services/auth.service';
import {
  PayrollEmployeeLoan,
  PayrollLoanScheduleLineInput,
  PayrollSalarySetupService
} from '../../services/payroll-salary-setup.service';

@Component({
  selector: 'app-payroll-loans-page',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  styles: [`
    .pl-page { display: flex; flex-direction: column; gap: var(--rp-space-4); }
    .pl-grid { display: grid; gap: var(--rp-form-gap); grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr)); }
    .pl-grid--inline { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--rp-form-gap); }
    .pl-grid--inline > app-ui-form-field { flex: 1 1 14rem; max-width: 24rem; }
    .pl-wide { grid-column: 1 / -1; }
    .pl-form-actions { display: flex; flex-wrap: wrap; align-items: center; gap: var(--rp-space-2); }
    .pl-actions { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); }
    .pl-facts { display: grid; gap: var(--rp-space-3) var(--rp-space-5); grid-template-columns: repeat(auto-fit, minmax(13rem, 1fr)); margin: 0 0 var(--rp-space-4); }
    .pl-facts > div { color: var(--rp-color-text-secondary); font-size: var(--rp-text-body-size); line-height: var(--rp-text-body-line); }
    .pl-facts strong { color: var(--rp-color-text-primary); font-weight: var(--rp-font-weight-semibold); }
    .pl-section { margin-top: var(--rp-space-4); padding-top: var(--rp-space-4); border-top: 1px solid var(--rp-color-border); }
    .pl-subtitle { margin: 0 0 var(--rp-space-3); font-size: var(--rp-text-label-size); font-weight: var(--rp-font-weight-semibold); line-height: var(--rp-text-label-line); }
    .pl-steps { margin: 0 0 var(--rp-space-4); padding-left: var(--rp-space-5); color: var(--rp-color-text-secondary); }
    .pl-history summary { cursor: pointer; font-weight: var(--rp-font-weight-medium); border-radius: var(--rp-radius-sm); }
    .pl-history summary:focus-visible { outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring); outline-offset: var(--rp-focus-ring-offset); }
    .pl-history-body { display: flex; flex-direction: column; gap: var(--rp-space-4); margin-top: var(--rp-space-3); }
    @media (max-width: 639.98px) {
      .pl-form-actions > button, .pl-grid--inline > button { flex: 1 1 100%; }
      .pl-grid--inline > app-ui-form-field { max-width: none; }
    }
  `],
  template: `
    <main class="mx-auto max-w-7xl px-4 py-4">
      <app-ui-page-header title="Employee Loans &amp; Advances"
        description="Confidential loan records are limited to your authorized Business and location scope." />

      <div class="pl-page">
        @if (error()) {
          <app-ui-alert variant="danger">{{ error() }}</app-ui-alert>
        }
        @if (busy()) {
          <app-ui-state kind="loading" title="Loading / saving..." [compact]="true" />
        }
        @if (!canView()) {
          <app-ui-alert variant="warning">You do not have employee loan view access.</app-ui-alert>
        }

        @if (canView()) {
          <form (ngSubmit)="loadLoans()">
            <app-ui-card title="Loan history">
              <div class="pl-grid--inline">
                <app-ui-form-field label="Employee ID" [required]="true">
                  <input appUiControl name="employeeId" type="number" min="1" required [(ngModel)]="employeeId">
                </app-ui-form-field>
                <button appUiButton type="submit" [disabled]="busy()">Load loan history</button>
              </div>
            </app-ui-card>
          </form>
          @if (loaded() && loans().length === 0) {
            <app-ui-state kind="empty" title="No loans or advances were found for this employee." />
          }

          @if (canManage()) {
            <form (ngSubmit)="createLoan()">
              <app-ui-card title="Create loan or advance request">
                <div class="pl-grid">
                  <app-ui-form-field label="Employee ID" [required]="true">
                    <input appUiControl name="newLoanEmployeeId" type="number" min="1" required [(ngModel)]="newEmployeeId">
                  </app-ui-form-field>
                  <app-ui-form-field label="Type" [required]="true">
                    <select appUiControl name="loanType" required [(ngModel)]="loanType">
                      <option value="Loan">Loan</option>
                      <option value="Advance">Advance</option>
                    </select>
                  </app-ui-form-field>
                  <app-ui-form-field label="Principal amount" [required]="true">
                    <input appUiControl name="principalAmount" type="number" min="0.0001" step="0.0001" required
                      [(ngModel)]="principalAmount">
                  </app-ui-form-field>
                  <app-ui-form-field label="Recovery starts" [required]="true">
                    <input appUiControl name="recoveryStartDate" type="date" appCompanyDate required [(ngModel)]="recoveryStartDate">
                  </app-ui-form-field>
                  <app-ui-form-field label="Recovery mode" [required]="true">
                    <select appUiControl name="recoveryMode" required [(ngModel)]="recoveryMode">
                      <option value="FixedMonthly">Fixed monthly</option>
                      <option value="Manual">Manual repayment</option>
                      <option value="CustomSchedule">Custom schedule</option>
                    </select>
                  </app-ui-form-field>
                  @if (recoveryMode === 'FixedMonthly') {
                    <app-ui-form-field label="Monthly installment" [required]="true">
                      <input appUiControl name="fixedInstallment" type="number" min="0.0001" step="0.0001" required
                        [(ngModel)]="fixedInstallment">
                    </app-ui-form-field>
                  }
                  @if (recoveryMode === 'CustomSchedule') {
                    <app-ui-form-field class="pl-wide" label="Custom schedule (one ISO date,amount per line)">
                      <textarea appUiControl name="customSchedule" rows="4" [(ngModel)]="customScheduleText"
                        placeholder="2026-11-01,500"></textarea>
                    </app-ui-form-field>
                  }
                  <app-ui-form-field label="Reason" [required]="true">
                    <input appUiControl name="loanReason" required maxlength="1000" [(ngModel)]="reason">
                  </app-ui-form-field>
                  <app-ui-form-field label="Reference">
                    <input appUiControl name="loanReference" maxlength="150" [(ngModel)]="reference">
                  </app-ui-form-field>
                </div>
                <div uiCardFooter class="pl-form-actions">
                  <button appUiButton type="submit" [disabled]="busy()">Create approval request</button>
                </div>
              </app-ui-card>
            </form>
          }
          @if (!canManage()) {
            <app-ui-alert variant="info">Loan creation, schedule changes and repayment recording require loan manage permission.</app-ui-alert>
          }

          @for (loan of loans(); track loan.id) {
            <app-ui-card [title]="loan.loanType + ' #' + loan.id + ' — ' + loan.status">
              <app-ui-badge uiCardActions [variant]="statusVariant(loan.status)">{{ loan.status }}</app-ui-badge>
              <div class="pl-facts">
                <div>Employee #{{ loan.employeeId }}</div>
                <div>Principal: <strong>{{ loan.principalAmount }}</strong></div>
                <div>Approved: <strong>{{ loan.approvedAmount ?? '—' }}</strong></div>
                <div>Outstanding: <strong>{{ loan.outstandingAmount }}</strong></div>
                <div>Disbursed: <strong>{{ (loan.disbursedDate | appDate) || '—' }}</strong></div>
                <div>Recovery: <strong>{{ loan.recoveryMode }}</strong> from {{ (loan.recoveryStartDate | appDate) }}</div>
                <div class="pl-wide">{{ loan.reason }} @if (loan.reference) {<span>({{ loan.reference }})</span>}</div>
                @if (loan.decisionReason) {
                  <div class="pl-wide">Decision: {{ loan.decisionReason }}</div>
                }
              </div>
              @if (loan.approvalSteps.length) {
                <ul class="pl-steps">
                  @for (step of loan.approvalSteps; track $index) {
                    <li>Approval step {{ step.sequence }}: {{ step.decision }}</li>
                  }
                </ul>
              }
              <div class="pl-actions">
                @if (loan.status === 'Draft' && canManage()) {
                  <button appUiButton type="button" [disabled]="busy()" (click)="submitLoan(loan)">Submit for approval</button>
                }
                @if (loan.status === 'PendingApproval' && canApprove()) {
                  <button appUiButton type="button" [disabled]="busy()" (click)="decideLoan(loan, 'approve')">Approve &amp; disburse</button>
                  <button appUiButton variant="danger" type="button" [disabled]="busy()" (click)="decideLoan(loan, 'reject')">Reject</button>
                }
                @if (loan.status === 'PendingApproval' && canManage()) {
                  <button appUiButton variant="outline" type="button" [disabled]="busy()" (click)="decideLoan(loan, 'cancel')">Cancel request</button>
                }
              </div>

              @if (loan.status === 'Active' && canManage()) {
                <form (ngSubmit)="recordRepayment(loan)" class="pl-section">
                  <h3 class="pl-subtitle">Record partial repayment or early settlement</h3>
                  <div class="pl-grid">
                    <app-ui-form-field label="Amount" [required]="true">
                      <input appUiControl name="repaymentAmount{{loan.id}}" type="number" min="0.0001" step="0.0001" required
                        [(ngModel)]="repaymentAmount">
                    </app-ui-form-field>
                    <app-ui-form-field label="Payment date" [required]="true">
                      <input appUiControl name="repaymentDate{{loan.id}}" type="date" appCompanyDate required [(ngModel)]="paymentDate">
                    </app-ui-form-field>
                    <app-ui-form-field label="Reason" [required]="true">
                      <input appUiControl name="repaymentReason{{loan.id}}" maxlength="1000" required [(ngModel)]="repaymentReason">
                    </app-ui-form-field>
                    <app-ui-form-field label="Reference">
                      <input appUiControl name="repaymentReference{{loan.id}}" maxlength="150" [(ngModel)]="repaymentReference">
                    </app-ui-form-field>
                  </div>
                  <div class="pl-form-actions pl-section">
                    <button appUiButton type="submit" [disabled]="busy()">Record repayment</button>
                  </div>
                </form>
              }

              @if (loan.scheduleHistory.length || loan.repayments.length) {
                <details class="pl-history pl-section">
                  <summary>Schedule and repayment history</summary>
                  <div class="pl-history-body">
                    @for (version of loan.scheduleHistory; track version.id) {
                      <div>
                        <h4 class="pl-subtitle">Schedule v{{ version.versionNo }} ({{ version.status }}) — {{ version.recoveryMode }}</h4>
                        <app-ui-data-table
                          [ariaLabel]="'Schedule v' + version.versionNo + ' lines for loan ' + loan.id"
                          density="compact"
                          [columns]="scheduleColumns"
                          [rows]="version.lines"
                          rowKey="installmentNo"
                          [searchable]="false"
                          [paginated]="false"
                          emptyTitle="No schedule lines." />
                      </div>
                    }
                    @if (loan.repayments.length) {
                      <app-ui-data-table
                        [ariaLabel]="'Repayments for loan ' + loan.id"
                        density="compact"
                        [columns]="repaymentColumns"
                        [rows]="loan.repayments"
                        rowKey="id"
                        [searchable]="false"
                        [paginated]="false"
                        emptyTitle="No repayments recorded." />
                    }
                  </div>
                </details>
              }
            </app-ui-card>
          }

          @if (canManage() && hasActiveLoans()) {
            <form (ngSubmit)="updateSchedule()">
              <app-ui-card title="Update recovery schedule">
                <div class="pl-grid">
                  <app-ui-form-field label="Active loan ID" [required]="true">
                    <input appUiControl name="scheduleLoanId" type="number" min="1" required [(ngModel)]="scheduleLoanId">
                  </app-ui-form-field>
                  <app-ui-form-field label="Recovery starts" [required]="true">
                    <input appUiControl name="scheduleStartDate" type="date" appCompanyDate required [(ngModel)]="scheduleStartDate">
                  </app-ui-form-field>
                  <app-ui-form-field label="Recovery mode" [required]="true">
                    <select appUiControl name="scheduleMode" required [(ngModel)]="scheduleMode">
                      <option value="FixedMonthly">Fixed monthly</option>
                      <option value="Manual">Manual repayment</option>
                      <option value="CustomSchedule">Custom schedule</option>
                    </select>
                  </app-ui-form-field>
                  @if (scheduleMode === 'FixedMonthly') {
                    <app-ui-form-field label="Monthly installment" [required]="true">
                      <input appUiControl name="scheduleFixedInstallment" type="number" min="0.0001" step="0.0001" required
                        [(ngModel)]="scheduleFixedInstallment">
                    </app-ui-form-field>
                  }
                  @if (scheduleMode === 'CustomSchedule') {
                    <app-ui-form-field class="pl-wide" label="Custom schedule (one ISO date,amount per line)">
                      <textarea appUiControl name="scheduleCustomText" rows="4" [(ngModel)]="scheduleCustomText"></textarea>
                    </app-ui-form-field>
                  }
                </div>
                <div uiCardFooter class="pl-form-actions">
                  <button appUiButton type="submit" [disabled]="busy()">Save schedule version</button>
                </div>
              </app-ui-card>
            </form>
          }
        }
      </div>
    </main>
  `
})
export class PayrollLoansPageComponent {
  readonly scheduleColumns: UiTableColumn[] = [
    { key: 'dueDate', type: 'date', header: 'Due date' },
    { key: 'plannedAmount', header: 'Planned', align: 'end' },
    { key: 'actualRecoveredAmount', header: 'Recovered', align: 'end' },
    { key: 'status', header: 'Status' }
  ];
  readonly repaymentColumns: UiTableColumn[] = [
    { key: 'paymentDate', type: 'date', header: 'Payment date' },
    { key: 'amount', header: 'Amount', align: 'end' },
    { key: 'reason', header: 'Reason' }
  ];

  statusVariant(status: string): UiBadgeVariant {
    switch (status) {
      case 'Active': return 'success';
      case 'PendingApproval': return 'warning';
      case 'Rejected': return 'danger';
      case 'Settled': return 'info';
      default: return 'neutral';
    }
  }

  private readonly service = inject(PayrollSalarySetupService);
  private readonly auth = inject(AuthService);
  readonly loans = signal<PayrollEmployeeLoan[]>([]);
  readonly loaded = signal(false);
  readonly error = signal('');
  readonly busy = signal(false);
  employeeId: number | null = null;
  newEmployeeId: number | null = null;
  loanType: 'Loan' | 'Advance' = 'Loan';
  principalAmount: number | null = null;
  recoveryStartDate = new Date().toISOString().slice(0, 10);
  recoveryMode: 'FixedMonthly' | 'Manual' | 'CustomSchedule' = 'FixedMonthly';
  fixedInstallment: number | null = null;
  customScheduleText = '';
  reason = '';
  reference = '';
  repaymentAmount: number | null = null;
  paymentDate = new Date().toISOString().slice(0, 10);
  repaymentReason = '';
  repaymentReference = '';
  scheduleLoanId: number | null = null;
  scheduleStartDate = new Date().toISOString().slice(0, 10);
  scheduleMode: 'FixedMonthly' | 'Manual' | 'CustomSchedule' = 'FixedMonthly';
  scheduleFixedInstallment: number | null = null;
  scheduleCustomText = '';
  decisionReason = '';

  canView(): boolean {
    return this.auth.hasPermission('payroll.loan.view');
  }

  canManage(): boolean {
    return this.auth.hasPermission('payroll.loan.manage');
  }

  canApprove(): boolean {
    return this.auth.hasPermission('payroll.loan.approve');
  }

  activeLoan(): PayrollEmployeeLoan | undefined {
    return this.loans().find(loan => loan.id === this.scheduleLoanId && loan.status === 'Active');
  }

  hasActiveLoans(): boolean {
    return this.loans().some(loan => loan.status === 'Active');
  }

  loadLoans(): void {
    if (!this.employeeId || this.employeeId <= 0) {
      this.error.set('Enter a valid employee ID.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.service.employeeLoans(this.employeeId).subscribe({
      next: rows => {
        this.loans.set(rows);
        this.loaded.set(true);
        this.busy.set(false);
      },
      error: () => {
        this.error.set('Unable to load loan history. Verify access and try again.');
        this.busy.set(false);
      }
    });
  }

  createLoan(): void {
    if (!this.newEmployeeId || !this.principalAmount) {
      this.error.set('Employee ID and a positive principal amount are required.');
      return;
    }
    let schedule: PayrollLoanScheduleLineInput[];
    try {
      schedule = this.recoveryMode === 'CustomSchedule'
        ? this.parseSchedule(this.customScheduleText)
        : [];
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Custom schedule is invalid.');
      return;
    }
    this.run(() => this.service.createEmployeeLoan({
      employeeId: this.newEmployeeId!,
      loanType: this.loanType,
      principalAmount: this.principalAmount!,
      recoveryStartDate: this.recoveryStartDate,
      recoveryMode: this.recoveryMode,
      fixedInstallment: this.recoveryMode === 'FixedMonthly' ? this.fixedInstallment : null,
      schedule,
      reason: this.reason,
      reference: this.reference || null,
      idempotencyKey: crypto.randomUUID()
    }), loan => {
      this.newEmployeeId = null;
      this.principalAmount = null;
      this.reason = '';
      this.reference = '';
      this.refreshEmployee(loan.employeeId);
    });
  }

  submitLoan(loan: PayrollEmployeeLoan): void {
    this.run(() => this.service.submitEmployeeLoan(loan), () => this.refreshEmployee(loan.employeeId));
  }

  decideLoan(loan: PayrollEmployeeLoan, decision: 'approve' | 'reject' | 'cancel'): void {
    this.run(() => this.service.decideEmployeeLoan(loan, decision, this.decisionReason),
      () => this.refreshEmployee(loan.employeeId));
  }

  recordRepayment(loan: PayrollEmployeeLoan): void {
    if (!this.repaymentAmount || this.repaymentAmount <= 0 || !this.repaymentReason.trim()) {
      this.error.set('A positive repayment amount and reason are required.');
      return;
    }
    this.run(() => this.service.recordEmployeeLoanRepayment(loan, {
      amount: this.repaymentAmount!,
      paymentDate: this.paymentDate,
      reason: this.repaymentReason,
      reference: this.repaymentReference || null,
      idempotencyKey: crypto.randomUUID()
    }), () => {
      this.repaymentAmount = null;
      this.repaymentReason = '';
      this.repaymentReference = '';
      this.refreshEmployee(loan.employeeId);
    });
  }

  updateSchedule(): void {
    const loan = this.activeLoan();
    if (!loan) {
      this.error.set('Select an active loan from the loaded history.');
      return;
    }
    let schedule: PayrollLoanScheduleLineInput[];
    try {
      schedule = this.scheduleMode === 'CustomSchedule'
        ? this.parseSchedule(this.scheduleCustomText)
        : [];
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Custom schedule is invalid.');
      return;
    }
    this.run(() => this.service.updateEmployeeLoanSchedule(loan, {
      recoveryStartDate: this.scheduleStartDate,
      recoveryMode: this.scheduleMode,
      fixedInstallment: this.scheduleMode === 'FixedMonthly' ? this.scheduleFixedInstallment : null,
      schedule,
      idempotencyKey: crypto.randomUUID()
    }), () => this.refreshEmployee(loan.employeeId));
  }

  private refreshEmployee(employeeId: number): void {
    this.employeeId = employeeId;
    this.loadLoans();
  }

  private parseSchedule(value: string): PayrollLoanScheduleLineInput[] {
    const lines = value.split(/\r?\n/).map(line => line.trim()).filter(Boolean);
    if (!lines.length) {
      throw new Error('Enter at least one schedule line as YYYY-MM-DD,amount.');
    }
    return lines.map((line, index) => {
      const [dueDate, amount, extra] = line.split(',').map(part => part.trim());
      if (extra !== undefined || !/^\d{4}-\d{2}-\d{2}$/.test(dueDate)
          || !Number.isFinite(Number(amount)) || Number(amount) <= 0) {
        throw new Error(`Schedule line ${index + 1} must be YYYY-MM-DD,positive amount.`);
      }
      return { dueDate, plannedAmount: Number(amount) };
    });
  }

  private run<T>(
    request: () => Observable<T>,
    success: (value: T) => void
  ): void {
    this.busy.set(true);
    this.error.set('');
    request().subscribe({
      next: value => {
        this.busy.set(false);
        success(value);
      },
      error: () => {
        this.error.set('The loan operation failed. Verify the request and your access, then try again.');
        this.busy.set(false);
      }
    });
  }
}
