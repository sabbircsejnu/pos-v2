import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  OnDestroy,
  OnInit,
  afterNextRender,
  computed,
  contentChild,
  contentChildren,
  effect,
  input,
  inject,
  model,
  output,
  signal,
  untracked
} from '@angular/core';
import { UiBadgeComponent, UiBadgeVariant } from './ui-badge.component';
import { UiButtonComponent } from './ui-button.component';
import { UiControlDirective } from './ui-control.directive';
import {
  UiTableCellContext,
  UiTableCellDirective,
  UiTableRowActionsContext,
  UiTableRowActionsDirective
} from './ui-data-table.directives';
import {
  applyClientQuery,
  cleanFilters,
  columnValue,
  formatCellText,
  isSearchable,
  readPath,
  sameRecord,
  totalPagesFor
} from './ui-data-table.logic';
import { UiColumnAlign, UiTableColumn, UiTableQuery, UiTableQueryReason, UiTableSort } from './ui-data-table.types';
import { UiStateComponent } from './ui-state.component';
import { CompanyDateService } from '../../../services/company-date.service';

export type UiTableMode = 'client' | 'server';
export type UiTableSelectionMode = 'none' | 'single' | 'multiple';
export type UiTableDensity = 'comfortable' | 'compact';
export type UiTableRowKey = string | number;

/**
 * Reusable, presentation-only data table. It never loads data and has no business knowledge:
 * the feature passes `rows` and reacts to `queryChange`.
 *
 * Client mode (default) searches, filters, sorts and paginates the in-memory `rows` (in that order,
 * without mutating them). Server mode only reports the query; the feature calls its API with it and
 * passes back the current page of `rows` plus `totalRecords`, so search/sort/filter always apply to
 * the whole dataset. See Docs/UI-Standardisation/PHASE_2B_REPORT.md for full examples.
 */
