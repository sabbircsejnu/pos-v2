import {
  CART_UPDATED_MESSAGE,
  DISCOUNTS_NOT_ENABLED_MESSAGE,
  DISCOUNT_NOT_PERMITTED_MESSAGE,
  INVOICE_PREFIX_MISSING_MESSAGE,
  NO_ACTIVE_TERMINAL_MESSAGE,
  TERMINAL_REQUIRED_MESSAGE,
  mapPosSaleError,
} from './pos-sale-errors';

function apiError(status: number, errorCode: string | undefined, data?: unknown) {
  return { status, message: 'raw', originalError: { error: { errorCode, data } } };
}

describe('mapPosSaleError', () => {
  it('maps the terminal and invoice prefix codes to actionable messages', () => {
    expect(mapPosSaleError(apiError(400, 'SALE_TERMINAL_REQUIRED'))).toMatchObject({
      kind: 'terminal_required', message: TERMINAL_REQUIRED_MESSAGE,
    });
    expect(mapPosSaleError(apiError(409, 'SALE_NO_ACTIVE_TERMINAL'))?.message).toBe(
      'No active terminal is configured for this outlet. Add or activate one in Outlet settings.');
    expect(mapPosSaleError(apiError(409, 'SALE_NO_ACTIVE_TERMINAL'))?.message).toBe(NO_ACTIVE_TERMINAL_MESSAGE);
    const prefix = mapPosSaleError(apiError(409, 'SALE_OUTLET_INVOICE_PREFIX_MISSING'));
    expect(prefix?.kind).toBe('invoice_prefix_missing');
    expect(prefix?.message).toBe(INVOICE_PREFIX_MISSING_MESSAGE);
    expect(prefix?.message).toContain('Outlet settings');
  });

  it('maps the discount codes, including the limit details', () => {
    expect(mapPosSaleError(apiError(403, 'SALE_DISCOUNT_NOT_PERMITTED'))?.message).toBe(DISCOUNT_NOT_PERMITTED_MESSAGE);

    const over = mapPosSaleError(apiError(409, 'SALE_DISCOUNT_LIMIT_EXCEEDED', { limitPercent: 10, effectivePercent: 12.5, scope: 'line' }));
    expect(over?.kind).toBe('discount_limit_exceeded');
    expect(over?.message).toContain('12.5%');
    expect(over?.message).toContain('10%');

    const disabled = mapPosSaleError(apiError(409, 'SALE_DISCOUNT_LIMIT_EXCEEDED', { limitPercent: 0, effectivePercent: 5, scope: 'invoice' }));
    expect(disabled?.message).toBe(DISCOUNTS_NOT_ENABLED_MESSAGE);
  });

  it('maps price and tax conflicts and keeps the structured data', () => {
    const prices = [{ variantId: 1, currentUnitPrice: 9 }];
    const priceChanged = mapPosSaleError(apiError(409, 'SALE_PRICE_CHANGED', prices));
    expect(priceChanged?.kind).toBe('price_changed');
    expect(priceChanged?.data).toEqual(prices);
    expect(priceChanged?.message).toBe(CART_UPDATED_MESSAGE);

    const taxMismatch = mapPosSaleError(apiError(409, 'SALE_TAX_MISMATCH', { serverTax: 4.2 }));
    expect(taxMismatch?.kind).toBe('tax_mismatch');
    expect(taxMismatch?.data).toEqual({ serverTax: 4.2 });
  });

  it('ignores unknown codes and errors without a structured body', () => {
    expect(mapPosSaleError(apiError(409, 'SALE_SOMETHING_ELSE'))).toBeNull();
    expect(mapPosSaleError(apiError(500, undefined))).toBeNull();
    expect(mapPosSaleError({ status: 0, message: 'offline' })).toBeNull();
    expect(mapPosSaleError(null)).toBeNull();
  });
});
