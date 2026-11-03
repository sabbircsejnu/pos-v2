import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { UI_KIT, UiBadgeVariant, UiBreadcrumb, UiTableColumn } from '../../components/shared/ui';
import { AuthService } from '../../services/auth.service';
import {
  PayrollRunResponse,
  PayrollRunValidationResponse,
  PayrollAccountingMapping,
  PayrollAccountingPosting,
  PayrollRunValidationService
} from '../../services/payroll-run-validation.service';
import {
  PayrollPayslipArtifact,
  PayrollPayslipService
} from '../../services/payroll-payslip.service';
import { PayrollExportService } from '../../services/payroll-export.service';

@Component({
  selector: 'app-payroll-run-validation-page',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  template: `
    <main class="mx-auto max-w-7xl px-4 py-4">
      <app-ui-page-header
        title="Payroll Validation Workbench"
        description="Review blocking errors and warnings before Payroll submission."
        [breadcrumbs]="breadcrumbs" />

      <div class="flex flex-col gap-4">
        @if (error()) {
          <app-ui-alert variant="danger">{{ error() }}</app-ui-alert>
        }
        @if (busy()) {
          <app-ui-state kind="loading" title="Loading validation findings..." [compact]="true" />
        }
        @if (!canView()) {
          <app-ui-alert variant="warning">
            Payroll run and salary view permissions are required to access validation findings.
          </app-ui-alert>
        }

        @if (canView()) {
          <app-ui-card title="Payroll run" subtitle="Enter the Payroll period and run to validate.">
            <form (ngSubmit)="load()" class="grid items-end gap-4 sm:grid-cols-2 lg:grid-cols-3">
              <app-ui-form-field label="Payroll period ID" [required]="true">
                <input appUiControl name="periodId" type="number" min="1" required [(ngModel)]="periodId">
              </app-ui-form-field>
              <app-ui-form-field label="Payroll run ID" [required]="true">
                <input appUiControl name="runId" type="number" min="1" required [(ngModel)]="runId">
              </app-ui-form-field>
              <div>
                <button appUiButton type="submit" [disabled]="busy()">Validate run</button>
              </div>
            </form>
          </app-ui-card>
        }

        @if (result(); as res) {
          <app-ui-card [title]="'Run #' + res.payrollRunId">
            <div class="flex flex-col gap-3">
              <p>Period #{{ res.payrollPeriodId }} · {{ res.runStatus }}
                · Calculation version {{ res.calculationVersion }}</p>
              @if (run(); as currentRun) {
                <p>
                  Run status: <app-ui-badge [variant]="runStatusVariant(currentRun.status)">{{ currentRun.status }}</app-ui-badge>
                  @if (currentRun.approvalInstanceId) {
                    <span> · Approval #{{ currentRun.approvalInstanceId }}</span>
                  }
                  @if (currentRun.submittedAtUtc) {
                    <span> · Submitted {{ currentRun.submittedAtUtc | appDate:'medium' }}</span>
                  }
                  @if (currentRun.decidedAtUtc) {
                    <span> · Decided {{ currentRun.decidedAtUtc | appDate:'medium' }}</span>
                  }
                  @if (currentRun.decisionReason) {
                    <span> · {{ currentRun.decisionReason }}</span>
                  }
                </p>
              }
              <div class="flex flex-wrap items-center gap-2" role="status">
                <app-ui-badge [variant]="res.blockingErrorCount > 0 ? 'danger' : 'success'">{{ res.blockingErrorCount }} blocking errors</app-ui-badge>
                <app-ui-badge [variant]="res.warningCount > 0 ? 'warning' : 'success'">{{ res.warningCount }} warnings</app-ui-badge>
              </div>
              @if (!res.isCalculated) {
                <app-ui-alert variant="info">
                  Calculate this run to validate frozen attendance, shift, and loan-recovery inputs.
                </app-ui-alert>
              }
              @if (res.canSubmit) {
                <app-ui-alert variant="success">Validation is clear for submission.</app-ui-alert>
              } @else {
                <app-ui-alert variant="danger">This run is not ready for submission.</app-ui-alert>
              }
              @if (res.totalFindings === 0) {
                <app-ui-state title="No validation findings were returned." [compact]="true" />
              }
              @if (res.findings.length) {
                <app-ui-data-table
                  [columns]="findingColumns" [rows]="res.findings"
                  [searchable]="false" [paginated]="false" density="compact"
                  ariaLabel="Validation findings">
                  <ng-template uiTableCell="employeeId" let-row>
                    {{ row.employeeId ? 'Employee #' + row.employeeId : '—' }}
                  </ng-template>
                  <ng-template uiTableCell="relatedRunId" let-row>
                    {{ row.relatedRunId ? 'Run #' + row.relatedRunId : '—' }}
                  </ng-template>
                </app-ui-data-table>
              }
              <nav class="flex flex-wrap items-center gap-3" aria-label="Findings pagination">
                <button appUiButton variant="outline" size="sm" type="button"
                  [disabled]="busy() || page() <= 1" (click)="changePage(page() - 1)">Previous</button>
                <span>Page {{ res.page }} · {{ res.totalFindings }} findings</span>
                <button appUiButton variant="outline" size="sm" type="button"
                  [disabled]="busy() || page() * pageSize >= res.totalFindings"
                  (click)="changePage(page() + 1)">Next</button>
              </nav>
              @if ((run()?.status === 'Draft' && res.canSubmit && canSubmit())
                || (run()?.status === 'PendingApproval' && canApprove())) {
                <div class="flex flex-wrap gap-3" role="group" aria-label="Run actions">
                  @if (run()?.status === 'Draft' && res.canSubmit && canSubmit()) {
                    <button appUiButton type="button" [disabled]="busy()" (click)="submit()">Submit for approval</button>
                  }
                  @if (run()?.status === 'PendingApproval' && canApprove()) {
                    <button appUiButton type="button" [disabled]="busy()" (click)="decide('approve')">Approve</button>
                  }
                  @if (run()?.status === 'PendingApproval' && canApprove()) {
                    <button appUiButton variant="danger" type="button" [disabled]="busy()" (click)="decide('reject')">Reject</button>
                  }
                </div>
              }
            </div>
          </app-ui-card>

          @if (isProtectedRun()) {
            <app-ui-card title="Accounting posting" [headingLevel]="3">
              <div class="flex flex-col gap-3">
                @if (accountingPosting(); as posting) {
                  <p role="status">
                    <app-ui-badge [variant]="postingVariant(posting.status)">{{ posting.status }}</app-ui-badge>
                    @if (posting.accountingReference) {
                      <span> · Journal {{ posting.accountingReference }}</span>
                    }
                    @if (posting.postedAtUtc) {
                      <span> · Posted {{ posting.postedAtUtc | appDate:'medium' }}</span>
                    }
                  </p>
                }
                @if (accountingPosting()?.errorMessage) {
                  <app-ui-alert variant="danger">{{ accountingPosting()!.errorMessage }}</app-ui-alert>
                }
                @if (canPostAccounting()) {
                  <div>
                    <button appUiButton type="button"
                      [disabled]="accountingBusy() || accountingPosting()?.status === 'Posted'"
                      (click)="postAccounting()">
                      {{ accountingBusy() ? 'Posting...' : 'Post Payroll expense' }}
                    </button>
                  </div>
                }
                @if (accountingMessage()) {
                  <app-ui-alert variant="info">{{ accountingMessage() }}</app-ui-alert>
                }
                @if (accountingError()) {
                  <app-ui-alert variant="danger">{{ accountingError() }}</app-ui-alert>
                }
              </div>
            </app-ui-card>
          }

          @if (canManageAccountingMappings()) {
            <app-ui-card title="Payroll Accounting mappings" [headingLevel]="3">
              <div class="flex flex-col gap-4">
                <app-ui-data-table
                  [columns]="mappingColumns" [rows]="accountingMappings()"
                  [searchable]="false" [paginated]="false" density="compact"
                  emptyTitle="No Payroll Accounting mappings" ariaLabel="Payroll Accounting mappings" />
                <form (ngSubmit)="saveAccountingMapping()" class="grid items-end gap-4 sm:grid-cols-2 lg:grid-cols-4">
                  <app-ui-form-field label="Component type">
                    <select appUiControl name="mappingType" [(ngModel)]="mappingType">
                      <option value="Earning">Earning</option>
                      <option value="Deduction">Deduction</option>
                      <option value="NetPayable">Net payable</option>
                    </select>
                  </app-ui-form-field>
                  <app-ui-form-field label="Component code" [required]="true">
                    <input appUiControl name="mappingCode" required maxlength="50" [(ngModel)]="mappingCode">
                  </app-ui-form-field>
                  <app-ui-form-field label="Accounting account ID" [required]="true">
                    <input appUiControl name="mappingAccountId" type="number" min="1" required [(ngModel)]="mappingAccountId">
                  </app-ui-form-field>
                  <div>
                    <button appUiButton type="submit" [disabled]="accountingBusy()">Save mapping</button>
                  </div>
                </form>
              </div>
            </app-ui-card>
          }

          @if (canUsePayslips() && isProtectedRun()) {
            <app-ui-card title="Confidential Payslip" subtitle="Generate only from this approved, protected Payroll result." [headingLevel]="3">
              <div class="flex flex-col gap-3">
                <div class="max-w-sm">
                  <app-ui-form-field label="Employee ID" [required]="true">
                    <input appUiControl name="payslipEmployeeId" type="number" min="1" required
                      [(ngModel)]="payslipEmployeeId">
                  </app-ui-form-field>
                </div>
                <div class="flex flex-wrap gap-3">
                  <button appUiButton type="button" [disabled]="payslipBusy()" (click)="generatePayslip()">
                    {{ payslipBusy() ? 'Working...' : 'Generate / reuse PDF' }}
                  </button>
                  @if (canDownloadPayslip()) {
                    <button appUiButton variant="outline" type="button"
                      [disabled]="payslipBusy() || !payslipArtifact()" (click)="downloadPayslip()">Download PDF</button>
                  }
                  @if (canPrintPayslip()) {
                    <button appUiButton variant="outline" type="button"
                      [disabled]="payslipBusy() || !payslipArtifact()" (click)="printPayslip()">Print</button>
                  }
                  @if (canEmailPayslip()) {
                    <button appUiButton variant="outline" type="button"
                      [disabled]="payslipBusy() || !payslipArtifact()" (click)="emailPayslip()">Email securely</button>
                  }
                </div>
                @if (payslipArtifact(); as artifact) {
                  <p role="status">
                    Version {{ artifact.versionNo }} · <app-ui-badge [variant]="scanVariant(artifact.scanStatus)">{{ artifact.scanStatus }}</app-ui-badge>
                  </p>
                }
                @if (payslipMessage()) {
                  <app-ui-alert variant="info">{{ payslipMessage() }}</app-ui-alert>
                }
                @if (payslipError()) {
                  <app-ui-alert variant="danger">{{ payslipError() }}</app-ui-alert>
                }
              </div>
            </app-ui-card>
          }

          @if (canExport() && isProtectedRun()) {
            <app-ui-card title="Payroll Exports" subtitle="Download the approved run's persisted Payroll results." [headingLevel]="3">
              <div class="flex flex-col gap-3">
                <div class="flex flex-wrap gap-3">
                  <button appUiButton variant="outline" type="button"
                    [disabled]="exportBusy() !== null || busy()" [loading]="exportBusy() === 'csv'"
                    (click)="downloadCsv()">
                    {{ exportBusy() === 'csv' ? 'Preparing CSV...' : 'Export CSV' }}
                  </button>
                  <button appUiButton variant="outline" type="button"
                    [disabled]="exportBusy() !== null || busy()" [loading]="exportBusy() === 'excel'"
                    (click)="downloadExcel()">
                    {{ exportBusy() === 'excel' ? 'Preparing Excel...' : 'Export Excel' }}
                  </button>
                  <button appUiButton variant="outline" type="button"
                    [disabled]="exportBusy() !== null || busy()" [loading]="exportBusy() === 'pdf-summary'"
                    (click)="downloadPdfSummary()">
                    {{ exportBusy() === 'pdf-summary' ? 'Preparing PDF...' : 'Export PDF Summary' }}
                  </button>
                </div>
                @if (exportBusy()) {
                  <p role="status" aria-live="polite">Preparing confidential Payroll export...</p>
                }
                @if (exportMessage()) {
                  <app-ui-alert variant="success">{{ exportMessage() }}</app-ui-alert>
                }
                @if (exportError()) {
                  <app-ui-alert variant="danger">{{ exportError() }}</app-ui-alert>
                }
              </div>
            </app-ui-card>
          }
        }
      </div>
    </main>
  `
})
export class PayrollRunValidationPageComponent {
  private readonly auth = inject(AuthService);
  private readonly service = inject(PayrollRunValidationService);
  private readonly payslipService = inject(PayrollPayslipService);
  private readonly exportService = inject(PayrollExportService);

