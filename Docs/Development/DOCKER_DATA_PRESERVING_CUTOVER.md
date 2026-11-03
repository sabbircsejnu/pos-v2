# IraniMart POS v2 — Data-Preserving Docker Cutover

**Status (17 Oct 2026): EXECUTED — PASSED WITH NOTES.** Cutover performed 17 Oct 2026 ~10:43–11:00 UTC under the Product Owner's instruction that all affected infrastructure is local (no downtime window needed). Legacy containers were stopped (not removed); all legacy volumes and backups are retained.

### Execution record
- Writers: no API process, 0 DB sessions, 0 write counters over 20 s.
- Final backup: `backups/local-cutover/retailpos_db_FINAL_20261017T104348Z.dump` (1,006,488 bytes, SHA-256 `5B4A5083…D88D51`), `pg_restore --list` OK (2,033 entries), rehearsal restore on A2 passed (only ownership differed).
- Stopped (not removed): `kibana`, `pgadmin`, `elasticsearch`, `redis`, `rabbitmq`, `postgres`, `retailpos-a2-pgtest-20261014`.
- Started: `iranimart-dev` (postgres, rabbitmq, redis, elasticsearch, kibana-bootstrap [one-shot, exit 0], kibana, pgadmin) and `iranimart-test` (postgres, rabbitmq, redis, elasticsearch).
- Restore into `iranimart-dev-postgres-1` (`retailpos_db` only): 0 snapshot differences (179 tables, 171 sequences, 56 migrations, 844 indexes, 472 FK, all row counts, key-table md5).
- Test user-secrets repointed with `Set-TestUserSecrets.ps1 -Apply` (55432 / 5673 `retailpos_test` / 6383 / 9201). 56 focused tests passed.
- API smoke (Development, migrate/seed off): Swagger 200, unauthenticated endpoint 401, login of a nonexistent user 401. The smoke wrote **one** `audit_events` row (16 → 17) to the new Development DB; the legacy volume is untouched at 16.
- Known: the sweeper worker logs `relation "stock_count_submission_jobs" does not exist` until API-05C is applied (not applied, per instruction).
- Follow-up: rotate the Redis password that was exposed in chat (separate credential-rotation process).

## 0. Final scope (confirmed by the Product Owner, 17 Oct 2026)

- **Development (6 services, all required):** PostgreSQL 5435, RabbitMQ 5672/15672, Redis 6382, Elasticsearch 9200, Kibana 5601, pgAdmin 8081 — `docker/compose.dev.yml`. Kibana/pgAdmin stay in the `admin` profile, and `docker/.env.dev` sets `COMPOSE_PROFILES=admin`, so the standard `docker compose --env-file docker\.env.dev -f docker\compose.dev.yml up -d` starts all six (verified with `config --services`).
- **Integration Test (4 services):** PostgreSQL 55432, RabbitMQ 5673/15673, Redis 6383, Elasticsearch 9201 — `docker/compose.test.yml`.
- **Data restored: ONLY `retailpos_db`** (schema, data, sequences, constraints, indexes, EF history, users, roles, permissions, sales, stock, accounting).
- **NOT restored, NOT deleted, NOT altered:** `iranimart_ecommerce`, legacy `retailpos_*` fixture databases, qsmart-ai-agent databases, `qbo.*` RabbitMQ queues, the Elasticsearch `products` index. They stay in the legacy volumes; the earlier `iranimart_ecommerce` dump is kept only as a retained protective copy.
- API-05B / API-05C migrations are **not** applied. Nothing is staged or committed. Stopping a container never authorizes deleting its data.

## 1. Old → new service mapping

