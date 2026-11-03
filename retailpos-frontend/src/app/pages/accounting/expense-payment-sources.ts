import { DropdownOption } from '../../components/searchable-dropdown/searchable-dropdown.component';

/** The payment sources the server accepts (`ExpensePaymentSources`); no other source type exists. */
export type ExpensePaymentSource = 'bank' | 'petty_cash' | 'pos_drawer';

export interface ExpensePaymentSourceOption extends DropdownOption {
  code: ExpensePaymentSource;
  description: string;
}

export const EXPENSE_PAYMENT_SOURCES: readonly ExpensePaymentSourceOption[] = [
  { id: 1, code: 'bank', name: 'Bank / other account', description: 'Paid outside the POS drawer; the drawer is not affected.' },
  { id: 2, code: 'petty_cash', name: 'Petty cash', description: 'Needs independent approval; the POS drawer is not affected.' },
  { id: 3, code: 'pos_drawer', name: 'POS cash drawer', description: 'Needs approval and an outlet. Cash leaves the drawer only when the payout is recorded at a terminal with an open session.' },
];

export function paymentSourceOption(code?: string | null): ExpensePaymentSourceOption {
  return EXPENSE_PAYMENT_SOURCES.find(option => option.code === code) ?? EXPENSE_PAYMENT_SOURCES[0];
}

export function paymentSourceLabel(code?: string | null): string {
  return code ? paymentSourceOption(code).name : 'Bank / other account (legacy)';
}

/**
 * Payout state of an expense, kept separate from its approval status: only an approved POS-drawer expense can be paid
 * out, and it is "paid" only once the drawer disbursement is recorded.
 */
export function payoutState(expense: { paymentSource?: string | null; status: string; disbursedAtUtc?: string | null; disbursedCashSessionId?: number | null }):
  'not_applicable' | 'awaiting_approval' | 'awaiting_payout' | 'paid' {
  if (expense.paymentSource !== 'pos_drawer') return 'not_applicable';
  if (expense.disbursedAtUtc || expense.disbursedCashSessionId) return 'paid';
  return expense.status?.toLowerCase() === 'approved' ? 'awaiting_payout' : 'awaiting_approval';
}
