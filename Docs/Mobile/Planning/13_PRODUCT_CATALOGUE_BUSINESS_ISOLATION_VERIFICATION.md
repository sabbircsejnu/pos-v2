# Product Catalogue Business Isolation Verification

**Review date:** 2026-10-14
**Scope:** Read-only static review of the current RetailPOS domain, EF model, API, Angular client and selected tests.
**Classification key:** **VERIFIED** = directly evidenced in current source or executed test; **PARTIALLY VERIFIED** = only part of the behavior is established; **NOT IMPLEMENTED** = required behavior is absent from the reviewed enforcement path; **UNKNOWN** = source does not establish the answer; **BLOCKED** = runtime/database evidence was not available.

## Executive result

The confirmed business rule—an independent product catalogue per Business shared by its Outlets—is **NOT IMPLEMENTED in the reviewed product ownership and API enforcement paths**. Product and variant rows have no Business key, product code and variant barcode uniqueness are global within their respective tables, and ordinary product reads/writes are not scoped through `TenantAccessService` or `UserOutletAccessService`. Thus the current implementation behaves like a global catalogue, not independently owned business catalogues.

The stock table does partition quantities by `(VariantId, LocationId, LocationType)`, but this does not prove that a product belongs to the Business that owns a location. A separate endpoint-level gap exposes stock hints for caller-supplied outlet IDs without checking outlet authorization.

No production database was accessed, no records were changed, and no migrations or application code were created or modified. Deployed schema/index parity and all live multi-business scenarios remain **UNVERIFIED**.

## 1. Business rules versus current implementation

| Confirmed requirement | Current evidence and result | Classification |
|---|---|---|
| Every Business owns an independent catalogue; its Outlets share it. | `Product`, `ProductVariant` and `Category` do not carry `BusinessId`; there is no product-to-business ownership relation in the reviewed model. No product-level outlet sharing relation was found. Ordinary product handlers do not resolve ownership via Outlet. | **NOT IMPLEMENTED** |
| Stock remains separate per Outlet/Location. | `Inventory` has `VariantId`, `LocationId`, and `LocationType`; the EF model defines uniqueness for this tuple. It has no `BusinessId`; there is no configured typed FK from the polymorphic location pair to Outlet/Warehouse. Quantity rows can be distinct by location, but product/business compatibility is not enforced. | **PARTIALLY VERIFIED** |
| Same `Product.ProductCode` can exist in different Businesses. | `ProductCode` is globally unique when present in the EF model; creation checks all products, not products in an authorized Business. | **NOT IMPLEMENTED** |
| `ProductCode`, `Product.Id` and `ProductVariant.Id` remain canonical identifiers. | `Product.ProductCode` is documented in the entity as the business-facing master identifier. Variant has required `ProductId`; IDs are `long`. | **VERIFIED** |
| Each sellable variant has a unique barcode. | Variant barcode has a unique index across the variant table. Product barcode is separately unique within the product table; there is no cross-table uniqueness constraint. Database case/normalization semantics are not a substitute for a canonicalization policy. | **PARTIALLY VERIFIED** |
| Business A cannot read or modify Business B products except via explicit authorized administration. | Product reads/writes are ID/global-catalogue based; product controllers use permissions but do not apply Business ownership checks. Product lookup is global. Super Admin/global authorization policy is not consistently distinguished from ordinary users at these handlers. | **NOT IMPLEMENTED** |
| Database constraints enforce business ownership, catalogue sharing and cross-business denial. | No product ownership column/relation or corresponding FK/index exists in reviewed model. The current keys enforce identifier uniqueness and parent/variant/location tuple relationships, not business boundaries. | **NOT IMPLEMENTED** |

## 2. Domain model, relationships and database constraints

### Ownership and identity

