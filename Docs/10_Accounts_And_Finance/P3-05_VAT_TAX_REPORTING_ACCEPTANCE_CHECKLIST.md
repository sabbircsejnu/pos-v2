# P3-05 VAT / Tax Reporting Acceptance Checklist

Status: **Accepted**

## Implemented

- [x] Typed VAT Summary API with Output VAT, Input VAT, net VAT position, payable/receivable status, rate/class breakdown, and exception count.
- [x] Typed VAT transaction detail API using persisted source snapshots and accounting posting dates.
- [x] Typed Output/Input/combined reconciliation API with independent JournalEntryLine GL calculations and signed differences.
- [x] Typed VAT exception API for incomplete sales VAT snapshots.
- [x] Dedicated `accounting.reports.vat` permission, guarded routes, SQL seed entries, and server-side location authorization.
- [x] Angular VAT Summary, Details, Reconciliation, and Exceptions pages with period filters, totals, differences, warnings, empty/error/loading states, and print behavior.
- [x] P3-05 prerequisite migration `20260905225627_AddVatPrerequisiteSnapshotsAndInputVatAccount` remains additive and nullable; no historical tax data is fabricated.

## Source and historical behavior

- [x] Sales, GRN items, Expenses, and Other Income now carry nullable immutable VAT snapshots.
- [x] Report calculations use snapshot data and posted journal dates rather than current product/system tax settings.
- [x] Existing sale/refund/void output VAT posting remains linked to Journal Engine history.
- [x] Later-period sale reversal event, original-period preservation, negative sign, and original/reversal linkage are covered by `P305VatReportAcceptanceTests`.
- [x] Incomplete legacy snapshots are surfaced through VAT exceptions and excluded from authoritative snapshot totals.

## Reconciliation

- [x] Output VAT report and VAT Payable GL are calculated independently.
- [x] Input VAT report and VAT Receivable GL are calculated independently.
- [x] Combined reconciliation exposes output, input, net, and overall differences separately.
- [x] Dedicated clean/mismatch reconciliation fixtures pass in `P305VatReportAcceptanceTests`.

## Validation evidence

- Existing focused VAT/sale/GRN/Expense/Other Income/configuration tests: 17 passed, 0 failed.
- Focused VAT/report/security/AP coverage after reporting implementation: 28 passed, 0 failed.
- Dedicated VAT acceptance fixtures: 4 passed, 0 failed, covering clean/mismatch reconciliation, reversal periods, legacy exclusion, and outlet isolation.
- Full backend suite after final VAT evidence changes: 387 passed, 0 failed.
- Frontend suite after final VAT evidence changes: 38 passed, 0 failed.
- Full backend suite before final VAT reporting additions: 383 passed, 0 failed.
- Frontend suite before final VAT reporting additions: 38 passed, 0 failed.
- Backend Release build: passed.
- Angular production build: passed.
- P3-05 reporting frontend production build: passed.
- Existing `NU1903` SQLitePCLRaw advisory remains non-blocking.

## Final closure evidence

 - [x] `ExpenseService.ApproveAsync` now wraps journal posting, Expense state/linkage, audit, and commit in one ReadCommitted transaction; `P305ExpenseVatAtomicityTests` proves fresh-context rollback after journal persistence and exact retry with NET/Input VAT/GROSS lines.
 - [x] GRN uses its existing encompassing transaction and `P302RelationalFaultInjectionTests` proves source/journal/AP/inventory rollback; Other Income uses its existing encompassing transaction and `OtherIncomeAtomicityTests` proves journal/source rollback. No unnecessary transaction refactor was made.
 - [x] `FinancialReportsHttpTests.VatReports_RequirePermissionAndRejectUnauthorizedLocations` covers all VAT endpoints for missing permission, unauthorized outlet/warehouse, and cross-business non-OK responses.
- [x] Final focused suite including `P305ExpenseVatAtomicityTests`: 27 passed, 0 failed.
- [x] Full backend suite: 389 passed, 0 failed.
- [x] Frontend suite: 38 passed, 0 failed.
- [x] Backend Release build passed.
- [x] Angular production build passed.
- [x] Focused P3-05 suite: 27 passed, 0 failed.
- [x] Full backend suite: 389 passed, 0 failed.
- [x] Frontend suite: 38 passed, 0 failed.
- [x] Backend Release build passed.
- [x] Angular production build passed.

## Decision

- [x] P3-05 ACCEPTED
