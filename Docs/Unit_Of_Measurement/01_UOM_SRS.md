# IraniMart POS v2 — Unit of Measurement (UOM), Packaging & Repacking
## Software Requirements Specification (SRS)

**Version:** 1.0 — approved business scope D01–D12  
**Date:** 2026-11-06  
**Status:** Requirements baseline; implementation subject to source-code compatibility audit  
**Audience:** Business owner, product analyst, frontend/backend engineers, QA, accounting reviewers  
**Suggested repository path:** `Docs/Unit_Of_Measurement/01_UOM_SRS.md`

> **Authority and scope:** Decisions D01–D12 below were expressly approved. Implementation choices, examples, proposed APIs, and safety gates in this document are *design recommendations*, not new owner approvals. Where existing POS/Finance rules are uncertain, inspect code and relevant approved documents; do not invent a conflicting rule. D13 (batch/lot, expiry, FEFO) and all later features are **out of scope for UOM v1**.

## 1. Purpose and outcome

Enable a product to be purchased in one or more units/packages, stocked in a canonical base unit, sold in fixed priced packs or custom quantities, counted/adjusted/transferred with unit-aware inputs, and optionally tracked as sealed/loose and repacked using packaging inventory. Ensure quantity, stock valuation, COGS and accounting reconcile. Existing `pcs` data must continue unchanged.

Primary example: buy **1 jar × 500 ml** of Attar for **৳1,000**, receive **500 ml** at **৳2/ml**, sell a **12 ml pack for ৳100** or a custom quantity at its configured rate. A 12 ml sale deducts 12 ml, not one jar. Without other movements, remaining 488 ml has Attar component cost ৳976. Optional bottle consumption is accounted separately; prepared packs cannot cause double consumption.

## 2. Approved decision register — immutable scope

| Decision | Approved rule |
|---|---|
| D01 | Unit/pack-wise selling prices including custom quantities |
| D02 | Hybrid conversion: global physical conversions plus product/variant-specific packaging conversions |
| D03 | POS: fixed selling packs **and** custom quantity |
| D04 | Optional separately tracked packaging inventory, packaging consumption and cost |
| D05 | Configurable quantity precision by UOM |
| D06 | Multiple purchase units and multiple selling packs, including defaults |
| D07 | Versioned unit configuration with immutable per-transaction snapshots |
| D08 | Condition-based return/exchange and inventory restoration |
| D09 | Unit-aware transfers, counts, adjustments |
| D10 | Optional sealed/loose pack-state tracking, open/unpack operation |
| D11 | GRN physical pack-state confirmation, accepted vs damaged/short quantity |
| D12 | Optional repacking workflow producing ready-to-sell packs |

**Existing-data compatibility (approved):** Retain existing product, stock and transaction quantities as `pcs`, factor `1`; do not rewrite historical transactions or quantities. Default new product UOM = `pcs`. Base UOM becomes immutable once stock/transactions exist. Existing ProductVariant/SKU stock identity remains authoritative.

## 3. Boundaries and exclusions

**In scope:** UOM master and conversions; product/variant unit setup; PO/GRN; inventory and cost; POS checkout; fixed/custom packs; packaging usage; returns/exchange interaction; unit-aware stock transfer, count and adjustment; optional pack-state, physical GRN confirmation and repacking; inventory/report/dashboard quantities and valuation; configuration audit; authorization; testing and backward compatibility.

**Not in scope:** D13+ batch/lot tracking, expiry dates, FEFO, recall tracking, weighing scale integration, food label regulations, country tax configuration, full manufacturing BOM/routing/work orders, multi-stage production planning, general warehouse bin management, unapproved inventory write-off accounting or historical cost correction. Architecture should leave future extension points without implementing them now.

## 4. Personas and rights

