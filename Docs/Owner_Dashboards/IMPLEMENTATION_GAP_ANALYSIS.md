# Owner Dashboards — Implementation Gap Analysis and Source Mapping

**Audited:** 2026-11-06, read-only, against branch `staging` with its uncommitted working tree.
**Specs:** `01_OWNER_DASHBOARDS_SRS.md` v1.1, `02_OWNER_DASHBOARDS_LLD.md` v1.1.
**Purpose:** the verified logical-to-physical mapping the SRS readiness gate and LLD §14 require, the decisions taken, and what still needs sign-off.

**Legend:** ✅ supported today · 🟡 source exists, dashboard query needed · 🔴 no source in the codebase.

## 1. Metric and source mapping (SRS §12 D-01 to D-15)

| Ref | Question | Verified source | Dashboard rule | State |
|---|---|---|---|---|
| D-01 | Net sales | `Sale.TotalAmount − Sale.Tax`, the same definition as `SaleJournalSnapshot.NetRevenue`. Contract: POS Master LLD reporting metric "net product sales excl VAT after discounts and linked void/refund reductions". | Sale counted on its `BusinessDate`. Reversals deducted using `SaleFinancialPosting` kind `reversal` (`RefundAmount`) on the snapshot's `AccountingDate` (P3.2-D02). `refunded` is **not** a filter: a partial refund also sets this status. Held sales are never in `sales`. | 🟡 |
| D-01b | Exchanges | `Sale.OriginalSaleId` / `ReplacementSaleId`. The original stays `completed` and is not reversed automatically. No Returns/Exchange SRD exists (`CASH_REFUND_POLICY_UNDEFINED`). No Angular checkout path sends `originalSaleId`. | Sales are counted as posted, matching finance. Unreversed replaced originals are counted and raised as a data-quality warning. See blocker B-03. | 🟡 |
| D-02 | Daily outlet targets | **None.** `OutletSalesTargetVersion` is specified in POS Master LLD and not implemented. `cash-session-list` already states "No approved sales-target model exists yet". | Target is `null` with status `not_configured`; the UI shows N/A. See blocker B-01. | 🔴 |
| D-03 | Purchase recognition | `Grn` with status `completed`; line value `TaxableAmount ?? ReceivedQty × UnitCost`, the amount the receipt journal uses (`GrnCompletionAdapters`). | Recognised on GRN `ReceivedDate` in the company zone, ex-VAT. Purchase orders and bills are never added, so no stage is double counted. | 🟡 |
| D-04 | Expense KPI vs payments | `Expense` status `Approved` (journal posted). `TaxableAmount ?? Amount` is the P&L amount (`ExpenseCommandStore`). Drawer expenses are paid only at `DisbursedAtUtc`. | KPI: approved, on `ExpenseDate`, ex-VAT. Chart: gross `Amount`; bank and petty cash on `ExpenseDate`, POS drawer on `DisbursedAtUtc`, less `DrawerExpenseReversal` cash movements. | 🟡 |
| D-05 | COGS | `SaleItem.HistoricalUnitCost × Quantity`, rounded to currency per sale as the journal snapshot does, less reversal snapshot `Cost` on its accounting date. | Any eligible line with null or zero cost makes Profit `unavailable`. Otherwise `estimated`. Never derived from purchases. | 🟡 |
| D-06 | Valuation | Accepted: weighted-average replay per variant + location from `StockLedger` (`InventoryValuationReadStore`, P2-06, reconciled to GL). Also present: `StockValuationReportReadStore` using `Product.CostPrice + CostAdjustment`. **The two can disagree.** | Uses the accepted P2-06 replay, excluding `IsCostValuationReliable = false`. Stock with no reliable cost is counted as unvalued, the card shows Partial Value or Not Valued, and master cost is never substituted. See §10 and blocker B-02. | ✅ |
| D-07 | Main code / SKU | `Product.ProductCode` (nullable), `ProductVariant.Sku`. | Null main code displays as `Not set`; never replaced by SKU. | ✅ |
| D-08 | Quantities | `Inventory.Quantity` only. There are no reserved, blocked or damaged columns; held sales reserve nothing; `ReservedQuantity` in read models is a hard-coded 0. Dispatch deducts the source, receive adds to the destination. | `available = max(0, onHand)`. `transferable = available − pendingOutbound` (draft, submitted, approved). `inbound` = draft, submitted, approved, in_transit. In-transit stock is in no location, so it is never double counted. | 🟡 |
| D-09 | In-stock history | `StockLedger.BalanceAfter`, `BusinessDate`, `MovementSequence`. | Eligible selling days are replayed from the ledger. A pair with no ledger evidence is `insufficient_evidence`. | 🟡 |
| D-10 | Authorization | `IUserOutletAccessService` with `LocationAccessPolicy`; `IBusinessScope`; permission catalog. | See §3. | ✅ |
| D-10b | Transfer create | `/stock-transfers/create`, `POST /api/stock-transfers`, `StockTransferWriteRules` re-validates on the server. | Prefill through query parameters. The form stays unsubmitted. See §5. | 🟡 |
| D-11 | Analytics infrastructure | No analytics projections. Elasticsearch is configuration only. | Optimised read-only EF queries in one `RepeatableRead` snapshot. No projection, cache, consumer or schema change. | ✅ |
| D-12 | Thresholds | None exist. | LLD §7.3/§7.4 proposed values as code defaults, overridable through the `OwnerDashboard` configuration section, returned by `/meta`. See blocker B-04. | 🟡 |
| D-13 | Profit and cost visibility | Decided by the Business Owner (SRS §2.1). | Visible to every holder of the relevant dashboard permission. | ✅ |
| D-14 | Date window | HR dashboard uses 366 days. | Same limit (proposed). Performance is not yet measured on production-like volume. | 🟡 |
| D-15 | Warehouse → showroom | Excluded by default in the SRS. | Not implemented. | ✅ |

