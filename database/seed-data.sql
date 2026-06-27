-- =====================================================
-- Retail POS - Comprehensive Seed Data
-- Seeds all modules: Roles, Users, Outlets, Warehouses,
-- Categories, Suppliers, Variations, Products, Inventory,
-- Customers, Accounts, POs, GRNs, Sales, Stock, Expenses, Bills
-- =====================================================

-- Clear existing data (optional - comment out to keep existing data)
-- TRUNCATE TABLE transactions, bills, expenses, stock_adjustment, stock_transfer_items, stock_transfers,
--               sale_items, sales, grn_items, grns, purchase_order_items, purchase_orders,
--               inventories, product_variant_options, product_variants, product_variations,
--               products, variation_options, variations, customers, suppliers,
--               categories, users, warehouses, outlets, accounts, roles
--               RESTART IDENTITY CASCADE;

-- =====================================================
-- 1. Insert Default Roles
-- =====================================================

INSERT INTO roles (name, permissions, created_at, updated_at)
VALUES 
    ('Super Admin', '["*"]'::jsonb, NOW(), NOW()),
    ('Admin', '[
        "users.view", "users.create", "users.edit", "users.delete",
        "roles.view", "roles.create", "roles.edit", "roles.delete",
        "products.view", "products.create", "products.edit", "products.delete",
        "barcode.view", "barcode.print", "barcode.bulk_print", "barcode.template_manage",
        "categories.view", "categories.create", "categories.edit", "categories.delete",
        "inventory.view", "inventory.create", "inventory.edit", "inventory.delete",
        "stock_adjustments.view", "stock_adjustments.create", "stock_adjustments.edit", "stock_adjustments.delete", "stock_adjustments.approve", "stock_adjustments.cancel",
        "StockCount.ViewOwn", "StockCount.ViewAll", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Approve", "StockCount.Reject", "StockCount.Reopen",
        "stock_transfers.view", "stock_transfers.create", "stock_transfers.edit", "stock_transfers.delete", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "low_stock_alerts.view", "low_stock_alerts.create", "low_stock_alerts.edit", "low_stock_alerts.delete",
        "sales.view", "sales.create", "sales.edit", "sales.delete",
        "purchases.view", "purchases.create", "purchases.edit", "purchases.delete",
        "customers.view", "customers.create", "customers.edit", "customers.delete",
        "suppliers.view", "suppliers.create", "suppliers.edit", "suppliers.delete",
        "outlets.view", "outlets.create", "outlets.edit", "outlets.delete",
        "warehouses.view", "warehouses.create", "warehouses.edit", "warehouses.delete",
        "reports.view", "reports.export",
        "settings.view", "settings.edit"
    ]'::jsonb, NOW(), NOW()),
    ('Manager', '[
        "products.view", "products.create", "products.edit",
        "barcode.view", "barcode.print", "barcode.bulk_print",
        "categories.view", "categories.create", "categories.edit",
        "inventory.view", "inventory.create", "inventory.edit",
        "stock_adjustments.view", "stock_adjustments.create",
        "StockCount.ViewOwn", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Reject", "StockCount.Reopen",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "stock_transfers.view", "stock_transfers.create", "stock_transfers.approve", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create",
        "low_stock_alerts.view",
        "sales.view", "sales.create", "sales.edit",
        "purchases.view", "purchases.create",
        "customers.view", "customers.create", "customers.edit",
        "suppliers.view", "suppliers.create", "suppliers.edit",
        "reports.view"
    ]'::jsonb, NOW(), NOW()),
    ('Cashier', '[
        "products.view",
        "barcode.view", "barcode.print",
        "inventory.view",
        "low_stock_alerts.view",
        "sales.view", "sales.create",
        "customers.view", "customers.create"
    ]'::jsonb, NOW(), NOW()),
    ('Stock Manager', '[
        "products.view",
        "barcode.view", "barcode.print", "barcode.bulk_print",
        "categories.view",
        "inventory.view", "inventory.create", "inventory.edit", "inventory.delete",
        "stock_adjustments.view", "stock_adjustments.create", "stock_adjustments.edit", "stock_adjustments.delete", "stock_adjustments.approve", "stock_adjustments.cancel",
        "StockCount.ViewOwn", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Reject", "StockCount.Reopen",
        "stock_transfers.view", "stock_transfers.create", "stock_transfers.edit", "stock_transfers.delete", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "low_stock_alerts.view", "low_stock_alerts.edit",
        "purchases.view", "purchases.create", "purchases.edit",
        "suppliers.view", "suppliers.create", "suppliers.edit",
        "warehouses.view",
        "reports.view"
    ]'::jsonb, NOW(), NOW()),
    ('User', '[
        "products.view",
        "barcode.view",
        "inventory.view",
        "low_stock_alerts.view",
        "sales.view",
        "reports.view"
    ]'::jsonb, NOW(), NOW()),
    -- v2 canonical roles
    ('BusinessOwner', '[
        "users.view", "users.create", "users.edit", "users.delete",
        "roles.view",
        "products.view", "products.create", "products.edit", "products.delete",
        "barcode.view", "barcode.print", "barcode.bulk_print", "barcode.template_manage",
        "categories.view", "categories.create", "categories.edit", "categories.delete",
        "inventory.view", "inventory.create", "inventory.edit", "inventory.delete",
        "stock_adjustments.view", "stock_adjustments.create", "stock_adjustments.edit", "stock_adjustments.delete", "stock_adjustments.approve", "stock_adjustments.cancel",
        "StockCount.ViewOwn", "StockCount.ViewAll", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Approve", "StockCount.Reject", "StockCount.Reopen",
        "stock_transfers.view", "stock_transfers.create", "stock_transfers.edit", "stock_transfers.delete", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "low_stock_alerts.view", "low_stock_alerts.create", "low_stock_alerts.edit", "low_stock_alerts.delete",
        "sales.view", "sales.create", "sales.edit", "sales.delete",
        "purchases.view", "purchases.create", "purchases.edit", "purchases.delete",
        "customers.view", "customers.create", "customers.edit", "customers.delete",
        "suppliers.view", "suppliers.create", "suppliers.edit", "suppliers.delete",
        "outlets.view", "outlets.create", "outlets.edit", "outlets.delete",
        "warehouses.view", "warehouses.create", "warehouses.edit", "warehouses.delete",
        "accounts.view", "accounts.create", "accounts.edit",
        "transactions.view", "transactions.create",
        "reports.view", "reports.export",
        "settings.view", "settings.edit"
    ]'::jsonb, NOW(), NOW()),
    ('BusinessAdmin', '[
        "users.view", "users.create", "users.edit",
        "roles.view",
        "products.view", "products.create", "products.edit", "products.delete",
        "barcode.view", "barcode.print", "barcode.bulk_print", "barcode.template_manage",
        "categories.view", "categories.create", "categories.edit", "categories.delete",
        "inventory.view", "inventory.create", "inventory.edit", "inventory.delete",
        "stock_adjustments.view", "stock_adjustments.create", "stock_adjustments.edit", "stock_adjustments.delete", "stock_adjustments.approve", "stock_adjustments.cancel",
        "StockCount.ViewOwn", "StockCount.ViewAll", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Approve", "StockCount.Reject", "StockCount.Reopen",
        "stock_transfers.view", "stock_transfers.create", "stock_transfers.edit", "stock_transfers.delete", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "low_stock_alerts.view", "low_stock_alerts.create", "low_stock_alerts.edit", "low_stock_alerts.delete",
        "sales.view", "sales.create", "sales.edit", "sales.delete",
        "purchases.view", "purchases.create", "purchases.edit", "purchases.delete",
        "customers.view", "customers.create", "customers.edit", "customers.delete",
        "suppliers.view", "suppliers.create", "suppliers.edit", "suppliers.delete",
        "outlets.view", "outlets.create", "outlets.edit",
        "warehouses.view", "warehouses.create", "warehouses.edit",
        "accounts.view", "accounts.create", "accounts.edit",
        "transactions.view", "transactions.create",
        "reports.view", "reports.export",
        "settings.view"
    ]'::jsonb, NOW(), NOW()),
    ('AccountsAdmin', '[
        "accounts.view", "accounts.create", "accounts.edit",
        "transactions.view", "transactions.create",
        "sales.view",
        "purchases.view",
        "suppliers.view",
        "customers.view",
        "reports.view", "reports.export"
    ]'::jsonb, NOW(), NOW()),
    ('OutletManager', '[
        "products.view", "products.create", "products.edit",
        "barcode.view", "barcode.print", "barcode.bulk_print",
        "categories.view",
        "inventory.view", "inventory.create", "inventory.edit",
        "stock_adjustments.view", "stock_adjustments.create",
        "StockCount.ViewOwn", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Reject", "StockCount.Reopen",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "stock_transfers.view", "stock_transfers.create", "stock_transfers.approve", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create",
        "low_stock_alerts.view",
        "sales.view", "sales.create", "sales.edit",
        "customers.view", "customers.create", "customers.edit",
        "suppliers.view",
        "outlets.view",
        "reports.view"
    ]'::jsonb, NOW(), NOW()),
    ('SalesPerson', '[
        "products.view",
        "barcode.view", "barcode.print",
        "inventory.view",
        "stock_transfers.view",
        "low_stock_alerts.view",
        "sales.view", "sales.create",
        "customers.view", "customers.create"
    ]'::jsonb, NOW(), NOW()),
    ('WarehouseManager', '[
        "products.view",
        "barcode.view", "barcode.print", "barcode.bulk_print",
        "categories.view",
        "inventory.view", "inventory.create", "inventory.edit", "inventory.delete",
        "stock_adjustments.view", "stock_adjustments.create", "stock_adjustments.edit", "stock_adjustments.delete", "stock_adjustments.approve", "stock_adjustments.cancel",
        "StockCount.ViewOwn", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Reject", "StockCount.Reopen",
        "stock_transfers.view", "stock_transfers.create", "stock_transfers.edit", "stock_transfers.delete", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "low_stock_alerts.view", "low_stock_alerts.edit",
        "purchases.view", "purchases.create", "purchases.edit", "purchases.receive",
        "suppliers.view", "suppliers.create", "suppliers.edit",
        "warehouses.view",
        "reports.view"
    ]'::jsonb, NOW(), NOW())
