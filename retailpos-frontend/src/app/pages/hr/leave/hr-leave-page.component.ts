import { CompanyDateService } from '../../../services/company-date.service';
import { inject as injectCompanyDates } from '@angular/core';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { forkJoin, of } from 'rxjs';
import { UI_KIT, UiBadgeVariant, UiTableColumn } from '../../../components/shared/ui';
import { AuthService } from '../../../services/auth.service';
import {
  HrLeaveBalance, HrLeavePolicy, HrLeaveRequest, HrLeaveService, HrLeaveType
} from '../../../services/hr-leave.service';
import { HrPayrollCapabilityStore } from '../../../services/hr-payroll-capability.store';

export interface LeaveTypeRow { id: number; code: string; name: string; pay: string; status: string }
export interface LeavePolicyRow {
  effectiveFrom: string;
  id: number; policy: string; scope: string; accrual: string; entitlement: number; period: string;
}
export interface LeaveRequestRow {
  summarySort: string;
  id: number; request: HrLeaveRequest; summary: string; reason: string; status: string;
  evidence: string; approval: string;
}
export interface LeaveApprovalRow {
  id: number; request: HrLeaveRequest; employee: string; leaveType: string; period: string;
  units: number; reason: string;
}
export interface LeaveBalanceRow {
  id: string; balance: HrLeaveBalance; leaveYear: string; type: string; status: string; details: string;
}
export interface LeaveLedgerRow {
  id: number; effectiveDate: string; transaction: string; quantity: number; reason: string; source: string;
}

