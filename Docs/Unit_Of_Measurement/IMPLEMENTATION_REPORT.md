# UOM v1 — Implementation Report

> **Update 2026-11-07:** the owner approved decisions I01–I05, and the follow-up work is in `UOM_FINAL_VERIFICATION_REPORT.md`, which supersedes §2, §8 and §11 below where they differ. In short:
> - **I02:** a sale can be refunded several times.
> - **I03:** promotions apply per selling option.
> - **I04:** the refund button shows for every eligible sale.
> - **I01 and I05:** confirmed by tests.
> - **Gaps closed:** whole-pack partial transfer receipt, opening prepared packs, and GRN lines with nothing accepted.
>
> The status is still IMPLEMENTED — VERIFICATION PENDING, because the Development migration could not be applied from this session and no browser was available.
>
> **Later the same day:** I06 and I07 were closed (cost redaction, refund VAT reporting, exact refund ledger value, the posting guard, unit-aware transfer opportunities). See `UOM_FINAL_GAP_CLOSURE_TRACKER.md` and `UOM_RELEASE_READINESS.md`.

**Date:** 2026-11-07. **Branch:** `staging`, local commits `8fe3aaea` to `781ba61b` plus this report; nothing pushed.

## Final status: IMPLEMENTED — VERIFICATION PENDING

All D01–D12 flows are built and integrated, and every automated test that can pass here passes. Status is not `COMPLETE — VERIFIED` because:

- No screen has been exercised in a browser, so the SRS end-to-end workflows and the 390/768/1440 layout checks are unverified.
- The migration has not been applied to Development.
- The decisions in §11 are still open.

## 1. Implementation summary

A product can now be bought in jars, stocked in ml, and sold as fixed packs or custom quantities. Packaging, sealed/loose stock and repacking are optional per variant. A variant with no configuration behaves as plain `pcs`, as before.

**The core rule:** every existing quantity column holds base units. What the user entered lives in a nullable snapshot on the line, and a null snapshot means legacy `pcs` × 1. Existing stock, COGS and report code therefore kept its meaning without rewriting history.

The Attar example, verified on PostgreSQL: 1 jar at ৳1,000 becomes 500 ml at ৳2/ml; 2 × 12 ml packs deduct 24 ml at ৳200; 240 ml plus 20 bottles at ৳8 become 20 prepared packs at ৳32 each.

## 2. Decision coverage

| Decision | Status | Notes |
|---|---|---|
| D01 Unit and pack prices, custom quantities | Done | Prices stand alone; automatic promotions apply per option when switched on (I03) |
| D02 Hybrid conversions | Done | Physical units use standard factors; packaging sizes are per product |
| D03 Fixed packs and custom quantity at POS | Done | Keyboard picker; browser unverified |
| D04 Packaging inventory and cost | Done | Consumed once, in COGS; shortage blocks the sale |
| D05 Configurable precision | Done | 0–4 per unit; excess is rejected, never rounded |
| D06 Multiple purchase units and packs | Done | With defaults |
| D07 Versioning and snapshots | Done | Stale requests get `UOM_CONFIG_CHANGED` |
| D08 Condition-based returns | Done | Several partial refunds per sale since I02 (see the verification report) |
| D09 Unit-aware transfer, count, adjustment | **Done with limits** | See §11 for sealed and prepared limits |
| D10 Sealed and loose | Done | Multiple sizes; open is audited; balances reconcile to stock |
| D11 GRN physical confirmation | **Done with limit** | Damaged stock is recorded, not stocked or payable; no quarantine bucket |
| D12 Repacking | Done | Value conserved; wastage refused; whole-job reversal only |

## 3. Changed modules and important files

345 files changed against `f9508fe8`.

- **Rules:** `src/RetailPOS.Application/Uom/` (`UomCore.cs`, `UomConfigurationCommands.cs`, `PackStateAndRepackCommands.cs`, `PurchaseUomRules.cs`, `InventoryUomRules.cs`)
- **Sale:** `Application/Sales/Checkout/SaleUomLines.cs`, and the Create, Refund and Void sale commands
- **Persistence:** `src/RetailPOS.Infrastructure/Uom/UomStores.cs`, `Data/UomModelConfiguration.cs`
- **Domain:** `src/RetailPOS.Domain/Entities/Uom/UomEntities.cs`
- **API:** `src/RetailPOS.API/Controllers/UomController.cs`
- **UI:** `retailpos-frontend/src/app/pages/uom/`, `pages/pos/pos-uom*`, `services/uom.service.ts`

