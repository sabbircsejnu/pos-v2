# Open Decisions and Risks

**Decision status:** all items are unresolved unless an accountable owner records approval in an ADR. Any item labelled **BLOCKS** must be resolved before dependent implementation or production enablement. Current repository evidence is distinguished from design proposals.

## Blocking architecture decisions

| ID / decision | Options and required evidence | Risk if assumed | Blocks / accountable approval |
|---|---|---|---|
| **ADR-AUTH-01 — Native authentication** | **Proposed:** reuse first-party login and rotating refresh only if Security approves this public-client pattern; no client secret. Use PKCE only with an approved IdP. Configure lifetimes, MFA, reuse/revocation and device-loss behavior. Existing system hashes/rotates refresh tokens and detects reuse; defaults are not policy approval. | Account compromise, refresh-token theft, unsupported client grant. | P1/P2; Security + Identity owner. |
| **ADR-AUTH-02 — Scope/context model** | **Proposed:** context list comes from server-authorized business/outlet access; every endpoint revalidates scope. Explicit switch confirmation and per-scope local data partition; stop writes after grant changes. | Cross-tenant/outlet access or stale scoped data. | P1/P2 auth; P3/P5 catalogue/count; Security + ERP owner. |
| **ADR-CAT-01 — Product visibility/ownership** | ProductCode reuse and ProductId→VariantId are verified. Product has no direct business/outlet fields; choose authoritative eligibility source and fail closed pending approval. | Wrong tenant catalogue or product data leakage. | P1 decision; blocks P3/P5 catalogue only; Product/data + Security owner. |
| **ADR-CAT-02 — Barcode uniqueness/normalization** | Separate Product and ProductVariant unique indexes exist; cross-table collision is possible. Exact mobile resolution must match one eligible variant barcode; preserve leading zeroes and raw payload. Version conservative, symbology-specific normalization; no arbitrary match. | Barcode resolves to wrong variant or ambiguous stock. | P1 policy; blocks P3/P5 scan path; Product + Inventory owner. |
| **ADR-CAT-03 — Canonical attribute projection** | Variation option joins and JSONB attributes both exist. Project relational named values plus arbitrary JSON keys; conflicting duplicate keys quarantine rather than silently precedence-select. Pin projection per revision. | Session mapping changes after catalogue update. | P1 contract; blocks P3/P5 data projection; Product owner. |
| **ADR-CAT-04 — Snapshot/revision/change capture** | Transactional snapshot and monotonic revisions vs rebuildable projection; cursor/tombstone retention and expiry. | Inconsistent snapshot/delta, missed product changes. | P2; Backend + DBA. |
| **ADR-CAT-05 — Unknown barcode resolution** | Authorized online mapping/manual exception workflow; ambiguous cases never guessed. | Incorrect variant count or unresolved operational loss. | P3/P5; Inventory + Product owner. |
| **ADR-CAT-06 — Cache protection/retention** | Android encryption approach, backup exclusion, wipe, tenant partition, device loss and unsynced operation recovery. | Confidential data exposure or silent loss. | P4/P5; Security + Privacy. |
| **ADR-STK-01 — Baseline and movement** | **Proposed resolution:** blind server baseline at open; ERP remains live; movement-aware reconciliation. Do not auto-compute variance from an offline observation with uncertain capture time. Example: baseline 20, sale -3, receipt +8, live 25; offline count 22 cannot safely imply -3 absent timing evidence—review/recount. | Misstated variance and incorrect ledger posting. | P4/P5; Inventory + Finance. |
| **ADR-STK-02 — Device concurrency** | **Proposed resolution:** one mobile editor lease/session; lease duration and heartbeat are validated configurable policy values, not hardcoded. 12-hour/5-minute are candidate values only. No automatic takeover; offline operations preserved after expiry but require online reauthorization; explicit supervisor takeover flags old-device pending operations for review. POS remains unlocked. | Duplicate/missing counts or unresolvable conflicts. | P4/P5; Inventory + Security + Operations. |
| **ADR-STK-03 — Scope completeness** | **Proposed resolution:** pin full outlet/category/explicit SKU scope. Partial-scope omissions are not zero; full-scope omissions become proposed zero only after explicit completion and authorized reconciliation; reviewer resolves omitted items. | Missing product interpreted as zero. | P4/P5; Inventory owner. |
| **ADR-STK-04 — Submission/audit retention** | **Proposed resolution:** immutable client-ID/payload-hash operations; exact replay returns same receipt, altered replay conflicts; online submit requires all ops resolved and expected revision; corrections via authorized ERP reopen/recount. Retain through finalization and at least approved legal/audit period (duration unknown). | Cannot audit or safely replay submitted count. | P4/P5; Audit/DBA. |
| **ADR-API-01 — Mobile API versioning** | Proposed `/api/mobile/v1/`; explicit DTO/OpenAPI, stable errors, idempotency, bounded paging, additive-compatible evolution and explicit deprecation policy. | Breaking mobile clients or duplicated contracts. | P1/P2 client contract; API owner + Architecture + Security. |
| **ADR-BRAND-01 — Design assets/tokens** | Owner confirms official logos supplied; identify/handoff source assets to proposed `retailpos-mobile/assets/brand/`. Approve proposed Gold/Brown/Ivory palette and visual mapping for every currently unverified screen. | Incorrect brand, inaccessible UI and unlicensed assets. | P1/P2 UI; Product/Brand owner. |
| **ADR-FORECAST-01 — Horizon and model quality** | Calendar-month vs rolling horizon, naive baseline, data sufficiency, stock-out censoring and acceptance thresholds. | False precision or poor operational decisions. | P8 only; Analytics/Business owner. Does not block P1/P2/P5. |
| **ADR-TOOL-01 — Flutter toolchain** | **Candidate baseline:** Flutter 3.47.7, Dart 3.13.5, Android SDK 36.0.0. Declared package constraints were checked; full resolver graph remains untested. Approve exact pins/lockfile, JDK 17 and compatible Android Gradle/Kotlin/tool versions after a clean candidate resolution. | Unbuildable or irreproducible app. | P1 policy; P2 build gate; Mobile tech lead. |
| **ADR-RET-01 — Data/telemetry retention** | Local unsynced data, server audit/operations, exports, crash traces and purge/legal policy. | Privacy exposure or loss of audit evidence. | P0/P4/P5/P9; Security/Privacy/Compliance. |

