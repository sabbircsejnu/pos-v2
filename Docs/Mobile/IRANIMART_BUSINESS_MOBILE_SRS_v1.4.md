# IraniMart Business Mobile — Software Requirements Specification (SRS)

**Version:** 1.4 Draft | **Date:** 2026-10-13 | **Status:** Architecture baseline, pending decision register | **Platform:** Android (Flutter) | **Repository:** `pos-v2/retailpos-mobile/` | **Docs location:** `pos-v2/Docs/Mobile/`

## 1. Purpose and document control
This SRS defines functional, nonfunctional, integration, security, and acceptance requirements for the IraniMart Business mobile companion app and its necessary ERP extensions. It is normative for implementation. The LLD must implement these requirements without silently changing policy. **MUST** means mandatory; **SHOULD** requires documented justification to deviate; **MAY** is optional. All numerical targets labelled *proposed* are provisional until approved.

### 1.1 Goals
- Give owners actionable sales, inventory and discount intelligence.
- Give outlet managers and salespersons permission-scoped, useful dashboards.
- Allow staff to reliably scan and count physical stock, including without network connectivity, and submit counts to the existing ERP.
- Keep stock take reconciliation, reviews, approvals, inventory postings, official report generation and audit in the authoritative ERP.
- Provide explainable 1/2/3-month forecasting and later advisory production recommendations without automatically creating orders.
- Maintain an independently maintainable Flutter Android application ready for future SaaS tenant isolation.

### 1.2 Scope and exclusions
**In scope:** shared notification policy configuration and web/mobile inbox, role-based authentication, outlet scope, dashboards, stock-take initiation/count/offline sync/submission/history/results/export initiation, read-only ERP status, insight drilldowns, 1–3 month forecast experience, alerts, profile/settings, server-side API changes, contract/security/test acceptance.

**Out of scope in mobile:** checkout, sale/refund operations, stock approval or posting, GL/accounting, payroll/HR, price updates, purchase order/manufacturing order creation, editing approved counts, independently maintaining product master data. Device-camera and export UI prototypes do not establish production device support.

### 1.3 User roles
| Role | Allowed capabilities (subject to server grants) | Restricted by default |
|---|---|---|
| Owner | Authorised multi-outlet dashboards, profitability, stock results, forecast, recommendations | Cross-business/tenant records |
| Sales Manager | Assigned outlets, sales KPIs, staff leaderboard, stock take, permitted insights | Profit/cost/production decisions unless specifically granted |
| Salesperson | Own sales and target, granted stock take sessions | Other staff's private data, company margin/cost, production forecasting |
| Inventory reviewer (ERP only) | Reviews, resolves exceptions, approves/rejects and adjusts in POS | Mobile adjustment actions |
| Super Admin (ERP only) | Manage grants and rollout flags | Implicit access without auditing / tenant context |

Permissions MUST be enforced server-side per business, outlet, resource and field; UI hiding is only a convenience. Existing ERP auth, role and navigation models MUST be inspected before mapping new grants. No automatic cross-business administrator bypass.

### 1.4 Repository and delivery boundaries
- **Monorepo:** `pos-v2/retailpos-frontend/` is the existing Angular application; `pos-v2/src/` is the existing .NET backend; `pos-v2/tests/` is the backend test tree. The Android Flutter application MUST reside at `pos-v2/retailpos-mobile/`, with its own Android toolchain, `pubspec.yaml`, `pubspec.lock`, tests and dedicated mobile CI workflow.
- **Authoritative documentation:** store this SRS and companion LLD in `pos-v2/Docs/Mobile/`. A separate mobile folder is NOT a separate Git repository or microservice. No Flutter files should be added to `RetailPOS.slnx` as .NET projects.
- **Brand assets:** use the provided full IraniMart ornamental logo and monogram without recreating them. Preserve the approved gold/ivory/brown palette; validate contrast of gold-on-ivory buttons and text before release.
- **Existing implementation wins:** all existing API/entity/permission names are provisional until Copilot discovers actual repo structures. Requirements and established safety invariants take precedence over a speculative route name.

## 2. Business concepts and definitions
- **Business**: SaaS/ERP tenant scope; **Outlet**: operational inventory location.
- **SKU/variant**: exact barcode-linked inventory identity, usually design + colour + size; alias barcodes are mappings, not independent stock.
- **Stock take session**: immutable server identity plus counting scope, outlet, snapshot/baseline policy, participants and lifecycle.
- **Physical count**: submitted staff observation, distinct from authoritative on-hand balance.
- **Count event**: durable increment/decrement/manual correction action with unique ID and revision.
- **Expected quantity**: ERP quantity at the approved baseline adjusted per documented movement policy.
- **Variance**: counted quantity − comparable expected quantity.
- **Forecast**: probabilistic demand estimate, not a guaranteed sale or approved production instruction.

## 3. Functional requirements
Each ID is stable; implementation, tests, and sign-off MUST reference it.

### 3.1 Identity and app shell
- **AUTH-001** MUST sign in via existing ERP identity provider / approved mobile OAuth2/OIDC PKCE flow; no parallel password database.
- **AUTH-002** MUST obtain server-validated user identity, business context, grants, allowed outlet IDs; enforce token expiry/refresh and revocation.
- **AUTH-003** MUST show accessible navigation only. Role-switch is demo-only, never a production privilege escalation.
- **AUTH-004** MUST allow only authorised outlet selection and clear tenant/outlet-scoped cached state on account/tenant switch or logout.
- **AUTH-005** MUST show offline/expired-token state and prohibit final server submission without valid authorisation.
- **AUTH-006** MUST hide or redact restricted financial fields and server must exclude them from payloads.

