import { UiTableColumn, UiTableSort } from './ui-data-table.types';
import { isIsoDate } from '../../../services/company-date.service';

const BLOCKED_PATH_PARTS = new Set(['__proto__', 'prototype', 'constructor']);
export type TableDateFormatter = (value: string | number | Date, withTime: boolean) => string;

/** Reads `a.b.c` from a row using own properties only; never evaluates anything. */
export function readPath(row: unknown, path: string): unknown {
  let current: unknown = row;
  for (const part of path.split('.')) {
    if (
      current === null ||
      typeof current !== 'object' ||
      BLOCKED_PATH_PARTS.has(part) ||
      !Object.prototype.hasOwnProperty.call(current, part)
    ) {
      return undefined;
    }
    current = (current as Record<string, unknown>)[part];
  }
  return current;
}

export function columnValue(row: unknown, column: UiTableColumn): unknown {
  return readPath(row, column.field ?? column.key);
}

export function isEmptyValue(value: unknown): boolean {
  return value === null || value === undefined || value === '';
}

function toNumber(value: unknown): number | null {
  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null;
  }
  if (typeof value === 'string' && value.trim() !== '') {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
}

function toTimestamp(value: unknown): number | null {
  if (value instanceof Date) {
    const time = value.getTime();
    return Number.isNaN(time) ? null : time;
  }
  if (typeof value === 'string' || typeof value === 'number') {
    const time = new Date(value).getTime();
    return Number.isNaN(time) ? null : time;
  }
  return null;
}

/** Display text for a cell. Search matches against this, so users find what they actually see. */
export function formatCellText(column: UiTableColumn, value: unknown, dates?: TableDateFormatter): string {
  if (isEmptyValue(value)) {
    return '';
  }
  if (dates && column.type === 'date' && isIsoDate(value)) {
    return dates(value, column.dateStyle === 'datetime' || value.includes('T'));
  }

  switch (column.type) {
    case 'number':
    case 'currency': {
      const number = toNumber(value);
      if (number === null) {
        return String(value);
      }
      const isCurrency = column.type === 'currency' && !!column.currencyCode;
      const decimals = column.decimals ?? (column.type === 'currency' ? 2 : undefined);
      return new Intl.NumberFormat(column.locale, {
        style: isCurrency ? 'currency' : 'decimal',
        currency: isCurrency ? column.currencyCode : undefined,
        minimumFractionDigits: decimals,
        maximumFractionDigits: decimals
      }).format(number);
    }
    case 'date': {
      const time = toTimestamp(value);
      if (time === null) {
        return String(value);
      }
      if (dates) return dates(time, column.dateStyle === 'datetime');
      const style = column.dateStyle ?? 'medium';
      const options: Intl.DateTimeFormatOptions =
        style === 'datetime' ? { dateStyle: 'medium', timeStyle: 'short' } : { dateStyle: style };
      return new Intl.DateTimeFormat(column.locale, options).format(time);
    }
    case 'status':
      return column.statusMap?.[String(value)]?.label ?? String(value);
    default:
      return typeof value === 'object' ? '' : String(value);
  }
}

export function isSearchable(column: UiTableColumn): boolean {
  if (column.visible === false) {
    return false;
  }
  return column.searchable ?? column.type !== 'custom';
}

export function cleanFilters(filters: Record<string, string>): Record<string, string> {
  const cleaned: Record<string, string> = {};
  for (const [key, value] of Object.entries(filters)) {
    const trimmed = (value ?? '').trim();
    if (trimmed) {
      cleaned[key] = trimmed;
    }
  }
  return cleaned;
}

export function sameRecord(a: Record<string, string>, b: Record<string, string>): boolean {
  const aKeys = Object.keys(a);
  return aKeys.length === Object.keys(b).length && aKeys.every(key => a[key] === b[key]);
}

