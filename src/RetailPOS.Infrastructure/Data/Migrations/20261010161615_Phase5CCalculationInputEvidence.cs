using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5CCalculationInputEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_payroll_employee_result_run_employee",
                table: "payroll_employee_results");

            migrationBuilder.AddColumn<string>(
                name: "calculation_input_hash",
                table: "payroll_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "calculation_version",
                table: "payroll_runs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "calculation_input_hash",
                table: "payroll_employee_results",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "calculation_version",
                table: "payroll_employee_results",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "evidence_code",
                table: "payroll_employee_result_lines",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "input_amount",
                table: "payroll_employee_result_lines",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "input_quantity",
                table: "payroll_employee_result_lines",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "input_rate",
                table: "payroll_employee_result_lines",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "source_version",
                table: "payroll_employee_result_lines",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE payroll_runs SET calculation_version = 1 WHERE calculated_at_utc IS NOT NULL; " +
                "UPDATE payroll_employee_results SET calculation_version = 1;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_run_calculation_version",
                table: "payroll_runs",
                sql: "calculation_version >= 0");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_employee_result_run_version_employee",
                table: "payroll_employee_results",
                columns: new[] { "business_id", "payroll_run_id", "calculation_version", "employee_id" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_employee_result_calculation_version",
                table: "payroll_employee_results",
                sql: "calculation_version > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_run_calculation_version",
                table: "payroll_runs");

            migrationBuilder.DropIndex(
                name: "ux_payroll_employee_result_run_version_employee",
                table: "payroll_employee_results");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_employee_result_calculation_version",
                table: "payroll_employee_results");

            migrationBuilder.DropColumn(
                name: "calculation_input_hash",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "calculation_version",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "calculation_input_hash",
                table: "payroll_employee_results");

            migrationBuilder.DropColumn(
                name: "calculation_version",
                table: "payroll_employee_results");

            migrationBuilder.DropColumn(
                name: "evidence_code",
                table: "payroll_employee_result_lines");

            migrationBuilder.DropColumn(
                name: "input_amount",
                table: "payroll_employee_result_lines");

            migrationBuilder.DropColumn(
                name: "input_quantity",
                table: "payroll_employee_result_lines");

            migrationBuilder.DropColumn(
                name: "input_rate",
                table: "payroll_employee_result_lines");

            migrationBuilder.DropColumn(
                name: "source_version",
                table: "payroll_employee_result_lines");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_employee_result_run_employee",
                table: "payroll_employee_results",
                columns: new[] { "business_id", "payroll_run_id", "employee_id" },
                unique: true);
        }
    }
}
