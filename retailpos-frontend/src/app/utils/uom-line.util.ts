import { PurchaseOption, UOM_ERROR_MESSAGES, VariantUomConfig } from '../models/uom.model';

/** Stored quantities carry at most six decimals. This only removes binary floating point noise from previews. */
export function roundQuantity(value: number): number {
  return Math.round(value * 1e6) / 1e6;
}

/** Decimals allowed for a quantity of a purchase unit. A sealed pack is always whole. */
export function purchaseUnitPrecision(option: PurchaseOption | null | undefined, config: VariantUomConfig): number {
  if (!option) return config.baseUnit.precision;
  return option.isSealedPack ? 0 : option.unit.precision;
}

export function isEntered(value: number | string | null | undefined): boolean {
  return value !== null && value !== undefined && value !== '';
}

export interface PackBreakdownCheck {
  /** False when neither sealed nor loose was entered: the server then derives the split. */
  stated: boolean;
  total: number;
  matches: boolean;
  message: string;
}

/** Mirrors the server rule `sealed × per pack + loose = accepted` for instant feedback. */
export function checkPackBreakdown(
  accepted: number,
  sealedPacks: number | null | undefined,
  loose: number | null | undefined,
  baseQtyPerPack: number,
  baseUnit: string,
): PackBreakdownCheck {
  const stated = isEntered(sealedPacks) || isEntered(loose);
  const sealed = Number(sealedPacks ?? 0);
  const looseQty = Number(loose ?? 0);
  const acceptedQty = roundQuantity(Number(accepted ?? 0));
  const total = roundQuantity(sealed * baseQtyPerPack + looseQty);
  const matches = !stated || total === acceptedQty;
  return {
    stated,
    total,
    matches,
    message: matches
      ? ''
      : `${sealed} sealed × ${baseQtyPerPack} + ${looseQty} loose = ${total} ${baseUnit}, but the accepted quantity is ${acceptedQty} ${baseUnit}.`,
  };
}

function errorBody(error: unknown): { errorCode?: unknown; message?: unknown } | null {
  const source = error as { originalError?: { error?: unknown }; error?: unknown } | null;
  const body = source?.originalError?.error ?? source?.error;
  return body && typeof body === 'object' ? (body as { errorCode?: unknown; message?: unknown }) : null;
}

export function apiErrorCode(error: unknown): string {
  const code = errorBody(error)?.errorCode;
  return typeof code === 'string' ? code : '';
}

/** A known unit error's friendly message, else the message the API sent, else null. */
export function uomErrorMessage(error: unknown): string | null {
  const known = UOM_ERROR_MESSAGES[apiErrorCode(error)];
  if (known) return known;
  const message = errorBody(error)?.message;
  return typeof message === 'string' && message ? message : null;
}
