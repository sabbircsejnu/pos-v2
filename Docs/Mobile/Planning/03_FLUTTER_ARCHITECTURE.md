# Flutter Architecture

**Status:** the overall product architecture remains **PROPOSED**. P2-A has established the Android-only Flutter foundation in `retailpos-mobile/` and locally validated its locked Riverpod / GoRouter / Dio dependency subset. The pinned runtime is Flutter 3.47.7 (revision `abaf9c523780a608bd46686fd5e53740a07077f8`) / Dart 3.13.5 / Android API 36; see [15_P2_A_FLUTTER_FOUNDATION_BOOTSTRAP.md](./15_P2_A_FLUTTER_FOUNDATION_BOOTSTRAP.md) for actual scope and results. Authentication, secure token storage, ERP access, Drift/catalogue/count implementation and final brand/UI remain unimplemented and approval-gated.

## Technology selection

Package metadata was checked against the supplied baseline. The first approved foundation resolved and locked Riverpod, GoRouter, Dio and lint dependencies; the rest of this table remains a proposed future package set and is not installed or fully resolved.

| Capability | Proposed stable candidate | Decision |
|---|---|---|
| Flutter / Dart / Android | Flutter 3.47.7 / Dart 3.13.5 / Android SDK 36.0.0 | Candidate baseline supplied by product owner. Pin exact Flutter archive revision and Android command-line/build tools in CI; fail fast on version mismatch. |
| State/DI | `flutter_riverpod` 3.4.3 (Dart `^3.12.0`) | Resolved and exact-pinned for P2-A. Adopt explicit providers; avoid generator unless justified. |
| Routing | `go_router` 18.0.2 (Dart `^3.12.0`, Flutter `>=3.44.0`) | Resolved and exact-pinned for P2-A. |
| HTTP | `dio` 5.11.1 (Dart `>=2.18.0`) | Resolved and exact-pinned for P2-A; no ERP base URL or request call site exists yet. |
| Local relational storage | `drift` 2.35.2 (Dart `>=3.10.0 <4.0.0`) | Compatible declared constraint; whole graph including `drift_dev` remains to resolve. |
| Secret storage | `flutter_secure_storage` 11.2.0 (Dart `>=3.8.0 <4.0.0`, Flutter `>=3.19.0`) | Compatible declared constraints; Android min SDK 23 per package docs. |
| Barcode | `mobile_scanner` 7.4.2 (Dart `^3.7.0`, Flutter `>=3.29.0`) | Compatible declared constraints; validate formats, permissions, device support and lifecycle. |
| JSON/code generation | `json_serializable` 6.14.1 (Dart `^3.9.0`, analyzer `>=10.0.0 <15.0.0`, build `^4.0.4`); `build_runner` 2.16.2 (Dart `^3.11.0`, analyzer `>=14.3.0 <15.0.0`, build `^4.0.9`); `json_annotation` 4.12.0 (Dart `^3.9.0`) | Declared analyzer/build ranges intersect; verify full generator graph with Drift before adoption. |
| Lint/tests | `flutter_lints` 6.0.0 (Dart `^3.8.0`); `mocktail` 1.0.5 (Dart `>=2.12.0 <4.0.0`) | `flutter_lints` is resolved and exact-pinned for P2-A; `mocktail` remains proposed and is not installed. |
| Connectivity | `connectivity_plus` 7.3.2 (Dart `>=3.2.0 <4.0.0`, Flutter `>=3.7.0`) | Compatible declared constraints; connectivity is a hint, never proof of internet or sync success. |
| Optional share | `share_plus` 13.3.1 (Dart `>=3.10.0`, Flutter `>=3.38.1`) | Optional only; Android build requires Java 17, Kotlin 2.2.0, AGP >=8.12.1 and Gradle >=8.13. Prefer ERP-generated official exports. |
| PDF / Freezed | Not selected initially | Exclude device-generated official PDF (ERP remains authoritative) and avoid extra generator graph until justified and resolved. |

### Reproducible dependency and Android build policy

- Pin Flutter 3.47.7 / Dart 3.13.5 and verify Flutter framework revision `abaf9c523780a608bd46686fd5e53740a07077f8` in CI; do not use a floating Flutter version for build jobs.
- Pin Android SDK platform/build-tools and command-line tools; use JDK 17. If `share_plus` is retained, meet its stated Kotlin/AGP/Gradle minimums. Choose compatible AGP/Gradle/Kotlin versions as one tested set; check Android `minSdk` is at least 23 for secure storage and document target SDK 36 policy.
- Pin direct dependencies to exact versions, commit `pubspec.lock` for the application, and run `flutter pub get --enforce-lockfile` in CI. Use controlled dependency-update PRs with resolver/analyze/test/build gates. Do not use dependency overrides to mask incompatibilities.
- For future dependencies (including Drift/code generation), resolve and lock the complete graph, then run the relevant generator, `flutter analyze`, `flutter test`, and signed-config-free Android flavor builds before approval. P2-A's current graph and validation results are recorded in planning document 15.

## Dependency direction

```text
app/bootstrap
  -> core (configuration, errors, logging, auth/session, network, database, permissions)
  -> feature composition (auth, catalogue, stock_take, dashboard, insights, notifications, settings)

feature presentation (widgets + Riverpod controllers)
  -> feature application (use cases, feature state, repository interfaces)
  -> feature domain (entities/value objects/policies)

feature infrastructure (Dio/Drift adapters, DTOs, mappers)
  -> feature application/domain contracts
  -> core network/database
```

