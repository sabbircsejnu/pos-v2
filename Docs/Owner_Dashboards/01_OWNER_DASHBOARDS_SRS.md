# IraniMart POS v2 — Owner Dashboards
## Software Requirements Specification (SRS)

**Document ID:** IM-POSV2-OWN-DASH-SRS-001  
**Version:** 1.3 — includes the Business Owner's final permission decision (§2) and the stock valuation decision (§7.2.1)  
**Prepared:** 2026-11-06  
**Status:** Proposed; business decisions and codebase mapping require confirmation  
**Screens:** Sales & Finance Dashboard; Inventory Intelligence Dashboard  
**Design references:** Owner-approved Figma Make dashboard screenshots (information architecture and business flows); local Able Pro Angular v3.3.0 reference project (UI components and visual implementation patterns) at `design-reference/able-pro/able-pro-angular-v3.3.0`.

> **Scope rule:** This document describes the dashboards shown in the supplied prototype and their necessary backend behaviour. It does not authorize changes to the established sales, inventory, purchase, accounting, or transfer transaction lifecycles. Existing approved POS v2 specifications and security rules remain authoritative wherever this document does not explicitly define a dashboard-specific behaviour.

---

## 1. Purpose and business objectives

The Business Owner needs a concise, multi-showroom view of commercial performance and inventory health, with actionable recommendations for moving excess stock toward locations that demonstrate stronger demand.

Two separate sidebar destinations under **Dashboard** shall be implemented:

1. **Sales & Finance:** selected-period sales, target, shortfall, purchase, expense, estimated profit, showroom sales performance, cash received versus operational payments, and sales trend.
2. **Inventory Intelligence:** current/as-of inventory valuation and quantity, stocked-product counts, slow-moving valuation, out-of-stock locations/variants, best-selling products, slow-moving products, stock value by location, and source→destination stock-transfer opportunities.

The dashboards must share tenant, permissions and design conventions while retaining independent filters. Their primary purpose is decision support, not transaction entry or accounting reconciliation.

### 1.1 Success criteria

- Owner can answer the main sales/finance questions without leaving the Sales dashboard.
- Owner can identify product **main code**, exact variant/SKU, affected showroom and slow/fast movement on the Inventory dashboard.
- Owner can review why a transfer is suggested and create a **prefilled, unsubmitted** request in the existing transfer workflow; no stock moves from the dashboard.
- Totals reconcile with the authoritative business documents and defined financial/inventory valuation policies.
- The system never reveals data from another business or a location outside the user's authorized scope.

## 2. Audience, roles, and authorization

| Actor | Responsibilities | Required behavior |
|---|---|---|
| Sales & Finance viewer | Read the Sales & Finance dashboard | Holds `dashboard.sales_finance.view`; sees all of the current Business |
| Inventory Intelligence viewer | Read the Inventory Intelligence dashboard, including transfer recommendations | Holds `dashboard.inventory_intelligence.view`; sees all of the current Business |
| Transfer creator | Start a real stock transfer from a recommendation | Additionally needs the existing Stock Transfer permissions and location rules |
| User without the permission | No dashboard access | Menu entry hidden; API returns 403 |

### 2.1 Final permission decision (Business Owner, 2026-11-06)

This decision supersedes every earlier statement in this SRS about dashboard location scope and separate financial or cost permissions.

**Exactly two view permissions**, assigned in Role Create/Edit, control both the sidebar entry and the API:

| Permission | Grants |
|---|---|
| `dashboard.sales_finance.view` | Every Sales & Finance figure, including Sales, Purchase, Expense, COGS-based Estimated Profit and cash figures |
| `dashboard.inventory_intelligence.view` | Every Inventory Intelligence figure, including stock value at cost and transfer recommendations |

**Business-wide visibility.** A holder sees all outlets and warehouses (where applicable) of the current Business. Dashboard queries are not restricted by Step 3 inventory access, Step 5 assignments, the default outlet, the currently selected operational location, or any other module's view permission. A warehouse-only user or a user with no assignments sees the same data as anyone else holding the permission.

**The location dropdown is a reporting filter, not an authorization boundary.** Wherever this SRS says "authorized locations" for a dashboard read, it means all locations of the current Business. A location that does not belong to the current Business is rejected.

