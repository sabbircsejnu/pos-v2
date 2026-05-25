# 📋 Complete Implementation Roadmap - Retail POS System

> **Project Status:** Phase 4.1 Backend Complete (Purchase Order Management - Backend) ✅  
> **Current Phase:** Phase 4.1 Frontend - Purchase Order UI Components  
> **Last Updated:** 2026-01-31  
> **Estimated Total Duration:** 20 Weeks (5 Months)

---

## 🎯 Tech Stack
- **Backend:** ASP.NET Core 8+ Web API
- **Frontend:** Angular 18+ (Standalone Components)
- **Database:** PostgreSQL 16+
- **ORM:** Entity Framework Core 9.0
- **Authentication:** JWT Bearer Tokens
- **State Management:** Angular Signals + Optional NgRx
- **UI Library:** PrimeNG or Angular Material
- **Architecture:** Clean Architecture Pattern

---

## 📊 Overall Progress Tracking

### Phase Status Legend
- ⬜ Not Started
- 🟦 In Progress
- ✅ Completed
- ⚠️ Blocked
- 🔄 Under Review

### Current Phase Status
```
Foundation:              ✅ Complete
Authentication:          ✅ Complete
User & Role Mgmt:        ✅ Complete
Navigation System:       ✅ Complete
Master Data:             ✅ Complete
Product Management:      ✅ Complete
Inventory:               ✅ Complete
Advanced Variations:     ✅ Complete
Purchase Orders:         🟦 In Progress
Pricing Engine:          ⬜ Not Started (NEW)
Stock Ledger:            ⬜ Not Started (NEW)
GRN & Receiving:         ⬜ Not Started
POS Performance Layer:   ⬜ Not Started (NEW)
POS & Sales:             ⬜ Not Started
Returns & Exchanges:     ⬜ Not Started (NEW)
Stock Movement:          ⬜ Not Started
Accounting:              ⬜ Not Started
Reporting & Dashboard:   ⬜ Not Started
Audit & Settings:        ⬜ Not Started
Testing & Deployment:    ⬜ Not Started
```

---

## 🗺️ PHASE 1: Foundation & Authentication (Week 1-2)

### ✅ Priority 1.1: Authentication & Authorization System
**Status:** ✅ Complete  
**Duration:** 1 week (Completed: 2026-01-28)  
**Priority:** 🔴 Critical

#### Backend Tasks (ASP.NET Core API)
- [x] Install packages: `Microsoft.AspNetCore.Authentication.JwtBearer`, `BCrypt.Net-Next`
- [x] Create `AuthenticationService` interface and implementation
- [x] Create `TokenService` for JWT generation/validation
- [x] Implement password hashing with BCrypt
- [x] Create DTOs:
  - [x] `LoginRequestDto` (email, password)
  - [x] `LoginResponseDto` (token, refreshToken, user info)
  - [x] `RegisterRequestDto`
  - [x] `RefreshTokenRequestDto`
- [x] Create `AuthController`:
  - [x] `POST /api/auth/login`
  - [x] `POST /api/auth/register`
  - [x] `POST /api/auth/refresh-token`
  - [x] `POST /api/auth/logout`
  - [x] `GET /api/auth/me`
- [x] Add JWT configuration in `appsettings.json`
- [x] Register JWT authentication in `Program.cs`
- [x] Create `[Authorize]` attribute helpers
- [x] Implement custom authorization policies for permissions
- [x] Add audit logging for authentication events (AuditLog entity ready)

#### Frontend Tasks (Angular)
- [x] Setup Angular 21 project: `ng new retailpos-frontend --standalone`
- [x] Install dependencies: Tailwind CSS v3 configured
- [x] Create `auth` module folder structure:
  - [x] `services/auth.service.ts`
  - [x] `guards/auth.guard.ts` (authGuard, loginGuard, permissionGuard, roleGuard)
  - [x] `interceptors/auth.interceptor.ts`
  - [x] `interceptors/error.interceptor.ts`
  - [x] `models/auth.models.ts`
- [x] Implement `AuthService`:
  - [x] `login(email, password)`
  - [x] `register(userData)`
  - [x] `logout()`
  - [x] `refreshToken()` (stub implemented)
  - [x] `isAuthenticated()` with JWT expiration check
  - [x] Store tokens in localStorage
  - [x] `hasPermission()`, `hasAnyPermission()`, `hasRole()` methods
- [x] Create `AuthGuard` for route protection (4 guards implemented)
- [x] Create `AuthInterceptor` to attach JWT to requests
- [x] Create `ErrorInterceptor` for global error handling
- [x] Create components:
  - [x] `login.component.ts` (email, password, remember me, modern Tailwind UI)
  - [x] `register.component.ts` (user registration form with password match validator)
  - [x] `dashboard.component.ts` (protected with stats cards)
  - [ ] `forgot-password.component.ts` (not yet implemented)
- [x] Setup routing with guards
- [x] Add loading indicators
- [x] Implement error display in components

#### Testing Checklist
- [x] Test login with valid credentials ✅
- [x] Test login with invalid credentials ✅
- [ ] Test token refresh mechanism (stub implemented, needs full testing)
- [x] Test logout and token cleanup ✅
- [x] Test protected routes with AuthGuard ✅
- [x] Test token expiration handling ✅

#### Additional Completed Items
- [x] Docker PostgreSQL setup (postgres:16-alpine)
- [x] Database migrations applied (25 tables)
- [x] Seed data created with Super Admin user
- [x] Default roles: Super Admin, Admin, Manager, Cashier, Stock Manager, User
- [x] Default users: admin@retailpos.com, manager@retailpos.com, cashier@retailpos.com
- [x] Password: Admin@123 (BCrypt hashed)
- [x] Swagger UI configured at http://localhost:5000/swagger
- [x] JWT Authentication configured in Swagger
- [x] API running on port 5000
- [x] Frontend running on port 4200
- [x] CORS configured for Angular app
- [x] Build errors fixed (Tailwind CSS v4→v3 downgrade)

---

### ✅ Priority 1.2: User & Role Management Module
**Status:** ✅ Complete (Core Features)  
**Duration:** 1 week (Completed: 2026-01-28)  
**Priority:** 🔴 Critical

#### Backend Tasks
- [x] Create repositories:
  - [x] `IUserRepository` interface (12 methods)
  - [x] `UserRepository` implementation (with EF Core, pagination, filtering)
  - [x] `IRoleRepository` interface (8 methods)
  - [x] `RoleRepository` implementation
- [x] Create services:
  - [x] `IUserService` interface (10 methods)
  - [x] `UserService` implementation (with BCrypt, validation, error handling)
  - [x] `IRoleService` interface (6 methods)
  - [x] `RoleService` implementation (with JSONB permissions)
- [x] Create DTOs:
  - [x] `UserDto` (response with role/outlet names)
  - [x] `CreateUserDto` (name, email, password, roleId, outletId)
  - [x] `UpdateUserDto`
  - [x] `ChangePasswordDto` (currentPassword, newPassword)
  - [x] `RoleDto` (with permissions array)
  - [x] `CreateRoleDto` (name, permissions[])
  - [x] `UpdateRoleDto`
  - [x] `UserListDto` (for pagination with totalCount)
- [ ] Setup AutoMapper profiles (❌ Skipped - using manual mapping)
  - [ ] `UserProfile`
  - [ ] `RoleProfile`
- [ ] Create validators (FluentValidation) (❌ Skipped - using DataAnnotations)
  - [ ] `CreateUserDtoValidator`
  - [ ] `UpdateUserDtoValidator`
  - [ ] `ChangePasswordDtoValidator`
  - [ ] `CreateRoleDtoValidator`
- [x] Create `UsersController`:
  - [x] `GET /api/users` (pagination, search, filter by role/outlet/status)
  - [x] `GET /api/users/{id}`
  - [x] `POST /api/users` (with BCrypt password hashing)
  - [x] `PUT /api/users/{id}`
  - [x] `DELETE /api/users/{id}` (soft delete - set IsActive=false)
  - [x] `POST /api/users/{id}/change-password` (validates current password)
  - [x] `GET /api/users/outlet/{outletId}`
  - [x] `GET /api/users/role/{roleId}`
  - [x] `PUT /api/users/{id}/activate`
  - [x] `PUT /api/users/{id}/deactivate`
  - [ ] `GET /api/users/{id}/audit-logs` (⏳ Pending - AuditLog integration)
- [x] Create `RolesController`:
  - [x] `GET /api/roles`
  - [x] `GET /api/roles/{id}`
  - [x] `POST /api/roles`
  - [x] `PUT /api/roles/{id}`
  - [x] `DELETE /api/roles/{id}` (validates not assigned to users)
  - [x] `GET /api/roles/permissions` (returns 40 predefined permissions)
- [x] Implement permission constants/enum (40 permissions organized by category)
- [x] Add audit logging for all user/role changes (ILogger integration)
- [x] Registered all services in Program.cs DI container

#### Frontend Tasks
- [x] Create `user-management` module structure
- [x] Create models:
  - [x] `user.model.ts` (User, CreateUserRequest, UpdateUserRequest, ChangePasswordRequest)
  - [x] `role.model.ts` (Role, CreateRoleRequest, UpdateRoleRequest)
  - [ ] `permission.model.ts` (⏳ Using inline definitions)
- [x] Create services:
  - [x] `user.service.ts` (with Angular signals for reactive state)
  - [x] `role.service.ts` (with Angular signals)
- [x] Create components:
  - [x] `user-list.component.ts` (DataTable with pagination, Tailwind CSS)
  - [ ] `user-create.component.ts` (⏳ Pending Phase 1.3 - shows placeholder alert)
  - [ ] `user-edit.component.ts` (⏳ Pending Phase 1.3 - shows placeholder alert)
  - [ ] `user-details.component.ts` (⚠️ Using alert dialog temporarily)
  - [ ] `change-password.component.ts` (⏳ Pending Phase 1.3)
  - [x] `role-list.component.ts` (Grid cards layout)
  - [ ] `role-create.component.ts` (⏳ Pending Phase 1.3)
  - [ ] `role-edit.component.ts` (⏳ Pending Phase 1.3 - shows placeholder alert)
  - [ ] `permission-selector.component.ts` (⏳ Pending Phase 1.3)
