# User Management Module Index (POS v2)

Updated: 2026-06-07

## High-level overview

The User Management module is built on multi-business SaaS tenancy, role-based authorization, and location-scoped access control.

Location model baseline:

- Outlet is a stock location.
- Warehouse is an additional stock location.
- Assignment is role-dependent, not globally mandatory.

Core characteristics in the current codebase:

- Implemented
  - Multi-business SaaS model.
  - Super Admin boundary for business onboarding.
  - RBAC with JWT permission claims.
  - User CRUD lifecycle with tenant-scoped operations.
  - Invitation-based password setup.
  - Refresh-token lifecycle (hash, rotate, revoke).
- Partially Implemented
  - Canonical role and permission consistency across onboarding, seeding, and role-switch.
  - Full alignment of role-based assignment rules across all user-creation paths.
  - Warehouse-level authorization model for WarehouseManager.
- Planned
  - UserWarehouseAssignment architecture and warehouse-scoped enforcement.
  - Dedicated self-service password boundary and generic forgot-password flow.

## Document map

- [ROLE_DEFINITION.md](ROLE_DEFINITION.md)
  - Canonical role model, target assignment rules, and role-vocabulary governance.

- [USER_MANAGEMENT.md](USER_MANAGEMENT.md)
  - User CRUD lifecycle, tenant scope behavior, and assignment-policy gaps.

- [PERMISSION_MATRIX.md](PERMISSION_MATRIX.md)
  - Current permission templates, policy drift, and authorization composition needs.

- [BUSINESS_ONBOARDING.md](BUSINESS_ONBOARDING.md)
  - Super Admin onboarding transaction, role seeding, and invitation issuance.

- [OUTLET_ASSIGNMENT_RULES.md](OUTLET_ASSIGNMENT_RULES.md)
  - Outlet and warehouse assignment rules, including new role-dependent clarification.

- [USER_INVITATION_AND_PASSWORD.md](USER_INVITATION_AND_PASSWORD.md)
  - Invitation/password lifecycle with current refresh-token implementation status.

- [USER_MANAGEMENT_GAP_ANALYSIS.md](USER_MANAGEMENT_GAP_ANALYSIS.md)
  - Consolidated cross-document gap register and priorities.

## Issue prioritization summary

- Critical:
  - Permission-policy mismatches (controller policies vs registered permission catalog).

- High:
  - Role and permission drift across onboarding, seeding, and validation sources.
  - Ambiguous tenant placement risk for some Super Admin-created outlet-exempt users.
  - Partial mismatch between approved assignment rules and generic user CRUD enforcement.

- Medium:
  - Warehouse assignment model is indirect and not assignment-table based.
  - Mixed admin/self-service password management boundaries.
  - Missing generic forgot-password flow.

- Low:
  - Soft-delete behavior requires consistent downstream filtering.

## Module acceptance baseline

The User Management module should be considered implementation-complete when:

- Role, permission, and policy definitions are unified and validated automatically.
- Tenant and role-dependent outlet/warehouse assignment rules are consistently enforced across all user-affecting modules.
- WarehouseManager scope is enforced through UserWarehouseAssignment (not inferred ad hoc).
- Password and invitation lifecycle includes secure reset, refresh token rotation, and revocation.
- Super Admin and Business Owner boundaries remain explicit, enforced, and auditable.
