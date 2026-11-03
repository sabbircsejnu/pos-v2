import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { UI_KIT, UiBadgeVariant, UiTableColumn } from '../../components/shared/ui';
import { AuthService } from '../../services/auth.service';
import {
  PayrollEmployeeSalary,
  PayrollEmployeeAdjustment,
  PayrollEmployeeAdjustmentRequest,
  PayrollSalaryComponentData,
  PayrollSalaryComponentRow,
  PayrollSalarySetupComponentInput,
  PayrollSalaryRevisionRequest,
  PayrollSalarySetupRequest,
  PayrollSalarySetupService,
  PayrollSalaryStructureData,
  PayrollSalaryStructureLineData,
  PayrollSalaryStructureRow
} from '../../services/payroll-salary-setup.service';

@Component({
  selector: 'app-payroll-salary-setup-page',
  standalone: true,
  imports: [FormsModule, ...UI_KIT],
  styles: [`
    .ps-page {
      padding: var(--rp-space-6);
    }

    .ps-block {
      margin-bottom: var(--rp-space-6);
    }

    .ps-nested {
      margin-top: var(--rp-space-4);
    }

    .ps-fieldset {
      min-width: 0;
      margin: 0;
      padding: var(--rp-space-4);
      border: 1px solid var(--rp-color-border);
      border-radius: var(--rp-radius-control);
    }

    .ps-fieldset legend {
      padding: 0 var(--rp-space-2);
      color: var(--rp-color-text-primary);
      font-weight: var(--rp-font-weight-medium);
    }

    .ps-subheading {
      margin: var(--rp-space-6) 0 var(--rp-space-3);
      color: var(--rp-color-text-primary);
      font-size: var(--rp-text-body-size);
      font-weight: var(--rp-font-weight-semibold);
    }

    .ps-actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--rp-space-2);
    }

    .ps-meta {
      margin: 0 0 var(--rp-space-2);
      color: var(--rp-color-text-secondary);
    }

    .ps-list {
      margin: 0 0 var(--rp-space-3);
      padding-left: var(--rp-space-5);
    }

    @media (max-width: 767px) {
      .ps-page {
        padding: var(--rp-space-4);
      }
    }
  `],
  template: `
    <main class="ps-page">
      <app-ui-page-header title="Payroll Salary Setup"
        description="Manage reusable Business-scoped salary components and structure templates." />
      @if (error()) {
        <app-ui-alert variant="danger" class="ps-block">{{ error() }}</app-ui-alert>
      }
      @if (busy()) {
        <app-ui-alert variant="info" class="ps-block">Loading / saving...</app-ui-alert>
      }
      @if (!canView()) {
        <app-ui-alert variant="warning" class="ps-block">You do not have salary setup view access.</app-ui-alert>
      }

      @if (canView()) {
        <app-ui-card title="Initial employee salary setup" class="ps-block">
          <form (ngSubmit)="createInitialSalarySetup()" class="grid gap-4 md:grid-cols-2">
            <app-ui-form-field label="Employee ID" [required]="true">
              <input appUiControl name="setupEmployeeId" type="number" min="1" required [(ngModel)]="setupEmployeeId">
            </app-ui-form-field>
            <app-ui-form-field label="Salary structure" [required]="true">
              <select appUiControl name="setupStructureId" required [(ngModel)]="setupStructureId" (ngModelChange)="selectSetupStructure()">
                <option [ngValue]="0">Select structure</option>
                @for (structure of activeStructures(); track structure) {
                  <option [ngValue]="structure.id">
                    {{ structure.data.code }} - {{ structure.data.name }}
                  </option>
                }
              </select>
            </app-ui-form-field>
            <app-ui-form-field label="Effective from" [required]="true">
              <input appUiControl name="effectiveFrom" type="date" appCompanyDate required [(ngModel)]="effectiveFrom">
            </app-ui-form-field>
            <app-ui-form-field label="Reason" [required]="true">
              <input appUiControl name="setupReason" required maxlength="500" [(ngModel)]="setupReason">
            </app-ui-form-field>
            @if (selectedSetupStructure(); as structure) {
              <fieldset class="ps-fieldset md:col-span-2">
                <legend>Proposed component values</legend>
                <div class="grid gap-4 md:grid-cols-2">
                  @for (line of structure.data.lines; track line; let i = $index) {
                    <app-ui-form-field [label]="componentName(line.salaryComponentId)"
                      [hint]="isSetupValueEditable(line.salaryComponentId) ? '' : 'Calculated by the salary structure'">
                      <input appUiControl [name]="'setupComponent'+i" type="number" min="0" step="0.0001"
                        [disabled]="!canManage() || !isSetupValueEditable(line.salaryComponentId)"
                        [(ngModel)]="setupComponentValues[line.salaryComponentId]">
                    </app-ui-form-field>
                  }
                </div>
              </fieldset>
            }
            <div class="ps-actions md:col-span-2">
              <button appUiButton type="submit" [disabled]="busy() || !canCreateSetup()">Create salary setup request</button>
            </div>
          </form>
          @if (!canCreateSetup()) {
            <app-ui-alert variant="info" class="ps-nested">Salary setup creation requires salary manage and setup-create permissions.</app-ui-alert>
          }
        </app-ui-card>

        <app-ui-card title="Employee salary requests and approved salary" class="ps-block">
          <form (ngSubmit)="loadEmployeeSalary()" class="flex flex-wrap items-end gap-4">
            <app-ui-form-field label="Employee ID" [required]="true">
              <input appUiControl name="historyEmployeeId" type="number" min="1" required [(ngModel)]="historyEmployeeId">
            </app-ui-form-field>
            <button appUiButton type="submit" [disabled]="busy()">Load requests and approved salary</button>
          </form>
          @if (salaryRequests().length) {
            <app-ui-form-field class="ps-nested" label="Decision reason (required for reject/cancel)">
              <input appUiControl name="decisionReason" maxlength="500" [(ngModel)]="decisionReason">
            </app-ui-form-field>
          }
          @if (salaryHistoryLoaded() && !salaryRequests().length && !currentSalary()) {
            <app-ui-state class="ps-nested" [compact]="true"
              title="No salary setup requests or approved salary were found." />
          }
          @if (currentSalary(); as salary) {
            <app-ui-card class="ps-nested" title="Approved effective salary" [headingLevel]="3">
              <p class="ps-meta">{{ salary.structureCode }} — effective {{ (salary.effectiveFrom | appDate) }}</p>
              @if (salary.salaryRevisionRequestId) {
                <p class="ps-meta">
                  Approved revision #{{ salary.salaryRevisionRequestId }}
                  from salary version #{{ salary.predecessorSalaryVersionId }}
                </p>
              }
              <app-ui-data-table [columns]="salaryComponentColumns" [rows]="salary.components"
                rowKey="salaryComponentId" [searchable]="false" [paginated]="false" density="compact"
                ariaLabel="Approved salary components" emptyTitle="No salary components" />
              @if (canCreateRevision()) {
                <form (ngSubmit)="createSalaryRevision()" class="ps-nested grid gap-4">
                  <h3 class="ps-subheading">Propose salary revision</h3>
                  <app-ui-form-field label="Effective from" [required]="true">
                    <input appUiControl name="revisionEffectiveFrom" type="date" appCompanyDate required [(ngModel)]="revisionEffectiveFrom">
                  </app-ui-form-field>
                  <app-ui-form-field label="Reason" [required]="true">
                    <input appUiControl name="revisionReason" required maxlength="1000" [(ngModel)]="revisionReason">
                  </app-ui-form-field>
                  <fieldset class="ps-fieldset">
                    <legend>Proposed component values (structure stays frozen)</legend>
                    <div class="grid gap-4 md:grid-cols-2">
                      @for (component of salary.components; track component; let i = $index) {
                        <app-ui-form-field [label]="component.code + ' — ' + component.componentType"
                          [hint]="component.valueType === 'Formula' || component.valueType === 'Calculated' ? 'Retained from the frozen salary snapshot' : ''">
                          <input appUiControl [name]="'revisionComponent'+i" type="number" min="0" step="0.0001"
                            [disabled]="component.valueType === 'Formula' || component.valueType === 'Calculated'"
                            [(ngModel)]="revisionComponentValues[component.salaryComponentId]">
                        </app-ui-form-field>
                      }
                    </div>
                  </fieldset>
                  <div class="ps-actions">
                    <button appUiButton type="submit" [disabled]="busy()">Create revision request</button>
                  </div>
                </form>
              }
              @if (!canCreateRevision()) {
                <app-ui-alert variant="info" class="ps-nested">Salary manage and revision-create permissions are required to propose a revision.</app-ui-alert>
              }
            </app-ui-card>
          }

          @if (salaryRequests().length) {
            <h3 class="ps-subheading">Setup request and approval history</h3>
          }
          @for (request of salaryRequests(); track request) {
            <app-ui-card class="ps-nested" [headingLevel]="4" [title]="'Request #' + request.id"
              [subtitle]="request.structureCode + ' · effective ' + request.effectiveFrom">
              <app-ui-badge uiCardActions [variant]="statusVariant(request.status)">{{ request.status }}</app-ui-badge>
              <p class="ps-meta">Structure version {{ request.salaryStructureVersion }} · request version {{ request.version }}</p>
              @if (request.submittedAtUtc) {
                <p class="ps-meta">Submitted {{ request.submittedAtUtc | appDate:'medium' }}</p>
              }
              @if (request.decidedAtUtc) {
                <p class="ps-meta">Decision {{ request.decisionReason || request.status }} · {{ request.decidedAtUtc | appDate:'medium' }}</p>
              }
              @if (request.approvalSteps.length) {
                <p class="ps-meta">Approval steps: {{ request.approvalSteps.length }}</p>
              }
              @if (request.approvalHistory.length) {
                <ul class="ps-list">
                  @for (decision of request.approvalHistory; track decision) {
                    <li>
                      {{ decision.decision }} · {{ decision.occurredAtUtc | appDate:'medium' }}
                      @if (decision.reason) {
                        <span> — {{ decision.reason }}</span>
                      }
                    </li>
                  }
                </ul>
              }
              @if (request.status === 'Approved') {
                <p class="ps-meta">Approved salary snapshot is effective from {{ (request.effectiveFrom | appDate) }}.</p>
              }
              <div class="ps-actions">
                @if (request.status === 'Draft' && canSubmit()) {
                  <button appUiButton size="sm" type="button" [disabled]="busy()"
                    (click)="submitRequest(request)">Submit for approval</button>
                }
                @if (request.status === 'PendingApproval' && canApprove()) {
                  <button appUiButton size="sm" type="button" [disabled]="busy()"
                    (click)="decideRequest(request, 'approve')">Approve</button>
                }
                @if (request.status === 'PendingApproval' && canApprove()) {
                  <button appUiButton size="sm" variant="danger" type="button" [disabled]="busy()"
                    (click)="decideRequest(request, 'reject')">Reject</button>
                }
                @if (request.status === 'PendingApproval' && canSubmit()) {
                  <button appUiButton size="sm" variant="outline" type="button" [disabled]="busy()"
                    (click)="decideRequest(request, 'cancel')">Cancel request</button>
                }
              </div>
            </app-ui-card>
          }

          @if (salaryRevisionRequests().length) {
            <h3 class="ps-subheading">Salary revision history</h3>
          }
          @for (request of salaryRevisionRequests(); track request) {
            <app-ui-card class="ps-nested" [headingLevel]="4" [title]="'Revision #' + request.id"
              [subtitle]="'effective ' + request.effectiveFrom">
              <app-ui-badge uiCardActions [variant]="statusVariant(request.status)">{{ request.status }}</app-ui-badge>
              <p class="ps-meta">Source salary version #{{ request.sourceSalaryVersionId }} · request version {{ request.version }}</p>
              <p>{{ request.reason }}</p>
              <app-ui-data-table [columns]="salaryComponentColumns" [rows]="request.proposedComponents"
                rowKey="salaryComponentId" [searchable]="false" [paginated]="false" density="compact"
                [ariaLabel]="'Proposed components of revision #' + request.id" emptyTitle="No proposed components" />
              @if (request.approvalHistory.length) {
                <ul class="ps-list ps-nested">
                  @for (decision of request.approvalHistory; track decision) {
                    <li>
                      {{ decision.decision }} · {{ decision.occurredAtUtc | appDate:'medium' }}
                      @if (decision.reason) {
                        <span> — {{ decision.reason }}</span>
                      }
                    </li>
                  }
                </ul>
              }
              <div class="ps-actions ps-nested">
                @if (request.status === 'Draft' && canSubmitRevision()) {
                  <button appUiButton size="sm" type="button" [disabled]="busy()"
                    (click)="submitRevision(request)">Submit for approval</button>
                }
                @if (request.status === 'PendingApproval' && canApproveRevision()) {
                  <button appUiButton size="sm" type="button" [disabled]="busy()"
                    (click)="decideRevision(request, 'approve')">Approve</button>
                }
                @if (request.status === 'PendingApproval' && canApproveRevision()) {
                  <button appUiButton size="sm" variant="danger" type="button" [disabled]="busy()"
                    (click)="decideRevision(request, 'reject')">Reject</button>
                }
                @if (request.status === 'PendingApproval' && canSubmitRevision()) {
                  <button appUiButton size="sm" variant="outline" type="button" [disabled]="busy()"
                    (click)="decideRevision(request, 'cancel')">Cancel request</button>
                }
              </div>
            </app-ui-card>
          }
        </app-ui-card>
      }

      @if (canViewAdjustments()) {
        <app-ui-card title="Employee allowances and deductions" subtitle="Fixed-amount adjustment records are effective-dated and versioned; amounts are visible only with adjustment view permission." class="ps-block">
          <form (ngSubmit)="loadAdjustments()" class="flex flex-wrap items-end gap-4">
            <app-ui-form-field label="Employee ID" [required]="true">
              <input appUiControl name="adjustmentEmployeeId" type="number" min="1" required [(ngModel)]="adjustmentEmployeeId">
            </app-ui-form-field>
            <app-ui-form-field label="Effective on" [required]="true">
              <input appUiControl name="adjustmentAsOf" type="date" appCompanyDate required [(ngModel)]="adjustmentAsOf">
            </app-ui-form-field>
            <button appUiButton type="submit" [disabled]="busy()">Load adjustments</button>
          </form>
          @if (adjustmentsLoaded() && !adjustments().length) {
            <app-ui-state class="ps-nested" [compact]="true" title="No effective adjustments were found." />
          }
          @if (canManageAdjustments()) {
            <app-ui-card             class="ps-nested" [headingLevel]="3"              [title]="adjustmentToVersion ? 'Create recurring adjustment successor' : 'Create employee adjustment'">
              <form (ngSubmit)="saveAdjustment()" class="grid gap-4 md:grid-cols-2">
                <app-ui-form-field label="Adjustment type">
                  <select appUiControl name="adjustmentKind" [(ngModel)]="adjustmentKind" (ngModelChange)="resetAdjustmentComponent()"
                    [disabled]="!!adjustmentToVersion">
                    <option value="RecurringAllowance">Recurring allowance</option>
                    <option value="RecurringDeduction">Recurring deduction</option>
                    <option value="OneTimeEarning">One-time earning</option>
                    <option value="OneTimeDeduction">One-time deduction</option>
                  </select>
                </app-ui-form-field>
                <app-ui-form-field label="Salary component" [required]="true">
                  <select appUiControl name="adjustmentComponent" required [(ngModel)]="adjustmentComponentId"
                    [disabled]="!!adjustmentToVersion">
                    <option [ngValue]="0">Select active fixed component</option>
                    @for (component of eligibleAdjustmentComponents(); track component) {
                      <option [ngValue]="component.id">
                        {{ component.data.code }} — {{ component.data.name }}
                      </option>
                    }
                  </select>
                </app-ui-form-field>
                <app-ui-form-field label="Classification">
                  <select appUiControl name="adjustmentClassification" [(ngModel)]="adjustmentClassification">
                    <option value="Manual">Manual</option><option value="Statutory">Statutory</option>
                    <option value="Incentive">Incentive</option><option value="Bonus">Bonus</option>
                  </select>
                </app-ui-form-field>
                <app-ui-form-field label="Amount" [required]="true">
                  <input appUiControl name="adjustmentAmount" type="number" min="0.0001" max="99999999999999.9999" step="0.0001"
                    required [(ngModel)]="adjustmentAmount">
                </app-ui-form-field>
                <app-ui-form-field label="Effective from" [required]="true">
                  <input appUiControl name="adjustmentEffectiveFrom" type="date" appCompanyDate required [(ngModel)]="adjustmentEffectiveFrom">
                </app-ui-form-field>
                <app-ui-form-field label="Effective to (optional)">
                  <input appUiControl name="adjustmentEffectiveTo" type="date" appCompanyDate [(ngModel)]="adjustmentEffectiveTo">
                </app-ui-form-field>
                <app-ui-form-field label="Reason" [required]="true">
                  <input appUiControl name="adjustmentReason" required maxlength="1000" [(ngModel)]="adjustmentReason">
                </app-ui-form-field>
                <app-ui-form-field label="Reference (optional)">
                  <input appUiControl name="adjustmentReference" maxlength="150" [(ngModel)]="adjustmentReference">
                </app-ui-form-field>
                <div class="ps-actions md:col-span-2">
                  <button appUiButton type="submit" [disabled]="busy() || !adjustmentEmployeeId || !adjustmentComponentId">
                    {{ adjustmentToVersion ? 'Create successor version' : 'Save adjustment' }}
                  </button>
                  @if (adjustmentToVersion) {
                    <button appUiButton variant="outline" type="button" (click)="resetAdjustmentForm()">Cancel version</button>
                  }
                </div>
              </form>
            </app-ui-card>
          }
          @if (canManageAdjustments() && !eligibleAdjustmentComponents().length) {
            <app-ui-alert variant="info" class="ps-nested">
              No active fixed salary components match the selected adjustment type.
            </app-ui-alert>
          }
          @if (!canManageAdjustments()) {
            <app-ui-alert variant="info" class="ps-nested">Adjustment management permission is required to create, version, or end adjustments.</app-ui-alert>
          }
          @if (adjustments().length) {
            <app-ui-data-table class="ps-nested" [columns]="adjustmentColumns" [rows]="adjustments()"
              rowKey="id" density="compact" ariaLabel="Employee adjustments"
              searchLabel="Search adjustments" searchPlaceholder="Search adjustments…"
              emptyTitle="No effective adjustments were found.">
              <ng-template uiTableRowActions let-row>
                <div class="ps-actions">
                  <button appUiButton size="sm" variant="outline" type="button" [disabled]="busy()"
                    [attr.aria-label]="'Version history of ' + row.componentCode + ' adjustment'"
                    (click)="loadAdjustmentHistory(row)">Version history</button>
                  @if (canManageAdjustments() && row.status === 'Active' && isRecurringAdjustment(row)) {
                    <button appUiButton size="sm" variant="outline" type="button" [disabled]="busy()"
                      [attr.aria-label]="'Create successor of ' + row.componentCode + ' adjustment'"
                      (click)="beginAdjustmentVersion(row)">Create successor</button>
                  }
                  @if (canManageAdjustments() && row.status === 'Active' && isRecurringAdjustment(row)) {
                    <button appUiButton size="sm" variant="outline" type="button" [disabled]="busy()"
                      [attr.aria-label]="'End ' + row.componentCode + ' adjustment'"
                      (click)="endAdjustment(row)">End adjustment</button>
                  }
                </div>
              </ng-template>
            </app-ui-data-table>
          }
          @if (adjustmentHistory().length) {
            <app-ui-card class="ps-nested" title="Adjustment version history" [headingLevel]="3">
              <app-ui-data-table [columns]="adjustmentHistoryColumns" [rows]="adjustmentHistory()"
                rowKey="id" [searchable]="false" [paginated]="false" density="compact"
                ariaLabel="Adjustment version history" emptyTitle="No version history" />
            </app-ui-card>
          }
        </app-ui-card>
      }

      @if (canView()) {
        <app-ui-card title="Salary components" class="ps-block">
          @if (canManage()) {
            <app-ui-card [headingLevel]="3" [title]="componentEdit ? 'Edit component' : 'Create component'">
              <form (ngSubmit)="saveComponent()" class="grid gap-4 md:grid-cols-2">
                <app-ui-form-field label="Code" [required]="true">
                  <input appUiControl name="componentCode" required maxlength="50" [(ngModel)]="componentData.code"
                    [readonly]="!!componentEdit">
                </app-ui-form-field>
                <app-ui-form-field label="Name" [required]="true">
                  <input appUiControl name="componentName" required maxlength="100" [(ngModel)]="componentData.name">
                </app-ui-form-field>
                <app-ui-form-field label="Type">
                  <select appUiControl name="componentType" [(ngModel)]="componentData.componentType">
                    <option value="Earning">Earning</option><option value="Deduction">Deduction</option>
                  </select>
                </app-ui-form-field>
                <app-ui-form-field label="Value type">
                  <select appUiControl name="valueType" [(ngModel)]="componentData.valueType">
                    <option value="Fixed">Fixed</option><option value="Percentage">Percentage</option>
                    <option value="Formula">Formula</option><option value="Calculated">Calculated</option>
                  </select>
                </app-ui-form-field>
                @if (componentData.valueType === 'Formula') {
                  <app-ui-form-field class="md:col-span-2" [required]="true"
                    label="Formula (component codes, decimal numbers, + - * / and parentheses)">
                    <textarea appUiControl name="formulaExpression" required maxlength="1000"
                      [(ngModel)]="componentData.formulaExpression"></textarea>
                  </app-ui-form-field>
                }
                <app-ui-form-field label="Tax treatment code">
                  <input appUiControl name="taxTreatmentCode" maxlength="50" [(ngModel)]="componentData.taxTreatmentCode">
                </app-ui-form-field>
                <app-ui-form-field label="Calculation order" [required]="true">
                  <input appUiControl name="componentOrder" type="number" min="0" required [(ngModel)]="componentData.calculationOrder">
                </app-ui-form-field>
                <label class="ui-check"><input appUiControl name="isRecurring" type="checkbox" [(ngModel)]="componentData.isRecurring"> Recurring</label>
                <div class="ps-actions md:col-span-2">
                  <button appUiButton type="submit" [disabled]="busy()">Save component</button>
                  @if (componentEdit) {
                    <button appUiButton variant="outline" type="button" (click)="resetComponent()">Cancel edit</button>
                  }
                </div>
              </form>
            </app-ui-card>
          }
          <app-ui-data-table [class.ps-nested]="canManage()" [columns]="componentColumns" [rows]="components()"
            rowKey="id" density="compact" ariaLabel="Salary components" [loading]="busy()"
            searchLabel="Search salary components" searchPlaceholder="Search salary components…"
            emptyTitle="No salary components have been configured.">
            <ng-template uiTableCell="valueType" let-row>
              {{ row.data.valueType }}@if (row.data.formulaExpression) {<span>: {{ row.data.formulaExpression }}</span>}
            </ng-template>
            @if (canManage()) {
              <ng-template uiTableRowActions let-row>
                @if (row.isActive) {
                  <div class="ps-actions">
                    <button appUiButton size="sm" variant="outline" type="button"
                      [attr.aria-label]="'Edit component ' + row.data.code" (click)="editComponent(row)">Edit</button>
                    <button appUiButton size="sm" variant="outline" type="button"
                      [attr.aria-label]="'Deactivate component ' + row.data.code" (click)="deactivateComponent(row)">Deactivate</button>
                  </div>
                }
              </ng-template>
            }
          </app-ui-data-table>
        </app-ui-card>
      }

      @if (canView()) {
        <app-ui-card title="Salary structure templates" class="ps-block">
          @if (canManage()) {
            <app-ui-card [headingLevel]="3" [title]="structureEdit ? 'Edit structure' : 'Create structure'">
              <form (ngSubmit)="saveStructure()" class="grid gap-4 md:grid-cols-2">
                <app-ui-form-field label="Code" [required]="true">
                  <input appUiControl name="structureCode" required maxlength="50" [(ngModel)]="structureData.code"
                    [readonly]="!!structureEdit">
                </app-ui-form-field>
                <app-ui-form-field label="Name" [required]="true">
                  <input appUiControl name="structureName" required maxlength="100" [(ngModel)]="structureData.name">
                </app-ui-form-field>
                <fieldset class="ps-fieldset md:col-span-2">
                  <legend>Included components</legend>
                  @for (line of structureData.lines; track line; let i = $index) {
                    <div class="ps-nested grid items-start gap-4 md:grid-cols-4">
                      <app-ui-form-field label="Component" [required]="true">
                        <select appUiControl [name]="'lineComponent'+i" [(ngModel)]="line.salaryComponentId" required>
                          <option [ngValue]="0">Select component</option>
                          @for (component of activeComponents(); track component) {
                            <option [ngValue]="component.id">
                              {{ component.data.code }} - {{ component.data.name }}
                            </option>
                          }
                        </select>
                      </app-ui-form-field>
                      <app-ui-form-field label="Default value">
                        <input appUiControl [name]="'lineDefault'+i" type="number" min="0" step="0.0001" [(ngModel)]="line.defaultValue">
                      </app-ui-form-field>
                      @if (isFormulaComponent(line.salaryComponentId)) {
                        <app-ui-form-field label="Formula override">
                          <input appUiControl [name]="'lineFormula'+i" maxlength="1000" [(ngModel)]="line.formulaOverride">
                        </app-ui-form-field>
                      }
                      <app-ui-form-field label="Calculation order" [required]="true">
                        <input appUiControl [name]="'lineOrder'+i" type="number" min="0" required [(ngModel)]="line.calculationOrder">
                      </app-ui-form-field>
                      <div class="ps-actions">
                        <button appUiButton variant="outline" size="sm" type="button" [attr.aria-label]="'Remove line ' + (i + 1)"
                          (click)="removeLine(i)" [disabled]="structureData.lines.length <= 1">Remove line</button>
                      </div>
                    </div>
                  }
                  <div class="ps-actions ps-nested">
                    <button appUiButton variant="outline" size="sm" type="button" (click)="addLine()">Add component</button>
                  </div>
                </fieldset>
                <div class="ps-actions md:col-span-2">
                  <button appUiButton type="submit" [disabled]="busy()">Save structure</button>
                  @if (structureEdit) {
                    <button appUiButton variant="outline" type="button" (click)="resetStructure()">Cancel edit</button>
                  }
                </div>
              </form>
            </app-ui-card>
          }
          <app-ui-data-table [class.ps-nested]="canManage()" [columns]="structureColumns" [rows]="structures()"
            rowKey="id" density="compact" ariaLabel="Salary structure templates" [loading]="busy()"
            searchLabel="Search salary structure templates" searchPlaceholder="Search salary structures…"
            emptyTitle="No salary structure templates have been configured.">
            @if (canManage()) {
              <ng-template uiTableRowActions let-row>
                @if (row.isActive) {
                  <div class="ps-actions">
                    <button appUiButton size="sm" variant="outline" type="button"
                      [attr.aria-label]="'Edit structure ' + row.data.code" (click)="editStructure(row)">Edit</button>
                    <button appUiButton size="sm" variant="outline" type="button"
                      [attr.aria-label]="'Deactivate structure ' + row.data.code" (click)="deactivateStructure(row)">Deactivate</button>
                  </div>
                }
              </ng-template>
            }
          </app-ui-data-table>
        </app-ui-card>
      }
    </main>
  `
})
export class PayrollSalarySetupPageComponent implements OnInit {
  private readonly service = inject(PayrollSalarySetupService);
  private readonly auth = inject(AuthService);

