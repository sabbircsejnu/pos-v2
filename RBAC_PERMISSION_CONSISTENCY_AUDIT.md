# RBAC Permission Consistency Audit

Scope: permission registry, Role Edit, sidebar, Angular routes/actions, API policies. No new authorization architecture; no new permission codes.

## 1. Root causes

**Issue 1 – Inventory Advanced Filters 403 (Outlet Manager).** The drawer embeds the shared `app-variant-search-picker`.
- On init it calls `GET /api/variations` (class policy `variations.view`) only for display metadata.
- Its search/prefill calls `GET /api/products/variants/search`, guarded by `ProductsController`'s class-level `products.view`.
- A role with `inventory.view` but without those unrelated codes got a 403, which the error interceptor shows as the "You do not have permission…" dialog.
- `GET /api/inventory/variant-attribute-filters` and `GET /api/me/outlets` were already correct.
- *Inferred from code; not reproduced in a live browser. The reporter's exact role permission set is unknown.*

**Issue 2 – Finance permissions.**
- Definitions exist in `PermissionCatalog`, and `GET /api/roles/permissions` returns the whole catalog. Seeding is not a factor: roles store codes as JSON and there is no permission table.
- Expenses and Expense Categories have no dedicated codes. Both use `accounting.expenses.view`, and category create/update uses `accounting.expenses.edit`.
- Role Edit listed them under "Accounting Expenses" (no "Finance" label, no mention of categories).
- 26 implemented Finance permissions were not in Role Edit at all: statement import, settlements, liquidity transfers, reconciliation, adjustments, exceptions, close (the Bank Reconciliation and Close Workbench menu entries could not be granted), and `financial_years.reopen`.
- *I could not reproduce "expenses.* missing from Role Edit" in the current source; they were already listed. A stale build or the naming is the likely explanation (unverified).*

**Issue 3 – Other inconsistencies.**
- 13 enforced payroll permissions were missing from Role Edit.
- Frontend alias `settings.view/edit` → `accounting.configuration.*` has no backend counterpart: the menu showed, the API answered 403.
- The "Reports" menu (`reports.sales` or `reports.inventory`) linked to a route requiring `reports.sales`. The Supplier Payments menu (payments or advances view) linked to a route requiring payments-view.
- Write actions on the Expense Categories, Supplier Payments and Accounting Configuration pages were shown to view-only users, who then received a 403 dialog.

## 2. Modules audited
Registry, Role Edit, sidebar, all route guards, `AuthService`/aliases, all API controllers (per-action policy scan), role templates (`BusinessRolePermissions`, `AccountingManagerPermissions`, `DbSeeder`), 403 handling. Deep UI/action review covered Inventory and Finance. Catalog, menu, route and policy checks covered HR/Payroll; their page-level action gating was not reviewed.

## 3. Missing / mismatched items found
- Unassignable in Role Edit: 26 `accounting.*` and 13 `payroll.*` codes (fixed). Legacy `accounts.*`, `transactions.*`, `expenses.*` and `accounting.dashboard.view` are deliberately hidden (see 7).
- No frontend or backend code references an unregistered permission. All controller policies resolve.
- Frontend alias ≠ backend alias: `accounting.configuration.*`←`settings.*` (removed); `accounting.journals.view`←`accounting.manual_journals.view` (added to frontend to match backend).
- Menu-without-route-access: Reports, Supplier Payments (fixed).
- Lookup endpoints blocking authorized roles: variant search, variations (fixed).

## 4. Fixes implemented
- New `LookupAuthorizationPolicies` (`lookup.product_variants`, any-of: `products.view`, `inventory.view`, `stock_adjustments.create|edit`, `stock_transfers.view|create`, `purchases.create|edit`, `reports.inventory`). These are the consumers of the picker found in the frontend. Registered in `Program.cs`.
- `GET /api/products/variants/search` moved unchanged (same route, location authorization, cost redaction) into `ProductVariantLookupController`.
- Picker loads variation metadata only with `variations.view`.
- Role Edit: Finance groups mirroring the sidebar, `financial_years.reopen`, new groups for statements/settlements/transfers, reconciliation/adjustments, period close. The Expenses group is labelled for Expense Categories. The payroll codes were added to `hr-payroll-permissions.ts`.
- Menu split into Sales Reports and Inventory Reports. Supplier Payments route accepts payments-view or advances-view.
- Action gating added on the Expense Categories, Supplier Payments and Accounting Configuration pages.
- `Docs/02_Security/PERMISSION_MODEL.md` updated.
- 403 handling was already correct (no logout, message only); covered by the existing `session.interceptor.spec.ts`, which passes.

