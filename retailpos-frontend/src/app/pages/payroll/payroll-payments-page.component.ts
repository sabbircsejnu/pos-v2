import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin, Observable } from 'rxjs';
import { UI_KIT, UiBadgeVariant, UiBreadcrumb, UiStatusOption, UiTableColumn } from '../../components/shared/ui';
import { AuthService } from '../../services/auth.service';
import {
  PayrollPaymentBatch,
  PayrollPayment,
  PayrollPaymentAllocationInput,
  PayrollPaymentReconciliation,
  PayrollPaymentService
} from '../../services/payroll-payment.service';

const EMPTY_ROWS: readonly never[] = [];

interface BatchReconciliationDraft {
  settledAmount: number;
  settlementDate: string;
  externalReference: string;
  notes: string;
  isException: boolean;
  expectedReconciliationVersion: number;
}

@Component({
  selector: 'app-payroll-payments-page',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  template: `
    <main class="mx-auto max-w-7xl px-4 py-4">
      <app-ui-page-header
        title="Payroll Payment Attempts"
        description="Record a pending payment attempt from an approved Payroll result. Amounts remain unsettled until separate settlement evidence is recorded."
        [breadcrumbs]="breadcrumbs" />

      <div class="flex flex-col gap-4">
        @if (error()) {
          <app-ui-alert variant="danger">{{ error() }}</app-ui-alert>
        }
        @if (busy()) {
          <app-ui-state kind="loading" title="Loading / saving Payroll payment..." [compact]="true" />
        }
        @if (!canRead()) {
          <app-ui-alert variant="warning">
            Payroll payment and salary view permissions are required to read payment details.
          </app-ui-alert>
        }

        @if (canProcess()) {
          <app-ui-card title="Create payment attempt">
            <form (ngSubmit)="create()" class="grid gap-4 md:grid-cols-2">
              <app-ui-form-field label="Payroll employee result ID" [required]="true">
                <input appUiControl name="resultId" type="number" min="1" required [(ngModel)]="resultId">
              </app-ui-form-field>
              <app-ui-form-field label="Approved run version" [required]="true">
                <input appUiControl name="runVersion" type="number" min="1" required [(ngModel)]="runVersion">
              </app-ui-form-field>
              <app-ui-form-field label="Cash amount">
                <input appUiControl name="cashAmount" type="number" min="0" step="0.000001" [(ngModel)]="cashAmount">
              </app-ui-form-field>
              @if (canUseSensitiveDestination()) {
                <app-ui-form-field label="Bank amount">
                  <input appUiControl name="bankAmount" type="number" min="0" step="0.000001" [(ngModel)]="bankAmount">
                </app-ui-form-field>
                <app-ui-form-field label="Mobile amount">
                  <input appUiControl name="mobileAmount" type="number" min="0" step="0.000001" [(ngModel)]="mobileAmount">
                </app-ui-form-field>
              } @else {
                <app-ui-alert class="md:col-span-2" variant="info">
                  Bank and mobile payment attempts require payment destination sensitive-view permission.
                </app-ui-alert>
              }
              <app-ui-form-field label="External reference">
                <input appUiControl name="externalReference" maxlength="150" [(ngModel)]="externalReference">
              </app-ui-form-field>
              <app-ui-form-field class="md:col-span-2" label="Notes">
                <input appUiControl name="notes" maxlength="1000" [(ngModel)]="notes">
              </app-ui-form-field>
              <div class="md:col-span-2">
                <button appUiButton type="submit" [disabled]="busy()">Create pending attempt</button>
              </div>
            </form>
          </app-ui-card>
        }

        @if (canRead()) {
          <app-ui-card title="Payment lookup">
            <form (ngSubmit)="load()" class="flex flex-wrap items-end gap-4">
              <app-ui-form-field class="w-full sm:w-64" label="Payment ID" [required]="true">
                <input appUiControl name="paymentId" type="number" min="1" required [(ngModel)]="paymentId">
              </app-ui-form-field>
              <button appUiButton type="submit" [disabled]="busy()">Load payment details</button>
            </form>
          </app-ui-card>
        }

        @if (payment(); as pay) {
          <div class="flex flex-col gap-4" aria-live="polite">
            <app-ui-card [title]="'Payment #' + pay.id">
              <app-ui-badge uiCardActions [variant]="paymentStatusVariant(pay.status)">{{ pay.status }}</app-ui-badge>
              <div class="flex flex-col gap-3">
                <p>Employee #{{ pay.employeeId }} · Run #{{ pay.payrollRunId }}
                  · Attempt {{ pay.attemptNo }}</p>
                <p>Payable: {{ pay.payableAmount }} {{ pay.currencyCode }}
                  · Settled: {{ pay.paidAmount }}
                  · Outstanding: {{ pay.outstandingAmount }}</p>
                @if (pay.isReplay) {
                  <app-ui-alert variant="info">This is a replay of the existing idempotent request.</app-ui-alert>
                }
                <app-ui-data-table
                  [columns]="auth.hasPermission('payroll.payment.view_sensitive') ? allocationColumnsSensitive : allocationColumns"
                  [rows]="rowsOf(pay.allocations)" rowKey="id"
                  [searchable]="false" [paginated]="false" density="compact"
                  ariaLabel="Payment allocations">
                  <ng-template uiTableCell="destination" let-row>
                    @if (row.bankName || row.accountNumber || row.mobileProvider || row.mobileAccountNumber) {
                      @if (row.bankName) {
                        <span class="block">{{ row.bankName }}</span>
                      }
                      @if (row.accountNumber) {
                        <span class="block">{{ row.accountNumber }}</span>
                      }
                      @if (row.mobileProvider) {
                        <span class="block">{{ row.mobileProvider }}</span>
                      }
                      @if (row.mobileAccountNumber) {
                        <span class="block">{{ row.mobileAccountNumber }}</span>
                      }
                    } @else {
                      —
                    }
                  </ng-template>
                </app-ui-data-table>
                @if (pay.holdReason) {
                  <app-ui-alert variant="warning">Hold reason: {{ pay.holdReason }}</app-ui-alert>
                }
                @if (pay.failureReturnReason) {
                  <app-ui-alert variant="danger">
                    Failure/return reason: {{ pay.failureReturnReason }}
                  </app-ui-alert>
                }
                <app-ui-data-table
                  [columns]="evidenceColumns" [rows]="rowsOf(pay.evidence)" rowKey="id"
                  [searchable]="false" [paginated]="false" density="compact"
                  emptyTitle="No settlement or return evidence recorded"
                  ariaLabel="Settlement and return evidence">
                  <ng-template uiTableCell="allocations" let-row>
                    @if (row.type === 'Settlement') {
                      @for (evidenceAllocation of row.allocations; track evidenceAllocation.allocationNo) {
                        <div class="flex flex-wrap items-center gap-2">
                          <span>Allocation {{ evidenceAllocation.allocationNo }} / {{ evidenceAllocation.method }}
                            · {{ evidenceAllocation.amount }}</span>
                          @if (canPostAccounting()) {
                            <button appUiButton variant="outline" size="sm" type="button"
                              [disabled]="busy()"
                              (click)="postSettlementAccounting(row.id, evidenceAllocation.allocationNo)">
                              {{ settlementPostingStatus(row.id, evidenceAllocation.allocationNo)
                                || 'Post settlement to Accounting' }}
                            </button>
                          }
                        </div>
                      }
                    } @else {
                      —
                    }
                  </ng-template>
                </app-ui-data-table>
                @if (canRead() && !canPostAccounting()) {
                  <app-ui-alert variant="info">
                    Payroll Accounting posting permission and salary view are required to post verified settlements.
                  </app-ui-alert>
                }
              </div>
            </app-ui-card>

            @if (canReconcile()) {
              <app-ui-card title="Payment reconciliation" [headingLevel]="3">
                <div class="flex flex-col gap-4">
                  <form (ngSubmit)="reconcilePayment()" class="grid gap-4 md:grid-cols-2">
                    <app-ui-form-field label="Allocation" [required]="true">
                      <select appUiControl name="reconciliationAllocationNo" required
                        [(ngModel)]="reconciliationAllocationNo">
                        @for (allocation of pay.allocations; track allocation.allocationNo) {
                          <option [ngValue]="allocation.allocationNo">
                            {{ allocation.allocationNo }} · {{ allocation.method }} · expected
                            {{ allocation.amount }} · verified settled {{ allocation.settledAmount }}
                          </option>
                        }
                      </select>
                    </app-ui-form-field>
                    <app-ui-form-field label="Settled amount confirmed" [required]="true">
                      <input appUiControl name="reconciliationSettledAmount" type="number" min="0"
                        step="0.000001" required [(ngModel)]="reconciliationSettledAmount">
                    </app-ui-form-field>
                    <app-ui-form-field label="Settlement date">
                      <input appUiControl name="reconciliationDate" type="date" appCompanyDate
                        [(ngModel)]="reconciliationDate">
                    </app-ui-form-field>
                    <app-ui-form-field label="External / cash reference">
                      <input appUiControl name="reconciliationReference" maxlength="150"
                        [(ngModel)]="reconciliationReference">
                    </app-ui-form-field>
                    <app-ui-form-field class="md:col-span-2" label="Exception / difference reason">
                      <input appUiControl name="reconciliationNotes" maxlength="1000"
                        [(ngModel)]="reconciliationNotes">
                    </app-ui-form-field>
                    <label class="ui-check md:col-span-2">
                      <input appUiControl name="reconciliationException" type="checkbox"
                        [(ngModel)]="reconciliationIsException">
                      <span>Record this as an explicit reconciliation exception</span>
                    </label>
                    <div class="md:col-span-2">
                      <button appUiButton type="submit" [disabled]="busy()">Record reconciliation</button>
                    </div>
                  </form>
                  @if (paymentReconciliations().length) {
                    <app-ui-data-table
                      [columns]="reconciliationColumns" [rows]="paymentReconciliations()" rowKey="id"
                      [searchable]="false" [paginated]="false" density="compact"
                      ariaLabel="Allocation reconciliation status" />
                  }
                </div>
              </app-ui-card>
            }
            @if (pay.status === 'Pending' || pay.status === 'Ready') {
              @if (canHold()) {
                <app-ui-card title="Hold payment" [headingLevel]="3">
                  <form (ngSubmit)="hold()" class="flex flex-wrap items-end gap-4">
                    <app-ui-form-field class="w-full sm:w-96" label="Hold reason" [required]="true">
                      <input appUiControl name="holdReason" maxlength="1000" required [(ngModel)]="actionReason">
                    </app-ui-form-field>
                    <button appUiButton variant="outline" type="submit" [disabled]="busy()">Hold payment</button>
                  </form>
                </app-ui-card>
              }
            }
            @if (canRelease() && pay.status === 'Held') {
              <app-ui-card title="Release payment" [headingLevel]="3">
                <form (ngSubmit)="release()" class="flex flex-wrap items-end gap-4">
                  <app-ui-form-field class="w-full sm:w-96" label="Release reason" [required]="true">
                    <input appUiControl name="releaseReason" maxlength="1000" required [(ngModel)]="actionReason">
                  </app-ui-form-field>
                  <button appUiButton type="submit" [disabled]="busy()">Release payment</button>
                </form>
              </app-ui-card>
            }
            @if (canProcess() && pay.status === 'Processing') {
              <app-ui-card title="Mark payment failed" [headingLevel]="3">
                <form (ngSubmit)="fail()" class="flex flex-wrap items-end gap-4">
                  <app-ui-form-field class="w-full sm:w-96" label="Failure reason" [required]="true">
                    <input appUiControl name="failureReason" maxlength="1000" required [(ngModel)]="actionReason">
                  </app-ui-form-field>
                  <app-ui-form-field class="w-full sm:w-64" label="External reference">
                    <input appUiControl name="failureReference" maxlength="150" [(ngModel)]="externalReference">
                  </app-ui-form-field>
                  <button appUiButton variant="danger" type="submit" [disabled]="busy()">Mark failed</button>
                </form>
              </app-ui-card>
            }
            @if (canProcess() && pay.status === 'Processing' && !canUseEvidenceFields()) {
              <app-ui-alert variant="info">
                Settlement and return evidence requires sensitive destination permission for Bank/Mobile.
              </app-ui-alert>
            }
            @if (canUseEvidenceFields() && (pay.status === 'Processing' || pay.status === 'PartiallyPaid')) {
              <app-ui-card title="Record verified settlement" [headingLevel]="3">
                <form (ngSubmit)="settle()" class="grid gap-4 md:grid-cols-2">
                  <app-ui-form-field label="Allocation number" [required]="true">
                    <input appUiControl name="settlementAllocationNo" type="number" min="1" required
                      [(ngModel)]="evidenceAllocationNo">
                  </app-ui-form-field>
                  <app-ui-form-field label="Settled amount" [required]="true">
                    <input appUiControl name="settlementAmount" type="number" min="0.000001" step="0.000001"
                      required [(ngModel)]="evidenceAmount">
                  </app-ui-form-field>
                  <app-ui-form-field label="Settlement date" [required]="true">
                    <input appUiControl name="settlementDate" type="date" appCompanyDate required [(ngModel)]="evidenceDate">
                  </app-ui-form-field>
                  <app-ui-form-field label="Evidence reference" [required]="true">
                    <input appUiControl name="settlementReference" maxlength="150" required
                      [(ngModel)]="evidenceReference">
                  </app-ui-form-field>
                  <div class="md:col-span-2">
                    <button appUiButton type="submit" [disabled]="busy()">Record verified settlement</button>
                  </div>
                </form>
              </app-ui-card>
            }
            @if (canUseEvidenceFields()
              && (pay.status === 'PartiallyPaid' || pay.status === 'Paid' || pay.status === 'Returned')
              && pay.paidAmount > 0) {
              <app-ui-card title="Record verified return" [headingLevel]="3">
                <form (ngSubmit)="recordReturn()" class="grid gap-4 md:grid-cols-2">
                  <app-ui-form-field label="Allocation number" [required]="true">
                    <input appUiControl name="returnAllocationNo" type="number" min="1" required
                      [(ngModel)]="evidenceAllocationNo">
                  </app-ui-form-field>
                  <app-ui-form-field label="Returned amount" [required]="true">
                    <input appUiControl name="returnAmount" type="number" min="0.000001" step="0.000001"
                      required [(ngModel)]="evidenceAmount">
                  </app-ui-form-field>
                  <app-ui-form-field label="Return date" [required]="true">
                    <input appUiControl name="returnDate" type="date" appCompanyDate required [(ngModel)]="evidenceDate">
                  </app-ui-form-field>
                  <app-ui-form-field label="Return evidence reference" [required]="true">
                    <input appUiControl name="returnReference" maxlength="150" required
                      [(ngModel)]="evidenceReference">
                  </app-ui-form-field>
                  <app-ui-form-field class="md:col-span-2" label="Return reason" [required]="true">
                    <input appUiControl name="returnReason" maxlength="1000" required [(ngModel)]="actionReason">
                  </app-ui-form-field>
                  <div class="md:col-span-2">
                    <button appUiButton variant="danger" type="submit" [disabled]="busy()">Record verified return</button>
                  </div>
                </form>
              </app-ui-card>
            }
            @if (canRetry() && pay.outstandingAmount > 0
              && (pay.status === 'Failed' || pay.status === 'Returned')) {
              <app-ui-card title="Create resolved reissue" [headingLevel]="3">
                <form (ngSubmit)="retry()" class="grid gap-4 md:grid-cols-2">
                  <app-ui-form-field label="Resolution evidence reference" [required]="true">
                    <input appUiControl name="resolutionEvidenceReference" maxlength="150" required
                      [(ngModel)]="resolutionEvidenceReference">
                  </app-ui-form-field>
                  <app-ui-form-field label="Retry amount" [required]="true">
                    <input appUiControl name="retryAmount" type="number" min="0.000001" step="0.000001"
                      required [(ngModel)]="retryAmount">
                  </app-ui-form-field>
                  <app-ui-form-field label="Method">
                    <select appUiControl name="retryMethod" [(ngModel)]="retryMethod">
                      <option value="Cash">Cash</option>
                      <option value="Bank" [disabled]="!canUseSensitiveDestination()">Bank</option>
                      <option value="Mobile" [disabled]="!canUseSensitiveDestination()">Mobile</option>
                    </select>
                  </app-ui-form-field>
                  <div class="md:col-span-2">
                    <button appUiButton type="submit" [disabled]="busy()">Retry payment</button>
                  </div>
                </form>
              </app-ui-card>
            }
          </div>
        }

        <app-ui-card title="Payment batches">
          <div class="flex flex-col gap-4">
            @if (!canProcess()) {
              <app-ui-alert variant="info">
                Payroll payment processing and salary view permissions are required to create a batch.
              </app-ui-alert>
            }
            @if (canProcess()) {
              <form (ngSubmit)="createBatch()" class="grid gap-4 md:grid-cols-2">
                <app-ui-form-field label="Approved Payroll run ID" [required]="true">
                  <input appUiControl name="batchRunId" type="number" min="1" required [(ngModel)]="batchRunId">
                </app-ui-form-field>
                <app-ui-form-field label="Batch reference" [required]="true">
                  <input appUiControl name="batchReference" maxlength="100" required [(ngModel)]="batchReference">
                </app-ui-form-field>
                <app-ui-form-field class="md:col-span-2" label="Payment IDs and expected versions" [required]="true">
                  <input appUiControl name="batchPayments" required [(ngModel)]="batchPayments"
                    placeholder="paymentId:version, paymentId:version">
                </app-ui-form-field>
                <div class="md:col-span-2">
                  <button appUiButton type="submit" [disabled]="busy()">Create ready batch</button>
                </div>
              </form>
            }
            @if (canRead()) {
              <form (ngSubmit)="loadBatches()" class="flex flex-wrap items-end gap-4">
                <app-ui-form-field class="w-full sm:w-96" label="Filter batches by Payroll run ID (optional)">
                  <input appUiControl name="batchListRunId" type="number" min="1" [(ngModel)]="batchListRunId">
                </app-ui-form-field>
                <button appUiButton variant="outline" type="submit" [disabled]="busy()">Load batches</button>
              </form>
            }
            @if (batches().length) {
              <app-ui-data-table
                [columns]="batchColumns" [rows]="batches()" rowKey="id"
                [searchable]="false" [paginated]="false" density="compact"
                ariaLabel="Payment batches">
                <ng-template uiTableCell="id" let-row>Batch #{{ row.id }}</ng-template>
                <ng-template uiTableCell="expectedAmount" let-row>{{ row.expectedAmount }} {{ row.currencyCode }}</ng-template>
                <ng-template uiTableRowActions let-row>
                  <button appUiButton variant="outline" size="sm" type="button"
                    [attr.aria-label]="'View batch #' + row.id" (click)="selectBatch(row)">View</button>
                </ng-template>
              </app-ui-data-table>
            }
            @if (canRead()) {
              <form (ngSubmit)="loadBatch()" class="flex flex-wrap items-end gap-4">
                <app-ui-form-field class="w-full sm:w-64" label="Payment batch ID" [required]="true">
                  <input appUiControl name="batchId" type="number" min="1" required [(ngModel)]="batchId">
                </app-ui-form-field>
                <button appUiButton type="submit" [disabled]="busy()">Load batch details</button>
              </form>
            }
          </div>
        </app-ui-card>

        @if (paymentBatch(); as batch) {
          <div class="flex flex-col gap-4" aria-live="polite">
            <app-ui-card [title]="'Batch #' + batch.id" [headingLevel]="3">
              <app-ui-badge uiCardActions [variant]="paymentStatusVariant(batch.status)">{{ batch.status }}</app-ui-badge>
              <div class="flex flex-col gap-3">
                <p>{{ batch.method }} · {{ batch.expectedAmount }}
                  {{ batch.currencyCode }} · Version {{ batch.version }}</p>
                @if (batch.submittedAtUtc) {
                  <p>
                    Submitted {{ (batch.submittedAtUtc | appDate:'medium') }} by user
                    #{{ batch.submittedByUserId }}
                  </p>
                }
                <app-ui-data-table
                  [columns]="batchPaymentColumns" [rows]="rowsOf(batch.payments)" rowKey="paymentId"
                  [searchable]="false" [paginated]="false" density="compact"
                  ariaLabel="Batch payments">
                  <ng-template uiTableCell="paymentId" let-row>#{{ row.paymentId }}</ng-template>
                  <ng-template uiTableCell="employeeId" let-row>#{{ row.employeeId }}</ng-template>
                </app-ui-data-table>
                @if (canRead() && !canReconcile()) {
                  <app-ui-alert variant="info">
                    Payment reconciliation requires the payment reconcile permission and salary view.
                  </app-ui-alert>
                }
                @if (rowsOf(batch.bankFileVersions).length) {
                  <app-ui-data-table
                    [columns]="bankFileColumns" [rows]="rowsOf(batch.bankFileVersions)" rowKey="versionNo"
                    [searchable]="false" [paginated]="false" density="compact"
                    ariaLabel="Bank file versions">
                    <ng-template uiTableCell="versionNo" let-row>Bank file v{{ row.versionNo }}</ng-template>
                    <ng-template uiTableCell="contentHash" let-row><span class="break-all">SHA-256 {{ row.contentHash }}</span></ng-template>
                  </app-ui-data-table>
                }
                @if (batch.status === 'Ready' && batch.method !== 'Cash' && batch.bankFileVersions.length === 0) {
                  <app-ui-alert variant="info">Generate a bank file before submitting this batch.</app-ui-alert>
                }
                @if (batch.method !== 'Cash' && !canGenerateBankFile()) {
                  <app-ui-alert variant="info">
                    Bank-file generation requires bank-file generation and payment destination sensitive-view permissions.
                  </app-ui-alert>
                }
              </div>
            </app-ui-card>

            @if (canReconcile() && batchPaymentDetails().length) {
              <app-ui-card title="Batch settlement reconciliation" [headingLevel]="3"
                subtitle="Enter the externally confirmed settlement amount and reference for each allocation. Rows with no verified settlement may be left unchanged unless recording an exception.">
                <div class="flex flex-col gap-4">
                  <form (ngSubmit)="reconcileBatch()" class="flex flex-col gap-4">
                    @for (detail of batchPaymentDetails(); track detail.id) {
                      <section class="flex flex-col gap-3 rounded-rp-card border border-rp-border p-4">
                        <h4 class="font-semibold">Payment #{{ detail.id }} · Employee #{{ detail.employeeId }}
                          · {{ detail.status }}</h4>
                        @for (allocation of detail.allocations; track allocation.allocationNo) {
                          @if (batchDraftFor(detail.id, allocation.allocationNo); as draft) {
                            <div class="grid gap-4 md:grid-cols-2">
                              <p class="md:col-span-2">
                                Allocation {{ allocation.allocationNo }} · {{ allocation.method }}
                                · expected {{ allocation.amount }} · verified settled {{ allocation.settledAmount }}
                              </p>
                              <app-ui-form-field label="Reconciled settled amount" [required]="true">
                                <input appUiControl [name]="'batch-amount-' + detail.id + '-' + allocation.allocationNo"
                                  type="number" min="0" step="0.000001" required
                                  [(ngModel)]="draft.settledAmount">
                              </app-ui-form-field>
                              <app-ui-form-field label="Settlement date" [required]="draft.settledAmount > 0">
                                <input appUiControl [name]="'batch-date-' + detail.id + '-' + allocation.allocationNo"
                                  type="date" appCompanyDate [required]="draft.settledAmount > 0"
                                  [(ngModel)]="draft.settlementDate">
                              </app-ui-form-field>
                              <app-ui-form-field label="External / cash reference" [required]="draft.settledAmount > 0">
                                <input appUiControl [name]="'batch-reference-' + detail.id + '-' + allocation.allocationNo"
                                  maxlength="150" [required]="draft.settledAmount > 0"
                                  [(ngModel)]="draft.externalReference">
                              </app-ui-form-field>
                              <app-ui-form-field label="Exception reason">
                                <input appUiControl [name]="'batch-notes-' + detail.id + '-' + allocation.allocationNo"
                                  maxlength="1000" [(ngModel)]="draft.notes">
                              </app-ui-form-field>
                              <label class="ui-check md:col-span-2">
                                <input appUiControl [name]="'batch-exception-' + detail.id + '-' + allocation.allocationNo"
                                  type="checkbox" [(ngModel)]="draft.isException">
                                <span>Record an explicit exception for this allocation</span>
                              </label>
                            </div>
                          }
                        }
                      </section>
                    }
                    <div>
                      <button appUiButton type="submit" [disabled]="busy()">Record batch reconciliation</button>
                    </div>
                  </form>
                  @if (batchReconciliations().length) {
                    <app-ui-data-table
                      [columns]="batchReconciliationColumns" [rows]="batchReconciliations()" rowKey="id"
                      [searchable]="false" [paginated]="false" density="compact"
                      ariaLabel="Batch reconciliation status" />
                  }
                </div>
              </app-ui-card>
            }

            @if (canGenerateBankFile()) {
              <app-ui-card title="Bank file" [headingLevel]="3">
                <form (ngSubmit)="generateBankFile()" class="flex flex-wrap items-end gap-4">
                  <app-ui-form-field class="w-full sm:w-96" label="Generation / regeneration reason" [required]="true">
                    <input appUiControl name="bankFileReason" maxlength="1000" required [(ngModel)]="bankFileReason">
                  </app-ui-form-field>
                  <button appUiButton type="submit" [disabled]="busy()">Generate and download bank file</button>
                </form>
              </app-ui-card>
            }
            @if (canSubmitBatch()) {
              <app-ui-card title="Batch submission" [headingLevel]="3">
                <form (ngSubmit)="submitBatch()" class="grid gap-4 md:grid-cols-2">
                  <app-ui-form-field label="External submission reference" [required]="true">
                    <input appUiControl name="submissionExternalReference" maxlength="150" required
                      [(ngModel)]="submissionExternalReference">
                  </app-ui-form-field>
                  <app-ui-form-field label="Submission reason" [required]="true">
                    <input appUiControl name="submissionReason" maxlength="1000" required
                      [(ngModel)]="submissionReason">
                  </app-ui-form-field>
                  <div class="md:col-span-2">
                    <button appUiButton type="submit" [disabled]="busy()">Record batch submission</button>
                  </div>
                </form>
              </app-ui-card>
            }
          </div>
        }
      </div>
    </main>
  `
})
export class PayrollPaymentsPageComponent {
  readonly auth = inject(AuthService);
  private readonly service = inject(PayrollPaymentService);
  readonly payment = signal<PayrollPayment | null>(null);
  readonly paymentReconciliations = signal<PayrollPaymentReconciliation[]>([]);
  readonly settlementPostingStatuses = signal<Record<string, string>>({});
  readonly batches = signal<PayrollPaymentBatch[]>([]);
  readonly paymentBatch = signal<PayrollPaymentBatch | null>(null);
  readonly batchPaymentDetails = signal<PayrollPayment[]>([]);
  readonly batchReconciliations = signal<PayrollPaymentReconciliation[]>([]);
  readonly busy = signal(false);
  readonly error = signal('');

