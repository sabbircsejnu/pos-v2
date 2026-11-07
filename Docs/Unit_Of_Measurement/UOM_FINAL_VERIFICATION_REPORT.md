# UOM v1 — Final Verification Report

**Date:** 2026-11-07. **Scope:** D01–D12 and owner decisions I01–I05.

## Final status: IMPLEMENTED — VERIFICATION PENDING

The code for D01–D12 and I01–I05 is in place and tested on real PostgreSQL, but the status rules for `COMPLETE — VERIFIED` are not met:

- **The Development migration is not applied.** Every safety gate passed (§D), but applying it was blocked by this environment's permission controls. The brief says not to bypass such a block, so it was not.
- **No browser verification was possible.** This session had no browser tool, so no screen was opened and the 390/768/1440 layouts are unchecked (§F).
- The post-migration workflow checks on Development depend on both of the above.

## A. Git state

| Item | Value |
|---|---|
| Branch | `staging` (no new branch) |
| Baseline | `66694088` (the six earlier UOM commits are kept) |
| New commits | `5ffb06e7` (I01–I04 and gap fixes), `f81342ba` (I01/I05 proofs, mobile guard), and the commit carrying this report |
| Working tree | Clean after the final commit, except the untracked, gitignored backup in `backups/` |
| Push / deploy | Nothing pushed, merged or deployed. Production untouched. |

## B. Decision coverage

"DB verified" means checked on real PostgreSQL through the HTTP pipeline and real migrations in the test fixture, not on Development.

| Decision | Implemented | Tested | Browser | DB verified | Remaining limits |
|---|---|---|---|---|---|
| D01 Unit/pack prices | Yes | Yes | No | Yes | — |
| D02 Hybrid conversions | Yes | Yes | No | Yes | — |
| D03 Packs and custom POS quantities | Yes | Yes | No | Yes | Keyboard flow unverified in a browser |
| D04 Packaging inventory and cost | Yes | Yes | No | Yes | — |
| D05 Configurable precision | Yes | Yes | No | Yes | — |
| D06 Multiple purchase units/packs | Yes | Yes | No | Yes | — |
| D07 Versioning and snapshots | Yes | Yes | No | Yes | — |
| D08 Condition-based returns | Yes | Yes | No | Yes | No quarantine stock; exchange is a link only (§G) |
| D09 Unit-aware transfer/count/adjustment | Yes | Yes | No | Yes | Mixed-form transfer lines are still taken whole |
| D10 Sealed/loose | Yes | Yes | No | Yes | — |
| D11 GRN physical confirmation | Yes | Yes | No | Yes | A fully rejected delivery is not a GRN |
| D12 Repacking | Yes | Yes | No | Yes | Wastage via stock adjustment only |
| I01 Six permissions | Yes | Yes | No | Yes | — |
| I02 Multiple partial refunds | Yes | Yes | No | Yes | — |
| I03 Promotion eligibility per option | Yes | Yes | No | Yes | Ambiguous rules are skipped and named, by design |
| I04 Unified refund UI | Yes | Vitest | No | n/a | Not opened in a browser |
| I05 Pattern P1 | Yes | Yes | No | Yes | Unpack-recovery interpretation (§G) |

### I01 — permissions
The six keys are unchanged and there are no duplicates. `BusinessOwner` holds them through the wildcard; no other role is granted anything automatically. A server-side matrix test covers:
- cashier and warehouse staff: they read unit setups through their own operational permissions, but cannot manage units, open packs or repack;
- a manager given only view keys;
- a custom role with an unrelated permission: refused;
- another business's owner: cannot read or act on this business's items.