Domain/application code must not import Flutter widgets, Dio, Drift-generated classes, or another feature's private implementation. Cross-feature communication uses narrow public contracts (e.g., `BarcodeVariantResolver`, `SessionContext`, `NotificationUnreadCount`) or app-level composition, not imports into another feature's internals.

## Proposed layout

```text
retailpos-mobile/
  lib/
    app/                 # bootstrap, app shell, route table, theme composition
    core/
      auth/               # session interface and authorization state
      database/           # Drift database, migrations, transaction helpers
      network/            # Dio setup, interceptors, API error types
      observability/      # redacted structured logs, crash/trace adapters
      security/           # secure storage abstractions, redaction
      sync/               # shared sync coordinator and connectivity hints
      widgets/            # design-system primitives, not feature widgets
    features/
      auth/{domain,application,infrastructure,presentation}/
      catalogue/{domain,application,infrastructure,presentation}/
      stock_take/{domain,application,infrastructure,presentation}/
      dashboard/{domain,application,infrastructure,presentation}/
      insights/{domain,application,infrastructure,presentation}/
      notifications/{domain,application,infrastructure,presentation}/
      settings/{domain,application,infrastructure,presentation}/
  test/                   # unit, widget, integration, contract fixtures
  integration_test/
  assets/                 # only approved/licensed IraniMart assets
```

## Public feature contracts

| Feature | Public contract and boundaries |
|---|---|
| Auth | `SessionRepository` sign-in/refresh/sign-out/current context; `AuthState` drives routing; business/outlet selection only from authorized server-provided choices. |
| Catalogue | Read-only local queries by normalized barcode, variant ID and main product code; sync exposes active catalogue revision/status. Never writes product master data. |
| Stock take | Start/resume/close a count session; append immutable local count operations; query pending/conflicted/unknown items; submit idempotently; consume server receipts/reconciliation. Cannot post ledger entries. |
| Dashboard | Role-aware read models from server APIs; no independent sales/profit formula. |
| Insights | Server-calculated/authorized facts and insight aggregates. Provenance and permissions travel with the payload. |
| Forecast | Prediction DTO explicitly includes horizon, model/version, generated-at and uncertainty where approved; explanation is a separate optional field. |
| Notifications | Shared inbox query/read-state commands; notification-specific links route only after access revalidation. |

## State, DI and navigation

- Use Riverpod providers as the composition and lifecycle boundary. Register repository implementations at app composition and inject abstractions into feature use cases/controllers.
- Keep screen state immutable and feature-scoped; use async loading/error/data states, preserve the last usable local catalogue when online refresh fails, and expose sync status rather than silently implying success.
- Centralize `GoRouter` redirects for unauthenticated, expired, unauthorized and required-context states. Enforce authorization again on the server; route visibility is not security.
- Proposed routes: `/sign-in`, `/outlet`, `/dashboard`, `/catalogue/search`, `/stock-take`, `/stock-take/:sessionId`, `/insights`, `/notifications`, `/settings`. These are proposals, not existing API/UI paths.
- Avoid a global service locator or feature-to-feature imports. Use explicit interfaces, constructors/providers, and test overrides.

## Offline data, security and error handling

- Drift owns the local catalogue projection and durable count-operation queue. Use DB transactions to import a complete revision and atomically switch the active revision. Never partially activate a catalogue page set.
- Count session stores an immutable pinned catalogue revision and a unique server session ID. Device operations have client-generated globally unique operation IDs and append-only payloads; retries resend the same ID/payload.
- Secure storage contains only tokens/secret material and minimal session metadata. Exclude secrets from logs, analytics, backups and crash reports. Define device loss/logout/revocation and local data retention before release.
- Dio maps transport, timeout, auth, validation, conflict, rate-limit and server failures into typed errors. A network timeout after mutation is **unknown outcome**, not failure; resolve with idempotent retry/status query.
- Unknown barcode is a first-class count result requiring explicit operator disposition; never map it to an arbitrary product or silently drop it.
- Observability uses correlation/request IDs, event counters and redacted error categories; no barcode, personal data or count values in telemetry without an approved privacy basis.

## Brand and design system

- **VERIFIED:** product owner states official IraniMart logos were supplied. Repository scan has not located source files beyond `retailpos-frontend/public/favicon.ico`.
- **PROPOSED tokens from accessible Figma Make brief (not final approval):** Gold `#C69B45`, Champagne Gold `#E7CF98`, Primary Brown `#75583E`, Rich Espresso `#35291F`, Warm Ivory `#FAF7F2`, Soft Cream `#F4EDE3`, White `#FFFFFF`, Charcoal `#292724`, Muted Taupe `#83776A`, Success `#258568`, Warning `#B97820`, Error `#C34E4E`. Validate accessible contrast and approve token roles before implementation.
- **PROPOSED asset location:** `retailpos-mobile/assets/brand/` for owner-provided approved logo variants, with provenance/license and usage metadata. This directory is not created in Phase 0.
- **BLOCKED:** visual approval of each screen/state and final typography/logo asset handoff. Screens not actually inspected remain pending visual approval; do not infer layouts from prompt descriptions.
- Remove prototype-only demo-role sign-in from production auth.

## Architecture acceptance gates

1. `flutter pub get` resolves on the exact CI-pinned stable Flutter/Dart and Android toolchain without overrides.
2. Dependency graph has no domain-to-framework or cross-feature-private imports.
3. Generated files are deterministic; `dart format` and `flutter analyze` pass.
4. Unit/widget tests run without network/database services; adapters are replaceable by fakes.
5. Database encryption/backup/revocation threat model is reviewed and approved.
