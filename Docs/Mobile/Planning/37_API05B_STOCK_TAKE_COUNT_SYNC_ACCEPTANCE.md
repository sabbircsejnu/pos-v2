# API-05B Mobile Stock Take Count Sync Acceptance

**Status:** API-05B count-operation protocol implemented; full Mobile Stock Take remains incomplete.  
**Scope:** Durable, revisioned `SetQuantity` operation batches, replay recovery, and baseline/movement safety metadata. API-05C submission, API-05D reconciliation/history, Flutter changes, and ERP approval/posting are out of scope.

## Delivered contract

Base route:

```text
/api/mobile/v1/stock-take/sessions
```

| Method and route | Purpose |
|---|---|
| `POST /{sessionId}/operations:batch` | Apply or durably reject up to 100 `SetQuantity` operations as one revision-checked batch |
| `GET /{sessionId}/operations?afterRevision={revision}` | Read accepted operations after a server revision; returns at most 500 items per page |
| `GET /{sessionId}/operations/{clientOperationId}` | Recover the durable receipt for one accepted or rejected operation |

Batch request:

```json
{
  "deviceSessionId": "device-session-opaque-id",
  "expectedRevision": 0,
  "operations": [
    {
      "clientOperationId": "4dc8e34a-bdc2-4eab-aafe-8195c41898e1",
      "clientSequence": 1,
      "productVariantId": 9104,
      "countedQuantity": 2.5,
      "rawBarcode": "0000009876",
      "clientObservedAt": "2026-10-15T12:00:00Z"
    }
  ]
}
```

Accepted response:

```json
{
  "success": true,
  "message": "Operation successful",
  "data": {
    "currentRevision": 1,
    "errorCode": null,
    "receipts": [
      {
        "clientOperationId": "4dc8e34a-bdc2-4eab-aafe-8195c41898e1",
        "deviceSessionId": "device-session-opaque-id",
        "clientSequence": 1,
        "productVariantId": 9104,
        "operationType": "SetQuantity",
        "countedQuantity": 2.5,
        "rawBarcode": "0000009876",
        "acceptedRevision": 1,
        "sessionRevisionAtDecision": 1,
        "outcome": "Accepted",
        "rejectionCode": null,
        "isDuplicate": false
      }
    ]
  },
  "errors": null
}
```

History response includes `currentRevision`, `nextRevision`, `hasMore`, and receipt `items`. Continue with `afterRevision=nextRevision` when `hasMore` is true. Only accepted operations appear in this revision feed; rejected decisions are recovered by their stable operation ID.

## Authorization and validation

- Write operations require an authenticated Mobile Business Owner or Sales Manager (including existing `Manager` / `OutletManager` role mapping) and `StockCount.Upload`.
- Receipt reads require the same supported role and `StockCount.ViewOwn` or `StockCount.ViewAll`.
- Salesperson, Cashier, role-switched principals, SuperAdmin/global context, and unauthorized roles are denied. Existing ERP permissions are not revoked or changed.
- Business and Outlet are derived from the authenticated server context and `IUserOutletAccessService`. Session scope is rechecked by the persistence store; unauthorized session IDs return not-found rather than revealing the session.
- The single-Business readiness guard runs before session/operation reads and writes. Missing/conflicting Business readiness fails closed.
- Operations identify the exact session `ProductVariantId`. Product must be active, and the Variant must be present in that session. If `rawBarcode` is supplied, it must exactly match one unique Variant barcode and must not collide with a Product-level barcode. The server does not resolve or substitute a different Variant from a barcode.
- Request envelope: 1–100 operations, unique non-empty operation IDs, unique client sequence values within a batch, non-empty bounded device session ID, and a non-negative expected revision. Receipt-storable decimal input is bounded to `numeric(28,8)`; the existing count line accepts non-negative quantities up to `numeric(18,3)`. Invalid quantity/sequence/Variant/barcode decisions that fit the receipt schema are durably rejected. Inputs that cannot form a safe receipt are rejected as HTTP 400.
- No client-provided role, Business, Outlet, session status, stock baseline, variance, or inventory adjustment is accepted.

## Revision, idempotency and recovery behavior

- PostgreSQL locks the `stock_counts` session row with `FOR UPDATE` for each batch. The current revision is checked while holding that lock.
- Each accepted fresh `SetQuantity` increments the session revision once and stores that revision on its receipt. Explicit quantity `0` is valid.
- A batch with any invalid fresh operation or stale expected revision applies no count-line updates. Rejections are persisted as receipts with stable reason codes; a stale batch returns HTTP 409, while validation rejection returns HTTP 422 with the durable receipt details and `success: false`.
- Identical replay of an operation ID and canonical payload returns the original receipt with `isDuplicate: true` and does not increment the revision. The payload fingerprint binds device-session ID, operation ID, sequence, Variant, operation type, quantity, raw barcode and client-observed timestamp. Reusing an operation ID with a different fingerprint returns HTTP 409 `IDEMPOTENCY_KEY_PAYLOAD_MISMATCH`.
- Unique database constraints enforce `(StockCountId, ClientOperationId)` and `(StockCountId, DeviceSessionId, ClientSequence)`.
- Operation receipt lookup is scoped to the authorized session. A history cursor greater than the server revision returns HTTP 409 `REVISION_AHEAD_OF_SERVER`.
- Legacy ERP Excel count uploads and StockCount lifecycle transitions now lock the same PostgreSQL session row. An upload that changes count data increments `CountRevision` once; unchanged uploads do not. This serializes existing ERP edits/status transitions with Mobile writes without altering Angular/POS route contracts.

