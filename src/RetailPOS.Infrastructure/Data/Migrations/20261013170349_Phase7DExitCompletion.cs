using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7DExitCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_exit_completion_evidence",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    exit_case_id = table.Column<long>(type: "bigint", nullable: false),
                    exit_version = table.Column<long>(type: "bigint", nullable: false),
                    payroll_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    completion_mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    final_settlement_id = table.Column<long>(type: "bigint", nullable: true),
                    final_settlement_revision_id = table.Column<long>(type: "bigint", nullable: true),
                    final_settlement_revision_no = table.Column<int>(type: "integer", nullable: true),
                    final_settlement_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    final_settlement_evidence_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    net_settlement = table.Column<decimal>(type: "numeric", nullable: true),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    evidence_json = table.Column<string>(type: "text", nullable: false),
                    evidence_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    completed_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_exit_completion_evidence", x => x.id);
                    table.UniqueConstraint("a_k_hr_exit_completion_evidence_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_exit_completion_business", "business_id > 0");
                    table.CheckConstraint("ck_hr_exit_completion_financial", "(payroll_enabled = FALSE AND final_settlement_id IS NULL AND final_settlement_revision_id IS NULL AND final_settlement_revision_no IS NULL AND final_settlement_status IS NULL AND final_settlement_evidence_hash IS NULL AND net_settlement IS NULL AND currency_code IS NULL) OR (payroll_enabled = TRUE AND final_settlement_id IS NOT NULL AND final_settlement_revision_id IS NOT NULL AND final_settlement_revision_no IS NOT NULL AND final_settlement_status IS NOT NULL AND final_settlement_evidence_hash IS NOT NULL AND net_settlement IS NOT NULL AND currency_code IS NOT NULL)");
                    table.CheckConstraint("ck_hr_exit_completion_hashes", "length(evidence_hash) = 64");
                    table.CheckConstraint("ck_hr_exit_completion_mode", "completion_mode IN ('HrOnly', 'PositiveSettlement', 'ZeroSettlement', 'NegativeSettlement')");
                    table.CheckConstraint("ck_hr_exit_completion_version", "exit_version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_exit_completion_evidence_hr_exit_cases_business_id_exit_~",
                        columns: x => new { x.business_id, x.exit_case_id },
                        principalTable: "hr_exit_cases",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_hr_exit_completion_exit",
                table: "hr_exit_completion_evidence",
                columns: new[] { "business_id", "exit_case_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_exit_completion_evidence");
        }
    }
}
