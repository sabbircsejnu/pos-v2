# Phased Development Plan

**Status:** proposed implementation sequence, not authorization to start coding. The present Phase 0 task ends with these documents. All implementation phases remain blocked until architecture approval and their entry gates pass.

## Ordered dependency graph

```text
P0: approve architecture + blocking ADRs
  -> P1: approve authentication, security, product identity/scope and brand contracts
      -> P2: Flutter Foundation/auth/secure shell may begin once auth/security/brand contracts are approved
      -> P3: backend catalogue revision/snapshot/delta
          -> P4: backend mobile stock-count session/operation protocol
P2 + P3 + P4: converge at P5: offline catalogue + stock-take client + ERP reconciliation/export (operational priority)
  -> P6: dashboards and sales insights
  -> P7: shared notifications (move ahead of AI forecasting; producers depend on verified source events)
  -> P8: forecasting bounded context
  -> P9: security, performance, device pilot and staged release
```

P2 is deliberately unblocked from backend catalogue/count implementation after authentication, security and brand contracts are approved; it must use approved API contracts/stubs and cannot freeze unapproved backend behavior. P3 may proceed alongside P2 after product identity/scope approval; P4 follows the catalogue contract and may proceed alongside P2. P5 is the operational delivery priority and requires P2, P3 and P4 gates. P6/P7/P8 are sequentially ordered here, with notifications ahead of forecasting; only producers with verified source events are enabled. Phase labels in this document and traceability matrix identify these slices.

## Validation command conventions

Run from repository root in PowerShell. These are future validation commands; none were run for this documentation-only task.

```powershell
dotnet build .\RetailPOS.slnx
dotnet test .\tests\RetailPOS.IntegrationTests\RetailPOS.IntegrationTests.csproj --filter "FullyQualifiedName~StockCount"
npm --prefix .\retailpos-frontend run build
npm --prefix .\retailpos-frontend test -- --watch=false
flutter analyze
dart format --output=none --set-exit-if-changed .
flutter test
flutter test integration_test
flutter build appbundle --flavor staging
```

Only run Flutter commands after app creation. For each new API capability add a focused test filter for its requirement/slice. Before each merge run the relevant focused suite plus .NET/Angular regressions if shared components are touched. Exact CLI/test-runner options must be confirmed against installed SDKs and project scripts.

## P0 — Architecture and P1 decision approval (this Phase 0/P1 gate)

- **Objective/scope:** approve architecture and settle decisions needed for P1/P2 plus dependent product/count contracts. Notification-specific ADRs gate only P7; forecasting decisions gate only P8. Neither is required to start the wider mobile program or offline stock-take path.
- **Dependencies:** current twelve planning documents; product/data-quality inspection; security and product/inventory owners.
- **Reuse:** existing SRS/LLD, auth, product/variant, stock-count, outbox and test architecture.
- **Proposed file/component changes:** only approved ADRs/API contract documents and planning updates; no runtime files.
- **API/database:** decide public-client flow, IDs/scope, versioning, stock-count idempotency and retention; no schema changes.
- **Security/audit:** threat model and least-privilege matrix approved.
- **Automated tests:** N/A; document review and evidence validation.
- **Acceptance:** Architecture, Security/Identity, Product/Inventory, API and Brand owners approve their P1 decisions; later-phase ADRs have named owners/gates and are safely deferred.
- **Validation:** check requirement traceability, review existing source evidence and verify no production files changed.
- **Regression:** no build/tests required for docs-only work.
- **Rollback:** revert only planning/ADR documents through normal review; retain current application unchanged.
- **Completion gate:** written P1 approval matrix decisions. No runtime implementation is authorized by this planning activity alone.

## P1 — API/auth foundation and product identity

