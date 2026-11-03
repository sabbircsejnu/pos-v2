import { percentToDiscountAmount, validateManualDiscount } from './pos-discount';

const line = (quantity: number, unitPrice: number, lineDiscount = 0) => ({ quantity, unitPrice, lineDiscount });

describe('pos-discount pre-validation', () => {
  const base = { canApply: true, canOverrideLimit: false, limitPercent: 10, invoiceDiscount: 0 };

  it('reports no manual discount when nothing is discounted', () => {
    expect(validateManualDiscount({ ...base, lines: [line(1, 100)] }).status).toBe('none');
  });

  it('refuses a manual discount without sales.discount.apply', () => {
    const check = validateManualDiscount({ ...base, canApply: false, lines: [line(1, 100, 5)] });
    expect(check.status).toBe('not_permitted');
  });

  it('allows a line discount within the limit and reports the effective percentage', () => {
    const check = validateManualDiscount({ ...base, lines: [line(2, 100, 20), line(1, 50)] });
    expect(check.status).toBe('ok');
    expect(check.scope).toBe('line');
    expect(check.effectivePercent).toBeCloseTo(10, 6);
  });

  it('uses the maximum line percentage against the amount before the manual discount', () => {
    const check = validateManualDiscount({ ...base, lines: [line(1, 100, 5), line(1, 200, 30)] });
    expect(check.status).toBe('over_limit');
    expect(check.effectivePercent).toBeCloseTo(15, 6);
  });

  it('blocks an invoice discount over the limit without the override permission', () => {
    const check = validateManualDiscount({ ...base, invoiceDiscount: 16, lines: [line(1, 100), line(1, 50)] });
    expect(check.status).toBe('over_limit');
    expect(check.scope).toBe('invoice');
    expect(check.effectivePercent).toBeCloseTo(10.6667, 3);
  });

  it('allows an invoice discount that is exactly the limit', () => {
    const check = validateManualDiscount({ ...base, invoiceDiscount: 15, lines: [line(1, 100), line(1, 50)] });
    expect(check.status).toBe('ok');
  });

  it('allows a discount over the limit when the user can override it', () => {
    const check = validateManualDiscount({ ...base, canOverrideLimit: true, invoiceDiscount: 25, lines: [line(1, 100)] });
    expect(check.status).toBe('override');
  });

  it('treats a limit of 0 as manual discounts not enabled unless the user can override', () => {
    const blocked = validateManualDiscount({ ...base, limitPercent: 0, lines: [line(1, 100, 1)] });
    expect(blocked.status).toBe('over_limit');
    const overridden = validateManualDiscount({ ...base, limitPercent: 0, canOverrideLimit: true, lines: [line(1, 100, 1)] });
    expect(overridden.status).toBe('override');
  });

  it('leaves the decision to the server while the limit is unknown', () => {
    const check = validateManualDiscount({ ...base, limitPercent: null, lines: [line(1, 100, 90)] });
    expect(check.status).toBe('ok');
  });

  it('accepts a discount exactly at the limit', () => {
    expect(validateManualDiscount({ ...base, lines: [line(1, 100, 10)] }).status).toBe('ok');
  });
});

describe('percentToDiscountAmount', () => {
  it('rounds half away from zero to cents', () => {
    expect(percentToDiscountAmount(200, 15, null, false)).toBe(30);
    expect(percentToDiscountAmount(33.35, 10, null, false)).toBe(3.34);
  });

  it('rounds down when rounding up would push a within-limit percentage over the limit', () => {
    expect(percentToDiscountAmount(33.35, 10, 10, false)).toBe(3.33);
  });

  it('does not cap a percentage above the limit (the pre-validation reports it instead)', () => {
    expect(percentToDiscountAmount(100, 12, 10, false)).toBe(12);
    expect(percentToDiscountAmount(33.35, 10, 10, true)).toBe(3.34);
  });
});
