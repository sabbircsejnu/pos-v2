import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface HrOrgUnit {
  id: number;
  businessId: number;
  code: string;
  name: string;
  unitType: string;
  parentOrgUnitId: number | null;
  operationalLocationProjectionId: number | null;
  effectiveFrom: string;
  effectiveTo: string | null;
  isActive: boolean;
  version: number;
}

export interface HrOrgUnitWriteData {
  code: string;
  name: string;
  unitType: string;
  parentOrgUnitId: number | null;
  isActive: boolean;
  operationalLocationProjectionId?: number | null;
  effectiveTo?: string | null;
}

export interface HrOperationalLocation {
  id: number;
  businessId: number;
  sourceSystem: string;
  sourceEntityType: 'Outlet' | 'Warehouse';
  sourceEntityId: string;
  name: string;
  address: string;
  sourceVersion: number;
  sourceUpdatedAtUtc: string;
  lastSyncedAtUtc: string;
  isSourceActive: boolean;
  isSourceDeleted: boolean;
  syncStatus: string;
}

export interface HrPosition {
  id: number;
  businessId: number;
  code: string;
  name: string;
  orgUnitId: number;
  operationalLocationProjectionId: number | null;
  reportsToPositionId: number | null;
  status: 'Vacant' | 'Filled' | 'TemporarilyFilled' | 'Inactive';
  effectiveFrom: string | null;
  effectiveTo: string | null;
  version: number;
}

export interface HrPositionWriteData {
  code: string;
  name: string;
  orgUnitId: number;
  reportsToPositionId: number | null;
  status: Exclude<HrPosition['status'], 'Inactive'>;
}

export interface HrEmploymentType {
  id: number;
  code: string;
  name: string;
}

export interface HrEmployeeSearchItem {
  id: number;
  employeeCode: string;
  firstName: string;
  middleName: string | null;
  lastName: string | null;
  displayName: string;
  workEmail: string | null;
  currentEmploymentStatus: string;
  currentEmploymentTypeId: number;
  currentEmploymentTypeName: string;
  joiningDate: string;
  version: number;
}

export interface HrEmployeeSensitiveFields {
  dateOfBirth: string | null;
  genderCode: string | null;
  personalEmail: string | null;
  mobile: string | null;
  presentAddress: string | null;
  permanentAddress: string | null;
  emergencyContactName: string | null;
  emergencyContactPhone: string | null;
  nationalId: string | null;
  passportNo: string | null;
}

export interface HrEmployeeProfile extends HrEmployeeSearchItem {
  currentPositionId: number | null;
  currentOrgUnitId: number | null;
  confirmationDate: string | null;
  sensitiveFields: HrEmployeeSensitiveFields;
}

export interface HrEmployeeUserLink {
  id: number;
  employeeId: number;
  identityUserId: number;
  linkedAtUtc: string;
  linkedByUserId: number;
  linkReason: string;
  unlinkedAtUtc: string | null;
  unlinkedByUserId: number | null;
  unlinkReason: string | null;
  isActive: boolean;
  version: number;
}

export interface HrEmployeeSelf {
  id: number;
  employeeCode: string;
  displayName: string;
  currentEmploymentStatus: string;
}

export interface HrEmployeeLifecycleEvent {
  id: number;
  employeeId: number;
  employmentPeriodId: number;
  eventType: string;
  effectiveDate: string;
  previousStateJson: string;
  newStateJson: string;
  reason: string | null;
  actorUserId: number | null;
  occurredAtUtc: string;
}

export interface HrEmployeePositionAssignment {
  id: number;
  employeeId: number;
  positionId: number;
  positionCode: string;
  positionName: string;
  orgUnitId: number;
  orgUnitName: string;
  reportsToEmployeeId: number | null;
  reportsToEmployeeName: string | null;
  assignmentType: 'Primary' | 'Temporary' | 'Acting';
  effectiveFrom: string;
  effectiveTo: string | null;
  reason: string | null;
  isCurrent: boolean;
  version: number;
}

export interface HrEmployeeLifecycleTransitionData {
  eventType: 'Probation' | 'Confirmed' | 'EmploymentTypeChanged' | 'Suspended' | 'Reinstated' | 'Exited';
  effectiveDate: string;
  reason: string | null;
  employmentTypeId?: number;
}

export interface HrEmployeeRehireData {
  employmentTypeId: number;
  employmentStatus: 'Active' | 'Probation';
  effectiveDate: string;
  reason: string | null;
}

export interface HrEmployeePositionAssignmentData {
  positionId: number;
  orgUnitId: number;
  reportsToEmployeeId: number | null;
  assignmentType: 'Primary' | 'Temporary' | 'Acting';
  effectiveFrom: string;
  effectiveTo: string | null;
  reason: string | null;
}

