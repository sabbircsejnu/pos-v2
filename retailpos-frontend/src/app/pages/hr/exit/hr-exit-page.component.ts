import { DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';
import { UI_KIT, UiBadgeVariant, UiTableColumn } from '../../../components/shared/ui';
import { AuthService } from '../../../services/auth.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';
import {
  HrClearanceItem,
  HrClearanceTemplate,
  HrExitCase,
  HrExitCompletionPreview,
  HrExitFilters,
  HrExitService,
  HrExitRequest,
  PayrollFinalSettlement,
  ApprovalDecisionResult,
  FinalSettlementPaymentRequest,
  PayrollFinalSettlementExecution
} from '../../../services/hr-exit.service';

@Component({
  selector: 'app-hr-exit-page',
  standalone: true,
  imports: [DecimalPipe, FormsModule, ...UI_KIT],
  styles: [`
    :host {
      display: block;
    }

    .ex-page {
      display: flex;
      flex-direction: column;
      gap: var(--rp-space-4);
    }

    .ex-form-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
      gap: var(--rp-space-4);
      align-items: start;
    }

    .ex-span-all {
      grid-column: 1 / -1;
    }

    .ex-form-actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--rp-space-2);
    }

    .ex-subheading {
      margin: 0 0 var(--rp-space-2);
      font-size: var(--rp-text-body-size);
      font-weight: var(--rp-font-weight-semibold);
    }

    .ex-checklist-row {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(10rem, 1fr));
      gap: var(--rp-space-3);
      align-items: end;
      margin-bottom: var(--rp-space-3);
      padding: var(--rp-space-3);
      border: 1px solid var(--rp-color-border);
      border-radius: var(--rp-radius-md);
    }

    .ex-section-title {
      margin: 0 0 var(--rp-space-3);
      font-size: var(--rp-text-section-size);
      line-height: var(--rp-text-section-line);
      font-weight: var(--rp-font-weight-semibold);
    }

    .ex-filters {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: var(--rp-space-3);
      width: 100%;
    }

    .ex-filters app-ui-form-field {
      flex: 1 1 10rem;
      max-width: 16rem;
    }

    @media (max-width: 639px) {
      .ex-filters app-ui-form-field {
        max-width: none;
      }
    }

    .ex-cell {
      display: flex;
      flex-direction: column;
      gap: var(--rp-space-1);
      min-width: 12rem;
    }

    .ex-cell--wide {
      min-width: 20rem;
    }

    .ex-cell--actions {
      min-width: 24rem;
      gap: var(--rp-space-3);
    }

    .ex-muted {
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-helper-size);
      line-height: var(--rp-text-helper-line);
    }

    .ex-text {
      margin: 0;
    }

    .ex-item {
      display: flex;
      flex-direction: column;
      gap: var(--rp-space-2);
      padding-bottom: var(--rp-space-2);
      border-bottom: 1px solid var(--rp-color-border);
    }

    .ex-item__head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--rp-space-2);
    }

    .ex-item__edit {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--rp-space-2);
    }

    .ex-item__edit .ui-control {
      flex: 1 1 9rem;
      width: auto;
      min-width: 0;
    }

    .ex-buttons {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--rp-space-2);
    }

    .ex-panel {
      display: flex;
      flex-direction: column;
      gap: var(--rp-space-2);
      padding: var(--rp-space-3);
      border: 1px solid var(--rp-color-border);
      border-radius: var(--rp-radius-md);
      background: var(--rp-color-surface-muted);
    }

    .ex-panel p,
    .ex-panel h3,
    .ex-panel h4,
    .ex-panel h5 {
      margin: 0;
    }

    .ex-panel h3,
    .ex-panel h4,
    .ex-panel h5 {
      font-size: var(--rp-text-body-size);
      font-weight: var(--rp-font-weight-semibold);
    }

    .ex-list {
      margin: 0;
      padding: 0 0 0 var(--rp-space-4);
      list-style: disc;
    }

    .ex-list--plain {
      padding: 0;
      list-style: none;
    }

    .ex-hash {
      overflow-wrap: anywhere;
      font-family: var(--rp-font-family-mono);
      font-size: var(--rp-text-helper-size);
    }
  `],
  template: `<main class="container mx-auto px-4 py-3">
    <app-ui-page-header
      title="Employee Exit and Clearance"
      description="Final settlement uses persisted Payroll evidence for approved exits. Approval freezes the calculation; Accounting and payment execution remain separate auditable actions."
      [breadcrumbs]="breadcrumbs" />

    <div class="ex-page">
      @if (error()) {
        <app-ui-alert variant="danger">{{error()}}</app-ui-alert>
      }
      @if (notice()) {
        <app-ui-alert variant="success">{{notice()}}</app-ui-alert>
      }
      @if (busy()) {
        <app-ui-state kind="loading" [compact]="true" title="Loading exit cases..." />
      }

      @if (canClearance) {
        <app-ui-card title="Clearance templates" subtitle="Templates are immutable once created; create a new version to change one.">
          <form class="ex-form-grid" (ngSubmit)="createTemplate()">
            <app-ui-form-field label="Code" [required]="true">
              <input appUiControl name="templateCode" maxlength="50" required [(ngModel)]="template.code">
            </app-ui-form-field>
            <app-ui-form-field label="Name" [required]="true">
              <input appUiControl name="templateName" maxlength="100" required [(ngModel)]="template.name">
            </app-ui-form-field>
            <app-ui-form-field label="Scope">
              <select appUiControl name="templateScope" [(ngModel)]="template.scopeType">
                <option value="Business">Business</option><option value="LocationType">Location type</option>
                <option value="Location">Location</option>
              </select>
            </app-ui-form-field>
            @if (template.scopeType !== 'Business') {
              <app-ui-form-field label="Scope reference" [required]="true">
                <input appUiControl name="templateScopeReference" required [(ngModel)]="template.scopeReference">
              </app-ui-form-field>
            }
            <app-ui-form-field label="Version" [required]="true">
              <input appUiControl name="templateVersion" type="number" min="1" required [(ngModel)]="template.versionNo">
            </app-ui-form-field>
            <app-ui-form-field label="Effective from" [required]="true">
              <input appUiControl name="templateFrom" type="date" appCompanyDate required [(ngModel)]="template.effectiveFrom">
            </app-ui-form-field>
            <app-ui-form-field label="Effective to">
              <input appUiControl name="templateTo" type="date" appCompanyDate [(ngModel)]="template.effectiveTo">
            </app-ui-form-field>
            <label class="ui-check">
              <input appUiControl name="templateActive" type="checkbox" [(ngModel)]="template.isActive">
              <span>Active</span>
            </label>
            <div class="ex-span-all">
              <h3 class="ex-subheading">Checklist items</h3>
              @for (item of template.items; track $index; let index = $index) {
                <div class="ex-checklist-row">
                  <app-ui-form-field label="Sequence" [required]="true">
                    <input appUiControl [name]="'sequence' + index" type="number" min="1" required [(ngModel)]="item.sequence">
                  </app-ui-form-field>
                  <app-ui-form-field label="Code" [required]="true">
                    <input appUiControl [name]="'itemCode' + index" maxlength="50" required [(ngModel)]="item.code">
                  </app-ui-form-field>
                  <app-ui-form-field label="Name" [required]="true">
                    <input appUiControl [name]="'itemName' + index" maxlength="100" required [(ngModel)]="item.name">
                  </app-ui-form-field>
                  <app-ui-form-field label="Responsible role">
                    <input appUiControl [name]="'itemRole' + index" maxlength="100" [(ngModel)]="item.responsibleRole">
                  </app-ui-form-field>
                  <app-ui-form-field label="Responsible reference">
                    <input appUiControl [name]="'itemReference' + index" maxlength="150" [(ngModel)]="item.responsibleReference">
                  </app-ui-form-field>
                  <label class="ui-check">
                    <input appUiControl [name]="'mandatory' + index" type="checkbox" [(ngModel)]="item.isMandatory">
                    <span>Mandatory</span>
                  </label>
                  @if (template.items.length > 1) {
                    <button appUiButton variant="outline" size="sm" type="button" (click)="removeTemplateItem(index)">Remove item</button>
                  }
                </div>
              }
              <button appUiButton variant="outline" type="button" (click)="addTemplateItem()">Add checklist item</button>
            </div>
            <div class="ex-span-all ex-form-actions">
              <button appUiButton type="submit" [disabled]="busy()">Create immutable template version</button>
            </div>
          </form>

          <app-ui-data-table
            [columns]="templateColumns"
            [rows]="templates()"
            rowKey="id"
            ariaLabel="Clearance templates"
            density="compact"
            [searchable]="false"
            emptyTitle="No clearance templates are configured.">
            <ng-template uiTableCell="code" let-row>{{row.code}} v{{row.versionNo}}</ng-template>
            <ng-template uiTableCell="scope" let-row>
              {{row.scopeType}}
              @if (row.scopeReference) {
                <span>({{row.scopeReference}})</span>
              }
            </ng-template>
            <ng-template uiTableCell="effective" let-row>{{ (row.effectiveFrom | appDate) }}–{{ (row.effectiveTo | appDate) || 'open' }}</ng-template>
          </app-ui-data-table>
        </app-ui-card>
      }

      @if (canInitiate) {
        <app-ui-card title="Initiate non-financial exit">
          <form class="ex-form-grid" (ngSubmit)="initiate()">
            <app-ui-form-field label="Employee ID" [required]="true">
              <input appUiControl name="employeeId" type="number" min="1" required [(ngModel)]="newExit.employeeId">
            </app-ui-form-field>
            <app-ui-form-field label="Exit type" [required]="true">
              <select appUiControl name="exitType" required [(ngModel)]="newExit.exitType">
                <option value="Resignation">Resignation</option><option value="Termination">Termination</option>
                <option value="ContractCompletion">Contract completion</option><option value="Retirement">Retirement</option>
              </select>
            </app-ui-form-field>
            <app-ui-form-field label="Initiated date" [required]="true">
              <input appUiControl name="initiatedDate" type="date" appCompanyDate required [(ngModel)]="newExit.initiatedDate">
            </app-ui-form-field>
            <app-ui-form-field label="Proposed last working date" [required]="true">
              <input appUiControl name="proposedDate" type="date" appCompanyDate required [(ngModel)]="newExit.proposedLastWorkingDate">
            </app-ui-form-field>
            <app-ui-form-field class="ex-span-all" label="Reason" [required]="true">
              <textarea appUiControl name="exitReason" required maxlength="1000" [(ngModel)]="newExit.reason"></textarea>
            </app-ui-form-field>
            <div class="ex-span-all ex-form-actions">
              <button appUiButton type="submit" [disabled]="busy()">Create exit case</button>
            </div>
          </form>
        </app-ui-card>
      }

      <section aria-labelledby="exit-cases-heading">
        <h2 id="exit-cases-heading" class="ex-section-title">Exit cases</h2>
        <app-ui-data-table
          ariaLabel="Exit cases"
          [columns]="caseColumns"
          [rows]="cases()"
          rowKey="id"
          [searchable]="false"
          [emptyTitle]="searched() ? 'No exit cases match these filters.' : 'No exit cases to show yet.'">
          <form uiTableToolbar class="ex-filters" (ngSubmit)="search()">
            <app-ui-form-field label="Initiated from">
              <input appUiControl name="from" type="date" appCompanyDate [(ngModel)]="filters.from">
            </app-ui-form-field>
            <app-ui-form-field label="To">
              <input appUiControl name="to" type="date" appCompanyDate [(ngModel)]="filters.to">
            </app-ui-form-field>
            <app-ui-form-field label="Employee ID">
              <input appUiControl name="filterEmployee" type="number" min="1" [(ngModel)]="filters.employeeId">
            </app-ui-form-field>
            <app-ui-form-field label="Status">
              <select appUiControl name="status" [(ngModel)]="filters.status">
                <option value="">All statuses</option><option>Draft</option><option>PendingApproval</option>
                <option>Approved</option><option>Rejected</option><option>Cancelled</option><option>Completed</option>
              </select>
            </app-ui-form-field>
            <button appUiButton type="submit" [disabled]="busy()">Search</button>
          </form>

          <ng-template uiTableCell="employee" let-row>
            <div class="ex-cell">
              <strong>{{row.employeeCode}} — {{row.employeeName}} ({{row.employeeId}})</strong>
              <span class="ex-muted">{{row.exitType}} · {{row.reason}}</span>
            </div>
          </ng-template>
          <ng-template uiTableCell="dates" let-row>
            <div class="ex-cell">
              <span>Initiated {{ (row.initiatedDate | appDate) }} · proposed {{ (row.proposedLastWorkingDate | appDate) }}</span>
              <span class="ex-muted">actual {{ (row.actualLastWorkingDate | appDate) || '—' }} · location {{row.primaryLocationProjectionId || '—'}} / {{row.locationType || '—'}}</span>
            </div>
          </ng-template>
          <ng-template uiTableCell="status" let-row>
            <div class="ex-cell">
              <span><app-ui-badge [variant]="exitStatusVariant(row.status)">{{row.status}}</app-ui-badge> · v{{row.version}}</span>
              <span class="ex-muted">Template {{row.clearanceTemplateId}} v{{row.clearanceTemplateVersionNo}}
                @if (row.approvalInstanceId) {
                  <span> · approval {{row.approvalInstanceId}}</span>
                }</span>
            </div>
          </ng-template>
          <ng-template uiTableCell="clearance" let-row>
            <div class="ex-cell ex-cell--wide">
              @for (item of row.clearanceItems; track item.id) {
                <div class="ex-item">
                  <div class="ex-item__head">
                    <strong>{{item.sequence}}. {{item.name}}</strong> ·
                    <app-ui-badge [variant]="clearanceStatusVariant(item.status)">{{item.status}}</app-ui-badge>
                    @if (item.isMandatory) {
                      <span class="ex-muted">(mandatory)</span>
                    }
                  </div>
                  @if (item.comment) {
                    <p class="ex-text ex-muted">Reason: {{item.comment}}</p>
                  }
                  @if (canClearance && (row.status === 'Draft' || row.status === 'Approved')) {
                    <div class="ex-item__edit">
                      <select appUiControl [attr.aria-label]="'Status for ' + item.name" [name]="'itemStatus' + item.id" [(ngModel)]="itemStatus[item.id]">
                        <option>Pending</option><option>Cleared</option><option>Exception</option><option>NotApplicable</option>
                      </select>
                      <input appUiControl [attr.aria-label]="'Reason / comment for ' + item.name" [name]="'itemComment' + item.id" placeholder="Reason / comment" [(ngModel)]="itemComment[item.id]">
                      <input appUiControl [attr.aria-label]="'Evidence reference for ' + item.name" [name]="'itemEvidence' + item.id" placeholder="Evidence reference" [(ngModel)]="itemEvidence[item.id]">
                      <button appUiButton variant="secondary" size="sm" type="button" [disabled]="busy()" [attr.aria-label]="'Save ' + item.name" (click)="saveClearance(row, item)">Save</button>
                    </div>
                  }
                </div>
              }
            </div>
          </ng-template>
          <ng-template uiTableCell="actions" let-row>
            <div class="ex-cell ex-cell--actions">
              <div class="ex-buttons">
                @if (canInitiate && row.status === 'Draft') {
                  <button appUiButton size="sm" type="button" [disabled]="busy()" (click)="submit(row)">Submit</button>
                }
                @if (canInitiate && (row.status === 'Draft' || row.status === 'PendingApproval')) {
                  <button appUiButton variant="outline" size="sm" type="button" [disabled]="busy()" (click)="cancel(row)">Cancel</button>
                }
              </div>
              @if (canClearance && row.status === 'Approved') {
                <div class="ex-panel">
                  <app-ui-form-field label="Actual last working date">
                    <input appUiControl [name]="'actualDate' + row.id" type="date" appCompanyDate [(ngModel)]="actualDate[row.id]">
                  </app-ui-form-field>
                  <app-ui-form-field label="Completion reason">
                    <input appUiControl [name]="'completeReason' + row.id" [(ngModel)]="completeReason[row.id]">
                  </app-ui-form-field>
                  <div class="ex-buttons">
                    @if (canCompletionPreview) {
                      <button appUiButton variant="outline" size="sm" type="button" [disabled]="busy()" (click)="loadCompletionPreview(row)">
                        {{completionPreviews()[row.id] ? 'Refresh completion readiness' : 'Preview completion readiness'}}
                      </button>
                    }
                    <button appUiButton size="sm" type="button" [disabled]="busy()" (click)="complete(row)">Complete exit</button>
                  </div>
                  @if (completionPreviews()[row.id]; as preview) {
                    <div class="ex-panel">
                      <p>
                        Completion readiness:
                        <app-ui-badge [variant]="preview.canComplete ? 'success' : 'danger'">{{preview.canComplete ? 'Ready' : 'Blocked'}}</app-ui-badge>
                        @if (preview.payrollEnabled) {
                          <span> · payroll-backed</span>
                        }
                        @if (!preview.payrollEnabled) {
                          <span> · HR-only</span>
                        }
                      </p>
                      @if (preview.pendingMandatoryClearanceCount || preview.reasonlessNotApplicableCount) {
                        <p>
                          Pending mandatory items {{preview.pendingMandatoryClearanceCount}}
                          · reasonless NotApplicable items {{preview.reasonlessNotApplicableCount}}
                        </p>
                      }
                      @if (preview.blockers.length) {
                        <ul class="ex-list">
                          @for (blocker of preview.blockers; track $index) {
                            <li>{{blocker}}</li>
                          }
                        </ul>
                      }
                      @if (preview.settlement; as settlement) {
                        <div class="ex-panel">
                          <p>
                            Settlement revision {{settlement.revisionNo}} · {{settlement.status}}
                            · {{settlement.completionMode}} · net
                            {{settlement.netSettlement | number:'1.0-2'}} {{settlement.currencyCode}}
                          </p>
                          <p>
                            Payments {{settlement.activePaymentCount}}
                            @if (settlement.paymentRequired) {
                              <span>
                                · outstanding {{settlement.outstandingAmount | number:'1.0-2'}}
                                · satisfied {{settlement.paymentsSatisfied ? 'yes' : 'no'}}
                              </span>
                            }
                          </p>
                          <p>
                            Reconciliation {{settlement.reconciliationRequired ? 'required' : 'not required'}}
                            · {{settlement.reconciliationSatisfied ? 'satisfied' : 'unsatisfied'}}
                          </p>
                          <p>
                            Accounting {{settlement.accountingRequired ? 'required' : 'not required'}}
                            @if (settlement.requiredPostingType) {
                              <span> · {{settlement.requiredPostingType}}</span>
                            }
                            · {{settlement.accountingSatisfied ? 'satisfied' : 'unsatisfied'}}
                          </p>
                          @if (settlement.requiredPaymentAccountingCount) {
                            <p>
                              Payment evidence postings {{settlement.postedPaymentAccountingCount}} /
                              {{settlement.requiredPaymentAccountingCount}}
                            </p>
                          }
                          @if (settlement.submittedEvidenceHash) {
                            <p>
                              Frozen settlement evidence <span class="ex-hash">{{settlement.submittedEvidenceHash}}</span>
                            </p>
                          }
                        </div>
                      }
                    </div>
                  }
                </div>
              }
              @if (canFinalSettlementView && row.status === 'Approved') {
                <section class="ex-panel">
                  <h3>Payroll final settlement</h3>
                  <div class="ex-buttons">
                    @if (canFinalSettlementCalculate) {
                      <button appUiButton variant="outline" size="sm" type="button" [disabled]="busy()" (click)="calculateFinalSettlement(row)">
                        {{settlements()[row.id] ? 'Create new calculation revision' : 'Calculate settlement'}}
                      </button>
                    }
                    @if (!settlements()[row.id]) {
                      <button appUiButton variant="outline" size="sm" type="button" [disabled]="busy()" (click)="loadFinalSettlement(row)">Load existing calculation</button>
                    }
                  </div>
                  @if (settlements()[row.id]; as settlement) {
                    <p>Revision {{settlement.revisionNo}} · {{ (settlement.settlementDate | appDate) }} · {{settlement.currencyCode}}</p>
                    <p>Gross due {{settlement.grossDue | number:'1.0-' + settlement.decimalPrecision}}
                      · Deductions {{settlement.totalDeductions | number:'1.0-' + settlement.decimalPrecision}}
                      · Net {{settlement.netSettlement | number:'1.0-' + settlement.decimalPrecision}}</p>
                    <ul class="ex-list">
                      @for (line of settlement.lines; track line.lineNo) {
                        <li>
                          {{line.componentName}} · {{line.lineType}}
                          @if (line.isApplicable) {
                            <span>{{line.amount | number:'1.0-' + settlement.decimalPrecision}}</span>
                          } @else {
                            <span>Not applicable: {{line.notApplicableReason}}</span>
                          }
                        </li>
                      }
                    </ul>
                    <p>Snapshot hash: <span class="ex-hash">{{settlement.snapshotHash}}</span></p>
                    <p>Approval status: <app-ui-badge [variant]="settlementStatusVariant(settlement.status)">{{settlement.status}}</app-ui-badge>
                      @if (settlement.approvalPolicyCode) {
                        <span> · {{settlement.approvalPolicyCode}} v{{settlement.approvalPolicyVersion}}</span>
                      }
                      @if (settlement.requestedAtUtc) {
                        <span> · submitted {{ (settlement.requestedAtUtc | appDate:'medium') }}</span>
                      }
                    </p>
                    @if (settlement.submittedEvidenceHash) {
                      <p>
                        Frozen approval evidence: <span class="ex-hash">{{settlement.submittedEvidenceHash}}</span>
                      </p>
                    }
                    @if (settlement.status === 'Pending') {
                      <app-ui-form-field label="Decision reason">
                        <input appUiControl [name]="'settlementDecisionReason' + settlement.id"
                          [(ngModel)]="settlementDecisionReason[settlement.id]" maxlength="1000">
                      </app-ui-form-field>
                    }
                    <div class="ex-buttons">
                      @if (canFinalSettlementSubmit && settlement.status === 'Calculated') {
                        <button appUiButton size="sm" type="button" [disabled]="busy()" (click)="submitFinalSettlement(row, settlement)">
                          Submit for approval
                        </button>
                      }
                      @if (canFinalSettlementApprove && settlement.status === 'Pending') {
                        <button appUiButton size="sm" type="button" [disabled]="busy()" (click)="approveFinalSettlement(row, settlement)">
                          Approve
                        </button>
                      }
                      @if (canFinalSettlementReject && settlement.status === 'Pending') {
                        <button appUiButton variant="danger" size="sm" type="button" [disabled]="busy()" (click)="rejectFinalSettlement(row, settlement)">
                          Reject
                        </button>
                      }
                      @if (canFinalSettlementCancel && settlement.status === 'Pending') {
                        <button appUiButton variant="outline" size="sm" type="button" [disabled]="busy()" (click)="cancelFinalSettlement(row, settlement)">
                          Cancel approval
                        </button>
                      }
                    </div>
                    @if (settlement.status === 'Approved' && canFinalSettlementFinancialView) {
                      <section class="ex-panel">
                        <h4>Financial execution</h4>
                        <div class="ex-buttons">
                          <button appUiButton variant="outline" size="sm" type="button" [disabled]="busy()" (click)="loadFinalSettlementExecution(settlement)">
                            {{executions()[settlement.id] ? 'Refresh execution status' : 'Load execution status'}}
                          </button>
                        </div>
                        @if (executions()[settlement.id]; as execution) {
                          <p>Settlement {{execution.settlementStatus}} · revision {{execution.revisionNo}}</p>
                          <div class="ex-buttons">
                            @if (canFinalSettlementAccountingPost && settlement.netSettlement !== 0) {
                              <button appUiButton size="sm" type="button" [disabled]="busy()" (click)="postFinalSettlementAccounting(row, settlement)">
                                Post approved settlement to Accounting
                              </button>
                            }
                          </div>
                          @if (canFinalSettlementPaymentCreate && settlement.netSettlement > 0) {
                            <app-ui-form-field label="Payment amount">
                              <input appUiControl [name]="'finalSettlementPaymentAmount' + settlement.id" type="number"
                                min="0.01" step="0.01" [max]="settlement.netSettlement"
                                [(ngModel)]="paymentAmount[settlement.id]">
                            </app-ui-form-field>
                            <app-ui-form-field label="Payment method">
                              <select appUiControl [name]="'finalSettlementPaymentMethod' + settlement.id"
                                [(ngModel)]="paymentMethod[settlement.id]">
                                <option value="Cash">Cash</option><option value="Bank">Bank</option>
                                <option value="Mobile">Mobile</option>
                              </select>
                            </app-ui-form-field>
                            <div class="ex-buttons">
                              <button appUiButton size="sm" type="button" [disabled]="busy()" (click)="createFinalSettlementPayment(settlement)">
                                Create payment attempt
                              </button>
                            </div>
                          }
                          <h5>Payments</h5>
                          @if (!execution.payments.length) {
                            <p>No payment attempts recorded.</p>
                          }
                          <ul class="ex-list">
                            @for (payment of execution.payments; track payment.id) {
                              <li>
                                Payment {{payment.id}} · {{payment.method}} · <app-ui-badge [variant]="paymentStatusVariant(payment.status)">{{payment.status}}</app-ui-badge> ·
                                payable {{payment.payableAmount | number:'1.0-' + settlement.decimalPrecision}} ·
                                paid {{payment.paidAmount | number:'1.0-' + settlement.decimalPrecision}} ·
                                outstanding {{payment.outstandingAmount | number:'1.0-' + settlement.decimalPrecision}}
                                <ul class="ex-list">
                                  @for (evidence of payment.evidence; track evidence.id) {
                                    <li>
                                      {{evidence.type}} · {{evidence.amount | number:'1.0-' + settlement.decimalPrecision}} ·
                                      {{evidence.evidenceReference}} · {{ (evidence.evidenceDate | appDate) }}
                                      @if (evidence.type === 'Settlement' && canFinalSettlementAccountingPost) {
                                        <button appUiButton variant="outline" size="sm" type="button" [disabled]="busy()"
                                          (click)="postFinalSettlementPaymentAccounting(
                                            settlement, payment.id, evidence.id)">
                                          Post reconciled payment evidence
                                        </button>
                                      }
                                    </li>
                                  }
                                </ul>
                              </li>
                            }
                          </ul>
                          <h5>Accounting postings</h5>
                          @if (!execution.accountingPostings.length) {
                            <p>No Accounting posting recorded.</p>
                          }
                          <ul class="ex-list">
                            @for (posting of execution.accountingPostings; track $index) {
                              <li>
                                {{posting.postingType}} · {{posting.status}} · {{posting.postingReference}}
                                @if (posting.accountingReference) {
                                  <span> · {{posting.accountingReference}}</span>
                                }
                                @if (posting.errorMessage) {
                                  <span> · {{posting.errorMessage}}</span>
                                }
                              </li>
                            }
                          </ul>
                        }
                      </section>
                    }
                    @if (settlement.approvalHistory?.length) {
                      <h4>Approval history</h4>
                    }
                    <ul class="ex-list">
                      @for (decision of settlement.approvalHistory; track $index) {
                        <li>
                          {{decision.decision}} · user {{decision.actorUserId}} · {{ (decision.occurredAtUtc | appDate:'medium') }}
                          @if (decision.reason) {
                            <span> · {{decision.reason}}</span>
                          }
                        </li>
                      }
                    </ul>
                  }
                </section>
              }
              <ul class="ex-list ex-muted">
                @for (event of row.events; track $index) {
                  <li>{{event.eventType}} · {{event.newStatus}} · {{ (event.occurredAtUtc | appDate:'medium') }}</li>
                }
              </ul>
            </div>
          </ng-template>
        </app-ui-data-table>
      </section>
    </div>
  </main>`
})
export class HrExitPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly capability = inject(HrPayrollCapabilityStore);
  private readonly api = inject(HrExitService);
  readonly breadcrumbs = [{ label: 'HR' }, { label: 'Exit and clearance' }];
  readonly templateColumns: UiTableColumn[] = [
    { key: 'code', header: 'Code / version', sortable: true },
    { key: 'name', header: 'Name', sortable: true },
    { key: 'scope', header: 'Scope', field: 'scopeType', sortable: true },
    { key: 'effective', type: 'date', header: 'Effective', field: 'effectiveFrom', sortable: true }
  ];
  readonly caseColumns: UiTableColumn[] = [
    { key: 'employee', header: 'Employee / type', field: 'employeeName', type: 'custom', sortable: true },
    { key: 'dates', header: 'Dates / location', field: 'initiatedDate', type: 'custom', sortable: true },
    { key: 'status', header: 'Status / version', field: 'status', type: 'custom', sortable: true },
    { key: 'clearance', header: 'Clearance', type: 'custom' },
    { key: 'actions', header: 'Actions / history', type: 'custom' }
  ];
  private readonly today = new Date().toISOString().slice(0, 10);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly searched = signal(false);
  readonly cases = signal<HrExitCase[]>([]);
  readonly templates = signal<HrClearanceTemplate[]>([]);
  readonly completionPreviews = signal<Record<number, HrExitCompletionPreview>>({});
  readonly settlements = signal<Record<number, PayrollFinalSettlement>>({});
  readonly executions = signal<Record<number, PayrollFinalSettlementExecution>>({});
  readonly canView = this.auth.hasPermission('hr.exit.view');
  readonly canInitiate = this.auth.hasPermission('hr.exit.initiate');
  readonly canClearance = this.auth.hasPermission('hr.exit.clearance');
  readonly canCompletionPreview = this.auth.hasPermission('hr.exit.clearance')
    && (!this.capability.isEnabled('payroll.enabled')
      || (this.auth.hasPermission('payroll.final_settlement.view')
        && this.auth.hasPermission('payroll.salary.view')));
  readonly canFinalSettlementView = this.auth.hasPermission('payroll.final_settlement.view')
    && this.auth.hasPermission('payroll.salary.view')
    && this.capability.isEnabled('payroll.enabled');
  readonly canFinalSettlementCalculate = this.auth.hasPermission('payroll.final_settlement.calculate')
    && this.capability.isEnabled('payroll.enabled');
  readonly canFinalSettlementSubmit = this.auth.hasPermission('payroll.final_settlement.submit')
    && this.capability.isEnabled('payroll.enabled');
  readonly canFinalSettlementApprove = this.auth.hasPermission('payroll.final_settlement.approve')
    && this.capability.isEnabled('payroll.enabled');
  readonly canFinalSettlementReject = this.auth.hasPermission('payroll.final_settlement.reject')
    && this.capability.isEnabled('payroll.enabled');
  readonly canFinalSettlementCancel = this.auth.hasPermission('payroll.final_settlement.cancel')
    && this.capability.isEnabled('payroll.enabled');
  readonly canFinalSettlementFinancialView =
    this.auth.hasPermission('payroll.final_settlement.financial.view')
    && this.capability.isEnabled('payroll.enabled');
  readonly canFinalSettlementPaymentCreate =
    this.auth.hasPermission('payroll.final_settlement.payment.create')
    && this.capability.isEnabled('payroll.enabled');
  readonly canFinalSettlementAccountingPost =
    this.auth.hasPermission('payroll.final_settlement.accounting.post')
    && this.capability.isEnabled('payroll.enabled');
  readonly filters: HrExitFilters = { from: '', to: '', employeeId: null, status: '' };
  readonly newExit: HrExitRequest = {
    employeeId: 0, exitType: 'Resignation', initiatedDate: this.today,
    proposedLastWorkingDate: this.today, reason: ''
  };
  template: {
    code: string;
    name: string;
    scopeType: 'Business' | 'LocationType' | 'Location';
    scopeReference: string | null;
    versionNo: number;
    effectiveFrom: string;
    effectiveTo: string | null;
    isActive: boolean;
    items: import('../../../services/hr-exit.service').HrClearanceItemInput[];
  } = this.newTemplate();
  itemStatus: Record<number, string> = {};
  itemComment: Record<number, string> = {};
  itemEvidence: Record<number, string> = {};
  actualDate: Record<number, string> = {};
  completeReason: Record<number, string> = {};
  private readonly settlementIdempotencyKeys: Record<number, string> = {};
  settlementDecisionReason: Record<number, string> = {};
  paymentAmount: Record<number, number | null> = {};
  paymentMethod: Record<number, 'Cash' | 'Bank' | 'Mobile'> = {};
  private readonly paymentIdempotencyKeys: Record<number, string> = {};
  private readonly accountingIdempotencyKeys: Record<string, string> = {};
  private readonly settlementSubmissionKeys: Record<string, string> = {};

  ngOnInit() {
    if (!this.capability.isEnabled('hr.enabled')) {
      this.error.set('HR capability is disabled.');
      return;
    }
    if (this.canView) this.search();
    if (this.canClearance) this.loadTemplates();
  }

  search() {
    if (this.busy() || !this.capability.isEnabled('hr.enabled') || !this.canView) return;
    this.busy.set(true);
    this.clearMessages();
    this.api.cases(this.filters).subscribe({
      next: rows => {
        this.cases.set(rows);
        this.completionPreviews.set({});
        this.searched.set(true);
        this.busy.set(false);
      },
      error: error => this.fail(error, 'Unable to load exit cases.')
    });
  }

  createTemplate() {
    if (this.busy() || !this.canClearance || !this.capability.isEnabled('hr.enabled')) return;
    this.busy.set(true);
    this.clearMessages();
    this.api.createTemplate(this.template).subscribe({
      next: () => {
        this.notice.set('Clearance template version created.');
        this.template = this.newTemplate();
        this.busy.set(false);
        this.loadTemplates();
      },
      error: error => this.fail(error, 'Unable to create clearance template.')
    });
  }

  addTemplateItem() {
    this.template.items.push({
      sequence: this.template.items.length + 1, code: '', name: '',
      responsibleRole: null, responsibleReference: null, isMandatory: true
    });
  }

  removeTemplateItem(index: number) {
    if (this.template.items.length > 1) this.template.items.splice(index, 1);
    this.template.items.forEach((item, itemIndex) => item.sequence = itemIndex + 1);
  }

  initiate() {
    if (this.busy() || !this.canInitiate || !this.capability.isEnabled('hr.enabled')) return;
    this.busy.set(true);
    this.clearMessages();
    this.api.initiate(this.newExit).subscribe({
      next: row => {
        this.notice.set(`Exit case ${row.id} created.`);
        this.cases.update(rows => [row, ...rows]);
        this.newExit.reason = '';
        this.busy.set(false);
      },
      error: error => this.fail(error, 'Unable to initiate employee exit.')
    });
  }

  submit(row: HrExitCase) {
    if (!this.canInitiate || row.status !== 'Draft') return;
    this.runAction(this.api.submit(row), 'Exit case submitted.', 'Unable to submit exit case.');
  }

  cancel(row: HrExitCase) {
    if (!this.canInitiate || !window.confirm(`Cancel exit case ${row.id}?`)) return;
    this.runAction(this.api.cancel(row, 'Cancelled from the exit workbench.'),
      'Exit case cancelled.', 'Unable to cancel exit case.');
  }

  saveClearance(row: HrExitCase, item: HrClearanceItem) {
    if (!this.canClearance) return;
    const status = this.itemStatus[item.id] ?? item.status;
    this.runAction(this.api.updateClearance(item, status,
      this.itemComment[item.id] ?? item.comment,
      this.itemEvidence[item.id] ?? item.evidenceReference),
    'Clearance item updated.', 'Unable to update clearance item.');
  }

  complete(row: HrExitCase) {
    if (this.busy() || !this.canClearance) return;
    const date = this.actualDate[row.id];
    const reason = this.completeReason[row.id]?.trim();
    if (!date || !reason) {
      this.error.set('Actual last working date and completion reason are required.');
      return;
    }
    if (!this.canCompletionPreview) {
      this.runAction(this.api.complete(row, date, reason),
      'Exit completed.', 'Unable to complete exit case.');
      return;
    }
    this.busy.set(true);
    this.clearMessages();
    this.api.completionPreview(row).subscribe({
      next: preview => {
        this.completionPreviews.update(rows => ({ ...rows, [row.id]: preview }));
        if (!preview.canComplete) {
          this.error.set(preview.blockers[0] ?? 'Exit completion is blocked.');
          this.busy.set(false);
          return;
        }
        this.api.complete(row, date, reason).subscribe({
          next: () => {
            this.notice.set('Exit completed.');
            this.busy.set(false);
            this.invalidateCompletionPreview(row.id);
            if (this.canView) this.search();
          },
          error: error => this.fail(error, 'Unable to complete exit case.')
        });
      },
      error: error => this.fail(error, 'Unable to load completion readiness.')
    });
  }

  loadCompletionPreview(row: HrExitCase) {
    if (this.busy() || !this.canCompletionPreview || row.status !== 'Approved') return;
    this.busy.set(true);
    this.clearMessages();
    this.api.completionPreview(row).subscribe({
      next: preview => {
        this.completionPreviews.update(rows => ({ ...rows, [row.id]: preview }));
        this.notice.set(preview.canComplete
          ? 'Exit completion is ready.'
          : 'Exit completion blockers were refreshed.');
        this.busy.set(false);
      },
      error: error => this.fail(error, 'Unable to load completion readiness.')
    });
  }

  calculateFinalSettlement(row: HrExitCase) {
    if (this.busy() || !this.canFinalSettlementCalculate
      || !this.capability.isEnabled('payroll.enabled') || row.status !== 'Approved') return;
    if (this.settlements()[row.id]
      && !window.confirm('This creates a new immutable settlement calculation revision. Continue?')) return;
    this.busy.set(true);
    this.clearMessages();
    const idempotencyKey = this.settlementIdempotencyKeys[row.id]
      ?? crypto.randomUUID();
    this.settlementIdempotencyKeys[row.id] = idempotencyKey;
    this.api.calculateFinalSettlement(row, idempotencyKey).subscribe({
      next: settlement => {
        this.settlements.update(rows => ({ ...rows, [row.id]: settlement }));
        this.invalidateCompletionPreview(row.id);
        delete this.settlementIdempotencyKeys[row.id];
        this.notice.set(settlement.isReplay
          ? `Settlement calculation revision ${settlement.revisionNo} was returned from the prior request.`
          : `Settlement calculation revision ${settlement.revisionNo} was created.`);
        this.busy.set(false);
      },
      error: error => this.fail(error, 'Unable to calculate final settlement.')
    });
  }

  loadFinalSettlement(row: HrExitCase) {
    if (this.busy() || !this.canFinalSettlementView
      || !this.capability.isEnabled('payroll.enabled')) return;
    this.busy.set(true);
    this.clearMessages();
    this.api.finalSettlementForExit(row).subscribe({
      next: settlement => {
        this.settlements.update(rows => ({ ...rows, [row.id]: settlement }));
        this.invalidateCompletionPreview(row.id);
        this.busy.set(false);
      },
      error: error => {
        if (error?.status === 404) {
          this.notice.set('No final settlement calculation exists for this exit case.');
          this.busy.set(false);
          return;
        }
        this.fail(error, 'Unable to load final settlement.');
      }
    });
  }

  submitFinalSettlement(row: HrExitCase, settlement: PayrollFinalSettlement) {
    if (!this.canFinalSettlementSubmit || settlement.status !== 'Calculated') return;
    const key = `${settlement.id}:${settlement.revisionNo}`;
    const idempotencyKey = this.settlementSubmissionKeys[key] ?? crypto.randomUUID();
    this.settlementSubmissionKeys[key] = idempotencyKey;
    this.runFinalSettlementDecision(row,
      this.api.submitFinalSettlement(settlement, idempotencyKey),
      'Final settlement submitted for approval.', 'Unable to submit final settlement.');
  }

  approveFinalSettlement(row: HrExitCase, settlement: PayrollFinalSettlement) {
    if (!this.canFinalSettlementApprove || settlement.status !== 'Pending') return;
    this.runFinalSettlementDecision(row,
      this.api.approveFinalSettlement(settlement, this.settlementDecisionReason[settlement.id] ?? ''),
      'Final settlement approved.', 'Unable to approve final settlement.');
  }

  rejectFinalSettlement(row: HrExitCase, settlement: PayrollFinalSettlement) {
    if (!this.canFinalSettlementReject || settlement.status !== 'Pending') return;
    const reason = this.settlementDecisionReason[settlement.id]?.trim();
    if (!reason) {
      this.error.set('A rejection reason is required.');
      return;
    }
    this.runFinalSettlementDecision(row,
      this.api.rejectFinalSettlement(settlement, reason),
      'Final settlement rejected.', 'Unable to reject final settlement.');
  }

  cancelFinalSettlement(row: HrExitCase, settlement: PayrollFinalSettlement) {
    if (!this.canFinalSettlementCancel || settlement.status !== 'Pending') return;
    const reason = this.settlementDecisionReason[settlement.id]?.trim();
    if (!reason) {
      this.error.set('A cancellation reason is required.');
      return;
    }
    this.runFinalSettlementDecision(row,
      this.api.cancelFinalSettlement(settlement, reason),
      'Final settlement approval cancelled.', 'Unable to cancel final settlement approval.');
  }

  loadFinalSettlementExecution(settlement: PayrollFinalSettlement) {
    if (this.busy() || !this.canFinalSettlementFinancialView
      || !this.capability.isEnabled('payroll.enabled')) return;
    this.busy.set(true);
    this.clearMessages();
    this.api.finalSettlementExecution(settlement).subscribe({
      next: execution => {
        this.executions.update(rows => ({ ...rows, [settlement.id]: execution }));
        this.busy.set(false);
      },
      error: error => this.fail(error, 'Unable to load final settlement execution status.')
    });
  }

  createFinalSettlementPayment(settlement: PayrollFinalSettlement) {
    const amount = this.paymentAmount[settlement.id] ?? settlement.netSettlement;
    if (this.busy() || !this.canFinalSettlementPaymentCreate
      || settlement.status !== 'Approved' || settlement.netSettlement <= 0
      || !Number.isFinite(amount) || amount <= 0 || amount > settlement.netSettlement) {
      this.error.set('Enter a positive payment amount no greater than the approved payable amount.');
      return;
    }
    const request: FinalSettlementPaymentRequest = {
      revisionNo: settlement.revisionNo,
      idempotencyKey: this.paymentIdempotencyKeys[settlement.id] ?? crypto.randomUUID(),
      allocations: [{
        method: this.paymentMethod[settlement.id] ?? 'Cash',
        amount
      }]
    };
    this.paymentIdempotencyKeys[settlement.id] = request.idempotencyKey;
    this.runExecutionAction(settlement,
      this.api.createFinalSettlementPayment(settlement, request),
      'Final settlement payment attempt created.', 'Unable to create final settlement payment.',
      () => delete this.paymentIdempotencyKeys[settlement.id]);
  }

  postFinalSettlementAccounting(row: HrExitCase, settlement: PayrollFinalSettlement) {
    if (this.busy() || !this.canFinalSettlementAccountingPost
      || settlement.status !== 'Approved' || settlement.netSettlement === 0) return;
    const key = `${settlement.id}:${settlement.revisionNo}`;
    const idempotencyKey = this.accountingIdempotencyKeys[key] ?? crypto.randomUUID();
    this.accountingIdempotencyKeys[key] = idempotencyKey;
    this.runExecutionAction(settlement,
      this.api.postFinalSettlementAccounting(settlement, idempotencyKey),
      'Final settlement Accounting posting processed.',
      'Unable to post final settlement to Accounting.',
      () => delete this.accountingIdempotencyKeys[key]);
  }

  postFinalSettlementPaymentAccounting(
    settlement: PayrollFinalSettlement,
    paymentId: number,
    evidenceId: number
  ) {
    if (this.busy() || !this.canFinalSettlementAccountingPost) return;
    this.runExecutionAction(settlement,
      this.api.postFinalSettlementPaymentAccounting(paymentId, evidenceId),
      'Final settlement payment Accounting posting processed.',
      'Unable to post final settlement payment evidence to Accounting.');
  }

  private runExecutionAction(
    settlement: PayrollFinalSettlement,
    request: Observable<unknown>,
    success: string,
    failure: string,
    afterSuccess: () => void = () => undefined
  ) {
    if (this.busy() || !this.capability.isEnabled('payroll.enabled')) return;
    this.busy.set(true);
    this.clearMessages();
    request.subscribe({
      next: () => {
        afterSuccess();
        this.api.finalSettlementExecution(settlement).subscribe({
          next: execution => {
            this.executions.update(rows => ({ ...rows, [settlement.id]: execution }));
            this.invalidateCompletionPreview(settlement.exitCaseId);
            this.notice.set(success);
            this.busy.set(false);
          },
          error: error => this.fail(error, failure)
        });
      },
      error: error => this.fail(error, failure)
    });
  }

  private runFinalSettlementDecision(
    row: HrExitCase,
    request: Observable<ApprovalDecisionResult>,
    success: string,
    failure: string
  ) {
    if (this.busy() || !this.capability.isEnabled('payroll.enabled')) return;
    this.busy.set(true);
    this.clearMessages();
    request.subscribe({
      next: () => this.api.finalSettlementForExit(row).subscribe({
        next: settlement => {
          this.settlements.update(rows => ({ ...rows, [row.id]: settlement }));
          this.invalidateCompletionPreview(row.id);
          this.settlementDecisionReason[settlement.id] = '';
          this.notice.set(success);
          this.busy.set(false);
        },
        error: error => this.fail(error, failure)
      }),
      error: error => this.fail(error, failure)
    });
  }

  exitStatusVariant(status: string): UiBadgeVariant {
    switch (status) {
      case 'Approved': return 'success';
      case 'Completed': return 'info';
      case 'PendingApproval': return 'warning';
      case 'Rejected': return 'danger';
      default: return 'neutral';
    }
  }

  clearanceStatusVariant(status: string): UiBadgeVariant {
    switch (status) {
      case 'Cleared': return 'success';
      case 'Exception': return 'danger';
      case 'Pending': return 'warning';
      default: return 'neutral';
    }
  }

  settlementStatusVariant(status: string): UiBadgeVariant {
    switch (status) {
      case 'Approved': return 'success';
      case 'Pending': return 'warning';
      case 'Rejected': return 'danger';
      case 'Calculated': return 'info';
      default: return 'neutral';
    }
  }

  paymentStatusVariant(status: string): UiBadgeVariant {
    const value = status.toLowerCase();
    if (value.includes('fail') || value.includes('reject') || value.includes('cancel')) return 'danger';
    if (value.includes('paid') || value.includes('verified') || value.includes('reconciled')) return 'success';
    if (value.includes('pending') || value.includes('initiated')) return 'warning';
    return 'neutral';
  }

  private loadTemplates() {
    this.api.templates().subscribe({
      next: rows => this.templates.set(rows as HrClearanceTemplate[]),
      error: error => this.fail(error, 'Unable to load authorized clearance templates.')
    });
  }

  private runAction(request: Observable<unknown>, success: string, failure: string) {
    if (this.busy() || !this.capability.isEnabled('hr.enabled')) return;
    this.busy.set(true);
    this.clearMessages();
    request.subscribe({
      next: () => {
        this.notice.set(success);
        this.busy.set(false);
        if (this.canView) this.search();
      },
      error: error => this.fail(error, failure)
    });
  }

  private newTemplate() {
    return {
      code: '', name: '', scopeType: 'Business' as const, scopeReference: null as string | null,
      versionNo: 1, effectiveFrom: this.today, effectiveTo: null as string | null, isActive: true,
      items: [{ sequence: 1, code: '', name: '', responsibleRole: null,
        responsibleReference: null, isMandatory: true }]
    };
  }

  private fail(error: any, fallback: string) {
    this.error.set(error?.error?.detail ?? fallback);
    this.busy.set(false);
  }

  private clearMessages() {
    this.error.set('');
    this.notice.set('');
  }

  private invalidateCompletionPreview(exitCaseId: number) {
    this.completionPreviews.update(rows => {
      const next = { ...rows };
      delete next[exitCaseId];
      return next;
    });
  }
}
