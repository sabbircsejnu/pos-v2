# Project Structure Overview

Updated: 2026-06-07

## Repository Snapshot

The repository has evolved from a model-only baseline into a multi-layer backend + frontend + testing + documentation workspace.

## Current Top-Level Structure

```text
pos-v2/
├── .github/
├── database/
├── Docs/
├── retailpos-frontend/
├── src/
├── tests/
├── docker-compose.yml
└── RetailPOS.slnx
```

## Backend Solution Structure

```text
src/
├── RetailPOS.Core/
│   ├── RetailPOS.Core.csproj
│   ├── Entities/
│   └── Audit/
├── RetailPOS.Infrastructure/
│   ├── RetailPOS.Infrastructure.csproj
│   ├── Data/
│   │   ├── RetailPOSDbContext.cs
│   │   ├── RetailPOSDbContextFactory.cs
│   │   └── Migrations/
│   ├── Repositories/
│   └── Audit/
└── RetailPOS.API/
    ├── RetailPOS.API.csproj
    ├── Program.cs
    ├── appsettings.json
    ├── Controllers/
    ├── DTOs/
    ├── Services/
    ├── Middleware/
    ├── Authorization/
    ├── Models/
    ├── Exceptions/
    ├── Settings/
    └── wwwroot/
```

## Frontend Structure

```text
retailpos-frontend/
├── package.json
├── angular.json
├── tailwind.config.js
├── public/
└── src/
    ├── main.ts
    ├── styles.css
    ├── environments/
    └── app/
```

## Test Structure

```text
tests/
└── RetailPOS.Tests/
    ├── RetailPOS.Tests.csproj
    ├── Accounts/
    ├── Audit/
    ├── Infrastructure/
    └── Products/
```

## Documentation Structure

```text
Docs/
├── 00_Overview/
├── 01_Architecture/
├── 02_Security/
├── 03_User_Management/
├── 04_Business_Management/
├── 05_Product_Catalogue/
├── 06_Inventory/
├── 07_Purchase_And_Supplier/
├── 08_Sales_And_POS/
├── 09_Customer/
├── 10_Accounts_And_Finance/
├── 11_Reports_And_Dashboard/
├── 12_SaaS_Platform/
├── 13_Notifications/
├── 14_Integrations/
├── 15_API_Documentation/
├── 16_Development/
├── 17_Testing/
├── 18_DevOps/
├── 19_Operations/
├── DATABASE_SCHEMA.md
├── DATABASE_SEEDER_GUIDE.md
└── QUICK_REFERENCE.md
```

## Module Breakdown by Current File Count

| Layer | Current Count | Notes |
|------|---------------|-------|
| Core entity files | 41 top-level + 4 audit files | Domain model classes and audit entities |
| Infrastructure repositories | 48 files | Repository implementations/interfaces |
| API controllers | 34 files | REST endpoints across modules |
| API services | 77 files | Business logic and orchestration |
| EF migrations | 42 C# files | Ongoing schema evolution |
| Tests (source files) | 9 files | Focused integration and module tests |

## Database Model Footprint

From RetailPOSDbContext:

- 43 DbSet mappings are currently configured.
- The model includes tenancy, auth/session, catalog, inventory, purchasing, sales, accounting, pricing, and audit v2 domains.

## Dependency Graph

```text
RetailPOS.API
    ├── RetailPOS.Infrastructure
    │       └── RetailPOS.Core
    └── RetailPOS.Core

RetailPOS.Tests
    └── RetailPOS.API (integration-host style test setup)

retailpos-frontend
    └── RetailPOS.API (HTTP API consumer)
```

## Current Package Dependencies

### RetailPOS.Core

- No direct external package dependencies.

### RetailPOS.Infrastructure

```xml
<PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.2" />
```

### RetailPOS.API

```xml
<PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.2" />
<PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="10.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.2" />
<PackageReference Include="SixLabors.ImageSharp" Version="3.1.10" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="7.2.0" />
```

### retailpos-frontend

- Angular 21.1.x
- TypeScript 5.9.x
- Tailwind CSS 3.4.x
- Vitest 4.x

## Architecture Status

### Implemented

- Layered backend architecture (Core, Infrastructure, API)
- Large module surface with controllers, services, repositories
- JWT authentication with refresh-token lifecycle
- Permission-based authorization
- Multi-business SaaS foundations (business, features, subscription metadata)
- Inventory, purchasing, sales, accounting, reporting, pricing modules
- Angular frontend application
- Automated test project with focused integration scenarios

### Partially Implemented

- Full consistency of tenant and location scoping across every module
- Permission catalog and role-template normalization across all sources
- Comprehensive module-level test coverage

### Planned

- Additional hardening and consistency work described in security/user-management docs
- Expanded SaaS platform and operations implementation details in dedicated docs

## Configuration and Runtime Notes

- Target framework: net10.0 across backend projects.
- ORM/provider: EF Core 9 + Npgsql 9.
- API uses Swagger/OpenAPI via Swashbuckle.
- Redis distributed cache can be enabled through connection string; in-memory distributed cache is used as fallback.

## Development Workflow (Current)

### Backend

```powershell
dotnet ef database update --project .\src\RetailPOS.Infrastructure\ --startup-project .\src\RetailPOS.API\
dotnet build .\RetailPOS.slnx
dotnet run --project .\src\RetailPOS.API\
```

### Frontend

```powershell
Set-Location .\retailpos-frontend
npm install
npm run start
```

## Notes

- This document intentionally reflects the current implementation footprint.
- Planned features are listed as planned and are not represented as already delivered.