- **Objective/scope:** establish mobile API versioning/error conventions, native-client auth/session contract, authorized business/outlet context and approved product/variant/Main Product Code/barcode identity.
- **Dependencies:** P0 approval; security, product and data owners; data-quality report.
- **Reuse:** existing auth/refresh/me, claims, `TenantAccessService`, `UserOutletAccessService`, product models and controller; clean architecture/CQRS and test patterns.
- **Proposed file/component changes:** Application/API request/response DTOs/handlers/policies; auth tests; API contract/schema tests; focused architecture docs.
- **API/database:** add/adjust mobile identity and capability contracts only as approved; no product identity migration until duplicate/orphan audit and chosen ownership model.
- **Security/audit:** validate token audience/rotation/revocation, all tenant/outlet checks, no client secret, sensitive claim minimization.
- **Automated tests:** login/refresh/revoke; role/grant/outlet matrix; cross-business negative tests; contract compatibility.
- **Acceptance:** exact authorized context and stable ERP IDs returned; all unauthorized scopes denied; schema/version documented.
- **Validation:** `dotnet build .\RetailPOS.slnx`; focused auth integration test filter; `npm --prefix .\retailpos-frontend run build` if shared auth changes.
- **Regression:** existing auth/authorization, Angular login and architecture-dependency suites.
- **Rollback:** additive endpoint/feature flag; preserve old auth routes and clients; forward-fix any applied schema, not destructive rollback.
- **Completion gate:** security owner approves native flow and tenant/product identity invariants; contract tests pass.

## P2 — Flutter foundation, session and secure shell

**P2-A foundation status:** the approved non-authentication bootstrap is
implemented and locally validated; see
[15_P2_A_FLUTTER_FOUNDATION_BOOTSTRAP.md](./15_P2_A_FLUTTER_FOUNDATION_BOOTSTRAP.md).
This completion does not authorize or complete P2-B: authentication, token
storage, business context, ERP API access and branded screens remain gated by
their ADR approvals and a separate review.

- **Objective/scope:** create `retailpos-mobile/` as approved standalone Android Flutter app; configure CI/toolchain, feature-first boundaries, theme shell, secure auth and routing.
- **Dependencies:** P0 approval; approved P1 authentication/security/brand contracts; exact Flutter 3.47.7/Dart 3.13.5/Android SDK 36.0.0 baseline and package graph resolved. Does not wait for P3/P4 implementation if versioned API contracts are approved.
- **Reuse:** SRS/LLD, approved IraniMart brand assets/tokens, API auth contract; no reuse of Angular code.
- **Proposed file/component changes:** Flutter bootstrap/core/network/security/database scaffolding, auth feature, route guard, theme, lint/CI and initial tests.
- **API/database:** auth/me/context endpoints only; no app-specific migrations on ERP in this slice.
- **Security/audit:** secure token storage, redaction, logout/tenant-switch warning, unauthorized route handling; server remains authoritative.
- **Automated tests:** auth state, refresh/expiry, route guards, secure-storage adapter, error states, architecture/import checks.
- **Acceptance:** pinned reproducible build; no domain/framework boundary violations; production has no demo role switch.
- **Validation:** `flutter analyze`; `dart format --output=none --set-exit-if-changed .`; `flutter test`; staging build.
- **Regression:** P1 API contract and auth integration suite.
- **Rollback:** no ERP data changes; disable mobile release/channel and revert app-only slice.
- **Completion gate:** reproducible CI build and security review.

## P3 — Authoritative catalogue manifest, snapshot and delta

- **Objective/scope:** implement bounded, stable snapshot/delta with revisions, tombstones, cursors and explicit barcode ambiguity.
- **Dependencies:** P1 identity/scope ADR and canonical attributes/barcode projection.
- **Reuse:** Product/ProductVariant/Variation data and repository/query layers; existing barcode lookup; PostgreSQL and API paging conventions.
- **Proposed file/component changes:** catalogue read model/service, handlers/controllers/DTOs, migration/change capture if required, contract and data-quality tests.
- **API/database:** manifest, snapshot pages, incremental changes and cursor-expiry behavior; revision/tombstone/change source according to approved design.
- **Security/audit:** authorized business/outlet eligibility; minimize catalogue fields; no costs/margins; audit privileged exports/config changes.
- **Automated tests:** snapshot consistency under writes, delta/full equivalence, tombstones, duplicate/cross-table barcode behavior, cursor expiry, access isolation and query bounds.
- **Acceptance:** each response refers to one stable revision; resumable bounded transfer; ambiguity never resolves arbitrarily.
- **Validation:** `dotnet build .\RetailPOS.slnx`; focused catalogue integration tests; representative query-plan test.
- **Regression:** product CRUD, lookup, barcode and inventory workflows.
- **Rollback:** feature-flag mobile catalogue routes/read model; keep source entities intact; rebuild projection from authoritative data.
- **Completion gate:** catalogue data-quality report passes approved thresholds and contract tests pass.

## P4 — Server stock-count session and durable operation protocol