ON CONFLICT (name) DO UPDATE SET permissions = EXCLUDED.permissions, updated_at = NOW();

-- =====================================================
-- 2. Insert Outlets (managers assigned after users)
-- =====================================================

INSERT INTO outlets (name, address, contact_number, created_at, updated_at)
VALUES
    ('Main Store',       '123 Main Street, New York, NY 10001',      '+1-555-0100', NOW(), NOW()),
    ('Downtown Branch',  '456 Downtown Ave, New York, NY 10002',     '+1-555-0200', NOW(), NOW())
ON CONFLICT DO NOTHING;

-- =====================================================
-- 3. Insert Warehouses (managers assigned after users)
-- =====================================================

INSERT INTO warehouses (name, address, capacity, created_at, updated_at)
VALUES
    ('Central Warehouse', '789 Warehouse Blvd, Brooklyn, NY 11201', 5000, NOW(), NOW()),
    ('North Warehouse',   '321 North Industrial Rd, Queens, NY 11101', 3000, NOW(), NOW())
ON CONFLICT DO NOTHING;

-- =====================================================
-- 4. Insert Users
-- Password for all users: Admin@123 (BCrypt work factor 11)
-- =====================================================

INSERT INTO users (name, email, password_hash, role_id, outlet_id, is_active, created_at, updated_at)
VALUES
    (
        'Super Admin', 'suparadmin@sabbir.com',
        '$2a$11$A1K7RvjnXpXriu/iW6S87eDSoHZSG8vZIGYlF4RSsPVgiNUpMm3Su',
        (SELECT id FROM roles WHERE name = 'Super Admin'), NULL, true, NOW(), NOW()
    ),
    (
        'John Admin', 'john.admin@retailpos.com',
        '$2a$11$A1K7RvjnXpXriu/iW6S87eDSoHZSG8vZIGYlF4RSsPVgiNUpMm3Su',
        (SELECT id FROM roles WHERE name = 'Admin'),
        (SELECT id FROM outlets WHERE name = 'Main Store'), true, NOW(), NOW()
    ),
    (
        'Sarah Manager', 'sarah.manager@retailpos.com',
        '$2a$11$A1K7RvjnXpXriu/iW6S87eDSoHZSG8vZIGYlF4RSsPVgiNUpMm3Su',
        (SELECT id FROM roles WHERE name = 'Manager'),
        (SELECT id FROM outlets WHERE name = 'Main Store'), true, NOW(), NOW()
    ),
    (
        'Mike Cashier', 'mike.cashier@retailpos.com',
        '$2a$11$A1K7RvjnXpXriu/iW6S87eDSoHZSG8vZIGYlF4RSsPVgiNUpMm3Su',
        (SELECT id FROM roles WHERE name = 'Cashier'),
        (SELECT id FROM outlets WHERE name = 'Main Store'), true, NOW(), NOW()
    ),
    (
        'Lisa Branch Manager', 'lisa.manager@retailpos.com',
        '$2a$11$A1K7RvjnXpXriu/iW6S87eDSoHZSG8vZIGYlF4RSsPVgiNUpMm3Su',
        (SELECT id FROM roles WHERE name = 'Manager'),
        (SELECT id FROM outlets WHERE name = 'Downtown Branch'), true, NOW(), NOW()
    ),
    (
        'Tom Stock Manager', 'tom.stock@retailpos.com',
        '$2a$11$A1K7RvjnXpXriu/iW6S87eDSoHZSG8vZIGYlF4RSsPVgiNUpMm3Su',
        (SELECT id FROM roles WHERE name = 'Stock Manager'),
        NULL, true, NOW(), NOW()
    )
