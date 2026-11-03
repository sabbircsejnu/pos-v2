# Offline Catalogue and Stock Take Design

**Status:** design proposal only. Existing ERP product/variant and stock-count records remain authoritative; this design adds a mobile projection and durable operation protocol, not a second product or inventory master.

## Identity and catalogue projection

Verified logical relation and proposed mobile projection:

```text
authoritative product / Main Product Code
  -> sellable ProductVariant (stable ERP VariantId)
  -> configured attribute/value pairs (for example colour, size)
  -> one normalized barcode per sellable variant
```

**VERIFIED:** `Product.ProductCode` is documented and used by product creation/variant identifier generation as the master product code; it can serve as SRS Main Product Code without a new column. `ProductVariant.ProductId` is the required parent FK, and `Product.Id` / `ProductVariant.Id` (`long`) are authoritative IDs. Variations/options are represented by `Variation`, `VariationOption`, `ProductVariation`, `ProductVariationSelectedOption` and `ProductVariantOption`; `ProductVariant.Attributes` separately holds JSONB attributes.
**UNKNOWN / BLOCKED:** option joins and JSONB may diverge; product ownership/business visibility is not defined by entity fields; deployed data quality and database migration/index parity are unknown. Product and variant barcode indexes are separate, so cross-table collision is possible. Parent `Product.Barcode` is not a variant identity. Resolve exact barcode only against eligible variant barcodes and return ambiguity instead of an arbitrary match.

**Required data remediation before catalogue export:** profile null/blank ProductCode, duplicate/normalized collisions across both barcode tables, missing/orphan ProductId/VariantId, duplicate variant attributes/options, and JSONB-vs-option-join mismatches. Do not fabricate codes, auto-merge products, or silently pick an identity. Quarantine unresolved rows from barcode resolution and require authorized ERP remediation with audit. Preserve legacy rows for owner-directed handling.

Catalogue fields proposed for mobile: ERP ProductId, ERP VariantId, ProductCode as MainProductCode, display product name, variant display label, attribute/value projection, normalized and raw barcode, eligibility/status, source revision and optional stock-count-only code fields. Exclude sale pricing, cost and expected stock unless separately authorized. Business/outlet eligibility must come from an approved server scope policy; current product entities do not themselves provide tenant keys.

## Sync lifecycle

1. **Scope:** server authenticates user and authorizes business/outlet; client does not select arbitrary tenant scope.
2. **Manifest:** server returns scope ID, catalogue revision, schema version, record count, page size, generated-at and cursor/resume metadata.
3. **Initial snapshot:** client downloads pages into staging tables keyed by target revision. Each page is checksum/ordering validated. A transfer interruption resumes from a server cursor tied to the same revision.
4. **Atomic activation:** after all pages and checksums validate, a single local transaction marks the target revision active and the old revision inactive. An incomplete snapshot never replaces the usable catalogue.
5. **Incremental delta:** request changes after active revision to a declared target revision; apply ordered upserts and tombstones in a transaction. If cursor/revision retention has expired or a gap is detected, abandon delta and perform a full snapshot.
6. **Session pin:** starting a count session stores its exact catalogue revision and server session ID. The active catalogue may advance for later sessions; an active session continues resolving with its pinned revision.
7. **Resume/expiry:** persist sync cursor, target revision and last acknowledged operation. Reconnect with same cursor/session/operation IDs. Expired lease/session requires server-directed resume/reopen, never silent rebasing.

## Proposed Drift model

Tables are projections/queue state, not master records:

| Table | Purpose / key constraints |
|---|---|
| `catalogue_revisions` | Scope, revision, schema version, state (staging/active/retired), page/checksum completion. One active revision per scope. |
| `catalogue_variants` | Revision + ERP VariantId; product ID, main code, searchable labels, attributes JSON, normalized/raw barcode, active/tombstone state. Index normalized barcode and main code; detect duplicate barcode rather than picking a result. |
| `catalogue_sync_state` | Scope, active/target revision, cursor, last successful sync, error state. |
| `count_sessions` | Server session ID, outlet, actor, pinned revision, status, lease/expiry, baseline policy/as-of and local lifecycle. |
| `count_operations` | Append-only client operation UUID, session, sequence, VariantId or unknown barcode, quantity/event, captured time, actor/device, pinned revision, payload hash, upload/receipt/conflict state. Unique client operation ID. |
| `count_receipts` | Server receipt/result, accepted/rejected/conflict reason, acknowledged revision/time and reconciliation references. |
| `unknown_barcodes` | Raw/canonical value, count/session, operator disposition and resolution state; retained as an explicit workflow item. |

Use schema migrations with tested upgrade/downgrade policy; protect sensitive local data, define backup exclusion and wipe-on-revocation behavior. Avoid storing user password or access/refresh tokens in Drift.

## Offline counting semantics

