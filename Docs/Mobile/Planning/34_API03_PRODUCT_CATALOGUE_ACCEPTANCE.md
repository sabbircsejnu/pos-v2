# API-03 Product Catalogue Online Read API Acceptance

**Scope:** Read-only mobile catalogue API for online Flutter integration  
**Decision:** **READY for controlled online integration**, conditional on a validated single-Business deployment and catalogue dataset  
**Out of scope:** Offline catalogue snapshot/delta synchronization, Product/Category ownership changes, inventory, pricing approval, and barcode normalization

## Implemented routes

| Method and route | Contract |
|---|---|
| `GET /api/mobile/v1/catalogue/categories` | Paged active category IDs, names and optional parent IDs |
| `GET /api/mobile/v1/catalogue/products` | Paged product search with category, status and exact JSONB attribute filters |
| `GET /api/mobile/v1/catalogue/products/{productId}` | Product details without variant expansion |
| `GET /api/mobile/v1/catalogue/products/{productId}/variants` | Bounded, paged variants for a product |
| `GET /api/mobile/v1/catalogue/products/{productId}/variants/{variantId}` | A variant only when it belongs to the specified product |
| `GET /api/mobile/v1/catalogue/barcodes/{barcode}` | Exact Product-level or Variant-level barcode resolution |

All actions have typed `ProducesResponseType` metadata. The existing Swagger/OpenAPI generation exposes these routes and DTO schemas. Every route accepts a cancellation token and is read-only.

### Search and pagination

`GET /products` accepts:

- `search` (maximum 100 characters), case-insensitive substring matching on name, description, Main Product Code, Product SKU, Product barcode, Variant SKU and Variant barcode.
- `categoryId` (positive ID).
- `status` (`Active`, `Inactive` or `Draft`; defaults to `Active`).
- Optional `attributeName` and `attributeValue` together (maximum 100 and 200 characters). These match one exact JSONB key/string-value pair on any product variant.
- `page` (starts at 1, maximum 100,000) and `pageSize` (1–100; defaults to 50).

Categories are ordered by display order, name, then ID. Products are ordered by name then ID. Variants are ordered by Variant ID. Responses include `items`, `page`, `pageSize` and `totalCount`; this is online offset pagination, not a stable offline snapshot or delta cursor.

Invalid filter combinations, status values, or bounds return HTTP 400 with stable messages such as `INVALID_CATALOGUE_FILTER` or `INVALID_PAGINATION`. Missing product/variant/barcode returns HTTP 404 with a stable error code. Exact barcode collisions return HTTP 409 `AMBIGUOUS_BARCODE`.

## Authorization and single-Business guard

- The authenticated principal must have the server-issued `products.view` permission and an effective role of `BusinessOwner` or Sales Manager. Sales Manager accepts the current backend role mappings `SalesManager`, `OutletManager` and `Manager`.
- Missing permission, Cashier/Salesperson/unsupported roles, role-switched sessions, SuperAdmin/global context, and missing Business context are denied. This is a mobile API role allowlist; existing ERP/web POS permission assignments are unchanged.
- Business identity comes only from `IRoleSwitchContext.EffectiveBusinessId`. No Business, Outlet, user or role filter is accepted as authority from the client.
- `IMobileSingleBusinessReadinessGuard` runs before each database read. It fails closed unless exactly one active Business record matches the authenticated context and every Outlet is owned by that Business.
- Product and Category still have no Business ownership columns; the endpoint deliberately adds no ownership migration or cross-tenant infrastructure. The runtime guard cannot establish the provenance of legacy global catalogue rows. Operations must verify that the target instance contains only its intended single-Business catalogue before connecting Flutter.

## Data projection and identity semantics

| Response field | Persisted source |
|---|---|
| `id` | `Product.Id` (`long`) |
| `mainProductCode` | `Product.ProductCode` |
| `sku` | `Product.Sku` |
| `productBarcode` | `Product.Barcode` |
| `categoryId`, `categoryName` | `Product.CategoryId` and `Category.Name` |
| `status`, `hasVariants`, `variantCount` | `Product.Status`, `Product.HasVariants`, and related `ProductVariant` count |
| Variant `id`, `productId`, `name`, `sku`, `barcode` | `ProductVariant.Id`, `ProductVariant.ProductId`, `Name`, `Sku`, `Barcode` |
| Variant `attributes` | The stored `ProductVariant.Attributes` JSONB value, returned as JSON |

Product and Variant IDs, Main Product Code, Product SKU, Variant SKU, and raw barcode strings remain separate. Barcodes are exact, case-sensitive database equality lookups; no trimming, padding, normalization or cross-table winner selection occurs. A unique Product-level barcode returns `matchType: "product"` with `variant: null`; it does not identify a sellable Variant. A unique Variant barcode returns `matchType: "variant"`. Any multiple match, including a Product/Variant cross-table collision, fails with 409.