- **Objective/scope:** create/resume pinned server sessions; accept idempotent immutable mobile operations; submit into existing review/approval/reconciliation path.
- **Dependencies:** P1 auth/scope; P3 catalogue revisions; stock baseline, lease and concurrency ADR.
- **Reuse:** StockCount/StockCountLine, current lifecycle/permissions, stock adjustment/ledger, audit and PostgreSQL concurrency fixtures.
- **Proposed file/component changes:** stock-count application handlers/API DTOs, receipt/session persistence migration if approved, audit metadata, integration/concurrency tests.
- **API/database:** create/resume, batch operations, per-op receipts/status, submit/status; unique operation ID plus payload hash and transactionally applied effects.
- **Security/audit:** blind count, session ownership/scope, actor/device/correlation, reason and prior/new delta; ERP-only approval/posting.
- **Automated tests:** duplicate replay, changed-payload ID conflict, partial receipt, transaction rollback, concurrent devices, session expiry, invalid SKU and double-post prevention.
- **Acceptance:** timeout-safe retries have one effect; every result recoverable; submitted count read-only; stock adjustment remains at most once.
- **Validation:** `dotnet build .\RetailPOS.slnx`; focused stock-count lifecycle/HTTP/PostgreSQL concurrency/audit tests.
- **Regression:** existing stock-count Excel/upload/approval/reopen/adjustment-draft and stock-ledger tests.
- **Rollback:** feature flag for mobile operations; retain receipt/audit evidence; do not drop operation history after release; no direct ledger change.
- **Completion gate:** invariant tests and reviewer approval for baseline/movement and multi-device policy.

## P5 — Offline catalogue and stock-take vertical slice

- **Objective/scope:** atomically sync catalogue, pin a prepared count session, commit offline operations durably, recover after restart and submit/reconcile.
- **Dependencies:** P3 catalogue API; P4 operation protocol; P2 app shell; offline lease, retention and unknown-barcode decisions.
- **Reuse:** Drift, Riverpod, barcode adapter, P2/P3 APIs, existing ERP stock-count review/adjustment path.
- **Proposed file/component changes:** local schema/migrations, catalogue repositories/use cases, scan/search/count screens, sync coordinator, exception/reconciliation UI and device tests.
- **API/database:** no duplicate product/inventory master; local read projection and append-only operation queue; server receipts and official exports from ERP.
- **Security/audit:** business/session partition, secure logout/revoke handling, no sensitive stock exposure before approval, device/session actor audit.
- **Automated tests:** full-vs-delta equivalence, pinned revisions, repeated scan vs duplicate retry, process kill/reboot, unknown/ambiguous barcode, partial ack, conflict, offline test device.
- **Acceptance:** scan hot path has no network; success only after local commit; pending count survives restart; approved server receipt/reconciliation is visible and no local stock posting occurs.
- **Validation:** Flutter analyze/format/test/integration; focused backend stock-count/catalogue integration; database migration tests.
- **Regression:** existing ERP count review, adjustments, product lookup and outbox/audit suites.
- **Rollback:** app feature flag/remote disable for new sessions; preserve queued operation recovery path; server retains accepted data and old ERP workflow.
- **Completion gate:** offline recovery and idempotency UAT plus concurrency/security sign-off.

## P6 — Role dashboards and Sales Insights

- **Objective/scope:** deliver role-based owner/manager/salesperson dashboards and insights using server-owned authorized metrics.
- **Dependencies:** P1 scope/permission contract; metric dictionary; targets and cost/profit field availability.
- **Reuse:** sales report summary/top-products/by-outlet/payment/daily-trend queries; accounting profitability only under its existing grants; Angular dashboard metric semantics where verified.
- **Proposed file/component changes:** mobile read-model handlers/DTOs, Angular-neutral shared API metric definitions, Flutter dashboard/insight screens and golden tests.
- **API/database:** compose existing APIs or add aggregation endpoints; no copied sales/discount/profit calculations in Flutter.
- **Security/audit:** manager assigned-outlet and salesperson self-only scope; redact profit, cost and restricted rankings.
- **Automated tests:** every metric vs reference query; timezone/tax/return/void/filter cases; permission and stale/no-target/error states.
- **Acceptance:** each metric exposes currency, timezone, reporting window, refresh time and definition; no unauthorized metric is returned.
- **Validation:** backend build/focused reports tests; Flutter analyze/test; Angular build/tests if shared code changed.
- **Regression:** sales reports, accounting grants and existing dashboards.
- **Rollback:** feature-flag new endpoint/cards; fallback to existing web reports; no data migration.
- **Completion gate:** business owner signs metric dictionary and role matrix.

