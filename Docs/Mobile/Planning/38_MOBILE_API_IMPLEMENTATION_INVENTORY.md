# Mobile API Implementation Inventory

**Audit date:** 2026-10-15  
**Scope:** Source-verified backend API surfaces relevant to the Mobile SRS/LLD v1.4, including dedicated `/api/mobile/v1/` routes and existing ERP candidates. This is an implementation inventory, not approval to consume a general ERP API from Flutter.  
**Architecture:** Single-Business, Multi-Outlet. Mobile Catalogue, Inventory and Stock Take are restricted to Business Owner and Sales Manager (including existing `Manager` / `OutletManager` mappings). Salesperson and Cashier remain denied these modules. Existing ERP/web POS grants and workflows are unchanged.

## Status and counting rules

- **IMPLEMENTED & TESTED** — dedicated Mobile route exists and has route-level coverage in the recorded PostgreSQL HTTP acceptance evidence.
- **IMPLEMENTED — TESTING INCOMPLETE** — route exists, but the latest source changes are newer than the recorded passing test run.
- **PARTIALLY IMPLEMENTED** — related ERP behavior exists but a required Mobile-safe contract, scope, or integration behavior is absent.
- **NOT IMPLEMENTED** — no suitable route/feature was found in the current source.
- **BLOCKED** — route or underlying behavior exists, but a named financial/security/business acceptance prevents use.
- **DEFERRED** — explicitly outside the current release.

Route counts refer only to dedicated `/api/mobile/v1/` action routes. A generic ERP endpoint is not counted as a Mobile endpoint and is not considered Mobile-ready. For an ERP route with action-specific policies, “ERP policy” means the endpoint’s existing authorization and scope implementation; it does not imply an approved Mobile role grant.

## Summary

| Measure | Result |
|---|---:|
| Dedicated Mobile API endpoints currently implemented | **21** |
| Dedicated Mobile endpoints with recorded passing route tests | **21** (the 17 earlier routes, including the three API-05B routes previously flagged for rerun, passed in the 2026-10-17 96-test focused run; the four API-05D routes passed 15 new PostgreSQL HTTP tests) |
| Dedicated Mobile endpoints whose latest changes still require test rerun | **0** |
| Dedicated Mobile routes not implemented | **No defensible route count**; the SRS/LLD describes capabilities, not approved route definitions |
| Dedicated Mobile endpoints partially implemented as routes | **0** |
| Dedicated Mobile endpoints blocked from their intended use | **1** (`dashboard/sales`, because financial measures are unapproved) |
| SRS capability groups with no suitable Mobile API | **2** (notifications, forecasting) |
| SRS capability groups with partial API surface | **9** |
| Main SRS capability groups with at least one suitable API slice | **9 of 12 (75%)** |
| Main SRS capability groups fully ready end-to-end | **0 of 12** |

The 75% figure is an **estimated capability-surface presence proxy**, calculated as the nine partially surfaced groups divided by the twelve groups in the requested inventory. It is not a claim that 75% of Mobile features are complete. Authentication/profile, context, dashboard/insights, catalogue, inventory, Stock Take sessions/count synchronization, and settings have partial surfaces; asynchronous Stock Take submission and review/results (API-05C/05D, contract-limited) have partial surfaces; notifications and forecasting have no suitable Mobile API. The route denominator for an exact SRS endpoint-coverage percentage is not specified by the SRS/LLD, so no exact overall endpoint percentage can be established without inventing required route counts.

## Dedicated Mobile API endpoints

### 1. Authentication and User Profile

There is **no dedicated Mobile authentication/profile endpoint** under `/api/mobile/v1/`. General ERP Auth routes are catalogued below. Native-client token lifecycle, stable mobile profile/permissions DTO, role-aware navigation contract, client cache clearing, and mobile session restoration have not been accepted as a complete integration contract.

### 2. Business and Outlet Context

| Method / full route | Purpose | Allowed roles | Required permission | Status | Tests |
|---|---|---|---|---|---|
| `GET /api/mobile/v1/inventory/outlets` | Return the caller’s server-authorized Outlets for Mobile inventory/context selection | Business Owner; Sales Manager (`SalesManager`, `Manager`, `OutletManager` mappings) | `inventory.view` | **IMPLEMENTED & TESTED** for the inventory Outlet-list contract; not a general profile/context API | `MobileInventoryPostgresHttpTests.InventoryReadsEnforceMobileRolesAndAuthorizedOutletScope`; authorized scope and role denial assertions |

No Mobile context-switch endpoint accepts client-provided Business, role, or arbitrary Outlet as authority. Outlet selection must be checked against the returned server-authorized set.

### 3. Owner / Manager / Salesperson Dashboard

