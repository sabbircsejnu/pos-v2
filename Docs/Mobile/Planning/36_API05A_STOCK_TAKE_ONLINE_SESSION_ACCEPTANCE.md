# API-05A Mobile Stock Take Online Session Acceptance

**Scope:** Online session list, detail/resume, paged count-line reads and creation  
**Status:** **API-05A foundation implemented**; the complete Mobile Stock Take workflow is not ready  
**Architecture:** Single-Business / Multi-Outlet; existing ERP StockCount remains authoritative

## Implemented routes

| Method and route | Behavior |
|---|---|
| `GET /api/mobile/v1/stock-take/sessions` | Returns a bounded page of sessions in the authenticated Business and server-authorized Outlets. Optional filters: `outletId`, `status`, `search`, `page`, `pageSize`. |
| `GET /api/mobile/v1/stock-take/sessions/{sessionId}` | Reads an authorized session's persisted reference, Outlet, date, lifecycle status and permitted action metadata. An inaccessible or missing ID returns `404 STOCK_TAKE_NOT_FOUND`. |
| `GET /api/mobile/v1/stock-take/sessions/{sessionId}/lines` | Returns a bounded page of product/variant identity and persisted counted quantities. It does not return expected/current quantity or variance. |
| `POST /api/mobile/v1/stock-take/sessions` | Creates an ERP StockCount through the existing `CreateStockCountCommand`; only an authorized Outlet and the existing full-location count behavior are accepted. Returns `201 Created`. |

Pages are 1-based, page size is at most 100, and ordering is stable. Invalid paging/status/filter values return HTTP 400 with a stable error code. Request Business, role, user and location type are not accepted as authority. The create body is:

```json
{
  "outletId": 5101,
  "stockCountDate": "2026-10-15",
  "remarks": "Monthly count"
}
```

Example successful session response:

```json
{
  "success": true,
  "message": "Operation successful",
  "data": {
    "id": 9001,
    "reference": "SC-20261015-0001",
    "scope": {
      "locationType": "outlet",
      "locationId": 5101,
      "locationName": "Authorized outlet"
    },
    "stockCountDate": "2026-10-15",
    "status": "Draft",
    "remarks": "Monthly count",
    "totalItems": 5,
    "createdAt": "2026-10-15T17:30:00Z",
    "submittedAt": null,
    "revision": null,
    "permittedActions": ["resume"]
  }
}
```

`permittedActions: ["resume"]` for a Draft means the client may re-fetch the authorized session and its pages. It is not a server start transition, ownership lease, lock, count mutation or submission. Other lifecycle states expose no Mobile action in this slice. The `revision` property is explicitly null: the persisted StockCount model has no revision value, so API-05A does not fabricate one. `scope` describes only the persisted Outlet location, not a revisioned SKU scope or closure/completeness declaration.

## Authorization and data scope

- Only authenticated Business Owner and Sales Manager role mappings are accepted: `BusinessOwner`, `SalesManager`, `OutletManager`, and `Manager`.
- Reads require existing `StockCount.ViewOwn` or `StockCount.ViewAll`; creation requires `StockCount.Create`. Existing ERP permissions are not changed.
- Salesperson, Cashier, unsupported roles, role-switched sessions, SuperAdmin/global contexts and missing Business context are denied.
- `IMobileSingleBusinessReadinessGuard` runs before Outlet resolution or StockCount data access. Outlet access is derived from `IUserOutletAccessService.GetAuthorizedOutletsForBusinessAsync`; Business Owners receive their authorized Business Outlets and Sales Managers only their assigned/authorized Outlets.
- The read store independently filters by the authenticated Business, `LocationType = outlet`, actual Outlet ownership and the server-resolved authorized Outlet IDs. Direct-ID access outside this scope returns 404 rather than disclosing session existence.
- No Mobile approval, adjustment-generation, accounting or inventory-posting route is added.

## Entity and workflow mapping

The implementation uses the existing `StockCount` and `StockCountLine` entities, not a second mobile stock master:

- Session: `Id`, `StockCountNo`, `BusinessId`, `LocationId`, `LocationType`, `StockCountDate`, `Status`, `Remarks`, `TotalItems`, `CreatedAt` and `SubmittedAt`.
- Scope: persisted `LocationId`/`LocationType`; the API returns an Outlet name resolved only after server authorization.
- Line: `Id`, `ProductId`, `VariantId`, product/variant display identity, `PhysicalStock` as `countedQuantity`, and line remarks.
- The API does **not** project `CurrentStock` or `Difference` into its Mobile read models or DTOs. The integration test asserts these and `expectedQuantity` are absent.
- Create calls the established `CreateStockCountCommand` and `StockCountCreationStore`. That store creates a Draft, enumerates variants whose Product status is Active, snapshots Outlet inventory into `CurrentStock`, initializes `PhysicalStock` and `Difference` to null, and persists the existing actor/time fields. API-05A does not change this ERP behavior.
- Creation currently supports only the existing location-wide Outlet count; there is no persisted category/SKU scope, accepted count revision, baseline movement policy, server count-operation receipt or session lease.

**Deployment caveat:** Product/Category/Variant ownership remains deferred. The runtime guard establishes one active Business and that Outlets belong to it, but it cannot prove the provenance of global Product/Variant rows. Because the existing count creator enumerates all active Product variants, a target deployment must verify its catalogue data provenance before controlled Mobile session creation. Do not interpret the guard alone as proof of catalogue provenance.

## Resume, concurrency and submission boundary

The GET detail/lines routes permit a client to restore a Draft session's persisted information, but no Mobile count write or offline synchronization is implemented. API-05A therefore does not establish concurrent-device behavior, immutable accepted-count revision, conflict detection, offline operation idempotency or movement reconciliation.

The Product Owner-approved asynchronous submission architecture is recorded in the SRS requirement STK-013A and LLD section 8.3. In the future API-05C, HTTP 202 must follow one PostgreSQL transaction that persists a durable job, session transition/lock and transactional-outbox event. An outbox dispatcher and bounded worker use RabbitMQ only as transport, revalidate API-05B's immutable accepted-count revision, and call the existing ERP submission workflow while preserving the original audit actor. HTTP 202 means durable acceptance only—not ERP submission, approval or posting. API-05B must precede API-05C. No async submission/job/outbox/worker capability is part of API-05A.

## Verification

| Check | Result |
|---|---|
| API-05A disposable PostgreSQL HTTP authorization/contract test | **Passed: 1 test, 0 failures** |
| API-05A plus existing StockCount regression filter | **Passed: 36 tests, 0 failures** |
| Integration test project Release build | **Passed: 0 errors** |
| Full Release solution build | **Passed: 0 errors, 15 warnings** |
| Whitespace checks | **Passed for tracked and added API-05A files** |
| Frontend and database safety | No Flutter or Angular files changed; test fixture verifies the dedicated isolated PostgreSQL 16 target and uses a unique disposable database |

The API-05A HTTP test exercises Owner create/list/detail/line reads, Sales Manager creation and assigned-Outlet restriction, unauthorized direct-ID concealment, Salesperson/Cashier denial, missing permissions, role-switch denial, pagination/filter validation, invalid creation, absence of a Mobile approval route, blind-count field exclusions and the single-Business guard. The Release builds report existing ImageSharp `NU1902`/`NU1903` advisories; package versions were not changed.

## Readiness and next slice

**Ready only as an online session foundation** for a deployment that passes the guard and has verified catalogue provenance. It is not a complete Stock Take integration contract and is not ready for scan/count workflow integration:

1. API-05B must define durable count operations, idempotent receipts, conflict behavior and the authoritative accepted-count revision. Product Owner decisions are still required for baseline/movement reconciliation, count scope/completeness and concurrent-device lease or merge behavior.
2. API-05C implements the approved durable asynchronous submission after API-05B.
3. API-05D adds authorized review/reconciliation status reads if required.

No later API slice is started by this acceptance.
