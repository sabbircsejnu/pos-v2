# Project Structure Overview

## Complete File Tree

```
pos-v2/
├── README.md                              # Main documentation
├── DATABASE_SCHEMA.md                     # Detailed database documentation
├── QUICK_REFERENCE.md                     # Developer quick reference
├── PROJECT_STRUCTURE.md                   # This file
├── RetailPOS.slnx                         # Solution file (.NET 10 format)
│
├── src/
│   ├── RetailPOS.Core/                    # Domain Layer (Entities)
│   │   ├── RetailPOS.Core.csproj
│   │   └── Entities/
│   │       ├── Account.cs                 # Chart of accounts
│   │       ├── AuditLog.cs                # Audit trail
│   │       ├── Bill.cs                    # Supplier bills
│   │       ├── Category.cs                # Product categories (hierarchical)
│   │       ├── Customer.cs                # Customer records
│   │       ├── Expense.cs                 # Business expenses
│   │       ├── Grn.cs                     # Goods Received Note (header)
│   │       ├── GrnItem.cs                 # GRN line items
│   │       ├── Inventory.cs               # Stock per location
│   │       ├── Outlet.cs                  # Store/branch locations
│   │       ├── Product.cs                 # Base products
│   │       ├── ProductVariant.cs          # Product variants (SKU level)
│   │       ├── PurchaseOrder.cs           # Purchase order header
│   │       ├── PurchaseOrderItem.cs       # PO line items
│   │       ├── Role.cs                    # User roles with permissions
│   │       ├── Sale.cs                    # Sales transactions
│   │       ├── SaleItem.cs                # Sale line items
│   │       ├── StockAdjustment.cs         # Manual stock corrections
│   │       ├── StockTransfer.cs           # Transfer between locations
│   │       ├── StockTransferItem.cs       # Transfer line items
│   │       ├── Supplier.cs                # Supplier information
│   │       ├── Transaction.cs             # Financial transactions
│   │       ├── User.cs                    # System users
│   │       └── Warehouse.cs               # Warehouse locations
│   │
│   ├── RetailPOS.Infrastructure/          # Data Access Layer
│   │   ├── RetailPOS.Infrastructure.csproj
│   │   ├── Data/
│   │   │   ├── RetailPOSDbContext.cs      # Main DbContext with configurations
│   │   │   ├── RetailPOSDbContextFactory.cs # Design-time factory for migrations
│   │   │   └── Migrations/
│   │   │       ├── 20260128081847_InitialCreate.cs       # Initial migration (Up/Down)
│   │   │       ├── 20260128081847_InitialCreate.Designer.cs
│   │   │       └── RetailPOSDbContextModelSnapshot.cs    # Current model state
│   │   │
│   │   └── (Future: Repositories/, Services/, etc.)
│   │
│   └── RetailPOS.API/                     # Presentation Layer (Web API)
│       ├── RetailPOS.API.csproj
│       ├── Program.cs                     # Application entry point
│       ├── appsettings.json               # Configuration (with connection string)
│       ├── appsettings.Development.json
│       ├── Properties/
│       │   └── launchSettings.json
│       │
│       └── (Future: Controllers/, DTOs/, Middleware/, etc.)
│
└── (Future directories)
    ├── tests/                             # Unit & Integration tests
    │   ├── RetailPOS.Core.Tests/
    │   ├── RetailPOS.Infrastructure.Tests/
    │   └── RetailPOS.API.Tests/
    │
    └── docs/                              # Additional documentation
        ├── API.md
        ├── DEPLOYMENT.md
        └── ARCHITECTURE.md
```

---

## Module Breakdown by File Count

| Layer | Files | Purpose |
|-------|-------|---------|
| **Core** | 24 entities | Domain models, no dependencies |
| **Infrastructure** | 4 files + migrations | Data access, EF Core |
| **API** | 3 files | Web API, controllers (to be added) |
| **Documentation** | 4 markdown files | Setup & reference guides |

---

## Dependency Graph

```
RetailPOS.API
    ↓ depends on
    ├── RetailPOS.Infrastructure
    │       ↓ depends on
    │       └── RetailPOS.Core
    │               (No dependencies - pure domain)
    │
    └── RetailPOS.Core
```

---

## NuGet Package Dependencies

### RetailPOS.Core
- No external dependencies (pure POCO classes)

### RetailPOS.Infrastructure
```xml
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.2" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0" />
```

### RetailPOS.API
```xml
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.2" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0" />
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="9.0.0" />
```