| Method / full route | Purpose | Allowed roles | Required permission | Status | Tests |
|---|---|---|---|---|---|
| `GET /api/mobile/v1/dashboard/sales` | Return sales dashboard totals, net revenue, invoice/unit counts, and average invoice value | Business Owner / Manager: authorized Business or Outlet scope; Salesperson: own sales only; Cashier denied | Owner/Manager: `reports.sales`; Salesperson: `sales.view` | **BLOCKED** for financial KPI integration. Route exists, but VAT/refund and invoice-discount accounting reconciliation is deferred; do not consume monetary or average values as approved KPIs | `MobileSalesDashboardPostgresHttpTests`; route and authorization tests are recorded, but financial acceptance is explicitly pending |
| `GET /api/mobile/v1/insights/sales/activity` | Return UTC zero-filled daily counts of eligible invoices, without financial measures | Business Owner / Manager: authorized Outlet scope; Salesperson: self-only; Cashier denied | Owner/Manager: `reports.sales`; Salesperson: `sales.view` | **IMPLEMENTED & TESTED** for non-financial invoice activity only | `MobileSalesDashboardPostgresHttpTests`; status filtering, scope, zero-fill, UTC bounds, range limit and denials |

The activity endpoint does not establish sales trends by amount, target attainment, outlet comparison, top products, or discount analysis. The dashboard’s monetary values are not production-approved.

### 4. Sales Insights and Reports

| Method / full route | Purpose | Allowed roles | Required permission | Status | Tests |
|---|---|---|---|---|---|
| `GET /api/mobile/v1/insights/sales/activity` | UTC invoice activity trend (same route as above) | Owner/Manager authorized Outlet scope; Salesperson self-only; no Cashier | Owner/Manager: `reports.sales`; Salesperson: `sales.view` | **IMPLEMENTED & TESTED** for invoice counts only | `MobileSalesDashboardPostgresHttpTests` |
| `GET /api/mobile/v1/dashboard/sales` | Monetary totals and dashboard measures (same route as above) | Owner/Manager and Salesperson under server-derived scopes; no Cashier | `reports.sales` or `sales.view` by role | **BLOCKED** for financial integration | `MobileSalesDashboardPostgresHttpTests`; accounting acceptance remains deferred |
| `GET /api/reports/sales/summary` | ERP sales totals, discount/tax/net revenue, transactions and average order value | ERP roles granted `reports.sales`, with Outlet filter resolved by ERP access service | `reports.sales` | **PARTIALLY IMPLEMENTED** as ERP only; not approved for Mobile KPI use | No Mobile-specific authorization/financial acceptance test |
| `GET /api/reports/sales/top-products` | ERP top-selling Product/Variant report | ERP roles granted `reports.sales`, Outlet filter resolved by ERP access service | `reports.sales` | **PARTIALLY IMPLEMENTED** as ERP only; no Mobile-safe response/scope acceptance | No Mobile-specific authorization test |
| `GET /api/reports/sales/by-outlet` | ERP sales totals grouped by Outlet | ERP roles granted `reports.sales`; non-owner scope is narrowed by ERP Outlet access | `reports.sales` | **PARTIALLY IMPLEMENTED** as ERP only; mobile cross-Outlet contract and mobile-specific authorization tests absent | No Mobile-specific authorization test |
| `GET /api/reports/sales/by-payment-method` | ERP sales grouped by payment method | ERP roles granted `reports.sales`, Outlet filter resolved by ERP access service | `reports.sales` | **PARTIALLY IMPLEMENTED** as ERP only; no Mobile-safe DTO/field approval | No Mobile-specific authorization test |
| `GET /api/reports/sales/daily-trend` | ERP daily sales trend, including financial measures | ERP roles granted `reports.sales`, Outlet filter resolved by ERP access service | `reports.sales` | **PARTIALLY IMPLEMENTED** as ERP only; financial definitions and Mobile scope not accepted | No Mobile-specific authorization/financial test |

### 5. Product Catalogue and Variants

All six online catalogue routes have typed Mobile DTOs, bounded search/pagination where applicable, server-derived authorization, and single-Business readiness checks. They are limited to Business Owner and Sales Manager mappings; no Salesperson/Cashier access.

| Method / full route | Purpose | Allowed roles | Required permission | Status | Tests |
|---|---|---|---|---|---|
| `GET /api/mobile/v1/catalogue/categories` | List catalogue categories | Owner / Sales Manager mappings | `products.view` | **IMPLEMENTED & TESTED** for online reads | `MobileProductCataloguePostgresHttpTests`; role, Business guard and response allowlist coverage |
| `GET /api/mobile/v1/catalogue/products` | Search/list products with bounded page and filters | Owner / Sales Manager mappings | `products.view` | **IMPLEMENTED & TESTED** for online reads | `MobileProductCataloguePostgresHttpTests`; pagination/search and authorization assertions |
| `GET /api/mobile/v1/catalogue/products/{productId}` | Read product details | Owner / Sales Manager mappings | `products.view` | **IMPLEMENTED & TESTED** | `MobileProductCataloguePostgresHttpTests` |
| `GET /api/mobile/v1/catalogue/products/{productId}/variants` | Read a product’s variants | Owner / Sales Manager mappings | `products.view` | **IMPLEMENTED & TESTED** | `MobileProductCataloguePostgresHttpTests`; bounded paging |
| `GET /api/mobile/v1/catalogue/products/{productId}/variants/{variantId}` | Read a specific Product Variant | Owner / Sales Manager mappings | `products.view` | **IMPLEMENTED & TESTED** | `MobileProductCataloguePostgresHttpTests` |
| `GET /api/mobile/v1/catalogue/barcodes/{barcode}` | Exact barcode lookup; preserves Product/Variant barcode identity | Owner / Sales Manager mappings | `products.view` | **IMPLEMENTED & TESTED** for online exact lookup | `MobileProductCataloguePostgresHttpTests`; exact and unmatched barcode scenarios |

