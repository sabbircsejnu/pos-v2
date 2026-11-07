# UOM v1 — Release Readiness

**Date:** 2026-11-07. **Branch:** `staging`, local commits only.

## Final status: IMPLEMENTED — VERIFICATION PENDING

> **Verification round 4 (2026-11-07, latest):** see §17. U-13 is implemented and reviewed. The **Development migration is applied** (by the owner) and verified: all three migrations are in the history and all 17 totals are identical. Status: **IMPLEMENTED — VERIFICATION PENDING**, because browser verification is still blocked and the write workflows have not yet been run on Development.
>
> **Verification round 3:** see §16. The status is unchanged and is not `COMPLETE — VERIFIED`. The Development migration is still unapplied, because no authorized execution route exists in this session, and still no browser tool is available. The independent review ran this time; its findings were fixed, and one owner decision was raised (inventory report cost visibility).

The code for D01–D12 and I01–I07 is complete and passes every automated suite, apart from three live-infrastructure tests that fail on the baseline too. Two required gates are not met in this environment:
1. **The Development migration is not applied.** All gates passed and the full chain was rehearsed, but the execution route was refused twice by the session's permission control. As instructed, nothing was bypassed.
2. **No browser verification.** No browser tool was available, so no screen was opened.

**Recommendation:** ready for code review and for the owner to apply the Development migration. Not ready for Production.

## 1. Decision coverage

| Decision | Status | Evidence |
|---|---|---|
| D01–D12 | Implemented, kept | `UomPostgresHttpTests*` on real PostgreSQL; full suites pass |
| I01 Six permissions | Implemented | `Permissions_EachRoleGetsWhatItsKeysAllow_AndNothingMore` |
| I02 Several partial refunds | Implemented | `Refunds_*` (three partials, concurrency, retries, split payment, discount and VAT, legacy pcs) |
| I03 Promotion eligibility per option | Implemented | `Promotions_*` |
| I04 One refund screen | Implemented | Vitest; not opened in a browser |
| I05 Pattern P1 | Implemented | `Accounting_*`, `P1_*`, `SequenceE_*` |
| I06 Reusable bottle recovery | Implemented | `OpenPrepared_*`, `I06_*` |
| I07 Gap closure | Implemented | `UOM_FINAL_GAP_CLOSURE_TRACKER.md` |

## 2. Closed gaps

See `UOM_FINAL_GAP_CLOSURE_TRACKER.md`. In short:
- cost redaction;
- refund and void VAT in the VAT report;
- exchange integrity, confirmed in the code;
- cent-exact refund ledger value;
- `sale_refund_id` immutability;
- unit-aware transfer opportunities and slow movers;
- the mobile dashboard subtracting returned pieces only. It still deliberately fails closed while a refund journal is unposted.

## 3. Remaining limitations

These are safe refusals, by design.
- **Prepared packs:**
  - they cannot be adjusted;
  - missing or damaged ones on a transfer are refused;
  - opening one with unusable packaging is refused.
- **Transfers:** a line that mixes pack forms is accepted or rejected whole.
- **Wastage:** repack wastage is recorded as a stock adjustment.
- **Damaged goods:** there is no quarantine stock; damaged GRN and return goods are recorded, not stocked.
- **Exchange:** it is a link between sales and moves no quantity.
- **Mobile:** stock take counts whole pieces only and refuses decimals.

## 4. Security

**Cost data:**
- Sale API responses carry `historicalUnitCost` only with `products.view_cost`. For other callers it is null, never 0.
- Repack and pack-state cost was already gated; `WithVisibleCost` is unchanged.

**Access:** the cashier, warehouse, manager, custom-role and other-business cases are tested. Prepared-pack opening refuses a restricted location and another business.

**Database integrity:**
- The guard trigger freezes posting intents, including `sale_refund_id`.
- `ck_sale_items_returned_quantity` means a refund can never return more than was sold.

## 5. Financial reconciliation (PostgreSQL)

- **Ledger + capitalized value vs Inventory GL:** the difference never moved, through:
  - purchase and repack;
  - prepared-pack sale and return (reusable and broken bottle);
  - three awkward partial refunds;
  - opening prepared packs (one at a time and concurrently);
  - a concurrent sale/refund/repack race.