  private static readonly activeStatusMap: Record<string, { label: string; variant: UiBadgeVariant }> = {
    true: { label: 'Active', variant: 'success' },
    false: { label: 'Inactive', variant: 'neutral' }
  };

  readonly salaryComponentColumns: UiTableColumn[] = [
    { key: 'code', header: 'Component' },
    { key: 'value', header: 'Value', align: 'end', emptyText: '' }
  ];

  readonly componentColumns: UiTableColumn[] = [
    { key: 'code', header: 'Code', field: 'data.code', sortable: true },
    { key: 'name', header: 'Name', field: 'data.name', sortable: true },
    { key: 'componentType', header: 'Type', field: 'data.componentType', sortable: true },
    { key: 'valueType', header: 'Value', field: 'data.valueType' },
    {
      key: 'status', header: 'Status', field: 'isActive', type: 'status',
      statusMap: PayrollSalarySetupPageComponent.activeStatusMap
    },
    { key: 'version', header: 'Version', align: 'end' }
  ];

  readonly structureColumns: UiTableColumn[] = [
    { key: 'code', header: 'Code', field: 'data.code', sortable: true },
    { key: 'name', header: 'Name', field: 'data.name', sortable: true },
    { key: 'lines', header: 'Components', field: 'data.lines.length', align: 'end' },
    {
      key: 'status', header: 'Status', field: 'isActive', type: 'status',
      statusMap: PayrollSalarySetupPageComponent.activeStatusMap
    },
    { key: 'version', header: 'Version', align: 'end' }
  ];