### 3.2 Dashboards
- **DASH-001** Owner: sales, change, gross profit/margin if granted, invoices, units, average invoice, discounts, returns, 7/30-day trends, outlet and design ranking.
- **DASH-002** Manager: assigned-outlet sales and target attainment, staff sales leaderboard, invoice/discount/product trends and stock alerts.
- **DASH-003** Salesperson: self-only sales, target, attainment, remaining target, invoice count, units and average transaction; ranking only if granted.
- **DASH-004** Every metric MUST specify currency, timezone, reporting window, refresh time, definition and whether tax/refund/void are included; no inconsistent denominator between screens.
- **DASH-005** Date/outlet filters MUST be server-authorised, persisted within current session and show loading/empty/error states.
- **DASH-006** UI MUST handle no target, negative sales, stale cached data and unavailable profitability gracefully.

### 3.3 Stock take lifecycle
- **STK-001** Create a stock take only for authorised outlet and server-supported scope (full outlet, category or explicit SKU scope).
- **STK-002** Server MUST allocate opaque session ID, human-readable reference, revision, baseline specification and product-catalogue version.
- **STK-003** Lifecycle: Draft → InProgress → Submitted → UnderReview → Approved → Adjusted; Rejected/Cancelled terminal or explicit retry/recount transitions must be documented. No arbitrary client status transitions.
- **STK-004** Staff MUST be able to scan the same barcode repeatedly, each accepted scan incrementing the matching SKU by exactly one.
- **STK-005** Scan feedback MUST be immediate, distinct for success/unknown/invalid, with configurable sound/vibration and accessible visual confirmation.
- **STK-006** Camera scan MUST be throttled against a *single prolonged camera detection* (no phantom duplicates), while permitting genuinely repeated scans of the same physical barcode.
- **STK-007** Allow manual barcode lookup, SKU search, +/- count and numeric quantity correction; require recorded reason for adjustments according to configurable control policy.
- **STK-008** Allow undo of the last local count operation, including after temporary disconnect, without erasing previously acknowledged history.
- **STK-009** Support pause/resume, force close/reopen recovery and durable local counting across app termination.
- **STK-010** Default to blind count: expected ERP quantity MUST NOT be returned to unauthorised counting screen or pre-submission endpoint.
- **STK-011** Support separately identifiable unknown/ambiguous barcodes as exceptions, never silently map to another SKU.
- **STK-012** Session MUST have a clear scope and closure/completeness declaration; absent SKU ≠ physically zero until authorised complete scope reconciliation.
- **STK-013** Count submission MUST be online, fully synced, authenticated, idempotent and use an expected session revision; conflicts must be surfaced and resolved, never silently overwritten.
- **STK-013A** Approved submission architecture: the API MUST return HTTP 202 only after a PostgreSQL transaction durably records the submission job, session transition/lock and transactional-outbox event. A 202 response means accepted for processing only; it MUST NOT be presented as ERP-submitted, approved or posted. A bounded background worker consumes the outbox event through RabbitMQ, revalidates the server-acknowledged immutable count revision and invokes the existing ERP StockCount submission workflow while preserving the initiating actor for audit. PostgreSQL is authoritative; RabbitMQ is transport. This flow is API-05C and MUST NOT be implemented before the durable count-operation/revision protocol in API-05B.
- **STK-014** After successful server submission, counting records are read-only in mobile; correction/recount only via approved ERP workflow.
- **STK-015** POS Inventory → Stock Count MUST list mobile-submitted sessions and support existing ERP review, exception, approval, rejection and stock adjustment, guarded by permissions.
- **STK-016** ERP MUST post inventory adjustments at most once per approved, versioned reconciliation; no posting in mobile.
- **STK-017** Counts MUST not be compared against moving stock without baseline/movement handling; conflicts, mid-count sales/receipts and negative inventory must be handled via explicit policy.
- **STK-018** Allow authorised read-only comparison of expected, counted and variance after server calculation, including separate shortages/overages and status timeline.
- **STK-019** Official Excel and print-ready PDF MUST be generated by backend with outlet, timestamp/timezone, scope, baseline, SKU identifiers, system/count/variance and approval metadata.
- **STK-020** Audit MUST capture actor, device/session, event/action, prior/new count or delta, timestamp, outlet, correlation/request ID, reason and approval/posting lineage; privacy controls apply.
- **STK-021** Concurrent devices editing the same stock take MUST be either explicitly disallowed via leased ownership or supported by conflict-safe per-event merging; policy MUST be decided before implementation.

**Implementation status (API-05A/API-05B):** The backend currently provides authorized online session creation/reads and durable revision-checked `SetQuantity` operation batches with idempotent receipts. This is only a limited subset of the requirements above: it does not provide a catalogue revision pin, a counting lease, undo/compensation, scan-increment workflow, unknown-barcode exceptions, movement reconciliation, Mobile submission, ERP review/result history, or a local durable Flutter queue. An accepted API-05B receipt is not an inventory posting or an approval. Baseline movement reconciliation remains unresolved, and ERP adjustment-draft generation fails closed for such Mobile counts. See [API-05B acceptance](./Planning/37_API05B_STOCK_TAKE_COUNT_SYNC_ACCEPTANCE.md); do not treat SRS requirements as fulfilled by route existence.

### 3.4 Offline and synchronization
- **SYNC-001** Product/barcode index and active session must be securely stored on device before offline counting begins.
- **SYNC-002** Every scan/correction MUST be committed to local durable storage before showing success; UI reads from local repository stream.
- **SYNC-003** Sync uses unique immutable operation IDs, ordered/revisioned batches, acknowledgements, retry with backoff and idempotent backend ingestion.
- **SYNC-004** Retries MUST NOT duplicate scan quantities; partial batch acknowledgement MUST be recoverable.
- **SYNC-005** No silent data loss on crash, reboot, network change, expired login or application update.
- **SYNC-006** Show local pending count, last sync, offline, conflict and sync-failed statuses accurately; network connectivity signal is not proof of API reachability.
- **SYNC-007** Account logout or switching tenant with unsynced operations MUST warn/block until explicit managed resolution; prevent cross-tenant data leaks.
- **SYNC-008** Catalogue version mismatches, SKU deactivation and barcode remapping MUST produce resolvable exceptions, not automatic incorrect reconciliation.

