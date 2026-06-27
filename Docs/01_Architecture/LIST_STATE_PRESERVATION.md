# List State Preservation

Updated: 2026-06-07

## Overview

This document defines the **application-wide standard** for preserving list page state across navigation in the RetailPOS frontend. All list pages must implement this standard consistently to ensure a professional, friction-free user experience.

---

## 1. Problem Statement

### Current Behaviour (Without This Standard)

When a user navigates away from a list page — to an edit form, a detail view, or a create form — and then navigates back, the list page resets to its default state. This means:

- All applied search text is lost.
- All applied filters are cleared.
- Active/inactive status selections are reset.
- The pagination cursor resets to page 1 with the default page size.
- Sorting column and direction revert to defaults.
- Selected tab (e.g., Active / Inactive / All) reverts to the default tab.
- View mode (e.g., table vs. card view) reverts to default.

### Impact on Users

- Users must repeatedly re-enter search queries and reapply filters after every navigation.
- In large datasets, this forces the user to scroll back to the record they were working on.
- It creates a disjointed workflow, especially in high-volume operational contexts (e.g., sales entry, stock adjustment, customer management).
- Trust in the interface is eroded when the system appears to "forget" what the user was doing.

---

## 2. Expected Behaviour (After This Standard)

When a user navigates back to a list page after any forward navigation, **the list page must restore to the exact state it was in before the user left it**.

### State Properties to Preserve

| State Property          | Description                                                                 |
|-------------------------|-----------------------------------------------------------------------------|
| **Search text**         | The text entered in the search/filter input field.                         |
| **Column filters**      | Any per-column or panel-based filter values (e.g., date range, category).  |
| **Status selection**    | Active / Inactive / All or any equivalent status toggle.                   |
| **Pagination**          | Current page number and selected page size (e.g., page 3, 20 per page).    |
| **Sorting**             | The active sort column and sort direction (ascending/descending).           |
| **Selected tab**        | The active tab on tabbed list views (e.g., Pending / Approved / Rejected). |
| **View mode**           | The selected layout (e.g., table view vs. card/grid view).                 |

### What Does NOT Need to Be Preserved

- Checkbox row selections (select-all, individual row select).
- Inline editing state.
- Unsaved form data outside the list.
- Temporary UI state (e.g., open dropdowns, tooltips, modal state).

---

## 3. Scope

The following list pages **must** implement list state preservation. This list is exhaustive for currently planned modules and applies to all future list pages by default.

### Currently Defined List Pages

| Module            | Page / Route                       |
|-------------------|------------------------------------|
| Users             | `/settings/users`                  |
| Roles             | `/settings/roles`                  |
| Products          | `/products` (planned route)        |
| Categories        | `/categories` (planned route)      |
| Inventory         | `/inventory` (planned route)       |
| Warehouses        | `/settings/warehouses`             |
| Outlets           | `/settings/outlets`                |
| Suppliers         | `/suppliers`                       |
| Customers         | `/customers`                       |
| Sales             | `/sales`                           |
| Purchases         | `/purchase-orders`                 |
| Expenses          | `/accounting/expenses`             |
| GRN               | `/grn`                             |
| Stock Adjustments | `/stock-adjustments`               |
| Stock Transfers   | `/stock-transfers`                 |
| Businesses        | `/businesses` (super admin)        |

### Future List Pages

