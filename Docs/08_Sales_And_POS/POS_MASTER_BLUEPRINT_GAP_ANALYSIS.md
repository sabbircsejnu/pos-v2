# IraniMart POS v2 — Master Blueprint Gap Analysis

| Property | Value |
|---|---|
| Document ID | IM-POS-GAP-001 |
| Date | 2026-11-02 |
| Nature | **Read-only** repository gap analysis. No application code, migrations, data, configuration, SRS or LLD were changed. |
| Blueprint reviewed | `IRANIMART_POS_MASTER_SRS_V1_1.md`, `IRANIMART_POS_MASTER_LLD_V1_1.md` (primary POS authority) |
| Loyalty authority | `Docs/Customer_Loyalty_Module/CUSTOMER_LOYALTY_MODULE_SRS.md` (CL-SRS-001), `…_LLD.md` |
| Historical context | `PHASE_01_IMPLEMENTATION.md`, `PHASE_01_IMPLEMENTATION_UPDATED.md`, `POS_PHASE_01_UPDATE_ADDENDUM.md`, `iranimart_pos_prototype.html` |
| Status | For user review. **Implementation is not authorized.** |
| Revision | **Rev 3 (2026-11-02)** — focused verification pass added (§O); Rev 2 = consolidated with an independent second-pass review by Claude Sonnet (see §0.1 and §N). Tags: **[CS+]** (Rev 2 addition/enhancement), **[CS-fix]** (Rev 2 correction), **[V3]** (Rev 3 verification result). |

---

## 0. Method, evidence basis and limits

**Method.** Static code tracing of actual execution flows (Angular → controller → MediatR handler → store/adapter → EF model → PostgreSQL constraint), not class-name matching. Each status below is based on reading the cited file. Nothing was executed: no build, no test run, no database queried, no browser session. Visual comparison with the prototype is therefore from template/CSS/TypeScript source versus the prototype HTML source only.

**Evidence labels used in this report**

| Label | Meaning |
|---|---|
| **Verified** | The behavior was read directly in the cited file/method. |
| **Assumption** | Reasoned inference not directly confirmed in code. Marked explicitly. |
| **Not verified** | Area not inspected deeply enough to classify; listed in §J. |

**Path aliases** (all relative to the repository root)

| Alias | Path |
|---|---|
| `API/` | `src/RetailPOS.API/` |
| `APP/` | `src/RetailPOS.Application/` |
| `DOM/` | `src/RetailPOS.Domain/Entities/` |
| `INF/` | `src/RetailPOS.Infrastructure/` |
| `FE/` | `retailpos-frontend/src/app/` |
| `TST/` | `tests/` |

### 0.1 Claude Sonnet independent review — provenance and independence caveat

The first revision of this report was written earlier in the same working session. The second-pass reviewer therefore **could not be fully independent**: it had the first pass's conclusions in context. To reduce anchoring, the second pass (a) re-read the four authoritative documents' requirement text, (b) deliberately targeted areas the first pass had marked unverified or had not opened (product/catalog tenancy, seeding and onboarding paths, ledger/transaction nesting, column types, test assertions, held sales, receipt side effects, payment/tender persistence, expense approval), (c) re-verified several first-pass claims against code, and (d) only then compared with and edited the existing text. Findings below are tagged so a reader can see exactly what changed. Where the second pass could not confirm something it is marked **Verification Required** rather than asserted.

**[V3] / Rev 3:** a further focused verification pass (§O) resolved the Verification Required rows using read-only inspection of the Development database, the unit-test suite, model-drift checking and Npgsql documentation; it did not run PostgreSQL-backed integration tests (§O.2).

**Priority scale.** **P0** = blocks safe production use (financial integrity, security, tenant isolation, data loss). **P1** = required core blueprint capability. **P2** = important, not blocking. **P3** = polish/consistency.

**Not examined deeply (see §J):** pricing-rule internals, `UserOutletAccess` internals, product search/tenancy of products, receipt PDF rendering internals, Audit v2 internals, live DB state/data quality, existing Elasticsearch usage beyond configuration.

---

## A. Executive Summary

### A.1 Overall readiness

The repository contains a **working, tested core checkout** (sale create, split payment rows, stock deduction with optimistic concurrency, journal posting, void, whole-sale refund, receipt document/PDF, held-cart parking, a basic cash-session open/close with a 15-hour guard). It does **not** yet contain most of what the Master Blueprint adds: outlet-embedded terminals, sales targets, salesperson attribution, session landing/history/detail pages, handover/attachments/denominations, Expense-to-drawer integration, 20-hour/business-date correctness, customer tenancy and phone normalization, Loyalty integration, outbox-driven reporting projections, and the v3 POS layout.

The 74 matrix rows (65 in §B.1–B.13 plus 9 added in §B.15 by the Claude Sonnet review) classify, after the Rev 3 verification pass, as **3 Implemented (4%), 31 Partially Implemented (42%), 17 Missing (23%), 21 Conflicting (28%), 2 Verification Required (3%)** (roll-up at the end of §B.15; Rev 2 was 3/29/17/20/5). Practical readiness against the Master Blueprint remains **20–25%** (unchanged by Rev 3; confidence in the estimate is higher because the Verification Required items were resolved with evidence). Fulfilled requirements are concentrated in the checkout/stock/accounting core the blueprint tells us to preserve; the lower bound reflects the tender/change loss on the default checkout, the missing production terminal provisioning, and the close-time coupling to accounting configuration. The 21 *Conflicting* rows matter most: they are places where existing behavior contradicts an approved rule or is unsafe, not merely incomplete.

### A.2 Top architectural risks (detail in §G)

| # | Risk | Severity |
|---|---|---|
| 1 | **Checkout trusts the client** for unit price, line/invoice discount, VAT total and `CashierId` (`CreateSaleDto`, `CreateSaleCommand`). No server re-pricing, no discount exclusivity check, cashier identity is spoofable. | **P0** |
| 2 | **Customers are not tenant-scoped** (no `BusinessId`, global search, global exact-string phone check, one global Walk-in). Legacy `add-points`/`redeem-points` endpoints are open to anyone with `customers.view` and bypass any OTP. | **P0** |
| 3 | **Cash-session close is not serialized with checkout** (summary computed outside the transaction, ReadCommitted, no row lock; sale validator is also unlocked). A sale can attach to a session after the expected cash was computed. | **P0** |
| 4 | **Expected-cash formula double-subtracts voided/refunded cash sales** and ignores expenses; it still reads legacy `cash_in/cash_out` transaction types the blueprint removes (D01). | **P0** |
| 5 | **Idempotency is ineffective end to end**: the frontend generates a new UUID on every click; a server-side race loser surfaces `DbUpdateException` instead of the original result; the key is optional. | **P1** |
| 6 | **20-hour rule is 15 hours** everywhere (default, fallback, error message, UI message; tests mock the value) and is stored in a single global JSON file, not per business. | **P1** |
| 7 | **No durable events for POS**: sale/session events go through `LoggingSaleEventPublisher` as fire-and-forget `_ =` calls. A mature outbox/inbox/RabbitMQ stack exists but is not used by sales or cash sessions. Reporting projections and loyalty entitlement cannot be built safely on the current publishers. | **P1** |
| 8 | **Invoice numbers come from a JSON file** (`JsonSystemSettingsStore.AllocateNextInvoiceNumberAsync`), non-transactional, single-instance, and `sales.sale_number` has **no unique index**. | **P1** |
| 9 | **Per-line VAT snapshot is mathematically inconsistent when an invoice discount exists** (line VAT sums to more than `Sale.Tax`), and the VAT report reads those snapshots. | **P1** |
| 10 | **Legacy loyalty is a plain integer** (`Customer.LoyaltyPoints`, 1 pt per ৳1 of VAT-inclusive total) contradicting the authoritative Loyalty SRS (net-of-VAT/discount, ৳100/pt, immutable ledger, OTP). It must be retired through the Loyalty module, not extended inside POS. | **P1** |
| 11 **[CS+]** | **Production businesses cannot create POS terminals**: the only terminal writers are the opt-in dev seeder; onboarding never creates one and no Outlet UI/API does. POS cannot sell for any newly onboarded business. | **P0** (go-live blocker) |
| 12 **[CS+]** | **Default checkout loses cash tendered/change** (single-method path always stores `Tendered = null`; FE sends no payments list), so receipts show change 0 and "cash received" is not auditable. | **P1** |
| 13 **[CS+]** | **Checkout and session close are hard-coupled to accounting state**: the sale journal and the variance journal join the same transaction and throw on missing posting accounts/closed periods, so a sale or a session close with variance can fail for accounting reasons. | **P1** |
| 14 **[CS+]** | **Exchange link and held-sale ownership are not authorized/derived server-side** (`OriginalSaleId` mutates any sale; held-sale `CashierId` is client-supplied). | **P1/P2** |
| 15 **[CS+]** | **`cash_sessions.business_date` is `timestamptz` (not `date`) and the Npgsql legacy-timestamp switch is on**; POS cash-session code compares Local-kind `OpenedAt` with `DateTime.UtcNow`. **[V3]** Mechanism verified (Npgsql docs + repo workarounds elsewhere); the 20h cutoff shifts by the API host's UTC offset when the host OS zone ≠ UTC. Not executed; deployment zone unknown. | **P0 (conditional)** |

### A.3 Prioritized critical gaps (summary)

1. **P0 hardening of existing checkout** (items 1, 3, 4 above plus secure legacy loyalty endpoints and customer tenancy) — these are defects in code the blueprint says to *preserve and harden*.
2. **Foundational schema/config**: business-level 20h setting, outlet timezone for `BusinessDate`, terminal model, customer normalized phone/`BusinessId`, `SalespersonUserId`, handover/attachment/target tables.
3. **Cash-session lifecycle redesign** (server-derived business date, row-locked gate, permissions, history/detail, close snapshot).
4. **Expense integration** (Payment Source + session linkage) — currently no field exists.
5. **Durable eventing** reuse of existing outbox before any projection/loyalty work.
6. **UX conformance** (landing/open/close pages, sticky action model, fullscreen, salesperson, customer lookup/link).
7. **Loyalty integration through ports** into the Loyalty module (not built here).
8. **[CS+] Prerequisite fixes added by the second review:** production terminal provisioning (risk 11), tender/change persistence (12), exchange-link authorization and held-sale actor derivation (14), `business_date` type/Kind runtime verification (15), and decoupling/decision on accounting coupling at checkout and close (13). The full dependency-ordered backlog is in §I and §N.5.

### A.4 Items needing user decisions before implementation
See §H (SRS/LLD corrections) and §K (unresolved technical questions). The most consequential: how voids/refunds affect drawer cash across sessions, whether the existing automatic cash-difference journal at close is permitted under FR-CLOSE-03, where the 20-hour setting lives, and how legacy `LoyaltyPoints` balances are treated at Loyalty go-live.

---

## B. Requirement Traceability Matrix

Columns: **ID** (SRS unless noted) · **Feature** · **Status** · **Existing paths** · **Evidence (Verified unless marked)** · **Missing / Conflicting** · **Recommended action** · **Priority**.

Status keys: **Impl** = Implemented, **Part** = Partially Implemented, **Miss** = Missing, **Conf** = Conflicting, **VR** = Verification Required.

### B.1 Terminals and Outlet setup (SRS §5.1, D04; LLD §3.3, §4)

| ID | Feature | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| FR-TERM-01 / D04 | POS Terminals section inside Outlet Create/Edit (Name, Code, Description, Active; add/edit/delete/deactivate) | **Miss** | `FE/components/outlets/outlet-form.component.{ts,html}`, `FE/models/outlet.model.ts`, `API/Controllers/OutletsController.cs`, `API/DTOs/Outlet/OutletDtos.cs`, `APP/Outlets/Commands/OutletCommands.cs`, `INF/Outlets/OutletWriteStore.cs` | `CreateOutletDto`/`UpdateOutletDto` carry only Name, Address, ContactNumber, ManagerId. Text search for "terminal"/"target" in `FE/components/outlets`, `outlet.model.ts`, `outlet.service.ts` returns no matches. | No terminal write path exists anywhere in `src/`. Terminals are created only by `INF/Data/DbSeeder.cs` (per-outlet "Counter 1", code `OUTLET-nn`, ~L73–94, ~L806–814) and `database/seed-data.sql:218`. **[CS+]** Seeding is opt-in (`Database:SeedOnStartup`, `API/Program.cs` ~L1165–1201, rejected outside development) and the business-onboarding path (`INF/BusinessAdministration/BusinessOnboardingWriteStore.cs`, `APP/Outlets/Commands/BusinessSetupOutletCommands.cs`) never creates a `PosTerminal` (the only `new PosTerminal` sites are `DbSeeder.cs`). Consequence: **a newly onboarded production business has no terminals and no way to create one, so POS selling is impossible for it today** — a go-live blocker, not just a missing screen. | Extend existing Outlet command/store/form with terminal child collection; reuse `IOutletWriteStore`; no standalone terminal module. | P1 |
| FR-TERM-01A | Draft terminal rows on Create Outlet, saved atomically; edit shows only the outlet's terminals incl. inactive | **Miss** | as above | `OutletWriteStore.CreateAsync` saves the Outlet only (via `OperationalLocationSourcePersistence.SaveCreatedAsync`, which already appends a source event + outbox row in one transaction). | No child persistence; no concurrency token on terminals (Outlet has `SourceVersion` concurrency token, terminal has none). | Add child rows inside the same transaction used by `SaveCreatedAsync`/`SaveUpdatedAsync`. | P1 |
| FR-TERM-01B | Active terminals authoritative for Open Session and POS; 1 → auto-select, many → explicit choice, 0 → setup-required | **Part** | `API/Controllers/PosController.cs` (`GetTerminals`), `APP/Pos/Queries/GetPosTerminalsQuery.cs`, `INF/Pos/PosTerminalReadStore.cs`, `FE/pages/pos/pos-screen.component.ts` (`loadTerminals`, `onTerminalChange`) | Read store returns `OutletId == x && IsActive` ordered default-first. FE auto-selects `isDefault ?? first` even with several terminals; HTML shows a select with literal "No terminals" option when empty (L24–40 of `.html`). Server `SaleCreationStore.ResolveActiveTerminalIdAsync` silently falls back to default/first active terminal when `TerminalId` is null. | Silent default selection and silent server fallback contradict "explicit valid selection" and LLD "never silently switch a cart to another terminal". `IsDefault` is not in the blueprint. No "setup required" state with action. | Decide fate of `IsDefault` (see §K-5); require explicit `TerminalId` server-side; add setup-required state. | P1 |
| FR-TERM-02 | Outlet saved without terminal; POS cannot start without active terminal | **Part** | `INF/Sales/SaleCreationStore.cs`, `APP/Sales/Commands/CreateSaleCommand.cs` | Sale create throws "No active terminal is configured…" when none; outlets can be created without terminals today (nothing creates them). | Open-session path only checks terminal ownership, not a UX setup state. | Covered by FR-TERM-01B work. | P2 |
| FR-TERM-03 | Hard delete only when unused; deactivate blocked by open/overdue session; no outlet move; server-side uniqueness & concurrency | **Miss** | `INF/Data/RetailPOSDbContext.cs` (PosTerminal L405–420, CashSession L960–1004, Sale L1006–1053) | DB has unique `(OutletId, Code)` (case-sensitive, not normalized). `PosTerminal → Outlet` is **Cascade**; `Sale.TerminalId → PosTerminal` is **SetNull**; `CashSession → Terminal` is Restrict. No write path to enforce guards. | Deleting an outlet cascades terminals; a terminal delete with only sales/receipts nulls `Sale.TerminalId` (history loss) — only cash sessions are protected by Restrict. `Description`, `BusinessId`, normalized code, row version absent. | Add guards in the terminal save command; change `Sale.TerminalId` delete behavior to Restrict as part of the (approved) migration. | P1 |

### B.2 Sales targets (SRS §5.2, D05; LLD §3.1, §18.4)

| ID | Feature | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| FR-TARGET-01 | Seven weekday daily targets, nonnegative; unset = "Not configured" | **Miss** | none | Search for `SalesTarget`/`OutletTarget` across `src/`, `FE/`, `database/` returns no entities, tables, endpoints or UI. | Entire capability. | New outlet-scoped target aggregate inside the Outlet feature. | P1 |
| FR-TARGET-02 | Auto weekly/half-month(1–15, 16–end)/monthly sums from real calendar | **Miss** | none | — | Calendar calculator absent. | Pure domain calculator with leap-year tests. | P1 |
| FR-TARGET-03 | Independent overrides, Reset to Auto, effective-dated history, actor audit | **Miss** | none | — | — | As LLD §3.1 `OutletSalesTargetVersion` + overrides; reuse Audit v2. | P1 |
| FR-TARGET-04 | Landing cards: Daily Target, all-terminal actual for BusinessDate, signed variance, icons, freshness | **Miss** | `INF/SalesReports/SalesReportReadStore.cs`, `APP/Sales/Queries/GetSaleTodaySummaryQuery.cs` | Existing sales reports aggregate `Sale.TotalAmount` (VAT-**inclusive** payable) filtered by `SaleDate`, not `BusinessDate`, directly on transactional tables. | Metric definition conflicts with SRS suggestion (net ex-VAT after discounts and voids); no projection/freshness. | Define a versioned metric (see §H-12) and compute from authoritative tables until projections exist. | P1 |

### B.3 Terminal resolution, cash-session landing, open (SRS §6; LLD §5.1, §7, §8)

| ID | Feature | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| FR-CS-01 | Resolve outlet+terminal; valid open session → POS; none → landing; one unclosed session/terminal | **Part** | `API/Controllers/PosController.cs` (`GetActiveCashSession`), `APP/Pos/Queries/CashSessionQueries.cs`, `INF/Pos/CashSessionReadStore.cs`, `INF/Pos/CashSessionOpenStore.cs`, `FE/pages/pos/pos-screen.component.ts` (`loadActiveCashSession`) | `GET /api/pos/cash-sessions/active?outletId&terminalId`. `ux_cash_sessions_one_open_per_terminal` = unique `(outlet_id, terminal_id) WHERE status='open'` (`RetailPOSDbContext.cs` L971–975); race on open handled (`CashSessionOpenStore.IsOpenSessionUniqueViolation`); PG concurrency test exists (`TST/RetailPOS.IntegrationTests/Sales/SaleCreationPostgresConcurrencyTests.cs` `ConcurrentCashSessionOpen_…`). | No landing route: with no session the POS page itself pops an *Open Cash Session modal*. Outlet = the user's single `outletId`; terminal chosen client-side. Index covers `open` only (see FR-CS-04). | Add `Cash Sessions` landing route and entry resolution; keep the unique index approach. | P1 |
| FR-CS-02 | Landing: context card, target cards, history with filters (Business Date, Terminal, Status, Opened By), paging, View | **Miss** | `FE/services/pos.service.ts` (only active/open/summary/close) | No list/get-by-id endpoint, no route in `FE/app.routes.ts` (only `path: 'pos'`, L416), no page. | Entire page and `GET /cash-sessions` query. | New query + page; breadcrumbs Sales › POS › Cash Sessions. | P1 |
| FR-CS-03 | Open page: Outlet RO, Terminal selector, **server-derived** BusinessDate and OpenedAt, Opening Cash ≥ 0, Notes, right summary, sticky footer | **Conf** | `APP/Pos/Commands/OpenCashSessionCommand.cs`, `API/DTOs/Pos/CashSessionDtos.cs`, `FE/pages/pos/pos-screen.component.{ts,html}` (modal L526–580) | `BusinessDate = (request.BusinessDate ?? now).Date` where `BusinessDate` is **client-supplied** (`OpenCashSessionDto.BusinessDate`; FE sends `formatDateForInput(now())` = browser-local date). Otherwise UTC date, not outlet timezone. Modal, not a page; no opening summary panel. | Violates "no client backdating" and "outlet local timezone". `OpenedAt = DateTime.UtcNow` (server) is correct. Outlet has no timezone (`DOM/Outlet.cs`); only a global `Company.TimeZone = "Asia/Dhaka"` in `APP/Settings/SystemSettings.cs` L18. | Ignore client date; derive from server UTC + outlet/business timezone; build Open page per prototype. | P0 |
| FR-CS-04 | States Open→Closed, Open→Overdue→Closed; opener not exclusive; actor on every mutation | **Part** | `DOM/CashSession.cs`, `INF/Pos/SaleCashSessionValidator.cs`, `INF/Pos/CashSessionOpenAuditWriter.cs`, `INF/Pos/CashSessionCloseAuditWriter.cs` | `Status` is a string `open`/`closed`; **Overdue is derived** (`now >= OpenedAt + hours`) and never persisted; Overdue event is published only when a sale is attempted. Opener is not exclusive (validator ignores user). Open/close actor audited. Close accepts any session in status `open` (so derived-overdue is closable — correct). | Unique index and queries ignore any persisted `overdue` value (none exists). No `RowVersion`. Audit/events written **after** commit (see G-7). | Decide persisted vs derived Overdue (§K-4) and align index. | P1 |
| FR-CS-05 | Session detail: status, overview, source breakdown, expected/actual/variance, handover, timeline, related sales, print/PDF, back; read-only archived data | **Miss** | `API/Controllers/PosController.cs` (`GetCashSessionSummary`) | `GET …/cash-sessions/{id}/summary` is a **live close-preview** recalculated at `DateTime.UtcNow`, not an archived snapshot; no detail endpoint, timeline, related-sales link, PDF. Frontend only uses it inside the close modal. | Everything except the close-time numbers. | Build `GetCashSessionDetails` from persisted close snapshot. | P1 |

### B.4 20-hour cutoff and business dates (SRS §7; LLD §5.2–5.3)

