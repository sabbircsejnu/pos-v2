# IraniMart POS v2 — Local Docker Compose

Two separate Docker Compose projects:

- `compose.dev.yml` (project `iranimart-dev`): six **required** Development services — PostgreSQL, RabbitMQ, Redis, Elasticsearch, Kibana and pgAdmin. Kibana and pgAdmin (and the one-shot `kibana-bootstrap`) are in the `admin` profile, which `.env.dev` enables through `COMPOSE_PROFILES=admin`, so the normal startup command starts all six.
- `compose.test.yml` (project `iranimart-test`): four isolated integration-test services — PostgreSQL, RabbitMQ, Redis, Elasticsearch (no Kibana/pgAdmin).

| Service | Development host port | Test host port |
|---|---|---|
| PostgreSQL | 5435 | 55432 |
| RabbitMQ / Management | 5672 / 15672 | 5673 / 15673 |
| Redis | 6382 | 6383 |
| Elasticsearch | 9200 | 9201 |
| Kibana | 5601 | — |
| pgAdmin | 8081 | — |

**Data scope:** only `retailpos_db` is restored into the new Development PostgreSQL. `iranimart_ecommerce`, the legacy `retailpos_*` fixture databases, qsmart-ai-agent data, `qbo.*` queues and the Elasticsearch `products` index are NOT migrated and must never be deleted or altered in their legacy volumes. See `Docs/Development/DOCKER_DATA_PRESERVING_CUTOVER.md`.

**CAUTION: THESE FILES DEFINE NEW PROJECTS AND NEW VOLUMES. THEY DO NOT ADOPT YOUR EXISTING CONTAINERS OR PRESERVE THEIR DATABASE CONTENT AUTOMATICALLY.** Your current Docker Desktop already runs these ports, including the A2 PostgreSQL test container on `55432`. Running these new stacks alongside the existing services will produce port conflicts. Do not stop the shared legacy containers (they belong to the `qsmart-ai-agent` Compose project) without explicit Product Owner confirmation of the downtime and impact on that project. Stopping is not permission to delete data. Do not remove existing containers or volumes to force the new stacks to start. For Development, first plan a controlled migration or continue using the existing running stack and only use these Compose files as future templates. For A2, consider retaining your existing `docker-compose.a2-postgres.yml` unchanged rather than moving it.

## Configure

1. Copy `env.dev.example` to `.env.dev`; `env.test.example` to `.env.test`.
2. Replace all `CHANGE_ME` placeholders with strong, distinct environment-specific credentials (for a data-preserving cutover reuse the existing user-secrets credentials). Never check `.env.dev` or `.env.test` into Git (they are gitignored). For the Test stack keep the PostgreSQL user/database identical to `RETAILPOS_TEST_POSTGRES`; after cutover run `Set-TestUserSecrets.ps1 -Apply` to repoint the test user-secrets.
3. Check exact existing host-port mappings using `docker ps --format "table {{.Names}}\t{{.Ports}}"` and `docker port rabbitmq`. Edit `.env` port variables if needed.
4. Confirm existing data volume names/mounts (`docker inspect <container>`) before adopting any existing data; never guess volume names. For previously initialized PostgreSQL or RabbitMQ data volumes, changing the environment credentials does not reset database/broker passwords.
5. Kibana's `kibana_system` password is set by the one-shot `kibana-bootstrap` service before Kibana starts; never use the `elastic` superuser password. pgAdmin starts with `DEV_PGADMIN_EMAIL`/`DEV_PGADMIN_PASSWORD` and an empty server list: register the Development database as host `postgres`, port `5432` (Docker network) or `host.docker.internal:5435`. Legacy pgAdmin saved servers are not migrated.

## Validate config (no containers started)

```powershell
 docker compose --env-file .env.dev -f compose.dev.yml config --quiet   # includes the admin profile via COMPOSE_PROFILES
 docker compose --env-file .env.test -f compose.test.yml config --quiet
```

## Start (ONLY AFTER Product Owner cutover approval, port release and the final restore plan)

```powershell
 docker compose --env-file .env.dev -f compose.dev.yml up -d     # all six Development services (admin profile enabled by .env.dev)
 docker compose --env-file .env.test -f compose.test.yml up -d   # four Test services
```

Check health (Kibana and pgAdmin have no Compose health check; verify `http://127.0.0.1:5601` and `http://127.0.0.1:8081`):

```powershell
 docker compose --env-file .env.dev -f compose.dev.yml ps
 docker compose --env-file .env.test -f compose.test.yml ps
```

To stop *these new stacks only*: `docker compose --env-file .env.test -f compose.test.yml stop` (analogous for dev). **Never run `down -v` on any project containing important data.**

## Application and Copilot configuration

Configure real .NET service-registration keys/connection strings from the existing codebase, not guessed names. When running ASP.NET Core/tests on the Windows host, use `127.0.0.1` plus the corresponding HOST port. Inside the Compose network, use `postgres:5432`, `rabbitmq:5672`, `redis:6379`, and `http://elasticsearch:9200` for the matching stack. Test fixtures must enforce `55432` and isolated test-only resources, not Development's `5435`.

Test RabbitMQ and Elasticsearch are separate services, not just vhosts/indices. Redis Test is non-persistent. PostgreSQL Test and RabbitMQ Test have separate dedicated volumes; fixtures may delete only their own databases/queues, never these volumes or unrelated resources. No staging or production connection is provided by these local stacks.

**Security:** Ports are loopback-bound; Elasticsearch HTTP TLS is disabled only for this *local* Compose example while auth stays enabled. Do not reuse this TLS setting for production or expose these ports publicly. Redis (Dev and Test) requires a password (`DEV_REDIS_PASSWORD` / `TEST_REDIS_PASSWORD`).
