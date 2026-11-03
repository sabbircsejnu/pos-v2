# IraniMart POS v2 — Master Software Requirements Specification (SRS)

**Document ID:** IM-POS-SRS-MASTER  
**Version:** 1.2 — approved business-decision consolidated specification  
**Date:** 2026-11-02  
**Status:** Revised specification for final review; implementation NOT authorized  
**Target folder:** `Docs/08_Sales_And_POS/`  
**Companion:** `IRANIMART_POS_MASTER_LLD_V1_2.md`  
**Design reference:** `iranimart_pos_prototype.html` (interactive UX reference, not production code)

**v1.2 precedence note:** Sections 18–26 provide controlling amendments to the existing §1–17 baseline; in any inconsistency v1.2 approved rules prevail.

## 1. Purpose and authority

This SRS consolidates the uploaded `PHASE_01_IMPLEMENTATION(1).md`, `PHASE_01_IMPLEMENTATION_UPDATED(1).md`, and the approved `POS_PHASE_01_UPDATE_ADDENDUM(2).md`, plus the later approved prototype v3 screens and interaction changes. It specifies the intended customer-facing workflow, validations, financial behaviors, access, and acceptance. The companion LLD describes implementation design. **Preserve existing successful checkout/accounting/inventory implementation; audit the repository first.** Prototype sample numbers, names, navigation links and JavaScript behavior are illustrative, not authoritative pricing, tax, permission or ledger logic.

**Precedence:** (1) explicitly approved business decisions, (2) this consolidated SRS, (3) latest approved prototype for layout/interaction, (4) older baseline for nonconflicting details. Conflicts with existing legal/accounting/authorization policy must be surfaced during gap analysis, not silently overridden. All money displayed in BDT; server money uses decimal, never floating point.

## 2. Scope and decision register

| ID | Approved requirement |
|---|---|
| D01 | No standalone Cash In/Cash Out. Business cash outflow uses Expense Module; closing handles cash handover/bank deposit. |
| D02 | Expense payment source includes POS Cash Drawer, separate Petty Cash, existing bank/other accounts. |
| D03 | Strict 20-hour cash-session checkout cutoff; **Close Old Session → Open New Session → Resume Sales**; no role bypass. |
| D04 | Terminal Add/Edit/Delete/Deactivate is embedded within Outlet Create/Edit; fields Name, Code, Description, Active. |
| D05 | Each outlet has seven weekday daily targets; weekly, first-half (1–15), second-half (16–end), and monthly default auto sums with independent manual overrides. |
| D06 | Compact POS context; no redundant business-date/clock/item counts/shared-cashier banner; small close/expand actions. |
| D07 | UI/receipt use **VAT**; internal generic Tax components with immutable VAT snapshots and configurable inclusive/exclusive behavior. |
| D08 | One normalized phone per customer per business; sale may be attributed to a relative's loyalty account without claiming that person purchased. |
| UX-v3 | Preserve supplied POS v3 visual design; sticky footer actions; rich customer/loyalty cards; cash session landing/open/close/history/detail; post-sale confirmation. |

### 2.1 Functional solution coverage

- Existing checkout completion, sales, stock and payments preserved and hardened.
- Outlet-embedded terminals and sales targets; outlet-validated terminal selection.
- Cash Sessions landing, Open Cash Session, valid-session entry, read-only historical details/summary, Close Cash Session/reconciliation/denomination/handover/attachments.
- Shared authorized cashiers in one terminal session; separately selected salesperson.
- One barcode/name/SKU/variant search, editable cart, customer search and quick add, discounts, VAT, split tender, hold/recall continuity and sticky checkout actions.
- Stable customer phone identity and loyalty-account linking **without enabling unauthorized redemption**.
- Receipt/print/PDF, void permission, audit, durable events, pre-aggregated reporting readiness.
- Outlet/day target-versus-sales on session landing page, including variance icons.

### 2.2 Related modules and specification boundaries

This blueprint covers the complete POS checkout, terminals, cash-session and closing experience, plus the contracts needed to integrate approved adjacent modules. **Returns/exchanges** have a separate SRD but must integrate with sale reversal, stock, session cash, and reporting. **The Loyalty Programme** has its own 40-decision SRS/LLD and must be integrated for real tier, earning, redemption, OTP, and ledger behavior when the in-process Loyalty Module is enabled. **Promotions/coupons** require an existing or separately approved campaign/offer engine; do not invent policy. **Desktop offline POS** has a separate offline-first specification; retain idempotent checkout and sync-compatible contracts but do not imply an online browser implements offline sales. **Dashboard visualization**, bespoke receipt designer, and advanced analytics are separate experiences, although data projections and event contracts are required here. Layaway, reservation, quotation conversion, sales-order conversion, ecommerce pickup, click-and-collect, and mobile POS are deliberately **not required**. A prototype-only interaction must never post fake points or payments.


### 2.3 Authoritative Customer Loyalty Module documents (cross-reference)

The POS must integrate with the existing independently maintained Loyalty Module specifications at the following project-relative paths (relative to `pos-v2` repository root):

- `Docs/Customer_Loyalty_Module/CUSTOMER_LOYALTY_MODULE_SRS.md`
- `Docs/Customer_Loyalty_Module/CUSTOMER_LOYALTY_MODULE_LLD.md`

**Authority boundary:** these two documents are the source of truth for membership tiers, earning, OTP redemption, reservation/expiry, reversal, point ledgers, tier calculations, campaigns and loyalty accounting. This POS SRS owns checkout placement, customer/account selection, consent/verification presentation, integration, payment/tender reconciliation and cashier workflow. Where integration rules conflict, Copilot must record the exact conflicting requirement and request blueprint amendment; do not silently implement an alternative. The Loyalty Module folder is sibling to `Docs/08_Sales_And_POS/`. **Read and verify both authoritative Loyalty documents in the repository before implementation; their detailed policies are not reproduced here.**

## 3. Personas and access context

| Role/context | Capabilities |
|---|---|
| Authorized outlet cashier | Enter eligible open terminal session and sell; authenticated identity recorded as `CashierUserId`. |
| Selected salesperson | Eligible active user attributed to this transaction; distinct from cashier; no login impersonation. |
| Session opener | Authenticated person who opens cash session; default salesperson if currently eligible, but not exclusive seller. |
| Authorized closer/manager | Can reconcile, attach evidence, hand over cash, close a session under independent permission. |
| Outlet administrator | Manages own authorized outlet, POS terminal settings and targets as permitted. |
| Business owner/Super Admin | Established wildcard/platform scope honored; cannot bypass 20h checkout rule. |

Scope requests by authenticated business and accessible outlets. Reuse existing user role, permission catalog and step-5 outlet assignment semantics. `sales.create` does not grant customer create/edit, session close, terminal administration or target administration.

## 4. Screen inventory and reference behaviors

