import { DestroyRef, Signal, signal } from '@angular/core';
import { Observable, Subscription } from 'rxjs';
import { StockValuationCoverage } from '../../../models/owner-dashboard.model';

export type DatePreset = 'today' | 'yesterday' | 'last7' | 'last30' | 'thisMonth' | 'lastMonth' | 'custom';

export const DATE_PRESETS: { value: DatePreset; label: string }[] = [
  { value: 'today', label: 'Today' },
  { value: 'yesterday', label: 'Yesterday' },
  { value: 'last7', label: 'Last 7 Days' },
  { value: 'last30', label: 'Last 30 Days' },
  { value: 'thisMonth', label: 'This Month' },
  { value: 'lastMonth', label: 'Last Month' },
  { value: 'custom', label: 'Custom' }
];

export interface DateRange {
  from: string;
  to: string;
}

/** The filter a screen has committed with Apply. */
export interface AppliedFilter extends DateRange {
  preset: DatePreset;
  /** `outlet:5`, `warehouse:9`, or empty for every location of the Business. */
  location: string;
  categoryId: number | null;
}

function parseIso(iso: string): Date {
  const [year, month, day] = iso.split('-').map(Number);
  return new Date(Date.UTC(year, month - 1, day));
}

function toIso(date: Date): string {
  return date.toISOString().slice(0, 10);
}

function addDays(iso: string, days: number): string {
  const date = parseIso(iso);
  date.setUTCDate(date.getUTCDate() + days);
  return toIso(date);
}

/** Resolves a preset against the server's business date, never the browser's clock or zone. */
export function resolvePreset(preset: Exclude<DatePreset, 'custom'>, today: string): DateRange {
  const date = parseIso(today);
  switch (preset) {
    case 'today':
      return { from: today, to: today };
    case 'yesterday': {
      const yesterday = addDays(today, -1);
      return { from: yesterday, to: yesterday };
    }
    case 'last7':
      return { from: addDays(today, -6), to: today };
    case 'last30':
      return { from: addDays(today, -29), to: today };
    case 'thisMonth':
      return { from: toIso(new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1))), to: today };
    case 'lastMonth':
      return {
        from: toIso(new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() - 1, 1))),
        to: toIso(new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 0)))
      };
  }
}

export const MAX_RANGE_DAYS = 366;

export function isIsoDateOnly(value: unknown): value is string {
  if (typeof value !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value)) {
    return false;
  }
  const parsed = parseIso(value);
  return !Number.isNaN(parsed.getTime()) && toIso(parsed) === value;
}

/** Null when the range is valid; otherwise the message to show. The server checks the same rules. */
export function validateRange(from: string, to: string): string | null {
  if (!isIsoDateOnly(from) || !isIsoDateOnly(to)) {
    return 'Choose both a From date and a To date.';
  }
  if (from > to) {
    return 'From date must be on or before To date.';
  }
  const days = (parseIso(to).getTime() - parseIso(from).getTime()) / 86_400_000 + 1;
  return days > MAX_RANGE_DAYS ? `The date range cannot exceed ${MAX_RANGE_DAYS} days.` : null;
}

export function parseLocation(value: string): { id: number; type: 'outlet' | 'warehouse' } | null {
  const match = /^(outlet|warehouse):(\d+)$/.exec(value);
  return match ? { type: match[1] as 'outlet' | 'warehouse', id: Number(match[2]) } : null;
}

export interface ChartScale {
  min: number;
  max: number;
  ticks: number[];
}

/** A round axis that always includes zero, so bars stay honest and negative refund days stay visible. */
export function buildScale(values: readonly number[], tickCount = 4): ChartScale {
  const top = Math.max(0, ...values);
  const bottom = Math.min(0, ...values);
  const span = top - bottom;
  if (span === 0) {
    return { min: 0, max: 1, ticks: [0] };
  }

  const rawStep = span / tickCount;
  const magnitude = 10 ** Math.floor(Math.log10(rawStep));
  const normalized = rawStep / magnitude;
  const step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 2.5 ? 2.5 : normalized <= 5 ? 5 : 10) * magnitude;
  const min = Math.floor(bottom / step) * step;
  const max = Math.ceil(top / step) * step;
  const ticks: number[] = [];
  for (let tick = min; tick <= max + step / 2; tick += step) {
    ticks.push(Math.round(tick * 100) / 100);
  }
  return { min, max, ticks };
}

export function scalePercent(value: number, scale: ChartScale): number {
  return ((value - scale.min) / (scale.max - scale.min)) * 100;
}

const COMPACT = new Intl.NumberFormat('en-US', { notation: 'compact', maximumFractionDigits: 1 });

export function compactNumber(value: number): string {
  return COMPACT.format(value);
}

const SHORT_DATE = new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short', timeZone: 'UTC' });

/** An unambiguous axis label such as `06 Nov`; never a mixed DD/MM or MM/DD reading. */
export function shortDate(iso: string): string {
  return SHORT_DATE.format(parseIso(iso));
}

export function bucketLabel(from: string, to: string): string {
  return from === to ? shortDate(from) : `${shortDate(from)} – ${shortDate(to)}`;
}