| ID | Feature | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| FR-TIME-01 / D03 | 20h default, per-business config, strict `<` boundary for all roles | **Conf** | `APP/Settings/SystemSettings.cs` L74 (`MaxCashSessionDurationHours = 15`), `API/system-settings.json:57` (15), `APP/Pos/Commands/OpenCashSessionCommand.cs`, `CloseCashSessionCommand.cs`, `APP/Pos/Queries/CashSessionQueries.cs`, `INF/Pos/SaleCashSessionValidator.cs` (fallback 15 and error text "more than 15 hours"), `INF/Settings/JsonSystemSettingsStore.cs` | Boundary logic `now >= OpenedAt.AddHours(max)` is correct and role-agnostic. Default/fallback/message all **15**. Setting stored in one JSON file for the whole deployment. **[CS-fix]** Tests do not "assert 15h as a rule": they inject `15` (and in one case `8`) through a mocked `ICashSessionReadSettings` (`CashSessionServiceTests.CreateService(maxDurationHours: 15)`, `CloseCashSessionCommandHandlerTests` L54–67/L216–227); only fallback/error-text assertions (`maxDurationHours` metadata, `Assert.Equal(15, result.MaxDurationHours)` where the mock returns null/15) will need updating when the default changes. | Value conflicts with approved D03; not per business. | Move to business-level setting (default 20); update messages and the few default-asserting tests with approval. | P1 |
| FR-TIME-02 | Warn at 18h/19h; block ≥20h; preserve cart; Close old → Open new → **revalidate cart** → resume | **Part** | `FE/pages/pos/pos-screen.component.ts` (`sessionWarningMessage`, `sessionIsOverdue`, `completeSale` L1441–1444) | Warnings are "1 hour" and "30 min" before expiry (i.e. 14h/14.5h for 15h limit; would be 19h/19.5h for 20h) — not 18h/19h. Elapsed time uses the **client clock** (`now()` updated by `setInterval` every 1s, L401) rather than a server offset. Cart signals are retained across close/reopen (not cleared), but **no revalidation** of price/stock/VAT/discount/customer/payment after reopening. Server blocks at the gate (`SaleCashSessionValidator`). | Message hard-codes "15 hours"; no revalidation step; no server-time offset. | Align thresholds, add server-time offset, add revalidation call on resume. | P1 |
| FR-TIME-02 (commit gate) | Final recheck at commit so a sale that started before cutoff cannot commit at/after it | **Conf** | `APP/Sales/Commands/CreateSaleCommand.cs` (L96–140, L240–260), `INF/Pos/SaleCashSessionValidator.cs` | The age check is executed **mid-transaction**, after the inventory decrement and before journal posting/commit; the session row is read with `FirstOrDefaultAsync` (no lock). `now` is captured at validator entry; the commit that follows can occur after the boundary. | No final-gate recheck, no session row lock (LLD §5.2 requires lock + recheck). **[V3 verified against the live schema]** There is no database-level backstop either: the Development database has **no trigger and no check constraint** on `sales` or `cash_sessions`, and `sales.cash_session_id` is a plain FK (`ON DELETE SET NULL`). PostgreSQL's FK insert takes only `FOR KEY SHARE` on the parent row, which does **not** conflict with the close's `UPDATE … status` (`FOR NO KEY UPDATE`), so a sale insert and a session close do not block each other. Interleaving (sale validated while open → close computes summary without the uncommitted sale and commits → sale commits into a closed session) is therefore possible. See §O.9. | Add locked session read + recheck as the last statement before commit; optionally a DB guard (trigger or `FOR SHARE` on the session row from the sale path). | P0 |
| FR-TIME-03 | `BusinessDate` = opening local date, immutable; distinct `CreatedAtUtc`, effective `SaleDateTime`, backdate only with `sales.backdate`, never future | **Part** | `APP/Sales/Commands/CreateSaleCommand.cs`, `API/Controllers/SalesController.cs` (`Create` L310–370), `DOM/Sale.cs` | `Sale.BusinessDate = session.BusinessDate.Date` (inherited, correct). Backdate gated by `User.HasPermission("sales.backdate")` and future dates rejected server-side (date-level compare). | **Field semantics are inverted vs the SRS**: repo `Sale.SaleDate` = requested/effective date; `Sale.SaleDateTime` = actual `now`; `Sale.CreatedAt` = now. Posting dates are inconsistent: sale journal posts at `SaleDateTime ?? SaleDate` (= actual now), stock ledger at `saleDate` (= backdated), reports/VAT use `SaleDate`. Invoice number date uses `saleDate`. Backdate audit written after commit and swallowed on error. | Document the mapping (§H-5); decide posting-date policy per SRS ("must be reviewed"). | P1 |

### B.5 POS screen and checkout workflow (SRS §8, §16.2; LLD §6, §7, §10)

| ID | Feature | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| FR-POS-01 / D06 | Compact context: Cash opening, live Open for, cashier, salesperson, close icon, fullscreen; Outlet/Terminal/Sale No row; **no** Business Date, big clock, item/qty counts | **Conf** | `FE/pages/pos/pos-screen.component.html` L1–76, `.ts` | Header renders "Business Date", a `HH:mm:ss` digital clock, "Item Count", "Quantity Count", text "Close Cash Session" button; no salesperson, no icon/tooltip close, no fullscreen. Sale-Date input only for `canBackdateSale()` (acceptable). | Contradicts D06 explicitly. | Refit header per prototype; keep backdate input per §H. | P2 |
| FR-POS-01 (fullscreen) | Fullscreen hides ERP shell, preserves state, Esc exits | **Miss** | — | No `requestFullscreen`/fullscreen handling in `pos-screen.component.*`. | — | Add Fullscreen toggle + state preservation (state is already component-scoped signals). | P2 |
| FR-POS-02 | One input for barcode/SKU/name/code/variant; browse icon **inside** input; results show variant attrs, SKU, barcode, price, **outlet stock**; repeat scan increments | **Part** | `FE/pages/pos/pos-screen.component.{ts,html}`, `API/Controllers/PosController.cs` (`Lookup`), `INF/Pos/PosProductLookupStore.cs` | Backend lookup is an exact barcode/SKU/variantId single-result endpoint (Redis cached). Name/code search is **client-side over the first 200 products** (`pageSize` 200 in `loadProducts`), so catalogs >200 variants are partially unsearchable. "Browse" is a separate `btn-ghost` button (L171), not an in-input icon. Stock is shown from outlet stock (`getOutletStockQty`); stock-hint is a documented display hint only. | Search completeness/tenancy risk; browse control placement; variant attribute display only partially mapped. | Server-side paged search endpoint scoped to outlet; move Browse into input. | P1 |
| FR-POS-03 | Cart columns (image/name/attrs, unit price, qty, discount %, VAT, line total, remove), direct qty typing + ±, UOM rules, server stock gate | **Part** | `FE/pages/pos/pos-screen.component.html` L180–280, `DOM/SaleItem.cs`, `DOM/Product.cs`, `DOM/Inventory.cs` | Cart table with editable qty/discount exists. `SaleItem.Quantity` and `Inventory.Quantity` are `int`. **[CS-fix]** `Product`/`ProductVariant` have **no unit-of-measure field at all** (verified entity), so "product UOM rules" is Missing, not "Verification Required". Stock gate is server-side (see FR-SALE-02). Tax column derived from one global rate. | UOM/fractional quantities unsupported by the data model. | Approve whether UOM is in scope; if not, amend SRS FR-POS-03 to "integer quantities". | P2 |
| FR-POS-04 | Customer & Loyalty block: seeded immutable Walk-in, Lookup, Quick Add, selected account, Link Loyalty | **Part** | `FE/pages/pos/pos-screen.component.{ts,html}`, `FE/services/customer.service.ts`, `INF/Sales/SaleCreationStore.cs` | Search input + Quick Add button exist; selected customer chip. Walk-in on the client is a synthetic object `id: 0` (L373); the server finds `CustomerCode == "WALKIN"` **globally** and auto-creates/repairs it (`CreateSaleCommand.EnsureWalkInCustomerAsync`). No "Link Loyalty", no loyalty card. | Walk-in not per business and not seeded per tenant; no loyalty linkage. | Per-business protected Walk-in; Link Loyalty after Loyalty module ports exist. | P1 |
| FR-POS-05 | Sticky **left** footer: Hold, Recall, Link Loyalty, Clear Cart; sticky **right** footer: **only** Complete Sale (F9) | **Conf** | `FE/pages/pos/pos-screen.component.{html,css}` | Right `.checkout-footer` (`position: sticky; bottom: 0`, CSS L520) contains **Clear, Hold, Complete Sale**. Hold/Recall buttons are in the customer row. `.payment-lane` is a bounded flex column with `.payment-scroll` — structurally close to the LLD pattern. | Right footer has duplicate Clear/Hold; no left sticky footer; no Link Loyalty. 1366×768 usability not measured. | Re-split footers per LLD §6; verify at 1366×768 with a screenshot test. | P2 |
| FR-POS-06 | Navy summary (items/qty, MRP, SD, VAT, discount, payable); single tender table (Round Off, Payable, Cash, Card, Mobile, Change); **no Payment Breakdown tabs**; split tender; change only on cash over-tender | **Conf** | `FE/pages/pos/pos-screen.component.html` L280–390 | A "Tender Details" block exists **and** a separate "Payment Breakdown" section with method buttons and cash-tendered field (L321–375). Split payment supported (`useSplitPayment`, `splitPayments`). Currency formatted via `CurrencyService`. | Duplicate tab-style block contradicts FR-POS-06/§16.2. | Merge into one tender table. | P2 |
| FR-POS-07 | Line vs invoice discount mutually exclusive; confirm on switch; **server rejects both** | **Part** | `FE/pages/pos/pos-screen.component.ts` (~L1296–1360), `APP/Sales/Commands/CreateSaleCommand.cs` | FE detects switching and asks confirmation (L1349–1354) and audits via `PosAuditController`. Server accepts any `Items[].DiscountAmount` **and** `Discount` simultaneously. | No server rule. | Validator on `CreateSaleCommand`. | P0 |
| FR-POS-08 | Hold/Recall continuity; shortcuts F2/F4/F6/F7/F9; fullscreen keeps state; completion idempotent & atomic with stock | **Part** | `FE/pages/pos/pos-screen.component.ts` (`handleKeyboardShortcuts` L435–), `DOM/HeldSale.cs`, `INF/Sales/HeldSaleStore.cs`, `API/Controllers/HeldSalesController.cs` | Shortcuts implemented (F2, F4, F6, F7, F9, Ctrl+1/2/3). Hold = JSON cart parked in `held_sales` (no terminal/session/salesperson/inventory reservation, by design). Recall is client-side GET + DELETE (no atomic server "recall"), so two terminals can recall the same held cart. **[CS+]** `HeldSalesController.Create` forwards `dto.CashierId` from the client and `GET held-sales/cashier/{cashierId}` lets any `sales.view` user list another cashier's held carts inside their authorized outlets (`HeldSaleStore.GetByCashierAsync`); see N-6. | Idempotency: see FR-SALE-02. Hold has no session/salesperson fields. | Add atomic recall; add salesperson/terminal to hold payload if required; derive cashier from claims. | P2 |

### B.6 Stock visibility, search and negative-stock prevention

| ID | Feature | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| FR-POS-02 / §16.2 | Current-outlet stock visibility; never sell negative | **Impl** (stock gate) / **Part** (visibility) | `APP/Sales/Commands/CreateSaleCommand.cs`, `INF/Sales/SaleCreationStore.cs`, `INF/Data/RetailPOSDbContext.cs` L804–814, `APP/Pos/Queries/GetPosStockHintQuery.cs` | Within the transaction each line loads outlet inventory and throws "Insufficient stock…" if `Quantity < requested`; `Inventory.XMin` (PostgreSQL `xmin`) is an optimistic concurrency token and `SaveFinalChangesAsync` maps `DbUpdateConcurrencyException` → "Stock was modified…". `Inventory.AllowNegativeStock` setting exists but is **not** consulted by sales (correct vs SRS). | Display uses a 30s cache hint (documented as non-gating). Concurrency of two cashiers is covered only for idempotency/session-open/void/refund tests; no test for oversell race in checkout. | Add a PG oversell concurrency test. | P2 |

### B.7 Cashier / salesperson attribution (SRS §3, §8; LLD §3.1, §4)

| ID | Feature | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| Cashier derived from auth | `CashierUserId` never from client | **Conf** | `API/DTOs/Sale/CreateSaleDto.cs` (`[Required] CashierId`), `API/Controllers/SalesController.cs` (`Create`), `FE/pages/pos/pos-screen.component.ts` L1464 | The controller forwards `dto.CashierId` straight into `CreateSaleCommand`; FE sends `user.id`. Any authenticated `sales.create` caller can attribute a sale (and its journal `createdBy`, stock ledger actor, audit) to another user. | Direct contradiction of LLD §8.1/§18.6. | Derive from claims; ignore/deprecate the DTO field. | P0 |
| Salesperson selection/attribution | Eligible active outlet-assigned user, distinct from cashier | **Miss** | `DOM/Sale.cs`, `DOM/UserOutletAssignment.cs`, `APP/BusinessAdministration/BusinessRolePermissions.cs`, `database/seed-v2-roles.sql` | No `SalespersonUserId` on any transaction entity (search across `src/`, `FE/`, `database/`, `tests/`). A **`SalesPerson` role** exists (role seeds/`BusinessRolePermissions`), which is a permission role, not transaction attribution. Assignment data exists (`user_outlet_assignments`, `IsActive`) to support an eligible-user query. | Entity field, endpoint (`GET users/eligible-salespersons?outletId`), UI, receipt, reporting dimension; eligibility rule (role-based vs outlet-assignment-based) undefined. | Add after approved schema change; reuse `IUserOutletAccess` for eligibility; decide role vs assignment rule (§K-19). | P1 |
| AC-05 | Two cashiers share one session; performance attributed separately | **Part** | `INF/Pos/SaleCashSessionValidator.cs` | Shared-session selling works de facto (validator keys on outlet+terminal only). No salesperson dimension. | Salesperson half missing. | As above. | P1 |

### B.8 Customer lookup, phone uniqueness, Quick Add, Walk-in (SRS §9; LLD §9)

| ID | Feature | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| FR-CUST-01 / D08 | Normalized BD phone unique **per business**; safe duplicate handling; protected Walk-in | **Conf** | `DOM/Customer.cs`, `INF/Data/RetailPOSDbContext.cs` L946–958, `INF/Customers/CustomerCreateStore.cs`, `APP/Customers/Commands/CreateCustomerCommand.cs` | `Customer` has **no `BusinessId`** and no normalized phone. `PhoneExistsAsync` is an exact string compare across the whole table (so `017…` ≠ `+88017…`), non-race-safe, and no DB unique index on phone. `CustomerCode` unique index is global. Seed data uses non-BD phones (`+1-555-…`, `DbSeeder.cs` L423–428). `UpdateCustomerCommand`/`DeleteCustomerCommand` reject `IsSystem` (Walk-in protection works). | Tenancy, normalization, DB constraint, duplicate remediation, race handling all absent. Loyalty SRS P0-G1 is a prerequisite. | Prerequisite data-quality diagnostic then additive columns + filtered unique index (see §D). | P0 |
| FR-CUST-02 | Lookup `customers.view`, Quick Add `customers.create`, edit `customers.edit`; mask phone; race-safe create offers existing | **Part** | `API/Controllers/CustomersController.cs`, `INF/Customers/CustomerReadStore.cs` | Policies per action are correct (`customers.view` class-level; `create`, `edit`, `delete`). **No business scoping** in `SearchAsync`/`QuickSearchAsync` (`dbContext.Customers.Where(IsActive)…`) → cross-tenant read. Phone returned unmasked. Quick search matches `Phone.Contains(lower)` on raw text. | Tenant leakage; no masking; no duplicate-offer response. | Scope by business; mask in lookup DTO; 409 with existing match. | P0 |
| FR-CUST-02 (legacy loyalty endpoints) | — | **Conf** | `API/Controllers/CustomersController.cs` (`add-points`, `redeem-points`), `APP/Customers/Commands/{Add,Redeem}CustomerLoyaltyPointsCommand.cs`, `INF/Customers/CustomerLoyaltyStore.cs` | Both POST endpoints inherit only class-level `customers.view`; any such user can mint or burn points for any customer id. Redeem is read-modify-write on an integer with no OTP/concurrency check. Characterization tests lock this behaviour (`TST/…/Customers/CustomerLoyalty*HttpCharacterizationTests.cs`). | Violates Loyalty SRS D03/D08/FR-05.3/TR-14.5 and POS FR-LOY-UI-02. | Guard immediately (permission + feature flag), then retire via Loyalty module. | P0 |
| FR-CUST-03 | Relative's account nominated; no purchaser claim; phone never authorizes redemption | **Part** | `DOM/Sale.cs` (`CustomerId`) | Any customer can be selected as `CustomerId` today; no separate beneficiary/purchaser model. | Need `LoyaltyMembershipId` vs `CustomerId` separation (Loyalty FR-06.2, D40). | Provided by Loyalty integration ports. | P1 |
| Walk-in | Seeded, per-business, immutable | **Part** | `INF/Sales/SaleCreationStore.cs`, `APP/Sales/Commands/CreateSaleCommand.cs`, migration `20261013041308_RestoreAuditAndWalkInDatabaseProtections.cs` | Global singleton code `WALKIN`; created/repaired lazily inside a checkout transaction. **[V3 correction]** Immutability **is** implemented at two layers: application guards (`UpdateCustomerCommand`/`DeleteCustomerCommand` reject `IsSystem`) **and** a live database trigger `trg_customers_protect_walkin` (BEFORE UPDATE OR DELETE on `customers`) that rejects deleting or changing `customer_code`, `name`, `phone`, `email`, `is_system`, `is_active` of a system/WALKIN row (definition read from the Development database). | Tenant-global (unique `customer_code` index is global, so a second business cannot own its own `WALKIN` row); lazy creation inside sale transaction; note the app's "repair" branch (`CreateSaleCommand.EnsureWalkInCustomerAsync`, sets `IsActive/IsSystem`) would be rejected by the trigger if it ever fired on a tampered row. | Seed per business during onboarding; redesign the unique key to `(business_id, customer_code)` and adapt the trigger. | P1 |

### B.9 Loyalty integration (SRS §9, §16.3; Loyalty SRS/LLD)

| ID | Feature | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| FR-LOY-UI-01 / AC-15 | Display tier/points/earn estimate only from the real Loyalty service; no fabricated values | **Part** | `FE/pages/pos/pos-screen.component.ts` L376/L1843 (`loyaltyPoints: 0`), `FE/pages/customers/*` | POS shows **no** loyalty UI (no fabricated values — compliant). Customer pages show the legacy `loyaltyPoints` integer. | Real tier/points absent (Loyalty module not implemented). | Gate by Loyalty feature flag; no work until Loyalty ports exist. | P2 |
| Earning (SRS FR-LOY-UI-02, Loyalty FR-05.1–05.2) | Eligible net after VAT/discount/redemption; ৳100/pt; tier multiplier | **Conf** | `APP/Sales/Commands/CreateSaleCommand.cs` (L228–240), `VoidSaleCommand.cs` (L72–82), `API/Services/ISaleEventPublisher.cs` | Legacy rule: `earnedPoints = floor(totalAmount)` (1 pt/৳1 of the **VAT-inclusive payable**) added directly to `Customer.LoyaltyPoints`; void subtracts `floor(TotalAmount)`. Example: Loyalty AT01 (1000 − 100 VAT − 200 disc → 7 pts) would yield ~900 under legacy. **[CS+]** Legacy handling is also asymmetric: `VoidSaleCommand` decrements points but `RefundSaleCommand` never adjusts them, so refunded sales keep their points (balance drift). | Legacy earning contradicts approved decisions D02/D10/C01. | Remove legacy earning when Loyalty module goes live; do not create a parallel POS ledger. | P1 |
| FR-LOY-UI-01 (redeem) | OTP redemption (6-digit, 15-min reservation, ≥100 pts, ≤50%) | **Miss** | — | No OTP, reservation, ledger or redemption payment allocation. `SalePayment.Method` comment mentions `"loyalty"` but nothing populates it. | Entire flow — belongs to Loyalty module (§F). | Integrate through ports. | P1 |
| Loyalty FR-10.2 / SRS §18.1 | Atomic sale + redemption + durable earning entitlement in the checkout transaction | **Miss** | `APP/Sales/Commands/CreateSaleCommand.cs` | No extension point; checkout has no hook between stock/journal and commit for third-party in-transaction work, and no outbox write. | Needs integration contract (§F.4, §H-3). | Define port before building. | P1 |
| Loyalty FR-10.4 | Receipt: masked phone, tier, earned/redeemed/balance/pending | **Miss** | `APP/Sales/Commands/GenerateSaleReceiptDocumentCommand.cs`, `API/DTOs/Sale/ReceiptDocumentDto.cs` | Receipt model has no loyalty block. | — | Add after ports exist. | P2 |

### B.10 Discounts, VAT, payment methods, split payment (SRS §8.6, §16.2, D07; LLD §10)

