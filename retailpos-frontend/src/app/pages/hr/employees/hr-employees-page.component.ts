import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  HrEmployeeCreateData,
  HrEmployeeLifecycleEvent,
  HrEmployeeLifecycleTransitionData,
  HrEmployeeLocationAssignment,
  HrEmployeeLocationAssignmentData,
  HrEmployeeLocationTransfer,
  HrEmployeePositionAssignment,
  HrEmployeePositionAssignmentData,
  HrEmployeeProfile,
  HrEmployeeRehireData,
  HrEmployeeSearchItem,
  HrEmployeeUpdateData,
  HrEmployeeUserLink,
  HrEmploymentType,
  HrPosition,
  HrOperationalLocation,
  HrOrganisationService
} from '../../../services/hr-organisation.service';
import { AuthService } from '../../../services/auth.service';
import {
  UI_KIT,
  UiBadgeVariant,
  UiBreadcrumb,
  UiStatusOption,
  UiTableColumn,
  UiTableQuery
} from '../../../components/shared/ui';
import { EmployeeDocumentsComponent } from './employee-documents.component';

// Presentation only: raw status text is always shown; the mapping only picks the badge colour.
const STATUS_VARIANTS: Record<string, UiBadgeVariant> = {
  Active: 'success',
  Effective: 'success',
  Probation: 'info',
  Approved: 'info',
  ApprovedPendingEffective: 'info',
  PendingApproval: 'warning',
  Suspended: 'warning',
  Rejected: 'danger',
  Cancelled: 'danger',
  Exited: 'neutral',
  Superseded: 'neutral'
};

function statusMapFor(...statuses: string[]): Record<string, UiStatusOption> {
  return Object.fromEntries(statuses.map(status =>
    [status, { label: status, variant: STATUS_VARIANTS[status] ?? 'neutral' }]));
}

