# P3-01 Acceptance Checklist - Balance Sheet and Enhanced General Ledger

Updated: 2026-09-05
Scope: P3-01 journal-only Balance Sheet, enhanced General Ledger, and comparative statement reporting
Source of truth: [PHASE_3_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/PHASE_3_ACCOUNTING_DEVELOPMENT_PLAN.md)

Status legend:
- Passed
- Pending final regression validation
- Not Applicable

---

## 1. Scope Gate

- P3-01 uses only historically posted `JournalEntry` and `JournalEntryLine` data as its financial source.
- P3-01 adds no operational posting, no current-year earnings journal, and no historical closing journal.
- P3-02 through P3-08 remain out of scope.

## 2. Requirement Matrix

| Requirement | Status | Evidence |
|---|---|---|
| Balance Sheet supports as-of, journal-only Asset/Liability/Equity presentation | Passed | [AccountingReportService.cs](src/RetailPOS.API/Services/AccountingReportService.cs) and `P301BalanceSheetAndEnhancedGlTests.BalanceSheet_UsesHierarchyNormalBalancesAndDynamicEarnings_AndReconciles` |
| Draft, submitted, and approved-but-unposted journals are excluded | Passed | `P301BalanceSheetAndEnhancedGlTests.BalanceSheet_ExcludesUnpostedJournalsAndReconcilesAssetLiabilityAndEquityAccountsToEnhancedGl` |
| Debit-normal and credit-normal account rules are respected | Passed | `P301BalanceSheetAndEnhancedGlTests.BalanceSheet_UsesHierarchyNormalBalancesAndDynamicEarnings_AndReconciles` verifies Asset debit-normal, Liability credit-normal, Owner Capital credit-normal, and Owner Drawings debit-normal presentation. |
| Existing chart hierarchy rolls up child balances | Passed | `P301BalanceSheetAndEnhancedGlTests.BalanceSheet_UsesHierarchyNormalBalancesAndDynamicEarnings_AndReconciles` verifies Current Assets parent rollup from its Cash child. |
| Owner Capital, Owner Drawings, and configured Retained Earnings remain separate | Passed | `P301BalanceSheetAndEnhancedGlTests.BalanceSheet_UsesHierarchyNormalBalancesAndDynamicEarnings_AndReconciles` verifies distinct presentation roles and balances. |
| Current Year Earnings is dynamic and journal-only | Passed | `P301BalanceSheetAndEnhancedGlTests.BalanceSheet_UsesHierarchyNormalBalancesAndDynamicEarnings_AndReconciles` verifies Income less COGS/Expense through the selected date. |
| Unclosed Prior-Year Earnings is separately presented | Passed | `P301BalanceSheetAndEnhancedGlTests.BalanceSheet_UsesHierarchyNormalBalancesAndDynamicEarnings_AndReconciles` verifies prior earnings are separate from Owner Capital and Retained Earnings. |
| Future year-end transfer does not double count prior earnings | Passed | `P301BalanceSheetAndEnhancedGlTests.BalanceSheet_ExcludesClosedYearDynamicEarningsWhenYearEndTransferExists` verifies a posted `YearEndClosing` using `FinancialYearClose` and the financial-year ID reference: transferred prior profit is shown through Retained Earnings, excluded from Unclosed Prior-Year Earnings, while current-year earnings remain dynamic and the statement remains balanced. |
| Opening-balance/cutover limitation is surfaced | Passed | `P301BalanceSheetAndEnhancedGlTests.ComparativeBalanceSheetAndProfitLoss_AreCalculatedIndependently_AndCutoverIsVisible` and [balance-sheet-report.component.spec.ts](retailpos-frontend/src/app/pages/accounting/balance-sheet-report.component.spec.ts) |
| Balance Sheet equation and account-to-GL reconciliation are exposed | Passed | `P301BalanceSheetAndEnhancedGlTests.BalanceSheet_UsesHierarchyNormalBalancesAndDynamicEarnings_AndReconciles` verifies $Assets = Liabilities + Equity$, Cash leaf, and Current Assets parent rollup to Enhanced GL. `BalanceSheet_ExcludesUnpostedJournalsAndReconcilesAssetLiabilityAndEquityAccountsToEnhancedGl` verifies Asset, Liability, Owner Capital, Owner Drawings, and Retained Earnings account equality to Enhanced GL closing, including opening and reversal activity. |
| Enhanced GL provides opening, debit, credit, deterministic running, and closing balances | Passed | `P301BalanceSheetAndEnhancedGlTests.EnhancedGeneralLedger_CalculatesOpeningMovementsRunningAndClosingInDeterministicOrder` |
| Same-date movement ordering is deterministic and drill-down fields are preserved | Passed | `P301BalanceSheetAndEnhancedGlTests.EnhancedGeneralLedger_OrdersSameDateByJournalNumberThenLineId_AndKeepsReversalInItsOwnPeriod` and `EnhancedGeneralLedger_CalculatesOpeningMovementsRunningAndClosingInDeterministicOrder` verify PostingDate, Journal Number, Journal ID, Line ID order plus narration, source reference, outlet, customer, payment method, and cost-center fields. |
| Reversed original and reversal journal appear in actual posting order | Passed | `P301BalanceSheetAndEnhancedGlTests.EnhancedGeneralLedger_OrdersSameDateByJournalNumberThenLineId_AndKeepsReversalInItsOwnPeriod` verifies the original remains in January and the linked reversal appears in February. |
| Comparative Balance Sheet and P&L are independently calculated | Passed | `P301BalanceSheetAndEnhancedGlTests.ComparativeBalanceSheetAndProfitLoss_AreCalculatedIndependently_AndCutoverIsVisible` and `ComparativeProfitLoss_IncludesOriginalAndReversalInTheirOwnPostingPeriods` verify independently queried cross-year values and actual reversal-date treatment. |
| Tenant and unauthorized location isolation are enforced | Passed | `P301BalanceSheetAndEnhancedGlTests.BalanceSheetAndEnhancedLedger_AreTenantScoped`, `BalanceSheet_RejectsInvalidLocationType`, `LocationScopedBalanceSheet_IdentifiesManagementViewAndSurfacesUnassignedCounterpartDifference`, and `FinancialReportsHttpTests.P301Reports_EnforcePermissionsAndLocationAuthorization` verify tenant scope, invalid-type rejection, outlet/warehouse denial, management-view identification, and surfaced scoped reconciliation difference. |
| Dedicated Balance Sheet and existing GL permissions protect endpoints/pages | Passed | [AccountingReportsController.cs](src/RetailPOS.API/Controllers/AccountingReportsController.cs), [app.routes.ts](retailpos-frontend/src/app/app.routes.ts), and `FinancialReportsHttpTests.P301Reports_EnforcePermissionsAndLocationAuthorization` |
| Typed Angular Balance Sheet and enhanced GL pages are available | Passed | [balance-sheet-report.component.ts](retailpos-frontend/src/app/pages/accounting/balance-sheet-report.component.ts), [enhanced-general-ledger.component.ts](retailpos-frontend/src/app/pages/accounting/enhanced-general-ledger.component.ts), and [accounting.model.ts](retailpos-frontend/src/app/models/accounting.model.ts) |

