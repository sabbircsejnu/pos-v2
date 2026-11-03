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
}

export type UpdateOutletRequest = CreateOutletRequest;

export interface OutletStats {
  id: number;
  name: string;
  userCount: number;
  salesCount: number;
  totalSales: number;
}