  resultId: number | null = null;
  runVersion: number | null = null;
  paymentId: number | null = null;
  cashAmount: number | null = null;
  bankAmount: number | null = null;
  mobileAmount: number | null = null;
  externalReference = '';
  notes = '';
  actionReason = '';
  evidenceDate = new Date().toISOString().slice(0, 10);
  evidenceReference = '';
  evidenceAllocationNo: number | null = 1;
  evidenceAmount: number | null = null;
  reconciliationAllocationNo: number | null = 1;
  reconciliationSettledAmount: number | null = null;
  reconciliationDate = new Date().toISOString().slice(0, 10);
  reconciliationReference = '';
  reconciliationNotes = '';
  reconciliationIsException = false;
  resolutionEvidenceReference = '';
  retryAmount: number | null = null;
  retryMethod: PayrollPaymentAllocationInput['method'] = 'Cash';
  batchRunId: number | null = null;
  batchReference = '';
  batchPayments = '';
  batchListRunId: number | null = null;
  batchId: number | null = null;
  bankFileReason = '';
  submissionExternalReference = '';
  submissionReason = '';
  batchReconciliationDrafts: Record<string, BatchReconciliationDraft> = {};
  private idempotencyKey = crypto.randomUUID();
  private evidenceIdempotencyKey = crypto.randomUUID();
  private retryIdempotencyKey = crypto.randomUUID();
  private batchIdempotencyKey = crypto.randomUUID();
  private bankFileIdempotencyKey = crypto.randomUUID();
  private batchSubmissionIdempotencyKey = crypto.randomUUID();
  private reconciliationIdempotencyKey = crypto.randomUUID();
  private batchReconciliationIdempotencyKey = crypto.randomUUID();