- Each scan/manual count appends a new immutable operation; corrections append a reversal/correction referencing the prior operation instead of overwriting history.
- Capture barcode, resolved ERP VariantId (if any), pinned catalogue revision, count quantity/unit, operator/device, timestamp and operation ID. Quantity is validated server-side again.
- Unknown barcode is captured with raw value and explicit status; the operator may retry, record as unresolved or use an approved manual identification flow. It is never silently discarded or mapped by name similarity.
- Duplicate scan policy is an ADR: default proposal is distinct count events that aggregate deterministically on server, with a visible current total. Retransmitting one event ID is not a second count.
- **Recommended ADR-STK-01:** take a blind baseline at server session opening while ERP inventory remains live; do not lock POS sales or treat stock as frozen. Record movement after baseline. Do not automatically compare an offline observation to a later live balance if its observation time cannot be trusted; route affected variant to reviewer/recount with ledger evidence before adjustment. Example: baseline 20, sale -3 and receipt +8 during the count, resulting stock 25. An offline count of 22 with unknown capture timing must not be automatically posted as -3; reviewer determines whether the observation was before/after movements or requests recount.
- **Recommended ADR-STK-02:** one active device owns the mobile editing lease per session; lease controls mobile editors only, never POS inventory. Lease duration and heartbeat interval are validated configurable policy values, never hardcoded; policy version is recorded per session. Candidate starting values only: 12-hour lease, 5-minute heartbeat, subject to Operations/Inventory approval; enforce heartbeat < lease duration with safety margin. Preserve local operations after expiry but require online reauthorization before submit. No automatic takeover. Supervisor takeover is explicit and marks previous-device unacknowledged operations for conflict review. If old and new devices both counted a variant, never sum blindly or use last-write-wins; reconcile operation histories.
- **Recommended ADR-STK-03:** session pins full outlet/category/explicit SKU scope and its catalogue revision. In partial scope, absent variant means “not in scope”, never zero. In declared full scope, absence can become proposed zero only after explicit completion and authorized ERP reconciliation; omitted expected items remain unresolved until reviewer disposition.
- **Recommended ADR-STK-04:** immutable operation log with client UUID and payload hash; exact replay returns original receipt, same UUID/different payload conflicts. Submission requires online authentication, all operations acknowledged/resolved, expected session revision match and an idempotent submit receipt. Corrections use authorized ERP reopen/recount, never rewrite accepted evidence. Keep operation and receipt evidence through finalization and at least the approved audit/legal retention period; do not invent a fixed duration before policy approval.
- Offline device identity is informational, not an authorization substitute. Session lease limits, user deactivation, token expiry and remote revoke behavior must be approved.

## Upload, idempotency and reconciliation

1. Submit ordered batches with the same operation IDs and stable payload hashes on every retry.
2. Server transaction inserts operation receipt and applies valid count evidence to the existing StockCount aggregate; exact replay returns prior result.
3. Reuse of an ID with changed payload returns conflict and an audit event.
4. Response is per operation: accepted, duplicate/replayed, rejected (validation), unresolved barcode, or conflict, with stable reason and server reference.
5. Client persists response before marking operation complete. A timeout is an unknown outcome; query receipt/retry with the same ID.
6. Server returns session state, accepted count, unresolved/conflicted operations and authoritative reconciliation summary.
7. User resolves eligible conflicts or requests an approved reopen. Server approval and stock adjustment/ledger posting remain ERP-only. (Implemented API-05D exposes read-only lifecycle, evidence status and approved results; it resolves nothing.)
8. Retain operation/receipt evidence through finalization and the approved audit/legal retention period, with explicit purge policy. Never report local queued counts as server-posted stock.

## Exports and integrity

**VERIFIED:** existing Excel stock-count output has Product Name, Product Code, Variant, Current Stock, Physical Count, Difference and Remarks; `print` returns a DTO.  
**UNKNOWN:** official PDF rendering, distinct Main Product Code/Barcode columns and dynamic attribute rows are not established.

**PROPOSED:** extend the ERP-authoritative export after review, with approved Main Product Code, product, attributes, barcode, counted quantity, discrepancy/status and audit/session metadata. PDF and Excel must derive from the same reconciled server data; mobile-local export may be a clearly labelled working copy and must not look like an approved adjustment.

Integrity checks include stable unique product/variant IDs, canonical barcode collision report, revision consistency, atomic activation, payload hash validation, one operation ID→one payload, session scope checks, optimistic concurrency and ERP-side reconciliation against the stock ledger.

## Acceptance tests and failure cases

- Full snapshot and delta produce identical active projections at revision R.
- Tombstone removes a variant from new sessions but pinned session R still resolves it.
- Duplicate barcodes are surfaced as ambiguity; no arbitrary winner.
- Interrupt every page/batch boundary, restart app and resume without data loss or duplicate operations.
- Retry identical operation many times; exactly one durable count effect and stable receipt.
- Reuse operation ID with altered payload; reject and audit.
- Submit simultaneous sessions/devices; enforce approved lease/baseline/concurrency policy.
- Expired cursor, expired session, revoked user, scope change, unknown barcode and local database corruption all produce explicit recoverable states.
- Cross-business/outlet attempts are denied even if a client modifies stored IDs.
