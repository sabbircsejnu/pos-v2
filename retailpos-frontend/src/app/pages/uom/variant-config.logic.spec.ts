import { ResolvedUnit, SellingOption, UomUnit, VariantUomConfigView } from '../../models/uom.model';
import { ConfigValue, SellingValue, standardFactor, suggestOptionCode, toConfigInput, toConfigValue, validateConfig } from './variant-config.logic';

const unit = (id: number, code: string, dimension: UomUnit['dimension'], quantityPrecision = 0): UomUnit => ({
  id, code, name: code, symbol: code, dimension, quantityPrecision, isGlobal: true, isWholeUnit: quantityPrecision === 0, isActive: true, isInUse: false,
});
const PCS = unit(1, 'pcs', 'count');
const ML = unit(2, 'ml', 'volume', 2);
const LITRE = unit(3, 'L', 'volume', 3);
const JAR = unit(4, 'jar', 'packaging');
const KG = unit(5, 'kg', 'mass', 3);
const UNITS = [PCS, ML, LITRE, JAR, KG];

const pack = (overrides: Partial<SellingValue> = {}): SellingValue => ({
  code: '12ml', label: '12 ml', unitId: null, baseQuantity: 12, price: 100, isDefault: false,
  sealedPackCode: '', isPreparedEligible: false, components: [], applyAutomaticPricingRules: false, ...overrides,
});
const config = (overrides: Partial<ConfigValue> = {}): ConfigValue => ({
  baseUnitId: ML.id, packStateTrackingEnabled: false, repackingEnabled: false,
  purchaseOptions: [], packs: [pack()], customs: [], ...overrides,
});
const jar = { code: 'jar500', label: '500 ml Jar', unitId: JAR.id, size: 500, factorDenominator: 1, isDefault: true, isSealedPack: false };

describe('toConfigValue', () => {
  it('starts a variant without a configuration from plain pcs', () => {
    const view: VariantUomConfigView = { variantId: 7, hasStockActivity: false, current: null, history: [] };
    expect(toConfigValue(view, UNITS)).toEqual({
      baseUnitId: PCS.id, packStateTrackingEnabled: false, repackingEnabled: false, purchaseOptions: [], packs: [], customs: [],
    });
  });
});

const ML_RESOLVED: ResolvedUnit = { id: ML.id, code: 'ml', symbol: 'ml', dimension: 'volume', precision: 2, isWhole: false };

describe('toConfigValue promotion flag', () => {
  const option = (kind: 'pack' | 'custom', applyAutomaticPricingRules?: boolean): SellingOption => ({
    id: 1, kind, code: kind, label: kind, unit: kind === 'custom' ? ML_RESOLVED : null,
    baseQuantity: kind === 'pack' ? 12 : null, factorNumerator: 1, factorDenominator: 1, price: 10, isDefault: false,
    sealedPackCode: null, isPreparedEligible: false, components: [], applyAutomaticPricingRules,
  });
  const view = (options: SellingOption[]): VariantUomConfigView => ({
    variantId: 7, hasStockActivity: false, history: [],
    current: { configId: 1, version: 1, variantId: 7, baseUnit: ML_RESOLVED, packStateTrackingEnabled: false, repackingEnabled: false, purchaseOptions: [], sellingOptions: options },
  });

  it('loads the saved value', () => {
    const value = toConfigValue(view([option('pack', true), option('custom', false)]), UNITS);
    expect(value.packs[0].applyAutomaticPricingRules).toBe(true);
    expect(value.customs[0].applyAutomaticPricingRules).toBe(false);
  });

  it('falls back to the server default when the value is missing', () => {
    const value = toConfigValue(view([option('pack'), option('custom')]), UNITS);
    expect(value.packs[0].applyAutomaticPricingRules).toBe(false);
    expect(value.customs[0].applyAutomaticPricingRules).toBe(true);
  });
});