### I02 — multiple partial refunds
- **Records:** each refund is a `sale_refunds` row with its own number (`INV-…-R2`), lines, payment split, idempotency key and fingerprint, cash amount, and its own journal intent.
- **Journal identity:** the first refund of a sale keeps the identity refunds always had (`sale:{id}:reversal:v1`, journal reference = sale id), so a single refund looks exactly as before. Later refunds use `sale:{id}:refund:{n}:v1` and reference `{id}:R{n}`. Historical refund journals are never touched or reposted.
- **Exact shares:** net, VAT, cost and packaging are taken cumulatively. Each refund gets `share(returned before + now) − share(returned before)`, so any number of partial refunds adds up to the sale to the cent. Each payment pays back in proportion to what it still holds, so no method ever pays back more than it took.
- **The drawer matches the journal:** the cash paid out equals the journal's cash credit. A stated cash amount can only confirm that figure.
- **Concurrency:** the sale row is locked (`FOR UPDATE`) before reading, and a database check (`returned_quantity <= quantity`) backs the rule. A key replay returns the earlier result with no new effect, and a reused key with a different request gets 409.
- **Older clients:** a client that sends no key keeps one refund per sale, with the old message and status code.
- **Status:**
  - `partially_refunded` while something remains; `refunded` once nothing does.
  - Reports, dashboards, cash-session totals, reprint and void rules all handle the new value.
  - Sales refunded before this change stay closed; their returned quantities were backfilled from their posting snapshots.

### I03 — promotion eligibility
- **Setting:** `ApplyAutomaticPricingRules` per selling option. New packs default off, new custom options on. A save that does not state it keeps the option's previous value, so nothing becomes eligible by accident. Migrated rows start off.
- **Engine:** the existing pricing engine picks the rule (priority, then id, then target).
- **Thresholds:** a pack's `min_quantity` counts packs, never millilitres. A percentage rule discounts the pack or custom line; a fixed rule takes its amount off each pack.
- **Rules that cannot apply to a custom quantity:** a fixed amount, or any minimum above 1, has no meaning for millilitres. Such a rule is not applied, and the POS names it under the line.
- **Snapshots and stale prices:** the applied rule and effective price are snapshotted on the sale line. `expectedLineGross` refuses a sale whose promotion changed after the cart was priced. Manual discount and VAT apply afterwards as before, and plain `pcs` pricing is untouched.

### I04 — unified refund UI
- **One dialog for every sale:** `pcs`, unit and mixed. For each line it shows the original unit, sold, previously returned and remaining quantities, condition, an estimated amount, and the refund history.
- **Same safeguards for all:** the server enforces them identically for every sale type.

### I05 — Pattern P1
All twelve proofs required by the brief are covered by tests (§E). One interpretation needs Finance's confirmation (§G, item 1).

## C. Gaps from the earlier report

| Gap | Root cause | Fix | Tests | Status |
|---|---|---|---|---|
| One refund per sale | Reversal key and unique index per sale | I02 above | `UomPostgresHttpTests.Refunds.cs`, updated sale suites | Closed |
| Sealed/prepared transfer whole-line only | Receipt had no pack-level split | A single-form line can be received in part, in whole packs. Rejected packs return as packs with the remaining capitalized value. Fractions are refused. | `Transfer_TenSealedJarsDispatched…`, `Transfer_PreparedPacks_SplitIntoWholePacks…` | Closed for single-form lines. A missing prepared pack is still refused (§G). |
| Prepared packs cannot be opened | No per-pack treatment | `POST /api/v1/uom/pack-state/open-prepared`: content becomes loose without moving, and the bottle returns to packaging stock with its capitalized share. Refused if the bottle is not reusable, or if the option has several packaging items. | `OpenPrepared_OnePack…` | Closed, pending §G-1 |
| Prepared packs cannot be adjusted | Bottle value has no destination | Unchanged: refused | — | Open (needs a policy) |
| Repack wastage | No approved loss account | Unchanged: refused, and the message points to stock adjustment | — | By design |
| GRN line with nothing accepted | Received quantity had to be over zero | A line may carry only damaged or rejected quantity. It posts no stock, value or payable, and a GRN still needs one accepted line. | `Grn_ALineWithNothingAccepted…` | Closed |
| Slow movers compared ml/day with pieces/day | One velocity threshold for all units | Measured units use stock cover (beyond 60 days is slow) and need only some stock to qualify. `pcs` is unchanged. | `UnitAwareSlowMoverTests` (clothing, Attar, fabric, grocery) | Closed |
| Operational valuation used master cost | A deliberate estimate screen | Labelled "Estimated value (master cost)" with a pointer to book value. The accounting valuation stays ledger + capitalized. | — | Closed by labelling |
| Mixed-unit totals | `pcs`+ml summed | The inventory report total is pcs only and labelled so. The earlier v1 work already grouped other totals. | — | Partly closed (§G) |
| Product with no variants | Inventory is per variant | No change. A product without a variant cannot hold stock, so it has nothing to configure. | — | By design |
| Flutter integer counts | `int.tryParse` silently dropped "2.5" | Such counts are now refused with a message ("count measured items on the web") | 2 Dart tests | Isolated. Decimal mobile counting is not built. |
| Race and regression coverage | Fixes without tests | Concurrent refunds and same-key retries, the database check, the sale/refund/repack race, refund after tracking off, applied rule recorded, fingerprint | Refunds, Accounting and Promotions test files | Closed (§F notes what remains untested) |

