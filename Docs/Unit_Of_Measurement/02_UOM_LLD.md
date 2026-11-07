# IraniMart POS v2 — Unit of Measurement (UOM), Packaging & Repacking
## Low-Level Design (LLD)

**Version:** 1.0 — implementation design for approved D01–D12  
**Date:** 2026-11-06  
**Status:** Proposed technical design; source-code audit and migration verification required  
**Companion:** `01_UOM_SRS.md`  
**Suggested repository path:** `Docs/Unit_Of_Measurement/02_UOM_LLD.md`

> **Implementation authority:** SRS D01–D12 and existing approved POS/Finance business rules take precedence. This LLD is a proposed mapping into the known .NET 10 modular monolith, Angular and PostgreSQL architecture; actual classes/columns/routes MUST be discovered in the source before implementation. SQL names and endpoints below are illustrative contracts, **not assertions that these exist**. No D13 batch/expiry/FEFO.

## 1. Architectural context and principles

Known project direction: .NET 10 modular monolith / Clean Architecture, MediatR, FluentValidation, EF Core/Npgsql/PostgreSQL, Angular frontend, ProductVariant-centred inventory, existing accounting stock valuation/cost correction. Retain CQRS patterns and application/infrastructure boundaries. Do not add new microservices or Elasticsearch to deliver UOM.

**Primary design principles:**
1. A ProductVariant always owns **one canonical base UOM**; quantity on authoritative inventory balance/ledger is denominated in that unit.
2. UOM conversion/pricing configuration is **versioned** and every committed transactional movement keeps the exact interpreted snapshot.
3. Unit-aware entry is a presentation/input convenience; all stock accounting takes **base quantity**.
4. Packaging materials are genuine separately valued ProductVariants; repacking reclassifies value, it must not create total inventory value from nothing.
5. Pack-state (loose/sealed/prepared) is a **physical allocation subledger**, not an independent valuation source; totals reconcile to the authoritative inventory.
6. Existing `pcs` values remain `pcs` with factor one, without history rewrites.
7. Every state mutation passes business/location authorization, idempotency and concurrency checks. Server never trusts price, conversion, computed base quantity or cost from UI.

## 2. Read-only discovery gate (mandatory before code)

Inspect actual entities, EF mappings and database types for: Product/ProductVariant/SKU; UOM fields if any; PO/POItem; GRN/GRNItem; Inventory balances and StockLedger; Sale/SaleItem/SaleJournalSnapshot; returns/refunds/exchanges; transfer/requisition/dispatch/receipt; count/adjustment; package/barcode flows; stock-cost writer; weighted-average/reconciliation/GL journal posting; outlet/warehouse assignments; cash sessions; dashboard read stores; Flutter/offline sale schema if implemented; permission catalog; testing conventions.

Record in `Docs/Unit_Of_Measurement/UOM_IMPLEMENTATION_GAP_ANALYSIS.md`:
- Actual paths and column types: `integer`, `numeric(p,s)`, rounding conventions; cross-module quantity API contracts.
- All readers/writers that assume `pcs`/integer (including exports, invoices, migration tooling).
- Existing transactional consistency, concurrency policy (`xmin`/row locks), idempotency keys and GL posting lifecycle.
- Whether reusable unit definitions/package settings exist; existing SKU, barcode, price/version patterns.
- Features that cannot yet support damage/quarantine, reverse/refund accounting or value reclassification. Surface blockers, don't invent finance rules.

The following sections provide a **target model to adapt**, not instructions to replace an existing compatible pattern.

## 3. Logical component architecture

```text
Angular UI
 ├─ UOM Catalog / Product Unit Config
 ├─ PO/GRN Unit Entry / Pack-state confirmation
 ├─ POS Pack Selector / Custom Quantity
 ├─ Transfer / Count / Adjustment Unit Entry
 ├─ Open/Unpack / Repack Forms
 └─ Inventory Intelligence Unit-aware Read Models
          │ HTTPS (user inputs; server-authoritative evaluation)
.NET Application (MediatR + FluentValidation)
 ├─ UomCatalogQueries / UomConfigCommands
 ├─ IUnitConversionService (effective-version resolver)
 ├─ IUnitQuantityPolicy (precision, representability)
 ├─ IPriceResolver (fixed/custom/discount/VAT)
 ├─ Purchasing / POS / Inventory existing command pipelines
 ├─ IPackStateService / IRepackingService
 ├─ IInventoryCostingPort (existing authoritative valuation)
 └─ UOM-aware Dashboard Read Queries
          │
Infrastructure (EF/Npgsql + existing journal/ledger services)
 ├─ Versioned UOM/config records
 ├─ Existing Inventory/StockLedger/GL writers
 ├─ Pack-state allocation/movement records
 └─ Repacking component/output and audit records
```