### 3.5 Insights and forecasting
- **INS-001** Show top/slow-moving products, stock velocity, stock-out risk, design/category/size/colour demand, discounts and trend comparisons, scoped by permissions.
- **INS-002** Define all metrics; label discount dependency as descriptive rather than causally proven.
- **FCST-001** Provide 1, 2 and 3 calendar-month horizons (or clearly labelled rolling-day horizons; decision required), outlet/category/design/colour/size filters, forecasts and uncertainty ranges.
- **FCST-002** Forecast input MUST account for sales, returns, promotions, discounts, price, stock-out/censored demand, holidays (including Eid), seasonality, product lifecycle and available historical coverage where data permits.
- **FCST-003** Forecast output MUST carry model version, generation timestamp, horizon, training cutoff, actual history coverage, fallback indicator and quality/backtest metrics where validated.
- **FCST-004** Production recommendation MUST show demand, available stock, incoming quantities, safety stock, lead time, suggested net quantity and explanation; advisory only.
- **FCST-005** Allow what-if changes to safety stock, lead time and proposed units; distinguish simulation from saved business decisions.
- **FCST-006** Explainable AI summaries MUST be grounded solely in forecast/ERP facts, clearly separate facts from estimates, and gracefully degrade when LLM unavailable.
- **FCST-007** If insufficient data, show transparent fallback/not-enough-data rather than invented confidence or false precision.
- **FCST-008** Evaluate hierarchy consistency (business → outlet → category → design → variant), cold-start designs and forecast reconciliation. Predictions MUST NOT imply unproduced designs can be identified without attributes/external design-trend evidence.

### 3.6 Notifications, profile and observability
- **NOT-001** Grant-scoped alerts for submission/approval/rejection, stock risk, performance and new forecast; web and Android in-app delivery is required for enabled rules, with durable persistence and configurable recipient policies; mobile push is a separate optional channel.
- **PRF-001** Show profile, grants/outlets, scanner preferences, sync state, support info and logout.
- **OBS-001** Correlate mobile operations with backend requests; no credentials, raw tokens or confidential inventory data in telemetry.

## 3.7 Configurable, reliable notification management — Web and Android (NEW v1.1)

**Scope:** A shared, tenant/business-scoped notification subsystem in existing RetailPOS backend, consumed by Angular POS/ERP and Flutter Android. Notifications are server-generated business records, not device-local alerts. In-app notification is **mandatory on both clients** for enabled business rules; OS-level mobile push, email and SMS are optional future channels. Do not create a separate notification microservice for MVP. Reuse existing durable outbox/inbox, permission, audit and background worker facilities where verified.

### 3.7.1 Requirements
- **NOT-002 — Notification type catalogue:** Maintain versioned, stable code, name, description, category, severity, supported channels, audience scope, enabled default and permitted template variables for each type. Seed type definitions and proposed defaults are enumerated in §3.7.4; do not invent producers for unsupported types. Business-specific configuration may enable/disable types without deleting historical records.
- **NOT-003 — Policy and rules:** Authorised administrators configure enabled/disabled, conditions (supported metric/operator/threshold), threshold unit, inclusive/exclusive comparison, scope (business/outlet), effective date and recipient resolution policy. Example: on a **finalised sale** where applied discount percentage is strictly greater than 5%, notify authorised business-owner recipients. Define discount denominator (eligible gross line total vs total invoice, taxes/returns/stacked promotions) in an ADR before release; do not infer from UI values. Avoid notifications for drafts and failed/rolled-back sales. Sale corrections/voids require separately agreed handling.
- **NOT-004 — Recipient configuration:** Per notification type/policy assign eligible recipient users, roles, and/or business-owner dynamic recipients, optionally with outlet scope. Resolve users server-side at event processing time from current effective grants, business membership and active-account status; de-duplicate user recipients and never leak across tenants/businesses. Role assignments must adapt when owner users change. Explicit users still require resource visibility.
- **NOT-005 — Configuration UI (Angular):** Under permission-gated ERP Settings → Notifications provide type catalogue, rule editor, scope/recipients selector, enable toggle, validation, preview/test of rule (dry-run without business event), and rule history/audit. Do not allow arbitrary code or unrestricted expression evaluation. Provide grants `notification.config.view`, `notification.config.manage`, `notification.inbox.view`; align with existing permission catalogue conventions.
- **NOT-006 — User inbox (Angular + Flutter):** Same server-backed inbox showing ordered recent notifications, severity, title, summary, event time, read/unread, deep link to an authorised resource, pagination, filters (unread/type), unread count, mark-read and mark-all-read; client actions re-authorised. Support empty/loading/offline and revoked resource states. Do not expose sensitive detail on unauthorised devices.
- **NOT-007 — Durable generation:** Successful qualifying business transaction and event-outbox record committed atomically in the ERP database where possible; background worker processes after commit. Notification and per-recipient delivery records MUST be durable, idempotent and retried on transient failures; no lost events on worker crash; duplicate broker delivery MUST NOT produce duplicate recipient notifications.
- **NOT-008 — Delivery semantics:** Guaranteed persisted in-app inbox creation for eligible recipients under eventual delivery; **at-least-once processing with idempotent effect**, not false exactly-once end-to-end claims. Real-time SignalR/WebSocket hints MAY supplement; clients always reconcile from authoritative inbox API after reconnect/focus. A temporarily offline client does not block event capture.
- **NOT-009 — Reliability and transparency:** Persist processing status, attempt count, next attempt, error category, creation/delivery timestamps and correlation ID. Bounded exponential retry with jitter and dead-letter/quarantine plus operator replay and alerting. Poison messages must not block healthy messages indefinitely. Define p95 event-to-inbox latency SLO, retention, and retry ceilings during discovery.
- **NOT-010 — User preferences:** Individual opt-out may apply only to notification types explicitly marked optional; mandatory compliance/security/critical alerts cannot be disabled by recipient without approved policy. Business policies and user preferences have documented precedence.
- **NOT-011 — Security/privacy:** Tenant, business, outlet and row-level authorisation on all admin/inbox APIs; field redaction, safe templates, no unrestricted URL redirects, no unauthorised sale or discount detail, no PII in push-preview payload; audit every configuration change and operator replay.
- **NOT-012 — Event lifecycle:** Type/rule versions and event-time data snapshot are preserved for audit; resolve recipient list using an explicit documented timing policy. Track configuration version used, so changes do not silently reinterpret already-generated events.
- **NOT-013 — Cross-client consistency:** Read state is per recipient and shared across Angular and Flutter, with idempotent read updates; no reliance on device clock. Acknowledgement of real-time transport is not equivalent to inbox persistence.
- **NOT-014 — UX:** Notification bell + unread badge on both app shells; Angular settings access only with grant; mobile notification centre and deep link respecting role and outlet. Example event displayed: “Sale discount exceeded 5%” with actual discount and link only for a viewer authorised for that sale.

