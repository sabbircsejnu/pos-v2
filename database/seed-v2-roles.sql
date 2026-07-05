-- =============================================================
-- POS v2 Role Seeder
-- Run this against the live database to add the canonical v2 roles.
-- Safe to run multiple times (ON CONFLICT DO NOTHING).
-- =============================================================

INSERT INTO roles (name, permissions, created_at, updated_at)
VALUES
    -- -------------------------------------------------------
    -- BusinessOwner
    -- Full control over own business: all modules
    -- -------------------------------------------------------
    ('BusinessOwner', '[
        "users.view", "users.create", "users.edit", "users.delete",
        "roles.view",
        "products.view", "products.create", "products.edit", "products.delete",
        "products.view_cost",
        "barcode.view", "barcode.print", "barcode.bulk_print", "barcode.template_manage",
        "categories.view", "categories.create", "categories.edit", "categories.delete",
        "inventory.view", "inventory.create", "inventory.edit", "inventory.delete",
        "stock_adjustments.view", "stock_adjustments.create", "stock_adjustments.edit", "stock_adjustments.delete", "stock_adjustments.approve", "stock_adjustments.cancel",
        "StockCount.ViewOwn", "StockCount.ViewAll", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Approve", "StockCount.Reject", "StockCount.Reopen",
        "stock_transfers.view", "stock_transfers.create", "stock_transfers.edit", "stock_transfers.delete", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "low_stock_alerts.view", "low_stock_alerts.create", "low_stock_alerts.edit", "low_stock_alerts.delete",
        "sales.view", "sales.create",
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

    -- -------------------------------------------------------
    -- BusinessAdmin
    -- Similar to BusinessOwner but cannot delete users or roles
    -- -------------------------------------------------------
    ('BusinessAdmin', '[
        "users.view", "users.create", "users.edit",
        "roles.view",
        "products.view", "products.create", "products.edit", "products.delete",
        "products.view_cost",
        "barcode.view", "barcode.print", "barcode.bulk_print", "barcode.template_manage",
        "categories.view", "categories.create", "categories.edit", "categories.delete",
        "inventory.view", "inventory.create", "inventory.edit", "inventory.delete",
        "stock_adjustments.view", "stock_adjustments.create", "stock_adjustments.edit", "stock_adjustments.delete", "stock_adjustments.approve", "stock_adjustments.cancel",
        "StockCount.ViewOwn", "StockCount.ViewAll", "StockCount.Create", "StockCount.Download", "StockCount.Print", "StockCount.Upload", "StockCount.Submit", "StockCount.Approve", "StockCount.Reject", "StockCount.Reopen",
        "stock_transfers.view", "stock_transfers.create", "stock_transfers.edit", "stock_transfers.delete", "stock_transfers.approve", "stock_transfers.cancel", "stock_transfers.dispatch", "stock_transfers.receive", "stock_transfers.reject_receive", "stock_transfers.return_create", "stock_transfers.transfer_from_any_location",
        "stock_requisitions.view", "stock_requisitions.create", "stock_requisitions.edit", "stock_requisitions.approve", "stock_requisitions.reject", "stock_requisitions.convert_to_transfer",
        "low_stock_alerts.view", "low_stock_alerts.create", "low_stock_alerts.edit", "low_stock_alerts.delete",
        "sales.view", "sales.create",
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

    -- -------------------------------------------------------
    -- AccountsAdmin
    -- Business-level financial data access only
    -- -------------------------------------------------------
    ('AccountsAdmin', '[
        "accounts.view", "accounts.create", "accounts.edit",
        "transactions.view", "transactions.create",
        "sales.view",
        "purchases.view",
        "suppliers.view",
        "customers.view",
        "products.view", "products.view_cost",
        "reports.view", "reports.export"
    ]'::jsonb, NOW(), NOW()),

    -- -------------------------------------------------------
    -- OutletManager
    -- Assigned to one outlet; manages that outlet operations
    -- -------------------------------------------------------
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
        "sales.view", "sales.create",
        "customers.view", "customers.create", "customers.edit",
        "suppliers.view",
        "outlets.view",
        "reports.view"
    ]'::jsonb, NOW(), NOW()),

    -- -------------------------------------------------------
    -- SalesPerson
    -- Assigned to one outlet; sales operations only
    -- -------------------------------------------------------
    ('SalesPerson', '[
        "products.view",
        "barcode.view", "barcode.print",
        "inventory.view",
        "stock_transfers.view",
        "low_stock_alerts.view",
        "sales.view", "sales.create",
        "customers.view", "customers.create"
    ]'::jsonb, NOW(), NOW()),

    -- -------------------------------------------------------
    -- WarehouseManager
    -- Assigned to one or more warehouses; warehouse operations
    -- -------------------------------------------------------
    ('WarehouseManager', '[
        "products.view", "products.view_cost",
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

ON CONFLICT (name) DO NOTHING;

-- Verify
SELECT id, name FROM roles ORDER BY id;