Keep conversion/calculation pure where possible; mutation orchestration belongs to existing posting transaction boundaries. Reusing the existing GRN/transfer/COGS pricing source is mandatory.

## 4. Proposed data model (conceptual)

### 4.1 Master definitions

| Entity/table (proposed) | Key fields | Invariants |
|---|---|---|
| `uom_units` | Id, BusinessId nullable if global seed, Code, Name, Symbol, Dimension, QuantityPrecision, IsWholeUnit, IsActive, ConcurrencyToken | Unique scope+Code, immutable used dimension, precision changes safety-checked |
| `uom_standard_conversions` | Id, FromUnitId, ToUnitId, Numerator, Denominator, Version/EffectiveFrom, IsActive | Same dimension; positive exact rational; no conflicting paths/cycles |
| `variant_uom_configs` | Id, BusinessId, ProductVariantId, BaseUnitId, TrackingMode, EffectiveFrom/To, Version, IsActive | Base unit locked after first transaction; one effective config |
| `variant_purchase_unit_options` | Id, ConfigVersionId, PurchaseUnitId, Label, FactorNumerator/Denominator, IsDefault, IsActive | Conversion exactly representable in base precision; one default |
| `variant_selling_pack_options` | Id, ConfigVersionId, PackCode, Label, BaseQty, SellingPrice, IsDefault, IsActive, IsPreparedEligible | Unique effective code; positive base qty, price >= 0 |
| `variant_custom_sale_options` | Id, ConfigVersionId, UnitId, Factor, PerUnitPrice, Allowed, IsDefault | Allowed quantity precision and cost/price rules |
| `selling_pack_components` | Id, SellingPackOptionVersionId, PackagingVariantId, QuantityBase, IsRequired | Same business, no self-reference/cycle |

**Alternative:** Combine product config versions and selling/purchase options into a versioned aggregate if repository architecture prefers it. Do not make an unrelated master Product table just for UOM. Scope global UOM definitions centrally if supported; all variant/package links still business isolated.

### 4.2 Immutable transaction-line snapshots

Prefer adding a shared **embedded snapshot / owned value object** to existing line structures rather than an arbitrary polymorphic record if code conventions allow it:

```text
UnitTransactionSnapshot
  UnitCode / UnitId
  EnteredQuantity                 decimal
  FactorNumerator, Denominator   positive decimal-safe rational
  BaseUnitCode / BaseUnitId
  BaseQuantity                    decimal
  UomConfigVersionId
  PurchaseOptionId / SellingPackOptionId? 
  PricePerSelectedUnit / UnitPriceSource? 
  Tax/Discount/Cost snapshot fields from original module
```

For legacy records, read-only compatibility adapter supplies `pcs`, factor 1. **Do not update historical rows.** Persist snapshot on newly committed PO/GRN/sale/return/transfer/adjustment/repack lines at the correct existing boundary.

### 4.3 Physical pack-state and repacking

| Entity/table (proposed) | Main fields | Role |
|---|---|---|
| `pack_state_balances` | BusinessId, LocationType/Id, VariantId, State (`Loose`,`Sealed`,`Prepared`, optionally `Quarantine`), PackOptionVersionId nullable, BaseQty, PackCount nullable, RowVersion | Physical allocations; sum reconciles with approved inventory balance meaning |
| `pack_state_movements` | Id, BusinessId, Variant, Location, OperationType, StateFrom/To, BaseQty, PackCount, SourceDocument, IdempotencyKey, Actor, Date | Immutable audited state transitions; no separate GL amount |
| `repack_jobs` | Id, BusinessId, Location, Reference, Status, SourceVariantId, OutputPackOptionVersionId, OutputCount, Created/ExecutedBy, Dates, ConcurrencyToken | Operation lifecycle, approved under existing authorization design |
| `repack_component_lines` | JobId, ComponentVariantId, QtyBase, CostAmount, CostEvidence, InventoryMovementId | Attar component + packaging consumed once |
| `repack_output_lines` | JobId, VariantId, PreparedPackVersionId, PackCount, BaseContentQty, AllocatedValue, InventoryMovementId | Finished content/pack-state allocation and value reconciliation |

### 4.4 Pack-value modelling warning — mandatory design resolution

**Do not assign packaging value twice.** There are two safe implementation patterns:

