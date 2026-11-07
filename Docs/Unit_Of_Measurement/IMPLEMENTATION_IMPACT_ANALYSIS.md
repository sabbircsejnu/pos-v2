# UOM v1 — Implementation Impact Analysis

**Date:** 2026-11-06. **Scope:** D01–D12. **Basis:** read-only source audit of branch `staging` at `f9508fe8`.
Satisfies the LLD §2 discovery gate (and stands in for `UOM_IMPLEMENTATION_GAP_ANALYSIS.md`).

## 1. Current state of quantities

Every stock quantity in the system is a 32-bit `int`. Nothing can hold 12.5 ml today.

| Table.column (C# type) | Today | Meaning | Decision |
|---|---|---|---|
| `inventories.quantity` | `integer` | on-hand per variant + location, no quarantine bucket | widen |
| `stock_ledgers.qty_in/qty_out/balance_after` | `integer` | immutable movements | widen |
| `purchase_order_items.quantity` | `integer` | ordered units | widen; base units |
| `grn_items.received_qty` | `integer` | received = stock posted | widen; base units |
| `sale_items.quantity` | `integer` | sold units | widen; base units |
| `stock_transfer_items.*quantity` (7 cols) | `integer` | lifecycle quantities | widen |
| `stock_adjustment_lines.previous/quantity_change/new` | `integer` | | widen |
| `stock_requisition_lines.requested/fulfilled` | `integer` | | widen |
| `stock_count_lines.current_stock/physical_stock/difference` | `numeric` already | | keep |
| `stock_count_operation_receipts.counted_quantity` | `numeric` already | | keep |
| `price_rules.min_quantity`, `inventories.low_stock_threshold`, barcode counts | `integer` | not stock quantities | keep |

About 140 backend files (DTOs, read models, stores, exporters) and 65 Angular files carry these values.
There are no database triggers or views over them. One check constraint exists: `ck_stock_transfer_items_discrepancy_not_negative`, which stays valid.

Price and cost columns that cannot hold a per-base value: `purchase_order_items.unit_price` (10,2), `grn_items.unit_cost` (10,2), `sale_items.unit_price` (10,2), `sale_items.historical_unit_cost` (10,4), `stock_ledgers.unit_cost` (18,4).

## 2. Accounting and valuation findings

- **One Inventory GL account per business.** `AccountingConfiguration.InventoryAccountId`. Moving value between variants or locations therefore needs no journal. Transfers already post none (`StockTransferCostFlow`).
- **Sale cost** is `SaleItem.HistoricalUnitCost`, a weighted average of all completed GRN lines of the variant, falling back to master cost when no GRN exists. `SaleFinancialIntentFactory.ForSale` sums `cost × quantity` into an immutable snapshot, posted asynchronously (Dr COGS / Cr Inventory).
- **Valuation** replays `stock_ledgers` per variant + location into a weighted-average pool (`WeightedAverageCostPool`, `InventoryValuationReadStore`). It reconciles to the GL Inventory balance.
- **GRN** debits Inventory with `TaxableAmount`, and the ledger line takes the same value (`GrnLedgerCost.Resolve`).
- **Stock adjustments** post a real journal after approval, so an approved write-off route already exists.
- **Refunds:** one reversal per sale. `sale:{id}:reversal:v1` is a unique source key and the sale becomes `refunded`. Partial quantities are allowed, but a second refund on the same sale is impossible. Cumulative multi-refund does not exist.
- **Existing defect that UOM exposes:** sale and refund code find the sale line with `First(VariantId == …)`. Two lines of one variant (a 12 ml pack and a 50 ml pack) would be mismatched. Must be fixed.

## 3. Concurrency and idempotency

- `Inventory` is protected by `xmin`; a conflict surfaces as `SaleInventoryConcurrencyException`.
- Sale idempotency uses a unique key and a request fingerprint (`SaleRequestFingerprint`).
- Stock ledger sequence safety depends on saving the `xmin`-protected inventory row in the same transaction.
- Handlers own their transactions. There is no transaction pipeline behaviour.

## 4. Design decisions (with rationale)

**D-A. Every existing quantity column means base quantity.** Existing readers (ledger, COGS, reports, dashboards, valuation) stay correct with no change in meaning. Entered units live in a nullable snapshot. A null snapshot means legacy `pcs` × 1.

**D-B. Existing `UnitPrice`/`UnitCost` columns are per base unit** at up to 12 decimals, so `Quantity × UnitPrice` still rounds to the exact line amount everywhere. The vendor or pack price is kept in the snapshot, and commit rejects a line where the two do not reconcile to the cent.

**D-C. Unconstrained `numeric` plus a `scale(col) <= 6` check**, not `numeric(24,6)`, for quantities. `ALTER TYPE integer → numeric` keeps scale 0, so legacy values stay `5` on the wire instead of `5.000000`, and Dart `as int` parsing and string formatting keep working. A fixed-scale column would also round silently, which SRS CALC-002 forbids; the check rejects the row instead.

**D-D. Implicit `pcs`.** A variant with no config row behaves exactly as today. No backfill.

**D-E. Selling options live in one table** (`Kind` = `pack` or `custom`), as the LLD §4.1 alternative permits. This gives one component table and one resolver.

**D-F. No quarantine bucket.** The audit found none. Damaged or rejected GRN quantity is recorded on the GRN line as evidence, is not posted to stock, and is not payable to the supplier (PUR-008, PUR-009). The same outcome holds on returns.

**D-G. Repacking uses Pattern P1** (LLD §4.4). With one Inventory GL account, moving bottle value into prepared stock needs no journal.
- The packaging variant's ledger takes a valued `repack_consumption` out-movement.
- The same value enters `pack_state_balances.capitalized_value` on the prepared row, with an audited `pack_state_movements` delta.
- Attar quantity and its weighted-average pool do not change.
- When a prepared pack sells, COGS = content cost + its capitalized share, and Inventory is credited with both.
- Valuation = ledger replay + capitalized pool, so it continues to reconcile to GL. Proven in accounting tests.

**D-H. Non-resellable returns** reverse revenue and VAT but not cost. Stock is not restored, Inventory is not debited, and the cost stays expensed. No new journal type is invented. COGS already carries the loss.

**D-I. Pricing rules do not apply to option-priced lines.** SRS PROD-004 makes pack prices independently configured. Manual discount, VAT and discount-limit rules apply unchanged.

**D-J. Wastage is not part of a repack job.** REP-008 requires an existing approved route; the route is a stock adjustment, which posts its own journal. A job with non-zero wastage is rejected.

## 5. Permissions

Existing keys are reused where they fit: `products.edit` for unit configuration, and `purchases.*`, `grn.*`, `sales.*`, `stock_*` for operations. New keys, for owner review per SRS §18: `uom.view`, `uom.manage`, `inventory.pack_state.view`, `inventory.pack_state.manage`, `inventory.repacking.view`, `inventory.repacking.execute`.

## 6. Dependency map and sequence

```
Quantity widening (schema + 140 files)
  └─ UOM master + versioned config + conversion engine
       ├─ PO / GRN snapshots, physical confirmation ── pack-state balances
       ├─ POS quote + checkout + held sales + receipts ── packaging components
       │     └─ refund / void / exchange (condition, snapshots)
       ├─ transfer / count / adjustment entry
       └─ repack (needs pack-state + packaging + valuation)
            └─ valuation, dashboards, reports
```

## 7. Risks

| Risk | Mitigation |
|---|---|
| `ALTER TYPE` rewrites `stock_ledgers`, `sale_items` and others under an exclusive lock | Maintenance window; documented in the report. Not applied to Development by this work. |
| SQLite legacy tests cannot aggregate `decimal` in SQL | Client-side aggregation where the test provider requires it, or test-provider change; reported if any remain. |
| Flutter parses quantities as `int` in places | Legacy values stay integers (D-C). Mobile support for measured variants is checked and reported separately. |
| Silent mixed-unit totals (`rows.Sum(Quantity)`) | Report totals carry a unit breakdown; dashboards show value or per-unit figures. |
| Offline desktop checkout | Not implemented in this repo, so nothing to reconcile. |
