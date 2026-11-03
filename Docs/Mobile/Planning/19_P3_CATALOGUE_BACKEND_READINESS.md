# P3 Catalogue Backend Readiness Assessment

**Assessment:** Read-only source, EF model, migration, test-source, and planning-document review
**Verdict:** **BLOCKED**
**Scope boundary:** No application code, database, migration, seed data, Flutter integration, or Git history was changed. No database queries or test commands were run. Deployed schema parity and live data quality remain unverified.

## Executive result

The repository confirms that `Product.ProductCode` is the existing Main Product Code, `Product.Id` and `ProductVariant.Id` are the ERP identifiers, and a variant belongs to a product through `ProductVariant.ProductId`. It does **not** implement Business ownership for Products or Categories, nor a universal Business predicate across product reads and writes. It therefore cannot currently prove that Products are Business-owned and shared among only that Business's Outlets.

Stock rows are distinct by `(VariantId, LocationId, LocationType)`, but the inventory entity has no Business key or typed location relationship. Several inventory endpoints accept direct IDs or return aggregates without resolving the caller's authorized locations. Outlet authorization fixes are visible on POS stock-bearing paths (A1), but the broader A2/A3 containment is not complete.

The ownership decision and the required data profile remain proposed/unapproved in the planning record. No `api/mobile/v1` catalogue manifest/snapshot/delta implementation or approved mobile contract was found. The current ERP catalogue must not be exposed to Flutter until ownership, access containment, legacy mapping, and PostgreSQL-backed tests pass.

## Verified remediation status

| Item | Status | Evidence-based result |
|---|---|---|
| **A1 — POS and stock-bearing variant-search location containment** | **Implemented in the reviewed paths; acceptance evidence incomplete** | POS lookup and stock-hint authorize requested outlet context. Product and barcode variant-search routes use the shared location request authorizer before forwarding location filters. Existing HTTP test source checks unauthorized POS lookup/stock-hint behavior and authorized variant-search behavior; no explicit denied-location test was found for both variant-search routes. This does not establish Business ownership of the products returned. |
| **A2 — Inventory caller-supplied location containment** | **Not implemented consistently** | Direct `/api/inventory/outlet/{id}` and `/warehouse/{id}` handlers forward caller IDs without outlet-access resolution. Low-stock, out-of-stock, expiring and valuation handlers only reject simultaneous outlet/warehouse filters; they do not authorize those supplied IDs before querying. Inventory search and attribute-filter routes do resolve explicit filters, so the surface is inconsistent. |
| **A3 — Remaining stock-read scope audit** | **Open; gaps confirmed** | `/api/inventory` and `/api/inventory/{id}` read without location authorization; total-stock-by-variant and location-summary are aggregate reads without a caller scope argument. A complete audit of all ledger/report/reference routes is still required. |
| **B1 — Ownership/identifier decision and data profile** | **Not approved / not complete** | Planning documents explicitly leave ADR-CAT-01 and identifier policies proposed. No signed Product/Security/ERP/DBA ownership decision or approved production-equivalent SELECT-only profile was found. |
| **B2 — Additive ownership schema and scoped compatibility** | **Not implemented** | `Product` has no `BusinessId`; the current EF model and migration snapshot contain no Product business ownership relation or Business-scoped product-code index. |
| **B3 — Controlled backfill and validation** | **Not implemented / blocked by B1 and B2** | No product ownership mapping, backfill migration, quarantine state, or validation gate was found. Live legacy ownership and identifier counts are unknown. |
| **B4 — Enforced ownership, uniqueness, and audited administration** | **Not implemented** | Generic product read/write paths have no consistent Business-scoped persistence predicate. Existing ProductCode uniqueness remains global, and no explicit audited cross-Business Product administration workflow was found in the reviewed product controllers. |

The A1/A2/A3 and B1–B4 meanings and sequencing follow the [Product Isolation Remediation Plan](14_PRODUCT_ISOLATION_REMEDIATION_PLAN.md). The earlier assessment is treated as a hypothesis baseline; the status above is based on the current source and migration snapshot inspected for this review.

## Product ownership, Outlet sharing, and stock isolation

### Product ownership and sharing

