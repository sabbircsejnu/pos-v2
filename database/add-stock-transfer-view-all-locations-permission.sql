-- Migration: Add stock_transfers.view_all_locations to default cross-location transfer roles.
-- Safe to run multiple times. Appends only when missing.

UPDATE roles
SET permissions = permissions || '["stock_transfers.view_all_locations"]'::jsonb,
    updated_at = NOW()
WHERE name IN ('BusinessOwner', 'WarehouseManager', 'Stock Manager')
  AND NOT (permissions @> '["stock_transfers.view_all_locations"]'::jsonb);

-- Super Admin typically uses wildcard (*), but include explicit check for custom installs.
UPDATE roles
SET permissions = permissions || '["stock_transfers.view_all_locations"]'::jsonb,
    updated_at = NOW()
WHERE name = 'Super Admin'
  AND permissions <> '["*"]'::jsonb
  AND NOT (permissions @> '["stock_transfers.view_all_locations"]'::jsonb);

-- Verify
SELECT
  name,
  permissions @> '["stock_transfers.view_all_locations"]'::jsonb AS has_view_all_locations
FROM roles
WHERE name IN ('Super Admin', 'BusinessOwner', 'WarehouseManager', 'Stock Manager')
ORDER BY name;