ON CONFLICT (email) DO UPDATE SET name = EXCLUDED.name, role_id = EXCLUDED.role_id, updated_at = NOW();

-- Assign outlet managers
UPDATE outlets SET manager_id = (SELECT id FROM users WHERE email = 'sarah.manager@retailpos.com')
WHERE name = 'Main Store';
UPDATE outlets SET manager_id = (SELECT id FROM users WHERE email = 'lisa.manager@retailpos.com')
WHERE name = 'Downtown Branch';

-- Assign warehouse managers
UPDATE warehouses SET manager_id = (SELECT id FROM users WHERE email = 'tom.stock@retailpos.com');

-- =====================================================
-- 5. Insert Categories
-- =====================================================

INSERT INTO categories (name, description, parent_category_id, display_order, is_active, created_at, updated_at)
VALUES
    ('Electronics',      'Electronic devices and accessories',    NULL, 1, true, NOW(), NOW()),
    ('Clothing',         'Apparel and fashion items',             NULL, 2, true, NOW(), NOW()),
    ('Food & Beverages', 'Food products and drinks',              NULL, 3, true, NOW(), NOW()),
    ('Home & Garden',    'Home essentials and gardening',         NULL, 4, true, NOW(), NOW()),
    ('Sports & Outdoors','Sporting goods and outdoor equipment',  NULL, 5, true, NOW(), NOW())
ON CONFLICT DO NOTHING;

INSERT INTO categories (name, description, parent_category_id, display_order, is_active, created_at, updated_at)
VALUES
    ('Laptops',      'Laptop computers',              (SELECT id FROM categories WHERE name = 'Electronics'), 1, true, NOW(), NOW()),
    ('Mobile Phones','Smartphones and mobile devices',(SELECT id FROM categories WHERE name = 'Electronics'), 2, true, NOW(), NOW()),
    ('Accessories',  'Electronic accessories',        (SELECT id FROM categories WHERE name = 'Electronics'), 3, true, NOW(), NOW()),
    ('Men''s Wear',  'Men''s clothing',               (SELECT id FROM categories WHERE name = 'Clothing'),    1, true, NOW(), NOW()),
    ('Women''s Wear','Women''s clothing',             (SELECT id FROM categories WHERE name = 'Clothing'),    2, true, NOW(), NOW()),
    ('Coffee & Tea', 'Coffee beans, tea, hot beverages',(SELECT id FROM categories WHERE name = 'Food & Beverages'), 1, true, NOW(), NOW())
ON CONFLICT DO NOTHING;

-- =====================================================
-- 6. Insert Suppliers
-- =====================================================

INSERT INTO suppliers (name, contact, address, credit_limit, created_at, updated_at)
VALUES
    ('TechSupply Co.',           'contact@techsupply.com',  '500 Tech Park, San Jose, CA 95101',         50000, NOW(), NOW()),
    ('Fashion World Distributors','orders@fashionworld.com','200 Fashion District, Los Angeles, CA 90015',30000, NOW(), NOW()),
    ('Global Food Imports',      'sales@globalfood.com',   '100 Harbor View, Miami, FL 33101',          20000, NOW(), NOW())
ON CONFLICT DO NOTHING;

-- =====================================================
-- 7. Insert Variations and Options
-- =====================================================

INSERT INTO variations (name, display_order, is_active, created_at, updated_at)
VALUES
    ('Color',   1, true, NOW(), NOW()),
    ('Size',    2, true, NOW(), NOW()),
    ('Storage', 3, true, NOW(), NOW())
ON CONFLICT DO NOTHING;

INSERT INTO variation_options (variation_id, name, price_adjustment, display_order, is_active, created_at, updated_at)
VALUES
    ((SELECT id FROM variations WHERE name = 'Color'), 'Black', 0,  1, true, NOW(), NOW()),
    ((SELECT id FROM variations WHERE name = 'Color'), 'White', 0,  2, true, NOW(), NOW()),
    ((SELECT id FROM variations WHERE name = 'Color'), 'Blue',  0,  3, true, NOW(), NOW()),
    ((SELECT id FROM variations WHERE name = 'Color'), 'Red',   0,  4, true, NOW(), NOW()),
    ((SELECT id FROM variations WHERE name = 'Size'), 'XS', 0,   1, true, NOW(), NOW()),
    ((SELECT id FROM variations WHERE name = 'Size'), 'S',  0,   2, true, NOW(), NOW()),
    ((SELECT id FROM variations WHERE name = 'Size'), 'M',  0,   3, true, NOW(), NOW()),
    ((SELECT id FROM variations WHERE name = 'Size'), 'L',  5,   4, true, NOW(), NOW()),
    ((SELECT id FROM variations WHERE name = 'Size'), 'XL', 10,  5, true, NOW(), NOW()),
    ((SELECT id FROM variations WHERE name = 'Storage'), '128GB', 0,   1, true, NOW(), NOW()),
    ((SELECT id FROM variations WHERE name = 'Storage'), '256GB', 50,  2, true, NOW(), NOW()),
    ((SELECT id FROM variations WHERE name = 'Storage'), '512GB', 150, 3, true, NOW(), NOW())