  readonly breadcrumbs: UiBreadcrumb[] = [
    { label: 'Dashboard', link: '/dashboard' },
    { label: 'Payroll' },
    { label: 'Payment attempts' }
  ];
  private readonly statusMap: Record<string, UiStatusOption> = {
    Pending: { label: 'Pending', variant: 'warning' },
    Ready: { label: 'Ready', variant: 'info' },
    Held: { label: 'Held', variant: 'warning' },
    Processing: { label: 'Processing', variant: 'info' },
    Submitted: { label: 'Submitted', variant: 'info' },
    PartiallyPaid: { label: 'PartiallyPaid', variant: 'warning' },
    PartiallyReconciled: { label: 'PartiallyReconciled', variant: 'warning' },
    Unreconciled: { label: 'Unreconciled', variant: 'neutral' },
    Paid: { label: 'Paid', variant: 'success' },
    Settled: { label: 'Settled', variant: 'success' },
    Reconciled: { label: 'Reconciled', variant: 'success' },
    Completed: { label: 'Completed', variant: 'success' },
    Failed: { label: 'Failed', variant: 'danger' },
    Returned: { label: 'Returned', variant: 'danger' },
    Exception: { label: 'Exception', variant: 'danger' },
    Cancelled: { label: 'Cancelled', variant: 'danger' }
  };
  readonly allocationColumns: UiTableColumn[] = [
    { key: 'method', header: 'Method', sortable: true },
    { key: 'amount', header: 'Amount', align: 'end', sortable: true },
    { key: 'state', header: 'State', type: 'status', statusMap: this.statusMap },
    { key: 'externalReference', header: 'Reference' }
  ];
  readonly allocationColumnsSensitive: UiTableColumn[] = [
    ...this.allocationColumns,
    { key: 'destination', header: 'Destination', type: 'custom' }
  ];
  readonly evidenceColumns: UiTableColumn[] = [
    { key: 'type', header: 'Type' },
    { key: 'amount', header: 'Amount', align: 'end' },
    { key: 'evidenceDate', type: 'date', header: 'Date' },
    { key: 'evidenceReference', header: 'Reference' },
    { key: 'allocations', header: 'Allocations', type: 'custom' }
  ];
  readonly reconciliationColumns: UiTableColumn[] = [
    { key: 'allocationNo', header: 'Allocation' },
    { key: 'status', header: 'Status', type: 'status', statusMap: this.statusMap },
    { key: 'expectedAmount', header: 'Expected', align: 'end' },
    { key: 'settledAmount', header: 'Settled', align: 'end' },
    { key: 'difference', header: 'Difference', align: 'end' },
    { key: 'externalReference', header: 'Reference' }
  ];
  readonly batchColumns: UiTableColumn[] = [
    { key: 'id', header: 'Batch', type: 'custom', sortable: true },
    { key: 'batchReference', header: 'Reference', sortable: true },
    { key: 'status', header: 'Status', type: 'status', statusMap: this.statusMap },
    { key: 'expectedAmount', header: 'Expected amount', type: 'custom', align: 'end' }
  ];
  readonly batchPaymentColumns: UiTableColumn[] = [
    { key: 'paymentId', header: 'Payment', type: 'custom' },
    { key: 'employeeId', header: 'Employee', type: 'custom' },
    { key: 'amount', header: 'Amount', align: 'end' },
    { key: 'status', header: 'Status', type: 'status', statusMap: this.statusMap }
  ];
  readonly bankFileColumns: UiTableColumn[] = [
    { key: 'versionNo', header: 'Version', type: 'custom' },
    { key: 'rowCount', header: 'Rows', align: 'end' },
    { key: 'expectedAmount', header: 'Expected amount', align: 'end' },
    { key: 'contentHash', header: 'Content hash', type: 'custom' }
  ];
  readonly batchReconciliationColumns: UiTableColumn[] = [
    { key: 'payrollPaymentId', header: 'Payment' },
    { key: 'allocationNo', header: 'Allocation' },
    { key: 'status', header: 'Status', type: 'status', statusMap: this.statusMap },
    { key: 'expectedAmount', header: 'Expected', align: 'end' },
    { key: 'settledAmount', header: 'Settled', align: 'end' },
    { key: 'difference', header: 'Difference', align: 'end' }
  ];

