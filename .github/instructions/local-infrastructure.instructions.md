---
applyTo: "**/appsettings*.json,**/*IntegrationTests*/**,**/*Tests*/**,**/docker-compose*.yml,**/docker-compose*.yaml,**/compose.*.yml,**/*.env.example,**/env.*.example,**/*.csproj"
---

# IraniMart POS v2 — Local Infrastructure Instructions

## Purpose

Reuse the existing Docker infrastructure for local development and automated integration tests.

Do not create duplicate infrastructure services when a suitable existing service is available.

This file defines infrastructure discovery, configuration, isolation and safety rules. It does not replace the actual application configuration or integration-test fixtures.

## 1. Active Local Infrastructure (standardized Compose stacks)

Since the 17 Oct 2026 cutover the only active local infrastructure is the two standardized Compose stacks. The old stacks (Compose project `compose` of the separate qsmart-ai-agent project, and `retailpos-a2-pgtest` / `docker-compose.a2-postgres.yml`) are **retired and stopped**; their containers, volumes and backups are retained for rollback and must not be started, removed or referenced for new work.

| Service | Development (`iranimart-dev`) | Integration Tests (`iranimart-test`) |
|---|---|---|
| PostgreSQL | 127.0.0.1:5435 (persistent `retailpos_db`) | 127.0.0.1:55432 (disposable fixture DBs) |
| RabbitMQ AMQP (plain, no local TLS) | 127.0.0.1:5672, vhost `retailpos_dev` | 127.0.0.1:5673, vhost `retailpos_test` |
| RabbitMQ Management | 127.0.0.1:15672 | 127.0.0.1:15673 |
| Redis (password required) | 127.0.0.1:6382 | 127.0.0.1:6383 (ephemeral) |
| Elasticsearch (basic auth, HTTP) | http://127.0.0.1:9200 | http://127.0.0.1:9201 |
| Kibana | 127.0.0.1:5601 | not included |
| pgAdmin | 127.0.0.1:8081 | not included |

These are known local configuration references, not permanent guarantees of service health. Verify connectivity when required by the current task.

## 1a. Standardized Compose configuration

- `docker/compose.dev.yml` (project `iranimart-dev`, volumes `*_dev_data`; six required services — Kibana and pgAdmin via the `admin` profile, always enabled by `COMPOSE_PROFILES=admin`) and `docker/compose.test.yml` (project `iranimart-test`, volumes `*_test_data`; four services), with gitignored `docker/.env.dev` / `docker/.env.test` (templates `docker/env.dev.example`, `docker/env.test.example`).
- Only `retailpos_db` was migrated; unrelated legacy resources (`iranimart_ecommerce`, `retailpos_*` fixture DBs, `qbo.*` queues, Elasticsearch `products`) were never restored and must not be deleted or altered in their legacy volumes.
- Never run `docker compose down -v` or `docker system prune --volumes`; never delete legacy containers, volumes or backups without separate approval. Stopping or restarting a stack is not permission to delete data.
- Validate configuration only with `docker compose --env-file <env> -f <file> config --quiet` (never print resolved output).
- Test user-secrets (`RETAILPOS_TEST_*`) point at the Test stack; maintain them with `docker/Set-TestUserSecrets.ps1`. Rotate Redis passwords only with `docker/Rotate-RedisPassword.ps1` (dry-run by default).
- Backups live in the gitignored `backups/` folder; never commit dumps.

## 2. PostgreSQL Development

Development PostgreSQL:

- Host: 127.0.0.1
- Port: 5435
- Persistent application database: retailpos_db

Rules:

- Reuse the existing PostgreSQL development instance.
- Follow the current ASP.NET Core configuration conventions.
- Never use this instance for destructive integration tests.
- Do not drop, recreate, truncate or reseed persistent databases.
- Do not automatically apply new migrations without explicit approval.
- Never assume a development database is disposable.

## 3. Dedicated PostgreSQL Integration Tests

Dedicated Test stack (Compose project `iranimart-test`, `docker/compose.test.yml`):

- Container: iranimart-test-postgres-1 (replaces the retired A2 container `retailpos-a2-pgtest-20261014`, which is stopped and retained)
- Host: 127.0.0.1
- Port: 55432
- Internal port: 5432

This PostgreSQL instance passed readiness, TCP connectivity and the focused test run after the 17 Oct 2026 cutover.

Rules:

- Use the existing integration-test fixture and `RETAILPOS_TEST_POSTGRES` configuration.
- Use only dedicated disposable test databases approved by the existing fixture.
- Preserve test-database ownership and cleanup safeguards.
- Never redirect test execution to Development port 5435.
- Never delete the PostgreSQL Docker volume.
- Avoid creating another PostgreSQL container.
- Use bounded connectivity checks.
- Do not repeatedly investigate Docker after connectivity has been established.

The container being available does not guarantee successful authentication. Verify the configured credentials through existing secure configuration when needed.

## 4. RabbitMQ

Containers: `iranimart-dev-rabbitmq-1` (Development) and `iranimart-test-rabbitmq-1` (Tests), image rabbitmq:3.13-management. Management: http://127.0.0.1:15672 (Dev), http://127.0.0.1:15673 (Test). The old shared `rabbitmq` container is retired and stopped.

AMQP host ports: 5672 (Development, vhost `retailpos_dev`) and 5673 (Tests, vhost `retailpos_test`); plain AMQP only, 5671/AMQPS is not published. Each broker is a separate physical instance with its own user; the application does not use the default `/` vhost. Credentials live in user-secrets / `docker/.env.*`, never in committed files.

