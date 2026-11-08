# Accounting Operational Controls, Permissions and Audit Gap Assessment

Updated: 2026-09-06
Status: Accounting Operational Controls, Permissions & Audit Gap Assessment Complete. Operational Readiness Remediation Slice 1 - Accounting Manager Role & Governance Audit Controls is implemented and ready for bounded review.

## 1. Executive Summary

The Accounting & Finance module is financially coherent and materially protected by the accepted Journal Engine architecture. Phase 1, Phase 2, Phase 3 and P3-08 reporting/hardening evidence confirm that active authoritative financial posting paths use `JournalEntry`/`JournalEntryLine` or accepted operational subledger state linked to journals. P3-08 legacy write guards also prevent the previously identified `Account`/`Transaction`/`Bill` bypass from creating a second financial truth.

Operational readiness is **Partially Verified**. Menu/route/backend permission alignment is generally strong for Accounting screens and reports; route guards are present, backend policies exist, report exports reuse the same permission boundary, and representative tenant/permission tests pass. The main readiness gaps are operational-control gaps around role composition and audit coverage, not accounting calculation defects.

Key findings:

| Finding | Classification | Severity | Blocks operational readiness? | Summary |
| --- | --- | --- | --- | --- |
| No dedicated `Accounting Manager` role exists in seed assets/onboarding; `AccountsAdmin` exists but is broad and includes maker/checker permissions together. | Missing | Medium | No, if an equivalent custom role is manually configured before rollout | Need a bounded role-composition decision/seed for Accounting Manager with segregation-of-duties boundaries. |
| Role/user create/update/delete and permission assignment/removal are not explicitly audited via `IAuditService`; services log only. | Missing | High | Yes for environments requiring mandatory admin-permission change audit | Finance permission assignment/removal is a material control action and needs actor/action/before-after audit evidence. |
| Several bank/reconciliation/settlement actions lack explicit audit events even though they mutate controlled accounting state. | Missing | High | Yes for those workflows before operational rollout | Journal entries prove postings, but they do not prove user action history for reconciliation/settlement controls. |
| Issued access tokens retain permissions until refresh/re-login; `/auth/me` and refresh recompute from DB. | Accepted Limitation | Medium | No if token lifetime is accepted/documented | Current JWT model is claim-based; revocation is not immediate for already issued tokens. |
| Retained legacy Account/Transaction/Bill reads remain route-accessible but hidden from active finance navigation and labeled read-only. | Accepted Limitation | Low | No | Backend write rejection is authoritative; reads remain for historical evidence and Supplier Payment compatibility. |

No cross-Business accounting data exposure was demonstrated by the assessment tests. No migration is required for the assessment or for the recommended remediation slices unless a future remediation elects to seed a dedicated role through EF migration rather than SQL/seed update.

## 2. Menu/Submenu Inventory

