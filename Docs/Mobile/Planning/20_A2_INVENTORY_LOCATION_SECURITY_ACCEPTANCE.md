# A2 Inventory Location Security Acceptance

**Scope:** Security remediation A2 only — inventory reads using caller-supplied or omitted Outlet/Warehouse locations  
**Implementation status:** **PASSED**  
**A2 PostgreSQL environment status:** **PROVISIONED; READ-ONLY CONNECTION VERIFIED**  
**A2 PostgreSQL closeout status:** **PASSED — REAL POSTGRESQL HTTP TESTS VERIFIED**  
**Database/migration status:** No migration, seeder, persistent Development database, or production data was accessed or changed.  
**Git status:** No commit created. Existing worktree changes were preserved.

## Outcome

Inventory reads in this slice now authorize explicit locations through the existing location-access service before dispatching inventory queries. Every scoped query receives a non-null `InventoryLocationScope` containing only authorized Outlet and Warehouse IDs. Omitted filters preserve the Angular “All Locations” behavior, but resolve to the caller's authorized set instead of an unbounded database query.

Super Admin inventory access requires the middleware-validated `X-Business-Id` context. Inventory reads apply a Business-scoped authorized-location list for this selected Business, while ordinary shared location access continues to preserve existing POS behavior. Rejected requests return a generic 403 and do not dispatch an inventory query.

The A2 implementation and PostgreSQL-backed verification passed. The initial closeout was blocked because no isolated PostgreSQL target was available; after the dedicated target was provisioned, a real PostgreSQL HTTP fixture was added and all six focused tests passed. The test fixture creates a uniquely named, ownership-marked database, applies migrations and seed data only there, and the helper removes that exact database during fixture disposal.

## Exact endpoints in scope

| Verb | Route | Authorization behavior |
|---|---|---|
| `GET` | `/api/inventory/outlet/{outletId}` | Requires an authorized Outlet in the caller's Business/location set. |
| `GET` | `/api/inventory/warehouse/{warehouseId}` | Requires an authorized Warehouse in the caller's Business/location set. |
| `GET` | `/api/inventory/low-stock` | Explicit filters are authorized; omitted filters use the authorized location set. Existing `low_stock_alerts.view` policy is retained. |
| `GET` | `/api/inventory/out-of-stock` | Explicit filters are authorized; omitted filters use the authorized location set. |
| `GET` | `/api/inventory/expiring-soon` | Explicit filters are authorized; omitted filters use the authorized location set. |
| `GET` | `/api/inventory/valuation` | Explicit filters are authorized; omitted filters use the authorized location set. |
| `POST` | `/api/inventory/search` | Explicit Outlet/Warehouse filters are authorized; omitted filters use the authorized location set. |
| `GET` | `/api/inventory/variant-attribute-filters` | Explicit filters are authorized; omitted filters use the authorized location set. |

The verbs and route signatures were verified in the current `InventoryController`. Search is `POST`; the other listed routes are `GET`.

## Authorization rules implemented

- Uses authenticated user identity, effective role/context, existing endpoint permission policies, and centralized `IUserOutletAccessService`.
- Explicit Outlet and Warehouse requests are validated separately, with the requested location type enforced.
- Unauthorized, unknown, foreign-Business, same-Business unassigned, and wrong-type IDs are rejected before inventory-query dispatch.
- For callers without Super Admin privileges, omitted location filters resolve to the existing authorized Outlet and Warehouse sets.
- Super Admin inventory calls require a validated `BusinessContextMiddleware` item that matches the effective Business context. The controller obtains a location set scoped to that selected Business. An explicit location must also belong to that scoped set.
- Omitted filters no longer imply “all database locations”; empty authorized sets remain empty query scopes.
- Existing permission policies remain in place, including the additional low-stock permission.
- Authorization denial is logged without disclosing the requested location details; the response is a generic 403.
- Angular callers that omit the optional filters retain their “All Locations” interface, constrained to the locations they are authorized to access.

## HTTP negative/positive test matrix

The focused HTTP fixture uses `RetailPosApiFactory` with an isolated EF Core InMemory database. The strict mock sender verifies that rejected requests do not dispatch inventory queries.