@Component({
  selector: 'app-ui-data-table',
  standalone: true,
  imports: [NgTemplateOutlet, UiBadgeComponent, UiButtonComponent, UiControlDirective, UiStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ui-table" [attr.data-density]="density()" [attr.aria-busy]="busy() ? 'true' : null">
      <div class="ui-table__toolbar">
        @if (searchable()) {
          <div class="ui-table__search" role="search">
            <input
              appUiControl
              type="search"
              class="ui-table__search-input"
              [attr.aria-label]="searchLabel()"
              [placeholder]="searchPlaceholder()"
              [value]="searchInput()"
              (input)="onSearchInput($any($event.target).value)"
              (keydown.enter)="$event.preventDefault(); commitNow()"
              (keydown.escape)="onSearchEscape($event)" />
            @if (searchInput()) {
              <button appUiButton variant="ghost" size="sm" [iconOnly]="true" class="ui-table__search-clear" aria-label="Clear search" (click)="clearSearch()">
                <i class="fas fa-times" aria-hidden="true"></i>
              </button>
            }
          </div>
        }
        <div class="ui-table__toolbar-extra"><ng-content select="[uiTableToolbar]" /></div>
      </div>

      <div
        class="ui-table__scroll"
        role="region"
        tabindex="0"
        [attr.aria-label]="ariaLabel() || caption() || null"
        [style.max-height]="maxHeight()">
        <table class="ui-table__table" [class.ui-table__table--sticky]="stickyHeader()">
          @if (caption()) {
            <caption class="sr-only">{{ caption() }}</caption>
          }
          <thead>
            <tr>
              @if (selectionMode() !== 'none') {
                <th scope="col" class="ui-table__select">
                  @if (selectionMode() === 'multiple') {
                    <input
                      type="checkbox"
                      class="ui-check__input"
                      aria-label="Select all rows on this page"
                      [checked]="allPageSelected()"
                      [indeterminate]="somePageSelected()"
                      [disabled]="pageRows().length === 0"
                      (change)="toggleAllOnPage()" />
                  } @else {
                    <span class="sr-only">Select</span>
                  }
                </th>
              }
              @for (column of visibleColumns(); track column.key) {
                <th scope="col" [attr.aria-sort]="ariaSort(column)" [attr.data-align]="alignOf(column)" [style.width]="column.width">
                  @if (column.sortable) {
                    <button type="button" class="ui-table__sort" (click)="toggleSort(column)">
                      <span>{{ column.header }}</span>
                      <span class="ui-table__sort-icon" [attr.data-state]="sortState()?.columnKey === column.key ? sortState()?.direction : 'none'" aria-hidden="true"></span>
                    </button>
                  } @else {
                    {{ column.header }}
                  }
                </th>
              }
              @if (rowActions()) {
                <th scope="col" class="ui-table__actions-head">{{ actionsHeader() }}</th>
              }
            </tr>
            @if (hasColumnFilters()) {
              <tr class="ui-table__filters">
                @if (selectionMode() !== 'none') {
                  <th></th>
                }
                @for (column of visibleColumns(); track column.key) {
                  <th [attr.data-align]="alignOf(column)">
                    @if (column.filter; as filter) {
                      @if (filter.type === 'select') {
                        <select
                          class="ui-control ui-table__filter"
                          [attr.aria-label]="'Filter ' + column.header"
                          [value]="filterValue(column.key)"
                          (change)="onFilterSelect(column.key, $any($event.target).value)">
                          <option value="">All</option>
                          @for (option of filter.options ?? []; track option.value) {
                            <option [value]="option.value">{{ option.label }}</option>
                          }
                        </select>
                      } @else {
                        <input
                          type="search"
                          class="ui-control ui-table__filter"
                          [attr.aria-label]="'Filter ' + column.header"
                          [placeholder]="filter.placeholder ?? 'Filter'"
                          [value]="filterValue(column.key)"
                          (input)="onFilterInput(column.key, $any($event.target).value)"
                          (keydown.enter)="$event.preventDefault(); commitNow()" />
                      }
                    }
                  </th>
                }
                @if (rowActions()) {
                  <th></th>
                }
              </tr>
            }
          </thead>
          @if (stateKind() === null) {
            <tbody>
              @for (row of pageRows(); track trackRow(row, $index); let i = $index) {
                <tr [class.ui-table__row--selected]="isSelected(row)" [class.ui-table__row--clickable]="rowClickable()" (click)="onRowClick(row, $event)">
                  @if (selectionMode() !== 'none') {
                    <td class="ui-table__select">
                      @if (selectionMode() === 'multiple') {
                        <input type="checkbox" class="ui-check__input" [attr.aria-label]="selectLabel(row, i)" [checked]="isSelected(row)" (change)="toggleRow(row)" />
                      } @else {
                        <input type="radio" class="ui-check__input" [name]="groupName" [attr.aria-label]="selectLabel(row, i)" [checked]="isSelected(row)" (change)="toggleRow(row)" />
                      }
                    </td>
                  }
                  @for (column of visibleColumns(); track column.key) {
                    <td [attr.data-align]="alignOf(column)">
                      @if (cellTemplate(column.key); as template) {
                        <ng-container [ngTemplateOutlet]="template" [ngTemplateOutletContext]="cellContext(row, column, i)" />
                      } @else if (column.type === 'status' && hasValue(row, column)) {
                        <app-ui-badge [variant]="statusVariant(row, column)">{{ cellText(row, column) }}</app-ui-badge>
                      } @else {
                        {{ cellText(row, column) || (column.emptyText ?? '—') }}
                      }
                    </td>
                  }
                  @if (rowActions(); as actions) {
                    <td class="ui-table__actions">
                      <ng-container [ngTemplateOutlet]="actions.template" [ngTemplateOutletContext]="actionsContext(row, i)" />
                    </td>
                  }
                </tr>
              }
            </tbody>
          }
        </table>
      </div>

      @switch (stateKind()) {
        @case ('error') {
          <app-ui-state kind="error" [title]="errorTitle()" [message]="error() ?? ''">
            @if (retryable()) {
              <button appUiButton variant="outline" (click)="retry.emit()">{{ retryLabel() }}</button>
            }
          </app-ui-state>
        }
        @case ('loading') {
          <app-ui-state kind="loading" [title]="loadingLabel()" [compact]="true" />
        }
        @case ('no-results') {
          <app-ui-state title="No matching results" message="No rows match the current search or filters.">
            <button appUiButton variant="outline" (click)="clearCriteria()">Clear search and filters</button>
          </app-ui-state>
        }
        @case ('empty') {
          <app-ui-state [title]="emptyTitle()" [message]="emptyMessage()" />
        }
      }

      @if (paginated()) {
        <div class="ui-table__footer">
          <p class="ui-table__range" aria-live="polite">{{ rangeText() }}</p>
          <div class="ui-table__page-size">
            <span class="ui-table__page-size-label" aria-hidden="true">Rows per page</span>
            <select class="ui-control ui-table__page-size-select" aria-label="Rows per page" [value]="pageSizeState()" (change)="onPageSize($any($event.target).value)">
              @for (size of sizeOptions(); track size) {
                <option [value]="size" [selected]="size === pageSizeState()">{{ size }}</option>
              }
            </select>
          </div>
          <nav class="ui-table__pager" aria-label="Pagination">
            <button appUiButton variant="outline" size="sm" [iconOnly]="true" aria-label="First page" [disabled]="pageIndex() <= 1" (click)="goTo(1)">
              <i class="fas fa-angles-left" aria-hidden="true"></i>
            </button>
            <button appUiButton variant="outline" size="sm" [iconOnly]="true" aria-label="Previous page" [disabled]="pageIndex() <= 1" (click)="goTo(pageIndex() - 1)">
              <i class="fas fa-angle-left" aria-hidden="true"></i>
            </button>
            <span class="ui-table__page-indicator" aria-current="page">{{ pageIndicator() }}</span>
            <button appUiButton variant="outline" size="sm" [iconOnly]="true" aria-label="Next page" [disabled]="!hasNextPage()" (click)="goTo(pageIndex() + 1)">
              <i class="fas fa-angle-right" aria-hidden="true"></i>
            </button>
            <button appUiButton variant="outline" size="sm" [iconOnly]="true" aria-label="Last page" [disabled]="totalPages() === null || pageIndex() >= totalPages()!" (click)="goTo(totalPages()!)">
              <i class="fas fa-angles-right" aria-hidden="true"></i>
            </button>
          </nav>
        </div>
      }
    </div>
  `,
  styles: [`
    :host {
      display: block;
      font-family: var(--rp-font-family-base);
    }

    .ui-table {
      border: 1px solid var(--rp-color-border);
      border-radius: var(--rp-radius-card);
      background: var(--rp-color-surface-card);
      color: var(--rp-color-text-primary);
      font-size: var(--rp-text-table-cell-size);
      line-height: var(--rp-text-body-line);
    }

    .ui-table[aria-busy='true'] .ui-table__scroll {
      opacity: 0.6;
    }

    .ui-table__toolbar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--rp-space-3);
      padding: var(--rp-space-3) var(--rp-space-4);
      border-bottom: 1px solid var(--rp-color-border);
    }

    .ui-table__toolbar-extra {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--rp-space-2);
    }

    .ui-table__toolbar-extra:empty {
      display: none;
    }

    /* No search and nothing projected: do not render an empty bar above the table. */
    .ui-table__toolbar:not(:has(.ui-table__search)):has(> .ui-table__toolbar-extra:empty) {
      display: none;
    }

    .ui-table__search {
      position: relative;
      flex: 1 1 16rem;
      max-width: 24rem;
    }

    .ui-table__search-input {
      padding-right: 2.5rem;
    }

    .ui-table__search-input::-webkit-search-cancel-button {
      display: none;
    }

    .ui-table__search-clear {
      position: absolute;
      top: 50%;
      right: 0.25rem;
      transform: translateY(-50%);
    }

    .ui-table__scroll {
      overflow: auto;
    }

    .ui-table__scroll:focus-visible {
      outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring);
      outline-offset: -2px;
    }

    .ui-table__table {
      width: 100%;
      border-collapse: collapse;
    }

    .ui-table__table th {
      padding: var(--rp-table-cell-padding-y) var(--rp-table-cell-padding-x);
      border-bottom: 1px solid var(--rp-color-border);
      background: var(--rp-color-surface-muted);
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-table-head-size);
      font-weight: var(--rp-font-weight-semibold);
      letter-spacing: 0.04em;
      text-align: left;
      text-transform: uppercase;
      white-space: nowrap;
    }

    .ui-table__table--sticky thead th {
      position: sticky;
      top: 0;
      z-index: 1;
    }

    .ui-table__table td {
      padding: var(--rp-table-cell-padding-y) var(--rp-table-cell-padding-x);
      border-bottom: 1px solid var(--rp-color-border);
      vertical-align: middle;
    }

    .ui-table__table tbody tr:last-child td {
      border-bottom: 0;
    }

    .ui-table__table tbody tr:hover {
      background: var(--rp-color-surface-hover);
    }

    .ui-table__table tbody tr.ui-table__row--clickable {
      cursor: pointer;
    }

    .ui-table__table tbody tr.ui-table__row--selected {
      background: var(--rp-color-primary-soft);
    }

    [data-align='end'] {
      text-align: right;
    }

    [data-align='center'] {
      text-align: center;
    }

    [data-align='end'] .ui-table__sort {
      margin-left: auto;
    }

    .ui-table__select {
      width: 2.5rem;
    }

    .ui-table__actions-head,
    .ui-table__actions {
      text-align: right;
      white-space: nowrap;
    }

    .ui-table__filters th {
      padding-top: var(--rp-space-2);
      padding-bottom: var(--rp-space-2);
      background: var(--rp-color-surface-card);
      font-weight: var(--rp-font-weight-regular);
      text-transform: none;
    }

    .ui-table__filter {
      height: var(--rp-control-height-sm);
      font-size: var(--rp-text-label-size);
    }

    .ui-table__sort {
      display: inline-flex;
      align-items: center;
      gap: var(--rp-space-2);
      margin: calc(var(--rp-space-1) * -1) calc(var(--rp-space-2) * -1);
      padding: var(--rp-space-1) var(--rp-space-2);
      border-radius: var(--rp-radius-sm);
      color: inherit;
      font: inherit;
      letter-spacing: inherit;
      text-transform: inherit;
    }

    .ui-table__sort:hover {
      color: var(--rp-color-text-primary);
    }

    .ui-table__sort:focus-visible {
      outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring);
      outline-offset: 0;
    }

    th[aria-sort='ascending'],
    th[aria-sort='descending'] {
      color: var(--rp-color-primary-fg);
    }

    .ui-table__sort-icon {
      position: relative;
      display: inline-block;
      width: 0.5rem;
      height: 0.875rem;
    }

    .ui-table__sort-icon::before,
    .ui-table__sort-icon::after {
      content: '';
      position: absolute;
      left: 0;
      border-right: 0.25rem solid transparent;
      border-left: 0.25rem solid transparent;
      opacity: 0.35;
    }

    .ui-table__sort-icon::before {
      top: 0;
      border-bottom: 0.3125rem solid currentColor;
    }

    .ui-table__sort-icon::after {
      bottom: 0;
      border-top: 0.3125rem solid currentColor;
    }

    .ui-table__sort-icon[data-state='asc']::before,
    .ui-table__sort-icon[data-state='desc']::after {
      opacity: 1;
      color: var(--rp-color-primary);
    }

    .ui-table__footer {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--rp-space-3);
      padding: var(--rp-space-3) var(--rp-space-4);
      border-top: 1px solid var(--rp-color-border);
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-label-size);
    }

    .ui-table__range {
      margin: 0;
    }

    .ui-table__page-size {
      display: flex;
      align-items: center;
      gap: var(--rp-space-2);
    }

    .ui-table__page-size-select {
      width: auto;
      height: var(--rp-control-height-sm);
      font-size: var(--rp-text-label-size);
    }

    .ui-table__pager {
      display: flex;
      align-items: center;
      gap: var(--rp-space-1);
    }

    .ui-table__page-indicator {
      padding: 0 var(--rp-space-2);
      color: var(--rp-color-text-primary);
      white-space: nowrap;
    }

    @media (max-width: 639.98px) {
      .ui-table__footer {
        flex-direction: column;
        align-items: flex-start;
      }
    }
  `]
})
export class UiDataTableComponent<T extends object = Record<string, unknown>> implements OnInit, OnDestroy {
  private readonly companyDates = inject(CompanyDateService);
  // Data and mode
  readonly columns = input.required<readonly UiTableColumn[]>();
  readonly rows = input<readonly T[]>([]);
  /** Property of each row that uniquely identifies it (selection and row tracking). */
  readonly rowKey = input('id');
  readonly mode = input<UiTableMode>('client');
  /** Server mode: total records matching the query across all pages. Null while unknown. */
  readonly totalRecords = input<number | null>(null);

  // States
  readonly loading = input(false);
  readonly error = input<string | null>(null);
  readonly errorTitle = input('Could not load data');
  readonly retryable = input(true);
  readonly retryLabel = input('Retry');
  readonly loadingLabel = input('Loading…');
  readonly emptyTitle = input('No data yet');
  readonly emptyMessage = input('');
  /** Sequence of the query the current `rows` answer; lower than the latest query means the data is stale. */
  readonly responseSequence = input<number | null>(null);

  // Search, sorting, pagination configuration
  readonly searchable = input(true);
  readonly searchLabel = input('Search table');
  readonly searchPlaceholder = input('Search…');
  readonly searchDebounceMs = input(300);
  readonly defaultSort = input<UiTableSort | null>(null);
  /** When false, sorting alternates ascending/descending once a column is sorted. */
  readonly allowUnsorted = input(true);
  readonly paginated = input(true);
  readonly pageSizes = input<readonly number[]>([10, 25, 50, 100]);
  readonly initialPageSize = input<number | null>(null);
  readonly initialPage = input(1);
  readonly initialSearch = input('');
  readonly initialFilters = input<Record<string, string>>({});
  /** Emit the starting query on init so a server-side feature can load its first page. */
  readonly emitInitialQuery = input(true);

  // Selection
  readonly selectionMode = input<UiTableSelectionMode>('none');
  /** Keys of selected rows. They persist across pages; "select all" only affects the current page. */
  readonly selectedKeys = model<readonly UiTableRowKey[]>([]);

  // Presentation
  readonly density = input<UiTableDensity>('comfortable');
  readonly stickyHeader = input(true);
  /** CSS max-height of the scroll area; the sticky header only has something to stick to when this is set. */
  readonly maxHeight = input<string | null>(null);
  readonly caption = input('');
  readonly ariaLabel = input('');
  readonly actionsHeader = input('Actions');
  /**
   * Makes whole rows clickable (pointer cursor and `rowClick`). Clicks on links, buttons and form
   * controls inside the row are ignored. The mouse shortcut has no keyboard equivalent, so the
   * feature must also expose the same navigation as a link or button in a cell.
   */
  readonly rowClickable = input(false);

  readonly queryChange = output<UiTableQuery>();
  readonly retry = output<void>();
  readonly rowClick = output<T>();

  readonly cellTemplates = contentChildren(UiTableCellDirective);
  readonly rowActions = contentChild(UiTableRowActionsDirective);

  // Interaction state
  readonly searchInput = signal('');
  readonly filterInputs = signal<Record<string, string>>({});
  private readonly appliedSearch = signal('');
  private readonly appliedFilters = signal<Record<string, string>>({});
  readonly sortState = signal<UiTableSort | null>(null);
  readonly pageIndex = signal(1);
  readonly pageSizeState = signal(10);
  private readonly latestSequence = signal(0);
  /** False until the first render has finished and the initial query (if any) has been emitted. */
  private readonly started = signal(false);

  private lastQueryKey = '';
  private commitTimer: ReturnType<typeof setTimeout> | undefined;
  private static nextTableId = 0;
  readonly groupName = `ui-table-select-${++UiDataTableComponent.nextTableId}`;

  // Derived state
  readonly visibleColumns = computed(() => this.columns().filter(column => column.visible !== false));
  readonly hasColumnFilters = computed(() => this.visibleColumns().some(column => !!column.filter));
  readonly stale = computed(() => {
    const sequence = this.responseSequence();
    return sequence !== null && sequence < this.latestSequence();
  });
  readonly busy = computed(() => this.loading() || this.stale() || (this.mode() === 'server' && !this.started()));
  readonly hasCriteria = computed(() => this.appliedSearch() !== '' || Object.keys(this.appliedFilters()).length > 0);

  private readonly clientResult = computed(() =>
    applyClientQuery(this.rows(), this.columns(), {
      search: this.appliedSearch(),
      filters: this.appliedFilters(),
      sort: this.sortState(),
      page: this.paginated() ? this.pageIndex() : 1,
      pageSize: this.paginated() ? this.pageSizeState() : Number.MAX_SAFE_INTEGER
    }, (value, withTime) => this.companyDates.format(value, withTime))
  );

  readonly pageRows = computed<readonly T[]>(() => (this.mode() === 'client' ? this.clientResult().rows : this.rows()));

  readonly totalCount = computed<number | null>(() => {
    if (this.mode() === 'client') {
      return this.clientResult().total;
    }
    return this.totalRecords();
  });

  readonly totalPages = computed<number | null>(() => {
    const total = this.totalCount();
    return total === null ? null : totalPagesFor(total, this.pageSizeState());
  });

  readonly hasNextPage = computed(() => {
    const pages = this.totalPages();
    if (pages !== null) {
      return this.pageIndex() < pages;
    }
    return this.pageRows().length >= this.pageSizeState();
  });

  readonly stateKind = computed<'error' | 'loading' | 'empty' | 'no-results' | null>(() => {
    if (this.error()) {
      return 'error';
    }
    if (this.pageRows().length > 0) {
      return null;
    }
    if (this.busy()) {
      return 'loading';
    }
    return this.hasCriteria() ? 'no-results' : 'empty';
  });

  readonly sizeOptions = computed(() => {
    const sizes = [...this.pageSizes()];
    if (!sizes.includes(this.pageSizeState())) {
      sizes.push(this.pageSizeState());
      sizes.sort((a, b) => a - b);
    }
    return sizes;
  });

  readonly pageIndicator = computed(() => {
    const pages = this.totalPages();
    return pages === null ? `Page ${this.pageIndex()}` : `Page ${this.pageIndex()} of ${pages}`;
  });

  readonly rangeText = computed(() => {
    const total = this.totalCount();
    const count = this.pageRows().length;
    if (count === 0) {
      return total === null ? 'No records' : `Showing 0 of ${total}`;
    }
    const start = (this.pageIndex() - 1) * this.pageSizeState() + 1;
    const end = start + count - 1;
    return total === null ? `Showing ${start}–${end}` : `Showing ${start}–${end} of ${total}`;
  });

  private readonly pageKeys = computed(() =>
    this.pageRows()
      .map(row => this.identityOf(row))
      .filter((key): key is UiTableRowKey => key !== undefined)
  );
  private readonly selectedSet = computed(() => new Set(this.selectedKeys()));
  readonly allPageSelected = computed(() => this.pageKeys().length > 0 && this.pageKeys().every(key => this.selectedSet().has(key)));
  readonly somePageSelected = computed(() => !this.allPageSelected() && this.pageKeys().some(key => this.selectedSet().has(key)));

  private readonly cellTemplateMap = computed(() => {
    const map = new Map<string, UiTableCellDirective['template']>();
    for (const directive of this.cellTemplates()) {
      map.set(directive.columnKey(), directive.template);
    }
    return map;
  });

  constructor() {
    // Emit the starting query after the first render so a feature's handler never writes state mid-render.
    afterNextRender(() => {
      if (this.emitInitialQuery()) {
        this.emitQuery('init');
      }
      this.started.set(true);
    });

    // If the data shrinks (or the total drops) while on a later page, move back to the last page.
    effect(() => {
      const pages = this.totalPages();
      const page = this.pageIndex();
      if (pages !== null && page > pages && !this.busy()) {
        untracked(() => {
          this.pageIndex.set(pages);
          this.emitQuery('page');
        });
      }
    });
  }

  ngOnInit(): void {
    const sizes = this.pageSizes();
    this.pageSizeState.set(this.initialPageSize() ?? sizes[0] ?? 10);
    this.pageIndex.set(Math.max(1, this.initialPage()));
    this.sortState.set(this.defaultSort());
    this.searchInput.set(this.initialSearch());
    this.appliedSearch.set(this.initialSearch().trim());
    this.filterInputs.set({ ...this.initialFilters() });
    this.appliedFilters.set(cleanFilters(this.initialFilters()));
  }

  ngOnDestroy(): void {
    clearTimeout(this.commitTimer);
  }

  // Search and filters (debounced; page resets when the criteria really change)
  onSearchInput(value: string): void {
    this.searchInput.set(value);
    this.scheduleCommit();
  }

  onSearchEscape(event: Event): void {
    if (this.searchInput()) {
      event.preventDefault();
      this.clearSearch();
    }
  }

  clearSearch(): void {
    this.searchInput.set('');
    this.commitNow();
  }

  filterValue(key: string): string {
    return this.filterInputs()[key] ?? '';
  }

  onFilterInput(key: string, value: string): void {
    this.filterInputs.update(current => ({ ...current, [key]: value }));
    this.scheduleCommit();
  }

  onFilterSelect(key: string, value: string): void {
    this.filterInputs.update(current => ({ ...current, [key]: value }));
    this.commitNow();
  }

  /** Clears search and column filters at once (used by the "no matching results" action). */
  clearCriteria(): void {
    this.searchInput.set('');
    this.filterInputs.set({});
    this.commitNow('reset');
  }

  commitNow(reason?: UiTableQueryReason): void {
    clearTimeout(this.commitTimer);
    const search = this.searchInput().trim();
    const filters = cleanFilters(this.filterInputs());
    const searchChanged = search !== this.appliedSearch();
    const filtersChanged = !sameRecord(filters, this.appliedFilters());
    if (!searchChanged && !filtersChanged) {
      return;
    }

    this.appliedSearch.set(search);
    this.appliedFilters.set(filters);
    this.pageIndex.set(1);
    this.emitQuery(reason ?? (searchChanged ? 'search' : 'filter'));
  }

  private scheduleCommit(): void {
    clearTimeout(this.commitTimer);
    this.commitTimer = setTimeout(() => this.commitNow(), this.searchDebounceMs());
  }

  // Sorting
  toggleSort(column: UiTableColumn): void {
    if (!column.sortable) {
      return;
    }
    const current = this.sortState();
    let next: UiTableSort | null;
    if (!current || current.columnKey !== column.key) {
      next = { columnKey: column.key, direction: 'asc' };
    } else if (current.direction === 'asc') {
      next = { columnKey: column.key, direction: 'desc' };
    } else {
      next = this.allowUnsorted() ? null : { columnKey: column.key, direction: 'asc' };
    }
    this.sortState.set(next);
    this.pageIndex.set(1);
    this.emitQuery('sort');
  }

  ariaSort(column: UiTableColumn): 'ascending' | 'descending' | 'none' | null {
    if (!column.sortable) {
      return null;
    }
    const sort = this.sortState();
    if (!sort || sort.columnKey !== column.key) {
      return 'none';
    }
    return sort.direction === 'asc' ? 'ascending' : 'descending';
  }

  // Pagination
  goTo(page: number): void {
    const pages = this.totalPages();
    const target = Math.max(1, pages === null ? page : Math.min(page, pages));
    if (target === this.pageIndex()) {
      return;
    }
    this.pageIndex.set(target);
    this.emitQuery('page');
  }

  onPageSize(value: string): void {
    const size = Number(value);
    if (!Number.isFinite(size) || size < 1 || size === this.pageSizeState()) {
      return;
    }
    this.pageSizeState.set(size);
    this.pageIndex.set(1);
    this.emitQuery('pageSize');
  }

  onRowClick(row: T, event: Event): void {
    if (!this.rowClickable()) {
      return;
    }
    const target = event.target as Element | null;
    if (target?.closest('a, button, input, select, textarea, label, [data-row-click-ignore]')) {
      return;
    }
    this.rowClick.emit(row);
  }

  // Selection
  isSelected(row: T): boolean {
    const key = this.identityOf(row);
    return key !== undefined && this.selectedSet().has(key);
  }

  toggleRow(row: T): void {
    const key = this.identityOf(row);
    if (key === undefined) {
      return;
    }
    if (this.selectionMode() === 'single') {
      this.selectedKeys.set([key]);
      return;
    }
    const next = new Set(this.selectedKeys());
    if (!next.delete(key)) {
      next.add(key);
    }
    this.selectedKeys.set([...next]);
  }

  /** Selects every row of the current page, or clears them when they are all selected. Other pages are untouched. */
  toggleAllOnPage(): void {
    const keys = this.pageKeys();
    const next = new Set(this.selectedKeys());
    if (this.allPageSelected()) {
      keys.forEach(key => next.delete(key));
    } else {
      keys.forEach(key => next.add(key));
    }
    this.selectedKeys.set([...next]);
  }

  selectLabel(row: T, index: number): string {
    const first = this.visibleColumns()[0];
    const text = first ? this.cellText(row, first) : '';
    return text ? `Select ${text}` : `Select row ${index + 1}`;
  }

  // Cell helpers
  identityOf(row: T): UiTableRowKey | undefined {
    const value = readPath(row, this.rowKey());
    return typeof value === 'string' || typeof value === 'number' ? value : undefined;
  }

  trackRow(row: T, index: number): UiTableRowKey {
    return this.identityOf(row) ?? `index-${index}`;
  }

  cellText(row: T, column: UiTableColumn): string {
    return formatCellText(column, columnValue(row, column),
      (value, withTime) => this.companyDates.format(value, withTime));
  }

  hasValue(row: T, column: UiTableColumn): boolean {
    const value = columnValue(row, column);
    return value !== null && value !== undefined && value !== '';
  }

  statusVariant(row: T, column: UiTableColumn): UiBadgeVariant {
    return column.statusMap?.[String(columnValue(row, column))]?.variant ?? 'neutral';
  }

  alignOf(column: UiTableColumn): UiColumnAlign {
    return column.align ?? (column.type === 'number' || column.type === 'currency' ? 'end' : 'start');
  }

  cellTemplate(key: string) {
    return this.cellTemplateMap().get(key) ?? null;
  }

  cellContext(row: T, column: UiTableColumn, index: number): UiTableCellContext<T> {
    return { $implicit: row, value: columnValue(row, column), text: this.cellText(row, column), index };
  }

  actionsContext(row: T, index: number): UiTableRowActionsContext<T> {
    return { $implicit: row, index };
  }

  // Query emission (deduplicated)
  private emitQuery(reason: UiTableQueryReason): void {
    const search = this.appliedSearch();
    const filters = this.appliedFilters();
    const sort = this.sortState();
    const page = this.pageIndex();
    const pageSize = this.pageSizeState();

    const key = JSON.stringify([search, filters, sort, page, pageSize]);
    if (key === this.lastQueryKey) {
      return;
    }
    this.lastQueryKey = key;

    const sequence = this.latestSequence() + 1;
    this.latestSequence.set(sequence);
    this.queryChange.emit({
      search,
      searchColumns: this.columns().filter(isSearchable).map(column => column.key),
      filters: { ...filters },
      sort: sort ? { ...sort } : null,
      page,
      pageSize,
      sequence,
      reason
    });
  }
}