  readonly adjustmentColumns: UiTableColumn[] = [
    { key: 'componentCode', header: 'Component', sortable: true },
    { key: 'adjustmentKind', header: 'Adjustment type', sortable: true },
    { key: 'classification', header: 'Classification', sortable: true },
    { key: 'amount', header: 'Amount', align: 'end', emptyText: '' },
    { key: 'effectiveFrom', type: 'date', header: 'Effective from', sortable: true },
    { key: 'effectiveTo', type: 'date', header: 'Effective to' },
    {
      key: 'status', header: 'Status', type: 'status',
      statusMap: { Active: { label: 'Active', variant: 'success' } }
    },
    { key: 'version', header: 'Version', align: 'end' },
    { key: 'reason', header: 'Reason' },
    { key: 'reference', header: 'Reference' }
  ];

  readonly adjustmentHistoryColumns: UiTableColumn[] = [
    { key: 'version', header: 'Version', align: 'end' },
    {
      key: 'status', header: 'Status', type: 'status',
      statusMap: { Active: { label: 'Active', variant: 'success' } }
    },
    { key: 'amount', header: 'Amount', align: 'end', emptyText: '' },
    { key: 'effectiveFrom', type: 'date', header: 'Effective from' },
    { key: 'predecessorVersionId', header: 'Successor of version record', align: 'end' }
  ];

