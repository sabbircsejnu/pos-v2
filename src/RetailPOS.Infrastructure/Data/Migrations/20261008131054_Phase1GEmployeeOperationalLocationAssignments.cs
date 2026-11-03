using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1GEmployeeOperationalLocationAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "current_primary_location_projection_id",
                table: "hr_employees",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "a_k_hr_approval_instances_business_id_id",
                table: "hr_approval_instances",
                columns: new[] { "business_id", "id" });

            migrationBuilder.CreateTable(
                name: "hr_employee_location_assignments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    location_name_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    source_system_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_entity_type_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_entity_id_snapshot = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    location_source_version_snapshot = table.Column<long>(type: "bigint", nullable: false),
                    assignment_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_temporary = table.Column<bool>(type: "boolean", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    approved_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_employee_location_assignments", x => x.id);
                    table.CheckConstraint("ck_hr_employee_location_assignment_approval", "(approved_by_user_id IS NULL AND approved_at_utc IS NULL AND approval_instance_id IS NULL) OR (approved_by_user_id IS NOT NULL AND approved_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_hr_employee_location_assignment_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_employee_location_assignment_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_hr_employee_location_assignment_status", "status IN ('Effective', 'Closed')");
                    table.CheckConstraint("ck_hr_employee_location_assignment_type", "assignment_type IN ('Primary', 'Additional', 'Temporary')");
                    table.CheckConstraint("ck_hr_employee_location_assignment_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_employee_location_assignments_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_location_assignments_hr_employees_business_id_e~",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_location_assignments_hr_operational_location_pr~",
                        columns: x => new { x.business_id, x.operational_location_projection_id },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_employee_location_assignment_approval_instance",
                        columns: x => new { x.business_id, x.approval_instance_id },
                        principalTable: "hr_approval_instances",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_employee_location_transfer_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    target_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    location_name_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    source_system_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_entity_type_snapshot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_entity_id_snapshot = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    location_source_version_snapshot = table.Column<long>(type: "bigint", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    expected_employee_version = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    decided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_employee_location_transfer_requests", x => x.id);
                    table.UniqueConstraint("a_k_hr_employee_location_transfer_requests_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_employee_location_transfer_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_employee_location_transfer_status", "status IN ('PendingApproval', 'Approved', 'Rejected', 'Cancelled')");
                    table.CheckConstraint("ck_hr_employee_location_transfer_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_employee_location_transfer_requests_businesses_business_~",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_location_transfer_requests_hr_employees_busines~",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_location_transfer_requests_hr_operational_locat~",
                        columns: x => new { x.business_id, x.target_location_projection_id },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_employee_location_transfer_approval_instance",
                        columns: x => new { x.business_id, x.approval_instance_id },
                        principalTable: "hr_approval_instances",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employees_business_id_current_primary_location_projectio~",
                table: "hr_employees",
                columns: new[] { "business_id", "current_primary_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_location_assignments_business_id_approval_insta~",
                table: "hr_employee_location_assignments",
                columns: new[] { "business_id", "approval_instance_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_location_assignments_business_id_operational_lo~",
                table: "hr_employee_location_assignments",
                columns: new[] { "business_id", "operational_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_emp_loc_assign_emp_dates",
                table: "hr_employee_location_assignments",
                columns: new[] { "business_id", "employee_id", "effective_from", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_emp_loc_assign_emp_location_status",
                table: "hr_employee_location_assignments",
                columns: new[] { "business_id", "employee_id", "operational_location_projection_id", "status", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ux_hr_emp_loc_assign_active_primary",
                table: "hr_employee_location_assignments",
                columns: new[] { "business_id", "employee_id" },
                unique: true,
                filter: "assignment_type = 'Primary' AND status = 'Effective'");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_location_transfer_requests_business_id_target_l~",
                table: "hr_employee_location_transfer_requests",
                columns: new[] { "business_id", "target_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_emp_loc_transfer_emp_requested",
                table: "hr_employee_location_transfer_requests",
                columns: new[] { "business_id", "employee_id", "requested_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_hr_emp_loc_transfer_approval",
                table: "hr_employee_location_transfer_requests",
                columns: new[] { "business_id", "approval_instance_id" },
                unique: true,
                filter: "approval_instance_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_hr_emp_loc_transfer_pending_emp",
                table: "hr_employee_location_transfer_requests",
                columns: new[] { "business_id", "employee_id" },
                unique: true,
                filter: "status = 'PendingApproval'");

            migrationBuilder.AddForeignKey(
                name: "f_k_hr_employees_hr_operational_location_projections_business_i~",
                table: "hr_employees",
                columns: new[] { "business_id", "current_primary_location_projection_id" },
                principalTable: "hr_operational_location_projections",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_hr_employees_hr_operational_location_projections_business_i~",
                table: "hr_employees");

            migrationBuilder.DropTable(
                name: "hr_employee_location_assignments");

            migrationBuilder.DropTable(
                name: "hr_employee_location_transfer_requests");

            migrationBuilder.DropIndex(
                name: "i_x_hr_employees_business_id_current_primary_location_projectio~",
                table: "hr_employees");

            migrationBuilder.DropUniqueConstraint(
                name: "a_k_hr_approval_instances_business_id_id",
                table: "hr_approval_instances");

            migrationBuilder.DropColumn(
                name: "current_primary_location_projection_id",
                table: "hr_employees");
        }
    }
}
