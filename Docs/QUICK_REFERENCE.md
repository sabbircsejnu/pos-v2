# Quick Reference Guide

Updated: 2026-06-07

This guide is for day-to-day development in the current codebase state.

## Current Stack

- Backend: ASP.NET Core (.NET 10)
- Data: EF Core 9 + PostgreSQL (Npgsql)
- Auth: JWT Bearer + hashed refresh-token rotation
- API docs: Swagger (Swashbuckle)
- Frontend: Angular 21 + Tailwind CSS

## Repository Layout

```text
pos-v2/
├── src/
│   ├── RetailPOS.Core/
│   ├── RetailPOS.Infrastructure/
│   └── RetailPOS.API/
├── retailpos-frontend/
├── tests/RetailPOS.Tests/
├── Docs/
└── database/
```

## Daily Commands (From Repository Root)

### Build backend

```powershell
dotnet build .\RetailPOS.slnx
```

### Run API

```powershell
dotnet run --project .\src\RetailPOS.API\
```

### Apply EF migrations

```powershell
dotnet ef database update --project .\src\RetailPOS.Infrastructure\ --startup-project .\src\RetailPOS.API\
```

### Add EF migration

```powershell
dotnet ef migrations add MigrationName --project .\src\RetailPOS.Infrastructure\ --startup-project .\src\RetailPOS.API\
```

### Generate EF SQL script

```powershell
dotnet ef migrations script --project .\src\RetailPOS.Infrastructure\ --startup-project .\src\RetailPOS.API\ --output .\migration.sql
```

### Run frontend

```powershell
Set-Location .\retailpos-frontend
npm install
npm run start
```

### Build frontend

```powershell
Set-Location .\retailpos-frontend
npm run build
```

### Run tests

```powershell
dotnet test .\tests\RetailPOS.Tests\RetailPOS.Tests.csproj
```

## Useful API Endpoints (Implemented)

Auth and identity:

- POST /api/auth/login
- POST /api/auth/refresh-token
- POST /api/auth/logout
- POST /api/auth/complete-invitation
- POST /api/auth/switch-role
- POST /api/auth/return-owner
- GET /api/auth/me

Core user and role management:

- GET /api/users
- POST /api/users
- PUT /api/users/{id}
- DELETE /api/users/{id} (soft delete)
- GET /api/roles

SaaS and onboarding:

- POST /api/businesses (Super Admin)
- GET /api/businesses
- PATCH /api/businesses/{businessId}/subscription
- PATCH /api/businesses/{businessId}/features

Inventory and stock movement:

- GET /api/inventory
- POST /api/inventory/search
- GET /api/stock-transfers
- POST /api/stock-transfers
- GET /api/stock-adjustments
- POST /api/stock-adjustments

## Authorization Notes

- API policies are permission-based and registered from PermissionCatalog.
- Refresh-token lifecycle is implemented:
  - token hashes stored server-side
  - rotation on refresh
  - revocation on logout
- Current known gap: permission catalog drift for some policy names (documented in security and user-management docs).

## Important Documentation

- Overview: Docs/00_Overview/README.md
- Architecture: Docs/01_Architecture/PROJECT_STRUCTURE.md
- Security checklist: Docs/02_Security/SECURITY_CHECKLIST.md
- User management index: Docs/03_User_Management/USER_MANAGEMENT_INDEX.md
- User management gaps: Docs/03_User_Management/USER_MANAGEMENT_GAP_ANALYSIS.md
- Database schema: Docs/DATABASE_SCHEMA.md

## Common Troubleshooting

### dotnet ef command fails

- Ensure .NET SDK 10 is installed.
- Run from repository root and include both --project and --startup-project.

### API cannot connect to database

- Verify connection string in src/RetailPOS.API/appsettings.json.
- Ensure PostgreSQL is running and port/user/password are valid.

### 401/403 on API requests

- Confirm JWT is present and unexpired.
- Confirm role includes required permission claim.
- Confirm tenant/location scope rules are satisfied for requested resources.

### Frontend build issues

- Use Node and npm versions compatible with Angular 21.
- Run npm install inside retailpos-frontend before build/start.

## Status Snapshot

- Implemented:
  - Layered backend with controllers/services/repositories
  - JWT auth + refresh token rotation
  - Multi-business onboarding and feature flags
  - Inventory, purchasing, sales, finance, and reporting modules
- Partially Implemented:
  - Full tenant/location enforcement consistency across all modules
  - Canonical permission/role normalization across all sources
- Planned:
  - Remaining hardening and consistency items tracked in Docs/02_Security and Docs/03_User_Management