ON CONFLICT DO NOTHING;

-- =====================================================
-- 8. Insert Products
-- =====================================================

INSERT INTO products (name, description, sku, barcode, category_id, base_price, cost_price, tax_rate, has_variants, is_active, created_at, updated_at)
VALUES
    ('ProBook Laptop 15',     'High-performance 15-inch laptop',       'LPTP-001',     '8901234567890', (SELECT id FROM categories WHERE name = 'Laptops'),      999.99, 750.00, 8.5, false, true, NOW(), NOW()),
    ('SmartPhone X12',        'Latest flagship smartphone',            'PHN-X12',      '8901234567891', (SELECT id FROM categories WHERE name = 'Mobile Phones'), 699.99, 500.00, 8.5, true,  true, NOW(), NOW()),
    ('USB-C Cable 2m',        'High-speed USB-C cable 2 meters',       'ACC-USB-C-2M', '8901234567892', (SELECT id FROM categories WHERE name = 'Accessories'),   19.99,  5.00,  8.5, false, true, NOW(), NOW()),
    ('Classic Crew T-Shirt',  '100% cotton crew-neck t-shirt',         'CLT-MENS-TS',  '8901234567893', (SELECT id FROM categories WHERE name = 'Men''s Wear'),   29.99,  10.00, 0,   true,  true, NOW(), NOW()),
    ('Summer Floral Dress',   'Light and elegant summer dress',        'CLT-WMN-DRS',  '8901234567894', (SELECT id FROM categories WHERE name = 'Women''s Wear'), 49.99,  18.00, 0,   true,  true, NOW(), NOW()),
    ('Arabica Coffee Beans 1kg','Premium single-origin Arabica beans', 'FOOD-COF-1KG', '8901234567895', (SELECT id FROM categories WHERE name = 'Coffee & Tea'), 24.99,  12.00, 5,   false, true, NOW(), NOW()),
    ('TrueWireless Earbuds',  'Noise-cancelling wireless earbuds',     'ACC-EAR-TWS',  '8901234567896', (SELECT id FROM categories WHERE name = 'Accessories'),   89.99,  35.00, 8.5, true,  true, NOW(), NOW())
ON CONFLICT (barcode) DO NOTHING;

-- =====================================================
-- 9. Insert Product Variations (link products to variations)
-- =====================================================

INSERT INTO product_variations (product_id, variation_id, is_required, created_at)
VALUES
    ((SELECT id FROM products WHERE sku = 'PHN-X12'),     (SELECT id FROM variations WHERE name = 'Storage'), true, NOW()),
    ((SELECT id FROM products WHERE sku = 'PHN-X12'),     (SELECT id FROM variations WHERE name = 'Color'),   true, NOW()),
    ((SELECT id FROM products WHERE sku = 'CLT-MENS-TS'), (SELECT id FROM variations WHERE name = 'Color'),   true, NOW()),
    ((SELECT id FROM products WHERE sku = 'CLT-MENS-TS'), (SELECT id FROM variations WHERE name = 'Size'),    true, NOW()),
    ((SELECT id FROM products WHERE sku = 'CLT-WMN-DRS'), (SELECT id FROM variations WHERE name = 'Color'),   true, NOW()),
    ((SELECT id FROM products WHERE sku = 'CLT-WMN-DRS'), (SELECT id FROM variations WHERE name = 'Size'),    true, NOW()),
    ((SELECT id FROM products WHERE sku = 'ACC-EAR-TWS'), (SELECT id FROM variations WHERE name = 'Color'),   true, NOW())
ON CONFLICT DO NOTHING;

-- =====================================================
-- 10. Insert Product Variants
-- =====================================================