@Component({
  selector: 'app-hr-leave-page',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  styles: [`
    .lv-page { display: flex; flex-direction: column; gap: var(--rp-space-4); }
    .lv-grid { display: grid; gap: var(--rp-form-gap); grid-template-columns: repeat(auto-fit, minmax(13rem, 1fr)); }
    .lv-grid--inline { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--rp-form-gap); }
    .lv-grid--inline > app-ui-form-field { flex: 1 1 13rem; max-width: 24rem; }
    .lv-checks { display: flex; flex-wrap: wrap; align-items: center; gap: var(--rp-space-3) var(--rp-space-5); margin: 0; padding: 0; border: 0; min-inline-size: 0; }
    .lv-form-actions { display: flex; flex-wrap: wrap; align-items: center; gap: var(--rp-space-2); }
    .lv-actions { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); }
    .lv-stack { display: flex; flex-direction: column; gap: var(--rp-space-4); }
    .lv-secondary { display: block; color: var(--rp-color-text-secondary); font-size: var(--rp-text-helper-size); line-height: var(--rp-text-helper-line); }
    .lv-reason { min-width: 12rem; }
    @media (max-width: 639.98px) {
      .lv-form-actions > button, .lv-grid--inline > button { flex: 1 1 100%; }
      .lv-grid--inline > app-ui-form-field { max-width: none; }
    }
  `],
  template: `<main class="mx-auto max-w-7xl px-4 py-4">
    <app-ui-page-header title="Leave policies and balances"
      description="Balances are derived from the append-only leave ledger. Accrual method must be explicitly selected for every policy version." />

    <div class="lv-page">
      @if (error()) {
        <app-ui-alert variant="danger">{{ error() }}</app-ui-alert>
      }
      @if (busy()) {
        <app-ui-state kind="loading" title="Loading or saving leave data..." [compact]="true" />
      }

      <app-ui-card title="Leave types">
        <app-ui-data-table
          ariaLabel="Leave types"
          density="compact"
          [columns]="typeColumns"
          [rows]="typeRows()"
          rowKey="id"
          [loading]="busy()"
          [searchable]="false"
          [paginated]="false"
          emptyTitle="No leave types are configured." />
      </app-ui-card>

      @if (canManage()) {
        <form (ngSubmit)="createType()">
          <app-ui-card title="Add leave type">
            <div class="lv-grid">
              <app-ui-form-field label="Code" [required]="true">
                <input appUiControl name="typeCode" required maxlength="50" [(ngModel)]="typeForm.code">
              </app-ui-form-field>
              <app-ui-form-field label="Name" [required]="true">
                <input appUiControl name="typeName" required maxlength="100" [(ngModel)]="typeForm.name">
              </app-ui-form-field>
            </div>
            <fieldset class="lv-checks">
              <legend class="sr-only">Leave type options</legend>
              <label class="ui-check"><input appUiControl name="paid" type="checkbox" [(ngModel)]="typeForm.isPaid"><span>Paid</span></label>
              <label class="ui-check"><input appUiControl name="document" type="checkbox" [(ngModel)]="typeForm.requiresDocument"><span>Document required</span></label>
            </fieldset>
            <div uiCardFooter class="lv-form-actions">
              <button appUiButton type="submit" [disabled]="busy()">Add leave type</button>
            </div>
          </app-ui-card>
        </form>
      }

      @if (canRequest()) {
        <form (ngSubmit)="createRequest()">
          <app-ui-card title="Request leave">
            <div class="lv-grid">
              <app-ui-form-field label="Leave type" [required]="true">
                <select appUiControl name="requestType" required [(ngModel)]="requestForm.leaveTypeId">
                  <option [ngValue]="null" disabled>Select type</option>
                  @for (type of activeTypes(); track type.id) {
                    <option [ngValue]="type.id">{{ type.code }} — {{ type.name }}</option>
                  }
                </select>
              </app-ui-form-field>
              <app-ui-form-field label="From" [required]="true">
                <input appUiControl name="requestFrom" type="date" appCompanyDate required [(ngModel)]="requestForm.startDate">
              </app-ui-form-field>
              <app-ui-form-field label="To" [required]="true">
                <input appUiControl name="requestTo" type="date" appCompanyDate required [(ngModel)]="requestForm.endDate">
              </app-ui-form-field>
              <app-ui-form-field label="Reason" [required]="true">
                <input appUiControl name="requestReason" required maxlength="1000" [(ngModel)]="requestForm.reason">
              </app-ui-form-field>
              @if (selectedRequestType()?.requiresDocument) {
                <app-ui-form-field label="Supporting document" [required]="true">
                  <input appUiControl name="requestAttachment" type="file" required (change)="setRequestFile($event)">
                </app-ui-form-field>
              }
            </div>
            @if (!requestWorkflowEnabled()) {
              <app-ui-alert variant="info">
                Leave requests require the Attendance capability for roster-based day calculation.
              </app-ui-alert>
            }
            <div uiCardFooter class="lv-form-actions">
              <button appUiButton type="submit" [disabled]="busy() || !requestForm.leaveTypeId">Create draft</button>
            </div>
          </app-ui-card>
        </form>
      }

      @if (canUseRequestWorkflow()) {
        <app-ui-card title="My leave requests">
          <button uiCardActions appUiButton variant="outline" size="sm" type="button" (click)="loadRequests()" [disabled]="busy()">Refresh</button>
          @if (requestsLoaded() || myRequests().length) {
            <app-ui-data-table
              ariaLabel="My leave requests"
              density="compact"
              [columns]="requestColumns"
              [rows]="requestRows()"
              rowKey="id"
              [loading]="busy()"
              searchLabel="Search my leave requests"
              searchPlaceholder="Search my leave requests…"
              emptyTitle="No leave requests yet.">
              <ng-template uiTableCell="summary" let-row>
                <strong>{{ row.request.leaveTypeCode }} · {{ (row.request.startDate | appDate) }} – {{ (row.request.endDate | appDate) }}</strong>
                <span class="lv-secondary">({{ row.request.requestedUnits }} units)</span>
              </ng-template>
              <ng-template uiTableCell="status" let-row>
                <app-ui-badge [variant]="requestStatusVariant(row.request.status)">{{ row.request.status }}</app-ui-badge>
                <span class="lv-secondary">version {{ row.request.version }}</span>
              </ng-template>
              <ng-template uiTableCell="evidence" let-row>
                @for (day of row.request.days; track $index; let last = $last) {
                  <span>{{ (day.workDate | appDate) }} {{ day.dayClassification }} ({{ day.units }})@if (!last) {; }</span>
                }
              </ng-template>
              <ng-template uiTableCell="approval" let-row>
                @if (row.request.approvalInstanceId) {
                  <span>Approval instance {{ row.request.approvalInstanceId }}@if (row.request.currentApprovalStep) { · step {{ row.request.currentApprovalStep }} }</span>
                }
                @if (row.request.decisionReason) {
                  <span class="lv-secondary">Decision: {{ row.request.decisionReason }}</span>
                }
              </ng-template>
              <ng-template uiTableRowActions let-row>
                <div class="lv-actions">
                  @if (row.request.status === 'Draft') {
                    <button appUiButton size="sm" type="button" (click)="submitRequest(row.request)" [disabled]="busy()">Submit for approval</button>
                  }
                  @if (row.request.status === 'Draft' || row.request.status === 'Approved') {
                    <button appUiButton size="sm" variant="outline" type="button" (click)="cancelRequest(row.request)" [disabled]="busy()">Cancel request</button>
                  }
                </div>
              </ng-template>
            </app-ui-data-table>
          }
        </app-ui-card>
      }

      @if (canApprove()) {
        <app-ui-card title="Pending leave approvals">
          @if (approvalsLoaded() || approvalRequests().length) {
            <app-ui-data-table
              ariaLabel="Pending leave approvals"
              density="compact"
              [columns]="approvalColumns"
              [rows]="approvalRows()"
              rowKey="id"
              [loading]="busy()"
              searchLabel="Search pending leave approvals"
              searchPlaceholder="Search pending approvals…"
              emptyTitle="No pending leave approvals.">
              <ng-template uiTableCell="decision" let-row>
                <div class="lv-reason">
                  <input appUiControl [name]="'approvalReason' + row.id" maxlength="1000"
                    [attr.aria-label]="'Decision reason for ' + row.employee + ' ' + row.period"
                    [(ngModel)]="approvalReasons[row.id]">
                </div>
              </ng-template>
              <ng-template uiTableRowActions let-row>
                <div class="lv-actions">
                  <button appUiButton size="sm" type="button" (click)="decideRequest(row.request, 'approve')" [disabled]="busy()">Approve</button>
                  <button appUiButton size="sm" variant="danger" type="button" (click)="decideRequest(row.request, 'reject')" [disabled]="busy()">Reject</button>
                </div>
              </ng-template>
            </app-ui-data-table>
          }
        </app-ui-card>
      }

      @if (canView()) {
        <app-ui-card title="Policy versions">
          <div class="lv-stack">
            <app-ui-form-field label="Leave type">
              <select appUiControl name="policyType" [(ngModel)]="selectedTypeId" (ngModelChange)="loadPolicies()">
                <option [ngValue]="null">All leave types</option>
                @for (type of types(); track type.id) {
                  <option [ngValue]="type.id">{{ type.code }} — {{ type.name }}</option>
                }
              </select>
            </app-ui-form-field>
            <app-ui-data-table
              ariaLabel="Leave policy versions"
              density="compact"
              [columns]="policyColumns"
              [rows]="policyRows()"
              rowKey="id"
              [loading]="busy()"
              searchLabel="Search policy versions"
              searchPlaceholder="Search policy versions…"
              emptyTitle="No policies match this filter." />
          </div>
        </app-ui-card>

        @if (canManage()) {
          <form (ngSubmit)="createPolicy()">
            <app-ui-card title="Create policy version">
              <div class="lv-grid">
                <app-ui-form-field label="Leave type" [required]="true">
                  <select appUiControl name="newPolicyType" required [(ngModel)]="policyForm.leaveTypeId">
                    <option [ngValue]="null" disabled>Select type</option>
                    @for (type of types(); track type.id) {
                      <option [ngValue]="type.id">{{ type.code }} — {{ type.name }}</option>
                    }
                  </select>
                </app-ui-form-field>
                <app-ui-form-field label="Policy code" [required]="true">
                  <input appUiControl name="policyCode" required maxlength="100" [(ngModel)]="policyForm.code">
                </app-ui-form-field>
                <app-ui-form-field label="Name" [required]="true">
                  <input appUiControl name="policyName" required maxlength="150" [(ngModel)]="policyForm.name">
                </app-ui-form-field>
                <app-ui-form-field label="Scope" [required]="true">
                  <select appUiControl name="scope" required [(ngModel)]="policyForm.scopeType">
                    <option [ngValue]="0">Business</option><option [ngValue]="1">Location type</option>
                    <option [ngValue]="2">Location</option><option [ngValue]="3">Employee</option>
                  </select>
                </app-ui-form-field>
                @if (policyForm.scopeType !== 0) {
                  <app-ui-form-field label="Scope reference" [required]="true">
                    <input appUiControl name="scopeReference" required [(ngModel)]="policyForm.scopeReference">
                  </app-ui-form-field>
                }
                <app-ui-form-field label="Version" [required]="true">
                  <input appUiControl name="versionNo" type="number" min="1" required [(ngModel)]="policyForm.versionNo">
                </app-ui-form-field>
                <app-ui-form-field label="Accrual method" [required]="true">
                  <select appUiControl name="accrualMethod" required [(ngModel)]="policyForm.accrualMethod">
                    <option [ngValue]="null" disabled>Select an accrual method</option>
                    <option [ngValue]="0">Annual grant at leave-year start</option>
                    <option [ngValue]="1">Monthly on joining-date anniversary</option>
                    <option [ngValue]="2">Annual on joining-date anniversary</option>
                  </select>
                </app-ui-form-field>
                <app-ui-form-field label="Annual entitlement" [required]="true">
                  <input appUiControl name="annualEntitlement" type="number" min="0" step="0.000001" required [(ngModel)]="policyForm.annualEntitlement">
                </app-ui-form-field>
                <app-ui-form-field label="Leave-year start month" [required]="true">
                  <input appUiControl name="yearMonth" type="number" min="1" max="12" required [(ngModel)]="policyForm.leaveYearStartMonth">
                </app-ui-form-field>
                <app-ui-form-field label="Leave-year start day" [required]="true">
                  <input appUiControl name="yearDay" type="number" min="1" max="31" required [(ngModel)]="policyForm.leaveYearStartDay">
                </app-ui-form-field>
                <app-ui-form-field label="Effective from" [required]="true">
                  <input appUiControl name="effectiveFrom" type="date" appCompanyDate required [(ngModel)]="policyForm.effectiveFrom">
                </app-ui-form-field>
                <app-ui-form-field label="Effective to">
                  <input appUiControl name="effectiveTo" type="date" appCompanyDate [(ngModel)]="policyForm.effectiveTo">
                </app-ui-form-field>
                @if (policyForm.carryForwardAllowed) {
                  <app-ui-form-field label="Carry-forward cap">
                    <input appUiControl name="carryCap" type="number" min="0" step="0.000001" [(ngModel)]="policyForm.carryForwardCap">
                  </app-ui-form-field>
                  <app-ui-form-field label="Expiry months">
                    <input appUiControl name="expiry" type="number" min="1" [(ngModel)]="policyForm.carryForwardExpiryMonths">
                  </app-ui-form-field>
                }
                <app-ui-form-field label="Maximum balance">
                  <input appUiControl name="maximumBalance" type="number" min="0" step="0.000001" [(ngModel)]="policyForm.maximumBalance">
                </app-ui-form-field>
                <app-ui-form-field label="Payroll impact code">
                  <input appUiControl name="payrollImpact" maxlength="100" [(ngModel)]="policyForm.payrollImpactCode">
                </app-ui-form-field>
              </div>
              <fieldset class="lv-checks">
                <legend class="sr-only">Policy options</legend>
                <label class="ui-check"><input appUiControl name="carryForward" type="checkbox" [(ngModel)]="policyForm.carryForwardAllowed"><span>Carry forward allowed</span></label>
                <label class="ui-check"><input appUiControl name="negative" type="checkbox" [(ngModel)]="policyForm.negativeBalanceAllowed"><span>Negative balance allowed</span></label>
              </fieldset>
              <div uiCardFooter class="lv-form-actions">
                <button appUiButton type="submit" [disabled]="busy()">Create policy version</button>
              </div>
            </app-ui-card>
          </form>
        }
      }

      @if (canViewBalances() || canAdjust() || canAccrue() || canCloseLeaveYear() || canReopenLeaveYear()) {
        <app-ui-card title="Employee balance and ledger">
          <div class="lv-stack">
            <form (ngSubmit)="loadBalance()" class="lv-grid--inline">
              <app-ui-form-field label="Employee ID" [required]="true">
                <input appUiControl name="employeeId" type="number" min="1" required [(ngModel)]="balanceForm.employeeId">
              </app-ui-form-field>
              <app-ui-form-field label="Leave type">
                <select appUiControl name="balanceType" [(ngModel)]="balanceForm.leaveTypeId">
                  <option [ngValue]="null">All types</option>
                  @for (type of types(); track type.id) {
                    <option [ngValue]="type.id">{{ type.code }} — {{ type.name }}</option>
                  }
                </select>
              </app-ui-form-field>
              <button appUiButton type="submit" [disabled]="busy()">Load balance</button>
            </form>
            @if (canReopenLeaveYear()) {
              <app-ui-form-field label="Reason to reopen" [required]="true">
                <input appUiControl name="reopenReason" required maxlength="1000" [(ngModel)]="reopenReason">
              </app-ui-form-field>
            }
            @if (balanceLoaded() || balances().length) {
              <app-ui-data-table
                ariaLabel="Leave balances"
                density="compact"
                [columns]="balanceColumns"
                [rows]="balanceRows()"
                rowKey="id"
                [loading]="busy()"
                [searchable]="false"
                [paginated]="false"
                emptyTitle="No leave balances were found for this employee.">
                <ng-template uiTableCell="status" let-row>
                  <app-ui-badge [variant]="balanceStatusVariant(row.balance.status)">{{ row.balance.status }}</app-ui-badge>
                  <span class="lv-secondary">version {{ row.balance.version }}</span>
                  @if (row.balance.closingBalance !== null) {
                    <span class="lv-secondary">
                      Closed balance {{ row.balance.closingBalance }} · carry forward {{ row.balance.closeCarryForward }}
                      · expired {{ row.balance.closeExpired }}
                      @if (row.balance.carryForwardExpiresOn) {<span>· carried balance expires {{ row.balance.carryForwardExpiresOn }}</span>}
                    </span>
                  }
                  @if (row.balance.lastLifecycleReason) {
                    <span class="lv-secondary">{{ row.balance.lastLifecycleAction }}: {{ row.balance.lastLifecycleReason }}</span>
                  }
                </ng-template>
                <ng-template uiTableRowActions let-row>
                  <div class="lv-actions">
                    @if (canCloseLeaveYear() && row.balance.status === 'Open') {
                      <button appUiButton size="sm" variant="outline" type="button"
                        (click)="reviewLeaveYearClose(row.balance)" [disabled]="busy()">Review close</button>
                    }
                    @if (canCloseLeaveYear() && row.balance.status === 'ClosingReview') {
                      <button appUiButton size="sm" type="button"
                        (click)="closeLeaveYear(row.balance)" [disabled]="busy()">Confirm close</button>
                    }
                    @if (canReopenLeaveYear() && row.balance.status === 'Closed') {
                      <button appUiButton size="sm" variant="outline" type="button"
                        (click)="reopenLeaveYear(row.balance)" [disabled]="busy() || !reopenReason.trim()">Reopen</button>
                    }
                  </div>
                </ng-template>
              </app-ui-data-table>
            }
            @if (balanceLoaded() || ledger().length) {
              <app-ui-data-table
                ariaLabel="Leave ledger"
                density="compact"
                [columns]="ledgerColumns"
                [rows]="ledgerRows()"
                rowKey="id"
                [loading]="busy()"
                searchLabel="Search leave ledger"
                searchPlaceholder="Search ledger entries…"
                emptyTitle="No ledger entries are available for this filter." />
            }
          </div>
        </app-ui-card>
      }

      @if (canAdjust()) {
        <form (ngSubmit)="postLedgerEntry()">
          <app-ui-card title="Opening balance / adjustment">
            <div class="lv-grid">
              <app-ui-form-field label="Employee ID" [required]="true">
                <input appUiControl name="adjustEmployee" type="number" min="1" required [(ngModel)]="adjustment.employeeId">
              </app-ui-form-field>
              <app-ui-form-field label="Leave type" [required]="true">
                <select appUiControl name="adjustType" required [(ngModel)]="adjustment.leaveTypeId">
                  <option [ngValue]="null" disabled>Select type</option>
                  @for (type of types(); track type.id) {
                    <option [ngValue]="type.id">{{ type.code }} — {{ type.name }}</option>
                  }
                </select>
              </app-ui-form-field>
              <app-ui-form-field label="Entry type">
                <select appUiControl name="transactionType" [(ngModel)]="adjustment.transactionType">
                  <option [ngValue]="0">Opening</option><option [ngValue]="4">Adjustment</option>
                </select>
              </app-ui-form-field>
              <app-ui-form-field label="Quantity" [required]="true">
                <input appUiControl name="quantity" type="number" step="0.000001" required [(ngModel)]="adjustment.quantity">
              </app-ui-form-field>
              <app-ui-form-field label="Effective date" [required]="true">
                <input appUiControl name="effectiveDate" type="date" appCompanyDate required [(ngModel)]="adjustment.effectiveDate">
              </app-ui-form-field>
              <app-ui-form-field label="Idempotency key" [required]="true">
                <input appUiControl name="idempotencyKey" required maxlength="150" [(ngModel)]="adjustment.idempotencyKey">
              </app-ui-form-field>
              <app-ui-form-field label="Reason" [required]="true">
                <input appUiControl name="reason" required maxlength="1000" [(ngModel)]="adjustment.reason">
              </app-ui-form-field>
            </div>
            <div uiCardFooter class="lv-form-actions">
              <button appUiButton type="submit" [disabled]="busy()">Post audited entry</button>
            </div>
          </app-ui-card>
        </form>
      }

      @if (canAccrue()) {
        <form (ngSubmit)="accrue()">
          <app-ui-card title="Accrual">
            <div class="lv-grid">
              <app-ui-form-field label="Employee ID" [required]="true">
                <input appUiControl name="accrueEmployee" type="number" min="1" required [(ngModel)]="accrual.employeeId">
              </app-ui-form-field>
              <app-ui-form-field label="Leave type" [required]="true">
                <select appUiControl name="accrueType" required [(ngModel)]="accrual.leaveTypeId">
                  <option [ngValue]="null" disabled>Select type</option>
                  @for (type of types(); track type.id) {
                    <option [ngValue]="type.id">{{ type.code }} — {{ type.name }}</option>
                  }
                </select>
              </app-ui-form-field>
              <app-ui-form-field label="Through date" [required]="true">
                <input appUiControl name="throughDate" type="date" appCompanyDate required [(ngModel)]="accrual.throughDate">
              </app-ui-form-field>
            </div>
            <div uiCardFooter class="lv-form-actions">
              <button appUiButton type="submit" [disabled]="busy()">Run idempotent accrual</button>
            </div>
          </app-ui-card>
        </form>
      }
    </div>
  </main>`
})
export class HrLeavePageComponent implements OnInit {
  private readonly companyDates = injectCompanyDates(CompanyDateService);
  private readonly api = inject(HrLeaveService);
  private readonly auth = inject(AuthService);
  private readonly capability = inject(HrPayrollCapabilityStore);
  readonly types = signal<HrLeaveType[]>([]);
  readonly policies = signal<HrLeavePolicy[]>([]);
  readonly balances = signal<import('../../../services/hr-leave.service').HrLeaveBalance[]>([]);
  readonly ledger = signal<import('../../../services/hr-leave.service').HrLeaveLedgerEntry[]>([]);
  readonly busy = signal(false);
  readonly balanceLoaded = signal(false);
  readonly error = signal<string | null>(null);
  readonly myRequests = signal<HrLeaveRequest[]>([]);
  readonly approvalRequests = signal<HrLeaveRequest[]>([]);
  readonly requestsLoaded = signal(false);
  readonly approvalsLoaded = signal(false);
  approvalReasons: Record<number, string> = {};
  selectedTypeId: number | null = null;
  requestForm = {
    leaveTypeId: null as number | null,
    startDate: new Date().toISOString().slice(0, 10),
    endDate: new Date().toISOString().slice(0, 10),
    reason: ''
  };
  requestFile: File | null = null;
  typeForm = { code: '', name: '', isPaid: true, requiresDocument: false };
  policyForm = this.newPolicyForm();
  balanceForm = { employeeId: 0, leaveTypeId: null as number | null };
  adjustment = {
    employeeId: 0, leaveTypeId: null as number | null, transactionType: 4 as 0 | 4,
    quantity: 0, effectiveDate: new Date().toISOString().slice(0, 10),
    idempotencyKey: '', reason: ''
  };
  reopenReason = '';
  accrual = { employeeId: 0, leaveTypeId: null as number | null, throughDate: new Date().toISOString().slice(0, 10) };

