using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5CCalculationPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "calculation_policy_snapshot_json",
                table: "payroll_employee_results",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "calculation_policy_version_id",
                table: "payroll_employee_results",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payroll_calculation_policy_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    partial_month_basis = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    salary_base_component_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    deduct_absence = table.Column<bool>(type: "boolean", nullable: false),
                    deduct_unpaid_leave = table.Column<bool>(type: "boolean", nullable: false),
                    late_per_minute_rate = table.Column<decimal>(type: "numeric", nullable: true),
                    early_leave_per_minute_rate = table.Column<decimal>(type: "numeric", nullable: true),
                    overtime_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    overtime_divisor_days = table.Column<int>(type: "integer", nullable: false),
                    standard_paid_minutes = table.Column<int>(type: "integer", nullable: false),
                    normal_day_overtime_rate = table.Column<decimal>(type: "numeric", nullable: true),
                    weekly_off_overtime_rate = table.Column<decimal>(type: "numeric", nullable: true),
                    holiday_overtime_rate = table.Column<decimal>(type: "numeric", nullable: true),
                    special_day_overtime_rate = table.Column<decimal>(type: "numeric", nullable: true),
                    absence_component_id = table.Column<long>(type: "bigint", nullable: true),
                    unpaid_leave_component_id = table.Column<long>(type: "bigint", nullable: true),
                    late_component_id = table.Column<long>(type: "bigint", nullable: true),
                    early_leave_component_id = table.Column<long>(type: "bigint", nullable: true),
                    overtime_component_id = table.Column<long>(type: "bigint", nullable: true),
                    tax_statutory_component_id = table.Column<long>(type: "bigint", nullable: true),
                    configuration_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_calculation_policy_versions", x => x.id);
                    table.UniqueConstraint("a_k_payroll_calculation_policy_versions_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_calculation_policy_basis", "partial_month_basis IN ('CalendarDays', 'Fixed30', 'WorkingDays')");
                    table.CheckConstraint("ck_payroll_calculation_policy_effective_period", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_payroll_calculation_policy_overtime_mode", "overtime_mode IS NULL OR overtime_mode IN ('FixedHourlyRate', 'SalaryBaseMultiplier')");
                    table.CheckConstraint("ck_payroll_calculation_policy_rates", "(late_per_minute_rate IS NULL OR late_per_minute_rate >= 0) AND (early_leave_per_minute_rate IS NULL OR early_leave_per_minute_rate >= 0) AND (normal_day_overtime_rate IS NULL OR normal_day_overtime_rate >= 0) AND (weekly_off_overtime_rate IS NULL OR weekly_off_overtime_rate >= 0) AND (holiday_overtime_rate IS NULL OR holiday_overtime_rate >= 0) AND (special_day_overtime_rate IS NULL OR special_day_overtime_rate >= 0)");
                    table.CheckConstraint("ck_payroll_calculation_policy_version", "version_no > 0 AND overtime_divisor_days > 0 AND standard_paid_minutes >= 0");
                    table.ForeignKey(
                        name: "f_k_payroll_calculation_policy_versions_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_calculation_policy_absencecomponentid",
                        columns: x => new { x.business_id, x.absence_component_id },
                        principalTable: "payroll_salary_components",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_calculation_policy_earlyleavecomponentid",
                        columns: x => new { x.business_id, x.early_leave_component_id },
                        principalTable: "payroll_salary_components",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_calculation_policy_latecomponentid",
                        columns: x => new { x.business_id, x.late_component_id },
                        principalTable: "payroll_salary_components",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_calculation_policy_overtimecomponentid",
                        columns: x => new { x.business_id, x.overtime_component_id },
                        principalTable: "payroll_salary_components",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_calculation_policy_taxstatutorycomponentid",
                        columns: x => new { x.business_id, x.tax_statutory_component_id },
                        principalTable: "payroll_salary_components",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_calculation_policy_unpaidleavecomponentid",
                        columns: x => new { x.business_id, x.unpaid_leave_component_id },
                        principalTable: "payroll_salary_components",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_results_business_id_calculation_policy_ver~",
                table: "payroll_employee_results",
                columns: new[] { "business_id", "calculation_policy_version_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_calculation_policy_versions_business_id_absence_com~",
                table: "payroll_calculation_policy_versions",
                columns: new[] { "business_id", "absence_component_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_calculation_policy_versions_business_id_early_leave~",
                table: "payroll_calculation_policy_versions",
                columns: new[] { "business_id", "early_leave_component_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_calculation_policy_versions_business_id_effective_f~",
                table: "payroll_calculation_policy_versions",
                columns: new[] { "business_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_calculation_policy_versions_business_id_late_compon~",
                table: "payroll_calculation_policy_versions",
                columns: new[] { "business_id", "late_component_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_calculation_policy_versions_business_id_overtime_co~",
                table: "payroll_calculation_policy_versions",
                columns: new[] { "business_id", "overtime_component_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_calculation_policy_versions_business_id_tax_statuto~",
                table: "payroll_calculation_policy_versions",
                columns: new[] { "business_id", "tax_statutory_component_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_calculation_policy_versions_business_id_unpaid_leav~",
                table: "payroll_calculation_policy_versions",
                columns: new[] { "business_id", "unpaid_leave_component_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_calculation_policy_versions_business_id_version_no",
                table: "payroll_calculation_policy_versions",
                columns: new[] { "business_id", "version_no" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_employee_results_payroll_calculation_policy_version~",
                table: "payroll_employee_results",
                columns: new[] { "business_id", "calculation_policy_version_id" },
                principalTable: "payroll_calculation_policy_versions",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_employee_results_payroll_calculation_policy_version~",
                table: "payroll_employee_results");

            migrationBuilder.DropTable(
                name: "payroll_calculation_policy_versions");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_employee_results_business_id_calculation_policy_ver~",
                table: "payroll_employee_results");

            migrationBuilder.DropColumn(
                name: "calculation_policy_snapshot_json",
                table: "payroll_employee_results");

            migrationBuilder.DropColumn(
                name: "calculation_policy_version_id",
                table: "payroll_employee_results");
        }
    }
}
