using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4DSalaryRevisionApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "i_x_payroll_employee_salary_versions_business_id_employee_id_sa~",
                table: "payroll_employee_salary_versions");

            migrationBuilder.AddColumn<long>(
                name: "predecessor_salary_version_id",
                table: "payroll_employee_salary_versions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "salary_revision_request_id",
                table: "payroll_employee_salary_versions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payroll_salary_revision_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    source_salary_version_id = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    structure_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    current_component_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    proposed_component_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("p_k_payroll_salary_revision_requests", x => x.id);
                    table.UniqueConstraint("a_k_payroll_salary_revision_requests_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_salary_revision_request_status", "status IN ('Draft', 'PendingApproval', 'Approved', 'Rejected', 'Cancelled')");
                    table.CheckConstraint("ck_payroll_salary_revision_request_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_salary_revision_requests_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_salary_revision_requests_hr_approval_instances_busi~",
                        columns: x => new { x.business_id, x.approval_instance_id },
                        principalTable: "hr_approval_instances",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_salary_revision_requests_hr_employees_business_id_e~",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_salary_revision_requests_payroll_employee_salary_ve~",
                        columns: x => new { x.business_id, x.source_salary_version_id },
                        principalTable: "payroll_employee_salary_versions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_salary_versions_business_id_employee_id_sa~",
                table: "payroll_employee_salary_versions",
                columns: new[] { "business_id", "employee_id", "salary_setup_request_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_salary_versions_business_id_predecessor_sa~",
                table: "payroll_employee_salary_versions",
                columns: new[] { "business_id", "predecessor_salary_version_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_salary_versions_business_id_salary_revisio~",
                table: "payroll_employee_salary_versions",
                columns: new[] { "business_id", "salary_revision_request_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_salary_versions_predecessor_salary_version~",
                table: "payroll_employee_salary_versions",
                column: "predecessor_salary_version_id",
                unique: true,
                filter: "predecessor_salary_version_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_salary_versions_salary_revision_request_id",
                table: "payroll_employee_salary_versions",
                column: "salary_revision_request_id",
                unique: true,
                filter: "salary_revision_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_revision_requests_approval_instance_id",
                table: "payroll_salary_revision_requests",
                column: "approval_instance_id",
                unique: true,
                filter: "approval_instance_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_revision_requests_business_id_approval_insta~",
                table: "payroll_salary_revision_requests",
                columns: new[] { "business_id", "approval_instance_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_revision_requests_business_id_created_by_use~",
                table: "payroll_salary_revision_requests",
                columns: new[] { "business_id", "created_by_user_id", "employee_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_revision_requests_business_id_employee_id_st~",
                table: "payroll_salary_revision_requests",
                columns: new[] { "business_id", "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_revision_requests_business_id_source_salary_~",
                table: "payroll_salary_revision_requests",
                columns: new[] { "business_id", "source_salary_version_id" });

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_employee_salary_versions_payroll_employee_salary_ve~",
                table: "payroll_employee_salary_versions",
                columns: new[] { "business_id", "predecessor_salary_version_id" },
                principalTable: "payroll_employee_salary_versions",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_employee_salary_versions_payroll_salary_revision_re~",
                table: "payroll_employee_salary_versions",
                columns: new[] { "business_id", "salary_revision_request_id" },
                principalTable: "payroll_salary_revision_requests",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_employee_salary_versions_payroll_employee_salary_ve~",
                table: "payroll_employee_salary_versions");

            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_employee_salary_versions_payroll_salary_revision_re~",
                table: "payroll_employee_salary_versions");

            migrationBuilder.DropTable(
                name: "payroll_salary_revision_requests");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_employee_salary_versions_business_id_employee_id_sa~",
                table: "payroll_employee_salary_versions");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_employee_salary_versions_business_id_predecessor_sa~",
                table: "payroll_employee_salary_versions");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_employee_salary_versions_business_id_salary_revisio~",
                table: "payroll_employee_salary_versions");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_employee_salary_versions_predecessor_salary_version~",
                table: "payroll_employee_salary_versions");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_employee_salary_versions_salary_revision_request_id",
                table: "payroll_employee_salary_versions");

            migrationBuilder.DropColumn(
                name: "predecessor_salary_version_id",
                table: "payroll_employee_salary_versions");

            migrationBuilder.DropColumn(
                name: "salary_revision_request_id",
                table: "payroll_employee_salary_versions");

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_employee_salary_versions_business_id_employee_id_sa~",
                table: "payroll_employee_salary_versions",
                columns: new[] { "business_id", "employee_id", "salary_setup_request_id" },
                unique: true);
        }
    }
}
