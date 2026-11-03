# API-04 Mobile Inventory Online Read API Acceptance

**Scope:** Authorized Outlet list, stock availability and low-stock reads  
**Decision:** **READY for controlled online integration** for the routes below  
**Architecture:** Single-Business / Multi-Outlet; existing ERP Inventory and authorization behavior preserved

## Implemented routes

| Method and route | Contract |
|---|---|
| `GET /api/mobile/v1/inventory/outlets` | Returns only Outlets authorized for the authenticated principal and selected Business |
| `GET /api/mobile/v1/inventory/outlets/{outletId}/stock` | Bounded paged inventory for one server-authorized Outlet; supports `search`, `productId`, `variantId`, `lowStockOnly`, `page`, and `pageSize` |

The versioned controller returns typed response DTOs and OpenAPI response metadata. The endpoint is read-only, cancellation-aware and does not accept client Business, user, role, or location-type authority. Inventory search is stable by Product name, Variant ID, then Inventory ID; page size is capped at 100. The API returns only rows with `LocationType = outlet` and the requested, already-authorized Outlet ID.

### Field and filter behavior

- Product and Variant IDs, Product Code, Variant SKU/barcode, Product/Variant names, persisted JSONB attributes, Outlet ID/name, on-hand quantity, optional reorder level and low-stock flag are returned.
- Price, retail value, cost, valuation, margin, warehouse and customer data are excluded.
- `search` is a case-insensitive substring query over Product name, Product Code, Variant SKU and Variant barcode (up to 100 characters).
- Optional `productId` / `variantId` must be positive IDs. `page` is 1-based (maximum 100,000); `pageSize` is 1–100 (default 50).
- `lowStockOnly=true` uses the existing ERP threshold condition `Quantity <= LowStockThreshold`, including zero-quantity rows. This filter requires `low_stock_alerts.view`.
- When the caller has `inventory.view` but not `low_stock_alerts.view`, stock rows are readable, but `reorderLevel` and `isLowStock` are serialized as null. A low-stock-only query returns HTTP 403.
- Invalid filters return HTTP 400 with `INVALID_INVENTORY_FILTER`. A disallowed role, missing permission, unassigned/foreign Outlet, unsupported global context or failed deployment guard returns HTTP 403. The implementation does not disclose whether a denied Outlet exists.

## Mobile authorization contract

- Only authenticated Business Owners and Sales Managers can access the Mobile Inventory module.
- Accepted Sales Manager role names are `SalesManager`, `OutletManager` and the existing authorized `Manager` mapping. These are server-derived roles; a request parameter/header cannot elevate a role.
- Every route requires `inventory.view`; the low-stock filter additionally requires `low_stock_alerts.view`.
- Salesperson, Cashier, all other roles, role-switched sessions, SuperAdmin/global contexts, and absent Business context are denied by the Mobile API. This restriction is scoped to Mobile routes and does not revoke ERP/web POS permissions.
- `IMobileSingleBusinessReadinessGuard` is checked before resolving authorized Outlets or querying stock.
- `IUserOutletAccessService.GetAuthorizedOutletsForBusinessAsync` provides the effective Outlet set. Business Owner can read their authorized Business Outlets; Sales Manager receives only assigned/authorized Outlets. A requested Outlet must occur in this server-resolved set before the inventory store is called.
- Warehouse inventory is intentionally not exposed in this slice.

## OpenAPI / Flutter handoff

The API is available under the generated Swagger document in Development; all routes are versioned under `/api/mobile/v1/`.

Example request:

```http
GET /api/mobile/v1/inventory/outlets/5101/stock?search=jacket&page=1&pageSize=20
Authorization: Bearer <access-token>
```

Example successful response:

```json
{
  "success": true,
  "message": "Operation successful",
  "data": {
    "items": [
      {
        "inventoryId": 50102,
        "productId": 8102,
        "variantId": 9104,
        "productName": "Mobile Jacket",
        "productCode": "MOBILE-JACKET",
        "variantName": "Red / Medium",
        "sku": "JACKET-RED-M",
        "barcode": "0000009876",
        "attributes": {
          "Colour": "Red",
          "Size": "M"
        },
        "outletId": 5101,
        "outletName": "Dashboard outlet one",
        "quantity": 2,
        "reorderLevel": 5,
        "isLowStock": true
      }
    ],
    "page": 1,
    "pageSize": 20,
    "totalCount": 1
  },
  "errors": null
}
```

