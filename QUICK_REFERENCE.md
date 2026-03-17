# Quick Reference Guide

## Project Commands

### Build & Run
```bash
# Build entire solution
dotnet build

# Run API
cd src/RetailPOS.API
dotnet run

# Run with watch (auto-reload)
dotnet watch run
```

### Database Commands
```bash
# Apply all pending migrations
cd src/RetailPOS.Infrastructure
dotnet ef database update --startup-project ../RetailPOS.API/RetailPOS.API.csproj

# Create new migration
dotnet ef migrations add MigrationName --startup-project ../RetailPOS.API/RetailPOS.API.csproj

# Rollback to specific migration
dotnet ef database update MigrationName --startup-project ../RetailPOS.API/RetailPOS.API.csproj

# Generate SQL script
dotnet ef migrations script --startup-project ../RetailPOS.API/RetailPOS.API.csproj --output migration.sql

# Drop database
dotnet ef database drop --startup-project ../RetailPOS.API/RetailPOS.API.csproj
```

### PostgreSQL Commands
```bash
# Connect to database
psql -U postgres -d retailpos_db

# List tables
\dt

# Describe table
\d table_name

# View data
SELECT * FROM products LIMIT 10;

# Exit
\q
```

---

## Entity Quick Reference

### Core Entities with Navigation Properties

```csharp
// User with relationships
var user = await _context.Users
    .Include(u => u.Role)
    .Include(u => u.Outlet)
    .FirstOrDefaultAsync(u => u.Id == userId);

// Product with variants and inventory
var product = await _context.Products
    .Include(p => p.Category)
    .Include(p => p.Variants)
        .ThenInclude(v => v.Inventories)
    .FirstOrDefaultAsync(p => p.Id == productId);

// Sale with all details
var sale = await _context.Sales
    .Include(s => s.Outlet)
    .Include(s => s.Customer)
    .Include(s => s.Cashier)
    .Include(s => s.Items)
        .ThenInclude(i => i.Variant)
            .ThenInclude(v => v.Product)
    .FirstOrDefaultAsync(s => s.Id == saleId);

// Purchase Order with items and GRNs
var po = await _context.PurchaseOrders
    .Include(po => po.Supplier)
    .Include(po => po.Warehouse)
    .Include(po => po.Items)
        .ThenInclude(i => i.Variant)
    .Include(po => po.Grns)
        .ThenInclude(g => g.Items)
    .FirstOrDefaultAsync(po => po.Id == poId);
```

---

## Connection Strings

### Development
```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Database=retailpos_db;Username=postgres;Password=postgres;Port=5432"
}
```

### Production (Example with SSL)
```json
"ConnectionStrings": {
  "DefaultConnection": "Host=prod-server;Database=retailpos_db;Username=app_user;Password=<strong-password>;Port=5432;SSL Mode=Require;Trust Server Certificate=true"
}
```

### Azure PostgreSQL
```json
"ConnectionStrings": {
  "DefaultConnection": "Host=myserver.postgres.database.azure.com;Database=retailpos_db;Username=adminuser@myserver;Password=<password>;Port=5432;SSL Mode=Require"
}
```

---

## Common Patterns

### Repository Pattern (Future)
```csharp
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(long id);
    Task<IEnumerable<T>> GetAllAsync();
    Task<T> AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(long id);
}
```

### Unit of Work Pattern (Future)
```csharp
public interface IUnitOfWork : IDisposable
{
    IRepository<Product> Products { get; }
    IRepository<Sale> Sales { get; }
    // ... other repositories
    Task<int> SaveChangesAsync();
}
```

### Service Layer (Future)
```csharp
public interface IProductService
{
    Task<ProductDto> GetProductByIdAsync(long id);
    Task<IEnumerable<ProductDto>> GetAllProductsAsync();
    Task<ProductDto> CreateProductAsync(CreateProductDto dto);
    Task UpdateProductAsync(long id, UpdateProductDto dto);
    Task DeleteProductAsync(long id);
}
```

---

## Testing Checklist

### Database Tests
- [ ] Connection successful
- [ ] All migrations applied
- [ ] All tables created
- [ ] Foreign keys working
- [ ] Indexes created
- [ ] Check constraints working