- **Catalog administrator:** manages UOM master, product unit choices, selling packs and optional packaging links under existing product/admin authorization. New permission keys must follow a repository permission audit; no guessed keys.
- **Buyer/receiver:** enters PO units and confirms physically received, accepted and non-sellable quantities within existing purchase/location permissions.
- **Cashier:** selects fixed pack or custom quantity within existing POS and price/discount permissions; cannot override server price/conversion.
- **Inventory staff:** counts, adjusts, transfers, opens packs or repacks within appropriate existing or explicitly introduced action permissions.
- **Business owner/report reader:** sees total inventory *value* and unit-aware product/location information through approved dashboard permissions. Dashboard read permission does not confer inventory mutations.

Every write/read is business-scoped and operational actions follow existing location assignment, terminal/session, workflow and concurrency rules. Dashboard reporting access remains its separately approved business-wide model.

## 5. Glossary and invariants

- **UOM:** unit such as `pcs`, `ml`, `L`, `g`, `kg`, `m`, `jar`, `bottle`.
- **Dimension:** count, volume, mass, length, packaging. Cross-dimension conversion requires explicit product-specific content rule, not a global arbitrary factor.
- **Base UOM:** canonical quantity persisted for one ProductVariant in stock/ledger (e.g., ml).
- **Purchase Unit Option:** variant-specific allowed buying unit/package and conversion to base.
- **Selling Pack Option:** named option holding base quantity, independently configured price and optional packaging mapping; custom base quantity is also allowed.
- **Pack state:** physical distribution of one variant's base stock into sealed configurations, loose stock, and explicitly non-sellable quantities.
- **Prepared pack:** pre-filled, ready-to-sell physical pack from repacking; contents remain linked to the underlying variant; packaging usage is accounted once.
- **Conversion snapshot:** exact selected unit, quantity, factor, configuration version, base quantity, price/cost relevant to an immutable transaction line.

**Core invariants:**
1. Monetary stock valuation uses **base quantity × authoritative base-unit cost**, not retail selling price.
2. Quantity and cost conversions use decimal-safe arithmetic; no binary floating-point persistence.
3. Each material movement has a unique idempotency identity and never posts stock twice.
4. A physical pack-state change without waste/packaging consumption does **not** change Attar base quantity or its Attar value.
5. An aggregate product across unlike UOMs has no meaningful single `pcs` quantity; show per-UOM quantities or value.
6. The application cannot silently invent a cost, a pack-state, a batch, or a stock correction.

## 6. UOM master — functional requirements

| ID | Requirement | Priority |
|---|---|---|
| UOM-001 | Admin can list/search/create/edit/deactivate UOM definitions with name, symbol, dimension, precision, count/integer flag and active state. | Must |
| UOM-002 | Seed core units: pcs, pair, ml, L, g, kg, cm, m; packaging names may be configured (jar, bottle, box, roll). | Must |
| UOM-003 | Canonical physical conversions available only among dimension-compatible units (1 L = 1000 ml; 1 kg = 1000 g; 1 m = 100 cm). | Must |
| UOM-004 | Packaging conversions are **product/variant-specific**, never one globally assumed jar size. | Must |
| UOM-005 | Existing UOMs used in transactions cannot be deleted/modified incompatibly; deactivate for future use. | Must |
| UOM-006 | QuantityPrecision integer in bounded range (proposed 0–4, validate actual DB precision). `pcs` default 0; measured units configurable. | Must |
| UOM-007 | Prevent duplicate codes and ambiguous symbols within the permitted business/global catalog scope. | Must |

## 7. Product & variant setup

