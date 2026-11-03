using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5GPayrollPeriodControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "closed_reason",
                table: "payroll_periods",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payroll_period_close_assessments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_period_id = table.Column<long>(type: "bigint", nullable: false),
                    period_version = table.Column<long>(type: "bigint", nullable: false),
                    status_snapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    policy_version = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    gate_results_json = table.Column<string>(type: "text", nullable: false),
                    run_revision_refs_json = table.Column<string>(type: "text", nullable: false),
                    payment_state_versions_json = table.Column<string>(type: "text", nullable: false),
                    posting_state_versions_json = table.Column<string>(type: "text", nullable: false),
                    exceptions_json = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: false),
                    assessed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    was_closed = table.Column<bool>(type: "boolean", nullable: false),
                    result_period_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_period_close_assessments", x => x.id);
                    table.UniqueConstraint("a_k_payroll_period_close_assessments_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_period_close_assessment_policy", "policy_version > 0");
                    table.CheckConstraint("ck_payroll_period_close_assessment_version", "period_version > 0 AND result_period_version >= period_version");
                    table.ForeignKey(
                        name: "f_k_payroll_period_close_assessments_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_period_close_assessments_payroll_periods_business_i~",
                        columns: x => new { x.business_id, x.payroll_period_id },
                        principalTable: "payroll_periods",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_period_close_assessment_history",
                table: "payroll_period_close_assessments",
                columns: new[] { "business_id", "payroll_period_id", "period_version" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_period_close_assessment_idempotency",
                table: "payroll_period_close_assessments",
                columns: new[] { "business_id", "payroll_period_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_period_close_assessments");

            migrationBuilder.DropColumn(
                name: "closed_reason",
                table: "payroll_periods");
        }
    }
}