- **Pattern P1 (preferred if existing inventory ledger supports value-only adjustments):** Attar variant's total content quantity remains the same; track prepared vs loose physical allocations and a *capitalized packaging-value component* in an auditable value subaccount/read model. Packaging variant's stock/value decreases; combined Attar+packaging stock valuation stays constant, now attributed to prepared output. Sale COGS consumes content cost plus its allocated packaging cost.
- **Pattern P2 (if ledger requires every valued output to be a separate inventory variant):** Introduce a distinct finished prepared-pack SKU/variant whose content represents `N × baseQty` and source loose stock is **reduced** by that base qty; finished pack contains that quantity, not a second copy. Packaging is consumed into finished pack cost. Reconcile logical product aggregates without double-counting. This conflicts with the simpler D12 narrative of unchanged *Attar variant* quantity; preserve unchanged **business-wide Attar content** and obtain owner review if the model would expose a new sellable SKU.

Choose a feasible pattern **only after accounting/stock-ledger audit** and prove conservation across inventory GL and sales. Do not use both patterns simultaneously. `Prepared` physical pack state alone cannot magically capitalize packaging cost in an ordinary variant weighted-average ledger without an explicit value model.

## 5. PostgreSQL precision and migrations

Use `numeric(p,s)` with sufficient headroom; suggested target for *base stock quantity* is `numeric(24,6)` subject to the actual system's value ranges. Unit-level validation (0–4 fractional digits proposed) remains stricter than DB storage. Conversion factors may require rational numerator/denominator or higher precision; do not use `double` or floating SQL arithmetic. Cost per base unit requires more precision than monetary postings, e.g. `numeric(24,8)` after verification; currency posted to accounting at existing 2-decimal currency precision, with existing rounding mode.

**Schema migration strategy:**
1. Inventory balance, StockLedger, PO/GRN/Sale and transfer quantity columns must be audited. A decimal column only on UOM tables is inadequate if stock ledger still stores `int`.
2. Safely widen required quantity columns, associated EF properties, DTOs, command validators, constraints, indexes, SQL aggregates and report views. Include `quantity` in audit of cache/read models and offline sync contracts.
3. Widen by additive/compatible migration with cast/backward-compatible deployments where possible. No historical quantity conversion: old integer `100` remains numeric `100.000000 pcs` semantically.
4. `IsActive`, effective-date history, packaging links and optional physical allocation records are additive. Seed `pcs` and standard units idempotently.
5. Use verified migration scripts on isolated Test and Development; do not run on persistent DBs without approval. Backfill of user data is **not authorized**. Optional metadata defaults do not rewrite stock history.

## 6. Conversion and quantity evaluation engine

### 6.1 Interfaces (illustrative)

```csharp
public record UnitQuantityInput(Guid VariantId, Guid UnitOptionId,
    decimal EnteredQuantity, Guid? ExpectedConfigVersionId);
public record UnitQuantityResult(decimal EnteredQuantity, string EnteredUnit,
    decimal FactorToBase, decimal BaseQuantity, string BaseUnit,
    Guid AppliedVersionId);

public interface IUnitConversionService {
    Task<UnitQuantityResult> ConvertAsync(UnitQuantityInput input,
        DateOnly effectiveBusinessDate, CancellationToken ct);
}
public interface IUnitQuantityPolicy {
    void EnsurePrecision(decimal entered, int enteredUnitPrecision);
    void EnsureExactlyRepresentable(decimal baseQty, int baseUnitPrecision);
    void EnsurePositive(decimal qty);
}
```

**Algorithm:** Load effective version for business+variant+date; validate active option and unit dimension; resolve factor; multiply using decimal or exact rational; ensure quantity allowed and base representation exact; return snapshot ready for existing transaction command. Reject mismatched supplied version where draft revalidation is needed.

### 6.2 Examples

```text
Purchase: 2 Jar × (500 ml/Jar) = 1,000 ml
Sale: 2 × 12 ml fixed pack = 24 ml; price 2 × ৳100 = ৳200
Custom: 75 ml × ৳5/ml = ৳375
Count: 2 sealed jars × 500 + 175 loose = 1,175 ml
Invalid: 12.5678 ml with precision 2 → validation error (no rounding)
Invalid: 1.5 sealed jars with whole-only Jar → validation error
```

### 6.3 Safety around version changes

Effective versions may not overlap on same unit/pack key. Keep original factor and pricing snapshots for committed transactions. Drafts preserve displayed original configuration but are revalidated at submission/GRN completion/checkout; if stale, return a structured `UOM_CONFIG_CHANGED` conflict for UI review. Backdated posting must use the approved business-date authority, not caller-chosen arbitrary effective dates.

## 7. Selling price resolution

```text
IF fixed selling pack:
  baseQty = numberOfPacks * packBaseQty(version)
  grossLinePriceBeforeDiscount = numberOfPacks * packPrice(version)
ELSE custom quantity:
  baseQty = Convert(inputQty, selectedCustomUnit)
  grossLinePriceBeforeDiscount = inputQty * configuredPerUnitPrice(version)
THEN apply existing sale discount/VAT and rounding rules server-side.
```