/** Search (every word must match some searchable column) combined with column filters. */
export function filterRows<T>(
  rows: readonly T[],
  columns: readonly UiTableColumn[],
  search: string,
  filters: Record<string, string>,
  dates?: TableDateFormatter
): T[] {
  const tokens = search.toLowerCase().split(/\s+/).filter(Boolean);
  const searchable = columns.filter(isSearchable);
  const activeFilters = Object.entries(filters)
    .filter(([, value]) => value !== '')
    .map(([key, value]) => ({ column: columns.find(c => c.key === key), value }))
    .filter((entry): entry is { column: UiTableColumn; value: string } => !!entry.column);

  return rows.filter(row => {
    for (const { column, value } of activeFilters) {
      const raw = columnValue(row, column);
      if (column.filter?.type === 'select') {
        if (String(raw ?? '') !== value) {
          return false;
        }
      } else if (!formatCellText(column, raw, dates).toLowerCase().includes(value.toLowerCase())) {
        return false;
      }
    }

    if (tokens.length === 0) {
      return true;
    }
    const haystacks = searchable.map(column => formatCellText(column, columnValue(row, column), dates).toLowerCase());
    return tokens.every(token => haystacks.some(text => text.includes(token)));
  });
}

function compareValues(column: UiTableColumn, a: unknown, b: unknown): number {
  switch (column.type) {
    case 'number':
    case 'currency':
      return (toNumber(a) ?? 0) - (toNumber(b) ?? 0);
    case 'date':
      return (toTimestamp(a) ?? 0) - (toTimestamp(b) ?? 0);
    default:
      return formatCellText(column, a).localeCompare(formatCellText(column, b), undefined, {
        numeric: true,
        sensitivity: 'base'
      });
  }
}

function isMissing(column: UiTableColumn, value: unknown): boolean {
  if (isEmptyValue(value)) {
    return true;
  }
  if (column.type === 'number' || column.type === 'currency') {
    return toNumber(value) === null;
  }
  if (column.type === 'date') {
    return toTimestamp(value) === null;
  }
  return false;
}

/**
 * Stable sort on a copy. Empty values always sort last (in both directions) and ties keep their
 * original order, so repeated sorting never shuffles equal rows. Unknown or unsortable columns leave
 * the order untouched.
 */
export function sortRows<T>(rows: readonly T[], columns: readonly UiTableColumn[], sort: UiTableSort | null): T[] {
  const column = sort ? columns.find(c => c.key === sort.columnKey) : undefined;
  if (!sort || !column || !column.sortable) {
    return [...rows];
  }

  const factor = sort.direction === 'asc' ? 1 : -1;
  return rows
    .map((row, index) => ({ row, index, value: column.sortField ? readPath(row, column.sortField) : columnValue(row, column) }))
    .sort((a, b) => {
      const aMissing = isMissing(column, a.value);
      const bMissing = isMissing(column, b.value);
      if (aMissing || bMissing) {
        return aMissing === bMissing ? a.index - b.index : aMissing ? 1 : -1;
      }
      return factor * compareValues(column, a.value, b.value) || a.index - b.index;
    })
    .map(entry => entry.row);
}

export function totalPagesFor(total: number, pageSize: number): number {
  return Math.max(1, Math.ceil(total / Math.max(1, pageSize)));
}

export function paginate<T>(rows: readonly T[], page: number, pageSize: number): T[] {
  const start = (Math.max(1, page) - 1) * pageSize;
  return rows.slice(start, start + pageSize);
}

export interface ClientTableState {
  search: string;
  filters: Record<string, string>;
  sort: UiTableSort | null;
  page: number;
  pageSize: number;
}

export interface ClientTableResult<T> {
  /** Rows of the (clamped) current page. */
  rows: T[];
  /** Rows left after search/filters, before pagination. */
  total: number;
  page: number;
}

/** Filtering/search, then sorting, then pagination. The input array is never mutated. */
export function applyClientQuery<T>(
  rows: readonly T[],
  columns: readonly UiTableColumn[],
  state: ClientTableState,
  dates?: TableDateFormatter
): ClientTableResult<T> {
  const filtered = filterRows(rows, columns, state.search, state.filters, dates);
  const sorted = sortRows(filtered, columns, state.sort);
  const page = Math.min(Math.max(1, state.page), totalPagesFor(sorted.length, state.pageSize));
  return { rows: paginate(sorted, page, state.pageSize), total: sorted.length, page };
}