### 3.7.2 Required UAT examples
1. A completed 5.01% discount sale triggers one owner notification when rule is `> 5%`; exactly 5.00% and 4.99% do not. Confirm denominator and rounding policy against approved ADR.
2. Sale rollback/failure generates none; duplicated outbox/broker events generate one notification per resolved recipient.
3. Two eligible owners receive separate inbox records; unrelated business owners, suspended users and unauthorised users receive none.
4. If recipient is offline and notification worker restarts mid-processing, the message appears after recovery without duplicates.
5. Changing a recipient role/config creates an auditable policy version; future events obey current approved policy without corrupting prior recipient records.
6. Opening on Android and marking read causes Web inbox/unread count to converge; deep links deny access after permissions are revoked.
7. Rule changes and replay require proper privileges; missing outbox, worker/DB failure and poison events appear in operational alerts, not silent success.

**NFR-NOT-001:** Proposed SLO: 99% of eligible finalised-sale notifications persisted to recipient inbox within 60 seconds under healthy service; measure before approving for production. **NFR-NOT-002:** Zero permanently lost eligible events under tested crash/replay scenarios, within documented retention and recovery assumptions. **NFR-NOT-003:** Index inbox queries and enforce retention/archival, page size and rate limits. **NFR-NOT-004:** Worker lag, undelivered age, retry and poison metrics required in operations monitoring.

### 3.7.3 Open notification ADRs (BLOCK rollout until resolved)
- **ADR-N01:** Canonical event source and sales finalisation point, and definition of discount percentage (line vs invoice, discount stacking, tax, returns).
- **ADR-N02:** Rule precedence, default thresholds, strict vs inclusive comparator, thresholds per business/outlet and bulk-sale suppression.
- **ADR-N03:** Recipient resolution timing (at event vs processing), account/role changes, optional user preferences and mandatory alerts.
- **ADR-N04:** Inbox retention, event payload minimisation, future push/email support and delivery SLO.
- **ADR-N05:** Current ERP outbox/worker/SignalR infrastructure reuse, deployment topology, and migration strategy.

### 3.7.4 Extended notification type catalogue (v1.2)

**NOT-015 — Expanded seed registry:** The following is the proposed, versioned catalogue for future implementation. These are **candidate supported types, not claims of existing ERP events**. Before enabling any type, Copilot MUST verify that its producer/transition exists and that the required transactional data is available. All non-core types default OFF, with explicit rollout per business and outlet. Severity, recipients and delivery policy are proposed defaults only, not hard-coded authorisation. Every type requires a typed source event/schema, template allowlist, business/outlet scope, eligible roles, deduplication key, delivery mode, suppression/cooldown and test coverage.

