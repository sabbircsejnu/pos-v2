using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosV2P31SaleFinancialPostings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sale_financial_postings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    sale_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reversal_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    source_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    snapshot_json = table.Column<string>(type: "text", nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    terminal_id = table.Column<long>(type: "bigint", nullable: true),
                    cash_session_id = table.Column<long>(type: "bigint", nullable: true),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateTime>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    next_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    posted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_sale_financial_postings", x => x.id);
                    table.CheckConstraint("ck_sale_financial_postings_attempts", "attempts >= 0");
                    table.CheckConstraint("ck_sale_financial_postings_kind", "kind IN ('sale','reversal')");
                    table.CheckConstraint("ck_sale_financial_postings_posted_journal", "(status = 'posted') = (journal_entry_id IS NOT NULL AND posted_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_sale_financial_postings_reversal_type", "(kind = 'sale' AND reversal_type IS NULL) OR (kind = 'reversal' AND reversal_type IN ('void','refund'))");
                    table.CheckConstraint("ck_sale_financial_postings_status", "status IN ('pending','posted','failed')");
                    table.ForeignKey(
                        name: "f_k_sale_financial_postings_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_sale_financial_postings_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_sale_financial_postings_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_difference_approvals_posted_journal",
                table: "cash_difference_approvals",
                sql: "posting_status <> 'posted' OR journal_entry_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_difference_approvals_posting_requires_approval",
                table: "cash_difference_approvals",
                sql: "status = 'approved' OR posting_status = 'not_required'");

            migrationBuilder.CreateIndex(
                name: "ix_sale_financial_postings_business_status",
                table: "sale_financial_postings",
                columns: new[] { "business_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_financial_postings_due",
                table: "sale_financial_postings",
                columns: new[] { "status", "next_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_sale_financial_postings_journal_entry_id",
                table: "sale_financial_postings",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "ux_sale_financial_postings_sale_kind",
                table: "sale_financial_postings",
                columns: new[] { "sale_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_sale_financial_postings_source_key",
                table: "sale_financial_postings",
                columns: new[] { "business_id", "source_key" },
                unique: true);

            migrationBuilder.Sql(PostingGuardTriggerSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropPostingGuardTriggerSql);

            migrationBuilder.DropTable(
                name: "sale_financial_postings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_difference_approvals_posted_journal",
                table: "cash_difference_approvals");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_difference_approvals_posting_requires_approval",
                table: "cash_difference_approvals");
        }
    }
}