## D. Database migration

| Item | Result |
|---|---|
| Target | `127.0.0.1:5435 / retailpos_db`. Both `appsettings.Development.json` and user-secrets point at it, as does the container `iranimart-dev-postgres-1`. Classified as Development. |
| Baseline | `20261106173129_PosV2OutletSalesTargets` |
| Migrations | `20261106234558_PosV2UomFoundation`, `20261107084952_PosV2UomRefundsAndPromotionEligibility` |
| SQL review | 607 lines in one transaction. No dropped table, column, schema or data, and no `TRUNCATE` or `DELETE`. One index is dropped and recreated with a filter, under the same name. One `UPDATE` fills the new `returned_quantity` column. 24 column types are widened to unconstrained `numeric`, without loss. |
| Backup | `pg_dump -Fc` → `backups/retailpos_db_pre_uom_20261107T094240Z.dump`, 1,358,125 bytes, SHA-256 `903d3341…fd9818`. It was restored into a throwaway database on the test server, and every total matched Development. |
| Space | 941 GB free; the database is 29 MB |
| Writers | No other connection to `retailpos_db`, and no API listening |
| Rehearsal | The exact script ran on the restored copy in **1.4 s**, and all 17 totals were identical before and after (below). |
| Execution on Development | **Not applied.** `dotnet ef database update` is denied in project settings. The project's own Development-only route, starting the API once with `Database:MigrateOnStartup=true`, was blocked by the session's permission control. Following the brief, no other route was tried. Development is confirmed still on the baseline migration. |
| Rollback | `Down` narrows quantities back to `integer`, so it is only safe before any fractional quantity exists. After that, restore the backup above. |

Baseline totals (Development before, the restored copy, and the rehearsal after migration were all identical):

```
inventories 65 rows, quantity 4560 · stock_ledgers 73, in/out 1522/52, value 1,312,052.0000
sales 10 (all completed), total 30,428.98, tax 2,141.38 · sale_items 23, qty 41, subtotal 30,468.97
sale_payments 7, 28,499.07 · grn_items 23, qty 1701, value 1,243,620.00 · purchase_order_items 25
journal_entries 20 posted · lines 64, debit = credit = 1,280,196.82 · sale_financial_postings 7 (sale)
products 11 · variants 45 · customers 7 · cash_session_movements 1, -1,050.00
```

To apply it yourself, choose one route:
- **Migrate on startup:** start the API once in Development with `Database__MigrateOnStartup=true`, then confirm both migrations appear in `__EFMigrationsHistory`.
- **Run the reviewed SQL:** regenerate it with the `dotnet ef migrations script` command in `IMPLEMENTATION_REPORT.md` §12, then run it with `psql -v ON_ERROR_STOP=1`.

Then compare totals with the block above.

## E. Financial reconciliation (real PostgreSQL, test fixture)