| ID | Feature | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| Server-authoritative price | Selling price from pipeline; no arbitrary manual price | **Conf** | `API/DTOs/Sale/CreateSaleItemDto.cs`, `APP/Sales/Commands/CreateSaleCommand.cs`, `APP/Pos/Queries/CalculatePosCartPricesQuery.cs` | `UnitPrice`, `DiscountAmount`, `AppliedRuleId/Name` come from the request. The handler never calls the pricing query; `SaleItem.UnitPrice` is whatever was posted (only `Range(0, max)` attribute). A price pipeline (`/api/pos/cart-prices`) exists but is advisory. **[V3 verified]** Exhaustive search: `IPosCartPriceCalculator`/`CalculatePosCartPricesQuery` are referenced only by `PricingController` (L98, L150), `PosController.CartPrices` (L371) and `GetPosProductLookupQuery`; **never by `CreateSaleCommand`, `SalesController` or any sale store**. The pipeline itself is outlet-fixed/margin override → single best rule by priority → per-unit discount → per-variant `TaxRate`; it is cached in Redis per (variant, outlet, quantity). The checkout also ignores the pipeline's per-variant tax: the tax posted comes from the browser's single global `taxRate` signal. `[Range(0, max)]` is the only server rule on `UnitPrice`; `Discount`, `Tax` and `DiscountAmount` have none. | Client can post price 0 or any discount; no check discount ≤ line amount. | Re-price server-side inside the command (reuse `IPosCartPriceCalculator` with `ReadFromCache: false`); reject divergence; compute tax server-side. | P0 |
| D07 VAT | UI "VAT"; internal generic Tax components; immutable snapshots; configurable inclusive/exclusive | **Part** | `DOM/SaleItem.cs` (TaxableAmount, VatRate, VatAmount, TaxCode, TaxInclusive), `APP/Sales/Commands/CreateSaleCommand.cs` L170–190, `FE/pages/pos/pos-screen.component.ts` L70/L200 | Line snapshot fields exist, but: tax total is posted by the client from a **single global rate** (`taxRate` default 0.10 or settings); `TaxInclusive` is hard-coded `false`; product-level `TaxRate` (present in lookup) is unused at checkout; no tax-component table. **Defect:** `item.VatAmount = Tax × item.Subtotal / (Σsubtotal − Discount)`, so with an invoice discount the per-line VAT sums to more than `Sale.Tax` (e.g. subtotal 1000, discount 100, tax 90 → line VAT 100). `VatReportReadStore` sums `SaleItem.VatAmount`. | Inclusive mode unsupported; VAT snapshot inconsistent. Journal uses `Sale.Tax` so GL and VAT report disagree. | Fix allocation with remainder distribution; add unit tests. | P1 |
| FR-SALE-02 / §16.2 | Payments: Cash/Card/Mobile, split, full payment, no credit, no negative, no over-allocation except cash change | **Part** | `API/DTOs/Sale/CreateSaleDto.cs`, `APP/Sales/Commands/CreateSaleCommand.cs` | DTO enforces `Amount > 0`. Handler checks Σpayments ≈ computed total (±0.01). Method is `ToLower()`'d with **no whitelist** (`SalePayment.Method` max 20 chars). `Tendered` is stored **only when the client sends an explicit `Payments` list (split path)**; **no server check that cash `Tendered ≥ Amount`**; change is computed later in read models (`SaleReadStore` L52–53, `GenerateSaleReceiptDocumentCommand` L80–81). **[CS-fix]** The earlier text said "Tendered stored" unconditionally. In the handler's legacy single-method branch (`Payments.Count == 0`) the code reads `request.Payments.FirstOrDefault()?.Tendered` from the *empty* list, so `Tendered` is always null; the frontend sends `payments: undefined` for every non-split sale (`pos-screen.component.ts` ~L1470–1478) and never sends `cashTendered`. Result: for ordinary single-method cash sales the persisted sale carries no tendered amount, so receipts/Sale-Completed show **change = 0** (see N-2). Cash retained is `Amount` (not tendered) in the close calculator (correct against double-counting change). | No method whitelist/reference validation; no tendered check; tolerance ±0.01 permits underpayment; `Discount`/`Tax` may be negative (unchecked); tendered/change lost on single-method cash sales. | Validator + method catalog; persist tendered/change for every cash payment. | P1 |
| FR-SALE-02 | Idempotent, atomic completion | **Part** | `APP/Sales/Commands/CreateSaleCommand.cs`, `INF/Data/RetailPOSDbContext.cs` L1021–1024, `FE/pages/pos/pos-screen.component.ts` L1458–1459 | Unique `(OutletId, IdempotencyKey) WHERE key IS NOT NULL`; pre-lookup returns the original sale. The race **loser** gets `DbUpdateException` (asserted in `SaleCreationPostgresConcurrencyTests.ConcurrentSameKeyAfterBothInitialMisses…`) instead of the original result. FE calls `crypto.randomUUID()` **per click**, so a timeout-retry always produces a new key and a second sale. Key is optional server-side. | Idempotency effectively not enforced for real retries. | Stable per-cart key; make mandatory; catch unique violation → return original. | P1 |

### B.11 Sale completion, void, receipts (SRS §11; LLD §5.6, §12)

| ID | Feature | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| FR-SALE-02 | Sale + payment + inventory + ledger in one transaction | **Impl** (core) | `APP/Sales/Commands/CreateSaleCommand.cs`, `API/Services/SaleCreationAdapters.cs` | `BeginTransactionAsync` wraps stock decrement, sale insert, VAT/cost snapshots, journal creation+posting (`CreateOrGetSystemJournalDraftCommand` + `PostJournalEntryCommand`, carrying TerminalId/CashSessionId), stock-ledger append, audit, then commit. Persistence-level duplicate guarded. **[CS+]** Atomicity of the journal step is now *verified*, not assumed: `ManualJournalDraftStore` joins the ambient transaction (`ownsTransaction = dbContext.Database.CurrentTransaction is null`, L173/L323/L881) and `FrozenSystemJournalStore` requires one, so a journal failure (missing posting account, closed accounting period, unconfigured slot) **rolls back the whole sale** — checkout is hard-coupled to accounting configuration/period state (see G-23). | Invoice number allocator is outside the DB transaction (see G-9). `SaleCreationAccountingPoster` returns `null` (skips posting) if its dependencies are not supplied (DI-sensitive). | Preserve; fix allocator. | — |
| FR-SALE-03 / AC-13 | Void: `sales.void`, reason, any age, exactly-once stock/accounting reversal, audit | **Impl** | `API/Controllers/SalesController.cs` (`Void`, `[Authorize(Policy="sales.void")]`), `APP/Sales/Commands/VoidSaleCommand.cs`, `INF/Sales/SaleVoidStore.cs` | No age window. `TryMarkCompletedSaleVoidedAsync` is an atomic status gate; stock restored; reversal journal; `SaleVoid` row; audit; concurrent-void test exists. | Void does not record a **cash drawer effect** against any session (see FR-CLOSE-02); legacy loyalty decrement (see B.9); no outbox event. | Preserve; add session cash classification and events. | P1 |
| FR-SALE-01 | Lifecycle names | **Part** | `DOM/Sale.cs`, `DOM/HeldSale.cs`, `APP/Sales/Commands/RefundSaleCommand.cs` | `Sale.Status` is a free string (`completed`, `voided`, `refunded`); Hold is a separate table. **Partial refund marks the entire sale `refunded`** (`RefundSaleCommand`), contradicting line-level returns and the Loyalty AT08 partial-return rules. | Returns/Exchange SRD is separate; the current refund is whole-sale status flip with per-line stock quantities. | Treat as dependency (§F.5); no change in POS scope. | P2 |
| FR-RCPT-01 | 80mm default, A4, reprint, PDF, canonical **immutable** snapshot, failed print never reverses sale | **Part** | `APP/Sales/Commands/GenerateSaleReceiptDocumentCommand.cs`, `GenerateSaleReceiptPdfCommand.cs`, `PrintSaleReceiptCommand.cs`, `ReprintSaleReceiptCommand.cs`, `RegisterSaleReceiptAttemptCommand.cs`, `INF/Sales/SaleReceiptPdfRenderer.cs` (QuestPDF), `INF/Sales/SaleReceiptSettingsProvider.cs`, `DOM/ReceiptPrintHistory.cs` | Document/PDF/print/reprint endpoints and print-attempt audit exist; print is post-commit. The receipt is **rendered from live sale data + current JSON receipt/company settings**, not a stored snapshot, so a reprint after a header/footer/settings change differs from the original. | No stored canonical snapshot; no salesperson; no loyalty block; print-bridge abstraction (`IPrintProvider`) not present — FE uses `window.print()` and a heuristic `detectPrinterAvailability`. | Persist immutable receipt payload at completion (or deterministic rebuild from snapshot columns). | P2 |
| FR-RCPT-02 | Sale Completed modal: invoice, payable, cash/change, customer, **actual cashier vs salesperson**, New Sale/Reprint/Print | **Conf** | `FE/pages/pos/pos-screen.component.html` (receipt modal ~L420–520), `INF/Sales/SaleReadStore.cs`, `APP/Sales/Commands/GenerateSaleReceiptDocumentCommand.cs` | Receipt/completed modal exists with cashier; no salesperson. **[CS-fix]** Reclassified Part → Conf: cash/change on the receipt is derived from `SalePayment.Tendered`, which is never persisted for single-method cash sales (N-2), so change prints as 0 for the most common checkout. | Salesperson; loyalty-confirmed points; tendered/change persistence. | Fix tendered persistence first, then salesperson. | P1 |

### B.12 Cash reconciliation, Expense integration, handover, attachments (SRS §10; LLD §5.4–5.5)

| ID | Feature | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| D01 / D02 / FR-EXP-01 | No Cash In/Out; Expense Payment Source (POS Drawer, Petty Cash, Bank); searchable selectors; drawer expense tied to terminal session | **Miss** | `DOM/Expense.cs`, `INF/Expenses/ExpenseCommandStore.cs`, `API/Controllers/ExpensesController.cs`, `API/DTOs/Expense/ExpenseDtos.cs`, `FE/pages/accounting/expense-form.component.html` | `Expense` has `PaymentAccountId`, `OutletId?`, status `Draft/Submitted/Approved/Rejected/Cancelled`; **no `PaymentSource`, no `CashSessionId`**. FE form uses raw numeric inputs "Payment Account ID" (L51) and "Outlet ID" (L86). Approval posts a journal crediting the payment account (`ExpenseCommandStore.ApproveAsync`) — there is no separate "Paid" state. | No drawer linkage, no petty cash concept, no selectors; "paid/posted" is not defined in the current state machine (§H-14). | Add source + session reference to Expense; reuse approval kernel. | P1 |
| FR-CLOSE-02 | `Expected = Opening + net retained cash sales − drawer expenses − net refunds/void reversals (+ corrections)`; no double counting; from authoritative records | **Conf** | `APP/Pos/CashSessionCloseSummaryCalculator.cs`, `INF/Pos/CashSessionReadStore.cs` (`GetCloseSummaryDataAsync`) | Calculator: `cashSales` sums `Payment.Amount` (cash allocation, good) **only for `completed` sales**; sales with status `voided`/`refunded` are then **subtracted in full** as `cashRefunds`. A same-session voided cash sale was never added, so it is subtracted anyway → understated expected cash (double count). A partial refund subtracts every cash payment on the sale. `cashIn`/`cashOut` read `Transactions` rows with `ReferenceType` `cash_in`/`cash_out`/`cash_session_*` matched by `ReferenceId == session.Id` only — **no code anywhere writes these types** (dead legacy), and there is no Expense read. Void in a later session is attributed to the original session. | Formula incorrect vs SRS; expenses ignored; legacy Cash In/Out vocabulary still in code/tests (`CashSessionServiceTests.GetCloseSummary…ComputesCashInOut…`). | Rewrite calculator around classified immutable facts (§H-13). | P0 |
| FR-CLOSE-01/-03 | Four-section close page; counted cash; variance reason; denominations; **never auto-create expense / silently mutate ledger** | **Conf** | `APP/Pos/Commands/CloseCashSessionCommand.cs`, `API/Services/CashSessionCloseAdapters.cs` (`CashSessionVarianceJournalPoster`), `FE/pages/pos/pos-screen.component.html` L583–625 | Close takes `ActualClosingCashAmount` + free-text notes; a non-zero difference **posts a system journal** (Dr/Cr Cash vs `CashDifferenceGain/Loss`) inside the close transaction, idempotent by key `cash-session-close:{id}`. Reason is only "notes required if |diff| > 0.009". No denominations, no handover, no attachments, modal not page, no review step. **[CS+]** Because the variance journal is posted inside the close transaction and `ResolvePostingAccountAsync` throws when a slot is unconfigured, **a session with any variance cannot be closed if the Cash/CashDifferenceGain/CashDifferenceLoss accounts or the accounting period are not available** (test `CloseCommand_DoesNotPersistClosedSession_WhenVariancePostingFails` locks this in). That contradicts FR-CS-04 ("overdue remains closable") and the Loyalty principle that accounting failures must not block operations. Close also takes two separate `DateTime.UtcNow` readings (`summaryClosingAt` before, `closedAt` after the summary), so movements between them are in neither window. | The automatic journal may or may not be acceptable accounting policy; SRS says "never … silently mutate ledger". | Needs explicit decision (§K-2); if retained, decouple from close success (deferred/idempotent posting). | P1 |
| FR-CLOSE-01 (permission) | Closer needs **independent permission** | **Conf** | `API/Controllers/PosController.cs` L35, `API/Services/CashSessionOutletAuthorizerAdapter.cs` | Controller class-level `[Authorize(Policy = "sales.create")]` covers open, summary **and close**. No cash-session permissions exist in `APP/Identity/Permissions/PermissionCatalog.cs`. | Any cashier can close any session in an authorized outlet. | Add granular permissions + role mapping (see §E/§D). | P1 |
| FR-CLOSE-04 | Multiple handover lines (cash to person / bank deposit), retained = counted − handed over, deposit pending vs confirmed | **Miss** | — | No entity/table/endpoint. | — | New child tables (LLD §3.1). | P1 |
| FR-CLOSE-05 | Photo/PDF attachments, secure scoped storage, freeze snapshot, block double close/late commits | **Part** | `INF/AccountingJournals/JournalAttachmentStorageOptions.cs`, `APP/ConfidentialFiles/*`, `INF/Pos/CashSessionCloseStore.cs`, `APP/Pos/Commands/CloseCashSessionCommand.cs` | Attachment storage patterns exist elsewhere (journal attachments, confidential files) and are reusable. Cash-session attachments: none. Double-close is idempotent (returns existing) **[V3 correction: only for *sequential* repeats — `CloseCashSessionCommand` reads the tracked session before the transaction and `CashSessionCloseStore` has no row lock, no `WHERE status='open'` guard and no row version, so two concurrent closes can both pass the status check and both update/audit/publish; the variance journal is protected only by its create-or-get key `cash-session-close:{id}`]**. **No freeze against late commits**: close runs ReadCommitted with no lock; summary computed before the transaction. | Late sale/expense can commit after summary. | Lock session row; recompute inside transaction. | P0 |
| AC-08 / AC-07 | Close counts/handovers/immutable summary; drawer expense affects only its session once | **Miss** | — | See above. | — | — | P1 |

### B.13 Architecture, outbox, reporting, security (SRS §12–13; LLD §2, §11, §13)

| ID | Feature | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| LLD §1 MediatR/CQRS | Handlers via MediatR, validators, store abstractions | **Part** | `APP/DependencyInjection.cs` L29–31, `APP/Behaviors/ValidationBehavior.cs`, `APP/Sales`, `APP/Pos`, `INF/Pos`, `INF/Sales` | Clean MediatR + `I…Store` pattern is followed. Only **one** pipeline behavior (validation); **no** FluentValidation validators exist for Sale/Pos/Outlet/Customer commands (validators present only for auth/category/identity). Business-rule failures are `InvalidOperationException` → HTTP 400 via `GlobalExceptionHandlerMiddleware` (no 409/422). | Validation is imperative inside handlers; no idempotency/transaction/authorization behaviors. | Add validators; map conflicts to 409/422. | P2 |
| FR-REPORT-02 / TR-14.4 | Durable outbox, RabbitMQ, idempotent consumers, rebuildable projections | **Part** | `INF/Integration/{IntegrationOutbox,OutboxDispatcher,RabbitMqDelivery,IntegrationInbox}.cs`, `APP/Integration/DurableDeliveryContracts.cs`, `API/Program.cs` L607–633, `API/Services/LoggingSaleEventPublisher.cs`, `API/Services/ISaleEventPublisher.cs` | A real outbox exists (lease + `FOR UPDATE SKIP LOCKED`, attempts/backoff/dead-letter, envelope hash) and an inbox; used today for operational-location, stock-count adjustment and mobile stock-take. **Sales and cash sessions do not use it**: `ISaleEventPublisher` is bound to `LoggingSaleEventPublisher` (`Program.cs` L906) and events are published with `_ =` fire-and-forget after commit. `IntegrationOutbox.AppendAsync` additionally **requires** a transaction plus an active "required audit" scope (`RequireTransaction`). | No `SaleCompleted/SaleVoided/CashSession*` durable events; no POS projection tables (no `*Summary` DbSets); no replay/rebuild; replay authorization is a stub (`DisabledIntegrationReplayAuthorization`). | Adopt the existing envelope; add producers in the same transactions. | P1 |
| FR-REPORT-01 | Immutable transaction facts: business/outlet/terminal/session/business-date/cashier/salesperson/customer/VAT/tender | **Part** | `DOM/Sale.cs`, `DOM/SaleItem.cs`, `DOM/SalePayment.cs` | Has outlet, terminal, session, business date, cashier, customer, line VAT snapshot, historical unit cost. **Missing:** `BusinessId` on `Sale` (only via outlet), salesperson, category snapshot, tender reference, variant display snapshot. | — | Additive columns (§D). | P1 |
| FR-REPORT-03 | Reports by BusinessDate; salesperson correct after transfer | **Conf** | `INF/SalesReports/SalesReportReadStore.cs`, `INF/AccountingReports/VatReportReadStore.cs` | Reports and VAT filter on `SaleDate` (effective date) and aggregate VAT-inclusive `TotalAmount`; `VatReportReadStore` loads sales into memory before filtering. | — | Re-base on `BusinessDate` and a versioned net-ex-VAT metric. | P2 |
| §13 permissions | Granular permissions: customers, sales.backdate, sales.void, terminals, targets, session open/close, attachments, rebuild | **Part** | `APP/Identity/Permissions/PermissionCatalog.cs` L20–32, `API/Controllers/*` | Present: `sales.view/create/backdate/void/refund`, `customers.*`, `outlets.*`, `expenses.*` (+aliases; legacy alias `pos.sale.backdate` → `sales.backdate`). **Absent:** cash-session (view/open/close/attachments), terminal manage, target view/edit, projection rebuild; `accounting.reports.cash_closing` is declared but no endpoint uses it. | Everything under "Absent". | Extend catalog + role seeds. | P1 |
| §13 audit | Log open/close, cashier, salesperson, customer/loyalty, variance, expense, handover, terminal/target, receipt prints, declined cutoff attempts, correlation id | **Part** | `INF/Pos/CashSession{Open,Close}AuditWriter.cs`, `INF/Pos/SaleCashSessionValidator.cs`, `API/Services/SaleCreationAdapters.cs`, `INF/Sales/SaleReceiptActionAuditWriter.cs` | Audit v2 events exist for session open/close, declined overdue sale attempts, sale create/void/refund, receipt actions, discount changes. Open/close audits are written **after** the main commit; backdate audit swallowed on failure. | Terminal/target/handover/expense-session/salesperson audits absent. | Add as features land; consider writing audit within transaction. | P2 |
| §13 isolation | Server-authoritative business/outlet/terminal scope | **Part** | `API/Services/UserOutletAccessService.cs`, `APP/Identity/LocationAccess/UserOutletAccess.cs`, `INF/Data/RetailPOSDbContext.cs` | Outlet-level authorization is consistently used on sale/session/void/hold endpoints (`EnforceWriteOutletAsync`, `ResolveAndAuthorizeLocationAsync`). `Sale` has no `BusinessId`; `Outlet.BusinessId` and `CashSession.BusinessId` are **nullable**; no global query filters (`HasQueryFilter` count 0); `Customer` unscoped. | Cross-tenant risk for customers; integrity weaker than needed. | Customer scoping (P0); consider NOT NULL after data review. | P0 |
| §13 NFR | <2s load, <100ms scan, <1s checkout, <2s print | **VR** | `INF/Pos/PosProductLookupCache.cs`, `INF/Pos/PosCartPriceCache.cs` | Redis caching exists for lookup/pricing. No performance tests found. | No measurement. | Add benchmarks. | P3 |

### B.14 Decision register and acceptance-test roll-up

| Item | Status | One-line basis |
|---|---|---|
| D01 No Cash In/Out | **Part** | No Cash In/Out endpoints/UI exist in code, but expense linkage is absent and close calculator still reads legacy `cash_in/out`. |
| D02 Payment sources | **Miss** | No `PaymentSource` on `Expense`. |
| D03 20h strict | **Conf** | 15h default; no commit-gate recheck. |
| D04 Terminals in Outlet | **Miss** | See B.1. |
| D05 Weekday targets | **Miss** | See B.2. |
| D06 Compact POS | **Conf** | Header shows Business Date, clock, counts. |
| D07 VAT/Tax | **Part** | See B.10. |
| D08 One normalized phone | **Conf** | See B.8. |
| UX-v3 | **Part** | Cart/tender structure close; footers, header, fullscreen, session pages differ. |
| AC-01 | Miss | AC-02 Part · AC-03 Miss · AC-04 Conf · AC-05 Part · AC-06 Part (blocks at 15h, no commit gate) · AC-07 Miss · AC-08 Miss · AC-09 Miss · AC-10 Conf · AC-11 Part · AC-12 Part · AC-13 Impl · AC-14 Part · AC-15 Part · AC-16 Miss · AC-17 Part · AC-18 VR (no regression run). |

**Tallies of matrix rows (rows in B.1–B.13 only; roll-up table excluded):**

| Status | Rows | Share |
|---|---|---|
| Implemented | 3 | 4.6% |
| Partially Implemented | 28 | 43.1% |
| Missing | 17 | 26.2% |
| Conflicting | 16 | 24.6% |
| Verification Required | 1 | 1.5% |
| **Total** | **65** | 100% |

**[CS-fix]** Rev 1 showed Partial 29 / Conflicting 15; FR-RCPT-02 was reclassified Partial → Conflicting (see its row). Rows added by the Claude Sonnet review (B.15) are tallied separately below and in the combined roll-up.

(The stock-gate row in B.6 is counted as Implemented for the server gate; its display half is Partial. Counts reflect matrix rows, which group related SRS IDs; they are an indicator, not a weighted completion metric.)

### B.15 **[CS+]** Additional findings from the Claude Sonnet independent review

These are gaps not present (or only implicit) in Rev 1. IDs use `N-n` for traceability from §G/§N. Requirement IDs refer to the SRS/LLD/Loyalty documents.

