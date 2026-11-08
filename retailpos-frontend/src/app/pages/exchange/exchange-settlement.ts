import { ExchangeSettlementDirection, ExchangeSettlementInput, ExchangeSettlementMethod } from '../../models/exchange.model';

/** The cashier's settlement entry. Amounts are only checked against the server's settlement amount, never derived. */
export interface SettlementDraft {
  cash: number | null;
  card: number | null;
  mobile: number | null;
  tendered: number | null;
  refundMethod: ExchangeSettlementMethod | null;
  reference: string;
  refundConfirmed: boolean;
}

export const EMPTY_SETTLEMENT: SettlementDraft = {
  cash: null, card: null, mobile: null, tendered: null, refundMethod: null, reference: '', refundConfirmed: false
};

/** A collection defaults to the whole amount in cash, like the POS pay panel. */
export function defaultSettlement(direction: ExchangeSettlementDirection, amount: number): SettlementDraft {
  return direction === 'collect' ? { ...EMPTY_SETTLEMENT, cash: amount } : { ...EMPTY_SETTLEMENT };
}

export const MAX_REFERENCE_LENGTH = 100;

function cents(value: number | null | undefined): number {
  const number = Number(value ?? 0);
  return Number.isFinite(number) ? Math.round(number * 100) : NaN;
}

/** Why the settlement cannot be sent yet; empty when it can. */
export function settlementError(direction: ExchangeSettlementDirection, amount: number, draft: SettlementDraft): string {
  if (direction === 'none') return '';
  if (direction === 'collect') {
    const parts = [draft.cash, draft.card, draft.mobile].map(cents);
    if (parts.some(part => Number.isNaN(part) || part < 0)) return 'Enter valid, non-negative payment amounts.';
    const total = parts.reduce((sum, part) => sum + part, 0);
    if (total !== cents(amount)) return 'The payment amounts must add up exactly to the amount to collect.';
    const cash = cents(draft.cash);
    if (cash > 0 && (draft.tendered === null || cents(draft.tendered) < cash)) {
      return 'Enter the cash tendered (at least the cash amount).';
    }
    return '';
  }
  if (!draft.refundMethod) return 'Choose the refund method.';
  if (draft.refundMethod !== 'cash') {
    const reference = draft.reference.trim();
    if (!reference) return 'Enter the card or mobile refund reference.';
    if (reference.length > MAX_REFERENCE_LENGTH) return `The reference can have at most ${MAX_REFERENCE_LENGTH} characters.`;
    if (!draft.refundConfirmed) return 'Confirm that the refund was completed outside the system.';
  }
  return '';
}

/** The settlement lines sent to the server. Only methods with an amount are included. */
export function buildSettlements(
  direction: ExchangeSettlementDirection,
  amount: number,
  draft: SettlementDraft
): ExchangeSettlementInput[] {
  if (direction === 'none') return [];
  if (direction === 'collect') {
    const lines: ExchangeSettlementInput[] = [];
    if (cents(draft.cash) > 0) lines.push({ direction: 'collect', method: 'cash', amount: Number(draft.cash), tendered: draft.tendered });
    if (cents(draft.card) > 0) lines.push({ direction: 'collect', method: 'card', amount: Number(draft.card) });
    if (cents(draft.mobile) > 0) lines.push({ direction: 'collect', method: 'mobile', amount: Number(draft.mobile) });
    return lines;
  }
  if (!draft.refundMethod) return [];
  if (draft.refundMethod === 'cash') return [{ direction: 'refund', method: 'cash', amount }];
  return [{
    direction: 'refund',
    method: draft.refundMethod,
    amount,
    externalReference: draft.reference.trim(),
    externalRefundConfirmed: draft.refundConfirmed
  }];
}

/** Change to hand back, for display only; the server records the authoritative change. */
export function displayChange(draft: SettlementDraft): number {
  const change = cents(draft.tendered) - cents(draft.cash);
  return Number.isNaN(change) || change <= 0 ? 0 : change / 100;
}