### Cash received (SF-018)

`SalePayment.Amount` on the sale's business date, less the reversal snapshot's `Payments` on `AccountingDate`. Allowed tenders are cash, card and mobile. Legacy `loyalty` rows are excluded because they are not money received. Cash-session handovers and deposits are not read, so settlement movements cannot be counted.

## 2. Requirements coverage

| Area | Already available | Missing, now built |
|---|---|---|
| Shell | Sidebar, routes, `routePermissionGuard`, business selector | Two menu entries and routes |
| Filters | `app-ui-date-picker`, `appUiControl` | Shared filter bar with draft/applied state and per-screen retention |
| Sales KPIs | `SalesReportReadStore` (uses `SaleDate`, UTC-based, no VAT netting or status filter, so **not** reusable) | Business-date read store and handlers |
| Showroom performance, trend | None | Endpoints and UI |
| Cash vs expense | `CashBookReadStore` (GL-based, all journals) | Operational cash-basis series |
| Inventory KPIs | Stock reports (per-report logic) | Unified snapshot |
| Best sellers | `GetTopProductsAsync` (no status filter, no VAT netting) | Net-of-reversal aggregation |
| Slow movers | `GetSlowMovingInventoryItemsQuery` (days since last sale only) | Velocity and eligible-day classifier |
| Transfer engine | None | Deterministic engine, details, handoff |
| Transfer create | Full workflow | Additive prefill only |

**Existing reports are not changed.** Differences between these dashboards and `SalesReportReadStore` come from that store's looser rules and are expected.

## 3. Security mapping

Business Owner decision of 2026-11-06 (SRS §2.1). It replaces the earlier mapping to `reports.*` and `products.view_cost`.

| Permission (new catalog key) | Controls |
|---|---|
| `dashboard.sales_finance.view` | Sales & Finance menu entry, route and every `sales/*` endpoint |
| `dashboard.inventory_intelligence.view` | Inventory Intelligence menu entry, route and every `inventory/*` endpoint |
| Either of the two | `meta` |

