using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4FEmployeeLoans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_employee_loans",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    loan_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    principal_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    approved_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    disbursed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    recovery_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    recovery_mode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    fixed_installment = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    outstanding_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    proposed_schedule_json = table.Column<string>(type: "jsonb", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    idempotency_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    current_schedule_version_no = table.Column<int>(type: "integer", nullable: true),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    submitted_target_version = table.Column<long>(type: "bigint", nullable: true),
                    decided_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    decided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_employee_loans", x => x.id);
                    table.UniqueConstraint("a_k_payroll_employee_loans_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_employee_loan_amounts", "CAST(principal_amount AS NUMERIC) > 0 AND CAST(principal_amount AS NUMERIC) <= 99999999999999.9999 AND (approved_amount IS NULL OR (CAST(approved_amount AS NUMERIC) > 0 AND CAST(approved_amount AS NUMERIC) <= CAST(principal_amount AS NUMERIC))) AND CAST(outstanding_amount AS NUMERIC) >= 0 AND CAST(outstanding_amount AS NUMERIC) <= COALESCE(CAST(approved_amount AS NUMERIC), CAST(principal_amount AS NUMERIC)) AND version > 0");
                    table.CheckConstraint("ck_payroll_employee_loan_recovery_mode", "recovery_mode IN ('FixedMonthly', 'Manual', 'CustomSchedule')");
                    table.CheckConstraint("ck_payroll_employee_loan_status", "status IN ('Draft', 'PendingApproval', 'Active', 'Rejected', 'Cancelled', 'Settled')");
                    table.ForeignKey(
                        name: "f_k_payroll_employee_loans_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_employee_loans_hr_approval_instances_business_id_ap~",
                        columns: x => new { x.business_id, x.approval_instance_id },
                        principalTable: "hr_approval_instances",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_employee_loans_hr_employees_business_id_employee_id",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_loan_repayments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    loan_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    idempotency_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    recorded_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_loan_repayments", x => x.id);
                    table.UniqueConstraint("a_k_payroll_loan_repayments_business_id_loan_id_id", x => new { x.business_id, x.loan_id, x.id });
                    table.CheckConstraint("ck_payroll_loan_repayment_amount", "amount > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_loan_repayments_payroll_employee_loans_business_id_~",
                        columns: x => new { x.business_id, x.loan_id },
                        principalTable: "payroll_employee_loans",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_loan_schedule_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    loan_id = table.Column<long>(type: "bigint", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    recovery_start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    recovery_mode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    fixed_installment = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    idempotency_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_loan_schedule_versions", x => x.id);
                    table.UniqueConstraint("a_k_payroll_loan_schedule_versions_business_id_loan_id_id", x => new { x.business_id, x.loan_id, x.id });
                    table.CheckConstraint("ck_payroll_loan_schedule_version_number", "version_no > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_loan_schedule_versions_payroll_employee_loans_busin~",
                        columns: x => new { x.business_id, x.loan_id },
                        principalTable: "payroll_employee_loans",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_loan_schedule_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    loan_id = table.Column<long>(type: "bigint", nullable: false),
                    schedule_version_id = table.Column<long>(type: "bigint", nullable: false),
                    installment_no = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    planned_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_loan_schedule_lines", x => x.id);
                    table.UniqueConstraint("a_k_payroll_loan_schedule_lines_business_id_loan_id_schedule_ve~", x => new { x.business_id, x.loan_id, x.schedule_version_id, x.installment_no });
                    table.CheckConstraint("ck_payroll_loan_schedule_line_values", "installment_no > 0 AND planned_amount > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_loan_schedule_lines_payroll_loan_schedule_versions_~",
                        columns: x => new { x.business_id, x.loan_id, x.schedule_version_id },
                        principalTable: "payroll_loan_schedule_versions",
                        principalColumns: new[] { "business_id", "loan_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_loan_repayment_allocations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    loan_id = table.Column<long>(type: "bigint", nullable: false),
                    repayment_id = table.Column<long>(type: "bigint", nullable: false),
                    schedule_version_id = table.Column<long>(type: "bigint", nullable: false),
                    installment_no = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_loan_repayment_allocations", x => x.id);
                    table.CheckConstraint("ck_payroll_loan_repayment_allocation_amount", "amount > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_loan_repayment_allocations_payroll_loan_repayments_~",
                        columns: x => new { x.business_id, x.loan_id, x.repayment_id },
                        principalTable: "payroll_loan_repayments",
                        principalColumns: new[] { "business_id", "loan_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_loan_repayment_allocations_payroll_loan_schedule_li~",
                        columns: x => new { x.business_id, x.loan_id, x.schedule_version_id, x.installment_no },
                        principalTable: "payroll_loan_schedule_lines",
                        principalColumns: new[] { "business_id", "loan_id", "schedule_version_id", "installment_no" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_loans_approval_instance_id",
                table: "payroll_employee_loans",
                column: "approval_instance_id",
                unique: true,
                filter: "approval_instance_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_loans_business_id_approval_instance_id",
                table: "payroll_employee_loans",
                columns: new[] { "business_id", "approval_instance_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_loans_business_id_created_by_user_id_emplo~",
                table: "payroll_employee_loans",
                columns: new[] { "business_id", "created_by_user_id", "employee_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_loans_business_id_employee_id_created_at_u~",
                table: "payroll_employee_loans",
                columns: new[] { "business_id", "employee_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_loan_repayment_allocations_business_id_loan_id_repa~",
                table: "payroll_loan_repayment_allocations",
                columns: new[] { "business_id", "loan_id", "repayment_id", "schedule_version_id", "installment_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_loan_repayment_allocations_business_id_loan_id_sche~",
                table: "payroll_loan_repayment_allocations",
                columns: new[] { "business_id", "loan_id", "schedule_version_id", "installment_no" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_loan_repayments_business_id_loan_id_recorded_by_use~",
                table: "payroll_loan_repayments",
                columns: new[] { "business_id", "loan_id", "recorded_by_user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_loan_schedule_lines_business_id_loan_id_schedule_ve~",
                table: "payroll_loan_schedule_lines",
                columns: new[] { "business_id", "loan_id", "schedule_version_id", "due_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_loan_schedule_versions_business_id_loan_id_created_~",
                table: "payroll_loan_schedule_versions",
                columns: new[] { "business_id", "loan_id", "created_by_user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_loan_schedule_versions_business_id_loan_id_version_~",
                table: "payroll_loan_schedule_versions",
                columns: new[] { "business_id", "loan_id", "version_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_loan_repayment_allocations");

            migrationBuilder.DropTable(
                name: "payroll_loan_repayments");

            migrationBuilder.DropTable(
                name: "payroll_loan_schedule_lines");

            migrationBuilder.DropTable(
                name: "payroll_loan_schedule_versions");

            migrationBuilder.DropTable(
                name: "payroll_employee_loans");
        }
    }
}