| Scenario | Expected/observed result |
|---|---|
| Assigned Outlet read (`5001`) | `200 OK`; authorized result returned. |
| Assigned Warehouse read (`2001`) | `200 OK`; authorized result returned. |
| Same-Business unassigned Outlet (`5003`) | `403 Forbidden`; no inventory query dispatched. |
| Same-Business unassigned Warehouse (`2002`) | `403 Forbidden`; no inventory query dispatched. |
| Foreign-Business Outlet (`6001`) | `403 Forbidden`; no inventory query dispatched. |
| Foreign-Business Warehouse (`6002`) | `403 Forbidden`; no inventory query dispatched. |
| Outlet route supplied a Warehouse ID (`2001`) | `403 Forbidden`; no inventory query dispatched. |
| Warehouse route supplied an Outlet ID (`5001`) | `403 Forbidden`; no inventory query dispatched. |
| Unknown Outlet (`999999`) | `403 Forbidden`; no inventory query dispatched. |
| Explicit unauthorized filters on low-stock, out-of-stock, expiring, valuation, search, and attribute-filter routes | `403 Forbidden`; no inventory query dispatched. |
| Omitted filters on all six report/search/filter paths | `200 OK`; returned data and attribute options exclude unauthorized-location test records. |
| Super Admin without validated Business context | `403 Forbidden`; no inventory query dispatched. |
| Super Admin with validated Business 1 context requesting Business 2 Outlet `6001` | `403 Forbidden`; validation-context request is the only sender dispatch. |
| Low-stock caller lacking `low_stock_alerts.view` | `403 Forbidden`; existing permission policy retained. |

The fixture deliberately includes authorized, same-Business unassigned, and foreign-Business Outlet/Warehouse records. The omitted-filter assertions verify that secret variant attributes from unassigned and foreign locations are absent.

## Initial PostgreSQL isolation gate — 2026-10-14

**No PostgreSQL test was run.** The safety precondition could not be proven:

- The test infrastructure reads `RETAILPOS_TEST_POSTGRES` as the configured PostgreSQL base connection. A local environment check found this variable unset; no connection-string value, credentials, or database contents were read or displayed.
- `PostgresTestDatabase.CreateAsync` would connect to that server's `postgres` database, create a randomized, allowlisted test database name containing a full GUID, mark it with a per-run ownership comment, and `RunAsync` would clean up only the exact owned database after the test. This is a useful disposable-database lifecycle, but it does not establish that the *server* behind the base connection is dedicated and isolated.
- At least one existing PostgreSQL test fallback reads `src/RetailPOS.API/appsettings.json` → `ConnectionStrings:DefaultConnection`. That fallback target is not certified here as a dedicated test PostgreSQL server. It was not read, used, or attempted.
- No PostgreSQL endpoint/database target can therefore be verified as safe for this requested run. The configured target must be an approved, dedicated disposable test PostgreSQL instance—not `retailpos_db`, a production/shared server, or a database backed by an unknown-provenance Docker volume.
- No Docker/container commands, database connections, schema changes, migrations, seeders, database creation, or cleanup were performed in this closeout attempt.

At this initial blocked attempt, the existing `InventoryLocationAuthorizationHttpTests` was the available A2 HTTP fixture and used EF Core InMemory; it did not prove PostgreSQL query execution or SQL predicates. No A2 tests, test code changes, or application implementation changes occurred during that initial attempt. The later PostgreSQL-backed execution and its results are recorded below.

## Dedicated PostgreSQL test environment provisioned — 2026-10-14

The setup task created a dedicated PostgreSQL 16 endpoint using `pgvector/pgvector:pg16`:

- Compose project `retailpos-a2-pgtest`; container `retailpos-a2-pgtest-20261014`.
- Host binding `127.0.0.1:55432` → container port `5432`.
- Dedicated bridge `retailpos-a2-pgtest-net`, with only the test container attached.
- New local volume `retailpos-a2-pgtest-data`, mounted at `/var/lib/postgresql/data`; PostgreSQL reports its data directory as `/var/lib/postgresql/data/pgdata`.
- Bootstrap database `a2_test_guard`, user `a2_test_runner`; credentials are in Git-ignored `.env.a2-postgres` and are not recorded here.
- A temporary read-only Npgsql connection, explicitly built from `RETAILPOS_TEST_POSTGRES`, connected through `127.0.0.1:55432` and verified `current_database()`, `current_user`, `server_version_num` (`160013`, PostgreSQL 16.13), and `data_directory`.
- Container health is `healthy`; the host TCP endpoint is reachable. Existing Development/production containers, volumes, networks, databases, and the ERP API were not modified.
- No A2 tests, migrations, seeders, test-database creation, or cleanup were run. The temporary verification utility was removed after use.

The exact setup, explicit environment-variable configuration, isolation checks, and safe usage instructions are in [21_A2_POSTGRES_TEST_ENVIRONMENT.md](./21_A2_POSTGRES_TEST_ENVIRONMENT.md). Provisioning alone did not establish A2 PostgreSQL acceptance; the real-provider results are recorded in the validation section below.

## Files changed for A2

