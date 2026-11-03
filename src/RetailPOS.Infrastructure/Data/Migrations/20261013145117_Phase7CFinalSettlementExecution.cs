using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7CFinalSettlementExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_payroll_payment_result_attempt",
                table: "payroll_payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_accounting_mapping_code",
                table: "payroll_accounting_component_mappings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_accounting_mapping_component_type",
                table: "payroll_accounting_component_mappings");

            migrationBuilder.AlterColumn<long>(
                name: "payroll_run_id",
                table: "payroll_payments",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "payroll_period_id",
                table: "payroll_payments",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "payroll_employee_result_id",
                table: "payroll_payments",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "approval_instance_id",
                table: "payroll_payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "decimal_precision_snapshot",
                table: "payroll_payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "final_settlement_evidence_hash",
                table: "payroll_payments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "final_settlement_id",
                table: "payroll_payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "final_settlement_revision_id",
                table: "payroll_payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "final_settlement_revision_no",
                table: "payroll_payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "historical_location_id_snapshot",
                table: "payroll_payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rounding_mode_snapshot",
                table: "payroll_payments",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "component_type",
                table: "payroll_accounting_component_mappings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.CreateTable(
                name: "payroll_final_settlement_accounting_postings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    final_settlement_id = table.Column<long>(type: "bigint", nullable: false),
                    revision_id = table.Column<long>(type: "bigint", nullable: false),
                    revision_no = table.Column<int>(type: "integer", nullable: false),
                    posting_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    payment_id = table.Column<long>(type: "bigint", nullable: true),
                    payment_evidence_id = table.Column<long>(type: "bigint", nullable: true),
                    posting_reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    request_payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    request_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    conflict_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    accounting_journal_id = table.Column<long>(type: "bigint", nullable: true),
                    accounting_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    posted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_final_settlement_accounting_postings", x => x.id);
                    table.UniqueConstraint("a_k_payroll_final_settlement_accounting_postings_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_final_settlement_posting_hashes", "length(request_payload_hash) = 64 AND (conflict_payload_hash IS NULL OR length(conflict_payload_hash) = 64)");
                    table.CheckConstraint("ck_final_settlement_posting_revision", "revision_no > 0");
                    table.CheckConstraint("ck_final_settlement_posting_status", "status IN ('Pending', 'Processing', 'Posted', 'Failed', 'Conflict')");
                    table.CheckConstraint("ck_final_settlement_posting_type", "posting_type IN ('FinalSettlementExpense', 'FinalSettlementReceivable', 'FinalSettlementPayment')");
                    table.CheckConstraint("ck_final_settlement_posting_version", "attempt_count >= 0 AND version > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_final_settlement_accounting_postings_businesses_bus~",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_final_settlement_posting_payment",
                        columns: x => new { x.business_id, x.payment_id },
                        principalTable: "payroll_payments",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_final_settlement_posting_payment_evidence",
                        columns: x => new { x.business_id, x.payment_evidence_id },
                        principalTable: "payroll_payment_evidence",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_final_settlement_posting_revision",
                        columns: x => new { x.business_id, x.revision_id },
                        principalTable: "payroll_final_settlement_revisions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_final_settlement_posting_settlement",
                        columns: x => new { x.business_id, x.final_settlement_id },
                        principalTable: "payroll_final_settlements",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_payments_business_id_approval_instance_id",
                table: "payroll_payments",
                columns: new[] { "business_id", "approval_instance_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_payments_business_id_final_settlement_revision_id",
                table: "payroll_payments",
                columns: new[] { "business_id", "final_settlement_revision_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_final_settlement_attempt",
                table: "payroll_payments",
                columns: new[] { "business_id", "final_settlement_id", "final_settlement_revision_no", "attempt_no" },
                unique: true,
                filter: "final_settlement_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_result_attempt",
                table: "payroll_payments",
                columns: new[] { "business_id", "payroll_employee_result_id", "attempt_no" },
                unique: true,
                filter: "payroll_employee_result_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_payment_source",
                table: "payroll_payments",
                sql: "(payroll_period_id IS NOT NULL AND payroll_run_id IS NOT NULL AND payroll_employee_result_id IS NOT NULL AND final_settlement_id IS NULL AND final_settlement_revision_id IS NULL AND final_settlement_revision_no IS NULL) OR (payroll_period_id IS NULL AND payroll_run_id IS NULL AND payroll_employee_result_id IS NULL AND final_settlement_id IS NOT NULL AND final_settlement_revision_id IS NOT NULL AND final_settlement_revision_no > 0 AND approval_instance_id IS NOT NULL AND historical_location_id_snapshot > 0 AND decimal_precision_snapshot BETWEEN 0 AND 6 AND rounding_mode_snapshot IS NOT NULL AND length(final_settlement_evidence_hash) = 64)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_accounting_mapping_code",
                table: "payroll_accounting_component_mappings",
                sql: "(component_type = 'NetPayable' AND component_code = 'NET_PAYABLE') OR (component_type = 'FinalSettlementReceivable' AND component_code = 'FINAL_SETTLEMENT_RECEIVABLE') OR (component_type IN ('Earning', 'Deduction') AND length(component_code) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_accounting_mapping_component_type",
                table: "payroll_accounting_component_mappings",
                sql: "component_type IN ('Earning', 'Deduction', 'NetPayable', 'FinalSettlementReceivable')");

            migrationBuilder.CreateIndex(
                name: "ix_final_settlement_posting_evidence",
                table: "payroll_final_settlement_accounting_postings",
                columns: new[] { "business_id", "payment_evidence_id" });

            migrationBuilder.CreateIndex(
                name: "ix_final_settlement_posting_payment",
                table: "payroll_final_settlement_accounting_postings",
                columns: new[] { "business_id", "payment_id" });

            migrationBuilder.CreateIndex(
                name: "ix_final_settlement_posting_revision",
                table: "payroll_final_settlement_accounting_postings",
                columns: new[] { "business_id", "revision_id" });

            migrationBuilder.CreateIndex(
                name: "ix_final_settlement_posting_settlement",
                table: "payroll_final_settlement_accounting_postings",
                columns: new[] { "business_id", "final_settlement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_final_settlement_accounting_posting_identity",
                table: "payroll_final_settlement_accounting_postings",
                columns: new[] { "business_id", "final_settlement_id", "revision_no", "posting_type", "payment_id", "payment_evidence_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_final_settlement_accounting_posting_reference",
                table: "payroll_final_settlement_accounting_postings",
                columns: new[] { "business_id", "posting_reference" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_payments_hr_approval_instances_business_id_approval~",
                table: "payroll_payments",
                columns: new[] { "business_id", "approval_instance_id" },
                principalTable: "hr_approval_instances",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_payments_payroll_final_settlement_revisions_busines~",
                table: "payroll_payments",
                columns: new[] { "business_id", "final_settlement_revision_id" },
                principalTable: "payroll_final_settlement_revisions",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_payments_payroll_final_settlements_business_id_fina~",
                table: "payroll_payments",
                columns: new[] { "business_id", "final_settlement_id" },
                principalTable: "payroll_final_settlements",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_payments_hr_approval_instances_business_id_approval~",
                table: "payroll_payments");

            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_payments_payroll_final_settlement_revisions_busines~",
                table: "payroll_payments");

            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_payments_payroll_final_settlements_business_id_fina~",
                table: "payroll_payments");

            migrationBuilder.DropTable(
                name: "payroll_final_settlement_accounting_postings");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_payments_business_id_approval_instance_id",
                table: "payroll_payments");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_payments_business_id_final_settlement_revision_id",
                table: "payroll_payments");

            migrationBuilder.DropIndex(
                name: "ux_payroll_payment_final_settlement_attempt",
                table: "payroll_payments");

            migrationBuilder.DropIndex(
                name: "ux_payroll_payment_result_attempt",
                table: "payroll_payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_payment_source",
                table: "payroll_payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_accounting_mapping_code",
                table: "payroll_accounting_component_mappings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_accounting_mapping_component_type",
                table: "payroll_accounting_component_mappings");

            migrationBuilder.DropColumn(
                name: "approval_instance_id",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "decimal_precision_snapshot",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "final_settlement_evidence_hash",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "final_settlement_id",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "final_settlement_revision_id",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "final_settlement_revision_no",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "historical_location_id_snapshot",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "rounding_mode_snapshot",
                table: "payroll_payments");

            migrationBuilder.AlterColumn<long>(
                name: "payroll_run_id",
                table: "payroll_payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "payroll_period_id",
                table: "payroll_payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "payroll_employee_result_id",
                table: "payroll_payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "component_type",
                table: "payroll_accounting_component_mappings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_result_attempt",
                table: "payroll_payments",
                columns: new[] { "business_id", "payroll_employee_result_id", "attempt_no" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_accounting_mapping_code",
                table: "payroll_accounting_component_mappings",
                sql: "(component_type = 'NetPayable' AND component_code = 'NET_PAYABLE') OR (component_type IN ('Earning', 'Deduction') AND length(component_code) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_accounting_mapping_component_type",
                table: "payroll_accounting_component_mappings",
                sql: "component_type IN ('Earning', 'Deduction', 'NetPayable')");
        }
    }
}
