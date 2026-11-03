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