| ID | Feature / requirement | Status | Existing paths | Evidence | Missing / Conflicting | Recommended action | Pri |
|---|---|---|---|---|---|---|---|
| N-1 | Exchange/replacement link authorization (SRS §2.2/§16.1 sale lifecycle; §13 isolation) | **Conf** | `API/DTOs/Sale/CreateSaleDto.cs` (`OriginalSaleId`), `APP/Sales/Commands/CreateSaleCommand.cs` (~L196–206), `INF/Sales/SaleCreationStore.cs` (`FindSaleForUpdateAsync`) | `OriginalSaleId` comes from the request; the store looks the sale up **by id only** (no outlet/business/status check) and then writes `originalSale.ReplacementSaleId = sale.Id` and `sale.OriginalSaleId`. The controller authorizes only the *new* sale's outlet. A user can attach their sale as "replacement" of any sale id (other outlet/tenant, voided/refunded), mutating a foreign row. | No authorization/state validation of the original sale; no check it is `completed`, same outlet/business, or not already replaced. | Authorize original sale via `ISaleMutationOutletAuthorizer`; require completed, not already replaced; belongs in Returns/Exchange SRD scope. | P1 |
| N-2 | Cash tendered/change persistence (FR-POS-06, FR-RCPT-02, §16.2 "change only on cash over-tender") | **Conf** | `APP/Sales/Commands/CreateSaleCommand.cs` (payment build), `FE/pages/pos/pos-screen.component.ts` (~L1470–1478), `INF/Sales/SaleReadStore.cs` L52–53 | Single-method path builds `Tendered = primaryMethod == "cash" ? request.Payments.FirstOrDefault()?.Tendered : null` inside the `Payments.Count == 0` branch ⇒ always null; FE omits `payments` and `cashTendered` for non-split sales. | No persisted tendered/change for the default checkout; receipt/Sale-Completed change = 0; drawer audit of "cash received" impossible. | Persist tendered for all cash payments (server-validated ≥ amount); add test. | P1 |
| N-3 | `BusinessDate` column type/time semantics (FR-CS-03, FR-TIME-03; LLD §3.1 "BusinessDate (date)") | **Conf** **[V3: was VR — mechanism verified, see §O.8; conditional on host time zone]** | `INF/Data/Migrations/20261004095137_InitialBaseline.cs` (`cash_sessions.business_date`), `INF/Data/RetailPOSDbContext.cs` (Sale: `HasColumnType("date")`; CashSession: none), `API/Program.cs` L31 | Verified schema fact: `cash_sessions.business_date` is **`timestamp with time zone`**, while `sales.business_date` is **`date`**. `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)` is enabled globally, so `DateTime.Kind` is not enforced; the client-supplied date arrives as `Kind=Unspecified`. **[V3]** Live Development database confirms `cash_sessions.business_date`/`opened_at` = `timestamp with time zone`, `sales.business_date` = `date`. Npgsql 9.0.2 documentation states that with the legacy switch "reading a `timestamp with time zone` returns a Local DateTime instead of Utc". The repository itself already works around this elsewhere (`INF/Inventory/StockCounts/UtcTime.cs` L5, `INF/AccountingJournals/ManualJournalDraftStore.cs` L1110) but **no POS cash-session code does**: `SaleCashSessionValidator` (`now >= session.OpenedAt.AddHours(maxHours)`), `GetActiveCashSessionQueryHandler` (`overdue`, `elapsedSeconds`) and `OpenCashSessionCommand` compare a Local-kind value with `DateTime.UtcNow` by ticks (kind ignored). On a host whose OS time zone is not UTC the 20h cutoff and elapsed time shift by the host's UTC offset (e.g. +6 h host ⇒ cutoff effectively 26 h; +1 h host ⇒ 21 h). Not executed: no test touches this (`CashSessionServiceTests` uses the EF InMemory provider). | Latent defect that manifests only when the API host OS time zone ≠ UTC (deployment time zone is **not established** by any repository file; dev workstation is `GMT Standard Time`); inconsistent types across the two tables that must be joined on business date; effect of writing an `Unspecified` client date to `timestamptz` under the legacy switch not verified. | Run a PostgreSQL integration test with the process time zone forced to `Asia/Dhaka` and `UTC`; normalise `OpenedAt` to UTC at the store boundary (or drop the legacy switch deliberately); plan a `date` column in the approved migration. | P0 (cutoff correctness) |
| N-4 | Catalog tenancy (SRS §13 "server-authoritative business scope"; §8 product search) | **VR** **[V3: remains an explicit owner decision — see §O.6]** | `DOM/Product.cs`, `DOM/ProductVariant.cs`, `DOM/Category.cs`, `INF/Pos/PosProductLookupStore.cs`, `Docs/02_Security/DATA_ISOLATION.md` | Verified: `Product`, `ProductVariant` and `Category` have **no `BusinessId`**; lookups filter only by barcode/SKU/status. `DATA_ISOLATION.md` states isolation is application-level and not uniformly applied. Whether the catalog is *intended* to be shared across tenants was not determined. **[V3]** Live schema re-confirmed: no `business_id` on `products`, `product_variants`, `categories`, `customers`, `sales`, `held_sales`, `inventories`. Intent evidence is **contradictory/absent**: `Docs/05_Product_Catalogue/PRODUCT_MODULE_ANALYSIS.md` decision #20 makes Main Product Code "globally unique across the entire catalogue"; `Docs/Mobile/Planning/01_EXISTING_SYSTEM_ASSESSMENT.md` L44 records "VERIFIED structure / **UNKNOWN policy**"; `34_API03_PRODUCT_CATALOGUE_ACCEPTANCE.md` L38–40 states the catalog has no ownership columns and relies on a single-Business deployment guard; `Docs/02_Security/DATA_ISOLATION.md` claims multi-business SaaS. The Development database holds exactly 1 business. | If the catalog is per-business in practice, variants of one tenant are visible through `GET /api/pos/lookup` to another. **[V3 correction]** Checkout does *not* freely sell any variant id: `SaleCreationStore.FindOutletInventoryAsync` requires an `inventories` row for (variant, outlet, `outlet`) and otherwise throws "No inventory found", so cross-tenant *sale* exposure depends on how inventory rows are created, not on the catalog alone. | Decision: shared catalog (then document) vs tenant-owned (additive `business_id`); add cross-tenant sale test. | P1 |
| N-5 | Expense approval separation (FR-EXP-01 "existing approval/reversal rules") | **Part** **[V3: was VR — verified, see §O.7]** | `APP/Identity/Permissions/PermissionCatalog.cs` (aliases `expenses.edit` ⇒ `accounting.expenses.approve/reject`), `INF/Expenses/ExpenseCommandStore.cs` (`ApproveAsync`) | Approve is aliased to the legacy `expenses.edit` permission; no maker-checker/self-approval check was found in `ApproveAsync` (search for submitter≠approver comparisons returned nothing). **[V3 verified]** Routes are `POST api/accounting/expenses/{id}/submit` (`accounting.expenses.submit`) and `…/approve` (`accounting.expenses.approve`) in `API/Controllers/AccountingExpensesController.cs` L232–273: separate permissions and a `Draft→Submitted→Approved` state machine, so separation *by role* is possible. But (a) `ApproveAsync` (`INF/Expenses/ExpenseCommandStore.cs` L382–) checks only status/category/accounts — it never compares `actorUserId` with `CreatedBy`/`SubmittedBy`; (b) `BusinessRolePermissions.BusinessOwner = ["*"]` and `PermissionCatalog.IsGranted` honours the wildcard, so an owner can create, submit and approve alone; (c) legacy `expenses.edit` is accepted as an alias for `accounting.expenses.approve/reject` (`PermissionCatalog.cs` L76–82); (d) no test covers expense self-approval (self-approval tests exist only for manual journals, HR/payroll). The legacy `api/expenses` controller has no approve route, so it is not a bypass. | Under *existing* rules FR-EXP-01 is satisfiable, but those rules contain no separation of duties; for a cash-drawer outflow the recorder may also post it. | Owner decision (§O.14-D7): require SoD for POS Drawer / Petty Cash sources (reuse the manual-journal `AllowSelfApproval` configuration pattern or the HR approval kernel); add a test. | P1 |
| N-6 | Held-sale actor trust (FR-POS-08; LLD §18.6 "never trust CashierUserId") | **Conf** | `API/Controllers/HeldSalesController.cs` (`Create`, `GET cashier/{cashierId}`), `INF/Sales/HeldSaleStore.cs` | `CreateHeldSaleCommand` takes `dto.CashierId` from the client; the by-cashier list takes `cashierId` from the route. | Spoofable owner; cross-cashier visibility within authorized outlets. | Derive from claims; restrict list to caller unless manager permission. | P2 |
| N-7 | Receipt endpoint side effects (FR-RCPT-01 print history/audit) | **Conf** | `API/Controllers/SalesController.cs` (`GET {id}/receipt`), `APP/Sales/Commands/PrintSaleReceiptCommand.cs` | A `GET` runs `PrintSaleReceiptCommand` which writes receipt-print history (`ISaleReceiptHistoryWriter`). | State-changing GET: prefetching/crawling/refresh inflates print history; weak audit semantics. | Make history writes explicit POST (`register-attempt` already exists). | P3 |
| N-8 | Idempotent replay integrity (FR-SALE-02) | **Part** | `APP/Sales/Commands/CreateSaleCommand.cs`, `INF/Sales/SaleCreationStore.cs` (`FindByIdempotencyKeyAsync`) | Replay returns the stored sale for **any** request carrying the key (outlet-scoped) without comparing cart/payment/customer/actor; the race loser has already consumed an invoice number from the file counter. | A reused/colliding key silently returns an unrelated sale; invoice-number gaps. | Store a request hash; reject mismatch with 409; allocate number after the gate or from a DB sequence. | P2 |
| N-9 | POS access for owners/role-switched users (SRS §3 persona table: "Business owner/Super Admin … wildcard scope honored") | **Part** **[V3: was VR — verified, see §O.5]** | `FE/pages/pos/pos-screen.component.ts` (`user.outletId` guards L1425–1428, `loadTerminals(user.outletId)`), `API/Services/UserOutletAccessService.cs` | FE derives the outlet only from `user.outletId`; there is no outlet picker on POS. Backend supports multi-outlet owners (`EnforceWriteOutletAsync`). **[V3 verified by code]** Backend: `UserOutletAccess.GetAuthorizedOutletsAsync` gives Super Admin all outlets of the selected business (or **all businesses** when no `X-Business-Id` is supplied; `UserOutletAccessStore.ListOutletsAsync(null)` has no business filter), gives Business Owner / wildcard users every outlet of their business, and throws when a non-Super-Admin has no business (so the legacy no-`BusinessId` fallback described in `DATA_ISOLATION.md` is **stale**; tests `WildcardOwner_WithoutBusinessContext_DoesNotListAllBusinesses`, `SuperAdminAccess_IsScopedWhenBusinessContextIsSupplied`, `GlobalAccess_UsesSelectedBusinessAndDoesNotKeepForeignDefault` exist). Role switch: `RoleSwitchCommandHandler` verifies the acting outlet belongs to the owner's business and returns `outletId = ActingOutletId`; `RoleSwitchContext.IsBusinessOwner` is derived from the **real** role, so a role-switched owner keeps business-wide outlet scope with the acting role's permissions. Sale completion calls `tenantAccess.RequireBusinessId()` (`SaleCreationAccountingPoster`), which throws `BusinessContextRequiredException` for a Super Admin without `X-Business-Id`. Frontend: POS reads only `currentUser().outletId` (`pos-screen.component.ts` L390, L627, L732, L1425); onboarding sets the owner's `OutletId` to the first outlet (`BusinessOnboardingWriteStore.cs` L120/L298). | Owners can sell only at their default/acting outlet (no outlet picker), Super Admin cannot use POS without an assigned outlet and business header; backend would allow any outlet in scope. Cross-business protection for ordinary users rests on outlet-assignment filtering by business, which is covered by tests. | Add an outlet selector fed by `/api/me/outlets`; decide Super Admin selling policy (§O.14-D8). | P2 |

**Tally of B.15 rows (Rev 3):** by status — Conflicting 5 (N-1, N-2, N-3, N-6, N-7), Partially Implemented 3 (N-5, N-8, N-9), Verification Required 1 (N-4); total 9. **[V3: N-3 VR→Conf, N-5 VR→Part, N-9 VR→Part.]**

**Combined roll-up (B.1–B.13 + B.15), Rev 3:**

| Status | Rows | Share |
|---|---|---|
| Implemented | 3 | 4.1% |
| Partially Implemented | 31 | 41.9% |
| Missing | 17 | 23.0% |
| Conflicting | 21 | 28.4% |
| Verification Required | 2 | 2.7% |
| **Total** | **74** | 100% |

(Rev 2 was 3 / 29 / 17 / 20 / 5. The two remaining Verification Required rows are the §13 performance NFR (B.13) and catalog tenancy intent (N-4, an owner decision rather than a technical unknown). Percentages are rounded and sum to 100.1.)

*Requirement coverage:* 71 SRS identifiers were reviewed (53 decision/functional IDs: D01–D08, UX-v3, FR-TERM ×5, FR-TARGET ×4, FR-CS ×5, FR-TIME ×3, FR-POS ×8, FR-CUST ×3, FR-LOY-UI ×2, FR-EXP-01, FR-CLOSE ×5, FR-SALE ×3, FR-RCPT ×2, FR-REPORT ×3; plus AC-01…AC-18), together with LLD §3–§5, §8, §10–§13 contracts and Loyalty FR-10.x / TR-14.x integration contracts, consolidated into the 74 rows above.

---

## C. Backend and API Analysis

### C.1 Actual controller/route map vs LLD sketch

| LLD sketch | Actual | Notes |
|---|---|---|
| `GET /outlets/{id}/pos-terminals`, `POST/PATCH/DELETE …` | **Absent.** Closest: `GET /api/pos/terminals?outletId` (`PosController.GetTerminals`) | Read-only, active-only, default-first. |
| `GET/PUT /outlets/{id}/sales-targets`, `GET …/target-performance` | **Absent** | — |
| `GET /cash-sessions` (history) | **Absent** | — |
| `GET /cash-sessions/current?terminalId=` | `GET /api/pos/cash-sessions/active?outletId&terminalId` | Returns `IsOverdue`, `MaxDurationHours`, `ElapsedSeconds` (server clock seconds, but FE does not use a server offset). |
| `POST /cash-sessions` | `POST /api/pos/cash-sessions/open` | Accepts client `BusinessDate` (defect). No idempotency key. |
| `GET /cash-sessions/{id}` | **Absent** | — |
| `GET /cash-sessions/{id}/close-preview` | `GET /api/pos/cash-sessions/{id}/summary` | Name is misleading; it is a live close preview. |
| `POST /cash-sessions/{id}/close` | `POST /api/pos/cash-sessions/{id}/close` | Only counted cash + notes; no row-version, handovers, attachments, idempotency key (idempotent by status). |
| `GET /users/eligible-salespersons` | **Absent** | — |
| `GET /customers/search?phone=` | `GET /api/customers/search?q=` | Free-text, unscoped, unmasked. |
| `POST /customers` | `POST /api/customers` (`customers.create`) | No normalization/race handling. |
| `POST /sales` | `POST /api/sales` (`sales.create`) | Client-supplied `CashierId`, `UnitPrice`, `Tax`, `Discount`. |
| `POST /sales/{id}/void` | `POST /api/sales/{id}/void` (`sales.void`) | Meets requirement. |
| Receipt endpoints | `GET /api/sales/{id}/receipt`, `/receipt-document`, PDF, reprint, print-attempt | Present. |
| Held sales | `/api/held-sales` | Soft-state carts. |

Controller-level authorization: `PosController` (whole controller) = `sales.create`; `SalesController` = `sales.view` with per-action overrides; `CustomersController` = `customers.view` with per-action overrides. `UserOutletsController` returns **401** on `UnauthorizedAccessException` (vs LLD §13 "403 must not invalidate session"); the global handler maps `UnauthorizedAccessException` to 403 elsewhere.

### C.2 Checkout flow trace (`POST /api/sales`)

1. `SalesController.Create`: date/backdate checks (future date rejected; `sales.backdate` required for past date), `EnforceWriteOutletAsync` (non-owners pinned to authorized outlet), then `CreateSaleCommand` carrying the **client-provided** `CashierId`, `UnitPrice`, `DiscountAmount`, `Discount`, `Tax`, `PaymentMethod`, `Payments`, `IdempotencyKey`.
2. `CreateSaleCommandHandler`: item-count check → idempotency pre-lookup (outside transaction) → customer resolve (or lazily create global Walk-in) → payment-sum check (±0.01) → **BeginTransaction** (default isolation).
3. Per line: load outlet inventory, reject when `Quantity < requested`, decrement (xmin concurrency token).
4. Resolve terminal (fallback default/first active), **validate session** (`SaleCashSessionValidator`, unlocked read, 15h check, side-effecting audit/event publication on failure), allocate invoice number (JSON file), build `Sale/SaleItem/SalePayment`, save, compute VAT snapshots, cost snapshots, link original sale for exchange.
5. `SaleCreationAccountingPoster.PostSaleAsync`: system journal (`sale:{id}` key) with payment/discount/revenue/VAT/COGS/inventory/rounding lines; stock ledger append; legacy loyalty points `+= floor(total)`; `SaveFinalChangesAsync` (xmin conflict → retry message); audit; **commit**.
6. After commit: stock cache invalidation, `_ = saleCompletedPublisher.PublishAsync(...)` (logging publisher, fire-and-forget), return read model.

**Transaction boundaries (Verified):** steps 3–5 are one DB transaction (journal posting runs through MediatR on the same scoped `DbContext`). Outside the transaction: invoice number allocation (file), idempotency pre-check, session-expired event/audit, cache invalidation, event publish, backdate audit.

### C.3 Cash-session flow trace

- **Open:** `PosController.OpenCashSession` → `EnforceWriteOutletAsync` → `OpenCashSessionCommand` → terminal-active check → `HasOpenSession` → insert → unique-violation → `null` → "already exists". Audit (`RecordOpenedAsync`) and event publish occur **after** the insert, outside a shared transaction.
- **Close:** `PosController.CloseCashSession` → authorize → `CloseCashSessionCommand`: reads summary (outside transaction), requires note on difference, begins **ReadCommitted** transaction, posts variance journal, updates session, commits, then audit + event. Already-closed returns the existing session.
- **Validation:** `SaleCashSessionValidator` as above. No shared lock between open/close/sale/expense.

### C.4 Validation, services and architecture observations

- **No pipeline-level idempotency, authorization or transaction behavior**; each handler opens its own transaction through a store interface (`ISaleCreationStore.BeginTransactionAsync`, `ICashSessionCloseStore.BeginTransactionAsync`). This is consistent and testable but means the LLD "serializable session boundary" must be implemented in the stores.
- **Settings are file-based** (`JsonSystemSettingsStore`, `SemaphoreSlim` lock, cached object): company, currency, tax (single default rate), receipt, invoice-number, inventory, POS (max session hours). Not business-scoped, not transactional, process-local lock.
- **Posting accounts** come from `IAccountingConfigurationService` slots (Cash, Sales, VatPayable, Cogs, Inventory, SalesDiscount, RoundingDifference, CashDifferenceGain/Loss) — reusable for drawer/petty-cash design.
- **Approval kernel / maker-checker** exists (HR approvals, expense approvals) — reusable for handover verification if approved.
- Business-rule errors are `InvalidOperationException` (HTTP 400). LLD asks for 409/422 and stable machine codes (`CASH_SESSION_OVERDUE`, …) — absent.

---

## D. Database and Migration Analysis (no migrations generated or applied)

Existing state (Verified from `INF/Data/RetailPOSDbContext.cs` and the migration folder): 60 migrations; the latest is `20261023072233_PurchaseOrderOutletDestination`. Only `20261004095137_InitialBaseline` touches `pos_terminals`, `cash_sessions`, `customers` (no migration has altered POS tables since).

### D.1 Existing tables and constraints relevant to the blueprint

| Table (entity) | Key columns / constraints today | Relevant notes |
|---|---|---|
| `outlets` (`Outlet`) | `business_id` **nullable** (FK SetNull), `manager_id` (SetNull), `source_version` concurrency token | No timezone, no targets. |
| `pos_terminals` (`PosTerminal`) | `outlet_id` FK **Cascade**; unique `(outlet_id, code)`; index `(outlet_id, is_default)`; `is_active`, `is_default` | No `description`, `business_id`, normalized code, concurrency token, timestamps exist. |
| `cash_sessions` (`CashSession`) | `business_id` nullable; FKs Restrict (outlet, terminal, opened_by); `closed_by` SetNull; `status varchar(20)`; partial unique `ux_cash_sessions_one_open_per_terminal (outlet_id, terminal_id) WHERE status='open'`; indexes `(outlet_id, terminal_id, status)`, `(outlet_id, business_date)`, `opened_at`; decimals (18,2) | No row version, no persisted `overdue`, no close snapshot beyond Expected/Actual/Difference, no timezone basis for `business_date`. |
| `sales` (`Sale`) | `terminal_id` FK **SetNull**, `cash_session_id` SetNull, `customer_id` SetNull, `cashier_id` Restrict; unique `(outlet_id, idempotency_key) WHERE key IS NOT NULL`; indexes on `sale_date`, `business_date` (date), outlet, terminal, session; money `numeric(10,2)` | **No unique index on `sale_number`**. No `business_id`, `salesperson_user_id`. `numeric(10,2)` is narrower than the (18,2) used on payments/VAT columns elsewhere. |
| `sale_items` | VAT snapshot columns (18,2/5,2), `historical_unit_cost (10,4)` | `tax_inclusive` always false. |
| `sale_payments` | `method varchar(20)`, `amount`/`tendered (10,2)`, `payment_account_id` index | No reference/provider data, no tendered/change/retained columns beyond `tendered`. |
| `sale_voids` | `sale_id`, voided_by, reason, previous_status | No cash-effect classification, no idempotency key. |
| `held_sales` | outlet, cashier, customer, `items_json` | No terminal/session/salesperson. |
| `customers` (`Customer`) | unique `customer_code` (global, filtered not-null); `phone varchar(20)`; `loyalty_points int` | **No `business_id`, no normalized phone, no unique phone index.** |
| `expenses` (`Expense`) | `business_id` required; `payment_account_id`, `outlet_id?`, status string, `journal_entry_id`, `attachment_path` | No payment source, no cash-session reference. |
| `inventories` | unique `(variant_id, location_id, location_type)`; `xmin` token | Stock concurrency token (good). |
| `integration_outbox_messages`, inbox, deliveries | Outbox with lease/attempts/status/hash | Reusable envelope; not populated by POS today. |
| Audit v2 (`AuditEvents*`) | exists | Reusable. |
| Loyalty entities | **None.** Only `customers.loyalty_points`. | Belongs to Loyalty module. |
| Targets / handovers / attachments / projections / sessions history | **None** | — |

### D.2 Entities/changes the approved blueprint implies (analysis only)

| Need | Nature | Risk / notes |
|---|---|---|
| Terminal: `description`, normalized code unique incl. inactive, concurrency token, (optional) `business_id`; change `Sale.TerminalId`/`ReceiptPrintHistory` FK to Restrict | Additive columns + FK change | FK change affects delete semantics; requires approval. Existing `(outlet_id, code)` index may already be sufficient if codes are trimmed/upper-cased by the app; case-insensitive uniqueness needs a data-quality check first. |
| Business-level POS settings (max session hours default 20, timezone) | New table or business-setting reuse (`BusinessFeatureSetting` exists) | Replaces JSON file semantics; migration of the single global value is a business decision (§K-3). |
| `cash_sessions`: row version, close snapshot (counted/expected/variance/reason), denominations, optional persisted overdue, possibly extended unique index (`status IN ('open','overdue')`) | Additive + index change | Index change must be done online/concurrently; existing rows stay valid. |
| `cash_session_handovers`, `cash_session_attachments`, activity/timeline source | New tables | Reuse storage abstraction patterns (journal attachments). |
| `outlet_sales_target_versions`, period overrides | New tables | Effective dating; no historical backfill (must not invent history — LLD §15). |
| `sales`: `salesperson_user_id` (FK to `users`, nullable for history), `business_id` (optional), `tender`/reference data; **unique `(business/outlet, sale_number)`** | Additive columns + new unique index | The unique index requires a duplicate diagnostic query first; legacy sales have no salesperson (never fabricate). |
| `customers`: `business_id`, `normalized_phone`, filtered unique `(business_id, normalized_phone) WHERE normalized_phone IS NOT NULL` | Additive + backfill | **Needs data-quality review first** (global duplicates, non-BD phones, Walk-in global row, business attribution for existing customers). Loyalty P0-G1 gate. |
| `expenses`: `payment_source`, `cash_session_id` (+ check: required when source = POS drawer) | Additive | Existing rows default to `Bank/Other` semantics; no historical session attribution. |
| Outbox events for sale/session/void/expense; inbox dedupe `(consumer, event_id)`; projection tables | Reuse existing outbox/inbox tables; new projection tables | Elasticsearch has only config/validators in the repo (no client package in any `.csproj`), so read-model storage is a decision (§K-9). |
| Monetary precision for `sales`/`sale_payments` | Possible type widening | Verify against max invoice value before changing. |

