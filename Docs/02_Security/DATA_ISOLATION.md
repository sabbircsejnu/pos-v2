# Data Isolation - Current Implementation (POS v2)

Updated: 2026-06-07

## Current implementation summary

- The system is implemented as multi-business SaaS with `BusinessId` references across core entities.
- Tenant isolation is primarily application-level (service/repository filtering), not database-global query filters.
- `ITenantAccessService` + `IRoleSwitchContext` provide effective business scope and Super Admin bypass.
- `IUserOutletAccessService` enforces outlet/warehouse access rules and write scoping for many operational endpoints.
- Super Admin and Business Owner separation exists in both role checks and scope behavior.

## Existing files/classes/services involved

### Tenant and role context

- `src/RetailPOS.API/Services/TenantAccessService.cs`
- `src/RetailPOS.API/Services/RoleSwitchContext.cs`
- `src/RetailPOS.API/Services/UserOutletAccessService.cs`

### Services with explicit business scoping via tenant access

- `src/RetailPOS.API/Services/UserService.cs`
- `src/RetailPOS.API/Services/OutletService.cs`
- `src/RetailPOS.API/Services/WarehouseService.cs`
- `src/RetailPOS.API/Services/AccountService.cs`
- `src/RetailPOS.API/Services/TransactionService.cs`
- `src/RetailPOS.API/Services/PurchaseOrderService.cs`
- `src/RetailPOS.API/Services/FeatureEntitlementService.cs`

### Controllers/services with outlet/warehouse scope enforcement

- `src/RetailPOS.API/Controllers/SalesController.cs`
- `src/RetailPOS.API/Controllers/StockTransfersController.cs`
- `src/RetailPOS.API/Controllers/InventoryReportsController.cs`
- `src/RetailPOS.API/Controllers/StockAdjustmentsController.cs`
- `src/RetailPOS.API/Controllers/UserOutletsController.cs`

### Data model

- `src/RetailPOS.Infrastructure/Data/RetailPOSDbContext.cs`
  - contains `BusinessId` relationships for businesses, users, outlets, warehouses, feature settings, and business-linked modules

## Current flow explanation

1. JWT includes `businessId`, `outletId`, real role, and optional acting role/outlet claims.
2. `RoleSwitchContext` computes effective identity and scope.
3. `TenantAccessService` enforces business scope (`RequireBusinessId`, `EnsureBusinessMatch`) unless user is Super Admin.
4. Many services pass `businessId` to repositories so queries are tenant-filtered.
5. For operational endpoints needing location-level boundaries, controllers call `UserOutletAccessService` to:
   - resolve allowed filters
   - pin non-owner writes to default outlet
   - validate outlet/warehouse membership before read/write actions
6. Super Admin vs Business Owner behavior:
   - Super Admin: global cross-business scope
   - Business Owner: scoped to own business outlets/warehouses
   - non-owner users: generally constrained to default outlet or role-limited authorized set

## Gaps or risks found

- No global EF query filter for tenant isolation.
  - Isolation depends on each service/repository manually applying scope.
- Inconsistent module coverage for tenant filtering.
  - `InventoryController`/`InventoryService`/`InventoryRepository` allow broad reads without tenant scope checks.
  - `GrnService` and `GrnRepository` currently operate without explicit `BusinessId` constraints.
  - `PosController` accepts `outletId` input but does not enforce authorized outlet membership before lookup/stock-hint/cart-pricing.
- Legacy compatibility path can over-broaden scope.
  - `UserOutletAccessService` returns all outlets/warehouses if a BusinessOwner has no `BusinessId` claim (legacy fallback).
- Role-switch outlet validation is existence-based, not business-bound in `RoleSwitchService`.
- Outlet/Warehouse rules are strong in some flows (sales, transfers) but not uniformly applied across all inventory/GRN endpoints.

## Recommended improvements

- Introduce global tenant scoping patterns:
  - EF query filters where feasible, or
  - mandatory repository signatures that always include scope context.
- Add tenant + location authorization middleware/helpers for shared use across controllers.
- Refactor inventory and GRN modules to require business scope input and validate outlet/warehouse authorization consistently.
- In POS endpoints, validate `outletId` against `IUserOutletAccessService` before executing lookup/price/stock queries.
- Remove or tightly control legacy BusinessOwner no-business fallback with migration plan.
- Add integration tests that assert cross-business data cannot be read or mutated by non-SuperAdmin principals.

## Acceptance criteria

- All data access paths for business-owned entities are scoped by `BusinessId` unless caller is Super Admin.
- All location-scoped reads/writes enforce outlet/warehouse authorization using a shared mechanism.
- Business Owner sees only own business outlets/warehouses and cannot access other businesses.
- Non-owner roles cannot query or mutate data outside authorized outlet/warehouse scope.
- Role-switch acting outlet must belong to the same business as the real owner account.
- Automated tests cover cross-tenant and cross-location denial cases for inventory, GRN, sales, transfers, and reports.
