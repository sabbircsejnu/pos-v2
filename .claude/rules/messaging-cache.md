---
paths:
  - "src/**/Integration/**"
  - "src/**/*Outbox*"
  - "src/**/*Inbox*"
  - "src/**/*RabbitMq*"
  - "src/**/*Publisher*.cs"
  - "src/**/*Consumer*.cs"
  - "src/**/*Cache*.cs"
  - "src/**/*Elastic*"
  - "src/RetailPOS.API/Program.cs"
---

# Messaging, cache and search

## Messaging (approved: `Docs/ARCHITECTURE/ADR-ARCH-002_RABBITMQ_CLIENT_CUSTOM_OUTBOX_INBOX.md`)
- **Implementation:** `RabbitMQ.Client` 7.2 with a custom PostgreSQL transactional outbox/inbox in `src/RetailPOS.Infrastructure/Integration` (`IntegrationOutbox`; `OutboxDispatcher` with bounded exponential backoff → `DeadLetter`; `IntegrationInbox`; `RabbitMqConfirmedPublisher` with publisher confirms; `DurableDeliveryOptions`; `IntegrationEnvelope`). Preserve it and extend these components; don't build parallel messaging infrastructure.
- MassTransit (and any other messaging framework) is **deferred indefinitely**: do not add the package or plan a migration. ADR-ARCH-001 is superseded history.
- `DurableDelivery:Enabled` is false in every environment. PostgreSQL job tables and workers/sweepers complete business work and stay authoritative; a message is never the only record of required work.
- Requirements (ADR-002 §3):
  - Transport types stay in Infrastructure, behind application-owned ports.
  - Outbox rows are written in the handler's DbContext and transaction; nothing is published before commit; no broker calls inside transactions.
  - At-least-once delivery: consumers are idempotent (inbox plus a business guard) and tolerate duplicates and out-of-order messages.
  - Envelopes carry `BusinessId`, correlation, causation, UTC timestamp and schema version; consumers validate business scope.
  - Retries are bounded; validation and authorization failures are not retried; dead-letter reprocessing is audited.
  - Structured logs carry correlation IDs.
  - Outbox/inbox schema changes need an approved migration.
- Always: no secrets or unnecessary PII in messages; never consume Development queues from tests (Test vhost `retailpos_test`, port 5673).

## Redis (implemented)
- `IDistributedCache` via `RedisCacheRegistration`, `PosCacheService`, `PosCartPriceCache`. Keys are namespaced and business-scoped (Dev prefix `pos:`, tests `retailpos-test:<guid>:`), with TTLs and explicit invalidation.
- Cache is non-authoritative: final stock and price checks at checkout read the live DB row (see `IPosCacheService` notes). Cache failure must not break correctness. Never `FLUSHALL`/`FLUSHDB`.

## Elasticsearch (not implemented)
- Only configuration keys/validation exist (`Elasticsearch:Enabled`). Adding a client, indices or projections is new infrastructure and needs design approval. If approved, it is an eventually consistent, tenant-filtered read model rebuilt from PostgreSQL, never used to authorize sales.
