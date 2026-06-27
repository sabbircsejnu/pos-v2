# User Management - Current Implementation (POS v2)

Updated: 2026-06-07

## Implementation Status - COMPLETED

✅ **All recommended improvements have been implemented as of 2026-06-07.**

### What was implemented:

1. **RoleAssignmentPolicyService** - Centralized role-based validation
   - Validates outlet requirements per role
   - Validates warehouse requirements per role
   - Enforces WarehouseManager minimum warehouse assignment
   - Prevents validation logic duplication

2. **UserWarehouseAssignment Entity** - Multi-warehouse support
   - New table: `user_warehouse_assignments`
   - Supports one user managing multiple warehouses
   - Tracks primary warehouse assignment
   - Maintains business scope isolation

3. **Updated User DTOs**
   - CreateUserDto: Added WarehouseIds (list), BusinessId (for SuperAdmin)
   - UpdateUserDto: Added WarehouseIds (list)
   - UserDto: Added WarehouseAssignments, BusinessId, BusinessName fields

4. **Enhanced UserService**
   - Uses RoleAssignmentPolicyService for all validations
   - Handles warehouse assignment creation/update
   - Enforces SuperAdmin explicit BusinessId requirement
   - Maps warehouse assignments in UserDto responses

5. **Self-Service Password Endpoint**
   - New endpoint: `POST /api/users/my-password`
   - No special permission required (only [Authorize])
   - Separate from admin password reset capability
   - Users can change their own password securely

6. **Frontend User Form Updates**
   - Outlet: Now a dropdown (not manual ID input)
   - Outlet: Shows as required/optional based on role
   - Warehouses: Multi-select checkbox list (shown only for applicable roles)
   - Warehouses: Shows as required/optional based on role
   - Dynamic field validators based on selected role
   - Clear validation messages per role requirements

7. **User List Outlet Filter**
   - New Outlet dropdown filter in the User Management list page.
   - Options: All Outlets | No Outlet | each outlet name.
   - "No Outlet" shows users where OutletId IS NULL.
   - Outlet filter works with Search, Role, and Status filters.
   - Outlet filter state is preserved in URL: `/users?outletId=0` (no outlet), `/users?outletId=3` (specific outlet).
   - Filter is restored when navigating back from Edit User or User Details.
   - Outlets are scoped to the current business (non-SuperAdmin callers).

### Files Modified/Created:

**Backend:**
- ✅ src/RetailPOS.API/Services/IRoleAssignmentPolicyService.cs (NEW)
- ✅ src/RetailPOS.API/Services/RoleAssignmentPolicyService.cs (NEW)
- ✅ src/RetailPOS.Core/Entities/UserWarehouseAssignment.cs (NEW)
- ✅ src/RetailPOS.API/DTOs/Users/CreateUserDto.cs (UPDATED)
- ✅ src/RetailPOS.API/DTOs/Users/UpdateUserDto.cs (UPDATED)
- ✅ src/RetailPOS.API/DTOs/Users/UserDto.cs (UPDATED)
- ✅ src/RetailPOS.API/Services/UserService.cs (UPDATED)
- ✅ src/RetailPOS.API/Controllers/UsersController.cs (UPDATED)
- ✅ src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs (UPDATED)
- ✅ src/RetailPOS.API/Program.cs (UPDATED - registered service)
- ✅ src/RetailPOS.Infrastructure/Data/Migrations/20260607000000_AddUserWarehouseAssignment.cs (NEW)
- ✅ src/RetailPOS.Infrastructure/Data/Migrations/20260607000000_AddUserWarehouseAssignment.Designer.cs (NEW)

**Frontend:**
- ✅ retailpos-frontend/src/app/components/users/user-form.component.ts (UPDATED)
- ✅ retailpos-frontend/src/app/components/users/user-form.component.html (UPDATED)
- ✅ retailpos-frontend/src/app/components/users/user-list.component.ts (UPDATED - outlet filter)
- ✅ retailpos-frontend/src/app/components/users/user-list.component.html (UPDATED - outlet dropdown)
- ✅ retailpos-frontend/src/app/services/user.service.ts (UPDATED - outletId=0 param fix)
- ✅ retailpos-frontend/src/app/services/list-state.service.ts (UPDATED - numOrZero helper)