**Always enforced on the server:**

- The relevant dashboard permission on every dashboard API; otherwise 403.
- Business isolation. The Business comes from the authenticated context, a requested Business the user may not access is rejected, and data of another Business is never returned.
- Dashboard permissions grant no right to modify any transaction.

**Stock transfers.** Viewing a recommendation needs only `dashboard.inventory_intelligence.view`. Starting or submitting a real transfer still requires the existing Stock Transfer permissions, location authorization, validation and approval rules.

**One reporting scope for profit.** Sales, COGS and Expenses use the same business-wide scope, and a selected showroom filter applies to all three. All Locations includes warehouse and business-level expenses. Selecting a showroom includes only that showroom's expenses. Accounting postings and financial rules are unchanged.

## 3. Terms and canonical definitions

| Term | Definition |
|---|---|
| Business date | The operational local date attributed to a transaction by the authoritative business rules (including the POS cash-session opening-date rule where applicable) |
| Reporting timezone | The business-configured Bangladesh-local timezone, initially `Asia/Dhaka`; server stores instants in UTC and resolves business dates consistently |
| Net sales | Completed eligible sale value net of returns/eligible reversals and discounts, using documented VAT treatment; **not** payment collections |
| Target | Existing effective outlet sales target for a business date, aggregated according to approved target rules |
| Shortfall | `max(Target − NetSales, 0)`; target excess is separately `max(NetSales − Target, 0)` |
| Purchases | Authoritative selected-period purchase recognition metric; must explicitly choose posted GRN/purchase-invoice recognition at integration mapping time |
| Expense | Authoritative posted operational-expense metric, excluding purchase inventory acquisition and transfers |
| Cash received | Actual eligible cash/payment inflows allocated to the selected business reporting period; not synonymous with sales |
| Estimated operating profit | Comparable net revenue less COGS and attributable operating expenses; label estimated unless complete accounting derivation is verified |
| On-hand stock | Physical authoritative quantity at a location, excluding stock in transit from double counting |
| Available/transferable stock | On-hand minus applicable reservations, blocked, damaged and other unavailable quantities; transferable further constrained by source safety stock and pending commitments |
| Main code | The existing stable product-level main code, distinct from exact variant/SKU |
| Sales velocity | Net eligible units sold divided by eligible in-stock selling days within a period (not simply calendar days where stockout data exists) |
| Slow-moving | A configured rule-based classification requiring sufficient observation and on-hand inventory; zero sales and low velocity treated distinctly |
| Stock valuation | Authoritative inventory-cost valuation of eligible quantity using the company's approved cost method and historical cost data, not selling price |

### 3.1 Business-date versus calendar-date rule

A POS transaction occurring after midnight can belong to the prior cash-session business date. Sale-based metrics and target achievement must use the **authoritative sale business date**, not naive UTC truncation or browser-local date. For purchase, expense and payment reports, the authoritative transaction posting date/time policy must be documented separately and shown consistently. Date inputs are inclusive business dates and must be converted server-side into the correct boundaries.

## 4. Navigation and shared shell requirements

| ID | Requirement | Priority |
|---|---|---|
| NAV-001 | Sidebar Dashboard group contains two separate links: **Sales & Finance**, **Inventory Intelligence** | Must |
| NAV-002 | Active screen highlighted and direct/deep links supported | Must |
| NAV-003 | Sidebar remains collapsible; common top header has business name, current screen, refresh, last updated, profile | Should |
| NAV-004 | Switching screens retains each screen's previously applied filters independently | Must |
| NAV-005 | Layout supports desktop ≥1280px and responsive tablet/mobile stacking, with tables horizontally accessible | Must |
| NAV-006 | Only explicitly authorized navigation items are shown; API independently enforces authorization | Must |
| NAV-007 | All displays use BDT, accessible labels and clear empty/loading/error/restricted states | Must |

## 5. Shared filtering and time rules