| Service | Legacy (running now) | Target | Notes |
|---|---|---|---|
| Dev PostgreSQL | container `postgres` (project `compose`, file `E:\Projects\Churchfield\AI Agent\Source\qsmart-ai-agent\Deploy\Compose\infra.yml`), volume `compose_pgdata`, `:5435` | `iranimart-dev` / `postgres` (`pgvector/pgvector:pg16`), volume `iranimart-dev_postgres_dev_data`, `127.0.0.1:5435` | Same user `admin`/password so the API needs no change. Data comes only from the restore |
| Test PostgreSQL | `retailpos-a2-pgtest-20261014` (project `retailpos-a2-pgtest`, volume `retailpos-a2-pgtest-data`), `127.0.0.1:55432` | `iranimart-test` / `postgres`, volume `iranimart-test_postgres_test_data`, `127.0.0.1:55432` | Same user/db as `RETAILPOS_TEST_POSTGRES`; fixture DBs are created on demand |
| RabbitMQ | container `rabbitmq` (project `compose`), vhosts `retailpos_dev`, `retailpos_test`, `/`; `:5672`/`:15672` | Dev `:5672`/`:15672` vhost `retailpos_dev`; Test `:5673`/`:15673` vhost `retailpos_test` | vhost names now match the legacy names and the test constants (the earlier `/retailpos-*` defaults were removed) |
| Redis | container `redis` (project `compose`), `--requirepass`, container port 6382 | Dev `:6382` (AOF + `--requirepass`); Test `:6383` (ephemeral, `--requirepass`) | Auth is no longer dropped; password via `DEV_REDIS_PASSWORD`/`TEST_REDIS_PASSWORD` |
| Elasticsearch | container `elasticsearch` 8.19.4 (project `compose`), `:9200` | Dev `:9200`; Test `:9201`, security on, HTTP only | Health check authenticated, waits for yellow |
| Kibana | container `kibana` (project `compose`), `:5601` | Dev `:5601`, `admin` profile — **required** (enabled by `COMPOSE_PROFILES=admin` in `.env.dev`); the `kibana-bootstrap` one-shot sets the `kibana_system` password first | Not in Test |
| pgAdmin | container `pgadmin` (project `compose`), `:8081` | Dev `:8081`, `admin` profile — **required** (same switch) | Legacy pgAdmin saved servers live in `compose_pgadmin-data` and are not migrated; register `postgres:5432` again |
| Never start | `retailpos-postgres` (project `pos-v2`, state *Created*, `:5435`, volume `pos-v2_postgres_data`) | — | Orphan; would collide on 5435 |

Config: `docker/compose.dev.yml`, `docker/compose.test.yml`, `docker/.env.dev`, `docker/.env.test` (gitignored; created locally from the existing user-secrets so credentials are unchanged; random values only for Kibana/pgAdmin/Test Redis/Test Elasticsearch). Templates: `docker/env.dev.example`, `docker/env.test.example`.

## 2. Backup evidence (`backups/local-cutover/`, gitignored)

| Artifact | Detail |
|---|---|
| `retailpos_db_20261017T101445Z.dump` | `pg_dump -Fc`, PG 16.13, 1,006,488 bytes, SHA-256 `E8D1F841…1F06A25` |
| `globals_20261017T101445Z.sql` | roles (`admin` only), no passwords |
| `iranimart_ecommerce_20261017T101445Z.dump` | other project's DB on the same legacy instance — retained protective copy, **never restored by this cutover** |
| `rabbitmq_definitions_20261017T101445Z.json` | vhosts/users/permissions (hashes — sensitive) |
| `MANIFEST_20261017T101445Z.md` | source, versions, validation results, restore procedure |

Verification: `pg_restore --list` OK; **restore rehearsal passed** into disposable DB `cutover_restore_verify_20261017t101445z` on the A2 instance (not a fixture-prefixed name, so the fixture sweeper never touches it): 179/179 table counts, 171/171 sequences, 56 migrations, 844 indexes, 472 FKs, 64 unique, 283 check, 8 triggers, md5 of key tables identical; 0 restore errors. Only differences: owner (`--no-owner`) and PostgreSQL's harmless re-deparse of `ANY(ARRAY[...])` expressions.

The pre-freeze dump is **not authoritative** for the final restore; a new dump must be taken after writers stop.

## 3. Inventory facts that drive the plan