Any new list page added in the future **must** implement this standard from the outset. See the [Implementation Guidance](#7-implementation-guidance-for-frontend-developers) section.

---

## 4. Navigation Behaviour

The following navigation flows must all result in full state restoration:

### 4.1 List → Edit → Back

```
User opens /customers
Applies: search="Ahmad", status=Active, page=3
Clicks "Edit" on a customer row → navigates to /customers/42/edit
Makes edits and saves (or discards)
Navigates back (browser back button, or explicit "Back to Customers" link)

Expected result: /customers restores search="Ahmad", status=Active, page=3
```

### 4.2 List → Detail View → Back

```
User opens /purchase-orders
Applies: status=Pending, sortBy=createdAt desc, page=2
Clicks on a row → navigates to /purchase-orders/88
Reads the detail and clicks back

Expected result: /purchase-orders restores status=Pending, sortBy=createdAt desc, page=2
```

### 4.3 List → Create → Cancel / Back

```
User opens /suppliers
Applies: search="Ali", pageSize=50
Clicks "Add Supplier" → navigates to /suppliers/new
User fills in some fields, then clicks "Cancel" or browser back

Expected result: /suppliers restores search="Ali", pageSize=50
```

### 4.4 Browser Back Button

The browser's native back button must restore state identically to the application navigation links. This is guaranteed when the URL query parameter approach is used (see [Section 6](#6-recommended-implementation)).

### 4.5 Deep Linking

If a user copies the URL `/customers?search=Ahmad&status=active&page=3&pageSize=20&sortBy=name&sortDir=asc` and opens it in a new tab or sends it to a colleague, the page must load in that exact state.

---

## 5. Refresh Behaviour

Refresh behaviour differs by implementation approach:

| Approach              | Refresh Behaviour                                                                                              |
|-----------------------|----------------------------------------------------------------------------------------------------------------|
| **URL query params**  | State is preserved on refresh because the URL still contains the query parameters. **This is the expected and desired behaviour.** |
| **ListStateService**  | State is lost on hard refresh because in-memory service state is cleared. This is acceptable only for supplemental state (e.g., view mode) that cannot be encoded in the URL. |

**Standard:** The URL query parameter approach is preferred precisely because it survives browser refresh and supports deep linking. The `ListStateService` is an acceptable fallback only for state that is awkward or impractical to encode in a URL (e.g., complex multi-dimensional filter objects).

### Intentional State Reset

State should be explicitly reset only when:

- The user clicks a "Clear Filters" or "Reset" button.
- The user navigates to the list from the main sidebar/menu (top-level navigation), not from a child route back-navigation. This signals fresh intent.

---

## 6. Recommended Implementation

### Preferred Approach: URL Query Parameters

Encode all list state as URL query parameters. Angular's `Router` and `ActivatedRoute` make this straightforward.

**URL format:**

```
/users?search=john&status=active&page=2&pageSize=20&sortBy=name&sortDir=asc&tab=all
```

**Standard query parameter names:**

| Parameter   | Type    | Example Values              | Description                          |
|-------------|---------|-----------------------------|--------------------------------------|
| `search`    | string  | `john`, `ali`               | The search/filter text input value.  |
| `status`    | string  | `active`, `inactive`, `all` | Status filter selection.             |
| `page`      | integer | `1`, `2`, `3`               | Current page number (1-based).       |
| `pageSize`  | integer | `10`, `20`, `50`, `100`     | Number of records per page.          |
| `sortBy`    | string  | `name`, `createdAt`         | The column to sort by.               |
| `sortDir`   | string  | `asc`, `desc`               | Sort direction.                      |
| `tab`       | string  | `all`, `pending`, `active`  | Active tab identifier.               |
| `view`      | string  | `table`, `card`             | Active view mode.                    |

Additional module-specific filter parameters may be added alongside these (e.g., `categoryId=5`, `warehouseId=12`).

#### Sentinel value convention for nullable ID filters

Some filters distinguish between three states: **all records** (no filter), **records with no assignment** (IS NULL), and **records assigned to a specific entity**. Use `0` as a sentinel value for the "no assignment" state:

| URL param value | Meaning |
|---|---|
| absent | No filter — return all records |
| `0` | Return records where the FK column IS NULL |
| `> 0` | Return records assigned to that specific ID |

Example: `/users?outletId=0` → users with no outlet assigned; `/users?outletId=3` → users in outlet 3.

On the frontend, use `listState.numOrZero(params, 'outletId')` to restore this filter (returns `undefined` if absent, `0` if `'0'`, positive number otherwise). Pass the value directly to `listState.update()` — the service excludes `undefined` and `null` but keeps `0` in the URL.

#### Sentinel value convention for nullable ID filters

Some filters distinguish between three states: **all records** (no filter), **records with no assignment** (IS NULL), and **records assigned to a specific entity**. Use `0` as a sentinel value for the "no assignment" state:

| URL param value | Meaning |
|---|---|
| absent | No filter — return all records |
| `0` | Return records where the FK column IS NULL |
| `> 0` | Return records assigned to that specific ID |

Example: `/users?outletId=0` → users with no outlet assigned; `/users?outletId=3` → users in outlet 3.

On the frontend, use `listState.numOrZero(params, 'outletId')` to restore this filter (returns `undefined` if absent, `0` if `'0'`, positive number otherwise). Pass the value directly to `listState.update()` — the service already excludes `undefined` and `null` but keeps `0` in the URL.

**Implementation flow:**

1. On component init, read query params from `ActivatedRoute.queryParams`.
2. Populate the filter form / component state from query params.
3. Trigger the data fetch using the state read from query params.
4. On any state change (search, filter, page change, sort), call `Router.navigate` with `queryParams` and `queryParamsHandling: 'merge'` to update the URL without triggering a full navigation.

**Example Angular pattern (conceptual):**

```typescript
// On init — read state from URL
this.route.queryParams.pipe(takeUntilDestroyed()).subscribe(params => {
  this.filters = {
    search:   params['search']   ?? '',
    status:   params['status']   ?? 'all',
    page:     Number(params['page']    ?? 1),
    pageSize: Number(params['pageSize'] ?? 20),
    sortBy:   params['sortBy']   ?? 'createdAt',
    sortDir:  params['sortDir']  ?? 'desc',
  };
  this.loadData();
});

// On state change — write state to URL
updateFilters(patch: Partial<ListFilters>) {
  this.router.navigate([], {
    relativeTo: this.route,
    queryParams: patch,
    queryParamsHandling: 'merge',
    replaceUrl: true,
  });
}
```

> `replaceUrl: true` is recommended for filter/sort/page changes so that pressing the browser back button skips individual filter increments and returns to the previous page, rather than stepping through every filter change.

---

### Alternative Approach: `ListStateService`

A shared Angular service that stores list state in memory, keyed by route path.

**When to use:**

- As a complement to URL params for state that is complex to serialize into a URL (e.g., a multi-select filter with many values, object-shaped filters).
- As a fallback when URL param implementation is impractical for a specific page.

**Limitations:**

- State is lost on hard browser refresh.
- Does not support deep linking or sharable URLs.
- Requires careful lifecycle management to avoid stale state.

**Service interface (conceptual):**

```typescript
interface ListState {
  search?: string;
  filters?: Record<string, unknown>;
  page?: number;
  pageSize?: number;
  sortBy?: string;
  sortDir?: 'asc' | 'desc';
  tab?: string;
  viewMode?: string;
}

@Injectable({ providedIn: 'root' })
class ListStateService {
  private states = new Map<string, ListState>();

  save(key: string, state: ListState): void { ... }
  load(key: string): ListState | null { ... }
  clear(key: string): void { ... }
}
```

The `key` should be the canonical route path (e.g., `/customers`, `/purchase-orders`).

---

## 7. Acceptance Criteria

The following criteria must be met before a list page is considered compliant with this standard.

### Navigation Criteria

- [ ] Navigating from the list to an edit form and back restores all preserved state properties exactly.
- [ ] Navigating from the list to a detail view and back restores all preserved state properties exactly.
- [ ] Navigating from the list to a create form, cancelling, and going back restores all preserved state properties exactly.
- [ ] Pressing the browser back button from a child page restores list state.

### URL Criteria (if URL param approach is used)

- [ ] All preserved state properties are reflected in the URL query string.
- [ ] Pasting the URL into a new browser tab loads the list in the exact state encoded in the URL.
- [ ] Refreshing the browser on a list page does not reset the state.

### Reset Criteria

- [ ] Clicking "Clear Filters" resets all query parameters and returns the list to its default state.
- [ ] Navigating to the list from the top-level sidebar menu resets state to defaults (fresh navigation intent).

### State Property Criteria

- [ ] Search text is preserved.
- [ ] Status filter (Active / Inactive / All) is preserved.
- [ ] Pagination (page number and page size) is preserved.
- [ ] Sort column and sort direction are preserved.
- [ ] Active tab selection is preserved (where tabs are present).
- [ ] View mode (table / card) is preserved (where multiple view modes are present).

### Module-Level Coverage

- [ ] All list pages listed in [Section 3 – Scope](#3-scope) implement this standard.

---

## 8. Implementation Guidance for Frontend Developers

### Starting a New List Page

When building a new list page in Angular, follow these steps from the outset:

1. **Define the state shape.** Identify all filterable, sortable, and pageable properties for this list. Define a `<Module>ListFilters` interface.

2. **Choose the implementation approach.** Default to URL query parameters. Only use `ListStateService` if serialising state to URL params is genuinely impractical.

3. **Subscribe to `ActivatedRoute.queryParams` on init.** Populate the filter form from URL params as the source of truth.

4. **Emit state changes as URL updates.** Use `Router.navigate` with `queryParamsHandling: 'merge'` for every filter, sort, or page change.

5. **Derive default values.** Always provide safe defaults when a query param is absent (e.g., `page ?? 1`, `pageSize ?? 20`).

6. **Handle "Clear Filters".** Navigate to the same route with `queryParams: {}` and `queryParamsHandling: ''` (replace, not merge) to wipe all params.

7. **Handle sidebar navigation.** Detect whether navigation originated from the main menu vs. back-navigation, and reset state on fresh menu navigation if required. This can be done via a `NavigationExtras.state` flag or by checking `NavigationStart.navigationTrigger`.

### Naming Conventions

- Use consistent parameter names as defined in Section 6 across all modules.
- Module-specific additional filters should use camelCase parameter names (e.g., `categoryId`, `warehouseId`).
- Avoid abbreviating parameter names — prefer readability.

### Pitfalls to Avoid

| Pitfall | Why It's a Problem | Resolution |
|---|---|---|
| Storing state only in component variables | Lost on navigation | Use URL params or `ListStateService`. |
| Using `queryParamsHandling: 'merge'` for "Clear Filters" | Leaves stale params in URL | Use explicit empty `queryParams: {}` on clear. |
| Not providing default values when reading params | `undefined` causes unexpected behaviour | Always coerce with `?? defaultValue`. |
| Using `replaceUrl: false` for every filter change | Back button steps through every filter change | Use `replaceUrl: true` for incremental filter/sort/page changes. |
| Triggering data load outside the `queryParams` subscription | Causes double-fetch or missed updates | Drive all data loads from the `queryParams` subscription. |

### Testing Checklist for List Pages

- Navigate to a list, apply filters, navigate away, navigate back — verify state is restored.
- Refresh the page — verify URL-encoded state survives refresh.
- Copy and paste the URL into a new tab — verify the list loads in the correct state.
- Click "Clear Filters" — verify all params are removed and the list resets.
- Click the sidebar link to the same list — verify state resets (fresh intent).

---

## 9. Related Documents

- [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md) — overall project architecture
- [FRONTEND_ARCHITECTURE.md](FRONTEND_ARCHITECTURE.md) — frontend conventions and standards
- [UI_UX_STANDARDS.md](UI_UX_STANDARDS.md) — UI/UX standards (planned)
- [PERMISSION_MODEL.md](../02_Security/PERMISSION_MODEL.md) — permission-gated navigation context

---

*This document is the authoritative standard for list state preservation in the RetailPOS frontend. All list pages must comply.*