Existing POS rules remain: Selling Price authoritative, product-wise vs overall discount mutually exclusive, VAT server-calculated, permission-gated price overrides if present. Configure pack prices independently; do not infer fixed-pack price as per-ml price × size. The line's quantity, UOM, pack identity/version, VAT and price snapshot must appear on invoice/return records.

## 8. Purchase/GRN command integration

**Flow:** select variant and purchase option → capture PO unit snapshot → receive against approved PO snapshots → confirm physical pack state where enabled → validate accepted/rejected/short quantities → existing GRN cost/Journals and stock writer → pack-state write in same transaction → publish existing outbox event if applicable.

**Receiving example:** PO 3 × 500 ml jars = 1500 ml. Receiver confirms 2 sealed jars + 300 ml loose accepted + 200 ml damaged/quarantine = 1500 ml physically received; sellable accepted = 1300 ml. If existing inventory system has no quarantine bucket, block this split and require existing supplier rejection/adjustment policy; do not quietly put 200 ml into available stock.

**Posting algorithm** (pseudocode):
```text
BEGIN transaction (existing accounting/stock isolation)
  Validate permissions, business/location, PO balance, config snapshot
  Validate qty breakdown and pack counts
  Resolve actual accepted acquisition cost from approved GRN journal policy
  Existing GRN writer posts base stock and source cost reliably once
  Insert pack-state allocation/movements for accepted base qty
  Existing GL journal posts corresponding amount once
  Verify stock/pack-state & journal reconciliation invariants
  Commit and publish through existing outbox as appropriate
END
```

Missing cost must not become reliable zero by default. Handle legitimately evidenced zero acquisition separately per approved cost reliability rules.

## 9. POS checkout transaction integration

Front end sends `{variantId, optionVersionId, enteredQuantity, selectedUnitId, optionalPackagingOptionId}`; avoid client-supplied authoritative `baseQuantity`, `conversionFactor`, `unitPrice`, `COGS`. For fixed pack, quantity represents **pack count**; for custom, quantity represents measured selected unit. The server re-evaluates and retains snapshots.

**Atomic completion:** validate session/location and stock; lock relevant content, prepared-pack state and packaging inventory rows; resolve exact sale form; compute base qty and authoritative price; consume either loose + packaging OR prepared pack contents (never both); post sale, COGS, inventory ledger and packaging consumption using existing journal system; commit once. Hold/Recall must not create a duplicate deduction. Offline sales must follow existing offline restrictions and conflict/stock rules rather than bypassing server validation.

`InsufficientLooseStock`, `InsufficientPreparedPack`, `InsufficientPackagingStock`, `InvalidPackQty`, `StaleUomConfiguration` need stable application error codes and user-friendly Angular messages.

## 10. Pack-state operations and reconciliation

### 10.1 Core invariants

For each business+location+variant, when pack tracking is ON:

```text
Authoritative base quantity under the stock model
  = LooseBaseQty
    + Sum(SealedPackCount * BaseQtyPerSealedPackSnapshot)
    + Sum(PreparedPackCount * BaseQtyPerPreparedPackSnapshot)
    + tracked non-sellable allocations, if the inventory model includes them
```

When existing `Inventory.Quantity` means available rather than on-hand, reconcile only available allocations, while non-sellable stock resides in an explicit quarantine bucket. These semantics must be verified before implementation.

### 10.2 Open/unpack

```text
Input: location, variant, sealed pack config/version, count N
Preconditions: correct location rights; sealed count >= N; pack config version known
Effects: sealed count -= N; looseBaseQty += N * packBaseQty
        authoritative Inventory.Quantity unchanged
        Attar StockLedger value unchanged
        pack_state_movements event emitted once
```

Do not produce saleable empty containers automatically. If physical leakage occurs, record an approved stock adjustment/write-off as a separate event.

### 10.3 Transfers

Transfers must preserve original physical pack form when moving sealed/prepared packs; moving loose stock uses base qty. Existing Draft/Submit/Approve/Dispatch/Receive logic remains; reserve/lock source state at the correct existing transfer stage; preserve source cost per base qty at dispatch; receive same amount/state with no duplication. Do not assume physically sealed availability merely from total base stock.

## 11. Repacking implementation

### 11.1 Operation states

Proposed lifecycle: `Draft → ReadyForReview → Executed` or `Cancelled` before execution. Additional approval status only if required by existing stock-adjustment authorization convention. Generate immutable reference and idempotency key. Repacking is **not** production manufacturing.

### 11.2 Validate before execution