## Notification ADRs explicitly required by SRS

| SRS ADR | Open decision | Risk / required approver |
|---|---|---|
| ADR-N01 | Canonical event/finalization point and applied discount definition (line/invoice, stacking, tax, returns). | False/missed alert; Sales + Finance. |
| ADR-N02 | Rule precedence, defaults, comparator, business/outlet thresholds and bulk-sale suppression. | Alert spam or inconsistent policy; Product + Business. |
| ADR-N03 | Recipient resolution timing, role/account changes, optional preferences and mandatory alerts. | Unauthorized/missing recipient; Security + Business. |
| ADR-N04 | Retention, payload minimization, future channels, delivery SLO and retry ceiling. | Privacy/operational burden; Privacy + Operations. |
| ADR-N05 | Current outbox/worker/SignalR reuse, topology and migration strategy. | Lost/duplicate events or needless service; Backend + Operations. |

**Notification-specific gate (not a project-wide blocker):** before P7 enablement, prove sale transaction and outbox event atomicity; identify verified source events for each type; determine replay authorization; configure independent alerting. Notification ADRs do not block P1/P2/P5. The 28 candidate types and producer gates are recorded in `06_NOTIFICATION_ARCHITECTURE.md`.

## Ranked delivery risks

| Rank | Risk | Likelihood / impact | Mitigation and exit evidence |
|---|---|---|---|
| 1 | Catalogue ownership/scope and barcode integrity are ambiguous, including cross-table uniqueness and legacy ProductCode nulls. ProductCode itself is verified as Main Product Code. | High / Critical | Data profiling, owner decision and invariant tests before catalogue export. |
| 2 | Offline count races with live inventory movement or another device; baseline policy not agreed. | High / Critical | Blind count, session lease/versioning, movement-aware reconciliation and PostgreSQL concurrency tests. |
| 3 | Native auth flow and endpoint scope coverage are not yet approved/proven. | Medium / Critical | Security review, route-by-route permission matrix, cross-tenant negative integration tests. |
| 4 | Outbox exists, but sale-notification event is not verified as durable and transactionally atomic. | High / High | Prove same-transaction write, event payload completeness, replay and crash tests before enabling. |
| 5 | Required targets/discount/cost history may be incomplete, risking misleading dashboards/forecasting. | High / High | Data readiness report, metric dictionary, null/stock-out coverage, no-data fallback. |
| 6 | Figma brief and palette are accessible, but most screens lack visual approval and supplied logo files are not located in repository. | Medium / Medium | Obtain asset handoff and visual approval before screen sign-off. |
| 7 | Existing docs are stale in places; current migration chain differs from documented baseline. | Medium / High | Schema lineage audit and migration review against real deployments before DB changes. |
| 8 | Offline local storage, backup, logout and pending operations create privacy/data-loss tension. | Medium / High | Threat model and managed unsynced-data recovery policy; device-loss tests. |
| 9 | Flutter package latest versions may not resolve together; Freezed 4.0.2 exceeds candidate Dart baseline. | Medium / Medium | Lock exact SDK/packages in clean CI; omit incompatible generators until proven. |
| 10 | Notification/forecast types may be exposed as if implemented while sources are gated. | Medium / High | Registry availability flag, disabled defaults, producer acceptance gate and UI labels. |

## Approval and change control

1. P0 architecture and P1 auth/security/brand/API contract approval are required before dependent P2 implementation. P3/P4/P5 require their own product and stock-take gates.
2. Decision owners approve ADRs; no implementation should convert UNKNOWN/BLOCKED into an implicit default.
3. Forecast and notification ADRs gate only P8 and P7 respectively; neither blocks the full mobile project or offline stock-take vertical.
4. Changes to scope, thresholds, status transitions, metric formulas, API contracts or forecast math update SRS/LLD, traceability, tests and approval record.
5. A rejected decision may safely defer a feature behind a disabled gate; it must not silently substitute another data source or behavior.