| Parent | Menu label | Route | Component | Frontend permission | Guard | Backend surface | Backend policy | State | Consistency |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Finance | Accounting Configuration | `/settings/accounting` | `AccountingConfigurationComponent` | `accounting.configuration.view` | `routePermissionGuard` | `/api/accounting/configuration`, `/api/accounting/configuration/outlets`, `/api/accounting/configuration/resolve` | view/edit split | Active | Verified; menu view maps to route view, mutation endpoints require edit. |
| Finance | Financial Years / Periods | `/financial-years` | `FinancialYearsComponent` | `accounting.financial_years.view`, `accounting.periods.view` | `routePermissionGuard` | `/api/accounting/financial-years`, `/api/accounting/periods` | view/manage/close/reopen split | Active | Verified; route grants screen view, backend enforces action policies. |
| Finance | Opening Balance | `/opening-balance` | `OpeningBalanceComponent` | `accounting.opening_balances.post`, `accounting.opening_balances.adjust` | `routePermissionGuard` | `/api/accounting/opening-balances` | post/adjust | Active | Partially Verified; route currently requires posting/adjustment permissions, so a read-only status viewer role is not supported. |
| Finance | Expenses | `/expenses` plus create/edit routes | `ExpensesListComponent`/`ExpenseFormComponent` | `accounting.expenses.view/create/edit` | `routePermissionGuard` | `/api/accounting/expenses` | view/create/edit/submit/approve/reject/cancel | Active | Verified. |
| Finance | Expense Categories | `/expense-categories` | `ExpenseCategoriesComponent` | `accounting.expenses.view` | `routePermissionGuard` | `/api/accounting/expenses/categories` | view/edit | Active | Partially Verified; category mutations use `accounting.expenses.edit`, route is view-level. |
| Finance | Supplier Payments | `/supplier-payments` | `SupplierPaymentsComponent` | `accounting.supplier_payments.view`, `accounting.supplier_advances.view` | `routePermissionGuard` | `/api/accounting/supplier-payments`, `/api/accounting/supplier-advances`, utilization | view/create/submit/approve/reject/cancel/reverse | Active | Verified; backend has separate action policies. |
| Finance | Other Income | `/incomes` plus create/edit routes | `OtherIncomeListComponent`/`OtherIncomeFormComponent` | `accounting.other_income.view/create/edit` | `routePermissionGuard` | `/api/accounting/incomes` | view/create/edit/submit/approve/reject/cancel | Active | Verified. |
| Finance | Manual Journals | `/manual-journals` plus create/edit/detail | `ManualJournalList/Form/DetailComponent` | `accounting.manual_journals.view/create/update` | `routePermissionGuard` | `/api/accounting/journals` | manual_journals create/update/submit/approve/reject/cancel/post/reverse | Active | Verified; backend action-level policies are independent. |
| Finance | Bank Reconciliation | `/accounting/reconciliation` | `BankReconciliationComponent` | `accounting.reconciliation.view` | `routePermissionGuard` | `/api/accounting/reconciliation/*`, `/api/accounting/bank-statements/*` | reconciliation/statement/adjustment/exception split | Active | Partially Verified; menu exposes workbench at view permission, backend enforces create/match/close/adjust action permissions. |
| Finance | Close Workbench | `/accounting/close-workbench` | `AccountingCloseWorkbenchComponent` | `accounting.close.audit.view` | `routePermissionGuard` | `/api/accounting/period-close-runs/*`, `/api/accounting/financial-year-close-runs/*` | close prepare/approve/execute/reopen/audit | Active | Verified; route is audit/view, actions are backend guarded. |
| Reporting | General Ledger | `/reports/general-ledger` | `AccountingReportsComponent` | `accounting.reports.general_ledger` | `routePermissionGuard` | `/api/accounting/reports/general-ledger`, enhanced GL | `accounting.reports.general_ledger` | Active | Verified. |
| Reporting | Trial Balance | `/reports/trial-balance` | `AccountingReportsComponent` | `accounting.reports.trial_balance` | `routePermissionGuard` | `/api/accounting/reports/trial-balance` | `accounting.reports.trial_balance` | Active | Verified. |
| Reporting | Profit & Loss | `/reports/profit-loss` | `AccountingReportsComponent` | `accounting.reports.profit_loss` | `routePermissionGuard` | `/api/accounting/reports/profit-loss`, comparative | `accounting.reports.profit_loss` | Active | Verified. |
| Reporting | Profitability | `/reports/profitability` | `ProfitabilityReportComponent` | `accounting.reports.profitability` | `routePermissionGuard` | business/outlet/product profitability + export | `accounting.reports.profitability` | Active | Verified. |
| Reporting | Cost of Goods Sold | `/reports/cogs` | `ValuationReportsComponent` | `accounting.reports.cogs` | `routePermissionGuard` | `/api/accounting/reports/cogs` | `accounting.reports.cogs` | Active | Verified. |
| Reporting | Inventory Valuation | `/reports/inventory-valuation` | `ValuationReportsComponent` | `accounting.reports.inventory_valuation` | `routePermissionGuard` | `/api/accounting/reports/inventory-valuation` | `accounting.reports.inventory_valuation` | Active | Verified. |
| Reporting | Cash Book | `/reports/cash-book` | `CashBookReportComponent` | `accounting.reports.cash_book` | `routePermissionGuard` | `/api/accounting/reports/cash-book` | `accounting.reports.cash_book` | Active | Verified. |
| Reporting | Cash Flow | `/reports/cash-flow` | `CashFlowReportComponent` | `accounting.reports.cash_flow` | `routePermissionGuard` | cash-flow, comparative, export | `accounting.reports.cash_flow` | Active | Verified. |
| Reporting | Balance Sheet | `/reports/balance-sheet` | `BalanceSheetReportComponent` | `accounting.reports.balance_sheet` | `routePermissionGuard` | balance-sheet, comparative | `accounting.reports.balance_sheet` | Active | Verified. |
| Reporting | AP Subledger | `/reports/ap-subledger` | `ApReportComponent` | `accounting.reports.ap_subledger` | `routePermissionGuard` | `/api/accounting/reports/ap-subledger` | `accounting.reports.ap_subledger` | Active | Verified. |
| Reporting | AP Aging | `/reports/ap-aging` | `ApReportComponent` | `accounting.reports.ap_aging` | `routePermissionGuard` | `/api/accounting/reports/ap-aging` | `accounting.reports.ap_aging` | Active | Verified. |
| Reporting | Supplier Statement | `/reports/supplier-statement` | `ApReportComponent` | `accounting.reports.supplier_statement` | `routePermissionGuard` | `/api/accounting/reports/supplier-statement` | `accounting.reports.supplier_statement` | Active | Verified. |
| Reporting | AP Reconciliation | `/reports/ap-reconciliation` | `ApReportComponent` | `accounting.reports.ap_reconciliation` | `routePermissionGuard` | AP and supplier advance reconciliation reports | `accounting.reports.ap_reconciliation` | Active | Verified. |
| Reporting | VAT Summary/Details/Reconciliation/Exceptions | `/reports/vat-*` | `VatReportComponent` | `accounting.reports.vat` | `routePermissionGuard` | `/api/accounting/reports/vat-*` | `accounting.reports.vat` | Active | Verified. |
| Hidden retained legacy | Legacy Accounts | `/accounts` | `AccountsListComponent` | `accounting.chart_accounts.view` | `routePermissionGuard` | `/api/accounts` GET only effectively usable | `accounts.view` alias | Hidden/read-only | Accepted Limitation; route remains for historical read evidence, menu hidden, backend writes blocked. |
| Hidden retained legacy | Legacy Transactions | `/transactions` | `TransactionsListComponent` | `accounting.journals.view` | `routePermissionGuard` | `/api/transactions` GET/search/ledger | `transactions.view` alias | Hidden/read-only | Accepted Limitation; route remains for historical read evidence, menu hidden, backend writes blocked. |
| Hidden retained legacy | Legacy Bills | `/bills` | `BillsListComponent` | `accounting.chart_accounts.view` | `routePermissionGuard` | `/api/bills` GET/search/summary | `accounts.view` alias | Hidden/read-only | Accepted Limitation; route remains for Supplier Payment compatibility/historical evidence, backend writes blocked. |
| Not separately navigated | Bank Accounts | Embedded in reconciliation/configuration UI | `BankReconciliationComponent` and service calls | reconciliation/configuration permissions | `routePermissionGuard` | `/api/accounting/reconciliation/bank-accounts`, settlement bank account reads | `accounting.reconciliation.view` and tenant checks | Active embedded | Partially Verified; no standalone menu item. |
| Not separately navigated | Bank Settlement / Liquidity Transfer | Embedded in bank reconciliation workflow | `BankReconciliationComponent` | reconciliation/settlement permissions | `routePermissionGuard` | `/api/accounting/settlements`, `/api/accounting/liquidity-transfers` | settlement/liquidity split policies | Active embedded | Partially Verified; no standalone menu item. |
| Not separately navigated | Period Close / Year-End Close | Close Workbench tabs | `AccountingCloseWorkbenchComponent` | `accounting.close.audit.view` plus action policies | `routePermissionGuard` | period/year close run controllers | close split policies | Active embedded | Verified. |