**Integration limit:** controlled online catalogue reads only. Offline snapshot/delta/tombstone, catalogue revision, and versioning contracts do not exist. Deployment catalogue provenance and legacy global Product/Category ownership must be verified; B1–B4 ownership restructuring remains deferred.

### 6. Inventory and Stock Availability

| Method / full route | Purpose | Allowed roles | Required permission | Status | Tests |
|---|---|---|---|---|---|
| `GET /api/mobile/v1/inventory/outlets` | List server-authorized Outlets | Owner / Sales Manager mappings | `inventory.view` | **IMPLEMENTED & TESTED** | `MobileInventoryPostgresHttpTests`; role and scope checks |
| `GET /api/mobile/v1/inventory/outlets/{outletId}/stock` | Search/paginate Outlet stock by text, Product, Variant and optional low-stock filter | Owner / Sales Manager mappings; requested Outlet must be authorized | `inventory.view`; `low_stock_alerts.view` additionally required for low-stock filtering and disclosure | **IMPLEMENTED & TESTED** for online authorized stock reads | `MobileInventoryPostgresHttpTests`; pagination, Outlet restrictions, low-stock permission and field allowlist |

The Mobile routes expose no stock mutation. Online freshness metadata and offline synchronization are absent. The remaining ERP Inventory routes below are not a Mobile substitute.

### 7. Stock Take Sessions

| Method / full route | Purpose | Allowed roles | Required permission | Status | Tests |
|---|---|---|---|---|---|
| `GET /api/mobile/v1/stock-take/sessions` | Paged session list filtered by authorized Outlet, status and search | Owner / Sales Manager mappings | `StockCount.ViewOwn` or `StockCount.ViewAll` | **IMPLEMENTED & TESTED** for online session reads | `MobileStockTakePostgresHttpTests`; scope, filters and denials |
| `GET /api/mobile/v1/stock-take/sessions/{sessionId}` | Read authorized session details and safety metadata | Owner / Sales Manager mappings | `StockCount.ViewOwn` or `StockCount.ViewAll` | **IMPLEMENTED & TESTED** | `MobileStockTakePostgresHttpTests`; DTO allowlist and authorization assertions |
| `GET /api/mobile/v1/stock-take/sessions/{sessionId}/lines` | Paged counted line reads; does not expose expected stock or variance | Owner / Sales Manager mappings | `StockCount.ViewOwn` or `StockCount.ViewAll` | **IMPLEMENTED & TESTED** | `MobileStockTakePostgresHttpTests`; paging and authorized session checks |
| `POST /api/mobile/v1/stock-take/sessions` | Create a Mobile session through existing StockCount business logic | Owner / Sales Manager mappings; Outlet must be authorized | `StockCount.Create` | **IMPLEMENTED & TESTED** for online creation | `MobileStockTakePostgresHttpTests`; create permissions, Business/Outlet guard and session metadata |

These APIs do not lock a session to a device or pin a catalogue revision. Baseline read timestamps do not establish an atomic catalogue/inventory snapshot or an authoritative inventory movement cursor.

### 8. Stock Take Count Operations and Offline Synchronization

| Method / full route | Purpose | Allowed roles | Required permission | Status | Tests |
|---|---|---|---|---|---|
| `POST /api/mobile/v1/stock-take/sessions/{sessionId}/operations:batch` | Submit up to 100 revision-checked, idempotent `SetQuantity` operations | Owner / Sales Manager mappings; session Outlet must be authorized | `StockCount.Upload` | **IMPLEMENTED & TESTED** (rerun 2026-10-17, API-05D regression) | `MobileStockTakePostgresHttpTests`, `MobileStockTakeSubmissionPostgresHttpTests` |
| `GET /api/mobile/v1/stock-take/sessions/{sessionId}/operations?afterRevision={revision}` | Read accepted operations after a revision, paged by server cursor | Owner / Sales Manager mappings | `StockCount.ViewOwn` (own sessions) or `StockCount.ViewAll` | **IMPLEMENTED & TESTED** (rerun 2026-10-17) | `MobileStockTakePostgresHttpTests` |
| `GET /api/mobile/v1/stock-take/sessions/{sessionId}/operations/{clientOperationId}` | Recover one durable accepted/rejected operation receipt | Owner / Sales Manager mappings | `StockCount.ViewOwn` (own sessions) or `StockCount.ViewAll` | **IMPLEMENTED & TESTED** (rerun 2026-10-17) | `MobileStockTakePostgresHttpTests` |

The API uses durable receipts and revision checks, but **full offline synchronization is not ready**: client queue/restart behavior is outside this backend, and catalogue version pinning, lease/takeover, undo/compensation, unknown-barcode exception handling, and authoritative movement reconciliation remain absent. Count acceptance does not post inventory.

### 9. Stock Take Asynchronous Submission

