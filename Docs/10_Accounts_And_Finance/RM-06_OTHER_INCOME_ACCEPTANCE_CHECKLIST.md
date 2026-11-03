# RM-06 Acceptance Checklist - Other Income Workflow and Posting

Updated: 2026-08-01  
Milestone: RM-06 Other Income Workflow and Posting  
Status: Accepted

## 1) Scope gate

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| GATE-01 | Implementation is limited to RM-06 other income workflow and required contract/schema/client support | Passed | [src/RetailPOS.Application/OtherIncome/Commands](src/RetailPOS.Application/OtherIncome/Commands), [src/RetailPOS.Infrastructure/OtherIncome/OtherIncomeCommandStore.cs](src/RetailPOS.Infrastructure/OtherIncome/OtherIncomeCommandStore.cs), [src/RetailPOS.API/Controllers/AccountingOtherIncomeController.cs](src/RetailPOS.API/Controllers/AccountingOtherIncomeController.cs), [src/RetailPOS.Core/Entities/OtherIncomeEntry.cs](src/RetailPOS.Core/Entities/OtherIncomeEntry.cs) |
| GATE-02 | RM-01 to RM-05 contracts remain intact | Passed | Existing accounting regressions remained green in [tests/RetailPOS.Tests/Accounting/FinancialPeriodServiceTests.cs](tests/RetailPOS.Tests/Accounting/FinancialPeriodServiceTests.cs), [tests/RetailPOS.Tests/Accounting/ExpenseWorkflowServiceTests.cs](tests/RetailPOS.Tests/Accounting/ExpenseWorkflowServiceTests.cs), [tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs) |
| GATE-03 | RM-07 and later milestones were not implemented | Passed | No manual journal, reporting, or RM-07+ workflow implementation was introduced in this milestone |
| GATE-04 | The observed `1/4` indicator does not represent unfinished RM-06 work | Passed | Workspace/doc/frontend text search found no RM-06 progress artifact or checklist row using `1/4`; RM-06 acceptance evidence and tests are fully populated in this checklist and [Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md](Docs/10_Accounts_And_Finance/BASIC_ACCOUNTING_DEVELOPMENT_PLAN.md) |

## 2) API and authorization governance

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| API-01 | Canonical other income endpoints are exposed under `/api/accounting/incomes` | Passed | [src/RetailPOS.API/Controllers/AccountingOtherIncomeController.cs](src/RetailPOS.API/Controllers/AccountingOtherIncomeController.cs) |
| API-02 | Workflow actions are authorization-gated by canonical `accounting.other_income.*` permissions | Passed | [src/RetailPOS.API/Controllers/AccountingOtherIncomeController.cs](src/RetailPOS.API/Controllers/AccountingOtherIncomeController.cs), [src/RetailPOS.API/Authorization/PermissionCatalog.cs](src/RetailPOS.API/Authorization/PermissionCatalog.cs), [tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs](tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs) |
| API-03 | SQL seed assets and frontend role exposure include the canonical reject permission added for RM-06 | Passed | [database/add-accounting-v2-permissions.sql](database/add-accounting-v2-permissions.sql), [database/seed-data.sql](database/seed-data.sql), [database/seed-v2-roles.sql](database/seed-v2-roles.sql), [retailpos-frontend/src/app/components/roles/role-form.component.ts](retailpos-frontend/src/app/components/roles/role-form.component.ts) |

