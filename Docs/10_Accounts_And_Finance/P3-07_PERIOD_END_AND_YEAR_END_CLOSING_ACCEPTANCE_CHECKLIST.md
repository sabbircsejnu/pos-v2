# P3-07 Period-End and Year-End Closing Acceptance Checklist

Status: Accepted
Date: 2026-09-06

## Controlled Period Close

- [x] Tenant-scoped, versioned Accounting Period close runs preserve preparation, approval, execution, reopen, and prior evaluation history.
- [x] Gate snapshots are append-only and distinguish blocking controls from warnings.
- [x] Exact-zero gates cover configuration, Trial Balance, Balance Sheet, AP, Supplier Advance, VAT, Inventory, posted ledger, Bank reconciliation, Bank exceptions, and proposed Bank adjustments.
- [x] Only snapshots explicitly marked historical/cutover limitations may receive an immutable Finance-approved exception; current integrity failures cannot be overridden.
- [x] Period close follows `Open -> Locked -> reviewed/approved -> Closed` and commits the close run and period state atomically.
- [x] Period reopen requires permission, a reason, immutable audit evidence, and does not edit historical snapshots or journals.
- [x] Normal journals remain rejected in Locked and Closed periods.

## Financial Year Close

- [x] Financial Year close is separate from normal Period close and requires completed non-final periods plus an approved Locked final-period close run.
- [x] Financial Year close creates one idempotent, system-generated `YearEndClosing` journal using `FinancialYearClose` source identity.
- [x] Nonzero Income, COGS, and Expense account balances close directly to configured Retained Earnings; no Income Summary account is introduced.
- [x] Owner Drawings closes separately to configured Owner Capital; Owner Capital and Retained Earnings remain distinct.
- [x] The restricted Journal Engine path permits only the approved YearEndClosing journal in the Locked final period and does not weaken ordinary Locked/Closed posting controls.
- [x] Financial Year state, final-period state, close-run state, closing journal, and journal linkage are coordinated in one transaction.
- [x] Financial Year reopen requires dedicated authority, a reason, and a linked Journal Engine reversal; original closing journals and close evidence remain immutable.
- [x] Re-close after correction preserves prior close/reversal history and creates a new effective run/journal.

## Reporting and Carry-Forward

- [x] YearEndClosing and reversal journals remain visible in Trial Balance and General Ledger.
- [x] FinancialYearClose-reference journals are excluded only from historical P&L, COGS operating totals, and dynamic current/prior earnings calculations.
- [x] Balance Sheet carry-forward remains derived from the continuous posted ledger; no annual opening-balance journals are generated.
- [x] Opening Balance workflow remains limited to initial/cutover baselines and controlled adjustments.

## Finance Workbench and Evidence

- [x] Guarded Angular Close Workbench provides period/year selection, close-run history, gate evidence, blocking/warning states, exception approval, workflow actions, and read-only completed evidence.
- [x] Close/reopen actions are permission-aware and reload authoritative backend state after mutation.
- [x] Effective closing journal, reversal journal, actor/timestamp/reason evidence, and constituent period state are visible for investigation.
- [x] Tenant-scoped, permission-protected, evidence-only JSON close-package downloads are available for Period and Financial Year close runs.

## Validation Evidence

- Focused Slice 1 lifecycle/governance/Journal Engine tests: 20 passed, 0 failed.
- Focused Slice 2 financial-close, reporting, governance, and Journal Engine tests: 37 passed, 0 failed.
- Focused Angular Close Workbench tests: 4 passed, 0 failed.
- Full Angular suite: 46 passed, 0 failed.
- Full backend suite after Slice 3 export additions: 459 passed, 0 failed.
- Backend Release build after Slice 3 export additions: passed.
- Angular production build after Slice 3 workbench additions: passed.
- EF discovers pending P3-07 migrations `20260906100103_AddP307PeriodCloseRuns` and `20260906101105_AddP307FinancialYearCloseRuns`.

## Scope Limits

- No consolidation, intercompany elimination, statutory filing, Mushak/NBR integration, auditor portal, automated accrual/depreciation, budgeting, forecasting, new AR workflow, annual opening-balance posting, or arbitrary historical reconstruction is included.
- The existing `SQLitePCLRaw.lib.e_sqlite3` NU1903 advisory remains non-blocking.

## Final Decision

Decision: P3-07 ACCEPTED