import { coverageValueLabel, stockValueLabel, valuationCoverageText } from './owner-dashboard.logic';
import { StockValuationCoverage } from '../../../models/owner-dashboard.model';

import {
  buildScale,
  bucketLabel,
  isIsoDateOnly,
  parseLocation,
  resolvePreset,
  scalePercent,
  shortDate,
  validateRange
} from './owner-dashboard.logic';

describe('owner dashboard logic', () => {
  describe('resolvePreset', () => {
    it('resolves every preset from the server business date', () => {
      const today = '2026-11-06';
      expect(resolvePreset('today', today)).toEqual({ from: today, to: today });
      expect(resolvePreset('yesterday', today)).toEqual({ from: '2026-11-05', to: '2026-11-05' });
      expect(resolvePreset('last7', today)).toEqual({ from: '2026-10-31', to: today });
      expect(resolvePreset('last30', today)).toEqual({ from: '2026-10-08', to: today });
      expect(resolvePreset('thisMonth', today)).toEqual({ from: '2026-11-01', to: today });
      expect(resolvePreset('lastMonth', today)).toEqual({ from: '2026-10-01', to: '2026-10-31' });
    });

    it('crosses the year boundary', () => {
      expect(resolvePreset('lastMonth', '2027-01-15')).toEqual({ from: '2026-12-01', to: '2026-12-31' });
      expect(resolvePreset('yesterday', '2027-01-01')).toEqual({ from: '2026-12-31', to: '2026-12-31' });
    });
  });

  describe('validateRange', () => {
    it('accepts a valid inclusive range', () => {
      expect(validateRange('2026-11-06', '2026-11-06')).toBeNull();
    });

    it('rejects From after To', () => {
      expect(validateRange('2026-11-07', '2026-11-06')).toContain('on or before');
    });

    it('rejects missing and impossible dates', () => {
      expect(validateRange('', '2026-11-06')).toContain('both');
      expect(validateRange('2026-02-31', '2026-03-01')).toContain('both');
    });

    it('rejects a range over the limit', () => {
      expect(validateRange('2025-01-01', '2026-01-01')).toBeNull();
      expect(validateRange('2025-01-01', '2026-01-02')).toContain('366');
    });
  });

  it('accepts only real ISO dates, so a deep link cannot inject a value', () => {
    expect(isIsoDateOnly('2026-11-06')).toBe(true);
    expect(isIsoDateOnly('06/11/2026')).toBe(false);
    expect(isIsoDateOnly('2026-13-01')).toBe(false);
    expect(isIsoDateOnly(null)).toBe(false);
  });

  it('parses only well-formed locations', () => {
    expect(parseLocation('outlet:5')).toEqual({ type: 'outlet', id: 5 });
    expect(parseLocation('warehouse:9')).toEqual({ type: 'warehouse', id: 9 });
    expect(parseLocation('')).toBeNull();
    expect(parseLocation('business:1')).toBeNull();
    expect(parseLocation('outlet:1 OR 1=1')).toBeNull();
  });

  describe('buildScale', () => {
    it('always includes zero and rounds the top up', () => {
      const scale = buildScale([120, 870]);
      expect(scale.min).toBe(0);
      expect(scale.max).toBeGreaterThanOrEqual(870);
      expect(scale.ticks).toContain(0);
    });

    it('extends below zero for a refund-only day', () => {
      const scale = buildScale([1800, 100, -100]);
      expect(scale.min).toBeLessThan(0);
      expect(scale.ticks).toContain(0);
      expect(scalePercent(0, scale)).toBeGreaterThan(0);
    });

    it('does not divide by zero when every value is zero', () => {
      const scale = buildScale([0, 0]);
      expect(scale.max).toBeGreaterThan(scale.min);
      expect(Number.isFinite(scalePercent(0, scale))).toBe(true);
    });
  });

  it('labels dates unambiguously', () => {
    expect(shortDate('2026-11-06')).toBe('06 Nov');
    expect(bucketLabel('2026-11-06', '2026-11-06')).toBe('06 Nov');
    expect(bucketLabel('2026-11-01', '2026-11-07')).toBe('01 Nov – 07 Nov');
  });
});

describe('stock valuation labels', () => {
  const format = (amount: number) => `BDT ${amount}`;
  const coverage = (overrides: Partial<StockValuationCoverage>): StockValuationCoverage => ({
    valuedSubtotal: 500,
    status: 'valued',
    valuedRecords: 1,
    valuedQuantity: 10,
    zeroCostRecords: 0,
    zeroCostQuantity: 0,
    unvaluedRecords: 0,
    unvaluedQuantity: 0,
    ...overrides
  });

  it('labels a subtotal for how complete it is', () => {
    expect(coverageValueLabel(coverage({}), format)).toBe('BDT 500');
    expect(coverageValueLabel(coverage({ status: 'partial' }), format)).toBe('BDT 500 (Partial Value)');
    expect(coverageValueLabel(coverage({ status: 'not_valued', valuedSubtotal: null }), format)).toBe('Not Valued');
    expect(coverageValueLabel(coverage({ status: 'zero_cost', valuedSubtotal: 0 }), format)).toBe('BDT 0 (zero cost)');
    expect(coverageValueLabel(coverage({ status: 'no_stock', valuedSubtotal: null }), format)).toBe('No stock');
  });

  it('reads unknown cost as Not Valued and free stock as zero cost', () => {
    expect(stockValueLabel(null, 5, format)).toBe('Not Valued');
    expect(stockValueLabel(0, 5, format)).toBe('BDT 0 (zero cost)');
    expect(stockValueLabel(0, 0, format)).toBe('BDT 0');
  });

  it('describes each kind of stock with its quantity and record count', () => {
    expect(valuationCoverageText(coverage({
      valuedQuantity: 1000,
      valuedRecords: 2,
      zeroCostQuantity: 1,
      zeroCostRecords: 1,
      unvaluedQuantity: 2523,
      unvaluedRecords: 23
    }))).toBe('Valued: 1,000 units (2 records) · Zero cost: 1 unit (1 record) · Not valued: 2,523 units (23 records)');
    expect(valuationCoverageText(coverage({ status: 'no_stock' }))).toBeNull();
    expect(valuationCoverageText(null)).toBeNull();
  });
});