| Method / full route | Purpose | Allowed roles | Required permission | Status | Tests |
|---|---|---|---|---|---|
| `POST/GET /api/mobile/v1/stock-take/sessions/{sessionId}/submissions[/{jobId}]` | Durable asynchronous submit (202) and job status/recovery | Mobile Owner/Sales Manager + `StockCount.Submit` (status: `StockCount.ViewOwn`/`ViewAll`); server-derived Business/Outlet | **IMPLEMENTED (API-05C) — PARTIALLY PASSED** (live RabbitMQ not exercised) | 15 PostgreSQL HTTP/integration tests passed |

### 9A. Stock Take History, Lifecycle, Reconciliation Evidence and Results (API-05D)

| Method / full route | Purpose | Allowed roles | Required permission | Status | Tests |
|---|---|---|---|---|---|
| `GET /api/mobile/v1/stock-take/sessions/history` | Paged history with ERP stage, latest submission job and ERP reconciliation flag; filters Outlet, status, submission status, search, UTC created range | Owner / Sales Manager mappings; authorized Outlets only | `StockCount.ViewAll` (all authorized-Outlet sessions) or `StockCount.ViewOwn` (own created/submitted sessions) | **IMPLEMENTED & TESTED** | `MobileStockTakeReviewPostgresHttpTests` |
| `GET /api/mobile/v1/stock-take/sessions/{sessionId}/lifecycle` | Separate submission, reconciliation-evidence, ERP approval and inventory-adjustment facts; results availability | same | same | **IMPLEMENTED & TESTED** | `MobileStockTakeReviewPostgresHttpTests` |
| `GET /api/mobile/v1/stock-take/sessions/{sessionId}/submissions` | Paged durable submission-job history, newest first | same | same | **IMPLEMENTED & TESTED** | `MobileStockTakeReviewPostgresHttpTests` |
| `GET /api/mobile/v1/stock-take/sessions/{sessionId}/results` | Approved final results (counted, baseline, difference) only when ERP approval and deterministic movement evidence support them; otherwise `NotAvailable`/`Withheld` with reason codes | same | same | **IMPLEMENTED & TESTED**; results become `Available` only for ERP-approved, conflict-free evidence | `MobileStockTakeReviewPostgresHttpTests`, `MobileStockTakeReconciliationEvaluatorTests` |

API-05D is read-only and adds no migration. It does not resolve the persisted ERP `MovementReconciliationStatus`, approve counts, generate adjustment drafts or post inventory. See [40_API05D_RECONCILIATION_HISTORY_ACCEPTANCE.md](./40_API05D_RECONCILIATION_HISTORY_ACCEPTANCE.md).

Existing ERP submit/approve/reject/reopen and adjustment-draft routes are listed under ERP Stock Take APIs below. They are not Mobile submission APIs. Existing ERP adjustment generation fails closed when Mobile movement reconciliation is unresolved.

### 10. Notifications

| Method / full route | Purpose | Allowed roles | Required permission | Status | Tests |
|---|---|---|---|---|---|
| No suitable Mobile inbox route found | Inbox, unread count, read/unread changes, categories, recipient rules and durable delivery | Not established | Not established | **NOT IMPLEMENTED**; notification event sources, recipient policy, retention and retry/dead-letter decisions remain open | No Mobile notification API tests |

### 11. Sales Forecasting

| Method / full route | Purpose | Allowed roles | Required permission | Status | Tests |
|---|---|---|---|---|---|
| No suitable Mobile route found | Forecast summary, historical inputs, 1/2/3-month horizons, Product/category/Variant demand, uncertainty, provenance and advisory recommendations | Not established | Not established | **DEFERRED / BLOCKED** pending model/data contract, role policy and validation/backtesting | No Mobile forecasting API tests |

### 12. Profile and Settings

There is no dedicated Mobile preference or settings route for notification preferences, scanner settings, offline catalogue/sync status, or app/session metadata. `GET /api/Auth/me` and general ERP Settings routes exist but do not provide the accepted Mobile contract.

## Existing general ERP APIs (not dedicated Mobile routes)

These are listed to distinguish source-existing ERP behavior from Mobile integration readiness. General ERP grants may be broader, response DTOs may expose fields not approved for Mobile, and role/Outlet restrictions have not been verified for every endpoint in the Mobile context.

### Authentication and context candidates

