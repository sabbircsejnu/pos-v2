# ADR-ARCH-001 — MassTransit + RabbitMQ as the standard integration messaging framework

> **SUPERSEDED (2026-11-04) by `ADR-ARCH-002_RABBITMQ_CLIENT_CUSTOM_OUTBOX_INBOX.md`.** MassTransit is not adopted, because of licensing and long-term dependency considerations; its migration is deferred indefinitely. The current RabbitMQ.Client + custom outbox/inbox is **not** legacy. This record is history only. It is not binding and no task depends on it.

| Field | Value |
|---|---|
| Status | **Superseded** by ADR-ARCH-002. It was accepted on 2026-11-04 and was never implemented. |
| Decision owner | Product/technical owner (user-approved) |
| Scope | Asynchronous integration messaging only (cross-module/cross-process events and commands) |
| Related | `CLEAN_ARCHITECTURE_MEDIATR_CQRS_MIGRATION_PLAN.md` (layering, §7 MediatR, §10 transactions); `Docs/Development/LOCAL_INFRASTRUCTURE.md`; `.github/instructions/local-infrastructure.instructions.md` |

## 1. Context
- **Current (legacy) implementation:** raw `RabbitMQ.Client` 7.2 with a custom PostgreSQL outbox/inbox in `src/RetailPOS.Infrastructure/Integration` (`integration_outbox_messages`, `integration_inbox_messages`, `OutboxDispatcher`, `RabbitMqConfirmedPublisher`, `RabbitMqInboxDelivery`, and the delivery adapters for Stock Count adjustment and Mobile Stock Take submission).
- `DurableDelivery:Enabled` is `false` in every environment, and the publisher requires `amqps://`. RabbitMQ publishing is therefore not live anywhere. Business work completes through PostgreSQL-driven jobs and workers (for example `stock_count_adjustment_jobs`, `stock_count_submission_jobs`, the sale financial posting and cash-difference posting workers).
- Maintaining bespoke transport, retry, dead-letter, topology and inbox code is a reliability and cost risk as more modules (POS reporting, loyalty, HR, mobile, notifications) need durable integration.

## 2. Decision
1. **MassTransit + RabbitMQ** is the standard integration messaging framework.
2. **PostgreSQL transactional outbox/inbox** uses MassTransit's Entity Framework Core outbox (bus outbox + consumer inbox) on `RetailPOSDbContext`, where a message must be atomic with a business write.
3. **MediatR + CQRS stays** for in-process application operations. MassTransit is used only for asynchronous integration, never as an in-process mediator.
4. **The modular monolith and Clean Architecture stay unchanged:** one deployable, one PostgreSQL database, the same project dependency rules. No microservice split is implied.
5. The current raw `RabbitMQ.Client` outbox/inbox implementation is **legacy**: frozen for new features, removed by the migration.
6. **Messaging-specific data may be discarded** during migration: legacy outbox/inbox rows and legacy `retailpos.*` queues/exchanges. Preserving them is not required.
7. **Business transactional data must be preserved:** sales, payments, stock and ledgers, cash sessions, journals and postings, job tables, projections, audit and event tables, and all other non-messaging data. The migration must not drop, rewrite or re-derive them.

## 3. Binding design rules
### 3.1 Layering
- Application defines narrow ports (for example an integration event publisher). Application, Domain and controllers never reference MassTransit types.
- Infrastructure implements the ports with MassTransit (`IPublishEndpoint`/`ISendEndpointProvider`), owns consumers, outbox configuration and topology. API composes it in `Program.cs`.
- Message contracts are immutable, explicitly versioned records in a dedicated contracts namespace with stable message URNs/entity names. Renaming a type must not change its wire identity.

### 3.2 Transactions
- The command handler still owns the business transaction (migration plan §10). Integration messages are written to the EF Core outbox through the **same scoped DbContext and transaction** as the business change, then committed together. No message is published to the broker before commit.
- No distributed transactions, no broker calls inside database transactions, no blanket transaction pipeline behavior.
- PostgreSQL remains the source of truth. Durable job tables and their sweeper/recovery workers stay authoritative for long-running effects (stock-count adjustment, mobile submission, financial posting). A message is a trigger or notification for that work, never the only record of required work.

