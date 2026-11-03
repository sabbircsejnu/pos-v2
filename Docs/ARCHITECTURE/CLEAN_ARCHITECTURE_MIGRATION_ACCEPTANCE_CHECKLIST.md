# Clean Architecture Migration — Final Acceptance Checklist

**Verified:** 2026-10-06.

**Scope:** Accepted Slice 12ES, PostgreSQL lifecycle remediation, the frozen Purchase Order/GRN PDF closeout and bounded Final Repository Cleanup and Dependency Remediation. No new numbered slice. **Mandatory implementation backlog: zero.**

**Status:** **Local Runtime Smoke Gate Passed - Ready for Commit Review.** Architecture migration and final repository cleanup remain accepted.

## Bounded local runtime acceptance

- [x] Existing `retailpos_db` retained; no database recreation, schema/model change or migration generation.
- [x] Externalized JWT key supplied by local user-secrets; no hard-coded/recorded secret.
- [x] Six exact seeded operational users and identified demo locations/suppliers repaired; platform Super Admin remains unassigned.
- [x] Normal seed repeated: first changes only scope/role rows, second has identical fingerprints for all 89 tables.
- [x] Owner-approved explicit platform Business selector revalidates persisted active role and active Business on every scoped request.
- [x] No arbitrary Business default; no weakened tenant isolation or owner-role-switch policy.
- [x] Cashier categories and narrow POS configuration GETs succeed; full settings/edit/accounting remain denied.
- [x] Scope/auth/Settings/PDF/architecture focused tests36/36; relational/runtime/CashBook17/17; Application7/7.
- [x] Full Release backend: 248 UnitTests + 1,087 IntegrationTests; no failures/skips; build zero warnings/errors.
- [x] EF unchanged, single baseline; five original PostgreSQL databases and zero test databases after full suites.
- [x] Actual owner/admin scoped HTTP reports, supplier/PO/GRN/PDF and CSV200; prohibited cashier403; unauthenticated401.
- [x] Final Angular owner/admin/cashier navigation, direct Accounting/PO reload restoration and zero unexpected browser/API errors.
- [x] Full Angular95/95 (17 files), production build; protected Cashier route denied and no cash session opened.
- [x] Final data fingerprint/log review, Angular tests/build, documentation hygiene and clean owned-process shutdown.
- [x] Zero final API failures/critical errors/exceptions or logged secret values; only non-blocking existing EF hints.
- [x] API/Angular owned process trees stopped; ports5005/4200 have no listeners; zero staged files.

The existing Cash Flow provisioning path initializes accounting configuration (26 chart accounts, one configuration/year/period); this is not a posted financial transaction. Auth/audit rows change. No financial/operational records are manually created, posted, reversed or deleted. The prior cleanup tables below remain historical accepted evidence, not the current runtime test counts.