## Baseline and movement safety

- Session creation initializes `CountRevision=0` and records UTC timestamps bracketing the current-Variant and inventory reads. The inventory aggregation is one PostgreSQL statement snapshot; the Variant catalogue read is a separate query. These timestamps and `InventoryStatementSnapshot` status do **not** establish one atomic catalogue-plus-inventory snapshot and are not a ledger movement cursor.
- No server-authoritative movement reconciliation is implemented. After the first accepted Mobile count operation, `MovementReconciliationStatus` becomes `Unresolved`.
- ERP StockCount adjustment-draft generation now fails closed for a count with unresolved movement reconciliation. No Stock Take operation writes Inventory or StockLedger; the existing ERP approval and posting workflow remains the only inventory mutation path.
- No baseline stock quantity, variance, movement estimate, or reconciled physical quantity is returned by Mobile DTOs.

## Persistence and implementation

- Added `StockCount.CountRevision`, UTC baseline read-window fields, `BaselineConsistencyStatus` and `MovementReconciliationStatus`.
- Added durable `StockCountOperationReceipt` records with actor, Business/Outlet, device/sequence, payload hash, quantity/barcode, UTC receipt/decision timestamps, accepted/current revisions, outcome, rejection code and request correlation ID.
- Added EF Core mapping/indexes and migration `20261015204044_Api05BMobileStockCountOperations`.
- Added CQRS command/store contracts and the versioned API routes above. Mobile session DTO now exposes revision and safe baseline/reconciliation metadata.
- Audit v2 records receipt insertions. The PostgreSQL HTTP test host uses the production audit change-tracking interceptor.
- ERP Sales, POS, Accounting and Inventory posting formulas/workflows were not changed. No Flutter or Angular source was modified.

## Test and build evidence

Tests use the dedicated disposable PostgreSQL fixture guarded to the local A2 PostgreSQL 16 endpoint; it creates a unique disposable database and applies migrations there. No persistent Development or Production database was accessed.

The focused PostgreSQL HTTP test covers:

- Session starts at revision zero and exposes baseline/reconciliation metadata without baseline quantities or variance.
- Two-operation commit including an explicit zero and an exact unambiguous Variant barcode.
- Exact idempotent replay without another revision increment.
- Reused operation ID with a changed payload returns a conflict.
- Stale revision creates a durable rejection receipt and can be recovered by operation ID.
- Accepted-operation history cursor and current revision.
- Ambiguous Product-level barcode rejection atomically rejects the whole batch and leaves revision unchanged.
- Duplicate client sequence in one batch is rejected before persistence.
- Concurrent writes at the same expected revision serialize: one commits, one receives a revision conflict.
- ERP Excel upload advances the same revision on a meaningful edit and does not advance it for an identical no-op upload.
- Receipt audit rows are written and unresolved movement reconciliation blocks ERP adjustment-draft generation.
- Manager Outlet restrictions, Salesperson/Cashier denial, missing upload permission, role-switch denial, single-Business guard, and existing blind-count response allowlist.

Final validation:

- `dotnet test tests/RetailPOS.IntegrationTests/RetailPOS.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~MobileStockTakePostgresHttpTests|FullyQualifiedName~StockCountCreatePostgresConcurrencyTests|FullyQualifiedName~StockCountAdjustmentGenerationPostgresConcurrencyTests"` — **passed, 4 tests, 0 failed**. The filter runs the new Mobile Stock Take HTTP test and PostgreSQL StockCount concurrency regressions using the dedicated disposable PostgreSQL fixture.
- `dotnet build RetailPOS.slnx -c Release --no-restore` — **succeeded, 0 errors, 19 warnings**. Warnings include existing vulnerable `SixLabors.ImageSharp` package advisories and nullable diagnostics outside this change; no new API-05B compiler warnings were reported.

## Remaining blockers and integration decision

- **Controlled Flutter integration is safe for the API-05B request/response and retry contract only**, provided navigation is limited to Owner/Sales Manager and the client never treats receipt acceptance as inventory posting or approval.
- Full functional Stock Take is **not ready**: Flutter local queue/restart recovery is not covered here; offline catalogue snapshot/revision pinning, session lease/ownership, undo/compensation, unknown-barcode exception workflow, server-authoritative movement reconciliation, submission, job status, ERP review and result/history routes remain absent.
- API-05C durable asynchronous submission and API-05D result/history are not implemented. Do not submit Mobile counts or present them as reconciled/approved/posted.
- The inventory baseline and movement cutoff remain unresolved. Adjustment generation is intentionally blocked for unresolved Mobile counts until the business approves and engineering implements a verified reconciliation policy.
