import { PackStateView, PurchaseOption, VariantUomConfig } from '../models/uom.model';
import { fitsPrecision, formatUnitQuantity } from '../services/uom.service';

/**
 * Previews of unit-aware stock entry. The server converts and validates every quantity again; nothing here is final.
 * Every stored quantity is in base units, and quantities of unlike units are listed side by side, never added.
 */

export const PCS_UNIT_CODE = 'pcs';

export interface UnitTotal {
  unitCode: string;
  quantity: number;
}

/** What a unit and quantity selector reports. A null unit option means the item's base unit. */
export interface UnitEntry {
  unitOptionId: number | null;
  quantity: number | null;
}

/** Removes binary floating point noise so that previews compare as the decimals people typed. */
export function roundQuantity(value: number, digits = 6): number {
  return Number(value.toFixed(digits));
}

export function findStockOption(config: VariantUomConfig, unitOptionId: number | null | undefined): PurchaseOption | null {
  return unitOptionId === null || unitOptionId === undefined
    ? null
    : config.purchaseOptions.find(option => option.id === unitOptionId) ?? null;
}

/** Decimal places allowed for an entered quantity. A sealed pack is always counted whole. */
export function entryPrecision(config: VariantUomConfig, unitOptionId: number | null | undefined): number {
  const option = findStockOption(config, unitOptionId);
  if (!option) {
    return config.baseUnit.precision;
  }
  return option.isSealedPack ? 0 : option.unit.precision;
}

/** The base quantity of a positive entry, or null when it has no exact base quantity. */
export function toBaseQuantity(config: VariantUomConfig, unitOptionId: number | null | undefined, entered: number): number | null {
  const option = findStockOption(config, unitOptionId);
  const converted = option ? (entered * option.factorNumerator) / option.factorDenominator : entered;
  const base = Number(converted.toFixed(9));
  return fitsPrecision(base, config.baseUnit.precision) ? base : null;
}

/** Instant feedback for one entry, in the order the server checks it. */
export function entryError(
  config: VariantUomConfig,
  unitOptionId: number | null | undefined,
  entered: number | null | undefined,
  options: { allowNegative?: boolean; allowZero?: boolean } = {}
): string | null {
  if (entered === null || entered === undefined || !Number.isFinite(entered)) {
    return 'Enter a quantity.';
  }
  if (entered === 0 && !options.allowZero) {
    return options.allowNegative ? 'Quantity cannot be 0.' : 'Quantity must be greater than 0.';
  }
  if (entered < 0 && !options.allowNegative) {
    return 'Quantity cannot be negative.';
  }
  const size = Math.abs(entered);
  const precision = entryPrecision(config, unitOptionId);
  if (!fitsPrecision(size, precision)) {
    return precision === 0
      ? 'Enter a whole number for this unit.'
      : `This unit allows up to ${precision} decimal place${precision === 1 ? '' : 's'}.`;
  }
  if (toBaseQuantity(config, unitOptionId, size) === null) {
    return `This does not convert to an exact quantity of ${config.baseUnit.symbol}.`;
  }
  return null;
}

/** Adds quantities per unit, keeping first-seen order. */
export function sumByUnit(rows: readonly UnitTotal[]): UnitTotal[] {
  const totals = new Map<string, number>();
  for (const row of rows) {
    const unitCode = row.unitCode || PCS_UNIT_CODE;
    totals.set(unitCode, (totals.get(unitCode) ?? 0) + row.quantity);
  }
  return [...totals].map(([unitCode, quantity]) => ({ unitCode, quantity: roundQuantity(quantity) }));
}

export function hasNonPcsUnit(rows: readonly UnitTotal[] | null | undefined): boolean {
  return (rows ?? []).some(row => (row.unitCode || PCS_UNIT_CODE) !== PCS_UNIT_CODE);
}

/** `10 pcs · 500 ml`. Never a single number, because 10 pcs + 500 ml is not one. */
export function formatUnitTotals(rows: readonly UnitTotal[]): string {
  return rows.map(row => formatUnitQuantity(row.quantity, row.unitCode)).join(' · ');
}

/** A total's text: the plain sum while only pcs are involved, otherwise one figure per unit. */
export function unitTotalLabel(rows: readonly UnitTotal[]): string {
  const totals = sumByUnit(rows);
  return hasNonPcsUnit(totals)
    ? formatUnitTotals(totals)
    : String(totals.reduce((sum, row) => sum + row.quantity, 0));
}

/** `0.5 Litre` for a line entered in another unit. Null when it was entered in the base unit or has no snapshot. */
export function enteredUnitLabel(entered: number | null | undefined, label: string | null | undefined): string | null {
  return entered === null || entered === undefined || !label ? null : formatUnitQuantity(entered, label);
}

/** `Loose`, or `Sealed JAR500 (500 ml each)`. */
export function stockFormLabel(state: string | null | undefined, packCode: string | null | undefined, baseQtyPerPack: number | null | undefined, unitCode: string | null | undefined): string {
  if (state !== 'sealed' && state !== 'prepared') {
    return 'Loose';
  }
  return `${state === 'sealed' ? 'Sealed' : 'Prepared'} ${packCode ?? ''} (${formatUnitQuantity(baseQtyPerPack ?? 0, unitCode)} each)`;
}

/** Base quantity at a location not held in sealed or prepared packs. */
export function looseOnHand(view: PackStateView): number {
  const packed = view.balances
    .filter(row => row.state !== 'loose')
    .reduce((sum, row) => sum + row.baseQuantity, 0);
  return roundQuantity(view.onHand - packed);
}
