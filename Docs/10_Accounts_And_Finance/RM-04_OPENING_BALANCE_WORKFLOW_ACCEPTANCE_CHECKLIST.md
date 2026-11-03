# RM-04 Acceptance Checklist - Opening Balance Workflow

Updated: 2026-08-01  
Milestone: RM-04 Opening Balance Workflow  
Status: Accepted

## 1) Scope gate

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| GATE-01 | Implementation is limited to RM-04 and does not implement RM-03+ features | Passed | [src/RetailPOS.API/Services/OpeningBalanceService.cs](src/RetailPOS.API/Services/OpeningBalanceService.cs), [src/RetailPOS.API/Controllers/AccountingOpeningBalancesController.cs](src/RetailPOS.API/Controllers/AccountingOpeningBalancesController.cs), no RM-03 entity/model changes |
| GATE-02 | Implementation remains backward compatible with existing journal/configuration workflow | Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs), [src/RetailPOS.API/Authorization/PermissionCatalog.cs](src/RetailPOS.API/Authorization/PermissionCatalog.cs), [retailpos-frontend/src/app/services/auth.service.ts](retailpos-frontend/src/app/services/auth.service.ts) |

## 2) Opening balance governance

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| GOV-01 | Opening balance posts through controlled endpoint and creates journal entry | Passed | [src/RetailPOS.API/Controllers/AccountingOpeningBalancesController.cs](src/RetailPOS.API/Controllers/AccountingOpeningBalancesController.cs), [src/RetailPOS.API/Services/OpeningBalanceService.cs](src/RetailPOS.API/Services/OpeningBalanceService.cs) |
| GOV-02 | Mandatory accounting configuration mappings are required before opening balance posting | Passed | [src/RetailPOS.API/Services/OpeningBalanceService.cs](src/RetailPOS.API/Services/OpeningBalanceService.cs), [src/RetailPOS.API/Services/AccountingConfigurationService.cs](src/RetailPOS.API/Services/AccountingConfigurationService.cs) |
| GOV-03 | Missing/invalid required Balance Sheet mappings are rejected with clear validation messages | Passed | [src/RetailPOS.API/Services/OpeningBalanceService.cs](src/RetailPOS.API/Services/OpeningBalanceService.cs), [tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs#L75) |
| GOV-04 | Opening balance lines reject income/expense/COGS account types | Passed | [src/RetailPOS.API/Services/OpeningBalanceService.cs](src/RetailPOS.API/Services/OpeningBalanceService.cs), [tests/RetailPOS.Tests/Accounting/OpeningBalanceServiceTests.cs](tests/RetailPOS.Tests/Accounting/OpeningBalanceServiceTests.cs#L43) |
| GOV-05 | Opening balance lock is enforced after first operational posted journal | Passed | [src/RetailPOS.API/Services/OpeningBalanceService.cs](src/RetailPOS.API/Services/OpeningBalanceService.cs), [tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs#L101) |
| GOV-06 | Opening balance and opening balance adjustment journals do not trigger lock | Passed | [src/RetailPOS.API/Services/OpeningBalanceService.cs](src/RetailPOS.API/Services/OpeningBalanceService.cs) |
| GOV-07 | Original opening balance cannot be reversed; correction path is adjustment journal | Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs), [src/RetailPOS.API/Services/OpeningBalanceService.cs](src/RetailPOS.API/Services/OpeningBalanceService.cs) |

## 3) Adjustment governance

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| ADJ-01 | Adjustment uses dedicated endpoint and permission | Passed | [src/RetailPOS.API/Controllers/AccountingOpeningBalancesController.cs](src/RetailPOS.API/Controllers/AccountingOpeningBalancesController.cs), [src/RetailPOS.API/Authorization/PermissionCatalog.cs](src/RetailPOS.API/Authorization/PermissionCatalog.cs), [tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs#L126) |
| ADJ-02 | Adjustment requires reason | Passed | [src/RetailPOS.API/DTOs/Accounting/AccountingDtos.cs](src/RetailPOS.API/DTOs/Accounting/AccountingDtos.cs), [src/RetailPOS.API/Services/OpeningBalanceService.cs](src/RetailPOS.API/Services/OpeningBalanceService.cs) |
| ADJ-03 | Closed/locked periods reject adjustments | Passed | [src/RetailPOS.API/Services/OpeningBalanceService.cs](src/RetailPOS.API/Services/OpeningBalanceService.cs), [tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs#L165) |
| ADJ-04 | Adjustment journals are linked to the original opening balance journal | Passed | [src/RetailPOS.API/Services/OpeningBalanceService.cs](src/RetailPOS.API/Services/OpeningBalanceService.cs) |

## 4) Permission and seed synchronization

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| PERM-01 | `accounting.opening_balances.adjust` exists in canonical permission catalog | Passed | [src/RetailPOS.API/Authorization/PermissionCatalog.cs](src/RetailPOS.API/Authorization/PermissionCatalog.cs) |
| PERM-02 | SQL seed assets include opening balance adjustment permission | Passed | [database/add-accounting-v2-permissions.sql](database/add-accounting-v2-permissions.sql), [database/seed-v2-roles.sql](database/seed-v2-roles.sql), [database/seed-data.sql](database/seed-data.sql) |
| PERM-03 | Onboarding/DbSeeder/frontend role and alias maps include adjustment permission | Passed | [src/RetailPOS.API/Services/BusinessOnboardingService.cs](src/RetailPOS.API/Services/BusinessOnboardingService.cs), [src/RetailPOS.Infrastructure/Data/DbSeeder.cs](src/RetailPOS.Infrastructure/Data/DbSeeder.cs), [retailpos-frontend/src/app/components/roles/role-form.component.ts](retailpos-frontend/src/app/components/roles/role-form.component.ts), [retailpos-frontend/src/app/services/auth.service.ts](retailpos-frontend/src/app/services/auth.service.ts) |

## 5) Test evidence

| Test ID | Scope | Status | Evidence |
|---|---|---|---|
| TEST-01 | Unit: RM-04 service governance and validation rules | Passed | [tests/RetailPOS.Tests/Accounting/OpeningBalanceServiceTests.cs](tests/RetailPOS.Tests/Accounting/OpeningBalanceServiceTests.cs) |
| TEST-02 | Integration: RM-04 opening posting, adjustment posting, lock and period rejections | Passed | [tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/OpeningBalanceWorkflowHttpTests.cs) |
| TEST-03 | Permission regression remains synchronized | Passed | [tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs](tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs) |
| TEST-04 | Audit: opening and adjustment posting emits accounts audit entries | Passed | [tests/RetailPOS.Tests/Accounting/OpeningBalanceAuditTests.cs](tests/RetailPOS.Tests/Accounting/OpeningBalanceAuditTests.cs) |

Executed commands:

1. `dotnet build RetailPOS.slnx -c Release`
2. `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~RetailPOS.Tests.Accounting.OpeningBalance|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryServiceTests|FullyQualifiedName~RetailPOS.Tests.Accounting.JournalEntryWorkflowHttpTests|FullyQualifiedName~RetailPOS.Tests.Accounting.AccountingPermissionRegressionTests|FullyQualifiedName~RetailPOS.Tests.Accounting.AccountingConfigurationHttpTests"`
3. `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release`
4. `npm run -s build` from [retailpos-frontend](retailpos-frontend)

Observed result summary:

- Focused .NET test run: 30 passed, 0 failed, 0 skipped.
- Full .NET test run: 258 passed, 0 failed, 0 skipped.
- .NET build: passed.
- Frontend build: passed.

## 6) Final decision

RM-04 opening balance workflow implementation is accepted.

Out-of-scope confirmation:

1. RM-03 implementation was not started.
2. No later milestones were implemented in this change.