- Source product variant, base UOM and selling-pack version valid.
- Loose stock available for `OutputCount × PackContentBaseQty + explicitly approved wastage`.
- Required empty packaging/component variants and quantities available at **same location**.
- All count-based pack/material quantities whole; measured inputs precise; costs reliable or output valuation explicitly partial/blocked under existing policy.
- No cyclic component mappings, duplicate consumption, or cross-business links.

### 11.3 Cost conservation example

| Component | Quantity | Cost | Amount |
|---|---:|---:|---:|
| Attar content | 240 ml | ৳2/ml | ৳480 |
| Empty 12 ml bottles | 20 pcs | ৳8/pc | ৳160 |
| Finished 12 ml prepared bottles | 20 units | ৳32/each | **৳640** |

Pre-operation total relevant inventory value = Attar inventory value plus empty-bottle inventory value. Post-operation value = remaining loose Attar value plus prepared-pack value plus remaining empty bottles value. **Total value is conserved** absent separately authorized loss/expense/capitalization. Both material and cost movements need journal-consistent entries under the chosen pattern P1/P2 from §4.4.

### 11.4 Execute safely

```text
BEGIN transaction
  Authorize source location and repacking action
  Acquire locks for source loose allocation, packaging balances and output
  Resolve effective option versions and compute required content/packaging
  Validate precision, availability, costing and idempotency
  Consume packaging and reclassify its value
  Move Attar content from loose to prepared (or into finished SKU under P2)
  Update pack-state, inventory ledger/cost allocation and GL consistently
  Save immutable snapshots + audit + outbox
  Check quantity/value invariants
COMMIT
```

**No direct manipulation of aggregate `Inventory.Quantity` by a UI form.** If accounting cannot represent a value-only reclassification safely, treat repacking implementation as **blocked** pending an approved posting design rather than faking weighted-average cost.

## 12. Returns/exchanges and cost restoration

Use original `SaleItem` quantity UOM and pack/price/cost snapshots. Enforce cumulative return limit in **base units** and existing return/refund authorization. Condition inputs select actual restock eligibility for content and reusable packaging. If 12 ml was sold and 7 ml physically returned, never restore 12 ml. Original refunded amount/discount/VAT may differ from physically restocked value; apply approved return/expense and write-off accounting. If unavailable, return a policy-required state rather than post fictional stock or GL.

Prepared-pack return must either re-enter prepared stock only when unopened/eligible, or re-enter measured loose inventory after a documented unpack/condition operation; packaging consumption reversal is never automatic solely because payment is refunded. Preserve original-to-replacement exchange linkage.

## 13. Unit-aware count, adjustment and stock transfer

### Count
Capture multiple physical rows (`2 × Jar500`, `175 × ml`) per variant/location; compute a **single canonical base counted qty** and compare to authoritative stock snapshot. Keep individual counting source entries for audit. Existing stock-count status, approvals, concurrency and correction/writer rules remain.

### Adjustment
Input unit+qty+reason; backend computes base variance. Reject negative stock, unsupported loss/write-off accounting and unit precision violations. Physical state classification moves separately when quantity unchanged.

### Transfer
Store selected source unit and snapshot plus base moved; authorized source/destination, stage-specific stock, pack state and cost validated by existing transfer write rules. In-transit stock included once in total owned inventory only if consistent with current inventory/GL reporting definitions.

## 14. Inventory valuation and dashboards

- Monetary value across units is summable: `Σ reliableBaseQty × authoritativeBaseUnitCost` plus separately recognized prepared-pack packaging capitalization once.
- Quantity across units **is not** summable into a `pcs` KPI. Suggested overview: per-UOM breakdown and unique stocked variants/products; retain monetary aggregate.
- Per-location overview: current on-hand quantities grouped by UOM, reliable valued subtotal, unvalued quantity/records and completeness status.
- Best-selling products: rank across units by net revenue or within comparable base-UOM groups by base quantity. Never rank 500 ml against 10 pcs as a meaningful global quantity comparison.
- Slow movers: `baseUnitsSold / eligibleSellingDays`, with product/UOM-aware thresholds. Normalise sales for fixed-pack and custom entries to same base unit.
- Transfer suggestions: match exact variant and relevant physical state; do not propose sealed jar transfer from loose stock, and preserve safety stock.
- Product details: show base UOM, fixed-pack stock and quantity where applicable, versioned cost/value, packaging stock and partial/unvalued warnings. Keep owner-dashboard view permissions as approved.
- Reconcile dashboard subtotal to existing valuation engine and GL. Do not mask missing or unreliable cost with master cost or 0.

## 15. Proposed HTTP API contracts (illustrative; adapt actual controllers)

