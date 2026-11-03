# IraniMart POS v2 — Local Infrastructure and Docker Cutover

**Status:** CUTOVER EXECUTED 17 Oct 2026 — the `iranimart-dev` (six services) and `iranimart-test` (four services) Compose stacks are the active local infrastructure; `retailpos_db` was restored and verified; legacy containers (project `compose` and `retailpos-a2-pgtest`) are stopped and retained with all volumes. See [DOCKER_DATA_PRESERVING_CUTOVER.md](./DOCKER_DATA_PRESERVING_CUTOVER.md) for evidence and rollback. Sections below that describe "legacy" endpoints are historical.  
**Updated:** 17 October 2026  
**Cutover plan, evidence and rollback:** [DOCKER_DATA_PRESERVING_CUTOVER.md](./DOCKER_DATA_PRESERVING_CUTOVER.md)  
**Scope:** Local Development, isolated Integration Tests, and Copilot instructions.

## 1. Operating policy

- `docker/compose.dev.yml` and `docker/compose.test.yml` are the intended standard Compose definitions. Their presence does not mean a cutover has occurred.
- Preserve all data in legacy `retailpos_db`, including users, roles, products, stock, sales, accounting, migration history, and sequences. Do not remove original volumes until a verified, recoverable cutover and separate cleanup approval.
- **Cutover data scope (Product Owner, 17 Oct 2026):** restore ONLY `retailpos_db`. `iranimart_ecommerce`, legacy `retailpos_*` fixture databases, qsmart-ai-agent databases, `qbo.*` RabbitMQ queues and the Elasticsearch `products` index are not restored and must not be deleted or altered in their legacy volumes or backups. All six Development services (including Kibana and pgAdmin) and four Test services are required; the standard Development startup uses the `admin` profile (`COMPOSE_PROFILES=admin` in `docker/.env.dev`). API-05B/05C migrations stay unapplied.
- **Before cutover**, use only the *verified legacy endpoints* below. **After cutover**, use target endpoints only after authenticated connectivity, restore checks, and test-fixture migration pass.
- Do not stop or replace containers, delete volumes, seed/migrate persistent databases, or change running background workers without explicit cutover approval. The legacy containers belong to the separate `qsmart-ai-agent` Compose project: stop them only after the Product Owner confirms the downtime and impact on that project (see DOCKER_DATA_PRESERVING_CUTOVER.md §7). Stopping never authorizes deleting data.
- Do not reveal passwords in documentation, code, logs, or chat. Existing `.env` secrets and local backups must remain untracked.

## 2. Infrastructure inventory: retired legacy vs active target

**Retired (stopped 17 Oct 2026, retained for rollback):** the "legacy" column below. **Active:** the two Compose columns.

All endpoints below are **Windows-host-side**; Docker-internal clients use service/network DNS instead of `127.0.0.1`.

| Component | Retired legacy endpoint / identity | Active Development Compose | Active Test Compose |
|---|---|---|---|
| PostgreSQL | `postgres` (Compose project `compose`, owned by the separate `qsmart-ai-agent` stack, volume `compose_pgdata`), `127.0.0.1:5435`, `retailpos_db` | `127.0.0.1:5435`, persisted `retailpos_db` after restore | `127.0.0.1:55432`, isolated disposable fixture DBs |
| PostgreSQL tests | `retailpos-a2-pgtest-20261014`, `127.0.0.1:55432` | — | Replaces A2 *after fixture switch* |
| RabbitMQ AMQP | `rabbitmq`, `127.0.0.1:5672` (plain AMQP) | `127.0.0.1:5672` | `127.0.0.1:5673` |
| RabbitMQ management | `127.0.0.1:15672` | `127.0.0.1:15672` | `127.0.0.1:15673` |
| Redis | `127.0.0.1:6382`, password required | `127.0.0.1:6382` | `127.0.0.1:6383` |
| Elasticsearch | `http://127.0.0.1:9200`, basic auth | `http://127.0.0.1:9200` | `http://127.0.0.1:9201` |
| Kibana | `http://127.0.0.1:5601` | `5601` (**required**; `admin` profile, enabled by `COMPOSE_PROFILES=admin`) | Not included |
| pgAdmin | `http://127.0.0.1:8081` | `8081` (**required**; `admin` profile, enabled by `COMPOSE_PROFILES=admin`) | Not included |