## 3. Menu, Route and Permission Consistency

Verified:

- Accounting routes in `app.routes.ts` consistently use `routePermissionGuard` with canonical `accounting.*` permissions for active Accounting screens.
- Backend controllers independently enforce authorization policies. Representative tests cover configuration, reports, Cash Flow, profitability, legacy guards, tenant isolation and permission catalog alignment.
- Report export/download endpoints use the same report-level policy as the corresponding report for Cash Flow, Profitability, close evidence packages, audit export and inventory exports where inspected.
- Obsolete legacy write workflows are no longer exposed from active Finance navigation and are blocked backend-side.

Partially Verified / gaps:

- Hidden legacy routes `/accounts`, `/transactions`, and `/bills` are still directly route-accessible with view permissions. Their UI is read-only and backend writes are blocked, so this is an accepted historical-evidence limitation rather than a write-bypass defect.
- Bank Account and Settlement features are operationally embedded in the reconciliation component rather than exposed as standalone menu items. This is usable but less discoverable for an Accounting Manager role.
- Opening Balance route visibility requires `post` or `adjust`, so there is no pure read/status role for opening-balance review.

## 4. Accounting Manager Role Findings

Classification: `Missing`
Severity: `Medium`
Operational readiness blocker: **No, if an equivalent manually configured finance role is created before rollout; Yes for turnkey seeded-role rollout.**

Evidence:

- `database/seed-data.sql`, `database/seed-v2-roles.sql`, `database/add-accounting-v2-permissions.sql`, and `BusinessOnboardingService` define/use `AccountsAdmin`, `BusinessOwner`, `BusinessAdmin`, `OutletManager`, `WarehouseManager`, and `SalesPerson` but no `Accounting Manager` or `AccountingManager` role.
- Assessment test `SeedAssets_DoNotDefineDedicatedAccountingManagerRole_ButDoDefineAccountsAdmin` passed and characterizes this state.
- `AccountsAdmin` has broad accounting permissions including create/submit/approve/post/reverse style permissions in several areas. It is not a clean segregation-of-duties Accounting Manager role.

Minimum proposed composition, not implemented:

- View/report permissions: `accounting.dashboard.view`, `accounting.configuration.view`, accounting report permissions, AP/VAT/Cash Flow/Profitability report permissions.
- Operational maker permissions where appropriate: expense/other-income/supplier-payment/manual-journal create/update/submit; statement import/create reconciliation session/match proposal.
- Controlled approval permissions should be split by policy: approve/post/reverse/close/reopen should not all be bundled by default. Suggested split: `AccountingManager` (prepare/review/submit/match) and `AccountingApprover` or owner/admin approval role (approve/post/reverse/close/reopen/override exception).
- No unrelated `users.*`, `roles.*`, `settings.edit`, `businesses.*`, unrestricted `*`, or cross-Business administration permissions.

Recommended remediation slice: **OC-01 Role Composition and Seed Alignment**.

## 5. User Creation and Role Assignment Findings

Classification: `Partially Verified`
Severity: `Medium`
Operational readiness blocker: **No, provided an approved equivalent finance role is available.**

Evidence:

- `UsersController` enforces `users.view` for listing/read and `users.create`/`users.edit`/`users.delete` for mutation.
- `UserService.CreateUserAsync` validates role existence, tenant business scope, outlet/warehouse assignment scope, user limits and role-assignment policy.
- Assessment test `DisposableFinanceUser_CanBeCreatedAssignedFinanceRoleLoginAndAccessPermittedReportOnly` created a disposable finance user in isolated test data, logged in through `/api/auth/login`, confirmed finance report permissions, allowed Cash Flow report access, and denied `/api/users`.

Gap:

- The flow proves role assignment with an `AccountsAdmin` equivalent role, not a seeded `Accounting Manager`, because that role does not exist.
- User/role mutations are not explicitly audited with before/after permission changes. See audit findings.

## 6. Role and Permission Change / Revocation Behavior

Classification: `Accepted Limitation` with one audit gap
Severity: `Medium` for token behavior; `High` for missing permission-change audit
Operational readiness blocker: **Token behavior no, audit gap yes where admin-permission audit is mandatory.**

Evidence:

- `TokenService.GenerateAccessToken` emits each effective permission as a `permission` claim.
- `Program.cs` authorization policies check only the current principal claims and `PermissionCatalog.ExpandClaims`; they do not re-query role permissions on every request.
- `AuthService.GetCurrentUserAsync` and `RefreshTokenAsync` recompute permissions from the current DB role/user override state.
- Assessment test `RolePermissionRemoval_AffectsRefreshAndMe_ButPreviouslyIssuedAccessTokenClaimsRemainStatic` passed: after removing a permission from a role, `/auth/me`/refresh behavior recomputed permissions, but the already issued access token still contained the removed permission claim until refreshed/replaced.
- Angular `AuthService` stores `current_user` in localStorage and refreshes menu state only when login/logout/current-user refresh updates the BehaviorSubject.

Risk:

- Removed permissions can remain effective until access-token expiration if the old token remains valid. This is typical JWT behavior but must be documented and bounded by token lifetime.

Smallest future correction options:

- Document token-lifetime revocation semantics as accepted policy.
- For stronger revocation, add a user/role permission version or token-issued-at invalidation check to auth middleware. This would be a bounded auth hardening task, not Phase 4 feature scope.

## 7. Business/Tenant and Outlet Scope Findings

Classification: `Verified` for representative Accounting paths; `Partially Verified` for exhaustive matrix
Severity: `Low` residual risk due breadth
Operational readiness blocker: **No evidence of bypass found.**

Verified evidence:

- Configuration tests reject unauthorized outlet override access.
- Report tests verify unauthorized report locations are rejected and other-business report data is not leaked.
- Legacy Account/Transaction tenant tests preserve tenant-scoped reads and block writes.
- P3-08 Cash Flow and Profitability tests include tenant isolation.
- AP, VAT, reconciliation, close, and bank statement controllers use `_tenant.RequireBusinessId()` and/or `IUserOutletAccessService` in inspected paths.

Limitations:

- Not every single report/export/action endpoint has a bespoke cross-Business test. Existing representative tests and common service patterns provide strong evidence, but this remains `Partially Verified` for the full endpoint universe.

## 8. Accounting Audit Coverage Matrix

| Area/action | Audit status | Classification | Evidence / gap |
| --- | --- | --- | --- |
| Accounting configuration update | No explicit `IAuditService` in `AccountingConfigurationService`/controller | Missing | High severity; material control-account changes need actor/before-after audit. |
| Outlet accounting override changes | No explicit `IAuditService` in configuration service/controller | Missing | High; affects posting account resolution. |
| Chart Account create/update/status | No explicit `IAuditService` in `ChartAccountService` | Missing | High; chart mutations affect posting/reporting. |
| Financial Year create/update/activate/close/reopen | Explicit audit and failure audit in `FinancialPeriodService` | Verified | `FinancialPeriodAuditTests` pass. |
| Accounting Period create/update/lock/close/reopen | Explicit audit and failure audit in `FinancialPeriodService` | Verified | `FinancialPeriodAuditTests` pass. |
| Bank Account create | Explicit audit added in Slice 1 remediation and verified by focused test | Verified | `BankAccountService.CreateAsync` records actor, timestamp, entity ID, Business scope and non-sensitive created values; `AccountReference` is not copied into field-change evidence. |
| Bank Account update/status/link/scope/delete | No supported mutation surface exists | Not Applicable | No `UpdateAsync`/deactivate/delete method exists in `BankAccountService`, no controller route exists, and Angular only reads bank accounts. Corrections require a future approved Bank Account maintenance surface. |
| Role create/update/delete and permission assignment/removal | No explicit `IAuditService` in `RoleService`; controller logs errors only | Missing | High; admin permission changes require before/after audit. |
| User create/update/deactivate/role assignment | No explicit `IAuditService` in `UserService`; controller logs errors only | Missing | High; role assignment/removal is a material access-control event. |
| Opening Balance posting/adjustment | Journal audit via `JournalEntryService` | Verified | `OpeningBalanceAuditTests` pass; source/re-post adjustment journal linkage is retained. Direct reversal/update are not supported for historical posted opening balances. |
| Expense create/update/submit/approve/reject/cancel/delete | Explicit audit in `ExpenseService` | Verified | Slice 2 patching review verifies actor/Business via HTTP audit context, approval `JournalEntryId`, rejection/cancellation reason and material amount/account/VAT before-after update fields. Reverse is not supported. |
| Other Income create/update/submit/approve/reject/cancel | Explicit audit in `OtherIncomeService` | Verified | Slice 2 patching review verifies actor/Business via HTTP audit context, approval `JournalEntryId`, rejection/cancellation reason and material amount/account/VAT before-after update fields. Delete/reverse are not supported. |
| Manual Journal create/update/submit/approve/reject/cancel/post/reverse | Explicit audit and failure audit in `JournalEntryService` plus attachment audit | Verified | Slice 2 patching review verifies lifecycle before-after status, actor metadata, Business, references, line-total change summary, reason and posted/reversal journal linkage. Delete/reopen are not supported. |
| Sale financial posting / void/refund | Explicit audit added/verified in Slice 2 | Verified | Sale completion audit includes posted sale `JournalEntryId`; void/refund audit includes actor, reason, status transition and reversal journal linkage when accounting services are configured. |
| GRN completion and purchase receipt posting | Explicit audit added/verified in Slice 2 | Verified | GRN create/completion audit records actor, Business/Warehouse/Supplier/PO/GRN scope, received quantities/amounts and posted purchase receipt `JournalEntryId`; duplicate completion does not write a second success audit. |
| Supplier Payment / Supplier Advance / utilization / reversal | Explicit audit added/verified in Slice 2 | Verified | Payment, advance and advance-utilization lifecycle/reversal audit records actor, source entity, allocation/utilization details and journal/reversal journal linkage. Unsupported update/reject surfaces remain Not Applicable. |
| Stock Adjustment / Stock Count posting | Explicit audit verified and hardened in Slice 2 | Verified / Partially Audited for non-financial Stock Count lifecycle | Stock adjustment lifecycle audit exists; approval/reversal metadata includes posted/reversal journal references. Stock Count financial correction routes through generated Stock Adjustment with `SourceStockCountId`; direct Stock Count financial posting/reversal is not supported. Non-financial Stock Count lifecycle persists actor/status fields but has no explicit audit event in Slice 2. |
| Cash Session open/close/variance | Explicit audit verified and hardened in Slice 2 | Verified | Close audit now includes `varianceJournalEntryId` when a cash surplus/shortage posting is created; already-closed retry remains no-second-success-audit. |
| Bank transfer and settlement posting/reversal | No explicit audit in `SettlementTransferService` | Missing | High; settlement/transfer controls need action audit separate from journal. |
| Statement import | No explicit audit in `BankStatementImportService`; controller no audit | Missing | High; imported bank evidence changes reconciliation basis. |
| Match/unmatch | No explicit audit in `BankReconciliationService` | Missing | High; reconciliation evidence changes. |
| Reconciliation close/reopen | No explicit audit in `BankReconciliationService` | Missing | High; controlled close/reopen should capture reason/outcome. |
| Exception classification/resolution | Controller mutates directly without audit | Missing | High; exception handling is controlled financial evidence. |
| Bank adjustment proposal/approval/reversal | No explicit audit in `BankStatementAdjustmentService` | Missing | High; approval/reversal produces journaled financial effects. |
| Period close/reopen and exception approval | Explicit audit in `AccountingCloseService` | Verified | Close-workbench tests and source inspection. |
| Year-end close/reopen/re-close | Explicit audit in `AccountingFinancialYearCloseService` | Verified | Source inspection and P3-07 tests. |
| Legacy finance write rejection attempts | Controllers log warning but do not write audit events | Optional Enhancement / Partially Missing | Security-relevant rejected writes may merit audit; not required to preserve financial truth because writes are blocked. |
| Report viewing | No mandatory audit identified | Optional Enhancement | Treat as report-access telemetry unless policy requires. |
| Report/export download | Audit export and close packages have audit/evidence; accounting report CSV exports generally not audited | Optional Enhancement / Policy Decision | Sensitive financial export telemetry may be desirable but no field masking/report-access policy is frozen. |