  readonly typeColumns: UiTableColumn[] = [
    { key: 'code', header: 'Code', sortable: true },
    { key: 'name', header: 'Name', sortable: true },
    {
      key: 'pay', header: 'Pay', type: 'status', sortable: true,
      statusMap: { Paid: { label: 'Paid', variant: 'success' }, Unpaid: { label: 'Unpaid', variant: 'neutral' } }
    },
    {
      key: 'status', header: 'Status', type: 'status', sortable: true,
      statusMap: { Active: { label: 'Active', variant: 'success' }, Inactive: { label: 'Inactive', variant: 'neutral' } }
    }
  ];
  readonly policyColumns: UiTableColumn[] = [
    { key: 'policy', header: 'Policy', sortable: true },
    { key: 'scope', header: 'Scope', sortable: true },
    { key: 'accrual', header: 'Accrual', sortable: true },
    { key: 'entitlement', header: 'Entitlement', align: 'end' },
    { key: 'period', header: 'Period', sortable: true, sortField: 'effectiveFrom' }
  ];
  readonly requestColumns: UiTableColumn[] = [
    { key: 'summary', header: 'Request', sortable: true, sortField: 'summarySort' },
    { key: 'status', header: 'Status', sortable: true },
    { key: 'reason', header: 'Reason' },
    { key: 'evidence', header: 'Calendar evidence' },
    { key: 'approval', header: 'Approval' }
  ];
  readonly approvalColumns: UiTableColumn[] = [
    { key: 'employee', header: 'Employee', sortable: true },
    { key: 'leaveType', header: 'Leave type', sortable: true },
    { key: 'period', header: 'Period', sortable: true, sortField: 'request.startDate' },
    { key: 'units', header: 'Units', align: 'end' },
    { key: 'reason', header: 'Reason' },
    { key: 'decision', header: 'Decision reason', type: 'custom' }
  ];
  readonly balanceColumns: UiTableColumn[] = [
    { key: 'leaveYear', header: 'Leave year', sortable: true, sortField: 'balance.leaveYearStart' },
    { key: 'type', header: 'Type', sortable: true },
    { key: 'opening', header: 'Opening', field: 'balance.opening', align: 'end' },
    { key: 'accrued', header: 'Accrued', field: 'balance.accrued', align: 'end' },
    { key: 'used', header: 'Used', field: 'balance.used', align: 'end' },
    { key: 'adjusted', header: 'Adjusted', field: 'balance.adjusted', align: 'end' },
    { key: 'remaining', header: 'Remaining', field: 'balance.remaining', align: 'end' },
    { key: 'status', header: 'Status / close evidence', field: 'details' }
  ];
  readonly ledgerColumns: UiTableColumn[] = [
    { key: 'effectiveDate', type: 'date', header: 'Effective date', sortable: true },
    { key: 'transaction', header: 'Transaction', sortable: true },
    { key: 'quantity', header: 'Quantity', align: 'end' },
    { key: 'reason', header: 'Reason' },
    { key: 'source', header: 'Source' }
  ];