| Category | Stable type code | Business trigger / condition | Suggested recipient(s) | Default severity | Delivery policy | Initial rollout |
|---|---|---|---|---|---|---|
| Sales | sales.discount.threshold_exceeded | Finalised sale, effective invoice discount > configured % | Owner; selected manager | High | Immediate | Off; threshold example 5% |
| Sales | sales.discount.anomalous | Finalised sale discount deviates from configured category/outlet benchmark | Owner; manager | High | Immediate / digest | Off; benchmark configured |
| Sales | sales.refund.completed | Posted sale return/refund above optional amount threshold | Owner; manager | Medium | Immediate / digest | Off |
| Sales | sales.large_sale.completed | Finalised sale amount >= configured BDT threshold | Owner; manager | Info | Digest / immediate | Off |
| Sales | sales.target.achieved | Approved daily/monthly target first crossed | Salesperson; manager | Info | Immediate | Off |
| Sales | sales.target.missed | Closed reporting period below target threshold | Manager; owner | Medium | Scheduled digest | Off |
| Sales | sales.performance.drop | Completed comparison window falls below baseline by configured % | Owner; manager | High | Scheduled digest | Off |
| Inventory | inventory.low_stock | Available stock crosses configured reorder threshold | Manager; owner | Medium | Digest / immediate | Off |
| Inventory | inventory.critical_stock | Available stock crosses critical threshold | Manager; owner | High | Immediate | Off |
| Inventory | inventory.out_of_stock | Available stock transitions to zero for monitored SKU/outlet | Manager; owner | High | Immediate | Off |
| Inventory | stocktake.submitted | Authorised stock take successfully submitted | Inventory reviewer; manager | Medium | Immediate | On if stock take live |
| Inventory | stocktake.rejected | Stock take reviewer rejects submitted count | Count creator; manager | Medium | Immediate | On if stock take live |
| Inventory | stocktake.approved | Stock take is approved in ERP | Count creator; manager | Info | Immediate | On if stock take live |
| Inventory | stocktake.variance_detected | Reconciled count detects non-zero variance above rule tolerance | Reviewer; owner | High | Immediate | Off |
| Inventory | inventory.adjustment.posted | Authorised stock adjustment committed | Inventory manager; owner | Medium | Digest / immediate | Off |
| Inventory | stocktransfer.approval_required | Transfer enters approval-required state | Assigned approver | Medium | Immediate | Off / feature gated |
| Inventory | stocktransfer.received | Transfer receipt is posted to target outlet | Origin/target manager | Info | Digest | Off / feature gated |
| Purchase | purchaseorder.approval_required | PO enters approver queue | Assigned approver | Medium | Immediate | Off / feature gated |
| Purchase | goodsreceipt.completed | GRN completed/posted | Purchasing manager; inventory manager | Info | Digest | Off |
| Accounting | expense.large_submitted | Expense submitted above configured amount | Accounting manager; owner | Medium | Immediate | Off |
| Accounting | expense.approval_required | Expense enters approval stage | Assigned approver | Medium | Immediate | Off |
| Accounting | supplierpayment.completed | Supplier payment successfully posted | Accounting manager; owner | Info | Digest | Off |
| Accounting | cashsession.variance_detected | Cash session closing/reconciliation variance exceeds tolerance | Accounting manager; owner | High | Immediate | Off |
| Forecasting | forecast.ready | Scheduled or requested forecast run published successfully | Owner; authorised manager | Info | Digest / immediate | Off / feature gated |
| Forecasting | forecast.stockout_predicted | Model predicts stockout within configured horizon with eligible data quality | Owner; manager | High | Scheduled digest | Off / feature gated |
| Forecasting | forecast.production_recommendation_ready | Approved advisory production recommendation run published | Owner; authorised planner | Medium | Digest | Off / feature gated |
| System | system.background_job_failed | Relevant scheduled/durable job repeatedly fails after retry policy | IT admin; ops | High | Immediate + escalation | On for ops after readiness |
| System | system.notification_delivery_failed | Notification consumer dead-letter/backlog or delivery SLO breach | IT admin; ops | High | Independent ops alert | On for ops after readiness |

**NOT-016 — Typed policy support:** Allow per-type typed configuration only for supported fields (e.g. discount %, BDT amount, low stock units or days of cover, variance amount/units, prediction horizon, change %, target window); validate compatible operators and units. Rules MUST NOT allow arbitrary expressions, scripts or raw SQL. Specify whether threshold crossings are edge-triggered vs level-triggered; ensure sustained low stock does not spam on every inventory write. Threshold defaults are examples and are not automatically applied to all businesses.

**NOT-017 — Event timing and aggregation:** Distinguish **immediate transactional events**, **scheduled evaluations** (end-of-day target missed, performance decline, stockout forecast), and **digest aggregation**. Use an approved Bangladesh business calendar, outlet-local timezone and business-day cutoff for daily/monthly windows. For scheduled events, persist an evaluation-run identity/window and idempotency key; digest bundling must not discard the underlying recipient-level evidence.

**NOT-018 — Suppression and delivery controls:** Support per-type cooldown, dedupe window, quiet-hours/digest preference, severity routing, rate ceilings and escalation for critical operations. Mandatory security/system alerts cannot be silently disabled; failures of the notification pipeline MUST also alert through **independent** operational monitoring (not only the same failing notification pipeline). Show reasons for suppressed events in privileged diagnostics, not as user-visible inbox messages.

**NOT-019 — Event provenance and financial safety:** Trigger only from authoritative post-commit transitions or approved materialised evaluation snapshots. Prevent duplicate notifications on replays, backdated corrections, void/reversal, stock changes inside one transaction, and duplicate scheduler execution. High-discount metrics, cash variance, refund and gross-margin sensitive fields must be derived from approved ERP policies and redacted per viewer grant.

**NOT-020 — Feature-gated categories:** Types tied to unavailable capabilities (transfers, PO approvals, forecasting models, targets) remain registered as **disabled / not available** until verified implementations and acceptance gates pass. In-app inbox is mandatory for enabled types across Angular and Flutter; admin edit/config UI stays Angular-only for MVP. System operational events may use a restricted admin inbox and external monitoring, never ordinary sales staff.

**NOT-021 — Template/localisation and actionability:** Version per-type title/body templates, allowlisted variables, safe authorised deep-link resource types, severity display, accurate event time, and optional localisation; redact customer/payment/employee-sensitive data. Drill-down authorization MUST be checked again when opened, including after role changes.

### 3.7.5 Expanded notification acceptance matrix (v1.2)

- Every seeded type maps to a verified producer or a documented blocked/feature-gated dependency, to a `source event -> policy -> user inbox` integration test, and to a permissions test.
- Trigger boundary test each numeric rule: below, exactly equal, and above; use decimal arithmetic and approved units/rounding. For `sales.discount.threshold_exceeded`, `> 5%` means 5.00% is not eligible while 5.01% is.
- Inventory low/critical/zero notifications occur once per transition or cooldown window, not once per sale/write; stock transfer and GRN notification occur only at authoritative posted states.
- Closing-period and forecast scheduled rules generate once per `(business, outlet, type, period, evaluation version)` even across two concurrent schedulers and replay. Reopened periods and late corrections follow approved re-evaluation policy.
- Target-achieved and target-missed rules cannot fire contradictory events for the same stable window without an explicit version/correction policy.
- Retry/replay, broker duplicate delivery, multi-recipient fan-out, policy updates, user account disabling, cross-tenant isolation, Angular/Flutter read-state convergence and independent ops failure alerting all have passing automated evidence before enabling production rules.


## 3.8 Offline product catalogue and barcode reference data (NEW v1.3)

**Ownership:** The ERP product/variant/attribute catalogue and business/outlet eligibility are authoritative. Flutter maintains an encrypted-at-rest where feasible, tenant-partitioned, read-only catalogue projection to support fast offline barcode lookups, *not* a product master and not an inventory source of truth. The project must inspect the actual Product, Design, SKU, Attributes and Barcode schema first; field names and endpoint contracts below are proposed.