INSERT INTO product_variants (product_id, name, sku, barcode, attributes, price_adjustment, cost_adjustment, created_at, updated_at)
VALUES
    -- Laptop (single default variant)
    ((SELECT id FROM products WHERE sku = 'LPTP-001'),     'ProBook Laptop 15 - Default',        'LPTP-001-DEF',    NULL,            '{}',                              0,  0, NOW(), NOW()),
    -- SmartPhone variants
    ((SELECT id FROM products WHERE sku = 'PHN-X12'),      'SmartPhone X12 - Black 128GB',       'PHN-X12-BLK-128', '8901234568001', '{"Color":"Black","Storage":"128GB"}', 0,  0, NOW(), NOW()),
    ((SELECT id FROM products WHERE sku = 'PHN-X12'),      'SmartPhone X12 - Black 256GB',       'PHN-X12-BLK-256', '8901234568002', '{"Color":"Black","Storage":"256GB"}', 50, 30, NOW(), NOW()),
    ((SELECT id FROM products WHERE sku = 'PHN-X12'),      'SmartPhone X12 - White 128GB',       'PHN-X12-WHT-128', '8901234568003', '{"Color":"White","Storage":"128GB"}', 0,  0, NOW(), NOW()),
    ((SELECT id FROM products WHERE sku = 'PHN-X12'),      'SmartPhone X12 - White 256GB',       'PHN-X12-WHT-256', '8901234568004', '{"Color":"White","Storage":"256GB"}', 50, 30, NOW(), NOW()),
    -- USB Cable (single default variant)
    ((SELECT id FROM products WHERE sku = 'ACC-USB-C-2M'), 'USB-C Cable 2m - Default',           'ACC-USB-C-2M-DEF',NULL,            '{}',                              0,  0, NOW(), NOW()),
    -- T-Shirt variants
    ((SELECT id FROM products WHERE sku = 'CLT-MENS-TS'),  'Classic Crew T-Shirt - Black S',     'CLT-TS-BLK-S',    '8901234568101', '{"Color":"Black","Size":"S"}',    0,  0, NOW(), NOW()),
    ((SELECT id FROM products WHERE sku = 'CLT-MENS-TS'),  'Classic Crew T-Shirt - Black M',     'CLT-TS-BLK-M',    '8901234568102', '{"Color":"Black","Size":"M"}',    0,  0, NOW(), NOW()),
    ((SELECT id FROM products WHERE sku = 'CLT-MENS-TS'),  'Classic Crew T-Shirt - Black L',     'CLT-TS-BLK-L',    '8901234568103', '{"Color":"Black","Size":"L"}',    5,  2, NOW(), NOW()),
    ((SELECT id FROM products WHERE sku = 'CLT-MENS-TS'),  'Classic Crew T-Shirt - Blue S',      'CLT-TS-BLU-S',    '8901234568104', '{"Color":"Blue","Size":"S"}',     0,  0, NOW(), NOW()),
    ((SELECT id FROM products WHERE sku = 'CLT-MENS-TS'),  'Classic Crew T-Shirt - Blue M',      'CLT-TS-BLU-M',    '8901234568105', '{"Color":"Blue","Size":"M"}',     0,  0, NOW(), NOW()),
    -- Dress variants
    ((SELECT id FROM products WHERE sku = 'CLT-WMN-DRS'),  'Summer Floral Dress - White S',      'CLT-DRS-WHT-S',   '8901234568201', '{"Color":"White","Size":"S"}',    0,  0, NOW(), NOW()),
    ((SELECT id FROM products WHERE sku = 'CLT-WMN-DRS'),  'Summer Floral Dress - White M',      'CLT-DRS-WHT-M',   '8901234568202', '{"Color":"White","Size":"M"}',    0,  0, NOW(), NOW()),
    ((SELECT id FROM products WHERE sku = 'CLT-WMN-DRS'),  'Summer Floral Dress - Blue S',       'CLT-DRS-BLU-S',   '8901234568203', '{"Color":"Blue","Size":"S"}',     0,  0, NOW(), NOW()),
    ((SELECT id FROM products WHERE sku = 'CLT-WMN-DRS'),  'Summer Floral Dress - Blue M',       'CLT-DRS-BLU-M',   '8901234568204', '{"Color":"Blue","Size":"M"}',     0,  0, NOW(), NOW()),
    -- Coffee (single default variant)
    ((SELECT id FROM products WHERE sku = 'FOOD-COF-1KG'), 'Arabica Coffee Beans 1kg - Default', 'FOOD-COF-1KG-DEF',NULL,            '{}',                              0,  0, NOW(), NOW()),
    -- Earbuds variants
    ((SELECT id FROM products WHERE sku = 'ACC-EAR-TWS'),  'TrueWireless Earbuds - Black',       'ACC-EAR-TWS-BLK', '8901234568301', '{"Color":"Black"}',               0,  0, NOW(), NOW()),
    ((SELECT id FROM products WHERE sku = 'ACC-EAR-TWS'),  'TrueWireless Earbuds - White',       'ACC-EAR-TWS-WHT', '8901234568302', '{"Color":"White"}',               0,  0, NOW(), NOW())
ON CONFLICT (sku) DO NOTHING;

-- =====================================================
-- 11. Insert Product Variant Options (link variants to variation options)
-- =====================================================

INSERT INTO product_variant_options (variant_id, option_id, created_at)
SELECT v.id, o.id, NOW()
FROM product_variants v
JOIN variation_options o ON true
WHERE (v.sku = 'PHN-X12-BLK-128' AND o.name IN ('Black','128GB'))
   OR (v.sku = 'PHN-X12-BLK-256' AND o.name IN ('Black','256GB'))
   OR (v.sku = 'PHN-X12-WHT-128' AND o.name IN ('White','128GB'))
   OR (v.sku = 'PHN-X12-WHT-256' AND o.name IN ('White','256GB'))
   OR (v.sku = 'CLT-TS-BLK-S' AND o.name IN ('Black','S'))
   OR (v.sku = 'CLT-TS-BLK-M' AND o.name IN ('Black','M'))
   OR (v.sku = 'CLT-TS-BLK-L' AND o.name IN ('Black','L'))
   OR (v.sku = 'CLT-TS-BLU-S' AND o.name IN ('Blue','S'))
   OR (v.sku = 'CLT-TS-BLU-M' AND o.name IN ('Blue','M'))
   OR (v.sku = 'CLT-DRS-WHT-S' AND o.name IN ('White','S'))
   OR (v.sku = 'CLT-DRS-WHT-M' AND o.name IN ('White','M'))
   OR (v.sku = 'CLT-DRS-BLU-S' AND o.name IN ('Blue','S'))
   OR (v.sku = 'CLT-DRS-BLU-M' AND o.name IN ('Blue','M'))
   OR (v.sku = 'ACC-EAR-TWS-BLK' AND o.name = 'Black')
   OR (v.sku = 'ACC-EAR-TWS-WHT' AND o.name = 'White')
ON CONFLICT DO NOTHING;

-- =====================================================
-- 12. Insert Inventory
-- =====================================================

INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold)
SELECT v.id,
       (SELECT id FROM outlets WHERE name = 'Main Store'),
       'outlet',
       CASE
           WHEN v.sku LIKE 'LPTP%' THEN 15
           WHEN v.sku LIKE 'PHN%'  THEN 20
           WHEN v.sku LIKE 'ACC-USB%' THEN 100
           WHEN v.sku LIKE 'CLT-TS%'  THEN 30
           WHEN v.sku LIKE 'CLT-DRS%' THEN 20
           WHEN v.sku LIKE 'FOOD%' THEN 60
           WHEN v.sku LIKE 'ACC-EAR%' THEN 25
           ELSE 10
       END,
       CASE
           WHEN v.sku LIKE 'LPTP%' THEN 3
           WHEN v.sku LIKE 'PHN%'  THEN 5
           WHEN v.sku LIKE 'ACC-USB%' THEN 20
           WHEN v.sku LIKE 'CLT%' THEN 5
           WHEN v.sku LIKE 'FOOD%' THEN 10
           WHEN v.sku LIKE 'ACC-EAR%' THEN 5
           ELSE 5
       END
