-- ================================================================
-- Migration: Add products.view_cost permission to roles
-- Date: 2026-06-13
-- Run once against the live database.
-- Updates existing roles to include the new products.view_cost
-- permission where appropriate.
-- ================================================================

-- BusinessOwner: sees everything
UPDATE roles
SET permissions = permissions || '["products.view_cost"]'::jsonb,
    updated_at  = NOW()
WHERE name = 'BusinessOwner'
  AND NOT (permissions @> '["products.view_cost"]'::jsonb);

-- BusinessAdmin: same access level as BusinessOwner for cost data
UPDATE roles
SET permissions = permissions || '["products.view_cost"]'::jsonb,
    updated_at  = NOW()
WHERE name = 'BusinessAdmin'
  AND NOT (permissions @> '["products.view_cost"]'::jsonb);

-- AccountsAdmin: needs cost for financial reporting
UPDATE roles
SET permissions = permissions || '["products.view_cost"]'::jsonb,
    updated_at  = NOW()
WHERE name = 'AccountsAdmin'
  AND NOT (permissions @> '["products.view_cost"]'::jsonb);

-- WarehouseManager: needs cost for stock valuation and GRN processing
UPDATE roles
SET permissions = permissions || '["products.view_cost"]'::jsonb,
    updated_at  = NOW()
WHERE name = 'WarehouseManager'
  AND NOT (permissions @> '["products.view_cost"]'::jsonb);

-- OutletManager and SalesPerson do NOT receive products.view_cost.
-- (No update needed for those roles.)

-- SuperAdmin has wildcard '*' so no update needed.

-- Verify result
SELECT name,
       permissions @> '["products.view_cost"]'::jsonb AS has_view_cost
FROM roles
ORDER BY name;