| Flow | Observed |
|---|---|
| Purchase/GRN | 1 jar at ৳1,000 becomes +500 ml at ৳2/ml. Ledger and GL both rise by ৳1,000. |
| POS sale | 2 × 12 ml packs take 24 ml, revenue ৳200 + VAT, COGS ৳48 |
| Repack | 240 ml + 20 bottles at ৳8 make 20 prepared packs. Total value and GL unchanged; ৳160 capitalized; no packaging taken twice. |
| Prepared-pack sale | COGS ৳32 a pack (content ৳24 + bottle ৳8); the capitalized ৳8 is released once |
| Uneven capitalized value | 3 bottles for ৳10 are released as shares that add up exactly to what was capitalized; the reconciliation difference does not move |
| Partial refunds | 5 + 4 + 3 ml of a ৳100 pack return net ৳41.67 + ৳33.33 + ৳25.00 and VAT ৳4.17 + ৳3.33 + ৳2.50. Three journals; repeated posting creates none. |
| Discount and VAT | 3 packs, ৳10 line discount, paid ৳319 in total: three refunds return ৳319.00 exactly, VAT ৳29.00 |
| Split payment | Three refunds of a cash ৳33 / card ৳77 sale pay back exactly ৳33 cash (three drawer movements) and ৳77 card |
| Damaged return | Revenue and VAT reversed; stock and cost stay out |
| Prepared-pack return | A reusable bottle returns as a prepared pack with its ৳8. A broken one returns 12 ml loose, and its ৳8 stays in COGS. |
| Stock adjustment, transfer | Covered by the existing UOM suite. Partial whole-pack receipt keeps the capitalized value split exactly. |
| Ledger + capitalized vs Inventory GL | The difference is unchanged across every sequence above, including the concurrent sale/refund/repack race |
| Opening a prepared pack | Total value and GL unchanged; ৳8 moves from capitalized value to bottle stock |

## F. Testing

| Suite | Command | Result |
|---|---|---|
| Backend unit | `dotnet test tests/RetailPOS.UnitTests -c Release` | **1,046 passed, 0 failed** |
| Backend integration (all, includes PostgreSQL) | `dotnet test tests/RetailPOS.IntegrationTests -c Release` | **2,710 passed, 3 failed** (the live-infrastructure tests, as on the baseline), 0 skipped. Baseline: 2,681 passed, the same 3 failed. |
| UOM PostgreSQL HTTP | filter `UomPostgresHttpTests` | included above |
| Frontend | `npx ng test --watch=false` | **1,976 passed in 138 files** (baseline 1,951) |
| Production build | `npm run build` | Succeeds; one CSS budget warning on the POS screen, already over budget before this work |
| Flutter | `flutter test`, `flutter analyze` (stock take) | **124 passed**; no analyzer issues in the changed files |
| Browser and responsive | — | **Not run**: no browser tool in this session |
| Concurrency | refunds, retries, sale/refund/repack race, repack, oversell | Passed in the integration suite |
| Live infrastructure | Redis, RabbitMQ, Elasticsearch `LiveInfrastructureTests` | Fail as on the baseline; the test stack's services are unreachable |
| Flaky under load | An earlier full run also had two test-database drop timeouts (`DurableDeliveryFoundationTests`, a supplier-payment concurrency test) | Test server timeouts while dropping a fixture database; they passed on the final run |

**Existing tests changed because the requirement changed:**
- A partial refund now ends as `partially_refunded`, not `refunded`.
- The migration-chain head moved to the new migration.
- The sale DTO gains `refunds`.
- The transfer test now expects partial whole-pack receipt.
- Refund concurrency tests: the lock now comes before the read, and the posting test doubles return real intents.

**Not tested in a dedicated way:**
- a deadlock forced between two different variants;
- a configuration change in the middle of a stock operation;
- whether the separate POS slow-moving report and the transfer-opportunity engine are unit-aware (they compare velocities).

## Independent review

A separate read-only review of `66694088..HEAD` found no Critical issues.

**Fixed, with tests:**
- **H1, High:** a GRN line with nothing accepted could make a variant's cost snapshot 0 for later sales and adjustments. Cost queries now ignore lines with nothing received. Test: `Grn_ALineWithNothingAccepted…`.
- **M1:** a client that sends no `expectedLineGross` could be charged a custom-line promotion it never saw. It is now refused with `SALE_PRICE_CHANGED`. Test: `Promotions_AChangeSinceTheCartWasPriced…`.
- **M2:** a wrong stated cash amount now returns its own code, `CASH_REFUND_AMOUNT_MISMATCH`. Before, the screen read it as "old sale, state the cash".

**Found by the full-suite run:** under load, a refund racing a sale on the same item could fail on a stale inventory row version and return a 500. Void and refund now lock the inventory row (`FOR UPDATE`, after the sale row).