Flutter navigation and route contracts must expose Inventory only to Business Owner and Sales Manager roles and must still handle backend 403 responses. The API is authoritative; menu hiding is not a security boundary. On Outlet selection, display only results from `/inventory/outlets`; never construct an Outlet list from client context alone. Do not infer freshness or offline availability from this online API.

## Implementation

- Added `MobileInventoryController`, mobile-only typed DTO/read models, `IMobileInventoryReadStore`, and a PostgreSQL projection.
- Reused the existing Business readiness guard and authorized Outlet access service; no new permissions, migration, inventory master, write path or ERP route behavior was added.
- Added HTTP tests using the existing dedicated disposable PostgreSQL fixture. Test inventory seed rows are only in its uniquely owned test database.
- Added the API-04 contract/readiness and Product Owner mobile role rules to [33_MOBILE_BACKEND_API_COVERAGE_MATRIX.md](./33_MOBILE_BACKEND_API_COVERAGE_MATRIX.md). Related handoff requirements are also noted in the [M4 Catalogue UI acceptance](./25_M4_PRODUCT_CATALOGUE_UI_ACCEPTANCE.md), [M8 Stock Take UI acceptance](./29_M8_STOCK_TAKE_UI_ACCEPTANCE.md), and [API-03 Catalogue acceptance](./34_API03_PRODUCT_CATALOGUE_ACCEPTANCE.md). No Flutter files were modified.

## Verification

| Check | Result |
|---|---|
| Mobile Inventory + Catalogue + Sales Dashboard PostgreSQL HTTP acceptance filter | **Passed: 3 tests, 0 failures** |
| Mobile tests + A2 Inventory authorization, Product variant search and Product image regressions | **Passed: 47 tests, 0 failures** |
| Integration test project Release build | **Passed: 0 errors** |
| Full Release solution build | **Passed: 0 errors; 15 warnings, including existing ImageSharp advisories** |
| Scoped whitespace check | **Passed: `git diff --check` for tracked modifications; Markdown hard-break formatting retained in new docs** |

The new PostgreSQL HTTP acceptance test covers Owner access to the full authorized Outlet set, SalesManager access limited to assigned Outlets, authorized and unauthorized stock reads, paging, low-stock filtering, the extra low-stock permission, nulling low-stock details when that permission is absent, DTO field exclusions, Salesperson/Cashier denial, missing inventory permission, role-switch denial and mismatch Business context. API-03 role alias and Salesperson denial tests were expanded as well.

The existing A2 Inventory scope tests are separate; passing them verifies their existing ERP behavior remains intact. No persistent Development or Production database was accessed or changed. The Release build may report existing ImageSharp NU1902/NU1903 package advisories; dependencies are unchanged.

## Remaining scope and blockers

- This API provides **online Outlet stock availability and low-stock filtering only**. Warehouse stock, global inventory IDs, low-stock summary counts, valuation, inventory sync freshness, offline caching and catalogue sync revisions are not part of API-04.
- The deployment guard verifies one active Business and that all Outlets belong to it, but cannot prove provenance of the global Product/Category catalogue. Follow the deployment/data-profile condition in [API-03 acceptance](./34_API03_PRODUCT_CATALOGUE_ACCEPTANCE.md).
- This read path does not create Stock Take sessions, disclose a count baseline, accept count operations, or change stock. Future Mobile Stock Take endpoints must implement the same Owner/Sales Manager role boundary and Outlet scope, plus the approved offline/idempotent workflow. Existing ERP review/approval/posting remains authoritative.
- No Flutter or Angular code, existing ERP permission catalog, migrations, accounting behavior, persistent database, staging or commits were changed.
- API-04 is complete for the two routes above. No later API slice was started.