**Database:**
- ✅ Migration: AddUserWarehouseAssignment (creates user_warehouse_assignments table)

---

## Current implementation summary

- User CRUD endpoints are implemented with users.view/create/edit/delete policies.
- User create and update enforce role existence, email uniqueness, and role-based outlet/warehouse validation.
- Tenant scoping is applied for non-SuperAdmin callers via ITenantAccessService.
- User lifecycle supports activate/deactivate and soft delete (IsActive false).
- Self-service and admin password change endpoints are separated by permission boundary.
- Warehouse assignment model enables multi-warehouse management for WarehouseManager role.
- User list supports Outlet filter: All Outlets, No Outlet (IS NULL), or specific outlet by ID.
- Outlet filter state is preserved in the URL via list state preservation standard.

## Implementation status

- ✅ Fully Implemented
   - User create, update, list, get, activate, deactivate, soft-delete.
   - Tenant-aware scoping in all user service operations.
   - Role-based assignment validation via RoleAssignmentPolicyService.
   - SuperAdmin explicit BusinessId requirement for tenant user creation.
   - UserWarehouseAssignment model for warehouse-level authorization.
   - WarehouseManager minimum warehouse requirement enforcement.
   - Self-service password endpoint (POST /api/users/my-password).
   - Frontend outlet and warehouse field management.
   - User list Outlet filter with All Outlets / No Outlet / specific outlet options.
   - Outlet filter state preservation via URL query parameter (`outletId`).

## Existing files/classes/services involved

- src/RetailPOS.API/Controllers/UsersController.cs
- src/RetailPOS.API/Services/UserService.cs
- src/RetailPOS.API/Services/IRoleAssignmentPolicyService.cs (NEW)
- src/RetailPOS.API/Services/RoleAssignmentPolicyService.cs (NEW)
- src/RetailPOS.Infrastructure/Repositories/UserRepository.cs
- src/RetailPOS.Infrastructure/Repositories/RoleRepository.cs
- src/RetailPOS.Infrastructure/Repositories/OutletRepository.cs
- src/RetailPOS.API/Services/TenantAccessService.cs
- src/RetailPOS.API/Services/RoleSwitchContext.cs
- src/RetailPOS.API/Services/UserOutletAccessService.cs
- src/RetailPOS.API/DTOs/Users/CreateUserDto.cs
- src/RetailPOS.API/DTOs/Users/UpdateUserDto.cs
- src/RetailPOS.Core/Entities/User.cs
- src/RetailPOS.Core/Entities/UserWarehouseAssignment.cs (NEW)

## Current flow explanation

1. API request reaches UsersController action with permission policy check.
2. UserService resolves business scope:
   - Super Admin: Can specify BusinessId explicitly; defaults to null for multi-tenant context.
   - Other users: RequireBusinessId from tenant context.
3. Create flow:
    - Validate unique email
    - Validate role exists
    - Use RoleAssignmentPolicyService to validate outlet requirement based on role
    - Use RoleAssignmentPolicyService to validate warehouse requirement based on role
    - For SuperAdmin: Require explicit BusinessId when creating business-level users
    - Validate outlet belongs to business scope when provided
    - Validate warehouse IDs belong to business scope
    - Enforce business max users limit when configured
    - Hash password with BCrypt and create user
    - Create UserWarehouseAssignment records for each warehouse
4. Update flow:
   - Load scoped target user
   - Validate email uniqueness if changed
   - Validate role and role-based outlet/warehouse assignment rules
   - Apply profile, active state, and assignment updates
   - Remove and recreate warehouse assignments
5. Activate and deactivate flows toggle IsActive.
6. Delete flow calls repository soft delete, which sets IsActive false.
7. Self-service password: POST /api/users/my-password (only requires [Authorize])

## Target assignment rules (approved business policy)