| Screen | Main requirements |
|---|---|
| POS Sale — default | Existing ERP shell; condensed session/context; left cart and customer/loyalty; right dark summary and editable tender table. |
| POS Sale — fullscreen | Hide ERP shell and expand POS, retain same controls/data; Escape/exit control. |
| Find Customer / Loyalty Account | Search by name/normalized phone, distinguish existing customer/account; Select and Quick Add. |
| Quick Add Customer | Name, mobile required; optional gender/DOB; normalized unique phone; Create & Select. |
| Loyalty Account Linked | Account holder, masked phone, tier, points/expiry and estimated earn when real service available. |
| Redeem Points | UI-ready OTP modal and eligibility explanation; must not execute real redemption without approved online loyalty integration. |
| Cash Sessions — landing | Outlet/terminal context, no-active/overdue state, open action, three target performance cards, filterable paged prior sessions. |
| Open Cash Session | Reference v3: navy title, context and entry panel, right opening summary, sticky confirmation footer. |
| Close Cash Session | A: session; B: reconciliation; C: optional denomination; D: handover/attachments; sticky closing footer. |
| Cash Session Summary | Read-only record with status, reconciled amounts, handovers, session activity and related sales link; print/PDF/back. |
| Sale Completed | Invoice, amount, customer, payment/change, actual cashier and salesperson; New Sale, Print and Reprint. |
| Outlet Create/Edit | Existing fields with POS Terminals and Sales Targets sections added; same AblePro styles. |
| Expense Create/Edit | Existing expense flow enhanced with Payment Source and scoped POS Cash Session selection. |

**Prototype visual fidelity:** Recreate sizing, compact whitespace, typography, border radius, navy payment block, product/variant table, cards and modal hierarchy from `iranimart_pos_prototype.html`. The prototype is a reference, not an excuse to replace existing Angular page or create a parallel POS module. Use accessible and keyboard-operable HTML controls.

## 5. Outlet setup, terminals and sales targets

### 5.1 Terminals

**FR-TERM-01** Existing Outlet Create/Edit form (as shown in the supplied existing application screenshot) remains the entry point: Outlet Name, Address, Contact Number and Manager must be preserved. Within this same Outlet form, add a **POS Terminals** section/list and **Sales Targets** section. Terminal creation/update must NOT be performed from a disconnected standalone POS Terminals module. Show terminal name, outlet-scoped unique code, description, active flag and add/edit/delete/deactivate controls. For existing outlets, load the current linked terminal rows when the Edit Outlet screen opens.

**FR-TERM-01A** On Create Outlet, allow adding several terminal draft rows before Save; save the Outlet and terminal rows coherently using the existing create workflow. On Edit Outlet, list terminals belonging to the current Outlet only and open their Add/Edit dialog inline or as a drawer without leaving the Outlet context. Terminal form fields are Name (required), Code (required), Description (optional), Active (toggle). Terminal's OutletId is taken from the Outlet context, never typed freely.

**FR-TERM-01B** The active terminals configured here are the authoritative list to load in **Open Cash Session** and **POS Sales** for the selected authorized outlet. If exactly one active terminal is eligible, auto-select and display its name; if multiple, let the user choose; if none, show setup required and prohibit session opening/checkout. Refresh terminal selection after an admin changes configuration; never fabricate `No terminals` while a valid active terminal exists. Keep identifiers stable and historical associations intact.

**FR-TERM-02** A new outlet may be saved without a terminal; POS cannot start if there is no eligible active terminal. One active terminal auto-selects; multiple require explicit valid selection or approved device binding.

**FR-TERM-03** Hard delete only if no historic sale, session, printing/audit or other dependency; otherwise deactivate. An open or overdue session blocks deactivate/delete. A terminal with history cannot move to another outlet. Enforce uniqueness and concurrency server-side.

### 5.2 Targets

**FR-TARGET-01** Set separate nonnegative targets for Saturday through Friday. An unset target is `Not configured`, not zero or met.

**FR-TARGET-02** Auto-populate weekly sum of all seven weekdays; calculate first half **1–15**, second half **16–last calendar day**, monthly full selected month from actual weekday occurrences (including leap year). No 14-day rolling biweekly rule.

**FR-TARGET-03** Weekly, first-half, second-half, monthly each allow independent manual override and Reset to Auto. Auto updates do not erase a manual override. Effective dates/versions and actor audit preserve historical comparisons; period overrides are tied to their intended calendar period.

**FR-TARGET-04** Cash Session landing shows three prominent cards: Daily Target, This Business Day's Sales (all outlet terminals), signed variance = actual − target. Positive: green smile/joy icon; negative: red sad icon; zero neutral check; missing target neutral `Not configured`. Sales metric suggested: net completed sale amount excluding VAT, after discounts and posted void/return corrections; align with existing authoritative reporting metric during gap analysis. Projections may lag and must indicate freshness/fallback.

## 6. Cash Sessions — landing/open/lifecycle

**FR-CS-01** On POS entry resolve current outlet and terminal. A valid open session routes to POS Sale. Without session show the Cash Sessions landing page and `Open Cash Session`. Overdue session must be closed before checkout. Only one unclosed session per terminal; multiple terminals may have separate sessions.

**FR-CS-02** Landing follows supplied reference: title/action, outlet-and-terminal context card, no-active/ready-to-start panel, previous-session list with filters for Business Date, Terminal, Status, Opened By, pagination, View link. Include Opening/Closing Cash, opened/closed times, variance and status. Correct breadcrumbs belong to Sales/POS, **not** prototype's sample `HR > Employees`.

**FR-CS-03** Open page follows screenshot: navy title, left section Outlet (read-only), Terminal (required selector only when multiple), Business Date (server-derived, read-only), Opening Date/Time (server-derived), required Opening Cash ≥ 0, optional Notes; right Opening Summary; sticky Cancel/Open footer. `BusinessDate` is opening date in outlet's configured local timezone and remains immutable; no client backdating of session open.

**FR-CS-04** Persisted session states `Open → Closed` with derived operational `Overdue` while Open; overdue remains closable, never auto-financially closed. Session opener is not exclusive. Every mutation records actual actor; historical closed sessions are read-only.

**FR-CS-05** Session detail screen reproduces v3: closed/overdue status, Overview, source-based Cash/Card/Mobile breakdown, Expected/Actual/Variance, cash handover, audit activity timeline, View Related Sales, Print Summary, Download PDF, Back to Sessions. Do not present cash handover as operating expense. Printing uses real archived data, not prototype sample fixtures.

## 7. Twenty-hour cutoff and business dates

**FR-TIME-01** Maximum elapsed selling time: configurable per business with default 20 hours, never greater than 20 hours; effective duration is snapshotted at opening. Sale commit is permitted only strictly before opening + the session's snapshotted duration; at exact boundary it fails for all roles.

**FR-TIME-02** Warn at 18h and stronger warning at 19h; `Overdue` at ≥20h blocks sale completion, invoice/payment/stock posting. Preserve unsent cart. Close Old Session → Open New Session → Revalidate cart/stock/price/VAT/discount/customer/payment → Resume Sales. Closing permitted after expiry. Server validates within transaction; local clock cannot bypass.

**FR-TIME-03** Session `BusinessDate` is the opening local calendar date; all valid sales crossing midnight remain under it. Store distinct actual `CreatedAtUtc`, effective `SaleDateTime` (backdated only with `sales.backdate`, never future), and immutable `BusinessDate`. Backdate does not change session date or bypass financial period controls. Authoritative inventory/accounting posting date policy must be reviewed, not automatically backdated.

## 8. POS screen / cart and checkout UX