- [x] Implement features:
  - [x] Search users by name/email (Enter key trigger)
  - [x] Filter by role/outlet/status (dropdowns)
  - [ ] Sort by columns (⏳ Pending - backend supports, UI not implemented)
  - [x] Pagination controls (page size 10, navigation buttons)
  - [x] Individual activate/deactivate with confirmation
  - [ ] Bulk actions (⏳ Pending Phase 1.3)
  - [ ] Export to Excel (⏳ Pending Phase 1.3)
- [ ] Create user profile page (⏳ Pending Phase 1.3)
- [x] Add form validations (email format, password strength - in DTOs)
- [x] Implement confirmation dialogs for delete actions

#### Testing Checklist
- [x] Create user with valid data ✅
- [ ] Create user with duplicate email (should fail) (⏳ Backend validates, not tested)
- [x] Update user information ✅
- [x] Change password via API ✅
- [x] Deactivate/activate user ✅
- [x] Delete user (soft delete) ✅
- [x] Create role with permissions ✅
- [ ] Update role permissions (⏳ Backend supports, UI pending)
- [x] Assign role to user (during user creation) ✅
- [x] Delete role validation (prevents deletion if assigned) ✅

#### Additional Completed Features
- [x] Font Awesome 6.4.0 icons integrated
- [x] Success/error message toasts (3s auto-dismiss)
- [x] Loading states with spinners
- [x] Event.stopPropagation() for button clicks in table rows
- [x] Routes added to app.routes.ts (/users, /roles)
- [x] API running on port 5000 with Swagger
- [x] Frontend running on port 4200
- [x] 16 API endpoints fully functional
- [x] Comprehensive documentation (PHASE_1_2_COMPLETE.md)

#### Phase 1.3 Enhancement
- [x] Create user-form component (create/edit modes) ✅
- [x] Create user-details component (full view page) ✅
- [x] Create role-form component with permission checkboxes ✅
- [x] Implement change-password modal ✅
- [x] Update routes (/users/create, /users/edit/:id, /users/:id, /roles/create, /roles/edit/:id) ✅
- [x] Update list components to navigate to actual forms ✅
- [ ] Add bulk actions (select all, bulk activate/deactivate) (⏳ Future enhancement)
- [ ] Add Excel export functionality (⏳ Future enhancement)
- [ ] Implement AutoMapper and FluentValidation (❌ Not needed - using manual mapping and DataAnnotations)

**Phase 1.3 Status:** ✅ Complete (2026-01-29)
- All CRUD forms implemented with validation
- Professional UI with Tailwind CSS
- 7 new routes added
- Change password modal ready for integration
- See `PHASE_1_3_COMPLETE.md` for full details

---

### ✅ Priority 1.4: Modern Navigation System
**Status:** ✅ Complete  
**Duration:** 1 day (Completed: 2026-01-29)  
**Priority:** 🔴 Critical

#### Overview
Implemented a modern, responsive navigation system with:
- Top menu bar with parent menus and dropdowns
- Left sidebar showing child menus
- Persistent across all authenticated pages
- [ ] Permission-based menu filtering
- [ ] Button-level permission directives/components
- [ ] Route + component-level permission enforcement consistency check

#### Components Created
- [x] `LayoutComponent` - Main wrapper combining navbar, sidebar, and content
- [x] `NavbarComponent` - Top navigation bar (fixed, 64px height)
  - [x] Sidebar toggle button
  - [x] Logo and brand name
  - [x] Main navigation menu with dropdowns
  - [x] Notification bell (with badge)
  - [x] User dropdown (profile, change password, settings, logout)
- [x] `SidebarComponent` - Left sidebar (fixed, 256px width)
  - [x] Parent menu header with icon
  - [x] Child menu items with active state highlighting
  - [x] Collapsible on mobile
  - [x] Custom scrollbar styling
- [x] `MenuService` - Global state management
  - [x] `menuItems` signal - All menu items
  - [x] `activeParentMenu` signal - Currently selected parent
  - [x] `sidebarOpen` signal - Sidebar visibility
  - [x] Methods: setActiveParentMenu(), toggleSidebar(), getChildMenuItems()
- [x] `menu.model.ts` - Menu structure with MenuItem interface

#### Menu Structure (7 Parents, 16 Children)
- [x] Dashboard (single route)
- [x] Administration (2 children: Users, Roles)
- [x] Master Data (4 children: Outlets, Warehouses, Categories, Suppliers)
- [x] Products (2 children: Product List, Inventory)
- [x] Sales (3 children: POS, Sales List, Customers)
- [x] Reports (3 children: Sales, Inventory, Financial)
- [x] Settings (2 children: Company, System)

#### Routing Integration
- [x] Updated app.routes.ts to use LayoutComponent as parent for all authenticated routes
- [x] Login and Register routes remain outside layout
- [x] All existing pages work within layout

#### Component Updates
- [x] Dashboard component simplified (removed duplicate header, logout button)
- [x] User/Role list components compatible with layout
- [x] All routes tested and functional

