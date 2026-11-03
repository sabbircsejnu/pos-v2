import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { environment } from '../../../../environments/environment';
import { UI_KIT, UiBreadcrumb, UiTableColumn } from '../../../components/shared/ui';
import { AuthService } from '../../../services/auth.service';

interface ImportRow {
  rowNo: number;
  externalKey: string;
  status: string;
  errors: string[];
  createdEntityType: string | null;
  createdEntityId: number | null;
}

interface ImportBatch {
  id: number;
  importType: string;
  templateVersion: string;
  status: string;
  isDryRun: boolean;
  totalRows: number;
  validRows: number;
  invalidRows: number;
  rows: ImportRow[];
}

@Component({
  selector: 'app-opening-employee-import',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  template: `
    <section class="container mx-auto px-4 py-3">
      <app-ui-page-header [title]="pageTitle"
        description="CSV template v1. Dry-run is required. Execution is atomic: any failure rolls back all rows."
        [breadcrumbs]="breadcrumbs" />
      <div class="imp-stack">
        @if (!isPayrollRoute) {
          <app-ui-alert variant="info">Position and org unit IDs must reference existing records; locations use HR projection IDs.</app-ui-alert>
        }
        @if (isPayrollRoute) {
          <app-ui-alert variant="info">Salary and opening-loan imports create approval drafts; approval is required before activation.</app-ui-alert>
        }
        @if (error()) {
          <app-ui-alert variant="danger">{{ error() }}</app-ui-alert>
        }
        @if (busy()) {
          <app-ui-state kind="loading" title="Processing import request..." [compact]="true" />
        }
        <app-ui-card title="Upload import">
          <form class="imp-form" (ngSubmit)="upload()">
            <app-ui-form-field label="Import type">
              <select appUiControl name="type" [(ngModel)]="importType" [disabled]="busy()">
                @if (!isPayrollRoute) {
                  <option value="Employee">Employee</option>
                  <option value="Assignment">Operational Location Assignment</option>
                }
                @if (isPayrollRoute) {
                  <option value="Salary">Initial salary setup</option>
                  <option value="LoanOpening">Existing loan / advance balance</option>
                }
              </select>
            </app-ui-form-field>
            <div class="imp-actions">
              <button appUiButton variant="outline" type="button" (click)="downloadTemplate()" [disabled]="busy()">
                <i class="fas fa-download" aria-hidden="true"></i>
                <span>Download selected CSV v1 template</span>
              </button>
            </div>
            @if (canExecute()) {
              <app-ui-form-field label="Stable retry key" [required]="true">
                <input appUiControl name="key" [(ngModel)]="idempotencyKey" required maxlength="100">
              </app-ui-form-field>
              <app-ui-form-field label="CSV file">
                <input appUiControl type="file" accept=".csv,text/csv" (change)="selectFile($event)" [disabled]="busy()">
              </app-ui-form-field>
              <div class="imp-actions">
                <button appUiButton type="submit" [disabled]="busy()">Upload private import</button>
              </div>
            }
          </form>
        </app-ui-card>
        <app-ui-card title="Resume batch">
          <form class="imp-resume" (ngSubmit)="load()">
            <app-ui-form-field label="Resume batch ID" [required]="true">
              <input appUiControl type="number" min="1" name="batchId" [(ngModel)]="batchId" required>
            </app-ui-form-field>
            <button appUiButton variant="outline" type="submit" [disabled]="busy()">Load batch</button>
          </form>
        </app-ui-card>
        @if (!batch() && !busy()) {
          <app-ui-state title="No batch loaded."
            message="Upload a CSV or enter a batch ID to review its validation and execution evidence." />
        }
        @if (batch(); as result) {
          <app-ui-card [title]="'Batch ' + result.id + ': ' + result.status">
            <div class="imp-stack">
              <p>{{ result.importType }} · template v{{ result.templateVersion }} ·
                {{ result.totalRows }} rows / {{ result.validRows }} valid / {{ result.invalidRows }} invalid</p>
              @if (result.status === 'Completed') {
                <app-ui-alert variant="success">Import completed. Repeated execution will reuse this result.</app-ui-alert>
              }
              @if (canExecute(result.importType)) {
                <div class="imp-actions">
                  <button appUiButton variant="outline" type="button" (click)="process('dry-run')"
                    [disabled]="busy() || result.status === 'Completed'">Dry-run validation</button>
                  <button appUiButton type="button" (click)="process('execute')"
                    [disabled]="busy() || result.status !== 'Ready'">Execute validated batch</button>
                </div>
              }
              <app-ui-data-table
                ariaLabel="Import rows" density="compact" rowKey="rowNo"
                searchLabel="Search import rows"
                emptyTitle="No rows in this batch."
                [columns]="rowColumns" [rows]="batchRows()">
                <ng-template uiTableCell="result" let-row>
                  @for (message of row.errors; track $index) {
                    <div>{{ message }}</div>
                  }
                  @if (row.createdEntityId) {
                    <span>{{ row.createdEntityType }} #{{ row.createdEntityId }}</span>
                  }
                </ng-template>
              </app-ui-data-table>
            </div>
          </app-ui-card>
        }
      </div>
    </section>
  `,
  styles: [`
    :host { display: block; }
    .imp-stack { display: flex; flex-direction: column; gap: var(--rp-space-4); }
    .imp-stack p { margin: 0; }
    .imp-form { display: flex; flex-direction: column; gap: var(--rp-form-gap); max-width: 40rem; }
    .imp-actions { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); }
    .imp-resume { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--rp-space-3); }
    .imp-resume app-ui-form-field { flex: 1 1 12rem; max-width: 20rem; }
  `]
})
export class OpeningEmployeeImportComponent {
  readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly isPayrollRoute = this.router.url.startsWith('/payroll/');
  readonly pageTitle = `Opening ${this.isPayrollRoute ? 'Payroll' : 'Employee'} Import`;
  readonly breadcrumbs: UiBreadcrumb[] = [
    { label: this.isPayrollRoute ? 'Payroll' : 'HR' },
    { label: 'Opening import' }
  ];
  readonly rowColumns: UiTableColumn[] = [
    { key: 'rowNo', header: 'Row', type: 'number', align: 'start' },
    { key: 'externalKey', header: 'External key' },
    {
      key: 'status', header: 'Status', type: 'status',
      statusMap: {
        Completed: { label: 'Completed', variant: 'success' },
        Valid: { label: 'Valid', variant: 'success' },
        Invalid: { label: 'Invalid', variant: 'danger' },
        Failed: { label: 'Failed', variant: 'danger' }
      }
    },
    { key: 'result', header: 'Errors / result', type: 'custom' }
  ];
  private readonly http = inject(HttpClient);
  readonly batch = signal<ImportBatch | null>(null);
  readonly batchRows = computed(() => this.batch()?.rows ?? []);
  readonly error = signal('');
  readonly busy = signal(false);
  importType = this.isPayrollRoute ? 'Salary' : 'Employee';
  idempotencyKey = '';
  batchId: number | null = null;
  private file: File | null = null;

