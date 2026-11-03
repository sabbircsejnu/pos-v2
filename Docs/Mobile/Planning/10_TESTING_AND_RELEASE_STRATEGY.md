# Testing and Release Strategy

**Status:** future strategy. This Phase 0 work has not created a Flutter app, installed dependencies or run code tests.

## Existing test infrastructure (VERIFIED)

- .NET unit and integration projects in `tests/RetailPOS.UnitTests/` and `tests/RetailPOS.IntegrationTests/`.
- Architecture dependency tests under `tests/RetailPOS.IntegrationTests/Architecture/`.
- Existing stock-count HTTP/lifecycle, Excel/upload, PostgreSQL concurrency and adjustment-draft test patterns.
- Audit and durable-delivery tests under integration test areas.
- Angular package exposes `build` and `test` scripts; Vitest specs and route/component tests exist.
- No Flutter tests/project were present. Flutter CI workflow coverage was not verified.

The repository's checked-in documentation is not always current; tests and source take precedence. Run database integration suites only with their documented PostgreSQL/test environment. Do not replace concurrency tests with in-memory-provider tests.

## Test layers and ownership

| Layer | Backend | Angular | Flutter |
|---|---|---|---|
| Unit/domain | State transitions, authorization policy, delta projection, outbox/idempotency, metric and forecast policy | Components/services and policy-form validation | Pure domain, use cases, barcode normalization, operation ordering, reducer/state |
| Contract | Versioned API schemas, error codes, paging/cursors, auth/permissions | API-client compatibility | DTO serialization, provider/consumer contract fixtures and old-server compatibility |
| Integration | PostgreSQL unique keys, transaction rollback, outbox atomicity, scope denial, concurrency/replay | API service integration, routes, settings/inbox state | Dio + repositories + Drift migrations/transactions with replaceable adapters |
| UI/device | N/A | Vitest plus browser/accessibility smoke | Widget, Android integration, camera lifecycle/permissions, secure storage, process kill/reboot |
| System/UAT | ERP stock review/adjustment, notification worker and reports | Angular inbox/configuration | End-to-end offline count from catalog sync through ERP reconciliation |

## Mandatory test scenarios

### Identity, tenancy and security

- Each role, grant and outlet scope; cross-business/outlet denial; revoked/suspended user; token expiry/refresh/revoke.
- Client altered business/session/product IDs cannot bypass server authorization.
- Logout, account/tenant switch, lost device, backup/restore and pending-unsynced operation handling.
- Secret/PII/financial-field log and crash-report redaction; secure navigation/deep-link reauthorization.

### Catalogue and offline stock count

- Full snapshot equals same-revision delta; ordered tombstones, cursor expiry, revision gaps and atomic activation.
- Barcode leading zeros, aliases, duplicate/ambiguous/deactivated/missing mapping and arbitrary dynamic attributes.
- Scan operation durable before success; repeated physical scans count individually; retry of same operation ID counts once.
- Same operation ID with changed payload rejected; partial batches, timeout unknown outcome, lost receipt and resume.
- Process death, reboot, low storage, DB migration failure, app upgrade, network transitions and revoked account.
- Movement during count, simultaneous devices, lease expiry, baseline version and approved adjustment at most once.
- Unknown barcode remains separate from accepted SKU totals; changed/deactivated mapping fails explicitly.

### Dashboards, insights and forecasting

- Reconcile every reported metric to the ERP query for identical dates, outlet, timezone and permission.
- Include tax/returns/void/discount denominator, missing target, negative sales, stale cache, absent costs and role visibility.
- Forecast temporal holdout/backtesting, no target leakage, stock-out censoring, sparse/new designs, hierarchy consistency, drift, fallback and model failure.
- Clearly distinguish observed historical values, statistical predictions and optional generated explanations.

### Notifications

- Every enabled source event creates a durable record in same transaction where applicable; rollback means no notification.
- Numeric boundaries below/equal/above; duplicate source/broker delivery; two owners; disabled users; tenant isolation.
- Worker crash/retry/dead-letter/replay, concurrent scheduling, policy-version changes, recipient changes and operation alert independence.
- Angular/Flutter unread count and read state converge; revoked deep link denies; optional preferences do not suppress mandatory alerts.
- Enable no candidate type without verified producer, rule test, permission test and rollback flag.