Target Compose project names are `iranimart-dev` and `iranimart-test`; use **separate Docker networks and named volumes**. They do not automatically attach to legacy volumes. Existing Compose files (`docker-compose.yml`, `docker-compose.a2-postgres.yml`, `.env.a2-postgres`) remain temporarily for rollback.

## 3. Current verified legacy evidence (16 October 2026)

- A2 PostgreSQL: `pg_isready` accepted local container connections; Windows TCP `127.0.0.1:55432` succeeded; focused API-05B/05C PostgreSQL tests were reported passing.
- Development PostgreSQL: authenticated read-only query succeeded at `127.0.0.1:5435`.
- RabbitMQ: existing shared broker `5672` had separate legacy vhosts **`retailpos_dev`** and **`retailpos_test`** with restricted users. Live publish/consume/ack was reported in test vhost; plain AMQP is **not** production AMQPS acceptance.
- Redis: authenticated PING and isolated test key round-trip were reported successful.
- Elasticsearch: authenticated health green and isolated test-index create/search/delete were reported successful; application client is not yet implemented.

These are historical test results **for the legacy stack only**. They do not validate the newly proposed Compose stacks.

## 4. Compose configuration locations

| File | Responsibility |
|---|---|
| `docker/compose.dev.yml` | The six required Development services (PostgreSQL, RabbitMQ, Redis, Elasticsearch, Kibana, pgAdmin); Kibana/pgAdmin are in the `admin` profile, which `.env.dev` always enables |
| `docker/compose.test.yml` | Isolated Integration Test services |
| `docker/env.dev.example` / `env.test.example` | Non-secret variable names and examples; never real passwords |
| `docker/.env.dev` / `.env.test` | Local secrets for Compose; gitignored (created from the existing user-secrets) |
| `docker/Set-TestUserSecrets.ps1` | Repoints the `RETAILPOS_TEST_*` user-secrets at the Test stack (`-Apply` only at cutover) |
| `backups/local-cutover/` | Gitignored dumps, manifest, snapshots; never commit |
| `src/RetailPOS.API/appsettings.json` | Shared non-secret defaults |
| `src/RetailPOS.API/appsettings.Development.json` | Development options and non-secret host endpoints |
| `src/RetailPOS.API/appsettings.Staging.json` | Non-secret staging policy; deployment-provided endpoints/secrets |
| `src/RetailPOS.API/appsettings.Production.json` | Non-secret production policy; deployment-provided endpoints/secrets |

**Staging and Production must never fall back to local Docker endpoints.** Preserve options/startup validation and explicit background-worker feature flags. Do not enable DurableDelivery or Elasticsearch merely because Docker containers are running.

## 5. Discrepancies: resolved in the Compose files vs. still open

**Resolved (17 Oct 2026, validated with `docker compose config`, not yet deployed):**

