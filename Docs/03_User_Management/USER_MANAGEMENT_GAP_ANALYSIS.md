# User Management Gap Analysis (POS v2)

Updated: 2026-06-07

## Scope

This document consolidates user-management gaps across:

- ROLE_DEFINITION.md
- USER_MANAGEMENT.md
- PERMISSION_MATRIX.md
- BUSINESS_ONBOARDING.md
- OUTLET_ASSIGNMENT_RULES.md
- USER_INVITATION_AND_PASSWORD.md

It reflects the approved architecture direction:

- Outlet is a stock location.
- Warehouse is an additional stock location.
- Assignment must be role-dependent.
- WarehouseManager authorization should be modeled using UserWarehouseAssignment.

## Current state snapshot

### Implemented

- User CRUD lifecycle with tenant scoping in core user-service path.
- Role- and permission-based authorization.
- Super Admin boundary for business onboarding.
- Invitation-based first-password setup.
- Refresh token lifecycle with hashed persistence, rotation, expiry checks, and logout revocation.

### Partially Implemented

- Canonical role/permission alignment across onboarding, seeding, and role-switch.
- Full consistency of approved assignment rules across all user provisioning paths.
- Warehouse-level authorization model for WarehouseManager.

### Planned

- UserWarehouseAssignment model/table.
- Unified assignment policy engine by role.
- Generic forgot-password flow and clearer self-service password boundary.

## Consolidated gap register

### Gap 1: Permission-policy drift

1. Current state:
   - Some controller policies (for example expenses.*) are not in PermissionCatalog.
   - RoleService permission validator does not include some controller-used permissions.
2. Risk:
   - Authorization behavior and role-management validation can diverge.
3. Recommended action:
   - Establish one canonical permission registry for policy registration, role validation, seeding, and onboarding templates.
4. Priority:
   - Critical

### Gap 2: Role vocabulary fragmentation

1. Current state:
   - Onboarding roles and seeded legacy roles use mixed naming sets.
2. Risk:
   - Inconsistent behavior across environments and role-switch workflows.
3. Recommended action:
   - Canonical role dictionary with alias mapping and migration strategy.
4. Priority:
   - High

### Gap 3: Assignment-rule mismatch with approved policy

1. Current state:
   - Approved policy says AccountsAdmin and WarehouseManager can be outlet-optional.
   - Generic user CRUD currently enforces outlet requirement for roles not in current outlet-exempt list.
2. Risk:
   - User provisioning behavior depends on pathway and may violate approved rules.
3. Recommended action:
   - Implement role-aware assignment validator for all user create/update paths.
4. Priority:
   - High

### Gap 4: WarehouseManager scope model is insufficient

1. Current state:
   - No explicit UserWarehouseAssignment relation.
   - Warehouse manager linkage relies on Warehouse.ManagerId and does not define scoped warehouse authorization model.
2. Risk:
   - Multi-warehouse authorization cannot be modeled and enforced cleanly at scale.
3. Recommended action:
   - Implement UserWarehouseAssignment table with fields:
     - UserId
     - WarehouseId
     - BusinessId
     - IsPrimary (optional)
     - IsActive
4. Priority:
   - Medium
5. Classification:
   - Architectural Decision Required

### Gap 5: SuperAdmin tenant placement ambiguity for outlet-optional users

1. Current state:
   - Business assignment can be implicit depending on creation path and outlet usage.
2. Risk:
   - Loosely scoped tenant users and governance ambiguity.
3. Recommended action:
   - Require explicit BusinessId in SuperAdmin tenant-user provisioning workflows.
4. Priority:
   - High

### Gap 6: Password management boundary is mixed

1. Current state:
   - Change password is tied to users.edit path and current-password verification.
   - Generic forgot-password flow is absent.
2. Risk:
   - Self-service and admin credential-management responsibilities are not clearly separated.
3. Recommended action:
   - Add dedicated self-service password endpoint and generic forgot/reset workflow.
4. Priority:
   - Medium

## Priority board

- Critical
  - Gap 1: Permission-policy drift
- High
  - Gap 2: Role vocabulary fragmentation
  - Gap 3: Assignment-rule mismatch
  - Gap 5: SuperAdmin tenant placement ambiguity
- Medium
  - Gap 4: WarehouseManager scope model
  - Gap 6: Password management boundary

## Target architecture note

UserWarehouseAssignment is the approved direction for warehouse authorization.

This does not mean every user must have warehouse assignment.

Expected role behavior:

- BusinessOwner/BusinessAdmin: warehouse assignment optional
- AccountsAdmin: warehouse assignment optional
- OutletManager/SalesPerson: no warehouse assignment required
- WarehouseManager: one or more active warehouse assignments required

## Acceptance criteria for closure

- Canonical permission registry is enforced across policy registration, role validation, seeding, and onboarding.
- Canonical role dictionary and alias governance are implemented.
- Role-based assignment validator enforces approved outlet/warehouse optionality requirements.
- UserWarehouseAssignment is used for WarehouseManager authorization scope.
- SuperAdmin tenant-user provisioning requires explicit business placement.
- Password lifecycle includes clear self-service and admin boundaries plus generic recovery flow.
