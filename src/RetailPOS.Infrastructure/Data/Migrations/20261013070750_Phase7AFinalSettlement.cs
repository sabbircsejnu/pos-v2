using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7AFinalSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_final_settlements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    exit_case_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    current_revision_no = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_final_settlements", x => x.id);
                    table.UniqueConstraint("a_k_payroll_final_settlements_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_final_settlement_business", "business_id > 0");
                    table.CheckConstraint("ck_payroll_final_settlement_revision", "current_revision_no > 0");
                    table.CheckConstraint("ck_payroll_final_settlement_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_final_settlements_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_final_settlements_hr_exit_cases_business_id_exit_ca~",
                        columns: x => new { x.business_id, x.exit_case_id },
                        principalTable: "hr_exit_cases",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_final_settlement_revisions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    final_settlement_id = table.Column<long>(type: "bigint", nullable: false),
                    revision_no = table.Column<int>(type: "integer", nullable: false),
                    settlement_date = table.Column<DateOnly>(type: "date", nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    decimal_precision = table.Column<int>(type: "integer", nullable: false),
                    rounding_mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    gross_due = table.Column<decimal>(type: "numeric", nullable: false),
                    total_deductions = table.Column<decimal>(type: "numeric", nullable: false),
                    net_settlement = table.Column<decimal>(type: "numeric", nullable: false),
                    calculation_input_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    snapshot_json = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    idempotency_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    calculated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    calculated_by_user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_final_settlement_revisions", x => x.id);
                    table.UniqueConstraint("a_k_payroll_final_settlement_revisions_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_final_settlement_revision_amounts", "gross_due >= 0 AND total_deductions >= 0 AND net_settlement = gross_due - total_deductions");
                    table.CheckConstraint("ck_payroll_final_settlement_revision_hashes", "length(calculation_input_hash) = 64 AND length(snapshot_hash) = 64 AND length(idempotency_payload_hash) = 64");
                    table.CheckConstraint("ck_payroll_final_settlement_revision_number", "revision_no > 0");
                    table.CheckConstraint("ck_payroll_final_settlement_revision_precision", "decimal_precision BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "f_k_payroll_final_settlement_revisions_payroll_final_settlement~",
                        columns: x => new { x.business_id, x.final_settlement_id },
                        principalTable: "payroll_final_settlements",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_final_settlement_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    revision_id = table.Column<long>(type: "bigint", nullable: false),
                    line_no = table.Column<int>(type: "integer", nullable: false),
                    component_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    component_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    line_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    is_applicable = table.Column<bool>(type: "boolean", nullable: false),
                    not_applicable_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    evidence_snapshot_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_final_settlement_lines", x => x.id);
                    table.CheckConstraint("ck_payroll_final_settlement_line_amount", "amount >= 0 AND (is_applicable OR (amount = 0 AND line_type = 'NotApplicable'))");
                    table.CheckConstraint("ck_payroll_final_settlement_line_number", "line_no > 0");
                    table.CheckConstraint("ck_payroll_final_settlement_line_reason", "(is_applicable AND not_applicable_reason IS NULL) OR (NOT is_applicable AND not_applicable_reason IS NOT NULL)");
                    table.CheckConstraint("ck_payroll_final_settlement_line_type", "line_type IN ('Earning', 'Deduction', 'NotApplicable')");
                    table.ForeignKey(
                        name: "f_k_payroll_final_settlement_lines_payroll_final_settlement_rev~",
                        columns: x => new { x.business_id, x.revision_id },
                        principalTable: "payroll_final_settlement_revisions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_final_settlement_line_number",
                table: "payroll_final_settlement_lines",
                columns: new[] { "business_id", "revision_id", "line_no" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE FUNCTION payroll_final_settlement_block_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION 'Final settlement revisions and lines are immutable'
                        USING ERRCODE = 'P0001';
                END;
                $$;

                CREATE TRIGGER trg_payroll_final_settlement_revisions_immutable
                    BEFORE UPDATE OR DELETE ON payroll_final_settlement_revisions
                    FOR EACH ROW EXECUTE FUNCTION payroll_final_settlement_block_immutable();

                CREATE TRIGGER trg_payroll_final_settlement_lines_immutable
                    BEFORE UPDATE OR DELETE ON payroll_final_settlement_lines
                    FOR EACH ROW EXECUTE FUNCTION payroll_final_settlement_block_immutable();

                CREATE FUNCTION payroll_final_settlement_header_guard()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'Final settlement identity cannot be deleted'
                            USING ERRCODE = 'P0001';
                    END IF;

                    IF OLD.business_id IS DISTINCT FROM NEW.business_id
                        OR OLD.exit_case_id IS DISTINCT FROM NEW.exit_case_id
                        OR OLD.employee_id IS DISTINCT FROM NEW.employee_id
                        OR OLD.created_at_utc IS DISTINCT FROM NEW.created_at_utc
                        OR OLD.created_by_user_id IS DISTINCT FROM NEW.created_by_user_id
                        OR NEW.current_revision_no <> OLD.current_revision_no + 1
                        OR NEW.version <> OLD.version + 1 THEN
                        RAISE EXCEPTION 'Final settlement headers only allow sequential revision advancement'
                            USING ERRCODE = 'P0001';
                    END IF;

                    RETURN NEW;
                END;
                $$;

                CREATE TRIGGER trg_payroll_final_settlement_header_guard
                    BEFORE UPDATE OR DELETE ON payroll_final_settlements
                    FOR EACH ROW EXECUTE FUNCTION payroll_final_settlement_header_guard();
                """);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_final_settlement_idempotency",
                table: "payroll_final_settlement_revisions",
                columns: new[] { "business_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_final_settlement_revision_number",
                table: "payroll_final_settlement_revisions",
                columns: new[] { "business_id", "final_settlement_id", "revision_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_final_settlement_exit",
                table: "payroll_final_settlements",
                columns: new[] { "business_id", "exit_case_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_payroll_final_settlement_header_guard
                    ON payroll_final_settlements;
                DROP FUNCTION IF EXISTS payroll_final_settlement_header_guard();

                DROP TRIGGER IF EXISTS trg_payroll_final_settlement_lines_immutable
                    ON payroll_final_settlement_lines;
                DROP TRIGGER IF EXISTS trg_payroll_final_settlement_revisions_immutable
                    ON payroll_final_settlement_revisions;
                DROP FUNCTION IF EXISTS payroll_final_settlement_block_immutable();
                """);

            migrationBuilder.DropTable(
                name: "payroll_final_settlement_lines");

            migrationBuilder.DropTable(
                name: "payroll_final_settlement_revisions");

            migrationBuilder.DropTable(
                name: "payroll_final_settlements");
        }
    }
}
