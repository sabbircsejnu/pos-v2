import { CartItem, SaleLineUomFields } from '../../models/sale.model';
import { PackStateBalanceRow, PackStateKind, PackStateView, SellingOption, VariantUomConfig } from '../../models/uom.model';
import { roundQuantity } from '../../utils/uom-line.util';

/** What the cashier chose in the unit picker. The server re-checks every part of it at checkout. */
export interface PosUomSelection {
  option: SellingOption;
  configId: number;
  quantity: number;
  fulfilmentState?: PackStateKind;
  includePackaging: boolean;
}

/** Two options, forms or packaging choices of one variant are separate lines. A pcs line is keyed by variant alone. */
export function cartLineKey(line: Pick<CartItem, 'variantId' | 'sellingOptionId' | 'fulfilmentState' | 'includePackaging'>): string {
  return line.sellingOptionId
    ? `${line.variantId}:${line.sellingOptionId}:${line.fulfilmentState ?? ''}:${line.includePackaging ? 1 : 0}`
    : String(line.variantId);
}

/** The base quantity an entered quantity uses. A preview only. */
export function sellingBaseQuantity(option: SellingOption, quantity: number): number {
  const perUnit = option.kind === 'pack'
    ? Number(option.baseQuantity ?? 0)
    : Number(option.factorNumerator) / Number(option.factorDenominator || 1);
  return roundQuantity(perUnit * quantity);
}

/** Decimals allowed for a quantity of an option: a pack count is always whole. */
export function sellingPrecision(option: SellingOption, config: VariantUomConfig): number {
  if (option.kind === 'pack') return 0;
  return option.unit?.precision ?? config.baseUnit.precision;
}

/** The stock forms the server accepts for an option. Empty when the variant does not track pack state. */
export function allowedForms(option: SellingOption, config: VariantUomConfig): PackStateKind[] {
  if (!config.packStateTrackingEnabled) return [];
  const forms: PackStateKind[] = [option.sealedPackCode ? 'sealed' : 'loose'];
  if (option.isPreparedEligible) forms.push('prepared');
  return forms;
}

export function packStateRow(view: PackStateView | null, option: SellingOption, form: PackStateKind): PackStateBalanceRow | null {
  if (!view) return null;
  return view.balances.find(row => {
    if (row.state !== form) return false;
    if (form === 'sealed') return row.packCode === option.sealedPackCode;
    if (form === 'prepared') return row.packCode === option.code;
    return true;
  }) ?? null;
}

/** Packaging is a choice only for stock filled at the counter: a sealed or prepared pack already is its packaging. */
export function canChoosePackaging(option: SellingOption, form: PackStateKind | undefined): boolean {
  return (form === undefined || form === 'loose') && option.components.some(component => !component.isRequired);
}

/** The unit fields of a request line. A pcs line adds nothing, so its payload stays exactly as it was. */
export function uomLineFields(line: SaleLineUomFields): SaleLineUomFields {
  if (!line.sellingOptionId) return {};
  return {
    sellingOptionId: line.sellingOptionId,
    expectedConfigVersionId: line.expectedConfigVersionId,
    fulfilmentState: line.fulfilmentState,
    includePackaging: !!line.includePackaging,
  };
}

/**
 * Whether the server would refuse this line because its unit setup no longer matches the variant's.
 * `undefined` means the configuration is not known yet; `null` means the variant is plain pcs.
 */
export function lineNeedsRepick(line: SaleLineUomFields, config: VariantUomConfig | null | undefined): boolean {
  if (config === undefined) return false;
  if (config === null) return !!line.sellingOptionId;
  if (!line.sellingOptionId) {
    return config.packStateTrackingEnabled || config.baseUnit.code.toLowerCase() !== 'pcs';
  }
  return line.expectedConfigVersionId !== config.configId
    || !config.sellingOptions.some(option => option.id === line.sellingOptionId);
}