  readonly result = signal<PayrollRunValidationResponse | null>(null);
  readonly run = signal<PayrollRunResponse | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly page = signal(1);
  readonly pageSize = 50;
  readonly payslipBusy = signal(false);
  readonly payslipArtifact = signal<PayrollPayslipArtifact | null>(null);
  readonly payslipMessage = signal('');
  readonly payslipError = signal('');
  readonly exportBusy = signal<'csv' | 'excel' | 'pdf-summary' | null>(null);
  readonly exportMessage = signal('');
  readonly exportError = signal('');
  readonly accountingPosting = signal<PayrollAccountingPosting | null>(null);
  readonly accountingMappings = signal<PayrollAccountingMapping[]>([]);
  readonly accountingBusy = signal(false);
  readonly accountingMessage = signal('');
  readonly accountingError = signal('');
  payslipEmployeeId: number | null = null;
  periodId: number | null = null;
  runId: number | null = null;
  mappingType = 'Earning';
  mappingCode = '';
  mappingAccountId: number | null = null;

  readonly breadcrumbs: UiBreadcrumb[] = [
    { label: 'Dashboard', link: '/dashboard' },
    { label: 'Payroll' },
    { label: 'Validation workbench' }
  ];
  readonly findingColumns: UiTableColumn[] = [
    {
      key: 'severity', header: 'Severity', type: 'status', sortable: true,
      statusMap: {
        BlockingError: { label: 'BlockingError', variant: 'danger' },
        Warning: { label: 'Warning', variant: 'warning' }
      }
    },
    { key: 'category', header: 'Category', sortable: true },
    { key: 'employeeId', header: 'Employee', type: 'custom', sortable: true },
    { key: 'relatedRunId', header: 'Related run', type: 'custom', sortable: true },
    { key: 'message', header: 'Message' }
  ];
  readonly mappingColumns: UiTableColumn[] = [
    { key: 'componentType', header: 'Component type' },
    { key: 'componentCode', header: 'Component code' },
    { key: 'accountCode', header: 'Account code' },
    { key: 'accountName', header: 'Account name' }
  ];