- **Product — VERIFIED:** [Product.cs](../../../src/RetailPOS.Domain/Entities/Product.cs#L3) defines `Id`, nullable `ProductCode`, `CategoryId`, status and navigation to variants/images/variations. There is no `BusinessId`, `TenantId`, or `OutletId`. Its `ProductCode` comment calls it the business-facing master product identifier and says it is unique when provided.
- **ProductVariant — VERIFIED:** [ProductVariant.cs](../../../src/RetailPOS.Domain/Entities/ProductVariant.cs#L3) carries required `ProductId`; the variant’s parent product is the authoritative identity path. It has no direct business/outlet owner.
- **Category — VERIFIED:** Category ownership is not modeled by a Business FK in the reviewed entity/model. `Product.CategoryId` is a product-category relationship, not evidence of Business ownership.
- **Product code uniqueness — VERIFIED in EF model, deployed DB UNKNOWN:** [RetailPOSDbContext.cs](../../../src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L539) configures a unique ProductCode index (and a separate product barcode index; see lines 539–585). It has no Business key, so uniqueness is global for non-null values. Product creation checks identifiers without a Business predicate in [ProductCreateStore.cs](../../../src/RetailPOS.Infrastructure/Products/ProductCreateStore.cs#L13). Product creation normalizes the code to uppercase; existing stored data and all write paths are not thereby proven canonical.
- **Variant barcode uniqueness — VERIFIED in EF model, deployed DB UNKNOWN:** [RetailPOSDbContext.cs](../../../src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L539) configures a unique variant barcode index independently from the Product barcode index. This prevents duplicate values in `ProductVariant.Barcode` (subject to nullable-index semantics) but does not prevent a Product barcode from equaling a ProductVariant barcode.
- **Variation/attribute relationship — PARTIALLY VERIFIED:** Product variation assignments, options, selected options and variant-option links are modeled separately. `ProductVariant.Attributes` also contains JSONB data. The reviewed configuration provides no invariant proving the relational option links and JSONB projection are complete and consistent.

### Inventory and FKs

- **Inventory — PARTIALLY VERIFIED:** [Inventory.cs](../../../src/RetailPOS.Domain/Entities/Inventory.cs#L3) stores per-location quantity and uses PostgreSQL `xmin` as an optimistic concurrency token. EF config in [RetailPOSDbContext.cs](../../../src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs#L795) configures a unique `(VariantId, LocationId, LocationType)` tuple and Variant FK. This supports separate stock rows for two locations for one variant; it does not establish same-Business ownership.
- **FKs — VERIFIED in EF model:** Product→Category, ProductVariant→Product and Inventory→ProductVariant relationships are configured. Inventory's `LocationId` and `LocationType` are a polymorphic location reference, not an ordinary FK to both Outlet and Warehouse. Application validation must enforce location type/existence and Business consistency.
- **Migrations/deployed schema — UNKNOWN/BLOCKED:** The checked-in EF model configuration is evidence of intended model constraints, not proof that all deployed databases have applied matching DDL. No safe read-only target database/schema was provided. The repository's available migration artifacts did not establish live migration history or current index definitions; verify those against a DBA-approved read replica before relying on constraints operationally.

## 3. API and authorization findings

### Product CRUD, list/search, variant and barcode paths

- **Product create — VERIFIED global behavior:** [ProductCreateStore.cs](../../../src/RetailPOS.Infrastructure/Products/ProductCreateStore.cs#L13) performs identifier checks without an effective Business argument/filter. Same product code in two businesses therefore collides with the global ProductCode index/check rather than creating two independent products.
- **Product read/list/search — NOT IMPLEMENTED business-scoped:** [ProductsController.cs](../../../src/RetailPOS.API/Controllers/ProductsController.cs#L104) and its product route actions apply permission policies, but the reviewed controller paths do not use tenant/outlet authorization for product ownership. [ProductReadStore.cs](../../../src/RetailPOS.Infrastructure/Products/ProductReadStore.cs#L124) filters identifiers/search/location hints rather than establishing product owner Business. Filtering by requested location is not a product ownership check.
- **Product update/delete — NOT IMPLEMENTED business-scoped:** [ProductUpdateStore.cs](../../../src/RetailPOS.Infrastructure/Products/ProductUpdateStore.cs#L10) and product delete paths operate by product ID without resolving and checking an owning Business. [ProductVariantMaintenanceStore.cs](../../../src/RetailPOS.Infrastructure/Products/ProductVariantMaintenanceStore.cs#L17) likewise reads/writes variants by `variantId`.
- **Variant/barcode lookup — NOT IMPLEMENTED business-scoped:** [ProductIdentifierLookupStore.cs](../../../src/RetailPOS.Infrastructure/Products/ProductIdentifierLookupStore.cs) is the identifier resolver; POS product lookup in [PosProductLookupStore.cs](../../../src/RetailPOS.Infrastructure/Pos/PosProductLookupStore.cs#L11) resolves barcode/SKU/variant ID globally. The POS lookup validates outlet context in part of its flow, but does not bind the product/variant to that outlet's Business.
- **Authorization services — VERIFIED as available, not universally integrated:** `TenantAccessService` in [TenantAccessService.cs](../../../src/RetailPOS.API/Services/TenantAccessService.cs#L18) offers scope checks; `UserOutletAccessService` provides outlet authorization. Permission policy registration in [Program.cs](../../../src/RetailPOS.API/Program.cs#L97) is not itself tenant filtering. Reviewed product handlers/controllers do not apply those services as product-ownership authorization.
- **Direct foreign IDs — NOT IMPLEMENTED at product boundary:** A caller with the relevant product permission can supply a ProductId or VariantId. The reviewed product update/variant maintenance stores use IDs without proving ownership against caller Business. Effective exploitability still depends on the caller's granted permissions; permission checks do not satisfy the Business boundary.
- **Super Admin — PARTIALLY VERIFIED:** Product endpoints appear globally unfiltered, so a Super Admin can operate across catalogue rows. The same lack of filtering also applies to ordinary permitted users; the code does not demonstrate that global product access is an explicit Super Admin-only exception. Actual role/claim runtime behavior is **UNVERIFIED**.

### Stock-level disclosure

**VERIFIED static authorization gap; runtime UNVERIFIED.** `POST /api/pos/stock-hint` accepts caller-supplied `variantId` and `outletId`; [PosController.cs](../../../src/RetailPOS.API/Controllers/PosController.cs#L292) forwards them without the outlet authorization used by another lookup path. [GetPosStockHintQuery.cs](../../../src/RetailPOS.Application/Pos/Queries/GetPosStockHintQuery.cs#L11) reads availability for those supplied IDs, and [PosProductLookupStore.cs](../../../src/RetailPOS.Infrastructure/Pos/PosProductLookupStore.cs#L103) filters by variant/location but not caller access. In addition, variant search accepts `locationId`/`locationType` and returns location stock in `ProductReadStore`; the product controller does not establish authorized access to the requested location.

This is a potential cross-outlet—and, where outlet IDs belong to another Business, cross-business—stock disclosure for authenticated users with applicable product/sales permissions. A malicious caller must know or guess valid identifiers; the endpoint does not need to reveal product names to disclose stock/sufficiency information.

## 4. Angular behavior

- **Product requests — VERIFIED client behavior:** [product.service.ts](../../../retailpos-frontend/src/app/services/product.service.ts#L159) calls product endpoints without a Business ID in the product request contract.
- **Business context header — VERIFIED client behavior:** [auth.interceptor.ts](../../../retailpos-frontend/src/app/interceptors/auth.interceptor.ts#L63) adds `X-Business-Id` for Super Admin requests. The reviewed product API handlers do not consume that header to scope product queries/writes. For ordinary users the header is not the enforcement mechanism.
- **Product Management and POS — PARTIALLY VERIFIED:** Angular product/POS clients invoke generic product and lookup endpoints; any visible Business/outlet selector or current-context UI cannot enforce isolation against direct API calls. The backend behavior remains global as described above.
- **Inventory UI — PARTIALLY VERIFIED:** Inventory workflows can request/display location-specific inventory, but UI selection is not backend authorization. Product-to-Business association cannot be inferred from an inventory row.
- **Every Outlet shares same catalogue — UNKNOWN as intentional behavior:** The global product APIs currently make the same products addressable from different outlets, but no owner policy or tested business relation proves that this sharing is authorized for all same-Business outlets and forbidden across Businesses.

**Conclusion:** Angular behavior is not sufficient evidence of isolation. Server-side filtering and authorization must enforce any Business/outlet rules.

## 5. Automated tests and execution

### Existing coverage found

The repository has relevant characterization tests:

- [ProductCreateStoreRelationalTests.cs](../../../tests/RetailPOS.IntegrationTests/Products/ProductCreateStoreRelationalTests.cs) tests product-plus-variant persistence and rollback on duplicate variant identifiers using SQLite.
- [ProductVariantSearchHttpTests.cs](../../../tests/RetailPOS.IntegrationTests/Products/ProductVariantSearchHttpTests.cs) tests variation/option HTTP projections using the in-memory test API factory.
- [UserOutletAccessBehaviorTests.cs](../../../tests/RetailPOS.IntegrationTests/Identity/UserOutletAccessBehaviorTests.cs) tests outlet-access policy behavior, including Super Admin's global outlet access. It does not test product ownership.
- [UserOutletAccessHttpTests.cs](../../../tests/RetailPOS.IntegrationTests/Identity/UserOutletAccessHttpTests.cs) tests the authorized-outlets API contract with an in-memory database.
- [PosLookupHttpTests.cs](../../../tests/RetailPOS.IntegrationTests/Sales/PosLookupHttpTests.cs) covers POS lookup behavior; it does not establish the required Business-owned catalogue policy.

No existing test located verifies the complete product isolation matrix: same ProductCode in distinct Businesses, shared ProductId between same-Business outlets, denied cross-Business ProductId/VariantId reads and writes, scoped barcode resolution, or explicitly authorized Super Admin product administration.

### Actual execution

Executed from repository root:

```powershell
dotnet test "tests\RetailPOS.IntegrationTests\RetailPOS.IntegrationTests.csproj" --filter "FullyQualifiedName~ProductCreateStoreRelationalTests|FullyQualifiedName~ProductVariantSearchHttpTests|FullyQualifiedName~UserOutletAccessBehaviorTests|FullyQualifiedName~UserOutletAccessHttpTests|FullyQualifiedName~PosLookupHttpTests" --no-restore --verbosity minimal
```

**Result:** Passed, 56 tests; failed 0; skipped 0; .NET SDK `10.0.401`, target `net10.0`. These are existing characterization tests using SQLite/in-memory test stores; they are not PostgreSQL constraint tests and do not prove catalogue Business isolation. Restore was not requested (`--no-restore`). Build emitted pre-existing NuGet audit warnings for `SixLabors.ImageSharp` 3.1.12 (multiple advisories, moderate/high); package remediation is outside this verification-only scope.

**PostgreSQL isolation scenarios:** **BLOCKED/UNVERIFIED.** A disposable PostgreSQL test database helper exists, but no verification was made that its required PostgreSQL service/configuration is available and isolated for this run. No tests were run against a production or shared database.

### Scenario status

| Scenario | Static implementation result | Executed outcome |
|---|---|---|
| A — Business A creates `ABA100P` | Product creation accepts master ProductCode and uppercases it. | **UNVERIFIED** as a Business-scoped request. Existing create characterization passed, but no Business ownership exists in the path. |
| B — Business B creates its own `ABA100P` | Global existence check and unique ProductCode index reject a duplicate code, regardless of Business. | **UNVERIFIED** at PostgreSQL/API runtime; desired two-catalogue behavior is not supported by reviewed source. |
| C — A1/A2 share catalogue, separate stock | Same global Product/Variant identifiers are addressable; distinct inventory rows can exist per location tuple. Product-to-Business consistency is absent. | **PARTIALLY VERIFIED** statically; no two-outlet/business end-to-end test executed. |
| D — Business B reads, modifies or scans A ProductId/VariantId | Product and variant stores/lookups do not enforce owner Business. Permission policies remain required but do not bind owner. | **UNVERIFIED** by runtime test; static code indicates cross-Business operation is not denied by product ownership enforcement. Stock-hint endpoint additionally lacks outlet authorization. |
| E — Super Admin access | Product endpoints are globally unfiltered; outlet authorization has an explicit global Super Admin behavior in its own tests, but that does not demonstrate an explicit product exception. | **PARTIALLY VERIFIED** statically; no Super Admin product API scenario executed. |

## 6. Security and leakage assessment

1. **HIGH potential risk — cross-Business product access/write:** Confirmed business isolation requires denial, but product handlers/stores do not associate the object with the caller's Business. Users possessing product permissions may read or modify records by ID outside their Business. This is a direct authorization-design gap. Runtime reachability under actual production roles is **UNVERIFIED**.
2. **MEDIUM — stock hint/availability disclosure:** Supplied outlet/variant IDs reach a location stock query without outlet access validation. This can disclose foreign outlet stock/sufficiency to an authenticated caller with the endpoint's required permission.
3. **MEDIUM integrity/availability impact — global code uniqueness:** A Business cannot register its independent product using a ProductCode already used by another Business. This is a functional tenant-collision and data-model limitation, even where it is not itself confidentiality leakage.
4. **Ambiguous barcode namespace:** Product and variant tables have separate uniqueness indexes; a scan may resolve ambiguously if a parent Product barcode equals a sellable variant barcode unless resolution explicitly rejects the ambiguity.
5. **Defense-in-depth gap:** No reviewed global EF tenant query filter or universal product authorization hook ensures new endpoints will remain scoped by default. Permission authorization is not data ownership authorization.

These findings are based on code/schema inspection. They are not a claim that a live request was successfully used to read or change another Business's record.

## 7. Business isolation support conclusion

**The confirmed requirements are not currently supported as an enforceable Business-owned catalogue.**

Verified implementation properties:

- ProductCode is the existing Main Product Code semantic; Product ID and Variant ID remain the ERP identifiers.
- Product→Variant relation exists and inventory can have independent location rows.
- ProductCode and variant barcode are globally unique in their respective EF model indexes.
- Current generic product and POS lookup paths behave globally and do not derive product ownership from the selected outlet.
- An explicit business ownership/eligibility mechanism for Product is not present in the reviewed path.

Unknown properties:

- The actual deployed database index/FK state and migration history.
- Production data ownership, orphan/collision counts, and whether the code has a separate business catalogue policy outside the inspected paths.
- Whether production role assignment makes the identified endpoints reachable to a typical Business user.
- Whether existing products were historically intended to be global/shared.

## 8. Minimal-risk architectural options

Do not immediately add a nullable `Product.BusinessId`, backfill heuristically, or rely on Angular filtering. First choose and profile an ownership model.

1. **Preferred candidate if ownership is one-Business-per-product:** Add a required Product→Business ownership relation after explicit resolution of existing records. Same-Business outlets use the same Product/Variant IDs; inventory stays location-partitioned. Scope every product query, direct-ID operation, barcode resolution, image/media access and nested variation/variant operation by authorized Business derived server-side from the selected outlet/user. Make ProductCode uniqueness Business-scoped. Enforce outlet/location Business equality where applicable.
2. **Candidate if explicit product sharing is a real requirement:** Use a catalogue ownership/share relation that allows a Business-owned product to be shared under audited, explicit policy. Avoid treating null owner as implicitly global. Scope all reads and writes through the relation.
3. **Not recommended:** Keep global catalogue and use UI filters. This cannot meet the confirmed requirement and offers no server-side guarantee.

Before choosing, Inventory/Product owners and Security must determine ownership for existing product records. A reviewed one-time mapping may need explicit business-owner input; category, inventory, sales, or current outlet usage alone must not be treated as authoritative ownership without approval.

## 9. Migration and historical-record impact

Any ownership implementation is a coordinated data migration, not a mobile-only schema change:

- **Products:** Add the chosen owner/share relation and resolve every existing Product to an authorized Business (or approved explicit shared scope). Preserve Product.Id and ProductCode. Rows with ambiguous ownership must be quarantined for owner review; do not infer or drop them.
- **ProductVariants:** Preserve Variant.Id and ProductId. Ownership should derive from the parent Product (or explicit catalogue-sharing relation); add consistency constraints/validation as selected.
- **Categories/variations/options:** Determine whether these are global definitions or Business-owned catalogue data. Product-category and option relations need to remain valid and must not widen product access.
- **Sales/history:** Sales lines typically reference Product/Variant identifiers. Preserve identifiers and historical display/snapshot fields; do not cascade-delete products or rewrite historical transactions. Confirm all reporting and export paths remain scoped without hiding legitimate historical transactions.
- **Inventory/stock adjustments/counts/transfers:** Preserve Inventory/stock-operation rows and location-specific quantities. Verify every location belongs to the Product's owning Business, resolving orphan/mismatched rows through approved remediation. Keep audit history and immutable stock-count references. Do not merge per-location stock when products are re-owned or split.
- **Global unique indexes:** Replace the global ProductCode constraint with the approved per-Business rule only after duplicate and normalization profiling. Variant barcode policy may be per Business or globally unique; owner/security approval must precede index design. Check product-vs-variant cross-table conflicts because separate indexes cannot enforce cross-table uniqueness.
- **Rollout safety:** Profile; back up; define owner mapping and exception reports; deploy additive nullable ownership/schema and dual validation if needed; backfill in a controlled, auditable migration; validate counts/references; add not-null/FKs/unique indexes; enable scoped API enforcement; then remove transitional behavior. Define rollback before cutover. Exact ordering depends on selected ownership model.

All impact estimates are **UNKNOWN** until read-only profiling reports row counts, duplicate codes/barcodes, missing identifiers, orphaned references, and location/business mismatches.

## 10. Recommended ADR-CAT-01 resolution

**Do not approve the earlier fail-closed mobile-only formulation as a complete ownership decision.** It protects a new mobile sync surface but leaves the existing generic ERP product APIs globally addressable, and “no BusinessId” is not proof of an intentional global ownership model.

Recommended ADR text:

> Product Catalogue is Business-owned. All authorized Outlets of that Business reference the same Product and ProductVariant identities; inventory remains separately owned by authorized Outlet/Warehouse locations. Product ownership is resolved and enforced server-side on all product reads, writes, search, media and identifier lookup. Cross-Business access is denied except through a separately permissioned, audited administrative workflow. ProductCode may repeat in different Businesses and is unique within the approved Business catalogue. Existing records without a verified owner remain unavailable to ordinary Business contexts until mapped or explicitly classified. Preserve ProductId/VariantId and historical transaction references. Product ownership source, shared-catalogue exception model, category ownership, barcode namespace and migration mapping require explicit owner/security/DBA approval before implementation.

**Status:** **PROPOSED — requires explicit Product owner, Security, ERP/API and DBA approval.** Evidence supports the need for server-enforced isolation; source does not prove current ownership or safe backfill.

## 11. Blockers and next safe implementation slice

### Explicit blockers

- Product owner must approve one-Business ownership versus an explicit audited sharing relation and define category/variation ownership.
- Security must approve role and Super Admin exceptions and require server-side Business/outlet checks on all object-ID and barcode paths.
- DBA/data owner must provide an approved read-only production replica/account (or certified representative snapshot) and run aggregate profiling. No live profiles/counts are available in this review.
- API/DB owners must reconcile EF model and migrations with deployed constraints and plan historical-record-safe rollout.
- Inventory owner must approve Business/location consistency rules and the handling of existing mismatches.
- The stock-hint and variant-search location authorization gaps require a separately scoped remediation decision; this verification report does not change them.

### Next safe implementation slice

1. Approve ADR-CAT-01 and the ownership mapping rules.
2. Run **read-only** aggregate data profiling on an approved replica: null/blank/duplicate ProductCodes; normalized duplicates; duplicate product and variant barcodes; cross-table barcode collisions; orphan ProductVariant/Product/category/option/inventory links; and product-use versus outlet/business ownership candidate counts.
3. Specify API authorization tests for product list/search/get/update/delete, variant maintenance, barcode/SKU/ID lookup, images, POS lookup and stock hint. Include ordinary users across Business A/B, two authorized same-Business outlets, unauthorized foreign outlet IDs and explicit Super Admin workflows.
4. Only after profile and approvals, prepare the additive schema/API rollout plan and tests. No migration or runtime implementation is approved by this verification task.

The Flutter mobile catalogue and stock-count sync should remain blocked on the approved server-side ownership policy and scoped catalogue API contract. The rest of mobile work that does not expose catalogue data can proceed only under its previously approved P1 auth/security contracts.
