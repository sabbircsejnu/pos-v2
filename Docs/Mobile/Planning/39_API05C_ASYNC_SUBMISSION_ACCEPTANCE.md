# API-05C Mobile Stock Take Asynchronous Submission Acceptance

**Status:** API-05C durable submission foundation implemented and PostgreSQL-tested. **PARTIALLY PASSED** — see [Remaining blockers](#remaining-blockers-and-integration-decision).  
**Scope:** durable submission job + transactional outbox, status recovery, bounded background worker invoking the *existing* ERP StockCount submission. Out of scope: API-05D result/history, approval, adjustment generation, inventory posting, Flutter/Angular changes, notifications, forecasting.

```text
Flutter → Mobile API → PostgreSQL job + transactional outbox → RabbitMQ → background worker → existing ERP StockCount submission
```

## Delivered contract

| Method and route | Purpose |
|---|---|
| `POST /api/mobile/v1/stock-take/sessions/{sessionId}/submissions` | Durably accept a submission request for the acknowledged count revision |
| `GET /api/mobile/v1/stock-take/sessions/{sessionId}/submissions/{jobId}` | Recover/poll the durable job state |

Request: header `Idempotency-Key` (exactly one value, 8–100 characters of `A–Z a–z 0–9 - _ . :`) and body:

```json
{ "acceptedRevision": 4 }
```

The server derives the canonical request fingerprint (SHA-256 of `mobile-stock-take-submission:v1|{sessionId}|{acceptedRevision}`) and persists it with the key. Clients never supply Business, Outlet, role, actor or fingerprint.

`202 Accepted` (with `Location` set to the status URL) is returned only **after** the PostgreSQL transaction that wrote the job, the outbox message and the Audit v2 event has committed:

```json
{
  "success": true,
  "data": {
    "jobId": "6f7d…",
    "stockTakeId": 9001,
    "acceptedRevision": 4,
    "status": "Accepted",
    "isTerminal": false,
    "attemptCount": 0,
    "nextAttemptAtUtc": null,
    "failureCode": null,
    "createdAtUtc": "2026-10-16T12:00:00Z",
    "updatedAtUtc": "2026-10-16T12:00:00Z",
    "completedAtUtc": null,
    "sessionStatus": "Draft",
    "movementReconciliationStatus": "Unresolved",
    "statusUrl": "/api/mobile/v1/stock-take/sessions/9001/submissions/6f7d…",
    "isReplay": false
  }
}
```

The DTO contains no cost, valuation, expected/current quantity or variance.

### Durable lifecycle (explicit states)

| Status | Meaning |
|---|---|
| `Accepted` | Durably recorded; ERP StockCount **not** yet submitted |
| `Processing` | Claimed by a worker with a lease |
| `RetryScheduled` | Retryable failure; `nextAttemptAtUtc` set |
| `ErpSubmitted` | Existing ERP submission succeeded (session is `Submitted`). **Not** approval, reconciliation or posting |
| `Failed` | Terminal; safe `failureCode` stored |

Failure codes: `STOCK_TAKE_NOT_FOUND`, `STOCK_TAKE_STATE_CHANGED`, `COUNT_REVISION_CHANGED`, `ACTOR_NOT_AUTHORIZED`, `ERP_SUBMISSION_REJECTED`, `TRANSIENT_FAILURE` (while retrying), `RETRY_ATTEMPTS_EXHAUSTED`.

### Error mapping

| HTTP | Code |
|---|---|
| 400 | `INVALID_IDEMPOTENCY_KEY`, `INVALID_STOCK_TAKE_SUBMISSION_REQUEST` (including `acceptedRevision <= 0`), `INVALID_STOCK_TAKE_SUBMISSION_ID` |
| 403 | role/permission/Business/Outlet-guard denial |
| 404 | `STOCK_TAKE_NOT_FOUND` (also for unauthorized Outlet), `STOCK_TAKE_SUBMISSION_NOT_FOUND` |
| 409 | `STALE_SESSION_REVISION`, `STOCK_TAKE_NOT_DRAFT`, `IDEMPOTENCY_KEY_PAYLOAD_MISMATCH`, `STOCK_TAKE_SUBMISSION_IN_PROGRESS` (response `data` carries the active job) |
| 422 | `NO_ACCEPTED_COUNTS` |

Same key + same fingerprint returns the original job (`isReplay: true`), so a client that timed out can recover the job identifier by repeating the identical request. Same key + different `acceptedRevision` returns `409 IDEMPOTENCY_KEY_PAYLOAD_MISMATCH`.

## Authorization

- Mobile Business Owner / Sales Manager only (`BusinessOwner`, `SalesManager`, `OutletManager`, `Manager`), plus existing `StockCount.Submit`. Reads of job status require `StockCount.ViewOwn` or `StockCount.ViewAll`.
- Salesperson, Cashier, role-switched sessions, SuperAdmin/global context and a Business that fails the single-Business guard are denied. Business and Outlet come from server context and `IUserOutletAccessService`; the session is re-verified under the row lock. An unauthorized Outlet's session returns `404`.
- No Mobile approve/reject/adjust/post route exists (the `approve` probe returns `404`).

## Persistence

- Entity `StockCountSubmissionJob` (`stock_count_submission_jobs`): session/Business/Outlet, persisted **actor user id and role name**, idempotency key, request fingerprint, accepted revision, status, attempt counter, next attempt, lease owner/expiry, failure code, outbox message id, Audit correlation id, request correlation id, UTC timestamps, optimistic-concurrency `version`.
- Constraints/indexes: unique `(stock_count_id, idempotency_key)`; **unique filtered index `ux_stock_count_submission_jobs_active_session` allowing one `Accepted/Processing/RetryScheduled` job per session**; unique `outbox_message_id`; status and revision check constraints; worker-scan index.
- Migration: `20261016124940_Api05CMobileStockTakeSubmissionJobs` (creates this table only; no existing data touched).

## Acceptance transaction

One Audit v2 required transaction (`RequiredAuditTransaction`): `SELECT … FOR UPDATE` on the `stock_counts` row; replay/mismatch check; active-job check; Draft check; revision check; insert job; append `IntegrationOutboxMessage` (`mobile.stock-take.submission.requested`) through the existing `IIntegrationOutbox`; flush Audit event; commit. RabbitMQ is never contacted inside the transaction. Replay/error returns stage no changes.

**Session lock:** while a job is active, Mobile count writes return `409 STOCK_TAKE_SUBMISSION_IN_PROGRESS` and ERP Excel uploads are refused, so the acknowledged revision cannot move. The lock is the job row only — **no inventory, ledger or POS lock** is taken. A terminal `Failed` job releases the session; a new request with a new key may then be accepted.

## Outbox, RabbitMQ and worker

- **Outbox/dispatch:** existing `OutboxDispatcher` (leases, bounded backoff, dead-letter) publishes the message; unchanged.
- **Consumer** (`MobileStockTakeSubmissionConsumerHostedService`, only when `DurableDelivery` is enabled): durable quorum queue `retailpos.mobile-stock-take.submission` bound to the existing topic exchange, prefetch 4, `x-delivery-limit` 5. `MobileStockTakeSubmissionDelivery` validates the envelope, calls the processor and ACKs; poison messages are NACKed without requeue; unexpected failures are NACKed without requeue because **PostgreSQL, not the broker, drives retries** — so there is no infinite redelivery loop and no lost job.
- **Sweeper** (`MobileStockTakeSubmissionSweeperHostedService`): every 5 s re-drives `RetryScheduled` jobs that are due, `Processing` jobs whose lease expired (worker crash/restart), and `Accepted` jobs older than `AcceptedRecoveryGraceSeconds` (60) whose message never arrived. Configure via `MobileStockTake:Submission` (`MaxAttempts` 5, `LeaseSeconds` 300, `BaseRetrySeconds` 10, `MaxRetrySeconds` 900, …).
- **Processor** (`MobileStockTakeSubmissionProcessor`): (1) audited claim transaction under `FOR UPDATE` with lease and attempt increment; attempts exhausted by crashes become `Failed`; (2) audited execution transaction: re-lock job and verify lease owner, **re-check the persisted actor** against current user/role/permission/Outlet policy, lock session, verify Business/Outlet/Draft status/acknowledged revision, then send the existing `SubmitStockCountCommand` with the persisted actor id, and mark the job `ErpSubmitted` in the same transaction; (3) transient exceptions roll back everything and an audited transaction records `RetryScheduled` (exponential backoff) or `Failed/RETRY_ATTEMPTS_EXHAUSTED`. Business/validation failures are terminal and never retried.
- **Actor:** the ERP step runs with Audit v2 identity = the original actor (`RealUserId`, original correlation id, source `Integration`). No SuperAdmin or privileged identity is created; claim/retry bookkeeping uses a Business-scoped system job identity. `StockCountLifecycleStore` now joins a caller-owned transaction so the ERP transition and job outcome commit atomically; standalone behaviour is unchanged.
- The worker never approves, rejects, generates adjustments, or writes Inventory/StockLedger.

## Movement reconciliation and stock safety

ERP submission to `Submitted` only moves the session into the existing ERP review state. The session keeps `MovementReconciliationStatus = Unresolved`, so ERP adjustment-draft generation still fails closed (asserted in the PostgreSQL tests). No movement after session creation is treated as variance and no final adjustment is calculated. `ErpSubmitted` ≠ approved ≠ reconciled ≠ posted.

## Test and build evidence

All runs use the dedicated disposable PostgreSQL 16 fixture (`127.0.0.1:55432`, unique disposable databases; port 5435 development database not touched).

| Run | Result |
|---|---|
| API-05B revalidation (`MobileStockTakePostgresHttpTests` + two StockCount concurrency suites) before API-05C changes | **4 passed, 0 failed** |
| New `MobileStockTakeSubmissionPostgresHttpTests` | **15 passed, 0 failed** |
| Focused set: API-05C + API-05A/B + StockCount concurrency + Mobile dashboard + durable-delivery foundation + RabbitMQ contract tests | **40 passed, 0 failed** |
| Broader `StockCount`/`StockTake` filter (ERP upload/lifecycle/Phase-1 + Mobile) | **51 passed, 0 failed** |
| `dotnet build RetailPOS.slnx -c Release --no-restore` | **Succeeded, 0 errors, 19 warnings** (existing `SixLabors.ImageSharp` advisories and nullable diagnostics; same count as the API-05B build) |

The 15 new tests cover: Owner and Sales Manager submission; Salesperson/Cashier/missing-permission/role-switched/foreign-Business/unsupported-identity denial; Outlet concealment; missing/invalid idempotency keys; same-key replay and mismatch; 8-way concurrent distinct-key and 6-way same-key races (one job, one outbox row); stale revision, no counts and non-Draft sessions; outbox-failure rollback before any 202 and clean retry; outbox broker outage then recovery with the job untouched; triple concurrent consumers and redelivery (ERP submitted exactly once, no approval, inventory/ledger unchanged, adjustment draft still blocked); crash recovery via expired lease; lost-message recovery; bounded retry then terminal failure and lock release; terminal failure codes (`COUNT_REVISION_CHANGED`, `STOCK_TAKE_STATE_CHANGED`, `ERP_SUBMISSION_REJECTED`) and revoked actor (`ACTOR_NOT_AUTHORIZED`); status scoping across sessions/Outlets/roles; a POS-style inventory/ledger write completing while the session row is locked; RabbitMQ delivery handler ack/nack semantics.

## Remaining blockers and integration decision

- **Live RabbitMQ is not exercised.** Publisher confirms/transport are covered by the existing mock-channel contract tests, and consumer ack/nack by mock-channel tests here; no broker-backed end-to-end test ran. `DurableDelivery` requires approved `amqps` credentials and a consumer queue/dead-letter topology that has not been provisioned. Until then, the sweeper alone completes jobs after the 60-second grace.
- **Authoritative movement/baseline reconciliation remains unresolved**, so counts cannot become approved/adjusted stock. API-05C deliberately stops at ERP `Submitted`.
- Operator redrive of dead-lettered *outbox* messages is still disabled by the existing `DisabledIntegrationReplayAuthorization`; the job table itself is recoverable by the sweeper.
- No device lease/takeover or multi-device merge policy was invented.
- Flutter may integrate the 202/status contract in a controlled way only: show "accepted for processing", poll the status URL, and never present `Accepted`/`ErpSubmitted` as approved, reconciled or posted.

**Verdict: PARTIALLY PASSED.**