## Performance, accessibility and release gates

- Benchmark scan-to-local-commit p95 <=150 ms on an agreed Android device after warm-up; demonstrate 5,000 scans/2,000 unique variants without UI blocking (SRS proposed target; approve device and measurement method).
- Test memory/storage, full/delta transfer duration, indexed lookup, batch size, long offline period and low-end Android behavior.
- Accessibility: touch targets, TalkBack labels/order, contrast, scalable text, non-color variance indicators and camera permission/failure controls.
- Build separate dev/staging/prod configurations with no secrets in binary; pin SDK/package lock; sign Android artifact with CI-injected secrets; verify signature.
- Release gates: security + access-isolation review, migration rehearsal/backup, device smoke, operational metrics/runbook, owner approvals, staged rollout thresholds and rollback rehearsal.

## Proposed reproducible Flutter toolchain and CI policy

- Candidate exact baseline: Flutter 3.47.7, bundled Dart 3.13.5, Android SDK 36.0.0. CI must fail if `flutter --version` or `flutter doctor -v` reports different pinned toolchain components.
- Pin the Flutter archive/revision, Android command-line tools/platform/build-tools and JDK 17. Use a committed `pubspec.lock`, exact direct package versions and `flutter pub get --enforce-lockfile`; dependency updates go through reviewed PRs with clean resolution, generator, analyze, test and Android build gates.
- Current candidate package versions and declared SDK constraints are recorded in `03_FLUTTER_ARCHITECTURE.md`. Metadata compatibility is not whole-graph resolution. Before Foundation implementation, resolve Drift, generators and all transitive constraints in a disposable clean app and record the full result. Avoid overrides and floating SDK/package channels.
- Android requirements include minSdk >= 23 for `flutter_secure_storage 11.2.0`; if optional `share_plus 13.3.1` is retained, use Java 17, Kotlin 2.2.0, AGP >=8.12.1 and Gradle wrapper >=8.13. Validate target SDK 36 policy and signing/build variants before release.

## Proposed CI/release checks

Run from repo root in PowerShell after corresponding projects exist:

```powershell
dotnet build .\RetailPOS.slnx
dotnet test .\tests\RetailPOS.UnitTests\RetailPOS.UnitTests.csproj
dotnet test .\tests\RetailPOS.IntegrationTests\RetailPOS.IntegrationTests.csproj --filter "FullyQualifiedName~StockCount"
npm --prefix .\retailpos-frontend run build
npm --prefix .\retailpos-frontend test -- --watch=false
flutter analyze
dart format --output=none --set-exit-if-changed .
flutter test
flutter test integration_test
flutter build appbundle --flavor staging
```

Add focused backend tests named/tagged by requirement, e.g. catalogue revision, mobile session, operation idempotency and notification delivery; replace the stock-count filter as appropriate. Confirm exact test runner flags during implementation. Full suite is required before release even if focused tests pass. Do not install packages for this Phase 0 task.

## Rollout and rollback

1. Deploy additive backend schema/code disabled by default; validate migrations against a sanitized production-sized copy and rehearse backup/restore.
2. Enable API/catalogue capability for internal test business; monitor authorization denials, latency, revision gaps, sync backlog and database load.
3. Pilot stock take with limited authorized users/outlets; compare mobile accepted evidence, ERP reconciliation, adjustment and audit trail.
4. Enable dashboards/forecast only after metric/model gates; notification types individually after producer and policy gate.
5. Stage Android distribution to a small group; monitor crash-free sessions, sync success, app/API compatibility and support tickets.
6. Roll back by stopping rollout and disabling the affected feature/policy. Preserve server receipts/audit and provide a managed recovery path for offline unsynced operations. Do not delete local/server evidence as a rollback shortcut.

Release only after product, architecture, security, operations and business sign-off. No HR/payroll behavior change is in scope.