export interface HrEmployeeLocationAssignment {
  id: number;
  employeeId: number;
  operationalLocationProjectionId: number;
  locationName: string;
  sourceSystem: string;
  sourceEntityType: 'Outlet' | 'Warehouse';
  sourceEntityId: string;
  locationSourceVersion: number;
  assignmentType: 'Primary' | 'Additional' | 'Temporary';
  isTemporary: boolean;
  effectiveFrom: string;
  effectiveTo: string | null;
  status: string;
  reason: string;
  requestedByUserId: number;
  requestedAtUtc: string;
  approvedByUserId: number | null;
  approvedAtUtc: string | null;
  approvalInstanceId: number | null;
  version: number;
}

export interface HrEmployeeLocationAssignmentData {
  operationalLocationProjectionId: number;
  assignmentType: 'Primary' | 'Additional' | 'Temporary';
  isTemporary: boolean;
  effectiveFrom: string;
  effectiveTo: string | null;
  reason: string;
}

export interface HrEmployeeLocationTransferData {
  operationalLocationProjectionId: number;
  effectiveFrom: string;
  expectedEmployeeVersion: number;
  reason: string;
}

export interface HrEmployeeLocationTransferSubmission {
  transferId: string;
  approvalInstanceId: number;
  status: string;
}

export interface HrEmployeeLocationTransfer {
  id: string;
  employeeId: number;
  targetLocationProjectionId: number;
  locationName: string;
  sourceSystem: string;
  sourceEntityType: 'Outlet' | 'Warehouse';
  sourceEntityId: string;
  effectiveFrom: string;
  status:
    | 'PendingApproval'
    | 'Approved'
    | 'ApprovedPendingEffective'
    | 'Effective'
    | 'Rejected'
    | 'Cancelled'
    | 'Superseded';
  reason: string;
  requestedByUserId: number;
  requestedAtUtc: string;
  approvalInstanceId: number | null;
  version: number;
}

export interface HrEmployeeCreateData {
  employeeCode: string;
  firstName: string;
  middleName: string | null;
  lastName: string | null;
  displayName: string;
  dateOfBirth: string | null;
  genderCode: string | null;
  nationalId: string | null;
  passportNo: string | null;
  personalEmail: string | null;
  workEmail: string | null;
  mobile: string | null;
  presentAddress: string | null;
  permanentAddress: string | null;
  emergencyContactName: string | null;
  emergencyContactPhone: string | null;
  employmentTypeId: number;
  employmentStatus: 'Active' | 'Probation';
  joiningDate: string;
  confirmationDate: string | null;
}

export interface HrEmployeeUpdateData extends Omit<HrEmployeeCreateData,
  'employeeCode' | 'employmentTypeId' | 'employmentStatus' | 'joiningDate' | 'confirmationDate'> {
}

