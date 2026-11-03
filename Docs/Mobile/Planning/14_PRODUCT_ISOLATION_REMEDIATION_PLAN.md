# Product Catalogue Isolation Remediation Plan

**Status:** Proposed for approval; planning only
**Date:** 2026-10-14
**Related verification:** [13_PRODUCT_CATALOGUE_BUSINESS_ISOLATION_VERIFICATION.md](./13_PRODUCT_CATALOGUE_BUSINESS_ISOLATION_VERIFICATION.md)

## Executive recommendation

Treat location authorization and product ownership as two distinct controls:

1. **Immediate containment:** authorize every caller-selected outlet/warehouse before returning location stock. Reuse `IUserOutletAccessService`; do not make an outlet check stand in for Product.BusinessId ownership. Prioritize `/api/pos/stock-hint`, both stock-bearing variant searches, and Inventory endpoints that accept location filters but only check that the outlet and warehouse filters are not both supplied.
2. **Catalogue ownership:** add required `Product.BusinessId` with FK after explicit legacy ownership mapping. Preserve `Product.Id` and `ProductVariant.Id`. Derive access from authenticated effective context and the authorized outlet; never accept a request-supplied BusinessId as authority.
3. **Migration:** expand → backfill → validate → enforce → contract with business-owner-approved mapping, audit, staged rollout, and reversible enforcement gates. Never infer a product owner solely from its inventory or sales references.

The stock-hint and variant-search authorization defects can be contained independently of catalogue ownership. Catalogue endpoints remain unsafe for the confirmed Business isolation rule until their ownership gate is implemented.

### Evidence classifications

- **VERIFIED:** source or tests directly establish behavior.
- **PROPOSED:** recommendation not implemented or approved.
- **UNKNOWN:** source/repository does not establish an answer.
- **BLOCKED:** requires approved production-equivalent data or owner decision.

No production-data assumptions in this plan are treated as verified. No production database, schema, data or test source was changed for this planning work.

## Current evidence and scope

The verification in document 13 established:

- **VERIFIED:** Product/Variant lack Business ownership in the reviewed EF model; Product.ProductCode and variant barcodes have global-per-table unique indexes; Inventory uniqueness is `(VariantId, LocationId, LocationType)`.
- **VERIFIED:** POS lookup and stock ledger/report paths already call outlet/location authorization services in relevant request paths.
- **VERIFIED:** POS stock hint accepts `variantId` and `outletId` without outlet authorization. Product and barcode variant-search endpoints accept `locationId`/`locationType` and return `StockQuantity` without location authorization.
- **VERIFIED:** `InventoryController.ResolveInventoryFiltersAsync` authorizes explicit filters, but several other Inventory actions invoke only `ValidateInventoryFiltersAsync`, which checks mutual exclusivity, not access.
- **UNKNOWN:** live deployed FK/index parity, existing product ownership, count of catalogue collisions, and whether production role grants make every statically exposed path reachable.

## Task A — Immediate security containment: location stock authorization

### A.1 Inventory-bearing endpoint inventory

This table covers API paths found in the reviewed source that accept a caller-controlled OutletId/LocationId or return location stock. It distinguishes endpoint paths from product ownership.

