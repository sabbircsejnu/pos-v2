using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2CDevicesAndMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_attendance_devices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    device_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    serial_number = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    vendor_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    protocol_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    connection_host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: true),
                    connection_port = table.Column<int>(type: "integer", nullable: true),
                    credential_secret_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    firmware_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    device_time_zone_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_attendance_devices", x => x.id);
                    table.UniqueConstraint("a_k_hr_attendance_devices_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_device_port", "connection_port IS NULL OR connection_port BETWEEN 1 AND 65535");
                    table.CheckConstraint("ck_hr_device_status", "status IN ('Active','Offline','Disabled','Error')");
                    table.CheckConstraint("ck_hr_device_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_attendance_devices_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_attendance_devices_hr_operational_location_projections_b~",
                        columns: x => new { x.business_id, x.operational_location_projection_id },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_device_user_mappings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    device_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    device_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_device_user_mappings", x => x.id);
                    table.CheckConstraint("ck_hr_mapping_interval", "effective_to_utc IS NULL OR effective_to_utc > effective_from_utc");
                    table.CheckConstraint("ck_hr_mapping_open", "is_active = (effective_to_utc IS NULL)");
                    table.CheckConstraint("ck_hr_mapping_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_device_user_mappings_hr_attendance_devices_business_id_d~",
                        columns: x => new { x.business_id, x.device_id },
                        principalTable: "hr_attendance_devices",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_device_user_mappings_hr_employees_business_id_employee_id",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_attendance_devices_business_id_device_code",
                table: "hr_attendance_devices",
                columns: new[] { "business_id", "device_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_attendance_devices_business_id_operational_location_proj~",
                table: "hr_attendance_devices",
                columns: new[] { "business_id", "operational_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_attendance_devices_vendor_code_serial_number",
                table: "hr_attendance_devices",
                columns: new[] { "vendor_code", "serial_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_device_user_mappings_business_id_device_id",
                table: "hr_device_user_mappings",
                columns: new[] { "business_id", "device_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_device_user_mappings_business_id_employee_id",
                table: "hr_device_user_mappings",
                columns: new[] { "business_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_device_user_mappings_device_id_device_user_id",
                table: "hr_device_user_mappings",
                columns: new[] { "device_id", "device_user_id" },
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_device_user_mappings_employee_id_is_active",
                table: "hr_device_user_mappings",
                columns: new[] { "employee_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_device_user_mappings");

            migrationBuilder.DropTable(
                name: "hr_attendance_devices");
        }
    }
}