**Not changed, and why:**
- **M3, lock order:** refund takes the sale and then the cash session; void takes the session and then the sale. A deadlock can only arise between a void and a refund of the same sale, and void is refused once a refund exists. A detected deadlock returns the retryable `STOCK_CONCURRENT_UPDATE` 409, not a 500. There is no dedicated test.
- **M4:** the mobile sales dashboard still throws while a refund journal is unposted. This existed before for refunds; there are more references now.
- **L1:** the immutability trigger on `sale_financial_postings` does not cover the new `sale_refund_id`. That needs a migration; it is noted for follow-up.
- **L2:** a partial return's ledger movement can differ from its GL amount by about a cent.
- **L3:** a refund of a variant sold on several lines must name the line, including plain `pcs` lines. This follows the requirement not to identify lines by variant alone.
- **L5:** exchanges from `partially_refunded` sales are missing from the "unreversed exchanges" count.
- **L6:** the new check constraint scans `sale_items`; the Production rehearsal should time it.
- **L7:** an open-prepared retry that collides with another surfaces as an error, not a replay.
- **L8:** the inventory report with no business scope counts every item as pcs.

## G. Remaining issues

**Decisions for the owner or Finance**
1. **Opening a prepared pack with a reusable bottle** returns the bottle to packaging stock carrying its capitalized value. This is the same treatment the approved whole-job reversal and reusable-packaging returns already use. However, SRS PACK-006 and REP-008 say unpack recovery needs an approved policy. Confirm it, or ask for the endpoint to be disabled.
2. **Missing or damaged prepared packs** on a transfer, and adjustments to prepared packs, are refused: the bottle value has no approved destination.
3. **Repack wastage** goes through a separate stock adjustment. There is no new loss account.

**Functional limits**
- No quarantine stock: a damaged or rejected GRN line or return is recorded, not stocked.
- An exchange stays a link to the original sale and moves no quantity, so it cannot consume returnable quantity.
- A transfer line mixing several pack forms is still accepted or rejected whole.
- **VAT report gap, from before this work:** the VAT report does not read `SalesReturnPosting` refund journals, so refund VAT is missing from it. The journals themselves are correct. This affected single refunds before too.
- The transfer-opportunity engine and the POS slow-moving report still compare raw velocities across units.
- Mobile stock take cannot enter decimal counts (it refuses them).

**Known since the earlier report:** `SaleItemDto.historicalUnitCost` is sent to every sales reader whether or not they hold `products.view_cost`. This work did not change it.

**Environment**
- No browser tool.
- The Development migration could not be applied.
- My cleanup of the throwaway rehearsal database `uom_restore_check_20261107094240` on the test server (55432), and of `/tmp` files in the Development and test containers, was blocked by a safety check. They are disposable and can be deleted by hand.

**Production migration risk:** `ALTER COLUMN ... TYPE` rewrites the main stock and sales tables under an exclusive lock. Rehearse on a Production-sized copy and plan a maintenance window.

## H. Release readiness

| Level | Status |
|---|---|
| Code implementation | Ready for review: all suites pass except the live-infrastructure tests, which fail as before |
| Development environment | **Not verified**: migration not applied, no browser walk-through |
| Production deployment | **Not ready**: needs the Development verification, a Production-sized rehearsal and a release review |

## Final gap-closure round (I06, I07)

The latest status, results and blockers are in `UOM_RELEASE_READINESS.md`, and the per-gap evidence is in `UOM_FINAL_GAP_CLOSURE_TRACKER.md`. This round:
- closed the cost exposure noted under §G "Known since the earlier report";
- fixed the VAT report gap: refunds and voids are now included;
- made the refund ledger value cent-exact;
- added `sale_refund_id` to the posting guard (migration `20261107104508_PosV2UomPostingRefundGuard`);
- made transfer opportunities unit-aware;
- made the mobile sales dashboard subtract returned pieces only. Its deliberate fail-closed rule for unposted refund journals stays.

**Migration:** a fresh backup was taken and restored, and all three migrations were rehearsed in 1.0 s with identical totals. A second attempt to apply them was again refused by the session's permission control, so Development is still on `20261106173129`.

**Review:** the independent sub-agent review of this round was also refused by the permission control. The riskiest points were checked in-session instead: VAT business scoping and double counting, the void netting to zero, the guard against the posting worker's updates, and the ledger value on void.
