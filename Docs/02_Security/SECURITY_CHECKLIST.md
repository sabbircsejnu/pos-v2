# Security Checklist - Current State (POS v2)

Updated: 2026-06-07

## Current implementation summary

- Core JWT auth, permission-based authorization, and partial tenant/location isolation are implemented.
- Super Admin vs Business Owner separation is implemented in key areas (business onboarding and role-switch context).
- Security controls are uneven across modules; some high-risk paths still rely on manual scoping and lack consistent enforcement.

## Existing files/classes/services involved

- Authentication:
  - `src/RetailPOS.API/Program.cs`
  - `src/RetailPOS.API/Controllers/AuthController.cs`
  - `src/RetailPOS.API/Services/AuthService.cs`
  - `src/RetailPOS.API/Services/TokenService.cs`
- Authorization & permissions:
  - `src/RetailPOS.API/Authorization/PermissionCatalog.cs`
  - `src/RetailPOS.API/Services/RoleService.cs`
  - `src/RetailPOS.API/Controllers/*` (policy and role attributes)
- Tenant/location isolation:
  - `src/RetailPOS.API/Services/TenantAccessService.cs`
  - `src/RetailPOS.API/Services/UserOutletAccessService.cs`
  - `src/RetailPOS.API/Services/RoleSwitchContext.cs`
- Frontend session handling:
  - `retailpos-frontend/src/app/services/auth.service.ts`
  - `retailpos-frontend/src/app/interceptors/auth.interceptor.ts`
  - `retailpos-frontend/src/app/guards/auth.guard.ts`

## Current flow explanation

1. User authenticates and receives JWT + refresh token (refresh token is persisted hashed with expiry/rotation/revocation).
2. API enforces policy/role attributes per endpoint.
3. Services/repositories apply business scoping where implemented.
4. Location-sensitive modules call user outlet access service to authorize read/write scope.
5. Frontend applies route guards and sends bearer token on API calls.

## Checklist

### Authentication

- [x] JWT bearer validation enabled (issuer, audience, signature, lifetime).
- [x] Password hashing with BCrypt.
- [x] Invitation/password-reset-first-login flow.
- [x] Refresh token rotation and persistence.
- [x] Server-side token revocation on logout (refresh token path).
- [x] Login/refresh/register rate limiting (IP-based fixed-window).
- [ ] Account-level lockout policy for repeated failed authentication.
- [ ] Secrets removed from source-controlled config.

### Authorization and RBAC

- [x] Permission-based policy model in API.
- [x] Role-based gate for Super Admin business endpoints.
- [x] Wildcard permission support.
- [ ] Single source of truth for permissions (currently duplicated lists).
- [ ] Full policy-to-controller consistency validation.
- [ ] Confirm/justify anonymous settings endpoint exposure.

### Tenant isolation (BusinessId)

- [x] Tenant access service with Super Admin bypass and business requirement.
- [x] Business-scoped repositories/services in multiple modules (users, outlets, warehouses, accounts, transactions, purchase orders).
- [ ] Global data isolation guarantee (no global query filter/mandatory scope abstraction).
- [ ] Consistent business scope in inventory and GRN modules.

### Outlet/Warehouse-level access rules

- [x] Authorized outlet/warehouse resolver service.
- [x] Write pinning for non-owner users in sales and transfers.
- [x] BusinessOwner vs non-owner behavior split.
- [ ] Uniform application of location authorization for all location-aware endpoints (POS/Inventory/GRN gaps remain).

### Super Admin vs Business Owner separation

- [x] Businesses module restricted to Super Admin role.
- [x] Business onboarding creates owner and scoped business data.
- [x] BusinessOwner role-switch to limited acting roles.
- [x] Acting outlet business-bound validation during role switch.
- [ ] Legacy owner accounts without BusinessId claim migration/removal.

### Registration exposure

- [x] Public register endpoint exposure reduced (restricted to Super Admin).
- [x] Register role assignment constrained to non-privileged defaults.
- [ ] Invitation-only provisioning for all production user creation paths.

## Gaps or risks found

- Permission drift between catalog, role validation, and controller usage.
- Access token revocation deny-list is not implemented (logout revokes refresh tokens only).
- Inconsistent tenant/location enforcement in selected modules (inventory, GRN, POS endpoints).
- Hardcoded local credentials/secrets in API config.
- Frontend token storage in `localStorage` (higher XSS impact).

## Recommended improvements

- Build a unified security contract:
  - one permission registry
  - one tenant-scope enforcement pattern
  - one location-scope enforcement pattern
- Add startup/CI checks:
  - undefined policy detection
  - controller-policy coverage
  - tenant-scope tests for every business-owned module
- Implement robust session lifecycle:
  - refresh token persistence + rotation + revocation
  - logout invalidation (refresh token path already implemented)
  - access token deny-list or short-lived access token strategy
- Add account-aware login lockout in addition to IP-based endpoint throttling.
- Keep frontend localStorage approach temporarily for compatibility, and plan migration to safer session delivery (HttpOnly-cookie/BFF model).
- Harden secret and credential management across environments.
- Add security-focused integration tests for Super Admin / Business Owner / Outlet roles.

## Acceptance criteria

- Security checklist items marked incomplete above are implemented or explicitly risk-accepted with owner and timeline.
- API passes automated regression tests for:
  - authentication failure/success paths
  - authorization policy coverage
  - cross-business and cross-location isolation denials
  - role-switch boundaries
- Production configuration has no plaintext secrets in repository.
- Documentation remains synchronized with code changes to auth, RBAC, and tenant-scoping behavior.