  paymentStatusVariant(status: string | null | undefined): UiBadgeVariant {
    return this.statusMap[status ?? '']?.variant ?? 'neutral';
  }

  rowsOf<T>(items: readonly T[] | null | undefined): readonly T[] {
    return items ?? EMPTY_ROWS;
  }

  canRead(): boolean {
    return this.auth.hasPermission('payroll.payment.view')
      && this.auth.hasPermission('payroll.salary.view');
  }

  canReconcile(): boolean {
    return this.canRead() && this.auth.hasPermission('payroll.payment.reconcile');
  }

  canPostAccounting(): boolean {
    return this.canRead() && this.auth.hasPermission('payroll.accounting.post');
  }

  settlementPostingStatus(evidenceId: number, allocationNo: number): string {
    return this.settlementPostingStatuses()[`${evidenceId}:${allocationNo}`] ?? '';
  }

  canProcess(): boolean {
    return this.auth.hasPermission('payroll.payment.process')
      && this.auth.hasPermission('payroll.salary.view');
  }

  canUseSensitiveDestination(): boolean {
    return this.auth.hasPermission('payroll.payment.view_sensitive');
  }

  canHold(): boolean {
    return this.canRead() && this.auth.hasPermission('payroll.payment.hold');
  }

  canRelease(): boolean {
    return this.canRead() && this.auth.hasPermission('payroll.payment.release');
  }

