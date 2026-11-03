using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2FAttendanceCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_attendance_correction_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    daily_attendance_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    correction_sequence = table.Column<int>(type: "integer", nullable: false),
                    expected_processing_version = table.Column<int>(type: "integer", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    before_evidence_json = table.Column<string>(type: "jsonb", nullable: false),
                    proposed_evidence_json = table.Column<string>(type: "jsonb", nullable: false),
                    applied_after_evidence_json = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    decided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_attendance_correction_requests", x => x.id);
                    table.CheckConstraint("ck_hr_attendance_correction_sequence", "correction_sequence > 0 AND expected_processing_version > 0 AND version > 0");
                    table.CheckConstraint("ck_hr_attendance_correction_status", "status IN ('PendingApproval','Approved','Rejected','Cancelled')");
                    table.ForeignKey(
                        name: "f_k_hr_attendance_correction_requests_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_attendance_correction_requests_hr_approval_instances_app~",
                        column: x => x.approval_instance_id,
                        principalTable: "hr_approval_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_attendance_correction_requests_hr_daily_attendance_busin~",
                        columns: x => new { x.business_id, x.daily_attendance_id },
                        principalTable: "hr_daily_attendance",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_hr_att_corr_approval_instance",
                table: "hr_attendance_correction_requests",
                column: "approval_instance_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_hr_att_corr_business_daily_sequence",
                table: "hr_attendance_correction_requests",
                columns: new[] { "business_id", "daily_attendance_id", "correction_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_hr_att_corr_pending_per_daily",
                table: "hr_attendance_correction_requests",
                columns: new[] { "business_id", "daily_attendance_id", "status" },
                unique: true,
                filter: "status = 'PendingApproval'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_attendance_correction_requests");
        }
    }
}