## 4. Database migration

`20261106234558_PosV2UomFoundation`:

- Widens quantity columns from `integer` to unconstrained `numeric`, with a `scale(col) <= 6` check on the main ones.
- Widens per-unit price and cost columns.
- Adds nullable snapshot columns and the 14 new tables.
- Seeds 12 global units and 3 conversions.

No existing row is rewritten. Unconstrained `numeric` keeps legacy values as `40`, not `40.000000`, so clients see the same numbers.

## 5. Backend changes

- `int` to `decimal` across about 140 files, including truncating casts and the stock cache's integer parsing.
- Sale, void, refund, PO, GRN, purchase-and-receive, transfer, adjustment and count flows are unit-aware.
- Fixed an existing defect: sale code matched lines by variant, which breaks with two packs of one variant.
- New permissions: `uom.view`, `uom.manage`, `inventory.pack_state.view`, `inventory.pack_state.manage`, `inventory.repacking.view`, `inventory.repacking.execute`.
- Repack costs and capitalized values follow `products.view_cost`.

## 6. Frontend changes

- **New screens:** Units of Measurement, per-variant Units & Pricing, Pack State, Repacking.
- **Unit-aware:** PO, GRN, POS, sale details, transfers, adjustments, counts, inventory lists, reports, owner dashboard.
- **New refund dialog:** the frontend had no refund UI. It appears only for sales with at least one unit line, so `pcs`-only sales look as they did (§11).

## 7. Inventory and accounting design

There is one Inventory GL account per business, which makes repacking safe without a new journal (Pattern P1):

- The bottle leaves its own pool in a valued ledger row, and that value moves into `pack_state_balances.capitalized_value`.
- Attar quantity and its pool do not change. The repack posts no journal.
- Selling a prepared pack takes content cost plus its capitalized share into COGS.
- Valuation = ledger replay + capitalized value.

**Proven:** `Accounting_LedgerPlusCapitalizedValueStaysEqualToTheGeneralLedger` follows purchase, repack and a posted sale, and the reconciliation difference does not move.

A non-resellable return reverses revenue and VAT but not cost: stock is not restored and the cost stays expensed.

## 8. Test results

| Suite | Command | Result |
|---|---|---|
| Backend unit | `dotnet test tests/RetailPOS.UnitTests -c Release` | **1,037 passed, 0 failed** |
| Backend integration | `dotnet test tests/RetailPOS.IntegrationTests -c Release` | **2,681 passed, 3 failed**, 0 skipped |
| Frontend | `npx ng test --watch=false` | **1,951 passed**, 138 files |
| Frontend build | `npm run build` | Succeeds; one CSS budget warning on the POS screen, already over before this work |

UOM end-to-end tests are in `tests/RetailPOS.IntegrationTests/Uom/UomPostgresHttpTests.cs`, on real PostgreSQL with real migrations and HTTP.

The frontend suite and build ran before the last backend-only commit changes; no frontend file changed after them.

**The 3 failures** are the Elasticsearch, RabbitMQ and Redis `LiveInfrastructureTests`. They also failed before this work, on the untouched baseline (RabbitMQ could not reach port 5673). They are unrelated to UOM.

**Not run:** browser or end-to-end workflows, Flutter tests, lint (no lint script exists).

## 9. Regression results

| Check | Before | After |
|---|---|---|
| Unit tests | 1,013 passed | 1,037 passed |
| Integration tests | 2,648 passed, same 3 failed | 2,681 passed, same 3 failed |

Existing tests were changed only for types and two real requirement changes:

- `It.IsAny<int>` became `decimal`.
- The migration chain test now names the new migration and expects `stock_ledgers.unit_cost` to be widened.

**Migration trial on real data:** a copy of Development ran the migration in 2 seconds. Row counts and totals for inventory, stock ledger, sale items, GRN items and journal entries were identical before and after. The copy was then dropped. Development data is small, so this says nothing about lock time on Production.

**Independent code review** found 10 issues, and all were addressed:

- **Fixed, with a new test:** sealed-pack detection after a config change, fractional `pcs` on transfer receipt, location checks on repack reads, and the idempotency fingerprint.
- **Fixed, with no dedicated test:** lock ordering, audits inside transactions, refund after tracking is switched off, and the pricing rule recorded on pack lines. The full suites still pass, but nothing forces these specific scenarios.
- **Reduced, not eliminated:** deadlocks and unique-key races now return a retryable 409 instead of a 500, and a tracking switch now waits for in-flight stock transactions. These races can still occur, and neither behaviour has been forced in a test.

The review did not cover report queries beyond valuation, the adjustment lifecycle commands, or the mobile count paths in depth.

## 10. Environment limitations

- Redis, RabbitMQ and Elasticsearch on the test stack were unreachable.
- No browser tool was used in this session.
- Development stays on the previous migration by decision: the type change is not additive, project rules require approval, and `dotnet ef database update` is denied in project settings.

## 11. Blockers and incomplete items

**Decisions for the owner**

| # | Decision | Current behaviour |
|---|---|---|
| 1 | Approve the six new permission keys (LLD §18 requires owner review) | Keys are live. `BusinessOwner` gets them by wildcard; other roles must be granted them in Role Edit. SQL seeds not edited. |
| 2 | Allow more than one refund per sale? (RET-002 expects a cumulative return) | One refund per sale. The reversal journal key allows only one, so changing this is an accounting decision. |
| 3 | Should pricing rules apply to pack and custom prices? | They do not. Manual discount and VAT do apply. |
| 4 | Show the new refund button for `pcs`-only sales? | Hidden, because those sales had no refund UI. One-line change. |
| 5 | Repack value treatment (Pattern P1, §7) | Implemented and proven. LLD §23 asks for owner sign-off. |

**Known limits (safe failures)**

- **No quarantine bucket:** damaged GRN stock is recorded but not held in stock. A line with zero accepted cannot be recorded on a GRN.
- **Transfers:** sealed or prepared lines are accepted or rejected whole. A loss is recorded separately as an adjustment.
- **Adjustments:** prepared packs cannot be adjusted. Reverse the job if unsold.
- **Counts:** a variance posts against loose stock. If sealed or prepared counts differ from the system, the count is refused until those packs are opened or adjusted. A count draft generates loose adjustment lines.
- **Prepared packs:** a single pack cannot be opened, because its bottle's value has no approved destination. Only a whole-job reversal exists.
- **Slow-mover thresholds** are not unit-aware, so a velocity in ml/day is compared with thresholds set for pieces. Related to existing open decision B-04.
- **Operational stock valuation report** still uses master cost × base quantity, so master cost must be per base unit for measured items. Accounting valuation and dashboards use the ledger.
- **A measured item with no GRN** falls back to master cost at sale, as `pcs` does today.
- **Product ledger report** totals can still mix units when a product's variants differ; the UI labels them "Mixed units".
- **"Total (pcs)" labels** react to the visible page only.
- **A product with no variant rows** has no unit setup link, because there is no variant to configure.

**Mobile: not updated.** The Flutter stock take uses `int` quantities and would ignore a fractional entry (`stock_take_page.dart:755`, `stock_take_presentation_models.dart:167,177-179`). It is not wired to the API yet, so nothing fails today. Fix before connecting it.

**Offline desktop checkout** is not implemented in this repo, so there was nothing to reconcile.

## 12. Deployment and migration instructions

1. Back up the database.
2. Review the SQL: `dotnet ef migrations script 20261106173129_PosV2OutletSalesTargets 20261106234558_PosV2UomFoundation --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API`.
3. Plan a maintenance window. `ALTER TYPE` rewrites `inventories`, `stock_ledgers`, `sale_items`, `purchase_order_items`, `grn_items` and the transfer, requisition and adjustment line tables under an exclusive lock. Rehearse on a Production-sized copy to measure the time.
4. Deploy the migration and the API together. The new API needs the new columns.
5. Grant the new permissions to roles in Role Edit.
6. Rollback: `Down` narrows quantities back to `integer`, so it is only safe before any fractional quantity is written. After that, restore from backup.

## 13. Before calling this verified

1. Apply the migration to Development, once approved.
2. Walk the SRS §20 scenarios in a browser, including POS keyboard use and 390/768/1440 layouts.
3. Have Finance review §7 and the §11 decisions.

**Final completion status: IMPLEMENTED — VERIFICATION PENDING**