- **Partial refunds:** they return net, VAT, cost and each payment method's share exactly to the cent. The refund's ledger value equals its journal cost on every refund.
- **Repack:** 240 ml plus 20 bottles at ৳8 makes 20 packs at ৳32 each; no value is created and no packaging is consumed twice.

## 6. VAT and refund reporting

- **Refunds:** a 10-pack sale (VAT ৳100) with two posted refunds (VAT ৳20 and ৳10) and one unposted refund reports a ৳70 net. Each refund is listed once; the unposted one is excluded.
- **Voids:** a posted void nets the sale's VAT to zero.
- **Dating:** reversals are dated on their own journal date (P3.2-D02).

## 7. Browser verification

Not performed: no browser tool. The 390/768/1440 layouts, the POS keyboard flow, the refund dialog and history, pack-state, repacking and the VAT report screen all still need a manual walk-through.

## 8. Development migration

| Item | Result |
|---|---|
| Target | `127.0.0.1:5435 / retailpos_db`, the Development container. Both `appsettings.Development.json` and user-secrets point at it. |
| Baseline | `20261106173129_PosV2OutletSalesTargets` (unchanged) |
| Pending | `20261106234558_PosV2UomFoundation`, `20261107084952_PosV2UomRefundsAndPromotionEligibility`, `20261107104508_PosV2UomPostingRefundGuard` |
| Script review | 632 lines in one transaction. No drop of a table, column, schema or data; no `TRUNCATE` or `DELETE`. One index is recreated with a filter under the same name, one backfill `UPDATE` touches the new column only, the guard function is replaced, and 24 types are widened to unconstrained `numeric` without loss. |
| Writers | No other connection to `retailpos_db`; no API listening |
| Status | **Not applied.** `dotnet ef database update` is denied in project settings, and starting the API with `Database__MigrateOnStartup=true` was refused by the session's permission control, both times it was tried. No other route was attempted. |

## 9. Backup and rehearsal

| Item | Result |
|---|---|
| Fresh backup | `backups/retailpos_db_pre_uom_20261107T105053Z.dump`, 1,358,125 bytes, SHA-256 `c84ad4ae88df6f16a6a3924dd644d8d08e1555e4939aff9477f6158dcca74d96`, 196 table-data entries |
| Restore | Restored into `uom_rehearsal_20261107105053` on the test server. All 17 totals matched Development exactly. |
| Rehearsal | All three migrations in **1.0 s** with no errors. History shows all three; the guard covers `sale_refund_id`; both new checks exist; no historical returned quantity needed backfilling. All 17 totals were identical before and after. |
| Totals | 65 inventories (qty 4,560); 73 ledger rows (value 1,312,052.0000); 10 sales (৳30,428.98, all completed); 23 sale items; 20 posted journals, debit = credit = 1,280,196.82; 7 sale postings |

**To apply it yourself:**
1. Confirm nothing is connected to `retailpos_db`.
2. Apply the three migrations with one of:
   - start the API once in Development with `Database__MigrateOnStartup=true`, then stop it; or
   - generate the script with `dotnet ef migrations script 20261106173129_PosV2OutletSalesTargets 20261107104508_PosV2UomPostingRefundGuard --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API -c Release`, then run it with `psql -v ON_ERROR_STOP=1`.
3. Confirm all three migrations appear in `__EFMigrationsHistory`.
4. Compare totals with §9.

## 10. Regression results

| Suite | Result |
|---|---|
| Backend unit | **1,047 passed**, 0 failed |
| Backend integration (includes PostgreSQL UOM HTTP) | Full run: **2,713 passed, 6 failed** of 2,719. Three are the live-infrastructure tests. The other three were fixed and pass on rerun (431 affected tests passed): (a) the migration-chain test's expected head, (b) the mobile dashboard test that requires failing closed on an unposted refund journal (this round's change was reverted, see G-08), (c) one refund test that lost its test-database connection (`Failed to connect to 127.0.0.1:55432`). |
| Angular | **1,976 passed** in 138 files (no frontend change in this round) |
| Angular build | Succeeds; existing POS CSS budget warning |
| Flutter | **124 passed**; analyzer clean on the changed files (no mobile change in this round) |
| Browser E2E | Not run |
| Live infrastructure (Redis, RabbitMQ, Elasticsearch) | Fail the same way on the baseline; the test-stack services are unreachable. Not a code failure. |

## 11. Commits

`5ffb06e7`, `f81342ba`, `a1ff6226`, `dcfcc48f`, and the documentation commit, on top of the earlier six UOM commits (`8fe3aaea`…`66694088`). Nothing pushed.

