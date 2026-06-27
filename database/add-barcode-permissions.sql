-- =============================================================
-- Migration: Add barcode permissions to roles
-- Safe to run multiple times. Appends only missing permissions.
-- =============================================================

-- Admin-level roles: full barcode access including template management
UPDATE roles
SET permissions = permissions
    || '["barcode.view", "barcode.print", "barcode.bulk_print", "barcode.template_manage"]'::jsonb,
    updated_at = NOW()
WHERE name IN ('Admin', 'BusinessOwner', 'BusinessAdmin')
  AND NOT (
      permissions @> '["barcode.view"]'::jsonb
  AND permissions @> '["barcode.print"]'::jsonb
  AND permissions @> '["barcode.bulk_print"]'::jsonb
  AND permissions @> '["barcode.template_manage"]'::jsonb
  );

-- Operational manager roles: view/print/bulk print
UPDATE roles
SET permissions = permissions
    || '["barcode.view", "barcode.print", "barcode.bulk_print"]'::jsonb,
    updated_at = NOW()
WHERE name IN ('Manager', 'OutletManager', 'Stock Manager', 'WarehouseManager')
  AND NOT (
      permissions @> '["barcode.view"]'::jsonb
  AND permissions @> '["barcode.print"]'::jsonb
  AND permissions @> '["barcode.bulk_print"]'::jsonb
  );

-- Sales roles: view + single print
UPDATE roles
SET permissions = permissions
    || '["barcode.view", "barcode.print"]'::jsonb,
    updated_at = NOW()
WHERE name IN ('Cashier', 'SalesPerson')
  AND NOT (
      permissions @> '["barcode.view"]'::jsonb
  AND permissions @> '["barcode.print"]'::jsonb
  );

-- Read-only user role: view only
UPDATE roles
SET permissions = permissions
    || '["barcode.view"]'::jsonb,
    updated_at = NOW()
WHERE name = 'User'
  AND NOT (permissions @> '["barcode.view"]'::jsonb);

-- Verification
SELECT
    id,
    name,
    permissions @> '["barcode.view"]'::jsonb AS has_barcode_view,
    permissions @> '["barcode.print"]'::jsonb AS has_barcode_print,
    permissions @> '["barcode.bulk_print"]'::jsonb AS has_barcode_bulk_print,
    permissions @> '["barcode.template_manage"]'::jsonb AS has_barcode_template_manage
FROM roles
WHERE name IN (
    'Admin', 'Manager', 'Cashier', 'Stock Manager', 'User',
    'BusinessOwner', 'BusinessAdmin', 'OutletManager', 'SalesPerson', 'WarehouseManager'
)
ORDER BY id;
