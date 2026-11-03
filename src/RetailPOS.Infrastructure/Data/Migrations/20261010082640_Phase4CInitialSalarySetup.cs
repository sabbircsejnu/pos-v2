using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4CInitialSalarySetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_salary_setup_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    salary_structure_id = table.Column<long>(type: "bigint", nullable: false),
                    salary_structure_version = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    structure_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    component_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    idempotency_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true),
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
                    table.PrimaryKey("p_k_payroll_salary_setup_requests", x => x.id);
                    table.UniqueConstraint("a_k_payroll_salary_setup_requests_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_salary_setup_request_status", "status IN ('Draft', 'PendingApproval', 'Approved', 'Rejected', 'Cancelled')");
                    table.CheckConstraint("ck_payroll_salary_setup_request_version", "version > 0 AND salary_structure_version > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_salary_setup_requests_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_salary_setup_requests_hr_approval_instances_busines~",
                        columns: x => new { x.business_id, x.approval_instance_id },
                        principalTable: "hr_approval_instances",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_salary_setup_requests_hr_employees_business_id_empl~",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_salary_setup_requests_payroll_salary_structures_bus~",
                        columns: x => new { x.business_id, x.salary_structure_id },
                        principalTable: "payroll_salary_structures",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_employee_salary_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    salary_setup_request_id = table.Column<long>(type: "bigint", nullable: false),
                    salary_structure_id = table.Column<long>(type: "bigint", nullable: false),
                    salary_structure_version = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    structure_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    component_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: false),
                    approved_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_employee_salary_versions", x => x.id);
                    table.UniqueConstraint("a_k_payroll_employee_salary_versions_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_employee_salary_structure_version", "salary_structure_version > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_employee_salary_versions_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_employee_salary_versions_hr_approval_instances_busi~",
                        columns: x => new { x.business_id, x.approval_instance_id },
                        principalTable: "hr_approval_instances",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_employee_salary_versions_hr_employees_business_id_e~",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_employee_salary_versions_payroll_salary_setup_reque~",
                        columns: x => new { x.business_id, x.salary_setup_request_id },
                        principalTable: "payroll_salary_setup_requests",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_employee_salary_versions_payroll_salary_structures_~",
                        columns: x => new { x.business_id, x.salary_structure_id },
                        principalTable: "payroll_salary_structures",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_salary_versions_business_id_approval_insta~",
                table: "payroll_employee_salary_versions",
                columns: new[] { "business_id", "approval_instance_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_salary_versions_business_id_employee_id_ef~",
                table: "payroll_employee_salary_versions",
                columns: new[] { "business_id", "employee_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_salary_versions_business_id_employee_id_sa~",
                table: "payroll_employee_salary_versions",
                columns: new[] { "business_id", "employee_id", "salary_setup_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_salary_versions_business_id_salary_setup_r~",
                table: "payroll_employee_salary_versions",
                columns: new[] { "business_id", "salary_setup_request_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_salary_versions_business_id_salary_structu~",
                table: "payroll_employee_salary_versions",
                columns: new[] { "business_id", "salary_structure_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_setup_requests_approval_instance_id",
                table: "payroll_salary_setup_requests",
                column: "approval_instance_id",
                unique: true,
                filter: "approval_instance_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_setup_requests_business_id_approval_instance~",
                table: "payroll_salary_setup_requests",
                columns: new[] { "business_id", "approval_instance_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_setup_requests_business_id_created_by_user_i~",
                table: "payroll_salary_setup_requests",
                columns: new[] { "business_id", "created_by_user_id", "employee_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_setup_requests_business_id_employee_id_effec~",
                table: "payroll_salary_setup_requests",
                columns: new[] { "business_id", "employee_id", "effective_from", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_setup_requests_business_id_salary_structure_~",
                table: "payroll_salary_setup_requests",
                columns: new[] { "business_id", "salary_structure_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_employee_salary_versions");

            migrationBuilder.DropTable(
                name: "payroll_salary_setup_requests");
        }
    }
}