1. **RabbitMQ vhosts:** Compose defaults now use the legacy/test-constant names `retailpos_dev` / `retailpos_test` (no URI-encoding needed). Legacy vhosts had no queues or custom exchanges, so nothing needs transferring.
2. **Redis authentication:** Dev and Test Redis now start with `--requirepass` (password from `DEV_REDIS_PASSWORD` / `TEST_REDIS_PASSWORD` via `REDISCLI_AUTH`; syntax rehearsed in a throwaway container). Dev keeps AOF persistence; Test stays ephemeral.
3. **Kibana credentials:** a one-shot `kibana-bootstrap` service (admin profile) sets the `kibana_system` password through the Elasticsearch API before Kibana starts. The password must not contain `"` or `\`.
4. **Elasticsearch health check:** authenticated and waits for yellow.

**Still open:**

5. **Elasticsearch TLS:** local Compose deliberately uses HTTP with basic authentication, bound to `127.0.0.1`. Do not present this as TLS-enabled or Production-approved. Preserve security settings and index isolation.
6. **RabbitMQ DurableDelivery:** current `RabbitMqDeliveryOptions.Validate()` requires `amqps://`; proposed local brokers only expose plain AMQP. Do not change or disable this validation just to turn on the production delivery pipeline. Keep the existing safe sweeper fallback and treat AMQPS live acceptance as pending until an approved TLS-enabled test topology exists.
7. **Backups and volumes:** target Development PostgreSQL uses a *new* Compose volume; **restore the final verified PostgreSQL backup** before the API reconnects. The target Test stack replaces the old A2 instance; the fixtures read `RETAILPOS_TEST_*`, so they only need `docker/Set-TestUserSecrets.ps1 -Apply` at cutover.
8. **Port conflicts and shared ownership (approval gate):** legacy Development containers own the target Dev ports and belong to a different Compose project (`qsmart-ai-agent`) that also hosts `iranimart_ecommerce`, RabbitMQ `/` queues `qbo.*` and an Elasticsearch `products` index. They are **not stopped** until the Product Owner explicitly confirms the downtime and the impact on that project; stopping never authorizes deleting data. Legacy A2 owns `55432`. Do not run both stacks on conflicting ports.
9. **Pending EF migrations:** `Api05BMobileStockCountOperations` and `Api05CMobileStockTakeSubmissionJobs` exist in code but are not applied to Development; do not apply them without approval.
10. **Secrets:** `docker/.env.dev` / `.env.test` exist locally (gitignored) and were derived from the existing user-secrets; never print `docker compose config` output without `--quiet`.

## 6. PostgreSQL data-preserving cutover requirements

**Preflight / no downtime yet:**

1. Inventory existing DB roles/extensions, migration history, test DBs, volumes, writers and scripts referencing legacy Compose.
2. Validate both Compose files and env templates without starting conflicting services.
3. Create a timestamped `pg_dump -Fc` backup of `retailpos_db`; capture required global role/ownership definitions separately where needed.
4. Verify archive list and perform a genuine isolated restore rehearsal. Check tables, indexes, constraints, sequences, extensions, `__EFMigrationsHistory`, and critical row counts. Never overwrite A2 fixture-owned databases.
5. Create rollback instructions identifying old Compose project, container IDs, volume names, original connection strings and background workers.
6. **Stop and request explicit approval for service cutover.**

**Approved cutover only:**

1. Enter maintenance mode; stop all API, POS writers and workers (including outbox/sweeper), ensure no writes continue to the legacy DB.
2. Take the **final** backup after write quiescence and verify it; backup from before the freeze is not authoritative for final restore.
3. Stop only identified legacy containers **without** `down -v`, volume deletion or container prune.
4. Start target Development Compose and restore the final dump into its new PostgreSQL volume; restore required roles/extensions and verify ownership.
5. Validate critical counts and business data, migrations, settings, API authentication, POS sales read paths, stock count states, and accounting smoke tests before releasing traffic.
6. Start target Test Compose, migrate test-fixture configuration, and run safe focused PostgreSQL + messaging + Redis + Elasticsearch tests using isolated test resources.
7. Verify RabbitMQ pending-message/outbox continuity, Redis cache strategy, and Elasticsearch rebuild/snapshot requirements. Broker *definitions export does not preserve queued messages*.
8. Resume only the approved workers and traffic; confirm no old+new duplicate writers or consumers remain.
9. Keep legacy volumes and backup artifacts until independent verification and separate deletion approval.

**Abort/rollback:** If restore or validation fails, keep new writes disabled, restore original legacy services/connection settings and investigate. Never start both write-capable stacks simultaneously. If post-cutover writes occurred, assess reconciliation before rolling back.