**FR-POS-01** Preserve existing ERP navigation shell unless fullscreen. Compact context shows Cash Opening, live `Open for`, logged-in cashier and selected salesperson in top line, small Close Session icon with tooltip/confirmation and fullscreen control; another compact context row has Outlet, Terminal and Sale Order Number. No duplicate Business Date, oversized digital clock, shared cashiers text or duplicate item/quantity header fields. Permission-only Sale Date is allowed if actually approved/needed; newest prototype omits it visually, so resolve placement at UI acceptance while retaining backend backdate permission.

**FR-POS-02** Single prominent search input supports barcode, SKU, product name/code, variant attributes; product browse icon **inside** input trailing edge, not a duplicate input/button. Search results must show name, variant (color, size, fabric), main code, SKU, barcode, selling price, **current outlet stock**. Repeated scan of same variant increases quantity. Unavailable stock cannot be sold.

**FR-POS-03** Left cart table: Product/Variant image/name/attributes, Unit Price, Qty, Discount %, VAT, Line Total, Remove; no redundant standalone Product Key/Description columns. Qty field large enough for direct typing plus +/- with product UOM rules and server-side stock gate. Keep product list scroll independent of sticky bottom actions.

**FR-POS-04** Customer and Loyalty block: seeded immutable Walk-in Customer; Lookup, Quick Add, selected account, link loyalty. Selected phone may be a relative's; do not infer physical purchaser. Registered customer/loyalty may be linked to a sale, but redemption and points display are gated by real loyalty availability.

**FR-POS-05** **Sticky left footer:** Hold Invoice, Recall Invoice, Link Loyalty, Clear Cart; quick-search help above it. **Sticky right footer:** only `Complete Sale` (F9); no duplicate Clear/Hold in right footer. Persistent dark payment summary and tender section. Ensure at 1366×768 checkout remains usable without scrolling to find key actions.

**FR-POS-06** Display item/quantity, MRP subtotal, configured SD component if actually applicable, (+) VAT, (−) discount and payable; tender table has Round Off, Payable, Cash, Card, Mobile Banking, Change, editable amounts/reference when required. **No separate Payment Breakdown tabs**. Cash entered/tendered field readable with sufficient padding/height. Currency BDT. Payment fully covers payable, no credit sale; allow split tender, calculate change only on cash over-tender, avoid pretending card/mobile overpayment is cash change.

**FR-POS-07** Line discount vs invoice discount mutually exclusive. Switching when a nonzero existing discount would be removed requires confirmation; cancel leaves values unchanged. Server rejects both simultaneously. Other coupon/promotion eligibility follows separate engine scope; prototype coupon display is not proof of support.

**FR-POS-08** Preserve Hold/Recall functionality where supported; do not invent persistence semantics beyond established implementation. Keyboard shortcuts: F2 search, F4 browse, F6 Hold, F7 Recall, F9 Complete (subject to focus and accessibility). Fullscreen must not clear state. Sale completion idempotent and atomic with immediate outlet stock deduction.

## 9. Customer and loyalty requirements

**FR-CUST-01** Within a business normalized Bangladesh phone is unique for one registered customer (including `017...`/`+88017...` equivalence). Handle existing duplicates safely before DB constraint. Non-phone Walk-in seeded record remains protected and stable, not editable/deletable.

**FR-CUST-02** Lookup is available only under existing `customers.view`; Quick Add requires `customers.create`; Edit requires `customers.edit`; no implicit grant by `sales.create`. Search results mask phone where appropriate. On create, race-safe duplicate recheck must offer existing record.

**FR-CUST-03** Purchaser may nominate a family member's registered account for this sale. `CustomerId`/selected loyalty-account association is stable per sale; optional actual physical purchaser identity is **not** required. Phone alone never authorizes redemption.

**FR-LOY-UI-01** Prototype states may show membership tier, available/expiring points, estimated earning, redeem modal with 6-digit OTP, and sale-completed points summary. Real values, availability, OTP reservation, expiry and redemption only after integration with separately approved Loyalty SRS/LLD. Avoid hardcoded reward values, misleading displayed points, or simulated successful financial redemption in production.

**FR-LOY-UI-02** In design, points earning is based on eligible net product consideration after excluding VAT, discounts, loyalty redemption and non-product charges as defined by the Loyalty SRS (subject to approved loyalty exclusions, rounding and reversals); on actual implementation use the approved loyalty specification, not calculations from the HTML prototype. Redemption online-only and OTP-protected; offline or unconfigured service must disable action with clear text.

## 10. Expense, reconciliation and closing

**FR-EXP-01** Reuse Expense Module, adding Payment Source `POS Cash Drawer`, `Petty Cash`, existing Bank/other accounts. Replace raw Payment Account ID and Outlet ID fields with permission-scoped searchable selectors. Drawer-funded expense requires specific active terminal session; petty cash/bank do not change POS drawer. Only verified physical POS Drawer disbursement affects drawer, exactly once, under the D7 approval and original-session cash-effect policy; accounting posting is asynchronous where specified.

**FR-CLOSE-01** Close page follows prototype v3 four sections (A information; B reconciliation; C optional denomination; D handover/attachments) with sticky footer containing Cancel, Review Closing Summary, Confirm & Close. Closer requires separate permission.

**FR-CLOSE-02** `ExpectedDrawerCash = OpeningFloat + NetCashRetainedFromCompletedCashSales − PostedDrawerExpenses − NetCashRefundsOrApplicableVoidCashReversals + AuthorizedExistingCorrections`. Never double-count change or reversals; card/mobile cannot increase physical cash. Show reconciliation from authoritative records, not delayed dashboard projections.

**FR-CLOSE-03** Counted Cash required; denomination optional and totals reconcile if used. `Variance = CountedCash − ExpectedDrawerCash`; nonzero requires reason. Discrepancy audit, **never** auto-create expense or silently mutate ledger.

**FR-CLOSE-04** Multiple handover lines support method Cash to Manager/Owner or Bank Deposit, Amount, Given By, Taken By/Recipient, notes, bank account/reference/date when applicable, optional evidence. Show `Counted = HandedOver + RetainedAtOutlet`, no negative retained amount. Deposit awaiting bank verification must not be labeled confirmed. Custody/bank transfer is **not** operating expense.

**FR-CLOSE-05** Support multiple photo/PDF attachments with secure scoped storage, validation, authorization, metadata and download. After successful close freeze session financial snapshot, prevent new sale/expense, double close and late commits. Any corrections require compensating authorized workflow.

## 11. Sale lifecycle, void and receipts

**FR-SALE-01** Lifecycle names: Draft, Hold, Completed, Cancelled/Void, Returned, Exchange (latter two have separate SRD execution). Preserve sale and related return/exchange references; do not rewrite completed transactions.

**FR-SALE-02** Completed sale requires at least one item, valid price from selling-price pipeline, positive quantity, no negative stock, total payment, valid active session and authorized outlet/terminal/customer/salesperson. Backend checks discount and VAT, prevents duplicate idempotency key and atomically commits sale, payment, inventory, immutable financial posting intent, audit and outbox; GL posting follows asynchronously under D10.

**FR-SALE-03** User with `sales.void` can void eligible completed sale **regardless of age**, with reason/audit and correct once-only inventory/accounting/payment effects. No void time window. No double reversal.

**FR-RCPT-01** Print 80mm thermal by default, optional A4, reprint, PDF. Canonical immutable receipt includes business/outlet, invoice, timestamps, cashier, salesperson where configured, customer display, variants/qty/selling price/discount/VAT, payment/tender/change, footer/return text, optional barcode/QR. Compatible print provider with browser dialog fallback; cannot reliably detect arbitrary printer or silently print from standard browser. Failed print never reverses sale.