| ID | Testable requirement |
|---|---|
| CAT-001 | Prior to offline count, download a complete, integrity-checked catalogue covering the authorised business, outlet and declared stock-take scope; report ready status only after durable local commit. |
| CAT-002 | A local SKU projection MUST include opaque ERP product ID, product name, main/product code, optional design code, variant/SKU ID, active status, barcode(s)/aliases, typed attribute IDs/names/values (e.g. Colour: Black, Size: XL), catalogue revision, and optional display category. Store original identity, never infer SKU from display labels. |
| CAT-003 | Support arbitrary ERP-defined attributes/values, not only fixed colour and size columns. Attributes changing with time require versioned snapshots or a stable session mapping. |
| CAT-004 | Barcode → exactly one eligible SKU within business/outlet/scope for the session revision; ambiguous, duplicate, deactivated, missing, or unrecognised barcodes MUST be flagged, not guessed or counted against another SKU. Preserve leading zeros and distinguish barcode symbology/normalisation policy. |
| CAT-005 | Allow an explicit initial snapshot and incremental catalogue sync with opaque cursor/revision, tombstones, stable ordering, pagination, bounded batch sizes, integrity checking, atomic apply and full-resync recovery after cursor expiry. |
| CAT-006 | Check server catalogue freshness and assigned outlet/scope eligibility before a session starts online. Offline continuation is permitted for an already authorised prepared session under a documented offline validity policy; new-session offline creation requires an explicit ADR. |
| CAT-007 | Pin SKU/barcode identity mapping and baseline catalogue revision to each stock-take session. Refreshing a global catalogue MUST NOT silently remap counts or change an in-progress session's resolved SKU. |
| CAT-008 | Offline unknown barcode occurrences MAY be durably retained as unresolved observations, with count and scan history, but MUST NOT be included in accepted SKU totals or submitted as reconciled inventory lines until an authorised online resolution. |
| CAT-009 | Newly created or modified ERP products must become available after a successful catalogue sync; while disconnected, the app MUST transparently display the last successful sync time, revision and any stale/unsupported status. |
| CAT-010 | Keep offline projection minimised to scanning requirements; do not download costs, margins, confidential financial values, or full-resolution product images unless separately authorised. |
| CAT-011 | Partition catalogue and pending count data by business, outlet, principal and session; enforce access revocation on the server and define secure local wipe/retention rules on logout, account switch and device loss. Pending unsynced data requires managed recovery, not silent deletion. |
| CAT-012 | On submission the ERP MUST validate session ownership, scope, catalogue revision and every resolved SKU against server-side mapping and report changed/deactivated SKU exceptions explicitly. |
| CAT-013 | Search offline by barcode, name, main code, design and selected attribute values, with performant indexed retrieval and bounded result lists on budget Android hardware. |
| CAT-014 | Optional thumbnails are downloaded only on demand with a bounded cache and fallback; product images are not necessary to consider a session offline-ready. |
| CAT-015 | Catalogue sync failures, hash mismatches, incomplete pages, app termination mid-import and partial writes MUST leave the last verified snapshot usable without a false 'ready' indicator. |

### 3.8.1 Catalogue UAT scenarios

- **CAT-UAT-01:** Download a scope-restricted full catalogue, disconnect the internet, scan one known barcode three distinct times; correct name/main code/all dynamic attributes display and count equals three after app restart.
- **CAT-UAT-02:** Add a product and modify an attribute in ERP; reconnect and sync; the next prepared session sees changes without altering existing in-progress session mappings.
- **CAT-UAT-03:** Two active SKUs share a barcode in the same scope; app refuses automatic counting, logs ambiguity and permits audited resolution.
- **CAT-UAT-04:** Scan an unknown barcode while offline; record unresolved scans, recover online, resolve only against authoritative ERP identity, reconcile without double count.
- **CAT-UAT-05:** Simulate interrupted download, corrupted page, expired cursor, device restart and logout with pending operations; recover without data loss or cross-business leakage.
- **CAT-UAT-06:** Server retires/remaps an SKU between download and submission; reject/reconcile explicitly rather than silently mapping inventory to the replacement.

## 3.9 Long-term architecture and engineering governance (NEW v1.3)

| ID | Mandatory nonfunctional acceptance |
|---|---|
| ARCH-001 | One Flutter Android app under `pos-v2/retailpos-mobile/`, independent from Angular `retailpos-frontend/` and ASP.NET Core `src/`, with independent build pipeline. |
| ARCH-002 | Feature-first modular Clean Architecture; presentation, domain/application use cases, data boundaries; domain rules independent of UI, transport and persistence. |
| ARCH-003 | Enforce allowed/forbidden imports with CI architecture tests/lint checks; prohibit circular feature dependencies and feature-internal cross-imports. |
| ARCH-004 | Define each feature's explicit public contracts and composition-root dependency injection; forbid service locators accessed directly from domain entities. |
| ARCH-005 | Establish versioned API schemas and contract tests, backward-compatible rollout strategy and error codes; use existing ERP capabilities rather than duplicated business rules. |
| ARCH-006 | Use approved design tokens/components based on IraniMart Figma Make and actual logo assets; mobile UI remains native Flutter, not copied web React code. |
| ARCH-007 | Require automated unit, widget, integration, API contract, concurrency/offline-recovery, access-isolation and regression tests as appropriate to each slice. |
| ARCH-008 | Pin compatible stable dependency versions via `pubspec.lock`, document SDK compatibility, periodically audit vulnerable/deprecated libraries and preserve reproducible CI builds. |
| ARCH-009 | Maintain ADRs for auth/permissions, cache ownership, offline lease/freshness, catalogue sync, cross-feature contracts, monitoring, and future package/service extraction; decisions requiring approval block dependent implementation. |
| ARCH-010 | Define observability for errors, scan/sync success/failure, catalogue freshness, latency and operational notification delivery without collecting unneeded PII or leaking financial data. |
| ARCH-011 | Modularise for expansion, but avoid unused abstraction layers, speculative multi-package scaffolding and premature microservices. Extract Dart packages/services only on evidence (reuse, deployment isolation, organisational ownership or scale). |
| ARCH-012 | All Copilot-generated implementation slices must reference requirement IDs, pass architecture and regression gates, and receive explicit approval before moving to the next phase. |

