-- Idempotent Stock Count permission seeding for existing databases
-- Run after deploying Stock Count feature to ensure required permissions are assigned.

BEGIN;

-- Full stock count lifecycle permissions for business-level roles.
UPDATE roles
SET permissions = permissions
    || '["StockCount.ViewOwn","StockCount.ViewAll","StockCount.Create","StockCount.Download","StockCount.Print","StockCount.Upload","StockCount.Submit","StockCount.Approve","StockCount.Reject","StockCount.Reopen"]'::jsonb,
    updated_at = NOW()
WHERE name IN ('BusinessOwner', 'BusinessAdmin', 'Admin')
  AND NOT (permissions @> '["StockCount.ViewOwn"]'::jsonb);

-- Location-scoped stock count permissions for operational roles.
UPDATE roles
SET permissions = permissions
    || '["StockCount.ViewOwn","StockCount.Create","StockCount.Download","StockCount.Print","StockCount.Upload","StockCount.Submit","StockCount.Reject","StockCount.Reopen"]'::jsonb,
    updated_at = NOW()
WHERE name IN ('OutletManager', 'WarehouseManager', 'Manager', 'Stock Manager')
  AND NOT (permissions @> '["StockCount.ViewOwn"]'::jsonb);

COMMIT;

-- Verification helper.
SELECT
    name,
    permissions @> '["StockCount.ViewOwn"]'::jsonb AS has_view_own,
    permissions @> '["StockCount.ViewAll"]'::jsonb AS has_view_all,
    permissions @> '["StockCount.Create"]'::jsonb AS has_create,
    permissions @> '["StockCount.Download"]'::jsonb AS has_download,
    permissions @> '["StockCount.Print"]'::jsonb AS has_print,
    permissions @> '["StockCount.Upload"]'::jsonb AS has_upload,
    permissions @> '["StockCount.Submit"]'::jsonb AS has_submit,
    permissions @> '["StockCount.Approve"]'::jsonb AS has_approve,
    permissions @> '["StockCount.Reject"]'::jsonb AS has_reject,
    permissions @> '["StockCount.Reopen"]'::jsonb AS has_reopen
FROM roles
WHERE name IN ('Admin', 'BusinessOwner', 'BusinessAdmin', 'OutletManager', 'WarehouseManager', 'Manager', 'Stock Manager')
ORDER BY name;
