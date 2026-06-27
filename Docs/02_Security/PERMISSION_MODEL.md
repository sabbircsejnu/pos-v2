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
- `src/RetailPOS.API/Services/RoleSwitchService.cs`
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
