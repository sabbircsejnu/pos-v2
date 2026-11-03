using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StockCountApprovalAdjustmentJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_count_adjustment_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_count_id = table.Column<long>(type: "bigint", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    approver_user_id = table.Column<long>(type: "bigint", nullable: false),
                    approver_role_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    approved_count_revision = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lease_owner = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    evidence_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    blocked_reason_codes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    stock_adjustment_id = table.Column<long>(type: "bigint", nullable: true),
                    outbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    audit_correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_count_adjustment_jobs", x => x.id);
                    table.CheckConstraint("ck_stock_count_adjustment_jobs_revision", "approved_count_revision >= 0 AND attempt_count >= 0");
                    table.CheckConstraint("ck_stock_count_adjustment_jobs_status", "status IN ('Pending','Processing','RetryScheduled','Completed','Blocked','Failed')");
                    table.ForeignKey(
                        name: "f_k_stock_count_adjustment_jobs_stock_counts_stock_count_id",
                        column: x => x.stock_count_id,
                        principalTable: "stock_counts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_adjustment_jobs_business_id_created_at_utc",
                table: "stock_count_adjustment_jobs",
                columns: new[] { "business_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_adjustment_jobs_outbox_message_id",
                table: "stock_count_adjustment_jobs",
                column: "outbox_message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_adjustment_jobs_status_next_attempt_at_utc_leas~",
                table: "stock_count_adjustment_jobs",
                columns: new[] { "status", "next_attempt_at_utc", "lease_expires_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_stock_count_adjustment_jobs_count_revision",
                table: "stock_count_adjustment_jobs",
                columns: new[] { "stock_count_id", "approved_count_revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_count_adjustment_jobs");
        }
    }
}
