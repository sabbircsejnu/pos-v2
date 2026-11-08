namespace RetailPOS.Infrastructure.Data.Migrations
{
    // Hand-written parts of PosV2ExchangeFoundation. They live in their own file so regenerating the scaffolded
    // migration never loses them. They are the database backstop for EX-14 (completed exchanges are immutable) and
    // D-16 (policy versions are append-only); application code never relies on them for normal flow.
    public partial class PosV2ExchangeFoundation
    {
        private const string ImmutabilityTriggersSql = @"
CREATE OR REPLACE FUNCTION fn_exchange_append_only() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'EXCHANGE_IMMUTABLE: % on % is not permitted', TG_OP, TG_TABLE_NAME USING ERRCODE = 'P0001';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_exchange_policy_versions_append_only BEFORE UPDATE OR DELETE ON exchange_policy_versions
    FOR EACH ROW EXECUTE FUNCTION fn_exchange_append_only();
CREATE TRIGGER trg_exchange_source_invoices_append_only BEFORE UPDATE OR DELETE ON exchange_source_invoices
    FOR EACH ROW EXECUTE FUNCTION fn_exchange_append_only();
CREATE TRIGGER trg_exchange_return_lines_append_only BEFORE UPDATE OR DELETE ON exchange_return_lines
    FOR EACH ROW EXECUTE FUNCTION fn_exchange_append_only();
CREATE TRIGGER trg_exchange_lineage_links_append_only BEFORE UPDATE OR DELETE ON exchange_lineage_links
    FOR EACH ROW EXECUTE FUNCTION fn_exchange_append_only();
CREATE TRIGGER trg_exchange_settlements_append_only BEFORE UPDATE OR DELETE ON exchange_settlements
    FOR EACH ROW EXECUTE FUNCTION fn_exchange_append_only();
CREATE TRIGGER trg_exchange_salesperson_adjustments_append_only BEFORE UPDATE OR DELETE ON exchange_salesperson_adjustments
    FOR EACH ROW EXECUTE FUNCTION fn_exchange_append_only();
CREATE TRIGGER trg_exchange_receipt_snapshots_append_only BEFORE UPDATE OR DELETE ON exchange_receipt_snapshots
    FOR EACH ROW EXECUTE FUNCTION fn_exchange_append_only();
CREATE TRIGGER trg_exchange_reversals_no_delete BEFORE DELETE ON exchange_reversals
    FOR EACH ROW EXECUTE FUNCTION fn_exchange_append_only();

-- An exchange header changes only by gaining its journal (inside its own transaction) or by being reversed once.
CREATE OR REPLACE FUNCTION fn_exchange_header_guard() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'EXCHANGE_IMMUTABLE: DELETE on exchanges is not permitted' USING ERRCODE = 'P0001';
    END IF;
    IF (to_jsonb(NEW) - 'status' - 'reversal_id' - 'journal_entry_id') <> (to_jsonb(OLD) - 'status' - 'reversal_id' - 'journal_entry_id')
        OR (OLD.journal_entry_id IS NOT NULL AND NEW.journal_entry_id IS DISTINCT FROM OLD.journal_entry_id)
        OR (OLD.reversal_id IS NOT NULL AND NEW.reversal_id IS DISTINCT FROM OLD.reversal_id)
        OR (OLD.status = 'reversed' AND NEW.status <> 'reversed')
        OR (NEW.status = 'reversed' AND NEW.reversal_id IS NULL) THEN
        RAISE EXCEPTION 'EXCHANGE_IMMUTABLE: a completed exchange cannot be changed' USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_exchanges_guard BEFORE UPDATE OR DELETE ON exchanges
    FOR EACH ROW EXECUTE FUNCTION fn_exchange_header_guard();

-- A replacement line's only mutable fact is how much of it later exchanges consumed (C-03).
CREATE OR REPLACE FUNCTION fn_exchange_replacement_line_guard() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'EXCHANGE_IMMUTABLE: DELETE on exchange_replacement_lines is not permitted' USING ERRCODE = 'P0001';
    END IF;
    IF (to_jsonb(NEW) - 'returned_quantity') <> (to_jsonb(OLD) - 'returned_quantity') THEN
        RAISE EXCEPTION 'EXCHANGE_IMMUTABLE: a replacement line cannot be changed' USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_exchange_replacement_lines_guard BEFORE UPDATE OR DELETE ON exchange_replacement_lines
    FOR EACH ROW EXECUTE FUNCTION fn_exchange_replacement_line_guard();";

        private const string DropImmutabilityTriggersSql = @"
DROP TRIGGER IF EXISTS trg_exchange_replacement_lines_guard ON exchange_replacement_lines;
DROP FUNCTION IF EXISTS fn_exchange_replacement_line_guard();
DROP TRIGGER IF EXISTS trg_exchanges_guard ON exchanges;
DROP FUNCTION IF EXISTS fn_exchange_header_guard();
DROP TRIGGER IF EXISTS trg_exchange_reversals_no_delete ON exchange_reversals;
DROP TRIGGER IF EXISTS trg_exchange_receipt_snapshots_append_only ON exchange_receipt_snapshots;
DROP TRIGGER IF EXISTS trg_exchange_salesperson_adjustments_append_only ON exchange_salesperson_adjustments;
DROP TRIGGER IF EXISTS trg_exchange_settlements_append_only ON exchange_settlements;
DROP TRIGGER IF EXISTS trg_exchange_lineage_links_append_only ON exchange_lineage_links;
DROP TRIGGER IF EXISTS trg_exchange_return_lines_append_only ON exchange_return_lines;
DROP TRIGGER IF EXISTS trg_exchange_source_invoices_append_only ON exchange_source_invoices;
DROP TRIGGER IF EXISTS trg_exchange_policy_versions_append_only ON exchange_policy_versions;
DROP FUNCTION IF EXISTS fn_exchange_append_only();";
    }
}