| Method / full route | Purpose | ERP access / permission | Mobile status | Tests |
|---|---|---|---|---|
| `POST /api/Auth/login` | ERP login and token response | Login/identity validation; no Mobile-specific grant | **PARTIALLY IMPLEMENTED**; native-client/PKCE and mobile token contract not accepted | ERP authentication tests exist; no Mobile session acceptance |
| `POST /api/Auth/refresh-token` | Refresh ERP authentication | Refresh credential/session validation | **PARTIALLY IMPLEMENTED**; native lifecycle and restoration contract not accepted | ERP refresh tests; no Mobile lifecycle acceptance |
| `POST /api/Auth/logout` | Revoke current ERP session/token | Authenticated session | **PARTIALLY IMPLEMENTED** as ERP behavior only | No Mobile-specific acceptance |
| `POST /api/Auth/logout-all` | Revoke all ERP sessions | Authenticated identity; endpoint-specific checks | **PARTIALLY IMPLEMENTED** as ERP behavior only | No Mobile-specific acceptance |
| `GET /api/Auth/me` | Current ERP profile | Authenticated identity | **PARTIALLY IMPLEMENTED**; no stable Mobile profile/permissions DTO | No Mobile DTO/field-allowlist acceptance |
| `POST /api/Auth/business-context` | ERP Business context operation | SuperAdmin-only per current route contract | **BLOCKED** for ordinary Mobile context selection; not an authorized Outlet-list substitute | No Mobile context-selection test |
| `POST /api/Auth/switch-role` | ERP role-switch operation | ERP role-switch checks | **DEFERRED / DENIED in Mobile scope**; Mobile endpoints reject role-switched sessions | Mobile endpoint negative role-switch assertions exist |
| `POST /api/Auth/return-owner` | Return to ERP owner role context | ERP role-switch checks | **DEFERRED / DENIED in Mobile scope** | Mobile endpoint negative role-switch assertions exist |
| `POST /api/Auth/register` | ERP account registration | Invitation/registration validation | **NOT A MOBILE SIGN-IN CONTRACT** | No Mobile sign-up acceptance |
| `POST /api/Auth/complete-invitation` | Complete ERP invitation | Invitation token and account validation | **NOT A MOBILE SIGN-IN CONTRACT** | No Mobile invitation acceptance |

### Sales report candidates

The five `/api/reports/sales/*` ERP routes are individually inventoried above. The controller uses `reports.sales` and resolves Outlet filters through the ERP access service, but no complete Mobile salesperson-self-only, field allowlist, or financial acceptance is established for this route family.

### Product ERP candidates

| Method / full route | Purpose | ERP access / permission | Mobile status | Tests |
|---|---|---|---|---|
| `GET /api/Products` | ERP Product list | `products.view`; cost fields have separate visibility handling | **BLOCKED for Mobile reuse**; response includes ERP fields and broader scope | No equivalent Mobile authorization/field test for this route |
| `POST /api/Products/search` | ERP Product search | `products.view` plus command/query checks | **BLOCKED for Mobile reuse** | No Mobile-specific test |
| `GET /api/Products/{id}` | ERP Product detail | `products.view`; cost redaction applies | **BLOCKED for Mobile reuse** | No Mobile-specific test |
| `GET /api/Products/sku/{sku}` | ERP SKU lookup | `products.view` | **BLOCKED for Mobile reuse** | No Mobile-specific test |
| `GET /api/Products/barcode/{barcode}` | ERP barcode lookup | `products.view` | **BLOCKED for Mobile reuse**; Mobile exact identity contract is the versioned catalogue route | No Mobile-specific test for ERP route |
| `GET /api/Products/category/{categoryId}` | ERP category Product list | `products.view` | **BLOCKED for Mobile reuse** | No Mobile-specific test |
| `GET /api/Products/variants/search` | ERP Variant search | `products.view`; cost visibility may vary | **BLOCKED for Mobile reuse** | No Mobile-specific test |
| `POST /api/Products` | Create ERP Product | `products.view` controller policy plus mutation authorization/validation | **NOT A MOBILE API** | No Mobile mutation contract/test |
| `PUT /api/Products/{id}` | Update ERP Product | ERP mutation authorization/validation | **NOT A MOBILE API** | No Mobile mutation contract/test |
| `DELETE /api/Products/{id}` | Delete ERP Product | ERP mutation authorization/validation | **NOT A MOBILE API** | No Mobile mutation contract/test |
| `POST /api/Products/generate-sku` | Generate ERP SKU | ERP policy/validation | **NOT A MOBILE API** | No Mobile contract/test |

### Inventory ERP candidates

| Method / full route | Purpose | ERP access / permission | Mobile status | Tests |
|---|---|---|---|---|
| `GET /api/Inventory` | ERP inventory list | `inventory.view` | **BLOCKED for Mobile reuse**; Mobile endpoint-specific role/Outlet and field controls are not established here | No Mobile-specific test for this route |
| `GET /api/Inventory/{id}` | ERP inventory detail | `inventory.view` | **BLOCKED for Mobile reuse** | No Mobile-specific test |
| `GET /api/Inventory/outlet/{outletId}` | ERP Outlet inventory | `inventory.view` plus ERP Outlet access | **BLOCKED for Mobile reuse**; use Mobile Outlet stock route | No Mobile-specific test |
| `GET /api/Inventory/warehouse/{warehouseId}` | ERP Warehouse inventory | ERP inventory/location checks | **BLOCKED for Mobile reuse** | No Mobile-specific test |
| `GET /api/Inventory/low-stock` | ERP low-stock report | ERP inventory/alert checks | **BLOCKED for Mobile reuse**; use Mobile permission-gated low-stock filter | No Mobile-specific test |
| `GET /api/Inventory/out-of-stock` | ERP out-of-stock report | ERP inventory checks | **BLOCKED for Mobile reuse** | No Mobile-specific test |
| `GET /api/Inventory/expiring-soon` | ERP expiry report | ERP inventory checks | **BLOCKED for Mobile reuse** | No Mobile-specific test |
| `GET /api/Inventory/variant/{variantId}/total-stock` | ERP aggregate stock for a Variant | ERP inventory checks; aggregate scope requires review | **BLOCKED for Mobile reuse** | No Mobile-specific Mobile-scope test |
| `GET /api/Inventory/valuation` | ERP inventory valuation | ERP inventory and cost visibility controls | **BLOCKED for Mobile reuse**; valuation/cost is not approved Mobile data | No Mobile-specific test |
| `GET /api/Inventory/summary` | ERP inventory summary | ERP inventory checks | **BLOCKED for Mobile reuse** | No Mobile-specific test |
| `POST /api/Inventory/search` | ERP inventory search | ERP inventory checks and query validation | **BLOCKED for Mobile reuse** | No Mobile-specific test |
| `GET /api/Inventory/variant-attribute-filters` | ERP inventory attribute filters | ERP inventory checks | **BLOCKED for Mobile reuse** | No Mobile-specific test |
| `PUT /api/Inventory/{id}/threshold` | Update ERP inventory threshold | ERP mutation permission and validation | **NOT A MOBILE API** | No Mobile mutation contract/test |