#### Features
- [x] Responsive design (mobile overlay, collapsible sidebar)
- [x] Dropdown menus with click-outside-to-close
- [x] Active route highlighting in sidebar
- [x] Smooth transitions and animations (300ms)
- [x] Gradient blue theme (#3B82F6 → #2563EB)
- [x] Font Awesome icons
- [x] User authentication integration (shows logged-in user)
- [x] Logout functionality in navbar

#### Documentation
- [x] Complete navigation documentation in `NAVIGATION_SYSTEM.md`

#### Future Enhancements (Not Started)
- [ ] Permission-based menu filtering (hide items user can't access)
- [ ] Actual notification system (currently shows badge "3")
- [ ] User profile page
- [ ] Breadcrumbs
- [ ] Remember sidebar state in localStorage
- [ ] Keyboard navigation
- [ ] Search functionality
- [ ] Quick access/favorites
- [ ] Dark mode toggle

---

## 🗺️ PHASE 2: Master Data Setup (Week 3-4)

### 🟦 Priority 2.1: Outlet & Warehouse Management Module
**Status:** ✅ Backend Complete, ✅ Frontend Complete  
**Duration:** Completed  
**Priority:** 🟠 High

#### Backend Tasks
- [x] Create repositories:
  - [x] `IOutletRepository` and implementation (❌ Direct service usage)
  - [x] `IWarehouseRepository` and implementation (❌ Direct service usage)
- [x] Create services:
  - [x] `IOutletService` and implementation
  - [x] `IWarehouseService` and implementation
- [x] Create DTOs:
  - [x] `OutletDto`, `CreateOutletDto`, `UpdateOutletDto`
  - [x] `WarehouseDto`, `CreateWarehouseDto`, `UpdateWarehouseDto`
  - [ ] `OutletStatsDto` (sales, inventory count, etc.) (⏳ Future enhancement)
  - [ ] `WarehouseStatsDto` (⏳ Future enhancement)
- [ ] Create validators (❌ Using DataAnnotations instead)
- [x] Create `OutletsController`:
  - [x] `GET /api/outlets`
  - [x] `GET /api/outlets/{id}`
  - [x] `POST /api/outlets`
  - [x] `PUT /api/outlets/{id}`
  - [x] `DELETE /api/outlets/{id}`
  - [ ] `GET /api/outlets/{id}/stats` (⏳ Future enhancement)
  - [ ] `GET /api/outlets/{id}/inventory` (⏳ Future enhancement)
  - [ ] `GET /api/outlets/{id}/users` (⏳ Future enhancement)
- [x] Create `WarehousesController` (similar endpoints)
- [x] Add audit logging (using ILogger)

#### Frontend Tasks
- [x] Create `location-management` module (folder structure created)
- [x] Create models: `outlet.model.ts`, `warehouse.model.ts`
- [x] Create services: `outlet.service.ts`, `warehouse.service.ts`
- [x] Create components:
  - [x] `outlet-list.component.ts` (with search, pagination, CRUD actions)
  - [x] `outlet-form.component.ts` (create/edit modes)
  - [x] `outlet-details.component.ts` (full details view)
  - [x] `warehouse-list.component.ts` (with search, pagination, CRUD actions)
  - [x] `warehouse-form.component.ts` (create/edit modes)
  - [x] `warehouse-details.component.ts` (full details view)
  - [ ] `location-dashboard.component.ts` (⏳ Future enhancement)
- [ ] Create shared component:
  - [ ] `location-selector.component.ts` (dropdown for navbar) (⏳ Future enhancement)
- [ ] Implement location switching in navbar (⏳ Future enhancement)
- [ ] Store selected location in service/signal (⏳ Future enhancement)
- [ ] Create location stats cards (⏳ Future enhancement)

#### Testing Checklist
- [x] Create outlet via API ✅
- [x] Create warehouse via API ✅
- [ ] View outlet statistics (⏳ Pending stats endpoint)
- [ ] Switch between locations in navbar (⏳ Future enhancement)

---

### ✅ Priority 2.2: Category Management Module
**Status:** ✅ Complete  
**Duration:** Completed in 1 day (2026-01-30)  
**Priority:** 🟠 High

#### Backend Tasks
- [x] Create `ICategoryRepository` and implementation (with tree queries, recursive children)
- [x] Create `ICategoryService` and implementation (with validation, circular reference prevention)
- [x] Create DTOs: `CategoryDto`, `CategoryTreeDto`, `CreateCategoryDto`, `UpdateCategoryDto`, `MoveCategoryDto`
- [ ] Implement recursive tree query for nested categories ⏳ (using iterative approach for now)
- [x] Create `CategoriesController`:
  - [x] `GET /api/categories` (flat list)
  - [x] `GET /api/categories/tree` (tree structure)
  - [x] `GET /api/categories/{id}`
  - [x] `GET /api/categories/{id}/children`
  - [x] `POST /api/categories`
  - [x] `PUT /api/categories/{id}`
  - [x] `DELETE /api/categories/{id}` (prevents deletion if has children/products)
  - [x] `PUT /api/categories/{id}/move` (change parent)
- [x] Enhanced Category entity (Description, ImageUrl, DisplayOrder, IsActive, ParentCategoryId)
- [x] Database migration applied successfully
- [x] Audit logging implemented (ILogger integration)

#### Frontend Tasks
- [x] Create `category.model.ts` (Category, CategoryTree, Request DTOs)
- [x] Create `category.service.ts` (with Angular signals, tree helpers)
- [x] Create components:
  - [x] `category-list.component.ts` (dual view: tree/list with toggle)
  - [x] `category-form.component.ts` (create/edit modes)
- [x] Implement features:
  - [x] Tree view with expand/collapse (colored borders by level)
  - [x] List view with table display
  - [x] Expand all/collapse all functionality
  - [x] Search functionality (list view)
  - [x] Parent category selector (hierarchical dropdown)
  - [x] Circular reference prevention
  - [x] Confirmation dialogs for delete actions
  - [x] Success/error notifications
- [x] Routes added: `/categories`, `/categories/create`, `/categories/edit/:id`
- [ ] Drag-drop for reordering (⏳ Future enhancement)
- [ ] Image upload integration (⏳ Future enhancement)

#### Testing Checklist
- [x] Create root category ✅
- [x] Create child categories ✅
- [ ] Move category to different parent ⏳ (API ready, UI pending test)
- [ ] Delete category with children (should fail) ⏳
- [x] View category tree ✅
- [x] Toggle between tree and list views ✅

---

### ✅ Priority 2.3: Supplier Management Module
**Status:** ✅ Complete  
**Duration:** 1 day (Completed: 2026-01-31)  
**Priority:** 🟠 High

#### Backend Tasks
- [x] Create `ISupplierRepository` and implementation (11 methods in Infrastructure layer)
- [x] Create `ISupplierService` and implementation (with validation, performance metrics)
- [x] Create DTOs: `SupplierDto`, `CreateSupplierDto`, `UpdateSupplierDto`, `SupplierPerformanceDto`, `SupplierSearchDto`, `SupplierListDto`
- [x] Create `SuppliersController`:
  - [x] `GET /api/suppliers` (list all)
  - [x] `GET /api/suppliers/{id}` (get by ID)
  - [x] `POST /api/suppliers` (create)
  - [x] `PUT /api/suppliers/{id}` (update)
  - [x] `DELETE /api/suppliers/{id}` (with cascade protection)
  - [x] `GET /api/suppliers/{id}/performance` (credit utilization, health status)
  - [x] `POST /api/suppliers/search` (server-side filtering, sorting, pagination)
  - [ ] `GET /api/suppliers/{id}/purchase-orders` (⏳ Future - requires Phase 4.1)
  - [ ] `GET /api/suppliers/{id}/bills` (⏳ Future - requires Phase 4.2)
- [x] Name uniqueness validation
- [x] Cascade delete protection (checks POs and Bills)
- [x] Credit health calculation (Good < 70%, Warning 70-90%, Critical ≥ 90%)
- [x] Server-side search (name, contact, address - case-insensitive with `.ToLower()`)
- [x] Server-side filters (min/max credit limit)
- [x] Server-side sorting (name, creditLimit, createdAt)
- [x] Server-side pagination (10/25/50/100 per page)
- [x] DI registration in Program.cs
- [x] Build successful with no errors

#### Frontend Tasks
- [x] Create `supplier.model.ts` (6 interfaces with performance metrics + search request/response)
- [x] Create `supplier.service.ts` (with signals, 7 API methods including search)
- [x] Create components:
  - [x] `supplier-list.component.ts` (server-side search, pagination, sorting, CRUD actions, credit utilization colors)
  - [x] `supplier-form.component.ts` (create/edit with validation)
  - [x] `supplier-details.component.ts` (full view with performance dashboard)
- [x] Advanced search with debouncing (500ms delay, prevents excessive API calls)
- [x] Sortable columns (click headers to toggle asc/desc)
- [x] Pagination controls (page size selector, prev/next buttons, page numbers)
- [x] Credit limit range filters (min/max inputs with debouncing)
- [x] Create supplier performance view (6 metrics, health badge, utilization bar)
- [x] Modern black/white theme matching product management
- [x] Routes configured: `/suppliers`, `/suppliers/create`, `/suppliers/edit/:id`, `/suppliers/:id`
- [x] Build successful with no errors

#### Testing Checklist
- [x] Create supplier ✅
- [x] Update supplier credit limit ✅
- [x] View supplier performance metrics ✅
- [x] Delete supplier (with cascade protection) ✅
- [x] Search suppliers (debounced, case-insensitive) ✅
- [x] Filter by credit limit range ✅
- [x] Sort by name/credit/date ✅
- [x] Pagination (page size change, prev/next) ✅
- [x] Credit health status display ✅

#### Documentation
- [x] Complete documentation in `PHASE_2_3_COMPLETE.md` (850 LOC)

### ⬜ Priority 2.4: Role-Based UI Permission Layer (NEW)
**Status:** Not Started  
**Duration:** 2 days  
**Priority:** 🔴 Critical

#### Frontend Tasks
- [ ] Hide menus based on permission map
- [ ] Hide action buttons (create/edit/delete/approve)
- [ ] Add reusable permission directive/helper
- [ ] Prevent unauthorized view rendering even if route is manually accessed
- [ ] Align UI permissions with backend policies

#### Testing Checklist
- [ ] Cashier cannot access admin routes
- [ ] Stock manager sees only relevant actions
- [ ] Direct URL navigation blocked correctly

---

## 🗺️ PHASE 3: Product & Inventory Core (Week 5-6)

### ✅ Priority 3.1: Product Management Module (Multi-Variant)
**Status:** ✅ Complete  
**Duration:** 1.5 weeks (Completed: 2026-01-30)  
**Priority:** 🔴 Critical

#### Backend Tasks
- [x] Create repositories:
  - [x] `IProductRepository` and implementation (14 methods)
  - [x] `IProductVariantRepository` and implementation (10 methods)
- [x] Create services:
  - [x] `IProductService` and implementation (10 methods with variant management)
- [x] Create DTOs:
  - [x] `ProductDto` (with variants embedded, CategoryName, TotalStock)
  - [x] `CreateProductDto` (with variants collection)
  - [x] `UpdateProductDto`
  - [x] `ProductVariantDto`
  - [x] `CreateProductVariantDto` (with Name, Attributes JSON)
  - [x] `UpdateProductVariantDto`
  - [x] `ProductListDto` (for pagination with HasNext/PreviousPage)
  - [x] `ProductSearchDto` (search filters with price range, category, variants)
  - [ ] `BulkImportDto` (not yet implemented)
- [x] Implement JSONB attribute handling for variants
- [x] Create `ProductsController`:
  - [x] `GET /api/products` (list all products)
  - [x] `POST /api/products/search` (with filters, pagination, sorting, case-insensitive search)
  - [x] `GET /api/products/{id}`
  - [x] `GET /api/products/sku/{sku}` (get by SKU)
  - [x] `GET /api/products/barcode/{barcode}` (get by barcode)
  - [x] `GET /api/products/category/{categoryId}`
  - [x] `POST /api/products` (create with variants)
  - [x] `PUT /api/products/{id}` (update product)
  - [x] `DELETE /api/products/{id}` (with inventory protection)
  - [x] `POST /api/products/generate-sku` (auto-generate SKU)
  - [ ] `GET /api/products/{id}/variants` (separate variant management not yet implemented)
  - [ ] `POST /api/products/{id}/variants` (not yet implemented)
  - [ ] `PUT /api/products/variants/{id}` (not yet implemented)
  - [ ] `DELETE /api/products/variants/{id}` (not yet implemented)
  - [ ] `POST /api/products/bulk-import` (not yet implemented)
  - [ ] `POST /api/products/generate-barcode` (not yet implemented)
- [x] Enhanced Product entity (added SKU, HasVariants, ImageUrl, IsActive)
- [x] Enhanced ProductVariant entity (added Name property)
- [x] Auto-SKU generation algorithm (3-char prefix + timestamp)
- [x] SKU/Barcode uniqueness validation
- [x] Case-insensitive search (Name, Description, Sku, Barcode with `.ToLower()`)
- [x] Delete protection (prevents deletion if has inventory)
- [x] Migration applied: `20260129194453_EnhanceProductEntities`
- [ ] Barcode validation (EAN-13, UPC) (not yet implemented)
- [ ] Excel/CSV import parser (not yet implemented)
- [ ] Image upload handling (URL only, no file upload yet)

#### Frontend Tasks
- [x] Create models:
  - [x] `product.model.ts` (Product, ProductVariant, Request/Response interfaces)
  - [x] `product-variant.model.ts` (included in product.model.ts)
  - [ ] `product-attribute.model.ts` (VariantAttribute helper interface only)
- [x] Create services:
  - [x] `product.service.ts` (with signals, 10 API methods)
  - [ ] `barcode.service.ts` (not yet implemented)
- [x] Create components:
  - [x] `product-list.component.ts` (DataTable with variants, search, filters)
  - [x] `product-form.component.ts` (create/edit with variant manager)
  - [ ] `variant-manager.component.ts` (integrated in product-form)
  - [ ] `attribute-input.component.ts` (JSON text input only)
  - [ ] `product-search.component.ts` (autocomplete not yet implemented)
  - [ ] `barcode-scanner.component.ts` (not yet implemented)
  - [ ] `bulk-import.component.ts` (not yet implemented)
  - [ ] `image-uploader.component.ts` (URL input only)
- [x] Implement features:
  - [x] Dynamic variant management (add/edit/remove in form)
  - [x] Automatic SKU generation (API call)
  - [x] Variant price calculation (base + adjustment with final price display)
  - [x] Image URL support (no preview/cropping yet)
  - [x] Advanced search filters (category, price range, status, variants)
  - [x] Debounced search (500ms delay, prevents excessive API calls)
  - [x] Case-insensitive search (works with uppercase/lowercase)
  - [x] Pagination with configurable page sizes (10/25/50/100)
  - [x] Sortable columns (Name, Category, Price, Created Date)
  - [x] Product images with fallback placeholder
  - [x] Stock indicators (color-coded green/red)
  - [x] Variant count badges
  - [x] Active/Inactive status badges
  - [ ] Barcode generation (not yet implemented)
  - [ ] Image preview and cropping (not yet implemented)
  - [ ] Excel template download for bulk import (not yet implemented)
  - [ ] Import validation and error display (not yet implemented)
- [x] Add routes: `/products`, `/products/create`, `/products/edit/:id`
- [x] Validation: Name (required), Category (required), Price (>0), Tax (0-100%)
- [ ] Product quick-view modal (not yet implemented)

#### Testing Checklist
- [ ] Create product without variants
- [ ] Create product with multiple variants
- [ ] Add variant to existing product
- [ ] Update variant attributes
- [ ] Delete variant
- [ ] Search product by name/SKU
- [ ] Scan barcode to find product
- [ ] Bulk import products from Excel

---

### ✅ Priority 3.2: Inventory Management Module
**Status:** ✅ Complete  
**Duration:** 1 day (Completed: 2026-01-31)  
**Priority:** 🔴 Critical

#### Backend Tasks
- [x] Create `IInventoryRepository` and implementation
- [x] Create `IInventoryService` and implementation
- [x] Create DTOs:
  - [x] `InventoryDto`
  - [x] `InventoryByLocationDto`
  - [x] `LowStockDto`
  - [x] `InventoryValuationDto`
  - [x] `CategoryValuationDto`
  - [x] `UpdateStockThresholdDto`
  - [x] `InventorySearchDto`
- [x] Create `InventoryController`:
  - [x] `GET /api/inventory` (filter by location, product, low stock)
  - [x] `GET /api/inventory/{id}`
  - [x] `GET /api/inventory/variant/{variantId}`
  - [x] `GET /api/inventory/location/{locationId}?type={outlet|warehouse}`
  - [x] `GET /api/inventory/low-stock` (with optional location filter)
  - [x] `GET /api/inventory/out-of-stock`
  - [x] `GET /api/inventory/expiring-soon?days={30}`
  - [x] `GET /api/inventory/valuation` (by location, total)
  - [x] `GET /api/inventory/summary` (aggregated stats)
  - [x] `POST /api/inventory/search` (7 filters: variant, location, category, search, stock level, batch, expiry)
  - [x] `PUT /api/inventory/{id}/threshold` (update low stock alert threshold)
  - [ ] `GET /api/inventory/batch/{batchNumber}` (not yet implemented)
- [x] Implement inventory valuation calculations (CostPrice, RetailPrice)
- [x] Price calculation: Product.BasePrice + Variant.PriceAdjustment
- [x] Cost calculation: Product.CostPrice + Variant.CostAdjustment
- [x] Polymorphic location support (LocationId + LocationType: outlet/warehouse)
- [ ] Create background job for low stock alerts (not yet implemented)
- [x] Add expiry date tracking

#### Frontend Tasks
- [x] Create `inventory.model.ts`, `inventory.service.ts`
- [x] Create components:
  - [x] `inventory-list.component.ts` (comprehensive table with all features)
  - [x] `low-stock-alerts.component.ts` (dashboard with summary cards)
  - [ ] `inventory-dashboard.component.ts` (not yet implemented)
  - [ ] `expiring-items.component.ts` (not yet implemented)
  - [ ] `inventory-valuation.component.ts` (not yet implemented)
  - [ ] `batch-tracking.component.ts` (not yet implemented)
  - [ ] `threshold-settings.component.ts` (modal integrated in list)
- [x] Implement features:
  - [x] Color-coded stock levels (green: in stock, red: low/out of stock)
  - [x] Real-time stock updates via service signals
  - [x] Filter by location (outlet/warehouse dropdowns)
  - [x] Advanced search (7 filters)
  - [x] Low stock threshold update modal
  - [x] Summary statistics cards (total, critical, shortage)
  - [ ] Export to Excel (not yet implemented)
  - [ ] Low stock email notifications (not yet implemented)
- [x] Added routes: `/inventory`, `/inventory/low-stock`
- [x] Navigation menu items added

#### Testing Checklist
- [x] View stock levels by location (outlets/warehouses)
- [x] Check low stock alerts (≤ threshold)
- [x] View expiring items (within 30 days)
- [x] Calculate inventory valuation (cost/retail/profit)
- [x] Update stock threshold
- [x] Search with multiple filters
- [x] Filter by category
- [x] View summary statistics

**Phase 3.2 Status:** ✅ Complete (2026-01-31)
- Comprehensive inventory tracking system operational
- Low stock alerts functional with real-time data
- Financial valuation with profit margin calculations
- Multi-location support (outlets + warehouses)
- Advanced search with 7 filter options
- See `PHASE_3_2_COMPLETE.md` for full details (445 lines, 1,652 LOC)

---
✅ Priority 3.3: Advanced Product Variations System
**Status:** ✅ Complete  
**Duration:** 1 day (Completed: 2026-01-31)  
**Priority:** 🔴 Critical

This phase focused on creating a sophisticated system for managing product variations (like Size, Color, Material) that can be dynamically combined to generate product variants.

#### Backend Tasks
- [x] Create `IVariationRepository` and implementation
- [x] Create `IVariationOptionRepository` and implementation
- [x] Create `IVariationService` and implementation
- [x] Create DTOs:
  - [x] `VariationDto` (with options list)
  - [x] `CreateVariationDto` (with options)
  - [x] `UpdateVariationDto`
  - [x] `VariationOptionDto`
  - [x] `CreateVariationOptionDto`
  - [x] `UpdateVariationOptionDto`
- [x] Create `VariationsController`:
  - [x] `GET /api/variations` (list with optional includeInactive)
  - [x] `GET /api/variations/{id}`
  - [x] `POST /api/variations` (create with options)
  - [x] `PUT /api/variations/{id}` (update)
  - [x] `DELETE /api/variations/{id}`
  - [x] `POST /api/variations/{variationId}/options` (add option)
  - [x] `PUT /api/variations/options/{id}` (update option)
  - [x] `DELETE /api/variations/options/{id}` (delete option)
- [x] Variation entity (Name, DisplayOrder, IsActive)
- [x] VariationOption entity (Name, PriceAdjustment, DisplayOrder, IsActive)
- [x] Name uniqueness validation
- [x] Cascade delete protection (check if variation is used by products)

#### Frontend Tasks
- [x] Create `variation.model.ts` (Variation, VariationOption, DTOs)
- [x] Create `variation.service.ts` (with signals, 8 API methods)
- [x] Create components:
  - [x] `variations.component.ts` (list view with expand/collapse)
- [x] Implement features:
  - [x] Create variation with multiple options
  - [x] Edit variation (name, display order, status)
  - [x] Add/edit/delete options within variation
  - [x] Display options with price adjustments
  - [x] Expand/collapse option list
  - [x] Confirmation dialogs for delete
  - [x] Inline option management
  - [x] Display order sorting
  - [x] Active/inactive status toggle
- [x] Added route: `/variations`
- [x] Added to navigation menu under Products

#### Testing Checklist
- [x] Create variation (e.g., "Size")
- [x] Add options (Small, Medium, Large)
- [x] Edit variation name
- [x] Add option with price adjustment
- [x] Delete option
- [x] Deactivate variation
- [x] View all variations with options expanded

**Phase 3.3 Status:** ✅ Complete (2026-01-31)
- Full CRUD for Variations and Options
- Inline option management within variation modal
- Price adjustment support for option-level pricing
- Display order control for both variations and options
- Active/Inactive toggling
- Reusable across multiple products
- Navigation menu integration


### ⬜ Priority 3.4: Pricing Engine (NEW)
**Status:** Not Started  
**Duration:** 1 week  
**Priority:** 🔴 Critical

#### Objective
Centralize all pricing logic so POS, reporting, offers, and outlet-based pricing use the same rules.

#### Backend Tasks
- [ ] Create `IPriceService` and implementation
- [ ] Create pricing rule entities/tables:
  - [ ] `PriceRule`
  - [ ] `OutletPriceOverride`
  - [ ] `CampaignPriceRule`
  - [ ] `CustomerTypePriceRule` (optional for wholesale/future)
- [ ] Support pricing inputs:
  - [ ] Base price
  - [ ] Variant adjustment
  - [ ] Outlet override/margin
  - [ ] Discount rule
  - [ ] Campaign/seasonal offer
- [ ] Create DTOs:
  - [ ] `PriceCalculationRequestDto`
  - [ ] `PriceCalculationResponseDto`
  - [ ] `PriceRuleDto`
- [ ] Add APIs:
  - [ ] `GET /api/pricing/calculate`
  - [ ] `GET /api/pricing/product/{variantId}`
  - [ ] `POST /api/pricing/rules`
  - [ ] `PUT /api/pricing/rules/{id}`
- [ ] Add validity range support (`ValidFrom`, `ValidTo`)
- [ ] Add rule priority resolution
- [ ] Add pricing cache with Redis

#### Frontend Tasks
- [ ] Create pricing rule management page
- [ ] Show effective price breakdown in product/POS screens
- [ ] Show campaign badge / override label where applicable

#### Testing Checklist
- [ ] Base + variant price works
- [ ] Outlet-specific pricing works
- [ ] Campaign price overrides standard price
- [ ] Expired discount no longer applies
- [ ] Same pricing result returned across POS and reports

### ⬜ Priority 3.5: Stock Ledger System (NEW)
**Status:** Not Started  
**Duration:** 4 days  
**Priority:** 🔴 Critical

#### Objective
Maintain a full transaction-level history of all stock movement.

#### Backend Tasks
- [ ] Create `StockLedger` entity/table with fields:
  - [ ] `Id`
  - [ ] `ProductVariantId`
  - [ ] `LocationId`
  - [ ] `LocationType`
  - [ ] `TransactionType` (GRN, Sale, TransferOut, TransferIn, Adjustment, Return, Exchange)
  - [ ] `QtyIn`
  - [ ] `QtyOut`
  - [ ] `BalanceAfter`
  - [ ] `ReferenceType`
  - [ ] `ReferenceId`
  - [ ] `Remarks`
  - [ ] `CreatedBy`
  - [ ] `CreatedAt`
- [ ] Create `IStockLedgerRepository` and service
- [ ] Insert ledger rows from all stock-affecting modules
- [ ] Add stock movement query APIs
- [ ] Add reconciliation helpers

#### Frontend Tasks
- [ ] Create stock ledger viewer
- [ ] Add filters by product, location, type, date
- [ ] Add movement history tab from inventory detail page

#### Testing Checklist
- [ ] GRN inserts ledger rows
- [ ] Sale inserts ledger rows
- [ ] Adjustment inserts ledger rows
- [ ] Transfer inserts both outbound and inbound rows
- [ ] BalanceAfter matches inventory state

---

## 🗺️ PHASE 4: Procurement & Receiving (Week 7-8)

### 🟦 Priority 4.1: Purchase Order Management Module
**Status:** 🟦 In Progress (Backend ✅ Complete, Frontend 🟦 In Progress)  
**Duration:** 1 week (Started: 2026-01-31)  
**Priority:** 🔴 Critical

#### Backend Tasks
- [x] Create repositories:
  - [x] `IPurchaseOrderRepository` and implementation (15 methods)
- [x] Create `IPurchaseOrderService` and implementation (12 methods with workflow)
- [x] Implement PO workflow state machine (Draft → Pending → Approved → Received)
- [x] Create DTOs:
  - [x] `PurchaseOrderDto` (with items, supplier/warehouse names)
  - [x] `CreatePurchaseOrderDto` (with items array)
  - [x] `UpdatePurchaseOrderDto` (with items array)
  - [x] `PurchaseOrderItemDto` (with product/variant details)
  - [x] `PurchaseOrderListDto` (with pagination)
  - [x] `PurchaseOrderSearchDto` (7 filters: status, supplier, warehouse, date range, sorting, pagination)
  - [x] `UpdatePurchaseOrderStatusDto` (status + optional reason)
- [x] Create `PurchaseOrdersController`:
  - [x] `GET /api/purchase-orders` (filter by status, supplier, warehouse)
  - [x] `POST /api/purchase-orders/search` (with filters, pagination, sorting)
  - [x] `GET /api/purchase-orders/{id}` (with full details and items)
  - [x] `GET /api/purchase-orders/pending-approvals` (list pending)
  - [x] `GET /api/purchase-orders/total-amount` (aggregated by filters)
  - [x] `POST /api/purchase-orders` (create with items)
  - [x] `PUT /api/purchase-orders/{id}` (update - only draft/pending)
  - [x] `DELETE /api/purchase-orders/{id}` (only if draft/pending with no GRNs)
  - [x] `POST /api/purchase-orders/{id}/submit` (Draft → Pending)
  - [x] `POST /api/purchase-orders/{id}/approve` (Pending → Approved)
  - [x] `POST /api/purchase-orders/{id}/reject` (with reason)
  - [x] `POST /api/purchase-orders/{id}/cancel` (with reason)
  - [ ] `GET /api/purchase-orders/{id}/pdf` (⏳ Future - PDF generation)
  - [ ] `POST /api/purchase-orders/{id}/email` (⏳ Future - send to supplier)
- [x] Automatic total calculation (sum of item quantities × unit prices)
- [x] Status validation (can only update draft/pending, can only delete draft/pending without GRNs)
- [x] Workflow transitions (submit → approve → receive)
- [x] DI registration in Program.cs
- [x] Build successful with 2 warnings only
- [ ] PDF generation for PO (⏳ Future enhancement)
- [ ] Email sending (⏳ Future enhancement)

#### Frontend Tasks
- [x] Create models: `purchase-order.model.ts` (7 interfaces + helper functions)
- [x] Create service: `purchase-order.service.ts` (with signals, 11 API methods)
- [x] Create components:
  - [x] `po-list.component.ts` (with status filters, server-side pagination, workflow actions)
  - [x] `po-form.component.ts` (multi-step form with item management, create/edit modes)
  - [x] `po-details.component.ts` (view with approval actions and audit info)
  - [ ] ~~`po-item-selector.component.ts` (product variant search)~~ (Integrated into form)
  - [ ] ~~`po-approval.component.ts` (approve/reject workflow)~~ (Integrated into details)
- [x] Implement features:
  - [x] Supplier selection dropdown
  - [x] Warehouse selection dropdown
  - [x] Status filtering (draft, pending, approved, received, cancelled, rejected)
  - [x] Date range filtering (start/end date)
  - [x] Debounced search (500ms delay)
  - [x] Server-side pagination (10/25/50/100 per page)
  - [x] Sortable columns (PO#, Date, Supplier, Amount, Status)
  - [x] PO status badge display (colored: draft, pending, approved, etc.)
  - [x] Approval workflow UI (submit/approve/reject/cancel buttons with modals)
  - [x] Action buttons based on status (view, edit, delete, submit, approve, reject, cancel)
  - [x] Empty state with "Create First PO" prompt
  - [x] Currency and date formatting
  - [x] Product variant search and add to items
  - [x] Quantity and price input per item
  - [x] Automatic total calculation (frontend)
  - [x] Dynamic item rows (add/remove items)
  - [x] Validation (required fields, quantity > 0, price >= 0)
  - [x] Reason modal for reject/cancel actions
  - [x] Audit information display (created by, dates)
  - [ ] PDF preview and download (⏳ Future)
  - [ ] Print PO (⏳ Future)
  - [ ] Email PO to supplier (⏳ Future)
- [x] Add routes: `/purchase-orders`, `/purchase-orders/create`, `/purchase-orders/edit/:id`, `/purchase-orders/:id`
- [x] Add navigation menu item under "Procurement"
- [x] Build successful (2.51 MB - main.js: 2.45 MB, styles.css: 56 KB)

#### Testing Checklist
- [ ] Create PO in draft
- [ ] Add items to PO
- [ ] Submit PO for approval
- [ ] Approve PO
- [ ] View PO PDF
- [ ] Cancel PO

---

### ⬜ Priority 4.2: GRN (Goods Received Note) Module
**Status:** Not Started  
**Duration:** 1 week  
**Priority:** 🟠 High

#### Backend Tasks
- [ ] Create repositories:
- [ ] `IGrnRepository` and implementation
- [ ] `IGrnItemRepository` and implementation
- [ ] Create `IGrnService` and implementation
- [ ] Implement stock auto-update on GRN completion
- [ ] Create DTOs:
- [ ] `GrnDto`
- [ ] `CreateGrnDto`
- [ ] `GrnItemDto`
- [ ] `VarianceReportDto`
- [ ] Create `GrnsController`:
- [ ] `GET /api/grns`
- [ ] `GET /api/grns/{id}`
- [ ] `POST /api/grns` (from PO)
- [ ] `PUT /api/grns/{id}`
- [ ] `POST /api/grns/{id}/complete`
- [ ] `GET /api/grns/pending-pos` (POs awaiting receipt)
- [ ] `GET /api/grns/{id}/variance` (ordered vs received)
- [ ] Handle partial receipts (update PO status accordingly)
- [ ] Update inventory on GRN completion
- [ ] Create bill from GRN
- [ ] Insert Stock Ledger records on GRN completion
- [ ] Support partial receipt and backorder tracking
- [ ] Recalculate PO status accurately (Partial Received / Fully Received)
- [ ] Trigger inventory cache invalidation
- [ ] Publish `GRNCompleted` event for accounting integration

#### Frontend Tasks
- [ ] Create models: `grn.model.ts`
- [ ] Create service: `grn.service.ts`
- [ ] Create components:
  - [ ] `grn-list.component.ts`
  - [ ] `grn-create.component.ts` (select PO)
  - [ ] `grn-receipt-form.component.ts`
  - [ ] `grn-details.component.ts`
  - [ ] `variance-report.component.ts`
- [ ] Implement features:
  - [ ] Select PO to receive
  - [ ] Display expected quantities
  - [ ] Input received quantities
  - [ ] Highlight variances (red if less, green if more)
  - [ ] Add notes for discrepancies
  - [ ] Print GRN receipt
- [ ] Show stock update confirmation

#### Testing Checklist
- [ ] Create GRN from approved PO
- [ ] Receive full quantity
- [ ] Receive partial quantity
- [ ] Complete GRN and verify stock update
- [ ] View variance report

---

## 🗺️ PHASE 5: POS & Sales (Week 9-11) - CRITICAL

### ⬜ Priority 5.1: Customer Management Module
**Status:** Not Started  
**Duration:** 3 days  
**Priority:** 🟠 High

#### Backend Tasks
- [ ] Create `ICustomerRepository` and implementation
- [ ] Create `ICustomerService` and implementation
- [ ] Create DTOs:
  - [ ] `CustomerDto`
  - [ ] `CreateCustomerDto`
  - [ ] `UpdateCustomerDto`
  - [ ] `CustomerSearchDto`
- [ ] Create `CustomersController`:
  - [ ] `GET /api/customers`
  - [ ] `GET /api/customers/{id}`
  - [ ] `POST /api/customers`
  - [ ] `PUT /api/customers/{id}`
  - [ ] `DELETE /api/customers/{id}`
  - [ ] `GET /api/customers/search?q={phone/email/name}`
  - [ ] `POST /api/customers/{id}/add-points`
  - [ ] `POST /api/customers/{id}/redeem-points`
  - [ ] `GET /api/customers/{id}/sales-history`
- [ ] Implement loyalty points logic

#### Frontend Tasks
- [ ] Create models: `customer.model.ts`
- [ ] Create service: `customer.service.ts`
- [ ] Create components:
  - [ ] `customer-quick-add.component.ts` (modal)
  - [ ] `customer-search.component.ts` (autocomplete)
  - [ ] `customer-list.component.ts`
  - [ ] `customer-form.component.ts`
  - [ ] `customer-details.component.ts`
- [ ] Implement quick-add in POS
- [ ] Show loyalty points balance

#### Testing Checklist
- [ ] Create customer
- [ ] Search customer by phone
- [ ] Add loyalty points
- [ ] View purchase history

---

### ⬜ Priority 5.2: POS Performance & Concurrency Layer (NEW)
**Status:** Not Started  
**Duration:** 4 days  
**Priority:** 🔴 Critical

#### Objective
Prepare the system for real POS load before building the main sales screen.

#### Backend Tasks
- [ ] Add Redis cache for barcode/product lookup
- [ ] Add Redis cache for effective price lookup
- [ ] Define stock concurrency strategy:
  - [ ] Optimistic concurrency token OR row version
  - [ ] Reservation-based approach for cart/hold sale if needed
- [ ] Add cache invalidation rules on stock or price changes
- [ ] Add POS-focused lightweight product lookup endpoint

#### Frontend Tasks
- [ ] Preload fast-moving products/categories
- [ ] Optimize cart with in-memory Angular Signals
- [ ] Reduce unnecessary API chatter during scanning
- [ ] Add graceful retry messaging for concurrency conflicts

#### Testing Checklist
- [ ] Same item scanned rapidly by multiple terminals
- [ ] Concurrency conflict handled without silent oversell
- [ ] Barcode lookup returns under target response time
- [ ] Cache refresh works after stock changes

### ⬜ Priority 5.3: POS Sales Module (Core Feature)
**Status:** Not Started  
**Duration:** 2 weeks  
**Priority:** 🔴 CRITICAL

#### Backend Tasks
- [ ] Insert Stock Ledger rows on sale
- [ ] Publish `SaleCompleted` event
- [ ] Create accounting transaction trigger via event, not direct hard coupling
- [ ] Add held/parked sale entity if needed
- [ ] Create repositories:
  - [ ] `ISaleRepository` and implementation
  - [ ] `ISaleItemRepository` and implementation
- [ ] Create `ISaleService` and implementation
- [ ] Implement stock deduction on sale
- [ ] Implement payment processing logic
- [ ] Create DTOs:
  - [ ] `SaleDto`
  - [ ] `CreateSaleDto`
  - [ ] `SaleItemDto`
  - [ ] `SaleSummaryDto`
  - [ ] `EndOfDayReportDto`
  - [ ] `RefundRequestDto`
- [ ] Create `SalesController`:
  - [ ] `POST /api/sales` (create sale transaction)
  - [ ] `GET /api/sales` (filter by date, outlet, cashier)
  - [ ] `GET /api/sales/{id}`
  - [ ] `POST /api/sales/{id}/void` (only within same day)
  - [ ] `POST /api/sales/{id}/refund`
  - [ ] `GET /api/sales/today/summary`
  - [ ] `GET /api/sales/end-of-day`
  - [ ] `GET /api/sales/{id}/receipt`
  - [ ] `POST /api/sales/{id}/email-receipt`
- [ ] Implement receipt generation (HTML/PDF)
- [ ] Handle multiple payment methods
- [ ] Calculate tax automatically
- [ ] Update customer loyalty points on sale
- [ ] Create accounting transactions on sale

#### Frontend Tasks
- [ ] Create models: `sale.model.ts`, `cart.model.ts`
- [ ] Create services:
  - [ ] `sale.service.ts`
  - [ ] `cart.service.ts` (signal-based state)
  - [ ] `receipt.service.ts`
- [ ] **Create POS Screen Component** (MOST IMPORTANT):
  - [ ] `pos-screen.component.ts`
  - [ ] Layout: 2-column (products search + cart)
  - [ ] **Left Panel:**
    - [ ] Barcode scanner input (autofocus)
    - [ ] Product search with autocomplete
    - [ ] Category filter buttons
    - [ ] Product grid/list view
    - [ ] Quick-add buttons for common items
  - [ ] **Right Panel (Cart):**
    - [ ] Cart items list
    - [ ] Quantity adjustment (+/-)
    - [ ] Remove item button
    - [ ] Subtotal display
    - [ ] Discount input (% or fixed)
    - [ ] Tax calculation display
    - [ ] Total amount (large, bold)
  - [ ] **Bottom Section:**
    - [ ] Customer selection/quick-add
    - [ ] Payment method selector (Cash, Card, Mobile)
    - [ ] Numeric keypad (for cash amounts)
    - [ ] Tendered amount input
    - [ ] Change calculation
    - [ ] "Complete Sale" button (big, green)
    - [ ] "Hold/Park" sale button
    - [ ] "Cancel" sale button
  - [ ] **Variant Selector:**
    - [ ] Modal/popover when product has variants
    - [ ] Show variant options (size, color)
    - [ ] Display stock availability per variant
  - [ ] **Touch Optimization:**
    - [ ] Large buttons (min 44x44px)
    - [ ] Swipe gestures for item removal
    - [ ] Keyboard shortcuts (F1-F12 for quick items)
- [ ] Create other components:
  - [ ] `sales-list.component.ts`
  - [ ] `sale-details.component.ts`
  - [ ] `void-sale.component.ts` (with reason)
  - [ ] `refund-sale.component.ts`
  - [ ] `receipt-preview.component.ts`
  - [ ] `end-of-day-report.component.ts`
  - [ ] `held-sales.component.ts` (parked transactions)
- [ ] Implement features:
  - [ ] Real-time cart calculation
  - [ ] Barcode scanning (USB scanner support)
  - [ ] Keyboard shortcuts
  - [ ] Print receipt (thermal printer)
  - [ ] Email receipt
  - [ ] Hold/retrieve sales
  - [ ] Offline mode support (optional)
  - [ ] Sound feedback on actions
  - [ ] Cash drawer open signal
- [ ] Create receipt template (HTML)

#### Testing Checklist
- [ ] Duplicate submit prevention works
- [ ] Sale creates stock ledger + accounting event
- [ ] Scan product barcode
- [ ] Add product to cart manually
- [ ] Select variant (size/color)
- [ ] Apply discount
- [ ] Select customer
- [ ] Process cash payment with change
- [ ] Process card payment
- [ ] Complete sale and verify stock deduction
- [ ] Print receipt
- [ ] Void sale
- [ ] Process refund
- [ ] Hold and retrieve sale
- [ ] Run end-of-day report

---

### ⬜ Priority 5.4: Offline POS Support (NEW)
**Status:** Not Started  
**Duration:** 4 days  
**Priority:** 🟠 High

#### Objective
Allow limited POS operation during temporary connectivity issues.

#### Frontend Tasks
- [ ] Store offline cart/sales queue in IndexedDB
- [ ] Add online/offline indicator
- [ ] Add sync queue status
- [ ] Prevent unsupported actions when offline

#### Backend / Sync Tasks
- [ ] Add sale sync endpoint for offline-created transactions
- [ ] Add conflict handling rules
- [ ] Add duplicate sync protection with client transaction ID

#### Testing Checklist
- [ ] Create sale offline
- [ ] Restore connection and sync successfully
- [ ] Duplicate sync prevented
- [ ] Out-of-stock conflict handled visibly

### ⬜ Priority 5.5: Return & Exchange Module (NEW)
**Status:** Not Started  
**Duration:** 1 week  
**Priority:** 🔴 Critical

#### Objective
Support apparel retail workflows such as size exchange and partial return.

#### Backend Tasks
- [ ] Create `SalesReturn` and related items entity
- [ ] Create exchange workflow logic
- [ ] APIs:
  - [ ] `POST /api/sales/{id}/return`
  - [ ] `POST /api/sales/{id}/exchange`
  - [ ] `GET /api/sales/{id}/return-history`
- [ ] Support:
  - [ ] Partial return
  - [ ] Full return
  - [ ] Size/color exchange
  - [ ] Refund to original / store credit
  - [ ] Optional restocking fee
- [ ] Insert Stock Ledger rows
- [ ] Publish return/exchange accounting events

#### Frontend Tasks
- [ ] Return UI from sale details
- [ ] Exchange flow with variant selector
- [ ] Refund summary preview
- [ ] Restocking fee entry if applicable

#### Testing Checklist
- [ ] Partial return updates stock and totals
- [ ] Exchange deducts new item and re-adds old item
- [ ] Return affects reports correctly
- [ ] Ledger reflects return/exchange correctly

## 🗺️ PHASE 6: Stock Movement (Week 12-13)

### ⬜ Priority 6.1: Stock Transfer Module
**Status:** Not Started  
**Duration:** 1 week  
**Priority:** 🟡 Medium

#### Backend Tasks
- [ ] Create repositories:
  - [ ] `IStockTransferRepository` and implementation
  - [ ] `IStockTransferItemRepository` and implementation
- [ ] Create `IStockTransferService` and implementation
- [ ] Implement dual-location stock update (deduct from source, add to destination)
- [ ] Create DTOs:
  - [ ] `StockTransferDto`
  - [ ] `CreateStockTransferDto`
  - [ ] `StockTransferItemDto`
  - [ ] `TransferApprovalDto`
- [ ] Create `StockTransfersController`:
  - [ ] `GET /api/stock-transfers` (filter by status, locations)
  - [ ] `GET /api/stock-transfers/{id}`
  - [ ] `POST /api/stock-transfers`
  - [ ] `PUT /api/stock-transfers/{id}`
  - [ ] `POST /api/stock-transfers/{id}/submit`
  - [ ] `POST /api/stock-transfers/{id}/approve`
  - [ ] `POST /api/stock-transfers/{id}/reject`
  - [ ] `POST /api/stock-transfers/{id}/send` (mark as in-transit)
  - [ ] `POST /api/stock-transfers/{id}/receive`
  - [ ] `POST /api/stock-transfers/{id}/cancel`
- [ ] Implement workflow: Pending → Approved → In-Transit → Received
- [ ] Update stock on receive confirmation

#### Frontend Tasks
- [ ] Create models: `stock-transfer.model.ts`
- [ ] Create service: `stock-transfer.service.ts`
- [ ] Create components:
  - [ ] `transfer-list.component.ts`
  - [ ] `transfer-create.component.ts`
  - [ ] `transfer-details.component.ts`
  - [ ] `transfer-approval.component.ts`
  - [ ] `transfer-receive.component.ts`
- [ ] Implement features:
  - [ ] Select from/to locations
  - [ ] Add items with quantities
  - [ ] Check stock availability
  - [ ] Approval workflow UI
  - [ ] Status tracking
  - [ ] Print transfer note

#### Testing Checklist
- [ ] Create transfer request
- [ ] Approve transfer
- [ ] Mark as in-transit
- [ ] Receive transfer and verify stock update

---

### ⬜ Priority 6.2: Stock Adjustment Module
**Status:** Not Started  
**Duration:** 3 days  
**Priority:** 🟡 Medium

#### Backend Tasks
- [ ] Create `IStockAdjustmentRepository` and implementation
- [ ] Create `IStockAdjustmentService` and implementation
- [ ] Create DTOs:
  - [ ] `StockAdjustmentDto`
  - [ ] `CreateStockAdjustmentDto`
- [ ] Create `StockAdjustmentsController`:
  - [ ] `GET /api/stock-adjustments`
  - [ ] `GET /api/stock-adjustments/{id}`
  - [ ] `POST /api/stock-adjustments`
  - [ ] `GET /api/stock-adjustments/history` (by product/location)
- [ ] Implement immediate stock update on adjustment
- [ ] Validate adjustment reasons
- [ ] Create accounting entries for adjustments

#### Frontend Tasks
- [ ] Create models: `stock-adjustment.model.ts`
- [ ] Create service: `stock-adjustment.service.ts`
- [ ] Create components:
  - [ ] `adjustment-create.component.ts`
  - [ ] `adjustment-list.component.ts`
  - [ ] `adjustment-history.component.ts`
- [ ] Implement features:
  - [ ] Select location and product
  - [ ] Input quantity change (+/-)
  - [ ] Select reason dropdown (Damage, Loss, Found, Expired, etc.)
  - [ ] Add notes
  - [ ] Confirmation dialog with impact

#### Testing Checklist
- [ ] Create positive adjustment
- [ ] Create negative adjustment
- [ ] Verify stock updated immediately
- [ ] View adjustment history

### ⬜ Priority 6.3: Multi-Outlet Sync Strategy (NEW)
**Status:** Not Started  
**Duration:** 3 days  
**Priority:** 🟠 High

#### Objective
Ensure stock and movement consistency across multiple outlets and warehouse nodes.

#### Tasks
- [ ] Define source-of-truth rules for outlet/warehouse inventory
- [ ] Publish stock change events from all movement modules
- [ ] Add eventual consistency design note
- [ ] Add retry/reconciliation background jobs
- [ ] Add mismatch detection report

#### Testing Checklist
- [ ] Transfer updates destination visibility correctly
- [ ] Delayed event processing reconciles successfully
- [ ] Stock mismatch report identifies broken cases

---

## 🗺️ PHASE 7: Accounting & Finance (Week 14-15)

### ⬜ Priority 7.1: Chart of Accounts Module
**Status:** Not Started  
**Duration:** 3 days  
**Priority:** 🟡 Medium

#### Backend Tasks
- [ ] Create `IAccountRepository` and implementation
- [ ] Create `IAccountService` and implementation
- [ ] Create DTOs: `AccountDto`, `CreateAccountDto`
- [ ] Create `AccountsController`:
  - [ ] `GET /api/accounts` (tree structure)
  - [ ] `GET /api/accounts/{id}`
  - [ ] `POST /api/accounts`
  - [ ] `PUT /api/accounts/{id}`
  - [ ] `DELETE /api/accounts/{id}`
  - [ ] `GET /api/accounts/{id}/balance`
- [ ] Setup default accounts on system init

#### Frontend Tasks
- [ ] Create models: `account.model.ts`
- [ ] Create service: `account.service.ts`
- [ ] Create components:
  - [ ] `account-list.component.ts`
  - [ ] `account-form.component.ts`
  - [ ] `account-tree.component.ts`

#### Testing Checklist
- [ ] Create account
- [ ] View account balance
- [ ] View account hierarchy

---

### ⬜ Priority 7.2: Transaction & Journal Entry Module
**Status:** Not Started  
**Duration:** 1 week  
**Priority:** 🟡 Medium

#### Backend Tasks
- [ ] Create `ITransactionRepository` and implementation
- [ ] Create `ITransactionService` and implementation
- [ ] Implement double-entry bookkeeping logic
- [ ] Create DTOs: `TransactionDto`, `JournalEntryDto`
- [ ] Create `TransactionsController`:
  - [ ] `GET /api/transactions` (filter by account, date)
  - [ ] `GET /api/transactions/{id}`
  - [ ] `POST /api/transactions/journal-entry`
  - [ ] `GET /api/transactions/ledger` (account ledger)
  - [ ] `GET /api/transactions/trial-balance`
- [ ] Auto-create transactions from:
  - [ ] Sales (debit Cash, credit Sales Revenue)
  - [ ] Purchases (debit Inventory, credit Accounts Payable)
  - [ ] Payments

#### Frontend Tasks
- [ ] Create models: `transaction.model.ts`
- [ ] Create service: `transaction.service.ts`
- [ ] Create components:
  - [ ] `transaction-list.component.ts`
  - [ ] `journal-entry-form.component.ts`
  - [ ] `ledger-view.component.ts`
  - [ ] `trial-balance.component.ts`

#### Testing Checklist
- [ ] Create manual journal entry
- [ ] Verify double-entry balance
- [ ] View account ledger
- [ ] Generate trial balance

---

### ⬜ Priority 7.3: Bills & Expenses Module
**Status:** Not Started  
**Duration:** 1 week  
**Priority:** 🟡 Medium

#### Backend Tasks
- [ ] Create repositories:
  - [ ] `IBillRepository` and implementation
  - [ ] `IExpenseRepository` and implementation
- [ ] Create services
- [ ] Create DTOs
- [ ] Create `BillsController`:
  - [ ] `GET /api/bills` (filter by status, supplier)
  - [ ] `GET /api/bills/{id}`
  - [ ] `POST /api/bills` (from GRN)
  - [ ] `PUT /api/bills/{id}`
  - [ ] `POST /api/bills/{id}/pay`
  - [ ] `GET /api/bills/unpaid`
  - [ ] `GET /api/bills/overdue`
- [ ] Create `ExpensesController`:
  - [ ] `GET /api/expenses`
  - [ ] `POST /api/expenses`
  - [ ] `PUT /api/expenses/{id}`
  - [ ] `DELETE /api/expenses/{id}`
  - [ ] `GET /api/expenses/categories`

#### Frontend Tasks
- [ ] Create components:
  - [ ] `bill-list.component.ts`
  - [ ] `bill-payment.component.ts`
  - [ ] `expense-list.component.ts`
  - [ ] `expense-form.component.ts`
  - [ ] `expense-category-manager.component.ts`

#### Testing Checklist
- [ ] Create bill from GRN
- [ ] Pay bill
- [ ] Create expense
- [ ] View overdue bills


### ⬜ Priority 7.4: Event-Based Accounting Integration (NEW)
**Status:** Not Started  
**Duration:** 3 days  
**Priority:** 🔴 Critical

#### Objective
Keep sales/procurement operational modules decoupled from accounting through asynchronous events.

#### Tasks
- [ ] Add Outbox table/process if not already present in this solution
- [ ] Publish events:
  - [ ] `SaleCompleted`
  - [ ] `GRNCompleted`
  - [ ] `SalesReturnCompleted`
  - [ ] `PaymentReceived`
  - [ ] `StockAdjustmentCompleted`
- [ ] Create accounting event consumer
- [ ] Map business events to journal entries
- [ ] Add idempotency handling for duplicate message delivery

#### Testing Checklist
- [ ] Sale creates journal entries asynchronously
- [ ] GRN creates inventory/payable entry
- [ ] Duplicate message does not create duplicate accounting entries
---

## 🗺️ PHASE 8: Reporting & Analytics (Week 16-17)

### ⬜ Priority 8.1: Sales Reports
**Status:** Not Started  
**Duration:** 1 week  
**Priority:** 🟠 High

#### Backend Tasks
- [ ] Create `IReportService` and implementation
- [ ] Create `ReportsController`:
  - [ ] `GET /api/reports/sales/summary?from={date}&to={date}&outletId={id}`
  - [ ] `GET /api/reports/sales/by-outlet`
  - [ ] `GET /api/reports/sales/by-cashier`
  - [ ] `GET /api/reports/sales/by-product`
  - [ ] `GET /api/reports/sales/top-products?limit=10`
  - [ ] `GET /api/reports/sales/hourly` (for today)
  - [ ] `GET /api/reports/sales/export?format=pdf|excel`
- [ ] Implement complex aggregation queries
- [ ] Add PDF/Excel export functionality

#### Frontend Tasks
- [ ] Create components:
  - [ ] `report-dashboard.component.ts`
  - [ ] `sales-report.component.ts`
  - [ ] `outlet-comparison.component.ts`
  - [ ] `top-products.component.ts`
  - [ ] `cashier-performance.component.ts`
- [ ] Implement charts:
  - [ ] Sales trend line chart
  - [ ] Sales by category pie chart
  - [ ] Top products bar chart
  - [ ] Hourly sales chart
- [ ] Add date range picker
- [ ] Implement export buttons

#### Testing Checklist
- [ ] Generate sales summary report
- [ ] View top selling products
- [ ] Compare outlets
- [ ] Export to PDF/Excel

---

### ⬜ Priority 8.2: Inventory Reports
**Status:** Not Started  
**Duration:** 3 days  
**Priority:** 🟠 High

#### Backend Tasks
- [ ] Add inventory report endpoints:
  - [ ] `GET /api/reports/inventory/stock-levels`
  - [ ] `GET /api/reports/inventory/valuation`
  - [ ] `GET /api/reports/inventory/movement`
  - [ ] `GET /api/reports/inventory/slow-moving?days=90`
  - [ ] `GET /api/reports/inventory/fast-moving?days=30`
  - [ ] `GET /api/reports/inventory/dead-stock`

#### Frontend Tasks
- [ ] Create components:
  - [ ] `inventory-report.component.ts`
  - [ ] `stock-valuation.component.ts`
  - [ ] `movement-report.component.ts`
  - [ ] `slow-moving-items.component.ts`

#### Testing Checklist
- [ ] Generate stock level report
- [ ] View inventory valuation
- [ ] Identify slow-moving items

---

### ⬜ Priority 8.3: Financial Reports
**Status:** Not Started  
**Duration:** 1 week  
**Priority:** 🟡 Medium

#### Backend Tasks
- [ ] Add financial report endpoints:
  - [ ] `GET /api/reports/financial/profit-loss?from={date}&to={date}`
  - [ ] `GET /api/reports/financial/balance-sheet?asOfDate={date}`
  - [ ] `GET /api/reports/financial/cash-flow?from={date}&to={date}`
  - [ ] `GET /api/reports/financial/expense-summary`

#### Frontend Tasks
- [ ] Create components:
  - [ ] `financial-report.component.ts`
  - [ ] `profit-loss.component.ts`
  - [ ] `balance-sheet.component.ts`
  - [ ] `cash-flow.component.ts`

#### Testing Checklist
- [ ] Generate P&L statement
- [ ] View balance sheet
- [ ] Generate cash flow report

---

### ⬜ Priority 8.4: Purchase & Transfer Reports
**Status:** Not Started  
**Duration:** 3 days  
**Priority:** 🟡 Medium

#### Backend Tasks
- [ ] Add report endpoints:
  - [ ] `GET /api/reports/purchases/summary`
  - [ ] `GET /api/reports/purchases/by-supplier`
  - [ ] `GET /api/reports/purchases/pending-grns`
  - [ ] `GET /api/reports/transfers/summary`
  - [ ] `GET /api/reports/transfers/pending`

#### Frontend Tasks
- [ ] Create components:
  - [ ] `purchase-report.component.ts`
  - [ ] `supplier-performance.component.ts`
  - [ ] `transfer-report.component.ts`

#### Testing Checklist
- [ ] View purchase summary
- [ ] Analyze supplier performance
- [ ] View pending transfers

### ⬜ Priority 8.5: Real-Time Operations Dashboard (NEW)
**Status:** Not Started  
**Duration:** 3 days  
**Priority:** 🟠 High

#### Features
- [ ] Today sales summary
- [ ] Sales by outlet (today)
- [ ] Top products today
- [ ] Active POS terminals / cashiers
- [ ] Low stock alerts
- [ ] Pending approvals / pending GRNs
- [ ] Stock mismatch warnings (future)

#### Backend Tasks
- [ ] Lightweight summary endpoints
- [ ] Aggregated cache strategy for dashboard widgets

#### Frontend Tasks
- [ ] Dashboard cards
- [ ] Mini charts
- [ ] Auto-refresh logic

---

## 🗺️ PHASE 9: Audit & System Settings (Week 18)

### ⬜ Priority 9.1: Audit Log Module
**Status:** Not Started  
**Duration:** 3 days  
**Priority:** 🟠 High

#### Backend Tasks
- [ ] Include request correlation / trace ID
- [ ] Add stock and pricing changes to audit scope
- [ ] Create `IAuditLogRepository` and implementation
- [ ] Implement audit logging interceptor/middleware
- [ ] Create `AuditLogsController`:
  - [ ] `GET /api/audit-logs` (with filters: user, module, date, action)
  - [ ] `GET /api/audit-logs/{id}`
  - [ ] `GET /api/audit-logs/export`
- [ ] Auto-log all create/update/delete operations
- [ ] Store request/response details

#### Frontend Tasks
- [ ] Create components:
  - [ ] `audit-log-viewer.component.ts`
  - [ ] `audit-log-filters.component.ts`
- [ ] Implement advanced search
- [ ] Add JSON details viewer

#### Testing Checklist
- [ ] View audit logs
- [ ] Filter by user
- [ ] Filter by date range
- [ ] Export audit logs

---

### ⬜ Priority 9.2: System Settings Module
**Status:** Not Started  
**Duration:** 1 week  
**Priority:** 🟡 Medium

#### Backend Tasks
- [ ] Create `ISettingsRepository` and implementation
- [ ] Create `ISettingsService` and implementation
- [ ] Create DTOs for various settings
- [ ] Create `SettingsController`:
  - [ ] `GET /api/settings`
  - [ ] `GET /api/settings/company`
  - [ ] `PUT /api/settings/company`
  - [ ] `GET /api/settings/tax`
  - [ ] `PUT /api/settings/tax`
  - [ ] `GET /api/settings/receipt-template`
  - [ ] `PUT /api/settings/receipt-template`
  - [ ] `GET /api/settings/email`
  - [ ] `PUT /api/settings/email`
  - [ ] `GET /api/settings/hardware`
  - [ ] `PUT /api/settings/hardware`

#### Frontend Tasks
- [ ] Create components:
  - [ ] `settings-page.component.ts` (tabs)
  - [ ] `company-settings.component.ts`
  - [ ] `tax-settings.component.ts`
  - [ ] `receipt-template-editor.component.ts`
  - [ ] `email-settings.component.ts`
  - [ ] `hardware-settings.component.ts`
  - [ ] `backup-restore.component.ts`
- [ ] Implement features:
  - [ ] Company profile (name, logo, address)
  - [ ] Tax configuration (rates, types)
  - [ ] Receipt template customization
  - [ ] Email SMTP settings
  - [ ] Printer configuration
  - [ ] Barcode scanner settings
  - [ ] Cash drawer settings
  - [ ] Hardware settings per outlet/device
  - [ ] Receipt layout versioning
  - [ ] Barcode scanner profiles

#### Testing Checklist
- [ ] Update company profile
- [ ] Configure tax rates
- [ ] Customize receipt template
- [ ] Test email settings
- [ ] Configure printer

---

## 🗺️ PHASE 10: Testing & Deployment (Week 19-20)

### ⬜ Priority 10.1: Testing
**Status:** Not Started  
**Duration:** 1.5 weeks  
**Priority:** 🔴 Critical

#### Backend Testing
- [ ] Setup xUnit/NUnit test project
- [ ] Write unit tests:
  - [ ] Service layer tests
  - [ ] Repository tests
  - [ ] Validation tests
- [ ] Write integration tests:
  - [ ] API endpoint tests
  - [ ] Database integration tests
- [ ] Test coverage > 70%
- [ ] Performance testing (load test POS endpoint)
- [ ] Security testing (penetration testing)

#### Frontend Testing
- [ ] Setup Jasmine/Jest
- [ ] Write component tests
- [ ] Write service tests
- [ ] E2E tests with Cypress/Playwright:
  - [ ] Login flow
  - [ ] Create product flow
  - [ ] POS complete sale flow
  - [ ] Create purchase order flow
- [ ] Accessibility testing (WCAG)
- [ ] Cross-browser testing

#### Testing Checklist
- [ ] All unit tests passing
- [ ] All integration tests passing
- [ ] E2E critical flows tested
- [ ] Performance benchmarks met
- [ ] Security vulnerabilities addressed
- [ ] Load testing for POS endpoints
- [ ] Concurrency testing for stock deduction
- [ ] Duplicate submit testing
- [ ] Offline sync conflict testing
- [ ] Security testing for permission bypass attempts

---

### ⬜ Priority 10.2: Deployment Setup
**Status:** Not Started  
**Duration:** 3 days  
**Priority:** 🔴 Critical

#### Backend Deployment
- [ ] Create `Dockerfile` for API
- [ ] Create `docker-compose.yml` (API + PostgreSQL + Redis)
- [ ] Setup CI/CD pipeline (GitHub Actions/Azure DevOps):
  - [ ] Build on commit
  - [ ] Run tests
  - [ ] Build Docker image
  - [ ] Push to registry
  - [ ] Deploy to staging
- [ ] Configure production `appsettings.Production.json`
- [ ] Setup environment variables
- [ ] Configure connection strings
- [ ] Setup SSL certificates
- [ ] Configure CORS for production
- [ ] Setup logging (Serilog to file/cloud)
- [ ] Configure health checks

#### Frontend Deployment
- [ ] Build production Angular app
- [ ] Create nginx configuration
- [ ] Create `Dockerfile` for frontend
- [ ] Setup CDN (optional)
- [ ] Configure environment files

#### Database Deployment
- [ ] Create production database
- [ ] Run migrations on production
- [ ] Setup database backups (automated)
- [ ] Configure connection pooling
- [ ] Setup database monitoring

#### Infrastructure
- [ ] Choose hosting (Azure, AWS, DigitalOcean)
- [ ] Setup monitoring (Application Insights, Datadog)
- [ ] Configure alerts (email, SMS)
- [ ] Setup log aggregation
- [ ] Configure firewall rules
- [ ] Setup backup strategy
- [ ] Create disaster recovery plan

#### Documentation
- [ ] Update README with deployment instructions
- [ ] Create API documentation (Swagger/ReDoc)
- [ ] Create user manual
- [ ] Create admin manual
- [ ] Create troubleshooting guide

#### Deployment Checklist
- [ ] Docker containers running
- [ ] Database migrations applied
- [ ] HTTPS configured
- [ ] Backups scheduled
- [ ] Monitoring active
- [ ] Documentation complete


### ⬜ Priority 10.3: Security Hardening (NEW)
**Status:** Not Started  
**Duration:** 2 days  
**Priority:** 🔴 Critical

#### Tasks
- [ ] Move secrets to secure storage
- [ ] Device/IP restrictions for POS terminals where applicable
- [ ] Tighten token lifetime and refresh flow
- [ ] Review CORS and origin restrictions
- [ ] Add rate limiting for auth and critical endpoints
- [ ] Mask sensitive data in logs

---

## 📊 Progress Tracking Template

### Current Sprint: _________
**Dates:** __________ to __________

#### In Progress:
- [ ] Task 1
- [ ] Task 2

#### Completed This Sprint:
- [x] Task 1
- [x] Task 2

#### Blocked:
- [ ] Task requiring external dependency

#### Next Sprint Planning:
- To be determined

---

## 🎯 Key Performance Indicators (KPIs)

### Development KPIs
- [ ] Code coverage > 70%
- [ ] Build time < 5 minutes
- [ ] Zero critical security vulnerabilities
- [ ] All features documented

### Performance KPIs
- [ ] API response time < 200ms (95th percentile)
- [ ] POS transaction completion < 3 seconds
- [ ] Page load time < 2 seconds
- [ ] Support 100+ concurrent users

### Business KPIs
- [ ] POS uptime > 99.5%
- [ ] Sale transaction success rate > 99%
- [ ] User satisfaction score > 4.5/5
- [ ] Average training time < 2 hours

---

## 🚀 Quick Start for Each Phase

### Starting a New Phase:
1. Review phase requirements
2. Create feature branch: `feature/phase-X-module-name`
3. Update status to 🟦 In Progress
4. Implement backend first
5. Test backend with Postman/Swagger
6. Implement frontend
7. Test integration
8. Create pull request
9. Code review
10. Merge to main
11. Update status to ✅ Complete

---

## 📝 Notes & Considerations

### Important Reminders:
- **Always test in development before production**
- **Backup database before migrations**
- **Use feature flags for gradual rollout**
- **Monitor performance after each deployment**
- **Keep documentation updated**

### Common Pitfalls to Avoid:
- Skipping validation on DTOs
- Not testing edge cases
- Ignoring error handling
- Poor database indexing
- Not implementing proper logging
- Hardcoding configuration values

### Best Practices:
- Follow SOLID principles
- Keep controllers thin
- Use dependency injection
- Implement proper exception handling
- Write meaningful commit messages
- Conduct regular code reviews
- Maintain consistent coding style

---

## 🔗 Related Documents

- [README.md](README.md) - Project overview
- [DATABASE_SCHEMA.md](DATABASE_SCHEMA.md) - Database documentation
- [QUICK_REFERENCE.md](QUICK_REFERENCE.md) - Developer commands
- [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md) - File organization
- [SETUP_COMPLETE.md](SETUP_COMPLETE.md) - Initial setup summary

---

**Last Updated:** 2026-01-30  
**Document Version:** 2.1  
**Status:** Phase 1 Complete, Phase 2.1-2.2 Complete, Ready for Phase 2.3 or Phase 3

---

## ✅ Sign-Off

- [x] Roadmap reviewed by team
- [x] Priorities confirmed
- [x] Resources allocated
- [x] Timeline approved
- [x] Phase 1.1 Complete ✅ (Authentication & Authorization)
- [x] Phase 1.2 Complete ✅ (User & Role Management)
- [x] Phase 1.3 Complete ✅ (CRUD Forms & Details)
- [x] Phase 1.4 Complete ✅ (Navigation System)
- [x] Phase 2.1 Complete ✅ (Outlets & Warehouses)
- [x] Phase 2.2 Complete ✅ (Category Management)

---

**Next Action:** 
- **Immediate:** Finish Phase 4.1 frontend and complete Phase 4.2 GRN
- **Then:** Implement Phase 3.5 Pricing Engine + Phase 3.6 Stock Ledger
- **Then:** Build Phase 5.2 POS Performance Layer
- **Then:** Start Phase 5.3 POS Sales Module