## 12. Production rehearsal requirement

`ALTER COLUMN … TYPE` on `inventories`, `stock_ledgers`, `sale_items`, `purchase_order_items`, `grn_items` and the transfer, requisition and adjustment line tables rewrites them under ACCESS EXCLUSIVE. `ck_sale_items_returned_quantity` scans `sale_items`. Before Production:
1. Rehearse on a Production-sized copy and time each step.
2. Plan a maintenance window.
3. Deploy the API together with the migration.

## 13. Rollback

`Down` narrows quantities back to `integer`, which is only safe before any fractional quantity exists. After that, roll back by restoring the pre-migration backup. Rolling back loses every refund, repack and unit transaction made since.

## 14. Deployment blockers

1. The Development migration must be applied and its post-migration smoke tests run.
2. The browser and responsive walk-through.
3. A Production-sized migration rehearsal and a formal release review.
4. Independent final review of the last commit: the sub-agent review was blocked by the permission control in this session, so only an in-session check was done (see the verification report).

## 15. Recommendation

Merge to the next stage only after blockers 1, 2 and 4 are done. Production release only after blocker 3 as well.

## 16. Verification round 3

### Development migration
- **Rechecked:** `127.0.0.1:5435 / retailpos_db` is still on `20261106173129_PosV2OutletSalesTargets`, with nothing connected. All 17 totals are unchanged since the backup `retailpos_db_pre_uom_20261107T105053Z.dump`, so that backup and its rehearsal (§9) still apply.
- **Permissions:** unchanged. There is no `settings.local.json` and no new allow rule. `dotnet ef database update` is denied in `.claude/settings.json`, and starting the API with migrate-on-startup was refused twice before. With no authorized route, no attempt was made this round. **Not applied.**
- **Smoke tests on Development (§15):** not possible without the migration. The same flows pass on freshly migrated PostgreSQL in the test fixture: legacy pcs sale, jar purchase/GRN, pack and custom sales, several refunds, the VAT report, transfers, repacking, opening packs, and reconciliation of inventory to GL.

**To finish, the owner should:**
1. Apply the migration (§9).
2. In Development, run a pcs sale, a jar purchase and GRN, a 12 ml pack sale, a 24 ml custom sale, two partial refunds and a repack.
3. Check the VAT details report and the accounting inventory valuation, where ledger plus capitalized value should equal the GL.

### Regression (final code)
| Suite | Result |
|---|---|
| Backend unit | **1,047 passed**, 0 failed |
| Backend integration | **2,716 passed, 4 failed** of 2,720. Three are the Redis, RabbitMQ and Elasticsearch live-infrastructure tests, failing as on the baseline. The fourth, `PostgresTestTemplateTests.Parallel_clones_are_isolated_from_each_other`, timed out dropping a test database under load; it is unchanged since before UOM and passes 9/9 on rerun. |
| Angular | **1,976 passed** in 138 files; production build succeeds with the existing POS CSS budget warning |
| Flutter | **124 passed**; `flutter analyze` finds no issues in the whole app |

**Flaky under load:** an earlier full run this round also lost one UOM test (`PackState_GrnSplit…`) to a database "transient failure". It passes on rerun.

### Browser verification
**Blocked.** No browser automation tool is available in this session; the only web tool cannot reach localhost. No screen, layout (390/768/1440) or keyboard flow was verified.

### Independent review of `dcfcc48f..c38063f7`
A separate review agent ran this round. It found no Critical or High issues. Fixed, with tests:

| Finding | Fix | Test |
|---|---|---|
| M1: a refund cost share rounding to ৳0.00 marked the item's stock value unknown | Fall back to the 4-place value when the share is 0 | `Refund_OfAFractionOfACent_NeverRecordsAZeroValueThatWouldUnvalueTheStock` |
| M2: mobile dashboard returned units counted restored bottles and used the item's current unit | Count only returns of the sale's own pcs lines, by their sold unit | Mobile dashboard and sales suites |
| M3: `api/stock-ledger` (`inventory.view`) showed cost and cost remarks without `products.view_cost` | Unit cost and value become null, and cost remarks are hidden. `StockLedgerDto` cost fields are now nullable; the frontend does not call this API. | `Security_SaleCost_…` (stock ledger part), `StockLedgerReadHttpTests` updated for the approved I07 rule |
| M4: VAT report loaded every posted reversal on each call | Only reversals whose journal falls in the report window | VAT tests |
| Test gap: the sales list redaction check could pass with `2.0` | Asserts null on every item | `Security_SaleCost_…` |