| ID | Requirement |
|---|---|
| PROD-001 | Product/Variant Create/Edit displays base UOM, enabled purchase unit options, selling pack options, default choices and custom selling unit price. Default base/purchase/sale is `pcs` ×1 for conventional products. |
| PROD-002 | Multiple purchase options can reference distinct package sizes, e.g. `500 ml Jar` → 500 ml, `1 L Bottle` → 1000 ml. Exactly one active default where purchase is enabled. |
| PROD-003 | Multiple selling packs: label, conversion/base quantity, selling price, active/default status and optional packaging components. |
| PROD-004 | Fixed pack price is independent of custom quantity rate. Two 12 ml packs and 24 custom ml may have different prices; stock reduction may be equal. |
| PROD-005 | ProductVariant is stock identity, including manually created Standard/Default variation in catalog; this module does **not** require automatic internal variants. |
| PROD-006 | Do not permit base UOM changes after the variant has stock/transactions, unless a separately approved safe conversion process exists (out of scope). |
| PROD-007 | Versions of purchase options, selling options, prices, packaging links and precision-critical configuration carry effective dates/audit; no overlapping active versions for the same key. |
| PROD-008 | Inactivate options with history instead of deleting; reject invalid or circular conversions, zero/negative conversion factors, and unresolved dependencies. |

### Reference configuration — illustrative

| Attribute | Premium Attar |
|---|---|
| Main code | ATR-0002 |
| Variant SKU | ATR-0002-STD |
| Base UOM | ml |
| Purchase option A | 1 Jar = 500 ml |
| Purchase option B | 1 Bottle = 1000 ml |
| Fixed selling packs | 6 ml/৳60; 12 ml/৳100; 50 ml/৳350 |
| Custom price | ৳5 per ml |
| Packaging | Optional: 12 ml pack consumes 1 empty bottle |

## 8. Conversion, precision, rounding and snapshots

| ID | Requirement |
|---|---|
| CALC-001 | BaseQty = EnteredQty × Factor-to-base, validated using authoritative version selected for the transaction. |
| CALC-002 | Entered qty precision obeys selected unit; resulting base qty must be exactly representable in the base unit's precision. Reject unsupported fractional result; never silently round stock. |
| CALC-003 | Count-based sealed pack quantities must be whole numbers, even if their liquid content is measured. |
| CALC-004 | Cost per base unit = eligible inventory acquisition cost ÷ acquired base qty under approved purchase/GRN valuation policy; money rounded only at approved posting boundaries. |
| CALC-005 | Server validates factor, price, version and availability; browser may preview only. |
| CALC-006 | Committed PO/GRN/Sale/Return/Transfer/Count/Adjustment/Repack lines snapshot unit, selected option/version, factor, entered and base qty and relevant prices/costs. |
| CALC-007 | A draft created under an obsolete configuration must be revalidated on finalization; obtain confirmation/reprice according to existing draft policy, not silently reinterpret. |
| CALC-008 | Preserve historic `pcs` factor-1 semantics without retroactively updating rows. |

## 9. Purchasing and GRN receiving

| ID | Requirement |
|---|---|
| PUR-001 | Buyer selects active variant purchase option; PO line shows entered quantity, selected unit, base equivalent and vendor cost. |
| PUR-002 | Multiple GRNs and partial receiving retain the PO conversion snapshot and remaining base quantity. |
| PUR-003 | With pack tracking off, use existing GRN receiving and convert to base quantity. |
| PUR-004 | With pack tracking on, receiver confirms actual sealed units, loose quantity and separately damaged/quarantine/rejected quantities. |
| PUR-005 | GRN reconciles ordered, physically received, accepted, rejected and shortage/overage quantities; exceptions follow existing GRN approval rules. |
| PUR-006 | Accepted stock posts once to authoritative inventory and stock ledger; pack-state balances are updated in same consistent boundary. |
| PUR-007 | GRN cost reaches inventory ledger using approved reliable acquisition-cost path, matching applicable GL posting. No arbitrary cost fallback. |
| PUR-008 | Distinguish received-but-unavailable stock from saleable stock; if quarantine capability is missing, block that state with documented gap rather than fake available stock. |
| PUR-009 | Return/rejection vendor and freight/tax valuation follow existing approved accounting rules, not automatically allocated across accepted units. |

## 10. POS selling and pricing