  readonly components = signal<PayrollSalaryComponentRow[]>([]);
  readonly structures = signal<PayrollSalaryStructureRow[]>([]);
  readonly salaryRequests = signal<PayrollSalarySetupRequest[]>([]);
  readonly salaryRevisionRequests = signal<PayrollSalaryRevisionRequest[]>([]);
  readonly currentSalary = signal<PayrollEmployeeSalary | null>(null);
  readonly adjustments = signal<PayrollEmployeeAdjustment[]>([]);
  readonly adjustmentHistory = signal<PayrollEmployeeAdjustment[]>([]);
  readonly adjustmentsLoaded = signal(false);
  readonly salaryHistoryLoaded = signal(false);
  readonly error = signal('');
  readonly busy = signal(false);
  setupEmployeeId: number | null = null;
  setupStructureId = 0;
  effectiveFrom = new Date().toISOString().slice(0, 10);
  setupReason = '';
  setupComponentValues: Record<number, number | null> = {};
  historyEmployeeId: number | null = null;
  decisionReason = '';
  revisionEffectiveFrom = new Date().toISOString().slice(0, 10);
  revisionReason = '';
  revisionComponentValues: Record<number, number | null> = {};
  adjustmentEmployeeId: number | null = null;
  adjustmentAsOf = new Date().toISOString().slice(0, 10);
  adjustmentKind: PayrollEmployeeAdjustment['adjustmentKind'] = 'RecurringAllowance';
  adjustmentClassification: PayrollEmployeeAdjustment['classification'] = 'Manual';
  adjustmentComponentId = 0;
  adjustmentAmount: number | null = null;
  adjustmentEffectiveFrom = new Date().toISOString().slice(0, 10);
  adjustmentEffectiveTo = '';
  adjustmentReason = '';
  adjustmentReference = '';
  adjustmentToVersion: PayrollEmployeeAdjustment | null = null;

