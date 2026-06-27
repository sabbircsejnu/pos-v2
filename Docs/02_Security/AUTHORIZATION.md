# Authorization - Current Implementation (POS v2)

Updated: 2026-06-07

## Current implementation summary

- Authorization is primarily permission-policy based using `[Authorize(Policy = "...")]`.
- Policies are registered dynamically from `PermissionCatalog.All` in `src/RetailPOS.API/Program.cs`.
- Policy assertion checks `permission` claims in JWT, including wildcard `*` support.
- Super Admin-only business onboarding endpoints are enforced by role (`[Authorize(Roles = RoleSwitchClaims.SuperAdminRoleName)]`) in `BusinessesController`.
- Frontend also applies route-level guards, but backend policy enforcement is the actual security boundary.

## Existing files/classes/services involved

### Policy and role enforcement

- `src/RetailPOS.API/Program.cs`
  - `AddAuthorization(...)` with permission-based policies
- `src/RetailPOS.API/Authorization/PermissionCatalog.cs`
- `src/RetailPOS.API/Services/TokenService.cs`
  - emits `permission` claims into JWT
- `src/RetailPOS.API/Services/RoleService.cs`
  - role creation/update + permission validation
- `src/RetailPOS.API/Controllers/BusinessesController.cs`
  - super-admin role gate

### Scope-aware authorization helpers

- `src/RetailPOS.API/Services/UserOutletAccessService.cs`
- `src/RetailPOS.API/Services/RoleSwitchContext.cs`
- `src/RetailPOS.API/Services/TenantAccessService.cs`

### Frontend guard layer

- `retailpos-frontend/src/app/guards/auth.guard.ts`
- `retailpos-frontend/src/app/app.routes.ts`

## Current flow explanation

1. Token is validated by JWT middleware.
2. `UseAuthorization()` evaluates endpoint requirements.
3. For policy-protected endpoints:
   - required policy name maps to permission name
   - request is allowed if user has matching `permission` claim or `*`
4. For role-protected endpoints (example: businesses module): role claim must include `Super Admin`.
5. In many modules, authorization is layered:
   - coarse permission policy at controller/action
   - fine-grained outlet/location checks in controller/service using `IUserOutletAccessService`

## Gaps or risks found

- Policy catalog drift exists.
  - `ExpensesController` uses `expenses.*` policies, but `PermissionCatalog` does not define `expenses.view/create/edit/delete`.
  - Missing policy registration can cause endpoint authorization failures or startup/runtime policy mismatch behavior.
- Permission source is duplicated.
  - `PermissionCatalog` and `RoleService.AllPermissions` are separate lists and already diverge.
  - `RoleService.AllPermissions` omits `accounts.*`, `transactions.*`, and `expenses.*`, so role management may reject valid controller policies.
- One endpoint is intentionally anonymous:
  - `SettingsController.GetCurrency` has `[AllowAnonymous]` while class default is protected.
  - If this is not intentional for public storefront display, it weakens settings exposure posture.
- Some business modules enforce permission but rely on module-specific code for tenant/resource scope, creating inconsistent authorization depth across modules.

## Recommended improvements

- Make a single permission source of truth used by:
  - policy registration
  - role validation
  - frontend route metadata validation checks
- Add startup validation to fail fast if controllers reference undefined policies.
- Add an automated test that compares controller policy names with permission catalog entries.
- Review and explicitly document which settings endpoints are public vs private.
- Keep frontend guards as UX aid only; continue treating API authorization as definitive.

## Acceptance criteria

- Every `[Authorize(Policy = "x")]` used by controllers has a corresponding registered policy.
- Role/permission management accepts and validates the same permission set enforced by API policies.
- Super Admin endpoints remain inaccessible to Business Owner and lower roles.
- Anonymous endpoints are explicitly approved and documented.
- Authorization decisions include both permission checks and scope checks for tenant/outlet/warehouse resources.