| ID | Requirement |
|---|---|
| POS-001 | Cashier finds variant by existing name/main code/SKU/barcode rules and chooses a fixed pack or custom quantity. |
| POS-002 | Fixed packs calculate price per configured pack × pack count; decrement base quantity = pack count × pack base qty. |
| POS-003 | Custom quantity uses the configured custom per-unit price and precision, converted to base qty. No unrestricted cashier unit-price edit. |
| POS-004 | POS backend recomputes sale price, VAT, discounts and base stock from effective configuration; client values are untrusted. |
| POS-005 | Decrement stock immediately on completed sale according to existing POS lifecycle; prohibit negative available stock and maintain session/terminal authorization. |
| POS-006 | On-the-fly pack preparation with packaging inventory enabled consumes the configured empty packaging item once. |
| POS-007 | Prepared pack sales consume prepared pack state; they must NOT also deduct loose contents or a new bottle twice. |
| POS-008 | Show remaining stock with unit; for pack-state-enabled items, show sealed/loose availability and enforce eligibility of the chosen physical selling form. |
| POS-009 | Hold/Recall, offline desktop checkout (when implemented), idempotency and retries preserve unit, version, price and inventory consistency. Offline catalogue/version staleness requires safe reconciliation or sale refusal; no implied silent override. |
| POS-010 | Receipt/invoice renders quantity + selected unit/pack and unit price accurately, with server VAT/discount calculations. |

## 11. Optional packaging inventory (D04)

Packaging items are ordinary stock-managed product variants (e.g., empty 12 ml bottle as `pcs`). A selling pack may optionally associate component variant + quantity. Pack mappings are effective-dated and versioned.

| ID | Requirement |
|---|---|
| PACK-001 | Packaging use is optional by variant/selling pack. Products without packaging mappings use existing normal POS. |
| PACK-002 | Packaging deduction and component COGS are atomic with the sale or repack event, using authoritative unit cost. |
| PACK-003 | Insufficient required packaging stock blocks the relevant transaction. |
| PACK-004 | Loose/custom sales may select configured packaging or no packaging, subject to that option's business configuration. |
| PACK-005 | Do not treat missing packaging cost as zero for reliable margin reporting. |
| PACK-006 | Unpacking sealed stock does not automatically produce saleable empty-bottle inventory without an approved recovery policy. |

## 12. Optional sealed/loose pack-state (D10)

| ID | Requirement |
|---|---|
| STATE-001 | Pack-state tracking opt-in per variant; disabled products use ordinary base-unit stock. |
| STATE-002 | Track sealed pack counts by configured pack definition and loose base quantity per location; separately model any supported non-sellable quantity. |
| STATE-003 | Valid pack-state allocation reconciles with authoritative base inventory. Never use pack-state as a second accounting ledger. |
| STATE-004 | `Open/Unpack` converts one sealed pack into its corresponding loose base qty with no base inventory quantity or value change (unless separately authorized wastage). |
| STATE-005 | Selling sealed full pack requires sealed availability; selling loose quantity requires loose availability; do not auto-open without an explicit audited action. |
| STATE-006 | Transfer carries exact pack-state quantities; destination confirms received states without creating/destroying base stock. |
| STATE-007 | Pack-state count supports sealed counts plus loose measurements, converted and reconciled to base quantity. |
| STATE-008 | Existing legacy `pcs` balances require no retrospective sealed/loose categorization. |

## 13. Unit-aware transfer, count, adjustment (D09)

| ID | Requirement |
|---|---|
| INV-001 | Authorized user can enter quantity in allowed unit; UI previews base equivalent. |
| INV-002 | Server resolves conversion snapshot and rechecks stock/permissions/concurrency before dispatch/receipt, count approval or adjustment posting. |
| INV-003 | Transfer preserves source weighted-average cost through in-transit/receipt and no duplicate inventory GL gain/loss. |
| INV-004 | Count compares physical counted base qty to actual expected base qty using current snapshot and approved stock-count concurrency rules. |
| INV-005 | Adjustments with damaged/leakage reasons must use existing approved accounting/write-off policy; block unsupported scenarios rather than invent GL journals. |
| INV-006 | Existing transfer lifecycle, approvals, requisitions and outlet/warehouse assignments remain authoritative. |