  componentEdit: PayrollSalaryComponentRow | null = null;
  structureEdit: PayrollSalaryStructureRow | null = null;
  componentData: PayrollSalaryComponentData = this.emptyComponent();
  structureData: PayrollSalaryStructureData = this.emptyStructure();

  ngOnInit(): void {
    if (this.canView()) this.refresh();
  }

  canView(): boolean {
    return this.auth.hasPermission('payroll.salary.view');
  }

  statusVariant(status: string): UiBadgeVariant {
    switch (status) {
      case 'Approved': return 'success';
      case 'PendingApproval': return 'warning';
      case 'Rejected': return 'danger';
      default: return 'neutral';
    }
  }

  canViewAdjustments(): boolean {
    return this.auth.hasPermission('payroll.allowance_deduction.view');
  }

  canManageAdjustments(): boolean {
    return this.auth.hasPermission('payroll.allowance_deduction.manage');
  }

  canManage(): boolean {
    return this.auth.hasPermission('payroll.salary.manage');
  }

  canCreateSetup(): boolean {
    return this.canManage() && this.auth.hasPermission('payroll.salary_setup.create');
  }

  canSubmit(): boolean {
    return this.auth.hasPermission('payroll.salary_setup.submit');
  }

  canApprove(): boolean {
    return this.auth.hasPermission('payroll.salary_setup.approve');
  }