  canRetry(): boolean {
    return this.canRead() && this.auth.hasPermission('payroll.payment.retry');
  }

  canGenerateBankFile(): boolean {
    return this.canRead()
      && this.auth.hasPermission('payroll.bank_file.generate')
      && this.canUseSensitiveDestination();
  }

  canSubmitBatch(): boolean {
    const batch = this.paymentBatch();
    return !!batch && batch.status === 'Ready' && this.canProcess()
      && (batch.method === 'Cash' || this.canUseSensitiveDestination());
  }

  canUseEvidenceFields(): boolean {
    const current = this.payment();
    return this.canRead() && this.canProcess() && (!current
      || !current.allocations.some(item => item.method === 'Bank' || item.method === 'Mobile')
      || this.canUseSensitiveDestination());
  }

  create(): void {
    if (!this.canProcess()) {
      this.error.set('Payroll payment processing and salary view permissions are required.');
      return;
    }
    if (!this.resultId || this.resultId <= 0 || !this.runVersion || this.runVersion <= 0) {
      this.error.set('Enter a valid Payroll result ID and approved run version.');
      return;
    }
    const enteredAmounts = [this.cashAmount, this.bankAmount, this.mobileAmount];
    if (enteredAmounts.some(amount =>
      amount !== null && (!Number.isFinite(amount) || amount < 0))) {
      this.error.set('Payment allocation amounts must be zero or positive numbers.');
      return;
    }
    if (!this.canUseSensitiveDestination()
        && ((this.bankAmount ?? 0) > 0 || (this.mobileAmount ?? 0) > 0)) {
      this.error.set('Bank and mobile allocations require payment destination sensitive-view permission.');
      return;
    }
    const allocations = this.allocations();
    if (!allocations.length) {
      this.error.set('Enter a positive amount for at least one payment method.');
      return;
    }

    this.busy.set(true);
    this.error.set('');
    this.service.create({
      payrollEmployeeResultId: this.resultId,
      expectedRunVersion: this.runVersion,
      idempotencyKey: this.idempotencyKey,
      allocations,
      notes: this.notes.trim() || null
    }).subscribe({
      next: payment => {
        this.payment.set(payment);
        this.paymentId = payment.id;
        this.evidenceAllocationNo = payment.allocations[0]?.allocationNo ?? null;
        this.idempotencyKey = crypto.randomUUID();
        this.busy.set(false);
      },
      error: error => {
        this.error.set(error instanceof HttpErrorResponse && error.status === 409
          ? 'The payment request conflicts with an existing attempt or a changed Payroll run. Refresh and verify before retrying.'
          : 'Unable to create the Payroll payment attempt. Verify access and request details, then retry.');
        this.busy.set(false);
      }
    });
  }

