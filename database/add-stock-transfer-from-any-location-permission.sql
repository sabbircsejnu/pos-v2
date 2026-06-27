-- Migration: Add stock_transfers.transfer_from_any_location permission to selected roles
-- Safe to run multiple times. Appends only if missing.

UPDATE roles
SET permissions = permissions || '["stock_transfers.transfer_from_any_location"]'::jsonb,
    updated_at = NOW()
WHERE name IN ('Admin', 'BusinessOwner', 'BusinessAdmin', 'Stock Manager', 'WarehouseManager')
  AND NOT (permissions @> '["stock_transfers.transfer_from_any_location"]'::jsonb);

-- Verify
SELECT
  name,
  permissions @> '["stock_transfers.transfer_from_any_location"]'::jsonb AS has_transfer_from_any_location
FROM roles
WHERE name IN ('Admin', 'BusinessOwner', 'BusinessAdmin', 'Stock Manager', 'WarehouseManager')
ORDER BY name;
