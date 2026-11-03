# P2-07 Acceptance Checklist - Cash Book Report

Updated: 2026-09-05
Scope: P2-07 journal-backed Cash Book reporting
Source of truth: [Docs/10_Accounts_And_Finance/PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/PHASE_2_ACCOUNTING_DEVELOPMENT_PLAN.md)

## Scope Gate

- P2-07 is a read-only accounting reporting milestone.
- P2-08 remains out of scope for this acceptance decision.
- Posted journal lines are the only financial transaction source; operational tables are not joined into Cash Book calculations.

## Requirement Status

| Requirement | Status | Evidence |
|---|---|---|
| Posted journal lines are the financial source of truth | Passed | [src/RetailPOS.API/Services/AccountingReportService.cs](src/RetailPOS.API/Services/AccountingReportService.cs), `P207CashBookReportServiceTests.CashBook_UsesOnlyJournalLines_ForOpeningInflowsOutflowsAndRunningBalance` |
| Opening, inflow, outflow, running, and closing balances are calculated per account | Passed | `P207CashBookReportServiceTests.CashBook_UsesOnlyJournalLines_ForOpeningInflowsOutflowsAndRunningBalance` |
| Cash and Bank remain separate configured account books | Passed | `P207CashBookReportServiceTests.CashBook_SeparatesConfiguredCashAndBank_AndFiltersTenantAndLocation` |
| Outlet account overrides are respected | Passed | `P207CashBookReportServiceTests.CashBook_UsesOutletResolvedCashAccount` |
| Original and reversal journals appear in their respective posting periods | Passed | `P207CashBookReportServiceTests.CashBook_ReportsReversedOriginalInOriginalPeriod_AndPostedReversalInItsOwnPeriod` |
| POS sale/refund, expense, other income, supplier payment/advance, cash-session, opening-balance, and manual-journal effects are included once | Passed | `P207CashBookReportServiceTests.CashBook_UsesOnlyJournalLines_ForOpeningInflowsOutflowsAndRunningBalance` |
| Tenant and location filtering prevent cross-scope rows | Passed | `P207CashBookReportServiceTests.CashBook_SeparatesConfiguredCashAndBank_AndFiltersTenantAndLocation` |
| Exact GL reconciliation is returned per account | Passed | `P207CashBookReportServiceTests.CashBook_UsesOnlyJournalLines_ForOpeningInflowsOutflowsAndRunningBalance` |
| Cash Book endpoint requires its dedicated permission | Passed | `FinancialReportsHttpTests.CashBook_RequiresDedicatedPermission` |
| API and Angular reporting page are available | Passed | [src/RetailPOS.API/Controllers/AccountingReportsController.cs](src/RetailPOS.API/Controllers/AccountingReportsController.cs), [retailpos-frontend/src/app/pages/accounting/cash-book-report.component.ts](retailpos-frontend/src/app/pages/accounting/cash-book-report.component.ts) |
| Non-zero reconciliation differences are visibly displayed | Passed | `cash-book-report.component.spec.ts` supplies a non-zero API result and verifies the reconciliation exception label, value, and error styling are rendered. |

## Automated Evidence

### Focused P2-07 suite
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~P207CashBookReportServiceTests|FullyQualifiedName~CashBook_RequiresDedicatedPermission" --logger "console;verbosity=minimal"`
- Observed result: 5 passed, 0 failed, 0 skipped

### Frontend Cash Book UI suite
- Command: `npm test` from `retailpos-frontend`
- Observed result: 37 passed, 0 failed, including `cash-book-report.component.spec.ts`

### Build validation
- Backend: `dotnet build RetailPOS.slnx -c Release --no-restore`
- Observed result: succeeded; the existing `SQLitePCLRaw.lib.e_sqlite3` NU1903 dependency advisory remains.
- Frontend: `npm run -s build` from `retailpos-frontend`
- Observed result: production bundle generated successfully.

### Accounting/report regression suite
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~Accounting|FullyQualifiedName~FinancialReports|FullyQualifiedName~P206|FullyQualifiedName~P207" --logger "console;verbosity=minimal"`
- Observed result: 105 passed, 0 failed, 0 skipped

### Full backend suite
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --logger "console;verbosity=minimal"`
- Observed result: 339 passed, 0 failed, 0 skipped

## Final Decision

Decision: Accepted

Follow-up items:
- No P2-07 blockers remain.
- P2-08 must not begin until P2-07 approval is confirmed.