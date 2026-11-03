namespace RetailPOS.Infrastructure.Data.Migrations
{
    // Hand-written parts of PosV2P3CashSessionManagement. They live in their own file so regenerating the scaffolded
    // migration never loses them.
    public partial class PosV2P3CashSessionManagement
    {
        // The legacy column held UTC-midnight instants; read them as UTC so neither the host nor the session time zone
        // can shift the calendar date. Status is normalised first so the new CHECK constraint holds.
        private const string ConvertBusinessDateSql = @"
UPDATE cash_sessions SET status = lower(btrim(status)) WHERE status <> lower(btrim(status));
ALTER TABLE cash_sessions ALTER COLUMN business_date TYPE date USING (business_date AT TIME ZONE 'UTC')::date;";

        private const string RevertBusinessDateSql = @"
ALTER TABLE cash_sessions ALTER COLUMN business_date TYPE timestamp with time zone USING (business_date::timestamp AT TIME ZONE 'UTC');";

        // Sessions opened before Phase 3 carry the zone and duration that applied to them (outlet override, else
        // Business setting, else the platform defaults); closed rows keep their historical meaning.
        private const string BackfillSessionSnapshotsSql = @"
UPDATE cash_sessions cs
SET time_zone_id_snapshot = COALESCE(
        NULLIF(btrim((SELECT o.time_zone_id FROM outlets o WHERE o.id = cs.outlet_id)), ''),
        (SELECT s.default_time_zone_id FROM business_pos_settings s WHERE s.business_id = cs.business_id),
        'Asia/Dhaka'),
    duration_snapshot_hours = COALESCE(
        (SELECT s.max_cash_session_duration_hours FROM business_pos_settings s WHERE s.business_id = cs.business_id),
        20);";

        // The triggers are the database-level backstop for the application lock contract: a sale or cash movement can
        // never be inserted into a session that is not open, and cash facts/snapshots are append-only.
        private const string CashFactGuardTriggersSql = @"
CREATE OR REPLACE FUNCTION fn_cash_session_require_open() RETURNS trigger AS $$
DECLARE
    session_status text;
BEGIN
    IF NEW.cash_session_id IS NULL THEN
        RETURN NEW;
    END IF;
    SELECT status INTO session_status FROM cash_sessions WHERE id = NEW.cash_session_id FOR SHARE;
    IF session_status IS NULL OR session_status <> 'open' THEN
        RAISE EXCEPTION 'CASH_SESSION_CLOSED' USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_sales_require_open_cash_session
    BEFORE INSERT ON sales
    FOR EACH ROW EXECUTE FUNCTION fn_cash_session_require_open();

CREATE TRIGGER trg_cash_session_movements_require_open
    BEFORE INSERT ON cash_session_movements
    FOR EACH ROW EXECUTE FUNCTION fn_cash_session_require_open();

CREATE OR REPLACE FUNCTION fn_cash_session_fact_immutable() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'CASH_SESSION_FACT_IMMUTABLE: % on % is not permitted', TG_OP, TG_TABLE_NAME USING ERRCODE = 'P0001';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_cash_session_movements_append_only
    BEFORE UPDATE OR DELETE ON cash_session_movements
    FOR EACH ROW EXECUTE FUNCTION fn_cash_session_fact_immutable();

CREATE TRIGGER trg_cash_session_close_snapshots_immutable
    BEFORE UPDATE OR DELETE ON cash_session_close_snapshots
    FOR EACH ROW EXECUTE FUNCTION fn_cash_session_fact_immutable();";

        private const string DropCashFactGuardTriggersSql = @"
DROP TRIGGER IF EXISTS trg_cash_session_close_snapshots_immutable ON cash_session_close_snapshots;
DROP TRIGGER IF EXISTS trg_cash_session_movements_append_only ON cash_session_movements;
DROP TRIGGER IF EXISTS trg_cash_session_movements_require_open ON cash_session_movements;
DROP TRIGGER IF EXISTS trg_sales_require_open_cash_session ON sales;
DROP FUNCTION IF EXISTS fn_cash_session_fact_immutable();
DROP FUNCTION IF EXISTS fn_cash_session_require_open();";

        // Roles that could sell before keep the ability to open/close the session they could already use; the new
        // granular permissions then let an administrator tighten close and approval rights per role.
        private const string GrantSessionPermissionsSql = @"
UPDATE roles r
SET permissions = (
    SELECT COALESCE(jsonb_agg(DISTINCT p), '[]'::jsonb)
    FROM jsonb_array_elements_text(
        r.permissions || '[""cash_sessions.view"",""cash_sessions.open"",""cash_sessions.close""]'::jsonb) AS p)
WHERE jsonb_typeof(r.permissions) = 'array'
  AND r.permissions ? 'sales.create';";

        private const string RevokeSessionPermissionsSql = @"
UPDATE roles r
SET permissions = (
    SELECT COALESCE(jsonb_agg(p), '[]'::jsonb)
    FROM jsonb_array_elements_text(r.permissions) AS p
    WHERE p NOT IN ('cash_sessions.view','cash_sessions.open','cash_sessions.close'))
WHERE jsonb_typeof(r.permissions) = 'array';";
    }
}
