# RetailPOS UI Standardisation — Phase 2B Report (Advanced Shared UI Components)

## Status: PASSED for automated gates; authenticated browser verification NOT performed

The reusable Data Table, Dialog / Confirm dialog, inline Alert and focus-trap are implemented, and the three existing selector/overlay components (`AlertComponent`, `SearchableDropdownComponent`, `AdvancedFilterDrawerComponent`, `VariantSearchPickerComponent`) were restyled and made keyboard-accessible with their public contracts unchanged. **No feature screen was migrated** (no feature template, route, guard, service or API call was touched in this phase).

## Results

| Gate | Result |
|---|---|
| Targeted specs | **PASS** — 8 new files / 165 tests |
| Full unit suite | **PASS — 59 files / 488 tests** (Phase 2A baseline 51 / 323: +8 files, +165 tests). One pre-existing assertion of mine in `ui-kit.spec.ts` was updated (`UI_KIT` length 7 → 14); no other existing test was edited or weakened. |
| Production build | **PASS**, no warnings (no NG8011/NG8102, no budget warnings) |
| Bundle | Initial total **2.54 MB raw / 396.09 kB transfer** (Phase 2A 2.51 MB / 392.00 kB → **+≈0.03 MB raw, +4.09 kB transfer**). `main` 2.43 MB / 381.02 kB → 2.45 MB / 385.11 kB. Global CSS 81.48 → 81.46 kB raw (10.98 kB transfer, unchanged). The Data Table is not imported by any screen yet, so it adds nothing to the initial bundle (tree-shaken); the growth is the shared dialog/focus-trap pulled in by the global `AlertComponent`, and the richer dropdown/drawer/picker. |
| `git diff --check` | **PASS** (tracked files); no trailing whitespace in the new untracked files |

### Tests added (165)

| File | Tests | Covers |
|---|---|---|
| `shared/ui/ui-data-table.logic.spec.ts` | 25 | filter → sort → paginate pipeline, stable sort, empty values last, no input mutation, page clamping, prototype-safe path reads, displayed-text search |
| `shared/ui/ui-data-table.component.spec.ts` | 58 | client and server modes, `aria-sort` cycle, default/unsorted states, debounce (default and custom), clear/Escape/Enter, column filters, search + page interaction, query events and sequence, loading/empty/no-results/error, selection scope, keyboard access, density, custom cell/actions templates |
| `shared/ui/ui-dialog.spec.ts` | 27 | dialog (labelling, sizes, status icon, initial focus, Tab wrap, focus restore, Escape/backdrop/close reasons, non-dismissible, stacked Escape, body scroll-lock) and confirm dialog (alertdialog, destructive focus, confirmed-does-not-close, Escape = cancel, backdrop ignored, busy) |
| `shared/ui/ui-alert.component.spec.ts` | 8 | variants, roles (`alert` vs `status`), icons, title/actions, dismiss |
| `alert/alert.component.spec.ts` | 12 | real `AlertService`: per-type rendering, OK/Escape/backdrop, confirm/cancel callbacks, Escape on a confirm acts as Cancel, backdrop ignored on confirm |
| `searchable-dropdown/*.spec.ts` | 18 | ARIA, 300 ms debounce, mouse/keyboard selection, Escape isolation, load-more, clear, disabled, loading/empty |
| `advanced-filter-drawer/*.spec.ts` | 7 | labelled dialog, footer actions, applied-count badge, Escape (and `defaultPrevented` skip), focus in/wrap/restore |
| `variant-search-picker/*.spec.ts` | 10 | label/ARIA, debounce and min-length, results, keyboard select, already-added rows, `hideExisting`, Escape, clear, disabled |

## Components implemented or reused

All new pieces live in `retailpos-frontend/src/app/components/shared/ui/` and are exported from `index.ts` (`UI_KIT` now has 14 items).