  canCreateRevision(): boolean {
    return this.canManage() && this.auth.hasPermission('payroll.salary_revision.create')
      && !!this.currentSalary();
  }

  canSubmitRevision(): boolean {
    return this.auth.hasPermission('payroll.salary_revision.submit');
  }

  canApproveRevision(): boolean {
    return this.auth.hasPermission('payroll.salary_revision.approve');
  }

  activeStructures(): PayrollSalaryStructureRow[] {
    return this.structures().filter(row => row.isActive);
  }

  selectedSetupStructure(): PayrollSalaryStructureRow | undefined {
    return this.activeStructures().find(row => row.id === this.setupStructureId);
  }

  componentName(id: number): string {
    const component = this.components().find(row => row.id === id);
    return component ? `${component.data.code} — ${component.data.name}` : `Component ${id}`;
  }

  isSetupValueEditable(id: number): boolean {
    const component = this.components().find(row => row.id === id);
    return component?.data.valueType === 'Fixed' || component?.data.valueType === 'Percentage';
  }

  eligibleAdjustmentComponents(): PayrollSalaryComponentRow[] {
    const deduction = this.adjustmentKind === 'RecurringDeduction'
      || this.adjustmentKind === 'OneTimeDeduction';
    const recurring = this.adjustmentKind === 'RecurringAllowance'
      || this.adjustmentKind === 'RecurringDeduction';
    return this.activeComponents().filter(row =>
      row.data.valueType === 'Fixed'
      && row.data.componentType === (deduction ? 'Deduction' : 'Earning')
      && row.data.isRecurring === recurring
    );
  }

  resetAdjustmentComponent(): void {
    if (!this.eligibleAdjustmentComponents().some(row => row.id === this.adjustmentComponentId)) {
      this.adjustmentComponentId = 0;
    }
  }

  loadAdjustments(): void {
    if (!this.canViewAdjustments() || !this.adjustmentEmployeeId || this.adjustmentEmployeeId <= 0
      || !this.adjustmentAsOf) {
      this.error.set('Adjustment view permission, employee ID, and effective date are required.');
      return;
    }
    this.error.set('');
    this.busy.set(true);
    this.adjustmentsLoaded.set(false);
    this.adjustmentHistory.set([]);
    this.service.employeeAdjustments(this.adjustmentEmployeeId, this.adjustmentAsOf).subscribe({
      next: rows => {
        this.adjustments.set(rows);
        this.adjustmentsLoaded.set(true);
        this.busy.set(false);
      },
      error: error => {
        this.adjustments.set([]);
        this.error.set(this.errorMessage(error));
        this.busy.set(false);
      }
    });
  }

  loadAdjustmentHistory(adjustment: PayrollEmployeeAdjustment): void {
    if (!this.canViewAdjustments()) return;
    this.error.set('');
    this.busy.set(true);
    this.service.adjustmentHistory(adjustment.adjustmentIdentity).subscribe({
      next: rows => {
        this.adjustmentHistory.set(rows);
        this.busy.set(false);
      },
      error: error => { this.error.set(this.errorMessage(error)); this.busy.set(false); }
    });
  }

  saveAdjustment(): void {
    if (!this.canManageAdjustments()) {
      this.error.set('Allowance and deduction manage permission is required.');
      return;
    }
    const component = this.eligibleAdjustmentComponents()
      .find(row => row.id === this.adjustmentComponentId);
    if (!this.adjustmentEmployeeId || !component || !this.adjustmentAmount
      || this.adjustmentAmount <= 0 || !this.adjustmentEffectiveFrom
      || !this.adjustmentReason.trim()
      || (this.adjustmentEffectiveTo && this.adjustmentEffectiveTo < this.adjustmentEffectiveFrom)) {
      this.error.set('Employee, matching active fixed component, positive amount, valid dates, and reason are required.');
      return;
    }
    if ((this.adjustmentClassification === 'Incentive' || this.adjustmentClassification === 'Bonus')
      && this.adjustmentKind !== 'OneTimeEarning') {
      this.error.set('Incentive and bonus classifications require a one-time earning.');
      return;
    }
    if (this.adjustmentClassification === 'Statutory'
      && this.adjustmentKind !== 'RecurringDeduction' && this.adjustmentKind !== 'OneTimeDeduction') {
      this.error.set('Statutory classification requires a deduction.');
      return;
    }
    const request: PayrollEmployeeAdjustmentRequest = {
      adjustmentIdentity: this.adjustmentToVersion?.adjustmentIdentity ?? null,
      expectedVersion: this.adjustmentToVersion?.version ?? null,
      employeeId: this.adjustmentEmployeeId,
      salaryComponentId: component.id,
      adjustmentKind: this.adjustmentKind,
      classification: this.adjustmentClassification,
      amount: this.adjustmentAmount,
      effectiveFrom: this.adjustmentEffectiveFrom,
      effectiveTo: this.adjustmentEffectiveTo || null,
      reason: this.adjustmentReason.trim(),
      reference: this.adjustmentReference.trim() || null,
      idempotencyKey: crypto.randomUUID()
    };
    this.error.set('');
    this.busy.set(true);
    this.service.saveAdjustment(request).subscribe({
      next: () => {
        this.resetAdjustmentForm();
        this.loadAdjustments();
      },
      error: error => { this.error.set(this.errorMessage(error)); this.busy.set(false); }
    });
  }