| ID | Requirement |
|---|---|
| FIL-001 | Business context is server-authenticated; user cannot query another business by changing an ID |
| FIL-002 | Screen-specific location selector lists the relevant locations of the current Business; **All** means every applicable location of the current Business, never another tenant's. The selector is a reporting filter, not an authorization boundary (§2.1) |
| FIL-003 | Date presets: Today, Yesterday, Last 7 Days, Last 30 Days, This Month, Last Month, Custom |
| FIL-004 | Inclusive From/To business dates, `From ≤ To`; reject invalid/unsupported ranges server-side |
| FIL-005 | Apply commits filter state, Reset restores documented screen defaults, Refresh re-fetches applied state |
| FIL-006 | Changing a filter affects all metrics governed by it; each widget explicitly displays any **independent** period |
| FIL-007 | Sales & Finance initial KPI period is **Today**; Inventory movement-analysis period is **Last 30 Days** |
| FIL-008 | Sales Dashboard income/expense initial series uses **Last 7 Days**, Sales Trend initial series **Last 30 Days**; independent local period selectors and labels must prevent misleading totals |
| FIL-009 | When the user applies a global date range, dependent widgets inherit it unless the user intentionally overrides a widget period; overrides must be visible and resettable |
| FIL-010 | Inventory value/quantity displays the latest authoritative stock snapshot as-of timestamp and is **not** silently backdated by the sales-analysis date filter |
| FIL-011 | For historical analysis show two separate badges: `Sales Analysis: …` and `Stock Snapshot: …` |
| FIL-012 | Filters, URLs and API requests encode dates unambiguously (`YYYY-MM-DD`); formatted display follows an explicit locale and does not mix `DD/MM` and `MM/DD` |
| FIL-013 | Multi-location KPI totals are computed from the current Business's locations without counting the same transaction or in-transit stock twice |
| FIL-014 | For a location-specific page, components that require comparisons gracefully switch to a single-location view or compare authorized locations when expressly selected |

## 6. Sales & Finance dashboard — functional requirements

### 6.1 KPI cards

| ID | Card | Required behavior |
|---|---|---|
| SF-001 | Net Sales | Sum eligible net sales for applied business date/location range, reconcile with Sale report; show previous comparable period change only when valid |
| SF-002 | Target | Aggregate existing configured daily targets across eligible showroom business dates; do not manufacture targets where none exist; show attainment percentage where target > 0 |
| SF-003 | Shortfall | `max(target − sales, 0)`; if exceeded show a positive exceeded amount/status, never negative shortfall |
| SF-004 | Purchase | Posted purchase recognition metric for applied period; avoid double counting purchase order, GRN and purchase invoice stages |
| SF-005 | Expense | Posted eligible operational expenses, with reversal/cancellation handling and explicit locations |
| SF-006 | Profit | Estimated or exact label according to accounting completeness; if valid COGS or attributable expense missing show `Unavailable`, not a guessed value |
| SF-007 | Presentation | Six compact cards, currency formatting, relevant tooltip, loading/no-data state; `Today` labels become `Selected Period` for non-Today range |
| SF-008 | VAT | Revenue, COGS and profitability calculations align with approved VAT-inclusive/product override policy; avoid recording VAT as revenue when it is a liability |
| SF-009 | Adjustments | Returns/exchanges/voids/discounts use authoritative posted statuses and sign conventions; held/draft/cancelled/unposted activity excluded |

**Financial policy awaiting sign-off:** The definition of `Net Sales` (revenue excluding output VAT versus VAT-inclusive customer-facing totals), purchase recognition moment, and whether expenses shown are accrued or cash-paid must be aligned with the existing finance ledger. The dashboard must label each metric and not silently mix bases. Any financial-card value used in the screenshot is **demo-only**.

### 6.2 Showroom performance

| ID | Requirement |
|---|---|
| SF-010 | Show net sales, target, achievement percentage and shortfall/exceeded per authorized selling showroom |
| SF-011 | Horizontal progress bars (including over-target visual treatment), optionally toggle chart/table |
| SF-012 | Sort by Sales or Achievement; click row/bar filters screen to that showroom |
| SF-013 | Warehouse-only locations are not treated as target-bearing retail showrooms |
| SF-014 | Show explicit `No target configured` rather than interpreting target zero as 0% achievement |

### 6.3 Cash received vs expense payments