### 3.3 Idempotency and ordering
- Delivery is **at-least-once**. Every consumer is idempotent through the MassTransit inbox (message ID + consumer) **and** a business-level guard (unique effect key, job ID or source version), so a replay after inbox cleanup is still safe.
- Consumers tolerate out-of-order delivery: use source versions or state checks instead of assuming order.
- Existing API idempotency keys (for example on cash session open and sale mutations) are unchanged and independent of messaging.

### 3.4 Tenant isolation
- Every contract carries `BusinessId`, a correlation ID, a causation ID where relevant, an occurred-at UTC timestamp and a schema version (the same identity the current `IntegrationEnvelope` enforces).
- Consumers validate business scope against trusted data before applying an effect, and never trust a client-supplied or header-only business ID. No cross-business batching in a consumer transaction.
- No secrets, tokens, OTPs or unnecessary PII in messages.

### 3.5 Retry, failure and poison handling
- Bounded retry with exponential backoff (immediate/interval retry, then delayed redelivery), with limits configured through typed options and validated at startup, like the current `DurableDeliveryOptions` bounds.
- Validation, authorization and business-rule failures are **not retried**: they go straight to the error queue.
- Exhausted or poison messages land in MassTransit error/dead-letter queues, with logging and monitoring. Re-processing a faulted message requires permission and is audited.
- Outbox delivery, consumer lag and error-queue depth are observable through structured logs and health checks, with correlation IDs carried across HTTP, messages and workers.

### 3.6 Transport and environments
- Staging and Production require AMQPS and environment-supplied credentials. `InfrastructureConfigurationValidator` keeps failing startup on missing or local endpoints. Local plain AMQP is for development and testing only.
- Development and Test stay isolated (separate brokers/vhosts per `LOCAL_INFRASTRUCTURE.md`). Tests never attach to Development queues.
- Delivery stays behind an explicit enable flag. The PostgreSQL-driven workers must keep the system correct while it is disabled.

## 4. Migration constraints (for the implementation task)
- Implement as its own planned slice: a characterization inventory of every current producer and consumer first, then MassTransit infrastructure, then a per-flow cutover, then removal of the legacy code.
- Add MassTransit outbox/inbox tables through a normal reviewed EF migration (approval required). Drop the legacy `integration_*` tables only in a later approved migration, after all callers move.
- **Before discarding legacy messaging data:** confirm that no business effect depends on an undelivered legacy row. For each pending row, either the authoritative job/sweeper already covers it, or it is reconciled from authoritative tables (for example rebuild `hr_operational_location_projections` from Outlets/Warehouses).
- Before adding the package, confirm the MassTransit version, its licensing terms and .NET 10 compatibility (licensing differs between major versions). Pin one version across projects.
- Tests: duplicate delivery, out-of-order delivery, poison and retry exhaustion, outbox atomicity with rollback, and tenant mismatch rejection. Use the PostgreSQL Test stack plus a live-broker check (`Category=LiveInfrastructure`).

## 5. Consequences
- **Positive:** standard retry, error-queue, topology and outbox/inbox behavior; less bespoke reliability code; a consistent pattern for upcoming modules.
- **Costs and risks:** a new dependency (version and licensing to confirm); a schema change for outbox/inbox tables; a cutover risk for flows that currently rely on custom envelope hashing and replay. These are mitigated by the characterization tests and the authoritative PostgreSQL jobs.
- **Compatibility with approved specs:** POS SRS/LLD v1.2 (FR-REPORT-02, FR-GL-01, AC-16/AC-27), Loyalty LLD, Mobile LLD v1.4 and HR LLD v1.3 require a durable outbox, RabbitMQ, idempotent consumers and same-transaction outbox writes. This ADR satisfies those requirements. It changes **how** they are implemented, not **what** the business behavior is. Table and class names in those documents that describe the custom outbox/inbox (for example HR LLD v1.3 §9 `integration_outbox_messages`/`integration_inbox_messages`) are implementation suggestions superseded by §2–3 here. Their business requirements (business scope, idempotency, reconciliation) still apply.