## 14. Return, exchange and refund (D08)

| ID | Requirement |
|---|---|
| RET-001 | Use original sale line's immutable unit/pack, base qty, price, discounts, tax, cost and packaging snapshots. |
| RET-002 | Support allowed partial returns for measured items; cumulative returned base qty cannot exceed sale's remaining returnable qty. |
| RET-003 | Assess item condition (resellable, non-resellable) and packaging reusability; restore only genuinely sellable quantity. |
| RET-004 | Original amount and approved return/refund policy determine financial reversal, not current catalogue price. |
| RET-005 | Prevent restock of damaged/expired/non-resellable items; if no quarantine/write-off handling exists, fail safely and report gap. |
| RET-006 | Exchange links original and replacement sale, respecting current POS/Finance posting rules and no duplicate revenue/COGS reversal. |
| RET-007 | Existing `pcs` returns keep their legacy workflow unless a new condition is relevant to the product. |

## 15. Optional repacking (D12)

**Business example:** Make 20 ready-to-sell 12 ml bottles from 240 ml loose Attar and 20 empty bottles. If Attar cost = ৳2/ml and bottles = ৳8 each, inputs cost ৳480 + ৳160 = ৳640, finished cost = **৳32/bottle** before wastage/labour. A contrary intermediate example such as `৳20 + ৳12` is not authoritative.

| ID | Requirement |
|---|---|
| REP-001 | Repacking is opt-in with dedicated create/review/execute operation; not a general manufacturing system. |
| REP-002 | Select source variant, source location, intended output selling pack, output count, optional required packaging components and expected consumed base qty. |
| REP-003 | Validate available **loose** source quantity and available packaging items; sealed source may require explicit Open operation first. |
| REP-004 | Atomically consume source loose allocation and packaging, create finished prepared-pack state, and post cost/value reclassification without duplicating base Attar quantity. |
| REP-005 | Finished pack retains the same underlying Attar ProductVariant identity plus a distinct physical prepared-pack identity/state. |
| REP-006 | Selling/transferring/counting prepared packs uses prepared-pack state, and never consumes component stock twice. |
| REP-007 | Audit operation reference, source, outputs, actual consumption, user, business/location, version snapshots, timestamps and any approved wastage. |
| REP-008 | Wastage, labour capitalization, unpack recovery and damaged outputs require existing approved cost/write-off rules; block/flag unsupported cases. |
| REP-009 | Support retry/idempotency, partial failure rollback and concurrency on every affected stock item. |

## 16. Inventory Intelligence & reporting

| ID | Requirement |
|---|---|
| BI-001 | Total valued stock = sum of reliably valued base quantities × cost, across authorized business locations; report incomplete valuation separately. |
| BI-002 | Never aggregate `pcs + ml + kg` into one numerically labelled `pcs` KPI. Show unit breakdown or an appropriate unit-neutral metric. |
| BI-003 | Location stock value aggregates monetary values across UOMs; per-product rows display quantity with unit, including partial/unvalued status. |
| BI-004 | Best sellers can rank across UOMs by net revenue; raw quantity comparisons across unlike units must be grouped or explicitly labelled incomparable. |
| BI-005 | Slow-mover velocity uses base quantity / eligible selling days; thresholds defined per relevant product/unit context. |
| BI-006 | Transfer suggestions compare the same variant/base UOM and physical transferability, including sealed/loose constraints. |
| BI-007 | Prepared packs, packaging stock and loose contents must never be double-counted in aggregate quantity or value. |
| BI-008 | Preserve approved dashboard role permissions and business-wide read visibility; actions still require existing operation permissions. |
| BI-009 | Cost corrections and historical unreliable cost handling retain the latest approved reconciliation policy; no silent master-cost substitution. |

## 17. User interface requirements

Use existing Angular application, design tokens and shared components; Able Pro template at `design-reference/able-pro/able-pro-angular-v3.3.0` is a **visual reference subject to licence and compatibility review**, not authorization to copy wholesale.