| Piece | Selector | Notes |
|---|---|---|
| `UiDataTableComponent` | `app-ui-data-table` | New. See "Data Table public API". |
| `UiTableCellDirective`, `UiTableRowActionsDirective` | `ng-template[uiTableCell]`, `ng-template[uiTableRowActions]` | Feature-owned cell and action templates. |
| `UiDialogComponent` | `app-ui-dialog` | New. `[(open)]`, `title`, `size` (`sm/md/lg`), `status`, `role` (`dialog`/`alertdialog`), `dismissible`, `closeOnBackdrop`, `closeOnEscape`, `showClose`; `(closed)` with reason `close/backdrop/escape`; footer slot `[uiDialogFooter]`. Labelled via `aria-labelledby`/`aria-describedby`, `aria-modal`, body scroll-lock, bottom-sheet layout on phones, only the top-most dialog handles Escape. |
| `UiConfirmDialogComponent` | `app-ui-confirm-dialog` | New. `alertdialog`; `destructive` (danger styling, **Cancel is focused first**), `busy` (blocks every dismissal and shows the confirm button as loading); `(confirmed)` / `(cancelled)`. The feature owns the action and closes the dialog. |
| `UiAlertComponent` | `app-ui-alert` | New inline alert: `success/warning/danger/info`, `role="alert"` for warning/danger and `role="status"` otherwise, optional title, `[uiAlertActions]`, dismissible. |
| `UiFocusTrapDirective` | `[appUiFocusTrap]` | New. Initial focus on `[data-autofocus]`, else the first focusable that is not `data-focus-skip`; Tab/Shift+Tab wrap; pulls stray focus back; only the top-most trap enforces; restores focus on destroy. Pure Angular, no Bootstrap JS. |
| `AlertComponent` | `app-alert` | **Reused / rebuilt on `app-ui-dialog`.** `AlertService` and every caller (including the error interceptor and `app.html`) are untouched. |
| `SearchableDropdownComponent` | `app-searchable-dropdown` | **Reused / restyled.** Inputs, outputs and public methods unchanged; added `ariaLabel`, `clearable`, `cleared`, combobox/listbox ARIA, Arrow/Enter/Escape/Tab keys, token styles. Default `inputClass` is now `ui-control`. |
| `AdvancedFilterDrawerComponent` | `app-advanced-filter-drawer` | **Reused / restyled.** Inputs/outputs unchanged; added `activeCount` ("n applied" badge), dialog semantics, focus trap, Escape, phone full-width layout. |
| `VariantSearchPickerComponent` | `app-variant-search-picker` | **Reused / restyled** with targeted edits to its pre-existing uncommitted changes (preserved): tokens, `ui-control`, label `for`, combobox/listbox/option ARIA, status region, keyboard navigation. |

**Alerts / notifications.** The existing alert is a modal driven by `AlertService` (also used by the HTTP error interceptor). There is no toast service, no caller needs one, and adding a second notification channel would duplicate behaviour, so **none was added**. Inline alerts use `app-ui-alert`.

## Data Table public API

### Inputs

| Input | Default | Purpose |
|---|---|---|
| `columns` (required) | — | `UiTableColumn[]` (plain data; never evaluated) |
| `rows` | `[]` | Client mode: the full dataset. Server mode: the current page returned by the API. |
| `rowKey` | `'id'` | Stable row identity (selection, tracking) |
| `mode` | `'client'` | `'client'` or `'server'` |
| `totalRecords` | `null` | Server mode: total matching records across all pages |
| `loading`, `error`, `errorTitle`, `retryable`, `retryLabel`, `loadingLabel` | | Independent loading/error states; `(retry)` is emitted, the feature re-requests |
| `emptyTitle`, `emptyMessage` | | "No data yet" state, distinct from "No results" (search/filter active) |
| `responseSequence` | `null` | Server mode: echo of the `sequence` of the response being shown; a stale value keeps the table busy |
| `searchable`, `searchLabel`, `searchPlaceholder`, `searchDebounceMs` | `true`, …, `300` | Global search |
| `defaultSort`, `allowUnsorted` | `null`, `true` | Initial sort; whether a third click returns to unsorted |
| `paginated`, `pageSizes`, `initialPageSize`, `initialPage` | `true`, `[10,25,50,100]`, first size, `1` | Pagination |
| `initialSearch`, `initialFilters`, `emitInitialQuery` | `''`, `{}`, `true` | Restore state; an `init` query is emitted after first render (disable with `false`) |
| `selectionMode`, `selectedKeys` (two-way) | `'none'` | `'none'`, `'single'`, `'multiple'` |
| `density`, `stickyHeader`, `maxHeight`, `caption`, `ariaLabel`, `actionsHeader` | `'comfortable'`, `true`, … | Presentation |

### Outputs

