namespace RetailPOS.Infrastructure.Data.Migrations
{
    // Hand-written parts of PosV2P31SaleFinancialPostings. They live in their own file so regenerating the scaffolded
    // migration never loses them.
    public partial class PosV2P31SaleFinancialPostings
    {
        // Database backstop for D10: a posting intent is never deleted, its identity and commit-time snapshot never
        // change, and "posted" is final (it cannot move back or point at a different journal).
        private const string PostingGuardTriggerSql = @"
CREATE OR REPLACE FUNCTION fn_sale_financial_posting_guard() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'SALE_FINANCIAL_POSTING_IMMUTABLE: DELETE on sale_financial_postings is not permitted' USING ERRCODE = 'P0001';
    END IF;
    IF NEW.business_id <> OLD.business_id OR NEW.sale_id <> OLD.sale_id OR NEW.kind <> OLD.kind
        OR NEW.reversal_type IS DISTINCT FROM OLD.reversal_type OR NEW.source_key <> OLD.source_key
        OR NEW.snapshot_json <> OLD.snapshot_json OR NEW.snapshot_hash <> OLD.snapshot_hash
        OR NEW.outlet_id <> OLD.outlet_id OR NEW.terminal_id IS DISTINCT FROM OLD.terminal_id
        OR NEW.cash_session_id IS DISTINCT FROM OLD.cash_session_id OR NEW.actor_user_id <> OLD.actor_user_id
        OR NEW.business_date <> OLD.business_date OR NEW.created_at_utc <> OLD.created_at_utc THEN
        RAISE EXCEPTION 'SALE_FINANCIAL_POSTING_IMMUTABLE: the posting intent snapshot cannot change' USING ERRCODE = 'P0001';
    END IF;
    IF OLD.status = 'posted' AND (NEW.status <> 'posted' OR NEW.journal_entry_id IS DISTINCT FROM OLD.journal_entry_id) THEN
        RAISE EXCEPTION 'SALE_FINANCIAL_POSTING_IMMUTABLE: a posted intent is final' USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_sale_financial_postings_guard
    BEFORE UPDATE OR DELETE ON sale_financial_postings
    FOR EACH ROW EXECUTE FUNCTION fn_sale_financial_posting_guard();";

        private const string DropPostingGuardTriggerSql = @"
DROP TRIGGER IF EXISTS trg_sale_financial_postings_guard ON sale_financial_postings;
DROP FUNCTION IF EXISTS fn_sale_financial_posting_guard();";
    }
}