## 9. Representative Audit Test Evidence

Executed evidence:

- `AuditServiceTests`: proves audit context, status, entity field changes, redaction and failure status support.
- `AuditScopeTests`: proves grouped audit scopes and failed scope behavior.
- `AuditFieldRedactionTests`: proves sensitive-field redaction behavior.
- `ManualJournalAuditTests`: proves manual journal submit/approve and attachment audit events.
- `OpeningBalanceAuditTests`: proves opening balance and adjustment posting write journal/audit events.
- `FinancialPeriodAuditTests`: proves accounting period status changes write Accounts audit events.

Unproven or missing representative audit coverage after Slice 2:

- Accounting configuration change audit.
- Chart Account change audit.
- Bank reconciliation match/unmatch/close/reopen/exception/adjustment audit.
- Role/permission assignment/removal audit.

New Slice 2 representative audit evidence:

- Manual Journal lifecycle audit payload test with create/update/submit/approve/post/reverse/cancel evidence.
- Opening Balance post and adjustment audit tests; direct reverse/update remain unsupported by design.
- Expense and Other Income audit payload tests for material amount/account/VAT changes, actor/Business HTTP context and forbidden-action no-success-audit behavior.
- Supplier Payment/Advance lifecycle/utilization/reversal audit tests.
- GRN completion audit and duplicate completion no-second-success-audit test.
- Sale completion/void/refund/exchange-linkage audit tests; Return is handled through Refund/Void return inventory movements, not as a separate workflow.
- Cash Session variance close audit journal-link test.
- Stock Adjustment approval audit journal-link test and Stock Count generated-adjustment source-link test.
- Combined Slice 2 patching-review backend bundle passed 91/91.
- Full backend regression suite passed 496/496 and Backend Release build passed.

These are not calculation defects, but they are operational-control gaps for production finance operations.

## 10. Identified Gaps and Defects

| ID | Classification | Severity | Evidence | Impact | Blocks readiness? | Smallest correction | Migration? | Slice |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| OC-GAP-01 | Missing | Medium | No `Accounting Manager` role in seeds/onboarding; assessment test confirms absence. | Operators lack a clean, segregated accounting role; `AccountsAdmin` may be too broad. | No with manual role workaround; yes for turnkey rollout. | Add approved role seed/composition and role-form grouping; define maker/checker split. | Usually no; SQL seed update sufficient. | OC-01 |
| OC-GAP-02 | Missing | High | No explicit audit in `RoleService`/`UserService`; permission-change test proves role changes alter effective auth. | Finance permission changes lack mandatory actor/before-after evidence. | Yes where admin-change audit is mandatory. | Add audit events to user/role create/update/delete/activate/deactivate with changed role/permissions. | No. | OC-02 |
| OC-GAP-03 | Partially Remediated | Medium | Slice 1 now audits configuration, outlet override, chart-account and Bank Account create changes. Bank Account update/status/link/scope/delete are not applicable because no supported mutation surface exists. | Remaining risk is limited to later banking/reconciliation transaction workflows, not Bank Account master-data create. | No for Slice 1. | Keep future Bank Account update/status audit tied to any future approved maintenance surface. | No. | OC-02 |
| OC-GAP-04 | Missing | High | No explicit audit in reconciliation/import/adjustment/settlement services. | Bank evidence and reconciliation decisions lack actor/reason/change history. | Yes before full bank-ops rollout. | Add audit events for import, match/unmatch, close/reopen, exception, adjustment, settlement/transfer post/reverse. | No. | OC-03 |
| OC-GAP-05 | Remediated in Slice 2 | High | `SupplierPaymentService` now records explicit audit for payment/advance create, submit, approve/post, reverse, advance utilization and utilization reversal; focused tests verify journal linkage and duplicate reverse no-second-success-audit. | AP operational actions now have action audit separate from journal posting. | No for supported Supplier Payment/Advance surfaces. | Unsupported update/reject surfaces remain Not Applicable until implemented. | No. | OC-03 |
| OC-LIM-01 | Accepted Limitation | Medium | JWT permission claims remain static until token refresh; assessment test confirms. | Removed permissions may remain until token expiry. | No if documented and token lifetime is accepted. | Document behavior, or add permission-version/token invalidation later. | Possibly yes only for versioning approach. | OC-04 optional hardening |
| OC-LIM-02 | Accepted Limitation | Low | Hidden `/accounts`, `/transactions`, `/bills` routes remain read-only. | Historical legacy evidence is accessible to users with view aliases. | No. | Optionally move to an explicit Legacy Finance History route/permission. | No. | OC-04 optional hardening |

No `Critical` cross-tenant exposure or active financial-write bypass was demonstrated.

## 11. Risk/Severity Classification

- `Critical`: none found.
- `High`: missing mandatory audit coverage remains for reconciliation/settlement controlled actions until Slice 3. User/role, configuration/chart/bank-account create and supported supplier payment/AP actions have been remediated by Slice 1/Slice 2.
- `Medium`: missing dedicated Accounting Manager role and documented JWT revocation semantics.
- `Low`: retained legacy read-only routes and embedded discoverability for bank-account/settlement functions.