| ID | Requirement |
|---|---|
| SF-015 | Two-series grouped bar chart labelled `Cash Received` and `Operational Expense Payments` |
| SF-016 | Default last seven business days; date-local presets and custom range, aggregation daily/weekly based on length |
| SF-017 | Show selected-period totals and difference; cash difference must **not** be labelled accounting profit |
| SF-018 | Cash received derived from actual posted payment collections and valid reversals/refunds, with consistent tender/payment-account treatment; avoid double counting settlement movements |
| SF-019 | Expense payments derived from actual disbursement events; unpaid accruals excluded from a cash-basis chart |
| SF-020 | Tooltips show exact date and currency; empty state visible; drill-down only if an existing report supports the scope |

### 6.4 Sales trend

| ID | Requirement |
|---|---|
| SF-021 | Time-series Net Sales vs Target, default Last 30 Days |
| SF-022 | Independent period selector, consistent buckets and target rules |
| SF-023 | Null/missing target distinctly rendered from zero target; no invented target values |
| SF-024 | Every point derives from the same authoritative sales source as SF-001 and SF-010 |

## 7. Inventory Intelligence dashboard — functional requirements

### 7.1 Inventory KPIs

| ID | Card | Required behavior |
|---|---|---|
| INV-001 | Total Stock Value | Inventory value of eligible on-hand (or accounting-owned) stock as-of timestamp, at approved cost basis; show selected locations and valuation method |
| INV-002 | Total Stock Quantity | Total on-hand units across selected locations; exclude in-transit double counting; distinguish physical vs available where tooltip relevant |
| INV-003 | Active Stocked Products | Distinct main product count with stock > 0, distinguish variants/SKUs, no count inflation across locations |
| INV-004 | Slow Moving Stock Value | Cost-value of inventory classified slow within chosen period and selected locations, with share of inventory value |
| INV-005 | Out of Stock Variants | Count eligible active *showroom × variant* combinations with available quantity ≤ 0; exclude ineligible locations and retired variants; label clearly |
| INV-006 | Transfer Opportunities | Count deduplicated currently actionable source/destination/SKU recommendations, high-priority count alongside |
| INV-007 | Snapshot | Show effective inventory as-of timestamp, refresh/freshness state, unit/currency basis |
| INV-008 | Scope | All Locations includes permitted warehouse/showroom physical inventory; sales-velocity comparisons distinguish selling showrooms from warehouse |

### 7.2 Stock value by location

| ID | Requirement |
|---|---|
| INV-010 | Horizontal value-by-location bars, including authorized showrooms and central warehouse if relevant |
| INV-011 | Display quantity, stock value and percentage of authorized selected-location total |
| INV-012 | Sort by value; chart/table toggle; clicking location applies filter |
| INV-013 | Sum must reconcile to Total Stock Value for the same inventory snapshot, valuation rule and location set |
| INV-014 | Each location shows its valued, zero-cost and unvalued quantities and record counts. A location with stock and no reliable value shows **Not Valued**, never a zero amount |

#### 7.2.1 Stock valuation decision (Business Owner, 2026-11-06)

Stock Value = sum of on-hand quantity × weighted-average inventory cost from the stock ledger. It is never derived from selling price, and missing cost is never replaced with Product Master cost.

| Class | Meaning | Shown as |
|---|---|---|
| Valued | Reliable movements explain the whole on-hand balance, at a positive value | The amount |
| Zero cost | The balance is explained, and stored documents prove the stock cost nothing | The amount of zero, labelled zero cost |
| Not valued | No reliable cost: no history, an unreliable movement, or a zero no document proves | **Not Valued**, excluded from every subtotal |

| ID | Requirement |
|---|---|
| INV-015 | Unknown cost is never interpreted as zero in any total, share, sort or label |
| INV-016 | Total Stock Value shows the valued subtotal. It is labelled **Partial Value** when any stock is unvalued, and **Not Valued** when none is valued. The card shows valued and unvalued quantities and record counts |
| INV-017 | The sum of valued location subtotals equals the Total Stock Value subtotal |
| INV-018 | Slow Moving Stock Value and product insights follow the same rules. A percentage share is shown only when both figures are complete |
| INV-019 | A zero cost is proven by a completed GRN line whose cost and order price are both zero, by a fully discounted order line, or by a transfer from stock proven free in that way. A supplied zero alone proves nothing |

| INV-019a | An approved cost correction counts as cost evidence. It is an append-only, audited record taken from the GRN and its posted journal. Original ledger rows, stock balances and journals are not changed |

