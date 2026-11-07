import { VariantUomConfig } from '../models/uom.model';
import {
  entryError,
  entryPrecision,
  formatUnitTotals,
  hasNonPcsUnit,
  looseOnHand,
  sumByUnit,
  toBaseQuantity,
  unitTotalLabel
} from './uom-quantity.util';

const unit = (code: string, precision: number) => ({ id: 1, code, symbol: code, dimension: 'volume' as const, precision, isWhole: precision === 0 });

export const attar: VariantUomConfig = {
  configId: 40,
  version: 2,
  variantId: 7,
  baseUnit: unit('ml', 2),
  packStateTrackingEnabled: false,
  repackingEnabled: false,
  sellingOptions: [],
  purchaseOptions: [
    { id: 1, code: 'L', label: 'Litre', unit: unit('L', 3), factorNumerator: 1000, factorDenominator: 1, baseQuantityPerUnit: 1000, isDefault: false, isSealedPack: false },
    { id: 2, code: 'JAR500', label: 'Jar 500 ml', unit: unit('jar', 3), factorNumerator: 500, factorDenominator: 1, baseQuantityPerUnit: 500, isDefault: false, isSealedPack: true },
    { id: 3, code: 'THIRD', label: 'Third', unit: unit('third', 3), factorNumerator: 1, factorDenominator: 3, baseQuantityPerUnit: 0.33, isDefault: false, isSealedPack: false }
  ]
};

describe('Unit-aware entry previews', () => {
  it('converts an entry to base units without floating point noise', () => {
    expect(toBaseQuantity(attar, 1, 1.1)).toBe(1100);
    expect(toBaseQuantity(attar, null, 2.5)).toBe(2.5);
  });

  it('reports no base quantity for a conversion that is not exact', () => {
    expect(toBaseQuantity(attar, 3, 1)).toBeNull();
    expect(entryError(attar, 3, 1)).toContain('exact quantity of ml');
  });

  it('counts a sealed pack whole, whatever its unit allows', () => {
    expect(entryPrecision(attar, 2)).toBe(0);
    expect(entryError(attar, 2, 1.5)).toBe('Enter a whole number for this unit.');
    expect(entryError(attar, 2, 2)).toBeNull();
  });

  it('applies the precision of the chosen unit', () => {
    expect(entryError(attar, null, 1.234)).toBe('This unit allows up to 2 decimal places.');
    expect(entryError(attar, 1, 1.234)).toBeNull();
  });

  it('refuses zero and negative quantities unless the operation allows them', () => {
    expect(entryError(attar, null, 0)).toBe('Quantity must be greater than 0.');
    expect(entryError(attar, null, null)).toBe('Enter a quantity.');
    expect(entryError(attar, null, -1)).toBe('Quantity cannot be negative.');
    expect(entryError(attar, null, -1, { allowNegative: true })).toBeNull();
    expect(entryError(attar, null, 0, { allowZero: true })).toBeNull();
  });

  it('never adds unlike units', () => {
    const rows = [
      { unitCode: 'pcs', quantity: 10 },
      { unitCode: 'ml', quantity: 0.1 },
      { unitCode: 'ml', quantity: 0.2 }
    ];

    expect(sumByUnit(rows)).toEqual([{ unitCode: 'pcs', quantity: 10 }, { unitCode: 'ml', quantity: 0.3 }]);
    expect(hasNonPcsUnit(rows)).toBe(true);
    expect(unitTotalLabel(rows)).toBe('10 pcs · 0.3 ml');
    expect(formatUnitTotals([{ unitCode: 'kg', quantity: 2.5 }])).toBe('2.5 kg');
  });

  it('keeps a plain number while only pcs are involved', () => {
    expect(unitTotalLabel([{ unitCode: 'pcs', quantity: 4 }, { unitCode: 'pcs', quantity: 6 }])).toBe('10');
    expect(unitTotalLabel([])).toBe('0');
    expect(hasNonPcsUnit(null)).toBe(false);
  });

  it('takes loose stock as what is on hand outside sealed and prepared packs', () => {
    expect(looseOnHand({
      variantId: 7, locationId: 1, locationType: 'outlet', baseUnitCode: 'ml', onHand: 1200, allocatedTotal: 1000, isReconciled: true,
      balances: [
        { state: 'sealed', packCode: 'JAR500', baseQtyPerPack: 500, packCount: 2, baseQuantity: 1000, capitalizedValue: 0 }
      ]
    })).toBe(200);
  });
});