All routes require existing authenticated business context and applicable permissions. No untrusted business override.

| Method / route (proposal) | Purpose | Important fields |
|---|---|---|
| `GET /api/uom/units` | Unit catalog | code, dimension, precision, active |
| `POST /api/uom/units` | Admin create | code, symbol, dimension, precision |
| `PUT /api/uom/units/{id}` | Versioned safe edit/deactivate | concurrency token |
| `GET /api/products/{id}/unit-config` | Current and optionally history | baseUnit, purchaseOptions, sellingOptions, packTracking |
| `PUT /api/products/{id}/unit-config` | Create new effective config | expectedVersion, effectiveFrom, options |
| `POST /api/uom/quote` | Safe conversion/price preview | variantId, optionId, qty, configVersion |
| `GET /api/inventory/pack-state?locationId&variantId` | Physical allocations | loose, sealed, prepared, reconciliation |
| `POST /api/inventory/pack-state/open` | Audited sealed→loose | sealedOptionId, count, version, idempotency |
| `POST /api/inventory/repacking` | Create draft job | source, output, components |
| `POST /api/inventory/repacking/{id}/execute` | Execute atomically | concurrency token, idempotency |

**Existing** PO, GRN, POS, return, stock-transfer, count and adjustment APIs should be **extended** with UOM input/snapshots; do not create competing parallel modules. Dashboard contracts gain explicit unit labels and coverage metadata additively; version API where changing quantity semantics would break clients.

### 15.1 Example quote request/response (conceptual)

```json
{
  "variantId": "<variant-guid>",
  "sellingOptionId": "<pack-guid>",
  "enteredQuantity": 2,
  "expectedConfigVersionId": "<version-guid>"
}
```

```json
{
  "selectedUnit": "pack",
  "enteredQuantity": 2,
  "baseUnit": "ml",
  "baseQuantity": 24,
  "currency": "BDT",
  "priceBeforeDiscountAndVat": 200,
  "effectiveConfigVersionId": "<version-guid>",
  "isPreviewOnly": true
}
```

Server recomputes at commit. Returned quote is informational, not an authorization to override stock/cost/price.

## 16. CQRS and validations

Suggested commands/queries (rename following repo conventions): `GetUomUnits`, `SaveUomUnit`, `GetVariantUnitConfig`, `UpdateVariantUnitConfig`, `PreviewUnitConversion`, `GetPackState`, `OpenSealedPack`, `CreateRepackJob`, `ExecuteRepackJob`, `GetUomInventoryBreakdown`.

Validators must check business ownership, existing SKU and packaging variant association, dimension, precision, effective-version overlap, state availability, pack count integer, positive factor, cost/GL eligibility, required workflow permissions, stale versions, inventory/price/quantity overflow and idempotency. Prefer existing validators for PO/GRN/POS instead of divergent duplicated logic.

## 17. Frontend Angular implementation plan

Proposed feature modules/components, adjust paths after repository inspection:

```text
catalog/uom/
  uom-list-page
  uom-edit-dialog
  unit-precision-input
products/
  product-unit-config-section
  purchase-unit-options-editor
  selling-pack-options-editor
purchase/grn/
  unit-entry-and-conversion-preview
  grn-pack-state-assessment
pos/
  selling-pack-picker
  custom-quantity-entry
inventory/
  pack-state-balance-detail
  open-pack-dialog
  unit-aware-quantity-input (shared)
  repack-job-page / execute-confirmation
owner-dashboard/
  unit-aware-quantity-badge and valuation coverage
```

Use Angular typed forms and shared design-system controls, `--rp-*` tokens and existing sidebar/navigation. Preserve form state on tab changes and keyboard accessible entry. Disable pack-specific controls for normal `pcs` products. Explicitly label count vs base quantity and price per selected unit. Present conflict/reload actions for obsolete versions. Do not import/copy Able Pro template assets before licence approval.

## 18. Permission and security design

Discover existing permission catalog and seed conventions; map catalog management to product/config permissions and operational actions to respective purchase/GRN/POS/stock rights. Introduce explicit granular `uom.*`, `inventory.pack_state.*` or `inventory.repacking.*` keys only after audit and owner review, not as assumed facts. Protect every new controller/action; ensure backend tenant context, location authorization, row-level business constraints, CSRF/session conventions and audit actor. Existing dashboard views are `dashboard.sales_finance.view` and `dashboard.inventory_intelligence.view` and allow business-wide reporting, but not mutation.

## 19. Transactionality, concurrency and posting safeguards

