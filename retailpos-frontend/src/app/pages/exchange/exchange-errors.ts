/** Error envelope fields the Exchange API sends (`ApiResponse.errorCode`, `data`). */
export interface ExchangeErrorInfo {
  status: number;
  code: string;
  message: string;
  data: unknown;
}

const MESSAGES: Record<string, string> = {
  EXCHANGE_NOT_CONFIGURED: 'Product exchange is not available: the business has not saved an exchange policy yet.',
  EXCHANGE_DISABLED: 'Product exchange is turned off in the business exchange policy.',
  EXCHANGE_SOURCE_NOT_FOUND: 'No invoice with that number was found. Check the number on the receipt.',
  EXCHANGE_SOURCE_INELIGIBLE: 'This invoice cannot be exchanged (it is voided, fully refunded or otherwise not eligible).',
  EXCHANGE_SOURCE_EVIDENCE_MISSING: 'One or more lines lack the original price or cost evidence, so they cannot be exchanged.',
  EXCHANGE_CUSTOMER_MISMATCH: 'Only invoices of the same registered customer can be combined in one exchange.',
  EXCHANGE_WALKIN_MULTI_INVOICE: 'Walk-in invoices cannot be combined: exchange a walk-in invoice on its own.',
  EXCHANGE_ITEM_MISMATCH: 'The returned item does not match the invoice line (product, variant or quantity).',
  EXCHANGE_CONDITION_NOT_SELLABLE: 'Every returned item must be in good, sellable condition.',
  EXCHANGE_QUANTITY_EXCEEDS_REMAINING: 'A returned quantity is more than what remains returnable on the invoice line.',
  EXCHANGE_APPROVAL_REQUIRED: 'A supervisor approval is required before this exchange can be confirmed.',
  EXCHANGE_APPROVAL_INVALID: 'The supervisor approval is no longer valid (expired, used or the exchange changed). Request it again.',
  EXCHANGE_APPROVAL_NOT_PERMITTED: 'The supervisor does not have the required permission or access to this outlet.',
  EXCHANGE_APPROVAL_LOCKED: 'Too many failed supervisor sign-ins. Wait 15 minutes and try again.',
  EXCHANGE_PHONE_VERIFICATION_UNAVAILABLE: 'Required phone verification is not available yet: customer phone numbers cannot currently be verified.',
  EXCHANGE_PHONE_VERIFICATION_FAILED: 'The phone number does not match the original sale\'s customer.',
  EXCHANGE_LOWER_PRICE_DISABLED: 'The replacements are worth less than the returned items, and the exchange policy does not allow lower-price exchanges.',
  EXCHANGE_REFUND_FORBIDDEN: 'You do not have permission to complete a refund exchange. A supervisor refund approval is needed.',
  EXCHANGE_SETTLEMENT_MISMATCH: 'The payment amounts do not match the amount to settle.',
  EXCHANGE_REFUND_EVIDENCE_REQUIRED: 'A card or mobile refund needs a reference and confirmation that it was completed outside the system.',
  EXCHANGE_CASH_INSUFFICIENT: 'The drawer does not hold enough cash for this refund. Choose card or mobile refund instead.',
  EXCHANGE_SESSION_REQUIRED: 'An active cash session is required for this terminal. Open one on the POS screen first.',
  EXCHANGE_STOCK_INSUFFICIENT: 'There is not enough sellable stock of a replacement item at this outlet.',
  EXCHANGE_STOCK_LOCATION_MISSING: 'This outlet has no stock location configured.',
  EXCHANGE_PAYMENT_ACCOUNT_MISSING: 'A payment account mapping is missing for the selected method. Ask an administrator to configure it.',
  EXCHANGE_ACCOUNTING_CONFIGURATION_MISSING: 'Accounting configuration is incomplete for exchanges. Ask an administrator to configure it.',
  EXCHANGE_ACCOUNTING_PERIOD_CLOSED: 'The accounting period for today\'s business date is closed. The exchange cannot be posted.',
  EXCHANGE_PREVIEW_STALE: 'Prices, stock or eligibility changed since the review. The exchange has been re-checked; review it again.',
  EXCHANGE_IDEMPOTENCY_MISMATCH: 'This request was already used with different details. Review the exchange and confirm again.',
  EXCHANGE_CONCURRENCY_CONFLICT: 'Another transaction changed these items at the same time. Try Confirm again.',
  EXCHANGE_REASON_REQUIRED: 'Choose an exchange reason (Other needs details).',
  EXCHANGE_OUTLET_PREFIX_MISSING: 'This outlet has no invoice prefix, so an exchange number cannot be issued.',
  EXCHANGE_REVERSAL_BLOCKED_DEPENDENCY: 'A replacement from this exchange has been exchanged again, so it cannot be reversed.',
  EXCHANGE_ALREADY_REVERSED: 'This exchange has already been reversed.',
  EXCHANGE_REVERSAL_STOCK_UNAVAILABLE: 'The returned goods are no longer available in sellable stock, so the exchange cannot be reversed.'
};

/**
 * Codes whose server message is more specific than the fixed text (it names the actual cause, e.g. an inactive
 * terminal rather than a missing cash session, or a supervisor who could not be verified rather than an expired token).
 */
const SERVER_MESSAGE_FIRST = new Set(['EXCHANGE_SESSION_REQUIRED', 'EXCHANGE_APPROVAL_INVALID', 'EXCHANGE_SOURCE_INELIGIBLE']);

/** Reads the error the HTTP error interceptor produced (or a raw HttpErrorResponse). */
export function readExchangeError(error: unknown): ExchangeErrorInfo {
  const source = (error ?? {}) as { status?: unknown; message?: unknown; originalError?: { error?: unknown }; error?: unknown };
  const rawBody = source.originalError?.error ?? source.error;
  const body = rawBody && typeof rawBody === 'object' ? rawBody as { errorCode?: unknown; message?: unknown; data?: unknown } : {};
  const code = typeof body.errorCode === 'string' ? body.errorCode : '';
  const serverMessage = typeof body.message === 'string' && body.message ? body.message
    : typeof source.message === 'string' ? source.message : '';
  const status = typeof source.status === 'number' ? source.status : 0;
  let message = (SERVER_MESSAGE_FIRST.has(code) && serverMessage) ? serverMessage : MESSAGES[code] ?? serverMessage;
  if (!message) {
    message = status === 0 ? 'Unable to reach the server. Check the connection and try again.'
      : status === 429 ? 'Too many attempts from this device. Wait a minute and try again.'
      : 'The request failed. Try again.';
  }
  return { status, code, message, data: body.data ?? null };
}

/** Approval types the server still requires, from `EXCHANGE_APPROVAL_REQUIRED` (`data.required`). */
export function requiredApprovalsFrom(data: unknown): string[] {
  if (data && typeof data === 'object') {
    const required = (data as { required?: unknown }).required;
    if (Array.isArray(required)) return required.filter((item): item is string => typeof item === 'string');
  }
  return [];
}
