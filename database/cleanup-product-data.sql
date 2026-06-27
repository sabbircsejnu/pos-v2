-- ============================================================
-- RetailPOS v2 — Product Data Cleanup Script
-- Clears all dummy/migrated product data and related transactions.
--
-- KEEPS: businesses, roles, users, outlets, warehouses,
--        categories, suppliers, customers, accounts, expenses,
--        audit data, system settings.
--
-- DELETES (child → parent order):
--   grn_items, grns,
--   purchase_order_items, bills, purchase_orders,
--   transactions,
--   sale_payments, sale_items, held_sales, sales,
--   stock_transfer_items, stock_transfers,
--   stock_adjustments, stock_ledgers, inventories,
--   outlet_price_overrides, price_rules,
--   product_images,
--   product_variant_options,
--   product_variation_selected_options,
--   product_variations,
--   product_variants, products,
--   variation_options, variations
-- ============================================================

-- ============================================================
-- SECTION 1: Record Counts BEFORE Delete
-- ============================================================
\echo '=== RECORD COUNTS BEFORE CLEANUP ==='

SELECT
    'grn_items'                          AS table_name, COUNT(*) AS rows FROM grn_items
UNION ALL SELECT 'grns',                                COUNT(*) FROM grns
UNION ALL SELECT 'purchase_order_items',                COUNT(*) FROM purchase_order_items
UNION ALL SELECT 'bills',                               COUNT(*) FROM bills
UNION ALL SELECT 'purchase_orders',                     COUNT(*) FROM purchase_orders
UNION ALL SELECT 'transactions',                        COUNT(*) FROM transactions
UNION ALL SELECT 'sale_payments',                       COUNT(*) FROM sale_payments
UNION ALL SELECT 'sale_items',                          COUNT(*) FROM sale_items
UNION ALL SELECT 'held_sales',                          COUNT(*) FROM held_sales
UNION ALL SELECT 'sales',                               COUNT(*) FROM sales
UNION ALL SELECT 'stock_transfer_items',                COUNT(*) FROM stock_transfer_items
UNION ALL SELECT 'stock_transfers',                     COUNT(*) FROM stock_transfers
UNION ALL SELECT 'stock_adjustments',                   COUNT(*) FROM stock_adjustments
UNION ALL SELECT 'stock_ledgers',                       COUNT(*) FROM stock_ledgers
UNION ALL SELECT 'inventories',                         COUNT(*) FROM inventories
UNION ALL SELECT 'outlet_price_overrides',              COUNT(*) FROM outlet_price_overrides
UNION ALL SELECT 'price_rules',                         COUNT(*) FROM price_rules
UNION ALL SELECT 'product_images',                      COUNT(*) FROM product_images
UNION ALL SELECT 'product_variant_options',             COUNT(*) FROM product_variant_options
UNION ALL SELECT 'product_variation_selected_options',  COUNT(*) FROM product_variation_selected_options
UNION ALL SELECT 'product_variations',                  COUNT(*) FROM product_variations
UNION ALL SELECT 'product_variants',                    COUNT(*) FROM product_variants
UNION ALL SELECT 'products',                            COUNT(*) FROM products
UNION ALL SELECT 'variation_options',                   COUNT(*) FROM variation_options
UNION ALL SELECT 'variations',                          COUNT(*) FROM variations
ORDER BY table_name;

\echo ''
\echo '=== KEPT TABLES (should remain untouched) ==='

SELECT
    'users'      AS table_name, COUNT(*) AS rows FROM users
UNION ALL SELECT 'roles',       COUNT(*) FROM roles
UNION ALL SELECT 'outlets',     COUNT(*) FROM outlets
UNION ALL SELECT 'warehouses',  COUNT(*) FROM warehouses
UNION ALL SELECT 'categories',  COUNT(*) FROM categories
UNION ALL SELECT 'suppliers',   COUNT(*) FROM suppliers
UNION ALL SELECT 'customers',   COUNT(*) FROM customers
UNION ALL SELECT 'accounts',    COUNT(*) FROM accounts
UNION ALL SELECT 'expenses',    COUNT(*) FROM expenses
ORDER BY table_name;

-- ============================================================
-- SECTION 2: Transactional Cleanup (children first)
-- ============================================================
\echo ''
\echo '=== STARTING TRANSACTIONAL CLEANUP ==='

BEGIN;

-- --- GRN chain ---
DELETE FROM grn_items;
DELETE FROM grns;

-- --- Purchase chain ---
DELETE FROM purchase_order_items;
DELETE FROM bills;               -- bills.po_id -> purchase_orders (SET NULL), bills.supplier_id -> suppliers (RESTRICT, but we keep suppliers)
DELETE FROM purchase_orders;

-- --- Accounting entries generated from product transactions ---
DELETE FROM transactions;

