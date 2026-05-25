# Database Schema Documentation

## Complete Entity Relationship Diagram (ERD)

### Tables Summary (24 Tables)

| Module | Tables | Count |
|--------|--------|-------|
| User & Role | roles, users | 2 |
| Location | outlets, warehouses | 2 |
| Product | categories, products, product_variants, inventories | 4 |
| Purchase | suppliers, purchase_orders, purchase_order_items, grns, grn_items | 5 |
| Sales | customers, sales, sale_items | 3 |
| Stock | stock_transfers, stock_transfer_items, stock_adjustments | 3 |
| Accounting | accounts, transactions, bills, expenses | 4 |
| Audit | audit_logs | 1 |

---

## Detailed Table Structures

### 1. User & Role Management

#### roles
```sql
id                BIGSERIAL PRIMARY KEY
name              VARCHAR(100) UNIQUE NOT NULL
permissions       JSONB NOT NULL DEFAULT '[]'
created_at        TIMESTAMPTZ DEFAULT NOW()
updated_at        TIMESTAMPTZ DEFAULT NOW()
```

#### users
```sql
id                BIGSERIAL PRIMARY KEY
name              VARCHAR(255) NOT NULL
email             VARCHAR(255) UNIQUE NOT NULL
password_hash     VARCHAR(255) NOT NULL
role_id           BIGINT FK -> roles(id)
outlet_id         BIGINT FK -> outlets(id)
is_active         BOOLEAN DEFAULT TRUE
created_at        TIMESTAMPTZ DEFAULT NOW()
updated_at        TIMESTAMPTZ DEFAULT NOW()
```

**Relationships:**
- User belongs to Role (many-to-one)
- User belongs to Outlet (many-to-one)

---

### 2. Location Management

#### outlets
```sql
id                BIGSERIAL PRIMARY KEY
name              VARCHAR(100) NOT NULL
address           TEXT NOT NULL
contact_number    VARCHAR(20)
manager_id        BIGINT FK -> users(id)
created_at        TIMESTAMPTZ DEFAULT NOW()
updated_at        TIMESTAMPTZ DEFAULT NOW()
```

#### warehouses
```sql
id                BIGSERIAL PRIMARY KEY
name              VARCHAR(100) NOT NULL
address           TEXT NOT NULL
capacity          INTEGER
manager_id        BIGINT FK -> users(id)
created_at        TIMESTAMPTZ DEFAULT NOW()
updated_at        TIMESTAMPTZ DEFAULT NOW()
```

**Relationships:**
- Outlet has one Manager (User)
- Warehouse has one Manager (User)

---

### 3. Product & Inventory Management

#### categories
```sql
id                BIGSERIAL PRIMARY KEY
name              VARCHAR(100) NOT NULL
parent_id         BIGINT FK -> categories(id) [SELF-REFERENCE]
created_at        TIMESTAMPTZ DEFAULT NOW()
updated_at        TIMESTAMPTZ DEFAULT NOW()
```

#### products
```sql
id                BIGSERIAL PRIMARY KEY
name              VARCHAR(255) NOT NULL
category_id       BIGINT FK -> categories(id)
base_price        NUMERIC(10,2) DEFAULT 0
cost_price        NUMERIC(10,2) DEFAULT 0
tax_rate          NUMERIC(5,2) DEFAULT 0
description       TEXT
barcode           VARCHAR(50) UNIQUE
created_at        TIMESTAMPTZ DEFAULT NOW()
updated_at        TIMESTAMPTZ DEFAULT NOW()
```

#### product_variants
```sql
id                BIGSERIAL PRIMARY KEY
product_id        BIGINT FK -> products(id)
sku               VARCHAR(50) UNIQUE NOT NULL
barcode           VARCHAR(50) UNIQUE
attributes        JSONB NOT NULL  -- {"size": "M", "color": "Red"}
price_adjustment  NUMERIC(10,2) DEFAULT 0
cost_adjustment   NUMERIC(10,2) DEFAULT 0
created_at        TIMESTAMPTZ DEFAULT NOW()
updated_at        TIMESTAMPTZ DEFAULT NOW()
```