- `Product` has identity, category, pricing, status, and product identifiers, but no `BusinessId`, `TenantId`, or `OutletId`. `ProductVariant` belongs to its parent by required `ProductId`; it has no independent Business key. `Category` likewise has no Business key. `Variation` is explicitly described as a global reusable definition.
- `Outlet.BusinessId` is nullable. That permits a Business/Outlet relationship to exist in the ERP model, but it does not assign an owner to a Product or establish Product sharing rules.
- `ProductsController` applies permission policies such as `products.view`, `products.create`, and `products.edit`, but its list/search/direct-ID/barcode/category paths do not resolve an authorized Business and do not pass a Business scope into product queries. Nested variation and product-image endpoints also accept Product IDs without a visible Business ownership check.
- The existing source therefore **does not confirm** that Products are Business-owned and shared across that Business's Outlets. That is the recommended target in the remediation plan, not current enforced behavior. Do not infer Product ownership from stock, Outlet, sales, category, creator, or identifier values.

Evidence: [Product.cs](../../../src/RetailPOS.Domain/Entities/Product.cs#L3), [ProductVariant.cs](../../../src/RetailPOS.Domain/Entities/ProductVariant.cs#L3), [Category.cs](../../../src/RetailPOS.Domain/Entities/Category.cs#L3), [Variation.cs](../../../src/RetailPOS.Domain/Entities/Variation.cs#L7), [Outlet.cs](../../../src/RetailPOS.Domain/Entities/Outlet.cs#L3), [ProductsController.cs](../../../src/RetailPOS.API/Controllers/ProductsController.cs#L16), [ProductVariationsController.cs](../../../src/RetailPOS.API/Controllers/ProductVariationsController.cs#L11), [ProductImagesController.cs](../../../src/RetailPOS.API/Controllers/ProductImagesController.cs#L9), [ProductReadStore.cs](../../../src/RetailPOS.Infrastructure/Products/ProductReadStore.cs#L18).

### Outlet-specific inventory

`Inventory` stores `VariantId`, `LocationId`, and `LocationType`; the EF model and baseline migration uniquely index that tuple. This supports separate stock balances per location. It does not enforce that the referenced location exists as the stated type, belongs to the same Business as the Product, or is authorized for the request. Inventory has no `BusinessId` and the polymorphic location pair is not a typed Outlet/Warehouse foreign key.

`UserOutletAccess` can resolve authorized locations from the authenticated/effective Business context, and `UserOutletAccessService` exposes those checks. That is useful authorization infrastructure, but it is only protective when each route invokes it and its result actually scopes the query. The inventory controller currently does not do so consistently. Consequently, the source does **not** prove universal Outlet inventory isolation.

Evidence: [Inventory.cs](../../../src/RetailPOS.Domain/Entities/Inventory.cs#L3), [RetailPOSDbContext.cs](../../../src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L813), [InitialBaseline.cs](../../../src/RetailPOS.Infrastructure/Data/Migrations/20261004095137_InitialBaseline.cs#L4118), [UserOutletAccess.cs](../../../src/RetailPOS.Application/Identity/LocationAccess/UserOutletAccess.cs#L168), [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L58).

## A1–A3 security findings

### A1 — POS and stock-bearing variant search

`PosController.Lookup` resolves a supplied outlet through `ResolveAndAuthorizeOutletFilterAsync`; `StockHint` requires a positive outlet and calls `ResolveAndAuthorizeLocationAsync`. Both `/api/products/variants/search` and `/api/barcodes/variants/search` call `StockLocationRequestAuthorizer` and pass only the authorized location result to the query. When a location is omitted, the existing variant-search projection does not fetch stock for a location.

The existing [PosLookupHttpTests.cs](../../../tests/RetailPOS.IntegrationTests/Sales/PosLookupHttpTests.cs#L103) contains foreign/same-Business outlet denial cases for lookup and a missing-outlet denial for stock hint. [ProductVariantSearchHttpTests.cs](../../../tests/RetailPOS.IntegrationTests/Products/ProductVariantSearchHttpTests.cs#L902) covers an authorized location and stock/cost projection, but no explicit forbidden-location test was found for both variant-search routes. A1 therefore has meaningful source implementation but still needs focused negative tests and acceptance closure. It is **not** a product-scoping control: the variant list itself remains globally searchable.

### A2 — Inventory location filters and direct reads

Confirmed unscoped routes include:

- `GetByOutlet` and `GetByWarehouse`: send caller-supplied location IDs directly to query handlers.
- `GetLowStock`, `GetOutOfStock`, `GetExpiringSoon`, and `GetValuation`: `ValidateInventoryFiltersAsync` only rejects requests that provide both location kinds; it does not authorize either one before sending the query.
- `GetAll` and `GetById`: no location authorization.

By contrast, `Search` and `GetVariantAttributeFilterOptions` use `ResolveInventoryFiltersAsync` when explicit filters are supplied. This route-by-route inconsistency is material; authorization in a shared service does not protect handlers that do not call it.

Evidence: [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L58), [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L128), [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L163), [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L204), [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L239), [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L406).

### A3 — Aggregates and remaining read paths

`GetTotalStockByVariant` returns a total across locations using only `variantId`. `GetInventorySummary` returns a location-grouped summary without accepting or deriving an authorized location set. These are concrete aggregate scope gaps. Other stock ledger, transaction report, inventory references, and report paths require a complete endpoint inventory before A3 can be closed; this assessment does not claim that every such path was tested.

Evidence: [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L220), [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L276).

## Product, variant, SKU, and barcode identity

| Identity | Current verified representation and constraint | Implication |
|---|---|---|
| Main Product Code | Nullable `Product.ProductCode` (max 50 in EF). Product entity documentation identifies it as the business-facing master code. A partial unique index applies globally to non-null values. | It is the existing Main Product Code field; no second master/code field is justified. Same code in different Businesses is currently prevented by the global index and global duplicate checks. |
| Product | `Product.Id` is a PostgreSQL `bigint` identity; variants and related records reference it. | Preserve existing Product IDs and history; do not clone Products per Outlet to simulate sharing. |
| Sellable variant | `ProductVariant.Id` is a `bigint` identity; required `ProductId` FK links it to its parent Product. | This is the authoritative scanner/count target. Resolve a barcode to exactly one authorized VariantId. |
| Product-level SKU | `Product.Sku` is nullable. No Product-level SKU index was found in the Product EF configuration. Application create/update checks exist, but they are global and cannot provide a database concurrency guarantee by themselves. | Do not confuse Product SKU with the sellable variant SKU. Confirm legacy normalization and actual uniqueness before relying on it. |
| Variant SKU | Required `ProductVariant.Sku`, globally unique in the EF model and migration snapshot. | Lookup and duplicate checks are global today; no tenant boundary is applied. |
| Product barcode | Nullable `Product.Barcode`, globally unique within the Products table. | It is a separate parent-level identifier and is not an exact sellable-variant identity. |
| Variant barcode | Nullable `ProductVariant.Barcode`, globally unique within the ProductVariants table. | A unique variant barcode maps to a VariantId only within that table; it does not establish Business eligibility. |
| Cross-table barcode namespace | Product and variant barcodes use separate unique indexes; no single constraint spans both tables. Product and variant lookup paths also query separate entity sets. | A parent Product barcode may collide with a variant barcode. Cross-table and normalized collisions must be profiled and handled explicitly; never select an arbitrary match. |
| Dynamic attributes | `ProductVariant.Attributes` is required JSONB. Named variations/options are stored relationally and linked to products/variants. | There is no verified invariant reconciling JSONB values with selected relational options. Define one allowlisted read projection and quarantine conflicting variant data. |
| Inventory key | Unique `(VariantId, LocationId, LocationType)` tuple. | Ensures at most one row per variant/location/type; it does not enforce cross-Business product/location compatibility. |

Evidence: [Product.cs](../../../src/RetailPOS.Domain/Entities/Product.cs#L3), [ProductVariant.cs](../../../src/RetailPOS.Domain/Entities/ProductVariant.cs#L3), [ProductIdentifierLookupStore.cs](../../../src/RetailPOS.Infrastructure/Products/ProductIdentifierLookupStore.cs#L10), [ProductCreateStore.cs](../../../src/RetailPOS.Infrastructure/Products/ProductCreateStore.cs#L13), [RetailPOSDbContext.cs](../../../src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L539), [InitialBaseline.cs](../../../src/RetailPOS.Infrastructure/Data/Migrations/20261004095137_InitialBaseline.cs#L4118), [InitialBaseline.cs](../../../src/RetailPOS.Infrastructure/Data/Migrations/20261004095137_InitialBaseline.cs#L4368), [InitialBaseline.cs](../../../src/RetailPOS.Infrastructure/Data/Migrations/20261004095137_InitialBaseline.cs#L4415).

The EF model and migration snapshot are source evidence only. The currently deployed PostgreSQL schema/indexes were not queried; parity is unknown. No production or certified read-replica data profile was performed, so null/blank ProductCode, duplicate/canonical barcode, cross-table collision, orphan relationship, and attribute mismatch counts are **unknown**.

## Severity and risk

| Severity | Finding | Evidence and impact |
|---|---|---|
| **HIGH** | Cross-Business Product disclosure and mutation risk | Generic product list/search/direct-ID/SKU/barcode/category operations use permission policies but the reviewed controller/query/store flow has no effective Business predicate. Nested variant/variation and product-image paths also address caller-supplied Product/Variant IDs without an ownership boundary. A user with the relevant existing permission may receive or mutate a record outside the intended Business. Static review only; no live exploit test was run. |
| **HIGH** | Inventory location disclosure through incomplete A2/A3 checks | Caller-supplied outlet/warehouse IDs, direct inventory IDs, no-filter reads, and aggregate endpoints bypass authorized-location resolution in confirmed controller paths. Actual runtime exposure depends on deployed user/permission configuration, which was not tested here. |
| **HIGH** | No enforced Product-to-location Business consistency | Neither Product nor Inventory carries the ownership keys needed to enforce that a counted/stocked variant belongs to the Business owning its location. Outlet BusinessId is nullable and Inventory.LocationId is polymorphic. |
| **MEDIUM** | Barcode ambiguity across tables and unverified normalization | Independent Product and ProductVariant unique indexes cannot prevent collisions between the two tables or under a future normalization rule. This can misresolve a scanned code or produce inconsistent lookup behavior. |
| **MEDIUM** | Identifier policy conflicts with independent Business catalogues | Current global ProductCode and variant SKU/barcode indexes/checks prevent some cross-Business reuse. Whether that is intended is an unapproved owner decision; changing uniqueness before scoped lookup is safe would create disclosure and ambiguity risks. |
| **MEDIUM** | Unprofiled legacy data and attribute projection | Deployed index parity, Product ownership mappings, orphan references, duplicate identifiers, and JSONB/relational attribute consistency are unverified. Incorrect backfill or projection could assign or resolve the wrong sellable variant. |

These are source-level risk classifications, not a claim that a specific production account has exploited them. No live database, user, or HTTP access was exercised for this assessment.

## Reusable backend components

Reuse the existing Clean Architecture/MediatR patterns, but do **not** wire a new mobile API directly to unscoped handlers as-is:

- **Product read pipeline:** `SearchProductsQuery`, `SearchProductVariantsQuery`, `GetProductByIdQuery`, `GetProductByLookupQuery`, `IProductReadStore`, and `ProductReadStore` provide query/handler/store/read-model seams and existing product/variant/category/image mapping. Add an explicit server-derived Business scope and a mobile-specific allowlisted projection before reuse.
- **Variant identifier resolution:** `IProductIdentifierLookupStore` and `ProductIdentifierResolver` provide existing identifier-generation and lookup abstractions. They are not a safe tenant-scoped barcode-to-VariantId API in their current global form. Build/extend exact barcode resolution with Business eligibility, active/stock-count eligibility, ambiguity handling, and non-disclosing errors.
- **Location authorization:** `IUserOutletAccessService`, `UserOutletAccessService`, and `UserOutletAccess` provide authenticated context and authorized location resolution. Ensure each endpoint uses the resolved scope inside the query and that data relationships prove the Product/location Business match.
- **Current inventory store/query families:** existing Inventory query handlers and read models may provide mapping and operational semantics, but must be audited and given an enforced authorized-location set for every read.
- **Integration test infrastructure:** the repository has `RetailPOS.IntegrationTests`, `RetailPosApiFactory`, and PostgreSQL test-database infrastructure, plus product, inventory, barcode, and POS HTTP test files. Existing characterization tests are not evidence of Business catalogue isolation unless they assert cross-Business denial and non-disclosure against PostgreSQL.

## API contract and approval status

- No `/api/mobile/v1` controller or catalogue manifest/snapshot/changes endpoint was found under `src`.
- The `/api/mobile/v1/catalogue/manifest`, `/snapshot`, and `/changes` routes in [Backend API and Database Plan](04_BACKEND_API_AND_DATABASE_PLAN.md) and the revision/delta protocol in [Offline Catalogue and Stock Take Design](05_OFFLINE_CATALOGUE_AND_STOCK_TAKE_DESIGN.md) are proposals, not implemented contracts.
- Some existing routes have literal version prefixes (for example HR/Payroll); no general API-versioning registration was found in `Program.cs`. A route naming precedent is not approval of the mobile versioning, compatibility, or deprecation policy.
- [Phase 0 Review and Proposed ADR Resolutions](12_PHASE_0_REVIEW_AND_PROPOSED_ADR_RESOLUTIONS.md) says selected decisions require named-owner approval. ADR-CAT-01 ownership, ADR-CAT-02 barcode rules, and ADR-CAT-03 attribute projection are not evidenced as approved. ADR-API-01 is also listed as requiring technical approval.
- Therefore snapshot revision semantics, stable paging/cursors, delta ordering/tombstones, schema versioning, scope-switch behavior, error/non-disclosure shape, and retention are not approved API contracts.

## Minimum safe implementation sequence

| Order | Slice | Dependencies and required outcome |
|---:|---|---|
| 0 | **Approval and read-only profile** | Product, Security, ERP/API, DBA, Inventory, and Operations approve Product/Category/Variation ownership, shared/system-product treatment, null-owner behavior, barcode/SKU uniqueness and canonicalization, historical references, authorization context, and migration policy. DBA supplies an authorized read-only replica/snapshot profile: null/blank codes, raw and canonical duplicates, cross-table barcode collisions, orphan links, Inventory-location mismatches, and attribute-store disagreements. No schema change or backfill before signed mapping and exception thresholds. |
| 1 | **Close A1 safely** | Preserve the existing POS and variant-search authorization checks. Add explicit foreign/same-Business and omitted-location negative HTTP tests for both product and barcode variant-search routes, including no stock quantity disclosure. Confirm cache keys cannot bypass authorization. |
| 2 | **Complete A2** | Resolve and authorize all caller-supplied Outlet/Warehouse filters and direct reads before query execution. Specify and implement omitted-filter behavior as the approved authorized location set/default; null must never accidentally mean all database locations for an ordinary user. Add route-matrix tests for outlet, warehouse, mixed/invalid filters, and unauthorized IDs. |
| 3 | **Complete A3 route audit** | Inventory all aggregate, direct-ID, ledger, report, export, and reference stock reads. Make scope explicit in application query inputs and enforce the resolved authorized location set in persistence predicates. Test no-location, multi-location, cross-Business, and non-disclosure cases. |
| 4 | **B2 additive ownership model and scoped transition** | Only after B1/profile approval, add nullable ownership fields/relationships and indexes using an expand-first migration. Derive Business from trusted authenticated context/authorized Outlet; never trust caller BusinessId. Keep legacy rows gated from ordinary access and preserve ProductId/VariantId. Inventory location Business consistency must be enforceable. |
| 5 | **B3 controlled backfill** | Apply only signed mappings in bounded, auditable PostgreSQL batches. Quarantine ambiguous/unmapped records; preserve before-images and IDs; validate row counts, FKs, Product-to-location Business consistency, and normalized identifier conflicts. |
| 6 | **B4 enforcement** | Require ownership for in-scope records; make every read/write/search/barcode/media/nested-variant/POS/report path use the same Business boundary. Add the approved Business-scoped ProductCode uniqueness and explicitly approved barcode/SKU policy. Global administration must be separately permissioned, explicit, and audited; Super Admin status alone must not silently bypass tenant scoping. |
| 7 | **Approve and implement mobile API contract** | After ERP enforcement stabilizes, approve `/api/mobile/v1`, allowlisted DTOs, stable snapshot revision and cursor behavior, deltas/tombstones, retention/expiry, scope binding, error semantics, rate limits, and compatibility support. Expose only scoped catalogue reads; do not copy pricing/cost or unrelated business fields into the mobile projection. |
| 8 | **Enable mobile catalogue after gates** | PostgreSQL-backed API tests, mobile contract tests, security review, migration rehearsal, canary, operational monitoring and rollback gates pass. Flutter remains unable to call catalogue endpoints before this gate. |

Rollback for all security-sensitive steps must fail closed or disable the route/feature; restoring an unscoped query is not an acceptable rollback.

## Required PostgreSQL-backed tests and gates

Use the existing integration-test infrastructure and a real PostgreSQL provider. Provider-independent mocks or in-memory EF tests are insufficient for uniqueness, FK, transaction, migration, and concurrency guarantees.

1. **Ownership schema/migration:** additive migration from representative legacy data; preserves ProductId/VariantId and all historical references; same ProductCode may exist across Businesses only if ADR approves; same-Business conflict fails; null/quarantined ownership cannot leak; rollback/restore rehearsal.
2. **Cross-Business product API matrix:** Business A can read/search/lookup/update only A; Business B cannot read Product, Variant, Category-dependent details, image, variation, or barcode existence from A through list, search, direct ID, SKU, ProductCode, barcode, nested IDs, exports, or cache. Use non-disclosing 404/403 per approved contract.
3. **Outlet sharing:** two authorized Outlets in the same Business resolve the same ProductId/VariantId and catalogue revision; another Business is denied. A caller-supplied BusinessId/outlet cannot extend identity-derived access.
4. **Inventory isolation:** Outlet A sees its own Inventory only; Outlet B and unauthorized warehouses cannot be selected by direct ID, filter, aggregate, valuation, search, summary, stock hint, or variant lookup. Verify Product Business equals location Business on reads and writes.
5. **Identifier uniqueness and ambiguity:** concurrent duplicate creates; ProductCode canonicalization; global or Business-scoped SKU/barcode decision; cross-table Product/Variant barcode collisions; leading zeros and approved symbologies; ambiguous/cross-scope scanner lookup returns no arbitrary VariantId.
6. **Attributes and catalogue projection:** relational variation selections plus JSONB attributes; deterministic attribute/value projection; conflicts quarantined; only allowlisted fields serialized; status/eligibility filtering; stable ProductId/VariantId.
7. **Snapshot/delta contract (after API approval):** scope-bound manifest, repeatable revision-stable paging, ordered changes/tombstones, cursor resume/expiry, duplicate/replay and revision-gap behavior, concurrent edits during snapshot, authorization revocation, and non-disclosure.
8. **Rollout gates:** migration rehearsal on a production-like PostgreSQL clone; query plan/index review; canary Business with multiple Outlets; reconciliation counts and monitored 403/404, unmapped rows, identifier conflicts, latency, and audit events. Do not use the running development database as a substitute for an approved production-equivalent profile.

Existing product, inventory, and POS integration tests were inspected as source only; **no tests were executed** for this assessment.

## Open blockers and decision gates

1. Written Product-owner decision that the catalogue is Business-owned and shared across that Business's authorized Outlets, plus explicit treatment of system/shared products and ambiguous legacy rows.
2. Security/ERP approval of identity-derived Business/Outlet context, global-administration exception and audit requirements, and error/non-disclosure behavior.
3. Inventory/DBA approval of location ownership constraints and read-only profile source; live schema parity and data-quality counts remain unknown.
4. Product/Inventory approval of ProductCode, SKU, barcode namespace/canonicalization, cross-table collision handling, and dynamic-attribute projection.
5. Closure of A2/A3 outlet and warehouse inventory leakage paths and A1 negative-test gaps.
6. API/Architecture approval of `/api/mobile/v1` versioning, stable snapshot/delta contract, schema evolution and support policy.
7. PostgreSQL-backed migration, authorization, uniqueness, concurrency, snapshot and regression test gates.

## Final verdict: BLOCKED

P3 catalogue backend implementation and Flutter business-data integration are **not ready**. The blocking reasons are the absent/unapproved Business ownership model, confirmed unscoped Product and Inventory paths, incomplete A2/A3 containment, unprofiled legacy identifiers/ownership, and unapproved mobile API contracts. A1 authorization code is present on reviewed POS and variant-search paths, but test coverage is incomplete and does not address the missing Product Business boundary.

This assessment makes no implementation change. Stop here and obtain the listed approvals and remediation gates before beginning P3.