-- --- Sales chain ---
DELETE FROM sale_payments;       -- sale_payments.sale_id -> sales (CASCADE)
DELETE FROM sale_items;          -- sale_items.sale_id -> sales (CASCADE), .variant_id -> product_variants (RESTRICT)
DELETE FROM held_sales;          -- held POS carts (items stored as JSONB)
DELETE FROM sales;

-- --- Stock movement ---
DELETE FROM stock_transfer_items; -- .transfer_id -> stock_transfers (CASCADE), .variant_id -> product_variants (RESTRICT)
DELETE FROM stock_transfers;
DELETE FROM stock_adjustments;    -- .variant_id -> product_variants (RESTRICT)
DELETE FROM stock_ledgers;        -- .variant_id -> product_variants

-- --- Inventory balances ---
DELETE FROM inventories;          -- .variant_id -> product_variants (RESTRICT)

-- --- Pricing engine ---
DELETE FROM outlet_price_overrides; -- .product_variant_id -> product_variants (CASCADE)
DELETE FROM price_rules;

-- --- Product structure (deepest children first) ---
DELETE FROM product_images;                     -- .product_id -> products (CASCADE)
DELETE FROM product_variant_options;            -- .variant_id -> product_variants (CASCADE), .option_id -> variation_options (CASCADE)
DELETE FROM product_variation_selected_options; -- .product_id -> products (CASCADE)
DELETE FROM product_variations;                 -- .product_id -> products (CASCADE), .variation_id -> variations (CASCADE)
DELETE FROM product_variants;                   -- .product_id -> products (CASCADE)
DELETE FROM products;

-- --- Variation master (attribute types like "Color", "Size" and their values) ---
DELETE FROM variation_options;    -- .variation_id -> variations (CASCADE)
DELETE FROM variations;

-- ============================================================
-- SECTION 3: In-transaction verification
-- ============================================================
DO $$
DECLARE
    total_remaining INTEGER := 0;
BEGIN
    SELECT
        (SELECT COUNT(*) FROM grn_items) +
        (SELECT COUNT(*) FROM grns) +
        (SELECT COUNT(*) FROM purchase_order_items) +
        (SELECT COUNT(*) FROM bills) +
        (SELECT COUNT(*) FROM purchase_orders) +
        (SELECT COUNT(*) FROM transactions) +
        (SELECT COUNT(*) FROM sale_payments) +
        (SELECT COUNT(*) FROM sale_items) +
        (SELECT COUNT(*) FROM held_sales) +
        (SELECT COUNT(*) FROM sales) +
        (SELECT COUNT(*) FROM stock_transfer_items) +
        (SELECT COUNT(*) FROM stock_transfers) +
        (SELECT COUNT(*) FROM stock_adjustments) +
        (SELECT COUNT(*) FROM stock_ledgers) +
        (SELECT COUNT(*) FROM inventories) +
        (SELECT COUNT(*) FROM outlet_price_overrides) +
        (SELECT COUNT(*) FROM price_rules) +
        (SELECT COUNT(*) FROM product_images) +
        (SELECT COUNT(*) FROM product_variant_options) +
        (SELECT COUNT(*) FROM product_variation_selected_options) +
        (SELECT COUNT(*) FROM product_variations) +
        (SELECT COUNT(*) FROM product_variants) +
        (SELECT COUNT(*) FROM products) +
        (SELECT COUNT(*) FROM variation_options) +
        (SELECT COUNT(*) FROM variations)
    INTO total_remaining;

    IF total_remaining = 0 THEN
        RAISE NOTICE 'VERIFY OK: All % product-related rows deleted. Committing.', total_remaining;
    ELSE
        RAISE EXCEPTION 'VERIFY FAILED: % rows still remain in product tables. Rolling back.', total_remaining;
    END IF;
END $$;

COMMIT;

\echo '=== CLEANUP COMMITTED ==='

-- ============================================================
-- SECTION 4: Reset identity sequences
-- ============================================================
\echo ''
\echo '=== RESETTING SEQUENCES ==='