## P7 — Shared notifications (backend, Angular and Flutter)

- **Objective/scope:** shared registry, typed policies, durable generation, scoped inbox/read state, Angular configuration and mobile inbox for verified producers.
- **Dependencies:** P0 ADR-N01..N05; audited source transitions; transactional outbox proof; P2 mobile auth; existing worker operation review.
- **Reuse:** outbox/inbox dispatcher, RabbitMQ/hosted worker, audit, grants and Angular settings/routing. Do not create a notification service.
- **Proposed file/component changes:** notification domain/persistence/API/worker, Angular settings/inbox, Flutter inbox/badge/deep links, dashboards/alerts and tests.
- **API/database:** registry/rule versions, notification/inbox/read records and event receipts; additive migration and idempotency constraints.
- **Security/audit:** recipient revalidation, row-level isolation, safe templates/deep links, redaction; audit rule edits and replay; independent failure alerts.
- **Automated tests:** atomic outbox rollback, each enabled source→policy→recipient inbox path, boundary thresholds, duplicates/retries/dead-letter/replay, cross-tenant isolation, Angular/Flutter read convergence.
- **Acceptance:** only verified producers enabled; source transaction failure produces no alert; eventual durable in-app delivery without duplicates; independent ops alarm works.
- **Validation:** focused notification PostgreSQL/worker integration tests; Angular build/Vitest; Flutter tests; load/lag dashboards.
- **Regression:** outbox users/operational events, existing reminder notifications, both clients' authentication and routing.
- **Rollback:** disable individual rules/categories and consumer via feature flags; preserve inbox/history and event evidence; keep safe monitoring path.
- **Completion gate:** standalone notification delivery gate passes before any business-wide enablement.

## P8 — Forecasting bounded context

- **Objective/scope:** assess data readiness; implement validated statistical baseline and forecast API; distinguish historical facts/prediction/optional generated explanation.
- **Dependencies:** P1 auth/scope; P6 metric definitions; data readiness and forecast ADRs.
- **Reuse:** authoritative sales, returns, stock movement, product lifecycle and cost history where available; in-process module/API first.
- **Proposed file/component changes:** forecasting domain/application/read model, model run/version metadata, evaluation jobs, API DTOs, Flutter forecast views.
- **API/database:** separate forecast records/run metadata from sale/inventory master; do not require microservice extraction.
- **Security/audit:** role-scoped forecasts; label model/horizon/training cutoff/data coverage/uncertainty; recommendations are advisory only.
- **Automated tests:** time-based holdout backtesting, cold-start/sparse/stock-out cases, hierarchy reconciliation, insufficient data fallback, facts-vs-prediction serialization.
- **Acceptance:** no false precision when data insufficient; baseline beats or is compared transparently to naive model; explanations cannot invent unsupported facts.
- **Validation:** backend tests/build; data-quality/backtest report; Flutter tests; monitoring checks.
- **Regression:** dashboards and sales/inventory source metrics.
- **Rollback:** disable forecast endpoints/cards and scheduled evaluation; historical records remain unchanged.
- **Completion gate:** model/data owner approval of quality thresholds and business sign-off.

## P9 — Hardening, pilot and release

- **Objective/scope:** threat review, performance/accessibility, signed staged Android release, operational runbooks and measured pilot.
- **Dependencies:** all enabled features' preceding gates and approved retention/device policy.
- **Reuse:** .NET/Angular CI patterns, existing architecture/audit tests; add mobile pipeline independently.
- **Proposed file/component changes:** mobile CI/build variants, signing secret references, release checks, observability dashboards, support/rollback runbooks.
- **API/database:** backward-compatible client support and migration monitoring; no unreviewed schema changes.
- **Security/audit:** dependency audit, storage/backup test, device revoke/logout, tenant isolation, least privilege and log redaction.
- **Automated tests:** performance target NFR-001/002 on agreed device; offline reboot/recovery, accessibility, Android upgrade, API contract, security and rollout smoke tests.
- **Acceptance:** signed reproducible build; staged rollout health thresholds; crash/sync/API/worker telemetry; support and rollback owner named.
- **Validation:** Flutter analyze/test/integration/build; backend/Angular regressions; security and pilot checklist.
- **Regression:** full approved backend, Angular and mobile suites; no changes to HR/payroll behavior.
- **Rollback:** stop staged rollout, disable affected API feature flags, retain safely recoverable offline operations, roll app back through store/managed distribution policy.
- **Completion gate:** product, security, operations and architecture release sign-off.