**Open decision gates:** ADR-CAT-01 (barcode uniqueness/aliases and scope), ADR-CAT-02 (snapshot consistency and session pinning), ADR-CAT-03 (sync cursor/tombstones and full-resync), ADR-CAT-04 (offline validity/revocation), ADR-CAT-05 (unknown barcode resolution), ADR-CAT-06 (cache encryption, retention and recovery), ADR-ARCH-01 (feature contracts/DI), ADR-ARCH-02 (contract versioning), ADR-ARCH-03 (package extraction and CI enforcement).

## 4. Nonfunctional requirements
- **NFR-001 (proposed):** Scan-to-local-commit p95 ≤ 150 ms on an agreed test Android device with warmed index; no network in scan hot path.
- **NFR-002 (proposed):** 5,000 scans / 2,000 unique SKUs/session with no UI blocking; capacity and device class to be measured in pilot.
- **NFR-003:** Offline counts survive process kill and reboot and reproduce exact quantities without doubles/loss.
- **NFR-004:** Mobile app remains responsive under poor connectivity and never requires connection for an individual scan after local preparation.
- **NFR-005:** TLS required; Android Keystore-backed credential storage, encrypted sensitive data-at-rest subject to threat model, secure logout, no secrets in app binary.
- **NFR-006:** Accessibility: legible fonts, touch targets, screen reader labels, high-contrast variance signals, not colour alone.
- **NFR-007:** Internationalisation-ready (English initial), BDT, explicit Asia/Dhaka outlet-local dates and UTC timestamps on wire.
- **NFR-008:** Structured logs, crash capture (approved privacy settings), metrics, tracing/correlation and release health dashboard.
- **NFR-009:** Build variants dev/staging/prod; reproducible pinned dependency resolution and signed Android release.
- **NFR-010:** Tenant/business/outlet isolation and least privilege verified in integration and security tests.
- **NFR-011:** Retention periods for local sessions, server count events, documents and telemetry require policy approval.
- **NFR-012:** Forecast business metrics MUST be monitored for drift, accuracy, bias across sparse SKUs and training/inference failures.

## 5. Integration boundaries
- Existing ERP remains system of record for users, permissions, products, outlets, stock, movements, count status, approval/adjustment, audits and official exports.
- The mobile app uses versioned HTTPS APIs and server-enforced grants; no direct ERP database access or broker credentials on phone.
- Forecasting service (later phase) consumes authorised, minimised ERP sales/inventory events or governed aggregates through outbox/inbox and durable messaging. No shared writable ERP tables.
- Figma Make prototype is a design reference, not implementation source, auth provider or backend specification.

## 6. Business rules requiring approval (decision register)
| ID | Question | Provisional approach |
|---|---|---|
| ADR-M01 | Stock baseline for moving inventory? | Server snapshot + movement-aware reconciliation |
| ADR-M02 | Multi-device/session concurrency? | Single counting owner for MVP, explicit transfer/lease; never implicit merge |
| ADR-M03 | Zero quantities for unscanned SKUs? | Only after explicit full-scope completeness declaration |
| ADR-M04 | Manual count correction reason? | Audit every correction; require reason when configured |
| ADR-M05 | Mobile identity flow? | Reuse existing ERP auth; review whether OIDC PKCE available |
| ADR-M06 | Export entitlement and sensitive columns? | Field-level permission checks on backend |
| ADR-M07 | Forecast period meaning? | Calendar-month horizons, with timezone-aware period definition |
| ADR-M08 | Forecast rollout? | Start with existing ERP forecasting read contract; split service when justified |
| ADR-M09 | Target definitions and KPI calculation? | Use one server-owned metric catalogue |
| ADR-M10 | Session/scan retention, device policy? | Confirm with owner before release |

No decision marked provisional is approved solely by this document. Block affected production slices until decided.

## 7. Phasing and acceptance
### Phase A — Discovery & contracts
Inventory current ERP stock movement, RBAC, stock-count features, reports, audit/outbox, product barcode uniqueness and existing API conventions; freeze ADRs and API schema. **Exit:** signed inventory of reuse vs new endpoints and approved security model.

### Phase B — Flutter foundation & dashboards
App shell, auth, protected routing, theme/logo assets, role/outlet filters, reporting repository, tests. **Exit:** owner/manager/salesperson screens show correct scoped API data and denied fields never returned.

### Phase C — Stock take vertical slice
ERP session, local durable scan, offline replay, conflict handling, review, submit, POS review + approval + once-only adjustment, reports, audits. **Exit:** all STK/SYNC requirements demonstrated end-to-end with Android device and PostgreSQL-backed tests.

### Phase D — Insights
Product and discount analytics with source-calculated metrics and drilldown. **Exit:** totals reconcile against ERP for fixture data, field grants enforced.

### Phase E — Forecasts
Model baseline, backtesting, read API, explainable dashboard, recommendation simulation. **Exit:** recorded forecast quality vs naïve baseline, transparent sparse-data handling and approval for production.

### Phase F — Harden & rollout
Threat review, Android device tests, reliability/load, observability, UAT, staged rollout, rollback plan, support procedures.