**FR-RCPT-02** Sale Completed modal shows invoice, actual payable, cash and change, selected customer, actual cashier vs credited salesperson, and New Sale / Reprint Receipt / Print Receipt. Awarded points shown only when confirmed by real loyalty service. Print history and failure audit required.

## 12. Reporting and target-readiness

**FR-REPORT-01** Capture business/outlet/terminal/session/business date, created/effective time, cashier/salesperson, customer account, product/variant/category, selling price, discounts, VAT components, tender and lifecycle/void facts on immutable transactions.

**FR-REPORT-02** Durable outbox and RabbitMQ publish versioned facts; idempotent consumers incrementally maintain rebuildable outlet-day, terminal-session, cashier-day, salesperson-day, product-variant-day, payment-method, VAT, target variance and session-close projections. Void/refund corrections once only. Reporting endpoints do not lock checkout and never authorize stock/cash close.

**FR-REPORT-03** Historical reports retain business date even when actual sale time crosses midnight; salesperson attribution remains correct after staff transfer. Define and version gross/net/returns/VAT metrics and projection freshness.

## 13. Security, audit, nonfunctional

- Server-authoritative business/outlet/terminal scope; phone uniqueness; permissions for customers, sales.backdate, sales.void, terminal settings, session open/close, targets, expense, attachments and reporting rebuild.
- Log who opened/closed, actual cashier, chosen salesperson, customer/loyalty association, cash discrepancy, expense/reversal, handover, terminal/target changes, receipt print/PDF, declined 20h checkout attempts, correlation ID and UTC timestamp. Never expose OTP or sensitive credentials in logs.
- Target normal-LAN initial page <2s; cache-hit lookup and scan-to-cart <100ms; typical ≤20-line checkout API <1s and printing initiated <2s, subject to measured test environment. Errors should not cause partial sale and printer failures must not block an already committed sale.
- Concurrency on session open/close, payment, stock and ledger. Accessibility: tooltips for icon actions, text plus color for variances, focus control, screen reader labels, keyboard/ESC, sticky actions reachable on target resolutions.

## 14. Acceptance tests

| ID | Scenario / expected result |
|---|---|
| AC-01 | Outlet edit saves terminals + weekday targets; reused codes rejected; historical terminal cannot delete, open-session terminal cannot deactivate. |
| AC-02 | One terminal auto-selected, multiple selection required, zero blocks; concurrent open creates at most one unclosed session per terminal. |
| AC-03 | Session landing filters/history/View opens closed detail; detail shows authoritative reconciliation, handover, timeline, print/PDF and return. |
| AC-04 | Open page reflects prototype, server-derived BusinessDate/OpenedAt, nonnegative float, right live summary and footer. |
| AC-05 | Two authorized cashiers share session; actual cashier differs from selected salesperson; performance attributed separately. |
| AC-06 | 09:00 opening, sale 03:30 next day uses opening business date; commit at exact 20h denied; only close/reopen re-enables checkout. |
| AC-07 | Expense from Drawer B reduces only session B once; Petty Cash/Bank have no drawer effect; reversal once. |
| AC-08 | Closing counts, variance reason, optional denominations and multi-handover + bank evidence produce immutable correct summary, no double expense. |
| AC-09 | Different weekday targets, independently editable weekly/half-month/month overrides and reset; landing target and all-terminal actual, green/red/neutral icons. |
| AC-10 | Bangladesh phone normalized; repeat sale same customer; family-account credit allowed; unknown phone quick-add guarded by customers permission. |
| AC-11 | Prototype-style cart and sticky left action bar/right Complete; search browse trailing icon, direct qty entry, VAT/tender table, fullscreen preserves state. |
| AC-12 | Swapping line/invoice discount asks confirmation; server prevents both; no negative inventory, no credit sale, idempotent completion. |
| AC-13 | Void requires sales.void and reason, any eligible age, reverse stock/financial facts once, durable audit. |
| AC-14 | Receipt auto-print attempt/PDF/reprint works with canonical snapshot; printer failure does not undo completion. |
| AC-15 | Loyalty display and OTP are disabled when real loyalty backend is absent; no fabricated points/redemption; integration uses separate approved loyalty contracts. |
| AC-16 | Outbox/replay/inbox dedupe prevents double analytics; rebuilding projections agrees with authoritative source. |
| AC-17 | Backdate permission enforced server-side; cannot move BusinessDate, bypass cutoff or future date prohibition. |
| AC-18 | Current ERP menus, customer permissions, inventory and payment integrations continue without regression. |

## 15. Repository gap analysis and change-control workflow

This document and its companion LLD are **the intended blueprint**, not a claim about current repository implementation. GitHub Copilot must **first** review the actual project and produce a read-only gap analysis, without editing production code, database schema, migrations, or documentation. For each requirement provide `Implemented / Partial / Missing / Conflict / Needs verification`, implementation evidence (path, symbol, migration, route), impact/risk, proposed correction, and tests.

**Required review deliverables:** existing-state architecture map, API/DTO comparisons, permission mapping, database schema and data-quality findings, POS v3 visual differences, integration readiness for Loyalty/Returns/Expense/Reporting, financial consistency analysis, migration and rollback plan, and a prioritized list of gaps. Do not silently resolve conflicts between approved requirements and existing accounting/security policy. User reviews gap findings; **then** SRS and LLD may be amended by explicit agreement; **only then** request authorization to implement fixes. There is no prescribed delivery phase order in this blueprint.

## 16. Extended end-to-end requirements

### 16.1 Checkout states and related documents

- Persist `Draft`, `Hold`, `Completed`, `Void/Cancelled` distinctions using the actual existing sale state model; no sale with payments may silently become a draft. Preserve Hold/Recall where implemented and ensure duplicate recall cannot create duplicate sales.
- Full and partial returns and exchanges may occur immediately or on a later date against an original completed sale. They require original-sale links, line-level returned quantities, immutable reversal evidence, and separate authorized processing governed by the Returns/Exchange SRD. A replacement order may include newly bought items; a return may contain no new purchases. Never rewrite the original sale.
- Authorized `sales.void` users may void any eligible completed sale regardless of age, with reason, audit, and exactly-once reversal; a past-date void is not equivalent to a return.

### 16.2 Payment and checkout behavior

- Support Cash, Card, Mobile Banking and split payments in a single checkout. Payment is required in full before completion; credit sale is not supported. Reject negative payments, over-allocation (except cash tender with computed change), and forged client-calculated amounts.
- The right-hand panel includes a navy sales/tax summary, tender table with Method/Amount/Reference, round-off, payable, Cash Tendered, Change, plus one sticky `Complete Sale` CTA. No redundant cash/card/mobile tab block. If a mode requires a provider reference, show and validate it without expanding the checkout unnecessarily.
- Product search shows available stock at **current outlet** (not whole business), variant attributes and selling price. Repeated scan increments the matching variant's quantity. Final stock is checked atomically server-side; never allow negative stock.
- Unit price is the configured Selling Price; no arbitrary manual price override. A line discount OR an invoice discount is allowed, never both nonzero; changing modes requires confirmation before discarding an existing discount. Coupon/promotion and loyalty redemption must have explicit calculation ordering and non-stacking policy from their own approved specifications rather than being misclassified as the mutually exclusive manual discount.