### Stock Take ERP workflow candidates

| Method / full route | Purpose | ERP access / permission | Mobile status | Tests |
|---|---|---|---|---|
| `POST /api/stock-counts/search` | Search ERP Stock Count sessions | StockCount ERP policies | **BLOCKED for Mobile reuse**; use Mobile session list | No Mobile-specific scope test for ERP route |
| `GET /api/stock-counts/{id}` | ERP Stock Count detail | StockCount ERP policies | **BLOCKED for Mobile reuse** | No Mobile-specific test |
| `POST /api/stock-counts` | Create ERP Stock Count | StockCount ERP create permission | **BLOCKED for Mobile reuse**; use Mobile create route | No Mobile-specific test |
| `GET /api/stock-counts/{id}/download` | Download ERP count sheet | StockCount ERP policies | **NOT A MOBILE CONTRACT** | No Mobile contract |
| `GET /api/stock-counts/{id}/print` | Print ERP count sheet | StockCount ERP policies | **NOT A MOBILE CONTRACT** | No Mobile contract |
| `POST /api/stock-counts/{id}/upload` | Upload ERP count sheet | StockCount upload permission | **NOT A MOBILE CONTRACT**; Mobile operations use durable operation receipts | ERP concurrency regression exists; not Mobile API acceptance |
| `POST /api/stock-counts/{id}/submit` | Submit ERP Stock Count | Existing ERP lifecycle permission | Unchanged ERP route; the Mobile worker invokes the same `SubmitStockCountCommand` (API-05C) with the persisted actor | Covered by the API-05C worker PostgreSQL tests |
| `POST /api/stock-counts/{id}/approve` | ERP approve | Existing ERP reviewer permission | **NOT A MOBILE API**; Mobile does not approve/post | ERP workflow tests only |
| `POST /api/stock-counts/{id}/reject` | ERP reject | Existing ERP reviewer permission | **NOT A MOBILE API** | ERP workflow tests only |
| `POST /api/stock-counts/{id}/reopen` | ERP reopen | Existing ERP lifecycle permission | **NOT A MOBILE API** | ERP workflow tests only |
| `POST /api/stock-counts/{id}/generate-adjustment-draft` | ERP adjustment draft from count | Existing ERP adjustment permission; fails closed for unresolved Mobile movement reconciliation | **NOT API-05C/D**; do not call as Mobile submission | PostgreSQL adjustment-generation concurrency acceptance exists |

### Settings ERP candidates

The following general ERP configuration routes are not user preference/session APIs. Route-level ERP authorization varies; none has an accepted Mobile preference DTO or Mobile authorization suite.

| Method / full route | Purpose | ERP access / permission | Mobile status | Tests |
|---|---|---|---|---|
| `GET /api/Settings` | Read ERP system configuration | ERP settings access | **PARTIALLY IMPLEMENTED** as general settings only; not Mobile profile/settings | No Mobile settings acceptance |
| `GET /api/Settings/company` | Read company settings | ERP settings access | **NOT A MOBILE PROFILE CONTRACT** | No Mobile-specific test |
| `PUT /api/Settings/company` | Update company settings | `settings.edit` | **NOT A MOBILE API** | No Mobile mutation contract |
| `GET /api/Settings/currency` | Read currency configuration | ERP settings access | **NOT A MOBILE SETTINGS CONTRACT** | No Mobile-specific test |
| `PUT /api/Settings/currency` | Update currency configuration | `settings.edit` | **NOT A MOBILE API** | No Mobile mutation contract |
| `GET /api/Settings/tax` | Read tax configuration | ERP settings access | **NOT A MOBILE SETTINGS CONTRACT** | No Mobile-specific test |
| `PUT /api/Settings/tax` | Update tax configuration | `settings.edit` | **NOT A MOBILE API** | No Mobile mutation contract |
| `GET /api/Settings/receipt` | Read receipt configuration | ERP settings access | **NOT A MOBILE SETTINGS CONTRACT** | No Mobile-specific test |
| `PUT /api/Settings/receipt` | Update receipt configuration | `settings.edit` | **NOT A MOBILE API** | No Mobile mutation contract |
| `GET /api/Settings/invoice-number` | Read ERP invoice-number configuration | ERP settings access | **NOT A MOBILE SETTINGS CONTRACT** | No Mobile-specific test |
| `PUT /api/Settings/invoice-number` | Update ERP invoice-number configuration | `settings.edit` | **NOT A MOBILE API** | No Mobile mutation contract |
| `GET /api/Settings/inventory` | Read ERP inventory settings | `settings.view`, `inventory.view`, `stock_adjustments.view`, or `stock_adjustments.create` | **NOT A MOBILE INVENTORY API** | No Mobile-specific test |
| `PUT /api/Settings/inventory` | Update ERP inventory settings | `settings.edit` | **NOT A MOBILE API** | No Mobile mutation contract |

