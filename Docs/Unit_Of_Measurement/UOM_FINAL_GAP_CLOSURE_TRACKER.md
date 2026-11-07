# UOM v1 — Final Gap Closure Tracker

**Date:** 2026-11-07. **Branch:** `staging`. Gaps come from `UOM_FINAL_VERIFICATION_REPORT.md` and the owner's brief (I06, I07). Each was checked against the current source before it was changed.

| ID | Gap | Severity | Root cause | Fix (files) | Decision | Tests | Status |
|---|---|---|---|---|---|---|---|
| G-01 | Sale responses exposed `historicalUnitCost` to every sales reader | P0 security | `SalesController` mapped cost with no permission check | `ToDto` nulls cost without `products.view_cost` (null, never 0), for get, list, create, void and refund. Prices and quantities are unchanged. (`SalesController.cs`) | I07 | `Security_SaleCost_IsAbsentFromTheResponse_WithoutViewCost` | Closed |
| G-02 | VAT report left out refund and void VAT | P0 financial | It read only legacy `JournalReversal` reversals; P3.2 `SalesReturnPosting` journals were missing | Posted reversal intents are reported per line at the original rate, dated on the reversal journal. Unposted and failed intents, and pre-v2 snapshots, are excluded. Legacy reversals are not counted twice. (`VatReportReadStore.cs`) | I07 | `Vat_PostedPartialRefunds_…`, `Vat_APostedVoid_…` | Closed |
| G-03 | Exchange might let the same quantity come back twice | P0 integrity | Investigated: an exchange is a link (`OriginalSaleId`/`ReplacementSaleId`) plus a separate sale. It records no returned quantity and moves no stock. | No change: an exchange consumes nothing, so the refund (the only goods return) can't double-count. The shared limit is `sale_items.returned_quantity <= quantity`. | I07 | `Refunds_AndExchange_ShareOneSafeguard` | Closed (documented) |
| G-04 | A partial refund's stock-ledger value could differ from its GL cost by a cent | P1 financial | The ledger valued the return at unit cost × quantity; the journal used the cumulative share | The ledger movement takes the same cumulative share the journal posts (`RefundSaleCommand.cs`, `SaleCreationAdapters.cs`) | I07 | `Refunds_ThreeAwkwardPartials_LedgerAndJournalRestoreExactlyTheSameCost` | Closed |
| G-05 | `sale_refund_id` could be changed after a posting intent was written | P1 integrity | The guard trigger predated the column | Migration `20261107104508_PosV2UomPostingRefundGuard` adds it to the guard | I07 | `Immutability_ARefundIntentCannotBeMovedToAnotherRefund` | Closed (migration not applied to Development) |
| G-06 | Opening a prepared pack and recovering its bottle | I06 | Approved policy | Already built. Tests added for one-at-a-time and concurrent openings, retry, over-quantity, restricted location and another business. | I06 | `OpenPrepared_OnePack…`, `I06_OpeningPacksOneAtATime…` | Closed |
| G-07 | Transfer opportunities compared ml with piece thresholds | P1 reporting | Minimum demand, retained, suggestion and cap were counts of pieces | These apply to `pcs` only. Measured items use demand > 0 and cover days. (`OwnerDashboardCalculators.cs`) | I07 | `TransferOpportunities_AMeasuredItem_IsNotHeldToPieceThresholds` | Closed |
| G-08 | The mobile sales dashboard fails while a refund journal is unposted, and took ml off pcs | P2 reliability, P1 reporting | The failure is a deliberate, tested fail-closed rule (`Dashboard_AppliesRoleScopesAndFailsClosed…`); the unit mix was a bug | Fail-closed kept; it subtracts returned pieces only (`SalesReportReadStore.cs`) | I07 | Mobile dashboard suite | Closed (fail-closed by design) |
| G-09 | Slow movers compared ml/day with pieces/day | P1 reporting | One velocity threshold for all units | Done earlier: measured units use stock cover | I07 | `UnitAwareSlowMoverTests` | Closed |
| G-10 | Master-cost valuation shown as if it were book value | P1 | Estimate screen | Done earlier: labelled "Estimated value (master cost)"; book value stays ledger plus capitalized value | I07 | — | Closed |
| G-11 | Void/refund lock order | P2 | Refund takes the sale then the cash session; void takes the session then the sale | Not changed. Void is refused once a refund exists, and a detected deadlock returns the retryable 409. | — | — | Accepted risk |
| G-12 | A prepared-pack retry racing another copy of itself | P2 | The idempotency pre-check runs outside the transaction | A collision hits the unique index and returns a retryable 409; the retry replays. The concurrent test sees no 500. | I06 | `I06_…` | Closed |
| G-13 | Inventory report with no business scope counts all items as pcs | P2 | No business, so no unit lookup | Not changed: every caller has a business; SuperAdmin has none and no stock scope | — | — | Accepted |
| G-14 | Mixed-form transfer line taken whole | P2 | Pack forms can't be told apart by base quantity | Not changed: a safe refusal | D09 | — | Limitation |
| G-15 | Prepared packs cannot be adjusted, and missing or damaged ones are refused | P2 | No approved write-off treatment for the bottle value | Not changed: needs a new owner policy | — | — | Limitation |
| G-16 | A refund must name the line when a variant is on several lines | P2 | Requirement: never by variant alone | Kept, by design | I02 | `Refunds_MixedUnits…` | By design |
| G-17 | "Unreversed exchanges" count ignores `partially_refunded` originals | P2 | The count is "completed and replaced" | Not changed: a partly refunded original has been partly reversed, so it isn't clearly unreversed | — | — | Accepted |
| G-18 | No dedicated test of a configuration change during a stock operation | P2 | — | Not added. Covered indirectly: `VariantUomReader` takes `FOR SHARE` in transactions, and `UOM_CONFIG_CHANGED` refuses stale versions. | — | `ChangedConfiguration_RefusesStaleCheckout…` | Partly covered |