## 7. RabbitMQ, Redis and Elasticsearch test isolation

- Prefer independent physical brokers for target Development (`5672`) and Test (`5673`) rather than the old shared-vhost approach. Development/Test credentials, exchanges, queues and consumers must never overlap.
- In tests, do not purge Development RabbitMQ queues, Redis keys or Elasticsearch indices. Test cleanup must target positively identified test-owned resources only.
- Test Redis (`6383`) is ephemeral under the proposed Compose; this is appropriate only for test-owned cache data. Development Redis remains persistent, with authentication policy resolved before migration.
- Elastic indices must use distinct environment/test-specific prefixes. Elasticsearch client/DI registration is feature-dependent: its presence in Compose does **not** imply it is registered in `Program.cs`.
- A running container does not prove authenticated connectivity or an actual publisher/consumer/client integration test.

## 8. Application and integration-test configuration

- Preserve `InfrastructureConfigurationValidator`/existing environment validators and existing DI extension pattern; configure PostgreSQL, RabbitMQ, Redis and Elasticsearch through the current registrations, without duplicating clients/hosted services.
- Key families: `ConnectionStrings:DefaultConnection`, `ConnectionStrings:Redis`, `PosCache:InstanceName`, `DurableDelivery:*`, `MobileStockTake:Submission:WorkerEnabled`, `Elasticsearch:*`, `Database:MigrateOnStartup`, `Database:SeedOnStartup`, `JwtSettings:Secret`.
- Staging/Production must use deployment-provided separate, non-local credentials/endpoints and appropriate TLS, with fail-fast sanitized validation for missing mandatory values.
- For host-run Copilot tests after cutover, test endpoint defaults are `127.0.0.1:55432` (Postgres), `127.0.0.1:5673` (RabbitMQ), `127.0.0.1:6383` (Redis), and `127.0.0.1:9201` (Elasticsearch). These are **targets**, not yet verified connectivity.
- Update `RETAILPOS_TEST_POSTGRES`, `RETAILPOS_TEST_RABBITMQ_URI`, `RETAILPOS_TEST_REDIS`, `RETAILPOS_TEST_ELASTICSEARCH_URI`, `_USER`, `_PASSWORD` **only at the approved cutover**, using `docker/Set-TestUserSecrets.ps1 -Apply` (reads `docker/.env.test`, writes via stdin, prints no secrets). Until then the test secrets still point at legacy ports (`55432`, `5672`/`retailpos_test`, `6382`, `9200`) with isolated namespaces. Preserve explicit rejection of destructive tests pointed at Development port `5435`.
- `dotnet ef database update`, seeding and schema changes against persistent Development/Staging/Production require separate approval. Production schema changes go through a release process, not startup.
- PostgreSQL-backed tests fail closed: `PostgresTestDatabase.CreateAsync` (the single entry point for every disposable database) rejects the Development port `5435` and any `retailpos_db*` database name, and `InfrastructureTestSettings.RequireIsolatedPostgres()` throws when `RETAILPOS_TEST_POSTGRES` is unset. There is no fallback to `appsettings`/Development; start the Test stack (`docker/compose.test.yml`) and configure the user-secret first. The Sale PostgreSQL harness builds its schema with EF migrations, not `EnsureCreated`.
- Never put credentials in these documentation files. Use User Secrets, environment variables, or an approved deployment secret store.

## 9. Safe diagnostics (read-only before cutover)

```powershell
# Existing running containers and ports
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"

# Validate target Compose syntax (avoid printing resolved secrets)
cd docker
docker compose --env-file .env.dev -f compose.dev.yml config --quiet
docker compose --env-file .env.dev -f compose.dev.yml config --quiet   # includes the admin profile via COMPOSE_PROFILES
docker compose --env-file .env.test -f compose.test.yml config --quiet

# Legacy A2 readiness and TCP connectivity
docker exec retailpos-a2-pgtest-20261014 pg_isready
Test-NetConnection 127.0.0.1 -Port 55432

# Legacy broker listeners and virtual hosts
docker exec rabbitmq rabbitmq-diagnostics listeners
docker exec rabbitmq rabbitmqctl list_vhosts
```