FROM product_variants v
ON CONFLICT (variant_id, location_id, location_type) DO NOTHING;

INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold)
SELECT v.id,
       (SELECT id FROM outlets WHERE name = 'Downtown Branch'),
       'outlet',
       CASE
           WHEN v.sku LIKE 'ACC-USB%' THEN 50
           WHEN v.sku LIKE 'CLT-TS%'  THEN 15
           WHEN v.sku LIKE 'CLT-DRS%' THEN 10
           WHEN v.sku LIKE 'ACC-EAR%' THEN 10
           ELSE 10
       END, 3
FROM product_variants v
WHERE v.sku LIKE 'ACC-USB%' OR v.sku LIKE 'CLT%' OR v.sku LIKE 'ACC-EAR%'
ON CONFLICT (variant_id, location_id, location_type) DO NOTHING;

INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold)
SELECT v.id,
       (SELECT id FROM warehouses WHERE name = 'Central Warehouse'),
       'warehouse',
       CASE
           WHEN v.sku LIKE 'LPTP%' THEN 50
           WHEN v.sku LIKE 'PHN%'  THEN 80
           WHEN v.sku LIKE 'ACC-USB%' THEN 500
           WHEN v.sku LIKE 'CLT-TS%'  THEN 200
           WHEN v.sku LIKE 'CLT-DRS%' THEN 100
           WHEN v.sku LIKE 'FOOD%' THEN 300
           WHEN v.sku LIKE 'ACC-EAR%' THEN 100
           ELSE 50
       END,
       CASE
           WHEN v.sku LIKE 'LPTP%' THEN 10
           WHEN v.sku LIKE 'PHN%'  THEN 20
           WHEN v.sku LIKE 'ACC-USB%' THEN 100
           WHEN v.sku LIKE 'CLT%' THEN 50
           WHEN v.sku LIKE 'FOOD%' THEN 50
           WHEN v.sku LIKE 'ACC-EAR%' THEN 20
           ELSE 10
       END
FROM product_variants v
ON CONFLICT (variant_id, location_id, location_type) DO NOTHING;

-- =====================================================
-- 13. Insert Customers
-- =====================================================

INSERT INTO customers (name, phone, email, loyalty_points, created_at)
VALUES
    ('Alice Johnson', '+1-555-1001', 'alice.johnson@example.com', 250, NOW()),
    ('Bob Williams',  '+1-555-1002', 'bob.williams@example.com',  100, NOW()),
    ('Carol Davis',   '+1-555-1003', 'carol.davis@example.com',   500, NOW()),
    ('David Brown',   '+1-555-1004', 'david.brown@example.com',    75, NOW()),
    ('Emma Wilson',   '+1-555-1005', 'emma.wilson@example.com',   320, NOW())
ON CONFLICT DO NOTHING;

-- =====================================================
-- 14. Insert Accounts
-- =====================================================

INSERT INTO accounts (name, type, balance)
VALUES
    ('Main Store Cash Register', 'asset',     5000),
    ('Business Bank Account',    'asset',   150000),
    ('Accounts Payable',         'liability', 12000),
    ('Accounts Receivable',      'asset',     8500),
    ('General Expenses',         'expense',      0),
    ('Sales Revenue',            'revenue',      0)
ON CONFLICT DO NOTHING;

-- =====================================================
-- 15. Insert Purchase Orders
-- =====================================================

INSERT INTO purchase_orders (supplier_id, warehouse_id, order_date, expected_delivery, total_amount, status, created_by, created_at, updated_at)
VALUES
    (
        (SELECT id FROM suppliers WHERE name = 'TechSupply Co.'),
        (SELECT id FROM warehouses WHERE name = 'Central Warehouse'),
        NOW() - INTERVAL '30 days', NOW() - INTERVAL '20 days',
        18500, 'received',
        (SELECT id FROM users WHERE email = 'suparadmin@sabbir.com'),
        NOW() - INTERVAL '30 days', NOW() - INTERVAL '20 days'
    ),
    (
        (SELECT id FROM suppliers WHERE name = 'Fashion World Distributors'),
        (SELECT id FROM warehouses WHERE name = 'Central Warehouse'),
        NOW() - INTERVAL '15 days', NOW() - INTERVAL '5 days',
        5500, 'received',
        (SELECT id FROM users WHERE email = 'tom.stock@retailpos.com'),
        NOW() - INTERVAL '15 days', NOW() - INTERVAL '5 days'
    ),
    (
        (SELECT id FROM suppliers WHERE name = 'TechSupply Co.'),
        (SELECT id FROM warehouses WHERE name = 'North Warehouse'),
        NOW() - INTERVAL '5 days', NOW() + INTERVAL '5 days',
        10500, 'pending',
        (SELECT id FROM users WHERE email = 'tom.stock@retailpos.com'),
        NOW() - INTERVAL '5 days', NOW() - INTERVAL '5 days'
    )
ON CONFLICT DO NOTHING;

-- =====================================================
-- 16. Insert Purchase Order Items
-- =====================================================

INSERT INTO purchase_order_items (po_id, variant_id, quantity, unit_price)
SELECT po.id, v.id, qty, price
FROM (VALUES
    (1, 'LPTP-001-DEF',    20, 750),
    (1, 'PHN-X12-BLK-128', 30, 500),
    (2, 'CLT-TS-BLK-S',   100,  10),
    (2, 'CLT-TS-BLK-M',   100,  10),
    (2, 'CLT-DRS-WHT-S',   50,  18),
    (3, 'ACC-EAR-TWS-BLK', 150, 35),
    (3, 'ACC-EAR-TWS-WHT', 150, 35)
) AS t(po_seq, variant_sku, qty, price)
JOIN product_variants v ON v.sku = t.variant_sku
JOIN purchase_orders po ON po.id = (
    SELECT id FROM purchase_orders ORDER BY id LIMIT 1 OFFSET (t.po_seq - 1)
)
ON CONFLICT DO NOTHING;

