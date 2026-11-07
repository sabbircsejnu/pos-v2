using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <summary>
    /// The posting intent guard also freezes <c>sale_refund_id</c>: once written, an intent can never be moved to another
    /// refund, posted or not. Only the function body changes; the trigger, existing rows and every other rule stay as
    /// they were (PosV2P31SaleFinancialPostings). Down restores the previous body exactly.
    /// </summary>
    public partial class PosV2UomPostingRefundGuard : Migration
    {
        private const string GuardWithRefund = @"
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
        OR NEW.business_date <> OLD.business_date OR NEW.created_at_utc <> OLD.created_at_utc
        OR NEW.sale_refund_id IS DISTINCT FROM OLD.sale_refund_id THEN
        RAISE EXCEPTION 'SALE_FINANCIAL_POSTING_IMMUTABLE: the posting intent snapshot cannot change' USING ERRCODE = 'P0001';
    END IF;
    IF OLD.status = 'posted' AND (NEW.status <> 'posted' OR NEW.journal_entry_id IS DISTINCT FROM OLD.journal_entry_id) THEN
        RAISE EXCEPTION 'SALE_FINANCIAL_POSTING_IMMUTABLE: a posted intent is final' USING ERRCODE = 'P0001';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;";

        private const string GuardBefore = @"
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
$$ LANGUAGE plpgsql;";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(GuardWithRefund);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(GuardBefore);
    }
}