The current domain has a Product status but no independently persisted Variant status. The API does not invent one. It does not return base/sale prices, cost, tax rate, stock, inventory, margin, profit or unapproved financial KPIs. Existing ERP Product routes and DTO behavior are unchanged.

JSONB attributes and normalized Variation/VariationOption relationships have no verified equivalence invariant. The API exposes only the JSONB attributes as persisted and does not merge or reconcile the relational representation. Attribute filtering currently supports string values only; there is no canonical size/colour vocabulary or attribute-facet API.

## Example contracts

Product page:

```json
{
  "success": true,
  "message": "Operation successful",
  "data": {
    "items": [
      {
        "id": 8102,
        "name": "Mobile Jacket",
        "description": "Catalogue item",
        "mainProductCode": "MOBILE-JACKET",
        "sku": "JACKET-PRODUCT-SKU",
        "productBarcode": "0000123456",
        "categoryId": 7101,
        "categoryName": "Clothing",
        "status": "Active",
        "hasVariants": true,
        "variantCount": 2
      }
    ],
    "page": 1,
    "pageSize": 50,
    "totalCount": 1
  },
  "errors": null
}
```

Exact Variant barcode:

```json
{
  "success": true,
  "message": "Operation successful",
  "data": {
    "matchType": "variant",
    "product": {
      "id": 8102,
      "name": "Mobile Jacket",
      "description": "Catalogue item",
      "mainProductCode": "MOBILE-JACKET",
      "sku": "JACKET-PRODUCT-SKU",
      "productBarcode": "0000123456",
      "categoryId": 7101,
      "categoryName": "Clothing",
      "status": "Active",
      "hasVariants": true
    },
    "variant": {
      "id": 9104,
      "productId": 8102,
      "name": "Red / Medium",
      "sku": "JACKET-RED-M",
      "barcode": "0000009876",
      "attributes": {
        "Colour": "Red",
        "Size": "M"
      }
    }
  },
  "errors": null
}
```

## Implementation

- Added a mobile-only controller and typed DTOs; ERP `ProductsController` behavior was not changed.
- Added an Application read-store abstraction/read models and a PostgreSQL-backed Infrastructure projection using `AsNoTracking`, bounded paging and allowlisted fields.
- Reused the existing `IMobileSingleBusinessReadinessGuard` without adding database migrations.
- Added OpenAPI response metadata, exact barcode ambiguity handling, and focused HTTP tests against the dedicated disposable PostgreSQL fixture.
- Updated [33_MOBILE_BACKEND_API_COVERAGE_MATRIX.md](./33_MOBILE_BACKEND_API_COVERAGE_MATRIX.md) with API-03 readiness and remaining blockers. The matrix records the binding Mobile role/navigation contract: Owner and Sales Manager only; no Salesperson or Cashier access.

## Verification

| Check | Result |
|---|---|
| `MobileProductCataloguePostgresHttpTests` against the dedicated disposable PostgreSQL 16 test server | **Passed: 1 test, 0 failures** |
| Catalogue + Dashboard + existing Variant Search + Product Images characterization regression filter | **Passed: 34 tests, 0 failures** |
| `dotnet build RetailPOS.slnx --configuration Release --no-restore -v:q` | **Passed: 0 errors** |
| Scoped whitespace check | **Passed** |

The PostgreSQL test exercises filtered search, category lookup, pagination, DTO field exclusions, Product/Variant details, parent-Variant matching, exact Product and Variant barcodes including leading zeros, cross-table ambiguity, unknown barcode, invalid status, Owner/Manager access, missing permission, unsupported Cashier role, role switch, mismatched Business, unowned Outlet and conflicting Business records. The test fixture verifies and uses only its dedicated disposable A2 PostgreSQL target. No persistent Development or Production database was accessed.

The Release build reports existing `SixLabors.ImageSharp` NU1902/NU1903 package advisories; no dependency changes were made for this slice.

## Readiness and remaining blockers

- **Online catalogue reads:** ready for controlled Flutter integration after validating the target instance's single-Business state and intended catalogue dataset. Flutter navigation/route policy must expose the module to Owner and Sales Manager (existing Manager/OutletManager mapping) only. Flutter must handle 403, 404 and 409 `AMBIGUOUS_BARCODE`, and must not treat a Product-level barcode as a Variant.
- **Offline snapshot/delta/cache:** blocked. No authoritative catalogue revision, change feed, tombstone or atomic offline activation contract exists; `UpdatedAt` is not used as a substitute.
- **Catalogue provenance:** not established for any production/deployment database. Product and Category have no Business owner fields, and the guard cannot attribute legacy catalogue rows to a Business. Verify the deployment/data profile before connecting a real instance.
- **Attribute facets and canonical filters:** not established. Returned JSONB is opaque and not reconciled with normalized Variation/VariationOption data.
- **Inventory/stock availability:** not included and remains blocked pending A2/A3 location authorization tests.
- Product ownership B1–B4, accounting/refund changes, Angular, Flutter, commits and staging remain untouched.

No subsequent API slice was started.