The Compose syntax checks require safely populated local `.env.dev` and `.env.test`. They do **not** start or stop containers and they do **not** prove application connectivity.

## 10. Acceptance and project status

| Gate | Status now |
|---|---|
| Legacy stacks (project `compose`, `retailpos-a2-pgtest`) | **Retired and stopped 17 Oct 2026**; containers, volumes and backups retained for rollback |
| Target Compose syntax and credential compatibility | **Validated** (`config --quiet`) and deployed |
| Final PostgreSQL backup | **Verified** — `backups/local-cutover/retailpos_db_FINAL_20261017T104348Z.dump` |
| Controlled service cutover | **Executed 17 Oct 2026**: six Development and four Test services healthy |
| New Development `retailpos_db` restored and application smoke-tested | **Passed** — 0 differences vs the final source backup |
| New Test Compose services / migrated tests | **Passed** — test user-secrets repointed, 56 focused tests green |
| API-05B / API-05C migrations | **Applied to Development 17 Oct 2026** (58 migrations; additive; verified) — DOCKER_DATA_PRESERVING_CUTOVER.md §8 |
| `PurchaseOrderOutletDestination` migration | **Applied to Development 23 Oct 2026** with approval (61 migrations; additive: `purchase_orders.warehouse_id` nullable, `outlet_id` + FK, single-destination check, outlet idempotency index). Pre-migration backup `backups/local-cutover/retailpos_db_PREMIG_PODEST_20261023T082613Z.dump`; script `migrate_podest_20261023T082613Z.sql`. Existing 3 POs unchanged (all warehouse) |
| POS v2 Phase 1+2 migrations (`PosV2P1CheckoutPaymentEvidenceAndIdempotency`, `PosV2P2TenancyTaxSettingsInvoiceNumbering`) | **Applied to Development 2 Nov 2026** (63 migrations; additive; gated per the Phase 2 prompt: verified backup `backups/phase2/retailpos_db-pre-phase2-20261102-190815.dump`, rehearsal on a restored copy, SQL reviewed). Row counts unchanged; all existing customers/catalogue/sales backfilled to the single Business. Details: `Docs/08_Sales_And_POS/POS_V2_IMPLEMENTATION_PROGRESS.md` §7.9 |
| POS v2 Phase 2.1 migration `PosV2P21PricingRuleTenancy` (`price_rules.business_id`, `outlet_price_overrides.business_id`, cross-Business triggers) | **Generated, NOT applied to Development (still 63 migrations).** Rehearsed only on disposable Test-server databases: a read-only dump of Dev restored to a scratch DB and migrated (63 → 64, `business_id` NOT NULL, both triggers present, row counts unchanged), then dropped. Dev currently holds 0 price rules and 0 outlet overrides, so the backfill is unambiguous. Apply only with explicit approval — `Docs/08_Sales_And_POS/POS_V2_IMPLEMENTATION_PROGRESS.md` §8.3 |
| Test stack (Phase 2.1) | All four Test services (PostgreSQL 55432, RabbitMQ 5673, Redis 6383, Elasticsearch 9201) were started with `docker compose --env-file .env.test -f compose.test.yml -p iranimart-test up -d`; `LiveInfrastructureTests` pass. Staging/Production `appsettings` no longer carry a Development connection-string fallback (they fail startup until deployment supplies `ConnectionStrings:DefaultConnection`) |
| Redis password rotation | **Development rotated and verified**; Test Redis never exposed (unchanged) — §9 of the cutover doc |
| Legacy container/volume deletion | **Not authorized** |

Update this document with actual evidence **after** each gate; never replace legacy facts with unverified target-state claims. Keep backups, data, and existing Git changes intact. Do not stage or commit without explicit instruction.