- **Catalog > Units of Measurement:** searchable list, add/edit/deactivate, dimension, precision, standard conversions.
- **Product Create/Edit > Units & Pricing:** base UOM, purchase options with defaults, fixed selling packs, custom price, optional packaging mapping, optional sealed/loose, repacking enablement; clean UX and no extra complexity for `pcs` products.
- **Purchase/GRN:** selected purchase package, conversion preview, physical confirmation when enabled, discrepancy validation.
- **POS:** pack cards, custom quantity form, price and stock preview, optional packaging selector, sealed/loose indicator.
- **Inventory operations:** unit selector, base equivalent, validations, pack-state breakdown where enabled.
- **Repacking:** source, planned output, packaging, preview/reconciliation, review/execute confirmation.
- **Inventory Intelligence:** UOM-labelled quantities and value coverage; pack-state detail only for opted-in products.

Loading, error, permission-denied and missing-configuration states required. Keyboard and responsive behaviours follow existing application standards.

## 18. Permissions and audit

No guessed permission names or blanket owner override: inspect existing permission catalog first. Add a minimal explicit permission set only for new UOM/repacking actions if necessary; seed/role-edit integration according to existing pattern. Authorization always server-side; tenant/business isolation mandatory; location security on stock-mutating operations mandatory. Immutable audit records for configuration changes, unpack, repack, corrections and quantity-changing operations.

## 19. Nonfunctional requirements and deployment

- **Correctness:** transactional stock/GL consistency; decimal precision; no negative stock; retry/idempotency and concurrency safe.
- **Performance:** conversion lookup indexed/cached safely by business/product/version; avoid full-ledger replay per POS request; avoid N+1 dashboards; measure actual loads.
- **Reliability:** no partial writes between inventory, pack-state and journal; clear failure states if dependencies cannot post.
- **Security:** authoritative price/quantity/cost, no cross-business product/packaging links, no unauthorized operational locations.
- **Compatibility:** existing `pcs` products and historical data continue unchanged; additive schema and safe defaults preferred; no production DB changes without separate approval.
- **Observability:** structured audit/tracing and reconciliation metrics for pack-state vs base stock, repacking component value and account postings.

## 20. Acceptance criteria and test matrix

| Area | Minimum acceptance scenarios |
|---|---|
| UOM | Create/deactivate, dimension mismatch rejected, quantity precision and decimals validated |
| Product | Default `pcs`; multiple purchase/selling options; immutable base after transactions; version history |
| PO/GRN | 1 jar × 500 ml at ৳1000 => +500 ml at ৳2/ml; split GRN, damaged/rejected reconciliation |
| POS | 2 × 12 ml packs => 24 ml deducted at pack-specific price; custom 75 ml using own rate; no negative stock |
| Packaging | Optional bottle consumption once, insufficient bottle blocks, no double consumption after repack |
| Pack-state | 2 sealed 500 ml jars + 175 ml loose = 1175 ml; unpack leaves aggregate quantity/value intact |
| Transfers | Transfer 1 jar or 250 ml, preserve cost and source/destination reconciliation |
| Count/adjust | Mixed physical units, stock variance and supported damage accounting only |
| Returns | Condition-based partial measured return, safe packaging restoration, original snapshots |
| Repack | 240 ml + 20 bottles => twenty 12 ml prepared packs, overall value conserved, no component duplication |
| Dashboard | Monetarily reconciled location totals; no mixed-UOM `pcs` total; partial/unvalued statuses |
| Security | Cross-business attempt denied; permissions at UI and API; authorized location enforced on writes |
| Regression | Existing `pcs` sale/GRN/transfer/count, historical reporting and accounts unchanged |

Run backend unit and PostgreSQL-backed integration tests, Angular tests, production build, end-to-end workflows and relevant existing regression tests. Report failures, skipped tests and unverified browser scenarios explicitly.

## 21. Migration and go-live safeguards