## API-05B and API-05C/API-05D status

- API-05A online session list/detail/lines/create routes are implemented and covered by the recorded PostgreSQL HTTP acceptance.
- API-05B has three source routes for operation batch, revision history, and receipt recovery. It was revalidated on 2026-10-16 against the final source on disposable PostgreSQL (4 focused tests passed) and again inside the 40-test and 51-test API-05C regression runs. Readiness is **IMPLEMENTED — TESTED**.
- API-05C adds `POST/GET .../submissions[/{jobId}]`, the `stock_count_submission_jobs` table (migration `20261016124940_Api05CMobileStockTakeSubmissionJobs`), the transactional-outbox message, a RabbitMQ consumer, a PostgreSQL-driven retry/recovery sweeper and the worker that invokes the existing ERP `SubmitStockCountCommand`. 15 new PostgreSQL tests and a Release build (0 errors) passed. Readiness: **PARTIALLY PASSED** — live RabbitMQ was not exercised. See [39_API05C_ASYNC_SUBMISSION_ACCEPTANCE.md](./39_API05C_ASYNC_SUBMISSION_ACCEPTANCE.md).
- API-05D (2026-10-17) adds read-only `GET .../sessions/history`, `.../{id}/lifecycle`, `.../{id}/submissions` and `.../{id}/results`, plus `StockCount.ViewOwn` enforcement (own created/submitted sessions) on all existing Mobile Stock Take read routes. No migration. Movement evidence is evaluated deterministically from StockLedger/Inventory and returns `Blocked`/`Unresolved` whenever it cannot prove consistency; the persisted ERP reconciliation flag is never changed. 15 PostgreSQL HTTP tests + 32 evaluator unit tests passed; 96-test focused regression and Release build passed. Readiness: **PARTIALLY PASSED** (live RabbitMQ not exercised; ERP reconciliation resolution/lease/merge policies still undefined). See [40_API05D_RECONCILIATION_HISTORY_ACCEPTANCE.md](./40_API05D_RECONCILIATION_HISTORY_ACCEPTANCE.md).
- Accepted count operations and `ErpSubmitted` jobs still do not equal ERP approval, inventory posting, or reconciled stock.
- **Stock Count approval → asynchronous adjustment (2026-10-17):** behind `FeatureFlags:StockCountAsyncAdjustmentEnabled` (default off) the ERP `POST /api/stock-counts/{id}/approve` atomically persists approval, a `stock_count_adjustment_jobs` row and an outbox message and returns `Approved/Pending`; a worker reuses the existing adjustment/posting logic and completes the count. Mobile has no approve route; the API-05D lifecycle `inventoryAdjustment.job` exposes the job state read-only. See [41_STOCK_COUNT_ASYNC_ADJUSTMENT_ACCEPTANCE.md](./41_STOCK_COUNT_ASYNC_ADJUSTMENT_ACCEPTANCE.md).

## Readiness, security and Product Owner dependencies

### Safe for controlled integration (contract-limited)

1. `GET /api/mobile/v1/insights/sales/activity` — invoice counts only; label and display as UTC activity, never revenue or demand.
2. Six `/api/mobile/v1/catalogue/*` routes — online read-only use by Owner/Sales Manager after confirming deployed single-Business/catalogue provenance and barcode uniqueness. Offline catalogue sync is not ready.
3. Two `/api/mobile/v1/inventory/*` routes — online Outlet stock only, restricted to Owner/Sales Manager and server-authorized Outlets; low-stock fields/filter additionally require `low_stock_alerts.view`.
4. Four API-05A session routes, three API-05B routes, the API-05C submission routes and four API-05D read routes — online Stock Take capture, submission status and ERP review visibility only, preserving blind-count fields. This is not a completed Stock Take: ERP approval, reconciliation resolution and posting remain ERP-only.

### Security/deployment checks still required

- Verify the real deployment passes the single-Business readiness guard and that legacy global Product/Category catalogue data has acceptable provenance. The guard is fail-closed for tested conflicting/missing Business states; it does not prove legacy catalogue ownership.
- Keep Owner/Manager-only access for Mobile Catalogue, Inventory and Stock Take enforced server-side. Flutter menu hiding is not an authorization control.
- Do not route Flutter through generic ERP Products, Inventory, Reports, or StockCounts endpoints absent endpoint-specific Mobile scope, role, field, and negative authorization tests.
- Do not expose SuperAdmin/global context, client-supplied Business/Outlet/role, role-switched sessions, cost/profit/margin, or unapproved inventory fields.
- API-05B routes were revalidated on 2026-10-17 as part of the API-05D regression run.

