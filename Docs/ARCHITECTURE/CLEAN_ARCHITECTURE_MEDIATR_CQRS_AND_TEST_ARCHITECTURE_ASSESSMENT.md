# Clean Architecture, MediatR, CQRS and Test Architecture Assessment

**Assessment date:** 2026-10-06

**Last numbered slice:** Slice 12ES — System Settings (accepted).

**Current status:** **Local Runtime Smoke Gate Passed - Ready for Commit Review.** Architecture migration and bounded cleanup remain accepted. No mandatory architecture implementation slice remains. No further numbered work, staging, commit or push.

## Latest bounded runtime gate

The first real local smoke exposed absent Business linkage for six seeded operational users and their demo outlets/warehouses/suppliers, denied Cashier category/tax/receipt initialization reads, and no existing Super Admin Business selector. The owner approved explicit server-validated Super Admin selection rather than reusing BusinessOwner-only role switching.

Current bounded changes preserve persisted login/refresh claims, tenant/outlet checks and HTTP semantics. Exact known demo assignments are repaired idempotently; foreign assignments are rejected and unrelated users are unchanged. Each selected `X-Business-Id` is validated through Application against persisted active Super Admin/active Business before becoming trusted request scope. Super Admin remains platform-scoped until explicit selection; regular users retain signed scope. Cashier receives only `categories.view` and can use existing `sales.create` for tax/receipt GET; settings mutation, full settings and accounting remain denied.

Real relational smoke also exposed concurrent Cash Book slot resolution sharing one scoped EF session, and untranslatable outlet filters/order after record projection. Sequential resolution and pre-projection entity predicates/order fix the runtime defects without changing accounting formulas, fields, tenancy policy or schema. New regression coverage includes 7 Application context/concurrency cases, 6 runtime HTTP/seed cases and 2 additional SQLite outlet-translation cases.

Latest backend evidence: focused bundles 17/17 and 36/36; full Release 248 UnitTests and 1,087 IntegrationTests (1,335 total), zero failed/skipped; build zero warnings/errors; all 10 architecture cases pass. EF model unchanged and single baseline retained. First normal seed changes only existing demo scope/role tables; the second seed has identical fingerprints across all 89 tables. Full-suite PostgreSQL inventory returns to five original databases and zero test databases.

