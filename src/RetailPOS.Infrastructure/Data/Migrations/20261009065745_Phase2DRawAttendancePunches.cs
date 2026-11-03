using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2DRawAttendancePunches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_raw_attendance_punches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    device_id = table.Column<long>(type: "bigint", nullable: true),
                    device_serial_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    device_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    employee_id = table.Column<long>(type: "bigint", nullable: true),
                    punch_time_local = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    punch_time_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    time_zone_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    verification_method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    direction_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    raw_payload = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    idempotency_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    processing_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    processing_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    processing_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_raw_attendance_punches", x => x.id);
                    table.UniqueConstraint("a_k_hr_raw_attendance_punches_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_raw_punch_source", "source_type IN ('Manual','Device','Import')");
                    table.CheckConstraint("ck_hr_raw_punch_status", "processing_status IN ('Pending','Unmapped','Processed','Failed')");
                    table.CheckConstraint("ck_hr_raw_punch_version", "processing_version >= 0");
                    table.ForeignKey(
                        name: "f_k_hr_raw_attendance_punches_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_raw_attendance_punches_hr_attendance_devices_business_id~",
                        columns: x => new { x.business_id, x.device_id },
                        principalTable: "hr_attendance_devices",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_raw_attendance_punches_hr_employees_business_id_employee~",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_raw_attendance_punches_business_id_device_id",
                table: "hr_raw_attendance_punches",
                columns: new[] { "business_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_raw_attendance_punches_business_id_employee_id_punch_tim~",
                table: "hr_raw_attendance_punches",
                columns: new[] { "business_id", "employee_id", "punch_time_local" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_raw_attendance_punches_business_id_processing_status_rec~",
                table: "hr_raw_attendance_punches",
                columns: new[] { "business_id", "processing_status", "received_at_utc" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_raw_attendance_punches_idempotency_fingerprint",
                table: "hr_raw_attendance_punches",
                column: "idempotency_fingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_raw_attendance_punches");
        }
    }
}