@Component({
  selector: 'app-hr-employees-page',
  standalone: true,
  imports: [FormsModule, RouterLink, EmployeeDocumentsComponent, ...UI_KIT],
  template: `
    <main class="container mx-auto px-4 py-3">
      <app-ui-page-header [title]="pageTitle()"
        [description]="pageDescription()"
        [breadcrumbs]="breadcrumbs">
        @if (pageMode() === 'list' && auth.hasPermission('hr.import.view')) {
          <a uiPageSecondaryActions appUiButton variant="outline" routerLink="/hr/opening-imports">Opening Employee Import</a>
        }
        @if (pageMode() === 'list' && auth.hasPermission('hr.employee.create')) {
          <button uiPagePrimaryActions appUiButton type="button" (click)="beginCreate()">
            <i class="fas fa-plus" aria-hidden="true"></i>
            <span>New employee</span>
          </button>
        }
      </app-ui-page-header>
      @if (error()) {
        <app-ui-alert class="hr-block" variant="danger">{{ error() }}</app-ui-alert>
      }

      @if (pageMode() === 'create' || pageMode() === 'edit') {
        @if (pageMode() === 'edit' && profileLoading()) {
          <app-ui-state kind="loading" title="Loading employee for editing..." />
        } @else if (pageMode() === 'edit' && (profileLoadError() || profileNotFound())) {
          <app-ui-state [kind]="profileNotFound() ? 'empty' : 'error'"
            [title]="profileNotFound() ? 'Employee not found' : 'Unable to open employee for editing'"
            [message]="profileLoadError() || notFoundMessage">
            @if (!profileNotFound()) {
              <button appUiButton variant="outline" type="button" (click)="retryProfileLoad()">Retry</button>
            }
            <button appUiButton variant="outline" type="button" (click)="backToEmployees()">Back to Employees</button>
          </app-ui-state>
        } @else {
        <app-ui-card class="hr-block" [title]="selected() ? 'Edit employee' : 'Create employee'">
          <form class="hr-grid" (ngSubmit)="save()" [attr.aria-busy]="saving()">
            @if (saving()) {
              <app-ui-state class="hr-span-2" kind="loading" title="Saving employee..." [compact]="true" />
            }
            @if (formStepError()) {
              <app-ui-alert class="hr-span-2" variant="danger" role="alert">{{ formStepError() }}</app-ui-alert>
            }
            <app-ui-stepper
              class="hr-span-2"
              orientation="vertical"
              ariaLabel="Employee form steps"
              [(activeIndex)]="employeeStep"
              [canAdvance]="canAdvanceEmployeeStep"
              [allowCompletedNavigation]="true">
              <ng-template appUiStep="Employee details" description="Name and work contact"
                [error]="employeeStepHasError(0)">
                <div class="hr-grid">
                  @if (!selected()) {
                    <app-ui-form-field label="Employee code" appearance="filled" [required]="true">
                      <input appUiControl name="employeeCode" required maxlength="50" [ngModelOptions]="standaloneModel" [(ngModel)]="form.employeeCode">
                    </app-ui-form-field>
                  }
                  <app-ui-form-field label="First name" appearance="filled" [required]="true">
                    <input appUiControl name="firstName" required maxlength="100" [ngModelOptions]="standaloneModel" [(ngModel)]="form.firstName">
                  </app-ui-form-field>
                  <app-ui-form-field label="Middle name" appearance="filled">
                    <input appUiControl name="middleName" maxlength="100" [ngModelOptions]="standaloneModel" [(ngModel)]="form.middleName">
                  </app-ui-form-field>
                  <app-ui-form-field label="Last name" appearance="filled">
                    <input appUiControl name="lastName" maxlength="100" [ngModelOptions]="standaloneModel" [(ngModel)]="form.lastName">
                  </app-ui-form-field>
                  <app-ui-form-field label="Display name" appearance="filled" [required]="true">
                    <input appUiControl name="displayName" required maxlength="200" [ngModelOptions]="standaloneModel" [(ngModel)]="form.displayName">
                  </app-ui-form-field>
                  <app-ui-form-field label="Work email" appearance="filled">
                    <input appUiControl name="workEmail" maxlength="200" [ngModelOptions]="standaloneModel" [(ngModel)]="form.workEmail">
                  </app-ui-form-field>
                </div>
              </ng-template>

              @if (auth.hasPermission('hr.employee.view_sensitive') || auth.hasPermission('hr.employee.view_identity')) {
                <ng-template appUiStep="Personal information" description="Authorized personal and identity fields"
                  [error]="employeeStepHasError(personalInformationStepIndex())">
                  @if (auth.hasPermission('hr.employee.view_sensitive')) {
                    <fieldset class="hr-fieldset hr-grid hr-span-2">
                      <legend>Restricted personal information</legend>
                      <app-ui-date-picker label="Date of birth" name="dateOfBirth"
                        [ngModelOptions]="standaloneModel" [(ngModel)]="form.dateOfBirth" />
                      <app-ui-form-field label="Gender code" appearance="filled">
                        <input appUiControl name="genderCode" maxlength="30" [ngModelOptions]="standaloneModel" [(ngModel)]="form.genderCode">
                      </app-ui-form-field>
                      <app-ui-form-field label="Personal email" appearance="filled">
                        <input appUiControl name="personalEmail" maxlength="200" [ngModelOptions]="standaloneModel" [(ngModel)]="form.personalEmail">
                      </app-ui-form-field>
                      <app-ui-form-field label="Mobile" appearance="filled">
                        <input appUiControl name="mobile" maxlength="50" [ngModelOptions]="standaloneModel" [(ngModel)]="form.mobile">
                      </app-ui-form-field>
                      <app-ui-form-field label="Present address" appearance="filled">
                        <textarea appUiControl name="presentAddress" maxlength="4000" [ngModelOptions]="standaloneModel" [(ngModel)]="form.presentAddress"></textarea>
                      </app-ui-form-field>
                      <app-ui-form-field label="Permanent address" appearance="filled">
                        <textarea appUiControl name="permanentAddress" maxlength="4000" [ngModelOptions]="standaloneModel" [(ngModel)]="form.permanentAddress"></textarea>
                      </app-ui-form-field>
                      <app-ui-form-field label="Emergency contact name" appearance="filled">
                        <input appUiControl name="emergencyContactName" maxlength="150" [ngModelOptions]="standaloneModel" [(ngModel)]="form.emergencyContactName">
                      </app-ui-form-field>
                      <app-ui-form-field label="Emergency contact phone" appearance="filled">
                        <input appUiControl name="emergencyContactPhone" maxlength="50" [ngModelOptions]="standaloneModel" [(ngModel)]="form.emergencyContactPhone">
                      </app-ui-form-field>
                    </fieldset>
                  }
                  @if (auth.hasPermission('hr.employee.view_identity')) {
                    <fieldset class="hr-fieldset hr-grid hr-span-2">
                      <legend>Restricted identity information</legend>
                      <app-ui-form-field label="National ID" appearance="filled">
                        <input appUiControl name="nationalId" maxlength="100" [ngModelOptions]="standaloneModel" [(ngModel)]="form.nationalId">
                      </app-ui-form-field>
                      <app-ui-form-field label="Passport number" appearance="filled">
                        <input appUiControl name="passportNo" maxlength="100" [ngModelOptions]="standaloneModel" [(ngModel)]="form.passportNo">
                      </app-ui-form-field>
                    </fieldset>
                  }
                </ng-template>
              }
              @if (!selected()) {
                <ng-template appUiStep="Employment details" description="Type, status and dates"
                  [error]="employeeStepHasError(employmentStepIndex())">
                  <div class="hr-grid">
                    <app-ui-form-field label="Employment type" appearance="filled" [required]="true">
                      <select appUiControl name="employmentTypeId" required [ngModelOptions]="standaloneModel" [(ngModel)]="form.employmentTypeId">
                        <option value="">Select type</option>
                        @for (type of employmentTypes(); track type.id) {
                          <option [value]="type.id">{{ type.name }}</option>
                        }
                      </select>
                    </app-ui-form-field>
                    <app-ui-form-field label="Initial employment status" appearance="filled">
                      <select appUiControl name="employmentStatus" [ngModelOptions]="standaloneModel" [(ngModel)]="form.employmentStatus">
                        <option value="Active">Active</option><option value="Probation">Probation</option>
                      </select>
                    </app-ui-form-field>
                    <app-ui-date-picker
                      label="Joining date"
                      name="joiningDate"
                      [ngModelOptions]="standaloneModel"
                      [required]="true"
                      [(ngModel)]="form.joiningDate" />
                    <app-ui-date-picker
                      label="Confirmation date"
                      name="confirmationDate"
                      [ngModelOptions]="standaloneModel"
                      [(ngModel)]="form.confirmationDate" />
                  </div>
                </ng-template>
              }
            </app-ui-stepper>
            <div class="hr-actions hr-span-2">
              @if (isLastEmployeeStep()) {
                <button appUiButton type="submit" [disabled]="saving()">{{ selected() ? 'Save changes' : 'Create employee' }}</button>
              }
              <button appUiButton variant="outline" type="button" [disabled]="saving()" (click)="cancelForm()">
                Cancel
              </button>
            </div>
          </form>
        </app-ui-card>
        }
      }

      @if (pageMode() === 'view') {
        @if (profileLoading()) {
          <app-ui-state kind="loading" title="Loading employee profile..." />
        } @else if (profileNotFound()) {
          <app-ui-state kind="empty" title="Employee not found" [message]="notFoundMessage">
            <button appUiButton variant="outline" type="button" (click)="backToEmployees()">Back to Employees</button>
          </app-ui-state>
        } @else if (profileLoadError()) {
          <app-ui-state kind="error" title="Unable to load employee profile" [message]="profileLoadError() ?? ''">
            <button appUiButton variant="outline" type="button" (click)="retryProfileLoad()">Retry</button>
            <button appUiButton variant="outline" type="button" (click)="backToEmployees()">Back to Employees</button>
          </app-ui-state>
        } @else if (selected(); as profile) {
        <app-ui-stepper class="hr-profile-stepper hr-block" orientation="vertical"
          ariaLabel="Employee profile sections" [(activeIndex)]="profileStep"
          [allowCompletedNavigation]="true" [allowFutureNavigation]="true">
        <ng-template appUiStep="Employee overview" description="Identity and current employment">
        <app-ui-card [title]="profile.displayName">
          <button uiCardActions appUiButton variant="outline" size="sm" type="button" (click)="backToEmployees()">Back to Employees</button>
          @if (auth.hasPermission('hr.employee.edit')) {
            <button uiCardActions appUiButton size="sm" type="button" (click)="beginEdit()">Edit Employee</button>
          }
          <div class="hr-summary">
            <span class="hr-code">{{ profile.employeeCode }}</span>
            <app-ui-badge [variant]="statusVariant(profile.currentEmploymentStatus)">{{ profile.currentEmploymentStatus }}</app-ui-badge>
            <span>{{ profile.currentEmploymentTypeName }}</span>
          </div>
          <dl class="hr-dl">
            <div><dt>Joining date</dt><dd>{{ (profile.joiningDate | appDate) }}</dd></div>
            <div><dt>Confirmation date</dt><dd>{{ (profile.confirmationDate | appDate) || '—' }}</dd></div>
            <div><dt>Work email</dt><dd>{{ profile.workEmail || '—' }}</dd></div>
            <div><dt>Position</dt><dd>{{ currentPosition()?.positionName || '—' }}</dd></div>
            <div><dt>Department / Team</dt><dd>{{ currentPosition()?.orgUnitName || '—' }}</dd></div>
            <div><dt>Reports to</dt><dd>{{ currentPosition()?.reportsToEmployeeName || '—' }}</dd></div>
            <div><dt>Primary work location</dt><dd>{{ currentPrimaryLocation()?.locationName || 'Not assigned' }}</dd></div>
          </dl>
        </app-ui-card>
        </ng-template>
        @if (hasPersonalInformationStep()) {
        <ng-template appUiStep="Personal information" description="Authorized personal and identity details">
        @if (auth.hasPermission('hr.employee.view_sensitive')) {
          <app-ui-card title="Restricted personal information" [headingLevel]="3">
            <dl class="hr-dl">
              <div><dt>Date of birth</dt><dd>{{ (selected()?.sensitiveFields?.dateOfBirth | appDate) || '—' }}</dd></div>
              <div><dt>Gender</dt><dd>{{ selected()?.sensitiveFields?.genderCode || '—' }}</dd></div>
              <div><dt>Personal email</dt><dd>{{ selected()?.sensitiveFields?.personalEmail || '—' }}</dd></div>
              <div><dt>Mobile</dt><dd>{{ selected()?.sensitiveFields?.mobile || '—' }}</dd></div>
              <div><dt>Present address</dt><dd>{{ selected()?.sensitiveFields?.presentAddress || '—' }}</dd></div>
              <div><dt>Permanent address</dt><dd>{{ selected()?.sensitiveFields?.permanentAddress || '—' }}</dd></div>
              <div><dt>Emergency contact</dt><dd>{{ selected()?.sensitiveFields?.emergencyContactName || '—' }}
                {{ selected()?.sensitiveFields?.emergencyContactPhone || '' }}</dd></div>
            </dl>
          </app-ui-card>
        }
        @if (auth.hasPermission('hr.employee.view_identity')) {
          <app-ui-card title="Restricted identity information" [headingLevel]="3">
            <dl class="hr-dl">
              <div><dt>National ID</dt><dd>{{ selected()?.sensitiveFields?.nationalId || '—' }}</dd></div>
              <div><dt>Passport</dt><dd>{{ selected()?.sensitiveFields?.passportNo || '—' }}</dd></div>
            </dl>
          </app-ui-card>
        }
        </ng-template>
        }
        @if (auth.hasPermission('hr.employee.view')) {
        <ng-template appUiStep="Login User link" description="Employee account association and actions">
          <app-ui-card title="Login User link" [headingLevel]="3">
            <div class="hr-section">
              @if (userLink(); as link) {
                <p>
                  Linked Login User ID: {{ link.identityUserId }} ·
                  {{ link.linkedAtUtc | appDate:'medium' }} · {{ link.linkReason }}
                </p>
              }
              @if (!userLink()) {
                <p>No active Login User link.</p>
              }
              @if (!userLink() && auth.hasPermission('hr.employee.link_user')) {
                <form class="hr-grid" (ngSubmit)="linkUser()">
                  <app-ui-form-field label="Existing Login User ID" [required]="true">
                    <input appUiControl type="number" min="1" name="identityUserId" required [(ngModel)]="identityUserId">
                  </app-ui-form-field>
                  <app-ui-form-field label="Reason" [required]="true">
                    <input appUiControl name="userLinkReason" maxlength="500" required [(ngModel)]="userLinkReason">
                  </app-ui-form-field>
                  <div class="hr-actions hr-span-2">
                    <button appUiButton type="submit">Link existing Login User</button>
                  </div>
                </form>
              }
              @if (userLink() && auth.hasPermission('hr.employee.unlink_user')) {
                <form class="hr-grid" (ngSubmit)="unlinkUser()">
                  <app-ui-form-field label="Unlink reason" [required]="true">
                    <input appUiControl name="userUnlinkReason" maxlength="500" required [(ngModel)]="userUnlinkReason">
                  </app-ui-form-field>
                  <div class="hr-actions hr-span-2">
                    <button appUiButton variant="outline" type="submit">Unlink Login User</button>
                  </div>
                </form>
              }
            </div>
          </app-ui-card>
        </ng-template>
        }
        @if (auth.hasPermission('hr.employee_lifecycle.view')
          || auth.hasPermission('hr.employee_lifecycle.manage')
          || auth.hasPermission('hr.employee_lifecycle.rehire')) {
        <ng-template appUiStep="Employment and lifecycle" description="Employment status and lifecycle history">
        @if (auth.hasPermission('hr.employee_lifecycle.view')) {
          <app-ui-card title="Lifecycle history" [headingLevel]="3">
            <button uiCardActions appUiButton variant="outline" size="sm" type="button" (click)="loadLifecycleHistory()">Refresh history</button>
            <div class="hr-section">
              <app-ui-data-table
                ariaLabel="Employee lifecycle history" density="compact" rowKey="id"
                [columns]="lifecycleColumns" [rows]="lifecycleHistory()"
                [searchable]="false" [pageSizes]="historyPageSizes"
                emptyTitle="No lifecycle events recorded.">
                <ng-template uiTableCell="occurredAtUtc" let-event>{{ event.occurredAtUtc | appDate:'medium' }}</ng-template>
              </app-ui-data-table>
              @if (auth.hasPermission('hr.employee_lifecycle.manage')
                && selected()?.currentEmploymentStatus !== 'Exited') {
                <app-ui-card title="Lifecycle action" [headingLevel]="4">
                  <form class="hr-grid" (ngSubmit)="transitionLifecycle()">
                    <app-ui-form-field label="Action">
                      <select appUiControl name="lifecycleEventType" [(ngModel)]="lifecycleEventType">
                        <option value="Probation">Move to probation</option>
                        <option value="Confirmed">Confirm</option>
                        <option value="EmploymentTypeChanged">Change employment type</option>
                        <option value="Suspended">Suspend</option>
                        <option value="Reinstated">Reinstate</option>
                        <option value="Exited">Exit employment</option>
                      </select>
                    </app-ui-form-field>
                    <app-ui-date-picker label="Effective date" name="lifecycleEffectiveDate"
                      [required]="true" [(ngModel)]="lifecycleEffectiveDate" />
                    @if (lifecycleEventType === 'EmploymentTypeChanged') {
                      <app-ui-form-field label="Employment type">
                        <select appUiControl name="lifecycleEmploymentType" [(ngModel)]="lifecycleEmploymentTypeId">
                          @for (type of employmentTypes(); track type.id) {
                            <option [value]="type.id">{{ type.name }}</option>
                          }
                        </select>
                      </app-ui-form-field>
                    }
                    <app-ui-form-field label="Reason">
                      <input appUiControl name="lifecycleReason" maxlength="1000" [(ngModel)]="lifecycleReason">
                    </app-ui-form-field>
                    <div class="hr-actions hr-span-2">
                      <button appUiButton type="submit">Save lifecycle action</button>
                    </div>
                  </form>
                </app-ui-card>
              }
              @if (auth.hasPermission('hr.employee_lifecycle.rehire')
                && selected()?.currentEmploymentStatus === 'Exited') {
                <app-ui-card title="Rehire" [headingLevel]="4">
                  <form class="hr-grid" (ngSubmit)="rehire()">
                    <app-ui-form-field label="Employment type">
                      <select appUiControl name="rehireType" [(ngModel)]="rehireEmploymentTypeId">
                        @for (type of employmentTypes(); track type.id) {
                          <option [value]="type.id">{{ type.name }}</option>
                        }
                      </select>
                    </app-ui-form-field>
                    <app-ui-form-field label="Employment status">
                      <select appUiControl name="rehireStatus" [(ngModel)]="rehireStatus">
                        <option value="Active">Active</option><option value="Probation">Probation</option>
                      </select>
                    </app-ui-form-field>
                    <app-ui-date-picker label="Effective date" name="rehireDate"
                      [required]="true" [(ngModel)]="lifecycleEffectiveDate" />
                    <app-ui-form-field label="Reason">
                      <input appUiControl name="rehireReason" maxlength="1000" [(ngModel)]="lifecycleReason">
                    </app-ui-form-field>
                    <div class="hr-actions hr-span-2">
                      <button appUiButton type="submit">Rehire employee</button>
                    </div>
                  </form>
                </app-ui-card>
              }
            </div>
          </app-ui-card>
        }
        </ng-template>
        }
        @if (auth.hasPermission('hr.employee_transfer.view')) {
        <ng-template appUiStep="Work locations" description="Assignments and transfer requests">
          <app-ui-card title="Operational location assignments" [headingLevel]="3">
            <button uiCardActions appUiButton variant="outline" size="sm" type="button" (click)="loadLocationHistory()">Refresh assignments</button>
            <div class="hr-section">
              <p>Current Primary location: {{ currentPrimaryLocation()?.locationName || 'Not assigned' }}</p>
              <app-ui-data-table
                ariaLabel="Operational location assignment history" density="compact" rowKey="id"
                [columns]="locationColumns" [rows]="locationHistory()"
                [searchable]="false" [pageSizes]="historyPageSizes"
                emptyTitle="No visible location assignment history.">
                <ng-template uiTableCell="location" let-assignment>{{ assignment.locationName }} · {{ assignment.sourceEntityType }}</ng-template>
                <ng-template uiTableCell="effective" let-assignment>{{ (assignment.effectiveFrom | appDate) }} – {{ (assignment.effectiveTo | appDate) || 'Open' }}</ng-template>
              </app-ui-data-table>
              @for (transfer of transfers(); track transfer.id) {
                <app-ui-card>
                  <div class="hr-section">
                    <p class="hr-transfer-title">
                      <span>Primary transfer to {{ transfer.locationName }}</span>
                      <app-ui-badge [variant]="statusVariant(transfer.status)">{{ transfer.status }}</app-ui-badge>
                    </p>
                    <p>Effective {{ (transfer.effectiveFrom | appDate) }} · Approval #{{ transfer.approvalInstanceId || '—' }}</p>
                    @if (transfer.status === 'PendingApproval'
                      || transfer.status === 'ApprovedPendingEffective') {
                      <app-ui-form-field label="Action reason">
                        <input appUiControl name="transferActionReason" maxlength="500" [(ngModel)]="transferActionReason">
                      </app-ui-form-field>
                    }
                    <div class="hr-actions">
                      @if (transfer.status === 'PendingApproval'
                        && auth.hasPermission('hr.employee_transfer.approve')) {
                        <button appUiButton variant="outline" size="sm" type="button" (click)="decideTransfer(transfer, true)">Approve</button>
                      }
                      @if (transfer.status === 'PendingApproval'
                        && auth.hasPermission('hr.employee_transfer.approve')) {
                        <button appUiButton variant="outline" size="sm" type="button" (click)="decideTransfer(transfer, false)">Reject</button>
                      }
                      @if (transfer.status === 'PendingApproval'
                        && auth.hasPermission('hr.employee_transfer.create')) {
                        <button appUiButton variant="outline" size="sm" type="button" (click)="cancelTransfer(transfer)">Cancel request</button>
                      }
                      @if (transfer.status === 'ApprovedPendingEffective'
                        && auth.hasPermission('hr.employee_transfer.create')) {
                        <button appUiButton variant="outline" size="sm" type="button" (click)="cancelTransfer(transfer)">Cancel scheduled transfer</button>
                      }
                    </div>
                  </div>
                </app-ui-card>
              }
              @if (auth.hasPermission('hr.employee_transfer.create')
                && selected()?.currentEmploymentStatus !== 'Exited'
                && selected()?.currentEmploymentStatus !== 'Suspended') {
                <app-ui-card title="Assign location" [headingLevel]="4">
                  <form class="hr-grid" (ngSubmit)="saveLocationAssignment()">
                    <app-ui-form-field label="Operational location" [required]="true">
                      <select appUiControl name="locationId" required [(ngModel)]="locationProjectionId">
                        <option value="">Select location</option>
                        @for (location of operationalLocations(); track location.id) {
                          <option [value]="location.id"
                            [disabled]="!location.isSourceActive || location.isSourceDeleted || location.syncStatus !== 'Current'">
                            {{ location.name }} · {{ location.sourceEntityType }}
                          </option>
                        }
                      </select>
                    </app-ui-form-field>
                    <app-ui-form-field label="Assignment type">
                      <select appUiControl name="locationAssignmentType" [(ngModel)]="locationAssignmentType">
                        <option value="Primary" [disabled]="!!currentPrimaryLocation()">Primary (initial only)</option>
                        <option value="Additional">Additional</option><option value="Temporary">Temporary</option>
                      </select>
                    </app-ui-form-field>
                    <app-ui-date-picker label="Effective from" name="locationEffectiveFrom"
                      [required]="true" [(ngModel)]="locationEffectiveFrom" />
                    @if (locationAssignmentType === 'Temporary') {
                      <app-ui-date-picker label="Effective to" name="locationEffectiveTo"
                        [required]="true" [min]="locationEffectiveFrom" [(ngModel)]="locationEffectiveTo" />
                    }
                    <app-ui-form-field class="hr-span-2" label="Reason" [required]="true">
                      <input appUiControl name="locationReason" required maxlength="500" [(ngModel)]="locationReason">
                    </app-ui-form-field>
                    <div class="hr-actions hr-span-2">
                      <button appUiButton type="submit">Save location assignment</button>
                    </div>
                  </form>
                </app-ui-card>
              }
              @if (currentPrimaryLocation() && auth.hasPermission('hr.employee_transfer.create')
                && selected()?.currentEmploymentStatus !== 'Exited'
                && selected()?.currentEmploymentStatus !== 'Suspended') {
                <app-ui-card title="Request Primary location transfer" [headingLevel]="4">
                  <form class="hr-grid" (ngSubmit)="requestLocationTransfer()">
                    <app-ui-form-field label="New Primary location" [required]="true">
                      <select appUiControl name="transferLocationId" required [(ngModel)]="transferLocationProjectionId">
                        <option value="">Select location</option>
                        @for (location of operationalLocations(); track location.id) {
                          <option [value]="location.id"
                            [disabled]="!location.isSourceActive || location.isSourceDeleted || location.syncStatus !== 'Current'
                              || location.id === currentPrimaryLocation()?.operationalLocationProjectionId">
                            {{ location.name }} · {{ location.sourceEntityType }}
                          </option>
                        }
                      </select>
                    </app-ui-form-field>
                    <app-ui-date-picker label="Effective from" name="transferEffectiveFrom"
                      [required]="true" [(ngModel)]="transferEffectiveFrom" />
                    <app-ui-form-field class="hr-span-2" label="Reason" [required]="true">
                      <input appUiControl name="transferReason" required maxlength="500" [(ngModel)]="transferReason">
                    </app-ui-form-field>
                    <div class="hr-actions hr-span-2">
                      <button appUiButton type="submit">Request transfer approval</button>
                    </div>
                  </form>
                </app-ui-card>
              }
            </div>
          </app-ui-card>
        </ng-template>
        }
        @if (auth.hasPermission('hr.position.view')) {
        <ng-template appUiStep="Position and manager" description="Current and historical assignments">
          <app-ui-card title="Position and manager history" [headingLevel]="3">
            <button uiCardActions appUiButton variant="outline" size="sm" type="button" (click)="loadPositionHistory()">Refresh history</button>
            <div class="hr-section">
              <app-ui-data-table
                ariaLabel="Position and manager history" density="compact" rowKey="id"
                [columns]="positionColumns" [rows]="positionHistory()"
                [searchable]="false" [pageSizes]="historyPageSizes"
                emptyTitle="No position or manager history.">
                <ng-template uiTableCell="effective" let-assignment>{{ (assignment.effectiveFrom | appDate) }} – {{ (assignment.effectiveTo | appDate) || 'Current' }}</ng-template>
              </app-ui-data-table>
              @if (auth.hasPermission('hr.position.view') && auth.hasPermission('hr.position.manage')
                && selected()?.currentEmploymentStatus !== 'Exited'
                && selected()?.currentEmploymentStatus !== 'Suspended') {
                <app-ui-card title="Assign position / manager" [headingLevel]="4">
                  <form class="hr-grid" (ngSubmit)="assignPosition()">
                    <app-ui-form-field label="Position">
                      <select appUiControl name="positionId" [(ngModel)]="assignmentPositionId"
                        (ngModelChange)="setAssignmentOrgUnit($event)">
                        <option value="">Select position</option>
                        @for (position of positions(); track position.id) {
                          <option [value]="position.id" [disabled]="position.status === 'Inactive'">
                            {{ position.code }} · {{ position.name }}
                          </option>
                        }
                      </select>
                    </app-ui-form-field>
                    <app-ui-form-field label="Assignment type">
                      <select appUiControl name="assignmentType" [(ngModel)]="assignmentType">
                        <option value="Primary">Primary</option>
                        <option value="Temporary">Temporary</option>
                        <option value="Acting">Acting</option>
                      </select>
                    </app-ui-form-field>
                    <app-ui-form-field label="Reports to employee ID">
                      <input appUiControl type="number" name="reportsToEmployeeId" [(ngModel)]="reportsToEmployeeId">
                    </app-ui-form-field>
                    <app-ui-date-picker label="Effective from" name="assignmentEffectiveFrom"
                      [required]="true" [(ngModel)]="lifecycleEffectiveDate" />
                    @if (assignmentType !== 'Primary') {
                      <app-ui-date-picker label="Effective to" name="assignmentEffectiveTo"
                        [min]="lifecycleEffectiveDate" [(ngModel)]="assignmentEffectiveTo" />
                    }
                    <app-ui-form-field label="Reason">
                      <input appUiControl name="assignmentReason" maxlength="500" [(ngModel)]="lifecycleReason">
                    </app-ui-form-field>
                    <div class="hr-actions hr-span-2">
                      <button appUiButton type="submit">Save position assignment</button>
                    </div>
                  </form>
                </app-ui-card>
              }
            </div>
          </app-ui-card>
        </ng-template>
        }
        @if (auth.hasPermission('hr.employee_document.view')) {
          <ng-template appUiStep="Employee documents" description="Authorized documents, versions and upload actions">
            <app-employee-documents [employeeId]="profile.id"></app-employee-documents>
          </ng-template>
        }
        </app-ui-stepper>
        }
      }

      @if (pageMode() === 'list') {
        <app-ui-data-table
          mode="server"
          ariaLabel="Employees"
          density="compact"
          rowKey="id"
          actionsHeader="Action"
          loadingLabel="Loading employees..."
          emptyTitle="No employees found."
          searchLabel="Search employees"
          [columns]="employeeColumns"
          [rows]="employees()"
          [totalRecords]="totalCount"
          [loading]="loading()"
          [searchable]="false"
          [pageSizes]="[pageSize]"
          [initialPageSize]="pageSize"
          [initialPage]="page"
          [emitInitialQuery]="false"
          (queryChange)="onTableQuery($event)">
          <div uiTableToolbar class="hr-toolbar" role="search">
            <app-ui-form-field label="Search employees">
              <input appUiControl type="search" name="search" [(ngModel)]="search" (keyup.enter)="searchEmployees()">
            </app-ui-form-field>
              <button appUiButton variant="outline" type="button" (click)="searchEmployees()">Search</button>
          </div>
          <ng-template uiTableRowActions let-employee>
            <div class="hr-row-actions">
              @if (auth.hasPermission('hr.employee.view')) {
                <button appUiActionButton action="view" type="button"
                  [attr.aria-label]="'View profile for ' + employee.displayName"
                  (click)="openProfile(employee)">
                  <i class="fas fa-eye" aria-hidden="true"></i>
                  <span class="sr-only">View profile</span>
                </button>
              }
              @if (auth.hasPermission('hr.employee.view') && auth.hasPermission('hr.employee.edit')) {
                <button appUiActionButton action="edit" type="button"
                  [attr.aria-label]="'Edit employee ' + employee.displayName"
                  (click)="beginEditFromList(employee)">
                  <i class="fas fa-pen" aria-hidden="true"></i>
                  <span class="sr-only">Edit employee</span>
                </button>
              }
            </div>
          </ng-template>
        </app-ui-data-table>
      }
    </main>
  `,
  styles: [`
    :host { display: block; }
    .hr-block { margin-bottom: var(--rp-space-4); }
    .hr-stack { display: flex; flex-direction: column; gap: var(--rp-space-4); }
    .hr-section { display: flex; flex-direction: column; gap: var(--rp-space-4); }
    .hr-grid { display: grid; grid-template-columns: minmax(0, 1fr); gap: var(--rp-form-gap); }
    .hr-actions { display: flex; flex-wrap: wrap; gap: var(--rp-space-2); }
    .hr-fieldset {
      min-width: 0;
      margin: 0;
      padding: var(--rp-space-4);
      border: 1px solid var(--rp-color-border);
      border-radius: var(--rp-radius-card);
    }
    .hr-fieldset legend {
      padding: 0 var(--rp-space-2);
      color: var(--rp-color-text-primary);
      font-size: var(--rp-text-label-size);
      font-weight: var(--rp-font-weight-semibold);
    }
    .hr-summary {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--rp-space-2) var(--rp-space-3);
      margin-bottom: var(--rp-space-4);
    }
    .hr-code { font-family: var(--rp-font-family-mono); color: var(--rp-color-text-secondary); }
    .hr-dl { display: grid; grid-template-columns: minmax(0, 1fr); gap: var(--rp-space-3); margin: 0; }
    .hr-dl dt {
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-label-size);
      font-weight: var(--rp-font-weight-medium);
    }
    .hr-dl dd { margin: 0; overflow-wrap: anywhere; }
    .hr-transfer-title {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--rp-space-2);
      font-weight: var(--rp-font-weight-medium);
    }
    .hr-toolbar { display: flex; flex-wrap: wrap; align-items: flex-end; gap: var(--rp-space-3); }
    .hr-toolbar app-ui-form-field { min-width: 14rem; flex: 1 1 14rem; }
    .hr-row-actions { display: inline-flex; align-items: center; gap: var(--rp-space-2); }
    .hr-profile-stepper { min-width: 0; }
    @media (min-width: 768px) {
      .hr-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
      .hr-span-2 { grid-column: 1 / -1; }
      .hr-dl { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }
    @media (min-width: 1280px) {
      .hr-dl { grid-template-columns: repeat(3, minmax(0, 1fr)); }
    }
  `]
})
export class HrEmployeesPageComponent implements OnInit {
  private readonly service = inject(HrOrganisationService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private profileLoadRequestId = 0;
  private activeEmployeeId: number | null = null;
  readonly auth = inject(AuthService);
  readonly pageMode = signal<'list' | 'create' | 'view' | 'edit'>('list');
  readonly employees = signal<HrEmployeeSearchItem[]>([]);
  readonly employmentTypes = signal<HrEmploymentType[]>([]);
  readonly selected = signal<HrEmployeeProfile | null>(null);
  readonly profileStep = signal(0);
  readonly lifecycleHistory = signal<HrEmployeeLifecycleEvent[]>([]);
  readonly positionHistory = signal<HrEmployeePositionAssignment[]>([]);
  readonly locationHistory = signal<HrEmployeeLocationAssignment[]>([]);
  readonly operationalLocations = signal<HrOperationalLocation[]>([]);
  readonly transfers = signal<HrEmployeeLocationTransfer[]>([]);
  readonly userLink = signal<HrEmployeeUserLink | null>(null);
  readonly positions = signal<HrPosition[]>([]);
  readonly editingForm = signal(false);
  readonly profileLoading = signal(false);
  readonly profileLoadError = signal<string | null>(null);
  readonly profileNotFound = signal(false);
  readonly saving = signal(false);
  readonly notFoundMessage = 'This employee is not available in the current Business or may have been removed.';
  readonly employeeStep = signal(0);
  readonly formStepError = signal<string | null>(null);
  readonly standaloneModel = { standalone: true };
  readonly canAdvanceEmployeeStep = (index: number): boolean => this.validateEmployeeStep(index);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly pageSize = 25;
  get breadcrumbs(): UiBreadcrumb[] {
    const base: UiBreadcrumb[] = [{ label: 'HR' }, { label: 'Employees', link: '/hr/employees' }];
    if (this.pageMode() === 'create') return [...base, { label: 'New employee' }];
    if (this.pageMode() === 'view') return [...base, { label: 'Employee profile' }];
    if (this.pageMode() === 'edit') return [...base, { label: 'Edit employee' }];
    return base;
  }
  readonly historyPageSizes = [10, 25, 50];
  // Server search has no sort or page-size parameters, so none of these columns are sortable.
  readonly employeeColumns: UiTableColumn[] = [
    { key: 'employeeCode', header: 'Employee code' },
    { key: 'displayName', header: 'Name' },
    { key: 'workEmail', header: 'Work email' },
    { key: 'currentEmploymentTypeName', header: 'Employment type' },
    {
      key: 'currentEmploymentStatus', header: 'Status', type: 'status',
      statusMap: statusMapFor('Active', 'Probation', 'Suspended', 'Exited')
    },
    { key: 'joiningDate', type: 'date', header: 'Joining date' }
  ];
  readonly lifecycleColumns: UiTableColumn[] = [
    { key: 'effectiveDate', type: 'date', header: 'Effective date' },
    { key: 'eventType', header: 'Event' },
    { key: 'reason', header: 'Reason' },
    { key: 'actorUserId', header: 'Actor user ID' },
    { key: 'occurredAtUtc', header: 'Occurred', type: 'custom' }
  ];
  readonly locationColumns: UiTableColumn[] = [
    { key: 'location', header: 'Location', type: 'custom' },
    { key: 'assignmentType', header: 'Type' },
    { key: 'effective', header: 'Effective', type: 'custom' },
    {
      key: 'status', header: 'Status', type: 'status',
      statusMap: statusMapFor('Effective', 'PendingApproval', 'Approved', 'ApprovedPendingEffective',
        'Rejected', 'Cancelled', 'Superseded')
    },
    { key: 'reason', header: 'Reason' }
  ];
  readonly positionColumns: UiTableColumn[] = [
    { key: 'positionName', header: 'Position' },
    { key: 'orgUnitName', header: 'Department / Team' },
    { key: 'reportsToEmployeeName', header: 'Manager' },
    { key: 'assignmentType', header: 'Type' },
    { key: 'effective', header: 'Effective', type: 'custom' }
  ];
  page = 1;
  totalCount = 0;
  search = '';
  private lastListQueryKey: string | null = null;
  lifecycleEventType: HrEmployeeLifecycleTransitionData['eventType'] = 'Confirmed';
  lifecycleEffectiveDate = new Date().toISOString().slice(0, 10);
  lifecycleReason = '';
  lifecycleEmploymentTypeId = '';
  rehireEmploymentTypeId = '';
  rehireStatus: HrEmployeeRehireData['employmentStatus'] = 'Active';
  assignmentPositionId = '';
  assignmentOrgUnitId = 0;
  assignmentType: HrEmployeePositionAssignmentData['assignmentType'] = 'Primary';
  assignmentEffectiveTo = '';
  locationProjectionId = '';
  locationAssignmentType: HrEmployeeLocationAssignmentData['assignmentType'] = 'Primary';
  locationEffectiveFrom = new Date().toISOString().slice(0, 10);
  locationEffectiveTo = '';
  locationReason = '';
  transferLocationProjectionId = '';
  transferEffectiveFrom = new Date().toISOString().slice(0, 10);
  transferReason = '';
  transferActionReason = '';
  private readonly transferDecisionKeys = new Map<string, string>();
  identityUserId = '';
  userLinkReason = '';
  userUnlinkReason = '';
  reportsToEmployeeId = '';
  form = this.emptyForm();

  ngOnInit(): void {
    this.service.getEmploymentTypes().subscribe({
      next: types => this.employmentTypes.set(types),
      error: () => this.error.set('Unable to load employment types.')
    });
    this.restoreListQueryState(this.route.snapshot.queryParamMap.get('search'),
      this.route.snapshot.queryParamMap.get('page'));
    const routePath = this.route.snapshot.routeConfig?.path;
    if (routePath === 'hr/employee/new') {
      this.pageMode.set('create');
      this.editingForm.set(true);
      this.form = this.emptyForm();
      return;
    }
    if (routePath === 'hr/employee/:employeeId/view' || routePath === 'hr/employee/:employeeId/edit') {
      const mode = routePath.endsWith('/edit') ? 'edit' : 'view';
      this.pageMode.set(mode);
      this.editingForm.set(mode === 'edit');
      this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
        this.loadEmployeeFromRoute(params.get('employeeId'), mode === 'edit');
      });
      return;
    }

    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const search = params.get('search') ?? '';
      const parsedPage = Number(params.get('page'));
      const page = Number.isSafeInteger(parsedPage) && parsedPage > 0 ? parsedPage : 1;
      const key = `${search}\u0000${page}`;
      if (key === this.lastListQueryKey) return;
      const shouldLoad = this.lastListQueryKey === null || search !== this.search.trim() || page !== this.page;
      this.lastListQueryKey = key;
      this.restoreListQueryState(search, String(page));
      if (shouldLoad) this.load(false);
    });
  }

  private restoreListQueryState(search: string | null, rawPage: string | null): void {
    const parsedPage = Number(rawPage);
    this.search = search ?? '';
    this.page = Number.isSafeInteger(parsedPage) && parsedPage > 0 ? parsedPage : 1;
  }

  pageTitle(): string {
    switch (this.pageMode()) {
      case 'create': return 'Create Employee';
      case 'view': return 'Employee profile';
      case 'edit': return 'Edit Employee';
      default: return 'Employees';
    }
  }

  pageDescription(): string {
    switch (this.pageMode()) {
      case 'create': return 'Add a new employee to the current Business.';
      case 'view': return 'Employee profile, employment details and authorized history.';
      case 'edit': return 'Update employee details and authorized personal information.';
      default: return 'Manage employee records, profiles, lifecycle events and assignments.';
    }
  }

  listQueryParams(): { search: string | null; page: number | null } {
    return { search: this.search.trim() || null, page: this.page > 1 ? this.page : null };
  }

  load(updateUrl = true): void {
    if (this.pageMode() !== 'list') return;
    if (updateUrl) {
      void this.router.navigate([], {
        relativeTo: this.route,
        queryParams: this.listQueryParams(),
        queryParamsHandling: 'merge',
        replaceUrl: true
      });
    }
    this.loading.set(true);
    this.error.set(null);
    this.service.searchEmployees(this.search.trim(), this.page, this.pageSize).subscribe({
      next: result => {
        this.employees.set(result.items);
        this.totalCount = result.totalCount;
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Unable to load employees. Check HR access and Business scope.');
        this.loading.set(false);
      }
    });
  }

  searchEmployees(): void {
    this.search = this.search.trim();
    this.page = 1;
    this.load();
  }

  changePage(offset: number): void {
    this.page += offset;
    this.load();
  }

  onTableQuery(query: UiTableQuery): void {
    if (query.page !== this.page) {
      this.changePage(query.page - this.page);
    }
  }

  statusVariant(status: string): UiBadgeVariant {
    return STATUS_VARIANTS[status] ?? 'neutral';
  }

  openProfile(employee: HrEmployeeSearchItem): void {
    this.navigateToEmployee(employee.id, 'view');
  }

  closeProfile(): void {
    this.backToEmployees();
  }

  backToEmployees(): void {
    void this.router.navigate(['/hr/employees'], { queryParams: this.listQueryParams() });
  }

  retryProfileLoad(): void {
    if (this.activeEmployeeId !== null) {
      this.loadEmployeeProfile(this.activeEmployeeId, this.pageMode() === 'edit');
    }
  }

  currentPosition(): HrEmployeePositionAssignment | null {
    return this.positionHistory().find(assignment => assignment.isCurrent) ?? null;
  }

  private navigateToEmployee(employeeId: number, destination: 'view' | 'edit'): void {
    void this.router.navigate(['/hr/employee', employeeId, destination], {
      queryParams: this.listQueryParams()
    });
  }

  private loadEmployeeFromRoute(rawId: string | null, editing: boolean): void {
    const employeeId = rawId && /^\d+$/.test(rawId) ? Number(rawId) : NaN;
    this.selected.set(null);
    this.profileStep.set(0);
    this.lifecycleHistory.set([]);
    this.positionHistory.set([]);
    this.locationHistory.set([]);
    this.transfers.set([]);
    this.userLink.set(null);
    this.activeEmployeeId = Number.isSafeInteger(employeeId) && employeeId > 0 ? employeeId : null;
    this.profileLoadError.set(null);
    this.profileNotFound.set(this.activeEmployeeId === null);
    this.profileLoading.set(this.activeEmployeeId !== null);
    if (this.activeEmployeeId === null) return;
    this.loadEmployeeProfile(this.activeEmployeeId, editing);
  }

  private loadEmployeeProfile(employeeId: number, editing: boolean): void {
    const requestId = ++this.profileLoadRequestId;
    this.profileLoading.set(true);
    this.profileLoadError.set(null);
    this.profileNotFound.set(false);
    this.selected.set(null);
    this.service.getEmployee(employeeId).subscribe({
      next: profile => {
        if (requestId !== this.profileLoadRequestId || !this.isActiveEmployee(employeeId)) return;
        this.selected.set(profile);
        this.profileLoading.set(false);
        this.lifecycleEffectiveDate = new Date().toISOString().slice(0, 10);
        this.lifecycleEmploymentTypeId = String(profile.currentEmploymentTypeId);
        this.rehireEmploymentTypeId = String(profile.currentEmploymentTypeId);
        if (editing) this.initializeEditForm(profile);
        if (this.pageMode() === 'view') this.loadEmployeeRelatedData();
      },
      error: (error: unknown) => {
        if (requestId !== this.profileLoadRequestId || !this.isActiveEmployee(employeeId)) return;
        this.profileLoading.set(false);
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.profileNotFound.set(true);
          return;
        }
        this.profileLoadError.set(error instanceof HttpErrorResponse && error.status === 403
          ? 'You are not authorized to view this employee in the current Business.'
          : 'The employee could not be loaded. Check access and Business scope, then retry.');
      }
    });
  }

  private loadEmployeeRelatedData(): void {
    const employeeId = this.activeEmployeeId;
    if (employeeId === null) return;
    if (this.auth.hasPermission('hr.employee_lifecycle.view')) this.loadLifecycleHistory();
    if (this.auth.hasPermission('hr.position.view')) this.loadPositionHistory();
    if (this.auth.hasPermission('hr.employee_transfer.view')) this.loadLocationHistory();
    if (this.auth.hasPermission('hr.location.view')) {
      this.service.getOperationalLocations().subscribe({
        next: locations => {
          if (this.isActiveEmployee(employeeId)) this.operationalLocations.set(locations);
        },
        error: () => {
          if (this.isActiveEmployee(employeeId)) this.error.set('Unable to load operational locations.');
        }
      });
    }
    if (this.auth.hasPermission('hr.employee.view')) this.loadUserLink();
    if (this.auth.hasPermission('hr.position.view') && this.auth.hasPermission('hr.position.manage')) {
      this.service.getPositions().subscribe({
        next: positions => {
          if (this.isActiveEmployee(employeeId)) this.positions.set(positions);
        },
        error: () => {
          if (this.isActiveEmployee(employeeId)) this.error.set('Unable to load positions for employee assignment.');
        }
      });
    }
  }

  private isActiveEmployee(employeeId: number): boolean {
    return this.activeEmployeeId === employeeId;
  }

  loadUserLink(): void {
    const employee = this.selected();
    if (!employee || !this.auth.hasPermission('hr.employee.view')) return;
    this.service.getEmployeeUserLink(employee.id).subscribe({
      next: link => {
        if (this.isActiveEmployee(employee.id)) this.userLink.set(link);
      },
      error: () => {
        if (this.isActiveEmployee(employee.id)) this.error.set('Unable to load the employee Login User link.');
      }
    });
  }

  linkUser(): void {
    const employee = this.selected();
    const identityUserId = Number(this.identityUserId);
    const reason = this.userLinkReason.trim();
    if (!employee || !this.auth.hasPermission('hr.employee.link_user')
      || !Number.isSafeInteger(identityUserId) || identityUserId <= 0 || !reason) return;
    this.service.linkEmployeeUser(employee.id, identityUserId, reason).subscribe({
      next: link => {
        this.userLink.set(link);
        this.identityUserId = '';
        this.userLinkReason = '';
      },
      error: () => this.error.set('Unable to link Login User. Confirm the ID, Business, and active link state.')
    });
  }

  unlinkUser(): void {
    const employee = this.selected();
    const link = this.userLink();
    const reason = this.userUnlinkReason.trim();
    if (!employee || !link || !this.auth.hasPermission('hr.employee.unlink_user') || !reason) return;
    this.service.unlinkEmployeeUser(employee.id, link.version, reason).subscribe({
      next: () => {
        this.userLink.set(null);
        this.userUnlinkReason = '';
      },
      error: () => this.error.set('Unable to unlink Login User. The link may have changed; refresh and retry.')
    });
  }

  loadLifecycleHistory(): void {
    const employee = this.selected();
    if (!employee || !this.auth.hasPermission('hr.employee_lifecycle.view')) return;
    this.service.getEmployeeLifecycle(employee.id).subscribe({
      next: events => {
        if (this.isActiveEmployee(employee.id)) this.lifecycleHistory.set(events);
      },
      error: () => {
        if (this.isActiveEmployee(employee.id)) this.error.set('Unable to load employee lifecycle history.');
      }
    });
  }

  loadPositionHistory(): void {
    const employee = this.selected();
    if (!employee || !this.auth.hasPermission('hr.position.view')) return;
    this.service.getEmployeePositionHistory(employee.id).subscribe({
      next: assignments => {
        if (this.isActiveEmployee(employee.id)) this.positionHistory.set(assignments);
      },
      error: () => {
        if (this.isActiveEmployee(employee.id)) this.error.set('Unable to load employee position history.');
      }
    });
  }

  loadLocationHistory(): void {
    const employee = this.selected();
    if (!employee || !this.auth.hasPermission('hr.employee_transfer.view')) return;
    this.service.getEmployeeLocationHistory(employee.id).subscribe({
      next: assignments => {
        if (this.isActiveEmployee(employee.id)) this.locationHistory.set(assignments);
      },
      error: () => {
        if (this.isActiveEmployee(employee.id)) this.error.set('Unable to load employee location assignment history.');
      }
    });
    this.service.getEmployeeLocationTransfers(employee.id).subscribe({
      next: transfers => {
        if (this.isActiveEmployee(employee.id)) this.transfers.set(transfers);
      },
      error: () => {
        if (this.isActiveEmployee(employee.id)) this.error.set('Unable to load employee transfer requests.');
      }
    });
  }

  currentPrimaryLocation(): HrEmployeeLocationAssignment | null {
    const today = new Date().toISOString().slice(0, 10);
    return this.locationHistory().find(assignment => assignment.assignmentType === 'Primary'
      && assignment.status === 'Effective' && assignment.effectiveFrom <= today
      && (!assignment.effectiveTo || assignment.effectiveTo >= today)) ?? null;
  }

  saveLocationAssignment(): void {
    const employee = this.selected();
    if (!employee || !this.auth.hasPermission('hr.employee_transfer.create')) return;
    const data: HrEmployeeLocationAssignmentData = {
      operationalLocationProjectionId: Number(this.locationProjectionId),
      assignmentType: this.locationAssignmentType,
      isTemporary: this.locationAssignmentType === 'Temporary',
      effectiveFrom: this.locationEffectiveFrom,
      effectiveTo: this.locationAssignmentType === 'Temporary' ? this.locationEffectiveTo || null : null,
      reason: this.locationReason.trim()
    };
    this.service.assignEmployeeLocation(employee.id, employee.version, data).subscribe({
      next: () => {
        this.locationReason = '';
        this.loadLocationHistory();
        this.refreshEmployee(employee.id);
      },
      error: () => this.error.set('Unable to assign location. Check scope, status, dates, and employee version.')
    });
  }

  requestLocationTransfer(): void {
    const employee = this.selected();
    if (!employee || !this.auth.hasPermission('hr.employee_transfer.create')) return;
    this.service.requestEmployeeLocationTransfer(employee.id, {
      operationalLocationProjectionId: Number(this.transferLocationProjectionId),
      effectiveFrom: this.transferEffectiveFrom,
      expectedEmployeeVersion: employee.version,
      reason: this.transferReason.trim()
    }).subscribe({
      next: result => {
        this.transferReason = '';
        this.loadLocationHistory();
        this.error.set(result.status === 'PendingApproval'
          ? null : `Transfer request was created with status ${result.status}.`);
      },
      error: () => this.error.set('Unable to request location transfer. Approval policy, scope, or employee version may be invalid.')
    });
  }

  decideTransfer(transfer: HrEmployeeLocationTransfer, approve: boolean): void {
    const employee = this.selected();
    const reason = this.transferActionReason.trim();
    if (!employee || transfer.status !== 'PendingApproval' || !reason
      || !this.auth.hasPermission('hr.employee_transfer.approve')) return;
    this.service.decideEmployeeLocationTransfer(
      employee.id, transfer.id, this.transferDecisionKey(transfer.id, approve ? 'approve' : 'reject'), reason, approve
    ).subscribe({
      next: () => {
        this.transferDecisionKeys.delete(`${transfer.id}:${approve ? 'approve' : 'reject'}`);
        this.transferActionReason = '';
        this.loadLocationHistory();
        if (approve) this.refreshEmployee(employee.id);
      },
      error: () => this.error.set('Unable to process transfer approval. It may no longer be pending or you may not be an eligible approver.')
    });
  }

  cancelTransfer(transfer: HrEmployeeLocationTransfer): void {
    const employee = this.selected();
    const reason = this.transferActionReason.trim();
    if (!employee || transfer.status !== 'PendingApproval' || !reason
      || !this.auth.hasPermission('hr.employee_transfer.create')) return;
    this.service.cancelEmployeeLocationTransfer(
      employee.id, transfer.id, this.transferDecisionKey(transfer.id, 'cancel'), reason
    ).subscribe({
      next: () => {
        this.transferDecisionKeys.delete(`${transfer.id}:cancel`);
        this.transferActionReason = '';
        this.loadLocationHistory();
      },
      error: () => this.error.set('Unable to cancel transfer request.')
    });
  }

  private transferDecisionKey(transferId: string, action: string): string {
    const key = `${transferId}:${action}`;
    const existing = this.transferDecisionKeys.get(key);
    if (existing) return existing;
    const created = crypto.randomUUID();
    this.transferDecisionKeys.set(key, created);
    return created;
  }

  transitionLifecycle(): void {
    const employee = this.selected();
    if (!employee || !this.auth.hasPermission('hr.employee_lifecycle.manage')) return;
    const data: HrEmployeeLifecycleTransitionData = {
      eventType: this.lifecycleEventType,
      effectiveDate: this.lifecycleEffectiveDate,
      reason: this.lifecycleReason.trim() || null
    };
    if (data.eventType === 'EmploymentTypeChanged') {
      data.employmentTypeId = Number(this.lifecycleEmploymentTypeId);
    }
    this.service.transitionEmployeeLifecycle(employee.id, employee.version, data).subscribe({
      next: () => this.refreshEmployee(employee.id),
      error: () => this.error.set('Unable to apply lifecycle action. The employee may have changed or the transition is invalid.')
    });
  }

  rehire(): void {
    const employee = this.selected();
    if (!employee || employee.currentEmploymentStatus !== 'Exited'
      || !this.auth.hasPermission('hr.employee_lifecycle.rehire')) return;
    const data: HrEmployeeRehireData = {
      employmentTypeId: Number(this.rehireEmploymentTypeId),
      employmentStatus: this.rehireStatus,
      effectiveDate: this.lifecycleEffectiveDate,
      reason: this.lifecycleReason.trim() || null
    };
    this.service.rehireEmployee(employee.id, employee.version, data).subscribe({
      next: () => this.refreshEmployee(employee.id),
      error: () => this.error.set('Unable to rehire employee. Check that the employee is exited and the date is valid.')
    });
  }

  setAssignmentOrgUnit(positionId: string): void {
    const position = this.positions().find(candidate => candidate.id === Number(positionId));
    this.assignmentOrgUnitId = position?.orgUnitId ?? 0;
  }

  assignPosition(): void {
    const employee = this.selected();
    if (!employee || !this.auth.hasPermission('hr.position.manage')
      || !this.auth.hasPermission('hr.position.view')) return;
    const data: HrEmployeePositionAssignmentData = {
      positionId: Number(this.assignmentPositionId),
      orgUnitId: this.assignmentOrgUnitId,
      reportsToEmployeeId: this.reportsToEmployeeId.trim() ? Number(this.reportsToEmployeeId) : null,
      assignmentType: this.assignmentType,
      effectiveFrom: this.lifecycleEffectiveDate,
      effectiveTo: this.assignmentType === 'Primary' || !this.assignmentEffectiveTo
        ? null : this.assignmentEffectiveTo,
      reason: this.lifecycleReason.trim() || null
    };
    this.service.assignEmployeePosition(employee.id, employee.version, data).subscribe({
      next: () => {
        this.loadPositionHistory();
        this.refreshEmployee(employee.id);
      },
      error: () => this.error.set('Unable to assign position. Check the org unit, manager, effective date, and version.')
    });
  }

  private refreshEmployee(employeeId: number): void {
    this.service.getEmployee(employeeId).subscribe({
      next: profile => {
        this.selected.set(profile);
        this.loadLifecycleHistory();
        this.loadPositionHistory();
        this.load();
      },
      error: () => this.error.set('The action was saved, but the updated employee profile could not be loaded.')
    });
  }

  beginCreate(): void {
    if (!this.auth.hasPermission('hr.employee.create')) return;
    void this.router.navigate(['/hr/employee/new'], { queryParams: this.listQueryParams() });
  }

  beginEdit(): void {
    const employee = this.selected();
    if (!employee || !this.auth.hasPermission('hr.employee.edit')) return;
    this.navigateToEmployee(employee.id, 'edit');
  }

  private initializeEditForm(employee: HrEmployeeProfile): void {
    const sensitive = employee.sensitiveFields;
    this.form = {
      ...this.emptyForm(),
      employeeCode: employee.employeeCode,
      firstName: employee.firstName,
      middleName: employee.middleName || '',
      lastName: employee.lastName || '',
      displayName: employee.displayName,
      workEmail: employee.workEmail || '',
      dateOfBirth: sensitive.dateOfBirth || '',
      genderCode: sensitive.genderCode || '',
      nationalId: sensitive.nationalId || '',
      passportNo: sensitive.passportNo || '',
      personalEmail: sensitive.personalEmail || '',
      mobile: sensitive.mobile || '',
      presentAddress: sensitive.presentAddress || '',
      permanentAddress: sensitive.permanentAddress || '',
      emergencyContactName: sensitive.emergencyContactName || '',
      emergencyContactPhone: sensitive.emergencyContactPhone || ''
    };
    this.employeeStep.set(0);
    this.formStepError.set(null);
    this.editingForm.set(true);
  }

  beginEditFromList(employee: HrEmployeeSearchItem): void {
    if (!this.auth.hasPermission('hr.employee.edit')) return;
    this.navigateToEmployee(employee.id, 'edit');
  }

  isLastEmployeeStep(): boolean {
    const hasPersonalStep = this.hasPersonalInformationStep();
    const lastIndex = this.selected() ? (hasPersonalStep ? 1 : 0) : (hasPersonalStep ? 2 : 1);
    return this.employeeStep() === lastIndex;
  }

  personalInformationStepIndex(): number {
    return this.hasPersonalInformationStep() ? 1 : -1;
  }

  employmentStepIndex(): number {
    return this.hasPersonalInformationStep() ? 2 : 1;
  }

  employeeStepHasError(index: number): boolean {
    return this.formStepError() !== null && this.employeeStep() === index;
  }

  hasPersonalInformationStep(): boolean {
    return this.auth.hasPermission('hr.employee.view_sensitive')
      || this.auth.hasPermission('hr.employee.view_identity');
  }

  private validateEmployeeStep(index: number): boolean {
    if (this.saving()) return false;
    const invalid = index === 0
      ? (!this.selected() && !this.form.employeeCode.trim())
        || !this.form.firstName.trim()
        || !this.form.displayName.trim()
      : !this.selected() && index === this.employmentStepIndex()
        ? !this.form.employmentTypeId || !this.form.joiningDate
      : false;
    if (invalid) {
      this.formStepError.set(index === 0
        ? 'Enter the employee code, first name, and display name before continuing.'
        : 'Select an employment type and joining date before continuing.');
      return false;
    }
    this.formStepError.set(null);
    return true;
  }

  save(): void {
    if (this.saving()) return;
    this.error.set(null);
    const finalIndex = this.selected() ? (this.hasPersonalInformationStep() ? 1 : 0)
      : this.employmentStepIndex();
    for (let index = 0; index <= finalIndex; index++) {
      if (!this.validateEmployeeStep(index)) {
        this.employeeStep.set(index);
        return;
      }
    }
    const data = this.toCreateData();
    const employee = this.selected();
    if (employee) {
      const update: HrEmployeeUpdateData = {
        firstName: data.firstName,
        middleName: data.middleName,
        lastName: data.lastName,
        displayName: data.displayName,
        dateOfBirth: data.dateOfBirth,
        genderCode: data.genderCode,
        nationalId: data.nationalId,
        passportNo: data.passportNo,
        personalEmail: data.personalEmail,
        workEmail: data.workEmail,
        mobile: data.mobile,
        presentAddress: data.presentAddress,
        permanentAddress: data.permanentAddress,
        emergencyContactName: data.emergencyContactName,
        emergencyContactPhone: data.emergencyContactPhone
      };
      if (!this.auth.hasPermission('hr.employee.view_sensitive')) {
        update.dateOfBirth = employee.sensitiveFields.dateOfBirth;
        update.genderCode = employee.sensitiveFields.genderCode;
        update.personalEmail = employee.sensitiveFields.personalEmail;
        update.mobile = employee.sensitiveFields.mobile;
        update.presentAddress = employee.sensitiveFields.presentAddress;
        update.permanentAddress = employee.sensitiveFields.permanentAddress;
        update.emergencyContactName = employee.sensitiveFields.emergencyContactName;
        update.emergencyContactPhone = employee.sensitiveFields.emergencyContactPhone;
      }
      if (!this.auth.hasPermission('hr.employee.view_identity')) {
        update.nationalId = employee.sensitiveFields.nationalId;
        update.passportNo = employee.sensitiveFields.passportNo;
      }
      this.saving.set(true);
      this.service.updateEmployee(employee.id, employee.version, update).subscribe({
        next: () => {
          this.saving.set(false);
          this.navigateToEmployee(employee.id, 'view');
        },
        error: () => {
          this.saving.set(false);
          this.error.set('Unable to update employee. The record may have changed or access may be restricted.');
        }
      });
      return;
    }
    if (!this.auth.hasPermission('hr.employee.create')) return;
    this.saving.set(true);
    this.service.createEmployee(data).subscribe({
      next: () => {
        this.saving.set(false);
        this.page = 1;
        this.search = '';
        this.backToEmployees();
      },
      error: () => {
        this.saving.set(false);
        this.error.set('Unable to create employee. Check employee data and permissions.');
      }
    });
  }

  cancelForm(): void {
    if (this.selected()) {
      this.navigateToEmployee(this.selected()!.id, 'view');
      return;
    }
    this.backToEmployees();
  }

  private toCreateData(): HrEmployeeCreateData {
    const nullable = (value: string) => value.trim() || null;
    return {
      employeeCode: this.form.employeeCode.trim(),
      firstName: this.form.firstName.trim(),
      middleName: nullable(this.form.middleName),
      lastName: nullable(this.form.lastName),
      displayName: this.form.displayName.trim(),
      dateOfBirth: this.form.dateOfBirth || null,
      genderCode: nullable(this.form.genderCode),
      nationalId: nullable(this.form.nationalId),
      passportNo: nullable(this.form.passportNo),
      personalEmail: nullable(this.form.personalEmail),
      workEmail: nullable(this.form.workEmail),
      mobile: nullable(this.form.mobile),
      presentAddress: nullable(this.form.presentAddress),
      permanentAddress: nullable(this.form.permanentAddress),
      emergencyContactName: nullable(this.form.emergencyContactName),
      emergencyContactPhone: nullable(this.form.emergencyContactPhone),
      employmentTypeId: Number(this.form.employmentTypeId),
      employmentStatus: this.form.employmentStatus,
      joiningDate: this.form.joiningDate,
      confirmationDate: this.form.confirmationDate || null
    };
  }

  private emptyForm(): {
    employeeCode: string;
    firstName: string;
    middleName: string;
    lastName: string;
    displayName: string;
    dateOfBirth: string;
    genderCode: string;
    nationalId: string;
    passportNo: string;
    personalEmail: string;
    workEmail: string;
    mobile: string;
    presentAddress: string;
    permanentAddress: string;
    emergencyContactName: string;
    emergencyContactPhone: string;
    employmentTypeId: string;
    employmentStatus: 'Active' | 'Probation';
    joiningDate: string;
    confirmationDate: string;
  } {
    return {
      employeeCode: '', firstName: '', middleName: '', lastName: '', displayName: '',
      dateOfBirth: '', genderCode: '', nationalId: '', passportNo: '', personalEmail: '',
      workEmail: '', mobile: '', presentAddress: '', permanentAddress: '',
      emergencyContactName: '', emergencyContactPhone: '', employmentTypeId: '',
      employmentStatus: 'Active', joiningDate: new Date().toISOString().slice(0, 10),
      confirmationDate: ''
    };
  }
}
