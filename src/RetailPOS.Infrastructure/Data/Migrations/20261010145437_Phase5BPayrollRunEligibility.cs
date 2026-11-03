using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5BPayrollRunEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_period_id = table.Column<long>(type: "bigint", nullable: false),
                    run_no = table.Column<int>(type: "integer", nullable: false),
                    run_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    scope_id = table.Column<long>(type: "bigint", nullable: true),
                    scope_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    decimal_precision = table.Column<int>(type: "integer", nullable: true),
                    rounding_mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    idempotency_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_runs", x => x.id);
                    table.UniqueConstraint("a_k_payroll_runs_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_run_no", "run_no > 0");
                    table.CheckConstraint("ck_payroll_run_precision", "decimal_precision IS NULL OR decimal_precision BETWEEN 0 AND 6");
                    table.CheckConstraint("ck_payroll_run_regular_only", "run_type = 'Regular'");
                    table.CheckConstraint("ck_payroll_run_scope", "(scope_type = 'Business' AND scope_id IS NULL AND scope_key = 'BUSINESS') OR (scope_type = 'Location' AND scope_id IS NOT NULL AND scope_id > 0)");
                    table.CheckConstraint("ck_payroll_run_status", "status IN ('Draft', 'PendingApproval', 'Approved', 'PaymentInProgress', 'Paid', 'Completed', 'Cancelled')");
                    table.CheckConstraint("ck_payroll_run_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_runs_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_runs_payroll_periods_business_id_payroll_period_id",
                        columns: x => new { x.business_id, x.payroll_period_id },
                        principalTable: "payroll_periods",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_run_employee_selections",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_run_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_code_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    employee_name_snapshot = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    location_id_snapshot = table.Column<long>(type: "bigint", nullable: true),
                    employment_period_id = table.Column<long>(type: "bigint", nullable: true),
                    employment_start_date_snapshot = table.Column<DateOnly>(type: "date", nullable: true),
                    employment_end_date_snapshot = table.Column<DateOnly>(type: "date", nullable: true),
                    eligibility_date = table.Column<DateOnly>(type: "date", nullable: false),
                    employment_status_snapshot = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    salary_version_id = table.Column<long>(type: "bigint", nullable: true),
                    salary_effective_from_snapshot = table.Column<DateOnly>(type: "date", nullable: true),
                    is_included = table.Column<bool>(type: "boolean", nullable: false),
                    exclusion_reason_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    selected_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_run_employee_selections", x => x.id);
                    table.CheckConstraint("ck_payroll_run_employee_selection_outcome", "(is_included AND exclusion_reason_code IS NULL AND salary_version_id IS NOT NULL) OR ((NOT is_included) AND exclusion_reason_code IS NOT NULL)");
                    table.ForeignKey(
                        name: "f_k_payroll_run_employee_selections_hr_employees_business_id_em~",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_run_employee_selections_payroll_employee_salary_ver~",
                        columns: x => new { x.business_id, x.salary_version_id },
                        principalTable: "payroll_employee_salary_versions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_run_employee_selections_payroll_runs_business_id_pa~",
                        columns: x => new { x.business_id, x.payroll_run_id },
                        principalTable: "payroll_runs",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_run_employee_selections_business_id_employee_id",
                table: "payroll_run_employee_selections",
                columns: new[] { "business_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_run_employee_selections_business_id_salary_version_~",
                table: "payroll_run_employee_selections",
                columns: new[] { "business_id", "salary_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_employee_selection_page",
                table: "payroll_run_employee_selections",
                columns: new[] { "business_id", "payroll_run_id", "is_included", "employee_code_snapshot" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_run_employee_selection",
                table: "payroll_run_employee_selections",
                columns: new[] { "business_id", "payroll_run_id", "employee_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_run_business_period_status",
                table: "payroll_runs",
                columns: new[] { "business_id", "payroll_period_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_run_idempotency",
                table: "payroll_runs",
                columns: new[] { "business_id", "created_by_user_id", "payroll_period_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_run_period_no",
                table: "payroll_runs",
                columns: new[] { "payroll_period_id", "run_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_run_period_type_scope",
                table: "payroll_runs",
                columns: new[] { "payroll_period_id", "run_type", "scope_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_run_employee_selections");

            migrationBuilder.DropTable(
                name: "payroll_runs");
        }
    }
}
