import { AuthorizedLocationDto, AuthorizedOutletsDto } from '../../models/report.model';
import { UOM_ERROR_MESSAGES, UomDimension } from '../../models/uom.model';
import { newIdempotencyKey } from '../../services/uom.service';

export const UOM_FORBIDDEN_MESSAGE = 'You do not have permission to perform this action.';

export const DIMENSION_LABELS: Readonly<Record<UomDimension, string>> = {
  count: 'Count',
  volume: 'Volume',
  mass: 'Mass',
  length: 'Length',
  packaging: 'Packaging',
};

export const DIMENSIONS = Object.keys(DIMENSION_LABELS) as UomDimension[];

/** The server rejects a higher quantity precision (`UomUnit.MaxPrecision`). */
export const MAX_UOM_PRECISION = 4;

/** The permissions the variant search lookup accepts. Without one of them the picker can never find an item. */
export const VARIANT_SEARCH_PERMISSIONS: readonly string[] = [
  'products.view',
  'inventory.view',
  'stock_adjustments.create',
  'stock_adjustments.edit',
  'stock_transfers.view',
  'stock_transfers.create',
  'purchases.create',
  'purchases.edit',
  'reports.inventory',
];

export interface UomErrorInfo {
  status: number;
  code: string | null;
  message: string;
}

interface ErrorBody {
  message?: unknown;
  errorCode?: unknown;
}

interface ErrorLike {
  status?: unknown;
  error?: ErrorBody | string | null;
  originalError?: { error?: ErrorBody | string | null } | null;
}

/**
 * Reads a failed API call. The global error interceptor replaces the HttpErrorResponse with a wrapper that keeps the
 * response only under `originalError`, so the stable `errorCode` has to be read from there.
 */
export function readUomError(error: unknown, fallback: string): UomErrorInfo {
  const source = (error ?? {}) as ErrorLike;
  const status = typeof source.status === 'number' ? source.status : 0;
  const body = bodyOf(source.originalError?.error) ?? bodyOf(source.error);
  const code = typeof body?.errorCode === 'string' ? body.errorCode : null;

  if (status === 403) return { status, code, message: UOM_FORBIDDEN_MESSAGE };
  if (status === 0) return { status, code, message: 'Unable to connect to the server. Check your connection and try again.' };

  const apiMessage = typeof body?.message === 'string' ? body.message.trim() : '';
  const message = apiMessage && !apiMessage.startsWith('Http failure')
    ? apiMessage
    : (code && UOM_ERROR_MESSAGES[code]) || fallback;
  return { status, code, message };
}

function bodyOf(value: ErrorBody | string | null | undefined): ErrorBody | null {
  return value && typeof value === 'object' ? value : null;
}

/**
 * One idempotency key for one intended operation. A retry of the same payload reuses the key, so a request that succeeded
 * on the server but failed on the way back cannot post twice; a changed payload is a new operation and gets a new key.
 */
export class IdempotencyKeyHolder {
  private key: string | null = null;
  private fingerprint: string | null = null;

  constructor(private readonly prefix: string) {}

  keyFor(fingerprint: string): string {
    if (!this.key || this.fingerprint !== fingerprint) {
      this.key = newIdempotencyKey(this.prefix);
      this.fingerprint = fingerprint;
    }
    return this.key;
  }

  clear(): void {
    this.key = null;
    this.fingerprint = null;
  }
}

export interface LocationOption {
  key: string;
  id: number;
  type: 'outlet' | 'warehouse';
  name: string;
}

export function locationOptions(access: AuthorizedOutletsDto): LocationOption[] {
  const toOption = (type: 'outlet' | 'warehouse') => (location: AuthorizedLocationDto): LocationOption => ({
    key: `${type}:${location.id}`,
    id: location.id,
    type,
    name: location.name,
  });
  return [...access.outlets.map(toOption('outlet')), ...access.warehouses.map(toOption('warehouse'))];
}

export function defaultLocationKey(access: AuthorizedOutletsDto, options: readonly LocationOption[]): string {
  if (access.defaultLocationId && access.defaultLocationType) {
    const key = `${access.defaultLocationType}:${access.defaultLocationId}`;
    if (options.some(option => option.key === key)) return key;
  }
  return options.length === 1 ? options[0].key : '';
}

export function locationName(options: readonly LocationOption[], id: number, type: string): string {
  const match = options.find(option => option.id === id && option.type === type.toLowerCase());
  return match?.name ?? `${type === 'warehouse' ? 'Warehouse' : 'Outlet'} #${id}`;
}