function quantityAndRecords(label: string, quantity: number, records: number, mixedUnits: boolean): string {
  const recordsText = `${records.toLocaleString('en-US')} ${records === 1 ? 'record' : 'records'}`;
  if (mixedUnits) {
    return `${label}: ${recordsText}`;
  }
  const units = `${quantity.toLocaleString('en-US')} ${quantity === 1 ? 'unit' : 'units'}`;
  return `${label}: ${units} (${recordsText})`;
}

/** A quantity beside its unit. Pcs stay a bare number, as they always were. */
export function unitQuantityLabel(quantity: number, unitCode: string | null | undefined): string {
  const text = new Intl.NumberFormat(undefined, { maximumFractionDigits: 6 }).format(quantity);
  return unitCode && unitCode !== 'pcs' ? `${text} ${unitCode}` : text;
}

/** Like `unitQuantityLabel`, for sentences that need a noun: `5 unit(s)` or `300 ml`. */
export function unitQuantityPhrase(quantity: number, unitCode: string | null | undefined): string {
  return unitCode && unitCode !== 'pcs' ? unitQuantityLabel(quantity, unitCode) : `${quantity} unit(s)`;
}

/**
 * The valued and unvalued quantities behind a stock value. Null when nothing is held. The API adds these quantities
 * across units, so with other units in the stock (`mixedUnits`) only the record counts mean anything.
 */
export function valuationCoverageText(coverage: StockValuationCoverage | null | undefined, mixedUnits = false): string | null {
  if (!coverage || coverage.status === 'no_stock') {
    return null;
  }
  const parts: string[] = [];
  if (coverage.valuedRecords > 0) {
    parts.push(quantityAndRecords('Valued', coverage.valuedQuantity, coverage.valuedRecords, mixedUnits));
  }
  if (coverage.zeroCostRecords > 0) {
    parts.push(quantityAndRecords('Zero cost', coverage.zeroCostQuantity, coverage.zeroCostRecords, mixedUnits));
  }
  if (coverage.unvaluedRecords > 0) {
    parts.push(quantityAndRecords('Not valued', coverage.unvaluedQuantity, coverage.unvaluedRecords, mixedUnits));
  }
  return parts.join(' · ');
}

/** One stock record's value. Unknown cost reads as Not Valued, never as a zero amount. */
export function stockValueLabel(value: number | null, onHand: number, format: (amount: number) => string): string {
  if (value === null) {
    return 'Not Valued';
  }
  return value === 0 && onHand > 0 ? `${format(0)} (zero cost)` : format(value);
}

/** A subtotal over several records, labelled for how complete it is. */
export function coverageValueLabel(coverage: StockValuationCoverage, format: (amount: number) => string): string {
  const subtotal = coverage.valuedSubtotal ?? 0;
  switch (coverage.status) {
    case 'no_stock':
      return 'No stock';
    case 'not_valued':
      return 'Not Valued';
    case 'partial':
      return `${format(subtotal)} (Partial Value)`;
    case 'zero_cost':
      return `${format(0)} (zero cost)`;
    default:
      return format(subtotal);
  }
}

export const REASON_TEXT: Record<string, string> = {
  source_excess: 'The source holds more than it needs to keep for its own demand.',
  source_no_sales: 'The source sold no net units of this variant in the analysis period.',
  destination_low_cover: 'The destination is below its target stock cover.',
  destination_out_of_stock: 'The destination has no available stock of this variant.',
  velocity_gap: 'The destination sells this variant much faster than the source.',
  limited_by_source_spare: 'The quantity is limited by what the source can safely release.',
  limited_by_policy_cap: 'The quantity is limited by the configured maximum per transfer.',
  reduced_by_inbound: 'Stock already on its way to the destination reduces its need.',
  reduced_by_pending_outbound: 'Transfers already pending from the source reduce what it can release.'
};

export interface WidgetState<T> {
  readonly data: Signal<T | null>;
  readonly loading: Signal<boolean>;
  readonly error: Signal<string | null>;
  /** Replaces any in-flight request, so a slow old response can never overwrite a newer one. */
  load(source: Observable<T>): void;
  clear(): void;
}

/** One widget's request state. A failure stays in this widget; the rest of the page keeps working (NFR-010). */
export function createWidget<T>(destroyRef: DestroyRef, toMessage: (error: unknown) => string): WidgetState<T> {
  const data = signal<T | null>(null);
  const loading = signal(false);
  const error = signal<string | null>(null);
  let subscription: Subscription | undefined;
  destroyRef.onDestroy(() => subscription?.unsubscribe());

  return {
    data: data.asReadonly(),
    loading: loading.asReadonly(),
    error: error.asReadonly(),
    load(source) {
      subscription?.unsubscribe();
      loading.set(true);
      error.set(null);
      subscription = source.subscribe({
        next: value => {
          data.set(value);
          loading.set(false);
        },
        error: (failure: unknown) => {
          data.set(null);
          error.set(toMessage(failure));
          loading.set(false);
        }
      });
    },
    clear() {
      subscription?.unsubscribe();
      data.set(null);
      error.set(null);
      loading.set(false);
    }
  };
}
