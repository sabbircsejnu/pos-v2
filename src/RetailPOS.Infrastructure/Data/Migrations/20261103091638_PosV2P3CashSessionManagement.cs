using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosV2P3CashSessionManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "disbursed_at_utc",
                table: "expenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "disbursed_by_user_id",
                table: "expenses",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "disbursed_cash_session_id",
                table: "expenses",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_source",
                table: "expenses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "self_approved_under_owner_exception",
                table: "expenses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(ConvertBusinessDateSql);

            migrationBuilder.AddColumn<int>(
                name: "duration_snapshot_hours",
                table: "cash_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<string>(
                name: "open_idempotency_key",
                table: "cash_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_zone_id_snapshot",
                table: "cash_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<string>(
                name: "variance_reason",
                table: "cash_sessions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "allow_owner_expense_self_approval",
                table: "business_pos_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "cash_difference_approvals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    cash_session_id = table.Column<long>(type: "bigint", nullable: false),
                    variance_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reviewed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    reviewed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    review_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    decision_version = table.Column<int>(type: "integer", nullable: false),
                    posting_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    posting_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_posting_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_posting_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    posted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_cash_difference_approvals", x => x.id);
                    table.CheckConstraint("ck_cash_difference_approvals_nonzero", "variance_amount <> 0");
                    table.CheckConstraint("ck_cash_difference_approvals_posting", "posting_status IN ('not_required','pending','posted','failed')");
                    table.CheckConstraint("ck_cash_difference_approvals_status", "status IN ('pending','approved','rejected')");
                    table.ForeignKey(
                        name: "f_k_cash_difference_approvals__cash_sessions_cash_session_id",
                        column: x => x.cash_session_id,
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_session_attachments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    cash_session_id = table.Column<long>(type: "bigint", nullable: false),
                    handover_id = table.Column<long>(type: "bigint", nullable: true),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_cash_session_attachments", x => x.id);
                    table.ForeignKey(
                        name: "f_k_cash_session_attachments_cash_sessions_cash_session_id",
                        column: x => x.cash_session_id,
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_session_close_snapshots",
                columns: table => new
                {
                    cash_session_id = table.Column<long>(type: "bigint", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    opening_cash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cash_sales_retained = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cash_tendered = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    change_given = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    card_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    mobile_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    drawer_expenses = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cash_refunds = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    corrections = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    expected_cash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    counted_cash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    variance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    variance_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    handed_over_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    retained_at_outlet = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    denominations_json = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    closed_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    duration_snapshot_hours = table.Column<int>(type: "integer", nullable: false),
                    was_overdue_at_close = table.Column<bool>(type: "boolean", nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_cash_session_close_snapshots", x => x.cash_session_id);
                    table.CheckConstraint("ck_cash_session_close_snapshots_retained", "retained_at_outlet >= 0 AND handed_over_total >= 0 AND counted_cash >= 0");
                    table.ForeignKey(
                        name: "f_k_cash_session_close_snapshots_cash_sessions_cash_session_id",
                        column: x => x.cash_session_id,
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_session_handovers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    cash_session_id = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    given_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    recipient_user_id = table.Column<long>(type: "bigint", nullable: true),
                    recipient_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    bank_account_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    deposit_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    deposit_date = table.Column<DateTime>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confirmed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    confirmed_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_cash_session_handovers", x => x.id);
                    table.CheckConstraint("ck_cash_session_handovers_amount", "amount > 0");
                    table.CheckConstraint("ck_cash_session_handovers_method", "method IN ('cash_to_manager','cash_to_owner','bank_deposit')");
                    table.CheckConstraint("ck_cash_session_handovers_status", "status IN ('handed','pending_bank_verification','confirmed')");
                    table.ForeignKey(
                        name: "f_k_cash_session_handovers_cash_sessions_cash_session_id",
                        column: x => x.cash_session_id,
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_session_movements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    cash_session_id = table.Column<long>(type: "bigint", nullable: false),
                    movement_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    cash_effect = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    source_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_id = table.Column<long>(type: "bigint", nullable: true),
                    source_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    original_sale_id = table.Column<long>(type: "bigint", nullable: true),
                    original_cash_session_id = table.Column<long>(type: "bigint", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    recorded_by_user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_cash_session_movements", x => x.id);
                    table.CheckConstraint("ck_cash_session_movements_effect", "(movement_type IN ('cash_refund','drawer_expense') AND cash_effect < 0) OR (movement_type = 'drawer_expense_reversal' AND cash_effect > 0) OR (movement_type = 'correction' AND cash_effect <> 0)");
                    table.CheckConstraint("ck_cash_session_movements_type", "movement_type IN ('cash_refund','drawer_expense','drawer_expense_reversal','correction')");
                    table.ForeignKey(
                        name: "f_k_cash_session_movements_cash_sessions_cash_session_id",
                        column: x => x.cash_session_id,
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_disbursed_cash_session_id",
                table: "expenses",
                column: "disbursed_cash_session_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_maker_checker",
                table: "expenses",
                sql: "payment_source IS NULL OR payment_source = 'bank' OR approved_by IS NULL OR self_approved_under_owner_exception OR (approved_by IS DISTINCT FROM created_by AND approved_by IS DISTINCT FROM submitted_by)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_payment_source",
                table: "expenses",
                sql: "payment_source IS NULL OR payment_source IN ('bank','petty_cash','pos_drawer')");

            migrationBuilder.CreateIndex(
                name: "ux_cash_sessions_open_idempotency",
                table: "cash_sessions",
                columns: new[] { "outlet_id", "open_idempotency_key" },
                unique: true,
                filter: "open_idempotency_key IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_sessions_duration",
                table: "cash_sessions",
                sql: "duration_snapshot_hours BETWEEN 1 AND 20");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_sessions_opening_cash",
                table: "cash_sessions",
                sql: "opening_cash_amount >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_sessions_status",
                table: "cash_sessions",
                sql: "status IN ('open','closed')");

            migrationBuilder.CreateIndex(
                name: "i_x_cash_difference_approvals_business_id_status",
                table: "cash_difference_approvals",
                columns: new[] { "business_id", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_cash_difference_approvals_cash_session_id",
                table: "cash_difference_approvals",
                column: "cash_session_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_cash_difference_approvals_posting_status_next_posting_attem~",
                table: "cash_difference_approvals",
                columns: new[] { "posting_status", "next_posting_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "i_x_cash_session_attachments_cash_session_id",
                table: "cash_session_attachments",
                column: "cash_session_id");

            migrationBuilder.CreateIndex(
                name: "i_x_cash_session_close_snapshots_business_id_closed_at_utc",
                table: "cash_session_close_snapshots",
                columns: new[] { "business_id", "closed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "i_x_cash_session_handovers_cash_session_id",
                table: "cash_session_handovers",
                column: "cash_session_id");

            migrationBuilder.CreateIndex(
                name: "i_x_cash_session_movements_cash_session_id",
                table: "cash_session_movements",
                column: "cash_session_id");

            migrationBuilder.CreateIndex(
                name: "i_x_cash_session_movements_original_sale_id",
                table: "cash_session_movements",
                column: "original_sale_id");

            migrationBuilder.CreateIndex(
                name: "ux_cash_session_movements_source_key",
                table: "cash_session_movements",
                columns: new[] { "business_id", "source_key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_expenses_cash_sessions_disbursed_cash_session_id",
                table: "expenses",
                column: "disbursed_cash_session_id",
                principalTable: "cash_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(BackfillSessionSnapshotsSql);
            migrationBuilder.Sql(CashFactGuardTriggersSql);
            migrationBuilder.Sql(GrantSessionPermissionsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RevokeSessionPermissionsSql);
            migrationBuilder.Sql(DropCashFactGuardTriggersSql);
            migrationBuilder.DropForeignKey(
                name: "FK_expenses_cash_sessions_disbursed_cash_session_id",
                table: "expenses");

            migrationBuilder.DropTable(
                name: "cash_difference_approvals");

            migrationBuilder.DropTable(
                name: "cash_session_attachments");

            migrationBuilder.DropTable(
                name: "cash_session_close_snapshots");

            migrationBuilder.DropTable(
                name: "cash_session_handovers");

            migrationBuilder.DropTable(
                name: "cash_session_movements");

            migrationBuilder.DropIndex(
                name: "IX_expenses_disbursed_cash_session_id",
                table: "expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_maker_checker",
                table: "expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_payment_source",
                table: "expenses");

            migrationBuilder.DropIndex(
                name: "ux_cash_sessions_open_idempotency",
                table: "cash_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_sessions_duration",
                table: "cash_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_sessions_opening_cash",
                table: "cash_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_sessions_status",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "disbursed_at_utc",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "disbursed_by_user_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "disbursed_cash_session_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "payment_source",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "self_approved_under_owner_exception",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "duration_snapshot_hours",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "open_idempotency_key",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "time_zone_id_snapshot",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "variance_reason",
                table: "cash_sessions");

            migrationBuilder.DropColumn(
                name: "allow_owner_expense_self_approval",
                table: "business_pos_settings");

            migrationBuilder.Sql(RevertBusinessDateSql);
        }
    }
}
