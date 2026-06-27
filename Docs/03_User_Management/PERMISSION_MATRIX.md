# Permission Matrix - Current Implementation (POS v2)

Updated: 2026-06-13

## Current implementation summary

- Permission claims in JWT are the primary authorization unit.
- Policies are registered from PermissionCatalog and referenced by controllers.
- Role permission templates are sourced from both onboarding and legacy seeding.
- Permission catalog consistency is incomplete across all sources.

## Implementation status

- Implemented
  - JWT permission claim authorization is active.
  - Policy enforcement is used broadly by controllers.
- Partially Implemented
  - Canonical permission source of truth is not unified.
  - Role templates and policy catalog still drift in places.
- Planned
  - Canonical permission registry used by policy registration, role validation, onboarding templates, and seed data.

## Existing files/classes/services involved

- src/RetailPOS.API/Authorization/PermissionCatalog.cs
- src/RetailPOS.API/Program.cs
- src/RetailPOS.API/Services/RoleService.cs
- src/RetailPOS.API/Services/TokenService.cs
- src/RetailPOS.Infrastructure/Data/DbSeeder.cs
- src/RetailPOS.API/Services/BusinessOnboardingService.cs
- src/RetailPOS.API/Controllers/*.cs

## Current flow explanation

1. Role permissions are stored in Role.Permissions as JSON.
2. On login or role-switch, permissions are mapped to JWT permission claims.
3. Program registers one authorization policy per PermissionCatalog entry.
4. Controllers require policy names such as users.view, sales.create, reports.inventory.
5. Policy assertion allows access when permission claim matches policy or wildcard star exists.

## Current matrix from implementation

### Business onboarding role templates (active SaaS onboarding flow)

- BusinessOwner: star (full)
- OutletManager:
  - users.view
  - products.view, products.create, products.edit
  - categories.view
  - stock_transfers.view, stock_transfers.create, stock_transfers.approve
  - stock_adjustments.view, stock_adjustments.create
  - sales.view, sales.create
  - reports.sales
- SalesPerson:
  - products.view
  - stock_transfers.view
  - sales.view, sales.create
  - customers.view, customers.create
- AccountsAdmin:
  - accounts.view, accounts.create, accounts.edit, accounts.delete
  - expenses.view, expenses.create, expenses.edit
  - transactions.view, transactions.create, transactions.edit, transactions.delete
  - reports.financial
- WarehouseManager:
  - inventory.view, inventory.edit
  - stock_adjustments.view, stock_adjustments.create
  - stock_transfers.view, stock_transfers.create, stock_transfers.approve, stock_transfers.cancel
  - low_stock_alerts.view
  - warehouses.view
  - purchases.view, purchases.create, purchases.receive

### Seeder role templates (legacy/default seed)

- Super Admin: star
- BusinessOwner: star
- OutletManager, Salesman, Admin, Manager, Cashier, Stock Manager, User with legacy permission sets

## Role assignment policy interaction

This matrix is permission-focused only. Role assignment requirements are defined separately and must be enforced alongside permissions:

- BusinessOwner / BusinessAdmin: outlet optional, warehouse optional
- AccountsAdmin: outlet optional, warehouse optional
- OutletManager: outlet required
- SalesPerson: outlet required
- WarehouseManager: outlet optional, at least one warehouse assignment required (target architecture)

Permissions are necessary but not sufficient. Effective access must combine:

- permission policy
- tenant scope
- location assignment (outlet or warehouse)

## Gaps or risks found

- Policy coverage mismatch:
  - Controllers use expenses.view, expenses.create, expenses.edit, expenses.delete.
  - PermissionCatalog currently does not include expenses permissions.
- RoleService.AllPermissions does not include accounts or transactions or expenses permissions used by controllers and onboarding roles.
- Seeder permissions include keys not present in current policy model (for example reports.view, inventory.create, inventory.delete).
- Historical references to inventory.adjust and inventory.transfer should be treated as legacy keys; current inventory submenu model uses stock_adjustments.*, stock_transfers.*, and low_stock_alerts.*.
- Warehouse-level assignment authorization is not represented in current permission matrix model.
- Because of these mismatches, role CRUD validation and controller authorization can drift.

## Recommended improvements

- Create one canonical permission list and use it in:
  - PermissionCatalog policy registration
  - RoleService permission validation
  - Seeder role definitions
  - Onboarding role templates
- Keep inventory permission granularity explicit by separating:
  - inventory.* for core inventory screens/endpoints
  - stock_adjustments.* for adjustment screens/actions
  - stock_transfers.* for transfer screens/actions
  - low_stock_alerts.* for low-stock screens/endpoints
- Add automated controller-policy to catalog comparison test.
- Add migration scripts to normalize existing role permission JSON values.
- Add authorization composition rules that combine permissions with location-assignment checks.

## Issue register

### Issue 1: Controllers use policies missing from PermissionCatalog

1. Current implementation:
  - Some controller policies (for example expenses.*) are referenced but not defined in PermissionCatalog.
2. Why it is a problem:
  - Can break authorization behavior and creates mismatch between intended and enforceable permissions.
3. Recommended solution:
  - Align PermissionCatalog to actual controller policy usage and enforce validation in CI.
4. Priority:
  - Critical

### Issue 2: RoleService permission validation list is incomplete

1. Current implementation:
  - RoleService.AllPermissions omits keys used by controllers and onboarding templates.
2. Why it is a problem:
  - Valid runtime permissions can be rejected during role create/update operations.
3. Recommended solution:
  - Replace local list with shared canonical permission registry used for both policies and role validation.
4. Priority:
  - High

### Issue 3: Seeder permission keys include legacy/non-policy values

1. Current implementation:
  - Seeder includes permissions like reports.view and inventory.create that do not map directly to current policy model.
2. Why it is a problem:
  - Makes role meaning inconsistent and hard to audit in production.
3. Recommended solution:
  - Migrate role permission payloads to canonical keys and deprecate unsupported entries.
4. Priority:
  - High

### Issue 4: Permission model does not include explicit warehouse-assignment dimension

1. Current implementation:
  - Permissions authorize actions, but warehouse-scoped user assignment is not modeled as a first-class authorization input.
2. Why it is a problem:
  - WarehouseManager cannot be reliably restricted to assigned warehouses in a scalable way.
3. Recommended solution:
  - Add UserWarehouseAssignment-based scope checks in authorization composition.
4. Priority:
  - Medium
5. Classification:
  - Architectural Decision Required

## Acceptance criteria

- Every policy used by controllers exists in canonical permission list.
- Every role permission entry in database validates against canonical list.
- Seeder and onboarding role templates pass canonical validation.
- Permission matrix reflects both permission keys and required location-scope composition rules.
