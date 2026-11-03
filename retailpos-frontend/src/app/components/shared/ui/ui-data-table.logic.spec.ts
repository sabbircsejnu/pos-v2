import {
  applyClientQuery,
  cleanFilters,
  filterRows,
  formatCellText,
  paginate,
  readPath,
  sameRecord,
  sortRows,
  totalPagesFor
} from './ui-data-table.logic';
import { UiTableColumn } from './ui-data-table.types';

interface Row {
  id: number;
  name: string;
  qty: number | null;
  price: number | string | null;
  created: string | null;
  status: string;
  category: { name: string };
}

const COLUMNS: UiTableColumn[] = [
  { key: 'name', header: 'Name', sortable: true },
  { key: 'qty', header: 'Qty', type: 'number', locale: 'en-US', sortable: true },
  { key: 'price', header: 'Price', type: 'currency', currencyCode: 'USD', locale: 'en-US', sortable: true },
  { key: 'created', header: 'Created', type: 'date', locale: 'en-US', sortable: true },
  {
    key: 'status',
    header: 'Status',
    type: 'status',
    sortable: true,
    statusMap: { A: { label: 'Active', variant: 'success' }, I: { label: 'Inactive', variant: 'neutral' } }
  },
  { key: 'category', header: 'Category', field: 'category.name', sortable: true },
  { key: 'actions', header: 'Actions', type: 'custom' }
];

const ROWS: Row[] = [
  { id: 1, name: 'banana', qty: 10, price: 1.5, created: '2026-03-15T12:00:00Z', status: 'A', category: { name: 'Fruit' } },
  { id: 2, name: 'Apple', qty: 2, price: '12.25', created: '2026-01-10T12:00:00Z', status: 'I', category: { name: 'Fruit' } },
  { id: 3, name: 'cherry', qty: null, price: null, created: null, status: 'A', category: { name: 'Berry' } },
  { id: 4, name: 'apple pie', qty: 2, price: 8, created: '2026-02-01T12:00:00Z', status: 'A', category: { name: 'Bakery' } },
  { id: 5, name: 'Item 10', qty: 100, price: 3, created: '2026-04-01T12:00:00Z', status: 'A', category: { name: 'Misc' } },
  { id: 6, name: 'Item 9', qty: 20, price: 4, created: '2026-05-01T12:00:00Z', status: 'A', category: { name: 'Misc' } }
];

const ids = (rows: Row[]) => rows.map(row => row.id);

describe('readPath', () => {
  it('reads nested own properties and returns undefined for anything missing', () => {
    expect(readPath(ROWS[0], 'category.name')).toBe('Fruit');
    expect(readPath(ROWS[0], 'category.missing')).toBeUndefined();
    expect(readPath(null, 'a')).toBeUndefined();
  });

  it('never walks the prototype chain, so config cannot reach inherited or dangerous members', () => {
    expect(readPath({}, '__proto__')).toBeUndefined();
    expect(readPath({}, 'constructor')).toBeUndefined();
    expect(readPath({}, 'toString')).toBeUndefined();
    expect(readPath({ a: {} }, 'a.constructor.name')).toBeUndefined();
  });
});

describe('formatCellText', () => {
  it('formats numbers, currency, dates and statuses for display', () => {
    expect(formatCellText(COLUMNS[1], 1234.5)).toBe('1,234.5');
    expect(formatCellText(COLUMNS[2], '12.25')).toBe('$12.25');
    expect(formatCellText(COLUMNS[3], '2026-03-15T12:00:00Z')).toContain('2026');
    expect(formatCellText(COLUMNS[4], 'A')).toBe('Active');
    expect(formatCellText(COLUMNS[4], 'unknown')).toBe('unknown');
  });

  it('formats currency columns as plain two-decimal numbers when no currency code is configured', () => {
    expect(formatCellText({ key: 'p', header: 'P', type: 'currency', locale: 'en-US' }, 5)).toBe('5.00');
  });

  it('returns empty text for null/undefined/empty and never renders objects', () => {
    expect(formatCellText(COLUMNS[0], null)).toBe('');
    expect(formatCellText(COLUMNS[0], undefined)).toBe('');
    expect(formatCellText(COLUMNS[0], '')).toBe('');
    expect(formatCellText(COLUMNS[0], { toString: () => 'x' })).toBe('');
  });

  it('falls back to the raw value for unparseable numbers and dates', () => {
    expect(formatCellText(COLUMNS[1], 'n/a')).toBe('n/a');
    expect(formatCellText(COLUMNS[3], 'not a date')).toBe('not a date');
  });
});