## 5. Files changed
Backend: `src/RetailPOS.API/Authorization/LookupAuthorizationPolicies.cs` (new), `Controllers/ProductVariantLookupController.cs` (new), `Controllers/ProductsController.cs`, `Program.cs`.
Frontend (`retailpos-frontend/src/app`): `components/variant-search-picker/*.ts` (+spec), `components/roles/role-form.component.ts` (+new spec), `models/hr-payroll-permissions.ts`, `models/menu.model.ts`, `models/menu-route-consistency.spec.ts`, `app.routes.ts`, `services/auth.service.ts` (+spec), `pages/accounting/expense-categories.component.html`, `supplier-payments.component.html`, `accounting-configuration.component.{ts,html}`.
Tests: `tests/RetailPOS.IntegrationTests/Security/RbacPermissionConsistencyTests.cs` (new), `StockLocationAuthorizationHttpTests.cs`, `Accounting/AccountingPermissionRegressionTests.cs`.
Docs: `Docs/02_Security/PERMISSION_MODEL.md`, this report.

## 6. Tests and results
| Run | Result |
|---|---|
| Frontend `npm test` (81 files) | 1078 / 1078 passed |
| Frontend `npm run build` | Succeeded |
| Backend unit tests | 541 / 541 passed |
| Focused integration (RBAC consistency, accounting permission regression, stock-location/variant lookup) | 61 / 61 passed |
| Full integration suite (run twice) | 1845 / 1851 passed; 6 failures, all unrelated to this change (see below) |

The six full-suite failures:
- `StockCountPhase1HttpTests.Download_WithPermission_ReturnsExcelFile` (Excel date format).
- `CleanArchitectureDependencyTests…` (`MobileSingleBusinessReadinessGuard` references `DbContext`).
- Four `PosLookupHttpTests` (`/api/pos` outlet lookups return 403).

All six are in files that were already uncommitted in the working tree, and none touches code changed here. *I did not prove them against a clean baseline.*

Behaviours now covered by automated tests:
- Variant lookup is open to the listed screen permissions and rejects unrelated ones (`customers.view`, `variations.view`, `inventory.edit`, `sales.view`).
- Unassigned and foreign-business outlets are still 403 for `inventory.view`.
- The Business Owner wildcard works; cost redaction is intact; Super Admin global location access is unchanged.
- Role Edit exposes the Finance groups, and the permissions persist on update (frontend spec with a mocked `RoleService`).
- The frontend alias mirror never exceeds the backend aliases.

## 7. Remaining issues and limits
- **Not verified live.** No API or browser was running; HTTP behaviour was checked in the in-process test host, UI via component specs and build. The "Outlet Manager can use Advanced Filters" scenario is verified at API and spec level only. Sidebar-versus-grant matching is covered by menu specs, not by representative roles in a browser.
- **Seeding:** nothing in seeding was changed. `DbSeeder` idempotency is covered by existing tests (`AccountingOperationalReadinessSlice1Tests` runs it twice). No migration was needed, and no role assignments were modified.
- **Registered but not enforced or not assignable:**
  - `accounting.dashboard.view` has no endpoint or screen. It is seeded and deliberately not offered in Role Edit.
  - `accounting.reports.expense|income|cash_closing` are assignable but no endpoint or route uses them.
  - A literal scan found no policy reference for several other registered codes (for example `inventory.create|delete`, `low_stock_alerts.create|edit|delete`, `stock_adjustments.cancel`, `stock_transfers.delete`, `reports.financial|export`). They may be enforced inline; each was not verified individually.
- **Lookups still tied to other permissions** (not changed, to avoid broadening access):
  - Supplier Payments loads suppliers via `suppliers.view`.
  - Accounting Configuration loads account names via `accounting.chart_accounts.view`.
  - The Supplier Payments page appears to use sample data and has no list endpoint (not confirmed).
- **Role templates:** the onboarding `OutletManager` template grants no `inventory.*` or `variations.view`. Left as is, since adding them would broaden access.
- **Legacy codes:** roles holding legacy `expenses.*`, `accounts.*` or `transactions.*` are honoured via aliases but show unchecked in Role Edit. The frontend also still accepts the legacy `pos.sale.backdate`, which the backend normalizes at login.
- **Observed, not changed:** the Expenses list calls `DELETE /api/accounting/expenses/{id}`, and `AccountingExpensesController` has no such endpoint.