#### inventories
```sql
id                    BIGSERIAL PRIMARY KEY
variant_id            BIGINT FK -> product_variants(id)
location_id           BIGINT NOT NULL
location_type         VARCHAR(20) CHECK IN ('outlet','warehouse')
quantity              INTEGER DEFAULT 0
low_stock_threshold   INTEGER DEFAULT 10
batch_number          VARCHAR(50)
expiry_date           DATE
UNIQUE(variant_id, location_id, location_type)
```

**Relationships:**
- Product belongs to Category
- Category self-references (parent/child hierarchy)
- Product has many Variants
- Variant has many Inventory records (per location)

**Indexes:**
- idx_inventory_variant_location ON inventories(variant_id, location_id)

---

### 4. Supplier & Purchase Management

#### suppliers
```sql
id                BIGSERIAL PRIMARY KEY
name              VARCHAR(255) NOT NULL
contact           VARCHAR(255)
address           TEXT
credit_limit      NUMERIC(10,2) DEFAULT 0
created_at        TIMESTAMPTZ DEFAULT NOW()
updated_at        TIMESTAMPTZ DEFAULT NOW()
```

#### purchase_orders
```sql
id                  BIGSERIAL PRIMARY KEY
supplier_id         BIGINT FK -> suppliers(id)
warehouse_id        BIGINT FK -> warehouses(id)
order_date          DATE NOT NULL
expected_delivery   DATE
total_amount        NUMERIC(10,2) NOT NULL
status              VARCHAR(20) CHECK IN ('pending','approved','received','cancelled')
created_by          BIGINT FK -> users(id)
created_at          TIMESTAMPTZ DEFAULT NOW()
updated_at          TIMESTAMPTZ DEFAULT NOW()
```

#### purchase_order_items
```sql
id                BIGSERIAL PRIMARY KEY
po_id             BIGINT FK -> purchase_orders(id)
variant_id        BIGINT FK -> product_variants(id)
quantity          INTEGER NOT NULL
unit_price        NUMERIC(10,2) NOT NULL
```

#### grns (Goods Received Notes)
```sql
id                BIGSERIAL PRIMARY KEY
po_id             BIGINT FK -> purchase_orders(id)
received_date     DATE NOT NULL
status            VARCHAR(20) CHECK IN ('partial','full')
created_by        BIGINT FK -> users(id)
created_at        TIMESTAMPTZ DEFAULT NOW()
```

#### grn_items
```sql
id                BIGSERIAL PRIMARY KEY
grn_id            BIGINT FK -> grns(id)
po_item_id        BIGINT FK -> purchase_order_items(id)
received_qty      INTEGER NOT NULL
```

**Relationships:**
- PurchaseOrder belongs to Supplier
- PurchaseOrder belongs to Warehouse
- PurchaseOrder has many Items
- PurchaseOrder has many GRNs
- GRN has many Items
- GrnItem references PurchaseOrderItem

---

### 5. Customer & Sales Management

#### customers
```sql
id                BIGSERIAL PRIMARY KEY
name              VARCHAR(255)
phone             VARCHAR(20)
email             VARCHAR(255)
loyalty_points    INTEGER DEFAULT 0
created_at        TIMESTAMPTZ DEFAULT NOW()
```

#### sales
```sql
id                BIGSERIAL PRIMARY KEY
outlet_id         BIGINT FK -> outlets(id)
customer_id       BIGINT FK -> customers(id)
sale_date         TIMESTAMPTZ DEFAULT NOW()
total_amount      NUMERIC(10,2) NOT NULL
discount          NUMERIC(10,2) DEFAULT 0
tax               NUMERIC(10,2) DEFAULT 0
payment_method    VARCHAR(20) NOT NULL
status            VARCHAR(20) DEFAULT 'completed'
cashier_id        BIGINT FK -> users(id)
created_at        TIMESTAMPTZ DEFAULT NOW()
```

