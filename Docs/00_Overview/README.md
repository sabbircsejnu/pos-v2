# Retail POS v2 - Implementation Overview

Last verified: 2026-06-13

This document is the primary onboarding entry point for developers joining the project. It reflects current implementation in source code and current documentation status.

## Current State Snapshot

- Implemented
  - .NET 10 Web API with layered structure (Core, Infrastructure, API)
  - JWT authentication with refresh token rotation and revocation
  - Permission-policy authorization and role-based gates
  - Multi-business SaaS onboarding, subscription metadata, and feature flags
  - Product, inventory, purchasing, sales, accounting, pricing, and reporting APIs
  - Frontend application on Angular 21
- Partially Implemented
  - Tenant isolation consistency across all modules
  - Authorization permission catalog consistency across all policy sources
  - SaaS subscription lifecycle (reminders exist; billing automation not present)
- Planned
  - Further hardening and consistency passes called out in module docs under Docs/02_Security and Docs/03_User_Management

## 1) Project Structure Accuracy

Status: Implemented and corrected

Implemented
- Monorepo structure with backend, frontend, docs, tests, and database scripts:
  - RetailPOS.slnx
  - src/RetailPOS.Core
  - src/RetailPOS.Infrastructure
  - src/RetailPOS.API
  - retailpos-frontend
  - tests/RetailPOS.Tests
  - Docs
  - database

Outdated in previous version
- Previous structure omitted frontend, tests, docker-compose, and most docs modules.

Missing in previous version
- API implementation footprint: 34 controllers, 77 service files, 48 repository files.

Incorrect in previous version
- Described the repository as mostly model/migration-only.

## 2) Entity Count Accuracy

Status: Implemented and corrected

Implemented
- Current EF model registers 43 DbSet-backed domain/audit tables in RetailPOSDbContext.
- Core entity folder has 41 top-level model files plus 4 audit files under Entities/Audit.

Outdated in previous version
- Previous README said 24 entity classes.

Missing in previous version
- Newly present areas were not documented: business tenancy entities, invitation/refresh-token entities, pricing entities, held sale, split payments, stock ledger, and audit v2 entities.

Incorrect in previous version
- Entity inventory under-reported by a wide margin.

## 3) Module List Accuracy

Status: Partially Implemented (docs now corrected)

Implemented modules
- SaaS tenancy and onboarding
- Authentication and authorization
- User/role/outlet access management
- Catalog and variant system
- Inventory, stock transfers, stock adjustments, stock ledger
- Purchasing and GRN
- Sales, held sales, and sales reports
- Customers and suppliers
- Accounts, transactions, expenses, bills
- Pricing engine and outlet overrides
- Audit logging and audit events

Outdated in previous version
- Previous module list excluded SaaS onboarding/features, pricing engine, held sales, split payments, stock ledger, and audit v2.

Missing in previous version
- Did not describe reporting modules or user outlet access endpoints.

Incorrect in previous version
- Stated capabilities as if only foundational data model existed.

## 4) Technology Stack Accuracy

Status: Implemented and corrected

Implemented
- Backend: ASP.NET Core on .NET 10
- Data: EF Core 9 + PostgreSQL provider Npgsql.EntityFrameworkCore.PostgreSQL 9.0.2
- Security and auth: Microsoft.AspNetCore.Authentication.JwtBearer 10.0.2, BCrypt.Net-Next
- API docs: Swashbuckle.AspNetCore
- Optional cache layer: StackExchange Redis cache provider with in-memory fallback
- Frontend: Angular 21.1.x, TypeScript 5.9, Tailwind CSS 3.4

Outdated in previous version
- Frontend referenced as future Angular 18+ work.

Missing in previous version
- Did not mention existing frontend and active cache/auth/audit-related libraries.

Incorrect in previous version
- Implied Swagger and Redis as planned-only even though both are wired in API setup.

## 5) Authentication Implementation Status

Status: Implemented

Implemented
- JWT bearer auth configured in API startup
- Login, refresh, logout, and current-user endpoints
- Refresh token hashing, persistence, rotation, and revocation
- Invitation completion flow with must-reset-password gate
- Role switch and return-owner flows
- Auth endpoint rate limiting

Partially Implemented
- Access-token revocation deny-list is not implemented (refresh-token revocation is implemented).

Planned
- Additional hardening improvements documented in security docs.

## 6) Authorization Implementation Status

Status: Partially Implemented

