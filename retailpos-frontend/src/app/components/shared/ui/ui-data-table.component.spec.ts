import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { UiDataTableComponent, UiTableDensity, UiTableMode, UiTableSelectionMode } from './ui-data-table.component';
import { UiTableCellDirective, UiTableRowActionsDirective } from './ui-data-table.directives';
import { UiActionButtonDirective } from './ui-action-button.directive';
import { UiTableColumn, UiTableQuery, UiTableSort } from './ui-data-table.types';

interface Person {
  id: number;
  name: string;
  qty: number;
  price: number;
  status: string;
}

const COLUMNS: UiTableColumn[] = [
  { key: 'name', header: 'Person', sortable: true, filter: { type: 'text', placeholder: 'Filter people' } },
  { key: 'qty', header: 'Quantity', type: 'number', sortable: true, locale: 'en-US' },
  { key: 'price', header: 'Price', type: 'currency', currencyCode: 'USD', locale: 'en-US', sortable: true },
  {
    key: 'status',
    header: 'Status',
    type: 'status',
    filter: { type: 'select', options: [{ value: 'A', label: 'Active' }, { value: 'I', label: 'Inactive' }] },
    statusMap: { A: { label: 'Active', variant: 'success' }, I: { label: 'Inactive', variant: 'neutral' } }
  },
  { key: 'notes', header: 'Notes', visible: false }
];

function makePeople(count: number): Person[] {
  return Array.from({ length: count }, (_, i) => ({
    id: i + 1,
    name: `Person ${String(i + 1).padStart(2, '0')}`,
    qty: (i * 7) % 13,
    price: (i + 1) * 1.5,
    status: i % 2 === 0 ? 'A' : 'I'
  }));
}

@Component({
  standalone: true,
  imports: [UiDataTableComponent, UiTableCellDirective, UiTableRowActionsDirective, UiActionButtonDirective],
  template: `
    <app-ui-data-table
      [columns]="columns()"
      [rows]="rows()"
      [mode]="mode()"
      [totalRecords]="total()"
      [loading]="loading()"
      [error]="error()"
      [responseSequence]="sequence()"
      [selectionMode]="selection()"
      [(selectedKeys)]="selected"
      [density]="density()"
      [defaultSort]="defaultSort()"
      [allowUnsorted]="allowUnsorted()"
      [initialPageSize]="initialPageSize()"
      [initialPage]="initialPage()"
      [initialSearch]="initialSearch()"
      [initialFilters]="initialFilters()"
      [emitInitialQuery]="emitInitial()"
      [maxHeight]="maxHeight()"
      [searchDebounceMs]="debounce()"
      caption="People"
      emptyTitle="No people yet"
      (queryChange)="queries.push($event)"
      (retry)="retries = retries + 1"
      [rowClickable]="rowClickable()"
      (rowClick)="clicked.push($event.id)">
      <button uiTableToolbar type="button" id="extra">Extra</button>
      @if (customName()) {
        <ng-template uiTableCell="name" let-text="text"><strong class="custom-name">{{ text }}!</strong></ng-template>
      }
      @if (withActions()) {
        <ng-template uiTableRowActions let-row>
          <button appUiActionButton action="edit" class="row-action" [attr.aria-label]="'Edit Person ' + row.id" (click)="acted.push(row.id)">
            <i class="fas fa-pen" aria-hidden="true"></i> Edit {{ row.id }}
          </button>
        </ng-template>
      }
    </app-ui-data-table>
  `
})
class HostComponent {
  columns = signal<UiTableColumn[]>(COLUMNS);
  rows = signal<readonly Person[]>(makePeople(25));
  mode = signal<UiTableMode>('client');
  total = signal<number | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);
  sequence = signal<number | null>(null);
  selection = signal<UiTableSelectionMode>('none');
  selected = signal<readonly (string | number)[]>([]);
  density = signal<UiTableDensity>('comfortable');
  defaultSort = signal<UiTableSort | null>(null);
  allowUnsorted = signal(true);
  initialPageSize = signal<number | null>(null);
  initialPage = signal(1);
  initialSearch = signal('');
  initialFilters = signal<Record<string, string>>({});
  emitInitial = signal(true);
  maxHeight = signal<string | null>(null);
  debounce = signal(300);
  customName = signal(false);
  withActions = signal(false);
  rowClickable = signal(false);

  queries: UiTableQuery[] = [];
  retries = 0;
  acted: number[] = [];
  clicked: number[] = [];
}

