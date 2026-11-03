# Shared Notification Architecture

**Status:** proposed extension to the existing RetailPOS modular monolith. The SRS notification types are candidate registry entries, not evidence that their event producers already exist. In-app notification is required on both Angular and Flutter for enabled rules; push/email/SMS are out of MVP scope.

## Current foundation and gap

- **VERIFIED:** subscription reminders exist; durable integration outbox/inbox, RabbitMQ dispatcher, hosted worker, retries/lease/dead-letter state and audit foundations exist.
- **VERIFIED:** inspected event writer use is for operational-location changes; the sale-completed hook is in-process/logging and not verified as a transactional notification source. Current sale event payload does not establish all rule inputs.
- **VERIFIED:** current stock-count lifecycle supports submission/review/approval/rejection; there is no verified shared business notification policy/inbox/Angular configuration module.
- **PROPOSED:** extend existing persistence/worker infrastructure inside the monolith, after verifying transactional boundaries. Do not add a notification microservice for MVP. Add an independent operational alert when the notification pipeline itself fails.

## Logical flow

```text
authoritative committed business change / scheduled evaluation
  -> typed source event + outbox row (same transaction where applicable)
  -> idempotent policy evaluator (rule version + event-time snapshot)
  -> server-side scoped recipient resolution
  -> durable notification + per-recipient read/inbox records
  -> Angular and Flutter inbox APIs
  -> optional real-time hint; clients reconcile from inbox
```

At-least-once processing with idempotent effects is the guarantee; do not claim end-to-end exactly-once delivery. Store stable source event ID, policy/type version, recipient resolution evidence, deduplication key, correlation ID, attempt/error state and timestamps. Do not put sensitive sale/customer details in a broad event or push preview.

## Registry: all SRS v1.4 candidate notification types

The following 28 types are **PROPOSED registry entries** from SRS §3.7.4. Each defaults disabled unless the SRS expressly suggests otherwise and the producer is proven. For all rows, producer/outbox integration was **not verified** during repository inspection. Stock-count state transitions exist, but durable notification events for those transitions were not verified.

| Stable type code | Trigger family | Producer status in current ERP | Gate before enabling |
|---|---|---|---|
| `sales.discount.threshold_exceeded` | Finalized sale above approved discount percentage | **BLOCKED** | Finalization source and discount denominator/rounding ADR; transactionally durable event. |
| `sales.discount.anomalous` | Discount deviates from configured benchmark | **BLOCKED** | Approved benchmark and comparison window; no causal assertion. |
| `sales.refund.completed` | Posted refund/return threshold | **BLOCKED** | Verify posted-return source and policy. |
| `sales.large_sale.completed` | Finalized sale amount threshold | **BLOCKED** | Verify sale event, currency, amount treatment. |
| `sales.target.achieved` | First crossing of approved target | **BLOCKED** | Target source and stable period/edge-crossing event. |
| `sales.target.missed` | Closed reporting period below target | **BLOCKED** | Target data, business calendar, scheduler idempotency. |
| `sales.performance.drop` | Comparison window below baseline | **BLOCKED** | Metric definition, baseline and suppressions. |
| `inventory.low_stock` | Reorder threshold crossing | **BLOCKED** | Authoritative availability calculation, edge/cooldown policy. |
| `inventory.critical_stock` | Critical threshold crossing | **BLOCKED** | Threshold units, scope and transition semantics. |
| `inventory.out_of_stock` | Available quantity transitions to zero | **BLOCKED** | Authoritative stock availability and transition producer. |
| `stocktake.submitted` | Authorized count submission | **BLOCKED** | Existing lifecycle is verified; event producer/outbox not. |
| `stocktake.rejected` | ERP rejection | **BLOCKED** | Existing lifecycle is verified; event producer/outbox not. |
| `stocktake.approved` | ERP approval | **BLOCKED** | Existing lifecycle is verified; event producer/outbox not. |
| `stocktake.variance_detected` | Reconciled variance above tolerance | **BLOCKED** | Reconciliation event and approved tolerance. |
| `inventory.adjustment.posted` | Authorized adjustment committed | **BLOCKED** | Verify sole posting transaction and source event. |
| `stocktransfer.approval_required` | Transfer awaits approval | **BLOCKED / feature-gated** | Verify transfer capability and approval transition. |
| `stocktransfer.received` | Target outlet receipt posted | **BLOCKED / feature-gated** | Verify transfer and authoritative posted receipt. |
| `purchaseorder.approval_required` | PO enters approver queue | **BLOCKED / feature-gated** | Verify PO approval capability. |
| `goodsreceipt.completed` | GRN posted | **BLOCKED** | Verify completed/posted event, not draft. |
| `expense.large_submitted` | Submitted expense above threshold | **BLOCKED** | Verify expense domain and grants. |
| `expense.approval_required` | Expense enters approval state | **BLOCKED** | Verify approval transition and recipient scope. |
| `supplierpayment.completed` | Supplier payment posted | **BLOCKED** | Verify payment finalization and financial redaction. |
| `cashsession.variance_detected` | Closing variance above tolerance | **BLOCKED** | Verify cash reconciliation and sensitive field policy. |
| `forecast.ready` | Approved forecast run published | **BLOCKED / feature-gated** | Forecast implementation and model-run event. |
| `forecast.stockout_predicted` | Eligible forecast predicts stockout | **BLOCKED / feature-gated** | Data-quality threshold, horizon and prediction provenance. |
| `forecast.production_recommendation_ready` | Advisory recommendation run published | **BLOCKED / feature-gated** | Approved forecasting capability and non-authoritative wording. |
| `system.background_job_failed` | Relevant job fails after retries | **PROPOSED** | Worker-specific event and independent ops routing. |
| `system.notification_delivery_failed` | Notification dead-letter/backlog/SLO breach | **PROPOSED** | Independent monitoring path; never alert only through same pipeline. |

