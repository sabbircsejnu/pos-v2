# Business Setup Module

## Overview

The Business Setup Module provides Super Admin with a unified, **BusinessId-scoped** interface for configuring each tenant business. This resolves the SaaS architecture gap where a wildcard (`*`) permission holder (Super Admin) had no clean, scoped path to manage outlets and warehouses per business.

---

## Architecture Decision

### Problem

Super Admin holds a `*` (wildcard) permission and can access all permission-guarded routes. However, the existing `/outlets` and `/warehouses` routes operate on the **current user's business context** via `ITenantAccessService`. When Super Admin calls these routes, `IsSuperAdmin = true` causes `GetAllOutletsAsync()` to return outlets across **all businesses** with no filtering—making it unsuitable for per-business management.

### Solution

Introduce a **Business Setup page** accessible only from the Business Management list:

```
Super Admin → Businesses → [Manage Setup] → /businesses/:id/setup
```

This page exposes 5 tabs, all scoped to the selected `BusinessId`:

| Tab | Content |
|-----|---------|
| **Business Info** | View/toggle business status, subscription details |
| **Owner User** | View owner details, trigger access reset |
| **Outlets** | CRUD outlets scoped to this business |
| **Warehouses** | CRUD warehouses scoped to this business |
| **Settings** | Feature flags and subscription limits |

---

## Backend Design

### New Endpoints (Super Admin only)

Mounted under `BusinessesController` via a dedicated `BusinessSetupController`:

```
GET    /api/businesses/{businessId}/outlets
POST   /api/businesses/{businessId}/outlets
PUT    /api/businesses/{businessId}/outlets/{id}
DELETE /api/businesses/{businessId}/outlets/{id}

GET    /api/businesses/{businessId}/warehouses
POST   /api/businesses/{businessId}/warehouses
PUT    /api/businesses/{businessId}/warehouses/{id}
DELETE /api/businesses/{businessId}/warehouses/{id}
```

All endpoints are decorated with `[Authorize(Roles = "Super Admin")]`.

### Service Layer

`IOutletService` and `IWarehouseService` gain `*ForBusinessAsync` overloads that accept an **explicit** `businessId` parameter, bypassing the ambient `ITenantAccessService` context. This keeps tenant isolation logic centralized—the explicit parameter is only used by Super Admin paths.

### Audit Logging

All setup actions emit an `AuditEvent` with:
- `Module = "BusinessSetup"` 
- `ActionType` = Create / Update / Delete
- `PrimaryEntity` = `("Outlet" | "Warehouse", entityId.ToString())`
- Metadata includes `businessId`

---

## Frontend Design

### Route

```
/businesses/:id/setup          → BusinessSetupComponent
```

Protected by `roleGuard(['Super Admin'])`.

### Component Structure

`BusinessSetupComponent` uses tab-based navigation. Each tab is conditionally rendered via `@if (activeTab === '...')` to avoid loading all data upfront.

```
business-setup.component.ts      ← tab state, data loading, CRUD actions
business-setup.component.html    ← tab bar + tab panels
```

### Wildcard (*) Permission Verification

The existing `routePermissionGuard` and `MenuService.hasAccess()` both call `AuthService.hasAnyPermission()` / `hasPermission()` which check:

```ts
permissions.includes('*') || permissions.includes(permission)
```

This means any route guarded by `routePermissionGuard` is accessible to Super Admin via `*`. Business routes are further protected by `roleGuard(['Super Admin'])` which checks `user.roleName` directly.

**Result:** Wildcard works correctly for all route guards and menu filtering. No changes needed to the guard logic.

### Menu Changes

Outlets and Warehouses are added to the `ADMINISTRATION` section in `NAV_SECTIONS` with `permission: 'outlets.view'` / `permission: 'warehouses.view'`. These entries are visible to **Business Owner, Outlet Manager** etc. who manage their own business's outlets via the existing tenant-scoped routes.

Super Admin does **not** see these standalone menu items (their `outlets.view` check returns `true` via `*`, but the entries should only appear for non-Super-Admin roles). This is achieved by adding `excludeRole: 'Super Admin'` semantics via a `hideForRole` property on those menu items, or by listing them without explicit role restriction (they resolve to the tenant-scoped API which returns all outlets for Super Admin—not useful). 

> **Decision:** Outlets and Warehouses menu items are omitted from the menu for Super Admin; Super Admin always uses Business → Manage Setup → Outlets/Warehouses path.

---

## Data Flow (Super Admin setup path)

```
Business List
    └─ [Manage Setup] button
         └─ navigate to /businesses/{id}/setup
              ├─ Tab: Business Info   → GET /api/businesses/{id}
              ├─ Tab: Owner User      → GET /api/businesses/{id} (ownerEmail)
              │                          POST /api/businesses/{id}/owner/reset-access
              ├─ Tab: Outlets         → GET/POST/PUT/DELETE /api/businesses/{id}/outlets[/{oid}]
              ├─ Tab: Warehouses      → GET/POST/PUT/DELETE /api/businesses/{id}/warehouses[/{wid}]
              └─ Tab: Settings        → GET/PATCH /api/businesses/{id}/features
```

---

## Security Checklist

- [x] All new endpoints require `Roles = "Super Admin"` — no permission policy bypass
- [x] `businessId` is always sourced from the **route parameter**, never from user input body
- [x] `OutletService.GetByBusinessIdAsync` validates the business exists before querying
- [x] Audit log entries capture `businessId` in metadata
- [x] Frontend route is protected by `roleGuard(['Super Admin'])`
- [x] Wildcard `*` permission does not grant access to Super Admin-only role-guarded routes for other users