  // Presentation-only projections of the loaded data; the API rows themselves are never modified.
  readonly typeRows = computed<LeaveTypeRow[]>(() => this.types().map(type => ({
    id: type.id, code: type.code, name: type.name,
    pay: type.isPaid ? 'Paid' : 'Unpaid', status: type.isActive ? 'Active' : 'Inactive'
  })));
  readonly policyRows = computed<LeavePolicyRow[]>(() => this.policies().map(policy => ({
    effectiveFrom: policy.effectiveFrom,
    id: policy.id,
    policy: `${policy.code} v${policy.versionNo} — ${policy.name}`,
    scope: policy.scopeReference
      ? `${this.scopeName(policy.scopeType)}: ${policy.scopeReference}` : this.scopeName(policy.scopeType),
    accrual: this.accrualName(policy.accrualMethod),
    entitlement: policy.annualEntitlement,
    period: `${this.companyDates.format(policy.effectiveFrom, false)} – ${this.companyDates.format(policy.effectiveTo, false) || 'Open'}`
  })));
  readonly requestRows = computed<LeaveRequestRow[]>(() => this.myRequests().map(request => ({
    id: request.id, request,
    summary: `${request.leaveTypeCode} · ${this.companyDates.format(request.startDate, false)} – ${this.companyDates.format(request.endDate, false)} (${request.requestedUnits} units)`,
    summarySort: `${request.leaveTypeCode} · ${request.startDate} – ${request.endDate} (${request.requestedUnits} units)`,
    reason: request.reason,
    status: `${request.status} · version ${request.version}`,
    evidence: request.days.map(day => `${this.companyDates.format(day.workDate, false)} ${day.dayClassification} (${day.units})`).join('; '),
    approval: [
      request.approvalInstanceId
        ? `Approval instance ${request.approvalInstanceId}${request.currentApprovalStep ? ` · step ${request.currentApprovalStep}` : ''}`
        : '',
      request.decisionReason ? `Decision: ${request.decisionReason}` : ''
    ].filter(Boolean).join(' ')
  })));
  readonly approvalRows = computed<LeaveApprovalRow[]>(() => this.approvalRequests().map(request => ({
    id: request.id, request, employee: request.employeeName, leaveType: request.leaveTypeCode,
    period: `${this.companyDates.format(request.startDate, false)} – ${this.companyDates.format(request.endDate, false)}`, units: request.requestedUnits, reason: request.reason
  })));
  readonly balanceRows = computed<LeaveBalanceRow[]>(() => this.balances().map(balance => ({
    id: `${balance.employeeId}-${balance.leaveTypeId}-${balance.leaveYearStart}`,
    balance,
    leaveYear: `${this.companyDates.format(balance.leaveYearStart)} – ${this.companyDates.format(balance.leaveYearEnd)}`,
    type: `${balance.leaveTypeCode} — ${balance.leaveTypeName}`,
    status: balance.status,
    details: [
      `${balance.status} · version ${balance.version}`,
      balance.closingBalance !== null
        ? `Closed balance ${balance.closingBalance} · carry forward ${balance.closeCarryForward} · expired ${balance.closeExpired}`
        : '',
      balance.lastLifecycleReason ? `${balance.lastLifecycleAction}: ${balance.lastLifecycleReason}` : ''
    ].filter(Boolean).join(' ')
  })));
  readonly ledgerRows = computed<LeaveLedgerRow[]>(() => this.ledger().map(entry => ({
    id: entry.id, effectiveDate: entry.effectiveDate, transaction: this.transactionName(entry.transactionType),
    quantity: entry.quantity, reason: entry.reason, source: `${entry.sourceType}/${entry.sourceId}`
  })));

