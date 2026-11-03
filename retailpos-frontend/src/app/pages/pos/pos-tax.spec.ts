import { allocateProportionally, calculateTax, effectiveLineTaxRate, roundMoney, toCents } from './pos-tax';

const sum = (values: number[]) => Math.round(values.reduce((total, value) => total + value, 0) * 100) / 100;

describe('pos-tax (port of the server checkout algorithm)', () => {
  describe('shared server vectors', () => {
    it('(a) exclusive: 2 x 100 at 10% gives tax 20 and total 220', () => {
      const result = calculateTax({
        lines: [{ quantity: 2, unitPrice: 100, lineDiscount: 0, taxRate: 10 }],
        invoiceDiscount: 0,
        pricesIncludeTax: false,
      });

      expect(result.tax).toBe(20);
      expect(result.total).toBe(220);
      expect(result.inclusive).toBe(false);
    });

    it('(b) inclusive: 1 x 110 at 10% gives tax 10 and total 110', () => {
      const result = calculateTax({
        lines: [{ quantity: 1, unitPrice: 110, lineDiscount: 0, taxRate: 10 }],
        invoiceDiscount: 0,
        pricesIncludeTax: true,
      });

      expect(result.tax).toBe(10);
      expect(result.total).toBe(110);
      expect(result.inclusive).toBe(true);
    });

    it('(c) invoice discount 100 over lines 400 and 600 at 10% gives tax 90 and total 990', () => {
      const result = calculateTax({
        lines: [
          { quantity: 1, unitPrice: 400, lineDiscount: 0, taxRate: 10 },
          { quantity: 1, unitPrice: 600, lineDiscount: 0, taxRate: 10 },
        ],
        invoiceDiscount: 100,
        pricesIncludeTax: false,
      });

      expect(result.lines.map(line => line.invoiceDiscountShare)).toEqual([40, 60]);
      expect(result.tax).toBe(90);
      expect(result.total).toBe(990);
      expect(sum(result.lines.map(line => line.tax))).toBe(90);
    });

    it('(d) mixed 600 at 10% and 400 zero-rated with invoice discount 100 gives tax 54 and total 954', () => {
      const result = calculateTax({
        lines: [
          { quantity: 1, unitPrice: 600, lineDiscount: 0, taxRate: 10 },
          { quantity: 1, unitPrice: 400, lineDiscount: 0, taxRate: effectiveLineTaxRate(10, 'zero_rated', true) },
        ],
        invoiceDiscount: 100,
        pricesIncludeTax: false,
      });

      expect(result.tax).toBe(54);
      expect(result.total).toBe(954);
      expect(result.lines[1].tax).toBe(0);
    });

    it('(e) inclusive mixed 110 at 10%, 105 at 5% and 20 exempt gives tax 15 and total 235', () => {
      const result = calculateTax({
        lines: [
          { quantity: 1, unitPrice: 110, lineDiscount: 0, taxRate: 10 },
          { quantity: 1, unitPrice: 105, lineDiscount: 0, taxRate: 5 },
          { quantity: 1, unitPrice: 20, lineDiscount: 0, taxRate: effectiveLineTaxRate(10, 'exempt', true) },
        ],
        invoiceDiscount: 0,
        pricesIncludeTax: true,
      });

      expect(result.lines.map(line => line.tax)).toEqual([10, 5, 0]);
      expect(result.tax).toBe(15);
      expect(result.total).toBe(235);
    });

    it('(f) rounding: 33.33, 33.33, 33.34 at 7.5% with discount 5.00 allocates exactly', () => {
      const result = calculateTax({
        lines: [
          { quantity: 1, unitPrice: 33.33, lineDiscount: 0, taxRate: 7.5 },
          { quantity: 1, unitPrice: 33.33, lineDiscount: 0, taxRate: 7.5 },
          { quantity: 1, unitPrice: 33.34, lineDiscount: 0, taxRate: 7.5 },
        ],
        invoiceDiscount: 5,
        pricesIncludeTax: false,
      });

      // 500 cents over 3333/3333/3334: floors 166/166/166, the two leftover cents go to the largest remainders.
      expect(result.lines.map(line => line.invoiceDiscountShare)).toEqual([1.67, 1.66, 1.67]);
      expect(sum(result.lines.map(line => line.invoiceDiscountShare))).toBe(5);
      // Group base 95.00 x 7.5% = 7.125 -> 7.13 (half away from zero), allocated to the lines.
      expect(result.tax).toBe(7.13);
      expect(sum(result.lines.map(line => line.tax))).toBe(result.tax);
      expect(result.total).toBe(102.13);
    });
  });

  describe('details', () => {
    it('charges no tax when every line rate is zero, even if prices include tax', () => {
      const result = calculateTax({
        lines: [{ quantity: 3, unitPrice: 10, lineDiscount: 0, taxRate: 0 }],
        invoiceDiscount: 0,
        pricesIncludeTax: true,
      });

      expect(result.tax).toBe(0);
      expect(result.total).toBe(30);
      expect(result.inclusive).toBe(false);
    });

    it('applies manual line discounts before tax', () => {
      const result = calculateTax({
        lines: [{ quantity: 2, unitPrice: 100, lineDiscount: 20, taxRate: 10 }],
        invoiceDiscount: 0,
        pricesIncludeTax: false,
      });

      expect(result.itemsSubtotal).toBe(180);
      expect(result.tax).toBe(18);
      expect(result.total).toBe(198);
    });

    it('rounds half away from zero and tolerates floating point noise', () => {
      expect(roundMoney(1.005)).toBe(1.01);
      expect(roundMoney(2.675)).toBe(2.68);
      expect(roundMoney(-1.005)).toBe(-1.01);
      expect(toCents(0.1 + 0.2)).toBe(30);
    });

    it('breaks allocation ties in favour of the lower index and always sums to the total', () => {
      expect(allocateProportionally(1, [1, 1, 1])).toEqual([1, 0, 0]);
      expect(allocateProportionally(100, [1, 1, 1])).toEqual([34, 33, 33]);
      expect(allocateProportionally(0, [5, 5])).toEqual([0, 0]);
    });

    it('resolves the effective line rate from the Business switch and the classification', () => {
      expect(effectiveLineTaxRate(10, 'standard', true)).toBe(10);
      expect(effectiveLineTaxRate(10, 'standard', false)).toBe(0);
      expect(effectiveLineTaxRate(10, 'zero_rated', true)).toBe(0);
      expect(effectiveLineTaxRate(10, 'exempt', true)).toBe(0);
      expect(effectiveLineTaxRate(undefined, undefined, true)).toBe(0);
      expect(effectiveLineTaxRate(250, 'standard', true)).toBe(100);
    });
  });
});