1. Perform **read-only source and schema audit** to identify all integer-based quantities (including approvals, reservations, reports, POS/offline sync, GL journal snapshots). Existing product/stock/transaction records are **not rewritten**.
2. Add new UOM definitions/configuration/pack-state tables and decimal-capable data paths as necessary; compatibility projection treats legacy values as `pcs` ×1. Do **not** assume all existing integer DB columns can safely remain integers for new measured products.
3. For historical transaction rows without snapshots, legacy readers interpret them as `pcs`, not as today's product setting; do not retrofit guessed conversion metadata.
4. Validate forward-only posting, valuation/GL reconciliation, permissions and performance on isolated Test and Development before migration/deployment decisions.
5. Do not apply migration, backfill data, commit, push or deploy without explicit approval.

## 22. Decisions intentionally open for implementation verification

These are **not D13+ business scope expansions**: confirm with existing approved policy/code during audit, or report a blocker rather than guessing.

- Current inventory quantity meaning (on-hand vs sellable), damaged/quarantine treatment and write-off journal authority.
- Which exact POS hold/recall/offline paths require conversion-version reconciliation.
- Return/refund/exchange policy gaps in current implementation and whether partial measured returns are permitted.
- Sealed-pack and prepared-pack physical identifiers, especially avoiding duplicate cost when same variant has multiple pack forms.
- Monetary precision/rounding and allowed UOM precision based on actual schema.
- Unit-version effective-date rules for in-progress drafts; backdating privileges remain as already approved.
- Explicit role-permission keys for new UOM/repacking administration.

## 23. Definition of done

D01–D12 implementation is complete when all supported flows use canonical base unit and immutable conversion snapshots; pricing, stock quantity, packaging cost, pack state, returns and repacking reconcile; Inventory Intelligence displays meaningful per-unit quantities and reliable monetary totals; old `pcs` behaviour is preserved; docs/tests/security gates pass. No D13+ functionality may be silently introduced.

## Addendum — owner decisions I01–I05 (2026-11-07)

These approved decisions refine the requirements above. Where they differ, they govern.

- **I01:** the permission keys `uom.view`, `uom.manage`, `inventory.pack_state.view`, `inventory.pack_state.manage`, `inventory.repacking.view` and `inventory.repacking.execute` are approved. POS, purchase and inventory staff read unit setups through their own operational permissions.
- **I02 (refines RET-002):** a sale may have several partial refunds. Each one records its lines, condition, amounts and payment split, and posts its own journal exactly once.
  - Cumulative returned quantity never exceeds the quantity sold.
  - Sale status is `partially_refunded` until nothing remains, then `refunded`.
  - Sales refunded before this decision stay closed.
- **I03 (refines D01):** automatic pricing rules may apply to a selling option when that option allows it. New packs default off, new custom quantities on.
  - A pack's rule minimum counts packs, not base units.
  - A custom quantity takes only percentage rules without a minimum. Any other rule is not applied and is named to the cashier.
- **I04 (refines RET-007):** one refund workflow and dialog serves legacy `pcs`, unit and mixed sales, with the same server safeguards.
- **I05:** Pattern P1 (one Inventory account; packaging value capitalized into prepared packs and released through COGS) is approved, subject to the reconciliation tests passing.

## Addendum — owner decisions I06–I07 (2026-11-07)

- **I06 (refines PACK-006 and REP-008):** an individual prepared pack may be opened.
  - The content becomes loose stock with no change in quantity.
  - Reusable packaging returns to packaging stock at its stored capitalized value, and the prepared capitalization falls by the same amount, so total inventory value is unchanged.
  - Damaged or unusable packaging is never recovered; that opening is refused. So is a pack with several packaging items.
  - No disposal journal or loss account is authorized.
- **I07:**
  - Sale cost is shown only to holders of `products.view_cost`, and the API omits it otherwise.
  - The VAT report includes posted refunds and voids, each on its own accounting date.
  - A refund's stock-ledger value equals its journal cost.
  - Posting intents are immutable, including their refund link.
  - Every stock and velocity report and dashboard is unit-aware.