- `retailpos_db`: 179 tables, 336 rows, only extension `plpgsql`, one role (`admin`, superuser). EF history has 56 migrations; **2 code migrations are not applied** (`Api05BMobileStockCountOperations`, `Api05CMobileStockTakeSubmissionJobs`). Do not apply them as part of the cutover.
- No active DB sessions, no API/worker process, no listener on the API ports at inventory time. Writers in code: `IntegrationOutboxHostedService` (only registered when durable delivery is on; `DurableDelivery:Enabled=false`), `MobileStockTakeSubmissionSweeperHostedService`/`…ConsumerHostedService` (`WorkerEnabled=true`), `SubscriptionRenewalReminderHostedService`, `HrEmployeeLocationTransferHostedService`, plus the API itself. No pg_cron; one Windows task `MaintenanceTasks` exists and is unrelated to the DB.
- RabbitMQ `retailpos_dev` / `retailpos_test`: 0 queues, 0 custom exchanges (nothing to preserve). `/` vhost has `qbo.sync.queue` and `qbo.e2etest.response` (1 message) — **another project**. Outbox/inbox tables empty.
- Redis keyspace empty (cache only). Elasticsearch: no `retailpos-*` index; only an unrelated `products` index (3 docs).
- Leftover fixture databases exist on the **Development** instance (`retailpos_acc_seq_*`, `retailpos_baseline_*` ×3, `retailpos_grn_complete_*`, `retailpos_payroll_adjustment_*`). They are not restored/removed by this plan; clean up only with separate approval.

## 4. Known blockers / decisions required from the Product Owner

1. **Shared legacy stack (approval gate).** All legacy Dev containers belong to a *different* Compose project (`qsmart-ai-agent`, network `compose_oms-net`/`compose_ai-agent-net`). All six Development services must replace them on the same ports, so stopping them takes that project's DB `iranimart_ecommerce`, RabbitMQ `/` vhost queues `qbo.*` (one message in `qbo.e2etest.response`), Elasticsearch `products`, Kibana and pgAdmin offline. Their data stays in the legacy volumes and is not migrated; there is no restart-free alternative because the ports collide. **The shared containers are not stopped until the Product Owner explicitly confirms the downtime and the impact on that project.**
2. **Port ownership.** The new Dev stack needs 5435/5672/15672/6382/9200/5601/8081, and Test needs 55432 (legacy A2) — all currently owned by legacy containers.
3. **Pending migrations** (API-05B/05C) remain unapplied; applying them needs separate approval.
4. **DurableDelivery** stays disabled: `RabbitMqDeliveryOptions.Validate()` requires `amqps://` and local brokers are plain AMQP.
5. **Kibana bootstrap** and Test Redis/Elasticsearch passwords are new; the Test user-secrets must be repointed (`docker/Set-TestUserSecrets.ps1`) during the cutover.
6. The orphan `retailpos-postgres` container should be removed only under a separate cleanup approval.

## 5. Cutover sequence (requires explicit approval — do not run before)

Run from the repo root. Stop at the first failure and go to §6.

1. **Freeze writers.** Close the API (`dotnet`/IIS Express/launchSettings), the frontend is irrelevant to DB writes. Confirm: `Get-NetTCPConnection -State Listen -LocalPort 5000,5001,7000,7001` empty and no `RetailPOS.API` process.
2. **Prove no writer remains.** In `postgres`: `select count(*) from pg_stat_activity where datname='retailpos_db' and pid<>pg_backend_pid()` = 0 and `xact_commit/tup_inserted/tup_updated/tup_deleted` from `pg_stat_database` unchanged over 60 s.
3. **Final backup** (same commands as the manifest, new timestamp): `pg_dump -Fc`, `pg_dumpall --globals-only --no-role-passwords`, RabbitMQ definitions, `snapshot.sql` → `source_snapshot_<final>.txt` (only `retailpos_db` is in scope; do not re-dump or touch other databases). Save test secrets (DPAPI-encrypted) for rollback:
   `(dotnet user-secrets list --project tests\RetailPOS.IntegrationTests\RetailPOS.IntegrationTests.csproj | Out-String) | ConvertTo-SecureString -AsPlainText -Force | ConvertFrom-SecureString | Set-Content backups\local-cutover\test-secrets.dpapi`
