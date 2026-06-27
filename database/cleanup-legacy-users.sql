-- ============================================================
-- Legacy User Cleanup Script
-- Generated: 2026-06-07
-- Purpose: Remove 6 orphaned v1 seed users (no BusinessId, 
--          not SuperAdmin) and 4 obsolete v1 roles.
-- Backup: database/backup_before_cleanup_*.sql
-- ============================================================

BEGIN;

-- SAFETY CHECK: Abort if any orphaned user has transactional data
DO $$
DECLARE
  sale_count INT;
  adj_count  INT;
  held_count INT;
BEGIN
  SELECT COUNT(*) INTO sale_count FROM sales WHERE cashier_id IN (2,3,4,5,6,7);
  SELECT COUNT(*) INTO adj_count  FROM stock_adjustments WHERE adjusted_by IN (2,3,4,5,6,7);
  SELECT COUNT(*) INTO held_count FROM held_sales WHERE cashier_id IN (2,3,4,5,6,7);

  IF sale_count > 0 OR adj_count > 0 OR held_count > 0 THEN
    RAISE EXCEPTION 'ABORT: Orphaned users have transactional data (sales=%, adjustments=%, held=%). Manual review required.', sale_count, adj_count, held_count;
  END IF;

  RAISE NOTICE 'Safety check passed: no transactional data linked to orphaned users.';
END $$;

-- STEP 1: Null out outlet/warehouse manager references
UPDATE outlets SET manager_id = NULL WHERE manager_id IN (2,3,4,5,6,7);
UPDATE warehouses SET manager_id = NULL WHERE manager_id IN (2,3,4,5,6,7);

-- STEP 2: Delete orphaned users
-- CASCADE removes: user_refresh_tokens, user_invitations, user_warehouse_assignments
-- SET NULL on:     audit_logs, grns, purchase_orders, stock_ledgers, stock_transfers
DELETE FROM users WHERE id IN (2,3,4,5,6,7);

-- STEP 3: Delete orphaned v1 legacy roles (no users remain on them)
DELETE FROM roles
WHERE name IN ('Admin', 'Manager', 'Cashier', 'Stock Manager')
  AND NOT EXISTS (
    SELECT 1 FROM users WHERE role_id = roles.id
  );

-- STEP 4: Verification
DO $$
DECLARE
  remaining_users INT;
  remaining_roles INT;
BEGIN
  SELECT COUNT(*) INTO remaining_users FROM users WHERE id IN (2,3,4,5,6,7);
  SELECT COUNT(*) INTO remaining_roles FROM roles WHERE name IN ('Admin','Manager','Cashier','Stock Manager');

  IF remaining_users > 0 THEN
    RAISE EXCEPTION 'ABORT: % user(s) still exist. Rolling back.', remaining_users;
  END IF;

  RAISE NOTICE 'Cleanup complete: 6 orphaned users deleted, % v1 roles deleted.', (4 - remaining_roles);
END $$;

COMMIT;