## 8. Minimum end-to-end UAT scenarios
1. Salesperson cannot access profit, other outlets, or unauthorised forecast endpoints by UI or direct HTTP.
2. Repeated intentional scans of same barcode increment exactly; stationary barcode does not overcount automatically.
3. App terminated offline after 250 scans; restart retains exact count and local event history.
4. Same event batch retried 3 times; server count remains unchanged after first successful acknowledgement.
5. Two conflicting submissions with same session revision: one commits, one returns recoverable conflict.
6. Mid-count ERP sales/GRN movement does not create unflagged false variance.
7. Full scope vs partial scope properly distinguishes unscanned from zero-count items.
8. Submitted count becomes mobile read-only; ERP approval/adjustment occurs exactly once and is audited.
9. Excel/PDF shows accurate baseline, physical quantity, variance, scope and statuses, with permission-filtered fields.
10. Changing business/user leaves no accessible previous tenant offline cache.
11. Forecast with insufficient data shows fallback/uncertainty, not fake accuracy.
12. Mobile offline scanner has no synchronous network dependency.

## 9. Traceability and change management
The development plan MUST map every NOT-002..NOT-021 and NFR-NOT-001..004 to backend, Angular, Flutter and PostgreSQL integration tests; include a standalone notification delivery gate before hardening.

LLD sections and automated test names SHOULD reference requirement IDs. Changes to functional rules, status transitions, permission scope, API payloads, baseline or forecast math require SRS revision, corresponding LLD change, tests, and explicit approval. Copilot MUST NOT implement unresolved decisions as silent assumptions.

## 3.9 Main Product Code and unique variant barcode (normative, v1.4)

**Business rule:** A main product/design identifies a *family* of sellable variants, not a stock-countable SKU. For example, main product code `ABA100P` may have three colours (Black, White, Navy), each offered in four sizes (M, L, XL, XXL): **12 distinct variants, each with its own unique barcode**. These codes have different meanings and MUST never be conflated.

| ID | Mandatory requirement |
|---|---|
| PROD-001 | Each ERP main product has a stable `ProductId`, human-readable `ProductName`, and business-scoped `MainProductCode` (e.g. `ABA100P`); uniqueness and legacy collisions MUST be audited against the existing ERP model before choosing constraints. |
| PROD-002 | Each permitted combination of attributes/values represents a distinct sellable `VariantId`/SKU. Size and colour are illustrative; arbitrary existing ERP attribute/value dimensions MUST be supported. |
| PROD-003 | Every active stock-countable variant MUST resolve from its **unique primary barcode** within the agreed business/catalogue scope. Barcode uniqueness, symbology, allowed aliases, replacement history and existing legacy collisions require discovery/ADR approval. |
| PROD-004 | Both `MainProductCode` and `Barcode` are mandatory fields in offline catalogue payloads, on the scanning/review screen and in the stock take Excel/PDF report. Display Product Name, attribute names and values alongside these identifiers. |
| PROD-005 | Stock count operations and reconciliation MUST use the authoritative `VariantId` identity, not `MainProductCode`, product name, display attribute strings or a mutable barcode as the primary identity. |
| PROD-006 | Scanning one barcode MUST increment only its resolved variant. Scanning another size/colour barcode of the same main product MUST increment a separate variant count. |
| PROD-007 | Full and incremental catalogue sync MUST carry parent product identity and fields, variant identity and status, attribute/value IDs and display names, primary barcode and approved aliases/deactivations. Revisions MUST be applied atomically while preserving pinned session mapping. |
| PROD-008 | Barcode string values MUST be preserved losslessly (including leading zeros); they MUST NOT be parsed as numbers, trimmed/reformatted destructively or synthesised from Main Product Code and attributes. |
| PROD-009 | Manual search and review MUST support Main Product Code, Product Name, Barcode and attribute/value filters, but selecting a parent product alone MUST NOT create a count without selecting the exact variant. |
| PROD-010 | Unknown, duplicate, ambiguous, missing, retired or reassigned barcodes MUST produce explicit exceptions. No automatic guesses, silent reassignment, or cross-business identity leakage. |
| PROD-011 | ERP reports MUST support one row per variant with separate `MainProductCode`, `ProductName`, `Barcode`, attribute/value columns or a consistent attribute representation, system quantity, physical quantity and variance; optional grouping/subtotals by Main Product Code MUST NOT replace variant rows. |
| PROD-012 | Catalogue schema/version evolution, barcode corrections and concurrent ERP product changes MUST preserve historical stock-take identity and enable deterministic offline replay and audit investigation. |

### 3.9.1 Acceptance examples

- Fixture: `ABA100P` × {Black, White, Navy} × {M, L, XL, XXL} = 12 `VariantId`s with 12 distinct primary barcodes. Scanning Black/XL three times and Navy/XL twice results in counts 3 and 2, not one main-product count of 5.
- With airplane mode enabled, barcode lookup displays `ABA100P`, name, Black/XL and exact barcode using the verified local cache; no per-scan network call.
- ERP adds White/XXL after initial sync: delta sync downloads that variant with the same parent Main Product Code and a new barcode, without corrupting an active session's pinned identity.
- Export and POS Stock Count show a separate row for each colour/size barcode, accurate variance per `VariantId`, and optional `ABA100P` subtotal.
- Leading-zero barcodes and renamed attribute labels round-trip unchanged; barcode replacement or ambiguity raises an explicit exception rather than mapping to a different variant.

**Implementation constraint:** These are required semantics, **not** instructions to add duplicate ERP product/variant tables. Copilot MUST first inspect actual product models, uniqueness, attribute value tables and barcode constraints, document verified mappings, and raise ADR-PROD-01–04 before any migration.

## v1.4 change record
- Added PROD-001–PROD-012 with distinct Main Product Code/variant barcode identity, offline projection, scan and export requirements; all v1.3 requirements retained.

## v1.3 change record
- Added CAT-001–CAT-015, catalogue UAT and ARCH-001–ARCH-012. All original v1.2 requirements retained.
- The catalogue/projection/API approach is proposed pending inspection of the current ERP codebase and schema.
