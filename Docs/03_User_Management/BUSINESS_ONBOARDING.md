# Business Onboarding - Current Implementation (POS v2)

Updated: 2026-06-07

## Current implementation summary

- Business onboarding is implemented as a Super Admin only API flow.
- Onboarding is transactional and creates business, feature settings, default outlet, optional warehouse, and default users.
- Created users are invitation-based and must set password before normal login.
- BusinessId linkage is applied to created business records and users.

Stock location model in onboarding context:

- Outlet is a stock location.
- Warehouse is an additional central stock location.
- Default warehouse creation is optional during onboarding.

## Implementation status

- Implemented
   - Multi-business onboarding transaction.
   - Super Admin only onboarding boundary.
   - Owner reset invitation flow.
   - Default role user creation with invitation tokens.
- Partially Implemented
   - Role templates differ from legacy seed-role vocabulary.
   - Warehouse manager assignment is represented through Warehouse.ManagerId, not assignment table.
- Planned
   - UserWarehouseAssignment architecture for warehouse-level authorization.
   - Canonical role/permission template harmonization.

## Existing files/classes/services involved

- src/RetailPOS.API/Controllers/BusinessesController.cs
- src/RetailPOS.API/Services/BusinessOnboardingService.cs
- src/RetailPOS.API/DTOs/Business/*.cs
- src/RetailPOS.Core/Entities/Business.cs
- src/RetailPOS.Core/Entities/User.cs
- src/RetailPOS.Core/Entities/UserInvitation.cs
- src/RetailPOS.Core/Entities/Outlet.cs
- src/RetailPOS.Core/Entities/Warehouse.cs
- src/RetailPOS.Core/Entities/BusinessFeatureSetting.cs

## Current flow explanation

1. Super Admin calls POST api/businesses.
2. Service validates required fields and duplicate email/business name rules.
3. Transaction begins.
4. Service creates:
   - Business record
   - default feature settings
   - default outlet
   - optional default warehouse
5. Service ensures required roles exist.
6. Service creates onboarding users with:
   - BusinessId set
   - role assignment
   - outlet assignment for outlet-bound roles
   - MustResetPassword true
   - invitation token row with TTL
7. Outlet manager and optional warehouse manager are linked as manager IDs on outlet and warehouse.
8. Transaction commits.
9. API returns business IDs and invitation tokens.

## Target user assignment policy (applies to onboarding and post-onboarding)

- BusinessOwner / BusinessAdmin:
   - OutletId optional
   - Warehouse assignment optional
- AccountsAdmin:
   - OutletId optional
   - Warehouse assignment optional
- OutletManager:
   - OutletId required
   - Warehouse assignment not required
- SalesPerson:
   - OutletId required
   - Warehouse assignment not required
- WarehouseManager:
   - OutletId optional
   - At least one warehouse assignment required
   - Multi-warehouse assignments supported by target architecture

## Gaps or risks found

- Role naming and permission template differences between onboarding service and legacy seeder roles can create mixed deployments.
- Invitation tokens are returned directly in API response; secure distribution channel responsibility is external.
- No email delivery mechanism is implemented in this service (token handoff only).
- No explicit UserWarehouseAssignment model exists; warehouse manager relation is through Warehouse.ManagerId.
- Owner reset flow exists only for BusinessOwner under businesses endpoint, not for generic role users.

## Recommended improvements

- Unify onboarding role templates with canonical role-permission registry.
- Add secure invitation delivery workflow (email service or signed one-time link transport).
- Add audit coverage specific to onboarding and owner-reset events if not already centrally captured.
- Add explicit post-onboarding verification checks for tenant isolation and role access.
- Adopt UserWarehouseAssignment for warehouse-level authorization instead of global User.WarehouseId.

## Issue register

### Issue 1: Invitation token distribution is external to service

1. Current implementation:
   - Raw invitation tokens are returned in API response payload.
2. Why it is a problem:
   - Increases risk of exposure if transport/logging/operational handling is not tightly controlled.
3. Recommended solution:
   - Integrate secure delivery channel (email service with one-time link and minimal exposure in logs).
4. Priority:
   - High

### Issue 2: Onboarding role templates can drift from seeded role templates

1. Current implementation:
   - Onboarding uses one role-permission set while seed data contains legacy sets.
2. Why it is a problem:
   - Different tenants can receive different effective access behavior depending on environment history.
3. Recommended solution:
   - Enforce one canonical template source for onboarding and seeding.
4. Priority:
   - High

### Issue 3: Generic non-owner reset flow in onboarding module is absent

1. Current implementation:
   - Reset access endpoint under businesses module targets BusinessOwner only.
2. Why it is a problem:
   - Operational reset paths for other roles depend on separate flows and are not centralized.
3. Recommended solution:
   - Add documented, secure reset workflow for all tenant users.
4. Priority:
   - Medium

### Issue 4: Warehouse manager scope is not modeled through assignment table

1. Current implementation:
   - Warehouse manager relationship is set through Warehouse.ManagerId.
2. Why it is a problem:
   - Multi-warehouse authorization for managers is harder to formalize and govern.
3. Recommended solution:
   - Implement UserWarehouseAssignment and use it for warehouse-scope authorization.
4. Priority:
   - Medium
5. Classification:
   - Architectural Decision Required

## Acceptance criteria

- Only Super Admin can create businesses and reset owner access.
- Onboarding creates business, default locations, users, and invitation tokens atomically.
- Every created tenant-bound record has correct BusinessId.
- Outlet-bound users are created with required OutletId assignments.
- Optional warehouse path correctly creates warehouse and manager linkage when requested.
- Target architecture for warehouse authorization is documented as UserWarehouseAssignment (planned, not implemented).
- Onboarding failures roll back all writes.
