export interface OutletTerminal {
  /** null for a row the user added in the form and has not saved yet. */
  id: number | null;
  name: string;
  code: string | null;
  isActive: boolean;
  hasHistory: boolean;
  hasOpenSession: boolean;
}

/** A terminal row sent on create/update: rows with an id edit that terminal, rows without one create it. */
export interface OutletTerminalInput {
  id?: number;
  name: string;
  isActive: boolean;
}

export interface Outlet {
  id: number;
  name: string;
  address: string;
  contactNumber?: string;
  managerId?: number;
  managerName?: string;
  invoicePrefix?: string | null;
  /** True once invoices were issued with the prefix; it can no longer change. */
  invoicePrefixLocked?: boolean;
  /** IANA time zone override; null/absent inherits the Business default. */
  timeZoneId?: string | null;
  invoicePrintBranchName?: string | null;
  binNumber?: string | null;
  mushak?: string | null;
  returnExchangePolicy?: string | null;
  vatLawNotification?: string | null;
  terminals?: OutletTerminal[];
  userCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface CreateOutletRequest {
  name: string;
  address: string;
  contactNumber?: string;
  managerId?: number;
  /** Omitted/null = unchanged. Server uppercases it; unique per Business. */
  invoicePrefix?: string | null;
  /** Omitted/null = unchanged, '' = clear the override, otherwise an IANA id. */
  timeZoneId?: string | null;
  /** Omitted/null = leave terminals untouched (required when the user lacks outlets.terminals.manage). */
  terminals?: OutletTerminalInput[] | null;
  /** Printed-invoice settings. Omitted/null = unchanged, '' = clear. */
  invoicePrintBranchName?: string | null;
  binNumber?: string | null;
  mushak?: string | null;
  returnExchangePolicy?: string | null;
  vatLawNotification?: string | null;
  /** Weekly off days and sales targets. Omitted = unchanged. */
  salesTargets?: OutletSalesTargetsInput;
}

export type UpdateOutletRequest = CreateOutletRequest;

export type SalesTargetPeriodType = 'weekly' | 'first_half' | 'second_half' | 'monthly';

export interface OutletSalesTargetDay {
  /** 0 = Sunday to 6 = Saturday. */
  dayOfWeek: number;
  isOffDay: boolean;
  targetAmount: number | null;
}

export interface OutletSalesTargetOverrideInput {
  periodType: SalesTargetPeriodType;
  periodFrom: string;
  /** null = Reset to Auto. */
  overrideAmount: number | null;
  reason?: string | null;
}

export interface OutletSalesTargetsInput {
  isEnabled: boolean;
  effectiveFrom: string;
  days: OutletSalesTargetDay[];
  overrides?: OutletSalesTargetOverrideInput[];
  /** The version this form loaded; null when it loaded none. */
  expectedVersionNo: number | null;
}

export interface OutletSalesTargetVersion {
  id: number;
  versionNo: number;
  effectiveFrom: string;
  isEnabled: boolean;
  days: OutletSalesTargetDay[];
  createdByUserId: number | null;
  createdAt: string;
}

export interface OutletSalesTargetPeriod {
  periodType: SalesTargetPeriodType;
  from: string;
  to: string;
  calculated: number | null;
  target: number | null;
  overrideAmount: number | null;
  isOverridden: boolean;
  reason: string | null;
  updatedByUserId: number | null;
  updatedAt: string | null;
  workingDays: number;
  offDays: number;
  notConfiguredDays: number;
}

export interface OutletSalesTargetConfiguration {
  outletId: number;
  today: string;
  latest: OutletSalesTargetVersion | null;
  current: OutletSalesTargetVersion | null;
  periods: OutletSalesTargetPeriod[];
  history: OutletSalesTargetVersion[];
}

export interface OutletStats {
  id: number;
  name: string;
  userCount: number;
  salesCount: number;
  totalSales: number;
}