Rules:

- Reuse the standardized RabbitMQ brokers.
- Reuse existing messaging abstractions and Transactional Outbox infrastructure.
- Preserve existing exchanges, queues and messages.
- Isolate Development and Integration Test messaging using separate virtual hosts and restricted credentials where supported.
- Never attach test consumers to Development queues.
- Never delete unrelated exchanges or queues.
- Never hardcode broker credentials.
- Do not enable DurableDelivery globally without verifying existing application behaviour.
- Validate actual broker delivery for RabbitMQ-dependent acceptance tests.
- Do not treat mocked consumer tests as live-broker acceptance.
- Do not treat local plain AMQP validation as production AMQPS/TLS approval.

## 5. Redis

Redis services: `iranimart-dev-redis-1` (127.0.0.1:6382, AOF persistence) and `iranimart-test-redis-1` (127.0.0.1:6383, ephemeral). Both require a password. The old `redis` container is retired and stopped.

Rules:

- Reuse the standardized Redis containers (passwords supplied through user-secrets / `docker/.env.*`); rotate only with `docker/Rotate-RedisPassword.ps1`.
- Development keys use the `PosCache:InstanceName` namespace (`pos:`); tests use `retailpos-test:<guid>:`.
- Follow current application connection configuration.
- Verify authentication and connectivity when needed.
- Use isolated test key namespaces or another proven safe isolation mechanism.
- Never execute FLUSHALL or FLUSHDB against shared Development infrastructure.
- Never delete existing application cache data as part of routine test setup.

## 6. Elasticsearch

Elasticsearch services: `iranimart-dev-elasticsearch-1` (http://127.0.0.1:9200) and `iranimart-test-elasticsearch-1` (http://127.0.0.1:9201), version 8.19.4, HTTP with basic authentication. The old `elasticsearch` container is retired and stopped.

Rules:

- Reuse the standardized Elasticsearch instances (credentials in user-secrets / `docker/.env.*`). The application has no Elasticsearch client yet; `Elasticsearch:*` keys are validated only when `Elasticsearch:Enabled` is true.
- Development index prefix `retailpos-dev`; tests use `retailpos-test-*` indices they create and delete themselves.
- Respect existing authentication and TLS settings.
- Use dedicated test indices or namespaces.
- Never delete or recreate Development indices during integration tests.
- Do not disable Elasticsearch security for convenience.
- Avoid unnecessary index rebuilds during unrelated API development.

Kibana is an administration UI and not an application search endpoint.

## 7. Environment Configuration

Prefer existing project conventions:

- ASP.NET Core appsettings configuration
- Environment-specific appsettings files
- Environment variables
- .NET User Secrets for local credentials
- Existing integration-test fixture configuration
- Existing Docker Compose environment files

Do not introduce another configuration framework.

Do not commit credentials or secret-bearing environment files.

Ownership: `appsettings.json` holds shared non-secret defaults only; `appsettings.Development.json` holds the local endpoints; `appsettings.Staging.json` / `appsettings.Production.json` hold non-secret policy flags and must receive endpoints and credentials from the deployment environment. `InfrastructureConfigurationValidator` fails Staging/Production startup when settings are missing or point at local Development endpoints. Integration tests read `RETAILPOS_TEST_*` settings from environment variables or the test project's user-secrets (see LOCAL_INFRASTRUCTURE.md); the test assembly refuses PostgreSQL port 5435.

Use environment-specific configuration rather than hardcoded hostnames in application services.

Docker container-to-container networking may require Docker service names rather than 127.0.0.1. Determine the actual execution environment before choosing a hostname.

## 8. Integration Test Safety

Before running infrastructure-dependent tests:

1. Identify the specific external services required.
2. Resolve the existing configured endpoint.
3. Verify connectivity using a bounded check.
4. Verify the target environment is isolated and disposable where required.
5. Run focused tests.
6. Report actual results.

Do not restart or recreate infrastructure simply because a test failed.

Investigate the actual failure first.

Tests must not:

- Modify persistent Development databases.
- Consume Development RabbitMQ messages.
- Flush shared Redis data.
- Delete Development Elasticsearch indices.
- Modify Production infrastructure.

Test cleanup must affect only verified test-owned resources.

## 9. Copilot Agent Efficiency

For future backend development tasks:

- Consult this file before infrastructure changes.
- Reuse verified infrastructure rather than rediscovering it repeatedly.
- Inspect only relevant configuration and test fixtures.
- Avoid unnecessary repository-wide searches.
- Avoid repeated Docker troubleshooting.
- Use short, timeout-bounded diagnostic commands.
- Continue independent implementation work when an unrelated optional service is unavailable.
- Run focused tests during implementation.
- Run broader regression checks at appropriate acceptance gates.
- Do not claim tests passed without actual execution evidence.

Do not create, recreate, stop or delete existing infrastructure resources without explicit approval when doing so could disrupt other work or persistent data.

## 10. Git and Change Protection

The repository may contain unrelated uncommitted work.

Never run destructive Git operations.

Do not reset, clean, overwrite, stage or commit changes without explicit approval.

Preserve existing infrastructure configuration and avoid unrelated refactoring.

## 11. Infrastructure Readiness Reporting

When infrastructure validation is relevant, report only:

- Service
- Configured endpoint
- Connectivity result
- Authentication result, if tested
- Isolation verification
- Actual blocker, if any

Avoid long repetitive environment-discovery reports.

The objective is reliable feature implementation with existing infrastructure, not repeated Docker setup.