## 12. Recommended Bounded Remediation Slices

Remediation status update (2026-09-06): **OC-01, Slice 1 governance/master-data audit controls and Slice 2 core financial transaction audit controls are implemented.** The original assessment findings below are retained as historical baseline evidence; see `ACCOUNTING_OPERATIONAL_READINESS_REMEDIATION_PLAN.md` for final Slice 1/Slice 2 permission composition, audit surfaces, validation results and retained limitations.

**OC-01 - Accounting Manager Role and Segregation Policy**

- Define and seed `AccountingManager` or `Accounting Manager` role.
- Decide maker/checker split for prepare/submit vs approve/post/reverse/close/reopen.
- Update SQL seed assets, `BusinessOnboardingService`, role-form grouping and tests.
- No migration expected unless role seeding is required through EF migration policy.

**OC-02 - Master Data and Access-Control Audit Coverage**

- Add explicit audit events for role create/update/delete, user create/update/deactivate/reactivate, permission override changes, role assignment changes, accounting configuration, outlet overrides, chart account changes, and bank account changes.
- Include actor, business, entity, before/after permission/account fields, and success/failure where applicable.
- No migration expected.

**OC-03 - Financial Operations Audit Coverage**

- Add explicit audit events for Supplier Payment/Advance lifecycle, GRN completion action, statement import, match/unmatch, reconciliation close/reopen, exception classification/resolution, bank adjustment proposal/approval/reversal, settlement post/reverse, and liquidity transfer post/reverse.
- Keep journals as posting evidence; audit logs should capture user action/reason/outcome.
- No migration expected.

**OC-04 - Optional Token/Legacy UX Hardening**

- Document JWT revocation semantics or add permission-version/token invalidation if immediate revocation becomes policy.
- Optionally move retained legacy read-only routes under a clearly named Legacy Finance History view/permission.
- This is not a Phase 4 blocker unless immediate revocation is mandated.

## 13. Proposed Phase 4 Boundary

Phase 4 should not start by adding more accounting reports. Recommended order:

1. Complete OC-01 to define operational finance roles.
2. Complete OC-02/OC-03 audit coverage for mandatory operational controls.
3. Decide whether OC-04 token invalidation is required by security policy.
4. Then begin Phase 4 only for separately approved business capabilities, clearly outside operational-control remediation.

Phase 4 boundary candidates, separate from remediation:

- Future budgeting/targets, if approved.
- Future shared-cost allocation methodology, if approved.
- Future field/value financial disclosure matrix and masking, if approved.
- Future physical legacy-table decommissioning/data normalization, if approved.

## 14. Validation Results

Assessment tests added:

- `AccountingOperationalControlsAssessmentTests.SeedAssets_DefineDedicatedAccountingManagerRole_WithBoundedSegregatedPermissions`
- `AccountingOperationalControlsAssessmentTests.DisposableFinanceUser_CanBeCreatedAssignedFinanceRoleLoginAndAccessPermittedReportOnly`
- `AccountingOperationalControlsAssessmentTests.RolePermissionRemoval_AffectsRefreshAndMe_ButPreviouslyIssuedAccessTokenClaimsRemainStatic`

Executed validation:

- Focused assessment characterization tests: 3/3 passed.
- Focused backend operational-control evidence tests (assessment, permission, configuration auth, financial report auth, legacy guard, tenant isolation, representative audit tests): 76/76 passed.
- Focused Angular route/menu/read-only tests: 16/16 passed.
- Full backend regression suite: 487/487 passed.
- Backend Release build: passed.
- Full Angular test suite: 57/57 passed.
- Angular production build: passed.
- `git diff --check`: passed with CRLF normalization warnings only.

Post-remediation Slice 1 patching review validation:

- Focused Bank Account audit test: 1/1 passed.
- Bank Account/service, permission/tenant, legacy-guard and Slice 1 regression bundle: 35/35 passed.
- Full backend regression suite: 491/491 passed.
- Backend Release build: passed.
- No Angular code changed during the Bank Account patching review; Angular tests/build were not rerun for this bounded patch.

## 15. Non-Blocking Advisories

Observed during focused backend validation:

- Existing `SQLitePCLRaw.lib.e_sqlite3` NU1903 package advisory.
- Existing xUnit analyzer warnings in `P306AdjustmentRelationalAtomicityTests` for unused theory parameters.

These advisories are not introduced by this assessment and do not change the operational-control findings.

## Final Status

Accounting Operational Controls, Permissions & Audit Gap Assessment Complete - Ready for Scope Decision

## Slice 2 Patching Review Evidence Supplement (2026-10-03)

This supplement leaves the original assessment history above unchanged. It records the bounded Slice 2 evidence refresh; the detailed per-action lifecycle matrix is maintained in the [Accounting Operational Readiness Remediation Plan](./ACCOUNTING_OPERATIONAL_READINESS_REMEDIATION_PLAN.md).