describe('filterRows', () => {
  it('matches case-insensitively against displayed text of searchable columns', () => {
    expect(ids(filterRows(ROWS, COLUMNS, 'APPLE', {}))).toEqual([2, 4]);
    expect(ids(filterRows(ROWS, COLUMNS, '$12', {}))).toEqual([2]);
    expect(ids(filterRows(ROWS, COLUMNS, 'inactive', {}))).toEqual([2]);
  });

  it('requires every word to match somewhere (AND) and can span columns', () => {
    expect(ids(filterRows(ROWS, COLUMNS, 'apple fruit', {}))).toEqual([2]);
    expect(ids(filterRows(ROWS, COLUMNS, 'apple berry', {}))).toEqual([]);
  });

  it('only searches searchable, visible columns', () => {
    const cols: UiTableColumn[] = [
      { key: 'name', header: 'Name', searchable: false },
      { key: 'category', header: 'Category', field: 'category.name' },
      { key: 'status', header: 'Status', visible: false }
    ];
    expect(ids(filterRows(ROWS, cols, 'apple', {}))).toEqual([]);
    expect(ids(filterRows(ROWS, cols, 'berry', {}))).toEqual([3]);
    expect(ids(filterRows(ROWS, cols, 'inactive', {}))).toEqual([]);
  });

  it('applies text and select column filters together with the search', () => {
    const cols: UiTableColumn[] = [
      { key: 'name', header: 'Name', filter: { type: 'text' } },
      { key: 'status', header: 'Status', filter: { type: 'select' } }
    ];
    expect(ids(filterRows(ROWS, cols, '', { name: 'apple' }))).toEqual([2, 4]);
    expect(ids(filterRows(ROWS, cols, '', { name: 'apple', status: 'A' }))).toEqual([4]);
    expect(ids(filterRows(ROWS, cols, 'pie', { status: 'A' }))).toEqual([4]);
    expect(ids(filterRows(ROWS, cols, '', { unknownColumn: 'x' }))).toEqual(ids(ROWS));
  });

  it('returns every row for an empty search without copying surprises', () => {
    expect(filterRows(ROWS, COLUMNS, '   ', {})).toHaveLength(ROWS.length);
  });
});