Stock with no cost evidence stays Not Valued. Master cost and opening balances are not used to value it (decision of 2026-11-07, which replaced the earlier no-correction decision for uncosted GRN receipts and the transfers made from them).

### 7.3 Best selling products

| ID | Requirement |
|---|---|
| INV-020 | Table defaults to last 30 days and maximum 15 records with View All |
| INV-021 | Columns: Rank, **Product Main Code**, Product Name, exact Variant/SKU, Category, Net Units Sold, Net Sales, Current Available Stock |
| INV-022 | Search by main code/name/SKU; category/location filters, sortable columns |
| INV-023 | Default rank by net units sold descending, stable tie-breaking by main code/SKU |
| INV-024 | When aggregated by main product, label grouping explicitly; variant drill-down is exact and free of double counting |
| INV-025 | Current stock must be labelled `Current Available Stock` even for historical sales periods |

### 7.4 Slow moving products

| ID | Requirement |
|---|---|
| INV-030 | Separate always-visible table, maximum 15 rows with View All |
| INV-031 | Columns: **Main Code**, Name, Variant/SKU, Showroom, Available Stock, Net Units Sold, Days Since Last Sale, Stock Value, Classification |
| INV-032 | Filters: All Slow, Zero Sales, Low Sales, location, category, main code/name/SKU, stock range; sorts for quantity, stock value, last sale |
| INV-033 | Zero Sales means no eligible units sold in the chosen period **while sufficiently available for sale**; distinguish `Never Sold` if history is truly complete |
| INV-034 | Slow rule uses configurable thresholds including eligible selling days, velocity and minimum stock; do not mislabel newly introduced, frequently out-of-stock or retired merchandise |
| INV-035 | Stock value in each row is at inventory cost and is visible to every holder of `dashboard.inventory_intelligence.view` (§2.1) |
| INV-036 | Exclude warehouse from showroom demand classification unless warehouse demand model is separately defined |
| INV-037 | Explain each classification and confidence/coverage limitations in details drawer |

### 7.5 Stock transfer opportunities

| ID | Requirement |
|---|---|
| INV-040 | Dedicated table with ≤10 initial opportunities, View All; search/filter on source, destination, priority, category and main code/SKU |
| INV-041 | Columns: Main Code, Product/Exact Variant, Source Showroom, Source Current Available / Sales, Destination Showroom, Destination Current Available / Sales, Suggested Qty, Priority, Action |
| INV-042 | Match identical **variant/SKU** and business, never main-code-only substitution |
| INV-043 | Source must exhibit verifiable surplus; destination must exhibit verifiable replenishment need; use velocity, eligible selling days, projected cover, reservations and minimum retained stock |
| INV-044 | Recommend only positive whole-unit quantities within transferability and destination need; no overselling or negative stock |
| INV-045 | Prioritize via transparent configurable deterministic rule; show reasons, not unexplained AI scores |
| INV-046 | No same-source-and-destination recommendation; no cross-business or unauthorized location comparisons |
| INV-047 | By default only showroom→showroom opportunities; warehouse replenishment can be a separately approved future mode |
| INV-048 | Exclude or downgrade recommendations when insufficient history, poor stock-availability history, unusable inventory, pending source movements, stale balances or weak evidence |
| INV-049 | Each recommendation has freshness/as-of and is revalidated against live available stock at transfer creation |
| INV-050 | Clicking View Details opens a side drawer with both locations' sales, stock, velocity, cover, source retention, destination requirement and rationale |
| INV-051 | Suggested quantity is editable **only after** opening the transfer form; editing must revalidate against existing transfer policy |
| INV-052 | `Initiate Stock Transfer` opens prefilled existing stock-transfer create workflow; no stock mutation or auto-approval |
| INV-053 | `dashboard.inventory_intelligence.view` does not confer `stock_transfers.create`; users without the existing transfer permissions and source-location rights see a read-only recommendation |
| INV-054 | Recommendations must not be duplicated by main-code variant siblings or repeated identical source/destination/SKU keys |

### 7.6 Shared product detail drawer

