# Database Seeder Documentation

## Overview

The `DbSeeder` class provides automatic database seeding functionality that runs on application startup. It ensures your database has the essential data needed to operate the POS system.

## Features

✅ **Automatic Execution** - Runs on every application startup  
✅ **Idempotent** - Safe to run multiple times (checks if data exists)  
✅ **Essential Data Only** - Seeds only critical roles and admin user by default  
✅ **Optional Data** - Provides methods for seeding sample outlets and categories  
✅ **Secure** - Uses BCrypt for password hashing

## What Gets Seeded

### 1. Default Roles (6 roles)

| Role | Description | Permissions |
|------|-------------|-------------|
| **Super Admin** | Full system access | `["*"]` (wildcard) |
| **Admin** | Store management | 40+ permissions |
| **Manager** | Operational management | 16 permissions |
| **Cashier** | Sales operations | 6 permissions |
| **Stock Manager** | Inventory management | 14 permissions |
| **User** | Read-only access | 4 permissions |

### 2. Super Admin User

**Credentials:**
- **Email:** `suparadmin@sabbir.com`
- **Password:** `Admin@123`
- **Role:** Super Admin
- **Status:** Active

⚠️ **IMPORTANT:** Change this password immediately after first login!

### 3. Optional: Sample Outlets (commented out by default)

- Main Store
- Branch Store

### 4. Optional: Sample Categories (commented out by default)

- Electronics
- Clothing
- Food & Beverages
- Home & Garden
- Sports & Outdoors

## How It Works

### Automatic Seeding on Startup

The seeder is configured in [Program.cs](../../RetailPOS.API/Program.cs) to run automatically:

```csharp
using (var scope = app.Services.CreateScope())
{
    var context = services.GetRequiredService<RetailPOSDbContext>();
    
    // Apply migrations
    await context.Database.MigrateAsync();
    
    // Seed essential data (roles + super admin)
    await DbSeeder.SeedAsync(context);
    
    // Optional: Seed sample data (uncomment if needed)
    // await DbSeeder.SeedOutletsAsync(context);
    // await DbSeeder.SeedCategoriesAsync(context);
}
```

### Idempotent Design

The seeder checks if data exists before inserting:

```csharp
if (await context.Roles.AnyAsync())
{
    return; // Database already seeded
}
```

This makes it safe to run multiple times without creating duplicates.

## Usage

### Basic Usage (Default)

Just start your application:

```bash
cd src/RetailPOS.API
dotnet run
```

The seeder will:
1. ✅ Apply any pending migrations
2. ✅ Check if roles exist
3. ✅ Create 6 default roles if none exist
4. ✅ Create Super Admin user
5. ✅ Log success message

### Enable Optional Seeding

To seed sample outlets and categories, edit [Program.cs](../../RetailPOS.API/Program.cs):

```csharp
// Uncomment these lines:
await DbSeeder.SeedOutletsAsync(context);
await DbSeeder.SeedCategoriesAsync(context);
```

### Manual Seeding

You can also call the seeder manually in code:

```csharp
using var scope = app.Services.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<RetailPOSDbContext>();

// Seed essential data
await DbSeeder.SeedAsync(context);

// Seed optional data
await DbSeeder.SeedOutletsAsync(context);
await DbSeeder.SeedCategoriesAsync(context);
```

## Permission System

### Permission Format

Permissions are stored as JSONB arrays in PostgreSQL:

```json
["users.view", "users.create", "users.edit", "users.delete"]
```

### Permission Naming Convention

Format: `{resource}.{action}`

**Resources:**
- users, roles, products, categories, inventory
- sales, purchases, customers, suppliers
- outlets, warehouses, reports, settings

**Actions:**
- view, create, edit, delete, export

### Wildcard Permission

Super Admin has wildcard permission:
```json
["*"]
```

This grants access to everything in the system.

### Checking Permissions

In your code:

```csharp
// Check if user has permission
bool canEditUsers = user.Role.Permissions.Contains("users.edit");

// Check for wildcard
bool isSuperAdmin = user.Role.Permissions.Contains("*");
```

In frontend (Angular):

```typescript
// Check permission
hasPermission(permission: string): boolean {
  const user = this.authService.currentUser();
  return user.role.permissions.includes('*') || 
         user.role.permissions.includes(permission);
}
```

## Logs

When the seeder runs, you'll see logs like:

```
info: Program[0]
      Checking if database needs seeding...
info: Program[0]
      Database seeding completed successfully
```

If data already exists:

```
info: Program[0]
      Checking if database needs seeding...
info: Program[0]
      Database seeding completed successfully
```

(Seeder exits early, no new data created)

## Database Reset (Development Only)

To re-seed from scratch:

### Option 1: Delete and Recreate Database