4. **Verify the final backup**: `pg_restore --list`, drop the old rehearsal DB (only the one named `cutover_restore_verify_*`), restore into a new `cutover_restore_verify_<final>` on A2 with `--no-owner --exit-on-error`, compare snapshots (only ownership may differ).
5. **Stop legacy containers (no `rm`, no `down`, no volume commands):** `docker stop kibana pgadmin elasticsearch redis rabbitmq postgres retailpos-a2-pgtest-20261014`. Record `docker ps -a` output.
6. **Start Development stack (all six services):** `docker compose --env-file docker\.env.dev -f docker\compose.dev.yml up -d` (`COMPOSE_PROFILES=admin` in `.env.dev` includes `kibana-bootstrap`, Kibana and pgAdmin); wait for `healthy` (`docker compose … ps`), then check `http://127.0.0.1:5601` and `http://127.0.0.1:8081`.
7. **Restore only `retailpos_db`** from the final dump into `iranimart-dev-postgres-1` (see the manifest; `admin` already exists as image superuser, so ownership is preserved). Do not restore any other database.
8. **Roles/extensions:** none required beyond `admin`/`plpgsql`; pgvector is available in the image if later migrations need it.
9. **Verify** snapshot diff empty, 56 migrations, `select count(*) from pg_stat_activity` sane. Do **not** run `dotnet ef database update` or seeding.
10. **Start Test stack:** `docker compose --env-file docker\.env.test -f docker\compose.test.yml up -d`; wait `healthy`.
11. **Repoint tests:** `powershell docker\Set-TestUserSecrets.ps1` (validate) then `-Apply`. Verify connectivity with bounded checks (`Test-NetConnection` on 5435, 55432, 5672, 5673, 6382, 6383, 9200, 9201).
12. **Run focused tests:** `dotnet test tests\RetailPOS.IntegrationTests --filter "Category=LiveInfrastructure|FullyQualifiedName~InfrastructureConfigurationTests|FullyQualifiedName~PostgresTestDatabaseLifecycleTests"` (expect all 56 green) and an API smoke in Development (migrate/seed flags stay `false`): login + read-only GETs (products, sales list).
13. **Readiness:** all **six** Development services and all **four** Test services up (health checks for PostgreSQL/RabbitMQ/Redis/Elasticsearch; HTTP 200/302 for Kibana and pgAdmin; authenticated PING/health/AMQP checks); restored counts match; smoke passes; no second write-capable environment running.
14. **Retain** legacy volumes (`compose_pgdata`, `compose_rabbitmq-data`, `compose_redis-data`, `compose_elasticsearch-data`, `compose_pgadmin-data`, `retailpos-a2-pgtest-data`, `pos-v2_postgres_data`), all containers (stopped) and all backups until a separate cleanup approval.

## 6. Rollback plan

Trigger: any failed step 6–12, or the PO rejects readiness.

1. Stop the API/workers. Stop only the new stacks: `docker compose --env-file docker\.env.dev -f docker\compose.dev.yml stop` and the same for test. **Never** `down -v`.
2. **Write reconciliation.** If the API ran against the new DB, take `pg_dump -Fc` of the new `retailpos_db`, compare against the final snapshot, and decide per row with the PO before reverting. Without post-cutover writes the legacy volume is authoritative.
3. Restart legacy in the original order, one project at a time and only when the new stacks are stopped: `docker start postgres rabbitmq redis elasticsearch kibana pgadmin`, then `docker start retailpos-a2-pgtest-20261014`. Original owners: project `compose` (`infra.yml`) and `retailpos-a2-pgtest`; original volumes untouched.
4. Connection strings need no rollback (same ports/credentials). Restore test user-secrets from `test-secrets.dpapi` (parse `key = value` lines; `dotnet user-secrets set`) so tests use 6382/5672/9200 again.
5. RabbitMQ: legacy persistent volume still holds all legacy queues/messages; `retailpos_*` vhosts had none, and the outbox tables were empty (check `integration_outbox_messages` in the restored DB before starting workers; unpublished rows must be re-published only by one environment).
6. Redis/Elasticsearch: legacy volumes untouched; Redis holds no POS keys, ES no POS indices; nothing to reconcile.
7. Restart sequence: PostgreSQL → RabbitMQ → Redis → Elasticsearch → API (which starts the hosted workers). Do not start the old and new write-capable environments simultaneously.
8. Re-run the smoke test; keep new volumes and the post-attempt dump for diagnosis.

