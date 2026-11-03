using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3CLeaveRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_leave_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_type_id = table.Column<long>(type: "bigint", nullable: false),
                    scope_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    requested_units = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    decided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    current_attachment_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    submitted_target_version = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_leave_requests", x => x.id);
                    table.UniqueConstraint("a_k_hr_leave_requests_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_leave_request_actor", "requested_by_user_id > 0");
                    table.CheckConstraint("ck_hr_leave_request_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_leave_request_dates", "end_date >= start_date");
                    table.CheckConstraint("ck_hr_leave_request_decision", "(decided_by_user_id IS NULL AND decided_at_utc IS NULL) OR (decided_by_user_id IS NOT NULL AND decided_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_hr_leave_request_status", "status IN ('Draft', 'PendingApproval', 'Approved', 'Rejected', 'Cancelled')");
                    table.CheckConstraint("ck_hr_leave_request_submitted_version", "submitted_target_version IS NULL OR submitted_target_version > 0");
                    table.CheckConstraint("ck_hr_leave_request_units", "requested_units > 0");
                    table.CheckConstraint("ck_hr_leave_request_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_leave_requests_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_requests_hr_employees_business_id_employee_id",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_requests_hr_leave_types_business_id_leave_type_id",
                        columns: x => new { x.business_id, x.leave_type_id },
                        principalTable: "hr_leave_types",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_requests_hr_operational_location_projections_busin~",
                        columns: x => new { x.business_id, x.scope_location_projection_id },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_leave_request_approval_instance",
                        columns: x => new { x.business_id, x.approval_instance_id },
                        principalTable: "hr_approval_instances",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_leave_request_attachment_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_request_id = table.Column<long>(type: "bigint", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: true),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scan_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    encryption_key_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    uploaded_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_leave_request_attachment_versions", x => x.id);
                    table.UniqueConstraint("a_k_hr_leave_request_attachment_versions_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_leave_request_attachment_actor", "uploaded_by_user_id > 0");
                    table.CheckConstraint("ck_hr_leave_request_attachment_size", "size_bytes > 0");
                    table.CheckConstraint("ck_hr_leave_request_attachment_status", "scan_status IN ('Pending', 'Clean', 'Rejected', 'Error')");
                    table.CheckConstraint("ck_hr_leave_request_attachment_version_no", "version_no > 0");
                    table.ForeignKey(
                        name: "f_k_hr_leave_request_attachment_versions_hr_leave_requests_busi~",
                        columns: x => new { x.business_id, x.leave_request_id },
                        principalTable: "hr_leave_requests",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_leave_request_days",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_request_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    roster_id = table.Column<long>(type: "bigint", nullable: true),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    policy_version_id = table.Column<long>(type: "bigint", nullable: false),
                    units = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    day_classification = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    shift_snapshot_json = table.Column<string>(type: "text", nullable: false),
                    calendar_evidence_json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_leave_request_days", x => x.id);
                    table.CheckConstraint("ck_hr_leave_request_day_classification", "day_classification IN ('NormalWorkingDay', 'Holiday', 'WeeklyOff')");
                    table.CheckConstraint("ck_hr_leave_request_day_units", "units >= 0");
                    table.ForeignKey(
                        name: "f_k_hr_leave_request_days_hr_employees_business_id_employee_id",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_request_days_hr_leave_policy_versions_business_id_~",
                        columns: x => new { x.business_id, x.policy_version_id },
                        principalTable: "hr_leave_policy_versions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_request_days_hr_leave_requests_business_id_leave_r~",
                        columns: x => new { x.business_id, x.leave_request_id },
                        principalTable: "hr_leave_requests",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_request_days_hr_operational_location_projections_b~",
                        columns: x => new { x.business_id, x.operational_location_projection_id },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_hr_leave_request_attachment_version",
                table: "hr_leave_request_attachment_versions",
                columns: new[] { "business_id", "leave_request_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_request_days_business_id_employee_id",
                table: "hr_leave_request_days",
                columns: new[] { "business_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_request_days_business_id_operational_location_proj~",
                table: "hr_leave_request_days",
                columns: new[] { "business_id", "operational_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_request_days_business_id_policy_version_id",
                table: "hr_leave_request_days",
                columns: new[] { "business_id", "policy_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_hr_leave_request_day_date",
                table: "hr_leave_request_days",
                columns: new[] { "business_id", "leave_request_id", "work_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_requests_business_id_leave_type_id",
                table: "hr_leave_requests",
                columns: new[] { "business_id", "leave_type_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_requests_business_id_scope_location_projection_id",
                table: "hr_leave_requests",
                columns: new[] { "business_id", "scope_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_leave_request_employee_dates_status",
                table: "hr_leave_requests",
                columns: new[] { "business_id", "employee_id", "start_date", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_hr_leave_request_approval_instance",
                table: "hr_leave_requests",
                columns: new[] { "business_id", "approval_instance_id" },
                unique: true,
                filter: "approval_instance_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_leave_request_attachment_versions");

            migrationBuilder.DropTable(
                name: "hr_leave_request_days");

            migrationBuilder.DropTable(
                name: "hr_leave_requests");
        }
    }
}