  runStatusVariant(status: string | null | undefined): UiBadgeVariant {
    switch (status) {
      case 'Approved':
      case 'Paid':
      case 'Completed':
        return 'success';
      case 'PendingApproval':
        return 'warning';
      case 'PaymentInProgress':
        return 'info';
      case 'Rejected':
      case 'Cancelled':
        return 'danger';
      default:
        return 'neutral';
    }
  }

  postingVariant(status: string | null | undefined): UiBadgeVariant {
    switch (status) {
      case 'Posted':
        return 'success';
      case 'Failed':
        return 'danger';
      case 'Pending':
        return 'warning';
      default:
        return 'neutral';
    }
  }

  scanVariant(status: string | null | undefined): UiBadgeVariant {
    switch (status) {
      case 'Clean':
        return 'success';
      case 'Pending':
        return 'warning';
      case 'Infected':
      case 'Failed':
        return 'danger';
      default:
        return 'neutral';
    }
  }
  canView(): boolean {
    return this.auth.hasPermission('payroll.run.view')
      && this.auth.hasPermission('payroll.salary.view');
  }

  canSubmit(): boolean {
    return this.auth.hasPermission('payroll.run.submit');
  }

  canApprove(): boolean {
    return this.auth.hasPermission('payroll.run.approve');
  }