export interface HrEmployeeSearchPage {
  items: HrEmployeeSearchItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

@Injectable({ providedIn: 'root' })
export class HrOrganisationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/hr/v1`;

  getOrgUnits(): Observable<HrOrgUnit[]> {
    return this.http.get<HrOrgUnit[]>(`${this.baseUrl}/org-units`);
  }

  createOrgUnit(data: HrOrgUnitWriteData): Observable<HrOrgUnit> {
    return this.http.post<HrOrgUnit>(`${this.baseUrl}/org-units`, data);
  }

  updateOrgUnit(id: number, expectedVersion: number, data: HrOrgUnitWriteData): Observable<HrOrgUnit> {
    return this.http.put<HrOrgUnit>(`${this.baseUrl}/org-units/${id}`, { expectedVersion, data });
  }

  getOperationalLocations(): Observable<HrOperationalLocation[]> {
    return this.http.get<HrOperationalLocation[]>(`${this.baseUrl}/operational-locations`);
  }

  getPositions(search?: string): Observable<HrPosition[]> {
    return this.http.get<HrPosition[]>(`${this.baseUrl}/positions`, {
      params: search ? { search } : {}
    });
  }

  createPosition(data: HrPositionWriteData): Observable<HrPosition> {
    return this.http.post<HrPosition>(`${this.baseUrl}/positions`, data);
  }

  updatePosition(id: number, expectedVersion: number, data: HrPositionWriteData): Observable<HrPosition> {
    return this.http.put<HrPosition>(`${this.baseUrl}/positions/${id}`, { expectedVersion, data });
  }

  deactivatePosition(id: number, expectedVersion: number, effectiveTo: string): Observable<HrPosition> {
    return this.http.post<HrPosition>(`${this.baseUrl}/positions/${id}/deactivate`, {
      expectedVersion,
      effectiveTo
    });
  }

  getEmploymentTypes(): Observable<HrEmploymentType[]> {
    return this.http.get<HrEmploymentType[]>(`${this.baseUrl}/employment-types`);
  }

  searchEmployees(search: string, page: number, pageSize: number): Observable<HrEmployeeSearchPage> {
    return this.http.get<HrEmployeeSearchPage>(`${this.baseUrl}/employees`, {
      params: { search, page, pageSize }
    });
  }

  getEmployee(id: number): Observable<HrEmployeeProfile> {
    return this.http.get<HrEmployeeProfile>(`${this.baseUrl}/employees/${id}`);
  }

  createEmployee(data: HrEmployeeCreateData): Observable<HrEmployeeProfile> {
    return this.http.post<HrEmployeeProfile>(`${this.baseUrl}/employees`, data);
  }

  updateEmployee(id: number, expectedVersion: number, data: HrEmployeeUpdateData): Observable<HrEmployeeProfile> {
    return this.http.put<HrEmployeeProfile>(`${this.baseUrl}/employees/${id}`, { expectedVersion, data });
  }

  getEmployeeUserLink(id: number): Observable<HrEmployeeUserLink | null> {
    return this.http.get<{ activeLink: HrEmployeeUserLink | null }>(
      `${this.baseUrl}/employees/${id}/user-link`
    ).pipe(map(state => state.activeLink));
  }

  linkEmployeeUser(id: number, identityUserId: number, reason: string): Observable<HrEmployeeUserLink> {
    return this.http.post<HrEmployeeUserLink>(`${this.baseUrl}/employees/${id}/user-link`, {
      identityUserId,
      reason
    });
  }

  unlinkEmployeeUser(id: number, expectedVersion: number, reason: string): Observable<HrEmployeeUserLink> {
    return this.http.delete<HrEmployeeUserLink>(`${this.baseUrl}/employees/${id}/user-link`, {
      body: { expectedVersion, reason }
    });
  }

  getCurrentEmployee(): Observable<HrEmployeeSelf> {
    return this.http.get<HrEmployeeSelf>(`${this.baseUrl}/me/employee`);
  }

  getEmployeeLifecycle(id: number): Observable<HrEmployeeLifecycleEvent[]> {
    return this.http.get<HrEmployeeLifecycleEvent[]>(`${this.baseUrl}/employees/${id}/lifecycle`);
  }

  transitionEmployeeLifecycle(
    id: number, expectedVersion: number, data: HrEmployeeLifecycleTransitionData
  ): Observable<HrEmployeeLifecycleEvent> {
    return this.http.post<HrEmployeeLifecycleEvent>(`${this.baseUrl}/employees/${id}/lifecycle`, {
      expectedVersion,
      data
    });
  }

  rehireEmployee(id: number, expectedVersion: number, data: HrEmployeeRehireData): Observable<HrEmployeeLifecycleEvent> {
    return this.http.post<HrEmployeeLifecycleEvent>(`${this.baseUrl}/employees/${id}/rehire`, {
      expectedVersion,
      data
    });
  }

  getEmployeePositionHistory(id: number): Observable<HrEmployeePositionAssignment[]> {
    return this.http.get<HrEmployeePositionAssignment[]>(`${this.baseUrl}/employees/${id}/positions`);
  }

  assignEmployeePosition(
    id: number, expectedVersion: number, data: HrEmployeePositionAssignmentData
  ): Observable<HrEmployeePositionAssignment> {
    return this.http.post<HrEmployeePositionAssignment>(`${this.baseUrl}/employees/${id}/positions`, {
      expectedVersion,
      data
    });
  }

  getEmployeeLocationHistory(id: number): Observable<HrEmployeeLocationAssignment[]> {
    return this.http.get<HrEmployeeLocationAssignment[]>(`${this.baseUrl}/employees/${id}/locations`);
  }

  getEmployeeLocationTransfers(id: number): Observable<HrEmployeeLocationTransfer[]> {
    return this.http.get<HrEmployeeLocationTransfer[]>(
      `${this.baseUrl}/employees/${id}/location-transfers`
    );
  }

  assignEmployeeLocation(
    id: number, expectedEmployeeVersion: number, data: HrEmployeeLocationAssignmentData
  ): Observable<HrEmployeeLocationAssignment> {
    return this.http.post<HrEmployeeLocationAssignment>(
      `${this.baseUrl}/employees/${id}/location-assignments`,
      { expectedEmployeeVersion, data }
    );
  }

  requestEmployeeLocationTransfer(
    id: number, data: HrEmployeeLocationTransferData
  ): Observable<HrEmployeeLocationTransferSubmission> {
    return this.http.post<HrEmployeeLocationTransferSubmission>(
      `${this.baseUrl}/employees/${id}/location-transfers`, { data }
    );
  }

  decideEmployeeLocationTransfer(
    id: number, transferId: string, decisionKey: string, reason: string, approve: boolean
  ): Observable<unknown> {
    const decision = { decisionKey, reason };
    const action = approve ? 'approve' : 'reject';
    return this.http.post<unknown>(
      `${this.baseUrl}/employees/${id}/location-transfers/${transferId}/${action}`, decision
    );
  }

  cancelEmployeeLocationTransfer(
    id: number, transferId: string, decisionKey: string, reason: string
  ): Observable<unknown> {
    return this.http.post<unknown>(
      `${this.baseUrl}/employees/${id}/location-transfers/${transferId}/cancel`,
      { decisionKey, reason }
    );
  }
}