  canExecute(importType = this.importType): boolean {
    if (this.isPayrollRoute) {
      return this.auth.hasPermission('payroll.import.execute')
        && (importType === 'Salary'
          ? this.auth.hasPermission('payroll.salary.manage')
          : this.auth.hasPermission('payroll.loan.manage'));
    }
    return this.auth.hasPermission('hr.import.execute');
  }

  private get url(): string {
    return `${environment.apiUrl}/${this.isPayrollRoute ? 'payroll' : 'hr'}/v1/opening-imports`;
  }

  selectFile(event: Event): void {
    this.file = (event.target as HTMLInputElement).files?.[0] ?? null;
  }

  upload(): void {
    if (!this.file || !this.idempotencyKey.trim()) {
      this.error.set('Select a CSV file and provide a stable retry key.');
      return;
    }
    const form = new FormData();
    form.set('ImportType', this.importType);
    form.set('TemplateVersion', '1');
    form.set('IdempotencyKey', this.idempotencyKey.trim());
    form.set('File', new Blob([this.file], { type: 'text/csv' }), this.file.name);
    this.busy.set(true);
    this.http.post<ImportBatch>(this.url, form).subscribe({
      next: value => this.accept(value),
      error: error => this.fail(error)
    });
  }

  load(): void {
    if (!this.batchId || this.batchId <= 0) return;
    this.busy.set(true);
    this.http.get<ImportBatch>(`${this.url}/${this.batchId}`).subscribe({
      next: value => this.accept(value),
      error: error => this.fail(error)
    });
  }

  process(action: 'dry-run' | 'execute'): void {
    const batch = this.batch();
    if (!batch || (action === 'execute' && batch.status !== 'Ready')) return;
    this.busy.set(true);
    this.http.post<ImportBatch>(`${this.url}/${batch.id}/${action}`, {}).subscribe({
      next: value => this.accept(value),
      error: error => {
        this.fail(error);
        if (error.status === 409) {
          this.http.get<ImportBatch>(`${this.url}/${batch.id}`).subscribe({
            next: value => this.batch.set(value),
            error: reloadError => this.fail(reloadError)
          });
        }
      }
    });
  }

  downloadTemplate(): void {
    this.http.get(`${this.url}/template`, {
      params: { importType: this.importType, templateVersion: '1' },
      responseType: 'blob'
    }).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `opening-${this.importType}-v1.csv`;
        link.click();
        URL.revokeObjectURL(url);
      },
      error: error => this.fail(error)
    });
  }

  private accept(value: ImportBatch): void {
    this.batch.set(value);
    this.batchId = value.id;
    this.error.set('');
    this.busy.set(false);
  }

  private fail(error: HttpErrorResponse): void {
    this.busy.set(false);
    this.error.set(typeof error.error?.message === 'string'
      ? error.error.message : 'Import request failed. Check permissions and private storage configuration.');
  }
}