  canUsePayslips(): boolean {
    return this.auth.hasPermission('payroll.payslip.view')
      && this.auth.hasPermission('payroll.salary.view');
  }

  canDownloadPayslip(): boolean {
    return this.auth.hasPermission('payroll.payslip.download');
  }

  canPrintPayslip(): boolean {
    return this.auth.hasPermission('payroll.payslip.print');
  }

  canEmailPayslip(): boolean {
    return this.auth.hasPermission('payroll.payslip.email');
  }

  canExport(): boolean {
    return this.auth.hasPermission('payroll.export')
      && this.auth.hasPermission('payroll.salary.view');
  }

  canPostAccounting(): boolean {
    return this.auth.hasPermission('payroll.accounting.post')
      && this.auth.hasPermission('payroll.salary.view')
      && this.isProtectedRun();
  }

  canManageAccountingMappings(): boolean {
    return this.auth.hasPermission('accounting.configuration.edit');
  }

  isProtectedRun(): boolean {
    return ['Approved', 'PaymentInProgress', 'Paid', 'Completed'].includes(this.run()?.status ?? '');
  }

  generatePayslip(): void {
    if (!this.runId || !this.payslipEmployeeId || this.payslipEmployeeId <= 0
      || !this.canUsePayslips() || !this.isProtectedRun()) {
      this.payslipError.set('An approved run, valid employee ID, and Payslip permissions are required.');
      return;
    }
    this.payslipBusy.set(true);
    this.payslipError.set('');
    this.payslipMessage.set('');
    this.payslipService.generate(this.runId, this.payslipEmployeeId).subscribe({
      next: artifact => {
        this.payslipArtifact.set(artifact);
        this.payslipMessage.set(artifact.isReplay
          ? 'The existing immutable Payslip version was reused.'
          : 'Payslip artifact generated and stored privately.');
        this.payslipBusy.set(false);
      },
      error: () => {
        this.payslipArtifact.set(null);
        this.payslipError.set('Payslip generation failed. Verify access and private storage configuration.');
        this.payslipBusy.set(false);
      }
    });
  }