## 3) Workflow and posting governance

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| FLOW-01 | Other income lifecycle supports Draft, Submitted, Approved, Rejected, and Cancelled | Passed | [src/RetailPOS.Core/Entities/OtherIncomeEntry.cs](src/RetailPOS.Core/Entities/OtherIncomeEntry.cs), [src/RetailPOS.Infrastructure/OtherIncome/OtherIncomeCommandStore.cs](src/RetailPOS.Infrastructure/OtherIncome/OtherIncomeCommandStore.cs) |
| FLOW-02 | Only approval creates a posted journal | Passed | [src/RetailPOS.Application/OtherIncome/Commands/ApproveOtherIncomeCommand.cs](src/RetailPOS.Application/OtherIncome/Commands/ApproveOtherIncomeCommand.cs), [src/RetailPOS.Infrastructure/OtherIncome/OtherIncomeCommandStore.cs](src/RetailPOS.Infrastructure/OtherIncome/OtherIncomeCommandStore.cs), [tests/RetailPOS.IntegrationTests/Accounting/OtherIncomeWorkflowServiceTests.cs](tests/RetailPOS.IntegrationTests/Accounting/OtherIncomeWorkflowServiceTests.cs) |
| FLOW-03 | Rejected and cancelled other income entries do not create journals | Passed | [tests/RetailPOS.IntegrationTests/Accounting/OtherIncomeWorkflowServiceTests.cs](tests/RetailPOS.IntegrationTests/Accounting/OtherIncomeWorkflowServiceTests.cs) |
| FLOW-04 | Other Income uses a controlled income-account selection rule | Passed | Approval resolves posting account by validated precedence `approve override -> document override -> accounting configuration other_income slot`, with same-business active income-account validation in [src/RetailPOS.Infrastructure/OtherIncome/OtherIncomeCommandStore.cs](src/RetailPOS.Infrastructure/OtherIncome/OtherIncomeCommandStore.cs) |
| FLOW-05 | Approval reuses configuration mapping and period governance before posting | Passed | Configuration mapping and period checks in [src/RetailPOS.Infrastructure/OtherIncome/OtherIncomeCommandStore.cs](src/RetailPOS.Infrastructure/OtherIncome/OtherIncomeCommandStore.cs) |
| FLOW-06 | Failed approval is atomic and cannot leave or require cleanup of a transient journal outside the same database transaction | Passed | Approval transaction in [src/RetailPOS.Infrastructure/OtherIncome/OtherIncomeCommandStore.cs](src/RetailPOS.Infrastructure/OtherIncome/OtherIncomeCommandStore.cs), journal command poster in [src/RetailPOS.Infrastructure/AccountingJournals/OtherIncomeApprovalJournalPoster.cs](src/RetailPOS.Infrastructure/AccountingJournals/OtherIncomeApprovalJournalPoster.cs), rollback proof in [tests/RetailPOS.IntegrationTests/Accounting/OtherIncomeAtomicityTests.cs](tests/RetailPOS.IntegrationTests/Accounting/OtherIncomeAtomicityTests.cs) |

## 4) Data model and deployment readiness

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| DATA-01 | Other income workflow fields and journal linkage are persisted by EF migrations | Passed | [src/RetailPOS.Infrastructure/Data/Migrations/20260801121731_AddOtherIncomeWorkflowPhase1.cs](src/RetailPOS.Infrastructure/Data/Migrations/20260801121731_AddOtherIncomeWorkflowPhase1.cs) |
| DATA-02 | Existing other income rows receive safe `updated_at` and status normalization during migration | Passed | Backfill SQL in [src/RetailPOS.Infrastructure/Data/Migrations/20260801121731_AddOtherIncomeWorkflowPhase1.cs](src/RetailPOS.Infrastructure/Data/Migrations/20260801121731_AddOtherIncomeWorkflowPhase1.cs) |

