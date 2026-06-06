# POS v2 – Role Definition & Business Onboarding Requirement

## 1. System Role Overview

The POS system will support multi-business / multi-tenant operation.

### Roles

1. Super Admin
2. Business Owner / Admin
3. Outlet Manager
4. Warehouse Manager
5. Sales Person
6. Accounts Admin

---

## 2. Business Creation Flow

When Super Admin creates a new business, the system automatically creates:

- Business Owner user
- Default Outlet (Main Outlet)
- Outlet Manager user
- Sales Person user
- Accounts Admin user
- Optional Default Warehouse
- Optional Warehouse Manager user

### Acceptance Criteria

1. Business is created successfully.
2. Default users are created automatically.
3. Default outlet is created automatically.
4. Roles are assigned correctly.
5. BusinessId is linked to all records.
6. Transaction rollback if any step fails.
7. Invitation/reset password flow is used.
8. Duplicate email validation is enforced.

---

## 3. Role Responsibilities

### Super Admin

- Create and manage businesses
- Manage subscriptions/packages
- Activate/deactivate businesses
- View all tenants
- Reset Business Owner access

### Business Owner / Admin

- Manage users
- Manage outlets
- Manage products/categories
- View reports
- Configure settings

### Outlet Manager

- Manage outlet operations
- View outlet reports
- Manage outlet sales

### Warehouse Manager

- Manage inventory
- Receive stock
- Transfer stock
- Adjust stock

### Sales Person

- Create sales
- Process payments
- Print invoices
- Handle returns (if permitted)

### Accounts Admin

- Manage expenses
- Manage payments
- Financial reporting
- Due tracking

---

## 4. Data Isolation Rules

- Super Admin → All businesses
- Business Owner → Own business only
- Outlet Manager → Assigned outlet only
- Sales Person → Assigned outlet only
- Warehouse Manager → Assigned warehouse only
- Accounts Admin → Assigned business only

Required keys:

- BusinessId
- OutletId
- WarehouseId

---

## 5. Recommended Core Entities

- Business
- User
- Role
- Permission
- Outlet
- Warehouse
- Product
- Category
- Brand
- Supplier
- Customer
- Sale
- SaleItem
- Purchase
- PurchaseItem
- Stock
- StockMovement
- Payment
- Expense
- AuditLog

---

## 6. Retail POS Recommendations

### Inventory

- Barcode
- SKU
- Low Stock Alert
- Stock Transfer
- Stock Adjustment
- Damage Tracking

### Sales

- Hold Sale
- Return / Exchange
- Multiple Payment Methods
- Customer Due
- Cash Register

### Accounts

- Expense Tracking
- Supplier Due
- Customer Due
- VAT/Tax Report
- Profit Calculation

### SaaS Features

- Subscription Plans
- Trial Period
- Outlet Limits
- User Limits
- Feature Limits
- Renewal Reminder

---

## 7. Instructions for Claude

Analyze the POS v2 codebase and verify:

- Existing role structure
- Existing permission model
- Tenant isolation implementation
- Business creation workflow
- Default user creation
- Default outlet creation
- Authentication & authorization
- Frontend permission handling

Implement missing functionality using:

- Role-Based Access Control (RBAC)
- Database-driven permissions
- Transactional business onboarding
- Audit logging
- Proper tenant isolation