### 16.3 Loyalty integration requirements

- Within each business, normalized phone uniquely identifies a registered customer. Customer/loyalty lookup and Quick Add respect `customers.*` permissions. Seeded Walk-in Customer remains a real, protected system record.
- Buyer may nominate a relative's registered phone as the loyalty account credited for the purchase. Store that selected account without asserting the account owner is the physical purchaser. Separate purchase attribution and loyalty holder identity when needed; never grant redemption based on phone knowledge alone.
- When the authorized loyalty service is connected, display current tier, points balance, expiring balance, and estimated points. Points earning is based on eligible payable **after VAT and discounts**, governed by the separately approved Loyalty SRS (including 1 point per ৳100, tier multipliers, rounding, reversal and exclusions).
- Redemption uses the approved online-only OTP + reservation workflow, minimum 100 points, value ৳1/point and at most 50% of eligible invoice amount. Reconcile reservation commit/cancel/retry with sale completion. OTP must not be stored in logs. If the programme is paused/unconfigured or online OTP requirements cannot be met, disable redemption without blocking ordinary eligible POS sales.
- Use in-process Loyalty MediatR ports and authoritative programme statuses. Prototype figures/tier/sample OTP are illustrative, never production data.

### 16.4 Cash session, expense and close

- A terminal can have only one current Open or Overdue session. Multiple authorized cashiers may share it. Actual cashier differs from session opener and selected salesperson.
- Cash session `BusinessDate` derives from opening outlet-local date and remains fixed across midnight. At exactly 20 hours after authoritative `OpenedAtUtc` no sale can commit. User must close old session, then open new session, then resume selling; preserve and revalidate pending cart.
- No standalone Cash In/Out module. POS drawer paid/posted expenses are recorded in Expense Module with exactly one CashSession linkage; Petty Cash and bank expenses must never reduce terminal drawer. Actual closing cash must be entered; variance requires reason.
- Closing captures optional denomination breakdown, handover to people, bank deposit details, retained balance, documents, and a read-only, printable/PDF historical summary. Deposits and handovers are custody transfers, **not** operating expenses. Never count a pending bank deposit as confirmed without evidence.

### 16.5 Outlet targets, reporting and cash-session landing

- Outlet setup has seven weekday daily targets and auto-calculated, independently overridable Weekly, Semi-Monthly 1–15, Semi-Monthly 16–end, and Monthly targets with effective-dated history. Biweekly does not mean 14 days.
- Landing shows Daily Target, that Business Date's combined sales across **all outlet terminals**, and amount Above/Below Target with green smile/red frown/neutral equal indicator. Reporting projection must support corrections, voids and late events; cash closing does **not** use eventual reporting totals.

### 16.6 Accessibility and interaction

- Follow prototype v3 layout and current ERP design tokens: compact single header card, customer/loyalty identity row, product + variant image/table, accessible modals, navy right-hand payment panel, sticky left actions (Hold, Recall, Link Loyalty, Clear Cart) and sticky right `Complete Sale` button. `Browse` is an integrated search-field action; maintain barcode focus and keyboard shortcuts.
- Fullscreen hides ERP chrome, not operational session controls. Persist cart/customer/salesperson/payment state through fullscreen toggle. All actions and icons require keyboard access, labels/tooltips, focus states and sufficient contrast.
- Session history is filterable/pageable; its `View` opens **read-only** historical Cash Session Summary with reconciliation, cash handover, activity timeline, print and PDF. Screen breadcrumbs must refer to Sales/POS, not HR/Employees.

## 17. Blueprint verification and sign-off

**Document scope:** Full POS Sales and Cash Session blueprint, with explicit external-module integrations. This SRS is meant to be revised after codebase gap analysis, **before any fixes begin**. Older Phase 01 documents remain unchanged for historical traceability. Deviations require a documented user-approved revision; no assumption that this file reflects existing implementations.


---

## 18. Version 1.2 authority, decision freeze and baseline corrections

**This section and §§19–29 are normative.** Where their requirements differ from earlier §§1–17, v1.2 controls. Earlier approved UI, targets, session and checkout requirements remain in force unless expressly superseded here. This revision is documentation only; no source, test, migration, database or deployment action is authorized. Evidence baseline: `POS_MASTER_BLUEPRINT_GAP_ANALYSIS.md` Rev 3 (§O), including 541 DB-free passing tests and no executed PostgreSQL integration tests. Report estimates 3 implemented / 31 partial / 17 missing / 21 conflicting / 2 verification-required matrix rows; it is not an implementation completion certificate.

### 18.1 Approved decision index (D1–D14)

| ID | Frozen policy | Primary requirements |
|---|---|---|
| D1 | Actual cash refund belongs to the **active session dispensing cash**; original sale remains linked; closed session snapshot immutable; exactly-once cash effects | FR-VR-01 |
| D2 | Close session independently; immutably record variance; **approval first**, then asynchronous idempotent difference journal; GL failure cannot undo close | FR-GL-02 |
| D3 | PostgreSQL business POS settings, default 20 hours; configurable shorter only; opening-time duration snapshot; changes audited, permissioned and prospective | FR-CFG-01 |
| D4 | Persist Open/Closed only; Overdue derived at cutoff; blocks sales/new sessions, not closing | FR-CS-04R |
| D5 | New Loyalty starts every membership at **zero opening points**; **do not import** legacy integer balances; preserve historical evidence | FR-LOY-LEG-01 |
| D6 | Product, Variant and Category business-owned, shared among outlets **within the same business**; audit ownership before migration | FR-CAT-01 |
| D7 | Independent approval required by default for POS Drawer/Petty Cash expenses; ordinary users and AccountsAdmin cannot self-approve; Business Owner exception only by explicit setting and full audit | FR-EXP-APP-01 |
| D8 | Super Admin may sell only with explicit business/outlet/terminal and sales.create; owners may choose own-business outlets; others assigned outlets; separate session permissions | FR-AUTH-01 |
| D9 | Business default IANA timezone, optional outlet override; server derives opening business date and snapshots effective timezone/open instant/date/duration; cutoff UTC | FR-TIME-04 |
| D10 | Atomic validated sale/payment/stock/immutable financial intent/outbox; asynchronous idempotent GL journal; reconciliation/alert states; sale not rolled back by downstream GL failure | FR-GL-01 |
| D11 | Outlet Create/Onboarding suggests editable `Counter 1` draft, persists only confirmed rows; no active terminal blocks POS; one auto-selects, multiple explicit; history safeguards | FR-TERM-04 |
| D12 | Dedicated direct-completion Late Sales workflow, historical reporting, independent original/entry dates, original price, current-stock deduction, reconciliations, distinct references and all-consumer integration | §§21–25 |
| D13 | `OutletPrefix + BusinessDate(yyMMdd) + variable daily sequence`, no separators; sequence keyed business+outlet+date, from 1; both POS/Late Sales; immutable, unique, no reuse | FR-INV-01 |
| D14 | For normal POS cash allocations preserve immutable tendered, change given and net cash retained; split payment supported; non-cash evidence; late-sales tender unknown is explicit, never invented | FR-PAY-01 |

**Identity rule:** New D1–D14 labels are distinct from legacy D01–D08 in §2; both sets remain valid, with later explicit decisions taking precedence on conflicts.

