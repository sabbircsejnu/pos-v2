# A2 Disposable PostgreSQL Test Environment

**Purpose:** Provide an isolated PostgreSQL 16 target for future A2 integration tests.  
**Safety boundary:** Do not point tests at the Development `retailpos_db`, production/shared PostgreSQL, or a Docker volume whose provenance is unknown. This setup must not alter the existing ERP API or PostgreSQL resources.

## Provisioned-resource specification

| Resource | Dedicated test value |
|---|---|
| Compose project | `retailpos-a2-pgtest` |
| Compose file | `docker-compose.a2-postgres.yml` |
| Container | `retailpos-a2-pgtest-20261014` |
| Image / server major | `pgvector/pgvector:pg16` / PostgreSQL 16 |
| Host endpoint | `127.0.0.1:55432` → container port `5432` |
| Docker network | `retailpos-a2-pgtest-net`, dedicated bridge (test container only) |
| Docker volume | `retailpos-a2-pgtest-data`, dedicated named local volume |
| Bootstrap database | `a2_test_guard` |
| Bootstrap user | `a2_test_runner` |

The `pgvector/pgvector:pg16` image matches the PostgreSQL 16 major in the ERP Compose configuration and the already available pgvector PostgreSQL 16 image. The service binds only to loopback, uses a dedicated bridge network not shared with other Compose projects and an explicitly named volume, and has no mounts other than its dedicated volume. It does not reference `retailpos_db` or any existing Development/production volume.

The test lifecycle helper creates additional uniquely named, allowlisted databases, marks each database with a generated ownership comment, and drops only the exact test database it created. The helper uses the server named by `RETAILPOS_TEST_POSTGRES`, connects to its `postgres` maintenance database, and is not allowed to use `appsettings.json` fallback connections for A2 tests.

## Local secret configuration

The Compose file requires local-only values in the root `.env.a2-postgres` file. This filename is explicitly Git-ignored. Generate a unique random password locally; do not put credentials in source, command history, screenshots, or issue comments. Example structure (replace the placeholder with a locally generated secret):

```dotenv
A2_PG_DATABASE=a2_test_guard
A2_PG_USER=a2_test_runner
A2_PG_PASSWORD=REPLACE_WITH_A_UNIQUE_RANDOM_SECRET
```

Before provisioning, verify the exact container, network, and volume names are unused and that TCP port 55432 is free:

```powershell
docker ps --all --filter "name=^/retailpos-a2-pgtest-20261014$"
docker network ls --filter "name=^retailpos-a2-pgtest-net$"
docker volume ls --filter "name=^retailpos-a2-pgtest-data$"
Get-NetTCPConnection -State Listen -LocalPort 55432 -ErrorAction SilentlyContinue
```

If any resource exists or the port has a listener, stop and choose a new unique container/project/network/volume/port consistently in the Compose file and these instructions. Do not reuse, remove, inspect the contents of, or attach an unknown volume.

Provision only this dedicated Compose project:

```powershell
docker compose --project-name retailpos-a2-pgtest --file docker-compose.a2-postgres.yml --env-file .env.a2-postgres up --detach
docker compose --project-name retailpos-a2-pgtest --file docker-compose.a2-postgres.yml --env-file .env.a2-postgres ps
```

Inspect the test container before using it. Confirm the exact name/image, `127.0.0.1:55432` binding, only the `retailpos-a2-pgtest-data` mount at PostgreSQL's data path, membership only in `retailpos-a2-pgtest-net`, and the Compose project label. Confirm the healthcheck is healthy.

## Configure `RETAILPOS_TEST_POSTGRES`

Set the test variable in the same PowerShell session that will run `dotnet test`. Do not set a Development `DefaultConnection` or rely on the test helper's appsettings fallback.

```powershell
$local = Get-Content .env.a2-postgres | ConvertFrom-StringData
$env:RETAILPOS_TEST_POSTGRES = "Host=127.0.0.1;Port=55432;Database=$($local.A2_PG_DATABASE);Username=$($local.A2_PG_USER);Password=$($local.A2_PG_PASSWORD);Pooling=false;Timeout=5"
```

For a non-secret target check, parse the connection string without printing its password:

```powershell
$target = $env:RETAILPOS_TEST_POSTGRES
if ($target -notmatch '(?i)(?:^|;)Host=127\.0\.0\.1(?:;|$)' -or
    $target -notmatch '(?i)(?:^|;)Port=55432(?:;|$)' -or
    $target -notmatch '(?i)(?:^|;)Database=a2_test_guard(?:;|$)' -or
    $target -notmatch '(?i)(?:^|;)Username=a2_test_runner(?:;|$)') {
    throw "RETAILPOS_TEST_POSTGRES does not target the dedicated A2 PostgreSQL instance."
}
"Target verified: 127.0.0.1:55432/a2_test_guard as a2_test_runner"
```

The tests must also verify from a live connection that `current_database()` is the generated test database or the maintenance database used solely for test database creation, and that the PostgreSQL server reports major version 16. Do not execute migrations, seeders, `DROP DATABASE`, or A2 tests until the resource inspection and target check pass.

## Connection check and test policy

The safe connection check is read-only: connect using the bootstrap database and execute only `SELECT current_database(), current_user, version()`. The expected bootstrap database is `a2_test_guard`; the server major must be 16. Verify the endpoint and container identity before any test database is created.

When approved, run only the A2 PostgreSQL test class with the explicit environment variable set. The A2 tests should use the existing `PostgresTestDatabase.RunAsync` lifecycle and a dedicated prefix; they must not use `RetailPosApiFactory`'s InMemory provider or configure a fallback to Development.

Do not use `docker compose down --volumes`, `docker system prune`, `docker volume prune`, or broad cleanup commands. This task's provisioning does not run tests or remove resources. If later cleanup is approved, inspect the exact Compose project and only remove its exact container/network/volume after confirming their labels and names; never act on other Compose projects.

## Provisioning and verification record

| Check | Result |
|---|---|
| Existing ERP Compose configuration | PostgreSQL `postgres:16`; Development database volume is `pos-v2_postgres_data`; existing Compose project `pos-v2`. |
| Existing active PostgreSQL service | `pgvector/pgvector:pg16`, host port 5435, volume `compose_pgdata`, network `compose_oms-net`, project `compose` (read-only inspection). |
| Candidate host port 55432 | Available at preflight. |
| Dedicated test files/resources | Provisioned; container is healthy and has the expected project label, loopback-only port, dedicated network, and dedicated volume. |
| Live read-only connection | **Passed.** Npgsql connected through `127.0.0.1:55432` using the secret local configuration. Verified `a2_test_guard`, `a2_test_runner`, server version number `160013` (PostgreSQL 16.13), and data directory `/var/lib/postgresql/data/pgdata`. The temporary verifier was removed. |
| Host TCP endpoint | **Reachable** at `127.0.0.1:55432`; container reports healthy. |
| `RETAILPOS_TEST_POSTGRES` | Verified during the connection check; current shell variable was cleared after the check. Set it explicitly in the shell that runs approved integration tests; do not fall back to the Development connection string. |
| A2 PostgreSQL tests | **Not run** (0 executed). No migrations, seeders, generated test databases, or cleanup commands were run. |
