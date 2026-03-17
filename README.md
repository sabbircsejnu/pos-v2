# Retail POS System - ASP.NET Core + PostgreSQL

A comprehensive, production-ready Point of Sale system with multi-outlet, multi-warehouse support, and full inventory management capabilities.

## 🏗️ Project Structure

```
pos-v2/
├── RetailPOS.slnx                 # Solution file
├── src/
│   ├── RetailPOS.Core/            # Domain entities/models
│   │   └── Entities/              # 24 entity classes
│   ├── RetailPOS.Infrastructure/  # Data access layer
│   │   └── Data/
│   │       ├── RetailPOSDbContext.cs
│   │       ├── RetailPOSDbContextFactory.cs
│   │       └── Migrations/        # EF Core migrations
│   └── RetailPOS.API/             # Web API project
│       ├── Program.cs
│       └── appsettings.json
└── README.md
```

## 📦 Modules & Entities

### 1. User & Role Management
- **Role**: Role definitions with JSONB permissions
- **User**: User accounts with role-based access

### 2. Location Management
- **Outlet**: Store/branch management
- **Warehouse**: Warehouse locations

### 3. Product & Inventory Management
- **Category**: Hierarchical product categories
- **Product**: Base product information
- **ProductVariant**: Multi-variant support (size, color, etc.)
- **Inventory**: Stock tracking per location

### 4. Supplier & Purchase Management
- **Supplier**: Supplier information
- **PurchaseOrder**: Purchase orders with approval workflow
- **PurchaseOrderItem**: Line items for purchase orders
- **Grn** (Goods Received Note): Receipt confirmation
- **GrnItem**: Received items with quantities

### 5. Customer & Sales
- **Customer**: Customer records with loyalty points
- **Sale**: POS transactions
- **SaleItem**: Sale line items

### 6. Stock Movement
- **StockTransfer**: Transfer between locations
- **StockTransferItem**: Transfer line items
- **StockAdjustment**: Manual stock corrections

### 7. Accounting & Finance
- **Account**: Chart of accounts
- **Transaction**: Financial transactions
- **Bill**: Supplier bills/payables
- **Expense**: Operational expenses

### 8. Audit & Compliance
- **AuditLog**: Complete audit trail

## 🛠️ Technology Stack

- **Backend**: ASP.NET Core 10 (.NET 10)
- **ORM**: Entity Framework Core 9.0
- **Database**: PostgreSQL 16+
- **Provider**: Npgsql.EntityFrameworkCore.PostgreSQL 9.0.2

## 🚀 Getting Started

### Prerequisites

