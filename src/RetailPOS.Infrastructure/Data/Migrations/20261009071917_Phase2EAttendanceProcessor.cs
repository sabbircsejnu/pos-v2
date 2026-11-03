using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2EAttendanceProcessor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "a_k_hr_shifts_business_id_id",
                table: "hr_shifts",
                columns: new[] { "business_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "a_k_hr_roster_assignments_business_id_id",
                table: "hr_roster_assignments",
                columns: new[] { "business_id", "id" });

            migrationBuilder.CreateTable(
                name: "hr_daily_attendance",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    source_location_type_snapshot = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    shift_id = table.Column<long>(type: "bigint", nullable: false),
                    roster_assignment_id = table.Column<long>(type: "bigint", nullable: false),
                    day_classification = table.Column<string>(type: "text", nullable: false),
                    time_zone_id_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    first_in_at_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    last_out_at_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    worked_minutes = table.Column<int>(type: "integer", nullable: false),
                    break_minutes = table.Column<int>(type: "integer", nullable: false),
                    late_minutes = table.Column<int>(type: "integer", nullable: false),
                    early_leave_minutes = table.Column<int>(type: "integer", nullable: false),
                    overtime_minutes_calculated = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    processing_version = table.Column<int>(type: "integer", nullable: false),
                    last_processed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_manually_adjusted = table.Column<bool>(type: "boolean", nullable: false),
                    shift_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    location_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    calendar_evidence_json = table.Column<string>(type: "jsonb", nullable: false),
                    punch_ids_json = table.Column<string>(type: "jsonb", nullable: false),
                    calculation_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_daily_attendance", x => x.id);
                    table.UniqueConstraint("a_k_hr_daily_attendance_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_daily_attendance_minutes", "worked_minutes >= 0 AND break_minutes >= 0 AND late_minutes >= 0 AND early_leave_minutes >= 0 AND overtime_minutes_calculated >= 0");
                    table.CheckConstraint("ck_hr_daily_attendance_status", "status IN ('Present','Absent','Late','HalfDay','Leave','Holiday','WeeklyOff','MissingPunch')");
                    table.CheckConstraint("ck_hr_daily_attendance_version", "processing_version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_daily_attendance_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_daily_attendance_hr_employees_business_id_employee_id",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_daily_attendance_hr_operational_location_projections_bus~",
                        columns: x => new { x.business_id, x.operational_location_projection_id },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_daily_attendance_hr_roster_assignments_business_id_roste~",
                        columns: x => new { x.business_id, x.roster_assignment_id },
                        principalTable: "hr_roster_assignments",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_daily_attendance_hr_shifts_business_id_shift_id",
                        columns: x => new { x.business_id, x.shift_id },
                        principalTable: "hr_shifts",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_daily_attendance_business_id_employee_id_work_date",
                table: "hr_daily_attendance",
                columns: new[] { "business_id", "employee_id", "work_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_daily_attendance_business_id_operational_location_projec~",
                table: "hr_daily_attendance",
                columns: new[] { "business_id", "operational_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_daily_attendance_business_id_roster_assignment_id",
                table: "hr_daily_attendance",
                columns: new[] { "business_id", "roster_assignment_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_daily_attendance_business_id_shift_id",
                table: "hr_daily_attendance",
                columns: new[] { "business_id", "shift_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_daily_attendance_business_id_work_date_status",
                table: "hr_daily_attendance",
                columns: new[] { "business_id", "work_date", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_daily_attendance");

            migrationBuilder.DropUniqueConstraint(
                name: "a_k_hr_shifts_business_id_id",
                table: "hr_shifts");

            migrationBuilder.DropUniqueConstraint(
                name: "a_k_hr_roster_assignments_business_id_id",
                table: "hr_roster_assignments");
        }
    }
}
