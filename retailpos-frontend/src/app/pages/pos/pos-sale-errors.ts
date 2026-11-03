export type PosSaleErrorKind =
  | 'price_changed'
  | 'tax_mismatch'
  | 'discount_not_permitted'
  | 'discount_limit_exceeded'
  | 'terminal_required'
  | 'no_active_terminal'
  | 'invoice_prefix_missing';

export interface PosSaleError {
  code: string;
  kind: PosSaleErrorKind;
  message: string;
  /** The structured `data` of the API error body (price changes, `{ serverTax }`, discount limit details...). */
  data: unknown;
}

export const NO_ACTIVE_TERMINAL_MESSAGE =
  'No active terminal is configured for this outlet. Add or activate one in Outlet settings.';
export const TERMINAL_REQUIRED_MESSAGE = 'Select a POS terminal before completing the sale.';
export const INVOICE_PREFIX_MISSING_MESSAGE =
  'This outlet has no invoice prefix. Set one in Outlet settings before completing sales.';
export const DISCOUNT_NOT_PERMITTED_MESSAGE =
  'You do not have permission to apply manual discounts. Remove the discount and try again.';
export const DISCOUNTS_NOT_ENABLED_MESSAGE = 'Manual discounts are not enabled for this business.';
export const CART_UPDATED_MESSAGE =
  'The cart was updated with the current prices and tax. Please review it and complete the sale again.';

const KIND_BY_CODE: Record<string, PosSaleErrorKind> = {
  SALE_PRICE_CHANGED: 'price_changed',
  SALE_TAX_MISMATCH: 'tax_mismatch',
  SALE_DISCOUNT_NOT_PERMITTED: 'discount_not_permitted',
  SALE_DISCOUNT_LIMIT_EXCEEDED: 'discount_limit_exceeded',
  SALE_TERMINAL_REQUIRED: 'terminal_required',
  SALE_NO_ACTIVE_TERMINAL: 'no_active_terminal',
  SALE_OUTLET_INVOICE_PREFIX_MISSING: 'invoice_prefix_missing',
};

function formatPercent(value: number): string {
  return `${Number(value.toFixed(2))}%`;
}

export function discountLimitMessage(limitPercent: number | null | undefined, effectivePercent?: number | null): string {
  const limit = Number(limitPercent ?? 0);
  if (!Number.isFinite(limit) || limit <= 0) {
    return DISCOUNTS_NOT_ENABLED_MESSAGE;
  }

  const effective = Number(effectivePercent);
  const applied = Number.isFinite(effective) && effective > 0 ? `The discount (${formatPercent(effective)}) exceeds` : 'The discount exceeds';
  return `${applied} the manual discount limit of ${formatPercent(limit)}. Reduce the discount to continue.`;
}

/** Reads the structured API error body from the error shape produced by the HTTP error interceptor. */
export function readApiErrorBody(error: any): { errorCode?: string; data?: unknown } | null {
  const body = error?.originalError?.error ?? error?.error;
  return body && typeof body === 'object' ? body : null;
}

/** Maps the checkout error codes the POS handles specifically; returns null for any other error. */
export function mapPosSaleError(error: any): PosSaleError | null {
  const body = readApiErrorBody(error);
  const code = typeof body?.errorCode === 'string' ? body.errorCode : '';
  const kind = KIND_BY_CODE[code];
  if (!kind) {
    return null;
  }

  const data = body?.data;
  const details = (data && typeof data === 'object' ? data : {}) as Record<string, unknown>;
  let message: string;
  switch (kind) {
    case 'price_changed':
    case 'tax_mismatch':
      message = CART_UPDATED_MESSAGE;
      break;
    case 'discount_not_permitted':
      message = DISCOUNT_NOT_PERMITTED_MESSAGE;
      break;
    case 'discount_limit_exceeded':
      message = discountLimitMessage(
        readNumber(details['limitPercent'] ?? details['LimitPercent']),
        readNumber(details['effectivePercent'] ?? details['EffectivePercent']));
      break;
    case 'terminal_required':
      message = TERMINAL_REQUIRED_MESSAGE;
      break;
    case 'no_active_terminal':
      message = NO_ACTIVE_TERMINAL_MESSAGE;
      break;
    default:
      message = INVOICE_PREFIX_MISSING_MESSAGE;
      break;
  }

  return { code, kind, message, data };
}

function readNumber(value: unknown): number | null {
  const parsed = Number(value);
  return value === null || value === undefined || !Number.isFinite(parsed) ? null : parsed;
}