  load(): void {
    if (!this.canRead()) {
      this.error.set('Payroll payment and salary view permissions are required.');
      return;
    }
    if (!this.paymentId || this.paymentId <= 0) {
      this.error.set('Enter a valid Payroll payment ID.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.payment.set(null);
    this.service.get(this.paymentId).subscribe({
      next: payment => {
        this.payment.set(payment);
        this.evidenceAllocationNo = payment.allocations[0]?.allocationNo ?? null;
        this.service.listReconciliations(payment.id).subscribe({
          next: items => {
            this.paymentReconciliations.set(items);
            this.busy.set(false);
          },
          error: () => {
            this.error.set('Payment loaded, but reconciliation history could not be loaded.');
            this.busy.set(false);
          }
        });
      },
      error: () => {
        this.error.set('Unable to load Payroll payment details. Verify access and try again.');
        this.busy.set(false);
      }
    });
  }

  reconcilePayment(): void {
    const payment = this.payment();
    const allocationNo = this.reconciliationAllocationNo;
    if (!payment || !this.canReconcile() || allocationNo === null
        || allocationNo === undefined || this.reconciliationSettledAmount === null
        || !Number.isFinite(this.reconciliationSettledAmount)
        || this.reconciliationSettledAmount < 0) {
      this.error.set('Verify reconciliation permission, allocation, and settled amount.');
      return;
    }
    const allocation = payment.allocations.find(item => item.allocationNo === allocationNo);
    if (!allocation) {
      this.error.set('The selected allocation is no longer available. Reload the payment.');
      return;
    }
    const expectedReconciliationVersion = this.paymentReconciliations()
      .filter(item => item.allocationNo === allocationNo)
      .reduce((version, item) => Math.max(version, item.version), 0);
    this.busy.set(true);
    this.error.set('');
    this.service.reconcilePayment(payment.id, {
      expectedPaymentVersion: payment.version,
      expectedReconciliationVersion,
      idempotencyKey: this.reconciliationIdempotencyKey,
      allocationNo,
      settledAmount: this.reconciliationSettledAmount,
      settlementDate: this.reconciliationDate || null,
      evidenceSource: allocation.method,
      externalReference: this.reconciliationReference.trim() || null,
      notes: this.reconciliationNotes.trim() || null,
      isException: this.reconciliationIsException
    }).subscribe({
      next: result => {
        this.paymentReconciliations.set(result.items);
        this.reconciliationIdempotencyKey = crypto.randomUUID();
        this.busy.set(false);
        this.load();
      },
      error: error => {
        this.error.set(error instanceof HttpErrorResponse && error.status === 409
          ? 'Payment or reconciliation state changed. Reload the payment before retrying.'
          : 'Unable to record payment reconciliation. Verify access and evidence.');
        this.busy.set(false);
      }
    });
  }

  hold(): void {
    const payment = this.payment();
    if (!payment || !this.canHold() || !this.actionReason.trim()) return;
    this.perform(this.service.hold(payment.id, {
      expectedVersion: payment.version,
      reason: this.actionReason.trim()
    }), 'Unable to hold this Payroll payment. Verify access and payment status.');
  }

  release(): void {
    const payment = this.payment();
    if (!payment || !this.canRelease() || !this.actionReason.trim()) return;
    this.perform(this.service.release(payment.id, {
      expectedVersion: payment.version,
      reason: this.actionReason.trim()
    }), 'Unable to release this Payroll payment. Verify access and payment status.');
  }

  fail(): void {
    const payment = this.payment();
    if (!payment || !this.canProcess() || !this.actionReason.trim()) return;
    this.perform(this.service.fail(payment.id, {
      expectedVersion: payment.version,
      reason: this.actionReason.trim(),
      externalReference: this.externalReference.trim() || null
    }), 'Unable to mark this Payroll payment failed. Verify access and payment status.');
  }

  settle(): void {
    const payment = this.payment();
    if (!payment || !this.canUseEvidenceFields() || !this.validEvidenceInput()) return;
    this.perform(this.service.settle(payment.id, {
      expectedVersion: payment.version,
      idempotencyKey: this.evidenceIdempotencyKey,
      evidenceDate: this.evidenceDate,
      evidenceReference: this.evidenceReference.trim(),
      reason: null,
      allocations: [{ allocationNo: this.evidenceAllocationNo!, amount: this.evidenceAmount! }]
    }), 'Unable to record settlement evidence. Verify access, amount, and payment status.',
    () => { this.evidenceIdempotencyKey = crypto.randomUUID(); });
  }

  recordReturn(): void {
    const payment = this.payment();
    if (!payment || !this.canUseEvidenceFields() || !this.validEvidenceInput()
        || !this.actionReason.trim()) return;
    this.perform(this.service.return(payment.id, {
      expectedVersion: payment.version,
      idempotencyKey: this.evidenceIdempotencyKey,
      evidenceDate: this.evidenceDate,
      evidenceReference: this.evidenceReference.trim(),
      reason: this.actionReason.trim(),
      allocations: [{ allocationNo: this.evidenceAllocationNo!, amount: this.evidenceAmount! }]
    }), 'Unable to record return evidence. Verify access, amount, and payment status.',
    () => { this.evidenceIdempotencyKey = crypto.randomUUID(); });
  }

  postSettlementAccounting(evidenceId: number, allocationNo: number): void {
    const payment = this.payment();
    const allocation = payment?.allocations.find(row => row.allocationNo === allocationNo);
    if (!payment || !allocation || !this.canPostAccounting()
        || !['PartiallyPaid', 'Paid'].includes(payment.status)) return;
    const key = `${evidenceId}:${allocationNo}`;
    this.busy.set(true);
    this.error.set('');
    this.service.postSettlementAccounting(payment.id, allocation.id, evidenceId).subscribe({
      next: response => {
        const posting = response.posting;
        const status = posting?.status ?? response.message ?? 'No Accounting posting was created.';
        this.settlementPostingStatuses.update(current => ({ ...current, [key]: status }));
        if (posting?.status !== 'Posted') {
          this.error.set(posting?.errorMessage || response.message
            || 'Settlement posting was not confirmed. Refresh the payment and Accounting status.');
        }
        this.busy.set(false);
      },
      error: error => {
        const detail = error instanceof HttpErrorResponse
          ? error.error?.detail
          : null;
        this.error.set(typeof detail === 'string' && detail
          ? detail
          : 'Unable to post this settlement. Verify reconciliation, account mappings, and Accounting period status.');
        this.busy.set(false);
      }
    });
  }

  retry(): void {
    const payment = this.payment();
    if (!payment || !this.canRetry() || !this.resolutionEvidenceReference.trim()
        || !this.retryAmount || this.retryAmount <= 0) return;
    if ((this.retryMethod === 'Bank' || this.retryMethod === 'Mobile')
        && !this.canUseSensitiveDestination()) {
      this.error.set('Bank and mobile retries require payment destination sensitive-view permission.');
      return;
    }
    this.perform(this.service.retry(payment.id, {
      expectedVersion: payment.version,
      idempotencyKey: this.retryIdempotencyKey,
      resolutionEvidenceReference: this.resolutionEvidenceReference.trim(),
      allocations: [{
        method: this.retryMethod,
        amount: this.retryAmount,
        externalReference: null
      }],
      notes: null
    }), 'Unable to retry this Payroll payment. Verify resolution evidence and payment status.',
    () => { this.retryIdempotencyKey = crypto.randomUUID(); });
  }

  createBatch(): void {
    if (!this.canProcess()) {
      this.error.set('Payroll payment processing and salary view permissions are required.');
      return;
    }
    if (!this.batchRunId || this.batchRunId <= 0 || !this.batchReference.trim()) {
      this.error.set('Enter an approved Payroll run ID and batch reference.');
      return;
    }
    const payments = this.batchPayments.split(',').map(value => {
      const [paymentIdText, versionText, extra] = value.trim().split(':');
      const paymentId = Number(paymentIdText);
      const expectedVersion = Number(versionText);
      return extra === undefined && Number.isSafeInteger(paymentId) && paymentId > 0
        && Number.isSafeInteger(expectedVersion) && expectedVersion > 0
        ? { paymentId, expectedVersion }
        : null;
    });
    if (!payments.length || payments.some(item => item === null)) {
      this.error.set('Enter each payment as paymentId:expectedVersion, separated by commas.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.service.createBatch(this.batchRunId, {
      batchReference: this.batchReference.trim(),
      idempotencyKey: this.batchIdempotencyKey,
      payments: payments.filter(item => item !== null)
    }).subscribe({
      next: batch => {
        this.paymentBatch.set(batch);
        this.batchId = batch.id;
        this.batchIdempotencyKey = crypto.randomUUID();
        this.bankFileIdempotencyKey = crypto.randomUUID();
        this.loadBatches();
        this.busy.set(false);
      },
      error: error => {
        this.error.set(error instanceof HttpErrorResponse && error.status === 409
          ? 'A payment changed or is already batched. Refresh payment versions before retrying.'
          : 'Unable to create the payment batch. Verify approval, access, and payment details.');
        this.busy.set(false);
      }
    });
  }

  loadBatches(): void {
    if (!this.canRead()) {
      this.error.set('Payroll payment and salary view permissions are required.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.service.listBatches(this.batchListRunId ?? undefined).subscribe({
      next: batches => {
        this.batches.set(batches);
        this.busy.set(false);
      },
      error: () => {
        this.error.set('Unable to load payment batches. Verify access and try again.');
        this.busy.set(false);
      }
    });
  }

  selectBatch(batch: PayrollPaymentBatch): void {
    this.paymentBatch.set(batch);
    this.batchId = batch.id;
    this.loadBatchReconciliationDetails();
  }

  loadBatch(): void {
    if (!this.canRead() || !this.batchId || this.batchId <= 0) {
      this.error.set('Enter a valid payment batch ID and verify read permissions.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.service.getBatch(this.batchId).subscribe({
      next: batch => {
        this.paymentBatch.set(batch);
        this.bankFileIdempotencyKey = crypto.randomUUID();
        this.busy.set(false);
        this.loadBatchReconciliationDetails();
      },
      error: () => {
        this.error.set('Unable to load payment batch details. Verify access and try again.');
        this.busy.set(false);
      }
    });
  }

  batchDraftFor(paymentId: number, allocationNo: number): BatchReconciliationDraft | null {
    return this.batchReconciliationDrafts[`${paymentId}:${allocationNo}`] ?? null;
  }

  loadBatchReconciliationDetails(): void {
    const batch = this.paymentBatch();
    if (!batch || !this.canRead()) return;
    if (!batch.payments.length) {
      this.batchPaymentDetails.set([]);
      this.batchReconciliations.set([]);
      this.batchReconciliationDrafts = {};
      return;
    }
    forkJoin({
      payments: forkJoin(batch.payments.map(item => this.service.get(item.paymentId))),
      reconciliations: this.service.listReconciliations(undefined, batch.id)
    }).subscribe({
      next: result => {
        this.batchPaymentDetails.set(result.payments);
        this.batchReconciliations.set(result.reconciliations);
        const drafts: Record<string, BatchReconciliationDraft> = {};
        for (const payment of result.payments) {
          for (const allocation of payment.allocations) {
            const latest = result.reconciliations
              .filter(item => item.payrollPaymentId === payment.id
                && item.allocationNo === allocation.allocationNo)
              .reduce((version, item) => Math.max(version, item.version), 0);
            drafts[`${payment.id}:${allocation.allocationNo}`] = {
              settledAmount: allocation.settledAmount,
              settlementDate: new Date().toISOString().slice(0, 10),
              externalReference: '',
              notes: '',
              isException: false,
              expectedReconciliationVersion: latest
            };
          }
        }
        this.batchReconciliationDrafts = drafts;
      },
      error: () => {
        this.error.set('Unable to load batch payment and reconciliation details. Verify access and retry.');
      }
    });
  }

  reconcileBatch(): void {
    const batch = this.paymentBatch();
    if (!batch || !this.canReconcile() || !this.batchPaymentDetails().length) {
      this.error.set('Load a batch with payment details and verify reconciliation permission.');
      return;
    }
    const items = this.batchPaymentDetails().flatMap(payment =>
      payment.allocations.map(allocation => {
        const draft = this.batchDraftFor(payment.id, allocation.allocationNo);
        return draft ? {
          paymentId: payment.id,
          expectedPaymentVersion: payment.version,
          allocationNo: allocation.allocationNo,
          expectedReconciliationVersion: draft.expectedReconciliationVersion,
          settledAmount: draft.settledAmount,
          settlementDate: draft.settlementDate || null,
          evidenceSource: allocation.method,
          externalReference: draft.externalReference.trim() || null,
          notes: draft.notes.trim() || null,
          isException: draft.isException
        } : null;
      }).filter((item): item is NonNullable<typeof item> => item !== null)
    );
    if (!items.length || items.some(item =>
      !Number.isFinite(item.settledAmount) || item.settledAmount < 0)) {
      this.error.set('Every batch allocation needs a valid zero-or-positive settled amount.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.service.reconcileBatch(batch.id, {
      expectedBatchVersion: batch.version,
      idempotencyKey: this.batchReconciliationIdempotencyKey,
      items
    }).subscribe({
      next: result => {
        this.batchReconciliations.set(result.items);
        this.batchReconciliationIdempotencyKey = crypto.randomUUID();
        this.busy.set(false);
        this.loadBatchReconciliationDetails();
      },
      error: error => {
        this.error.set(error instanceof HttpErrorResponse && error.status === 409
          ? 'Batch or payment reconciliation state changed. Reload the batch before retrying.'
          : 'Unable to record batch reconciliation. Verify access and evidence.');
        this.busy.set(false);
      }
    });
  }

  generateBankFile(): void {
    const batch = this.paymentBatch();
    if (!batch || !this.canGenerateBankFile() || !this.bankFileReason.trim()) {
      this.error.set('Bank-file generation permissions and a generation reason are required.');
      return;
    }

    this.busy.set(true);
    this.error.set('');
    this.service.generateBankFile(batch.id, {
      expectedVersion: batch.version,
      idempotencyKey: this.bankFileIdempotencyKey,
      reason: this.bankFileReason.trim()
    }).subscribe({
      next: response => {
        if (!response.body) {
          this.error.set('The generated bank file was empty.');
          this.busy.set(false);
          return;
        }
        const disposition = response.headers.get('Content-Disposition') ?? '';
        const filename = /filename="?([^";]+)"?/i.exec(disposition)?.[1]
          ?? `payroll-bank-${batch.batchReference}.csv`;
        const url = URL.createObjectURL(response.body);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = filename;
        anchor.click();
        URL.revokeObjectURL(url);
        this.bankFileIdempotencyKey = crypto.randomUUID();
        this.bankFileReason = '';
        this.loadBatch();
        this.busy.set(false);
      },
      error: error => {
        this.error.set(error instanceof HttpErrorResponse && error.status === 409
          ? 'The payment batch changed or the file request conflicts. Refresh batch details.'
          : 'Unable to generate the bank file. Verify access and batch status.');
        this.busy.set(false);
      }
    });
  }