- Use existing EF transaction boundary / row lock and concurrency (`xmin` or repository's method), not independent database writes for pack-state after inventory commit.
- Assign idempotency identity to PO/GRN/checkout/transfer/return/open/repack effects. Enforce uniqueness at DB, not only in application cache.
- Ordered locking across content variant, packaging variants and pack-state balances prevents deadlocks; order by business/location/variant id.
- Enforce conservation rules after operation and in scheduled reconciliation diagnostics; reconciliation should be read-only unless explicitly approved correction.
- Existing GL inventory journal and COGS accounting rules are the sole authority; no second income/expense journal based only on pack-state movement.
- For packaging capitalization and damaged write-offs, identify actual ledger/posting capabilities. If they do not exist, block affected operation rather than invent a journal.
- Use existing outbox for downstream event publication where configured; no new microservice required.

## 20. Test plan with precise expected results

### Unit tests
- Pure conversions (L↔ml, kg↔g, jar500 for one variant vs jar250 for another).
- Precision: `12.5678 ml` rejected if precision2, `1.5 jar` rejected if whole-only, exact base representability.
- Multiple effective configurations; no overlap; historical snapshots stable.
- Fixed pack vs custom pricing independent.
- Pack-state invariant on open/unpack; prepared stock vs loose stock; no packaging double charge.
- Zero/invalid factor, unit dimension mismatch, stale version conflicts.

### PostgreSQL-backed integration tests
- Create conventional `pcs` product and sale: no behaviour change.
- PO 1 × 500ml jar @ ৳1000 → GRN accepted +500ml, ledger cost ৳2/ml, journal and valuation consistent.
- Partial GRN and mixed physical condition; reject unsupported quarantine scenario without posting partial ledger/GL.
- Sell 2 × 12ml @ ৳100 → stock −24ml; reconcile cost and tax; insufficient stock/packaging blocks atomically.
- Custom 75ml sale; same SKU from two locations; cross-business and location isolation.
- Open jar: sealed −1, loose +500ml, total Qty/GL unchanged.
- Transfer sealed jar and loose portion, dispatch/receipt and in-transit valuation consistency.
- Repack 240ml + 20 bottles → 20 prepared packs; component value conserved; sale of prepared pack no second content/bottle deduction.
- Return 7ml from 12ml sale condition-based; refund and inventory snapshots consistent with approved policy; cumulative return capped.
- Concurrent POS sale vs repack vs transfer; idempotent repeated commands; no negative stock, duplicate journal or wrong pack-state.
- Dashboard unit breakdown, valuation partial/unvalued and revenue ranking across units.

### Frontend and end-to-end
- UOM list/create/deactivate, product create/edit, version change warning, purchase/GRN, POS pack and custom input, mixed-unit count, open/repack, return drawer, stock details, dashboard display.
- Keyboard, 390/768/1440 responsive checks, currency formatting and API error states.
- Verify no hard-coded colors and existing build/unit/integration suite.

### Accounting assertions
- Quantity conserved in unpack and repack; packaging component value moved not duplicated.
- Purchased base value matches posted GRN inventory journal.
- Transfer carries source cost; no fictitious inventory gain.
- COGS and return reversals reconcile to original snapshots; per-sale rounding as current accounting.
- Inventory Dashboard reliable subtotal reconciles to valuation engine/GL (with explicit partial/unvalued coverage).

## 21. Delivery sequence / vertical slices

1. **Read-only compatibility audit:** exact quantity/cost paths, requirements status, accounting blockers, performance risks.
2. **Unit master & versioned configuration:** schema and migration proposal, safe defaults, product UOM forms.
3. **Decimal quantity and purchase flow:** PO/GRN unit conversion and reliable cost posting; end-to-end tests.
4. **POS selling:** fixed/custom packs, authoritative price/VAT, stock and invoice/return snapshots.
5. **Unit-aware inventory:** transfer, count, adjust and inventory-report integration.
6. **Optional packaging/pack-state:** packaging consumption and sealed/loose operation, reconciliation and tests.
7. **Optional repacking:** only after accounting-safe value pattern selected and verified; concurrency/reversal/packaged sale.
8. **Condition returns and dashboard:** condition-based restore and unit/value presentation; cross-module regression and browser verification.

Sequence may reorder behind existing code dependencies but must not skip security, migration and accounting gates.

## 22. Source audit and change-management rules

Respect `CLAUDE.md`, `.claude/rules`, `.claude/skills`, `.github` guidance and approved SRS/LLD of Sales/POS, Inventory, Purchase, Finance and Owner Dashboards. Preserve unrelated code/working tree. Create a `UOM_IMPLEMENTATION_GAP_ANALYSIS.md` with file-by-file code mapping, migration plan and authorization table **before writing**. Do not commit, deploy, backfill historical data, or touch Production without separate authorization. Development migrations and ledger correction likewise require explicit approval. Don't claim successful tests before executing them.

## 23. Known design decisions requiring targeted source validation

These are implementation blockers, **not approval for D13+**:

| Item | Why it matters | Required outcome |
|---|---|---|
| Quantity schema supports decimals? | Integer ledger loses fractional ml/kg | Verified widening plan covering all writers/readers |
| GRN accepted vs damaged posting | Need a real non-sellable state or supplier rejection | Existing policy reuse or explicit blocker |
| Pack-state vs inventory semantics | On-hand vs available can differ | Conservation invariant bound to real model |
| Repack capitalization | Packaging value must move into prepared output | Choose P1 or P2 with GL proof; seek owner sign-off for changed SKU/accounting semantics |
| Mixed-unit POS offline mode | Offline price/version and stock may be stale | Explicitly validated sync/conflict policy |
| Returns/exchanges policy | Current flows may be incomplete | No new finance/GL rules invented |
| Invoice/price precision | BDT rounding and quantity scale differ | Current finance rounding preserved |
| Historical compatibility | Legacy rows have no unit snapshot | Read legacy as `pcs` ×1, no data rewrite |

## 24. Definition of done

All D01–D12 approved outcomes have automated and real-workflow verification, including Attar 500 ml purchase/12 ml or custom sale, multi-package GRN, stock transfer/count/adjustment, optional packaging and pack-state, condition-based returns and optional repacking. Authoritative inventory/ledger/GL conserve quantity/value and report partial reliability honestly. All old products behave as `pcs`, no D13 features creep in. Provide exact changed files, migrations, tests, accounting reconciliation and release blockers.

## Addendum — implementation of I01–I05 (2026-11-07)

- **Refunds:**
  - New tables `sale_refunds` and `sale_refund_payments`. `sale_return_lines` gains `sale_refund_id`, `net_amount` and `vat_amount`. `sale_items` gains `returned_quantity`, with the check `0 <= returned_quantity <= quantity`.
  - `sale_financial_postings.sale_refund_id` is unique when set. The per-sale unique index now applies only to rows without one.
  - Source keys: the first refund keeps `sale:{id}:reversal:v1` and the journal reference `{id}`; later refunds use `sale:{id}:refund:{n}:v1` and `{id}:R{n}`.
  - Shares are cumulative: each refund takes `share(before + now) − share(before)`. Payment allocation is weighted by what each payment still holds.
  - The sale row is locked with `FOR UPDATE` before it is read.
- **Promotions:**
  - `variant_selling_options.apply_automatic_pricing_rules` (existing rows false).
  - `POST /api/pos/cart-prices` accepts `sellingOptionId` and `enteredQuantity`, and returns `lineGross` and `rulesNotApplied`.
  - Sale lines may send `expectedLineGross`; a difference is refused with `SALE_PRICE_CHANGED`.
- **Prepared packs:** `POST /api/v1/uom/pack-state/open-prepared` (`inventory.pack_state.manage`).
- **Transfers:** a single-form line can be received in part, in whole packs. A missing or damaged prepared pack is refused.
- **GRN:** a line may carry only damaged or rejected quantity, which posts nothing.
- **Slow movers:** measured units are judged by stock cover against `SourceExcessCoverageDays`.
- **Migration:** `20261107084952_PosV2UomRefundsAndPromotionEligibility`.

## Addendum — implementation of I06–I07 (2026-11-07)

- **Prepared packs (I06):** `POST /api/v1/uom/pack-state/open-prepared` requires `packagingReusable=true` and a single packaging component.
  - Lock order is the packaging inventory row, then pack-state balances.
  - The packaging ledger entry `prepared_open` is valued at the `CapitalizedValueShare` of the opened packs.
  - Pack-state movements carry the request's idempotency key.
- **Cost redaction:** `SalesController.ToDto` sets `historicalUnitCost` to null without `products.view_cost`.
- **VAT report:** `VatReportReadStore.AddPostedSaleReversalsAsync` reads posted reversal intents (status `posted`, journal posted). It reports their snapshot lines at the original line's `TaxCode`/`VatRate`, dated on the reversal journal's `PostingDate`, and skips sales already reported through a `ReversalOfJournalEntryId` journal.
- **Refund ledger value:** `ISaleRefundStockLedgerWriter.AppendReturn` takes `movementValue`; the refund passes the cumulative content-cost share.
- **Posting guard:** migration `20261107104508_PosV2UomPostingRefundGuard` adds `sale_refund_id` to `fn_sale_financial_posting_guard`.
- **Transfer opportunities:** `MinDemandVelocityPerDay`, `MinimumRetainedUnits`, `MaxSuggestedQty` and `MinRecommendationQty` apply to `pcs` items only.