  downloadPayslip(): void {
    this.readPayslip(false);
  }

  printPayslip(): void {
    if (!this.canReadPayslip(true)) {
      this.payslipError.set('An approved run, artifact, and print permissions are required.');
      return;
    }
    const printWindow = window.open('about:blank', '_blank');
    if (!printWindow) {
      this.payslipError.set('The browser blocked the secure print window.');
      return;
    }
    printWindow.opener = null;
    this.readPayslip(true, printWindow);
  }

  emailPayslip(): void {
    if (!this.runId || !this.payslipEmployeeId || !this.payslipArtifact()
      || !this.auth.hasPermission('payroll.payslip.email')) return;
    this.payslipBusy.set(true);
    this.payslipError.set('');
    this.payslipMessage.set('');
    this.payslipService.email(this.runId, this.payslipEmployeeId, crypto.randomUUID()).subscribe({
      next: response => {
        this.payslipMessage.set(response.status === 'Sent'
          ? `Payslip sent to ${response.recipientMasked}.`
          : `Payslip delivery ${response.status.toLowerCase()}: ${response.failureCode ?? 'delivery not confirmed'}.`);
        this.payslipBusy.set(false);
      },
      error: () => {
        this.payslipError.set('Payslip email request failed. Verify recipient, permission, and delivery configuration.');
        this.payslipBusy.set(false);
      }
    });
  }

  downloadCsv(): void {
    this.downloadExport('csv');
  }

