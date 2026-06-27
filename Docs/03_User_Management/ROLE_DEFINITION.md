# Role Definition - Current Implementation (POS v2)

Updated: 2026-06-07

## Purpose

This document defines user roles, role assignment rules, and effective access scope for User Management in POS v2.

It follows the current business rules:

- Outlets are stock locations.
- Warehouses are additional central stock locations.
- Not every user must have OutletId.
- Not every user must have warehouse assignment.
- Assignment requirements are role-dependent.

## Implementation status

- Implemented
  - Role-based access model with roles table + JSON permissions.
  - JWT permission claims and policy-based API authorization.
  - Super Admin boundary for business onboarding endpoints.
  - BusinessOwner role-switch capability.
- Partially Implemented
  - Canonical role vocabulary is not fully unified across onboarding, seed data, and role-switch logic.
  - Current outlet-exempt enforcement in user CRUD only exempts Super Admin and BusinessOwner.
  - WarehouseManager warehouse-level scope is not modeled via assignment table.
- Planned
  - Canonical role dictionary and alias governance.
  - Role-aware assignment engine matching the target rules in this document.
  - Warehouse-level authorization based on UserWarehouseAssignment.

## Canonical role model (target)

### 1. BusinessOwner / BusinessAdmin

- OutletId: optional
- Warehouse assignment: optional
- Access scope: all outlets and warehouses inside own business

### 2. AccountsAdmin

- OutletId: optional
- Warehouse assignment: optional
- Access scope: business-level financial data

### 3. OutletManager

- OutletId: required
- Warehouse assignment: not required
- Access scope: assigned outlet operations and outlet stock

### 4. SalesPerson

- OutletId: required
- Warehouse assignment: not required
- Access scope: sales operations from assigned outlet

### 5. WarehouseManager

- OutletId: optional
- Warehouse assignment: required (at least one active assignment)
- Access scope: assigned warehouse set only
- Responsibilities:
  - Purchase receiving
  - Warehouse inventory management
  - Warehouse stock adjustment
  - Warehouse-to-outlet transfer
  - Receiving outlet returns to warehouse
  - Warehouse reporting

## Current implementation mapping to target model

### Implemented

- BusinessOwner with no outlet is supported in onboarding and runtime.
- AccountsAdmin with no outlet is supported in onboarding flow.
- OutletManager and SalesPerson are created with required outlet in onboarding flow.
- WarehouseManager user can exist without OutletId.
- One warehouse manager can be linked to multiple warehouses via Warehouse.ManagerId on multiple warehouse rows.

### Partially Implemented

- UsersController/UserService role assignment enforcement currently uses IsOutletExempt for only:
  - Super Admin
  - BusinessOwner
- AccountsAdmin and WarehouseManager are not outlet-exempt in general user CRUD path.
- WarehouseManager warehouse access scoping is not enforced from warehouse-manager linkage.

### Planned

- Extend role assignment validator so AccountsAdmin and WarehouseManager can be created/updated without OutletId where required by policy.
- Introduce role-aware warehouse authorization for WarehouseManager based on explicit assignment records.

## Role vocabulary and naming status

### Implemented

- Active onboarding roles:
  - BusinessOwner
  - OutletManager
  - SalesPerson
  - AccountsAdmin
  - WarehouseManager
- Legacy seed roles still present in some environments:
  - Super Admin
  - Admin
  - Manager
  - Cashier
  - Stock Manager
  - Salesman
  - User

### Partially Implemented

- Role-switch allowed acting roles still rely on legacy names (Cashier, Salesman, Stock Manager).

### Planned

- Establish one canonical role list with explicit alias mapping and migration plan.

## Files involved

- src/RetailPOS.API/Services/RoleService.cs
- src/RetailPOS.API/Services/RoleSwitchService.cs
- src/RetailPOS.API/Services/RoleSwitchContext.cs
- src/RetailPOS.API/Services/UserService.cs
- src/RetailPOS.API/Services/BusinessOnboardingService.cs
- src/RetailPOS.API/Authorization/PermissionCatalog.cs
- src/RetailPOS.Infrastructure/Data/DbSeeder.cs
- src/RetailPOS.Core/Entities/Role.cs

## Gap register

### Gap 1: Canonical role vocabulary is fragmented

1. Current state:
   - Onboarding, seed data, and role-switch use overlapping but different role names.
2. Why it matters:
   - Assignment and authorization behavior can diverge by environment and workflow.
3. Planned direction:
   - Canonical role dictionary with alias resolution and CI validation.
4. Priority:
   - High

### Gap 2: Target role assignment rules are only partially enforced in user CRUD

1. Current state:
   - AccountsAdmin and WarehouseManager are not treated as outlet-optional in generic user CRUD path.
2. Why it matters:
   - Conflicts with approved business rules.
3. Planned direction:
   - Move to role-specific assignment rules in create/update validators.
4. Priority:
   - High

### Gap 3: WarehouseManager authorization model needs assignment table

1. Current state:
   - Warehouse access is not modeled with a dedicated user-warehouse assignment relation.
2. Why it matters:
   - Cannot accurately enforce multi-warehouse scoped access for managers.
3. Planned direction:
   - Add UserWarehouseAssignment as canonical authorization source.
4. Priority:
   - Medium
5. Classification:
   - Architectural Decision Required

## Acceptance criteria

- Role model explicitly distinguishes outlet-required and outlet-optional roles.
- WarehouseManager role requires at least one active warehouse assignment in target architecture.
- BusinessOwner/BusinessAdmin and AccountsAdmin are treated as outlet-optional roles.
- Role names, role-switch targets, and seed templates are governed by one canonical role dictionary.