#### sale_items
```sql
id                BIGSERIAL PRIMARY KEY
sale_id           BIGINT FK -> sales(id)
variant_id        BIGINT FK -> product_variants(id)
quantity          INTEGER NOT NULL
unit_price        NUMERIC(10,2) NOT NULL
subtotal          NUMERIC(10,2) NOT NULL
```

**Relationships:**
- Sale belongs to Outlet
- Sale belongs to Customer (optional)
- Sale belongs to Cashier (User)
- Sale has many Items

**Indexes:**
- idx_sales_date ON sales(sale_date)
- idx_sales_outlet ON sales(outlet_id)

---

### 6. Stock Movement & Adjustment

#### stock_transfers
```sql
id                  BIGSERIAL PRIMARY KEY
from_location_id    BIGINT NOT NULL
from_location_type  VARCHAR(20) NOT NULL  -- 'outlet' or 'warehouse'
to_location_id      BIGINT NOT NULL
to_location_type    VARCHAR(20) NOT NULL
transfer_date       DATE NOT NULL
status              VARCHAR(20) DEFAULT 'pending'
approved_by         BIGINT FK -> users(id)
created_by          BIGINT FK -> users(id)
created_at          TIMESTAMPTZ DEFAULT NOW()
```

#### stock_transfer_items
```sql
id                BIGSERIAL PRIMARY KEY
transfer_id       BIGINT FK -> stock_transfers(id)
variant_id        BIGINT FK -> product_variants(id)
quantity          INTEGER NOT NULL
```

#### stock_adjustments
```sql
id                BIGSERIAL PRIMARY KEY
location_id       BIGINT NOT NULL
location_type     VARCHAR(20) NOT NULL
variant_id        BIGINT FK -> product_variants(id)
quantity_change   INTEGER NOT NULL  -- Can be negative
reason            VARCHAR(255) NOT NULL
adjusted_by       BIGINT FK -> users(id)
adjustment_date   TIMESTAMPTZ DEFAULT NOW()
```

**Relationships:**
- StockTransfer tracks movement between any locations
- StockTransfer has many Items
- StockAdjustment belongs to Variant and User

---

### 7. Accounting & Finance

#### accounts
```sql
id                BIGSERIAL PRIMARY KEY
name              VARCHAR(100) NOT NULL
type              VARCHAR(20) CHECK IN ('asset','liability','expense','revenue')
balance           NUMERIC(10,2) DEFAULT 0
```

#### transactions
```sql
id                  BIGSERIAL PRIMARY KEY
account_id          BIGINT FK -> accounts(id)
amount              NUMERIC(10,2) NOT NULL
type                VARCHAR(10) CHECK IN ('debit','credit')
description         VARCHAR(255)
transaction_date    TIMESTAMPTZ DEFAULT NOW()
reference_id        BIGINT  -- FK to other entities (polymorphic)
reference_type      VARCHAR(50)  -- 'sale', 'purchase', etc.
```

#### bills
```sql
id                BIGSERIAL PRIMARY KEY
supplier_id       BIGINT FK -> suppliers(id)
po_id             BIGINT FK -> purchase_orders(id)
amount_due        NUMERIC(10,2) NOT NULL
due_date          DATE NOT NULL
status            VARCHAR(20) DEFAULT 'unpaid'
```

#### expenses
```sql
id                BIGSERIAL PRIMARY KEY
category          VARCHAR(100) NOT NULL
amount            NUMERIC(10,2) NOT NULL
description       TEXT
expense_date      DATE NOT NULL
outlet_id         BIGINT FK -> outlets(id)
```

**Relationships:**
- Transaction belongs to Account
- Bill belongs to Supplier and PurchaseOrder
- Expense belongs to Outlet

---

### 8. Audit & Compliance

#### audit_logs
```sql
id                BIGSERIAL PRIMARY KEY
user_id           BIGINT FK -> users(id)
action            VARCHAR(50) NOT NULL  -- 'create', 'update', 'delete', 'view'
module            VARCHAR(50) NOT NULL  -- 'products', 'sales', etc.
entity_id         BIGINT NOT NULL       -- ID of affected entity
details           JSONB                 -- Additional info
ip_address        VARCHAR(45)
timestamp         TIMESTAMPTZ DEFAULT NOW()
```