### 18.2 Corrected terms and external-module boundary

- Loyalty is an **in-process modular monolith** with MediatR integration and atomic redemption/earning entitlement where mandated; it is not an independent remote Loyalty API. Real OTP redemption remains online-only. Loyalty SRS/LLD own formula, tiers, FEFO, expiry, reservation and fraud control. Eligible earning basis is **net eligible product consideration**, not invoice total and not VAT-inclusive payable.
- Approved D5 is a prospective launch/migration policy: retain legacy historical balances for audit only; disable/retire unsafe legacy point mint/redeem and earning paths during cutover; no automatic retroactive entitlements. Existing real-customer promises or accounting liability concerns must be assessed in the launch plan, not silently erased.
- SQL `date` BusinessDate, actual UTC instant and authorized EffectiveSaleDate have different meanings. Backdating normal POS must never override the active session BusinessDate; special late entries follow §§21–25.
- The Return/Exchange SRD is a separate dependent specification; do not invent return-line and tax reversal semantics inside POS. Any late-sale return must link to the same authoritative sale once Returns/Exchange is implemented.

## 19. Business ownership, terminals, identity and operational access

**FR-CAT-01.** Every business-owned Product, Category, ProductVariant (including variant options/relationships where relevant), SKU/code, product pricing/rules, search result, API query/import/export and inventory association shall enforce server-authoritative BusinessId. Products are shared among authorized outlets **only within the same business**. Outlet stock and effective selling price may vary per outlet. Cross-business lookup, mutation, sale linkage and reporting shall be denied. Before migrating current unowned rows, perform read-only attribution/duplicate diagnostics, approved mapping and safe reversible schema transition. Do not guess ownership. Review globally unique product codes before introducing business-scoped uniqueness.

**FR-TERM-04.** Business onboarding and Outlet Create/Edit reuse one embedded terminal editor. Offer an editable suggested `Counter 1` draft, never silently persist a default: confirmed terminal rows and outlet save atomically. Zero active terminals permit outlet save but prevent session open/checkout; exactly one auto-selected; >1 require explicit terminal choice; no server default fallback. Preserve historical terminal FK associations and prevent move/delete with dependent sales, sessions, print or audit history. Inactive terminal history remains viewable; deactivation prohibited while an open/derived-overdue session exists.

**FR-AUTH-01.** Outlet menu options derive from `/api/me/outlets` or equivalent authorized outlet API. Super Admin must explicitly select a business and an allowed outlet before checkout; selected business is mandatory for GL context. Business Owner can choose any active outlet of their own business. Other users can choose only assigned and permission-eligible outlets; `sales.create`, cash-session open/close, `customers.create`, terminal administration and target permissions are independent. Role-switch context must not expand underlying business scope. CashierUserId is server-derived from authenticated real actor; salesperson is independently selected among active eligible outlet users. Shared cashiers may use one session without claiming its opener's identity. Cart/unsent payment never automatically transfers across outlet/terminal changes.

**FR-CUST-04.** Customer master and protected Walk-in records are business-owned and server-scoped; normalized phone unique by business for registered customers. The existing Walk-in protection trigger must survive a safe per-business key migration. Never expose another business's customer or product through search, held cart or exchange reference.

## 20. Cash session, funding, variance and accounting rules

**FR-CFG-01.** `MaxCashSessionDurationHours` is persisted per business in PostgreSQL, default 20, allowed configured value >0 and ≤20 (smaller values allowed). Restrict setting changes to authorized admins; audit old/new actor and UTC; new setting applies only to subsequently opened sessions. `DurationSnapshotHours` immutable per session. Warnings display at two and one hours before the effective cutoff (thus 18h/19h for default 20h); server/UTC commit gate is authoritative for shorter durations as well.

**FR-TIME-04.** Business holds default IANA TimeZoneId; outlet optionally overrides it. At session open the server resolves zone, opening UTC instant, date in that zone, and duration and stores all as immutable snapshots. `cash_sessions.business_date` uses a SQL date, not timestamptz; checkout sale copies its session BusinessDate. Host OS/browser timezone and Npgsql legacy DateTime.Kind must not affect expiry or BusinessDate. Test DST, midnight, UTC and Asia/Dhaka.

**FR-CS-04R.** Stored status is Open or Closed. Overdue is a read-time operational condition: `nowUtc >= OpenedAtUtc + DurationSnapshotHours` while Open. It blocks new sale and re-opening the same terminal until closed; existing overdue session can be closed with no role bypass. Persisted one-open-session uniqueness applies to all stored Open rows, including derived overdue. Serialize sale/close and enforce cutoff at final commit gate.

**FR-VR-01.** Original-sale void/reversal retains source history. A *physical cash refund* is a distinct immutable cash effect assigned to the currently active outlet/terminal session dispensing the cash, never retroactively to a closed session; once-only source transaction/reversal keys. A noncash refund follows approved return/payment workflow. The same-session and later-session refund must not be subtracted twice when computing expected cash. A cash refund requires a valid open/not-overdue dispensing session and authorization. Separate void and cash-payout events if payout is not immediate.

**FR-GL-02.** Closing records expected, counted, variance, reasons, denominations, handovers and an immutable close snapshot atomically and succeeds even when accounting is unavailable. A nonzero variance creates a pending approval/reconciliation fact, **not** a posted GL journal. Authorized accounting approval triggers idempotent asynchronous CashDifferenceGain/Loss posting. Denial/dispute or posting failure leaves auditable Pending/Rejected/Failed state; do not reopen or rewrite the closed session. Zero variance needs no difference journal. Separation of duties and versioned decision audit required.

**FR-EXP-APP-01.** POS Drawer and Petty Cash expenses default to independent maker-checker approval; creator/submitter may not approve their own expense, even if ordinary or AccountsAdmin permissions include approve. Business Owner self-approval requires explicit business configuration, special authorization and complete evidence/audit; no implicit wildcard exception. Physical cash movement facts/session linkage are captured at disbursement. If approval happens later or a session was closed, never rewrite its snapshot; reconcile with append-only facts and post accounting according to policy. An expense approval is not by itself proof of physical cash paid. Petty Cash and Bank expenses never reduce POS Drawer unless classified as actual drawer disbursement. No standalone Cash In/Out.

**FR-GL-01.** Normal POS checkout atomically persists server-calculated sale, payments, tax/discount/stock facts, immutable journal-intent snapshot, actor audit and durable outbox in one PostgreSQL transaction. GL journal is generated asynchronously, exactly-once in effect using idempotency keys/inbox, with Pending/Posted/Failed and monitored reconciliation. Missing accounts/closed accounting periods must not roll back a committed sale; require explicit exception/authorized period resolution, never silent date substitution. No separate normal sale GL approval. D2 variance journals remain approval-gated.

## 21. Late Sales Module — unified sales, independent workflow (D12)

**FR-LATE-01 — Menu and routes.** Under Sales provide `Late Sales` menu visible only with appropriate permission. Default click opens paginated searchable Late Sales List (`/sales/late-sales`), with authorized filters by original date, entry date, business/outlet, terminal, creator, customer, reference and status. `New Late Sale` (`/sales/late-sales/new`) shown only with `late_sales.create`; View (`/sales/late-sales/:id/view`) is read-only details, evidence and reconciliation status. No POS fullscreen/cashier clock/Hold/Recall UI; reuse compact ERP form/design system. Menu presence is not backend authorization.