| ID | Requirement |
|---|---|
| INV-060 | Show product image placeholder, name, main code, SKU/variant, category, permitted price/cost, selected-period sales, current stock and stock value |
| INV-061 | Show location-wise breakdown across only authorized locations |
| INV-062 | Exact dates, cost confidentiality, permissions and stock-as-of semantics remain visible |

## 8. Cross-cutting non-functional requirements

| ID | Requirement / target |
|---|---|
| NFR-001 | Security: all APIs authenticate and enforce the dashboard permission and Business isolation before query execution; fail closed. Location filters are validated as belonging to the Business (§2.1) |
| NFR-002 | Financial confidentiality: COGS, unit cost, stock valuation and profit are protected by the dashboard permissions in §2.1, with no separate cost or finance permission; no leakage to users without them via tooltip/export/debug payload |
| NFR-003 | Audit: log access to privileged dashboards where required by existing audit policy; audit transfer-request creation via existing audit kernel |
| NFR-004 | Accuracy: server computes money using `decimal`/PostgreSQL `numeric`; never JS floating point for authoritative calculations |
| NFR-005 | Timestamp accuracy: UTC storage, business timezone conversion and POS session business-date attribution; test midnight/DST-independent Bangladesh scenarios |
| NFR-006 | Performance target: at p95, dashboard summary ≤2s and table query ≤3s for agreed deployment data volumes on warmed datasets; validate with production-like data before sign-off |
| NFR-007 | Freshness: show `Last updated` and separate inventory snapshot as-of; initial target ≤5 minutes for routine summaries if architecture permits, with explicit stale warning |
| NFR-008 | Pagination: server pagination for View All tables; cap page size (proposed 100); stable sorting and count |
| NFR-009 | Accessibility: keyboard navigation, focus states, chart data table alternative, WCAG 2.2 AA as target |
| NFR-010 | Resilience: partial widget errors do not crash the other dashboard; timeout/retry/backoff and actionable no-data states |
| NFR-011 | Observability: tracing, latency, SQL timing, invalid-data counters, freshness watermark, recommendation generation metrics |
| NFR-012 | Maintainability: dashboard queries do not write to transactional tables; avoid long-running locks and ad hoc N+1 calls |
| NFR-013 | Data privacy: never surface customer PII, loyalty phone or personal payment details in aggregate dashboard endpoints |
| NFR-014 | Feature flags: allow staged rollout of valuation/profit/recommendation cards until authoritative semantics verified |

## 9. Business calculations and corner cases

1. **Reconciliation:** Net sales total = sum of included showrooms for same business-date range and status policy; income/payments are a separate ledger measure.
2. **Returns and exchanges:** Net unit/sales movements must reflect the *original sale and return recognition policy* approved in POS v2. Exchanges must not inflate sales by counting both gross legs without netting.
3. **Target gaps:** Missing targets are `N/A`, not zero; achievement cannot be computed against missing or zero targets.
4. **Profit availability:** Until product-level COGS, posting policy and expense attribution are confirmed, render estimated/partial or unavailable, and document which components are excluded.
5. **Stock quantities:** Negative stock is forbidden in transaction workflows; unexpected legacy/test negatives must be flagged, not silently included as available stock.
6. **Inventory values:** Quantity and value use the same inventory cost snapshot; reserved stock may still be physically owned and valued while not transferable.
7. **Moving versus zero sales:** Zero during a period does not establish permanent zero demand. Minimum eligible selling-day threshold is required.
8. **Attribution:** For multi-location transfers, neither dispatched nor received inventory is sales; avoid valuing in-transit twice.
9. **Filter precedence:** A widget-specific period override prevails for that widget with a visible badge; resetting override returns it to applied global period/default.
10. **Data anomalies:** Null cost, missing product main code, deleted/retired products, backdated documents and asynchronous reporting lag require explicit treatment; no invented values.

## 10. UI acceptance scenarios (Given / When / Then)

