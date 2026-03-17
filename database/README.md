# Database Seed Data

This directory contains seed data scripts for the Retail POS system.

## Quick Start

### Apply Seed Data

**Using PowerShell:**
```powershell
cd c:\ess\Temp\pos-v2\database
.\apply-seed.ps1
```

**Using Docker:**
```powershell
docker exec -i retailpos-postgres psql -U postgres -d retailpos_db < seed-data.sql
```

**Using psql directly:**
```powershell
$env:PGPASSWORD="postgres"
psql -h localhost -p 5432 -U postgres -d retailpos_db -f seed-data.sql
```

## Default Credentials

### Super Admin
- **Email:** `admin@retailpos.com`
- **Password:** `Admin@123`
- **Permissions:** Full system access (*)

### Manager
- **Email:** `manager@retailpos.com`
- **Password:** `Admin@123`
- **Permissions:** Store management operations

### Cashier
- **Email:** `cashier@retailpos.com`
- **Password:** `Admin@123`
- **Permissions:** Sales operations only

⚠️ **IMPORTANT:** Change all default passwords immediately after first login!

## What's Included

### 1. Roles (6 roles)
- **Super Admin** - Full system access
- **Admin** - Store management capabilities
- **Manager** - Operational management
- **Cashier** - Sales operations
- **Stock Manager** - Inventory management
- **User** - Read-only access

### 2. Users (3 users)
- Super Admin user
- Manager user
- Cashier user

### 3. Outlets (2 outlets)
- Main Store (MAIN-001)
- Branch Store (BRANCH-001)

### 4. Categories (5 categories)
- Electronics
- Clothing
- Food & Beverages
- Home & Garden
- Health & Beauty

### 5. Suppliers (2 suppliers)
- Tech Supplies Inc.
- Fashion Wholesale Ltd.

### 6. Customers (3 customers)
- Walk-in Customer
- John Customer
- Jane Customer

### 7. Warehouses (2 warehouses)
- Main Warehouse
- Branch Warehouse

## Permission System

The system uses a JSONB-based permission system. Here are the available permissions:

### User Management
- `users.view` - View users
- `users.create` - Create users
- `users.edit` - Edit users
- `users.delete` - Delete users

### Role Management
- `roles.view` - View roles
- `roles.create` - Create roles
- `roles.edit` - Edit roles
- `roles.delete` - Delete roles

### Product Management
- `products.view` - View products
- `products.create` - Create products
- `products.edit` - Edit products
- `products.delete` - Delete products

### Category Management
- `categories.view` - View categories
- `categories.create` - Create categories
- `categories.edit` - Edit categories
- `categories.delete` - Delete categories

### Inventory Management
- `inventory.view` - View inventory
- `inventory.create` - Add inventory
- `inventory.edit` - Edit inventory
- `inventory.delete` - Delete inventory

### Sales Management
- `sales.view` - View sales
- `sales.create` - Create sales
- `sales.edit` - Edit sales
- `sales.delete` - Delete sales

### Purchase Management
- `purchases.view` - View purchases
- `purchases.create` - Create purchases
- `purchases.edit` - Edit purchases
- `purchases.delete` - Delete purchases

### Customer Management
- `customers.view` - View customers
- `customers.create` - Create customers
- `customers.edit` - Edit customers
- `customers.delete` - Delete customers

### Supplier Management
- `suppliers.view` - View suppliers
- `suppliers.create` - Create suppliers
- `suppliers.edit` - Edit suppliers
- `suppliers.delete` - Delete suppliers

### Stock Management
- `stock-transfer.view` - View stock transfers
- `stock-transfer.create` - Create stock transfers
- `stock-adjustment.view` - View stock adjustments
- `stock-adjustment.create` - Create stock adjustments

### Reporting
- `reports.view` - View reports
- `reports.export` - Export reports

### Settings
- `settings.view` - View settings
- `settings.edit` - Edit settings

### Wildcard
- `*` - All permissions (Super Admin only)

## Verification

After applying seed data, verify with:

```sql
-- Check roles
SELECT id, name, description, is_active FROM roles ORDER BY id;

-- Check users
SELECT 
    u.id, u.name, u.email, 
    r.name as role, 
    o.name as outlet
FROM users u
LEFT JOIN roles r ON u.role_id = r.id
LEFT JOIN outlets o ON u.outlet_id = o.id;

-- Check permissions for a role
SELECT name, permissions FROM roles WHERE name = 'Super Admin';
```

## Security Notes

1. **Change Default Passwords:** All users have the same default password `Admin@123`. Change these immediately.
2. **Super Admin Access:** The Super Admin has wildcard permission `*` which grants access to everything.
3. **Password Hash:** Passwords are hashed using BCrypt with work factor 11.
4. **Email Uniqueness:** User emails must be unique in the system.

## Troubleshooting

### Connection Error
```
Error: Cannot connect to database
```
**Solution:** Ensure PostgreSQL is running:
```powershell
docker-compose up -d
```

### Permission Denied
```
Error: permission denied for table users
```
**Solution:** Ensure you're using the correct database user with sufficient privileges.

### Duplicate Key Error
```
Error: duplicate key value violates unique constraint
```
**Solution:** Seed data is already applied. To reapply, first truncate tables or modify the script.

## Re-applying Seed Data

To reapply seed data (this will update existing records):

1. The script uses `ON CONFLICT DO UPDATE` for roles and users
2. Existing data will be updated, not duplicated
3. To start fresh, uncomment the `TRUNCATE` line at the top of `seed-data.sql`

## Next Steps

After applying seed data:

1. Login with Super Admin credentials
2. Change the default password
3. Create additional users as needed
4. Configure outlets and warehouses
5. Add products and inventory
6. Start using the system!