| Data | Scope |
|---|---|
| Sales, COGS, expenses, purchases, stock, recommendations | Every location of the current Business. Step 3, Step 5, the default outlet and the selected location are not consulted. |
| Selected showroom | Applied to Sales, COGS and Expenses alike |
| All Locations | Also includes warehouse and business-level expenses |
| Starting a transfer | Existing `stock_transfers.create`, `stock_transfers.transfer_from_any_location` and `StockTransferSource`; the transfer API re-validates |

- Business always comes from `IBusinessScope.RequireBusinessId()`; there is no business parameter, and `BusinessContextMiddleware` validates a Super Admin's selected Business.
- A location that does not belong to the current Business returns 403.
- Customer, cashier and payment-account fields are never projected.
- No seeding and no migration. Wildcard roles hold the keys automatically; other roles need them assigned in Role Edit.

## 4. Design compatibility (SRS §13, LLD §15)

| Topic | Finding | Decision |
|---|---|---|
| Authority | `.github/copilot-instructions.md`: Able Pro is "read-only; never edit, copy wholesale or import it". Licence review is still pending. | Visual reference only. No Able Pro code, asset or dependency is copied. |
| Versions | App Angular 21.1; Able Pro 21.0.1 with `apexcharts` and `ng-apexcharts`. App has **no chart library**. | SRS UI-08 applies: adding packages needs approval, so charts are feature-local SVG using `--rp-*` tokens, each with a data-table alternative. |
| Shell | Existing sidebar and topbar stay. | No second shell. |
| Drawer | Design system: use `app-ui-dialog`, never hand-roll overlays. | Details open in `app-ui-dialog` size `lg`. **Documented deviation** from the side drawer described in INV-050. |
| Figma | Screenshots are **not in the repository**. | Layout follows the SRS text. UI gate B (side-by-side QA) cannot be done until they are supplied. |
| Mapping file | LLD §15.1 asks for `AblePro_UI_Component_Mapping.md`. | Recorded in §6 here. |

## 5. Transfer handoff

`Initiate Stock Transfer` routes to `/stock-transfers/create?fromLocationId&fromLocationType&toLocationId&toLocationType&variantId&quantity`.

The form validates each value against its own authorized lists, fetches the line through the existing variant lookup (live stock at the source), and leaves everything editable and unsubmitted. Without a prefill its behaviour is unchanged. Server validation in `StockTransferWriteRules` stays authoritative.

The row's `canInitiate` flag needs `stock_transfers.create`, the source in `StockTransferSource` scope, and either `stock_transfers.transfer_from_any_location` or the source being the user's default. The form only allows other sources with that permission.

## 6. UI component mapping

| Dashboard section | Able Pro pattern (visual reference) | POS v2 component | Choice |
|---|---|---|---|
| Page and filters | Compact toolbar | `app-ui-page-header`, `app-ui-card`, `appUiControl`, `app-ui-date-picker` | Reuse |
| KPI row | Statistic cards | New `app-od-kpi-card` | Rebuild with tokens |
| Comparison bars | Progress bars, horizontal bars | New `app-od-bar-list` | Rebuild; chart/table toggle |
| Grouped bars, trend line | Apex charts | New `app-od-column-chart`, `app-od-line-chart` | Rebuild as SVG |
| Tables | Data tables | `app-ui-data-table` (server mode) | Reuse |
| Details | Off-canvas | `app-ui-dialog` | Reuse (deviation above) |
| Status | Badges | `app-ui-badge` | Reuse |
| States | Skeletons, alerts | `app-ui-state`, `app-ui-alert` | Reuse |

## 7. Database

No migration is required. Indexes were not added: LLD §9 says to measure first, and `Sale(BusinessDate)` and `(OutletId, BusinessDate)` already exist.

## 8. Blockers and decisions awaiting sign-off

See `.claude/autonomous/BLOCKERS.md` for current state.