  requestStatusVariant(status: string): UiBadgeVariant {
    switch (status) {
      case 'Approved': return 'success';
      case 'PendingApproval': return 'warning';
      case 'Rejected': return 'danger';
      case 'Draft': return 'info';
      default: return 'neutral';
    }
  }

  balanceStatusVariant(status: string): UiBadgeVariant {
    switch (status) {
      case 'Open': return 'success';
      case 'ClosingReview': return 'warning';
      default: return 'neutral';
    }
  }

  ngOnInit() {
    if (!this.allowed()) return;
    this.busy.set(true);
    this.api.getTypes().subscribe({
      next: types => {
        this.types.set(types);
        this.selectedTypeId = types[0]?.id ?? null;
        this.policyForm.leaveTypeId = this.selectedTypeId;
        this.requestForm.leaveTypeId = this.types().find(type => type.isActive)?.id ?? null;
        if (this.canView()) this.loadPolicies();
        if (this.canUseRequestWorkflow() || this.canApprove()) this.loadRequests();
        this.busy.set(false);
      },
      error: error => this.fail(error, 'Unable to load authorized leave types.')
    });
  }

  canManage() { return this.auth.hasPermission('hr.leave.admin'); }
  canView() { return this.auth.hasPermission('hr.leave.view') || this.canManage(); }
  canRequest() { return this.auth.hasPermission('hr.leave.request'); }
  canApprove() {
    return this.auth.hasPermission('hr.leave.approve') && this.requestWorkflowEnabled();
  }
  canUseRequestWorkflow() { return this.canRequest() && this.requestWorkflowEnabled(); }
  requestWorkflowEnabled() { return this.capability.isEnabled('attendance.enabled'); }
  canViewBalances() { return this.auth.hasPermission('hr.leave_balance.view'); }
  canAdjust() { return this.auth.hasPermission('hr.leave_balance.adjust'); }
  canAccrue() { return this.auth.hasPermission('hr.leave_balance.accrue'); }
  canCloseLeaveYear() {
    return this.canViewBalances() && this.auth.hasPermission('hr.leave_year.close');
  }
  canReopenLeaveYear() {
    return this.canViewBalances() && this.auth.hasPermission('hr.leave_year.reopen');
  }