  submitBatch(): void {
    const batch = this.paymentBatch();
    if (!batch || !this.canSubmitBatch()
      || !this.submissionExternalReference.trim() || !this.submissionReason.trim()) {
      this.error.set('Batch submission permissions, reference, and reason are required.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.service.submitBatch(batch.id, {
      expectedVersion: batch.version,
      idempotencyKey: this.batchSubmissionIdempotencyKey,
      externalReference: this.submissionExternalReference.trim(),
      reason: this.submissionReason.trim()
    }).subscribe({
      next: submitted => {
        this.paymentBatch.set(submitted);
        this.batchSubmissionIdempotencyKey = crypto.randomUUID();
        this.submissionExternalReference = '';
        this.submissionReason = '';
        this.busy.set(false);
      },
      error: error => {
        this.error.set(error instanceof HttpErrorResponse && error.status === 409
          ? 'The payment batch changed or was already submitted. Refresh batch details.'
          : 'Unable to record the batch submission. Verify access and batch status.');
        this.busy.set(false);
      }
    });
  }

  private validEvidenceInput(): boolean {
    return !!this.evidenceAllocationNo && this.evidenceAllocationNo > 0
      && !!this.evidenceAmount && this.evidenceAmount > 0
      && !!this.evidenceDate && !!this.evidenceReference.trim();
  }