Implemented
- Permission-based policies via Authorize(Policy = ...)
- Dynamic policy registration from PermissionCatalog
- Role-gated super-admin endpoints (business onboarding)

Partially Implemented
- Permission catalog drift exists between policy list and role permission validation list.
- Tenant/location scope enforcement is strong in some modules and weaker in others.

Planned
- Consolidate permission source-of-truth and add automated drift checks.

## 7) Multi-Tenant Architecture Coverage

Status: Partially Implemented

Implemented
- BusinessId tenancy model across core business-owned data
- TenantAccessService and RoleSwitchContext for effective scope
- SuperAdmin and BusinessOwner separation in key flows
- Outlet/warehouse authorization helper used in multiple operational controllers

Partially Implemented
- No global EF tenant query filter.
- Some modules rely on per-service/per-controller checks and are not fully uniform.

Planned
- Uniform tenant-scope enforcement patterns and broader integration test coverage.

## 8) User Management Coverage

Status: Partially Implemented

Implemented
- UsersController CRUD, activate/deactivate, change password
- RolesController and role permission model
- Invitation and password setup lifecycle
- User outlet access endpoint and role-switch support

Partially Implemented
- Warehouse assignment model is partly indirect.
- Generic forgot-password self-service flow is not present.

Planned
- Additional lifecycle and governance consistency work documented in Docs/03_User_Management.

## 9) Inventory Architecture Coverage

Status: Partially Implemented

Implemented
- Inventory APIs, low stock and valuation, search
- Stock transfer and stock adjustment workflows
- Stock ledger module and reporting endpoints
- Variant-aware product and inventory model

Partially Implemented
- Tenant and location scope enforcement depth is not fully uniform across all inventory-related endpoints.

Planned
- Continue convergence on shared outlet/warehouse/tenant enforcement across every inventory and purchasing flow.

## 10) SaaS Architecture Coverage

Status: Partially Implemented

Implemented
- Business onboarding workflow for Super Admin
- Default outlet/warehouse creation
- Seed feature entitlement rows per business
- Subscription metadata fields and admin update APIs
- Renewal reminder hosted service and reminder notification persistence

Partially Implemented
- Full subscription billing lifecycle automation is not present.
- Docs/12_SaaS_Platform is currently empty and needs detailed technical docs.

Planned
- Complete SaaS architecture documentation and operational automation roadmap.

## Corrected High-Level Module Map

- Implemented
  - SaaS tenancy and onboarding
  - Security: authentication, authorization, audit
  - User and role management
  - Location and access scoping
  - Catalog, variants, media, pricing
  - Inventory and stock movement
  - Purchasing and GRN
  - Sales and POS including held sales
  - Accounting and finance
  - Reporting
- Partially Implemented
  - Cross-module consistency for tenant and permission enforcement
- Planned
  - Expanded SaaS and operations documentation under Docs/12_SaaS_Platform and Docs/19_Operations

## Developer Quick Start

Prerequisites

1. .NET 10 SDK
2. PostgreSQL 16+
3. Node.js and npm (for frontend)

Backend setup

1. Configure connection strings and JWT settings for local development.
2. From repository root:

```powershell
dotnet ef database update --project .\src\RetailPOS.Infrastructure\ --startup-project .\src\RetailPOS.API\
dotnet build .\RetailPOS.slnx
dotnet run --project .\src\RetailPOS.API\
```

Frontend setup

```powershell
Set-Location .\retailpos-frontend
npm install
npm run start
```

## Key Documentation Links

- Security overview: ../02_Security/SECURITY_CHECKLIST.md
- Authentication details: ../02_Security/AUTHENTICATION.md
- Authorization details: ../02_Security/AUTHORIZATION.md
- Data isolation status: ../02_Security/DATA_ISOLATION.md
- User management index: ../03_User_Management/USER_MANAGEMENT_INDEX.md
- Architecture map: ../01_Architecture/PROJECT_STRUCTURE.md
- Frontend architecture baseline: ../01_Architecture/FRONTEND_ARCHITECTURE.md
- List state preservation standard: ../01_Architecture/LIST_STATE_PRESERVATION.md
- List page filter standard: ../01_Architecture/LIST_PAGE_FILTER_STANDARDS.md

## Notes for New Contributors

- This codebase is beyond initial scaffolding; avoid treating planned items as already delivered.
- Use the security and user-management docs as the source of truth for implementation gaps and hardening priorities.
- Keep documentation updates in sync with actual code changes, especially for permission catalogs and tenant-scope rules.