| ID | Scenario | Expected result |
|---|---|---|
| AC-01 | Owner opens Dashboard menu | Sees and can navigate to two distinct dashboard routes |
| AC-02 | Viewer chooses a showroom or `All` | Only that showroom, or every showroom of the current Business, contributes to Sales cards |
| AC-03 | Selects custom From/To with From after To | Validation error; no data request executed |
| AC-04 | Selects historical sales period | KPI labels read `Selected Period`; current stock is marked as current snapshot |
| AC-05 | Sales exceeds configured target | Short card says `Target Exceeded`, amount positive |
| AC-06 | No target configured | Target/achievement show N/A, no divide-by-zero |
| AC-07 | Ledger lacks valid COGS | Profit says unavailable, never inferred from purchases |
| AC-08 | Current stock value per location summed | Matches inventory valuation card to defined rounding precision |
| AC-09 | Best seller searched by Main Code | Exact product/variants found, no main-code/variant confusion |
| AC-10 | New item with two days of selling availability and zero sales | Not marked slow if minimum history threshold unmet |
| AC-11 | Product fast at Outlet 1, excess exact variant at Outlet 5 | Recommendation shows source, destination, matching SKU, explanation, safe qty |
| AC-12 | Source currently has pending reservations | Recommended transfer qty bounded by live transferable stock |
| AC-13 | Viewer lacks the existing transfer permission or source-location right | Can view the recommendation, cannot initiate a transfer |
| AC-14 | Owner clicks Initiate Transfer | Existing transfer-create form prefilled and validated; no immediate stock mutation |
| AC-15 | User has no default outlet or assignments | Dashboard access depends only on the dashboard permission; all Business locations are visible |
| AC-16 | Cross-tenant ID is supplied to an endpoint | 403/404 per security convention, no tenant data leaked |
| AC-17 | Cash session spans midnight | Sale is reported on approved business date, not naively on UTC/calendar date |
| AC-18 | Widget fails to load | Partial failure is visible and retryable; rest of page remains usable |
| AC-19 | Showroom filtered to a single location | Recommendations where that showroom is source or destination are shown, with counterparts from the same Business only |
| AC-20 | Inventory snapshot stale | Timestamp/staleness notice visible; recommendations flagged or suppressed per freshness policy |

## 11. Out of scope

- Creating or posting sales, purchases, expenses, stock corrections or transfers directly inside dashboard widgets.
- Replacing general Sales, Inventory, Accounting or Transfer transaction pages.
- Autonomous/AI-authored inventory transfer execution.
- Budgeting, sales forecasting, loyalty analytics and IFRS accounting reporting beyond the defined estimated-profit card.
- Financial statements, bank reconciliation, and historical stock valuation backtesting without authoritative snapshot data.
- Multi-tenant cross-business comparisons.

## 12. Integration verification and unresolved decisions

**Mandatory read-only code/schema discovery before implementation** (do not invent table or enum names):

| Ref | Verify in POS v2 repository / business policy | Proposed safe fallback |
|---|---|---|
| D-01 | Sale/return/exchange authoritative states, VAT and net-sales values | Block financial calculation until mapped |
| D-02 | Source of daily outlet targets and business-date ownership | N/A when unavailable |
| D-03 | Purchase recognized at posted GRN, invoice or accounting entry | Explicitly label chosen source |
| D-04 | Expense accrued versus paid; expense posting and payment ledger | Separate Expense KPI and Expense Payments chart |
| D-05 | Product COGS valuation/period allocation and operating expense applicability | Profit unavailable/estimated until proven |
| D-06 | Inventory valuation method (moving average/FIFO), cost availability and stock in transit | Use approved costing service; no selling-price substitution |
| D-07 | Existing product `MainCode` and variant/SKU model | Map from canonical catalog |
| D-08 | Exact reserved/damaged/in-transit/on-hand quantities and pending transfers | Conservative transferable stock |
| D-09 | Historical in-stock days/stockout events available | Mark insufficient evidence; use conservative rules |
| D-10 | Existing transfer-create route and its authorization | Reuse the exact existing workflow. Dashboard reads are decided by §2.1 |
| D-11 | Existing analytics/snapshot/outbox/Elasticsearch service availability | Start from optimized read-only PostgreSQL queries; add projection if needed |
| D-12 | Threshold defaults for slow movement, safety coverage, high-demand and priority | Configurable defaults requiring owner approval |
| D-13 | Whether profit and stock cost should be visible to all owners/managers | **Decided (§2.1):** visible to every holder of the relevant dashboard permission |
| D-14 | Maximum filter date window and target data volume | Limit by performance testing and product policy |
| D-15 | Whether warehouse→showroom replenishment is desired in this release | Excluded by default |