describe('validateConfig', () => {
  it('accepts plain pcs with no options, so a pcs item needs nothing added', () => {
    expect(validateConfig(config({ baseUnitId: PCS.id, packs: [] }), UNITS)).toEqual([]);
  });

  it('requires a selling option for a measured base unit', () => {
    expect(validateConfig(config({ packs: [] }), UNITS)).toContain('An item measured in a unit other than pcs needs at least one selling option.');
  });

  it('rejects a packaging name as a base unit', () => {
    expect(validateConfig(config({ baseUnitId: JAR.id }), UNITS).join()).toContain('cannot be a base unit');
  });

  it('requires exactly one default purchase option', () => {
    const second = { ...jar, code: 'jar1000', size: 1000, isDefault: false };
    expect(validateConfig(config({ purchaseOptions: [{ ...jar, isDefault: false }, second] }), UNITS)).toContain('Choose exactly one default purchase unit.');
    expect(validateConfig(config({ purchaseOptions: [jar, { ...second, isDefault: true }] }), UNITS)).toContain('Choose exactly one default purchase unit.');
    expect(validateConfig(config({ purchaseOptions: [jar, second] }), UNITS)).toEqual([]);
  });

  it('allows at most one default across packs and custom quantities', () => {
    const errors = validateConfig(config({
      packs: [pack({ isDefault: true })],
      customs: [pack({ code: 'custom', unitId: ML.id, baseQuantity: null, isDefault: true })],
    }), UNITS);
    expect(errors).toContain('Choose at most one default selling option.');
  });

  it('needs a size for a packaging unit, at the precision of the base unit', () => {
    expect(validateConfig(config({ purchaseOptions: [{ ...jar, size: null }] }), UNITS).join()).toContain('how many ml one jar holds');
    expect(validateConfig(config({ purchaseOptions: [{ ...jar, size: 1.234 }] }), UNITS).join()).toContain('precision');
  });

  it('rejects a physical unit of another dimension', () => {
    const errors = validateConfig(config({ purchaseOptions: [{ ...jar, unitId: KG.id }] }), UNITS);
    expect(errors.join()).toContain('kg has no standard conversion to ml');
  });

  it('rejects a negative price and more than two decimals', () => {
    expect(validateConfig(config({ packs: [pack({ price: -1 })] }), UNITS).join()).toContain('price of zero or more');
    expect(validateConfig(config({ packs: [pack({ price: 1.234 })] }), UNITS).join()).toContain('2 decimal places');
  });

  it('rejects a pack quantity the base unit cannot hold', () => {
    expect(validateConfig(config({ packs: [pack({ baseQuantity: 1.234 })] }), UNITS).join()).toContain('precision');
    expect(validateConfig(config({ packs: [pack({ baseQuantity: 0 })] }), UNITS).join()).toContain('greater than zero');
  });

  it('rejects a duplicate selling code in any letter case', () => {
    const errors = validateConfig(config({ packs: [pack(), pack({ code: '12ML' })] }), UNITS);
    expect(errors.join()).toContain("'12ml' is used twice");
  });

  it('ties sealed, prepared and repacking to pack tracking', () => {
    expect(validateConfig(config({ purchaseOptions: [{ ...jar, isSealedPack: true }] }), UNITS).join()).toContain('needs pack tracking');
    expect(validateConfig(config({ packs: [pack({ isPreparedEligible: true })] }), UNITS).join()).toContain('needs pack tracking');
    expect(validateConfig(config({ repackingEnabled: true, packStateTrackingEnabled: true }), UNITS).join()).toContain('Repacking needs');
    expect(validateConfig(config({
      repackingEnabled: true, packStateTrackingEnabled: true, packs: [pack({ isPreparedEligible: true })],
    }), UNITS)).toEqual([]);
  });

  it('rejects a sealed pack code that is not a sealed purchase unit', () => {
    expect(validateConfig(config({ packStateTrackingEnabled: true, packs: [pack({ sealedPackCode: 'jar500' })] }), UNITS).join())
      .toContain('not a sealed purchase unit');
  });

  it('rejects a zero packaging quantity', () => {
    const components = [{ componentVariantId: 9, name: 'Bottle', quantity: 0, isRequired: true }];
    expect(validateConfig(config({ packs: [pack({ components })] }), UNITS).join()).toContain('quantity of Bottle must be greater than zero');
  });
});

describe('toConfigInput', () => {
  it('sends packs and custom quantities as selling options with server field names', () => {
    const input = toConfigInput(config({
      purchaseOptions: [{ ...jar, code: ' jar500 ', label: '' }],
      packs: [pack({ code: ' 12ml ', components: [{ componentVariantId: 9, name: 'Bottle', quantity: 1, isRequired: true }] })],
      customs: [pack({
        code: 'custom', label: '', unitId: ML.id, baseQuantity: 5, sealedPackCode: 'x', isPreparedEligible: true, price: 5, applyAutomaticPricingRules: true,
      })],
    }));
    expect(input.purchaseOptions[0]).toEqual({
      code: 'jar500', unitId: JAR.id, label: 'jar500', factorNumerator: 500, factorDenominator: 1, isDefault: true, isSealedPack: false,
    });
    expect(input.sellingOptions[0]).toEqual({
      kind: 'pack', code: '12ml', label: '12 ml', unitId: null, baseQuantity: 12, price: 100, isDefault: false,
      sealedPackCode: null, isPreparedEligible: false, components: [{ componentVariantId: 9, quantity: 1, isRequired: true }],
      applyAutomaticPricingRules: false,
    });
    expect(input.sellingOptions[1]).toEqual({
      kind: 'custom', code: 'custom', label: 'custom', unitId: ML.id, baseQuantity: null, price: 5, isDefault: false,
      sealedPackCode: null, isPreparedEligible: false, components: [], applyAutomaticPricingRules: true,
    });
  });

  it('always sends the promotion flag explicitly, including false', () => {
    const input = toConfigInput(config({ packs: [pack({ applyAutomaticPricingRules: false })] }));
    expect(input.sellingOptions[0].applyAutomaticPricingRules).toBe(false);
    expect('applyAutomaticPricingRules' in input.sellingOptions[0]).toBe(true);
  });
});

describe('helpers', () => {
  const conversions = [{ fromUnitId: LITRE.id, fromSymbol: 'L', toUnitId: ML.id, toSymbol: 'ml', numerator: 1000, denominator: 1 }];

  it('finds a standard factor in either direction', () => {
    expect(standardFactor(LITRE, ML, conversions)).toBe(1000);
    expect(standardFactor(ML, LITRE, conversions)).toBe(0.001);
    expect(standardFactor(ML, ML, conversions)).toBe(1);
    expect(standardFactor(KG, ML, conversions)).toBeNull();
  });

  it('suggests a code from a label', () => {
    expect(suggestOptionCode(' 500 ml Jar! ')).toBe('500-ml-jar');
  });
});
