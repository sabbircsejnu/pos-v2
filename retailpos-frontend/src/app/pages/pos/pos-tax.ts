/**
 * TypeScript port of the server checkout algorithm (SaleCheckoutCalculator). The server is authoritative: it
 * rejects a sale whose submitted tax differs from its own calculation by more than 0.01 (SALE_TAX_MISMATCH),
 * so every rounding and allocation rule here must stay identical to the C# implementation.
 *
 * All money is handled in whole cents and rounded half away from zero; the tax rate is scaled to 4 decimals so the
 * rate arithmetic stays exact (BigInt) for any realistic amount.
 */

import type { TaxClassification } from '../../models/sale.model';

export type { TaxClassification };

export interface TaxLineInput {
  quantity: number;
  /** Unit price after automatic price rules (the server `finalPrice`). */
  unitPrice: number;
  /** Manual line discount amount (never combined with an invoice discount). */
  lineDiscount: number;
  /** Effective tax rate in percent (0..100); 0 for zero-rated, exempt or untaxed lines. */
  taxRate: number;
}

export interface TaxCalculationInput {
  lines: readonly TaxLineInput[];
  invoiceDiscount: number;
  pricesIncludeTax: boolean;
}

export interface TaxLineResult {
  /** quantity x unit price - line discount. */
  net: number;
  invoiceDiscountShare: number;
  /** net - invoice discount share. */
  base: number;
  tax: number;
  taxRate: number;
}

export interface TaxCalculationResult {
  lines: TaxLineResult[];
  itemsSubtotal: number;
  invoiceDiscount: number;
  tax: number;
  total: number;
  /** True when the sale is tax-inclusive (prices include tax and at least one line is taxed). */
  inclusive: boolean;
}

const RATE_SCALE = 10_000;

/** Rounds a money value to cents, half away from zero, tolerant of binary floating point noise. */
export function toCents(value: number): number {
  const abs = Math.abs(Number.isFinite(value) ? value : 0);
  const cents = Math.round(Number((abs * 100).toPrecision(12)));
  return value < 0 ? -cents : cents;
}

export function roundMoney(value: number): number {
  return toCents(value) / 100;
}

function divideRoundHalfAwayFromZero(numerator: bigint, denominator: bigint): number {
  const negative = numerator < 0n;
  const abs = negative ? -numerator : numerator;
  const quotient = (2n * abs + denominator) / (2n * denominator);
  return Number(negative ? -quotient : quotient);
}

/**
 * Splits `totalCents` across `weights` (cents) proportionally, in whole cents. Largest remainder; ties go to the
 * lower index. The shares always add up to `totalCents` exactly.
 */
export function allocateProportionally(totalCents: number, weights: readonly number[]): number[] {
  const shares = weights.map(() => 0);
  if (totalCents === 0 || weights.length === 0) {
    return shares;
  }

  const negative = totalCents < 0;
  const total = BigInt(Math.abs(totalCents));
  const weightValues = weights.map(weight => BigInt(Math.max(0, Math.round(weight))));
  const weightSum = weightValues.reduce((sum, weight) => sum + weight, 0n);
  if (weightSum <= 0n) {
    return shares;
  }

  const remainders: bigint[] = [];
  let allocated = 0n;
  weightValues.forEach((weight, index) => {
    const exact = total * weight;
    const floor = exact / weightSum;
    shares[index] = Number(floor);
    remainders.push(exact % weightSum);
    allocated += floor;
  });

  const leftover = Number(total - allocated);
  const order = weights
    .map((_, index) => index)
    .sort((a, b) => (remainders[a] === remainders[b] ? a - b : remainders[a] > remainders[b] ? -1 : 1));
  for (let position = 0; position < leftover; position++) {
    shares[order[position]]++;
  }

  return negative ? shares.map(share => -share) : shares;
}

/** Tax rate to apply to a line: the Business switch, then the classification, then the line's own effective rate. */
export function effectiveLineTaxRate(
  taxRate: number | null | undefined,
  classification: TaxClassification | string | null | undefined,
  taxEnabled: boolean
): number {
  if (!taxEnabled || classification === 'zero_rated' || classification === 'exempt') {
    return 0;
  }

  const rate = Number(taxRate ?? 0);
  return Number.isFinite(rate) && rate > 0 ? Math.min(100, rate) : 0;
}

export function calculateTax(input: TaxCalculationInput): TaxCalculationResult {
  const count = input.lines.length;
  const netCents = input.lines.map(line =>
    toCents(line.quantity * line.unitPrice) - toCents(line.lineDiscount));
  const itemsSubtotalCents = netCents.reduce((sum, cents) => sum + cents, 0);
  const invoiceDiscountCents = toCents(input.invoiceDiscount);

  const discountShares = allocateProportionally(invoiceDiscountCents, netCents);
  const baseCents = netCents.map((net, index) => net - discountShares[index]);
  const rates = input.lines.map(line => (line.taxRate > 0 ? line.taxRate : 0));
  const inclusive = input.pricesIncludeTax && rates.some(rate => rate > 0);

  const taxShares = new Array<number>(count).fill(0);
  const groups = new Map<number, number[]>();
  rates.forEach((rate, index) => {
    if (rate > 0) {
      groups.set(rate, [...(groups.get(rate) ?? []), index]);
    }
  });

  groups.forEach((members, rate) => {
    const groupBase = members.reduce((sum, index) => sum + baseCents[index], 0);
    const scaledRate = BigInt(Math.round(rate * RATE_SCALE));
    const scaledHundred = BigInt(100 * RATE_SCALE);
    const groupTax = divideRoundHalfAwayFromZero(
      BigInt(groupBase) * scaledRate,
      inclusive ? scaledHundred + scaledRate : scaledHundred
    );
    const groupShares = allocateProportionally(groupTax, members.map(index => baseCents[index]));
    members.forEach((lineIndex, position) => {
      taxShares[lineIndex] = groupShares[position];
    });
  });

  const taxCents = taxShares.reduce((sum, cents) => sum + cents, 0);
  const taxableBaseCents = itemsSubtotalCents - invoiceDiscountCents;
  const totalCents = inclusive ? taxableBaseCents : taxableBaseCents + taxCents;

  return {
    lines: input.lines.map((_, index) => ({
      net: netCents[index] / 100,
      invoiceDiscountShare: discountShares[index] / 100,
      base: baseCents[index] / 100,
      tax: taxShares[index] / 100,
      taxRate: rates[index],
    })),
    itemsSubtotal: itemsSubtotalCents / 100,
    invoiceDiscount: invoiceDiscountCents / 100,
    tax: taxCents / 100,
    total: totalCents / 100,
    inclusive,
  };
}