- BusinessOwner / BusinessAdmin:
   - OutletId optional
   - Warehouse assignment optional
   - Access all outlets and warehouses in the business
- AccountsAdmin:
   - OutletId optional
   - Warehouse assignment optional
   - Access business-level financial data
- OutletManager:
   - OutletId required
   - Warehouse assignment not required
- SalesPerson:
   - OutletId required
   - Warehouse assignment not required
- WarehouseManager:
   - OutletId optional
   - Must be assigned to at least one warehouse
   - Can be assigned to multiple warehouses
   - Access must be restricted to assigned warehouse set

## Implementation notes

### Database Migration
Run: `dotnet ef database update --project .\src\RetailPOS.Infrastructure\ --startup-project .\src\RetailPOS.API\`

Migration timestamp: **20260607000000_AddUserWarehouseAssignment**
- Creates `user_warehouse_assignments` table
- Adds indexes on user_id, warehouse_id, business_id, and unique constraint on (user_id, warehouse_id)
- Foreign keys cascade to user, warehouse, and business deletions

### Frontend Outlet and Warehouse Fields
- **Outlet field behavior:**
  - Shows as required (red asterisk) for OutletManager, SalesPerson
  - Shows as optional for BusinessOwner, BusinessAdmin, AccountsAdmin
  - Displays as dropdown (not manual ID input)
  - Help text explains access scope for optional assignments

- **Warehouse field behavior:**
  - Only shown for WarehouseManager, BusinessOwner, BusinessAdmin
  - Shows as required for WarehouseManager (must select ≥1)
  - Shows as optional for BusinessOwner, BusinessAdmin
  - Multi-select checkboxes with warehouse names and addresses
  - Help text explains access scope

### User List API

**Endpoint:** `GET /api/users`

| Query param | Type | Description |
|---|---|---|
| `pageNumber` | int | Page number (default 1) |
| `pageSize` | int | Records per page (default 10) |
| `search` | string | Name or email search |
| `roleId` | long | Filter by role ID |
| `outletId` | long | `0` = no outlet assigned (IS NULL); `>0` = specific outlet |
| `isActive` | bool | Filter by active/inactive status |

**Outlet filter behavior:**
- `outletId` absent → no outlet filter (all users returned regardless of outlet).
- `outletId=0` → users where `OutletId IS NULL` (unassigned users).
- `outletId=N` (N > 0) → users assigned to outlet N.

The outlet dropdown in the frontend is scoped to the current business — non-SuperAdmin callers only see their own business's outlets.

### Self-Service Password Endpoint
- **URL:** POST /api/users/my-password
- **Auth:** Requires [Authorize] - any authenticated user can call
- **Body:** Same ChangePasswordDto as admin endpoint (CurrentPassword + NewPassword)
- **Difference from admin:** No "users.edit" permission check; user's own userId extracted from claims

### Role Policy Service
The `RoleAssignmentPolicyService` centralizes all role-based validation rules:
- `IsOutletRequired(roleName)` - Determines if outlet is mandatory
- `IsWarehouseRequired(roleName)` - Determines if warehouse is mandatory
- `ValidateOutletAssignment()` - Full outlet validation with error messages
- `ValidateWarehouseAssignment()` - Full warehouse validation with error messages
- `GetOutletOptionalRoles()` - Returns roles where outlet is optional
- `GetWarehouseAssignableRoles()` - Returns roles that can have warehouse assignments

## Testing Checklist

### Backend API Tests
- [ ] Create user with required outlet (OutletManager) - success
- [ ] Create user without outlet (OutletManager) - error
- [ ] Create user with optional outlet (BusinessOwner) - success both with and without outlet
- [ ] Create WarehouseManager with ≥1 warehouse - success
- [ ] Create WarehouseManager with 0 warehouses - error
- [ ] SuperAdmin user creation without BusinessId - error
- [ ] SuperAdmin user creation with explicit BusinessId - success
- [ ] Update user warehouse assignments - success
- [ ] POST /api/users/my-password with current user auth - success
- [ ] POST /api/users/{id}/change-password with users.edit - success (admin)

### Frontend Tests
- [ ] Create form loads outlets and warehouses
- [ ] Outlet required indicator shows/hides based on role
- [ ] Warehouse field shows only for applicable roles
- [ ] Warehouse multi-select works correctly
- [ ] Cannot submit create form without required fields
- [ ] Edit form loads existing warehouse assignments
- [ ] User list Outlet filter dropdown loads outlets for the current business
- [ ] Selecting "All Outlets" removes outlet filter — all users returned
- [ ] Selecting "No Outlet" returns only users where OutletId IS NULL
- [ ] Selecting a specific outlet returns only users assigned to that outlet
- [ ] Outlet filter combines correctly with Search, Role, and Status filters
- [ ] Outlet filter value is preserved in URL (e.g., `/users?outletId=3`)
- [ ] `outletId=0` in URL selects the "No Outlet" option on page load
- [ ] Navigating User List → Edit User → Back restores the outlet filter
- [ ] Clear Filters button resets outlet dropdown to "All Outlets"
- [ ] User list Outlet filter dropdown loads outlets for the current business
- [ ] Selecting "All Outlets" removes outlet filter
- [ ] Selecting "No Outlet" shows only users with no outlet assigned
- [ ] Selecting a specific outlet shows only users in that outlet
- [ ] Outlet filter combines correctly with Search, Role, and Status filters
- [ ] Outlet filter value is preserved in URL (e.g., `/users?outletId=3`)
- [ ] Navigating Edit User → Back restores the outlet filter

### Integration Tests
- [ ] User with assigned warehouse can only access that warehouse's data
- [ ] User without warehouse assignment (if allowed) can access all warehouses
- [ ] Soft-deleted users don't appear in dropdowns
- [ ] BusinessId correctly scoped for multi-tenant setup


   - IsActive
- Keep OutletId optional for roles where policy says optional.
- Enforce role-specific assignment validation matrix in one shared service.
- Add dedicated self-service password boundary and keep admin reset controls separate.

## Issue register

### Issue 1: BusinessId can remain ambiguous for some Super Admin-created users

1. Current implementation:
   - BusinessId is derived from tenant scope and/or outlet; outlet-exempt users can be created without explicit business assignment path.
2. Why it is a problem:
   - Can create loosely scoped user accounts that are harder to govern in a multi-business SaaS model.
3. Recommended solution:
   - Require explicit BusinessId selection for Super Admin user provisioning when creating tenant users.
4. Priority:
   - High

### Issue 2: No user-level WarehouseId field

1. Current implementation:
   - User entity has OutletId but no warehouse assignment model.
2. Why it is a problem:
   - Warehouse-level authorization cannot be modeled correctly when one WarehouseManager manages multiple warehouses.
3. Recommended solution:
   - Implement UserWarehouseAssignment instead of a single User.WarehouseId field.
4. Priority:
   - Medium
5. Classification:
   - Architectural Decision Required

### Issue 3: Change-password flow is permission-bound to users.edit

1. Current implementation:
   - Password change uses users.edit protected endpoint requiring current password.
2. Why it is a problem:
   - Mixes admin capability and self-service account security concerns in one permission boundary.
3. Recommended solution:
   - Add dedicated self-service password endpoint for current user and separate admin reset controls.
4. Priority:
   - Medium

### Issue 4: User delete is soft-delete only

1. Current implementation:
   - Delete marks IsActive false.
2. Why it is a problem:
   - If not clearly handled by all modules/reports, deactivated users may still affect analytics or selection flows.
3. Recommended solution:
   - Keep soft delete but enforce consistent filtering and document lifecycle states in all user queries.
4. Priority:
   - Low

## Acceptance criteria

- User create and update enforce role + outlet + tenant scope rules for all callers.
- Non-SuperAdmin callers cannot create or mutate users outside business scope.
- Approved role assignment policy is documented with explicit optional vs required OutletId behavior.
- WarehouseManager assignment requirement is modeled as warehouse-assignment relation (planned architecture) and not as globally mandatory WarehouseId.
- Soft delete behavior remains explicit and consistently handled by consuming modules.
