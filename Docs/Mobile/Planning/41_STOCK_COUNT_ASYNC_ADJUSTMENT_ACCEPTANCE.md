# Stock Count Approval → Asynchronous Stock Adjustment (Path B) Acceptance

**Date:** 2026-10-17  
**Status:** Implemented and PostgreSQL-tested on the isolated Test instance (`127.0.0.1:55432`). **Feature flag defaults to OFF; the migration is NOT applied to the Development database.** Live RabbitMQ was not exercised.

## Scope

Stock Take (Mobile) and Stock Count (Web) are the same ERP `StockCount`. Two stock-adjustment initiation paths now coexist:

- **Path A — Manual Stock Adjustment:** unchanged (create → submit → review → approve → post). No route, permission, UI or posting logic was modified.
- **Path B — Stock Count generated adjustment:** approving a Submitted StockCount atomically persists the approval, a durable adjustment job and a transactional-outbox message. A background worker then reuses the existing adjustment logic to create and post the adjustment.

Not introduced: a separate StockTake entity, a second inventory engine, a new microservice, a new permission or a role mapping.

## Delivered behaviour

| Step | Behaviour |
|---|---|
| Submit (Web/Mobile) | Unchanged (Web synchronous `Submitted`; Mobile API-05C 202 job). No stock effect |
| Approve `POST /api/stock-counts/{id}/approve` (flag on) | One required-audit PostgreSQL transaction: `FOR UPDATE` on the count, lifecycle/all-lines-counted checks, maker-checker, `Approved`, durable `stock_count_adjustment_jobs` row and `stock-count.adjustment.requested` outbox message. Returns immediately: `status: Approved`, `adjustmentState: Pending`, message "approved … inventory has not changed yet". Repeat clicks/concurrent calls return the same job (no second job or outbox row) |
| Worker | Claims the job (lease, bounded attempts), re-checks the persisted approver, locks the count, verifies status/revision, applies the movement-evidence gate, then in **one** audited transaction creates the draft (shared builder), submits it and calls the existing `StockAdjustmentApprovalStore.ApproveAsync`: Inventory, StockLedger, accounting journal, adjustment `Approved`, StockCount `Completed` and job `Completed` commit together |
| Outcomes | `Completed` (adjustment posted, or no differences so no adjustment), `Blocked` (ambiguous evidence, nothing changed), `Failed` (permanent, nothing changed), `RetryScheduled` (transient, whole transaction rolled back) |

### Job states and failure codes

`Pending`, `Processing`, `RetryScheduled`, `Completed`, `Blocked` (terminal), `Failed` (terminal). Codes: `STOCK_COUNT_NOT_FOUND`, `STOCK_COUNT_STATE_CHANGED`, `COUNT_REVISION_CHANGED`, `APPROVER_NOT_AUTHORIZED`, `MAKER_CHECKER_VIOLATION`, `ADJUSTMENT_ALREADY_EXISTS`, `ADJUSTMENT_VALIDATION_FAILED` (e.g. negative stock, fractional difference), `TRANSIENT_FAILURE`, `RETRY_ATTEMPTS_EXHAUSTED`. `Blocked` jobs carry `evidenceStatus` and `blockedReasonCodes` (the API-05D codes, e.g. `MOVEMENT_DURING_COUNT_WINDOW`).

## Product Owner rules

| Rule | Implementation |
|---|---|
| Do not duplicate posting logic | `StockCountAdjustmentDraftBuilder` is extracted from `StockCountAdjustmentDraftStore` and shared; posting is the unchanged `ApplyApprovalAsync`. `StockAdjustmentApprovalStore.ApproveAsync` only gained "join a caller-owned transaction" (manual behaviour unchanged) |
| Manual workflow untouched | `StockAdjustmentsController`, stores and the Angular adjustment pages unchanged; the existing manual/concurrency suites pass |
| Explicit combined authorization, no new permission | Approval requires `StockCount.Approve` **and** `stock_adjustments.approve` (403 otherwise; role-switched/SuperAdmin denied). The worker re-checks the same pair, active user and location access from current database state (`StockCountAdjustmentApproverPolicy`) |
| No second manual adjustment approval | The worker submits and approves the adjustment under the persisted approver's combined authorization |
| Maker-checker | Approver must differ from the submitter (403 at approval; re-verified in the worker → `MAKER_CHECKER_VIOLATION`) |
| Audit attribution | Job stores submitter, approver, approver role, source StockCount, audit/request correlation ids. Worker audit identity: real actor = approver, source `Integration`, request path `job:stock-count-adjustment-worker`, approval's correlation id. `StockAdjustment.AdjustedBy` = submitter, `ApprovedBy` = approver, StockLedger `CreatedBy` = approver |
| One effective adjustment per approved revision | Unique `(stock_count_id, approved_count_revision)` job index plus the existing unique `stock_adjustments.source_stock_count_id` |
| Atomicity / concurrency / idempotency | One `RequiredAuditTransaction`; job and count row locks; Inventory `xmin`, unique inventory index and journal idempotency key retained; duplicate/redelivered messages are no-ops |
| Ambiguous evidence never changes stock | The same evaluator and results policy as API-05D (`StockCountMovementEvidenceService`, now shared). Anything other than `NoConflictDetected` blocks the job |
| Immediate response | Approval returns `Approved/Pending`; never "Adjusted" |
| Both workflows in Angular | Count details page shows distinct state, polls while a job is active, links to the resulting adjustment; manual pages unchanged |