  private perform(
    request: Observable<PayrollPayment>,
    errorMessage: string,
    onSuccess: () => void = () => undefined): void {
    this.busy.set(true);
    this.error.set('');
    request.subscribe({
      next: payment => {
        this.payment.set(payment);
        this.paymentId = payment.id;
        this.evidenceAllocationNo = payment.allocations[0]?.allocationNo ?? null;
        this.actionReason = '';
        this.evidenceAmount = null;
        this.evidenceReference = '';
        this.resolutionEvidenceReference = '';
        this.retryAmount = null;
        onSuccess();
        this.busy.set(false);
      },
      error: error => {
        this.error.set(error instanceof HttpErrorResponse && error.status === 409
          ? 'The payment changed or the evidence conflicts with an existing record. Refresh before retrying.'
          : errorMessage);
        this.busy.set(false);
      }
    });
  }

  private allocations(): PayrollPaymentAllocationInput[] {
    const entries: Array<[PayrollPaymentAllocationInput['method'], number | null]> = [
      ['Cash', this.cashAmount],
      ['Bank', this.bankAmount],
      ['Mobile', this.mobileAmount]
    ];
    return entries
      .filter(([, amount]) => amount !== null && Number.isFinite(amount) && amount > 0)
      .map(([method, amount]) => ({
        method,
        amount: amount!,
        externalReference: this.externalReference.trim() || null
      }));
  }
}