ALTER SEQUENCE IF EXISTS grn_items_id_seq                          RESTART WITH 1;
ALTER SEQUENCE IF EXISTS grns_id_seq                               RESTART WITH 1;
ALTER SEQUENCE IF EXISTS purchase_order_items_id_seq               RESTART WITH 1;
ALTER SEQUENCE IF EXISTS bills_id_seq                              RESTART WITH 1;
ALTER SEQUENCE IF EXISTS purchase_orders_id_seq                    RESTART WITH 1;
ALTER SEQUENCE IF EXISTS transactions_id_seq                       RESTART WITH 1;
ALTER SEQUENCE IF EXISTS sale_payments_id_seq                      RESTART WITH 1;
ALTER SEQUENCE IF EXISTS sale_items_id_seq                         RESTART WITH 1;
ALTER SEQUENCE IF EXISTS held_sales_id_seq                         RESTART WITH 1;
ALTER SEQUENCE IF EXISTS sales_id_seq                              RESTART WITH 1;
ALTER SEQUENCE IF EXISTS stock_transfer_items_id_seq               RESTART WITH 1;
ALTER SEQUENCE IF EXISTS stock_transfers_id_seq                    RESTART WITH 1;
ALTER SEQUENCE IF EXISTS stock_adjustments_id_seq                  RESTART WITH 1;
ALTER SEQUENCE IF EXISTS stock_ledgers_id_seq                      RESTART WITH 1;
ALTER SEQUENCE IF EXISTS inventories_id_seq                        RESTART WITH 1;
ALTER SEQUENCE IF EXISTS outlet_price_overrides_id_seq             RESTART WITH 1;
ALTER SEQUENCE IF EXISTS price_rules_id_seq                        RESTART WITH 1;
ALTER SEQUENCE IF EXISTS product_images_id_seq                     RESTART WITH 1;
ALTER SEQUENCE IF EXISTS product_variant_options_id_seq            RESTART WITH 1;
ALTER SEQUENCE IF EXISTS product_variation_selected_options_id_seq RESTART WITH 1;
ALTER SEQUENCE IF EXISTS product_variations_id_seq                 RESTART WITH 1;
ALTER SEQUENCE IF EXISTS product_variants_id_seq                   RESTART WITH 1;
ALTER SEQUENCE IF EXISTS products_id_seq                           RESTART WITH 1;
ALTER SEQUENCE IF EXISTS variation_options_id_seq                  RESTART WITH 1;
ALTER SEQUENCE IF EXISTS variations_id_seq                         RESTART WITH 1;

\echo 'Sequences reset to 1.'

-- ============================================================
-- SECTION 5: Post-cleanup verification
-- ============================================================
\echo ''
\echo '=== RECORD COUNTS AFTER CLEANUP (all should be 0) ==='

SELECT
    'grn_items'                          AS table_name, COUNT(*) AS rows FROM grn_items
UNION ALL SELECT 'grns',                                COUNT(*) FROM grns
UNION ALL SELECT 'purchase_order_items',                COUNT(*) FROM purchase_order_items
UNION ALL SELECT 'bills',                               COUNT(*) FROM bills
UNION ALL SELECT 'purchase_orders',                     COUNT(*) FROM purchase_orders
UNION ALL SELECT 'transactions',                        COUNT(*) FROM transactions
UNION ALL SELECT 'sale_payments',                       COUNT(*) FROM sale_payments
UNION ALL SELECT 'sale_items',                          COUNT(*) FROM sale_items
UNION ALL SELECT 'held_sales',                          COUNT(*) FROM held_sales
UNION ALL SELECT 'sales',                               COUNT(*) FROM sales
UNION ALL SELECT 'stock_transfer_items',                COUNT(*) FROM stock_transfer_items
UNION ALL SELECT 'stock_transfers',                     COUNT(*) FROM stock_transfers
UNION ALL SELECT 'stock_adjustments',                   COUNT(*) FROM stock_adjustments
UNION ALL SELECT 'stock_ledgers',                       COUNT(*) FROM stock_ledgers
UNION ALL SELECT 'inventories',                         COUNT(*) FROM inventories
UNION ALL SELECT 'outlet_price_overrides',              COUNT(*) FROM outlet_price_overrides
UNION ALL SELECT 'price_rules',                         COUNT(*) FROM price_rules
UNION ALL SELECT 'product_images',                      COUNT(*) FROM product_images
UNION ALL SELECT 'product_variant_options',             COUNT(*) FROM product_variant_options
UNION ALL SELECT 'product_variation_selected_options',  COUNT(*) FROM product_variation_selected_options
UNION ALL SELECT 'product_variations',                  COUNT(*) FROM product_variations
UNION ALL SELECT 'product_variants',                    COUNT(*) FROM product_variants
UNION ALL SELECT 'products',                            COUNT(*) FROM products
UNION ALL SELECT 'variation_options',                   COUNT(*) FROM variation_options
UNION ALL SELECT 'variations',                          COUNT(*) FROM variations
ORDER BY table_name;

\echo ''
\echo '=== KEPT TABLES (confirm counts unchanged) ==='

SELECT
    'users'      AS table_name, COUNT(*) AS rows FROM users
UNION ALL SELECT 'roles',       COUNT(*) FROM roles
UNION ALL SELECT 'outlets',     COUNT(*) FROM outlets
UNION ALL SELECT 'warehouses',  COUNT(*) FROM warehouses
UNION ALL SELECT 'categories',  COUNT(*) FROM categories
UNION ALL SELECT 'suppliers',   COUNT(*) FROM suppliers
UNION ALL SELECT 'customers',   COUNT(*) FROM customers
UNION ALL SELECT 'accounts',    COUNT(*) FROM accounts
UNION ALL SELECT 'expenses',    COUNT(*) FROM expenses
ORDER BY table_name;

\echo ''
\echo '=== CLEANUP COMPLETE ==='