- API boundary and centralized access wiring:
  - `src/RetailPOS.API/Controllers/InventoryController.cs`
  - `src/RetailPOS.API/Services/IUserOutletAccessService.cs`
  - `src/RetailPOS.API/Services/UserOutletAccessService.cs`
- Application location access and inventory read-scope contracts:
  - `src/RetailPOS.Application/Identity/LocationAccess/IUserOutletAccess.cs`
  - `src/RetailPOS.Application/Identity/LocationAccess/IUserOutletAccessStore.cs`
  - `src/RetailPOS.Application/Identity/LocationAccess/LocationAccessModels.cs`
  - `src/RetailPOS.Application/Identity/LocationAccess/UserOutletAccess.cs`
  - `src/RetailPOS.Application/Inventory/Abstractions/IInventoryReadStore.cs`
  - `src/RetailPOS.Application/Inventory/Queries/InventoryLocationScope.cs`
  - `src/RetailPOS.Application/Inventory/Queries/InventorySearchCriteria.cs`
  - `src/RetailPOS.Application/Inventory/Queries/GetLowStockInventoryQuery.cs`
  - `src/RetailPOS.Application/Inventory/Queries/GetOutOfStockInventoryQuery.cs`
  - `src/RetailPOS.Application/Inventory/Queries/GetExpiringInventoryQuery.cs`
  - `src/RetailPOS.Application/Inventory/Queries/GetInventoryValuationQuery.cs`
  - `src/RetailPOS.Application/Inventory/Queries/GetVariantAttributeFilterOptionsQuery.cs`
- Infrastructure query scoping:
  - `src/RetailPOS.Infrastructure/Repositories/IInventoryRepository.cs`
  - `src/RetailPOS.Infrastructure/Repositories/InventoryRepository.cs`
  - `src/RetailPOS.Infrastructure/Inventory/InventoryReadStore.cs`
- Tests:
  - `tests/RetailPOS.IntegrationTests/Inventory/InventoryLocationAuthorizationHttpTests.cs`
  - `tests/RetailPOS.IntegrationTests/Inventory/InventoryReadHttpTests.cs`
  - `tests/RetailPOS.IntegrationTests/Identity/UserOutletAccessBehaviorTests.cs`
  - `tests/RetailPOS.IntegrationTests/Infrastructure/PostgresTestDatabase.cs` — allowlisted A2 database prefix and owned-database lifecycle.
  - `tests/RetailPOS.IntegrationTests/Inventory/InventoryLocationAuthorizationPostgresHttpTests.cs` — real PostgreSQL HTTP, SQL-scope, and disclosure tests.

The repository already contained extensive unrelated and pre-existing dirty changes; this list is the A2-related scope and is not a claim that these are the only changed files in the worktree.

## Validation evidence

| Validation | Result |
|---|---|
| Focused A2 PostgreSQL HTTP/integration tests | **6 passed, 0 failed, 0 skipped.** `InventoryLocationAuthorizationPostgresHttpTests` ran with `RETAILPOS_TEST_POSTGRES` explicitly set from the ignored local credentials file. The fixture connected through the approved endpoint, created/migrated/seeded its uniquely named temporary PostgreSQL database, executed HTTP queries through Npgsql, then dropped the owned database. |
| PostgreSQL location-query predicates and data scope | **Passed.** Outlet and Warehouse endpoint SQL contained the requested location ID and type predicate. Omitted-filter SQL contained the authorized Outlet/Warehouse `location_id` predicates; unauthorized location IDs were absent from SQL parameters. For a user with no assigned locations, SQL still carried empty location-scope arrays, and all responses were empty/zero-valued. |
| Authorization denial and non-disclosure | **Passed.** Same-Business unassigned locations, foreign-Business locations, unknown IDs, and wrong location types were denied on explicit-location routes and all six filter/search routes. Each denial dispatched no inventory SQL. The fixture verified no secret variant attributes or unauthorized quantities/valuation in responses. |
| Super Admin and permissions | **Passed.** Super Admin without validated Business context was denied before inventory SQL; selected Business 1 was scoped to its locations and did not disclose Business 2 records. Existing low-stock permission enforcement remained in place. |
| Focused Inventory/location-access/architecture integration regressions | **52 passed, 0 failed, 0 skipped.** Included `InventoryLocationAuthorizationHttpTests`, `InventoryReadHttpTests`, `InventoryReportReadHttpTests`, `UserOutletAccessHttpTests`, `UserOutletAccessBehaviorTests`, and `CleanArchitectureDependencyTests`. |
| POS compatibility regressions | **4 passed** when run separately to avoid shared-fixture interference: `Lookup_UsesEffectiveOutletAndSharedBusinessOwnerAndSuperAdminAccess`, `Lookup_CacheSeparatesOutletAndBusinessDataAndOmittedOutletRemainsUnscoped`, `CartPrices_ValidatesEverySuppliedOutletBeforeReturningPrices`, and `PricingCalculationRoutesPreserveProjectionOutletAndCacheBehavior`. |
| Angular tests | **203 passed across 39 test files.** Ran `npm test -- --watch=false` from `retailpos-frontend/`. |
| Angular build | **Passed.** Ran `npm run build` from `retailpos-frontend/`. |
| .NET Release build | **Passed, 0 errors.** `dotnet build tests\RetailPOS.IntegrationTests\RetailPOS.IntegrationTests.csproj --configuration Release --no-restore` built the test assembly and application dependencies. Earlier API Release build also passed. Existing ImageSharp `NU1902`/`NU1903` vulnerability warnings remain outside A2 scope. |
| PostgreSQL isolation and cleanup | **Passed.** Read-only container inspection confirmed `retailpos-a2-pgtest-20261014`, image `pgvector/pgvector:pg16`, Compose project `retailpos-a2-pgtest`, network `retailpos-a2-pgtest-net`, dedicated volume `retailpos-a2-pgtest-data`, and loopback-only `127.0.0.1:55432` binding. A read-only query from the test container reported `a2_test_guard|a2_test_runner|160013`, zero A2 temporary databases remaining, and zero `retailpos_db` databases on that instance. |
| Initial PostgreSQL isolation preflight | **Blocked before connection, as recorded above.** `RETAILPOS_TEST_POSTGRES` was unset at that earlier attempt; no database was created then. This was resolved by the approved isolated server and explicit environment configuration used for the successful run. |
| Persistent database changes / commit | **None.** Migrations and seed data were applied only to the temporary owned test database. No Development or production connection was configured or used; no files were staged or committed. |

