using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5DCalculationSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "calculation_snapshot_hash",
                table: "payroll_employee_results",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "calculation_snapshot_json",
                table: "payroll_employee_results",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "calculation_snapshot_hash",
                table: "payroll_employee_results");

            migrationBuilder.DropColumn(
                name: "calculation_snapshot_json",
                table: "payroll_employee_results");
        }
    }
}