**FR-LATE-02 — Creation.** A permitted actor selects original sale date/time (time may be unknown but date mandatory), business-authorized showroom/outlet, historically valid terminal, customer or Walk-in (lookup/create subject to customer permissions), products/variants with quantity, original historical selling price, original discount/VAT and actual original payment method/amount/reference, original manual receipt number, required reason (e.g. outage) and optional evidence attachment. System may find/link original session including a closed one; if none exists, record a clearly visible audited exception. Actual CreatedAtUtc/system entry actor is immutable. Authorized `late_sales.create` user can **direct-complete** without submit/manager approval after all critical validations pass. Do not require today's open POS session merely because historical session closed.

**FR-LATE-03 — Unified Sale.** Late Sale is the SAME business sales domain entity/transaction family as a normal completed Sale, with `EntryType=Late` (or equivalent), not a disconnected ledger/report-only table. Every authoritative sale consumer includes Late Sales **by default**: Sale History; daily/weekly/monthly revenue; sales targets; outlet/terminal/cashier/salesperson/product/category analytics; customer purchase history; inventory movement and COGS; VAT/tax reporting subject to legal tax-point policy; GL/accounting with period control; cash/session reconciliation; receipts; returns/exchanges/void; outbox, analytics projections, Elasticsearch dashboard, and AI forecasting/advisor datasets. Filters may isolate Late Sales; total aggregations may count each unique SaleId **once only**, even when it appears in both Late Sales List and Sales History. No special exclusion in queries, projections, metrics or downstream consumers without explicit separately approved business policy.

**FR-LATE-04 — Historical attribution.** Late Sale original BusinessDate and EffectiveSaleDate are the actual original outlet-local sale date. Normal POS Sale BusinessDate still inherits active session. Historical performance/target date buckets are restated with late facts, and projections/rebuilds handle corrections to earlier dates. Store distinct `OccurredAt` (if known), `EffectiveSaleDate`, original `BusinessDate`, `CreatedAtUtc`, `InvoiceIssuedAtUtc`, `EntryType`, original receipt reference and original session linkage. The entry date is NOT the sale reporting date. Do not rewrite original closed-session financial snapshot or erase previously published reports; expose revisions/freshness/as-of basis.

**FR-LATE-05 — Original price and stock.** Authorized Late Sale uses the original transacted unit price from actual manual evidence, **not** the current product selling price; server validates line totals, quantity, mutually exclusive manual discounts and appropriate historical VAT. This exception does not grant arbitrary manual pricing in normal POS. Deduct quantity from **current available outlet inventory** atomically at entry, enforce never-negative-stock/xmin contention, and write a stock-ledger fact carrying original effective occurrence date and actual recorded UTC timestamp. If an earlier manual inventory adjustment already removed these units, require inventory reconciliation/correction before completion; never blindly deduct twice or assume an unverified skip. Preserve source adjustment links and historical COGS evidence/exception without fabricating valuation.

**FR-LATE-06 — Original session and cash custody.** Link the original Cash Session when identifiable, including Closed. If not found allow a durable authorized exception with reason/evidence. Cash handling classification mandatory for cash: AlreadyIncludedInClosingCount, SeparatelyHeldNotCounted, UnknownNeedsReconciliation. Actual cash custody, not merely sale existence, determines drawer adjustment. Existing close snapshot remains immutable, while append-only LateSaleAdjustment and reconciled view update original session/day totals and exceptions. Card/Mobile has no drawer cash effect and must preserve available provider/settlement evidence. Unknown cash handling must stay visible in Finance exception queue without introducing a manager approval stage for sale completion.

**FR-LATE-07 — Acceptance gates.** (a) No duplicated inventory decrement when manual adjustment already occurred: reconcile before posting; (b) closed accounting period: preserve historic effective date, hold journal in pending/period exception until authorized adjustment/reopening procedure, never silently post into another period; (c) validate original manual receipt/payment and cash handling, attach available evidence, audit exceptions. Missing proof that prevents trustworthy monetary validation shall block direct completion; unknown cash tender details alone are allowed when amount/settlement evidence is sufficient per FR-PAY-02. No negative stock or phantom receipt.

**FR-LATE-08 — Original reference and idempotency.** Require OriginalManualReceiptNumber; normalized unique `(BusinessId, OutletId, NormalizedManualReceiptNumber)`, enforced by PostgreSQL constraint and collision-safe API. Preserve distinct `SaleNumber` and `LateSaleReference`, both generated server-side. A retry with same idempotency key and same payload returns same sale; differing payload returns conflict, never another stock/GL effect. Editing the original manual receipt number on completed sale forbidden except separate auditable correction/void workflow. Original document/photo may be attached as evidence, without exposing cross-business media.

**FR-LATE-09 — Customer and loyalty.** Late Sale is visible in ordinary customer purchases and eligible return workflows, but merely entering a historical sale after programme go-live must **not** mint new loyalty points. D5 zero-opening legacy policy remains; no retroactive point migration/earning. The new loyalty programme's approved exceptions must be resolved in its own SRS/LLD if it ever defines explicitly eligible historical sales.

## 22. Invoice numbering, payments and receipts (D13–D14)

**FR-INV-01.** Authoritative format is `OutletPrefix + BusinessDate(yyMMdd) + DailySequence`, with **no separator** and **variable-length decimal sequence** (first = `1`, example `DHK012611281`). The counter key is `(BusinessId, OutletId, BusinessDate)` and resets to 1 for each business/outlet/day; it does NOT reset per session or terminal. Normal POS and Late Sales draw from same PostgreSQL concurrency-safe allocator. Late Sale uses original historical BusinessDate and next available number for that date—even if entered later. Numbers immutable, never reassigned/reused; void/refund cannot return number to pool. Final SaleNumber unique within business. OutletPrefix must be stable, normalized and never reassigned to another outlet; changing formats/prefixes is versioned prospective configuration with collision checks. Persist structured BusinessId, OutletId, BusinessDate, DailySequence, prefix snapshot, InvoiceIssuedAtUtc; NEVER derive business scope or date by parsing rendered invoice text. Idempotent retries return the original invoice. Existing blank/duplicate seeded sale numbers require safe migration/backfill or partial index and no alteration of valid historical invoices.

**FR-PAY-01.** For each normal cash SalePayment persist immutable `AmountAllocated`, `CashTendered`, `ChangeGiven`, `NetCashRetained`; require tendered ≥ allocated, change = tendered − allocated, retained = allocated, valid decimal rounding, and sum of allocated payments equals server-calculated payable exactly at currency precision. Split cash/card/mobile allocations allowed; change only for cash, and never as additional revenue/drawer cash. Keep method/reference/provider/account, actor/time and verified provider status where available; reference alone does not prove settlement. Return/refund uses separate immutable linked reversal/cash effect; do not mutate original payment evidence. Reject invalid method, negative amounts, forged totals, line+invoice discount stacking and cashier spoofing.

**FR-PAY-02.** Late historical cash allocated amount/retained amount must still reconcile to the original purchase; where exact historical tender/change is unknowable, explicitly store `TenderEvidenceStatus=Unknown` and nullable tender/change, not a fictional `CashTendered=Amount` or fabricated zero change. Separately record D12.2 cash-handling classification. Document what original receipt/financial evidence supports paid amount. Payment allocated and physical drawer custody are independent facts.