---

## Code Statistics

### Entities (Core Layer)
- **24 Entity Classes**
- **~50-150 lines per entity** (average ~80)
- **Total: ~1,920 lines of domain code**

### DbContext Configuration
- **RetailPOSDbContext.cs: ~550 lines**
  - Table configurations
  - Relationship mappings
  - Index definitions
  - Snake_case converter

### Migrations
- **Initial Migration: ~950 lines**
  - 24 table creations
  - Foreign key constraints
  - Indexes
  - Check constraints

### Total Project
- **~3,500+ lines of code and configuration**

---

## Database Objects Created

| Type | Count | Examples |
|------|-------|----------|
| Tables | 24 | roles, users, products, sales, etc. |
| Foreign Keys | 35+ | User → Role, Sale → Outlet, etc. |
| Indexes | 10+ | Unique SKU, Email, Barcode, Timestamps |
| Check Constraints | 8+ | Status enums, Location types |

---

## Entity Categories

### Master Data (9 entities)
- Role, User, Outlet, Warehouse
- Category, Product, ProductVariant
- Supplier, Customer

### Transactional Data (12 entities)
- PurchaseOrder, PurchaseOrderItem, Grn, GrnItem
- Sale, SaleItem
- StockTransfer, StockTransferItem, StockAdjustment
- Transaction, Bill, Expense

### System Data (2 entities)
- Inventory (operational)
- AuditLog (compliance)

### Financial Data (1 entity)
- Account (chart of accounts)

---

## Key Features Implemented

### ✅ Completed
- [x] Complete entity model (24 classes)
- [x] DbContext with full configuration
- [x] PostgreSQL snake_case naming convention
- [x] JSONB support for flexible fields
- [x] Comprehensive foreign key relationships
- [x] Cascade/Restrict delete behaviors
- [x] Unique constraints on business keys
- [x] Indexes on frequently queried columns
- [x] Initial database migration
- [x] Connection string configuration
- [x] Design-time factory for migrations

### 🚧 Next Phase (Not Implemented Yet)
- [ ] Repository pattern
- [ ] Service layer
- [ ] API Controllers
- [ ] DTOs and AutoMapper
- [ ] Authentication & Authorization
- [ ] Input validation
- [ ] Error handling
- [ ] Logging
- [ ] Unit tests
- [ ] Integration tests

---

## Configuration Files

### appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=retailpos_db;Username=postgres;Password=postgres;Port=5432"
  }
}
```

### Program.cs Key Setup
```csharp
builder.Services.AddDbContext<RetailPOSDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
```

---

## Target Framework

- **.NET 10** (latest)
- **C# 13**
- **ASP.NET Core 10**
- **Entity Framework Core 9.0**

---

## Development Workflow

### 1. Model Changes
```
Edit Entity → Build → Create Migration → Update Database
```

### 2. Adding New Features
```
Create Entity → Add to DbContext → Configure Relationships → Generate Migration
```

### 3. Testing Changes
```
Update Database → Test with SQL → Verify in Application
```

---

## Naming Conventions Summary

### C# Code (PascalCase)
- Classes: `ProductVariant`, `PurchaseOrder`
- Properties: `TotalAmount`, `CreatedAt`
- Methods: `GetProductByIdAsync()`

### Database (snake_case)
- Tables: `product_variants`, `purchase_orders`
- Columns: `total_amount`, `created_at`
- Foreign Keys: `fk_sales_outlets_outlet_id`
- Indexes: `idx_sales_date`

### Automatic Conversion
All naming conversion is handled automatically by the `ToSnakeCase()` method in DbContext.

---

## File Size Summary

| File | Lines | Purpose |
|------|-------|---------|
| RetailPOSDbContext.cs | ~550 | Configuration |
| Each Entity | ~50-150 | Domain model |
| Initial Migration | ~950 | Database creation |
| README.md | ~250 | Main documentation |
| DATABASE_SCHEMA.md | ~500 | Schema details |
| QUICK_REFERENCE.md | ~300 | Developer guide |

---

## License & Credits

**Project**: Retail POS System  
**Architecture**: Clean Architecture / Onion Architecture  
**Pattern**: Repository + Service Layer (planned)  
**Database**: PostgreSQL with JSONB support  
**Framework**: ASP.NET Core Web API

---

**Document Version**: 1.0  
**Last Updated**: 2026-01-28  
**Status**: Phase 1 Complete (Models & Migrations)