  downloadExcel(): void {
    this.downloadExport('excel');
  }

  downloadPdfSummary(): void {
    this.downloadExport('pdf-summary');
  }

  private downloadExport(type: 'csv' | 'excel' | 'pdf-summary'): void {
    if (!this.runId || !this.canExport() || !this.isProtectedRun()) {
      this.exportError.set('An approved run and Payroll export permissions are required.');
      return;
    }

    const runId = this.runId;
    this.exportBusy.set(type);
    this.exportError.set('');
    this.exportMessage.set('');
    const request = type === 'csv'
      ? this.exportService.csv(runId)
      : type === 'excel'
        ? this.exportService.excel(runId)
        : this.exportService.pdfSummary(runId);
    request.subscribe({
      next: blob => {
        let url: string | null = null;
        try {
          if (blob.size === 0) {
            throw new Error('Empty Payroll export.');
          }
          url = URL.createObjectURL(blob);
          const anchor = document.createElement('a');
          anchor.href = url;
          anchor.download = this.exportFilename(type, runId);
          document.body.appendChild(anchor);
          anchor.click();
          anchor.remove();
          const downloadUrl = url;
          setTimeout(() => URL.revokeObjectURL(downloadUrl), 60_000);
          url = null;
          this.exportMessage.set('Payroll export downloaded securely.');
        } catch {
          if (url) URL.revokeObjectURL(url);
          this.exportError.set('Payroll export download failed. Try again or verify browser download settings.');
        } finally {
          this.exportBusy.set(null);
        }
      },
      error: () => {
        this.exportError.set('Payroll export failed. Verify access and try again.');
        this.exportBusy.set(null);
      }
    });
  }

  private exportFilename(type: 'csv' | 'excel' | 'pdf-summary', runId: number): string {
    const suffix = type === 'pdf-summary' ? 'summary.pdf' : 'register.' + (type === 'excel' ? 'xlsx' : 'csv');
    return `payroll-run-${runId}-${suffix}`;
  }

