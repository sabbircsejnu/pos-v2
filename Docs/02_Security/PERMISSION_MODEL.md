# Permission Model - Current Implementation (POS v2)

Updated: 2026-06-07

## Current implementation summary

- Permissions are stored per role as JSON in `roles.permissions`.
- Access tokens embed those permissions as repeated `permission` claims.
- `*` wildcard grants full policy access.
- Business onboarding creates default SaaS roles with predefined permissions (including wildcard for BusinessOwner).
- BusinessOwner can switch to limited acting roles; effective permissions come from acting role claims.

## Existing files/classes/services involved

- `src/RetailPOS.API/Authorization/PermissionCatalog.cs`
- `src/RetailPOS.API/Services/RoleService.cs`
- `src/RetailPOS.API/Services/TokenService.cs`
- `src/RetailPOS.Application/Authentication/Commands/RoleSwitchCommands.cs`
- `src/RetailPOS.Infrastructure/Authentication/AuthenticationStore.cs`
- `src/RetailPOS.API/Services/RoleSwitchContext.cs`
- `src/RetailPOS.API/Services/BusinessOnboardingService.cs`
- `src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs` (`Role.Permissions` mapped as `jsonb`)

## Current flow explanation

1. Role permissions are persisted as JSON strings in the DB.
2. On login (or role switch), permissions are parsed and inserted as JWT claims (`permission`).
3. API policy checks map policy name to required permission claim.
4. If permission claim list contains `*`, all permission policies pass.
5. For BusinessOwner role-switch:
   - `real_role` remains owner
   - `acting_role` permissions replace effective permission set in token
   - this enables "act as OutletManager/Cashier/Salesman/Stock Manager" behavior

## Gaps or risks found

- Permission definitions are not centralized.
  - `PermissionCatalog.All` and `RoleService.AllPermissions` differ.
- Missing expenses permissions in policy catalog.
  - Controllers reference `expenses.*` policies not present in `PermissionCatalog`.
- Missing accounts/transactions/expenses in role validation list.
  - `RoleService` can reject permission updates that APIs actually require.
- Role naming inconsistencies exist across code paths.
  - Role-switch allowed list includes variants like `Stock Manager` and `StockManager`, while onboarding role is `WarehouseManager`.
- Wildcard `*` for BusinessOwner simplifies management but increases blast radius if account is compromised.

## Recommended improvements

- Introduce a single permission registry used by both policy registration and role CRUD validation.
- Add CI/static test to detect policy-permission mismatches.
- Normalize role names and aliases into an explicit canonical role map.
- Consider replacing broad wildcard in owner role with explicit permission bundles plus privileged operations list.
- Add permission migration/versioning strategy so role JSON can be safely evolved.

## Acceptance criteria

- A single canonical permission list exists and is used everywhere.
- Every controller policy corresponds to a valid permission in the canonical list.
- Role create/update APIs accept exactly the same permission vocabulary enforced by authorization middleware.
- Role-switch acting roles are validated against canonical role definitions and documented aliases.
- Permission regression tests pass for all modules (users, roles, inventory, sales, purchases, GRN, accounts, expenses, reports, settings, audit).

## Wildcard evaluation and Business vs platform scope

- `PermissionCatalog.IsGranted` is the single authoritative evaluator (wildcard `*`, exact, alias, case-insensitive). Authorization policies, `ClaimsPrincipal.HasPermission` and the payroll access adapter use it; the Angular `AuthService.hasPermission` mirrors it. Menus, route guards and API policies therefore agree.
- `*` is a **Business-level** wildcard: it does not grant platform operations (Super Admin role endpoints such as `/api/businesses`), widen Business/Outlet scope, bypass feature entitlements (HR/Payroll capability requirement) or domain approval rules.
- `ITenantAccessService` distinguishes platform operations (`EnsurePlatformAdministrator`), Business-scoped operations (strict `RequireBusinessId`, never a default Business) and target-Business administration (`RequireTargetBusinessId`, used for feature changes: a Super Admin names the Business explicitly and needs no selected context). Super Admin read scopes use the selected Business and no longer read across all Businesses when none is selected.
- HR/Payroll menu entries depend on backend feature entitlements; `MenuService` loads `/api/hr/v1/capabilities` in the background when the user/Business scope changes.

## Registry, Role Edit, lookups and the permission-consistency guard

- `PermissionCatalog` (plus `HrPayrollPermissionCatalog`) is the only permission registry; roles store granted codes as JSON, so there is no separate permission table to seed. `GET /api/roles/permissions` returns the catalog and Role Edit (`role-form.component.ts`, `hr-payroll-permissions.ts`) groups it. Finance permissions are grouped as "Finance - ..." categories that mirror the sidebar Finance section. Legacy `accounts.*`, `transactions.*`, `expenses.*` stay backend-only aliases and `accounting.dashboard.view` has no implemented endpoint, so they are intentionally not offered.
- Expense Categories have no separate codes: listing needs `accounting.expenses.view`; creating/editing a category needs `accounting.expenses.edit`.
- Read-only lookups shared by several screens use named any-of policies from `LookupAuthorizationPolicies` (for example `lookup.product_variants` for `GET /api/products/variants/search`) instead of the owning module's full view permission. Business and Outlet/Warehouse scope and cost redaction remain enforced by the endpoint.
- `RbacPermissionConsistencyTests` fails when a registered permission cannot be assigned in Role Edit, a menu/route/controller references an unregistered permission, or the Angular alias mirror grants more than the backend aliases.