describe('sortRows', () => {
  it('sorts text ascending/descending using natural, case-insensitive order', () => {
    expect(ids(sortRows(ROWS, COLUMNS, { columnKey: 'name', direction: 'asc' }))).toEqual([2, 4, 1, 3, 6, 5]);
    expect(ids(sortRows(ROWS, COLUMNS, { columnKey: 'name', direction: 'desc' }))).toEqual([5, 6, 3, 1, 4, 2]);
  });

  it('sorts numbers and currency numerically, including numeric strings', () => {
    expect(ids(sortRows(ROWS, COLUMNS, { columnKey: 'qty', direction: 'asc' })).slice(0, 3)).toEqual([2, 4, 1]);
    expect(ids(sortRows(ROWS, COLUMNS, { columnKey: 'price', direction: 'asc' }))).toEqual([1, 5, 6, 4, 2, 3]);
    expect(ids(sortRows(ROWS, COLUMNS, { columnKey: 'price', direction: 'desc' }))).toEqual([2, 4, 6, 5, 1, 3]);
  });

  it('sorts dates chronologically', () => {
    expect(ids(sortRows(ROWS, COLUMNS, { columnKey: 'created', direction: 'asc' }))).toEqual([2, 4, 1, 5, 6, 3]);
  });

  it('keeps empty values last in both directions', () => {
    expect(ids(sortRows(ROWS, COLUMNS, { columnKey: 'qty', direction: 'asc' })).at(-1)).toBe(3);
    expect(ids(sortRows(ROWS, COLUMNS, { columnKey: 'qty', direction: 'desc' })).at(-1)).toBe(3);
  });

  it('is stable: equal values keep their original relative order in both directions', () => {
    // rows 2 and 4 both have qty 2; row 2 comes first in the input
    const asc = ids(sortRows(ROWS, COLUMNS, { columnKey: 'qty', direction: 'asc' }));
    const desc = ids(sortRows(ROWS, COLUMNS, { columnKey: 'qty', direction: 'desc' }));
    expect(asc.indexOf(2)).toBeLessThan(asc.indexOf(4));
    expect(desc.indexOf(2)).toBeLessThan(desc.indexOf(4));
  });

  it('sorts by the status label that users see, and through nested field paths', () => {
    expect(ids(sortRows(ROWS, COLUMNS, { columnKey: 'status', direction: 'asc' })).at(-1)).toBe(2);
    expect(ids(sortRows(ROWS, COLUMNS, { columnKey: 'category', direction: 'asc' }))[0]).toBe(4);
  });

  it('leaves the order untouched for no sort, unknown or non-sortable columns, and never mutates the input', () => {
    const original = [...ROWS];
    expect(ids(sortRows(ROWS, COLUMNS, null))).toEqual(ids(ROWS));
    expect(ids(sortRows(ROWS, COLUMNS, { columnKey: 'nope', direction: 'asc' }))).toEqual(ids(ROWS));
    expect(ids(sortRows(ROWS, COLUMNS, { columnKey: 'actions', direction: 'asc' }))).toEqual(ids(ROWS));
    expect(ROWS).toEqual(original);
  });

  it('identifies columns by key, never by header label', () => {
    expect(ids(sortRows(ROWS, COLUMNS, { columnKey: 'Name', direction: 'asc' }))).toEqual(ids(ROWS));
  });
});

describe('paginate, totalPagesFor and helpers', () => {
  it('slices pages and reports at least one page', () => {
    expect(paginate([1, 2, 3, 4, 5], 2, 2)).toEqual([3, 4]);
    expect(paginate([1, 2, 3, 4, 5], 3, 2)).toEqual([5]);
    expect(totalPagesFor(0, 10)).toBe(1);
    expect(totalPagesFor(21, 10)).toBe(3);
  });

  it('cleans filters and compares records', () => {
    expect(cleanFilters({ a: ' x ', b: '  ', c: '' })).toEqual({ a: 'x' });
    expect(sameRecord({ a: '1' }, { a: '1' })).toBe(true);
    expect(sameRecord({ a: '1' }, { a: '2' })).toBe(false);
    expect(sameRecord({ a: '1' }, {})).toBe(false);
  });
});

describe('applyClientQuery', () => {
  it('filters first, then sorts, then paginates', () => {
    const result = applyClientQuery(ROWS, COLUMNS, {
      search: 'item',
      filters: {},
      sort: { columnKey: 'qty', direction: 'desc' },
      page: 1,
      pageSize: 1
    });
    // only "Item 10" and "Item 9" match; sorted by qty desc => 10 (qty 100) first; page size 1
    expect(result.total).toBe(2);
    expect(ids(result.rows)).toEqual([5]);
  });

  it('paginates the filtered and sorted set, not the original', () => {
    const second = applyClientQuery(ROWS, COLUMNS, {
      search: 'item',
      filters: {},
      sort: { columnKey: 'qty', direction: 'desc' },
      page: 2,
      pageSize: 1
    });
    expect(ids(second.rows)).toEqual([6]);
  });

  it('clamps an out-of-range page and reports the page it used', () => {
    const result = applyClientQuery(ROWS, COLUMNS, { search: '', filters: {}, sort: null, page: 99, pageSize: 4 });
    expect(result.page).toBe(2);
    expect(ids(result.rows)).toEqual([5, 6]);
  });

  it('does not mutate the input array or its rows', () => {
    const input = ROWS.map(row => ({ ...row }));
    const snapshot = JSON.stringify(input);
    applyClientQuery(input, COLUMNS, { search: 'a', filters: { name: 'a' }, sort: { columnKey: 'name', direction: 'desc' }, page: 1, pageSize: 2 });
    expect(JSON.stringify(input)).toBe(snapshot);
  });
});
