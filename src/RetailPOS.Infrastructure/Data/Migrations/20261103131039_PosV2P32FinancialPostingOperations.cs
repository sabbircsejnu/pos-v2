using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosV2P32FinancialPostingOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "financial_posting_worker_heartbeats",
                columns: table => new
                {
                    worker_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    instance_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    host_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    host_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_run_started_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_run_completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_batch_due = table.Column<int>(type: "integer", nullable: false),
                    last_batch_failures = table.Column<int>(type: "integer", nullable: false),
                    consecutive_run_failures = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_financial_posting_worker_heartbeats", x => new { x.worker_name, x.instance_id });
                    table.CheckConstraint("ck_financial_posting_worker_heartbeats_role", "host_role IN ('Api','Worker')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_financial_posting_worker_heartbeats_updated",
                table: "financial_posting_worker_heartbeats",
                column: "updated_at_utc");

            migrationBuilder.Sql(GrantFinancialPostingPermissionsSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RevokeFinancialPostingPermissionsSql);

            migrationBuilder.DropTable(
                name: "financial_posting_worker_heartbeats");
        }
    }
}
