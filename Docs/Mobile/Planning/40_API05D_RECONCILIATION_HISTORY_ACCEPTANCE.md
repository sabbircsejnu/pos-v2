# API-05D Mobile Stock Take Reconciliation Status, Results and History Acceptance

**Status:** API-05D implemented and PostgreSQL-tested. **PARTIALLY PASSED** — see [Remaining blockers](#remaining-blockers-and-verdict).  
**Scope:** read-only history, lifecycle, submission-job history, deterministic movement-evidence status and approved final results. Out of scope: Mobile approval/rejection, adjustment generation, inventory/ledger posting, a device lease, a merge policy, migrations, Notifications, Forecasting, Flutter/Angular changes, Docker/infrastructure changes.

## Delivered contract

All routes are under `/api/mobile/v1/stock-take/sessions`, `GET`, read-only.

| Route | Purpose |
|---|---|
| `GET history` | Paged history (max page size 100, newest created first) with ERP status, lifecycle `stage`, latest submission job and the persisted ERP reconciliation flag. Filters: `outletId`, `status`, `submissionStatus` (latest job), `search`, `createdFrom`/`createdTo` (UTC) |
| `GET {sessionId}/lifecycle` | Four independent facts plus results availability (below) |
| `GET {sessionId}/submissions` | Paged durable submission-job history, newest first (`page`, `pageSize`) |
| `GET {sessionId}/results` | Approved final results (`differencesOnly`, `page`, `pageSize`); `NotAvailable` / `Withheld` / `Available` |

Existing routes reused unchanged in contract: session list/detail/lines, operation history/receipt, submission status (`.../submissions/{jobId}`).

### Lifecycle stage (`stage`)

`Counting`, `SubmissionInProgress` (active job), `SubmissionFailed` (latest job `Failed` for the current revision), `SubmittedForErpReview`, `RejectedByErp`, `ApprovedByErp`, `AdjustmentDraftGenerated`, `Completed`. `erpStatus` is the authoritative ERP `StockCount.Status`.

### Four distinct facts (`lifecycle`)

| Fact | Field | Values / meaning |
|---|---|---|
| Submission | `latestSubmission`, `submissionJobCount` | API-05C job state; `ErpSubmitted` means only that the ERP submission succeeded |
| Reconciliation | `reconciliation.erpStatus` | Persisted ERP flag (`NotStarted` / `Unresolved`). Never changed by Mobile; `resolvesErpReconciliation` is always `false` |
| | `reconciliation.evidenceStatus` | `NotEvaluated` (Draft/Rejected), `Blocked`, `Unresolved`, `NoConflictDetected` |
| Approval | `approval.state` | `NotSubmitted`, `PendingReview`, `Approved`, `Rejected` (+ decision time; `hasRejectionReason` only) |
| Inventory adjustment | `inventoryAdjustment` | `NotGenerated` or the ERP `StockAdjustment` status; `isInventoryPosted` only when the ERP adjustment is `Approved`/`Reversed`; `draftBlockedByErpReconciliation` for an Approved count still flagged `Unresolved` |

## Deterministic movement evidence

Evidence uses only existing records: `StockCount` baseline timestamps (`BaselineInventoryReadStartedAtUtc`/`CompletedAtUtc`, `InventoryStatementSnapshot`), line baseline (`CurrentStock`) and count, accepted operation receipts, `StockLedger` (`CreatedAt`, `MovementSequence`, `BalanceAfter`, transaction type) and `Inventory`. It is evaluated only for Submitted/Approved/AdjustmentGenerated/Completed sessions, per counted line:

| Check | Outcome |
|---|---|
| Baseline status not `InventoryStatementSnapshot`, or timestamps missing/inverted | `Blocked` `BASELINE_NOT_VERIFIABLE` |
| Submitted session without a submission time; no counted lines | `Blocked` `COUNT_SUBMISSION_TIME_MISSING` / `NO_COUNTED_LINES` |
| Non-zero baseline with no preceding ledger row | `Blocked` `BASELINE_LEDGER_EVIDENCE_MISSING` |
| Last ledger balance before the baseline read ≠ baseline quantity | `Blocked` `BASELINE_LEDGER_MISMATCH` |
| Sequence not strictly increasing, or a later sequence with an earlier creation time (backdated/late row) | `Blocked` `MOVEMENT_ORDER_ANOMALY` |
| `BalanceAfter` chain broken | `Blocked` `MOVEMENT_BALANCE_CHAIN_BROKEN` |
| Final ledger balance ≠ current `Inventory` quantity (movement missing from ledger) | `Blocked` `INVENTORY_LEDGER_MISMATCH` |
| Ledger row created from baseline read start up to the latest possible observation instant | `Unresolved` `MOVEMENT_DURING_COUNT_WINDOW` |
| Otherwise | `NoConflictDetected` |

The latest possible observation instant is the server receipt time of the accepted Mobile receipt whose quantity equals the stored count, capped at ERP submission; without such a receipt (for example ERP Excel upload) ERP submission time is used. Client observed time is untrusted and not used. Rows after that instant are counted as `postCountMovementCount` and do not conflict (the ERP draft applies the delta to current stock). The summary reports **row counts only** by category (sales, returns/exchanges, transfers, adjustments, goods receipts, other); no quantities.

This is advisory evidence. It does not resolve the ERP flag, approve, adjust or post, and it does not invent movements, merge policies or device-lease rules. Backdated *business dates* do not hide a movement because the creation time is evaluated; an explicitly backdated creation time or reused sequence is caught by the ordering check.

## Approved results and blind counting

`results.availability`:

- `NotAvailable` — Draft, Submitted or Rejected (`STOCK_TAKE_NOT_APPROVED`); no stock, baseline or variance is read.
- `Withheld` — ERP-approved state but approval time missing, lines uncounted or stored differences inconsistent, or evidence not `NoConflictDetected`; reasons list the codes. No lines or totals.
- `Available` — ERP `Approved`/`AdjustmentGenerated`/`Completed` with recorded approval and `NoConflictDetected`: `countedQuantity`, `baselineQuantity`, `difference` per line plus `countedLineCount`, `differenceLineCount`, `netDifference`.

Lifecycle, history, submission and non-approved results payloads contain no baseline/expected quantity or variance. Reviewer rejection text is withheld because it may disclose variance; only `hasRejectionReason` is returned.

## Authorization

- Business Owner and Sales Manager mappings only (`BusinessOwner`, `SalesManager`, `OutletManager`, `Manager`); Salesperson, Cashier, unauthenticated/unsupported roles, role-switched sessions, SuperAdmin/global context, foreign Business and missing permission → `403`. Single-Business readiness guard runs before data access.
- `StockCount.ViewAll` sees all sessions in the caller's authorized Outlets; `StockCount.ViewOwn` sees only sessions the caller created or submitted (applied to all Mobile Stock Take read routes, including previously delivered ones). An unresolvable identity matches nothing.
- Outlet filter outside the caller's authorized set → `403`; session outside the authorized Outlets (or not own under ViewOwn) → `404 STOCK_TAKE_NOT_FOUND`.
- Errors: `400 INVALID_STOCK_TAKE_FILTER` / `INVALID_STOCK_TAKE_PAGINATION` / `INVALID_STOCK_TAKE_ID`. No Mobile approve/reject/adjust/reconcile/post route exists (POST probes return `404`/`405`).
- No client-supplied Business, Outlet or role is trusted.

## Persistence and safety

No migration or persistent-database change. Implementation: `IMobileStockTakeReviewStore`/`MobileStockTakeReviewStore` (no tracking, no locks), pure `MobileStockTakeReconciliationEvaluator`/`MobileStockTakeResultsPolicy`/`MobileStockTakeLifecycleStageResolver` in Application, `MobileStockTakeController` endpoints and DTOs. Normal POS checkout is untouched; a test proves lifecycle reads and a POS-style ledger/inventory write complete while the session row is locked.

## Test and build evidence

All HTTP runs used the dedicated disposable PostgreSQL 16 fixture (`127.0.0.1:55432`); Development (5435) was not touched.

| Run | Result |
|---|---|
| `MobileStockTakeReconciliationEvaluatorTests` (pure) | **32 passed, 0 failed** |
| `MobileStockTakeReviewPostgresHttpTests` | **15 passed, 0 failed** |
| Focused regression: `MobileStockTake*` (API-05A/B/C/D), StockCount create/adjustment-generation concurrency, `MobileSalesDashboardPostgresHttpTests`, `MobileInventoryPostgresHttpTests`, `StockCountPhase1HttpTests` | **96 passed, 0 failed** |
| `dotnet build RetailPOS.slnx -c Release --no-restore` | **Succeeded, 0 errors, 15 warnings** (existing `SixLabors.ImageSharp` advisories/nullable diagnostics) |

The HTTP tests cover: pagination, newest-first order and lifecycle/submission/date/outlet filters with invalid-filter `400`s; Sales Manager Outlet restriction, Outlet/session concealment, Salesperson/Cashier/missing-permission/role-switched/foreign-Business/unauthenticated denial; ViewOwn versus ViewAll on new and existing routes; Draft/Submitted blindness (no baseline, expected or variance fields) and separation of submission, reconciliation, approval and adjustment; Rejected text withholding; failed/retried job history; approved clean results with posted-inventory/ledger unchanged and ERP adjustment draft still failing closed; movement during the count window withheld as `Unresolved`; six blocked-evidence scenarios (no ledger, ledger/baseline mismatch, unledgered inventory change, broken chain, reused sequence, unverified baseline); read-only behaviour, absence of write routes and no lock interference with POS-style movements.

## Remaining blockers and verdict

- **ERP reconciliation resolution is undefined.** Nothing in the codebase sets `MovementReconciliationStatus` to a resolved value, so ERP adjustment-draft generation remains blocked for every Mobile-counted session. API-05D reports evidence and withholds results conservatively; it does not and must not resolve the flag. An approved ERP policy (and owner/permission) for resolution is required.
- Sessions whose outlet inventory has no `StockLedger` evidence (e.g. seeded or legacy stock with non-zero baseline) will be `Blocked` (`BASELINE_LEDGER_EVIDENCE_MISSING`) and results withheld until ledger evidence exists; this is intentional.
- Ledger evidence is limited to integer quantities and Outlet-level locations as stored by the existing ledger; fractional counts and rows written outside the ledger cannot be proven.
- **Live RabbitMQ is still not exercised** (API-05C); no device lease, takeover or merge policy was invented; catalogue revision pinning remains absent.
- Flutter may show history, stage, submission status, evidence reason codes and results only when `results.availability == "Available"`; never present `Accepted`/`ErpSubmitted`/`NoConflictDetected` as approved, reconciled or posted stock.

**Verdict: PARTIALLY PASSED** — API-05D read surface is accepted for controlled integration; final stock outcomes remain gated by the ERP reconciliation-resolution policy.