SRS suggested rollout defaults, severity, audience and channel are proposals, not authorization. Catalogue entries must include stable code/version, template allowlist, severity, channel, audience scope, default-enabled flag and capability availability. Never register an unsupported producer as active.

## Configuration and recipient rules

- Angular Settings → Notifications provides permission-gated type catalogue, policy editor, supported metric/operator/unit/threshold, strict/inclusive comparison, scope/effective dates, recipients, enable/disable, dry-run preview and version history.
- No arbitrary expressions, scripts or SQL. Validate operators/units per type and type-check configuration.
- Resolve role/user/business-owner recipients server-side against current effective business membership, active-account state and resource visibility. Deduplicate and enforce business/outlet scope. Whether resolution is at event time or processing time is an ADR.
- Individual preferences may suppress only approved optional categories; critical/security/system notifications cannot silently disappear.
- Rule changes apply to future events. Preserve event-time snapshot, type/rule version and resolved recipient evidence for audit.
- Deep links identify an approved resource type, never an unrestricted URL. Reauthorize resource access when opened, including after role changes.

## Delivery, reliability and observability

- Persist event intent with the successful business transaction where applicable; rollback must remove the event.
- Consumer uses bounded exponential backoff with jitter, idempotency key per source event/type/policy/recipient and per-recipient delivery row. Poison events go to quarantine/dead-letter without blocking healthy work.
- Provide privileged, audited replay with collision protection. Existing replay authorization is disabled unless explicitly allowed; do not enable operational replay without permissions and review.
- Record status, attempt count, next attempt, error category, timestamps, rule version, correlation ID and suppression reason.
- Angular and Flutter inbox share server records and per-recipient read state. Read/mark-all are idempotent; server timestamps govern ordering/unread count. SignalR/push is optional hint only.
- Dashboard/alerts: outbox age, worker lag, processing latency, retries, dead letters, oldest undelivered, notification creation and inbox query latency. Pipeline-failure alert must use independent operations monitoring.
- SRS proposes 99% within 60 seconds under healthy service; measure in load/pilot before production acceptance rather than assert in advance.

## Security and audit

Enforce row-level recipient visibility and tenant/business/outlet authorization on every API and background recipient resolution. Redact discount, profit, customer, payment and employee-sensitive data. Audit every policy change, dry-run where sensitive, replay, suppression override and privileged access. Record actor and correlation. Never log tokens, full payloads or PII unnecessarily.

## SRS requirement coverage

| SRS IDs | Architecture coverage |
|---|---|
| NOT-001..006 | Shared type/rule setup, scoped recipient resolution, Angular configuration, shared inbox and role-safe client UX. |
| NOT-007..009 | Transactional outbox, durable idempotent worker, retries, dead-letter, replay and SLO/monitoring. |
| NOT-010..014 | Preference precedence, privacy, versioned event evidence, per-recipient read sync and notification UX. |
| NOT-015..021 | Full 28-type registry above, typed conditions, scheduled/digest evaluation, suppression, provenance, feature gates and safe templates/deep links. |
| NFR-NOT-001..004 | Latency measurement, crash/replay loss testing, indexed/retained inbox, worker-lag/dead-letter operations monitoring. |

SRS ADR-N01..N05 block production rollout of sale policies pending canonical event/discount definition, rule precedence, recipient timing, retention/SLO and infrastructure reuse decisions.