-- =====================================================
-- 17. Insert GRNs (Goods Received Notes)
-- =====================================================

INSERT INTO grns (po_id, received_date, status, created_by, created_at)
SELECT po.id, po.expected_delivery, 'full', po.created_by, po.expected_delivery
FROM purchase_orders po
WHERE po.status = 'received'
ON CONFLICT DO NOTHING;

INSERT INTO grn_items (grn_id, po_item_id, received_qty)
SELECT g.id, poi.id, poi.quantity
FROM grns g
JOIN purchase_order_items poi ON poi.po_id = g.po_id
ON CONFLICT DO NOTHING;

-- =====================================================
-- 18. Insert Sample Sales
-- =====================================================

INSERT INTO sales (outlet_id, customer_id, sale_date, total_amount, discount, tax, payment_method, status, cashier_id, created_at)
VALUES
    (
        (SELECT id FROM outlets WHERE name = 'Main Store'),
        (SELECT id FROM customers WHERE name = 'Alice Johnson'),
        NOW() - INTERVAL '10 days',
        1089.97, 0, 92.65, 'cash', 'completed',
        (SELECT id FROM users WHERE email = 'mike.cashier@retailpos.com'),
        NOW() - INTERVAL '10 days'
    ),
    (
        (SELECT id FROM outlets WHERE name = 'Main Store'),
        (SELECT id FROM customers WHERE name = 'Bob Williams'),
        NOW() - INTERVAL '7 days',
        89.97, 5, 0, 'card', 'completed',
        (SELECT id FROM users WHERE email = 'mike.cashier@retailpos.com'),
        NOW() - INTERVAL '7 days'
    ),
    (
        (SELECT id FROM outlets WHERE name = 'Downtown Branch'),
        (SELECT id FROM customers WHERE name = 'Carol Davis'),
        NOW() - INTERVAL '3 days',
        789.98, 10, 67.15, 'card', 'completed',
        (SELECT id FROM users WHERE email = 'lisa.manager@retailpos.com'),
        NOW() - INTERVAL '3 days'
    )
ON CONFLICT DO NOTHING;

-- =====================================================
-- 19. Insert Sale Items
-- =====================================================

INSERT INTO sale_items (sale_id, variant_id, quantity, unit_price, subtotal)
SELECT s.id, v.id, qty, price, qty * price
FROM (
    SELECT 1 AS sale_seq, 'LPTP-001-DEF'   AS vsku, 1 AS qty, 999.99 AS price UNION ALL
    SELECT 1, 'ACC-USB-C-2M-DEF', 2, 19.99 UNION ALL
    SELECT 1, 'FOOD-COF-1KG-DEF', 2, 24.99 UNION ALL
    SELECT 2, 'CLT-TS-BLK-S',     1, 29.99 UNION ALL
    SELECT 2, 'CLT-TS-BLK-M',     2, 29.99 UNION ALL
    SELECT 3, 'PHN-X12-BLK-128',  1, 699.99 UNION ALL
    SELECT 3, 'ACC-EAR-TWS-BLK',  1, 89.99
) t
JOIN product_variants v ON v.sku = t.vsku
JOIN sales s ON s.id = (
    SELECT id FROM sales ORDER BY id LIMIT 1 OFFSET (t.sale_seq - 1)
)
ON CONFLICT DO NOTHING;

-- =====================================================
-- 20. Insert Stock Adjustments
-- =====================================================

INSERT INTO stock_adjustments (location_id, location_type, variant_id, quantity_change, reason, adjusted_by, adjustment_date)
VALUES
    (
        (SELECT id FROM outlets WHERE name = 'Main Store'),
        'outlet',
        (SELECT id FROM product_variants WHERE sku = 'FOOD-COF-1KG-DEF'),
        -5, 'Damaged goods - water damage',
        (SELECT id FROM users WHERE email = 'sarah.manager@retailpos.com'),
        NOW() - INTERVAL '8 days'
    ),
    (
        (SELECT id FROM warehouses WHERE name = 'Central Warehouse'),
        'warehouse',
        (SELECT id FROM product_variants WHERE sku = 'ACC-USB-C-2M-DEF'),
        50, 'Stock count correction after audit',
        (SELECT id FROM users WHERE email = 'tom.stock@retailpos.com'),
        NOW() - INTERVAL '6 days'
    )
ON CONFLICT DO NOTHING;

-- =====================================================
-- 21. Insert Stock Transfer
-- =====================================================

INSERT INTO stock_transfers (from_location_id, from_location_type, to_location_id, to_location_type, transfer_date, status, approved_by, created_by, created_at)
VALUES
    (
        (SELECT id FROM warehouses WHERE name = 'Central Warehouse'),
        'warehouse',
        (SELECT id FROM outlets WHERE name = 'Downtown Branch'),
        'outlet',
        NOW() - INTERVAL '4 days', 'completed',
        (SELECT id FROM users WHERE email = 'sarah.manager@retailpos.com'),
        (SELECT id FROM users WHERE email = 'tom.stock@retailpos.com'),
        NOW() - INTERVAL '4 days'
    )
ON CONFLICT DO NOTHING;

INSERT INTO stock_transfer_items (transfer_id, variant_id, quantity)
SELECT t.id, v.id, qty
FROM (
    SELECT 'CLT-TS-BLU-S' AS vsku, 20 AS qty UNION ALL
    SELECT 'CLT-DRS-BLU-S', 10
) x
JOIN product_variants v ON v.sku = x.vsku
CROSS JOIN (SELECT id FROM stock_transfers ORDER BY id DESC LIMIT 1) t
ON CONFLICT DO NOTHING;

-- =====================================================
-- 22. Insert Expenses
-- =====================================================

