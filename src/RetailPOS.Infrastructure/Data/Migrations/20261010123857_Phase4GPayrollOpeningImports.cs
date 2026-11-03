using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4GPayrollOpeningImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_hr_import_batch_type",
                table: "hr_import_batches");

            migrationBuilder.AddColumn<bool>(
                name: "is_opening_balance",
                table: "payroll_employee_loans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "opening_balance_amount",
                table: "payroll_employee_loans",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "opening_disbursed_date",
                table: "payroll_employee_loans",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_employee_loan_opening_balance",
                table: "payroll_employee_loans",
                sql: "(is_opening_balance = FALSE AND opening_disbursed_date IS NULL AND opening_balance_amount IS NULL) OR (is_opening_balance = TRUE AND opening_disbursed_date IS NOT NULL AND CAST(opening_balance_amount AS NUMERIC) >= 0 AND CAST(opening_balance_amount AS NUMERIC) <= CAST(principal_amount AS NUMERIC))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_hr_import_batch_type",
                table: "hr_import_batches",
                sql: "import_type IN ('Employee', 'Assignment', 'Salary', 'LoanOpening')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_employee_loan_opening_balance",
                table: "payroll_employee_loans");

            migrationBuilder.DropCheckConstraint(
                name: "ck_hr_import_batch_type",
                table: "hr_import_batches");

            migrationBuilder.DropColumn(
                name: "is_opening_balance",
                table: "payroll_employee_loans");

            migrationBuilder.DropColumn(
                name: "opening_balance_amount",
                table: "payroll_employee_loans");

            migrationBuilder.DropColumn(
                name: "opening_disbursed_date",
                table: "payroll_employee_loans");

            migrationBuilder.AddCheckConstraint(
                name: "ck_hr_import_batch_type",
                table: "hr_import_batches",
                sql: "import_type IN ('Employee', 'Assignment')");
        }
    }
}