- `queryChange: UiTableQuery` — `{ search, searchColumns, filters, sort, page, pageSize, sequence, reason }`. `reason` is `init | search | filter | sort | page | pageSize | reset`.
- `retry: void`.

### Column definition (`UiTableColumn`)

`key` (stable id used for sort/filter/template/query), `header`, `field` (dot path, own properties only), `type` (`text | number | date | currency | status | custom`), `sortable`, `searchable`, `filter` (`text` or `select` with options), `align`, `width`, `visible`, `emptyText`, `locale`, `decimals`, `currencyCode`, `dateStyle`, `statusMap` (value → label + badge variant; status text is always shown). Complex cells use `<ng-template uiTableCell="key" let-row let-text="text">`; row actions use `<ng-template uiTableRowActions let-row>` with feature-owned `@if (canEdit(row))` checks.

## Sorting, searching and pagination behaviour

- **Sorting.** Sortable headers are native buttons inside `th[aria-sort]` (`ascending`/`descending`/`none`; non-sortable columns carry no `aria-sort`). Click cycles asc → desc → (unsorted if `allowUnsorted`, else asc). Sorting always uses the column **key**. Client sort is stable; empty values sort last in both directions.
- **Search.** Word tokens are AND-ed against the **displayed** text of searchable, **visible** columns (so formatted currency and status labels are searchable; hidden and `custom` columns are not unless flagged). Debounced by `searchDebounceMs` (default 300 ms); Enter and the clear button commit immediately; Escape clears. Identical consecutive queries are never re-emitted.
- **Filters.** Optional per-column text/select filters; only non-empty filters are included in the query.
- **Pagination.** First/Previous/Next/Last, page indicator, "x–y of N" range, page sizes 10/25/50/100. Changing search, filter, sort or page size resets to page 1; the search/filter/sort state is preserved while paging. If the total shrinks below the current page, the table moves to the last page.
- **Client mode** applies **filter/search → sort → paginate** on a copy; the input array is never mutated.
- **Server mode.** The table never slices, sorts or searches rows itself. It emits a `UiTableQuery` and the feature requests the matching page; `totalRecords` drives the pager. While `loading`, a stale `responseSequence`, or before the first response, the table shows loading rather than "no data".
- **Selection.** `single` or `multiple`. "Select all" applies to the **current page only** (with an indeterminate state); selected keys persist across pages.

## Usage

### Client mode

```html
<app-ui-data-table
  [columns]="columns" [rows]="suppliers()" rowKey="id"
  [defaultSort]="{ columnKey: 'name', direction: 'asc' }"
  ariaLabel="Suppliers" density="compact">
  <ng-template uiTableCell="name" let-row><a [routerLink]="['/suppliers', row.id]">{{ row.name }}</a></ng-template>
  <ng-template uiTableRowActions let-row>
    @if (canEdit()) { <button appUiButton variant="ghost" size="sm" (click)="edit(row)">Edit</button> }
  </ng-template>
</app-ui-data-table>
```

```ts
columns: UiTableColumn[] = [
  { key: 'name', header: 'Name', sortable: true },
  { key: 'balance', header: 'Balance', type: 'currency', currencyCode: 'BDT', sortable: true },
  { key: 'status', header: 'Status', type: 'status', statusMap: { active: { label: 'Active', variant: 'success' } } },
];
```

### Server mode (feature owns the API, cancellation and stale handling)

```ts
readonly query$ = new Subject<UiTableQuery>();
readonly rows = signal<Product[]>([]); total = signal(0);
loading = signal(false); error = signal<string | null>(null); shownSequence = signal<number | null>(null);

constructor() {
  this.query$.pipe(
    tap(() => { this.loading.set(true); this.error.set(null); }),
    switchMap(q => this.api.list({            // map to the existing API contract
      pageNumber: q.page, pageSize: q.pageSize, search: q.search,
      sortBy: q.sort?.columnKey, sortDirection: q.sort?.direction, ...q.filters,
    }).pipe(
      map(res => ({ q, res })),
      catchError(() => { this.error.set('Could not load products'); return EMPTY; }),
    )),                                       // switchMap cancels superseded requests
    takeUntilDestroyed(),
  ).subscribe(({ q, res }) => {
    this.rows.set(res.items); this.total.set(res.totalCount);
    this.shownSequence.set(q.sequence); this.loading.set(false);
  });
}
```

