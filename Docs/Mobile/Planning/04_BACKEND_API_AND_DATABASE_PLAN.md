# Backend API and Database Plan

**Status:** all new routes, DTOs, tables, and changes below are **PROPOSED**, not existing functionality. Build within the current modular .NET API/Application/Domain/Infrastructure architecture. Reuse existing authoritative aggregates and services.

## API principles

1. Add a versioned mobile surface, proposed prefix `/api/mobile/v1/`, with explicit DTOs and application handlers. Confirm path/versioning convention with an ADR before coding.
2. Reuse existing auth/security and reporting/product/stock-count services where their semantics meet the mobile contract; do not expose EF entities.
3. Require authenticated user, business and authorized outlet context on every scoped query/command. Validate scope in Application/API policy and test cross-tenant/outlet denial.
4. Preserve existing `long` product/variant identity unless approved migration analysis justifies a compatible stable external ID. Do not copy LLD UUID examples into live contracts by assumption.
5. Use UTC instants, explicit business time zone for date boundaries, bounded page sizes, stable ordering, correlation IDs, API error envelopes and optimistic concurrency tokens.
6. Use additive migrations and backward-compatible response evolution; do not modify unrelated HR/payroll tables or behavior.

## Proposed endpoint groups

| Proposed contract | Behavior | Existing capability/reuse |
|---|---|---|
| `GET /api/mobile/v1/me` | Mobile identity, roles/permissions, authorized businesses/outlets and current context | Existing auth `me`, role claims, tenant/outlet access services. |
| `POST /api/mobile/v1/session/refresh` or existing refresh endpoint | Refresh/revoke native session according to approved public-client design | Existing refresh rotation; route strategy depends on auth ADR. |
| `GET /api/mobile/v1/catalogue/manifest` | Current scope, revision, counts, schema version and snapshot metadata | New projection over authoritative Product/ProductVariant/attribute data. |
| `GET /api/mobile/v1/catalogue/snapshot?cursor=...` | Stable, bounded snapshot pages at one revision | New; include only fields required for lookup/count. |
| `GET /api/mobile/v1/catalogue/changes?afterRevision=...&cursor=...` | Ordered upserts and tombstones through a declared target revision | New; cursor expiry instructs full resnapshot. |
| `POST /api/mobile/v1/stock-take/sessions` | Create/resume a server-authorized count session and pin revision/baseline policy | Adapt existing StockCount lifecycle; do not create a parallel stock master. |
| `POST /api/mobile/v1/stock-take/sessions/{id}/operations:batch` | Accept idempotent count operations and return per-operation accepted/rejected/conflict receipt | New protocol; routes accepted data through current StockCount/adjustment workflow. |
| `GET /api/mobile/v1/stock-take/sessions/{id}/operations?after=...` | Recover receipt/status after timeout or process loss | New durable receipt/status contract. |
| `POST /api/mobile/v1/stock-take/sessions/{id}:submit` | Submit a complete session for ERP review/approval | Adapt current submit/approval lifecycle; enforce session state and completeness. |
| `GET /api/mobile/v1/dashboards/{role}` | Role-filtered server-owned metrics and freshness metadata | Compose existing sales reports and authorized accounting metrics. |
| `GET /api/mobile/v1/insights/sales` | Authorized sales insights with metric definitions and provenance | Reuse sales/report queries; add only approved aggregates. |
| `GET /api/mobile/v1/forecasts/sales` | Versioned statistical prediction, horizon and uncertainty metadata | New bounded context/module, initially in-process. |
| `GET /api/notifications` and `PATCH /api/notifications/{id}/read` (or equivalent versioned routes) | Shared scoped inbox/read-state API | New shared module; Angular and Flutter clients use same notification records. |
| Angular notification configuration APIs | CRUD/enable/disable and preview/test policy; authorize admin roles | New Angular/API capability. |

Exact route names and verbs are contract-review items; these are not promises of existing endpoints.

## Proposed persistence changes

### Catalogue