describe('UiDataTableComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;
  const root = () => fixture.nativeElement as HTMLElement;
  const q = <E extends HTMLElement = HTMLElement>(selector: string) => root().querySelector(selector) as E;
  const qa = <E extends HTMLElement = HTMLElement>(selector: string) => Array.from(root().querySelectorAll(selector)) as E[];
  const names = () => qa('tbody tr').map(row => row.querySelector('td')!.textContent!.trim());
  const rowsText = () => qa('tbody tr').map(row => (row.querySelector('td:not(.ui-table__select)') as HTMLElement).textContent!.trim());
  const header = (label: string) => qa('thead th').find(th => th.textContent!.trim().startsWith(label))!;
  const sortButton = (label: string) => header(label).querySelector('button') as HTMLButtonElement;
  const lastQuery = () => host.queries[host.queries.length - 1];

  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const flush = () => {
    fixture.detectChanges();
    fixture.detectChanges();
  };

  async function create(configure?: (h: HostComponent) => void): Promise<void> {
    TestBed.configureTestingModule({ imports: [HostComponent] });
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    configure?.(host);
    await settle();
  }

  function type(input: HTMLInputElement, value: string): void {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  const searchInput = () => q<HTMLInputElement>('.ui-table__search-input');
  const nextButton = () => q<HTMLButtonElement>('button[aria-label="Next page"]');
  const prevButton = () => q<HTMLButtonElement>('button[aria-label="Previous page"]');
  const firstButton = () => q<HTMLButtonElement>('button[aria-label="First page"]');
  const lastButton = () => q<HTMLButtonElement>('button[aria-label="Last page"]');

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  describe('client mode', () => {
    it('renders visible columns, the first page, the range and the page indicator', async () => {
      await create();
      expect(qa('thead tr:first-child th').map(th => th.textContent!.trim())).toEqual(['Person', 'Quantity', 'Price', 'Status']);
      expect(qa('tbody tr')).toHaveLength(10);
      expect(q('.ui-table__range').textContent).toBe('Showing 1–10 of 25');
      expect(q('.ui-table__page-indicator').textContent).toBe('Page 1 of 3');
    });

    it('formats typed cells: currency and numbers aligned to the end, status as a text badge', async () => {
      await create();
      const cells = qa('tbody tr:first-child td');
      expect(cells[2].textContent!.trim()).toBe('$1.50');
      expect(cells[2].getAttribute('data-align')).toBe('end');
      expect(cells[1].getAttribute('data-align')).toBe('end');
      expect(cells[0].getAttribute('data-align')).toBe('start');
      const badge = cells[3].querySelector('app-ui-badge')!;
      expect(badge.textContent!.trim()).toBe('Active');
      expect(badge.getAttribute('data-variant')).toBe('success');
    });

    it('exposes an accessible scroll region and caption and supports toolbar content', async () => {
      await create();
      const region = q('.ui-table__scroll');
      expect(region.getAttribute('role')).toBe('region');
      expect(region.getAttribute('aria-label')).toBe('People');
      expect(region.getAttribute('tabindex')).toBe('0');
      expect(q('caption').textContent).toBe('People');
      expect(q('.ui-table__toolbar-extra #extra')).not.toBeNull();
    });

    it('applies density, sticky header and a max height to the scroll area', async () => {
      await create(h => { h.density.set('compact'); h.maxHeight.set('20rem'); });
      expect(q('.ui-table').getAttribute('data-density')).toBe('compact');
      expect(q('table').classList).toContain('ui-table__table--sticky');
      expect(q('.ui-table__scroll').style.maxHeight).toBe('20rem');
    });

    describe('sorting', () => {
      it('marks sortable columns with aria-sort and leaves other columns without it', async () => {
        await create();
        expect(header('Person').getAttribute('aria-sort')).toBe('none');
        expect(header('Quantity').getAttribute('aria-sort')).toBe('none');
        expect(header('Status').hasAttribute('aria-sort')).toBe(false);
        expect(header('Status').querySelector('button')).toBeNull();
      });

      it('cycles ascending, descending and unsorted, updating aria-sort, indicator and rows', async () => {
        await create();
        sortButton('Quantity').click();
        flush();
        expect(header('Quantity').getAttribute('aria-sort')).toBe('ascending');
        expect(header('Quantity').querySelector('.ui-table__sort-icon')!.getAttribute('data-state')).toBe('asc');
        const asc = qa('tbody tr').map(tr => Number(tr.querySelectorAll('td')[1].textContent));
        expect(asc).toEqual([...asc].sort((a, b) => a - b));

        sortButton('Quantity').click();
        flush();
        expect(header('Quantity').getAttribute('aria-sort')).toBe('descending');
        const desc = qa('tbody tr').map(tr => Number(tr.querySelectorAll('td')[1].textContent));
        expect(desc).toEqual([...desc].sort((a, b) => b - a));

        sortButton('Quantity').click();
        flush();
        expect(header('Quantity').getAttribute('aria-sort')).toBe('none');
        expect(names()[0]).toBe('Person 01');
      });

      it('can disable the unsorted state so sorting alternates ascending and descending', async () => {
        await create(h => h.allowUnsorted.set(false));
        const states: (string | null)[] = [];
        for (let i = 0; i < 4; i++) {
          sortButton('Person').click();
          flush();
          states.push(header('Person').getAttribute('aria-sort'));
        }
        expect(states).toEqual(['ascending', 'descending', 'ascending', 'descending']);
      });

      it('sorting a different column replaces the sort and a new sort returns to page 1', async () => {
        await create();
        nextButton().click();
        flush();
        sortButton('Person').click();
        flush();
        sortButton('Price').click();
        flush();
        expect(header('Person').getAttribute('aria-sort')).toBe('none');
        expect(header('Price').getAttribute('aria-sort')).toBe('ascending');
        expect(q('.ui-table__page-indicator').textContent).toBe('Page 1 of 3');
      });

      it('applies a configured default sort on first render', async () => {
        await create(h => h.defaultSort.set({ columnKey: 'name', direction: 'desc' }));
        expect(header('Person').getAttribute('aria-sort')).toBe('descending');
        expect(names()[0]).toBe('Person 25');
      });

      it('sorts the whole filtered set, not just the visible page, and never mutates or reorders the input', async () => {
        const rows = Object.freeze(makePeople(25));
        await create(h => h.rows.set(rows));
        sortButton('Price').click();
        flush();
        sortButton('Price').click();
        flush();
        expect(names()[0]).toBe('Person 25');
        expect(rows[0].name).toBe('Person 01');
      });

      it('emits the stable column key, not the header label', async () => {
        await create();
        sortButton('Quantity').click();
        flush();
        expect(lastQuery().sort).toEqual({ columnKey: 'qty', direction: 'asc' });
      });

      it('renders a native, keyboard-focusable button for sortable headers', async () => {
        await create();
        const button = sortButton('Person');
        expect(button.tagName).toBe('BUTTON');
        expect(button.type).toBe('button');
        expect(button.tabIndex).toBe(0);
        expect(button.querySelector('.ui-table__sort-icon')!.getAttribute('aria-hidden')).toBe('true');
      });
    });

    describe('global search', () => {
      it('debounces typing: nothing is applied until the pause, then rows are filtered', async () => {
        await create();
        vi.useFakeTimers();
        type(searchInput(), 'inactive');
        vi.advanceTimersByTime(200);
        flush();
        expect(qa('tbody tr')).toHaveLength(10);
        expect(q('.ui-table__range').textContent).toBe('Showing 1–10 of 25');

        vi.advanceTimersByTime(100);
        flush();
        expect(q('.ui-table__range').textContent).toBe('Showing 1–10 of 12');
        expect(qa('tbody tr td:nth-child(4)').every(cell => cell.textContent!.trim() === 'Inactive')).toBe(true);
      });

      it('restarts the debounce on every keystroke and applies only the final term', async () => {
        await create();
        vi.useFakeTimers();
        for (const value of ['P', 'Pe', 'Per', 'Pers', 'Person 2']) {
          type(searchInput(), value);
          vi.advanceTimersByTime(100);
        }
        expect(host.queries.filter(query => query.reason === 'search')).toHaveLength(0);
        vi.advanceTimersByTime(300);
        flush();
        expect(host.queries.filter(query => query.reason === 'search')).toHaveLength(1);
        expect(lastQuery().search).toBe('Person 2');
      });

      it('honours a custom debounce delay', async () => {
        await create(h => h.debounce.set(600));
        vi.useFakeTimers();
        type(searchInput(), 'Person 07');
        vi.advanceTimersByTime(550);
        flush();
        expect(qa('tbody tr')).toHaveLength(10);
        vi.advanceTimersByTime(60);
        flush();
        expect(names()).toEqual(['Person 07']);
      });

      it('applies immediately on Enter without waiting for the debounce', async () => {
        await create();
        vi.useFakeTimers();
        type(searchInput(), 'Person 07');
        searchInput().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }));
        flush();
        expect(names()).toEqual(['Person 07']);
      });

      it('shows a clear button that resets the search immediately and focus-safe state', async () => {
        await create();
        vi.useFakeTimers();
        expect(q('.ui-table__search-clear')).toBeNull();
        type(searchInput(), 'Person 07');
        flush();
        expect(q('.ui-table__search-clear')).not.toBeNull();
        vi.advanceTimersByTime(300);
        flush();
        expect(qa('tbody tr')).toHaveLength(1);

        q<HTMLButtonElement>('.ui-table__search-clear').click();
        flush();
        expect(searchInput().value).toBe('');
        expect(qa('tbody tr')).toHaveLength(10);
        expect(q('.ui-table__search-clear')).toBeNull();
      });

      it('clears the search with Escape', async () => {
        await create();
        vi.useFakeTimers();
        type(searchInput(), 'Person 07');
        vi.advanceTimersByTime(300);
        flush();
        searchInput().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', cancelable: true }));
        flush();
        expect(qa('tbody tr')).toHaveLength(10);
      });

      it('searches displayed text, including formatted currency and status labels', async () => {
        await create();
        vi.useFakeTimers();
        type(searchInput(), '$37.50');
        searchInput().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }));
        flush();
        expect(names()).toEqual(['Person 25']);

        type(searchInput(), 'inactive');
        searchInput().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }));
        flush();
        expect(q('.ui-table__range').textContent).toBe('Showing 1–10 of 12');
      });

      it('never searches hidden columns', async () => {
        await create(h => h.rows.set(makePeople(25).map(p => ({ ...p, notes: 'secret' })) as never));
        vi.useFakeTimers();
        type(searchInput(), 'secret');
        searchInput().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }));
        flush();
        expect(q('app-ui-state')!.textContent).toContain('No matching results');
      });

      it('resets to page 1 when the search changes and keeps the term while paging', async () => {
        await create();
        vi.useFakeTimers();
        nextButton().click();
        flush();
        expect(q('.ui-table__page-indicator').textContent).toBe('Page 2 of 3');

        type(searchInput(), 'inactive');
        vi.advanceTimersByTime(300);
        flush();
        expect(q('.ui-table__page-indicator').textContent).toBe('Page 1 of 2');

        nextButton().click();
        flush();
        expect(searchInput().value).toBe('inactive');
        expect(q('.ui-table__page-indicator').textContent).toBe('Page 2 of 2');
        expect(names()).toEqual(['Person 22', 'Person 24']);
        expect(q('.ui-table__range').textContent).toBe('Showing 11–12 of 12');
      });

      it('can hide the search box and offers an accessible name and placeholder', async () => {
        await create();
        expect(searchInput().getAttribute('aria-label')).toBe('Search table');
        expect(searchInput().getAttribute('placeholder')).toBe('Search…');
        expect(q('[role="search"]')).not.toBeNull();
      });
    });

    describe('column filters', () => {
      it('filters by a text column after the debounce and by a select column immediately', async () => {
        await create();
        vi.useFakeTimers();
        const textFilter = q<HTMLInputElement>('input[aria-label="Filter Person"]');
        type(textFilter, 'Person 2');
        vi.advanceTimersByTime(300);
        flush();
        expect(q('.ui-table__range').textContent).toBe('Showing 1–6 of 6');

        const select = q<HTMLSelectElement>('select[aria-label="Filter Status"]');
        select.value = 'A';
        select.dispatchEvent(new Event('change'));
        flush();
        expect(names()).toEqual(['Person 21', 'Person 23', 'Person 25']);
        expect(lastQuery().filters).toEqual({ name: 'Person 2', status: 'A' });
      });

      it('combines filters with search and resets the page', async () => {
        await create();
        vi.useFakeTimers();
        nextButton().click();
        flush();
        const select = q<HTMLSelectElement>('select[aria-label="Filter Status"]');
        select.value = 'I';
        select.dispatchEvent(new Event('change'));
        flush();
        expect(q('.ui-table__page-indicator').textContent).toBe('Page 1 of 2');
        expect(q('.ui-table__range').textContent).toBe('Showing 1–10 of 12');
      });
    });

    describe('pagination', () => {
      it('offers 10, 25, 50 and 100 rows per page and applies a change from page 1', async () => {
        await create();
        const select = q<HTMLSelectElement>('select[aria-label="Rows per page"]');
        expect(Array.from(select.options).map(o => o.value)).toEqual(['10', '25', '50', '100']);

        nextButton().click();
        flush();
        select.value = '25';
        select.dispatchEvent(new Event('change'));
        flush();
        expect(qa('tbody tr')).toHaveLength(25);
        expect(q('.ui-table__page-indicator').textContent).toBe('Page 1 of 1');
        expect(q('.ui-table__range').textContent).toBe('Showing 1–25 of 25');
      });

      it('navigates first, previous, next and last with correct disabled states', async () => {
        await create();
        expect(firstButton().disabled).toBe(true);
        expect(prevButton().disabled).toBe(true);
        expect(nextButton().disabled).toBe(false);

        lastButton().click();
        flush();
        expect(q('.ui-table__page-indicator').textContent).toBe('Page 3 of 3');
        expect(q('.ui-table__range').textContent).toBe('Showing 21–25 of 25');
        expect(nextButton().disabled).toBe(true);
        expect(lastButton().disabled).toBe(true);

        prevButton().click();
        flush();
        expect(q('.ui-table__page-indicator').textContent).toBe('Page 2 of 3');
        firstButton().click();
        flush();
        expect(q('.ui-table__page-indicator').textContent).toBe('Page 1 of 3');
      });

      it('exposes pagination as a labelled navigation with named, native buttons', async () => {
        await create();
        const nav = q('nav');
        expect(nav.getAttribute('aria-label')).toBe('Pagination');
        for (const label of ['First page', 'Previous page', 'Next page', 'Last page']) {
          const button = q<HTMLButtonElement>(`button[aria-label="${label}"]`);
          expect(button.tagName).toBe('BUTTON');
          expect(button.type).toBe('button');
        }
        expect(q('.ui-table__page-indicator').getAttribute('aria-current')).toBe('page');
        expect(q('.ui-table__range').getAttribute('aria-live')).toBe('polite');
      });

      it('starts from the configured page size and page', async () => {
        await create(h => { h.initialPageSize.set(25); h.initialPage.set(1); });
        expect(qa('tbody tr')).toHaveLength(25);

        TestBed.resetTestingModule();
        await create(h => { h.initialPageSize.set(10); h.initialPage.set(3); });
        expect(q('.ui-table__page-indicator').textContent).toBe('Page 3 of 3');
      });

      it('moves back to the last page when the data shrinks underneath the current page', async () => {
        await create();
        lastButton().click();
        flush();
        host.rows.set(makePeople(12));
        await settle();
        expect(q('.ui-table__page-indicator').textContent).toBe('Page 2 of 2');
        expect(qa('tbody tr')).toHaveLength(2);
      });
    });

    describe('states', () => {
      it('shows "no data" when there are no rows and no criteria', async () => {
        await create(h => h.rows.set([]));
        const state = q('app-ui-state');
        expect(state.textContent).toContain('No people yet');
        expect(state.textContent).not.toContain('No matching');
        expect(q('tbody')).toBeNull();
      });

      it('shows "no matching results" for a search with no hits and lets the user clear it', async () => {
        await create();
        vi.useFakeTimers();
        type(searchInput(), 'zzz');
        vi.advanceTimersByTime(300);
        flush();
        const state = q('app-ui-state');
        expect(state.textContent).toContain('No matching results');
        expect(state.textContent).not.toContain('No people yet');

        state.querySelector<HTMLButtonElement>('button')!.click();
        flush();
        expect(searchInput().value).toBe('');
        expect(qa('tbody tr')).toHaveLength(10);
      });

      it('shows a loading state with no rows and keeps existing rows (dimmed, busy) while reloading', async () => {
        await create(h => { h.rows.set([]); h.loading.set(true); });
        expect(q('app-ui-state').getAttribute('role')).toBe('status');

        host.rows.set(makePeople(25));
        flush();
        expect(qa('tbody tr')).toHaveLength(10);
        expect(q('.ui-table').getAttribute('aria-busy')).toBe('true');
      });

      it('shows an error state with a feature-owned retry and keeps the toolbar and footer usable', async () => {
        await create(h => h.error.set('Server unavailable'));
        const state = q('app-ui-state');
        expect(state.getAttribute('role')).toBe('alert');
        expect(state.textContent).toContain('Server unavailable');
        expect(qa('tbody tr')).toHaveLength(0);
        expect(searchInput()).not.toBeNull();
        expect(q('.ui-table__footer')).not.toBeNull();

        state.querySelector<HTMLButtonElement>('button')!.click();
        expect(host.retries).toBe(1);
      });

      it('keeps empty, loading and error independent', async () => {
        await create(h => h.rows.set([]));
        expect(q('app-ui-state').textContent).toContain('No people yet');
        host.loading.set(true);
        flush();
        expect(q('app-ui-state').textContent).toContain('Loading');
        host.loading.set(false);
        host.error.set('Boom');
        flush();
        expect(q('app-ui-state').textContent).toContain('Boom');
      });
    });

    describe('selection', () => {
      it('has no selection column by default', async () => {
        await create();
        expect(q('.ui-table__select')).toBeNull();
      });

      it('selects rows with named checkboxes and reports keys through the model', async () => {
        await create(h => h.selection.set('multiple'));
        const boxes = qa<HTMLInputElement>('tbody input[type="checkbox"]');
        expect(boxes).toHaveLength(10);
        expect(boxes[0].getAttribute('aria-label')).toBe('Select Person 01');
        boxes[0].click();
        boxes[2].click();
        flush();
        expect(host.selected()).toEqual([1, 3]);
        expect(qa('tbody tr.ui-table__row--selected')).toHaveLength(2);
        boxes[0].click();
        flush();
        expect(host.selected()).toEqual([3]);
      });

      it('select-all applies to the current page only, shows indeterminate, and keeps other pages selected', async () => {
        await create(h => h.selection.set('multiple'));
        const all = () => q<HTMLInputElement>('thead input[type="checkbox"]');
        expect(all().getAttribute('aria-label')).toBe('Select all rows on this page');

        all().click();
        flush();
        expect(host.selected()).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        expect(all().checked).toBe(true);

        nextButton().click();
        flush();
        expect(all().checked).toBe(false);
        expect(all().indeterminate).toBe(false);
        qa<HTMLInputElement>('tbody input[type="checkbox"]')[0].click();
        flush();
        expect(all().indeterminate).toBe(true);
        expect(host.selected()).toHaveLength(11);

        all().click();
        flush();
        expect(host.selected()).toHaveLength(20);

        all().click();
        flush();
        expect(host.selected()).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
      });

      it('single selection uses radios and replaces the previous choice', async () => {
        await create(h => h.selection.set('single'));
        expect(q('thead tr:first-child input')).toBeNull();
        const radios = qa<HTMLInputElement>('tbody input[type="radio"]');
        radios[1].click();
        flush();
        expect(host.selected()).toEqual([2]);
        qa<HTMLInputElement>('tbody input[type="radio"]')[4].click();
        flush();
        expect(host.selected()).toEqual([5]);
        expect(new Set(radios.map(radio => radio.name)).size).toBe(1);
      });

      it('restores a pre-existing selection and ignores rows without a key', async () => {
        await create(h => { h.selection.set('multiple'); h.selected.set([2]); });
        expect(qa<HTMLInputElement>('tbody input[type="checkbox"]')[1].checked).toBe(true);
      });
    });

    describe('custom cells and row actions', () => {
      it('renders feature templates for cells and actions and calls feature handlers', async () => {
        await create(h => { h.customName.set(true); h.withActions.set(true); });
        expect(q('.custom-name').textContent).toBe('Person 01!');
        expect(qa('thead tr:first-child th').at(-1)!.textContent).toBe('Actions');
        expect(qa<HTMLButtonElement>('.row-action')[0].getAttribute('aria-label')).toBe('Edit Person 1');
        qa<HTMLButtonElement>('.row-action')[2].click();
        expect(host.acted).toEqual([3]);
      });

      it('escapes cell content instead of rendering it as HTML', async () => {
        await create(h => h.rows.set([{ id: 1, name: '<img src=x onerror=alert(1)>', qty: 1, price: 1, status: 'A' }]));
        expect(q('tbody img')).toBeNull();
        expect(q('tbody td').textContent).toContain('<img');
      });
    });
  });

  describe('server mode', () => {
    const serverSetup = (h: HostComponent) => {
      h.mode.set('server');
      h.rows.set(makePeople(10));
      h.total.set(95);
    };

    it('emits the initial query once after the first render with all query parts', async () => {
      await create(serverSetup);
      expect(host.queries).toHaveLength(1);
      expect(host.queries[0]).toEqual({
        search: '',
        searchColumns: ['name', 'qty', 'price', 'status'],
        filters: {},
        sort: null,
        page: 1,
        pageSize: 10,
        sequence: 1,
        reason: 'init'
      });
    });

    it('can skip the initial query and seeds the query from initial settings', async () => {
      await create(h => { serverSetup(h); h.emitInitial.set(false); });
      expect(host.queries).toHaveLength(0);

      TestBed.resetTestingModule();
      await create(h => {
        serverSetup(h);
        h.initialSearch.set(' tea ');
        h.initialPage.set(2);
        h.initialPageSize.set(25);
        h.initialFilters.set({ status: 'A', name: '' });
        h.defaultSort.set({ columnKey: 'price', direction: 'desc' });
      });
      expect(host.queries[0]).toMatchObject({
        search: 'tea',
        page: 2,
        pageSize: 25,
        filters: { status: 'A' },
        sort: { columnKey: 'price', direction: 'desc' }
      });
    });

    it('shows the server rows and total as given, without client-side slicing, sorting or filtering', async () => {
      await create(h => { serverSetup(h); h.rows.set([...makePeople(10)].reverse()); });
      expect(names()[0]).toBe('Person 10');
      expect(q('.ui-table__range').textContent).toBe('Showing 1–10 of 95');
      expect(q('.ui-table__page-indicator').textContent).toBe('Page 1 of 10');

      sortButton('Person').click();
      flush();
      expect(names()[0]).toBe('Person 10');
      expect(lastQuery().sort).toEqual({ columnKey: 'name', direction: 'asc' });
      expect(lastQuery().reason).toBe('sort');
    });

    it('emits sort queries for the whole dataset and returns to page 1', async () => {
      await create(serverSetup);
      lastButton().click();
      flush();
      expect(lastQuery()).toMatchObject({ page: 10, reason: 'page' });

      sortButton('Quantity').click();
      flush();
      expect(lastQuery()).toMatchObject({ page: 1, sort: { columnKey: 'qty', direction: 'asc' }, reason: 'sort' });
      sortButton('Quantity').click();
      flush();
      expect(lastQuery().sort).toEqual({ columnKey: 'qty', direction: 'desc' });
      sortButton('Quantity').click();
      flush();
      expect(lastQuery().sort).toBeNull();
    });

    it('sends one debounced search query for rapid typing and resets to page 1', async () => {
      await create(serverSetup);
      vi.useFakeTimers();
      nextButton().click();
      flush();
      expect(lastQuery()).toMatchObject({ page: 2, reason: 'page' });
      const before = host.queries.length;

      for (const value of ['t', 'te', 'tea', 'tea ', 'tea b']) {
        type(searchInput(), value);
        vi.advanceTimersByTime(120);
      }
      expect(host.queries.length).toBe(before);

      vi.advanceTimersByTime(300);
      flush();
      expect(host.queries.length).toBe(before + 1);
      expect(lastQuery()).toMatchObject({ search: 'tea b', page: 1, reason: 'search' });
    });

    it('does not emit again for unchanged criteria (whitespace, retyping the same term)', async () => {
      await create(serverSetup);
      vi.useFakeTimers();
      type(searchInput(), 'tea');
      vi.advanceTimersByTime(300);
      flush();
      const count = host.queries.length;

      type(searchInput(), 'tea  ');
      vi.advanceTimersByTime(300);
      flush();
      searchInput().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }));
      expect(host.queries.length).toBe(count);
    });

    it('keeps search while paging and sorting in later queries', async () => {
      await create(serverSetup);
      vi.useFakeTimers();
      type(searchInput(), 'tea');
      vi.advanceTimersByTime(300);
      flush();
      nextButton().click();
      flush();
      expect(lastQuery()).toMatchObject({ search: 'tea', page: 2 });
      sortButton('Person').click();
      flush();
      expect(lastQuery()).toMatchObject({ search: 'tea', page: 1, sort: { columnKey: 'name', direction: 'asc' } });
    });

    it('includes only non-empty column filters and resets the page', async () => {
      await create(serverSetup);
      nextButton().click();
      flush();
      const select = q<HTMLSelectElement>('select[aria-label="Filter Status"]');
      select.value = 'I';
      select.dispatchEvent(new Event('change'));
      flush();
      expect(lastQuery()).toMatchObject({ filters: { status: 'I' }, page: 1, reason: 'filter' });
      select.value = '';
      select.dispatchEvent(new Event('change'));
      flush();
      expect(lastQuery().filters).toEqual({});
    });

    it('changes page size from page 1 and reports it', async () => {
      await create(serverSetup);
      nextButton().click();
      flush();
      const select = q<HTMLSelectElement>('select[aria-label="Rows per page"]');
      select.value = '50';
      select.dispatchEvent(new Event('change'));
      flush();
      expect(lastQuery()).toMatchObject({ page: 1, pageSize: 50, reason: 'pageSize' });
    });

    it('computes the range from the server page and total, and navigates with first/last', async () => {
      await create(serverSetup);
      nextButton().click();
      flush();
      host.rows.set(makePeople(10));
      flush();
      expect(q('.ui-table__range').textContent).toBe('Showing 11–20 of 95');
      lastButton().click();
      flush();
      host.rows.set(makePeople(5));
      flush();
      expect(q('.ui-table__range').textContent).toBe('Showing 91–95 of 95');
      expect(nextButton().disabled).toBe(true);
    });

    it('handles an unknown total: no last page, next enabled only for a full page', async () => {
      await create(h => { serverSetup(h); h.total.set(null); });
      expect(q('.ui-table__page-indicator').textContent).toBe('Page 1');
      expect(lastButton().disabled).toBe(true);
      expect(nextButton().disabled).toBe(false);
      host.rows.set(makePeople(4));
      flush();
      expect(nextButton().disabled).toBe(true);
    });

    it('distinguishes no data from no results using the server answer', async () => {
      await create(h => { serverSetup(h); h.rows.set([]); h.total.set(0); });
      expect(q('app-ui-state').textContent).toContain('No people yet');

      vi.useFakeTimers();
      type(searchInput(), 'zzz');
      vi.advanceTimersByTime(300);
      flush();
      expect(q('app-ui-state').textContent).toContain('No matching results');
    });

    it('increments the sequence per emitted query so stale responses can be discarded', async () => {
      await create(serverSetup);
      nextButton().click();
      flush();
      sortButton('Person').click();
      flush();
      expect(host.queries.map(query => query.sequence)).toEqual([1, 2, 3]);
    });

    it('treats data from an older query as stale: busy, rows kept, never "no results"', async () => {
      await create(serverSetup);
      host.sequence.set(1);
      nextButton().click();
      flush();
      expect(q('.ui-table').getAttribute('aria-busy')).toBe('true');
      expect(qa('tbody tr')).toHaveLength(10);

      host.sequence.set(2);
      flush();
      expect(q('.ui-table').hasAttribute('aria-busy')).toBe(false);

      host.rows.set([]);
      host.total.set(0);
      host.sequence.set(2);
      sortButton('Person').click();
      flush();
      expect(q('app-ui-state').getAttribute('role')).toBe('status');
      expect(q('app-ui-state').textContent).toContain('Loading');
    });

    it('shows loading before the first response instead of a flash of "no data"', async () => {
      await create(h => { serverSetup(h); h.rows.set([]); h.total.set(null); h.loading.set(true); });
      expect(q('app-ui-state').getAttribute('role')).toBe('status');
      expect(q('app-ui-state').textContent).not.toContain('No people yet');
    });

    it('moves to the last available page when the server total shrinks', async () => {
      await create(serverSetup);
      lastButton().click();
      flush();
      expect(lastQuery().page).toBe(10);
      host.total.set(25);
      host.rows.set(makePeople(0));
      await settle();
      expect(lastQuery()).toMatchObject({ page: 3, reason: 'page' });
    });

    it('keeps the error state independent from the query controls', async () => {
      await create(h => { serverSetup(h); h.error.set('Network error'); });
      expect(q('app-ui-state').getAttribute('role')).toBe('alert');
      sortButton('Person').click();
      flush();
      expect(lastQuery().sort).toEqual({ columnKey: 'name', direction: 'asc' });
    });
  });

  describe('clickable rows', () => {
    it('is off by default: no pointer class and no rowClick', async () => {
      await create(h => h.withActions.set(true));
      expect(q('tbody tr').classList).not.toContain('ui-table__row--clickable');
      q('tbody tr td').click();
      expect(host.clicked).toEqual([]);
    });

    it('emits the clicked row when enabled', async () => {
      await create(h => h.rowClickable.set(true));
      expect(q('tbody tr').classList).toContain('ui-table__row--clickable');
      qa('tbody tr')[1].querySelector('td')!.click();
      expect(host.clicked).toEqual([2]);
    });

    it('ignores clicks on buttons, links and form controls inside the row', async () => {
      await create(h => { h.rowClickable.set(true); h.withActions.set(true); h.selection.set('multiple'); });
      q('.row-action').click();
      q('tbody input[type="checkbox"]').click();
      expect(host.acted).toEqual([1]);
      expect(host.clicked).toEqual([]);
    });
  });
});
