# RM-07 Acceptance Checklist - Manual Journal UX and Operational Controls

Updated: 2026-08-09
Milestone: RM-07 Manual Journal UX and Operational Controls
Status: Accepted

## 1) Scope gate

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| GATE-01 | RM-07 is limited to manual-journal workflow, API contract, permissions, UI, and supporting migration work | Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs), [src/RetailPOS.API/Controllers/AccountingJournalsController.cs](src/RetailPOS.API/Controllers/AccountingJournalsController.cs), [retailpos-frontend/src/app/pages/accounting/manual-journal-list.component.ts](retailpos-frontend/src/app/pages/accounting/manual-journal-list.component.ts) |
| GATE-02 | RM-03 to RM-06 accounting behavior remains intact | Passed | Full accounting regression suite remained green after RM-07 integration in [tests/RetailPOS.Tests/Accounting](tests/RetailPOS.Tests/Accounting) |
| GATE-03 | The implementation does not expand into RM-08 reporting scope | Passed | No reporting or financial-statement implementation was introduced beyond journal workflow and UI wiring |

## 2) Backend workflow and posting governance

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| API-01 | Manual journals are exposed through the accounting journals API with create/update/submit/approve/reject/cancel/post/reverse actions | Passed | [src/RetailPOS.API/Controllers/AccountingJournalsController.cs](src/RetailPOS.API/Controllers/AccountingJournalsController.cs) |
| API-02 | Workflow behavior follows the approved mode contract: Draft -> Post or Draft -> Submit -> Approve/Reject -> Post | Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs) |
| API-03 | Maker-checker and self-approval rules are enforced through configuration-driven behavior | Passed | [src/RetailPOS.Core/Entities/AccountingConfiguration.cs](src/RetailPOS.Core/Entities/AccountingConfiguration.cs), [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs) |
| API-04 | Posting continues to honor financial-year and accounting-period governance | Passed | [src/RetailPOS.API/Services/JournalEntryService.cs](src/RetailPOS.API/Services/JournalEntryService.cs), [tests/RetailPOS.Tests/Accounting/FinancialPeriodWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/FinancialPeriodWorkflowHttpTests.cs) |
| API-05 | Manual-journal attachments are supported and audited | Passed | [src/RetailPOS.Application/AccountingJournals/Commands/JournalAttachmentCommands.cs](../../src/RetailPOS.Application/AccountingJournals/Commands/JournalAttachmentCommands.cs), [src/RetailPOS.Infrastructure/AccountingJournals/JournalAttachmentStore.cs](../../src/RetailPOS.Infrastructure/AccountingJournals/JournalAttachmentStore.cs), [src/RetailPOS.Domain/Entities/JournalEntryAttachment.cs](../../src/RetailPOS.Domain/Entities/JournalEntryAttachment.cs) |

## 3) Authorization and permission governance

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| AUTH-01 | Canonical `accounting.manual_journals.*` permission family is registered and exposed by policy | Passed | [src/RetailPOS.API/Authorization/PermissionCatalog.cs](src/RetailPOS.API/Authorization/PermissionCatalog.cs) |
| AUTH-02 | Role and permission regression coverage remains aligned | Passed | [tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs](tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs) |

## 4) Data model and deployment readiness

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| DATA-01 | Workflow metadata and attachments are persisted by EF migration | Passed | [src/RetailPOS.Infrastructure/Data/Migrations](src/RetailPOS.Infrastructure/Data/Migrations) |
| DATA-02 | Existing businesses receive safe defaults for workflow mode and attachment size limits | Passed | [src/RetailPOS.Infrastructure/Data/Migrations](src/RetailPOS.Infrastructure/Data/Migrations) |

## 5) Frontend UX and client compatibility

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| UI-01 | Manual journals are visible through finance routes, menu, and service layer | Passed | [retailpos-frontend/src/app/app.routes.ts](retailpos-frontend/src/app/app.routes.ts), [retailpos-frontend/src/app/models/menu.model.ts](retailpos-frontend/src/app/models/menu.model.ts), [retailpos-frontend/src/app/services/accounting.service.ts](retailpos-frontend/src/app/services/accounting.service.ts) |
| UI-02 | List, create, detail, and workflow actions are available for manual journals | Passed | [retailpos-frontend/src/app/pages/accounting/manual-journal-list.component.ts](retailpos-frontend/src/app/pages/accounting/manual-journal-list.component.ts), [retailpos-frontend/src/app/pages/accounting/manual-journal-form.component.ts](retailpos-frontend/src/app/pages/accounting/manual-journal-form.component.ts), [retailpos-frontend/src/app/pages/accounting/manual-journal-detail.component.ts](retailpos-frontend/src/app/pages/accounting/manual-journal-detail.component.ts) |

## 6) Test evidence

| Test ID | Scope | Status | Evidence |
|---|---|---|---|
| TEST-01 | Backend manual-journal workflow lifecycle and attachment handling | Passed | [tests/RetailPOS.Tests/Accounting/ManualJournalWorkflowServiceTests.cs](tests/RetailPOS.Tests/Accounting/ManualJournalWorkflowServiceTests.cs), [tests/RetailPOS.Tests/Accounting/ManualJournalWorkflowHttpTests.cs](tests/RetailPOS.Tests/Accounting/ManualJournalWorkflowHttpTests.cs) |
| TEST-02 | Manual-journal audit events and configuration round-trip | Passed | [tests/RetailPOS.Tests/Accounting/ManualJournalAuditTests.cs](tests/RetailPOS.Tests/Accounting/ManualJournalAuditTests.cs), [tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs](tests/RetailPOS.Tests/Accounting/AccountingConfigurationHttpTests.cs) |
| TEST-03 | Broader accounting regression suite | Passed | [tests/RetailPOS.Tests/Accounting](tests/RetailPOS.Tests/Accounting) |

Executed commands:

1. `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj --no-restore`
2. `npm run -s build` (from `retailpos-frontend`)

Observed result summary:

- Full backend suite: 295 passed, 0 failed, 0 skipped.
- Frontend build: passed.

## 7) Final decision

RM-07 manual-journal workflow, permissions, attachments, UI, and migration support are accepted and ready for approval gate.