| ID | Decision | Effect today |
|---|---|---|
| B-01 | Sales targets do not exist. Implementing `OutletSalesTargetVersion` needs a migration and belongs to the POS programme. | Target, Shortfall and Achievement show N/A |
| B-02 | Confirm P2-06 weighted-average as the owner-facing valuation; it can differ from the operational Stock Valuation Report. | Implemented with P2-06 |
| B-03 | Exchange netting policy. | Counted as posted; warning shown |
| B-04 | Approve the slow-moving and transfer thresholds. | LLD proposed defaults, shown as proposed |
| B-05 | Reversal date policy: reversals reduce the period of their accounting date, so a prior-period void does not restate that period. Matches finance (P3.2-D02). | Implemented; confirm |
| B-06 | Supply Figma screenshots for UI gate B; complete the Able Pro licence review before any asset is reused. | Layout from the SRS; no assets copied |
| B-07 | **Resolved 2026-11-06:** no separate financial or cost permission (SRS §2.1). | `dashboard.sales_finance.view` covers every Sales & Finance figure |

## 9. Update (2026-11-06)

D-02 and B-01 are resolved in code: outlet sales targets exist and feed the dashboard. See `Docs/08_Sales_And_POS/OUTLET_SALES_TARGET_GAP_ANALYSIS.md`. The entries above are kept as the record of the earlier state.

## 10. Stock valuation root cause and fix (2026-11-06)

**Root cause.** The dashboard valued little because the ledger held little usable cost, and it hid that by summing unknown as zero.

| Defect | Where | Fix |
|---|---|---|
| Purchase receiving wrote the ledger with no cost | `PurchaseAndReceiveStore` | Writes the value its journal debits |
| GRN completion wrote the undiscounted cost while the journal debited the discounted value | `GrnCompletionStore` | Writes the journal value |
| Transfers carried a client cost, which the form sent as zero | `transfer-create.component.ts`, `StockRequisitionTransferStore`, `StockTransferCreationStore`, `StockTransferDraftStore` | The server sets cost at dispatch; client values are ignored |
| A supplied zero was flagged reliable | `StockLedgerWriter` | Reliable needs a positive cost or a proven zero |
| A zero pool counted as valued, and unknown summed as zero | `InventoryDashboardReadStore`, `InventoryIntelligenceQueries` | `StockCostLedger` and `StockValuationCoverage` |

**Historical correction (approved 2026-11-07, replacing the earlier no-correction decision for these rows).** Uncosted GRN receipts, and the transfers made from them, are corrected from their GRN and journal evidence by append-only audited rows; see LLD §7.1.2. No original ledger row, stock balance or journal is changed. Stock with no cost evidence stays Not Valued, and no opening balance was created.

Applied to Development on 2026-11-06 22:09 UTC (2026-11-07 Asia/Dhaka) for Business 1, after a rehearsal on a throwaway copy:

| Measure | Before | After |
|---|---|---|
| Corrections | 0 | 26 (audit event 1036) |
| Valued stock | 94 units, ৳1,128 | 1,220 units, ৳1,051,528 |
| Not valued | 4,216 units (61 records) | 3,090 units (44 records), the seeded stock |
| Inventory GL | ৳1,051,528 | ৳1,051,528, unchanged and equal to the valued total |

Staging and Production were not checked or changed. The tool refuses them, so any correction there needs its own approved release step.

**Period close.** A correction applies from its own business date, so closed-period evidence is not restated. A period ending before the correction date keeps its original P2-06 difference, and the inventory gate blocks that period unless it was closed with an approved exception. On Development the only period ends 2026-12-31, so this does not apply there. Decide this before correcting any environment with monthly periods.

**Known discrepancies left in place**

| Item | Effect | Needs |
|---|---|---|
| Missing and damaged transfer units leave the ledger with no journal | The P2-06 difference to GL grows by their value, and the period-close inventory gate blocks on any difference | A write-off policy |
| Purchase and receive debits `qty × UnitCost`, ignoring line discount and VAT, while GRN completion uses the discounted value and books VAT | The same purchase values differently by path | An accounting decision |
| Sale and stock adjustment snapshots use a business-wide GRN average, falling back to Product Master cost | COGS can differ from the location pool, and those rows are flagged reliable | An accounting decision |
| Selling verified-zero stock writes a zero-cost sale row, which is unreliable | That balance becomes Not Valued | Accepted as the conservative result |
| The P2-06 report does not apply the zero proof, and keeps negative pools | It can differ from the dashboard | Unchanged accepted report |
