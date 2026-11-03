using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5CCalculationLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "calculated_at_utc",
                table: "payroll_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payroll_employee_results",
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
                    salary_version_id = table.Column<long>(type: "bigint", nullable: false),
                    salary_effective_from_snapshot = table.Column<DateOnly>(type: "date", nullable: false),
                    gross_earnings = table.Column<decimal>(type: "numeric", nullable: true),
                    total_deductions = table.Column<decimal>(type: "numeric", nullable: true),
                    net_pay = table.Column<decimal>(type: "numeric", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    exception_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    calculated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_employee_results", x => x.id);
                    table.UniqueConstraint("a_k_payroll_employee_results_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_employee_result_amounts", "(status = 'Exception' AND gross_earnings IS NULL AND total_deductions IS NULL AND net_pay IS NULL AND exception_code IS NOT NULL) OR (status = 'Calculated' AND gross_earnings IS NOT NULL AND total_deductions IS NOT NULL AND net_pay IS NOT NULL AND exception_code IS NULL)");
                    table.CheckConstraint("ck_payroll_employee_result_status", "status IN ('Calculated', 'Exception')");
                    table.ForeignKey(
                        name: "f_k_payroll_employee_results_hr_employees_business_id_employee_~",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_employee_results_payroll_employee_salary_versions_b~",
                        columns: x => new { x.business_id, x.salary_version_id },
                        principalTable: "payroll_employee_salary_versions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_employee_results_payroll_runs_business_id_payroll_r~",
                        columns: x => new { x.business_id, x.payroll_run_id },
                        principalTable: "payroll_runs",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_employee_result_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_employee_result_id = table.Column<long>(type: "bigint", nullable: false),
                    salary_component_id = table.Column<long>(type: "bigint", nullable: true),
                    component_code_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    component_name_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    line_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    source_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    calculation_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_employee_result_lines", x => x.id);
                    table.CheckConstraint("ck_payroll_employee_result_line_amount", "amount >= 0");
                    table.CheckConstraint("ck_payroll_employee_result_line_order", "calculation_order >= 0");
                    table.CheckConstraint("ck_payroll_employee_result_line_type", "line_type IN ('Earning', 'Deduction')");
                    table.ForeignKey(
                        name: "f_k_payroll_employee_result_lines_payroll_employee_results_busi~",
                        columns: x => new { x.business_id, x.payroll_employee_result_id },
                        principalTable: "payroll_employee_results",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_employee_result_lines_payroll_salary_components_bus~",
                        columns: x => new { x.business_id, x.salary_component_id },
                        principalTable: "payroll_salary_components",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_result_lines_business_id_salary_component_~",
                table: "payroll_employee_result_lines",
                columns: new[] { "business_id", "salary_component_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_employee_result_line_order",
                table: "payroll_employee_result_lines",
                columns: new[] { "business_id", "payroll_employee_result_id", "calculation_order" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_results_business_id_employee_id",
                table: "payroll_employee_results",
                columns: new[] { "business_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_results_business_id_salary_version_id",
                table: "payroll_employee_results",
                columns: new[] { "business_id", "salary_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_employee_result_page",
                table: "payroll_employee_results",
                columns: new[] { "business_id", "payroll_run_id", "employee_code_snapshot" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_employee_result_run_employee",
                table: "payroll_employee_results",
                columns: new[] { "business_id", "payroll_run_id", "employee_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_employee_result_lines");

            migrationBuilder.DropTable(
                name: "payroll_employee_results");

            migrationBuilder.DropColumn(
                name: "calculated_at_utc",
                table: "payroll_runs");
        }
    }
}
