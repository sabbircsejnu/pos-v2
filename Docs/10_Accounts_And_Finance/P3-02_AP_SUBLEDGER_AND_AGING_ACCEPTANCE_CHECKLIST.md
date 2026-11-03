# P3-02 AP Subledger & Aging Acceptance Checklist

Status: **Accepted**

This checklist records implementation and executable evidence. It does not grant acceptance automatically.

## AP Subledger

- [x] Typed AP subledger API exposes payable documents, supplier, source/document references, recognition and invoice dates, due dates, original amounts, effective and reversed payment allocations, effective and reversed advance utilisations, derived outstanding/status, dispute state, warehouse/source outlet, and source journal reference.
- [x] Authoritative totals use `SupplierPayableDocument`, effective `SupplierPaymentAllocation`, effective `SupplierAdvanceUtilization`, and linked reversal history.
- [x] Legacy `Bill` balances are excluded from authoritative totals and surfaced as unreconciled exceptions.
- [x] Tenant and supplier ownership are constrained through business-scoped payable documents.

## Historical Reconstruction

- [x] `asOfDate` reconstruction uses recognition date, allocation effective date, allocation reversal effective date, utilisation effective date, and utilisation reversal effective date.
- [x] Current mutable payable status is not used for report status or outstanding amount.
- [x] Cross-period evidence exists in `P302ApReportServiceTests.Subledger_ReconstructsBeforeBetweenAndAfterReversalDates`.
- [x] Historical `Open` / `Partial` / `Paid` transitions are proven in `P302ApReportServiceTests.HistoricalStatus_DerivesOpenPartialPaidFromDatedEventsNotCurrentStatus`.
- [x] The migrated supplier ownership exception table is surfaced separately from authoritative AP totals; lightweight providers without the table fall back without distorting totals.

## Aging

- [x] Approved buckets are implemented: Current / Not Due, 1-30, 31-60, 61-90, and 90+ overdue.
- [x] Aging uses persisted due dates and reconstructed outstanding amounts.
- [x] Disputed balances remain included and are separately totalled.
- [x] Evidence exists in `P302ApReportServiceTests.Aging_UsesPersistedDueDateAndReturnsEveryApprovedBucketAndDisputedAmount`.
- [x] Exact due-today, 1, 30, 31, 60, 61, 90, and 91-day boundaries plus supplier-term mutation are proven in `P302ApReportServiceTests.Aging_UsesExactDueDateBoundariesAndPersistedSnapshot`.

## Supplier Statement

- [x] Typed supplier statement API exposes opening balance, new payables, payments, advance applications, reversals, closing balance, source references, journal references, and deterministic rows.
- [x] Opening plus movements equals closing in `P302ApReportServiceTests.SupplierStatement_OpeningPlusMovementsEqualsClosingWithDeterministicOrdering`.

## Reconciliation

- [x] AP subledger and Supplier Payable GL are calculated independently.
- [x] Unapplied Supplier Advance and Supplier Advance GL are exposed through a separate typed reconciliation endpoint.
- [x] Reconciliation differences are returned explicitly and never silently adjusted.
- [x] Clean and intentionally divergent payable reconciliation evidence exists in `P302ApReportServiceTests.Reconciliation_ComparesIndependentSubledgerAndGlAndSurfacesDifference`.
- [x] Clean and intentionally divergent Supplier Advance reconciliation evidence exists in `P302ApReportServiceTests.AdvanceReconciliation_ReconstructsUnappliedBalanceAndSurfacesMismatch`.

## API, Security, and UI

- [x] Typed endpoints exist for AP subledger, AP aging, supplier statement, AP reconciliation, and Supplier Advance reconciliation.
- [x] Dedicated permissions exist for AP subledger, AP aging, supplier statement, and AP reconciliation.
- [x] Routes use the existing permission guard and backend location authorization path.
- [x] Angular AP report surface provides filters, totals, aging values, detail rows, statement movements, reconciliation difference, loading/error/empty states, legacy warning, and print behavior.
- [x] AP routes and finance navigation are wired through the existing Angular conventions.
- [x] HTTP coverage verifies dedicated AP permission enforcement and unauthorized outlet/warehouse rejection; service coverage verifies cross-business payable exclusion.

## Executable Evidence

- Focused AP acceptance tests: 8 passed, 0 failed.
- Complete focused P3-02 acceptance/prerequisite matrix: 52 passed, 0 failed.
- Focused AP/report authorization set after final additions: 9 passed, 0 failed.
- Existing P3-02 prerequisite atomicity tests: payment allocation 3/3, advance utilisation 8/8, payment reversal 7/7, GRN fault injection 1/1.
- Accounting/purchase/supplier regression subset after final acceptance additions: 152 passed, 0 failed.
- Frontend test suite: 38 passed, 0 failed.
- Angular production build: passed.
- Backend Release build: passed.
- Full backend suite: 383 passed, 0 failed; final AP/report authorization rerun passed 9/9.

## Review Decision

- [x] P3-02 ACCEPTED
