BEGIN;
DO $$
DECLARE cnt INT;
BEGIN
  SELECT COUNT(*) INTO cnt FROM sales WHERE cashier_id = 67;
  IF cnt > 0 THEN RAISE EXCEPTION 'User 67 has % sales records', cnt; END IF;
  SELECT COUNT(*) INTO cnt FROM stock_adjustments WHERE adjusted_by = 67;
  IF cnt > 0 THEN RAISE EXCEPTION 'User 67 has % stock adjustment records', cnt; END IF;
  RAISE NOTICE 'Safety check passed for user 67.';
END $$;
DELETE FROM users WHERE id = 67;
COMMIT;
SELECT 'user_67_deleted' AS result, NOT EXISTS(SELECT 1 FROM users WHERE id = 67) AS success;
