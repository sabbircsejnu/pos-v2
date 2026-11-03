using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7BFinalSettlementApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_final_settlement_approval_submissions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    final_settlement_id = table.Column<long>(type: "bigint", nullable: false),
                    revision_id = table.Column<long>(type: "bigint", nullable: false),
                    revision_no = table.Column<int>(type: "integer", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    exit_case_id = table.Column<long>(type: "bigint", nullable: false),
                    calculation_snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    component_lines_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    gross_payable = table.Column<decimal>(type: "numeric", nullable: false),
                    total_recovery = table.Column<decimal>(type: "numeric", nullable: false),
                    net_settlement = table.Column<decimal>(type: "numeric", nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    source_evidence_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    calculation_policy_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    clearance_references_json = table.Column<string>(type: "jsonb", nullable: false),
                    clearance_references_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    submitter_user_id = table.Column<long>(type: "bigint", nullable: false),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    submitted_evidence_json = table.Column<string>(type: "jsonb", nullable: false),
                    submitted_evidence_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_final_settlement_approval_submissions", x => x.id);
                    table.UniqueConstraint("a_k_payroll_final_settlement_approval_submissions_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_final_settlement_submission_amounts", "gross_payable >= 0 AND total_recovery >= 0 AND net_settlement = gross_payable - total_recovery");
                    table.CheckConstraint("ck_payroll_final_settlement_submission_hashes", "length(calculation_snapshot_hash) = 64 AND length(component_lines_hash) = 64 AND length(source_evidence_hash) = 64 AND length(clearance_references_hash) = 64 AND length(payload_hash) = 64 AND length(submitted_evidence_hash) = 64");
                    table.ForeignKey(
                        name: "fk_payroll_final_settlement_submission_approval",
                        columns: x => new { x.business_id, x.approval_instance_id },
                        principalTable: "hr_approval_instances",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_final_settlement_submission_business",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_final_settlement_submission_revision",
                        columns: x => new { x.business_id, x.revision_id },
                        principalTable: "payroll_final_settlement_revisions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_final_settlement_submission_settlement",
                        columns: x => new { x.business_id, x.final_settlement_id },
                        principalTable: "payroll_final_settlements",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_final_settlement_approval_submissions_business_id_f~",
                table: "payroll_final_settlement_approval_submissions",
                columns: new[] { "business_id", "final_settlement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_final_settlement_submission_approval",
                table: "payroll_final_settlement_approval_submissions",
                columns: new[] { "business_id", "approval_instance_id" },
                unique: true,
                filter: "approval_instance_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_final_settlement_submission_idempotency",
                table: "payroll_final_settlement_approval_submissions",
                columns: new[] { "business_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_final_settlement_submission_revision",
                table: "payroll_final_settlement_approval_submissions",
                columns: new[] { "business_id", "revision_id" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION payroll_final_settlement_submission_guard()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'INSERT' AND NEW.approval_instance_id IS NULL THEN
                        RETURN NEW;
                    END IF;

                    IF TG_OP = 'UPDATE'
                       AND OLD.approval_instance_id IS NULL
                       AND NEW.approval_instance_id IS NOT NULL
                       AND (to_jsonb(NEW) - 'approval_instance_id')
                           = (to_jsonb(OLD) - 'approval_instance_id') THEN
                        RETURN NEW;
                    END IF;

                    RAISE EXCEPTION 'Payroll final settlement approval evidence is immutable'
                        USING ERRCODE = '23514';
                END;
                $$;

                CREATE TRIGGER trg_payroll_final_settlement_submission_guard
                    BEFORE INSERT OR UPDATE OR DELETE
                    ON payroll_final_settlement_approval_submissions
                    FOR EACH ROW
                    EXECUTE FUNCTION payroll_final_settlement_submission_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_payroll_final_settlement_submission_guard
                    ON payroll_final_settlement_approval_submissions;
                DROP FUNCTION IF EXISTS payroll_final_settlement_submission_guard();
                """);
            migrationBuilder.DropTable(
                name: "payroll_final_settlement_approval_submissions");
        }
    }
}