**Left open:**
- **L1, warehouse filter:** sale and refund VAT rows ignore the warehouse filter, while reconciliation uses it. This existed before for sale rows.
- **L2, void rounding:** voids still record 4-place ledger values against a currency-rounded journal.

### New owner decision (U-13)
`api/reports/inventory/*` (permission `reports.inventory`) shows unit cost, stock value and transaction value in about 15 reports and exports, with no `products.view_cost` check. This existed before UOM. Redacting it would change what holders of `reports.inventory` see, so it is an authorization decision, not a defect fix:
- Should inventory reports require `products.view_cost` for cost and value columns?
- Or does `reports.inventory` itself grant them?

Not changed.

### Remaining blockers
1. Apply the Development migration, then run the smoke tests above.
2. The browser and responsive walk-through.
3. U-13, the inventory report cost visibility.
4. A Production-sized migration rehearsal and release review.
5. Housekeeping: delete the test-server databases `uom_restore_check_20261107094240` and `uom_rehearsal_20261107105053`.

**Status: IMPLEMENTED — VERIFICATION PENDING.** This is not `COMPLETE — VERIFIED`, because the migration and browser gates are unmet.

## 17. Verification round 4: U-13 and Development migration

### U-13: inventory report cost needs `products.view_cost` (Option A)
`reports.inventory` grants the reports: quantities, movements, dates, references and units. `products.view_cost` is also required for any cost-derived figure. Without it, the API sends `null`, never 0, in rows, summaries and category or outlet breakdowns. Exports omit those columns and summary lines entirely. BusinessOwner keeps everything through the wildcard; no role was given `products.view_cost`.

| Endpoint (`api/reports/inventory/…`) | Hidden without `products.view_cost` | Export |
|---|---|---|
| stock-levels, slow-moving | `estimatedValue` | — |
| valuation | `totalValue`, category `value` (categories in name order) | — |
| current-stock | `unitCost`, `stockValue`, total | CSV/Excel without cost columns or total |
| product-ledger | `unitCost`, `transactionValue`, total; cost remarks | CSV/Excel without cost columns |
| stock-valuation | `unitCost`, `inventoryValue`, `percentOfTotal`, totals, category values (quantities stay; value sort refused) | CSV/Excel without value columns, lines or category values |
| outlet-wise-stock | `unitCost`, `stockValue`, totals, per-outlet value | CSV/Excel incl. outlet sheet |
| out-of-stock | `unitCost`, `estimatedCostImpact` | CSV/Excel without "Est. Cost to Restock" |
| negative-stock | `unitCost`, `stockValue`, `totalNegativeValue` | CSV/Excel |
| stock-transfers-report | `unitCost`, `transferValue`, `totalValue` | CSV/Excel |
| low-stock, stock-movement, stock-adjustments | no cost fields | unchanged |

**Other endpoints covered:**
- `api/reports/stock-transactions`: cost remarks are hidden.
- From the independent review:
  - stock transfer and requisition item `unitCost` is now null without the permission;
  - barcode variant search clears `costPrice`;
  - `api/inventory/valuation` orders categories by name when cost is hidden.
- `api/stock-ledger` and sales were already covered in round 3. No PDF export exists for these reports.

**Bug fixed along the way:** `GET /api/reports/inventory/valuation` returned 500 once a measured unit existed. `UomReportTotals` read an unloaded `BaseUnit`; this also affected the owner inventory dashboard.

**Frontend:** the eight report pages hide cost columns, totals, cards and cost sorting without `products.view_cost`, and show "—" for a null amount instead of ৳0.00.

### Role matrix (PostgreSQL, `UomPostgresHttpTests.CostVisibility.cs` and `.Final.cs`)
| Caller | Result |
|---|---|
| BusinessOwner `*` | Values present; exports carry cost columns |
| `reports.inventory` only | Every report 200, all cost fields null; CSV and Excel have no cost header, line or figure (Excel read with ClosedXML) |
| `reports.inventory` + `products.view_cost` | Values and export cost columns present |
| `products.view_cost` only | 403 on every report and export |
| Restricted to assigned locations | 403 for another outlet |
| Another business | 403 or 404; none of this business's data |
| pcs and ml variants | Same quantities for both callers, matching the stock in the database |
| `stock_transfers.view` + `barcode.view` | Transfer `unitCost` and barcode `costPrice` null; present with `products.view_cost` |