INSERT INTO expenses (category, amount, description, expense_date, outlet_id)
VALUES
    ('Utilities',  850,  'Monthly electricity bill',           NOW() - INTERVAL '5 days',  (SELECT id FROM outlets WHERE name = 'Main Store')),
    ('Rent',       5000, 'Monthly store rent',                  NOW() - INTERVAL '1 day',   (SELECT id FROM outlets WHERE name = 'Main Store')),
    ('Utilities',  620,  'Monthly electricity bill',           NOW() - INTERVAL '5 days',  (SELECT id FROM outlets WHERE name = 'Downtown Branch')),
    ('Marketing',  1200, 'Social media advertising campaign',  NOW() - INTERVAL '12 days', NULL),
    ('Supplies',   300,  'Office supplies and packaging',      NOW() - INTERVAL '8 days',  (SELECT id FROM outlets WHERE name = 'Main Store'))
ON CONFLICT DO NOTHING;

-- =====================================================
-- 23. Insert Bills (supplier payables)
-- =====================================================

INSERT INTO bills (supplier_id, po_id, amount_due, due_date, status)
VALUES
    (
        (SELECT id FROM suppliers WHERE name = 'TechSupply Co.'),
        (SELECT id FROM purchase_orders ORDER BY id LIMIT 1 OFFSET 0),
        18500, NOW() + INTERVAL '30 days', 'paid'
    ),
    (
        (SELECT id FROM suppliers WHERE name = 'Fashion World Distributors'),
        (SELECT id FROM purchase_orders ORDER BY id LIMIT 1 OFFSET 1),
        5500, NOW() + INTERVAL '15 days', 'unpaid'
    ),
    (
        (SELECT id FROM suppliers WHERE name = 'Global Food Imports'),
        NULL, 2400, NOW() + INTERVAL '20 days', 'unpaid'
    )
ON CONFLICT DO NOTHING;

-- =====================================================
-- 24. Insert Transactions (accounting entries)
-- =====================================================

INSERT INTO transactions (account_id, amount, type, description, transaction_date, reference_id, reference_type)
VALUES
    ((SELECT id FROM accounts WHERE name = 'Main Store Cash Register'),  1089.97, 'credit', 'Sale #1 - Cash payment received',              NOW() - INTERVAL '10 days', 1, 'sale'),
    ((SELECT id FROM accounts WHERE name = 'Business Bank Account'),       89.97, 'credit', 'Sale #2 - Card payment received',              NOW() - INTERVAL '7 days',  2, 'sale'),
    ((SELECT id FROM accounts WHERE name = 'Business Bank Account'),      789.98, 'credit', 'Sale #3 - Card payment received',              NOW() - INTERVAL '3 days',  3, 'sale'),
    ((SELECT id FROM accounts WHERE name = 'Business Bank Account'),    18500.00, 'debit',  'Payment to TechSupply Co. for PO#1',           NOW() - INTERVAL '18 days', 1, 'bill'),
    ((SELECT id FROM accounts WHERE name = 'Business Bank Account'),      850.00, 'debit',  'Electricity bill - Main Store',                NOW() - INTERVAL '5 days',  1, 'expense'),
    ((SELECT id FROM accounts WHERE name = 'Business Bank Account'),     5000.00, 'debit',  'Monthly rent - Main Store',                    NOW() - INTERVAL '1 day',   2, 'expense')
ON CONFLICT DO NOTHING;

-- =====================================================
-- Verification Queries
-- =====================================================

SELECT 'Roles:' AS entity, COUNT(*) AS count FROM roles
UNION ALL SELECT 'Users:', COUNT(*) FROM users
UNION ALL SELECT 'Outlets:', COUNT(*) FROM outlets
UNION ALL SELECT 'Warehouses:', COUNT(*) FROM warehouses
UNION ALL SELECT 'Categories:', COUNT(*) FROM categories
UNION ALL SELECT 'Suppliers:', COUNT(*) FROM suppliers
UNION ALL SELECT 'Variations:', COUNT(*) FROM variations
UNION ALL SELECT 'VariationOptions:', COUNT(*) FROM variation_options
UNION ALL SELECT 'Products:', COUNT(*) FROM products
UNION ALL SELECT 'ProductVariants:', COUNT(*) FROM product_variants
UNION ALL SELECT 'Inventories:', COUNT(*) FROM inventories
UNION ALL SELECT 'Customers:', COUNT(*) FROM customers
UNION ALL SELECT 'Accounts:', COUNT(*) FROM accounts
UNION ALL SELECT 'PurchaseOrders:', COUNT(*) FROM purchase_orders
UNION ALL SELECT 'GRNs:', COUNT(*) FROM grns
UNION ALL SELECT 'Sales:', COUNT(*) FROM sales
UNION ALL SELECT 'SaleItems:', COUNT(*) FROM sale_items
UNION ALL SELECT 'StockAdjustments:', COUNT(*) FROM stock_adjustments
UNION ALL SELECT 'StockTransfers:', COUNT(*) FROM stock_transfers
UNION ALL SELECT 'Expenses:', COUNT(*) FROM expenses
UNION ALL SELECT 'Bills:', COUNT(*) FROM bills
UNION ALL SELECT 'Transactions:', COUNT(*) FROM transactions;

-- =====================================================
-- SUCCESS MESSAGE
-- =====================================================

DO $$
BEGIN
    RAISE NOTICE '========================================';
    RAISE NOTICE 'Comprehensive Seed Data Inserted!';
    RAISE NOTICE '========================================';
    RAISE NOTICE '';
    RAISE NOTICE 'Default Login Credentials (all use password: Admin@123):';
    RAISE NOTICE '  Super Admin:   suparadmin@sabbir.com';
    RAISE NOTICE '  Admin:         john.admin@retailpos.com';
    RAISE NOTICE '  Manager:       sarah.manager@retailpos.com';
    RAISE NOTICE '  Cashier:       mike.cashier@retailpos.com';
    RAISE NOTICE '  Branch Mgr:    lisa.manager@retailpos.com';
    RAISE NOTICE '  Stock Mgr:     tom.stock@retailpos.com';
    RAISE NOTICE '';
    RAISE NOTICE 'WARNING: Change all default passwords after first login!';
    RAISE NOTICE '========================================';
END $$;

    
