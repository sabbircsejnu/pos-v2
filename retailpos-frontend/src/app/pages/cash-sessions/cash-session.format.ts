import { CashSessionState } from '../../services/cash-session.service';
import { UiBadgeVariant } from '../../components/shared/ui';

export function stateLabel(state: CashSessionState | string | undefined): string {
  switch (state) {
    case 'open': return 'Open';
    case 'overdue': return 'Overdue';
    case 'closed': return 'Closed';
    default: return state ?? '';
  }
}

/** Status is always conveyed by text and icon (via the badge), never by colour alone. */
export function stateVariant(state: CashSessionState | string | undefined): UiBadgeVariant {
  switch (state) {
    case 'open': return 'success';
    case 'overdue': return 'danger';
    default: return 'neutral';
  }
}

export function varianceVariant(variance: number | null | undefined): UiBadgeVariant {
  if (variance === null || variance === undefined || variance === 0) return 'neutral';
  return variance > 0 ? 'info' : 'danger';
}

export function formatElapsed(totalSeconds: number): string {
  const seconds = Math.max(0, Math.floor(totalSeconds));
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  return `${h}h ${String(m).padStart(2, '0')}m`;
}

export function movementLabel(type: string): string {
  switch (type) {
    case 'cash_refund': return 'Cash refund paid';
    case 'drawer_expense': return 'Drawer expense paid';
    case 'drawer_expense_reversal': return 'Drawer expense reversed';
    case 'correction': return 'Approved correction';
    default: return type;
  }
}

export function handoverMethodLabel(method: string): string {
  switch (method) {
    case 'cash_to_manager': return 'Cash to manager';
    case 'cash_to_owner': return 'Cash to owner';
    case 'bank_deposit': return 'Bank deposit';
    default: return method;
  }
}

export function handoverStatusLabel(status: string): string {
  switch (status) {
    case 'pending_bank_verification': return 'Pending bank verification';
    case 'handed': return 'Handed over';
    case 'confirmed': return 'Confirmed';
    default: return status;
  }
}

/** Dates arrive as `yyyy-MM-dd` calendar dates (BusinessDate) or UTC instants; both are shown as sent. */
export function isOutletOptionList(value: unknown): value is { id: number; name: string }[] {
  return Array.isArray(value);
}
