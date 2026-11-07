import { CreateStockAdjustmentLineDto, StockAdjustmentLineDto } from '../../models/stock-adjustment.model';
import { PackStateView, VariantUomConfig } from '../../models/uom.model';
import { UnitEntry, entryError, findStockOption, roundQuantity, toBaseQuantity } from '../../utils/uom-quantity.util';

export const LOOSE_CHOICE = 'loose';

/** A line's unit-aware entry. A line without a configuration has none of it and stays plain pcs. */
export interface AdjustmentUomEntry {
  uomStatus?: 'loading' | 'ready' | 'failed';
  uomConfig?: VariantUomConfig | null;
  unitOptionId?: number | null;
  enteredQuantity?: number | null;
  /** For a pack-tracked item: `LOOSE_CHOICE` or a `SealedChoice` key. Null means the user must choose. */
  packChoice?: string | null;
  packView?: PackStateView | null;
  /** The sealed pack a saved draft line names. It stays selectable even if it is no longer held or purchasable. */
  storedPack?: { packCode: string; baseQtyPerPack: number } | null;
}

export interface SealedChoice {
  key: string;
  label: string;
  packCode: string;
  baseQtyPerPack: number;
}

/** Sealed packs held at the location, plus those the item can be bought in. Prepared packs cannot be adjusted. */
export function sealedChoices(
  config: VariantUomConfig,
  view: PackStateView | null | undefined,
  storedPack?: AdjustmentUomEntry['storedPack']
): SealedChoice[] {
  const choices = new Map<string, SealedChoice>();
  const add = (packCode: string, baseQtyPerPack: number) => {
    const key = `sealed|${packCode}|${baseQtyPerPack}`;
    if (!choices.has(key)) {
      choices.set(key, { key, packCode, baseQtyPerPack, label: `Sealed ${packCode} (${baseQtyPerPack} ${config.baseUnit.code} each)` });
    }
  };
  if (storedPack) {
    add(storedPack.packCode, storedPack.baseQtyPerPack);
  }
  (view?.balances ?? []).filter(row => row.state === 'sealed').forEach(row => add(row.packCode, row.baseQtyPerPack));
  config.purchaseOptions.filter(option => option.isSealedPack).forEach(option => add(option.code, option.baseQuantityPerUnit));
  return [...choices.values()];
}

export function holdsPreparedStock(view: PackStateView | null | undefined): boolean {
  return (view?.balances ?? []).some(row => row.state === 'prepared' && row.packCount > 0);
}

/** The signed base change of an entry: a preview, or 0 when it has no exact base quantity. */
export function signedBaseChange(config: VariantUomConfig, entry: UnitEntry): number {
  const quantity = entry.quantity;
  if (!quantity) {
    return 0;
  }
  const base = toBaseQuantity(config, entry.unitOptionId, Math.abs(quantity)) ?? 0;
  return roundQuantity(quantity < 0 ? -base : base);
}

/** Instant feedback for a configured line. The server checks all of it again. */
export function adjustmentEntryError(line: AdjustmentUomEntry): string | null {
  const config = line.uomConfig;
  if (!config) {
    return null;
  }
  const error = entryError(config, line.unitOptionId, line.enteredQuantity, { allowNegative: true });
  if (error || !config.packStateTrackingEnabled) {
    return error;
  }
  if (!line.packChoice) {
    return 'Choose whether this changes loose or sealed stock.';
  }
  if (line.packChoice === LOOSE_CHOICE) {
    return null;
  }
  const choice = sealedChoices(config, line.packView, line.storedPack).find(candidate => candidate.key === line.packChoice);
  if (!choice) {
    return 'Choose whether this changes loose or sealed stock.';
  }
  const packs = signedBaseChange(config, { unitOptionId: line.unitOptionId ?? null, quantity: line.enteredQuantity ?? null }) / choice.baseQtyPerPack;
  return Number.isInteger(roundQuantity(packs)) ? null : 'Sealed stock is adjusted in whole packs.';
}

/** The unit and stock form fields of a line's request. Empty for plain pcs, whose request is unchanged. */
export function adjustmentUomPayload(line: AdjustmentUomEntry): Partial<CreateStockAdjustmentLineDto> {
  const config = line.uomConfig;
  if (!config) {
    return {};
  }
  const payload: Partial<CreateStockAdjustmentLineDto> = {
    enteredQuantity: line.enteredQuantity ?? undefined,
    unitOptionId: line.unitOptionId ?? null,
    expectedConfigVersionId: config.configId
  };
  if (!config.packStateTrackingEnabled) {
    return payload;
  }
  const choice = sealedChoices(config, line.packView, line.storedPack).find(candidate => candidate.key === line.packChoice);
  return choice
    ? { ...payload, packState: 'sealed', packCode: choice.packCode, baseQtyPerPack: choice.baseQtyPerPack }
    : { ...payload, packState: 'loose' };
}

/**
 * A saved draft line's entry, as it was made. If its unit is gone or no longer gives the stored quantity, it falls back
 * to the base quantity, which is what the line really holds.
 */
export function restoreDraftEntry(config: VariantUomConfig, line: StockAdjustmentLineDto): AdjustmentUomEntry {
  const option = findStockOption(config, line.uomOptionId);
  const entered = line.uomEnteredQuantity ?? null;
  const sameQuantity = !!option && entered !== null
    && signedBaseChange(config, { unitOptionId: option.id, quantity: entered }) === line.quantityChange;
  const storedPack = line.packState === 'sealed' && line.packCode && line.baseQtyPerPack
    ? { packCode: line.packCode, baseQtyPerPack: line.baseQtyPerPack }
    : null;
  return {
    unitOptionId: sameQuantity ? option.id : null,
    enteredQuantity: sameQuantity ? entered : line.quantityChange,
    storedPack,
    packChoice: storedPack
      ? `sealed|${storedPack.packCode}|${storedPack.baseQtyPerPack}`
      : line.packState === 'loose' ? LOOSE_CHOICE : null
  };
}

/**
 * What a draft line already holds, for when the current configuration cannot be read: the server converts it again, so
 * saving neither loses the stock form nor depends on a client conversion. Empty for plain pcs.
 */
export function storedDraftPayload(line: StockAdjustmentLineDto): Partial<CreateStockAdjustmentLineDto> {
  return {
    ...(line.uomEnteredQuantity !== null && line.uomEnteredQuantity !== undefined
      ? { enteredQuantity: line.uomEnteredQuantity, unitOptionId: line.uomOptionId ?? null }
      : {}),
    ...(line.packState
      ? { packState: line.packState, packCode: line.packCode ?? undefined, baseQtyPerPack: line.baseQtyPerPack ?? undefined }
      : {})
  };
}