**Migration/rollback considerations (analysis only):** all changes above can be additive with nullable columns and backfill; the three risky operations are (1) unique `sale_number`, (2) unique normalized customer phone, (3) altering the cash-session unique index. Each requires a pre-check query run against a copy, a documented rollback (drop index/column), and approval. Per project rules, applying migrations to the persistent Development database requires explicit approval.

### D.3 Data-integrity findings tied to schema

1. `ux_cash_sessions_one_open_per_terminal` ignores any `overdue` status (none persisted today).
2. `Sale.TerminalId` SetNull/`PosTerminal → Outlet` Cascade can destroy historical associations.
3. No unique `sale_number` while numbers come from a process-local file counter.
4. Customer phone not unique, not tenant-scoped; `customer_code` unique globally across tenants (would collide for a second tenant's Walk-in if created normally).
5. Nullable `BusinessId` on `outlets` and `cash_sessions`; `Sale` has none.
6. **[CS+]** Type inconsistency: `cash_sessions.business_date` is `timestamp with time zone` (`20261004095137_InitialBaseline.cs`), `sales.business_date` is `date`. With `Npgsql.EnableLegacyTimestampBehavior=true` (`Program.cs` L31) `DateTime.Kind` is not enforced. (Mechanism verified, runtime effect host-time-zone dependent — N-3, §O.8.)
7. **[CS+]** `Product`, `ProductVariant`, `Category` have no `business_id`; `Customer` has none either (catalog tenancy undecided — N-4). No unit-of-measure column on `Product`.
8. **[CS+]** `sale_payments` stores `tendered` only when supplied; no `change_given`/`retained` column. Cash retained must be derived (`amount`), which is correct only while `amount` is capped at outstanding payable.
9. **[CS+]** `sale_voids`/`sales.status` use free-text status compared with `ToLower()` in predicates (`SaleVoidStore`), defeating any status index and permitting case variants; no check constraint on status values. **[V3]** Live database confirms: **no check constraints at all** on `sales`, `cash_sessions`, `sale_payments`, `expenses`, `customers`, `pos_terminals`; no triggers on `sales`/`cash_sessions` (only `trg_customers_protect_walkin` among POS tables).
10. **[V3]** Live-data findings that gate migrations: the 3 legacy seeded sales have **empty-string `sale_number`** (so a plain unique index on `sale_number` would fail — use backfill or a partial index `WHERE sale_number <> ''`), `terminal_id` set but `cash_session_id`/`business_date` NULL, and **no `sale_payments` rows** (legacy sales have no payment rows; the close calculator's fallback branch uses `Sale.PaymentMethod`/`TotalAmount`). `cash_sessions.business_id` and `outlets.business_id` are nullable in the schema (no NULL rows today).
11. **[V3]** Migration hygiene: the live `__EFMigrationsHistory` holds **61** migrations, all present on disk (`…Phase5DCalculationSnapshots` included; latest `20261023072233_PurchaseOrderOutletDestination`), and `dotnet ef migrations has-pending-model-changes` reports "No changes have been made to the model since the last migration" — the EF model, snapshot and Development database are consistent. However the migration files are **untracked in git** (HEAD still contains the previous migration set; the working tree holds a rebaselined set), so the repository history does not reproduce the applied schema.

---

## E. Frontend and UX Analysis

Compared: `FE/pages/pos/pos-screen.component.{ts,html,css}` and `FE/components/outlets/*`, `FE/pages/accounting/expense-form.component.*` against `iranimart_pos_prototype.html` (views: `pos`, `opening`, `sessions`, `sessiondetail`, `closing`) and Master SRS §4/§6/§8. Source comparison only; no rendered screenshots.

| Area | Prototype / SRS | Current | Gap |
|---|---|---|---|
| Route structure | Landing → Open page → POS → Close page → Summary | Single route `pos` (`FE/app.routes.ts` L416); open/close are modals inside the POS component | No `cash-sessions` routes/pages; correct breadcrumb (Sales › POS › Cash Sessions) not applicable yet. |
| Component size | Component tree in LLD §6 (header, cart pane, payment pane, modals) | One 63 KB `pos-screen.component.ts` (~1,900 lines) + 36 KB template | No separable `CompactSessionHeader`, `CartPane`, `PaymentPane`, `CustomerLoyaltyPanel`; state is signals inside one component. Refactor required to meet LLD state separation (`ActiveSessionState`, `CartDraftState`, …) — **sequence after P0 backend fixes**. |
| Header | Single compact card; cashier + salesperson; close icon + fullscreen; Outlet/Terminal/Sale No | Cash-open strip with Business Date + HH:mm:ss clock; header card with Outlet, Terminal select, Cashier, Sale Order No, Sales Order Date (permission), Item Count, Quantity Count | Remove Business Date/clock/counts; add salesperson, icon actions. |
| Search | One input, browse icon inside | Smart-search input + separate Browse button; products preloaded (200) | Server-side search; in-input icon. |
| Customer block | Search + Quick Add + loyalty card + Link Loyalty | Separate search field with Quick Add button, selected chip | Rich card/loyalty/link missing (loyalty-gated). |
| Left footer | Hold, Recall, Link Loyalty, Clear Cart | Hold/Recall in customer row; Clear in right footer | Re-home. |
| Right footer | Only Complete Sale (F9) | Clear, Hold, Complete Sale | Remove duplicates. |
| Tender | Single table; no tabs | Tender Details + Payment Breakdown block | Merge. |
| Fullscreen | Shell hidden, state kept | Absent | Add. |
| Keyboard | F2/F4/F6/F7/F9 | Implemented via `@HostListener('window:keydown')` (also Ctrl+1/2/3) | Verify focus/typing conflicts (not tested here). |
| Clock/timers | Server-offset based | `setInterval(1000)` client clock; session overdue/warning from client `now()`; unsubscribed on destroy via `subs` | Add server offset; warning thresholds 18h/19h. |
| Idempotency | Stable key per cart | New `crypto.randomUUID()` each click (L1459) | Fix with stable draft key. |
| Tax | From server | Single `taxRate` signal from settings; frontend computes `taxAmount` and posts it | Move to server calculation. |
| Walk-in | Seeded immutable record | Synthetic `id: 0` client object; omitted from payload | Per-business seeded id. |
| Design system | Use `UI_KIT` tokens (project UI rules) | POS CSS uses literal hex/px values (`#e5e7eb`, `#ffffff`, …) and legacy `.btn/.input` classes | **Verification Required** whether this page is within the "migrate whole screen" rule; any rebuild must follow `Docs/UI-Standardisation/DESIGN_SYSTEM.md` (tokens, `app-ui-dialog`, `app-ui-page-header`, etc.). |
| Outlet form | Terminals + Sales Targets sections | Name/Address/Contact/Manager only | Add sections; reuse shared UI kit. |
| Expense form | Payment Source + searchable selectors | Raw numeric "Payment Account ID" and "Outlet ID" inputs | Replace with permission-scoped selectors; add source + session selector. |
| Accessibility | Tooltips/aria-label on icon actions, text + colour variances | Not assessed for the POS page | **Verification Required** (axe/keyboard test). |
| Responsive/sticky | 1366×768 without scrolling to key actions | `.payment-lane` bounded flex with `.payment-scroll` + sticky footer (good foundation); media queries at 1280/820 px | Needs screenshot test at 1366×768 and 1920×1080. |

---

## F. Customer Loyalty Integration

### F.1 What exists today (Verified)
- `Customer.LoyaltyPoints` (int), `customers.loyalty_points`; `CustomersController` `POST {id}/add-points`, `POST {id}/redeem-points` (query-string `points`); `AddCustomerLoyaltyPointsCommand`, `RedeemCustomerLoyaltyPointsCommand`, `CustomerLoyaltyStore`.
- Legacy earning inside `CreateSaleCommand` (`floor(total)`) and reversal in `VoidSaleCommand`; `SaleCompletedNotice.earnedPoints` and `LoggingSaleEventPublisher` log "Points".
- Customer list/detail/form display the integer.
- Seed customers carry non-zero legacy balances (`DbSeeder.cs` L423–428).
- Tests lock the legacy behaviours (unit and HTTP characterization).

### F.2 What the Loyalty SRS/LLD require that is entirely absent
Programme/policy versions, memberships and consent, provisional claims, tiers, immutable ledger and earning batches (FEFO), reservations and OTP challenges, campaigns, adjustments (maker-checker), merges, fraud alerts, notifications, accounting reconciliation, 12 admin screens/routes, Loyalty tab on customer profile, loyalty reports. None of the ~20 proposed logical entities exist in `DbContext`.

### F.3 Ownership boundary (what belongs where)

| Concern | Owner | Notes |
|---|---|---|
| Points ledger, batches, FEFO, expiry, debt recovery, tiers, campaigns, OTP, reservations, reversals, loyalty accounting, statements, fraud | **Loyalty Module** | POS must not add a ledger or earning formula. |
| Phone identity and uniqueness (Customer master) | **Shared prerequisite** (Loyalty P0-G1, POS FR-CUST-01) | One normalization/uniqueness implementation; owned by Customer master. |
| Customer lookup UI, Quick Add, selecting beneficiary account, Link Loyalty control, masking presentation, sale-completed/receipt placement | **POS** | Consumes Loyalty queries only. |
| Calling preview/reserve/consume inside checkout, persisting `LoyaltyMembershipId` and eligible-net snapshots on the sale | **POS orchestration** + Loyalty ports | In-transaction (same DB). |
| Void/refund/exchange loyalty reconciliation | Loyalty reconciler invoked by Sales | Requires Returns/Exchange SRD (not in repo). |
| Disabling UI when programme paused/not live | POS reads Loyalty state | Not a "service offline" concept (see F.4). |

### F.4 Integration contracts that must exist before POS wiring (recommended; not duplicates)
In-process MediatR ports owned by Loyalty (Loyalty TR-14.1/14.2 mandate a modular monolith, not a remote service): membership lookup (masked), balance, checkout preview, OTP challenge, reservation create/release, consume-within-sale, earning-entitlement persistence, reversal hooks. POS stores only `LoyaltyMembershipId`, purchaser `CustomerId`, and the monetary allocation snapshot the Loyalty SRS lists in FR-05.4 (invoice/line tax basis, VAT included flag, discount and redemption allocations, eligible amount, awarded points, policy snapshot, `CompletedAtUtc`). **Do not recommend** a POS-side points table, a POS-side OTP store, or a second balance cache.

### F.5 Dependencies that block faithful loyalty integration
1. Customer master tenancy and normalized phone uniqueness (P0).
2. A Returns/Exchange engine with line-level snapshots (current refund flips whole-sale status).
3. Durable events (outbox) for earning entitlement and accounting messages.
4. Per-line VAT/discount allocation that reconciles (current VAT line allocation is inconsistent, B.10).
5. Cash-session/hold hooks to release reservations on Hold/cancel.
6. Decision on migration of legacy `LoyaltyPoints` balances (Loyalty D18 says no retroactive points; existing integers need a documented treatment — §K-8).

---

## G. Architecture and Data Integrity Risks

| ID | Area | Risk | Evidence (Verified) | Severity | Mitigation direction |
|---|---|---|---|---|---|
| G-1 | Payment/price integrity | Client-controlled `UnitPrice`, `DiscountAmount`, `Discount`, `Tax`, `CashierId`; negative `Discount`/`Tax` accepted; payments tolerance ±0.01; no method whitelist; no tendered ≥ amount check | `CreateSaleDto`, `CreateSaleItemDto`, `CreateSaleCommand` | **Critical** | Server recompute/validate; ignore client monetary authority. |
| G-2 | Session concurrency | Close not serialized with checkout/expense; summary computed before the close transaction; ReadCommitted; no lock/row version; PG tests mock `ISaleCashSessionValidator` so the race is untested | `CloseCashSessionCommand`, `CashSessionCloseStore` (ReadCommitted), `SaleCashSessionValidator`, `SaleCreationPostgresConcurrencyTests` harness | **Critical** | Locked session gate shared by sale/expense/close; recompute inside transaction; integration test without mocks. |
| G-3 | Cutoff | Boundary checked mid-transaction; side effects (audit, `_ = publish…`) execute before the transaction fails; unobserved fire-and-forget tasks | `SaleCashSessionValidator` | High | Final-gate recheck; move side effects after rollback decision. |
| G-4 | Accounting/cash | Expected cash double-subtracts voids/refunds; ignores expenses; attributes later-session void cash to the original session; partial refund subtracts all cash payments | `CashSessionCloseSummaryCalculator` | **Critical** | Canonical cash-effect classification (§H-13). |
| G-5 | Accounting policy | Variance journal posted automatically at close | `CashSessionVarianceJournalPoster` | Medium (policy) | Decision §K-2. |
| G-6 | Idempotency | FE new key per click; server loser → `DbUpdateException`; key optional; hold/recall not atomic | `pos-screen.component.ts` L1459; concurrency test | High | Stable key, mandatory, return original. |
| G-7 | Auditability/events | Audit for open/close and events written after commit; no outbox; fire-and-forget events lost on crash | `OpenCashSessionCommand`, `CloseCashSessionCommand`, `Program.cs` L906 | High | In-transaction outbox + audit per existing Audit v2 atomic scope. |
| G-8 | Tenant isolation | Unscoped customers; global Walk-in; nullable BusinessId on outlet/session; variant/product tenancy in lookup not verified | `CustomerReadStore`, `CustomerCreateStore`, `PosProductLookupStore` | **Critical** (customers) | Scope all customer queries; verify product tenancy (§J). |
| G-9 | Numbering | File-based invoice counter, not transactional (gaps on rollback), not multi-instance safe, no unique `sale_number` | `JsonSystemSettingsStore.AllocateNextInvoiceNumberAsync`; no index in `RetailPOSDbContext` Sale block | High | DB sequence per business/outlet + unique index. |
| G-10 | Tax | Line VAT sums exceed `Sale.Tax` with invoice discount; `TaxInclusive` hard-coded false; single global rate | `CreateSaleCommand` L170–190; `VatReportReadStore` | High | Correct allocation + tests; align with D07. |
| G-11 | Posting dates | Journal at actual time, stock ledger at backdated date, reports at effective date | `SaleCreationAccountingPoster` (uses `SaleDateTime ?? SaleDate`), `CreateSaleCommand` (stock ledger `saleDate`) | Medium | Define policy per FR-TIME-03. |
| G-12 | Security | Legacy points endpoints unprotected beyond `customers.view`; PII unmasked; session close/summary under `sales.create`; `UserOutletsController` 401 | `CustomersController`, `PosController` | High | Granular permissions; masking. |
| G-13 | Inventory | Only per-line optimistic check; held carts not reserved (by design); `Quantity` int; cache hint may be stale (display only) | Verified code | Low | Add oversell race test. |
| G-14 | Event processing | Outbox replay authorization is a disabled stub; no POS consumers; no projection rebuild | `IntegrationOutbox.cs` (`DisabledIntegrationReplayAuthorization`) | Medium | Required before projections. |
| G-15 | Reporting performance | VAT report loads all sales in memory; sales reports on transactional tables | `VatReportReadStore` L190–215 | Medium | Projections (later). |
| G-16 | Time | Client-supplied business date; UTC date vs outlet local date; client clock for expiry display | `OpenCashSessionCommand`, `pos-screen.component.ts` | High | Server-derived with outlet timezone. |
| G-17 | Returns | Partial refund marks whole sale `refunded`; void/refund cash effects not session-classified | `RefundSaleCommand` | Medium | Returns SRD dependency. |
| G-18 | Audit gaps | Backdate audit swallowed; receipt audit post-commit | `SalesController.TryRecordBackdatedSaleAuditAsync` | Low | Accept or move in-transaction. |
| G-19 **[CS+]** | Provisioning | No code path creates terminals for onboarded businesses; POS unusable until Outlet-embedded terminals exist | `BusinessOnboardingWriteStore.cs` (no `PosTerminal`), `DbSeeder.cs` (opt-in seed) | **Critical (go-live)** | Deliver terminal management first (Step 3); decide whether onboarding creates a default terminal. |
| G-20 **[CS+]** | Payment evidence | Tendered/change not persisted on default checkout; receipts show change 0 | `CreateSaleCommand` payment branch; `pos-screen.component.ts` payload | High | Persist/validate tender; add regression test. |
| G-21 **[CS+]** | AuthZ | `OriginalSaleId` links/mutates any sale; held-sale cashier from client; by-cashier list | `CreateSaleCommand`, `HeldSalesController` | High / Medium | Authorize and derive from claims. |
| G-22 **[CS+]** | Time/schema | `cash_sessions.business_date` timestamptz vs `sales.business_date` date; legacy timestamp switch; Local-kind `OpenedAt` compared with `UtcNow` | Migration `InitialBaseline`, `Program.cs` L31, `SaleCashSessionValidator`, `CashSessionQueries` | **[V3]** High — mechanism verified, manifestation host-time-zone dependent, not executed | PostgreSQL integration test with process TZ = `Asia/Dhaka` and `UTC`; normalise at the store boundary. |
| G-23 **[CS+]** | Coupling | Sale/close fail when posting accounts/period unavailable (journals join the ambient transaction and throw) | `ManualJournalDraftStore` (L173/323/881 ambient txn), `CashSessionVarianceJournalPoster`, test `CloseCommand_DoesNotPersistClosedSession_WhenVariancePostingFails` | High | Decide: keep synchronous GL (validate config at open/onboarding) vs deferred idempotent posting. |
| G-24 **[CS+]** | Catalog isolation | No `business_id` on products/variants/categories (live schema confirmed); intent undocumented/contradictory | `DOM/Product.cs`, `PosProductLookupStore`, `CreateSaleCommand` | **[V3]** Medium — owner decision; checkout is partly protected by the outlet-inventory-row requirement | Decide shared vs tenant catalog (§O.14-D6); add cross-tenant lookup/sale tests. |
| G-25 **[CS+]** | Replay integrity | Idempotency replay not payload-bound; loser burns invoice number | `FindByIdempotencyKeyAsync`, allocator | Medium | Request hash + DB sequence. |
| G-26 **[CS+]** | Expense control | No separation of duties: `ApproveAsync` never compares actor with creator/submitter; owner wildcard `*`; legacy `expenses.edit` aliases approve/reject | `PermissionCatalog.cs` L76–82, `ExpenseCommandStore.ApproveAsync`, `AccountingExpensesController` L232–273 | **[V3]** Medium — verified gap, owner decision | Require SoD for POS Drawer / Petty Cash sources (§O.14-D7); add test. |
| G-27 **[CS+]** | Test blind spot | Existing PG concurrency tests mock `ISaleCashSessionValidator`; no unmocked sale-vs-close, oversell, or expense-vs-close tests; cash-session service/HTTP tests use the EF **InMemory** provider (no locks, no unique-index races, no Npgsql timestamp behavior). **[V3]** `PostgresHarness.CreateAsync` falls back to the **Development** server (`127.0.0.1:5435`, `retailpos_db`) when `RETAILPOS_TEST_POSTGRES` is unset, and builds its schema with `EnsureCreatedAsync` (not the migrations, so triggers/SQL in migrations such as `trg_customers_protect_walkin` are not exercised) | `SaleCreationPostgresConcurrencyTests` harness L1745–1784, `PostgresTestDatabase.cs`, `CashSessionServiceTests` L589, `RetailPosApiFactory` L46 | Medium | See §N.6 and §O.10; set `RETAILPOS_TEST_POSTGRES` to the Test stack (55432) before running any PG test; remove the Development fallback. |

---

## H. SRS/LLD Corrections Recommended (no edits made)

**H-1. Document identity.** SRS header says "Version 1.0" and companion `IRANIMART_POS_MASTER_LLD.md`; repository files are `…_V1_1.md`. Both reference `iranimart_pos_prototype_v3.html`; the repository file is `Docs/08_Sales_And_POS/iranimart_pos_prototype.html`. Align names/versions.

**H-2. LLD §1/§16 "paths claimed, not verified".** Verification results: `SalesController.cs`, `PosController.cs`, `GetPosStockHintQuery.cs`, `pos-screen.component.{ts,html}`, `PermissionCatalog` (`APP/Identity/Permissions`), `AuditService` (`INF/Audit`) exist. **`SaleService.cs` and `SettingsService` do not**: checkout is `APP/Sales/Commands/CreateSaleCommand.cs` (+ `API/Services/SaleCreationAdapters.cs`, `INF/Sales/SaleCreationStore.cs`); settings are `INF/Settings/JsonSystemSettingsStore.cs`; stock ledger is `INF/Inventory/StockLedgerWriter.cs`. Replace claims with verified paths.

**H-3. Loyalty is a module, not a "deployed service".** SRS §2.2/§2.3/§16.3 and LLD §9 ("Loyalty API", feature flags, "network failure after checkout", "offline service") assume a remote service. Loyalty SRS TR-14.1/14.2 and FR-10.2 require an in-process modular monolith with **atomic** sale + redemption in one DB transaction (only external *payment* is non-atomic, P0-G2). Rewrite POS-side language as in-process MediatR ports, replace "service offline" with programme-state/paused/disabled states, and state that the loyalty documents now exist in the repository (SRS says they were not uploaded).

**H-4. Earning basis wording.** SRS says earning is "based on eligible payable after VAT/discount". Loyalty FR-05.1 defines **eligible net product consideration** (VAT removed once, discounts, redemption, non-product charges excluded; never "payable"). Use the Loyalty term; add the AT01 example as a POS cross-test. Note repository `TotalAmount` is VAT-**exclusive subtotal − discount + VAT**, so "payable" ≠ eligible.

**H-5. Date/time glossary.** Map blueprint names to repository fields and Loyalty names:

| Blueprint | Loyalty SRS | Repository today |
|---|---|---|
| `CreatedAtUtc` | `CompletedAtUtc` | `Sale.CreatedAt` (and `Sale.SaleDateTime`) |
| Effective `SaleDateTime` | `BusinessSaleDate` | `Sale.SaleDate` |
| `BusinessDate` | — | `Sale.BusinessDate` (session date) |

State which repository field is renamed vs interpreted (recommend *interpret*, do not rename — LLD §15 "do not regenerate existing tables").

**H-6. Entity naming.** LLD §3.1 field names differ from the repo: `CashierUserId`→`CashierId`; `CountedCash`/`ExpectedCashSnapshot`/`Variance`→`ActualClosingCashAmount`/`ExpectedClosingCashAmount`/`DifferenceAmount`; `IsSystemWalkIn`→`IsSystem` + `CustomerCode='WALKIN'`; `OpenedAtUtc`→`OpenedAt`. Add a mapping table so implementers extend rather than duplicate.

**H-7. Overdue state.** SRS FR-CS-04 lists `Overdue` as a state; LLD §3.1 index says `Open,Overdue`; LLD §5.3 says "derived … marked idempotently". The code derives it. Decide: *derived only* (then remove `Overdue` from the partial-index text) or *persisted* (then specify the transition job, event, index and who may close).

**H-8. Commit-gate semantics.** LLD §5.2 pseudocode recheck uses application `clock.UtcNow` before commit; the blueprint wants "strictly before opening + 20h at commit". Specify the mechanism (row-locked session read + database-time comparison immediately before commit) and the accepted residual window; note that PostgreSQL Read Committed gives no guarantee without the lock. Also specify close-vs-sale serialization primitive (row lock vs advisory lock vs Serializable) and the expense path.

**H-9. Drawer-effect rules for void/refund (FR-CLOSE-02 ambiguity).** "NetCashRefundsOrApplicableVoidCashReversals" does not say **which session** bears a void/refund performed after the original session closed, nor how a same-session void nets to zero (the current code subtracts it). Define a `CashDrawerEffect` classification per payment fact with an effect-session rule, and whether voiding a cash sale from a closed session requires an open session for the refund.

**H-10. Cash-difference accounting.** FR-CLOSE-03 says "never silently mutate ledger"; the repository posts a system cash-difference journal at close. Add an explicit rule: either prohibit it (variance stays a session fact) or allow a documented journal to the configured Cash Difference accounts. The user must choose (§K-2).

**H-11. Terminals.** SRS silent on `IsDefault`. Add a rule (remove vs retain-as-preselect only) and remove server fallback to "first/default terminal" from checkout. Also state whether seeded per-outlet terminals (`DbSeeder`, `database/seed-data.sql`) may exist alongside outlet-managed terminals.

**H-12. Target metric.** Define the single authoritative "net sales" metric (excluding VAT, after discounts, net of voids/returns), its source (transactional vs projection), version and freshness fallback; existing reports use VAT-inclusive `TotalAmount` filtered by `SaleDate`.

**H-13. Expense "paid/posted".** The repository state machine ends at `Approved` (journal posted at approval); there is no Paid state or disbursement time. Specify which event makes a drawer expense "posted" (approval? a new payment step?), which session it binds to if approval happens after the original session closed or while no session is open, and how reversal on Cancel affects an already-closed session (compensating workflow). Also specify how POS Drawer and Petty Cash map to chart accounts (`PostingAccountSlots.Cash` vs a new Petty Cash slot).

**H-14. Event envelope.** LLD §11.1 envelope (`eventId,type,schemaVersion,aggregateId,aggregateVersion,…`) differs from the existing `IntegrationEnvelope` (`MessageId, BusinessId, Producer, MessageType, SchemaVersion, AggregateType, AggregateId, CorrelationId, CausationId, OccurredAtUtc, PayloadJson, PayloadHash, EnvelopeHash, SourceVersion`). Adopt the existing contract and state the producer name and message types. Note the outbox requires an active atomic audit scope (`RequireTransaction`) — POS commands must run inside it.

**H-15. Elasticsearch.** LLD §11.2 says to use the existing Elasticsearch architecture "if repository infrastructure provides it". Repository evidence: Elasticsearch appears in docker compose, `InfrastructureConfigurationValidator`, `appsettings*.json` and tests, but **no Elasticsearch client package** is referenced in any `.csproj`. State PostgreSQL read tables as the baseline projection store or request approval for a client package.

**H-16. Settings storage.** SRS FR-TIME-01 says "configurable per business"; repository setting is a single JSON file. Specify the business-scoped store and migration of the existing value (and whether `Pos.MaxCashSessionDurationHours` JSON key is retired).

**H-17. Idempotency contract.** LLD §3.1 proposes `(BusinessId, OutletId, ClientTransactionKey)`; the repo has optional `(OutletId, IdempotencyKey)`. Specify: mandatory, stable per cart, replays return the original committed result (including receipt link), key scope, and expiry.

**H-18. Integration gaps in the blueprint.**
- No contract for **Hold ↔ loyalty reservation release** (Loyalty D27) — held carts are JSON soft-state.
- No contract for **per-line tax/discount allocation snapshot** that Loyalty FR-05.4 needs (POS must supply deterministic line allocations).
- No statement on **backdated sale + loyalty** (Loyalty D38: actual completion timestamp governs).
- No definition of **`sales.void` ↔ loyalty reversal** and ↔ session cash effect.
- Receipt content for loyalty (Loyalty FR-10.4: masked phone, tier, earned, redeemed, balance, pending) vs SRS FR-RCPT-02 "only when confirmed" — reconcile with "pending" status.
- Phone uniqueness is stated "within a business" but `Customer` has no business; add a **customer tenancy** requirement and the duplicate-remediation gate (Loyalty P0-G1) as a prerequisite in the POS SRS.
- Non-Bangladesh phones: SRS says BD normalization; seed data uses `+1-555-…`.
- Permission list lacks concrete names; propose `cash_sessions.view/open/close`, `cash_sessions.attachments`, `outlets.terminals.manage`, `outlets.targets.view/edit`, `reports.pos.rebuild` (names for approval; legacy alias `pos.sale.backdate` exists).

**H-19. Internal contradictions/ambiguities.**
- FR-POS-01 keeps a permission-only Sale Date but says the newest prototype omits it ("resolve at UI acceptance") — decide now; backend permission already exists.
- FR-CS-01 mentions "approved device binding" but no binding model is defined.
- §2 D06 says no shared-cashier banner while §8 AC-05 requires shared cashiers visibly attributed on sale-complete — clarify where actual cashier is displayed.
- SRS §13 NFR "cache-hit lookup <100 ms" is already met by design (Redis) but not measured.
- FR-TARGET-04 says projections "may lag" while §16.5 says closing never uses projections; define whether the landing target card may use authoritative fallback under load.

---

## I. Recommended Implementation Order (recommendation only — not authorization)

Dependency-based; each step requires the approvals noted in §H/§K.

| Step | Scope | Depends on | Notes |
|---|---|---|---|
| 0 | **Blueprint finalization**: amend SRS/LLD per §H; answer §K questions; approve migrations plan | — | Gate for everything. |
| 1 | **P0 hardening of existing checkout** (no new features): server-derived cashier, server re-pricing/VAT/discount validators, discount exclusivity, payment validation, stable mandatory idempotency (FE + server replay), guard/permission legacy `add-points/redeem-points`, customer search tenant scoping and masking **[CS+] plus: persist tendered/change (N-2), authorize `OriginalSaleId` (N-1), derive held-sale cashier (N-6), payload-bound idempotency (N-8), fix per-line VAT allocation** | 0 | Keeps current behaviour for valid requests; add tests first (characterization exists). |
| 2 | **Foundations**: business-level POS settings (20h) and outlet timezone; `sale_number` uniqueness + DB-backed numbering; customer `business_id`/normalized phone with data-quality diagnostics (Loyalty P0-G1); per-business Walk-in | 0, 1 | Migration approvals; Development DB changes need explicit approval. |
| 3 | **Terminals + Outlet UI**: terminal child model inside Outlet create/edit, eligibility endpoint, explicit terminal selection, FK/delete-behavior fix **[CS+] — also the go-live unblocker for new businesses (G-19); consider moving it ahead of Step 2's non-terminal items** | 2 | Replaces silent default/fallback. |
| 4 | **Cash-session lifecycle**: server-derived `BusinessDate`, locked session gate shared by sale/close (and expense later), persisted-or-derived Overdue decision, row version, granular permissions, list/detail endpoints, 20h/18h/19h UX, revalidation on resume | 2, 3 | Includes unmocked PG race tests. |
| 5 | **Salesperson attribution** (schema, eligible-user endpoint, POS selector, receipt, audit) | 2 | Independent of 4 after schema. |
| 6 | **Expense integration**: Payment Source, session link, searchable selectors, approval/post rule, reversal rule | 4, §H-13 decisions | Needed by close reconciliation. |
| 7 | **Close reconciliation**: rewrite expected-cash calculator on classified facts, handovers, denominations, attachments, immutable snapshot, summary page/print/PDF, cash-difference policy | 4, 6 | Highest accounting risk; needs accountant sign-off. |
| 8 | **Durable events**: POS producers on the existing outbox (SaleCompleted/Voided, CashSession*, Expense drawer) inside transactions; replace `LoggingSaleEventPublisher` call sites | 4 | Prerequisite for projections and loyalty entitlement. |
| 9 | **Sales targets** (config → calculator → landing cards using authoritative query first) | 3, 4 | Projections later. |
| 10 | **POS UI refit** per prototype (landing/open/close pages, sticky footers, fullscreen, single search, tender table, header) following the project UI design system; split the 1,900-line component along LLD §6 | 4, 5, 7 | Do after back-end contracts stabilize. |
| 11 | **Reporting projections + rebuild + replay authorization**, BusinessDate re-basing of sales/VAT reports | 8 | Idempotent consumers with inbox. |
| 12 | **Loyalty integration through Loyalty-owned ports** (read/display → earning entitlement → OTP redemption last); retire legacy earning/endpoints in lock-step with Loyalty go-live | Loyalty Module Phases, 1, 2, 8 | Do not start before Loyalty P0 gates (identity, payment consistency, accounting validation). |
| 13 | **Receipt immutability/print provider** and receipt loyalty fields | 5, 12 | Can be earlier for salesperson. |
| 14 | **Cross-cutting tests**: PG concurrency (oversell, close-vs-sale, expense-vs-close), E2E at 1366×768/1920×1080, performance budgets | all | Continuous. |

---

## J. Verified Facts vs Assumptions; Areas Not Verified

### J.1 Verified facts (direct code reading)
Everything labelled **Verified** above, notably: 15h defaults (4 code locations + settings JSON), client-supplied `BusinessDate`/`CashierId`/`UnitPrice`/`Tax`, unlocked session validator and close, calculator formula, absent terminal/target/handover/salesperson/PaymentSource entities, absent customer tenancy/phone constraint, legacy loyalty endpoints/earning, JSON-file invoice numbering, existing outbox/inbox stack unused by POS, `LoggingSaleEventPublisher` binding, test inventory (unit + PG integration), FE header/footers/Payment Breakdown block, absent fullscreen/loyalty UI/salesperson.

### J.2 Assumptions (clearly marked)
- A1: **[CS-fix] Resolved.** `DbSeeder` terminal seeding is opt-in (`Database:SeedOnStartup`) and rejected outside development (`Program.cs` ~L1165–1201); onboarding does not create terminals (see FR-TERM-01).
- A2: Frontend `PosService` calls shown are the only client of the cash-session API (no mobile client use checked).
- A3: Company timezone `Asia/Dhaka` in settings is the intended outlet/business timezone.
- A4: Product-level `TaxRate` is intended for per-line VAT (not confirmed in tax settings design).

### J.3 Not verified / Verification Required
- ~~Pricing-rule pipeline internals and whether any server-side price check occurs elsewhere.~~ **[V3] Resolved** — pipeline read in full; checkout never calls it (§O.4).
- ~~Product/variant tenancy.~~ **[V3]** Structure verified in code and live schema; **intent remains an owner decision** (§O.6, D6).
- ~~`UserOutletAccess` internals / owner / role-switch behavior.~~ **[V3] Resolved** by code reading and existing test names (§O.5); the `DATA_ISOLATION.md` "legacy no-BusinessId fallback" is stale.
- Receipt PDF visual output; whether any other consumer reads `SaleItem.VatAmount` besides the VAT report. **[V3]** Audit v2 semantics resolved (§O.10-F): sale/void audit rows are written through the same `DbContext` inside the sale transaction (non-"required-scope" path), so an outbox producer must use `IntegrationOutbox.AppendSourceChangeAsync` or wrap the command in `IRequiredAuditTransaction`.
- ~~UOM/fractional quantities scope.~~ **[CS-fix]** Resolved: no UOM exists in the data model (FR-POS-03).
- ~~Runtime behavior of `cash_sessions.business_date`~~ → **[V3]** mechanism verified (§O.8); **execution under a non-UTC host zone still outstanding**. ~~Expense approval maker-checker~~ **[V3] Resolved** (§O.7). ~~Owner/role-switch POS outlet~~ **[V3] Resolved** (§O.5).
- Accessibility and 1366×768 behaviour of the existing POS page.
- ~~Live database state.~~ **[V3]** Development database inspected read-only (§O.3): 3 legacy sales (empty `sale_number`, no payments, no session), 0 cash sessions, 2 terminals, 1 business, 6 customers (no duplicate phones), 1 pending outbox row (none POS-related). **Production data quality remains unknown.**
- ~~Whether regression-free (AC-18)~~ — **[V3]** 541 non-database unit tests (`Pos` + `Customers`) pass; the PostgreSQL-backed and API-factory integration suites were **not run** (§O.2).

---

## K. Unresolved Technical Questions (for the user/architect)

1. **Void/refund cash effect:** which session bears the cash effect when a sale is voided after its session closed, and does voiding a cash sale require an open session? (§H-9)
2. **Cash difference at close:** keep the automatic Dr/Cr journal to Cash Difference Gain/Loss accounts, or stop and keep variance as a session fact only? (§H-10)
3. **20-hour setting location:** which business-scoped store, and what is the migration of the current global value (currently 15)? Is a per-business override allowed to be lower than 20?
4. **Overdue persistence:** derived-only or persisted state with a transition job/event? (§H-7)
5. **`IsDefault` terminal flag:** remove, or keep as preselect only? What happens to checkout when `TerminalId` is omitted (currently falls back)?
6. **Posting date policy for backdated sales:** journal, stock ledger, VAT and reports currently use different dates. Which governs? (FR-TIME-03)
7. **Expense "posted" event and session binding** when approval occurs after the session closed or when no session is open; Petty Cash chart-account mapping. (§H-13)
8. **Legacy loyalty balances** (`customers.loyalty_points`, seed non-zero): retain, freeze, migrate with consent, or zero at Loyalty go-live? (Loyalty D18)
9. **Read-model storage for POS projections:** PostgreSQL tables vs adding an Elasticsearch client (not currently referenced in any project file). (§H-15)
10. **Customer tenancy backfill:** how are existing customers attributed to a business, and what is the policy for duplicates/non-BD phones before the unique index? (Loyalty P0-G1)
11. **Invoice numbering model:** per business, per outlet, or per terminal; format changes; handling of existing file counter values.
12. **Inclusive vs exclusive VAT:** is `TaxInclusive` ever true for IraniMart? Which tax rate source is authoritative (global setting, product `TaxRate`, or tax-component table)?
13. **Permission names and default role mapping** for cash-session, terminals, targets, attachments, projection rebuild (§H-18).
14. **Returns/Exchange SRD timeline:** the partial-refund semantics block faithful loyalty reversal (Loyalty AT08/AT19) and cash-session void/refund effects.
15. **Business Owner/Super Admin sale outlets:** may they sell at any outlet, and how does that interact with session ownership and shared cashier access?
16. **Sale Date placement** on the POS screen (§H-19).
17. **Receipt snapshot policy:** store payload vs rebuild deterministically; retention.
18. **Existing tests that assert 15h and cash_in/out vocabulary:** approved to be rewritten when the blueprint is final?
19. **Salesperson eligibility rule:** any active user assigned to the outlet (`user_outlet_assignments`), only users holding the existing `SalesPerson` role, or an explicit flag? Should the session opener be the default salesperson only when eligible (SRS §3)?
20. **[CS+] Terminal provisioning for new businesses:** should onboarding auto-create a default terminal (e.g. "Counter 1") in addition to the Outlet-embedded editor, or must an admin add one before POS is usable?
21. **[CS+] Accounting coupling:** should checkout and session close remain synchronously dependent on GL posting accounts/open period (current), or move to validated-at-setup + deferred idempotent posting? (Affects P0 fixes and Loyalty FR-13.2 expectations.)
22. **[CS+] Catalog tenancy:** is the product/variant/category catalog shared across businesses by design, or must it become tenant-owned (`business_id`)?
23. **[CS+] Tender persistence:** is cash tendered/change a required persisted fact for every cash payment (recommended), and does it belong on `sale_payments` or a separate drawer-movement fact?
24. **[CS+] `business_date` type:** accept migrating `cash_sessions.business_date` to `date` (matching `sales.business_date`), and what is the authoritative timezone source (business vs outlet vs the global `Company.TimeZone`)?
25. **[CS+] Exchange semantics:** is `Sale.OriginalSaleId`/`ReplacementSaleId` kept as the exchange link until the Returns/Exchange SRD ships, and who may link (permission, same outlet only)?
26. **[V3] Expense separation of duties:** may a Business Owner/`AccountsAdmin` create, submit and approve the same expense, and must POS Drawer / Petty Cash expenses require a different approver? (§O.7, D7)

---

## L. Required Updates to the Master SRS and LLD (checklist)

| # | Document | Section(s) | Update |
|---|---|---|---|
| 1 | SRS/LLD | headers | Fix versions, companion filenames, prototype filename (§H-1). |
| 2 | LLD | §1, §16 | Replace "unverified baseline paths" with the verified map (§H-2). |
| 3 | SRS/LLD | §2.2–2.3, §9, §16.3, §18.3 | Loyalty as in-process module with atomic checkout; remove "service offline" semantics (§H-3). |
| 4 | SRS | §9 FR-LOY-UI-02, §16.3 | Use "eligible net product consideration" (§H-4). |
| 5 | SRS/LLD | §7 FR-TIME-03, LLD §3.1 | Date-field glossary and posting-date policy (§H-5, §K-6). |
| 6 | LLD | §3.1 | Entity/field mapping to repository names (§H-6). |
| 7 | SRS/LLD | FR-CS-04, LLD §3.1/§5.3 | Overdue state decision and index text (§H-7). |
| 8 | LLD | §5.2, §18.2 | Commit-gate and serialization primitive (§H-8). |
| 9 | SRS/LLD | FR-CLOSE-02, LLD §5.4 | Drawer-effect rules for void/refund/expense (§H-9, §H-13). |
| 10 | SRS | FR-CLOSE-03 | Cash-difference accounting rule (§H-10). |
| 11 | SRS | FR-TERM-* | `IsDefault`, server fallback removal, seeded terminals (§H-11). |
| 12 | SRS | FR-TARGET-04, §16.5 | Metric definition/source/freshness (§H-12). |
| 13 | SRS/LLD | FR-EXP-01, LLD §5.5 | Expense paid/posted definition and session binding (§H-13). |
| 14 | LLD | §11 | Adopt existing `IntegrationEnvelope`; mention atomic audit-scope requirement (§H-14). |
| 15 | LLD | §11.2 | Projection store decision; Elasticsearch client absent (§H-15). |
| 16 | SRS | FR-TIME-01 | Business-scoped settings store (§H-16). |
| 17 | SRS/LLD | FR-SALE-02, LLD §3.1 | Idempotency contract (§H-17). |
| 18 | SRS | §9, §13 | Customer tenancy prerequisite; permission names; Hold↔reservation; allocation snapshot; receipt loyalty fields (§H-18). |
| 19 | SRS | FR-POS-01, FR-CS-01 | Resolve Sale Date, "device binding" (§H-19). |
| 20 | SRS | §14 | Add acceptance tests: client cannot set cashier/price/tax; stable idempotent retry returns original; close-vs-sale race; voided same-session sale nets to zero; VAT line allocation reconciles; customer tenant isolation; legacy points endpoints guarded. |
| 21 **[CS+]** | SRS | §5.1 FR-TERM-02 | State that a business cannot sell until ≥1 active terminal exists and define the provisioning path (onboarding default vs Outlet editor). |
| 22 **[CS+]** | SRS | FR-POS-03 | Either add unit-of-measure/decimal quantity requirements or state integer quantities only (no UOM in the data model). |
| 23 **[CS+]** | SRS/LLD | FR-POS-06, FR-RCPT-02, LLD §10 | Require persisted cash tendered/change per cash payment and server validation (`Tendered ≥ Amount`). |
| 24 **[CS+]** | LLD | §3.1 | Specify `BusinessDate` as SQL `date` and document the timestamp-kind rule (the repo enables Npgsql legacy timestamps). |
| 25 **[CS+]** | SRS/LLD | §13, §18.6 | Add authorization requirements for exchange/replacement linking and held-sale ownership (actor from claims only). |
| 26 **[CS+]** | LLD | §5.2, §5.4 | State the accounting-coupling policy (§K-21) and that session close must remain possible when GL posting is unavailable. |
| 27 **[CS+]** | SRS | §13 | Add catalog tenancy statement (shared vs tenant-owned) and extend "multi-business isolation" acceptance to catalog, customers, held sales and replacement links. |

---

## N. Claude Sonnet Second-Pass Review — Comparison and Implementation Readiness

### N.1 Scope and caveat
See §0.1. The reviewer was not fully independent of the first revision (same session). The comparison below is therefore a **verification and gap-hunt** against repository evidence, not a blind duplicate analysis. No source, SRS, LLD, migration or data was modified.

### N.2 Comparison with Rev 1

**1. Rev 1 findings confirmed against code (kept unchanged).** Client-trusted price/discount/VAT/`CashierId`; unscoped customers and unguarded legacy loyalty endpoints; unlocked close vs checkout; expected-cash double subtraction and legacy `cash_in/out`; 15h defaults and global JSON setting; client-supplied `BusinessDate`; no terminal/target/handover/salesperson/PaymentSource entities; outbox/inbox exists but is unused by POS; JSON-file invoice numbering without unique `sale_number`; per-line VAT inconsistency; FE footers/tabs/header vs prototype; whole-sale refund status; receipt not an immutable snapshot.

**2. Additional gaps found by this review** (all now in the report): N-1 exchange-link authorization; N-2 tendered/change lost on default checkout; N-3 `business_date` type + legacy timestamp switch (VR); N-4 catalog tenancy (VR); N-5 expense approval separation (VR); N-6 held-sale actor trust; N-7 state-changing receipt GET; N-8 replay not payload-bound; N-9 owner/role-switch POS outlet (VR); production terminal provisioning gap (FR-TERM-01 evidence + G-19); accounting coupling of checkout/close (FR-CLOSE-01/-03 evidence, G-23, K-21); refund/void loyalty asymmetry (B.9); held-sale `cashier/{id}` listing; two-timestamp close window; DB schema notes D.3 #6–#9.

**3. Rev 1 statements corrected.** (a) "Tendered stored" for payments — only on the split path (FR-SALE-02 payments row, **[CS-fix]**). (b) FR-RCPT-02 Partial → Conflicting. (c) FR-POS-03 UOM "Verification Required" → data-model Missing. (d) A1 (seeding environments) resolved. (e) "Tests assert 15h" — tests mock/inject the value; only a few default-asserting tests need changes. (f) Readiness lowered from 20–30% to 20–25%; roll-up now 74 rows (B.15).

**4. Missing architectural risks/dependencies added.** Production terminal provisioning; accounting-state coupling; catalog tenancy; `DateTime.Kind`/legacy timestamp dependency of 20h logic; replay integrity; exchange link authorization; Audit v2 atomic-scope prerequisite for any outbox producer (the outbox throws unless a transaction and a "required audit" scope are active — verify how `CreateSaleCommand`'s scope is established before adding producers).

**5. Missing SRS/LLD corrections added.** §L rows 21–27 (terminal provisioning, UOM, tender persistence, `BusinessDate` as `date` + timestamp rule, exchange/held-sale authorization, accounting-coupling policy, catalog tenancy); §K-20…K-25.

**6. Missing implementation/regression-test considerations added.** §N.6.

### N.3 Existing code and services to reuse (do not rebuild)

| Capability | Reuse |
|---|---|
| Sale transaction pipeline | `CreateSaleCommandHandler` + `ISaleCreationStore`, `SaleCreationAccountingPoster` (system journal via `CreateOrGetSystemJournalDraftCommand`/`PostJournalEntryCommand`), `StockLedgerWriter`, xmin inventory token |
| Outlet authorization | `IUserOutletAccessService`/`UserOutletAccess` (`EnforceWriteOutletAsync`, `ResolveAndAuthorizeLocationAsync`), `UserOutletAssignment` (eligible salesperson source) |
| Void/refund | `VoidSaleCommand` atomic status gate (`TryMarkCompletedSaleVoidedAsync`) — pattern for close/handover gates |
| Cash session | `CashSession`, `ux_cash_sessions_one_open_per_terminal`, `CashSessionOpenStore` unique-violation handling, existing audit writers |
| Outlets | `IOutletWriteStore` + `OperationalLocationSourcePersistence` (already atomically appends outbox source events) |
| Eventing | `IntegrationOutbox`/`OutboxDispatcher` (lease + SKIP LOCKED + DLQ), `IntegrationInbox`, `RabbitMqDelivery`, `IntegrationEnvelope` |
| Audit | Audit v2 (`IAuditService`, `AuditEvents*`), `RequiredAuditOperation` scope |
| Approvals | HR approval kernel / expense approval adapters for maker-checker |
| Files | Journal attachment storage options, `ConfidentialFileService` patterns |
| Receipts | `Generate…ReceiptDocument/Pdf` commands, QuestPDF renderer, `ReceiptPrintHistory`, `RegisterSaleReceiptAttempt` |
| Settings | `PostingAccountSlots` (Cash, CashDifferenceGain/Loss) for drawer/petty-cash mapping; `BusinessFeatureSetting` as a possible business-scoped settings home |
| Frontend | Existing signals-based `PosScreenComponent` logic (shortcuts, discount-switch confirmation, receipt flow), `UI_KIT` shared components per project design rules |

### N.4 Critical architectural problems (consolidated)
1. Server trusts client for money, actor and links (G-1, N-1, N-6). 2. Session close/checkout/expense not serialized (G-2/G-3). 3. Cash expected formula wrong (G-4). 4. Tenancy gaps: customers, catalog (G-8, G-24). 5. No durable POS events (G-7, G-14). 6. Global file settings/numbering (G-9, 20h). 7. Terminal provisioning absent (G-19). 8. Tender/change not persisted (G-20). 9. Synchronous accounting coupling (G-23). 10. Time/type semantics (G-16, G-22).

### N.5 Database migration requirements (analysis only — none generated)
Ordered by dependency (each needs a pre-check query on a copy and explicit approval; Development DB changes need approval per project rules): (1) `sales.sale_number` uniqueness + DB sequence; (2) `customers` `business_id`/`normalized_phone` + filtered unique index after remediation; (3) `pos_terminals` description/normalized code/concurrency + FK delete behavior changes (`sales.terminal_id`, receipt history) ; (4) business-level POS settings (20h) and timezone; (5) `cash_sessions` row version, close snapshot, `business_date` → `date`, optional overdue/index change; (6) `sales.salesperson_user_id`, tender/tendered columns; (7) handover/attachment/denomination tables; (8) target version/override tables; (9) `expenses.payment_source`/`cash_session_id` with check constraint; (10) projection tables + inbox dedupe (reusing existing outbox/inbox); (11) loyalty tables belong to the Loyalty module. Rollback for each: drop new index/column/table; none may fabricate historical terminal/session/salesperson values.

**Integration dependencies:** Loyalty P0 gates (identity, payment consistency, accounting validation) before POS wiring; Returns/Exchange SRD before loyalty reversal; Audit v2 required scope before outbox producers; accounting configuration (Cash Difference slots, Petty Cash slot) before drawer/close work; Elasticsearch client approval only if used (none referenced in any `.csproj`).

### N.6 Concurrency, transaction and missing test coverage

Existing coverage (verified by name): `SaleCreationPostgresConcurrencyTests` (journal create-or-get, supplier flows, concurrent session open, same-key sale, concurrent void, concurrent refund — with a **mocked** `ISaleCashSessionValidator`), `CashSessionServiceTests`, `PosCashSessionHttpTests`, `SaleVoidCommandTests`, customer loyalty characterization tests, unit tests for open/close/query handlers, `pos-screen.component.spec.ts`.

Missing tests to add before/with each change:
- **P0 regression set (write first, against current behavior where applicable):** client-supplied `CashierId`/`UnitPrice`/`Tax`/negative discount rejected; line+invoice discount rejected; VAT lines sum to `Sale.Tax` with invoice discount; tender/change persisted for single-method cash; `OriginalSaleId` of another outlet/tenant rejected; customer search/create isolation across two businesses; legacy points endpoints denied without permission.
- **Unmocked PostgreSQL concurrency:** sale vs close (no sale committed after close; expected cash includes it or sale rejected), sale at exactly `OpenedAt+20h` (19:59:59 allowed / 20:00:00 blocked) including a transaction that starts before the boundary, oversell by two cashiers, expense posting vs close, double close, concurrent customer quick-add with same normalized phone, idempotent replay with differing payload.
- **Cash calculator table tests:** same-session void nets to zero; void in later session; partial refund; split tender with change; card/mobile never in drawer; drawer vs petty-cash expense; reversal exactly once.
- **Time/date:** `business_date` across UTC day boundary for Asia/Dhaka (N-3), midnight crossing, leap-year target month.
- **Accounting coupling:** close with variance when posting accounts missing / period closed (decide expected outcome after §K-21).
- **Outbox/inbox:** event written in the same transaction as sale/void/close; rollback leaves no event; consumer duplicate delivery; projection rebuild equality; replay authorization.
- **Frontend:** stable idempotency key across retry; single footers/no tabs; fullscreen preserves state; keyboard shortcuts vs text entry; 1366×768 and 1920×1080 layout; server-time offset for expiry warnings (18h/19h); owner/role-switch outlet selection.
- **Regression guard (AC-18):** keep existing characterization tests green; do not delete 15h/legacy loyalty tests without approval — migrate them deliberately.

### N.7 Recommended implementation order
Unchanged in structure from §I with these amendments: Step 1 gains N-1/N-2/N-6/N-8; Step 3 (terminals) is also the go-live unblocker and may precede non-terminal Step 2 items; add an explicit decision gate for §K-21 (accounting coupling) before Step 7; add a PostgreSQL runtime test for `business_date` before Step 4; add Audit-v2-scope verification before Step 8.

### N.8 Implementation-readiness assessment (Rev 2; reconfirmed in Rev 3 — see §O.15)
**Not ready to implement the Master Blueprint as written.** Approximately **20–25%** of requirements are met, almost all in the preserved checkout/stock/accounting core, which itself needs P0 hardening (server authority, serialization, correct cash maths). Ready to start immediately **after** user approval: Step 1 hardening (low design risk, characterization tests exist). Not ready: cash close, expense-to-drawer, targets, loyalty (blocked on §K decisions and external module gates). Reusable infrastructure (outbox/inbox, audit v2, outlet authorization, journals) is strong and lowers integration risk once the decisions in §K-2, §K-4, §K-9, §K-21 and §K-22 are made.

---

## O. Rev 3 Focused Verification Pass

*Scope: resolve every Verification Required row and the unresolved assumptions that affect architecture, using read-only evidence. This is not a new gap analysis; Rev 2 statements that the evidence confirmed are unchanged. Nothing in this pass modified application code, the Master SRS/LLD, migrations or database records.*

### O.1 Method and safety envelope
- **Static tracing** of the real code paths (pricing, authorization, expense approval, session open/close, sale commit, audit/outbox).
- **Read-only database inspection** of the already-running Development PostgreSQL 16.13 container (`iranimart-dev-postgres-1`, 127.0.0.1:5435, DB `retailpos_db`). Every session began with `SET default_transaction_read_only = on`; only `information_schema`, `pg_indexes`, `pg_constraint`, `pg_trigger` catalog queries and aggregate counts / non-PII columns (ids, codes, status, timestamps) were run. No credentials, phone numbers or names were printed; no row was inserted, updated or deleted; no container was started, stopped or recreated.
- **Existing non-mutating commands:** `dotnet test` for the DB-free unit-test project, and `dotnet ef migrations has-pending-model-changes` (model-only; uses the factory's inert placeholder host, so it cannot reach any database; it writes no files).
- **Official documentation** for Npgsql timestamp behaviour (https://www.npgsql.org/doc/types/datetime.html).

### O.2 Execution log and limits

| # | Action | Result |
|---|---|---|
| 1 | `docker ps` | Development stack (PostgreSQL, RabbitMQ, Redis, Elasticsearch, Kibana, pgAdmin) healthy; **Test stack containers exist but are `Exited`**. |
| 2 | Read-only catalog + aggregate queries (see §O.3) | Completed; no writes. |
| 3 | `dotnet test tests\RetailPOS.UnitTests --filter "FullyQualifiedName~Pos\|FullyQualifiedName~Customers"` | **541 passed, 0 failed, 0 skipped** (30 s). These tests use mocks/in-memory fakes — they prove handler logic, **not** locking, time zones or constraints. |
| 4 | `dotnet ef migrations has-pending-model-changes` | "**No changes have been made to the model since the last migration.**" Build emitted pre-existing NuGet advisories (`SixLabors.ImageSharp` 3.1.12, NU1902/NU1903) — outside POS scope, flagged for the security backlog only. |
| 5 | **Not run:** PostgreSQL-backed integration tests, API-factory HTTP tests, Angular tests | PG tests would have fallen back to the **Development** server (`RETAILPOS_TEST_POSTGRES` is not set; `PostgresHarness.CreateAsync` default is `127.0.0.1:5435/retailpos_db`) and create scratch databases there, which the project's local-infrastructure rules forbid; the Test stack (55432) is stopped and starting it needs approval. |

### O.3 Objective 3 — live PostgreSQL schema vs EF mappings vs migrations

| Check | Result | Matches EF/migration? |
|---|---|---|
| Applied migrations | **61** rows in `__EFMigrationsHistory`, latest `20261023072233_PurchaseOrderOutletDestination`; all 61 present on disk | Yes (my first comparison reported one "missing" file because of a filter bug on `Snapshots`; corrected) |
| EF model vs snapshot | No pending changes | Yes |
| `cash_sessions` | `business_date`, `opened_at`, `closed_at` = `timestamp with time zone`; `business_id` nullable; no row-version column; 18 columns exactly as `DOM/CashSession.cs` | Yes |
| `sales` | `business_date` = `date`; `sale_date`, `created_at` = `timestamptz`; `sale_number` varchar; **no `business_id`, no `salesperson_user_id`** | Yes |
| `customers` | `customer_code`, `phone` varchar; **no `business_id`, no normalized phone** | Yes |
| `products`, `product_variants`, `categories`, `held_sales`, `inventories` | **no `business_id` column** | Yes |
| `pos_terminals` | id, outlet_id, name, code, is_active, is_default, created_at, updated_at (no `description`, `business_id`, token) | Yes |
| `sale_payments` | `amount` numeric, `tendered` numeric NULL | Yes |
| Unique indexes (POS tables) | `ux_cash_sessions_one_open_per_terminal (outlet_id, terminal_id) WHERE status='open'`; `IX_pos_terminals_outlet_id_code`; `IX_sales_outlet_id_idempotency_key (outlet_id, idempotency_key) WHERE idempotency_key IS NOT NULL`; `IX_customers_customer_code WHERE customer_code IS NOT NULL`. **No unique index on `sales.sale_number`; none on `customers.phone`.** | Yes |
| FK delete rules | `cash_sessions→outlets` RESTRICT, `→pos_terminals` RESTRICT; `pos_terminals→outlets` **CASCADE**; `sales→outlets` RESTRICT, `sales→pos_terminals` **SET NULL**, `sales→cash_sessions` **SET NULL**; `receipt_print_histories→pos_terminals` **SET NULL** | Yes |
| Check constraints | **None** on `sales`, `cash_sessions`, `sale_payments`, `expenses`, `customers`, `pos_terminals` | n/a |
| Triggers | Only `trg_customers_protect_walkin` on POS tables (migration `20261013041308_…`); audit tables have immutability triggers | Matches migration |
| Data shape (Development) | 1 business, 3 outlets (0 with NULL business), 2 active default terminals (`MAIN-01`, `BR-01`), 3 sales (all `completed`, **`sale_number = ''`**, `cash_session_id` NULL, `business_date` NULL, **no payment rows**), **0 cash sessions**, 6 customers (1 `WALKIN`, **no duplicate phone groups**), 8 products / 24 variants / 11 categories, outbox: 1 `Pending` `operational-location.outlet.created`, **0 sale/cash-session events** | — |

**Conclusion:** no schema drift between EF model, migrations and the Development database. The data-level facts add one migration prerequisite (empty `sale_number` values) and confirm the outbox has never carried a POS event. **Production data quality is unknown.**

### O.4 Objective 4 — is pricing server-authoritative? **No (verified).**
- **What was verified:** the full pipeline (`APP/Pos/Queries/CalculatePosCartPricesQuery.cs`: base price + variant adjustment → outlet fixed/margin override → highest-priority rule (`variant`/`product`/`category`/`outlet`/`global`) → per-unit discount capped at price → per-variant `TaxRate`) and every caller.
- **Evidence:** `IPosCartPriceCalculator` / `CalculatePosCartPricesQuery` are referenced only by `API/Controllers/PricingController.cs` (L98, L150), `API/Controllers/PosController.cs` (L371) and `APP/Pos/Queries/GetPosProductLookupQuery.cs`. `CreateSaleCommandHandler`'s constructor (`APP/Sales/Commands/CreateSaleCommand.cs` L39–48) has no pricing dependency; `SaleItem.UnitPrice`, `DiscountAmount`, `AppliedRuleId/Name`, `Sale.Discount`, `Sale.Tax` and `Sale.TotalAmount` (`lineItemsTotal − Discount + Tax`) are computed from request values. The frontend posts `unitPrice` from its cart and a tax derived from one global `taxRate` signal (`pos-screen.component.ts` L70/L200, L1470). The pipeline also caches results in Redis per (variant, outlet, quantity) — a server re-price must bypass the cache (`ReadFromCache: false`).
- **Classification:** unchanged — **Conflicting, P0** (rows "Server-authoritative price", FR-POS-07, G-1).
- **Remaining uncertainty:** none on the finding; the *design* of re-pricing (rule-effective-date handling, multi-quantity tiers, rounding with invoice discount) needs LLD detail.
- **SRS/LLD correction required:** LLD §10 should say the commit path **re-invokes the existing pricing calculator** and recomputes tax per line from the product tax rate (no client authority), and name the tolerance policy (reject vs reprice) — no business-rule change.

### O.5 Objective 5 — Business Owner / Super Admin outlet authorization and role switch (verified)
- **Evidence:** `APP/Identity/LocationAccess/UserOutletAccess.cs` (Super Admin: L16–36 all outlets of the selected business, or every outlet when `EffectiveBusinessId` is null because `UserOutletAccessStore.ListOutletsAsync(null)` has no filter; owner/wildcard: L39–79 business-wide; non-global users: assignment-filtered by business L84–135, L203–256; `EnforceWriteOutletAsync` L442–481 requires the outlet to be in the authorized set); `API/Services/RoleSwitchContext.cs` (`IsBusinessOwner`/`IsSuperAdmin` from the **real** role; `EffectiveOutletId` = acting outlet when switched; `EffectiveBusinessId` = validated `X-Business-Id` else claim); `API/Middleware/BusinessContextMiddleware.cs` (header validated by `ValidateBusinessContextQuery`); `APP/Authentication/Commands/RoleSwitchCommands.cs` L40–98 (acting outlet must belong to the owner's business; response `outletId = ActingOutletId`); `API/Services/TenantAccessService.cs` L17–21; tests `UserOutletAccessBehaviorTests` (`WildcardOwner_WithoutBusinessContext_DoesNotListAllBusinesses`, `SuperAdminAccess_IsScopedWhenBusinessContextIsSupplied`, `GlobalAccess_UsesSelectedBusinessAndDoesNotKeepForeignDefault`, `LegacyBusinessOwnerAccess_RemainsBusinessScopedAndGlobalWithinBusiness`) and `RoleSwitchRelationalTests`.
- **Findings:** (a) backend honours the SRS persona rule (owner wildcard scope, role-switch keeps real-role scope with acting-role permissions); (b) **`Docs/02_Security/DATA_ISOLATION.md`'s "legacy BusinessOwner no-BusinessId fallback" is stale** — the current code throws "A Business context is required…"; (c) POS accounting posting calls `RequireBusinessId()`, so a **Super Admin without `X-Business-Id` cannot complete a sale** (rolled back); (d) the POS UI only uses `currentUser().outletId`, so owners are pinned to their default/acting outlet and Super Admin without an assigned outlet is blocked.
- **Classification:** N-9 **Verification Required → Partially Implemented**.
- **Remaining uncertainty:** role-switched POS behaviour end-to-end was **not executed** (no HTTP/E2E run); conclusion is from code and test names.
- **SRS/LLD correction required:** SRS §3 persona table should state the selling scope per persona (see D8) and require an outlet selector fed by `/api/me/outlets`; LLD §13 should note the wildcard does not widen scope (`PermissionCatalog.Wildcard` doc comment).

### O.6 Objective 6 — product catalog: shared by intent or missing isolation? **Intent cannot be established → explicit decision.**
- **Structure (verified in code and live schema):** `Product`, `ProductVariant`, `Category` (and `ProductVariation`) have no `BusinessId`; product list/lookup read stores apply no business predicate (`PosProductLookupStore`; only `ProductStockReadStore` filters *locations* by business).
- **Intent evidence (conflicting):** *for shared/single catalog* — `Docs/05_Product_Catalogue/PRODUCT_MODULE_ANALYSIS.md` decision #20: Main Product Code is "**globally unique across the entire catalogue**" (a tenant-less rule); `Docs/Mobile/Planning/34_API03_PRODUCT_CATALOGUE_ACCEPTANCE.md` L38–40 and `35_…` L108 rely on a single-Business deployment guard and state the catalog "has no Business ownership columns". *For multi-business* — `Docs/02_Security/DATA_ISOLATION.md` ("multi-business SaaS", acceptance criteria "all data access paths for business-owned entities are scoped by BusinessId"); `Docs/Mobile/Planning/01_EXISTING_SYSTEM_ASSESSMENT.md` L44 explicitly records "VERIFIED structure / **UNKNOWN policy** / BLOCKED". The Development database has a single business.
- **Effect on POS checkout:** limited by design — `SaleCreationStore.FindOutletInventoryAsync` requires an `inventories` row for (variant, outlet, `outlet`); without it checkout throws. `GET /api/pos/lookup` would disclose catalog data across tenants if more than one business ever shared the database.
- **Classification:** N-4 stays **Verification Required (owner decision D6)**. G-24 re-rated Medium.
- **SRS/LLD correction required (no business-rule change):** SRS §13 must state the assumed catalog scope explicitly (single-business-per-deployment vs tenant-owned), and the multi-business isolation acceptance tests (AC-18/§14) must be scoped accordingly.

### O.7 Objective 7 — expense approval permissions and maker-checker (verified)
- **Evidence:** `API/Controllers/AccountingExpensesController.cs` L162–273 (`create`, `edit`, `submit`, `approve`, `reject`, `cancel` each with its own `accounting.expenses.*` policy); `INF/Expenses/ExpenseCommandStore.cs` L334–360 (`SubmitAsync` sets `SubmittedBy`), L382–523 (`ApproveAsync`: status gate, category/account checks, journal via `approvalJournalPoster`; **no comparison of `actorUserId` with `CreatedBy`/`SubmittedBy`**); `APP/Identity/Permissions/PermissionCatalog.cs` L76–82 (new→legacy aliases: `accounting.expenses.approve` ⇐ `expenses.edit`), L133–148 (`IsGranted`: wildcard `*` or alias); `APP/BusinessAdministration/BusinessRolePermissions.cs` (`BusinessOwner = ["*"]`; `AccountsAdmin` holds create+submit+approve). Self-approval tests exist for manual journals (`ManualJournalWorkflow*Tests`, `ManualJournalAllowSelfApproval`), HR and payroll, **not for expenses**. `API/Controllers/ExpensesController.cs` (legacy `api/expenses`) exposes create/edit/delete only — it cannot approve, so it is not a bypass.
- **Classification:** N-5 **Verification Required → Partially Implemented** (an approval workflow with permission separation exists; separation of duties does not).
- **Remaining uncertainty:** whether a Business Owner is *intended* to self-approve cash outflows is a policy question (D7).
- **SRS/LLD correction required:** FR-EXP-01's phrase "existing approval/reversal rules" should be replaced with the actual rule set (Draft→Submitted→Approved; permission separation only) and the SoD policy for POS Drawer/Petty Cash sources.

### O.8 Objective 8 — BusinessDate and timestamp handling in PostgreSQL/Npgsql (mechanism verified, not executed)
- **Evidence:** `API/Program.cs` L31 `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true)`; `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.2 (`RetailPOS.Infrastructure.csproj`); `INF/Data/RetailPOSDbContext.cs` L2369 forces every `DateTime` property to `timestamp with time zone`; Npgsql docs: under the legacy switch "reading a `timestamp with time zone` returns a **Local** `DateTime` instead of Utc"; the repository documents the same hazard and compensates elsewhere (`INF/Inventory/StockCounts/UtcTime.cs` L5–10; `INF/AccountingJournals/ManualJournalDraftStore.cs` L1110–1114). POS code does **not**: `INF/Pos/SaleCashSessionValidator.cs` (`now >= session.OpenedAt.AddHours(maxHours)`), `APP/Pos/Queries/CashSessionQueries.cs` (`overdue`, `elapsedSeconds` from the DB-read `OpenedAt`), `APP/Pos/Commands/CloseCashSessionCommand.cs` (`Map(...)` elapsed seconds) and `APP/Pos/CashSessionCloseSummaryCalculator.cs` (`closingAt − session.OpenedAt`, which feeds the reported open duration). `DateTime` comparison and subtraction ignore `Kind`, so the Local value is compared by ticks with `DateTime.UtcNow`. `OpenCashSessionCommand` takes `BusinessDate` from the client (`(request.BusinessDate ?? now).Date`, Kind `Unspecified` from JSON) and `Company.TimeZone` (`Asia/Dhaka`) is used only for PDF/GRN **display** (`DocumentPdfFormatting.cs`, GRN pages), never to derive a business date.
- **Effect (derived, not executed):** if the API host's OS zone offset is *X*, the 20h cutoff fires at 20h + *X* (host +6 → 26 h; +1 → 21 h; −5 → 15 h) and displayed elapsed time is off by *X*. On a UTC host (typical Linux container) the effect disappears. The deployment host zone is not defined anywhere in the repository; the developer workstation is `GMT Standard Time` (UTC+1 in summer).
- **Existing tests do not cover it:** `CashSessionServiceTests` uses the EF InMemory provider (L589); the PG harness never exercises the session validator (mocked).
- **Classification:** N-3 **Verification Required → Conflicting (conditional on host zone)**; priority raised to **P0** because it silently breaks D03/FR-TIME-01.
- **Remaining uncertainty:** (1) deployment host zone; (2) behaviour of writing an `Unspecified` client `BusinessDate` to `timestamptz` under the legacy switch (expected: interpreted in host local time, i.e. a possible day shift when the host zone ≠ UTC) — **needs the PG/TZ integration test in §O.10 (T-2)**.
- **SRS/LLD correction required:** LLD §3.1/§5.1 must specify `BusinessDate` as SQL `date`, `OpenedAtUtc` as UTC-normalised at the store boundary, the authoritative time-zone source for the business date (business/outlet setting — **not** the global JSON `Company.TimeZone`, and not the host), and that the legacy Npgsql switch is either removed with a migration plan or neutralised for POS entities.

### O.9 Objective 9 — sale completion versus cash-session closing (verified by code + live schema; race not executed)
- **Sale path** (`APP/Sales/Commands/CreateSaleCommand.cs` + `INF/Pos/SaleCashSessionValidator.cs`): transaction begins (default isolation = PostgreSQL Read Committed) → inventory decrement → `ValidateActiveForSaleAsync` reads the session with an **un-locked `FirstOrDefaultAsync`** and checks `Status == "open"` and age → many further awaits (invoice number from file, journal create+post via MediatR, stock ledger, audit) → commit. The session is **never re-read or locked again**, and the sale transaction never writes to `cash_sessions`.
- **Close path** (`APP/Pos/Commands/CloseCashSessionCommand.cs`, `INF/Pos/CashSessionCloseStore.cs`): `FindForCloseAsync` (tracked read, **before** the transaction) → `GetCloseSummaryDataAsync` (separate read of sales/transactions at `summaryClosingAt`, **before** the transaction) → `BeginTransactionAsync(IsolationLevel.ReadCommitted)` → variance journal → `UPDATE cash_sessions` (no `WHERE status = 'open'`, no concurrency token) → commit; audit and event after commit; a second `DateTime.UtcNow` (`closedAt`) is taken after the summary.
- **Database backstop:** none. Live catalog: no trigger and no check constraint on `sales`/`cash_sessions`; `sales.cash_session_id` is a plain FK with `ON DELETE SET NULL`. PostgreSQL's FK check on `INSERT INTO sales` acquires `FOR KEY SHARE` on the referenced `cash_sessions` row; the close's non-key `UPDATE` takes `FOR NO KEY UPDATE`; these lock modes do **not** conflict, so neither transaction waits for the other.
- **Resulting interleavings that the code permits:** (1) sale validated while open → close summary computed without the uncommitted sale → close commits → sale commits into a **closed** session (cash missing from the frozen expected amount); (2) two concurrent closes both pass the status check and both update/audit/publish (variance journal deduplicated only by its create-or-get key); (3) a sale transaction that started before `OpenedAt + 20h` can commit after it. The only race with a real PostgreSQL test today is *open vs open* (`ConcurrentCashSessionOpen_…`).
- **Classification:** unchanged — **Conflicting, P0** (FR-TIME-02 commit gate, FR-CLOSE-05, G-2/G-3). FR-CLOSE-05's "double close is idempotent" text corrected to *sequential only*.
- **Remaining uncertainty:** the exact probability/impact needs the real-PostgreSQL test T-1; the conclusion that the lock modes do not conflict is PostgreSQL-documented behaviour, not a local measurement.
- **SRS/LLD correction required:** LLD §5.2/§5.4/§18.2 must name the serialization primitive concretely — e.g. `SELECT … FROM cash_sessions WHERE id = @id FOR UPDATE` (close, expense posting) vs `FOR SHARE` (sale) **inside** the transaction, re-check `status`/age after the lock and immediately before commit, take one `UtcNow` for the whole close, and make the close `UPDATE` conditional on `status IN ('open','overdue')` — and state that the FK does not provide this serialization.

### O.10 Objective 10 — which critical findings need integration tests, not static reading

| ID | Finding | Static evidence sufficient to *classify*? | Required executable evidence | Where it must run |
|---|---|---|---|---|
| T-1 | Sale vs close race; double close; sale at `OpenedAt+20h−ε` (G-2, G-3, N-3 gate) | Yes (classification) | Two real transactions with barrier hooks on a **real PostgreSQL** (no mocked `ISaleCashSessionValidator`): assert no sale commits into a closed session / expected cash includes it; one close wins; boundary 19:59:59 allowed, 20:00:00 blocked | Test stack (55432), schema built **from migrations** |
| T-2 | `OpenedAt`/`BusinessDate` under host time zones (N-3, G-22) | No (runtime effect unproven) | Same test run with process `TZ=UTC` and `TZ=Asia/Dhaka` (and a DST zone): open 23:30/00:30 local; assert cutoff, elapsed, `BusinessDate` round-trip, and that `sales.business_date` equals the session date | Test stack, PG + TZ matrix |
| T-3 | Server authority of price/tax/cashier/discount (G-1) | Yes | HTTP test through the real controller: forged `UnitPrice`, `Discount`, `Tax`, `CashierId`, both discount types → expect rejection/re-price | API factory (InMemory acceptable for logic; PG for money precision) |
| T-4 | Oversell by two cashiers (stock gate) | Partly (xmin token verified) | Concurrent sales of the last unit on PG; exactly one commits | Test stack |
| T-5 | Idempotent replay (stable key, payload mismatch, race loser returns original) (N-8, G-6) | Yes | PG test: concurrent same key → loser returns original (today `DbUpdateException`); FE e2e: retry after timeout reuses key | Test stack + Angular e2e |
| T-6 | Customer/catalog tenant isolation (G-8, N-4) | Yes for customers | Two-business fixtures: search/create/lookup cannot cross tenants; decision-dependent for catalog | API factory / PG |
| T-7 | Expense SoD and drawer posting vs close (N-5, FR-EXP-01) | Yes for SoD | HTTP: same user creates+submits+approves (expect denial once D7 approved); PG: posting concurrent with close | Test stack |
| T-8 | Cash expected-amount calculator (G-4) | Yes | Table-driven unit tests (void same session, void later session, partial refund, split tender, card/mobile) — **no DB needed** | Unit project |
| T-9 | VAT line allocation reconciles with `Sale.Tax` (G-10) | Yes | Unit/relational test with invoice discount | Unit / SQLite |
| T-10 | Migration safety (unique `sale_number`, unique normalized phone, terminal FK changes) | No | Dry-run diagnostics on a **copy** of production data: duplicate/empty `sale_number`, duplicate normalized phones, orphan terminals; rollback script rehearsal | Copy of production |
| T-11 | Walk-in trigger vs per-business Walk-in design | Partly | PG test of `trg_customers_protect_walkin` after key redesign (harness currently uses `EnsureCreated`, which **does not create the trigger**) | Test stack, migrations-built schema |
| T-12 | Outbox producer atomicity for sale/void/close; duplicate delivery; replay | No (no producer exists) | PG: event row commits/rolls back with the sale; inbox dedupe | Test stack |

**F. Audit/outbox prerequisite (verified):** `INF/Integration/IntegrationOutbox.cs` — `AppendAsync` calls `RequireTransaction`, which throws unless a transaction **and** `audit.RequiresAtomicPersistence` (a `IRequiredAuditTransaction` scope) are active for the same business; `AppendSourceChangeAsync` requires only an open transaction (and a matching business if the audit context is initialised). Sales and POS commands never use `IRequiredAuditTransaction` (zero references under `APP/Sales`, `APP/Pos`, `INF/Sales`, `INF/Pos`); their audit rows are written by `AuditService.RecordAsync` through the **same `DbContext`** inside the sale transaction (so audit failure rolls the sale back — proven by `CreateAsync_RollsBackJournalSaleAndStockLedger_WhenRequiredAuditFails`), whereas cash-session open/close audits are written **after** commit. LLD §11 must therefore specify which append method POS producers use (recommended: `AppendSourceChangeAsync` inside the existing sale/void/close transactions) or the command wrapping needed for `AppendAsync`.

### O.11 Verification Required register — outcome

| Item (Rev 2) | Outcome | New status | Basis |
|---|---|---|---|
| N-3 `BusinessDate`/timestamps | Mechanism verified; manifestation host-zone dependent; unexecuted | VR → **Conflicting (conditional)** | §O.8 |
| N-4 catalog tenancy | Structure verified; intent contradictory | **VR (owner decision D6)** | §O.6 |
| N-5 expense maker-checker | Verified: no SoD | VR → **Partially Implemented** | §O.7 |
| N-9 owner/role-switch POS | Verified by code/tests | VR → **Partially Implemented** | §O.5 |
| B.13 §13 performance NFR | Not measured (no benchmarks in repo) | **VR (unchanged)** | not testable statically |
| J.3 pricing pipeline internals | Resolved | closed | §O.4 |
| J.3 UserOutletAccess internals | Resolved | closed | §O.5 |
| J.3 Audit v2 semantics | Resolved | closed | §O.10-F |
| J.3 live DB state | Resolved for Development only | partly closed | §O.3 |
| A3 company time zone "intended outlet TZ" | Refuted as a *code* fact: used only for display | open decision D9 | §O.8 |

### O.12 Corrections to earlier report text (evidence-based)
1. **Walk-in protection** — Rev 2 implied application-level protection only; a database trigger also exists (`trg_customers_protect_walkin`). Walk-in *immutability* is Implemented; *per-business* Walk-in remains missing (row updated).
2. **FR-CLOSE-05 "double close idempotent"** — true only sequentially (row updated).
3. **N-4 wording "sale never checks variant ownership"** — softened: checkout requires an outlet inventory row, which indirectly constrains cross-tenant sale.
4. **DATA_ISOLATION.md "legacy no-BusinessId fallback"** — stale versus current code (documentation defect outside the SRS/LLD).
5. **Migration inventory** — all 61 applied migrations exist on disk; the set is untracked in git.
6. **Legacy test claim** — unchanged from Rev 2: tests mock/inject the 15h value.
7. **No Rev 2 status was downgraded by this pass**; four VR rows were resolved (see §O.11). Verified findings were left untouched.

### O.13 Final verified gap counts (Rev 3)

| Status | Rows | Share | Δ vs Rev 2 |
|---|---|---|---|
| Implemented | 3 | 4.1% | 0 |
| Partially Implemented | 31 | 41.9% | +2 (N-5, N-9) |
| Missing | 17 | 23.0% | 0 |
| Conflicting | 21 | 28.4% | +1 (N-3) |
| Verification Required | 2 | 2.7% | −3 |
| **Total** | **74** | 100% | 0 |

**Evidence grading of the 74 rows:** all were classified from code; the rows that cite live Development schema/data in §O.3 (terminals, cash-session columns and indexes, customer/phone/Walk-in, sale numbering, FK rules, constraints, outbox) were additionally cross-checked against the database; **0 were confirmed by executing a PostgreSQL-backed test** (not possible safely, §O.2); 541 DB-free unit tests passed.

### O.14 Business decisions requiring owner approval (consolidated; cross-referenced to §K)

| ID | Decision | Why it blocks | §K |
|---|---|---|---|
| D1 | Which session bears the cash effect of a void/refund after the original session closed; does a cash void need an open session? | Defines the expected-cash formula (FR-CLOSE-02) | K-1 |
| D2 | Keep or remove the automatic Cash-Difference journal at close; may close be blocked by GL-posting failure? | FR-CLOSE-03 vs implemented behavior | K-2, K-21 |
| D3 | Where the per-business max session hours lives; migrating 15 → 20; may a business set < 20? | D03 compliance | K-3 |
| D4 | Overdue as persisted state or derived only | Index/state-machine design | K-4 |
| D5 | Treatment of legacy `customers.loyalty_points` at Loyalty go-live | Data migration, Loyalty D18 | K-8 |
| D6 | Is the product/variant/category catalog shared (single business per deployment) or tenant-owned? | Isolation tests, schema, lookup security | K-22 |
| D7 | Separation of duties for POS-Drawer/Petty-Cash expenses (may an owner self-approve?) | Expense control design | K-26 (new) |
| D8 | Who may sell where: Super Admin selling (needs business header + outlet), owner outlet choice, shared-cashier rules | Persona table, POS outlet selector | K-15 |
| D9 | Authoritative time-zone source for `BusinessDate` (business vs outlet vs host) and the deployment host zone | Cutoff/business-date correctness | K-24 |
| D10 | Accounting coupling: synchronous GL posting in checkout/close vs validated-at-setup + deferred idempotent posting | Reliability design | K-21 |
| D11 | Terminal provisioning for new businesses (onboarding default vs Outlet editor only) | Go-live | K-20 |
| D12 | Posting-date policy for backdated sales (journal vs stock ledger vs reports) | FR-TIME-03 | K-6 |
| D13 | Invoice numbering model (per business/outlet/terminal, DB sequence) and migration of file counter | Uniqueness | K-11 |
| D14 | Persist cash tendered/change for every cash payment | Receipts, drawer audit | K-23 |

**[V3] K-26 (new):** may a Business Owner/`AccountsAdmin` create, submit and approve the same expense, and does POS Drawer/Petty Cash require a different approver? (= D7)

### O.15 Implementation readiness (Rev 3)
Unchanged conclusion: **not ready to implement the blueprint as written (≈20–25% met)**, but the evidence base is now sufficient to finalize the SRS/LLD text once D1–D14 are answered. Immediately implementable after approval (no open business decision): the P0 hardening set — server-authoritative price/tax/cashier/discount (T-3), stable idempotency (T-5), legacy-loyalty endpoint guard and customer tenant scoping (T-6), tender/change persistence (D14 aside), exchange-link authorization — plus the **test-infrastructure fix** (point `RETAILPOS_TEST_POSTGRES` at the Test stack, remove the Development fallback, build the PG schema from migrations) which must precede T-1/T-2/T-4/T-11/T-12. Not implementable yet: cash close, expense-to-drawer, targets, Loyalty wiring.

### O.16 Remaining unverified items
1. Any behaviour that needs a real PostgreSQL concurrency/time-zone run (T-1, T-2, T-4, T-5 race loser, T-12).
2. Deployment host time zone and production data (duplicate phones, empty/duplicate `sale_number`, terminals, open sessions).
3. Role-switched / Super Admin POS flow end-to-end (code + tests read, not executed).
4. §13 performance NFRs (no benchmarks exist).
5. Accessibility and 1366×768 layout of the current POS page; Angular test results.
6. Receipt PDF visual output; consumers of `SaleItem.VatAmount` beyond the VAT report.
7. Behaviour of writing an `Unspecified` `BusinessDate` to `timestamptz` under the legacy switch.
8. Elasticsearch usage beyond configuration (no client package is referenced by any `.csproj`).

### O.17 Technical corrections to the SRS/LLD that do **not** change approved business rules
1. LLD §1/§16: replace unverified paths with the verified ones (`SaleService.cs` → `APP/Sales/Commands/CreateSaleCommand.cs` + `API/Services/SaleCreationAdapters.cs`; `SettingsService` → `INF/Settings/JsonSystemSettingsStore.cs`).
2. LLD §3.1: `BusinessDate` is SQL `date`; `OpenedAtUtc` normalised to UTC at the store boundary; remove reliance on `DateTime.Kind` under the legacy Npgsql switch (§O.8).
3. LLD §5.2/§5.4/§18.2: concrete lock primitive and re-check points; conditional close `UPDATE`; single clock reading; state that the FK does not serialize (§O.9).
4. LLD §10: checkout re-invokes the existing pricing calculator (cache bypass) and computes tax per line server-side (§O.4).
5. LLD §11: use the existing `IntegrationEnvelope`; specify `AppendSourceChangeAsync` (transaction only) vs `AppendAsync` (requires a required-audit scope) for POS producers (§O.10-F).
6. LLD §3.1 constraints: unique `sale_number` and normalized-phone indexes must be **partial/back-filled** because live legacy rows have empty `sale_number` (§O.3); keep the filtered unique idempotency index.
7. LLD §3.3: record the existing FK delete behaviors (`pos_terminals→outlets` CASCADE; `sales→pos_terminals` SET NULL; receipt history SET NULL) as the baseline for the migration; the Walk-in uniqueness key must become `(business_id, customer_code)` with the protection trigger updated.
8. SRS FR-EXP-01: describe the real approval rules (Draft→Submitted→Approved, permission separation only) and add the D7 SoD requirement.
9. SRS §3/§13: per-persona selling scope, wildcard semantics (`*` does not widen business/outlet scope), catalog-scope statement (D6).
10. LLD §14 test design: PG tests must use the Test stack and migration-built schema; add a time-zone matrix; cash-session tests must not rely on the EF InMemory provider for lock/time behaviour.
11. SRS/LLD naming: companion file/version names and the prototype file name (H-1) — unchanged from Rev 2.
12. Out of the SRS/LLD but worth fixing in project docs: `Docs/02_Security/DATA_ISOLATION.md` legacy-fallback statement is stale; the `Company.TimeZone` global setting is display-only.

### O.18 Blockers to finalizing the Master Blueprint
- **No technical blocker** prevents writing the final SRS/LLD text: all architecture-affecting unknowns are either resolved above or reduced to the owner decisions in §O.14.
- **Business-decision blockers for the *final* text:** D1, D2, D3/D9 (time rules), D4, D6, D7, D10, D11, D14; D5 and D12/D13 can be finalized with documented defaults.
- **Dependency blockers for *implementation* (not for the text):** Returns/Exchange SRD (partial-refund semantics, loyalty reversal); Loyalty P0 gates (identity/duplicate-phone remediation, payment consistency, accounting validation); PostgreSQL test-infrastructure isolation (§O.15); approval to apply any migration to a persistent Development database.

---

## M. Appendix — Key file index

| Area | Primary files |
|---|---|
| Checkout | `APP/Sales/Commands/CreateSaleCommand.cs`, `API/Controllers/SalesController.cs`, `API/DTOs/Sale/CreateSaleDto.cs`, `API/Services/SaleCreationAdapters.cs`, `INF/Sales/SaleCreationStore.cs` |
| Void/refund | `APP/Sales/Commands/VoidSaleCommand.cs`, `RefundSaleCommand.cs`, `INF/Sales/SaleVoidStore.cs` |
| Cash sessions | `DOM/CashSession.cs`, `APP/Pos/Commands/{Open,Close}CashSessionCommand.cs`, `APP/Pos/CashSessionCloseSummaryCalculator.cs`, `APP/Pos/Queries/CashSessionQueries.cs`, `INF/Pos/CashSession*Store.cs`, `INF/Pos/SaleCashSessionValidator.cs`, `API/Services/CashSessionCloseAdapters.cs`, `API/Controllers/PosController.cs` |
| Terminals/outlets | `DOM/PosTerminal.cs`, `DOM/Outlet.cs`, `INF/Outlets/OutletWriteStore.cs`, `API/Controllers/OutletsController.cs`, `INF/Pos/PosTerminalReadStore.cs`, `INF/Data/DbSeeder.cs` |
| Customers/loyalty (legacy) | `DOM/Customer.cs`, `API/Controllers/CustomersController.cs`, `APP/Customers/**`, `INF/Customers/**` |
| Expenses | `DOM/Expense.cs`, `INF/Expenses/ExpenseCommandStore.cs`, `FE/pages/accounting/expense-form.component.*` |
| Eventing | `INF/Integration/*`, `APP/Integration/DurableDeliveryContracts.cs`, `API/Services/LoggingSaleEventPublisher.cs`, `API/Program.cs` |
| Settings | `INF/Settings/JsonSystemSettingsStore.cs`, `APP/Settings/SystemSettings.cs`, `API/system-settings.json` |
| Reports | `INF/SalesReports/SalesReportReadStore.cs`, `INF/AccountingReports/VatReportReadStore.cs` |
| Frontend POS | `FE/pages/pos/pos-screen.component.{ts,html,css}`, `FE/services/pos.service.ts`, `FE/app.routes.ts` |
| Tests (existing) | `TST/RetailPOS.UnitTests/Pos/*`, `TST/RetailPOS.IntegrationTests/Sales/{PosCashSessionHttpTests,CashSessionServiceTests,SaleCreationPostgresConcurrencyTests,SaleVoidCommandTests}.cs`, `TST/RetailPOS.IntegrationTests/Customers/CustomerLoyalty*Tests.cs` |

---

*End of report (Rev 2). No development has been started. The Master SRS and LLD were not modified.*
