# RM-05 Acceptance Checklist - Expense v2 Workflow and Posting

Updated: 2026-08-01  
Milestone: RM-05 Expense v2 Workflow and Posting  
Status: Accepted

## 1) Scope gate

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| GATE-01 | Implementation is limited to RM-05 expense workflow and required schema/client support | Passed | [src/RetailPOS.Application/Expenses/Commands](src/RetailPOS.Application/Expenses/Commands), [src/RetailPOS.Infrastructure/Expenses/ExpenseCommandStore.cs](src/RetailPOS.Infrastructure/Expenses/ExpenseCommandStore.cs), [src/RetailPOS.API/Controllers/AccountingExpensesController.cs](src/RetailPOS.API/Controllers/AccountingExpensesController.cs), [src/RetailPOS.Core/Entities/Expense.cs](src/RetailPOS.Core/Entities/Expense.cs), [src/RetailPOS.Core/Entities/ExpenseCategory.cs](src/RetailPOS.Core/Entities/ExpenseCategory.cs) |
| GATE-02 | RM-01 to RM-04 contracts remain intact | Passed | Existing journal/period/opening-balance regressions remained green in [tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs](tests/RetailPOS.Tests/Accounting/JournalEntryServiceTests.cs), [tests/RetailPOS.Tests/Accounting/FinancialPeriodServiceTests.cs](tests/RetailPOS.Tests/Accounting/FinancialPeriodServiceTests.cs), [tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs) |
| GATE-03 | RM-06 and later milestones were not implemented | Passed | No other-income/manual-journal/report milestone changes were introduced beyond shared model/default parity migrations |

## 2) API and authorization governance

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| API-01 | Canonical accounting expense endpoints are exposed under `/api/accounting/expenses` | Passed | [src/RetailPOS.API/Controllers/AccountingExpensesController.cs](src/RetailPOS.API/Controllers/AccountingExpensesController.cs) |
| API-02 | Expense category mapping endpoints are available for managed category configuration | Passed | `GET/POST/PUT /api/accounting/expenses/categories` in [src/RetailPOS.API/Controllers/AccountingExpensesController.cs](src/RetailPOS.API/Controllers/AccountingExpensesController.cs) |
| API-03 | Workflow actions are authorization-gated by canonical `accounting.expenses.*` permissions | Passed | [src/RetailPOS.API/Controllers/AccountingExpensesController.cs](src/RetailPOS.API/Controllers/AccountingExpensesController.cs), [src/RetailPOS.API/Authorization/PermissionCatalog.cs](src/RetailPOS.API/Authorization/PermissionCatalog.cs), [tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs](tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs) |

## 3) Workflow and posting governance

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| FLOW-01 | Expense lifecycle supports Draft, Submitted, Approved, Rejected, and Cancelled | Passed | [src/RetailPOS.Core/Entities/Expense.cs](src/RetailPOS.Core/Entities/Expense.cs), [src/RetailPOS.Infrastructure/Expenses/ExpenseCommandStore.cs](src/RetailPOS.Infrastructure/Expenses/ExpenseCommandStore.cs) |
| FLOW-02 | Only approval creates a posted journal | Passed | [src/RetailPOS.Application/Expenses/Commands/ApproveExpenseCommand.cs](src/RetailPOS.Application/Expenses/Commands/ApproveExpenseCommand.cs), [src/RetailPOS.Infrastructure/Expenses/ExpenseCommandStore.cs](src/RetailPOS.Infrastructure/Expenses/ExpenseCommandStore.cs), [tests/RetailPOS.IntegrationTests/Accounting/ExpenseWorkflowServiceTests.cs](tests/RetailPOS.IntegrationTests/Accounting/ExpenseWorkflowServiceTests.cs) |
| FLOW-03 | Rejected and cancelled expenses do not create journals | Passed | [tests/RetailPOS.IntegrationTests/Accounting/ExpenseWorkflowServiceTests.cs](tests/RetailPOS.IntegrationTests/Accounting/ExpenseWorkflowServiceTests.cs) |
| FLOW-04 | Expense account resolves from category mapping by default, with approver-time override support | Passed | Category mapping and approval override handling in [src/RetailPOS.Infrastructure/Expenses/ExpenseCommandStore.cs](src/RetailPOS.Infrastructure/Expenses/ExpenseCommandStore.cs) |
| FLOW-05 | Approval pre-validates financial year/period openness before any draft journal is created | Passed | Period checks in [src/RetailPOS.Infrastructure/Expenses/ExpenseCommandStore.cs](src/RetailPOS.Infrastructure/Expenses/ExpenseCommandStore.cs), closed-period regression in [tests/RetailPOS.IntegrationTests/Accounting/ExpenseWorkflowServiceTests.cs](tests/RetailPOS.IntegrationTests/Accounting/ExpenseWorkflowServiceTests.cs) |