### Decisions/dependencies still open

- **Financial KPIs:** VAT/refund accounting reconciliation and sale-level discount allocation remain deferred. Net Sales and derived average invoice value are not approved.
- **Auth/profile:** native-client token lifecycle, stable profile/permission/capability contract, authorized Outlet selection UX, and session/cache restoration.
- **Catalogue/inventory offline behavior:** catalogue revision/snapshot/delta/tombstones and stock freshness semantics.
- **Stock Take:** atomic baseline/movement cursor, lease/takeover, undo, unknown barcode workflow, an approved ERP movement-reconciliation resolution policy (API-05D only reports deterministic evidence), live RabbitMQ validation of the API-05C consumer topology.
- **Notifications:** shared event sources, recipient rules, preferences, retention, durable delivery, retries and audit.
- **Forecasting:** allowed roles, time horizon semantics, source quality, confidence/calibration, model/version provenance and advisory-only response contract.
- **Settings:** notification/scanner preferences and offline/sync/session metadata ownership and persistence.

## Dependency-ordered backend backlog

1. Revalidate the latest API-05B source changes against the dedicated disposable PostgreSQL fixture and Release build; update acceptance status only from actual results.
2. Complete Dashboard/Sales Insights only after financial definitions and role/Outlet scopes are approved; keep financial measures withheld until reconciliation.
3. Define catalogue revision/offline cache contract and validate production deployment catalogue provenance and barcode collisions.
4. Define inventory freshness and synchronization contract for authorized Outlets.
5. Resolve Stock Take baseline, movement, lease and unknown-barcode policies and validate the API-05C consumer against a live approved RabbitMQ topology; API-05D read-only reconciliation evidence/result history is implemented, while ERP retains approval, reconciliation resolution and posting.
6. Approve notification event/recipient/durability rules and implement the shared inbox.
7. Approve forecast model and role contract; implement advisory-only forecast after backtesting.
8. Define remaining mobile profile, preference, scanner and session settings contracts.

## Flutter handoff

- Use only versioned `/api/mobile/v1/` contracts for the dedicated Mobile routes in this report; do not infer readiness from similarly named ERP routes.
- Mobile Catalogue, Inventory and Stock Take navigation is allowed only for Business Owner and Sales Manager mappings. Backend authorization remains authoritative.
- Treat `dashboard/sales` financial fields as blocked. The sales activity response contains only UTC invoice counts.
- Catalogue/Inventory are online-only. Do not claim offline freshness, versioning, or sync guarantees.
- Treat a Stock Take operation receipt as a durable count-capture decision only. It is not submission, review, reconciliation, approval, or inventory posting.
- API-05B and API-05C/05D client integration is contract-limited (counts, 202/status polling, history/lifecycle/results reads; never present accepted/`ErpSubmitted` as approved, reconciled or posted; show results only when `results.availability` is `Available`; treat `Withheld`/`NotAvailable` as "no variance to show").
- Authentication/profile and notification/forecast/settings integrations require additional contracts; do not bind app behavior to ERP DTOs by assumption.

## Source and acceptance evidence

- Dedicated controllers: `MobileSalesDashboardController`, `MobileProductCatalogueController`, `MobileInventoryController`, and `MobileStockTakeController`.
- ERP controllers reviewed: `AuthController`, `SalesReportsController`, `ProductsController`, `InventoryController`, `StockCountsController`, and `SettingsController`.
- PostgreSQL HTTP test sources: `MobileSalesDashboardPostgresHttpTests`, `MobileProductCataloguePostgresHttpTests`, `MobileInventoryPostgresHttpTests`, `MobileStockTakePostgresHttpTests`, `MobileStockTakeSubmissionPostgresHttpTests` and `MobileStockTakeReviewPostgresHttpTests`.
- Acceptance documents: [30_MOBILE_SALES_DASHBOARD_API_ACCEPTANCE.md](./30_MOBILE_SALES_DASHBOARD_API_ACCEPTANCE.md), [34_API03_PRODUCT_CATALOGUE_ACCEPTANCE.md](./34_API03_PRODUCT_CATALOGUE_ACCEPTANCE.md), [35_API04_MOBILE_INVENTORY_READ_ACCEPTANCE.md](./35_API04_MOBILE_INVENTORY_READ_ACCEPTANCE.md), [36_API05A_STOCK_TAKE_ONLINE_SESSION_ACCEPTANCE.md](./36_API05A_STOCK_TAKE_ONLINE_SESSION_ACCEPTANCE.md), and [37_API05B_STOCK_TAKE_COUNT_SYNC_ACCEPTANCE.md](./37_API05B_STOCK_TAKE_COUNT_SYNC_ACCEPTANCE.md).
- Master backlog: [33_MOBILE_BACKEND_API_COVERAGE_MATRIX.md](./33_MOBILE_BACKEND_API_COVERAGE_MATRIX.md).