1. **.NET 10 SDK** - [Download here](https://dotnet.microsoft.com/download/dotnet/10.0)
2. **PostgreSQL 16+** - [Download here](https://www.postgresql.org/download/)
3. **VS Code or Visual Studio 2022**

### Database Setup

1. **Install PostgreSQL** and create a database:
```sql
CREATE DATABASE retailpos_db;
CREATE USER postgres WITH PASSWORD 'postgres';
GRANT ALL PRIVILEGES ON DATABASE retailpos_db TO postgres;
```

2. **Update Connection String** (if needed):
Edit `src/RetailPOS.API/appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=retailpos_db;Username=postgres;Password=postgres;Port=5432"
  }
}
```

### Run Migrations

Navigate to the Infrastructure project and apply migrations:

```bash
cd c:\ess\Temp\pos-v2\src\RetailPOS.Infrastructure

# Apply migration to database
dotnet ef database update --startup-project ../RetailPOS.API/RetailPOS.API.csproj
```

### Build & Run

```bash
cd c:\ess\Temp\pos-v2

# Build entire solution
dotnet build

# Run the API
cd src/RetailPOS.API
dotnet run
```

The API will start at `https://localhost:5001` (or `http://localhost:5000`)

## 📝 Database Schema Highlights

### Key Features:
- ✅ **Snake_case naming** for PostgreSQL best practices
- ✅ **JSONB columns** for flexible data (permissions, attributes, details)
- ✅ **Proper indexes** on frequently queried columns
- ✅ **Referential integrity** with foreign key constraints
- ✅ **Cascade/Restrict** delete behaviors configured
- ✅ **Unique constraints** on SKUs, emails, barcodes
- ✅ **Decimal precision** for financial fields (10,2)
- ✅ **Timestamps** with timezone support

### Sample Tables Created:
```
roles, users, outlets, warehouses, categories, products, 
product_variants, inventories, suppliers, purchase_orders, 
purchase_order_items, grns, grn_items, customers, sales, 
sale_items, stock_transfers, stock_transfer_items, 
stock_adjustments, accounts, transactions, bills, expenses, 
audit_logs
```

## 🔧 EF Core Commands

### Create a New Migration
```bash
cd src/RetailPOS.Infrastructure
dotnet ef migrations add MigrationName --startup-project ../RetailPOS.API/RetailPOS.API.csproj
```

### Update Database
```bash
dotnet ef database update --startup-project ../RetailPOS.API/RetailPOS.API.csproj
```

### Remove Last Migration
```bash
dotnet ef migrations remove --startup-project ../RetailPOS.API/RetailPOS.API.csproj
```

### Generate SQL Script
```bash
dotnet ef migrations script --startup-project ../RetailPOS.API/RetailPOS.API.csproj --output migration.sql
```

## 📊 Entity Relationships

```
Role 1──────* User
Outlet 1────* User
Outlet 1────* Sale
Warehouse 1─* PurchaseOrder
Supplier 1──* PurchaseOrder
PurchaseOrder 1──* PurchaseOrderItem
PurchaseOrder 1──* Grn
Product 1───* ProductVariant
ProductVariant 1──* Inventory
ProductVariant 1──* SaleItem
Category 1──* Product (self-referencing for hierarchy)
```

## 🎯 Next Steps (Future Development)

This project currently includes **Models and Migrations only**. Here's what to implement next:

### Phase 1: Core API
- [ ] Implement Repository pattern
- [ ] Create DTOs (Data Transfer Objects)
- [ ] Add AutoMapper configurations
- [ ] Build API Controllers for each module
- [ ] Implement authentication (JWT)
- [ ] Add authorization policies

### Phase 2: Business Logic
- [ ] Service layer with business rules
- [ ] Stock movement logic (auto-update on sales/transfers)
- [ ] Accounting transaction automation
- [ ] Validation with FluentValidation
- [ ] Error handling middleware

### Phase 3: Advanced Features
- [ ] Reporting endpoints with filtering
- [ ] Background jobs (Hangfire for stock alerts)
- [ ] Caching (Redis)
- [ ] Logging (Serilog)
- [ ] API documentation (Swagger/OpenAPI)

### Phase 4: Frontend (Angular 18+)
- [ ] Authentication & Authorization UI
- [ ] POS screen (touch-optimized)
- [ ] Inventory management dashboard
- [ ] Purchase order management
- [ ] Reports and analytics
- [ ] Multi-outlet selector

## 🔐 Security Considerations

- Passwords will be hashed (BCrypt/PBKDF2)
- JWT authentication for API
- Role-based permissions (JSONB field in roles table)
- Audit logging for all actions
- SQL injection prevention (EF Core parameterized queries)

## 📦 NuGet Packages Used

```xml
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="9.0.2" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.0" />
```

## 🤝 Contributing

This is a foundational setup. Future contributors should:
1. Follow the existing naming conventions
2. Update migrations when changing entities
3. Keep DbContext configurations organized
4. Document all major features

## 📄 License

[Specify your license here]

## 👨‍💻 Author

Created as a comprehensive POS system foundation for retail businesses.

---

**Status**: ✅ Models & Migrations Complete | 🚧 API & Business Logic Pending