Actual scoped owner/admin HTTP succeeds for supplier/PO/GRN/PDF, existing sale detail and GL/Trial Balance/Cash Flow/Profitability/CSV; Cashier initialization succeeds while prohibited settings/report calls are403 and anonymous calls401. Final real Angular owner/admin/cashier navigation and direct Accounting/PO reloads pass with zero console/page errors and zero HTTP failures. Restoration holds scoped work until explicit server validation and profile refresh complete; no saved Business ID is trusted before validation. Complete commands, data effects and final evidence are maintained in the [runtime gate](CLEAN_ARCHITECTURE_MEDIATR_CQRS_MIGRATION_PLAN.md#local-runtime-readiness-remediation---2026-10-06).

The smoke invokes the existing lazy accounting initialization: 26 chart accounts, one configuration, one financial year and one period are created for the previously uninitialized Demo Business. Auth/audit records change; no financial/operational transaction is created, posted, reversed or deleted. Across all retry runs: refresh tokens11->58, audit events26->136, audit entity rows11->87 and field changes88->953. All other fingerprints outside the documented scope/role/configuration/auth/audit tables remain unchanged. Final Angular95/95 (17 files) and production build pass; Swagger200 has375 paths. No schema, dependency-direction or mandatory architecture backlog changes.

Owned API/Angular processes are stopped and ports5005/4200 have no listeners. Final runtime logs show no failures/critical errors/exceptions or secret values; pre-existing EF sentinel/query-splitting hints remain non-blocking. Documentation hygiene and `git diff --check` pass; zero staged files. No commit/push/revert or additional architecture work.

## Executive summary

The repository now has `RetailPOS.Domain`, `RetailPOS.Application`, `RetailPOS.Infrastructure`, and `RetailPOS.API`. Application owns MediatR requests, handlers, validators, pipeline behavior, and narrow ports; Infrastructure implements persistence and external-resource adapters; API owns HTTP, authorization, DTOs, composition, and host/presentation adapters.

MediatR/CQRS and the FluentValidation pipeline are implemented. `RetailPOS.UnitTests` and `RetailPOS.IntegrationTests` are separate projects; the legacy `RetailPOS.Tests` project is retired. Completed workflow migrations through 12ES cover identity/governance, catalog/product/variation, POS/Sale/Cash Session, purchase/GRN, inventory/warehouse, supplier/AP, accounting, reconciliation/close workflows, audit, and selected worker/adapter paths. The current migration plan contains the slice-level evidence and limitations.

The final Purchase Order/GRN PDF persistence exceptions are now removed: the presentation adapters use established Application queries and Infrastructure read stores. Caller-proven legacy services and four registrations are retired. The expanded regression guard covers API controllers, services and document presenters. The PostgreSQL identifier-truncation leak is repaired; all 945 confirmed orphans were removed individually and repeated/full-suite validation leaves zero test databases.

SQLite remains IntegrationTests-only. The native package override now resolves to **2.1.12**, eliminating NU1903 without upgrading EF/Npgsql or suppressing warnings; all **177** SQLite-dependent cases pass. The redundant direct API BCrypt reference is removed while Infrastructure's dependency remains. The owner-approved JWT signing-key externalization preserves local configuration in development user-secrets and requires deployment secret configuration; it is not rotation of previously exposed keys.

## 1. Current verified state

| Area | Verified status |
|---|---|
| Projects and dependency direction | Domain, Application, Infrastructure, API, UnitTests, and IntegrationTests are present. Application does not depend on API, Infrastructure, or EF Core. API references Infrastructure for composition and selected adapters. |
| MediatR/CQRS | Migrated workflows dispatch through Application commands/queries and handlers. |
| Validation | FluentValidation validators and the Application validation pipeline are registered. |
| Test separation | UnitTests and IntegrationTests have distinct references and responsibilities. The legacy mixed test project is absent. No mandatory project-separation work remains. |
| Controllers | No direct `RetailPOSDbContext`, EF Core, or `RetailPOS.Infrastructure.Data` references were found under API Controllers. Current controller services are principally tenant/outlet authorization, feature gates, formatting, and PDF/host adapters. |
| API service data access | Current controller/service/document source and reflected-member checks reject Infrastructure repositories/Data, EF Core and `RetailPOSDbContext`. PDF adapters dispatch Application detail queries; the former repository-backed services are absent. |
| Architecture tests | Ten cases in `RetailPOS.IntegrationTests/Architecture/CleanArchitectureDependencyTests.cs` cover project direction, controller and service/document persistence boundaries, and four forbidden-source examples. Program remains the composition root; documented host/presentation adapters are retained. |
| EF migrations | One baseline migration, `20261004095137_InitialBaseline`, with its designer and `RetailPOSDbContextModelSnapshot.cs`. |
| PostgreSQL lifecycle | All 23 workflow/migration creators plus lifecycle tests use one owned async lifecycle. Names fit PostgreSQL's 63-byte limit; final repeated/full-suite inventory is 5 protected/system databases and 0 test databases. |
| SQLite/package isolation | Only IntegrationTests uses EF SQLite 9.0.0; native override 2.1.12, restored win-x64 engine 3.53.3. Zero SQLite packages in production/UnitTests assets or production publish. No NU1903; zero-warning Release build. |
| Signing-key configuration | No tracked JWT key/default. Development user-secrets or deployment `JwtSettings__Secret`; explicit startup options validation. Three HTTP test factories have an explicit test-only key. Owner rotation of previously exposed deployment keys remains separate. |
| Current active work | Frozen implementation and required acceptance validation complete. Zero remaining mandatory slices; no further numbered work started. |

## 2. Slice 12ES — System Settings

12ES reconciled the interrupted Settings implementation, old service characterization, DI registrations, deleted API model, and every production `ISettingsService` consumer.

- `SettingsController` now dispatches Application queries and update commands. Authorization, validation, route, response envelope/message, and Swagger operation contract are retained.
- `BarcodesController`, Purchase Order/GRN PDF services, Cash Session and Stock Adjustment adapters, and Sale invoice allocation now use Application requests or the Application invoice-allocation port.
- `ISettingsService`, `SettingsService`, its DI registration, and the stale service characterization test were removed after caller proof.
- `JsonSystemSettingsStore` remains one singleton registered as both `ISystemSettingsStore` and `ISaleInvoiceNumberAllocator`. Tests assert alias identity.
- Tests characterize JSON formatting/shape, case-insensitive deserialization, legacy currency backfill, in-instance cache behavior, serialized invoice allocation, cancellation, permissions, validation, response envelopes, and all Settings Swagger operations.
- The test reads Swagger through the configured `ISwaggerProvider`; this API does not expose `/openapi/v1.json`.

**Recorded slice-time validation (historical):** focused Settings tests **9/9**; affected Settings/architecture/Stock Adjustment/Barcode PDF/Sale IntegrationTests selection **67/67**; full UnitTests **241/241**; post-change Release solution build **0 errors, 2 NU1903 warnings**; EF pending-model check reported no changes; no-connect inventory listed only `20261004095137_InitialBaseline`; `git diff --check` passed. No schema or HTTP/OpenAPI contract change. Latest connected inventory and zero-warning cleanup validation below supersede those earlier limitations.

## 3. Final source audit and completed frozen boundaries

The final audit searched the current API, Application, Infrastructure, and test source for callers, repository/EF coupling, settings compatibility references, hosted workers, and architecture coverage.

| Current owner/path | Caller and responsibility evidence | Risk | Disposition |
|---|---|---:|---|
| `RetailPOS.API/Documents/Pdf/PurchaseOrderPdfService.cs` | Dispatches existing `GetPurchaseOrderByIdQuery`; Infrastructure read store preserves Warehouse Business scope and unrestricted Super Admin scope. Shared `PurchaseDocumentDtoMapper` preserves API/PDF projection. | Medium/high: tenant scope and projection correctness. | **Complete.** Former `PurchaseOrderService` / `IPurchaseOrderService` and registration removed after caller proof. |
| `RetailPOS.API/Documents/Pdf/GrnPdfService.cs` | Dispatches existing `GetGrnByIdQuery`; Infrastructure read store supplies the complete PDF projection and not-found behavior. | Medium/high: tenant scope and PDF correctness. | **Complete.** Former `GrnService` / `IGrnService` and registration removed. The previously unscoped PDF path now follows the established GRN Business-isolation policy; foreign-Business records return the existing not-found response. |
| Retired Role Assignment Policy implementation/interface and registration | Whole-source caller proof found no active consumer; Application role governance is retained. | Low; unused runtime path. | **Complete.** Removed `RoleAssignmentPolicyService` / `IRoleAssignmentPolicyService` without changing active role behavior. |
| Retired AP Report wrapper and registration | No production consumer; report routes already dispatch Application requests. | Low; unused wrapper. | **Complete.** Removed `ApReportService`; active read-model mapping remains. |
| `RetailPOS.API/Services/AccountingConfigurationService.cs` and `IAccountingConfigurationService.cs` | Active API adapter used by workflow adapters; methods dispatch Application queries and map Application read models to API DTOs. | Medium/high financial data, but no direct persistence access. | Retain as an API mapping/composition adapter. Its `CancellationToken.None` calls are a follow-up propagation opportunity, not a separate confirmed architecture blocker for this frozen backlog. |
| Tenant/outlet/feature adapters, `VariantDisplayFormatterService`, PDF/Excel/label presenters, cache invalidators, audit/event/logger adapters | Called by controllers or Application-owned workflow ports for request scope, authorization, formatting, file/host concerns, or side effects. No direct API DbContext/EF access was found. | Varies by workflow. | Legitimate API/host/presentation adapters; do not delete for architectural purity. |
| Accounting/journal poster adapters in API Services | Registered outer-layer implementations dispatch Application journal commands and use Application ports; they do not directly query DbContext or Infrastructure repositories. Some preserve existing line mapping/calculation in the adapter, as documented in their completed workflow slices. | High financial behavior. | Retain under the documented port/adapter decisions for this closeout. A future relocation of journal-line policy is optional unless a specific caller/behavior audit proves an active boundary defect; do not combine it speculatively with this final slice. |
| `SubscriptionRenewalReminderHostedService` | The API host schedules/logs and dispatches `ProcessSubscriptionRenewalRemindersCommand` through `ISender`; command behavior is in Application. | Low/medium operational. | Legitimate host worker adapter; no remaining worker migration is confirmed. |
| `RetailPOS.UnitTests`, `RetailPOS.IntegrationTests` | Separate projects and reference boundaries are in place. Old `RetailPOS.Tests` naming remains in some test namespaces, but that is not a remaining mixed project. | Low. | Project separation is complete. Renaming legacy namespaces is optional cleanup, not a mandatory migration item. |

### Remaining mandatory backlog

**Zero bounded implementation slices remain in the frozen target.** No further workflow endpoint, worker or test-project separation gap is confirmed by current-source evidence. Namespace renaming, adapter consolidation, cancellation propagation and accounting-poster relocation remain optional, not implied completion requirements. PDF delivery interfaces/renderers are intentionally retained as presentation adapters.

## 4. PostgreSQL lifecycle remediation and exact orphan evidence

The root cause was PostgreSQL's silent 63-byte identifier truncation: long approved prefixes plus GUIDs were created under truncated names, while cleanup searched for the original name and incorrectly returned as if the database had already been removed. Teardown wiring already existed in all creators.

`PostgresTestDatabase` now preserves the full GUID within a bounded ASCII name, registers exact instance ownership and verifies its catalog marker before serialized/idempotent disposal. It clears only the owned database's pool, disables new connections, terminates only that database's sessions, and drops its exact name. Setup-failure cleanup uses a fresh admin connection. Operation/cleanup errors retain original failures and report the database; no normal teardown wildcard or automatic pre-suite stale sweep exists.

Seven context initialization helpers now use the shared resource-initialization guard to dispose a context if migration/setup throws before returning it to the caller. The full [fixture inventory and exact modified helpers](CLEAN_ARCHITECTURE_MIGRATION_ACCEPTANCE_CHECKLIST.md#postgresql-creator-inventory) distinguish already-shared creators from actual edits.

Before the authorized bounded cleanup there were **950 databases**, including **945 approved, marked, session-free RetailPOS test orphans**. No running testhost/vstest process was found. Exact names/OIDs/owner/marker/session evidence was materialized before deletion and revalidated individually. Eleven invalid interrupted-DROP remnants required exact DROP without ALTER. All 945 removals are recorded by name and creating fixture in the [removal manifest](POSTGRES_TEST_DATABASE_CLEANUP_20261006.csv).

| Checkpoint | Total databases | Test databases |
|---|---:|---:|
| Before orphan cleanup | 950 | 945 |
| After cleanup / baseline | 5 | 0 |
| Final PostgreSQL bundle run 1 — 90/90 passed | 5 | 0 |
| Final PostgreSQL bundle run 2 — 90/90 passed | 5 | 0 |
| After full final backend Release suite | 5 | 0 |

The remaining exact names are `iranimart_ecommerce`, `postgres`, `retailpos_db`, `template0` and `template1`. No non-test database, production data, production schema, migration or accounting behavior was modified. Killed processes cannot run disposal; recovery remains an explicit bounded, evidence-checked operation rather than an automatic deletion policy.

## 5. Final acceptance evidence

| Gate | Verified result |
|---|---|
| Pre-switch PDF characterization | 2/2 passed. |
| Final lifecycle/PDF/GRN/SQLite/architecture focused selection | 41/41 passed: 27 lifecycle and 14 PDF/read/architecture cases. Covers success/failure/repeated cleanup, active pooled sessions, independent ownership, protected names, initialization plus disposal failures, projection, permissions, Business/Outlet behavior, Super Admin, not-found, PDF delivery and Swagger routes. |
| Reused Application detail handlers | 10/10 passed. |
| Final PostgreSQL lifecycle/creator/concurrency bundle | 90/90 passed twice, no failures/skips, exact 5/0 inventory restored after each. |
| Full backend Release suites after cleanup | 241/241 UnitTests + 1,079/1,079 IntegrationTests = **1,320/1,320**, zero failures/skips; one additional startup-key regression. |
| Exact SQLite-dependent selection | **177/177**: 167 in-memory and 10 temporary-file cases across 66 files; rollback/transactions/triggers/retry/idempotency/refresh/file cleanup pass. Mixed-provider files are selected by actual SQLite cases, not whole files. |
| Focused JWT startup/authentication/HTTP/OpenAPI/SQLite-host/architecture selection | **30/30**, zero failed/skipped. Startup validates after test-host configuration rather than reading the signing key too early. |
| Release solution build | Succeeded: **0 errors, 0 warnings**, zero NU1903 restored entries. |
| Production publish/dependency inspection | Succeeded; zero SQLite packages/binaries, no user-secrets file or tracked JWT key; BCrypt remains transitively supplied by Infrastructure. API/Infrastructure/Application/Domain/UnitTests assets have zero SQLite packages. |
| EF pending-model check | No changes since the last migration. |
| Connected migration inventory | Only `20261004095137_InitialBaseline`, with no pending annotation. Supersedes the earlier no-connect limitation. |
| Angular full suite and production build | 63/63 tests in 13 files; `npm run build` succeeded. |
| Source caller/dependency audit | Removed legacy types have no active consumers; ten Clean Architecture cases pass within focused/full suites. Legitimate host/presentation adapters are retained. |
| Hygiene and final database inventory | Changed-file diagnostics and `git diff --check` pass; documentation/CSV checked separately; exactly five protected/system databases and zero test databases. |

The [final acceptance checklist](CLEAN_ARCHITECTURE_MIGRATION_ACCEPTANCE_CHECKLIST.md) records the frozen target, exact commands, fixture inventory, results and optional items. No stage, commit, push, revert or further numbered slice.

## 6. Bounded repository cleanup and readiness

| Classification | Current disposition |
|---|---|
| Safe removal implemented | Redundant API BCrypt direct reference only; no extra caller-free code removal proved. Deleted only inventoried retired Core/mixed-test build artifacts, old root log/two TRX files, accidental nested generated EF targets and verified empty folders. Baseline/history/evidence are retained. |
| Intentionally retained | Governance ports/aliases, purchase DTO/PDF contracts, token adapter, host/presentation/auth/tenant/cache/audit/worker adapters and financial posters; SQLite relational test fixtures. |
| Optional future work | Namespace renaming, adapter consolidation, cancellation propagation, accounting-poster relocation and crash-recovery automation. |
| Ambiguous — unchanged | Potential `System.IO.Packaging` pin, npm packages without proven unused status, unrelated SQL scripts and accounting/HR documents. No speculative deletion. |

The [migration plan cleanup section](CLEAN_ARCHITECTURE_MEDIATR_CQRS_MIGRATION_PLAN.md#final-repository-cleanup-and-dependency-remediation--2026-10-06) lists exact generated removals, dependency graph, approved security scope and validation. Source-control inventory: **152 modified, 284 deleted, 1,364 untracked files**; accepted migration/evidence files are ready for review, not staged. Unrelated SQL/HR/accounting work is preserved and requires separate inclusion review. All five architecture evidence files, including the **945-name orphan manifest** and test-separation inventory, are retained.

`.gitignore` additionally covers WAL/SHM sidecars, `.testresults/` and the exact generated root test log. Build/publish/cache/results/local configuration remain ignored; development user-secrets and temporary verification tools are outside the repository. Filename review found no untracked local secret-file candidate; this is not a comprehensive credential-free certification.

Post-cleanup PostgreSQL evidence was rerun: baseline, after full IntegrationTests and after **90/90 twice** each show **five databases / zero test databases**. This cleanup required no database deletion; earlier 945 exact removals are preserved as historical evidence.

## 7. Known limitations and historical baseline

The previous **NU1903** advisory is resolved by the test-only **2.1.12** override. Existing LF/CRLF notices are not diff-check failures. Earlier PostgreSQL/full-suite-unavailable statements are historical; fresh connected/full-suite validation supersedes them. Crash-proof disposal is impossible for killed processes, so recovery remains explicit and bounded.

JWT externalization does not rotate previously exposed/deployed keys or remove Git history; owners must rotate deployment keys. Tracked PostgreSQL development credentials/Compose/seed configuration and Compose port exposure remain unchanged because the owner selected JWT-only remediation. They remain security follow-ups before public sharing/deployment, not silently fixed by this architecture task. This acceptance is limited to the frozen migration/cleanup target, not universal architecture/security perfection.

**Historical baseline — October 3, 2026:** The original assessment described the pre-migration architecture as lacking Domain/Application projects, MediatR/CQRS, test-project separation, and controller persistence boundaries. Those claims are history only and are not current facts. Still-useful historical cautions are to preserve accepted behavior, distinguish EF InMemory tests from relational guarantees, classify tests by runtime behavior, and avoid broad rewrites or speculative abstractions.
