# ADR-ARCH-002 — RabbitMQ.Client with the custom PostgreSQL outbox/inbox

| Field | Value |
|---|---|
| Status | **Accepted** (2026-11-04). Supersedes `ADR-ARCH-001_MASSTRANSIT_RABBITMQ_MESSAGING.md`. |
| Decision owner | Product/technical owner (user-approved) |
| Scope | Asynchronous integration messaging only |
| Related | `CLEAN_ARCHITECTURE_MEDIATR_CQRS_MIGRATION_PLAN.md` (layering, §7 MediatR, §10 transactions); `Docs/Development/LOCAL_INFRASTRUCTURE.md`; `.github/instructions/local-infrastructure.instructions.md` |

## 1. Context
- ADR-ARCH-001 chose MassTransit. It is **not adopted for now**, because of licensing and long-term dependency considerations. No MassTransit code or package was ever added.
- The current implementation is raw `RabbitMQ.Client` 7.2 with a custom PostgreSQL outbox/inbox in `src/RetailPOS.Infrastructure/Integration` (`integration_outbox_messages`, `integration_inbox_messages`, `IntegrationOutbox`, `OutboxDispatcher`, `IntegrationInbox`, `RabbitMqConfirmedPublisher`, `RabbitMqInboxDelivery`, `DurableDeliveryOptions`, `IntegrationEnvelope`).
- `DurableDelivery:Enabled` is `false` in every environment. PostgreSQL job tables and workers (for example `stock_count_adjustment_jobs` and `stock_count_submission_jobs`, plus the financial posting workers) complete business work today.

## 2. Decision
1. **`RabbitMQ.Client` remains the messaging client.** The custom PostgreSQL transactional outbox/inbox is **preserved** and is the approved implementation. It is not legacy.
2. **MediatR + CQRS stays** for in-process application operations. RabbitMQ is used only for asynchronous integration.
3. **The modular monolith and Clean Architecture stay unchanged.**
4. **MassTransit migration is deferred indefinitely.** It is not a scheduled task. Adopting MassTransit or any other messaging framework needs a new ADR and explicit approval.
5. **PostgreSQL job workers and existing business workflows are preserved.** No messaging, job, projection, audit or business data is discarded.

## 3. Engineering requirements (binding)
### 3.1 Replaceable transport
- Application and Domain depend only on application-owned ports. They never reference `RabbitMQ.Client` or other transport types.
- Infrastructure owns the outbox, dispatcher, publisher, inbox and topology. API composes them in `Program.cs`.
- The transport must be replaceable later without changing business logic.
- Contracts are explicitly versioned with stable wire names.

### 3.2 Transactional outbox and inbox
- The command handler owns the business transaction (migration plan §10).
- Outbox rows are written through the same scoped DbContext and transaction as the business change. Nothing is published before commit.
- No distributed transactions, and no broker calls inside database transactions.
- PostgreSQL is the source of truth. Durable job tables and their sweepers stay authoritative. A message triggers work but is never the only record of required work.

### 3.3 Idempotency and ordering
- Delivery is at-least-once.
- Consumers are idempotent through the inbox and a business-level guard (unique effect key, job ID or source version).
- Consumers tolerate duplicates and out-of-order delivery.
- API idempotency keys are independent of messaging.

### 3.4 Tenant isolation
- The envelope carries `BusinessId`, a correlation ID, a causation ID where relevant, an occurred-at UTC timestamp and a schema version. `IntegrationEnvelope` validation stays.
- Consumers validate business scope against trusted data before applying an effect.
- No cross-business batching. No secrets, tokens, OTPs or unnecessary PII in messages.

### 3.5 Retry and dead-letter handling
- Retries are bounded, use exponential backoff and are limited by validated `DurableDeliveryOptions`.
- Validation, authorization and business-rule failures are not retried.
- Exhausted or poison messages go to `DeadLetter` or dead-letter queues. Reprocessing them requires permission and is audited.

### 3.6 Observability
- Use structured logging with correlation IDs carried across HTTP, outbox, broker and workers.
- Outbox backlog, dead-letter counts and consumer failures are observable.

### 3.7 Transport and environments
- Staging and Production require AMQPS and environment-supplied credentials. `InfrastructureConfigurationValidator` stays.
- Development and Test stay isolated, and tests never use Development queues.
- Delivery stays behind the enable flag. The PostgreSQL workers keep the system correct while it is disabled.

## 4. Consequences
- No new dependency or licensing exposure. The team keeps ownership of the reliability code (retry, dead-letter, topology, inbox), so changes need focused tests: duplicate and out-of-order delivery, retry exhaustion, outbox atomicity with rollback, and tenant mismatch.
- Extend the existing `Integration` components instead of building parallel messaging infrastructure. Schema changes to the outbox/inbox tables still need an approved migration.
- **Compatible with approved specs:** POS SRS/LLD v1.2, the Loyalty LLD, Mobile LLD v1.4 and HR LLD v1.3 (including its §9 outbox/inbox tables). ADR-ARCH-001 §5's note about superseding the HR LLD §9 table names is withdrawn.