  loadPolicies() {
    if (!this.allowed() || !this.canView()) return;
    this.busy.set(true);
    this.api.getPolicies(this.selectedTypeId).subscribe({
      next: rows => { this.policies.set(rows); this.busy.set(false); },
      error: error => this.fail(error, 'Unable to load authorized leave policies.')
    });
  }

  activeTypes() { return this.types().filter(type => type.isActive); }
  selectedRequestType() {
    return this.types().find(type => type.id === this.requestForm.leaveTypeId) ?? null;
  }

  setRequestFile(event: Event) {
    this.requestFile = (event.target as HTMLInputElement).files?.[0] ?? null;
  }

  createRequest() {
    if (!this.allowed() || !this.canUseRequestWorkflow()) return;
    const leaveType = this.selectedRequestType();
    const leaveTypeId = this.requestForm.leaveTypeId;
    if (!leaveType || leaveTypeId === null || !this.requestForm.reason.trim()
        || (leaveType.requiresDocument && !this.requestFile)) {
      this.error.set('Select an active leave type, enter a reason, and attach the required document.');
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.api.createRequest({
      ...this.requestForm, leaveTypeId, reason: this.requestForm.reason.trim()
    }).subscribe({
      next: draft => {
        if (!this.requestFile) {
          this.busy.set(false);
          this.requestForm.reason = '';
          this.loadRequests();
          return;
        }
        this.api.uploadRequestAttachment(draft.id, this.requestFile).subscribe({
          next: () => {
            this.requestFile = null;
            this.requestForm.reason = '';
            this.loadRequests();
            this.busy.set(false);
          },
          error: error => this.fail(error, 'Draft created, but its document upload failed.')
        });
      },
      error: error => this.fail(error, 'Unable to create leave request draft.')
    });
  }

  loadRequests() {
    if (!this.allowed()) return;
    const mine = this.canUseRequestWorkflow() ? this.api.getMyRequests() : of([]);
    const pending = this.canApprove() ? this.api.getRequests('PendingApproval') : of([]);
    this.busy.set(true);
    forkJoin({ mine, pending }).subscribe({
      next: result => {
        this.myRequests.set(result.mine);
        this.approvalRequests.set(result.pending);
        this.requestsLoaded.set(this.canUseRequestWorkflow());
        this.approvalsLoaded.set(this.canApprove());
        this.busy.set(false);
      },
      error: error => this.fail(error, 'Unable to load authorized leave requests.')
    });
  }

  submitRequest(request: HrLeaveRequest) {
    if (!this.canUseRequestWorkflow()) return;
    this.run(this.api.submitRequest(request), 'Unable to submit leave request.',
      () => this.loadRequests());
  }

  cancelRequest(request: HrLeaveRequest) {
    if (!this.canUseRequestWorkflow()) return;
    this.run(this.api.cancelRequest(request.id, crypto.randomUUID(), null),
      'Unable to cancel leave request.', () => this.loadRequests());
  }

  decideRequest(request: HrLeaveRequest, decision: 'approve' | 'reject') {
    if (!this.canApprove() || !request.approvalInstanceId) return;
    const reason = this.approvalReasons[request.id]?.trim() || null;
    this.run(this.api.decideRequest(
      request.approvalInstanceId, decision, crypto.randomUUID(), reason),
    'Unable to record leave approval decision.', () => this.loadRequests());
  }

  createType() {
    if (!this.allowed() || !this.canManage()) return;
    this.run(this.api.createType(this.typeForm), 'Unable to create leave type.', type => {
      this.types.update(rows => [...rows, type]);
      this.typeForm = { code: '', name: '', isPaid: true, requiresDocument: false };
    });
  }

  createPolicy() {
    if (!this.allowed() || !this.canManage()) return;
    const form = this.policyForm;
    if (form.leaveTypeId === null || form.accrualMethod === null
        || (form.scopeType !== 0 && !form.scopeReference.trim())) {
      this.error.set('Select a leave type, accrual method, and required policy scope reference.');
      return;
    }
    const leaveTypeId = form.leaveTypeId;
    const accrualMethod = form.accrualMethod;
    const request = {
      ...form,
      leaveTypeId,
      accrualMethod,
      scopeReference: form.scopeType === 0 ? null : form.scopeReference.trim(),
      effectiveTo: form.effectiveTo || null,
      carryForwardCap: form.carryForwardAllowed ? form.carryForwardCap : null,
      carryForwardExpiryMonths: form.carryForwardAllowed ? form.carryForwardExpiryMonths : null,
      maximumBalance: form.maximumBalance,
      payrollImpactCode: form.payrollImpactCode.trim() || null
    };
    this.run(this.api.createPolicy(request), 'Unable to create leave policy version.', policy => {
      this.policies.update(rows => [...rows, policy]);
      this.policyForm = this.newPolicyForm();
      this.policyForm.leaveTypeId = this.selectedTypeId;
    });
  }

  loadBalance() {
    if (!this.allowed() || (!this.canViewBalances() && !this.canCloseLeaveYear() && !this.canReopenLeaveYear())) {
      this.error.set('Leave balance permission unavailable.');
      return;
    }
    if (this.balanceForm.employeeId <= 0) {
      this.error.set('Enter a valid employee ID.');
      return;
    }
    this.balanceLoaded.set(false);
    this.busy.set(true);
    this.api.getBalances(this.balanceForm.employeeId, this.balanceForm.leaveTypeId).subscribe({
      next: rows => {
        this.balances.set(rows);
        this.api.getLedger(this.balanceForm.employeeId, this.balanceForm.leaveTypeId).subscribe({
          next: ledger => { this.ledger.set(ledger); this.balanceLoaded.set(true); this.busy.set(false); },
          error: error => this.fail(error, 'Unable to load the authorized leave ledger.')
        });
      },
      error: error => this.fail(error, 'Unable to load authorized leave balances.')
    });
  }

  postLedgerEntry() {
    if (!this.allowed() || !this.canAdjust()) return;
    if (this.adjustment.employeeId <= 0 || this.adjustment.leaveTypeId === null) {
      this.error.set('Select an employee and leave type.');
      return;
    }
    const employeeId = this.adjustment.employeeId;
    const leaveTypeId = this.adjustment.leaveTypeId;
    this.run(this.api.postLedgerEntry({ ...this.adjustment, leaveTypeId }),
      'Unable to post audited leave ledger entry.', () => this.loadBalanceFor(employeeId, leaveTypeId));
  }

  accrue() {
    if (!this.allowed() || !this.canAccrue()) return;
    if (this.accrual.employeeId <= 0 || this.accrual.leaveTypeId === null) {
      this.error.set('Select an employee and leave type.');
      return;
    }
    const employeeId = this.accrual.employeeId;
    const leaveTypeId = this.accrual.leaveTypeId;
    this.run(this.api.accrue({ ...this.accrual, leaveTypeId }),
      'Unable to accrue leave under the effective policy.', () => this.loadBalanceFor(employeeId, leaveTypeId));
  }

  reviewLeaveYearClose(balance: HrLeaveBalance) {
    if (!this.allowed() || !this.canCloseLeaveYear()) return;
    this.run(this.api.reviewLeaveYearClose(balance, crypto.randomUUID()),
      'Unable to prepare the leave-year close review.',
      () => this.loadBalanceFor(balance.employeeId, balance.leaveTypeId));
  }

  closeLeaveYear(balance: HrLeaveBalance) {
    if (!this.allowed() || !this.canCloseLeaveYear()) return;
    this.run(this.api.closeLeaveYear(balance, crypto.randomUUID()),
      'Unable to close the reviewed leave year.',
      () => this.loadBalanceFor(balance.employeeId, balance.leaveTypeId));
  }

  reopenLeaveYear(balance: HrLeaveBalance) {
    const reason = this.reopenReason.trim();
    if (!this.allowed() || !this.canReopenLeaveYear() || !reason) return;
    this.run(this.api.reopenLeaveYear(balance, crypto.randomUUID(), reason),
      'Unable to reopen the leave year.',
      () => this.loadBalanceFor(balance.employeeId, balance.leaveTypeId));
  }

  scopeName(scope: number) {
    return ['Business', 'Location type', 'Location', 'Employee'][scope] ?? 'Unknown';
  }

  accrualName(method: number) {
    return ['Annual grant at leave-year start', 'Monthly joining-date anniversary', 'Annual joining-date anniversary'][method] ?? 'Unknown';
  }

  transactionName(type: number) {
    return ['Opening', 'Accrual', 'Consumption', 'Restoration', 'Adjustment',
      'Carry forward', 'Expiry', 'Close correction'][type] ?? 'Unknown';
  }

  private loadBalanceFor(employeeId: number, leaveTypeId: number) {
    this.balanceForm = { employeeId, leaveTypeId };
    this.loadBalance();
  }

  private run<T>(request: import('rxjs').Observable<T>, fallback: string, success: (value: T) => void) {
    this.busy.set(true);
    this.error.set(null);
    request.subscribe({
      next: value => { this.busy.set(false); success(value); },
      error: error => this.fail(error, fallback)
    });
  }

  private fail(error: unknown, fallback: string) {
    const response = error instanceof HttpErrorResponse ? error : null;
    this.error.set(response?.error?.detail ?? response?.error?.message ?? fallback);
    this.busy.set(false);
  }

  private allowed() {
    if (!this.capability.isEnabled('hr.enabled')) {
      this.error.set('HR capability unavailable.');
      return false;
    }
    if (!this.canView() && !this.canRequest() && !this.canApprove()
        && !this.canCloseLeaveYear() && !this.canReopenLeaveYear()) {
      this.error.set('Leave permission unavailable.');
      return false;
    }
    return true;
  }

  private newPolicyForm() {
    return {
      leaveTypeId: null as number | null, code: '', name: '', scopeType: 0,
      scopeReference: '', versionNo: 1, accrualMethod: null as number | null,
      annualEntitlement: 0, carryForwardAllowed: false, carryForwardCap: null as number | null,
      carryForwardExpiryMonths: null as number | null, maximumBalance: null as number | null,
      negativeBalanceAllowed: false, payrollImpactCode: '', leaveYearStartMonth: 1,
      leaveYearStartDay: 1, effectiveFrom: new Date().toISOString().slice(0, 10),
      effectiveTo: '', isActive: true
    };
  }
}