  private readPayslip(forPrint: boolean, printWindow?: Window): void {
    if (!this.canReadPayslip(forPrint)) {
      printWindow?.close();
      return;
    }
    if (!this.runId || !this.payslipEmployeeId || !this.payslipArtifact()) return;
    this.payslipBusy.set(true);
    this.payslipError.set('');
    const request = forPrint
      ? this.payslipService.print(this.runId, this.payslipEmployeeId)
      : this.payslipService.download(this.runId, this.payslipEmployeeId);
    request.subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        if (forPrint) {
          if (!printWindow) {
            this.payslipError.set('The secure print window is unavailable.');
          } else {
            printWindow.location.replace(url);
          }
        } else {
          const anchor = document.createElement('a');
          anchor.href = url;
          anchor.download = `payslip-${this.runId}-${this.payslipEmployeeId}.pdf`;
          document.body.appendChild(anchor);
          anchor.click();
          anchor.remove();
        }
        setTimeout(() => URL.revokeObjectURL(url), 60_000);
        this.payslipBusy.set(false);
      },
      error: () => {
        printWindow?.close();
        this.payslipError.set(`Payslip ${forPrint ? 'print' : 'download'} failed. Verify access and try again.`);
        this.payslipBusy.set(false);
      }
    });
  }

  private canReadPayslip(forPrint: boolean): boolean {
    return Boolean(this.runId && this.payslipEmployeeId && this.payslipArtifact())
      && this.canUsePayslips()
      && this.isProtectedRun()
      && (forPrint ? this.canPrintPayslip() : this.canDownloadPayslip());
  }

  load(): void {
    if (!this.canView()) {
      this.error.set('Payroll run and salary view permissions are required.');
      this.result.set(null);
      return;
    }
    if (!this.periodId || this.periodId <= 0 || !this.runId || this.runId <= 0) {
      this.error.set('Enter valid Payroll period and run IDs.');
      this.result.set(null);
      return;
    }
    this.page.set(1);
    this.fetch();
  }

  changePage(nextPage: number): void {
    if (nextPage < 1 || !this.periodId || !this.runId) return;
    this.page.set(nextPage);
    this.fetch();
  }

  private fetch(): void {
    if (!this.periodId || !this.runId) return;
    this.busy.set(true);
    this.error.set('');
    this.result.set(null);
    this.run.set(null);
    this.service.getRun(this.periodId, this.runId).subscribe({
      next: run => {
        this.run.set(run);
        this.service.get(this.periodId!, this.runId!, this.page()).subscribe({
          next: response => {
            this.result.set(response);
            this.loadAccountingState();
            this.busy.set(false);
          },
          error: () => {
            this.error.set('Unable to load Payroll validation. Verify run access and try again.');
            this.busy.set(false);
          }
        });
      },
      error: () => {
        this.error.set('Unable to load Payroll run status. Verify run access and try again.');
        this.busy.set(false);
      }
    });
  }

  private loadAccountingState(): void {
    if (!this.runId) return;
    if (this.auth.hasPermission('payroll.accounting.view_posting')) {
      this.service.getAccountingPosting(this.runId).subscribe({
        next: response => {
          this.accountingPosting.set(response.posting);
          this.accountingMessage.set(response.message ?? '');
        },
        error: () => this.accountingError.set('Unable to load the Accounting posting status.')
      });
    }
    if (this.canManageAccountingMappings()) {
      this.service.getAccountingMappings().subscribe({
        next: mappings => this.accountingMappings.set(mappings),
        error: () => this.accountingError.set('Unable to load Payroll Accounting mappings.')
      });
    }
  }

  postAccounting(): void {
    if (!this.runId || !this.canPostAccounting()) return;
    this.accountingBusy.set(true);
    this.accountingError.set('');
    this.accountingMessage.set('');
    this.service.postAccounting(this.runId).subscribe({
      next: response => {
        this.accountingPosting.set(response.posting);
        this.accountingMessage.set(response.message ?? '');
        this.accountingBusy.set(false);
      },
      error: () => {
        this.accountingError.set('Payroll Accounting posting failed. Refresh the status before retrying.');
        this.accountingBusy.set(false);
      }
    });
  }

  saveAccountingMapping(): void {
    if (!this.canManageAccountingMappings() || !this.mappingCode.trim()
      || !this.mappingAccountId || this.mappingAccountId <= 0) return;
    this.accountingBusy.set(true);
    this.accountingError.set('');
    this.service.upsertAccountingMapping(
      this.mappingType, this.mappingCode, this.mappingAccountId
    ).subscribe({
      next: () => {
        this.service.getAccountingMappings().subscribe({
          next: mappings => {
            this.accountingMappings.set(mappings);
            this.accountingBusy.set(false);
          },
          error: () => {
            this.accountingError.set('Mapping was saved, but the mapping list could not be refreshed.');
            this.accountingBusy.set(false);
          }
        });
      },
      error: () => {
        this.accountingError.set('Unable to save the Payroll Accounting mapping.');
        this.accountingBusy.set(false);
      }
    });
  }

  submit(): void {
    const run = this.run();
    if (!run || !this.periodId || !this.runId || !this.canSubmit()
      || run.status !== 'Draft' || !this.result()?.canSubmit) return;
    this.busy.set(true);
    this.service.submit(this.periodId, this.runId, run.version).subscribe({
      next: () => this.fetch(),
      error: () => {
        this.error.set('Payroll submission failed. Refresh validation and resolve any conflicts.');
        this.busy.set(false);
      }
    });
  }

  decide(decision: 'approve' | 'reject'): void {
    const run = this.run();
    if (!run?.approvalInstanceId || run.status !== 'PendingApproval' || !this.canApprove()) return;
    this.busy.set(true);
    this.service.decide(run.approvalInstanceId, decision).subscribe({
      next: () => this.fetch(),
      error: () => {
        this.error.set(`Payroll ${decision} failed. Refresh the run and retry if it is still pending.`);
        this.busy.set(false);
      }
    });
  }
}