## 5) Client compatibility

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| UI-01 | Frontend accounting client exposes the new other income API surface | Passed | [retailpos-frontend/src/app/services/accounting.service.ts](retailpos-frontend/src/app/services/accounting.service.ts) |
| UI-02 | New list and form pages exist and are route/menu visible behind canonical permissions | Passed | [retailpos-frontend/src/app/pages/accounting/other-income-list.component.ts](retailpos-frontend/src/app/pages/accounting/other-income-list.component.ts), [retailpos-frontend/src/app/pages/accounting/other-income-form.component.ts](retailpos-frontend/src/app/pages/accounting/other-income-form.component.ts), [retailpos-frontend/src/app/app.routes.ts](retailpos-frontend/src/app/app.routes.ts), [retailpos-frontend/src/app/models/menu.model.ts](retailpos-frontend/src/app/models/menu.model.ts) |
| UI-03 | Minimal account-id input remains documented as a non-blocking UX gap, not unfinished RM-06 business logic | Passed | Current form uses direct account-id entry in [retailpos-frontend/src/app/pages/accounting/other-income-form.component.html](retailpos-frontend/src/app/pages/accounting/other-income-form.component.html); this is treated as a UX follow-up for RM-07+, not a blocker to RM-06 workflow correctness |

## 6) Test evidence

| Test ID | Scope | Status | Evidence |
|---|---|---|---|
| TEST-01 | Application-command/Infrastructure-store: approval posting, missing-period rejection, non-posting reject/cancel | Passed | [tests/RetailPOS.IntegrationTests/Accounting/OtherIncomeWorkflowServiceTests.cs](tests/RetailPOS.IntegrationTests/Accounting/OtherIncomeWorkflowServiceTests.cs) |
| TEST-02 | Transactional rollback acceptance: posted journal and source update roll back together on post-approval failure | Passed | [tests/RetailPOS.IntegrationTests/Accounting/OtherIncomeAtomicityTests.cs](tests/RetailPOS.IntegrationTests/Accounting/OtherIncomeAtomicityTests.cs) |
| TEST-03 | HTTP/integration: create-submit-approve lifecycle and reject permission gate | Passed | [tests/RetailPOS.Tests/Accounting/OtherIncomeWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/OtherIncomeWorkflowHttpTests.cs) |
| TEST-04 | Permission regression alignment for new canonical policies | Passed | [tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs](tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs) |
| TEST-05 | Expanded accounting regression with RM-03 to RM-05 compatibility | Passed | Existing accounting suites plus RM-06 slice in [tests/RetailPOS.Tests/Accounting](tests/RetailPOS.Tests/Accounting) |

Executed commands:

1. `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~RetailPOS.Tests.Accounting.OtherIncomeWorkflow|FullyQualifiedName~RetailPOS.Tests.Accounting.OtherIncomeAtomicity|FullyQualifiedName~RetailPOS.Tests.Accounting.AccountingPermissionRegressionTests"`
2. `npm run -s build` (from `retailpos-frontend`)
3. `dotnet ef migrations add AddOtherIncomeWorkflowPhase1 --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API`
4. `dotnet ef database update --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API`
5. `dotnet build RetailPOS.slnx -c Release`
6. `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~RetailPOS.Tests.Accounting.OtherIncomeWorkflow|FullyQualifiedName~RetailPOS.Tests.Accounting.OtherIncomeAtomicity|FullyQualifiedName~RetailPOS.Tests.Accounting.ExpenseWorkflow|FullyQualifiedName~RetailPOS.Tests.Accounting.FinancialPeriod|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryServiceTests|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryWorkflowHttpTests|FullyQualifiedName~RetailPOS.Tests.Accounting.AccountingPermissionRegressionTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceWorkflowHttpTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceServiceTests|FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalanceAuditTests"`
7. `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release`

Observed result summary:

- Focused RM-06 other income + atomicity + permission run: 11 passed, 0 failed, 0 skipped.
- Expanded accounting regression: 53 passed, 0 failed, 0 skipped.
- Full backend suite: 284 passed, 0 failed, 0 skipped.
- Frontend build: passed.
- Database update: passed.
- Backend build: passed (NU1903 warning observed and tracked as non-blocking).

## 7) Final decision

RM-06 other income workflow and posting is accepted and ready for approval gate.

Out-of-scope confirmation:

1. RM-07 and later milestones were not implemented.
2. RM-01 to RM-05 behavior remained protected by regression coverage.