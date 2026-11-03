import { UiBadgeVariant } from './ui-badge.component';

export type UiSortDirection = 'asc' | 'desc';

/** Sorting is always by stable column key, never by the display label. */
export interface UiTableSort {
  columnKey: string;
  direction: UiSortDirection;
}

export type UiColumnType = 'text' | 'number' | 'date' | 'currency' | 'status' | 'custom';
export type UiColumnAlign = 'start' | 'center' | 'end';

export interface UiStatusOption {
  label: string;
  variant: UiBadgeVariant;
}

export interface UiColumnFilterOption {
  value: string;
  label: string;
}

/** Optional per-column filter shown under the header: free text, or one of a fixed set of options. */
export interface UiColumnFilter {
  type: 'text' | 'select';
  options?: UiColumnFilterOption[];
  placeholder?: string;
}

/**
 * Declarative column definition. It is plain data: nothing in it is evaluated as an expression or
 * rendered as HTML. Complex cells use an `<ng-template uiTableCell="key">` supplied by the feature.
 */
export interface UiTableColumn {
  /** Stable identifier used for sorting, filtering, cell templates and server queries. */
  key: string;
  header: string;
  /** Property path read from each row (dot notation, e.g. "category.name"). Defaults to `key`. */
  field?: string;
  /** Raw sort key for a formatted summary, so date order never depends on its display format. */
  sortField?: string;
  type?: UiColumnType;
  /** Defaults to false. */
  sortable?: boolean;
  /** Included in global search. Defaults to true except for `custom` columns. Hidden columns are never searched. */
  searchable?: boolean;
  filter?: UiColumnFilter;
  /** Defaults to end for number/currency and start otherwise. */
  align?: UiColumnAlign;
  /** Any CSS width, e.g. "8rem" or "20%". */
  width?: string;
  /** Defaults to true. */
  visible?: boolean;
  /** Placeholder for null/empty values. Defaults to an em dash. */
  emptyText?: string;
  locale?: string;
  /** Number and currency fraction digits. */
  decimals?: number;
  /** ISO 4217 code; without it currency columns are formatted as plain numbers. */
  currencyCode?: string;
  dateStyle?: 'short' | 'medium' | 'long' | 'datetime';
  /** Maps raw status values to a label and badge variant; unknown values fall back to a neutral badge. */
  statusMap?: Record<string, UiStatusOption>;
}

export type UiTableQueryReason = 'init' | 'search' | 'filter' | 'sort' | 'page' | 'pageSize' | 'reset';

/**
 * Everything a server-side table needs to ask its API for the right page. The feature maps it to its
 * own contract (`pageNumber` vs `page`, `sortBy`, ...). Search, filters and sort apply to the whole
 * dataset, never just the rows currently loaded.
 */
export interface UiTableQuery {
  search: string;
  /** Keys of the columns the global search should cover. */
  searchColumns: string[];
  /** Only non-empty column filters, by column key. */
  filters: Record<string, string>;
  sort: UiTableSort | null;
  /** 1-based. */
  page: number;
  pageSize: number;
  /** Increases with every emitted query; echo it back through `responseSequence` to ignore stale responses. */
  sequence: number;
  reason: UiTableQueryReason;
}
