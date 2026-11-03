# RetailPOS UI Standardisation — Phase 3A Report (Product List Migration)

## Status: PASSED for automated gates; authenticated browser verification NOT performed

The Product List (`/products`, `components/product-list/`) now runs on the shared UI kit: `app-ui-page-header`, `app-ui-data-table` (server mode), `appUiButton`, `app-ui-badge`, `appUiControl`/`app-ui-form-field`, `app-ui-state` (through the table) and `app-ui-confirm-dialog`. Search, sorting and pagination run on the server through the **existing** `POST /products/search` contract. No route, guard, service, model, backend or Flutter file was touched.

## Results

| Gate | Result |
|---|---|
| New tests | `product-list.spec.ts` **37**, `ui-data-table.component.spec.ts` **+3** (clickable rows) |
| Full unit suite | **PASS — 60 files / 528 tests** (Phase 2B baseline 59 / 488: +1 file, +40 tests). No existing test weakened; the only edit to an existing spec was additive (`rowClickable`/`rowClick` in the table spec host plus 3 tests). |
| Production build | **PASS**, no warnings |
| Bundle | Initial total **2.58 MB raw / 405.67 kB transfer** (Phase 2B 2.54 MB / 396.09 kB → **+≈0.04 MB, +9.58 kB transfer**). `main` 2.45 → 2.50 MB (385.11 → 394.69 kB). The route is eagerly imported, so the table, dialog and kit now ship in the initial bundle. Global CSS 81.46 → 81.11 kB (the old template's utility classes are gone). |
| `git diff --check` | **PASS** (tracked files); no trailing whitespace in new files |

## What changed

| File | Change |
|---|---|
| `components/product-list/product-list.ts` | Rewritten (was tracked and clean; the public route class `ProductList` is unchanged). Owns the API call, `switchMap` cancellation, `responseSequence`, category/status/advanced filters, URL state and delete flow. |
| `components/product-list/product-list.html` | Rebuilt on the kit. Cell templates keep the existing product/variant display, image, stock, price/cost and actions. |
| `components/product-list/product-list.css` | Token-based styles for cells and toolbar (`pl-*`). Previously empty. |
| `components/product-list/product-list.spec.ts` | New. |
| `shared/ui/ui-data-table.component.ts` (+spec) | Opt-in `rowClickable` input and `rowClick` output. |
| `shared/ui/ui-{page-header,card,state,alert,dialog,confirm-dialog}.component.ts`, `advanced-filter-drawer.component.ts` | `host: { '[attr.title]': 'null' }`. A static `title="…"` was staying on the component host and produced a native tooltip over the whole header/card/dialog (found while inspecting the rendered DOM). |

### Query contract (table → existing API)

| Table / feature state | `ProductSearchRequest` field |
|---|---|
| `query.search` (debounce **500 ms**, as before) | `searchQuery` |
| `query.page`, `query.pageSize` | `pageNumber`, `pageSize` |
| `query.sort.columnKey` / `direction` | `sortBy` (`name`, `category`, `price`) / `sortOrder` |
| Category, Status selects (feature-owned) | `categoryId`, `status` |
| Advanced drawer (feature-owned) | `hasVariants`, `minPrice`, `maxPrice` |

Column keys of the sortable columns equal the `sortBy` values the API already accepts. Sorting never goes unsorted (`allowUnsorted=false`), as before. Defaults are unchanged (page 1, size 10, `name asc`, status `active`).

### Request handling

- Table `queryChange` → `requests` Subject → `switchMap(productService.search(body))`: a newer query **cancels** the in-flight HTTP request (tested with `HttpTestingController`: the older request is `cancelled`).
- The component stores `rows`, `total`, `loading`, `loadError` locally and echoes `query.sequence` through `responseSequence`, so the table stays busy until the matching response and never shows "no data" mid-flight. Rows from a previous visit (the service is a root singleton) are never shown.
- `ProductService.search` converts failures into an empty result and sets `error()`; the component reads it to show the table error state and **Retry** (repeats the last query). `totalRecords` is `null` on error so the table does not drop the requested page.
- Filters that are not table state (category, status, advanced) restart from page 1 through the table (`goTo(1)` or a reload of the same query), so table and feature state cannot diverge.
- URL state (`ListStateService`) is restored on init (search, category, status, advanced filters, page, page size, sort) and written after every query, as before. **Clear** still wipes the URL (the next query does not rewrite it), resets search/filters to defaults and reloads page 1 keeping sort and page size.

### Preserved

- Product/variant display: name, description, thumbnail (opens the existing `ImageLightboxComponent`), SKU/barcode, product code, category, price, **cost only with `products.view_cost` and when > 0**, variant count / "Single", stock badge or "Out of Stock", status (Active / Draft / Inactive as text + badge).
- Actions: view, stock transactions (`/reports/stock-transactions?productId=`), edit, delete, Add Product; navigation still uses `queryParamsHandling: 'preserve'`. Whole-row click still opens the product; the name is also a real link for keyboard users.
- Category/Status/Advanced filter semantics ("All Status" sends no status), the `AdvancedFilterDrawerComponent` behaviour, `AuthService.hasPermission`, routes and guards.

### Deliberate behaviour differences (review these)

| Area | Before | Now |
|---|---|---|
| Delete confirmation | native `confirm()` | `app-ui-confirm-dialog` (destructive, Cancel focused first, busy state). Same message text. |
| Delete result | native `alert()` | existing `AlertService` (`success`/`error`). A failed delete no longer replaces the whole list with the service error (the service error is cleared; the alert reports it). |
| Pagination label | "Showing a to b of N products" | "Showing a–b of N" (table text) |
| Tooltip on kit hosts | tooltip on page header/dialog hosts (bug) | removed |
| Loading | list replaced by a spinner card | table keeps its chrome and shows loading/dims during reloads |

### Permissions note

The old page had **no permission checks on actions** other than `products.view_cost`; protection is the route guard (`products.view`, `products.create`, `products.edit` on the target routes). That is preserved exactly: Add/Edit/Delete are still visible to anyone who can open the list, and the delete API enforces authorization server-side. **Recommendation (not done, to avoid hiding actions from existing roles):** gate Add/Edit/Delete with `products.create`/`products.edit`/`products.delete` in a follow-up after confirming the role matrix.

## Tests (37 in `product-list.spec.ts`, real `ProductService` + `HttpTestingController`)

- Presentation: page header/breadcrumb/Add action; field-by-field rendering incl. variants, Single, Out of Stock, status text; no tooltip on kit hosts.
- Permissions: cost hidden without `products.view_cost` and when zero; all four row actions present with accessible names.
- Search: 500 ms debounce, one request, page reset, search kept while paging, no-results state and recovery.
- Sorting: default `aria-sort`, desc toggle, category/price keys, never unsorted, only API-sortable columns sortable, page reset.
- Pagination: next/previous/last, total and range, page size change (+URL).
- Filters: category, status ("All" = no status), empty-vs-no-match message, drawer apply/clear/applied count, Clear (URL wipe).
- URL: full restore of every parameter, URL written after queries.
- Cancellation: older request cancelled, only newest data shown, busy until it answers.
- Loading/error: loading state, API error + Retry repeats same query, page preserved on failure, empty state.
- Navigation/actions: row click, edit without row navigation, create, image lightbox, delete confirm/cancel/success/failure.

## Browser verification — what was and was not done

- **No authenticated browser verification.** The development API (`localhost:5005`) and dev server (`localhost:4200`) were not running and no safe account exists. Authentication was not bypassed and no backend was mocked.
- **Static render check only (not the authenticated app):** a temporary spec rendered the real `ProductList` with synthetic data and dumped the component DOM; that DOM plus the real built CSS was opened in the browser (the dump and spec were deleted afterwards). It confirmed: desktop layout (page header, search + filters + Advanced/Clear toolbar, 9-column table with badges and actions, footer pager), the destructive delete dialog, the advanced-filter drawer, and the error state (accessibility tree). Measured with Playwright: at **390 px** no page-level horizontal overflow (the table scrolls inside its own region, 1192 px content in 341 px), Add Product full-width, search full-width, category/status side by side; at **820 px** no overflow and the toolbar wraps to two rows. The screenshot tool lags one viewport resize behind, so mobile/tablet conclusions rely on the measurements rather than screenshots.
- Not verified: real data, real images, real currency formatting, real latency, keyboard/touch on devices, cross-browser.

## Remaining risks

- Visual regression against the live app is unverified (see above). First authenticated pass should check: 9 columns on tablets (horizontal scroll by design), long product names/descriptions, many categories in the select.
- Eager route import adds ≈9.6 kB transfer to the initial bundle; lazy-loading routes would recover it (out of scope).
- `ProductService.search` swallows errors into an empty result and signals through `error()`; the component relies on that. Changing the service later must keep a way to tell "empty" from "failed".
- Row-click remains a mouse shortcut; keyboard users use the name link and the action buttons.
- Category/Status are toolbar filters (not column filters) to keep the existing "All" semantics and async category loading; they restart through the table, so a future table API change to page/filter state must keep `restartFromFirstPage()` consistent.
- Other list screens still use legacy classes; nothing outside the Product List was migrated (Phase 3B not started).

## Changed-file inventory

Modified (tracked): `product-list/product-list.{ts,html,css}`, `alert/alert.component.ts` and `advanced-filter-drawer/advanced-filter-drawer.component.ts` (Phase 2B edits; the drawer also gets the host-title fix in this phase).
New (untracked): `product-list/product-list.spec.ts`; the `shared/ui/` directory (Phase 2A/2B, with this phase's `rowClick` and host-title changes).
Docs: this report, `MIGRATION_ROADMAP.md` (status note).
Untouched: backend, Flutter, routes, guards, `ProductService`, `CategoryService`, `ListStateService`, `AlertService`, interceptors, other feature modules, purchased Able Pro source.
