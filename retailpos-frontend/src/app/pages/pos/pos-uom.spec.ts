import { SellingOption, VariantUomConfig } from '../../models/uom.model';
import {
  allowedForms,
  canChoosePackaging,
  cartLineKey,
  cartPriceRequestItem,
  expectedLineGrossField,
  lineNeedsRepick,
  priceRequestMatches,
  sellingBaseQuantity,
  sellingPrecision,
  uomLineFields
} from './pos-uom';
import { CartItem } from '../../models/sale.model';

const unit = { id: 1, code: 'ml', symbol: 'ml', dimension: 'volume' as const, precision: 1, isWhole: false };

const option = (over: Partial<SellingOption> = {}): SellingOption => ({
  id: 10,
  kind: 'pack',
  code: 'P12',
  label: '12 ml bottle',
  unit: null,
  baseQuantity: 12,
  factorNumerator: 1,
  factorDenominator: 1,
  price: 500,
  isDefault: true,
  sealedPackCode: null,
  isPreparedEligible: false,
  components: [],
  ...over,
});

const config = (over: Partial<VariantUomConfig> = {}): VariantUomConfig => ({
  configId: 77,
  version: 1,
  variantId: 5,
  baseUnit: unit,
  packStateTrackingEnabled: false,
  repackingEnabled: false,
  purchaseOptions: [],
  sellingOptions: [option()],
  ...over,
});

describe('POS unit helpers', () => {
  it('keys two options of one variant separately and keeps a pcs line keyed by its variant', () => {
    expect(cartLineKey({ variantId: 5 })).toBe('5');
    expect(cartLineKey({ variantId: 5, sellingOptionId: 10 })).not.toBe(cartLineKey({ variantId: 5, sellingOptionId: 11 }));
    expect(cartLineKey({ variantId: 5, sellingOptionId: 10, includePackaging: true }))
      .not.toBe(cartLineKey({ variantId: 5, sellingOptionId: 10 }));
  });

  it('converts a pack count and a measured quantity to base', () => {
    expect(sellingBaseQuantity(option(), 3)).toBe(36);
    expect(sellingBaseQuantity(option({ kind: 'custom', baseQuantity: null, factorNumerator: 1000, factorDenominator: 1 }), 0.25)).toBe(250);
  });

  it('makes a pack count whole and a custom quantity follow its unit', () => {
    expect(sellingPrecision(option(), config())).toBe(0);
    expect(sellingPrecision(option({ kind: 'custom', unit }), config())).toBe(1);
  });

  it('offers only the stock forms the server accepts', () => {
    expect(allowedForms(option(), config())).toEqual([]);
    const tracked = config({ packStateTrackingEnabled: true });
    expect(allowedForms(option(), tracked)).toEqual(['loose']);
    expect(allowedForms(option({ sealedPackCode: 'B100' }), tracked)).toEqual(['sealed']);
    expect(allowedForms(option({ isPreparedEligible: true }), tracked)).toEqual(['loose', 'prepared']);
  });

  it('offers optional packaging only for stock filled at the counter', () => {
    const packaged = option({ components: [{ variantId: 9, quantity: 1, isRequired: false }] });
    expect(canChoosePackaging(packaged, undefined)).toBe(true);
    expect(canChoosePackaging(packaged, 'loose')).toBe(true);
    expect(canChoosePackaging(packaged, 'sealed')).toBe(false);
    expect(canChoosePackaging(option(), undefined)).toBe(false);
  });

  it('adds nothing to the request of a pcs line', () => {
    expect(uomLineFields({})).toEqual({});
    expect(uomLineFields({ sellingOptionId: 10, expectedConfigVersionId: 77 })).toEqual({
      sellingOptionId: 10,
      expectedConfigVersionId: 77,
      fulfilmentState: undefined,
      includePackaging: false,
    });
  });

  it('flags a line only when the server would refuse its units', () => {
    const current = { sellingOptionId: 10, expectedConfigVersionId: 77 };
    expect(lineNeedsRepick(current, undefined)).toBe(false);
    expect(lineNeedsRepick({}, null)).toBe(false);
    expect(lineNeedsRepick(current, null)).toBe(true);
    expect(lineNeedsRepick(current, config())).toBe(false);
    expect(lineNeedsRepick({ ...current, expectedConfigVersionId: 76 }, config())).toBe(true);
    expect(lineNeedsRepick({ ...current, sellingOptionId: 99 }, config())).toBe(true);
    expect(lineNeedsRepick({}, config())).toBe(true);
    expect(lineNeedsRepick({}, config({ baseUnit: { ...unit, code: 'pcs' } }))).toBe(false);
  });
});

describe('option line pricing helpers', () => {
  const line = (over: Partial<CartItem> = {}): CartItem => ({
    variantId: 5, productName: 'Attar', variantSku: 'ATR', quantity: 2, unitPrice: 500, discountAmount: 0, subtotal: 1000, ...over,
  });

  it('keeps the pcs cart-prices request unchanged', () => {
    expect(cartPriceRequestItem(line(), 3)).toEqual({ productVariantId: 5, quantity: 2, outletId: 3 });
  });

  it('sends an option line with quantity 1 and the entered quantity', () => {
    expect(cartPriceRequestItem(line({ sellingOptionId: 12, quantity: 2.5 }), 3))
      .toEqual({ productVariantId: 5, quantity: 1, outletId: 3, sellingOptionId: 12, enteredQuantity: 2.5 });
  });

  it('matches a result only to the line it was requested for', () => {
    const optionLine = line({ sellingOptionId: 12, quantity: 2.5 });
    const requested = cartPriceRequestItem(optionLine, 3);
    expect(priceRequestMatches(requested, optionLine)).toBe(true);
    expect(priceRequestMatches(requested, { ...optionLine, quantity: 3 })).toBe(false);
    expect(priceRequestMatches(requested, { ...optionLine, sellingOptionId: 11 })).toBe(false);
    expect(priceRequestMatches(requested, line())).toBe(false);
    expect(priceRequestMatches(requested, undefined)).toBe(false);
    expect(priceRequestMatches(cartPriceRequestItem(line(), 3), line({ quantity: 7 }))).toBe(true);
  });

  it('adds expectedLineGross only to a priced option line', () => {
    expect(expectedLineGrossField(line({ lineGross: 900 }))).toEqual({});
    expect(expectedLineGrossField(line({ sellingOptionId: 10 }))).toEqual({});
    expect(expectedLineGrossField(line({ sellingOptionId: 10, lineGross: 900 }))).toEqual({ expectedLineGross: 900 });
  });
});