- Do not create a duplicate product/variant master or a new `MainProductCode` field. Map `Product.ProductCode` to Main Product Code; preserve `Product.Id`/`ProductVariant.Id` (`long`) and the required `ProductVariant.ProductId` relationship.
- Before enabling catalogue export, approve catalogue visibility because inspected Product/Category entities do not directly carry business/outlet ownership. Profile null/blank legacy ProductCode values and cross-table product/variant barcode collisions.
- Resolve scan input only to one eligible variant barcode in the authorized scope. Product-level `Product.Barcode` is not an exact sellable variant identifier; separate unique indexes do not prevent a Product barcode from matching a ProductVariant barcode. The mobile projection must surface such ambiguity rather than selecting a match.
- Define whether variation-option joins or JSONB `ProductVariant.Attributes` are canonical for mobile projection and reconcile mismatches before data is considered ready.
- **PROPOSED only after ADR:** add a maintained normalized catalogue revision/change log or database change feed with monotonically ordered revisions, tombstones and retention. Avoid calculating deltas from `updated_at` alone if writes can miss related attribute/barcode changes.
- A snapshot must be repeatable at one declared revision. Every page carries that revision; paging must not mix concurrent updates.
- If persisted projection is chosen for performance, treat it as a rebuildable read model, not authority. Include source product/variant IDs and source version.

### Stock take

- Prefer extending the existing StockCount domain/line protocol to retain mobile origin and immutable operation evidence. Do not introduce a second count/adjustment lifecycle.
- Candidate persistence: session pin (catalogue revision, scope, baseline/as-of and lease), operation receipts with unique `(SessionId, ClientOperationId)`, stable payload hash, server result/status, received time, and conflict reason. Define retention and PII before migration.
- Enforce uniqueness in PostgreSQL and perform receipt insert plus count-line/workflow changes transactionally. Identical ID + payload returns original receipt; same ID + different payload is a conflict and is audited.
- Existing stock-adjustment/ledger remains the sole posting path. Mobile submit cannot create ledger entries directly.

### Notifications

- Extend existing durable integration event infrastructure only after verifying source-write transaction boundaries. Add a shared notification registry/rule/version model, recipient/read state, deduplication key, and delivery/processing state as required by approved design.
- Store durable event intent in the same database transaction as the relevant business change. Delivery fan-out/worker retries must not duplicate inbox items.
- Preserve business/outlet/user scope on every row and query. Define rule edit audit, expiry/retention, retry/dead-letter/redrive authorization.

### Dashboard and forecasting

- Start with query/read models over current Sale/SaleItem/report services. Cache only after measured latency and clear invalidation/freshness semantics.
- Forecast history/projections must not be stored in sale or inventory master tables. Store run/model/horizon, generated time, training window and evaluation metadata separately if persistent forecasts are approved.

## Security, audit and operational controls

- Reuse auth claims and access services, but prove every new handler applies them; do not rely on client context or route guards.
- Use explicit read/command permissions for cost/profit, forecast, notification configuration, count approval and export.
- Record actor, business, outlet, operation/session IDs, correlation ID and reason for privileged state changes and replays. Redact tokens and secret values.
- Rate limit login, catalogue endpoints, batch size and notification configuration; cap page/batch/body sizes; validate schema and barcode input server-side.
- Use idempotency and concurrency tokens for writes. Audit denied scope attempts without leaking resource existence.
- Outbox worker must expose queue age, attempts, dead-letter count, retry outcome and redrive authorization; alert on sustained backlog.

## Required verification before migration

1. Inspect all current migrations and deployed schema lineage; reconcile the stale baseline documentation.
2. Profile product, variant, barcode, variation and tenant data; quantify duplicates/nulls/orphans and legacy formats.
3. Prove sale notification event writer and business mutation share one EF transaction before relying on the outbox.
4. Confirm indexes/query plans for catalogue page/delta and outlet-scoped dashboard queries using representative data.
5. Approve API compatibility/versioning, payload retention and old-client support period.

## Tests and acceptance

- Unit tests for handlers, DTO mapping, metric policy, delta construction, tombstones and event rule evaluation.
- Integration tests for PostgreSQL uniqueness, transaction rollback, cross-scope denial, operation replay/conflicting payload, concurrent submission and revision-stable pagination.
- Contract tests for auth claims, API schema compatibility, pagination and error semantics.
- Existing regression baseline: .NET build, targeted integration tests, full integration suite before merge, and Angular build/tests when shared services or routes change. Exact commands are in `07_PHASED_DEVELOPMENT_PLAN.md`.
