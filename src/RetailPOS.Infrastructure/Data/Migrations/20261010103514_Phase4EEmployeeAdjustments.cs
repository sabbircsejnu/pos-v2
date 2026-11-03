using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4EEmployeeAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_employee_adjustment_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    adjustment_identity = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    predecessor_version_id = table.Column<long>(type: "bigint", nullable: true),
                    salary_component_id = table.Column<long>(type: "bigint", nullable: false),
                    component_code_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    component_name_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    component_type_snapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value_type_snapshot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    adjustment_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    classification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    idempotency_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_employee_adjustment_versions", x => x.id);
                    table.UniqueConstraint("a_k_payroll_employee_adjustment_versions_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_employee_adjustment_classification", "classification IN ('Manual', 'Statutory', 'Incentive', 'Bonus')");
                    table.CheckConstraint("ck_payroll_employee_adjustment_component_type", "component_type_snapshot IN ('Earning', 'Deduction')");
                    table.CheckConstraint("ck_payroll_employee_adjustment_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_payroll_employee_adjustment_kind", "adjustment_kind IN ('RecurringAllowance', 'RecurringDeduction', 'OneTimeEarning', 'OneTimeDeduction')");
                    table.CheckConstraint("ck_payroll_employee_adjustment_status", "status IN ('Active', 'Inactive')");
                    table.CheckConstraint("ck_payroll_employee_adjustment_version", "version > 0 AND amount > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_employee_adjustment_versions_hr_employees_business_~",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_employee_adjustment_versions_hr_operational_locatio~",
                        columns: x => new { x.business_id, x.operational_location_projection_id },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_employee_adjustment_versions_payroll_employee_adjus~",
                        columns: x => new { x.business_id, x.predecessor_version_id },
                        principalTable: "payroll_employee_adjustment_versions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_employee_adjustment_versions_payroll_salary_compone~",
                        columns: x => new { x.business_id, x.salary_component_id },
                        principalTable: "payroll_salary_components",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_adjustment_versions_business_id_operationa~",
                table: "payroll_employee_adjustment_versions",
                columns: new[] { "business_id", "operational_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_adjustment_versions_business_id_predecesso~",
                table: "payroll_employee_adjustment_versions",
                columns: new[] { "business_id", "predecessor_version_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_adjustment_versions_business_id_salary_com~",
                table: "payroll_employee_adjustment_versions",
                columns: new[] { "business_id", "salary_component_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_adj_effective",
                table: "payroll_employee_adjustment_versions",
                columns: new[] { "business_id", "employee_id", "effective_from", "adjustment_kind" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_adj_idempotency",
                table: "payroll_employee_adjustment_versions",
                columns: new[] { "business_id", "created_by_user_id", "employee_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_adj_identity_version",
                table: "payroll_employee_adjustment_versions",
                columns: new[] { "business_id", "employee_id", "adjustment_identity", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_adj_predecessor",
                table: "payroll_employee_adjustment_versions",
                column: "predecessor_version_id",
                unique: true,
                filter: "predecessor_version_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_employee_adjustment_versions");
        }
    }
}
