export type AuditActionType =
  | 'Create' | 'Update' | 'Delete' | 'Approve' | 'Cancel' | 'Submit' | 'Reject'
  | 'Login' | 'Logout' | 'RoleSwitch' | 'Export' | 'Import'
  | 'Payment' | 'Adjustment' | 'PermissionChange' | 'Reverse'
  | 'Hold' | 'Resume' | 'Print';

export type AuditSource = 'UI' | 'API' | 'SystemJob' | 'Import' | 'Integration';

export type AuditModule =
  | 'Sales' | 'Purchase' | 'Inventory' | 'Accounts'
  | 'Admin' | 'Settings' | 'Reports' | 'Auth' | 'System';

export type AuditStatus = 'Success' | 'Failed';

export interface AuditEventListItem {
  id: number;
  createdAt: string;
  userName: string | null;
  actingAsRole: string | null;
  actionType: AuditActionType;
  actionSummary: string;
  module: AuditModule;
  source: AuditSource;
  primaryEntityType: string | null;
  primaryEntityId: string | null;
  affectedEntitiesCount: number;
  status: AuditStatus;
  correlationId: string;
}

export interface AuditEventListResponse {
  items: AuditEventListItem[];
  total: number;
  page: number;
  pageSize: number;
}

export interface AuditFieldChange {
  fieldName: string;
  displayLabel: string | null;
  oldValue: unknown;
  newValue: unknown;
  oldDisplayValue: string | null;
  newDisplayValue: string | null;
  referenceEntityType: string | null;
  isReferenceField: boolean;
  redacted: boolean;
}

export interface AuditAffectedEntity {
  id: number;
  entityType: string;
  entityId: string;
  operationType: 'Insert' | 'Update' | 'Delete';
  fieldsChangedCount: number;
  isInternalOperation: boolean;
  internalOperationName: string | null;
  fieldChanges: AuditFieldChange[];
}

export interface AuditEventDetails extends AuditEventListItem {
  outletName: string | null;
  realUserName: string | null;
  ipAddress: string | null;
  rawIp: string | null;
  localMachineIp: string | null;
  isLocalRequest: boolean;
  device: string | null;
  browser: string | null;
  os: string | null;
  requestMethod: string | null;
  requestPath: string | null;
  errorMessage: string | null;
  modulesInvolved: AuditModule[];
  affectedEntities: AuditAffectedEntity[];
}

export interface AuditListFilter {
  search?: string;
  fromDate?: string;
  toDate?: string;
  userId?: number;
  actionType?: string[];
  module?: string[];
  source?: string[];
  outletId?: number;
  status?: AuditStatus;
  page?: number;
  pageSize?: number;
}

export interface AuditExportRequest extends AuditListFilter {
  format: 'csv' | 'xlsx';
}