  beginAdjustmentVersion(adjustment: PayrollEmployeeAdjustment): void {
    if (!this.canManageAdjustments() || !this.isRecurringAdjustment(adjustment)) return;
    this.adjustmentToVersion = adjustment;
    this.adjustmentEmployeeId = adjustment.employeeId;
    this.adjustmentKind = adjustment.adjustmentKind;
    this.adjustmentClassification = adjustment.classification;
    this.adjustmentComponentId = adjustment.salaryComponentId;
    this.adjustmentAmount = adjustment.amount;
    this.adjustmentEffectiveFrom = this.nextDate(adjustment.effectiveFrom);
    this.adjustmentEffectiveTo = adjustment.effectiveTo
      && adjustment.effectiveTo >= this.adjustmentEffectiveFrom
      ? adjustment.effectiveTo
      : '';
    this.adjustmentReason = adjustment.reason;
    this.adjustmentReference = adjustment.reference ?? '';
  }

  endAdjustment(adjustment: PayrollEmployeeAdjustment): void {
    if (!this.canManageAdjustments()) {
      this.error.set('Allowance and deduction manage permission is required.');
      return;
    }
    if (!this.isRecurringAdjustment(adjustment) || !this.adjustmentEffectiveFrom
      || this.adjustmentEffectiveFrom <= adjustment.effectiveFrom || !this.adjustmentReason.trim()) {
      this.error.set('A recurring adjustment requires an end date after its start date and a reason.');
      return;
    }
    this.error.set('');
    this.busy.set(true);
    this.service.endAdjustment(
      adjustment, this.adjustmentEffectiveFrom, this.adjustmentReason.trim()
    ).subscribe({
      next: () => this.loadAdjustments(),
      error: error => { this.error.set(this.errorMessage(error)); this.busy.set(false); }
    });
  }

  isRecurringAdjustment(adjustment: PayrollEmployeeAdjustment): boolean {
    return adjustment.adjustmentKind === 'RecurringAllowance'
      || adjustment.adjustmentKind === 'RecurringDeduction';
  }

  resetAdjustmentForm(): void {
    this.adjustmentToVersion = null;
    this.adjustmentKind = 'RecurringAllowance';
    this.adjustmentClassification = 'Manual';
    this.adjustmentComponentId = 0;
    this.adjustmentAmount = null;
    this.adjustmentEffectiveTo = '';
    this.adjustmentReason = '';
    this.adjustmentReference = '';
  }

  private nextDate(date: string): string {
    const [year, month, day] = date.split('-').map(Number);
    return new Date(Date.UTC(year, month - 1, day + 1)).toISOString().slice(0, 10);
  }

  selectSetupStructure(): void {
    const structure = this.selectedSetupStructure();
    this.setupComponentValues = {};
    for (const line of structure?.data.lines ?? []) {
      this.setupComponentValues[line.salaryComponentId] = line.defaultValue;
    }
  }

  createInitialSalarySetup(): void {
    if (!this.canCreateSetup()) {
      this.error.set('Salary manage and setup-create permissions are required.');
      return;
    }
    const structure = this.selectedSetupStructure();
    if (!this.setupEmployeeId || !structure || !this.effectiveFrom || !this.setupReason.trim()) {
      this.error.set('Employee, active salary structure, effective date, and reason are required.');
      return;
    }
    const components: PayrollSalarySetupComponentInput[] = structure.data.lines.map(line => ({
      salaryComponentId: line.salaryComponentId,
      value: this.isSetupValueEditable(line.salaryComponentId)
        ? this.setupComponentValues[line.salaryComponentId] ?? null
        : null
    }));
    this.error.set('');
    this.busy.set(true);
    this.service.createInitialSalarySetup({
      employeeId: this.setupEmployeeId,
      salaryStructureId: structure.id,
      effectiveFrom: this.effectiveFrom,
      components,
      reason: this.setupReason.trim(),
      idempotencyKey: crypto.randomUUID()
    }).subscribe({
      next: result => {
        this.historyEmployeeId = result.employeeId;
        this.setupReason = '';
        this.loadEmployeeSalary();
      },
      error: error => { this.error.set(this.errorMessage(error)); this.busy.set(false); }
    });
  }

  loadEmployeeSalary(): void {
    if (!this.canView() || !this.historyEmployeeId || this.historyEmployeeId <= 0) {
      this.error.set('Enter a valid employee ID to load salary history.');
      return;
    }
    this.error.set('');
    this.busy.set(true);
    this.salaryHistoryLoaded.set(false);
    forkJoin({
      requests: this.service.employeeSalarySetupRequests(this.historyEmployeeId),
      revisions: this.service.employeeSalaryRevisionRequests(this.historyEmployeeId),
      history: this.service.employeeSalaryHistory(this.historyEmployeeId)
    }).subscribe({
      next: result => {
        this.salaryRequests.set(result.requests);
        this.salaryRevisionRequests.set(result.revisions);
        const today = new Date().toISOString().slice(0, 10);
        this.currentSalary.set(result.history
          .filter(row => row.effectiveFrom <= today)
          .sort((left, right) => right.effectiveFrom.localeCompare(left.effectiveFrom))[0] ?? null);
        this.revisionComponentValues = Object.fromEntries(
          (this.currentSalary()?.components ?? []).map(component =>
            [component.salaryComponentId, component.value])
        );
        this.salaryHistoryLoaded.set(true);
        this.busy.set(false);
      },
      error: error => { this.error.set(this.errorMessage(error)); this.busy.set(false); }
    });
  }

  submitRequest(request: PayrollSalarySetupRequest): void {
    if (!this.canSubmit()) return;
    this.runRequestAction(() => this.service.submitInitialSalarySetup(request), request.employeeId);
  }

  createSalaryRevision(): void {
    const salary = this.currentSalary();
    if (!this.canCreateRevision() || !salary || !this.historyEmployeeId
      || !this.revisionEffectiveFrom || !this.revisionReason.trim()) {
      this.error.set('An approved salary, effective date, and revision reason are required.');
      return;
    }
    this.error.set('');
    this.busy.set(true);
    this.service.createSalaryRevision({
      employeeId: salary.employeeId,
      sourceSalaryVersionId: salary.id,
      effectiveFrom: this.revisionEffectiveFrom,
      components: salary.components.map(component => ({
        salaryComponentId: component.salaryComponentId,
        value: component.valueType === 'Formula' || component.valueType === 'Calculated'
          ? null
          : this.revisionComponentValues[component.salaryComponentId] ?? null
      })),
      reason: this.revisionReason.trim(),
      idempotencyKey: crypto.randomUUID()
    }).subscribe({
      next: result => {
        this.revisionReason = '';
        this.historyEmployeeId = result.employeeId;
        this.loadEmployeeSalary();
      },
      error: error => { this.error.set(this.errorMessage(error)); this.busy.set(false); }
    });
  }