**Verification evidence:** see `UOM_RELEASE_READINESS.md` §10 for the full regression, and §8–9 for the migration rehearsal.

## Round 3 (independent review of dcfcc48f..c38063f7)

| ID | Gap | Severity | Fix | Test | Status |
|---|---|---|---|---|---|
| G-19 | A refund share rounding to ৳0.00 marked the stock unvalued | Medium | Fall back to the 4-place value (`SaleCreationAdapters.cs`) | `Refund_OfAFractionOfACent_…` | Closed |
| G-20 | Dashboard returned units counted restored packaging and used the current unit | Medium | Only returns of the sale's own pcs lines (`SalesReportReadStore.cs`) | Mobile dashboard suite | Closed |
| G-21 | The stock ledger showed sale and refund cost to `inventory.view` holders | Medium security | Cost, value and cost remarks hidden without `products.view_cost` (`StockLedgerController.cs`, `StockLedgerDtos.cs`) | `Security_SaleCost_…`, `StockLedgerReadHttpTests` | Closed |
| G-22 | VAT report read every posted reversal | Medium performance | Report window only (`VatReportReadStore.cs`) | VAT tests | Closed |
| G-23 | Inventory reports show cost to `reports.inventory` holders | Security | U-13 Option A: cost needs `products.view_cost`; null in API, omitted in CSV/Excel (`InventoryReportsController.cs`, exporters, `ReportExportCostColumns.cs`, 8 Angular pages) | `UomPostgresHttpTests.CostVisibility.cs`, `inventory-report-cost-visibility.spec.ts` | Closed (`9cd6da61`) |

## Round 4 (U-13 review)

| ID | Gap | Severity | Fix | Test | Status |
|---|---|---|---|---|---|
| G-24 | Transfer and requisition item cost visible to `stock_transfers.view` | Medium security | Null without `products.view_cost` | `U13_TransferAndBarcodeReads…`, requisition test | Closed (`0e71c1d5`) |
| G-25 | Barcode variant search returned cost price | Medium security | Cleared without `products.view_cost` | `U13_TransferAndBarcodeReads…` | Closed |
| G-26 | `api/inventory/valuation` category order revealed cost | Low | Name order when cost is hidden | — | Closed |
| G-27 | Valuation report returned 500 once a measured unit existed | High (bug) | `UomReportTotals` reads the unit code in the query | CostVisibility tests | Closed (`9cd6da61`) |
| G-28 | Owner dashboard inventory value under `dashboard.inventory_intelligence.view` | Decision | None | — | Open (owner) |
| G-29 | `api/inventory/*` hides cost as 0 rather than null | Low | None (contract) | — | Open |