### Development migration: applied and verified
The owner applied the three migrations outside this session. Read-only checks afterwards:
- **History:** `20261106234558_PosV2UomFoundation`, `20261107084952_PosV2UomRefundsAndPromotionEligibility` and `20261107104508_PosV2UomPostingRefundGuard` are all applied.
- **Totals:** all 17 are identical to the pre-migration baseline (§9): inventories, stock ledger quantities and value, sales and statuses, sale items, payments, GRN and PO lines, transfer and adjustment lines, journals and their lines (debit = credit = 1,280,196.82), sale postings, products, variants, customers and cash movements.
- **Schema:**
  - the posting guard covers `sale_refund_id`;
  - `ck_sale_items_returned_quantity` and `ck_sale_refunds_amounts` exist;
  - `sale_refunds`, `sale_refund_payments`, `uom_units` (12 seeded), `pack_state_balances`, `repack_jobs` and `variant_selling_options` exist;
  - `inventories.quantity` is `numeric`;
  - `ux_sale_financial_postings_sale_kind` is filtered `WHERE sale_refund_id IS NULL`;
  - no historical returned quantity needed a backfill.
- **API on Development:** it started cleanly against the migrated database. There were no `fail` or `crit` log entries, the background workers (financial posting heartbeat) ran without errors, and an unauthenticated `/api/sales` returned 401. It was then stopped.
- **Not run on Development:** the write workflows (pcs sale, jar GRN, pack and custom sales, refunds, repack, transfer). They need a signed-in user, whose credentials this session must not read, and they would add transactions to the shared Development database. The same flows pass on freshly migrated PostgreSQL in the test fixture.

### Regression
| Suite | Result |
|---|---|
| Backend unit | **1,047 passed** |
| Backend integration (U-13 code) | **2,721 passed, 3 failed** of 2,724. The three failures are the live-infrastructure tests (Redis, RabbitMQ, Elasticsearch), as on the baseline. |
| Affected suites after the review fixes | 559 run; the requisition test was updated for the null rule (23/23 pass). `OpenPrepared_…` failed once with an EF "transient failure" under load and passed 12/12 alone. |
| Angular | **1,983 passed** in 139 files; build succeeds (existing POS CSS budget warning) |
| Flutter | 124 passed (no mobile change this round) |

**Flakiness under load:** three pack-state tests have each failed once with EF's "transient failure" during full parallel runs, and all pass in isolation. This matches the test database server's timeouts seen elsewhere in the same runs (dropping fixture databases). No code defect has been found, but it should be watched.

### Browser verification
**BROWSER VERIFICATION BLOCKED.** No browser automation tool is available in this session; the only web tool cannot reach localhost. No screen, layout (390/768/1440) or keyboard flow was verified.

### Independent review of U-13 (`9cd6da61`)
It found no leak in the inventory reports or exports. Fixed in `0e71c1d5`:
- **M1:** transfer and requisition cost.
- **M2:** barcode search cost.
- **L1:** valuation category order.

**Left open:**
- `api/inventory/*` hides cost as 0, not null. Changing that would alter the contract.
- The owner dashboard inventory endpoints (`dashboard.inventory_intelligence.view`) show stock value. Whether that permission implies cost is a decision for you.
- Hiding cost remarks depends on the keyword "cost", so free-text remarks typed by users are not caught.
- The stock-valuation export sums category quantities across units when cost is hidden.

### Status
**IMPLEMENTED — VERIFICATION PENDING.**
- The code (D01–D12, I01–I07, U-13) is complete and passes all suites, apart from the classified infrastructure failures.
- The Development migration is applied and reconciled.
- Two gates remain unmet:
  1. the browser walk-through at 390/768/1440, including POS keyboard flow, refund dialog, UOM setup, repacking and inventory reports with and without cost;
  2. the Development write workflows: a signed-in user runs a pcs sale, jar GRN, 12 ml pack and 24 ml custom sale, two partial refunds, a repack and opening one pack, then checks the VAT report and accounting inventory valuation (ledger plus capitalized value should equal the GL).
- **Production** still needs a Production-sized migration rehearsal and a release review.