## Provenance and accounting

Provenance reuses `StockAdjustment.SourceStockCountId` (the existing equivalent of `SourceType=StockCount`); no `StockAdjustment` schema change. The accounting posting adapters (`StockAdjustmentPostingAccountResolver`, `AccountingConfigurationTenantScopeAdapter`) were HTTP-bound; they now fall back to the Business of an initialized **Integration** audit identity only when there is no HTTP Business scope (HTTP behaviour unchanged).

## Delivery and configuration

- Flag `FeatureFlags:StockCountAsyncAdjustmentEnabled` (default **false**, read per request). While on, `generate-adjustment-draft` is refused (the worker creates the adjustment); while off, the existing synchronous approval and draft flow is unchanged.
- Worker: `StockCountAdjustmentSweeperHostedService` (PostgreSQL-driven; runs only while the flag is on; `StockCount:Adjustment:*` bounds) plus `StockCountAdjustmentConsumerHostedService` (durable quorum queue `retailpos.stock-count.adjustment`, prefetch 2, manual ack after processing, poison messages dead-lettered) registered only when `DurableDelivery` is enabled. Dispatcher/publisher are the existing confirmed-publisher components.
- Development `DurableDelivery` stays disabled (the validator requires `amqps://`); jobs complete via the sweeper (5 s interval, `PendingRecoveryGraceSeconds` default 5 s), so adjustment posting normally follows approval within seconds.
- Keep the flag on until pending jobs drain; the sweeper idles while it is off.
- Enabling requires the migration `20261017132713_StockCountApprovalAdjustmentJobs` (creates `stock_count_adjustment_jobs` only). It was applied to Test databases only.

## Evidence

All HTTP/worker runs used the isolated PostgreSQL fixture on port 55432; Development was not touched.

| Run | Result |
|---|---|
| `StockCountAsyncAdjustmentPostgresHttpTests` (new) | **12 passed** (repeated runs stable) |
| Regression: `MobileStockTake*`, `StockCount*`, `StockAdjustment*`, accounting configuration, Mobile inventory/dashboard, infrastructure configuration, migration chain | **173 of 174 passed** on the first run; the one failure was the migration-chain test pinning the previous latest migration, updated and re-run: **25 passed** |
| `dotnet build RetailPOS.slnx -c Release` | Succeeded, 0 errors, 15 existing warnings |
| Angular `tsc -p tsconfig.app.json --noEmit` | No errors |

New tests cover: immediate `Approved/Pending` response with durable job and outbox and no stock change; repeat/concurrent approvals creating one job; exactly-once posting under triple concurrent consumers and redelivery with Inventory/StockLedger/StockAdjustment/count/audit-attribution assertions; maker-checker, combined-permission, role-switch and role denials; blocked ambiguous evidence with no stock change; negative-stock permanent failure with rollback; revoked approver; transient failure rollback then recovery without duplicate posting; crashed-lease recovery and bounded attempts; no-difference completion; draft endpoint refusal; reads not blocked by locks; delivery-handler ack/nack semantics; Mobile lifecycle shows the job.

## Known limitations and follow-ups

- **Blocked/Failed jobs are terminal and the count stays `Approved`.** There is no reopen/retry action yet; the Product Owner's guidance is to correct stock with a Manual Stock Adjustment. A reviewer-controlled retry/reopen policy is a follow-up.
- Web counts uploaded from Excel have no per-line observation time, so the evidence gate uses ERP submission time; counts that straddle POS activity will block. This is intentional and conservative.
- Counts approved before the flag was enabled have no job; `generate-adjustment-draft` is refused while the flag is on.
- Live RabbitMQ publish/consume is not validated (no TLS broker); the sweeper path is tested.
- The Development database migration, enabling the flag and staging/production rollout need separate Product Owner approval.
