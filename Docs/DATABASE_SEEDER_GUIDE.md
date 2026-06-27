# 🌱 Database Seeder - Quick Start Guide

## TL;DR

Your POS system now automatically seeds the database with essential data on startup!

### Default Super Admin Credentials
```
Email:    suparadmin@sabbir.com
Password: Admin@123
```
⚠️ **Change this password after first login!**

---

## What Happens on Startup?

When you run `dotnet run`, the application automatically:

1. ✅ Applies all database migrations
2. ✅ Checks if database needs seeding
3. ✅ Creates 6 default roles (if none exist)
4. ✅ Creates Super Admin user (if no roles existed)
5. ✅ Logs results to console

**No manual SQL scripts needed!** 🎉

---

## Quick Commands

### Start Application (Auto-Seeds)
```bash
cd src/RetailPOS.API
dotnet run
```

### Apply Pending Migrations (Manual)
```bash
# From repository root
dotnet ef database update --project src/RetailPOS.Infrastructure --startup-project src/RetailPOS.API

# Or from src/RetailPOS.API
dotnet ef database update --project ../RetailPOS.Infrastructure
```

If `dotnet ef` is not available:
```bash
dotnet tool install --global dotnet-ef
```

### Reset Database (Development)
```bash
# Drop and recreate database
docker exec -it retailpos-postgres psql -U postgres -c "DROP DATABASE IF EXISTS retailpos_db;"
docker exec -it retailpos-postgres psql -U postgres -c "CREATE DATABASE retailpos_db;"

# Restart app (will auto-seed)
cd src/RetailPOS.API
dotnet run
```

### View Seeded Data
```sql
-- Check roles
SELECT name, jsonb_array_length(permissions) as perm_count FROM roles;

-- Check super admin
SELECT name, email, is_active FROM users WHERE email = 'suparadmin@sabbir.com';
```

---

## Seeded Data Summary

### 6 Default Roles

| Role | Permissions | Use Case |
|------|------------|----------|
| Super Admin | `["*"]` | Full system access |
| Admin | 40+ | Store management |
| Manager | 16 | Operations |
| Cashier | 6 | Sales only |
| Stock Manager | 14 | Inventory |
| User | 4 | Read-only |

### 1 Super Admin User

- **Name:** Super Admin
- **Email:** suparadmin@sabbir.com
- **Password:** Admin@123 (BCrypt hashed)
- **Role:** Super Admin
- **Outlet:** None (can access all)
- **Status:** Active

---

## Enable Optional Seeding

Want sample outlets and categories? Edit [Program.cs](src/RetailPOS.API/Program.cs):

```csharp
// Uncomment these lines:
await DbSeeder.SeedOutletsAsync(context);      // Seeds 2 sample outlets
await DbSeeder.SeedCategoriesAsync(context);   // Seeds 5 sample categories
```

---

## Permission System

### Format
```json
["resource.action", "another.action"]
```

### Examples
```json
["users.view", "users.create", "users.edit", "users.delete"]
["products.view", "sales.create"]
["*"]  // Super Admin - everything
```

### Check Permissions (Backend)
```csharp
bool canEdit = user.Role.Permissions.Contains("users.edit");
bool isSuperAdmin = user.Role.Permissions.Contains("*");
```

### Check Permissions (Frontend)
```typescript
hasPermission(permission: string): boolean {
  const permissions = this.currentUser.role.permissions;
  return permissions.includes('*') || permissions.includes(permission);
}
```

---

## Key Features

✅ **Idempotent** - Safe to run multiple times  
✅ **Automatic** - Runs on every startup  
✅ **Fast** - Checks existence before seeding  
✅ **Secure** - BCrypt password hashing (work factor 12)  
✅ **Logged** - See results in console  
✅ **Extensible** - Easy to add more seed data

---

## Files Changed

| File | Purpose |
|------|---------|
| [DbSeeder.cs](src/RetailPOS.Infrastructure/Data/DbSeeder.cs) | Main seeder logic |
| [Program.cs](src/RetailPOS.API/Program.cs) | Calls seeder on startup |
| [RetailPOS.Infrastructure.csproj](src/RetailPOS.Infrastructure/RetailPOS.Infrastructure.csproj) | Added BCrypt package |

---

## Testing the Seeder

### 1. Start Fresh
```bash
# Start PostgreSQL
docker-compose up -d

# Run application
cd src/RetailPOS.API
dotnet run
```

### 2. Check Logs
```
info: Program[0]
      Checking if database needs seeding...
info: Program[0]
      Database seeding completed successfully
```

### 3. Login via API
```bash
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "suparadmin@sabbir.com",
    "password": "Admin@123"
  }'
```

### 4. Login via Frontend
```
URL: http://localhost:4200/login
Email: suparadmin@sabbir.com
Password: Admin@123
```

---

## Troubleshooting

### "Database already seeded"
✅ This is normal! The seeder detects existing data and skips seeding.

### Duplicate key error
```bash
# Delete roles and users, then restart
docker exec -it retailpos-postgres psql -U postgres -d retailpos_db \
  -c "DELETE FROM users; DELETE FROM roles;"
```

### BCrypt error
```bash
# Ensure package is installed
cd src/RetailPOS.Infrastructure
dotnet restore
```

### Can't login
- Check email: `suparadmin@sabbir.com` (lowercase)
- Check password: `Admin@123` (case-sensitive)
- Check user exists: `SELECT * FROM users WHERE email = 'suparadmin@sabbir.com';`

---

## Production Checklist

Before deploying to production:

- [ ] Change Super Admin password
- [ ] Review all role permissions
- [ ] Disable optional seeding (outlets/categories)
- [ ] Set up proper backup strategy
- [ ] Enable audit logging
- [ ] Review security settings
- [ ] Test permission system thoroughly

---

## Need More Details?

📖 See [Infrastructure Data README](src/RetailPOS.Infrastructure/Data/README.md) for comprehensive documentation.

---

**Project:** Retail POS System v2  
**Date:** January 2026  
**Author:** Database Seeder Implementation