**FR-RCPT-03.** The immutable receipt snapshot includes official SaleNumber, original receipt reference and LateSaleReference when Late, actual original BusinessDate, actual system-entry timestamp, cashier/actor, salesperson if selected, source payment evidence (unknown labeled as such), tax and total. Generating a PDF or GET document must not silently register a print attempt: an explicit postback for actual printing/reprinting records the audit. Printing failure never reverses sale.

## 23. Reporting, durable events and universal sales inclusion

**FR-REPORT-04.** All sale-driven read models and APIs consume a single canonical sale fact stream and include `EntryType=Late` as Completed business sales by default. Where reports group by date, the business metric's chosen `BusinessDate` is the original historical date for Late Sales. Daily target variance, historical outlet-day metrics, product/category performance, customer spend, cashier/salesperson performance, returns/void corrections, cash/noncash composition, VAT, COGS and AI/forecasting sources must all reconcile to immutable authoritative sales. The existence of the Late Sales management menu cannot justify excluding its sales from an ordinary Sales Report.

**FR-REPORT-05.** Publish SaleCompleted with explicit entry type, original BusinessDate, invoice, financial/tax snapshots and stable idempotent event ID from same transaction as Sale; on late events update/rebuild older date buckets without double counts. Consumers must be idempotent and have versioned correction/reversal events. Rebuild from source must equal incremental projection results. Dashboard freshness/as-of and late-adjustment indicators must be available; analytics may use Elasticsearch as configured, but never derive close-time drawer totals from eventually consistent projections.

**FR-REPORT-06.** Historical reports may be restated by late entries while frozen Cash Session closing snapshots remain unchanged. Expose both immutable originally-closed amounts and append-only reconciled/current-as-of totals, with delta/source and audit timestamp. Reporting consumers cannot write into a closed session.

## 24. Security, duplicate suppression, accounting and implementation acceptance

**FR-SEC-01.** `late_sales.view` allows the authorized list/detail only; `late_sales.create` allows direct completion only after all server checks; access to reconciliation/evidence and any correction actions requires separate granular permission. A user with `sales.create` alone does not gain Late Sales creation. Late Sale does not bypass Customer, Outlet, Terminal or pricing evidence validation. All queries filter effective BusinessId/outlet scope, protect customer PII and validate source sales for replacements/exchanges. Held Sale cashier identity is derived from current principal; reads of other cashiers' held carts require specific approved manager access.

**FR-SEC-02.** No legacy loyalty `add-points` or `redeem-points` method may remain callable with merely `customers.view`; phase out unsafe endpoints with an explicit feature/cutover gate. New Loyalty points begin at zero under D5 and are minted only through authoritative module contracts for eligible new transactions.

**FR-SEC-03.** Require stable per-cart and per-late-sale idempotency key, hashed canonical request payload for replay matching, scope `(BusinessId, OutletId, Key)`, and consistent 409 for key-with-different-payload. Invoice number allocation, price recheck, session gate, cashier derivation, inventory and cash amount validation happen server-side. All replays and financially material exceptions are audited.

## 25. Acceptance tests — v1.2 additions

| ID | Required outcome |
|---|---|
| AC-19 | Business A cannot search/use B's product, category, variant, customer, held sale or replacement reference; shared product is accessible across A's own outlets only. |
| AC-20 | Terminal suggestion is never auto-persisted; one active auto-selects, many require explicit selection; no active terminal blocks; history/delete/session guards hold. |
| AC-21 | Non-UTC and DST time zones yield correct outlet-local BusinessDate and strict configured cutoff; shorter D3 snapshot unaffected by later settings change; status Overdue derived. |
| AC-22 | Two cashiers/sales concurrently with close/double close cannot attach a sale to already-closed session or lose payment effects; opening cutoff strictly enforced. |
| AC-23 | Void in older session and cash payout from current session affect only paying drawer once, original close snapshot immutable. |
| AC-24 | A variance session closes without live GL success; variance requires approval; failed posting retries idempotently; zero variance no adjustment journal. |
| AC-25 | Cash drawer/Petty Cash creator cannot self-approve absent explicit authorized audited Business Owner exception; later approval cannot rewrite historical closed count. |
| AC-26 | Super Admin sale requires business/outlet/terminal context + sales.create; owner outlets selectable; regular users only assigned; cashier claim-derived. |
| AC-27 | Sale payment/stock/financial intent/outbox commit atomically; GL posting failure leaves completed sale + monitored exception, not duplicate sale/journal. |
| AC-28 | Late Sales list/view/create permissions isolated; authorized direct completion works without POS-style UI or Manager approval. |
| AC-29 | Original receipt unique per business/outlet; collisions and payload-mismatched retries reject; same-request retry returns original sale/invoice. |
| AC-30 | Late Sale entered two days later appears in **every** applicable normal Sales Report, outlet target, customer history, inventory/COGS, payment, VAT/accounting, dashboards/AI and authorized return/void feeds **once**, attributed to original BusinessDate. |
| AC-31 | Closed-session late cash sale with AlreadyCounted / SeparatelyHeld / Unknown handling preserves original close snapshot and creates correct append-only reconciled view; no phantom drawer receipt. |
| AC-32 | Late Sale original unit price, VAT/discount evidence, current-stock deduction, historical event time, no-negative-stock and previous-manual-adjustment guard enforced. |
| AC-33 | Invoice `DHK012611281` format works, daily business/outlet reset, same day multi-terminal concurrency, and Late Sale next historical date sequence; original invoices immutable. |
| AC-34 | Normal cash tendered/change/net retained stored for single and split tender; drawer adds only retained; Late historical unknown fields explicit; card/mobile references persisted when applicable. |
| AC-35 | Production migrations are traceable in Git; PG Test-stack migrations-built schema passes race, timestamp, uniqueness, outbox and Walk-in trigger tests without contacting Development database. |

## 26. Change control, deliverables and remaining evidence gates

- All D1–D14 and D12 subdecisions including D12.5 universal reporting integration are **approved and frozen for drafting**. Do not reinterpret a frozen choice as a new unresolved business decision. This file is a revised **documentation artifact**, not a software change or a declaration that implementation is complete.
- Rev 3 findings to resolve in code after separate implementation authorization: server-trusted price/cashier/tax/discount, customer tenant scope, terminal provisioning, 15h legacy default, cash close/sale race, double subtraction of refunds, invoice allocation, tender lost on single-method payment, outbox gap, historic date/timestamp mismatch, stale permission aliases, legacy loyalty endpoints and other §B/§G findings.
- Existing approved UX, sales targets, VAT/tender/receipt, hold/recall, cashier/salesperson and session views remain required; any future UI refit must follow repository design-system tokens and accessibility.
- PostgreSQL-backed integration tests not run in Rev 3. Run only against isolated Test stack with `RETAILPOS_TEST_POSTGRES` explicitly set; remove unsafe Development fallback before testing. Use migration-built schema rather than EnsureCreated-only fixtures. Migration application to persistent Development/Production requires separate explicit approval.
- Returns/Exchange SRD, Loyalty P0 gates, production data quality, legal tax-point/closed-period interpretation and measured NFRs remain implementation/test dependencies, not authorization for silent policy changes. Do not claim final implementation readiness solely on document freeze.
