/**
 * Client-side mirror of the server's manual-discount authorisation (SaleDiscountAuthorizer). It only gives the cashier
 * early feedback; the server stays authoritative and answers 403 SALE_DISCOUNT_NOT_PERMITTED or
 * 409 SALE_DISCOUNT_LIMIT_EXCEEDED when a request is not allowed.
 */
import { toCents } from './pos-tax';

export type ManualDiscountScope = 'line' | 'invoice';

export type ManualDiscountStatus =
  /** No manual discount is applied. */
  | 'none'
  | 'ok'
  /** Over the limit, but the user holds sales.discount.override_limit. */
  | 'override'
  | 'not_permitted'
  | 'over_limit';

export interface ManualDiscountLine {
  quantity: number;
  /** Server unit price (automatic rule discounts are already inside it and never count). */
  unitPrice: number;
  lineDiscount: number;
}

export interface ManualDiscountInput {
  lines: readonly ManualDiscountLine[];
  invoiceDiscount: number;
  canApply: boolean;
  canOverrideLimit: boolean;
  /** Business limit in percent; null while the Business settings are unknown (the server then decides). */
  limitPercent: number | null;
}

export interface ManualDiscountCheck {
  status: ManualDiscountStatus;
  scope: ManualDiscountScope | null;
  effectivePercent: number;
  limitPercent: number | null;
}

export function validateManualDiscount(input: ManualDiscountInput): ManualDiscountCheck {
  const lineDiscounts = input.lines.filter(line => toCents(line.lineDiscount) > 0);
  const invoiceDiscountCents = toCents(input.invoiceDiscount);

  if (lineDiscounts.length === 0 && invoiceDiscountCents <= 0) {
    return { status: 'none', scope: null, effectivePercent: 0, limitPercent: input.limitPercent };
  }

  let scope: ManualDiscountScope;
  let effectivePercent: number;
  if (invoiceDiscountCents > 0) {
    scope = 'invoice';
    const itemsSubtotalCents = input.lines.reduce(
      (sum, line) => sum + toCents(line.quantity * line.unitPrice) - toCents(line.lineDiscount), 0);
    effectivePercent = itemsSubtotalCents <= 0 ? 100 : (invoiceDiscountCents / itemsSubtotalCents) * 100;
  } else {
    scope = 'line';
    effectivePercent = Math.max(...lineDiscounts.map(line => {
      const grossCents = toCents(line.quantity * line.unitPrice);
      return grossCents <= 0 ? 100 : (toCents(line.lineDiscount) / grossCents) * 100;
    }));
  }

  const check = (status: ManualDiscountStatus): ManualDiscountCheck =>
    ({ status, scope, effectivePercent, limitPercent: input.limitPercent });

  if (!input.canApply) {
    return check('not_permitted');
  }

  if (input.limitPercent === null || effectivePercent <= input.limitPercent + 1e-9) {
    return check('ok');
  }

  return check(input.canOverrideLimit ? 'override' : 'over_limit');
}

/**
 * Converts a percentage into a discount amount (cents precision, half away from zero). When the cashier may not exceed
 * the limit and the percentage is within it, an amount that would round just above the limit is rounded down instead,
 * so typing exactly the limit is never rejected because of cent rounding.
 */
export function percentToDiscountAmount(
  baseAmount: number,
  percent: number,
  limitPercent: number | null,
  canOverrideLimit: boolean
): number {
  const baseCents = toCents(baseAmount);
  const exactCents = Number(((baseCents * percent) / 100).toPrecision(12));
  let amountCents = Math.round(exactCents);

  const capped = limitPercent !== null && !canOverrideLimit && percent <= limitPercent;
  if (capped && baseCents > 0 && amountCents * 100 > limitPercent * baseCents + 1e-9) {
    amountCents = Math.floor(exactCents);
  }

  return amountCents / 100;
}