## 7. Required approval

The Product Owner must reply with explicit written confirmation of **all** of the following before any step in §5 from step 1 onward runs (preparation up to this point is complete; nothing below has been executed):

1. **Downtime window** for local Development infrastructure (start/end time), during which no POS API/worker writes.
2. **Impact on the `qsmart-ai-agent` Compose project:** its containers `postgres`, `rabbitmq`, `redis`, `elasticsearch`, `kibana`, `pgadmin` will be **stopped (not removed)**, making `iranimart_ecommerce`, the `qbo.*` queues and the `products` index unavailable until rollback or until that project gets its own infrastructure. Its data is neither restored nor deleted.
3. **Stopping the legacy A2 test container** `retailpos-a2-pgtest-20261014` to free port 55432.
4. **The cutover sequence in §5** (restore of `retailpos_db` only; Kibana and pgAdmin started via the `admin` profile; Test stack started; test user-secrets repointed).
5. Confirmation that API-05B/05C migrations stay unapplied.

Suggested approval text: *"I approve the IraniMart POS v2 Docker cutover per DOCKER_DATA_PRESERVING_CUTOVER.md §5 in window <start–end>, including stopping the shared legacy containers (postgres, rabbitmq, redis, elasticsearch, kibana, pgadmin) and retailpos-a2-pgtest-20261014 without deleting any container, volume or backup, and I accept the downtime impact on the qsmart-ai-agent project."*

Destructive cleanup (container/volume removal, fixture-database cleanup, orphan `retailpos-postgres`) needs a separate approval; stopping a container never authorizes deleting its data.

## 8. Post-cutover: API-05B / API-05C migrations (validated and APPLIED to Development on 17 Oct 2026)

**Executed** with the exact command below after Product Owner approval. Result: 58 applied migrations; `stock_count_operation_receipts` and `stock_count_submission_jobs` exist with all PK/FK/check constraints and indexes; the 5 new `stock_counts` columns are present; 0 invalid indexes, 0 unvalidated constraints; every pre-existing row count and sequence value is unchanged (diff vs the pre-migration snapshot is only the new objects). Starting the API afterwards ran the submission sweeper (12 polls of `stock_count_submission_jobs`) with 0 errors and no missing-table failures; the eight `api/mobile/v1/stock-take/...` routes are in the OpenAPI document and return 401 anonymously. Pre-migration backup: `retailpos_db_PREMIG_20261017T111103Z.dump` (SHA-256 `837FC8B9…223621`).

**Pending against Development `retailpos_db` before execution** (56 applied, last `20261013170349_Phase7DExitCompletion`; 58 in code):

| Order | Migration | Change |
|---|---|---|
| 57 | `20261015204044_Api05BMobileStockCountOperations` | `stock_counts` +5 columns (`baseline_consistency_status` NOT NULL default `'Unverified'`, `baseline_inventory_read_started/completed_at_utc` nullable, `count_revision` NOT NULL default 0, `movement_reconciliation_status` NOT NULL default `'NotStarted'`); new table `stock_count_operation_receipts` (PK, FK → `stock_counts` cascade, 3 indexes incl. 2 unique) |
| 58 | `20261016124940_Api05CMobileStockTakeSubmissionJobs` | new table `stock_count_submission_jobs` (PK, FK → `stock_counts` restrict, 2 check constraints, 5 indexes incl. unique and a partial unique active-session index) |