### Suggested implementation readiness gate

Do not begin coding until a mapping appendix lists real table/entity names, sale-status rules, inventory valuation authority, permissions, transfer route and agreed slow-moving thresholds. Prototype labels and demo figures are visual examples, not authoritative data definitions.

---

## 13. Mandatory UI design-source requirements — Able Pro Angular v3.3.0

**Source location (repository-relative):** `design-reference/able-pro/able-pro-angular-v3.3.0`

**Reference hierarchy:** The supplied **Figma Make Sales & Finance and Inventory Intelligence prototypes** remain authoritative for feature scope, dashboard content, information hierarchy, product tables and workflows. **Able Pro Angular v3.3.0** is the mandatory primary *visual design and reusable component reference* for implementing those screens. Existing IraniMart design, accessibility, branding and coding conventions take precedence where required for application consistency. Do not substitute Able Pro's demo business semantics for IraniMart's SRS.

| ID | UI requirement | Acceptance criterion |
|---|---|---|
| UI-01 | Inspect the local Able Pro Angular project before implementing either dashboard. | A verified mapping identifies reusable components, styles, dependencies, and compatible patterns; no guessed imports or paths. |
| UI-02 | Reuse/adapt existing compatible card, chart, table, dropdown, date-filter, tab, badge, icon, drawer/modal, breadcrumb, tooltip, loading and responsive grid patterns. | Dashboards match Able Pro's visual language while retaining all approved Figma dashboard content. |
| UI-03 | Reuse the **existing POS v2 application shell** (sidebar, topbar, branding, menu, routes, theme) rather than replacing the app with Able Pro's demo shell. | Two dashboard routes appear in current navigation, with no unrelated demo pages, banners or menu items. |
| UI-04 | Maintain all mandatory Owner Dashboard data fields, including Product Main Code, SKU, filters, slow-moving table and transfer opportunity details. | Every table and interaction in this SRS remains visible and works after styling integration. |
| UI-05 | Comply with accessibility and responsive layout requirements, including keyboard navigation, labels, text contrast, and tabular financial alignment. | Functional use on desktop/tablet and with keyboard; no information conveyed solely by colour. |
| UI-06 | Do not ship Able Pro demo data, demo pages, vendor promotional elements, fake transactions, or unused libraries. | Only IraniMart-specific routes and data appear; no Able Pro marketing UI remains. |
| UI-07 | Use the Able Pro reference only within its applicable licence and permitted use. | Licence/asset/dependency compatibility is checked and recorded before copying code/assets. |
| UI-08 | If a component would require incompatible Angular/Bootstrap/chart library versions or a competing shell, adapt its appearance using existing project dependencies instead. | No unapproved framework upgrades, duplicated UI frameworks, dependency conflicts or regression of existing POS pages. |

**Screenshots of Able Pro supplied by the owner** demonstrate reference patterns for sidebar and header, compact KPI cards and sparklines, chart containers, multi-series charts, tabs, transaction lists, and circular/pie visuals. They are style references; the new dashboards should use visualizations appropriate to the actual Figma business requirements rather than adding unrelated widgets.

### 13.1 SRS review and sign-off

The Owner Dashboard is accepted only when both dashboards visually fit the current POS v2 application, use Able Pro-derived compatible components wherever practical, preserve the approved dashboard layout and interactions, and do not alter existing feature behavior. Any visual deviation due to incompatibility must be documented before implementation.

---

**End of SRS v1.2**

## Addendum (2026-11-06): Sales targets connected

Resolves D-02: targets now come from the outlet sales target configuration (POS SRS §27). The access model is unchanged: `dashboard.sales_finance.view`, business-wide, with no location restrictions.

| Case | Target | Achievement and shortfall | Status |
|---|---|---|---|
| Configured | Sum of daily targets, or the override for an exact period | Calculated | `ok` |
| Scheduled off day | 0 | None; sales stay visible | `off_day` |
| Disabled or missing | N/A, never zero | None | `not_configured` |
| Only some showrooms or dates have a target | Sum of those, marked Partial | Compares only sales on days that have a target | `partial` |

A zero target on an open day shows no percentage. Today's Target and Today Short are shown when another period is selected.