### Entity Tests
```bash
# Test scripts (run in psql)

# 1. Create test role
INSERT INTO roles (name, permissions) 
VALUES ('Admin', '["view_all", "edit_all"]'::jsonb);

# 2. Create test user
INSERT INTO users (name, email, password_hash, role_id, is_active) 
VALUES ('John Doe', 'john@example.com', 'hashed_password', 1, true);

# 3. Create test category
INSERT INTO categories (name) 
VALUES ('Electronics');

# 4. Create test product
INSERT INTO products (name, category_id, base_price, cost_price) 
VALUES ('Laptop', 1, 999.99, 700.00);

# 5. Create test variant
INSERT INTO product_variants (product_id, sku, attributes, barcode) 
VALUES (1, 'LAP-001-BLK', '{"color": "Black", "ram": "16GB"}'::jsonb, '1234567890');

# 6. Verify cascade delete
DELETE FROM products WHERE id = 1;
SELECT COUNT(*) FROM product_variants WHERE product_id = 1; -- Should be 0
```

---

## Performance Tips

### Indexing Strategy
```sql
-- Already created by migration
idx_inventory_variant_location ON inventories(variant_id, location_id)
idx_sales_date ON sales(sale_date)
idx_sales_outlet ON sales(outlet_id)
idx_audit_timestamp ON audit_logs(timestamp)

-- Additional indexes to consider (future)
CREATE INDEX idx_products_barcode ON products(barcode) WHERE barcode IS NOT NULL;
CREATE INDEX idx_variants_sku ON product_variants(sku);
CREATE INDEX idx_sales_customer ON sales(customer_id);
```

### Query Optimization
```csharp
// Use AsNoTracking for read-only queries
var products = await _context.Products
    .AsNoTracking()
    .ToListAsync();

// Project to DTOs to reduce data transfer
var productList = await _context.Products
    .Select(p => new ProductDto 
    { 
        Id = p.Id, 
        Name = p.Name, 
        Price = p.BasePrice 
    })
    .ToListAsync();

// Use pagination
var pagedProducts = await _context.Products
    .OrderBy(p => p.Name)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
```

---

## Security Checklist

### Database
- [ ] Strong passwords for PostgreSQL users
- [ ] Limit user permissions (don't use postgres user in production)
- [ ] Enable SSL for connections
- [ ] Regular backups configured
- [ ] Connection pooling configured

### Application
- [ ] Password hashing implemented (BCrypt)
- [ ] JWT token authentication
- [ ] Role-based authorization
- [ ] Input validation on all endpoints
- [ ] SQL injection prevention (EF Core handles this)
- [ ] CORS configured properly
- [ ] HTTPS enforced in production

---

## Environment Variables

### Development
```bash
# Windows
setx ConnectionStrings__DefaultConnection "Host=localhost;Database=retailpos_db;Username=postgres;Password=postgres"

# Linux/Mac
export ConnectionStrings__DefaultConnection="Host=localhost;Database=retailpos_db;Username=postgres;Password=postgres"
```

### appsettings.Development.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.EntityFrameworkCore": "Information"
    }
  }
}
```

---

## Troubleshooting

### Migration Issues
```bash
# Error: "Build failed"
# Solution: Build the solution first
dotnet build

# Error: "No migrations found"
# Solution: Check you're in Infrastructure project directory

# Error: "Connection refused"
# Solution: Check PostgreSQL is running
# Windows: Check Services
# Linux: sudo systemctl status postgresql
```

### Common EF Core Errors
```csharp
// Error: "Cannot insert duplicate key"
// Solution: Check unique constraints

// Error: "The instance of entity type cannot be tracked"
// Solution: Use AsNoTracking or detach entities

// Error: "A second operation started on this context"
// Solution: Use await on async operations
```

---

## Useful Extensions (VS Code)

```json
{
  "recommendations": [
    "ms-dotnettools.csharp",
    "ms-dotnettools.csdevkit",
    "ms-mssql.mssql",
    "ckolkman.vscode-postgres",
    "formulahendry.dotnet-test-explorer"
  ]
}
```

---

## Next Implementation Steps

### 1. Authentication (Priority: High)
```csharp
// Services to implement
- IAuthService
- ITokenService
- IPasswordHasher
```

### 2. Product Management (Priority: High)
```csharp
// Controllers to implement
- ProductsController
- ProductVariantsController
- CategoriesController
```

### 3. Inventory Management (Priority: High)
```csharp
// Services to implement
- IInventoryService
- IStockMovementService
```

### 4. Sales & POS (Priority: Critical)
```csharp
// Controllers to implement
- SalesController
- POSController (specialized for fast checkout)
```

### 5. Purchase Management (Priority: Medium)
```csharp
// Controllers to implement
- PurchaseOrdersController
- GRNController
- SuppliersController
```

---

**Quick Reference Version**: 1.0  
**Last Updated**: 2026-01-28