| Endpoint | Caller-controlled location input / stock output | Current source behavior | Plan |
|---|---|---|---|
| `GET /api/pos/stock-hint` | Required `outletId`, `variantId`; returns available quantity and sufficiency | **VERIFIED GAP:** [PosController.cs](../../../src/RetailPOS.API/Controllers/PosController.cs#L292) sends the supplied IDs directly to the query; no `_outletAccess` call. | **Immediate containment.** Authorize required outlet before querying. |
| `GET /api/products/variants/search` | Optional `locationId`, `locationType`; returns `StockQuantity` | **VERIFIED GAP:** [ProductsController.cs](../../../src/RetailPOS.API/Controllers/ProductsController.cs#L439) forwards both values without location authorization. | **Immediate containment.** Authorize an explicit location; define safe behavior when omitted before allowing stock output. |
| `GET /api/barcodes/variants/search` | Optional `locationId`, `locationType`; returns `StockQuantity` | **VERIFIED GAP:** [BarcodesController.cs](../../../src/RetailPOS.API/Controllers/BarcodesController.cs#L39) forwards both values without location authorization. | **Immediate containment.** Same policy and shared helper as product variant search. |
| `GET /api/inventory/outlet/{outletId}` | Required outlet route ID; returns all stock at outlet | **VERIFIED GAP:** [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L130) sends the ID directly to the read query. | **Containment A.2.** Authorize exact outlet. |
| `GET /api/inventory/warehouse/{warehouseId}` | Required warehouse route ID; returns all stock at warehouse | **VERIFIED GAP:** [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L147) sends ID directly to the read query. | **Containment A.2.** Authorize exact warehouse. |
| `GET /api/inventory/low-stock` | Optional `outletId` / `warehouseId`; returns stock levels | **VERIFIED GAP:** [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L164) calls validation that only rejects both values together. | **Containment A.2.** Authorize supplied filter; constrain unfiltered results to authorized scope. |
| `GET /api/inventory/out-of-stock` | Optional `outletId` / `warehouseId`; returns inventory/product details | **VERIFIED GAP:** [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L183) uses the same exclusivity-only validation. | **Containment A.2.** Same scope policy. |
| `GET /api/inventory/expiring-soon` | Optional `outletId` / `warehouseId`; returns stock/batch details | **VERIFIED GAP:** [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L203) uses the same exclusivity-only validation. | **Containment A.2.** Same scope policy. |
| `GET /api/inventory/valuation` | Optional `outletId` / `warehouseId`; returns stock/value | **VERIFIED GAP:** [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L242) uses the same exclusivity-only validation. | **Containment A.2.** Same scope policy, including cost permission. |
| `POST /api/inventory/search` | Optional body `OutletId` / `WarehouseId`; returns stock | **PARTIALLY VERIFIED safe for explicit filters:** [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L307) calls `ResolveInventoryFiltersAsync` when a filter is supplied. Unfiltered behavior and multi-location scope must be checked before relying on it. | Regression test explicit foreign-location denial; constrain unfiltered query to authorized scope. |
| `GET /api/inventory/variant-attribute-filters` | Optional query `outletId` / `warehouseId`; reveals attributes used by stocked variants | **PARTIALLY VERIFIED safe for explicit filters:** [InventoryController.cs](../../../src/RetailPOS.API/Controllers/InventoryController.cs#L349) resolves filters; omitted-filter behavior still needs scope assertion. | Regression test explicit denial and authorized/default scope. |
| `GET /api/stock-ledger/variant/{variantId}/location/{locationId}` | Required location ID/type; stock history | **VERIFIED guarded:** [StockLedgerController.cs](../../../src/RetailPOS.API/Controllers/StockLedgerController.cs#L50) uses `ResolveAndAuthorizeLocationAsync`. | Regression only; preserve 403 on foreign location. |
| `POST /api/stock-ledger/search` | Optional body location ID/type; ledger rows | **VERIFIED guarded when location filter supplied:** [StockLedgerController.cs](../../../src/RetailPOS.API/Controllers/StockLedgerController.cs#L92) resolves and authorizes; absent-filter scope remains a separate audit item. | Regression tests and confirm no all-business result for non-global roles. |
| `GET /api/stock-transactions` | Optional `outletId` / `locationType`; product movement history | **VERIFIED guarded:** [StockTransactionReportsController.cs](../../../src/RetailPOS.API/Controllers/StockTransactionReportsController.cs#L34) resolves/authorizes location before querying. | Regression only; preserve existing behavior. |
| `GET /api/pos/lookup` | Optional `outletId`; returns product card and stock hint | **VERIFIED guarded for supplied outlet:** [PosController.cs](../../../src/RetailPOS.API/Controllers/PosController.cs#L212) calls outlet filter authorization. | Regression only. Product Business ownership is separate and remains required. |

**Related routes that require a separate scope audit:** `GET /api/inventory`, `GET /api/inventory/{id}`, `GET /api/inventory/variant/{variantId}/total-stock`, and `GET /api/inventory/summary` do not take a location filter but can return location-specific or cross-location stock. Before treating them as safe for Business users, verify that the endpoint permission is explicitly global or scope results to the authorized location set. `GET /api/stock-ledger/{id}` and `GET /api/stock-ledger/reference/{referenceType}/{referenceId}` also lack a location parameter and should be checked for record-level scope. This audit does not expand the immediate fix silently; approval is required if it changes the global reporting contract.

### A.2 Policy

**Proposed location authorization contract:**

1. Resolve the caller's real user, effective Business, effective outlet and role-switch context using the existing server-side `UserOutletAccessService` → `IUserOutletAccess` path. Never trust Angular state, query/body IDs, `X-Business-Id`, or a requested location as proof of access.
2. An explicitly supplied outlet/warehouse must be authorized for the caller and must match the requested `locationType`. For a required outlet (`stock-hint`), call an outlet-specific authorization method or `ResolveAndAuthorizeLocationAsync(outletId, "outlet")` and reject before the stock query. Return the API's standard **403** on an unauthorized known location.
3. A required location omitted from an endpoint that returns location stock must be resolved to an authorized default only if the existing service contract defines an unambiguous default. If no unique authorized location exists, require a selection and return a client error; never fall through to all locations.
4. Optional location filters may preserve explicitly approved “all authorized locations” behavior only if the query is restricted to the server-provided authorized location set. A null filter must not mean all database locations for a non-global user.
5. Keep `products.view`, `barcode.view`, `inventory.view`, `low_stock_alerts.view`, and sales/POS permission checks. Location authorization is an additional independent check, not a replacement for endpoint permission.
6. Distinguish this control from Product ownership: a caller authorized for Outlet A1 may access stock there only for Products that are also authorized to that Business after the Product.BusinessId work ships. Conversely, Product ownership does not grant access to stock at an unauthorized Outlet.
7. Super Admin location access follows the existing global access implementation but is not a substitute for the separate, permission-controlled global Product administration exception.

**Implementation shape:** centralize resolution in the existing `IUserOutletAccessService`; if the current service cannot express an authorized multi-location set safely, add a narrow typed method returning the authorized outlet/warehouse IDs rather than re-implementing role logic in controllers. Keep query contracts location-scoped. Avoid a new generic authorization framework.

### A.3 Exact proposed file changes (not yet made)

**Immediate slice A.1 — POS stock hint and variant lookup**

- `src/RetailPOS.API/Controllers/PosController.cs`
  - In `StockHint`, authorize the required `outletId` as an outlet before sending `GetPosStockHintQuery`.
  - Map unauthorized outlet context to 403 consistently with `Lookup`; do not query stock first.
- `src/RetailPOS.API/Controllers/ProductsController.cs`
  - In `SearchVariants`, authorize `(locationId, locationType)` if provided.
  - Define omission behavior: either omit the stock field for product-only searches or query only an unambiguous authorized effective location; no global fallback. Preserve PO/GRN call compatibility deliberately.
- `src/RetailPOS.API/Controllers/BarcodesController.cs`
  - Apply the same policy/contract as ProductsController variant search; avoid divergent permission logic.
- Potential small shared helper in `src/RetailPOS.API/Services/` only if it removes duplicated resolution and has a typed result/explicit optional-location behavior; do not introduce a second access-service abstraction if existing methods suffice.
- Tests to add under `tests/RetailPOS.IntegrationTests/Sales/` and `Products/`:
  - `PosStockHintOutletAuthorizationHttpTests.cs`
  - Extend or add `ProductVariantSearchLocationAuthorizationHttpTests.cs`
  - Extend/add barcode route coverage, e.g. `BarcodeVariantSearchLocationAuthorizationHttpTests.cs`.

**Immediate slice A.2 — direct Inventory location routes**

- `src/RetailPOS.API/Controllers/InventoryController.cs`
  - `GetByOutlet`/`GetByWarehouse`: call exact location authorization before query.
  - `GetLowStock`, `GetOutOfStock`, `GetExpiringSoon`, `GetValuation`: replace exclusivity-only validation with authorization-aware resolution. Preserve mutual-exclusion validation and approved default/all-authorized semantics.
  - Review `Search` and `GetVariantAttributeFilterOptions` to assert explicit and omitted filters are bounded to authorized locations.
  - If a location authorization exception is not globally mapped consistently, map it to the established 403 response format here without hiding database or query errors.
- Tests under `tests/RetailPOS.IntegrationTests/Inventory/`:
  - `InventoryLocationAuthorizationHttpTests.cs` covering every route in this slice; use a parameterized endpoint matrix where test readability permits.
  - Regression cases for authorized outlet, authorized warehouse, foreign outlet, foreign warehouse, wrong location type, no default/multiple authorized locations, and both filters submitted.

**Regression-only scope-audit slice A.3**

- `src/RetailPOS.API/Controllers/StockLedgerController.cs`, `StockTransactionReportsController.cs`, `PosController.cs`, plus `InventoryController.cs` routes without location inputs: verify explicit and omitted-filter isolation; make changes only if acceptance policy is approved.
- Tests in existing `StockLedgerReadHttpTests.cs`, `InventoryReadHttpTests.cs`, `PosLookupHttpTests.cs`, and `SalesReports`/accounting-report HTTP suites, with new tests only where gaps are confirmed.

**No application code, schema, migrations, or existing tests are changed in this plan.**

### A.4 Negative HTTP and regression tests

For each changed endpoint, tests must authenticate a normal Business user and seed two Businesses, outlets A1/A2 in A, outlet B1 in B, and at least one warehouse per relevant scope. Assert:

- Business A user requesting B1 stock gets 403 and does not receive quantity, product identifiers, names, batch/expiry data, valuation, or attribute/filter options.
- Business A user requesting A1/A2 is allowed only if the user's outlet-access policy authorizes that outlet.
- Outlet ID supplied with `locationType=warehouse`, and warehouse ID supplied with `locationType=outlet`, are rejected.
- Required outlet omitted or invalid fails closed; it must not invoke an all-location query.
- Optional location omitted follows the approved authorized-default or authorized-set policy; unfiltered output never includes B1 for an ordinary A user.
- POS stock hint performs no query/response for an unauthorized location and preserves existing authorized quantity/sufficiency behavior.
- Variant search and barcode variant search maintain existing pagination, product fields, cost redaction, and stock quantity behavior for authorized locations.
- Existing POS lookup, inventory search, stock ledger timeline/search, and stock transaction report continue to allow authorized locations and deny foreign locations.
- Super Admin behavior is tested only for its existing outlet-access authorization and does not implicitly authorize cross-Business Product operations.

## Task B — Business-owned Product model

### B.1 Recommended minimum-risk model

**Proposed:** Add required `Product.BusinessId` FK to `Business` after controlled backfill. Keep `Product.Id` and `ProductVariant.Id` unchanged. `ProductVariant.BusinessId` should not initially duplicate ownership: derive it through its required parent Product. If later database-level composite guarantees are needed, add a unique `(BusinessId, ProductId)` Product key and composite FK or another reviewed constraint; do not introduce redundant ownership that can drift without a specific enforcement benefit.

For an authenticated user, resolve the effective Business from the signed identity/context and verify the selected Outlet belongs to that Business and is authorized to the user. Product CRUD/search/lookup must then constrain by the server-derived Business. A caller-supplied BusinessId may be accepted only as a selection request that is verified against authorization; it is never a scope grant.

**Global administration exception:** introduce a distinct permission such as `products.manage_all_businesses` (name subject to the existing permission catalogue conventions). Require both the permission and an explicit global-administration workflow. Audit actor, permission, target Business, Product/Variant IDs, operation, timestamp, reason/correlation ID, and before/after values as applicable. `IsSuperAdmin` alone is not the permission. Default Super Admin product views should still be Business-scoped unless the caller enters the audited global workflow.

### B.2 ProductCode and barcode uniqueness

- **ProductCode — PROPOSED:** normalize new/updated codes using one approved canonical rule (recommend trim + uppercase, with max-length and blank-to-null policy) and enforce a partial unique index on `(BusinessId, ProductCode)` where ProductCode is not null. Preserve original display semantics only if a separate display value is required; avoid silent rewrite of legacy identifiers. Profile collisions under canonicalization before enforcement. Business A and B may use the same code.
- **Variant barcode — PROPOSED approval required:** lowest-risk initial policy is to retain global uniqueness among sellable `ProductVariant.Barcode`, matching the existing EF index and ensuring scanner uniqueness regardless of context. Product/Inventory owners must explicitly approve this scope. If product owners require duplicate physical barcode values in different Businesses, choose Business-scoped uniqueness only after all scan paths enforce Business scope, and model an enforceable key (e.g. a reviewed BusinessId + ProductId relational constraint/unique index or dedicated identifier registry). Do not drop the current global index before scoped resolution and data cleanup are proven.
- **Product barcode:** remains a non-sellable parent identifier unless Product/Inventory approve another behavior. Product and variant indexes are separate; cross-table collision detection/handling needs explicit service validation or a unified identifier registry. Variant scanning must resolve exactly one eligible VariantId; never select arbitrarily.
- **SKU:** decide uniqueness scope separately. Existing global unique SKU index is lowest risk to retain through initial migration. Lookup must nevertheless be Business-scoped and must not disclose whether a foreign Business has a matching SKU.

### B.3 Category, Variation, Option and attribute ownership

- **Category — PROPOSED:** Business-owned categories are recommended where names, hierarchy, visibility, or lifecycle can vary per Business. A category must not be used as a proxy for Product ownership. Add Category.BusinessId only after a mapping/profile; Product's BusinessId and Category.BusinessId must agree for Business-owned categories. If system categories are required, model them explicitly as immutable shared definitions, not as null-owned rows implicitly visible everywhere.
- **Variation and VariationOption — UNKNOWN policy / proposed deferral:** currently they are global reference definitions, while product assignment and selected-option relationships are per Product. Keep definitions read-only/shared in the first ownership migration only if product owners confirm the vocabulary is globally canonical. Otherwise introduce Business ownership for definitions and ensure assignment links cannot cross Business. Do not conflate shared attribute labels with shared Product records.
- **ProductVariant attributes — PROPOSED:** variant attributes remain variant/product data; preserve the current JSONB and relational option representation during first ownership rollout. Establish one canonical read projection and detect JSONB/option-link disagreement. Ownership always derives from Product.BusinessId.

### B.4 Server-side enforcement coverage

Create/use one Product ownership boundary in application/API query paths (existing architecture conventions govern the exact abstraction). For each request:

1. Resolve and validate authenticated effective Business and authorized Outlet where the operation is outlet-contextual.
2. Build query with server-derived BusinessId at the persistence predicate; avoid fetching a foreign entity first and trusting a later UI check.
3. Writes resolve target Product/Variant through the same scoped query, including nested IDs, before mutation. A Variant must have a Product in the authorized Business.
4. Return non-disclosing not-found/forbidden behavior consistently; do not leak foreign Product names, barcode matches, counts, images, pricing, or existence through error differences.
5. Use the same scope for list, search, category, SKU/barcode lookup, ProductId/VariantId lookup, variant creation/maintenance, variation assignment, images/media, POS lookup, product exports, stock-count catalogue, and background/report reads.
6. For writes, validate Inventory Location.BusinessId equals Product.BusinessId through the authorized location; do not authorize inventory merely because the Product is visible.
7. Explicit global administration bypasses Business predicate only through the separately permissioned, audited route/operation described above.

Endpoints requiring a route-by-route audit include `ProductsController`, `ProductVariationsController`, `ProductImagesController`, `BarcodesController`, POS lookup/cart/sale validation, inventory and reports, pricing/overrides, stock counts/transfers/adjustments, and imports/exports. Do not treat applying a filter to `ProductsController` alone as complete isolation.

## Task C — Legacy data migration

### C.1 Pre-migration prerequisites

1. Product owner approves ownership rule, treatment of explicitly shared/system Products, category/attribute ownership and who may approve mapping.
2. DBA provides a production read replica or certified snapshot and runs reviewed, SELECT-only profiling. Counts below remain **UNKNOWN** until that evidence is returned.
3. Inventory/Sales/Accounting owners approve historical-reference semantics; Security approves temporary handling of null/unmapped owners; Operations approves maintenance windows and rollback.
4. Save a signed-off mapping for each existing Product to one Business, explicit global/system scope, or a quarantine/manual-review bucket. Do not map by “has inventory,” latest sale, current outlet, category, creator, or any single historical reference alone.

### C.2 Expand → backfill → validate → enforce → contract

| Stage | Proposed work | Safeguards and gate |
|---|---|---|
| **Expand** | Add nullable `Product.BusinessId`, FK/index support, and a staging/exception mechanism for audited mapping. Keep existing global ProductCode index during transition. Add code paths/contracts able to persist and read the owner without changing Product/Variant IDs. | Additive only. Deploy compatible with old app/API. New Product creation must require verified Business context and write BusinessId. No new unowned ordinary Product records. |
| **Backfill** | Apply owner-approved mapping in small, restartable, auditable batches. Preserve all PKs and existing FKs. Quarantine ambiguous records and keep them inaccessible to ordinary Business contexts. | Before/after row counts, mapping source/approver, batch checksums, transaction boundaries, retry/idempotency, backup/restore rehearsal. Never infer ownership only from Inventory/Sales. |
| **Validate** | Check all Products mapped or explicitly quarantined; ProductVariant parent consistency; category/variation assignment policy; normalized code collisions; product/variant barcode collision policy; all inventory locations against Product Business; stock-count, transfer, adjustment and ledger references; sales and accounting history references. | No transition until zero unexplained mismatches or approved exception register exists. Validate referential totals and sample audit/history joins. |
| **Enforce** | Make BusinessId required for Business-owned Products; add FK and non-null constraint; replace global ProductCode index with partial composite `(BusinessId, ProductCode)` unique index; enforce scoped read/write authorization and explicit admin exception. Keep approved barcode/SKU uniqueness scope. | Feature flag/tenant canary, metrics for denied requests and unmapped access, online index strategy verified on PostgreSQL, rollback window and rehearsed restore. Keep quarantine inaccessible. |
| **Contract** | Remove transition-only nullable-owner writes/read fallbacks after all instances and jobs are upgraded. Update exports, reports, API contracts and operational documentation. Remove old global ProductCode index only after scoped duplicate checks and the replacement index are validated. | Separate release gate; no old mobile/ERP consumer can bypass ownership. Maintain stable IDs/historical references and audit retention. |

### C.3 Historical reference and data-quality requirements

Profile and report at least:

- Null/blank ProductCode; duplicate exact and normalized ProductCode; SKU and barcode duplicates; product-vs-variant barcode intersections.
- Product→Category, Variant→Product, ProductVariation/selected-option/VariantOption links, ProductImage→Product and orphan counts.
- Inventory VariantId/location existence and LocationType correctness; outlet/warehouse BusinessId versus candidate Product owner mismatches; duplicate inventory location keys.
- Sales line/return/void/held-sale references, sale outlet ownership, and product/variant references. Do not reassign sale history to make current ownership appear consistent.
- Stock ledger, stock-count headers/lines, adjustments, transfers, requisitions, purchase/GRN lines, barcode print history, pricing overrides and cost/valuation references by stable ProductId/VariantId and location.
- Accounting postings and historical valuation records that reference sales, inventory or product snapshots. Preserve immutable posted records; scope current product reads without rewriting historical posting meaning.
- Audit records, media storage paths, caches/search indexes, exports and integrations that resolve IDs or ProductCode.
- HR/Payroll: no Product FK is expected from the reviewed domain, but run regression tests and confirm the migration does not alter shared Business/Outlet entities or common authorization behavior. HR/Payroll must remain operational and untouched by ownership backfill.

**Ambiguous products:** require a signed mapping decision or quarantine. If one legacy Product is referenced by multiple Businesses, do not clone/reassign it automatically. Product owner must decide whether to split into new Products (new IDs only for newly created distinct catalogue entries, with an explicit history policy), retain a shared immutable/system record, or resolve underlying data error. Historical references keep their original ProductId/VariantId unless an approved audited data-correction process dictates otherwise.

## Task D — PostgreSQL-backed verification strategy

Use the repository's isolated PostgreSQL integration-test infrastructure (`PostgresTestDatabase` and existing PostgreSQL test conventions) only with disposable databases created by the test harness. Never point tests at production/shared databases. The current HTTP factory uses InMemory; ownership and unique-index acceptance must run through PostgreSQL with actual migrations/model DDL. Test configuration/availability is **UNKNOWN** until CI/runtime confirms the disposable PostgreSQL service.

### Required test groups

1. **ProductCode per Business:** create `ABA100P` for Business A and Business B; both succeed with distinct ProductIds. Duplicate within A fails. Include null/blank and canonicalization collision cases.
2. **Cross-Business Product CRUD:** A can read/update/delete its own Product; B receives approved non-disclosing denial for A ProductId. Ensure deletion cannot cascade into another Business's history; expected behavior for historical Product deletion is preserved (archive/restrict).
3. **Variant, barcode and SKU isolation:** exact VariantId, barcode and SKU lookup for an authorized Business succeeds; foreign Product/Variant identifiers return no foreign details. Verify product-vs-variant barcode collision policy and Super Admin audit event.
4. **Same-Business outlets:** A1 and A2 retrieve the same ProductId/VariantId and catalogue changes are visible to both under authorization; inventory rows remain distinct by location.
5. **Separate quantities:** set variant quantity at A1 and A2 to distinct values; each location query returns its own count, while approved aggregates equal their intended sum and do not include B.
6. **Location containment:** A user calling stock-hint with B1, foreign stock-bearing variant search with B1 location, or each in-scope Inventory location route receives 403/no stock body; authorized A1 succeeds; wrong location type, missing location, and multiple/no-default cases fail closed.
7. **Super Admin:** ordinary view remains scoped; explicit global permission route can access/manage across Businesses and emits required immutable audit record. A Super Admin claim without the explicit permission cannot use bypass.
8. **History/migration compatibility:** migrate a seeded legacy baseline through each stage; preserve ProductId/VariantId, sales/return/void links, Inventory, stock ledger/count/adjustment/transfer, accounting posting snapshots and audit; rerun idempotent backfill and rollback before contract.
9. **Database integrity:** PostgreSQL tests assert Business FK, required owner after enforcement, per-Business ProductCode index, retained barcode policy, Variant-parent ownership, and rejection of mismatched location/Product Business writes.
10. **Regression:** focused Product, POS, Inventory, Accounting suites, followed by all PostgreSQL integration tests and HR/Payroll test suites. Verify APIs, reporting totals, historical reads, exports, cost redaction, outlet access behavior, audit and concurrency.

### Acceptance gates

- No ordinary Business user can observe or modify another Business's Product or associated Variant, barcode, SKU, image, price, stock, or existence through an API path.
- Same-Business outlets share stable Product/Variant IDs, while stock stays location-specific.
- Same ProductCode succeeds across Businesses and conflicts only within the same Business under the approved canonicalization.
- All caller-controlled location queries authorize before read/write and cannot fall through to all-location queries.
- Global administrative operations require explicit permission, target scope, reason, and auditable actor/action record.
- Migration validation proves stable IDs and complete historical joins; no unexplained cross-Business location mismatches remain.
- POS, Inventory, Accounting, and HR/Payroll regression suites pass; PostgreSQL-only constraints are genuinely exercised.

## Proposed implementation slices and dependency order

| Slice | Objective and scope | Dependencies | Acceptance gate / rollback |
|---|---|---|---|
| **A1 Location containment: POS + variant search** | Authorize stock-hint outlet and both stock-bearing variant-search location inputs; no Product ownership logic in this slice. | Approve omitted-location contract and PO/GRN compatibility; reuse outlet service. | Negative HTTP tests pass; rollback by disabling the endpoint change only if it does not restore exposure—prefer keep endpoint closed/feature-disabled rather than reverting insecure behavior. |
| **A2 Inventory caller-supplied location containment** | Fix direct outlet/warehouse reads and low/out/expiring/valuation filters; prove search/filter omitted scope. | A1 service/error pattern; approve all-authorized multi-location semantics. | Endpoint matrix passes for outlet and warehouse; rollback to prior behavior is prohibited if it reopens foreign stock. Use deny/disable route if regression. |
| **A3 Remaining stock-read scope audit** | Review no-location aggregate and ID/reference ledger endpoints; approve global vs scoped report contracts, then fix/test gaps. | A1/A2; reporting owner decision. | Scope tests prove ordinary users cannot get unauthorized aggregates. Roll back only through safe access-denial/feature flag. |
| **B1 Ownership/identifier ADR + data profile** | Finalize Product ownership, Category/Variation/Option policy, barcode/SKU uniqueness and legacy mapping authority; profile read-only. | Product, Security, Inventory, DBA, Operations approvals. | No schema work until approved mapping and exception report are signed. No rollback required; planning/profile only. |
| **B2 Expand schema and scoped create/read compatibility** | Add nullable BusinessId/FK support, Business-scoped persistence/query abstractions and context-derived create. Do not yet remove existing global index. | B1; migration rehearsal; update every production writer/reader inventory. | Old clients and new code coexist; new writes always have owner. Rollback additive schema safely; retain field/data and disable new path, never drop mapping. |
| **B3 Controlled backfill and validation** | Backfill approved owner mappings, quarantine ambiguous rows, validate references and scope consistency. | B2; snapshot backup, read replica profile, signed mapping. | Counts reconcile and exception list approved. Rollback batch via recorded before-images/transaction log or restore rehearsal; do not delete unmatched rows. |
| **B4 Enforce ownership + uniqueness + audited admin** | Require owner; add composite unique ProductCode; enforce authorization throughout all surfaces; add explicit admin exception and audit. | B3 has no unexplained invalid rows; API/test matrix complete. | PostgreSQL migration/API tests and canary pass. Rollback via feature flag to deny sensitive operations, not global unscoped fallback; retain old schema/index until contract. |
| **B5 Contract and mobile catalogue enablement** | Remove transition paths; expose versioned scoped catalogue API for Flutter only after ERP enforcement stable. | B4, all consumers upgraded, operational metrics stable. | Full regressions and mobile contract tests pass. Rollback by disabling mobile sync/version endpoint; preserve ERP authoritative data and IDs. |

## Operational release and safeguards

- Run schema/migration rehearsals on a production-like PostgreSQL clone and measure lock/index duration before production scheduling.
- Take and verify backups; migration batch must be resumable, auditable, idempotent and bounded. Record mapping source, approver, old/new value and run identifier.
- Deploy additive schema before code requiring it. During transition, deny ambiguous/unowned products to ordinary tenant users; do not treat null as global.
- Canary by a selected Business with known mapping and multiple Outlets; monitor 403/404 by route, unowned-product blocks, index conflicts, query latency, audit events and inventory reconciliation.
- Coordinate POS and warehouse operation windows. Do not stop or rewrite sales, accounting postings, inventory ledger, stock counts or HR/Payroll processing as a shortcut. Use normal transactional deployment and verified backward-compatible schema.
- Keep `Product.Id`, `ProductVariant.Id`, sales/history references, ledger references and inventory identities stable. No cascade-delete or automatic clone/repoint.
- Rollback of API enforcement must never restore unscoped access: use a deny/disable gate or revert to a safe scoped release. Keep additive columns/mappings until all code and rollback windows are retired.
- Do not enable mobile catalogue sync or stock-count catalogue selection until B4 acceptance is met and the scoped API passes non-disclosure tests.

## Approval required before implementation

1. Security/Product owners approve immediate location containment semantics, including optional-location and multi-outlet requests, and whether A.3 aggregate endpoints join this release.
2. Product owner approves Business as the Product owner and defines treatment of globally shared/system products and ambiguous legacy products.
3. DBA/Product/Inventory approve canonical ProductCode rule and explicitly select global vs Business-scoped variant barcode and SKU uniqueness. Recommendation: retain global variant barcode and SKU constraints initially; ProductCode alone becomes unique per Business.
4. Product owner approves Category ownership and whether Variation/Option definitions are canonical shared reference data or Business-owned.
5. Security approves explicit Super Admin product-management permission, audit fields, and no-default-scope behavior.
6. DBA/Operations approve read-only profiling source, migration gates, backup/restore and rollback strategy.

**First implementation recommendation after approval:** Slice A1, a narrowly isolated server-side location authorization patch for stock-hint and stock-bearing variant searches with PostgreSQL-backed negative HTTP tests where practical. It reduces current stock disclosure risk without guessing legacy Product ownership or changing the product schema. A2 should follow promptly because the inventory filter endpoints show related location authorization gaps.
