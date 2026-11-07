# UOM v1 — Release Readiness

**Date:** 2026-11-07. **Branch:** `staging`, local commits only.

## Final status: IMPLEMENTED — VERIFICATION PENDING

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