  submitRevision(request: PayrollSalaryRevisionRequest): void {
    if (!this.canSubmitRevision()) return;
    this.runRequestAction(() => this.service.submitSalaryRevision(request), request.employeeId);
  }

  decideRevision(request: PayrollSalaryRevisionRequest, decision: 'approve' | 'reject' | 'cancel'): void {
    if ((decision === 'approve' || decision === 'reject') && !this.canApproveRevision()) return;
    if (decision === 'cancel' && !this.canSubmitRevision()) return;
    const reason = this.decisionReason.trim();
    if (decision !== 'approve' && !reason) {
      this.error.set(`A reason is required to ${decision} this salary revision.`);
      return;
    }
    this.runRequestAction(
      () => this.service.decideSalaryRevision(request, decision, reason),
      request.employeeId
    );
  }

  decideRequest(request: PayrollSalarySetupRequest, decision: 'approve' | 'reject' | 'cancel'): void {
    if ((decision === 'approve' || decision === 'reject') && !this.canApprove()) return;
    if (decision === 'cancel' && !this.canSubmit()) return;
    const reason = decision === 'approve' ? this.decisionReason.trim() : this.decisionReason.trim();
    if (decision !== 'approve' && !reason.trim()) {
      this.error.set(`A reason is required to ${decision} this salary setup.`);
      return;
    }
    this.runRequestAction(
      () => this.service.decideInitialSalarySetup(request, decision, reason),
      request.employeeId
    );
  }

  private runRequestAction(
    action: () => import('rxjs').Observable<unknown>,
    employeeId: number
  ): void {
    this.error.set('');
    this.busy.set(true);
    action().subscribe({
      next: () => {
        this.historyEmployeeId = employeeId;
        this.loadEmployeeSalary();
      },
      error: error => { this.error.set(this.errorMessage(error)); this.busy.set(false); }
    });
  }

  activeComponents(): PayrollSalaryComponentRow[] {
    return this.components().filter(row => row.isActive);
  }

  isFormulaComponent(id: number): boolean {
    return this.activeComponents().some(row => row.id === id && row.data.valueType === 'Formula');
  }

  addLine(): void {
    this.structureData.lines.push({
      salaryComponentId: 0,
      defaultValue: null,
      formulaOverride: null,
      calculationOrder: this.structureData.lines.length + 1
    });
  }

  removeLine(index: number): void {
    if (this.structureData.lines.length > 1) this.structureData.lines.splice(index, 1);
  }

  editComponent(row: PayrollSalaryComponentRow): void {
    if (!this.canManage()) return this.denyManage();
    this.componentEdit = row;
    this.componentData = { ...row.data };
    this.error.set('');
  }

  resetComponent(): void {
    this.componentEdit = null;
    this.componentData = this.emptyComponent();
  }

  saveComponent(): void {
    if (!this.canManage()) return this.denyManage();
    this.error.set('');
    this.busy.set(true);
    this.service.saveComponent({
      ...this.componentData,
      formulaExpression: this.componentData.valueType === 'Formula'
        ? this.componentData.formulaExpression?.trim() || null
        : null,
      taxTreatmentCode: this.componentData.taxTreatmentCode?.trim() || null
    }, this.componentEdit).subscribe({
      next: () => { this.resetComponent(); this.refresh(); },
      error: error => { this.error.set(this.errorMessage(error)); this.busy.set(false); }
    });
  }

  deactivateComponent(row: PayrollSalaryComponentRow): void {
    if (!this.canManage()) return this.denyManage();
    this.error.set('');
    this.busy.set(true);
    this.service.deactivateComponent(row).subscribe({
      next: () => this.refresh(),
      error: error => { this.error.set(this.errorMessage(error)); this.busy.set(false); }
    });
  }

  editStructure(row: PayrollSalaryStructureRow): void {
    if (!this.canManage()) return this.denyManage();
    this.structureEdit = row;
    this.structureData = {
      ...row.data,
      lines: row.data.lines.map(line => ({ ...line }))
    };
    this.error.set('');
  }

  resetStructure(): void {
    this.structureEdit = null;
    this.structureData = this.emptyStructure();
  }

  saveStructure(): void {
    if (!this.canManage()) return this.denyManage();
    this.error.set('');
    this.busy.set(true);
    this.service.saveStructure({
      ...this.structureData,
      lines: this.structureData.lines.map(line => ({
        ...line,
        formulaOverride: line.formulaOverride?.trim() || null
      }))
    }, this.structureEdit).subscribe({
      next: () => { this.resetStructure(); this.refresh(); },
      error: error => { this.error.set(this.errorMessage(error)); this.busy.set(false); }
    });
  }

  deactivateStructure(row: PayrollSalaryStructureRow): void {
    if (!this.canManage()) return this.denyManage();
    this.error.set('');
    this.busy.set(true);
    this.service.deactivateStructure(row).subscribe({
      next: () => this.refresh(),
      error: error => { this.error.set(this.errorMessage(error)); this.busy.set(false); }
    });
  }

  private refresh(): void {
    this.busy.set(true);
    forkJoin({
      components: this.service.components(),
      structures: this.service.structures()
    }).subscribe({
      next: result => {
        this.components.set(result.components);
        this.structures.set(result.structures);
        this.busy.set(false);
      },
      error: error => {
        this.error.set(this.errorMessage(error));
        this.busy.set(false);
      }
    });
  }

  private denyManage(): void {
    this.error.set('Manage access is required for salary setup changes.');
  }

  private errorMessage(error: { error?: { detail?: string; title?: string }; message?: string }): string {
    return error.error?.detail || error.error?.title || error.message || 'Salary setup request failed.';
  }

  private emptyComponent(): PayrollSalaryComponentData {
    return {
      code: '',
      name: '',
      componentType: 'Earning',
      valueType: 'Fixed',
      taxTreatmentCode: null,
      isRecurring: true,
      calculationOrder: 0,
      formulaExpression: null
    };
  }

  private emptyStructure(): PayrollSalaryStructureData {
    const line: PayrollSalaryStructureLineData = {
      salaryComponentId: 0,
      defaultValue: null,
      formulaOverride: null,
      calculationOrder: 1
    };
    return { code: '', name: '', lines: [line] };
  }
}