```bash
# Drop database (PowerShell)
docker exec -it retailpos-postgres psql -U postgres -c "DROP DATABASE IF EXISTS retailpos_db;"
docker exec -it retailpos-postgres psql -U postgres -c "CREATE DATABASE retailpos_db;"

# Run application (will auto-seed)
cd src/RetailPOS.API
dotnet run
```

### Option 2: Delete Specific Tables

```sql
-- Connect to database
docker exec -it retailpos-postgres psql -U postgres -d retailpos_db

-- Delete data only (keeps structure)
DELETE FROM users;
DELETE FROM roles;

-- Reset sequences
ALTER SEQUENCE users_id_seq RESTART WITH 1;
ALTER SEQUENCE roles_id_seq RESTART WITH 1;
```

Then restart the application to re-seed.

## Extending the Seeder

### Add New Roles

Edit [DbSeeder.cs](DbSeeder.cs) `GetDefaultRoles()` method:

```csharp
new Role
{
    Name = "Your New Role",
    Permissions = @"[
        ""resource.action"",
        ""another.permission""
    ]",
    CreatedAt = DateTime.UtcNow,
    UpdatedAt = DateTime.UtcNow
}
```

### Add More Users

Add a method like `CreateSuperAdminUser()`:

```csharp
private static User CreateManagerUser(long roleId, long outletId)
{
    return new User
    {
        Name = "Manager Name",
        Email = "manager@example.com",
        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123", 12),
        RoleId = roleId,
        OutletId = outletId,
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };
}
```

### Add Custom Seed Methods

Create new static methods:

```csharp
public static async Task SeedWarehousesAsync(RetailPOSDbContext context)
{
    if (await context.Warehouses.AnyAsync()) return;
    
    var warehouses = new List<Warehouse>
    {
        // Your data here
    };
    
    await context.Warehouses.AddRangeAsync(warehouses);
    await context.SaveChangesAsync();
}
```

Then call it in Program.cs:

```csharp
await DbSeeder.SeedWarehousesAsync(context);
```

## Security Considerations

### Password Security

✅ **BCrypt Hashing** - Uses work factor of 12  
✅ **Unique Salt** - BCrypt generates unique salt per password  
✅ **Slow by Design** - Protects against brute force attacks

### Default Credentials

⚠️ The default password `Admin@123` is **only for initial setup**

**Production Deployment Checklist:**
- [ ] Change Super Admin password immediately
- [ ] Enforce strong password policy
- [ ] Enable MFA (if implemented)
- [ ] Audit user accounts
- [ ] Review role permissions

### Permission Security

- Never store sensitive data in permissions
- Permissions are read frequently - keep them minimal
- Use wildcard `["*"]` sparingly
- Audit permission changes via audit logs

## Troubleshooting

### Seeder Not Running

Check logs for errors:

```bash
dotnet run --verbosity detailed
```

### Duplicate Key Errors

The seeder should prevent this, but if it happens:

1. Check if data already exists:
```sql
SELECT * FROM roles;
SELECT * FROM users;
```

2. If needed, delete existing data and restart app

### BCrypt Errors

Ensure BCrypt.Net-Next package is installed:

```bash
cd src/RetailPOS.Infrastructure
dotnet add package BCrypt.Net-Next
```

### Migration Errors

Apply migrations manually:

```bash
cd src/RetailPOS.API
dotnet ef database update
```

## Testing

### Test Seeder Locally

```csharp
[Fact]
public async Task Seeder_CreatesDefaultRoles()
{
    // Arrange
    var options = new DbContextOptionsBuilder<RetailPOSDbContext>()
        .UseInMemoryDatabase(databaseName: "TestDb")
        .Options;
        
    using var context = new RetailPOSDbContext(options);
    
    // Act
    await DbSeeder.SeedAsync(context);
    
    // Assert
    var roles = await context.Roles.ToListAsync();
    Assert.Equal(6, roles.Count);
    Assert.Contains(roles, r => r.Name == "Super Admin");
}
```

### Verify Seeded Data

After running the application:

```sql
-- Check roles
SELECT id, name, jsonb_array_length(permissions) as permission_count 
FROM roles;

-- Check super admin user
SELECT id, name, email, role_id, is_active 
FROM users 
WHERE email = 'suparadmin@sabbir.com';
```

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | Jan 2026 | Initial implementation with 6 roles and super admin |

## Related Files

- [DbSeeder.cs](DbSeeder.cs) - Main seeder implementation
- [Program.cs](../../RetailPOS.API/Program.cs) - Startup configuration
- [RetailPOSDbContext.cs](RetailPOSDbContext.cs) - Database context
- [User.cs](../../RetailPOS.Core/Entities/User.cs) - User entity
- [Role.cs](../../RetailPOS.Core/Entities/Role.cs) - Role entity

---

**Need Help?** Check the main [README.md](../../../README.md) or [IMPLEMENTATION_ROADMAP.md](../../../IMPLEMENTATION_ROADMAP.md)