```html
<app-ui-data-table mode="server" [columns]="columns" [rows]="rows()" [totalRecords]="total()"
  [loading]="loading()" [error]="error()" [responseSequence]="shownSequence()"
  (queryChange)="query$.next($event)" (retry)="reload()" />
```

### Dialogs and alerts

```html
<app-ui-dialog [(open)]="editing" title="Edit supplier" size="md">
  <form>…</form>
  <button uiDialogFooter appUiButton variant="outline" (click)="editing.set(false)">Cancel</button>
  <button uiDialogFooter appUiButton [loading]="saving()" (click)="save()">Save</button>
</app-ui-dialog>

<app-ui-confirm-dialog [(open)]="confirmDelete" title="Delete supplier?" confirmLabel="Delete"
  [destructive]="true" [busy]="deleting()" (confirmed)="remove()">This cannot be undone.</app-ui-confirm-dialog>

<app-ui-alert variant="warning" title="Credit limit reached" [dismissible]="true">Review before continuing.</app-ui-alert>
```

## Behaviour changes to existing components (review these)

| Component | Change | Risk |
|---|---|---|
| `AlertComponent` | Rebuilt on `app-ui-dialog`. **Escape now closes info/success/error alerts** and **acts as Cancel on a confirmation (the `onCancel` callback runs)**. Confirm backdrop clicks remain ignored. OK is focused on open. | Low–medium: global component; a confirm dismissed by Escape now runs `onCancel`. |
| `SearchableDropdownComponent` | Default `inputClass` is `ui-control` (was an inline Tailwind string); Escape closes the list and stops propagation; new opt-in `clearable`. Callers passing their own `inputClass` are unaffected. Used in `po-form`. | Low |
| `AdvancedFilterDrawerComponent` | Now a modal dialog with focus trap; Escape closes it (skipped if another control already handled the key). Used in `product-list`. | Low |
| `VariantSearchPickerComponent` | Arrow/Enter/Escape support, ARIA and token classes only; search, debounce and selection logic unchanged. Used in `transfer-create`. Preserved its pre-existing uncommitted edits. | Low |

## Known limitations

- **No authenticated browser verification.** The development API is unavailable and no safe credentials exist; authentication was not bypassed. All validation above is unit/component level plus a successful production build. The new table, dialogs and restyled components have **not** been visually inspected inside the real application, and no screen consumes the Data Table yet. Responsive behaviour (bottom-sheet dialog, horizontal table scroll, full-width drawer) is implemented in CSS but unverified on devices.
- Inter is declared first in the font stack but not bundled (licence review pending); the existing fallback applies.
- Not included by design: virtualisation, column reordering/resizing, inline editing, a toast service.
- Server-mode stale handling is a **contract**: the feature must cancel requests (`switchMap`) and echo `responseSequence`; the table cannot enforce it.
- Search is token-based on displayed text; it is not fuzzy and does not search hidden columns.
- Each migrated screen must still verify its own API's sort/filter parameter names; the table only supplies stable column keys.

## Changed-file inventory

New (all untracked): `components/shared/ui/` — `ui-data-table.component.ts`, `.logic.ts`, `.types.ts`, `.directives.ts`, `ui-dialog.component.ts`, `ui-confirm-dialog.component.ts`, `ui-alert.component.ts`, `ui-focus-trap.directive.ts`, and specs `ui-data-table.logic.spec.ts`, `ui-data-table.component.spec.ts`, `ui-dialog.spec.ts`, `ui-alert.component.spec.ts`; `alert/alert.component.spec.ts`; `searchable-dropdown/searchable-dropdown.component.spec.ts`; `advanced-filter-drawer/advanced-filter-drawer.component.spec.ts`; `variant-search-picker/variant-search-picker.component.spec.ts`.

Modified: `shared/ui/index.ts` (exports, `UI_KIT` 14 items), `shared/ui/ui-kit.spec.ts` (length 7 → 14), `alert/alert.component.ts`, `searchable-dropdown/searchable-dropdown.component.ts`, `advanced-filter-drawer/advanced-filter-drawer.component.ts`, `variant-search-picker/variant-search-picker.component.{ts,html}`.

Not touched: backend, Flutter, routes, guards, `AlertService`, interceptors, business services, POS checkout, feature pages, purchased Able Pro source.