The first real-provider run exposed test-fixture assumptions, not an application defect: SQL capture expected CLR-style table/column casing instead of PostgreSQL's `inventories`/`location_id`, and the expiring-soon expectation incorrectly included a zero-quantity record despite the route's existing `quantity > 0` predicate. The empty-location case also correctly executes queries with empty authorized ID arrays rather than skipping every repository query; its assertions now verify those empty SQL scopes and empty/zero-valued responses. The fixture assertions were corrected, and the final focused PostgreSQL run passed.

## Angular compatibility

The existing Angular Inventory callers may omit Outlet/Warehouse filter values to request “All Locations.” This behavior is preserved at the API level: omitted filters are resolved to the centralized service's authorized set and are applied as explicit Outlet/Warehouse ID predicates in the query path. Angular tests and production build pass. No Angular source change was required.

## Remaining risks and out-of-scope items

- **A1:** POS and stock-bearing variant-search protections were reported implemented in the prior assessment, but their full acceptance evidence remains incomplete. A2 did not change A1.
- **A3:** Direct-ID, aggregate, summary, ledger, report, and other remaining stock-read surfaces were not remediated in this slice. In particular, `/api/inventory`, `/api/inventory/{id}`, total-stock-by-variant, and location-summary require their planned A3 review; this acceptance does not claim they are safe.
- **B1–B4 Product ownership:** Ownership/identifier decisions remain unapproved; Product Business ownership schema, controlled legacy backfill, and universal Business-scoped Product enforcement remain unimplemented. A2 adds no Product ownership field, migration, uniqueness change, or backfill.
- The A2 PostgreSQL acceptance is limited to the eight routes and scenarios listed in this document; it does not certify the separate A3 direct-ID, aggregate, ledger, summary, or report surfaces.
- Existing NuGet audit warnings report vulnerable `SixLabors.ImageSharp` 3.1.12 packages; dependency remediation is outside this A2-only scope.

## Diff summary and completion gate

A2 changes add a non-null Outlet/Warehouse scope to the applicable inventory query contracts and repository predicates, authorize explicit location IDs at the API boundary, preserve the existing Angular omitted-filter contract without unrestricted queries, and add HTTP tests for location denials and no-query-dispatch behavior. Shared Super Admin location behavior used by POS remains covered by passing POS regressions; the selected-Business restriction is applied specifically to the A2 inventory boundary.

**Completion gate:** A2 PostgreSQL, Inventory/location-access, POS compatibility, architecture, and Release build gates passed. The dedicated server remains running; the per-test database was removed by the fixture and independently verified absent. Angular behavior/contracts were unchanged, so Angular regressions were not rerun during this PostgreSQL closeout; prior Angular tests/build passed.

**Final A2 closeout verdict: PASSED.** A2 is complete for the stated scope. A1 residual acceptance, A3, and B1–B4 remain separate risks and are not approved or started by this closeout.