| Workflow | Supported action | Classification | Actor | Status / before-after | Journal linkage | Test evidence |
| --- | --- | --- | --- | --- | --- | --- |
| Supplier Payment | Create, submit, approve/post, reverse | Audited and Verified | Initiating user retained in audit metadata; Business and audit timestamp supplied by request audit context. | Draft -> Submitted -> Posted -> Reversed; amount, allocation changes and reversal reason captured. | Payment source ID links original and reversal Journal Entries; payment allocation history is retained. | `SupplierPaymentWorkflowServiceTests.ApproveAsync_PostsPaymentJournal_AndCreatesAdvanceForOverpayment`; `SupplierPaymentWorkflowServiceTests.ReverseAsync_ReversesPostedPaymentAndEffectiveAllocationWithoutDeletingHistory`; `P302SupplierPaymentAllocationAtomicityTests`; `P302SupplierPaymentReversalAtomicityTests`. |
| Supplier Advance | Create, submit, approve/post, apply utilization, reverse utilization | Audited and Verified | Initiating user retained for advance and utilization actions. | Draft -> Submitted -> Posted; utilization amount/status and reversal reason captured. | Advance, utilization, payable and journal/reversal references retained. | `SupplierPaymentWorkflowServiceTests.CreateAdvanceAsync_AndApproveAdvance_PostsAdvanceJournal`; `SupplierPaymentWorkflowServiceTests.ApplyAdvanceAsync_AndReverseUtilization_RecordAuditWithJournalLinkage`; `P302SupplierAdvanceUtilisationAtomicityTests`. |
| GRN | Create/complete; duplicate completion and retry | Audited and Verified | Controller requires an authenticated user ID; the same completing actor is passed through the service to stock ledger, payable projection, and Journal Entry. No creator fallback or fabricated `0` actor remains. | Full -> Completed; receipt quantities, amounts, VAT and PO status recorded; completed retry does not create a second success audit. | GRN source reference links the authoritative purchase receipt Journal Entry. | `GrnsControllerActorTests.Complete_PassesAuthenticatedActor_AndRejectsMissingIdentity`; `PurchaseAndGrnAccountingTests.GrnCompletion_PostsPurchaseReceiptJournal_UsingGrnUnitCost`; `P308RetryAtomicityTests.GrnCompletionRetry_DoesNotDuplicateInventoryOrAuthoritativeJournal`. |
| Sale | Complete/post, void, refund | Audited and Verified | Cashier/action actor, Business and Outlet retained; void/refund require a positive authenticated action actor and pass that actor to audit, stock ledger and Journal Entry posting. No fallback to the original cashier. | New -> Completed; Completed -> Voided/Refunded; material amount/status and reason are captured. | Sale source links posting/reversal Journal Entry; original sale is retained for refund/exchange linkage. | `SaleServiceAccountingJournalTests.CreateAsync_PostsSaleJournal_WhenAccountingServicesAreAvailable`; `SaleServiceVoidJournalTests.VoidAsync_WritesSaleVoidJournal_WithActorAndReason`; `SalesControllerActorTests.VoidAndRefund_RejectRequestsWithoutPositiveAuthenticatedUserId`; `SaleServiceVatAndExchangeAccountingTests.RefundAsync_PostsVatReversal_ForPartialRefundOnly` (action actor differs from cashier). |
| Manual Journal | Create, update, submit, approve, reject, post, reverse, cancel | Audited and Verified | Controller actor is retained in audit context and action metadata; event Business and timestamp are persisted. | Previous/new lifecycle state, material reference/line-total changes, rejection/cancellation/reversal reasons and action timestamps are recorded. | Journal ID and source references retained; reverse event links original and reversal Journal Entries. | `JournalEntryServiceTests.ManualJournalLifecycle_AuditPayloadsIncludeStatusReferencesActorsAndJournalLinks`; `ManualJournalAuditTests.Submit_Approve_And_Attachment_Actions_Write_Accounts_AuditEvents`. |
| Manual Journal | Delete; reopen/return to draft | Not Applicable – no supported action | No such controller/service mutation exists. Rejected entries can be edited/resubmitted, not reopened by a separate action. | Not applicable. | Not applicable. | `AccountingJournalsController` route inventory; `JournalEntryService` lifecycle methods. |
| Opening Balance | Prepare/create, post, correction/re-post through adjustment | Audited and Verified / Accepted System-Generated Behavior | User-initiated controller actor is passed to system-draft creation and posting. | Draft -> Posted; each correction is a new reason-bearing adjustment Journal Entry; posted historical opening record is immutable. | Opening Balance source and resulting Journal Entry are distinct references; adjustment Journal Entry has its own ID and source reference. | `OpeningBalanceAuditTests.OpeningBalanceAndAdjustment_Posting_WritesAccountsAuditEvents`; `OpeningBalanceWorkflowHttpTests`; `OpeningBalanceServiceTests`. |
| Opening Balance | Update, separate submit/validate, reverse | Not Applicable – no supported action | No update/submit endpoint; direct reversal is blocked; correction uses a new adjustment. | Historical posted record remains unchanged. | No direct reversal Journal Entry; correction is a separately linked adjustment Journal Entry. | `AccountingOpeningBalancesController` route inventory; `OpeningBalanceService.PostOpeningBalanceAsync`; `JournalEntryService.ReverseAsync` guard. |
| Expense | Create, update (amount/account/VAT), submit, approve/post, reject, cancel, draft delete | Audited and Verified | Initiating user, Business/Outlet and audit timestamp retained. | Status transitions and old/new amount, VAT, tax code, account, category, outlet and reference values captured; rejection/cancellation reasons retained. | Expense source ID/reference links to the posted Journal Entry; no journal on reject/cancel/delete. | `ExpenseWorkflowServiceTests.UpdateAsync_AuditsMaterialAmountVatAndAccountChanges`; `ExpenseWorkflowServiceTests.ApproveAsync_PostsJournal_FromCategoryMapping`; `ExpenseWorkflowHttpTests.Approve_WithoutPermission_ReturnsForbidden`. |
| Expense | Reverse | Not Applicable – no supported action | No supported reverse service/API action. | Not applicable. | Not applicable. | `ExpenseService` and `ExpensesController` route inventory. |
| Other Income | Create, update (amount/account/VAT), submit, approve/post, reject, cancel | Audited and Verified | Initiating user, Business/Outlet and audit timestamp retained. | Status transitions and old/new amount, VAT, tax code, income/payment account, outlet and reference captured; rejection/cancellation reasons retained. | Other Income source ID/reference links to posted Journal Entry; rejected/cancelled entry creates no journal. | `OtherIncomeWorkflowServiceTests.UpdateAsync_AuditsMaterialAmountVatAndAccountChanges`; `OtherIncomeWorkflowServiceTests.ApproveAsync_PostsJournal_UsingDefaultConfigurationAccount`; `OtherIncomeAtomicityTests.ApproveAsync_RollsBack_PostedJournal_WhenApprovalAuditFails`; `OtherIncomeWorkflowHttpTests`. |
| Other Income | Delete / reverse | Not Applicable – no supported action | No supported delete/reverse service/API action. | Not applicable. | Not applicable. | `AccountingOtherIncomeController` and `OtherIncomeService` route/method inventory. |
| Return | Refund/void return side; no distinct Return workflow | Accepted System-Generated Behavior | Refund/void action actor is retained; system inventory return is generated from the Sale operation. | Sale -> Refunded/Voided with reason, returned quantity and value captured. | Refund/void reversal Journal Entry remains linked to the originating Sale. | `SaleServiceVoidJournalTests`; `SaleServiceVatAndExchangeAccountingTests`; `SaleServiceHistoricalCostSnapshotTests`. |
| Exchange | Replacement Sale with original-sale link (**legacy**; new links rejected per Exchange D-01; the dedicated Exchange module is specified in `Docs/08_Sales_And_POS/Exchange/`) | Audited and Verified | Cashier/action actor; Business/Outlet retained. | Replacement Sale is completed; original and replacement IDs are linked. | Replacement Sale Journal Entry is captured; the return side maps to Refund when used. | `SaleServiceVatAndExchangeAccountingTests.CreateAsync_PersistsExchangeLinkage_ForReplacementSale`. |
| Cash Session | Open/close; duplicate close/retry | Audited and Verified | Initiating user, Business/Outlet/Terminal and event timestamp retained. | Open -> Closed; expected/actual cash, variance and closing note captured; retry does not emit a second close success event. | Cash Session reference links to variance Journal Entry when a material difference exists. | `CashSessionServiceTests.CloseAsync_PostsVarianceJournal_WhenDifferenceIsNonZero`; `CashSessionServiceTests.CloseAsync_WhenAlreadyClosed_DoesNotRecordSecondSuccessfulCloseAudit`; `P308RetryAtomicityTests.CashSessionCloseRetry_ReturnsClosedSessionAndDoesNotDuplicateVarianceJournal`. |
| Stock Adjustment | Create/update/submit/approve/reject/cancel/reverse | Audited and Verified | Initiating user and Business/location retained. | Lifecycle changes, summarized quantity/value effects, rejection/reversal reasons and status transitions captured. | Approval links the adjustment Journal Entry; reversal links original and reversal Journal Entries. | `StockAdjustmentWorkflowHttpTests.Approve_ForPositiveQuantityChange_PostsInventoryGainJournal`; `StockAdjustmentWorkflowHttpTests.Reverse_ApprovedGainAdjustment_CreatesReversalJournalAndRestoresInventory`; reject/cancel cases in `StockAdjustmentWorkflowHttpTests`. |
| Stock Count | Count lifecycle and generated financial correction | Partially Audited for non-financial count lifecycle; financial mutation Audited and Verified through Stock Adjustment | Count actor/status timestamps persist; adjustment actor is retained when the approved count generates its linked draft. | Count variance is recorded; count approval alone does not change inventory. Generated Stock Adjustment applies the summarized quantity/value effect on its approval. | No direct Stock Count Journal Entry; generated Adjustment carries `SourceStockCountId` and its approval/reversal Journal Entry links. | `StockCountPhase1HttpTests.Approve_AndGenerateAdjustmentDraft_TransitionsAndCreatesDraftLines`; `StockCountPhase1HttpTests.GeneratedAdjustment_Approval_MarksStockCountCompleted`; `StockAdjustmentWorkflowHttpTests.Approve_ForPositiveQuantityChange_PostsInventoryGainJournal`. |
| User/system actors | Direct user and controller-to-service actions; background postings | Audited and Verified / Accepted System-Generated Behavior | HTTP context captures `RealUserId`; service methods receive the initiating actor. Slice 2 has no background financial-posting worker and assigns no fake/recent user. | Audit and entity lifecycle timestamps are retained. Retry behavior does not create a second success event. | Source transaction references remain on audit and Journal Entry records. | `AuditServiceTests`; `GrnsControllerActorTests`; manual/expense/income workflow tests; `Program.cs` hosted-service registration. |
| Failure / rollback | Posting failures, duplicate GRN/Cash close, idempotency rejection, unauthorized action | Audited and Verified | Only successful authorized mutations produce success audit events; unauthorized Expense/Other Income actions are blocked. | Relational failures roll back source status and Journal Entries; duplicates do not repeat successful state/audit transitions. | A retry creates or resolves the authoritative source-linked Journal Entry once. | `P302RelationalFaultInjectionTests.CompleteAsync_RollsBackJournalAndGrn_WhenPayableProjectionPersistenceFails_ThenRetriesSafely`; `OtherIncomeAtomicityTests.ApproveAsync_RollsBack_PostedJournal_WhenApprovalAuditFails`; `CashSessionServiceTests.CloseAsync_DoesNotPersistClosedSession_WhenVariancePostingFails`; `P308RetryAtomicityTests`; Expense/Other Income forbidden HTTP tests. |

### Bounded Review Outcome

- GRN completion and Sale void/refund actor propagation were the only Slice 2 production gaps found. Both now require an authenticated actor and use it consistently for their relevant audit and financial/stock mutations; GRN also propagates its completion actor to the payable projection.
- Manual Journal Reject now has explicit lifecycle audit payload assertions; persisted HTTP audit rows assert the initiating actor, Business and timestamp.
- No separate Return workflow or Stock Count financial posting/reversal path exists. Return maps to Refund/Void; approved Stock Count corrections delegate to Stock Adjustment.
- No migration was created. No banking, reconciliation, closing, report-telemetry, audit-UI, audit-framework redesign or Phase 4 work was performed.
- Angular code was not changed by this review; Angular tests/build were not required.
- Final focused/full validation counts are recorded in the remediation plan’s Slice 2 test results.