**Validation evidence (17 Oct 2026):**
- `dotnet ef migrations has-pending-model-changes`: none; EF model matches the last snapshot. Order is strictly chronological; 57 depends only on `stock_counts`, 58 only on `stock_counts`.
- Generated SQL: `backups/local-cutover/migrate_api05bc_20261017T104348Z.sql` (90 lines, single transaction, 5 `ALTER TABLE ADD`, 2 `CREATE TABLE`, 8 `CREATE INDEX`, 2 history inserts). **No DROP/DELETE/TRUNCATE/UPDATE of existing data**; `stock_counts` has 0 rows so the NOT NULL defaults rewrite nothing.
- Rehearsal on the isolated Test PostgreSQL (disposable copies of the final backup, since dropped): applying the SQL script and `dotnet ef database update` produced **identical schemas**; the only differences from the source were +2 tables, +2 PK, +2 FK, +2 check, +10 indexes, +1 sequence and 2 history rows (56 → 58). All pre-existing row counts and sequence values unchanged.
- Tests: `MigrationChainValidationTests` (fresh chain to `Api05C`, idempotent re-run), Mobile Stock Take and stock-count suites, configuration and live-infrastructure tests — 108/108 passed (assertion for the last migration id updated from `Phase7D` to `Api05C`).
- Backups available: `retailpos_db_FINAL_20261017T104348Z.dump` and `retailpos_db_PREMIG_<ts>.dump` (taken immediately before approval; includes the one post-cutover audit row).
- Shutdown required: no API/worker process may run (none is running); the sweeper worker currently fails without `stock_count_submission_jobs`.

**Exact execution (after approval, from the repo root):**

```powershell
$ts = (Get-Content backups\local-cutover\.final_ts).Trim()
docker cp "backups\local-cutover\migrate_api05bc_$ts.sql" iranimart-dev-postgres-1:/tmp/migrate.sql
docker exec iranimart-dev-postgres-1 psql -U admin -d retailpos_db -v ON_ERROR_STOP=1 -f /tmp/migrate.sql
docker exec iranimart-dev-postgres-1 rm -f /tmp/migrate.sql
```

Rollback if it fails: the script is one transaction (nothing is applied on error). After success, a revert would restore `retailpos_db_PREMIG_<ts>.dump` into an empty database (no business data is written by the migration).

## 9. Redis credential rotation (Development ROTATED 17 Oct 2026; Test not rotated)

The legacy Redis password was exposed in a chat transcript and had been reused for the new Development Redis. **Development was rotated** with `docker/Rotate-RedisPassword.ps1 -Target Dev -Apply`: the new Dev password is verified (anonymous → NOAUTH, old exposed password → WRONGPASS, API secret → PONG) and stored only in `docker/.env.dev` and the API user-secret `ConnectionStrings:Redis` (options preserved). Restart any running API to pick it up. The Test Redis uses a different, never-exposed password and was left unchanged (rotate with `-Target Test -Apply` if desired). A first attempt hit a script bug (docker stderr treated as an error); the script was fixed and re-run, and the password in use is the verified one.

- References (names only): `docker/.env.dev` → `DEV_REDIS_PASSWORD`; `docker/.env.test` → `TEST_REDIS_PASSWORD`; API user-secrets → `ConnectionStrings:Redis`; test user-secrets → `RETAILPOS_TEST_REDIS`. The two stores of each stack were verified to hold the same value.
- Tool: `docker/Rotate-RedisPassword.ps1 -Target Dev|Test|Both [-Apply]` (dry-run by default). It generates the new password in memory, recreates **only** the `redis` service with it, verifies NOAUTH for anonymous access and AUTH success with the new password, and only then updates the env file and the user-secret. On verification failure it restores the old password on the container and persists nothing.
- Impact: Development Redis container is recreated (seconds of downtime, volume `iranimart-dev_redis_dev_data` kept, 0 cache keys today); running API processes must be restarted to pick up the new secret; Test Redis (ephemeral) is recreated if `-Target Both|Test`. No other service is restarted. Recommended: rotate Development only (`-Target Dev`); the Test password was never exposed.
- The retired legacy `redis` container (stopped) still carries the old password in its command line; it belongs to the retired stack and is not modified.