**Indexes:**
- idx_audit_timestamp ON audit_logs(timestamp)

**Relationships:**
- AuditLog belongs to User

---

## Key Design Decisions

### 1. Naming Convention
- **snake_case** for all PostgreSQL objects (tables, columns, indexes)
- Follows PostgreSQL best practices
- Automatically converted by DbContext

### 2. JSONB Usage
- `roles.permissions`: Array of permission strings
- `product_variants.attributes`: Key-value pairs for variant properties
- `audit_logs.details`: Flexible audit information

### 3. Location Polymorphism
- `inventories.location_type` and `location_id` allow tracking stock in outlets OR warehouses
- Same approach in `stock_transfers` and `stock_adjustments`

### 4. Soft vs Hard Delete
- Most entities use hard delete (no deleted_at column)
- Use `is_active` flag for users instead

### 5. Timestamp Strategy
- `created_at` and `updated_at` on master tables
- `TIMESTAMPTZ` (with timezone) for PostgreSQL best practice
- Automatic defaults in database

### 6. Financial Precision
- All money fields: `NUMERIC(10,2)` for precision
- Supports up to 99,999,999.99

### 7. Audit Trail
- Comprehensive audit_logs table
- Immutable records (no updates/deletes)
- Captures who, what, when, where

---

## Sample Queries

### Get product with variants and stock
```sql
SELECT 
    p.name,
    pv.sku,
    pv.attributes,
    i.quantity,
    i.location_type,
    CASE 
        WHEN i.location_type = 'outlet' THEN o.name
        WHEN i.location_type = 'warehouse' THEN w.name
    END as location_name
FROM products p
JOIN product_variants pv ON p.id = pv.product_id
LEFT JOIN inventories i ON pv.id = i.variant_id
LEFT JOIN outlets o ON i.location_id = o.id AND i.location_type = 'outlet'
LEFT JOIN warehouses w ON i.location_id = w.id AND i.location_type = 'warehouse'
WHERE p.id = 1;
```

### Get sales report by outlet
```sql
SELECT 
    o.name as outlet,
    DATE(s.sale_date) as date,
    COUNT(s.id) as transaction_count,
    SUM(s.total_amount) as total_sales,
    SUM(s.discount) as total_discount,
    SUM(s.tax) as total_tax
FROM sales s
JOIN outlets o ON s.outlet_id = o.id
WHERE s.sale_date >= NOW() - INTERVAL '30 days'
GROUP BY o.name, DATE(s.sale_date)
ORDER BY date DESC, outlet;
```

### Get low stock items
```sql
SELECT 
    p.name,
    pv.sku,
    i.quantity,
    i.low_stock_threshold,
    i.location_type,
    CASE 
        WHEN i.location_type = 'outlet' THEN o.name
        WHEN i.location_type = 'warehouse' THEN w.name
    END as location
FROM inventories i
JOIN product_variants pv ON i.variant_id = pv.id
JOIN products p ON pv.product_id = p.id
LEFT JOIN outlets o ON i.location_id = o.id AND i.location_type = 'outlet'
LEFT JOIN warehouses w ON i.location_id = w.id AND i.location_type = 'warehouse'
WHERE i.quantity <= i.low_stock_threshold;
```

---

## Migration Notes

### Initial Migration: 20260128081847_InitialCreate
- Creates all 24 tables
- Sets up all foreign keys
- Creates indexes
- Defines check constraints
- Total: 950 lines of migration code

### To apply:
```bash
cd src/RetailPOS.Infrastructure
dotnet ef database update --startup-project ../RetailPOS.API/RetailPOS.API.csproj
```

---

**Document Version**: 1.0  
**Last Updated**: 2026-01-28  
**Database**: PostgreSQL 16+  
**EF Core**: 9.0
