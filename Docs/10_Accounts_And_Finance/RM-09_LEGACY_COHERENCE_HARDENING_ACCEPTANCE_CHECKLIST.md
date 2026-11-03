# RM-09 Acceptance Checklist - Legacy Coexistence Hardening and Phase 1 Sign-Off Gate

Updated: 2026-08-09
Milestone: RM-09 Legacy Coexistence Hardening and Phase 1 Sign-Off Gate
Status: Accepted

## 1) Scope gate

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| GATE-01 | RM-09 remained scoped to coexistence hardening, permission consistency, and sign-off evidence; no new accounting feature work was introduced | Passed | [src/RetailPOS.API/Authorization/PermissionCatalog.cs](src/RetailPOS.API/Authorization/PermissionCatalog.cs), [src/RetailPOS.API/Program.cs](src/RetailPOS.API/Program.cs), [retailpos-frontend/src/app/app.routes.ts](retailpos-frontend/src/app/app.routes.ts) |
| GATE-02 | Accounting-v2 surfaces remain the canonical implementation path for the approved Phase 1 scope | Passed | [src/RetailPOS.API/Controllers/AccountingReportsController.cs](src/RetailPOS.API/Controllers/AccountingReportsController.cs), [src/RetailPOS.API/Controllers/AccountingExpensesController.cs](src/RetailPOS.API/Controllers/AccountingExpensesController.cs), [src/RetailPOS.API/Controllers/AccountingOtherIncomeController.cs](src/RetailPOS.API/Controllers/AccountingOtherIncomeController.cs) |
| GATE-03 | Legacy finance aliases remain compatibility-only and do not drive new accounting workflow behavior | Passed | [src/RetailPOS.API/Authorization/PermissionCatalog.cs](src/RetailPOS.API/Authorization/PermissionCatalog.cs), [src/RetailPOS.API/Services/RoleService.cs](src/RetailPOS.API/Services/RoleService.cs) |

## 2) Permission and routing consistency

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| PERM-01 | Backend accounting policies are registered in the canonical permission catalog | Passed | [src/RetailPOS.API/Authorization/PermissionCatalog.cs](src/RetailPOS.API/Authorization/PermissionCatalog.cs) |
| PERM-02 | Controller authorization policies remain aligned with the catalog and do not fall back to legacy finance policies | Passed | [tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs](tests/RetailPOS.Tests/Accounting/AccountingPermissionRegressionTests.cs) |
| PERM-03 | Frontend accounting routes and menu entries are guarded by canonical accounting permissions | Passed | [retailpos-frontend/src/app/app.routes.ts](retailpos-frontend/src/app/app.routes.ts), [retailpos-frontend/src/app/models/menu.model.ts](retailpos-frontend/src/app/models/menu.model.ts) |
| PERM-04 | Frontend auth aliases and role-form permission lists remain aligned with backend canonical permissions | Passed | [retailpos-frontend/src/app/services/auth.service.ts](retailpos-frontend/src/app/services/auth.service.ts), [retailpos-frontend/src/app/components/roles/role-form.component.ts](retailpos-frontend/src/app/components/roles/role-form.component.ts) |

## 3) Regression and sign-off evidence

| Check ID | Requirement | Status | Evidence |
|---|---|---|---|
| REG-01 | Full backend regression suite passes without accounting regressions | Passed | `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release` -> 296 passed, 0 failed, 0 skipped |
| REG-02 | Permission-specific regression suite passes | Passed | `dotnet test tests/RetailPOS.Tests/RetailPOS.Tests.csproj -c Release --filter "FullyQualifiedName~AccountingPermissionRegressionTests"` -> 5 passed, 0 failed, 0 skipped |
| REG-03 | Frontend production build remains healthy after route and permission wiring changes | Passed | `npm run build` in [retailpos-frontend](retailpos-frontend) |

## 4) Final decision

RM-09 Legacy Coexistence Hardening and Phase 1 Sign-Off Gate is accepted. The accounting-v2 foundation remains consistent, the permission model is canonical across backend and frontend, and the required regression evidence is green for Phase 1 sign-off.