## 3. Automated Evidence

### Focused P3-01 backend suite
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~P301BalanceSheetAndEnhancedGlTests|FullyQualifiedName~P301Reports_EnforcePermissionsAndLocationAuthorization" --logger "console;verbosity=minimal"`
- Observed result: 11 passed, 0 failed, 0 skipped.

### Focused P3-01 frontend suite
- Command: `npx ng test retailpos-frontend --watch=false --include "src/app/pages/accounting/balance-sheet-report.component.spec.ts"`
- Observed result: 1 passed, 0 failed.

### Backend Release build
- Command: `dotnet build RetailPOS.slnx -c Release --no-restore`
- Observed result: succeeded. The existing `SQLitePCLRaw.lib.e_sqlite3` NU1903 dependency advisory remains.

### Accounting/report regression suite
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~Accounting|FullyQualifiedName~FinancialReports|FullyQualifiedName~P301" --logger "console;verbosity=minimal"`
- Observed result: 118 passed, 0 failed, 0 skipped.

### Full backend suite
- Command: `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --logger "console;verbosity=minimal"`
- Observed result: 352 passed, 0 failed, 0 skipped.

### Angular production build
- Command: `npm run -s build` from `retailpos-frontend`
- Observed result: production bundle generated successfully.

## 4. Final Decision

Decision: P3-01 ACCEPTED

Known limitations:
- The statement warns when no posted opening balance exists on or before the selected as-of date; it does not estimate missing pre-cutover history.
- P3-07 owns actual year-end close journal creation. P3-01 only recognizes the planned `YearEndClosing` journal type to prevent future dynamic prior-year double counting.
- P3-02 through P3-08 remain deferred and are not part of this acceptance decision.