## 4) Data model and deployment readiness

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| DATA-01 | Expense workflow fields and category mapping schema are persisted by EF migrations | Passed | [src/RetailPOS.Infrastructure/Data/Migrations/20260801120458_AddExpenseWorkflowPhase1.cs](src/RetailPOS.Infrastructure/Data/Migrations/20260801120458_AddExpenseWorkflowPhase1.cs) |
| DATA-02 | Existing expense rows are backfilled safely for `business_id` and workflow defaults | Passed | Manual migration backfill/update SQL in [src/RetailPOS.Infrastructure/Data/Migrations/20260801120458_AddExpenseWorkflowPhase1.cs](src/RetailPOS.Infrastructure/Data/Migrations/20260801120458_AddExpenseWorkflowPhase1.cs) |
| DATA-03 | EF snapshot parity is restored so database update runs cleanly | Passed | Follow-up default-parity migration [src/RetailPOS.Infrastructure/Data/Migrations/20260801120621_TempExpenseWorkflowDriftCheck.cs](src/RetailPOS.Infrastructure/Data/Migrations/20260801120621_TempExpenseWorkflowDriftCheck.cs) |

## 5) Client compatibility

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| UI-01 | Existing expense pages are updated to the accounting expense API contract | Passed | [retailpos-frontend/src/app/services/accounting.service.ts](retailpos-frontend/src/app/services/accounting.service.ts), [retailpos-frontend/src/app/pages/accounting/expense-form.component.ts](retailpos-frontend/src/app/pages/accounting/expense-form.component.ts), [retailpos-frontend/src/app/pages/accounting/expenses-list.component.ts](retailpos-frontend/src/app/pages/accounting/expenses-list.component.ts) |
| UI-02 | Client surfaces support category selection and workflow actions | Passed | [retailpos-frontend/src/app/pages/accounting/expense-form.component.html](retailpos-frontend/src/app/pages/accounting/expense-form.component.html), [retailpos-frontend/src/app/pages/accounting/expenses-list.component.html](retailpos-frontend/src/app/pages/accounting/expenses-list.component.html) |

## 6) Test evidence

| Test ID | Scope | Status | Evidence |
|---|---|---|---|
| TEST-01 | Application-command/Infrastructure-store: category mapping, approval posting, closed-period rejection, non-posting reject/cancel | Passed | [tests/RetailPOS.IntegrationTests/Accounting/ExpenseWorkflowServiceTests.cs](tests/RetailPOS.IntegrationTests/Accounting/ExpenseWorkflowServiceTests.cs) |
| TEST-02 | HTTP/integration: category create, expense lifecycle, approve permission gate | Passed | [tests/RetailPOS.Tests/Accounting/ExpenseWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/ExpenseWorkflowHttpTests.cs) |
| TEST-03 | Expanded accounting regression with RM-03/RM-04 compatibility | Passed | Existing accounting suites plus RM-05 slice in [tests/RetailPOS.Tests/Accounting](tests/RetailPOS.Tests/Accounting) |

Executed commands:

1. `dotnet build RetailPOS.slnx -c Release`
2. `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~RetailPOS.Tests.Accounting.ExpenseWorkflow"`
3. `npm run -s build` (from `retailpos-frontend`)
4. `dotnet ef migrations add AddExpenseWorkflowPhase1 --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API`
5. `dotnet ef migrations add TempExpenseWorkflowDriftCheck --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API`
6. `dotnet ef database update --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API`
7. `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~RetailPOS.Tests.Accounting.ExpenseWorkflow|FullyQualifiedName~RetailPOS.Tests.Accounting.FinancialPeriod|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryServiceTests|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryWorkflowHttpTests|FullyQualifiedName~RetailPOS.Tests.Accounting.AccountingPermissionRegressionTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceWorkflowHttpTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceServiceTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceAuditTests"`
8. `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release`

