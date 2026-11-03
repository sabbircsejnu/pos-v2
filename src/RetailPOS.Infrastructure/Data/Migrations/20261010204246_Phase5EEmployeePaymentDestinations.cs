using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5EEmployeePaymentDestinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_employee_payment_destinations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    bank_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    account_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    account_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    mobile_provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    mobile_account_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    mobile_account_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_employee_payment_destinations", x => x.id);
                    table.UniqueConstraint("a_k_payroll_employee_payment_destinations_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_payment_destination_effective_date", "effective_from > '0001-01-01'");
                    table.CheckConstraint("ck_payroll_payment_destination_method", "method IN ('Cash', 'Bank', 'Mobile')");
                    table.CheckConstraint("ck_payroll_payment_destination_method_fields", "(method = 'Cash' AND bank_name IS NULL AND account_name IS NULL AND account_number IS NULL AND mobile_provider IS NULL AND mobile_account_name IS NULL AND mobile_account_number IS NULL) OR (method = 'Bank' AND bank_name IS NOT NULL AND length(trim(bank_name)) > 0 AND account_name IS NOT NULL AND length(trim(account_name)) > 0 AND account_number IS NOT NULL AND length(trim(account_number)) > 0 AND mobile_provider IS NULL AND mobile_account_name IS NULL AND mobile_account_number IS NULL) OR (method = 'Mobile' AND mobile_provider IS NOT NULL AND length(trim(mobile_provider)) > 0 AND mobile_account_name IS NOT NULL AND length(trim(mobile_account_name)) > 0 AND mobile_account_number IS NOT NULL AND length(trim(mobile_account_number)) > 0 AND bank_name IS NULL AND account_name IS NULL AND account_number IS NULL)");
                    table.CheckConstraint("ck_payroll_payment_destination_primary", "is_primary");
                    table.CheckConstraint("ck_payroll_payment_destination_version", "version_no > 0 AND version > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_employee_payment_destinations_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_employee_payment_destinations_hr_employees_business~",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_payment_destination_effective",
                table: "payroll_employee_payment_destinations",
                columns: new[] { "business_id", "employee_id", "is_primary", "is_active", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_destination_effective_from",
                table: "payroll_employee_payment_destinations",
                columns: new[] { "business_id", "employee_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_destination_version",
                table: "payroll_employee_payment_destinations",
                columns: new[] { "business_id", "employee_id", "version_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_employee_payment_destinations");
        }
    }
}