Final repeat-run auth/audit totals: refresh tokens11->58; audit events26->136; audit entity rows11->87; audit field changes88->953. Besides the documented scope/role/accounting-initialization rows, all 89-table fingerprints remain unchanged. Final inventory is five original databases and zero test databases; no database cleanup/deletion was performed in this runtime remediation. Exact commands, accepted selection policy, restored-context race correction and evidence are in the [runtime acceptance](CLEAN_ARCHITECTURE_MEDIATR_CQRS_MIGRATION_PLAN.md#final-local-runtime-acceptance).

## Accepted frozen target

- [x] Domain and Application exist; Application owns MediatR/CQRS, FluentValidation and narrow ports without API/Infrastructure/EF dependencies.
- [x] UnitTests and IntegrationTests are separate; legacy mixed test project is retired.
- [x] System Settings retains JSON compatibility, singleton/cache semantics and serialized invoice allocation; the accepted 12ES evidence is preserved in the [assessment](CLEAN_ARCHITECTURE_MEDIATR_CQRS_AND_TEST_ARCHITECTURE_ASSESSMENT.md).
- [x] Purchase Order PDF uses existing Application detail query and Infrastructure read store.
- [x] GRN PDF uses existing Application detail query and Infrastructure read store.
- [x] Shared complete API/PDF DTO projection preserves fields, totals, formatting and missing-record behavior.
- [x] Routes, permissions, Swagger, media type, filenames and real PDF output are covered.
- [x] Established Business scope/Super Admin behavior is preserved; no extra Outlet restriction invented for Warehouse documents.
- [x] Legacy GRN PDF's unscoped access is corrected using the established GRN Business-isolation policy.
- [x] Caller proof precedes removal of seven legacy files and four registrations: PO/GRN service/interface pairs, Role Assignment Policy service/interface, AP Report wrapper.
- [x] API controllers/services/documents reject Infrastructure repositories/Data, EF Core and DbContext dependencies through source and reflection regression checks.
- [x] Program composition, authentication/tenant/feature composition, formatting, PDF delivery, cache/audit integration and worker hosting remain legitimate adapters.
- [x] No schema/migration, financial posting or production database behavior change in this closeout.

## PostgreSQL lifecycle and deletion safety

- [x] One lifecycle used by every identified PostgreSQL creator; 21 approved purposes.
- [x] Exact registered name and marker; full GUID retained within PostgreSQL's 63-byte identifier limit.
- [x] Owned contexts/connections/scopes disposed by caller lifetime before exact database cleanup.
- [x] Seven context initialization helpers also dispose on migration/setup failure before returning ownership to callers.
- [x] Async disposal is serialized and idempotent; already-removed databases are safe.
- [x] Relevant owned-database pool cleared; only exact owned sessions terminated and exact owned database dropped.
- [x] Failed operation and failed cleanup both reported; original operation failure is retained.
- [x] Independent/parallel ownership and protected/non-test-name rejection tested.
- [x] No wildcard normal teardown or automatic pre-suite stale deletion.
- [x] One-time orphan cleanup reviewed/materialized exact candidates, then revalidated their identities individually.
- [x] All **945** approved removals match the removal log; [manifest](POSTGRES_TEST_DATABASE_CLEANUP_20261006.csv) contains every exact name, OID, owner, session/marker evidence, invalid-drop flag and creating fixture.
- [x] Five original protected/system databases remain after both repeated runs and final full-suite validation.

**Root cause:** Overlong prefix-plus-GUID identifiers were silently truncated by PostgreSQL. Cleanup looked up their original names and returned early. Eleven invalid catalog remnants from interrupted prior DROP operations required exact DROP without ALTER.

**Orphan policy:** Approved full-GUID or precisely historical truncated prefix/hex name plus test marker, current-role ownership, non-template status, no current-run/active registration or sessions. No running testhost/vstest process before deletion. Name/OID/marker/owner/session evidence must match the reviewed manifest immediately before each drop. Prefix or age alone never authorizes deletion. A killed process requires this explicit bounded recovery; normal fixture teardown never sweeps other databases.

| Checkpoint | Passed tests | Total databases | Test databases |
|---|---:|---:|---:|
| Before authorized orphan cleanup | — | 950 | 945 |
| After cleanup / final-run baseline | — | 5 | 0 |
| Final PostgreSQL bundle run 1 | 90 | 5 | 0 |
| Final PostgreSQL bundle run 2 | 90 | 5 | 0 |
| Post-cleanup full backend Release suites | 1,320 | 5 | 0 |
| Post-cleanup PostgreSQL bundle run 1 | 90 | 5 | 0 |
| Post-cleanup PostgreSQL bundle run 2 | 90 | 5 | 0 |

Exact preserved inventory: `iranimart_ecommerce`, `postgres`, `retailpos_db`, `template0`, `template1`. Databases absent from the initial inventory were not created, modified or selected.

## PostgreSQL creator inventory

All paths below are relative to `tests/RetailPOS.IntegrationTests`. All 24 files already called the shared lifecycle; the shared implementation and its tests are repaired rather than claiming a nonexistent mass migration. Sales/Journal Number harnesses retain `await using`; other creators retain `RunAsync`/scoped resource disposal. Files marked **initialization hardened** are the seven actual context-helper edits.

| Area | Audited creator files |
|---|---|
| Accounting (11) | `AccountingFinancialYearCloseExecutionPostgresTests.cs`; `AccountingFinancialYearClosePreparationPostgresConcurrencyTests.cs`; `AccountingPeriodCloseExecutionPostgresConcurrencyTests.cs`; `AccountingPeriodClosePreparationPostgresConcurrencyTests.cs`; `ExpenseApprovalPostgresConcurrencyTests.cs`; `JournalNumberRelationalConcurrencyTests.cs`; `OpeningBalancePostgresConcurrencyTests.cs`; `OtherIncomeApprovalPostgresConcurrencyTests.cs`; `P306AdjustmentPostgresConcurrencyTests.cs`; `P306ReconciliationPostgresConcurrencyTests.cs`; `SettlementPostingPostgresConcurrencyTests.cs`. |
| Inventory (7) | `StockCountCreatePostgresConcurrencyTests.cs`; `StockCountAdjustmentGenerationPostgresConcurrencyTests.cs`; `StockAdjustmentApprovalPostgresConcurrencyTests.cs` (**initialization hardened**); `StockAdjustmentReversePostgresConcurrencyTests.cs` (**initialization hardened**); `StockTransferSubmitAndDispatchPostgresConcurrencyTests.cs` (**initialization hardened**); `StockRequisitionConversionPostgresConcurrencyTests.cs` (**initialization hardened**); `StockRequisitionLifecyclePostgresConcurrencyTests.cs` (**initialization hardened**). |
| Purchase (2) | `GrnCompletionRelationalTests.cs` (**initialization hardened**); `PurchaseAndReceiveRelationalTests.cs` (**initialization hardened**). |
| Sales (1) | `SaleCreationPostgresConcurrencyTests.cs`. |
| Pricing (1) | `PriceRuleReadStorePostgresTests.cs`. |
| Root (1) | `MigrationChainValidationTests.cs`. |
| Infrastructure (1) | `PostgresTestDatabaseLifecycleTests.cs` (**expanded**); shared implementation `PostgresTestDatabase.cs` owns creation/drop and is not an additional caller fixture. |

## Final validation commands and results

All .NET commands run at repository root; EF uses `src\RetailPOS.Infrastructure\RetailPOS.Infrastructure.csproj` and startup `src\RetailPOS.API\RetailPOS.API.csproj`. Angular commands run in `retailpos-frontend`.

| Command/gate | Result |
|---|---|
| `dotnet test tests\RetailPOS.IntegrationTests\RetailPOS.IntegrationTests.csproj -c Release --no-build --filter "FullyQualifiedName~PostgresTestDatabaseLifecycleTests\|FullyQualifiedName~PurchaseDocument\|FullyQualifiedName~GrnDetailHttpTests\|FullyQualifiedName~CleanArchitectureDependencyTests" --logger "console;verbosity=minimal"` | **41/41**, zero failed/skipped: 27 lifecycle plus 14 PDF/read-store/architecture cases. Architecture class contains ten cases. |
| `dotnet test tests\RetailPOS.UnitTests\RetailPOS.UnitTests.csproj -c Release --no-build --filter "FullyQualifiedName~GetPurchaseOrderByIdQueryHandlerTests\|FullyQualifiedName~GetGrnByIdQueryHandlerTests" --logger "console;verbosity=minimal"` | **10/10**, zero failed/skipped. |
| `dotnet test tests\RetailPOS.IntegrationTests\RetailPOS.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Postgres\|FullyQualifiedName~JournalNumberRelationalConcurrencyTests\|FullyQualifiedName~MigrationChainValidationTests\|FullyQualifiedName~GrnCompletionRelationalTests\|FullyQualifiedName~PurchaseAndReceiveRelationalTests" --logger "console;verbosity=minimal"` | **90/90 twice**, second run `--no-build`; exact 5/0 inventory after each. Intermediate 88-case version also passed twice before initialization-failure cases were added. |
| Full `dotnet test` on UnitTests and IntegrationTests projects with `-c Release --no-build --logger "console;verbosity=minimal"` | **241 UnitTests + 1,079 IntegrationTests = 1,320 passed**, zero failed/skipped. |
| Exact 177-case SQLite selection, `-c Release --no-build --filter` from audited discovered cases | **177/177**, zero failed/skipped: 167 in-memory + 10 temporary-file cases; three mixed-provider files narrowed to actual SQLite cases. |
| Focused `JwtConfigurationStartupTests`, `AuthenticationLifecycleCharacterizationTests`, `RoleGovernanceRelationalAuditTests`, `ProductVariantSearchIdentityHttpTests`, `CleanArchitectureDependencyTests` | **30/30**, zero failed/skipped. |
| `dotnet build RetailPOS.slnx -c Release --nologo` | **Succeeded, 0 errors, 0 warnings**; NU1903 resolved, not suppressed. |
| `dotnet publish src\RetailPOS.API\RetailPOS.API.csproj -c Release --no-restore` to session-only output | **Succeeded**; zero SQLite dependency entries/binaries, no user-secrets file or tracked JWT key; transitive Infrastructure BCrypt retained. |
| `dotnet ef migrations has-pending-model-changes --project src\RetailPOS.Infrastructure\RetailPOS.Infrastructure.csproj --startup-project src\RetailPOS.API\RetailPOS.API.csproj --configuration Release --no-build` | **No model changes**. |
| `dotnet ef migrations list --project src\RetailPOS.Infrastructure\RetailPOS.Infrastructure.csproj --startup-project src\RetailPOS.API\RetailPOS.API.csproj --configuration Release --no-build` | Connected read-only inventory: **`20261004095137_InitialBaseline` only**, no pending annotation. |
| `npm test -- --watch=false` | **63/63 tests, 13 files passed**. |
| `npm run build` | **Production build succeeded**. |
| Source callers and changed-file diagnostics | No active removed-service type callers; no changed-file diagnostic errors. |
| `git diff --check` and separate documentation/CSV hygiene | Passed; existing LF/CRLF notices are not failures. |
| Final PostgreSQL inventory | Exactly five protected/system databases; **zero test databases**. |

## Final repository cleanup and dependency remediation

- [x] Responsibility/caller audit distinguishes proven cleanup from legitimate adapters and ambiguous dependencies; no invented architectural work.
- [x] Removed redundant direct API BCrypt 4.0.3 reference; Infrastructure owns implementation/reference and production still resolves the same package.
- [x] No additional production/test service, interface, repository, method or DTO was sufficiently caller-proven unused for removal; active token/governance/PDF contracts retained.
- [x] IntegrationTests-only native override resolves **`SQLitePCLRaw.lib.e_sqlite3/2.1.12`**; restored win-x64 engine is **3.53.3**.
- [x] EF SQLite stays 9.0.0, managed bundle/core/provider stays 2.1.10, Npgsql stays 9.0.2; no production EF/Npgsql upgrade or NU1903 suppression.
- [x] API/Infrastructure/Application/Domain/UnitTests assets and production publish have zero SQLite dependencies.
- [x] All 177 SQLite cases preserve transaction/rollback/trigger/retry/idempotency, auth refresh and temporary-file behavior.
- [x] Owner-approved JWT-key externalization retains effective local value only in development user-secrets; deployments use `JwtSettings__Secret`; missing/blank key startup fails explicitly.
- [x] Three test-host factories supply an explicit test-only key; validation runs after host configuration; negative startup and auth/HTTP/OpenAPI regressions pass.
- [x] Removed only inventoried retired build/results artifacts, old root log/two TRX files, accidental nested EF targets and verified empty folders; no history/baseline/evidence deletion.
- [x] Related authentication documentation reconciles current owners, baseline and separate logout contracts; historical migration evidence remains labelled.
- [x] `.gitignore` covers new SQLite sidecar/results/root-log patterns; source/evidence files remain visible, generated/local files ignored.
- [x] All modified/untracked file inventory reviewed: **152 modified, 284 deleted, 1,364 untracked**. No staging/commit/push/revert; pre-existing unrelated work preserved.
- [x] Five architecture evidence files, the 945-name orphan manifest and relevant test/auth documentation retained for commit review.
- [x] No new database deletions; full-suite and repeated 90-case PostgreSQL inventories each return to five protected/system databases and zero test databases.

**Ambiguous/unrelated — unchanged:** potential `System.IO.Packaging` pin, npm dependencies without proven unused status, three untracked database SQL scripts, accounting/HR requirement/design documents. See the [plan's exact cleanup/readiness inventory](CLEAN_ARCHITECTURE_MEDIATR_CQRS_MIGRATION_PLAN.md#final-repository-cleanup-and-dependency-remediation--2026-10-06).

**Security scope limitation:** JWT externalization is not deployment-key rotation or Git-history removal. Owners must rotate exposed deployment keys. PostgreSQL development credentials/Compose/seed configuration and Compose port exposure remain unchanged by explicit JWT-only approval and require separate owner review before public sharing/deployment. No blanket security certification.

## Optional maintenance — not mandatory migration gaps

- Rename historical `RetailPOS.Tests` namespaces; the project itself is already retired.
- Consolidate/rename adapters or move accounting-poster mappings only if future behavior evidence justifies it.
- Propagate cancellation through legacy non-cancellable accounting-configuration adapter contracts in a separate bounded task.
- Consider crash recovery automation only with a separately approved active-run ownership protocol; no automatic broad stale sweep is introduced.

This checklist accepts only the frozen target from verified implementation and validation. It is not a claim of universal architecture perfection or absence of unrelated defects. No staging, commit, push, revert, unrelated change overwrite or extra numbered slice.

## Official replacement migration baseline — 2026-10-13

- The migration inventory recorded above is historical. The official chain for future environments is the replacement chain beginning at `20261004095137_InitialBaseline`, followed by its forward migrations through `20261013041308_RestoreAuditAndWalkInDatabaseProtections`.
- This is a fresh-install baseline only. The current `retailpos_db` is Development-only with disposable fake/demo data; no production database, customer data, or deployed legacy database exists. Supporting the superseded migration IDs is explicitly out of scope.
- Do not reconcile or rewrite `__EFMigrationsHistory` to bridge the superseded chain. Create future environments from an empty database using the replacement chain.
- The replacement chain retains `expenses.description` at the current model's `varchar(500)` length and restores database-level append-only audit and system/walk-in customer protections in a forward migration.
- The Phase 1D employee-type seed SQL terminates its `INSERT` statement explicitly so generated PostgreSQL idempotent migration scripts can execute.
- Database defaults are defined by the current model and application semantics. Legacy-only defaults that are not part of current model behavior are not reintroduced; current model-configured defaults remain in the replacement schema.