Observed result summary:

- Focused RM-05 expense workflow run: 5 passed, 0 failed, 0 skipped.
- Expanded accounting regression: 47 passed, 0 failed, 0 skipped.
- Full backend suite: 278 passed, 0 failed, 0 skipped.
- Frontend build: passed.
- Database update: passed.
- Backend build: passed (NU1903 warning observed and tracked as non-blocking).

## 7) Final decision

RM-05 expense v2 workflow and posting is accepted and ready for approval gate.

Out-of-scope confirmation:

1. RM-06 and later milestones were not implemented.
2. RM-01 to RM-04 behavior remained protected by regression coverage.

## 8) Update 2026-11-06 - Chart of Accounts and expense integration

Approved decisions D1 to D5. This section supersedes the earlier text where they differ: FLOW-04's override now follows the eligibility rule below, and category writes moved from `accounting.expenses.edit` to `accounting.expense_categories.manage`. No schema migration or data change was involved.

### Chart of Accounts access

- The screen is `/accounting/chart-of-accounts` under Finance, on the existing `api/accounting/chart-accounts` API and `accounting.chart_accounts.*` permissions. The legacy `/accounts` page stays hidden, read-only and separate.
- Operating expense accounts (Rent, Internet and so on) are created there by authorized users. They are not provisioned automatically, and the 26 default system accounts are unchanged.

### Expense account eligibility (`ExpenseAccountEligibilityPolicy`)

A newly chosen account must be in the current Business, active, Expense-type, not a control account, without active child accounts, and allow direct postings. A system account must also be on the explicit allowlist, which currently holds only `6310 Bank Fee Expense`. `4010 Sales Discounts`, `4020 Sales Returns` and the other system accounts cannot be newly chosen.

The same rule serves the lookup, category create, category remap, category reactivation and an approval override to a different account. Compatibility: a category edit that keeps its account and does not reactivate the category is not re-judged, and approval using the category's saved mapping checks only that the account is in the Business, active and Expense-type. An existing mapping is never rewritten.

Approval resolves the category's account at approval time, so remapping a category reaches its Draft, Submitted and Rejected expenses when they are approved. The remap is audited and the form warns about it. Approved expenses keep the account they posted to, and posted journals do not change.

### Payment source and account (`ExpensePaymentAccountPolicy`)

The model has no account subtype and no Cash Session ledger account, so accounts are classified from Accounting Configuration and the bank register for the expense's location:

| Payment Source | Allowed credit account |
|---|---|
| `pos_drawer` | The outlet's Cash mapping, or the Business Cash mapping if the outlet has no override. An outlet is required. |
| `petty_cash` | Business Cash or Petty Cash mappings, plus the outlet's overrides on an outlet expense |
| `bank`, and legacy rows with no source | Business Bank mapping and Business-wide active bank register accounts, plus the outlet's Bank override and register accounts on an outlet expense |

Create and edit also require the `IsExpensePaymentAccount` flag, except for the POS drawer's account, which configuration chooses. Approval applies the source rule but not the flag, so a flag change does not strand an expense. A flagged account that is not mapped or registered is offered for no source.

An existing expense that disagrees is refused at approval with no journal. To correct it, reject it, edit the source or account, and submit it again. Approved expenses are never altered.

An open Cash Session is required when the payout is recorded (`pay-from-drawer`), not at create or approval, as FR-EXP-APP-01 requires. Approval posts the journal; the payout records one cash movement and adds no journal.

### Account deactivation (`ChartAccountDeactivationPolicy`)

Every Active-to-Inactive change, through `PUT` or `POST .../deactivate`, uses one rule under a row lock. It is refused, with counts where available, while the account has active expense categories, active child accounts, any accounting configuration mapping, journal lines, active bank register links, or unapproved expenses, other income entries, supplier payments or supplier advances. System accounts cannot be deactivated. Reactivation goes through `PUT` and requires an active parent. Inactive accounts stay in historical ledgers and reports.

Mapping a category takes a shared lock on the account row, so it cannot race a deactivation into an active category on an inactive account.