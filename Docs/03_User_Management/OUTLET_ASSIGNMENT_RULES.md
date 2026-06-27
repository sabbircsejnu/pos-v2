# Outlet Assignment Rules - Current Implementation (POS v2)

Updated: 2026-06-07

## Clarification update (2026-06-27)

- Inventory Location Access, Assigned Locations, and Default Inventory Location are separate concepts.
- Default Inventory Location is the user's starting/current working location.
- Assigned locations are used only where business rules require explicit restriction.
- Not every module is restricted by assigned locations.

### Terminology

- Assigned outlets/warehouses:
   - Managed as explicit assignment relations for user scoping where needed.
- Default Inventory Location:
   - Stored separately as `DefaultLocationType` + `DefaultLocationId` in user setup payloads.
   - Used by UI/API flows that require a single working location anchor.

## Stock location clarification

- Each outlet maintains its own stock and is a stock location.
- A business may also have one or more warehouses as additional stock locations.
- Purchase receiving is typically warehouse-first.
- Stock movement supports warehouse-to-outlet and outlet-to-warehouse flows.
- Assignment rules must be role-based, not globally mandatory for every user.

## Current implementation summary

- User CRUD currently enforces OutletId for roles that are not outlet-exempt in code.
- Current outlet-exempt list is limited to BusinessOwner and Super Admin.
- Outlet validation is tenant-aware when OutletId is present.
- Warehouse assignment is not modeled as a dedicated user-warehouse relation.
- Warehouse manager linkage currently uses Warehouse.ManagerId.

## Implementation status

- Implemented
   - OutletId validation for outlet-bound roles in current code path.
   - Tenant-aware outlet validation.
   - Warehouse manager linkage via Warehouse.ManagerId.
- Partially Implemented
   - Role assignment behavior is not fully aligned with approved rules for AccountsAdmin and WarehouseManager.
   - Warehouse-level authorization is not sourced from assignment relation.
- Planned
   - UserWarehouseAssignment model for warehouse scope.
   - Unified role-aware assignment validator.

## Existing files/classes/services involved

- src/RetailPOS.API/Services/UserService.cs
- src/RetailPOS.API/Services/RoleSwitchContext.cs
- src/RetailPOS.Infrastructure/Repositories/OutletRepository.cs
- src/RetailPOS.API/Services/UserOutletAccessService.cs
- src/RetailPOS.API/Services/BusinessOnboardingService.cs
- src/RetailPOS.Core/Entities/User.cs
- src/RetailPOS.Core/Entities/Outlet.cs
- src/RetailPOS.Core/Entities/Warehouse.cs

## Current flow explanation

1. User create or update request arrives.
2. UserService loads selected role.
3. If role is not outlet-exempt in current code:
   - OutletId is mandatory.
4. If OutletId is provided:
   - repository fetch uses tenant business scope
   - request fails if outlet is outside authorized business scope
5. For onboarding:
   - OutletManager and SalesPerson are assigned default outlet
   - BusinessOwner and AccountsAdmin are created without outlet
   - Warehouse manager is linked through Warehouse.ManagerId, not User.OutletId or User.WarehouseId
6. Runtime outlet access for reads and writes is further controlled by UserOutletAccessService.

## Target role assignment rules

- BusinessOwner / BusinessAdmin:
   - OutletId optional
   - Warehouse assignment optional
   - Access all outlets and warehouses in business
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
   - Multi-warehouse assignment required by architecture support

## Module-specific location rules

### 1. Inventory List / Inventory Check

- Any user with inventory view permission can view stock for any outlet/warehouse in business scope.
- Default load should use the user's Default Inventory Location.
- User can change filter to any outlet/warehouse.
- No assigned-location restriction is applied for inventory check.

### 2. Stock Transfer Create

- Any permitted user can transfer from their Default Inventory Location.
- Source location is auto-filled from Default Inventory Location.
- Source location is readonly for users without special source override permission.
- Destination can be any outlet/warehouse in business scope.
- Source and destination cannot be the same.
- Product search is enabled only after valid source and destination are set.

### 3. Stock Transfer Receive / Reject

- User can receive or reject only transfers where destination equals the user's Default Inventory Location.
- Backend must enforce this rule.

### 4. Stock Adjustment

- If user has stock adjustment permission, user can create adjustment for any outlet/warehouse.
- No assigned/default location restriction is applied.

### 5. User Edit / User Location Setup

- Multiple outlet/warehouse assignments are always allowed.
- Assigned outlets and assigned warehouses are stored separately.
- Default Inventory Location is stored separately from assignments:
   - `DefaultLocationType`
   - `DefaultLocationId`
- For `all_locations`, default can be any outlet/warehouse in business scope.
- For restricted scopes, default should normally be one of the assigned locations.

## Gaps or risks found

- AccountsAdmin and WarehouseManager outlet-optional behavior is not consistently enforced in generic user CRUD path.
- WarehouseManager assignment is not modeled by explicit assignment relation and cannot properly express scoped multi-warehouse access.
- Some authorization paths rely on outlet-centric checks and do not yet apply warehouse-assignment semantics.
- SuperAdmin tenant placement for outlet-optional users can still be ambiguous without explicit business assignment controls.

## Recommended improvements

- Implement UserWarehouseAssignment model (not a global User.WarehouseId requirement).
- Enforce WarehouseManager minimum one active warehouse assignment.
- Keep OutletId optional only for approved roles.
- Enforce role-type assignment consistency in one shared validation policy.
- Add automated tests for outlet and warehouse assignment authorization boundaries.

## Issue register

### Issue 1: Warehouse assignment model is not explicitly represented

1. Current implementation:
   - User has OutletId but no UserWarehouseAssignment relation.
2. Why it is a problem:
   - Warehouse-level authorization cannot be modeled correctly when one WarehouseManager manages multiple warehouses.
3. Recommended solution:
   - Implement UserWarehouseAssignment with UserId, WarehouseId, BusinessId, IsPrimary (optional), IsActive.
4. Priority:
   - Medium
5. Classification:
   - Architectural Decision Required

### Issue 2: WarehouseManager account linkage is indirect

1. Current implementation:
   - Warehouse manager relation is set via Warehouse.ManagerId, not explicit user warehouse scope field.
2. Why it is a problem:
   - Can produce ambiguity when users manage multiple warehouses or when profile-level checks are needed.
3. Recommended solution:
   - Define and enforce explicit one-to-many warehouse assignments using UserWarehouseAssignment.
4. Priority:
   - Medium

### Issue 3: Tenant placement may be ambiguous for outlet-exempt users

1. Current implementation:
   - Super Admin can create outlet-exempt users without outlet-derived business binding.
2. Why it is a problem:
   - Risks cross-tenant governance inconsistency for accounts expected to be tenant-bound.
3. Recommended solution:
   - Require explicit BusinessId for tenant users created by Super Admin.
4. Priority:
   - High

## Acceptance criteria

- Role assignment rules clearly define where OutletId is required vs optional.
- OutletId remains required for OutletManager and SalesPerson.
- AccountsAdmin and WarehouseManager outlet-optional behavior is documented as target policy.
- WarehouseManager must be constrained to assigned warehouses through UserWarehouseAssignment (planned architecture).
- Cross-business outlet and warehouse assignment attempts are rejected by validated authorization logic.
- Inventory list/check is permission-based and not constrained by assigned locations.
- Stock transfer create enforces source = Default Inventory Location (unless elevated source override permission).
- Stock transfer receive/reject enforces destination = Default Inventory Location.
- Stock adjustment create is permission-based and not constrained by assigned/default location.
