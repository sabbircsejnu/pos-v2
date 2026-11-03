using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2AShiftHolidayWeeklyOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_holiday_calendars",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_holiday_calendars", x => x.id);
                    table.UniqueConstraint("a_k_hr_holiday_calendars_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_calendar_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_hr_calendar_scope", "(scope_type = 'Company' AND location_type IS NULL AND operational_location_projection_id IS NULL) OR (scope_type = 'LocationType' AND location_type IS NOT NULL AND location_type IN ('Outlet', 'Warehouse', 'Factory') AND operational_location_projection_id IS NULL) OR (scope_type = 'Location' AND location_type IS NULL AND operational_location_projection_id IS NOT NULL AND operational_location_projection_id > 0)");
                    table.CheckConstraint("ck_hr_calendar_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_holiday_calendars_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_holiday_calendars_hr_operational_location_projections_bu~",
                        columns: x => new { x.business_id, x.operational_location_projection_id },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_shifts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    start_time_local = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end_time_local = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    crosses_midnight = table.Column<bool>(type: "boolean", nullable: false),
                    minimum_work_minutes = table.Column<int>(type: "integer", nullable: false),
                    maximum_allowed_work_minutes = table.Column<int>(type: "integer", nullable: true),
                    allowed_early_punch_minutes = table.Column<int>(type: "integer", nullable: false),
                    allowed_late_punch_minutes = table.Column<int>(type: "integer", nullable: false),
                    minimum_rest_before_next_shift_minutes = table.Column<int>(type: "integer", nullable: true),
                    grace_in_minutes = table.Column<int>(type: "integer", nullable: false),
                    grace_out_minutes = table.Column<int>(type: "integer", nullable: false),
                    overtime_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_shifts", x => x.id);
                    table.CheckConstraint("ck_hr_shift_version", "version > 0");
                    table.CheckConstraint("ck_hr_shift_work", "minimum_work_minutes > 0 AND (maximum_allowed_work_minutes IS NULL OR maximum_allowed_work_minutes >= minimum_work_minutes)");
                    table.ForeignKey(
                        name: "f_k_hr_shifts_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_weekly_off_policy_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    scope_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: true),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_weekly_off_policy_versions", x => x.id);
                    table.CheckConstraint("ck_hr_weekly_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_hr_weekly_scope", "(scope_type = 'Company' AND location_type IS NULL AND operational_location_projection_id IS NULL) OR (scope_type = 'LocationType' AND location_type IS NOT NULL AND location_type IN ('Outlet', 'Warehouse', 'Factory') AND operational_location_projection_id IS NULL) OR (scope_type = 'Location' AND location_type IS NULL AND operational_location_projection_id IS NOT NULL AND operational_location_projection_id > 0)");
                    table.CheckConstraint("ck_hr_weekly_version", "version > 0 AND version_no > 0");
                    table.ForeignKey(
                        name: "f_k_hr_weekly_off_policy_versions_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_weekly_off_policy_versions_hr_operational_location_proje~",
                        columns: x => new { x.business_id, x.operational_location_projection_id },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_holidays",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    calendar_id = table.Column<long>(type: "bigint", nullable: false),
                    holiday_date = table.Column<DateOnly>(type: "date", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    holiday_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_holidays", x => x.id);
                    table.CheckConstraint("ck_hr_holiday_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_holidays_hr_holiday_calendars_business_id_calendar_id",
                        columns: x => new { x.business_id, x.calendar_id },
                        principalTable: "hr_holiday_calendars",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_shift_breaks",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    shift_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_shift_breaks", x => x.id);
                    table.CheckConstraint("ck_hr_shift_break", "sequence > 0 AND duration_minutes > 0");
                    table.ForeignKey(
                        name: "f_k_hr_shift_breaks_hr_shifts_shift_id",
                        column: x => x.shift_id,
                        principalTable: "hr_shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_weekly_off_policy_days",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    policy_version_id = table.Column<long>(type: "bigint", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_weekly_off_policy_days", x => x.id);
                    table.CheckConstraint("ck_hr_weekly_day", "day_of_week >= 0 AND day_of_week <= 6");
                    table.ForeignKey(
                        name: "f_k_hr_weekly_off_policy_days_hr_weekly_off_policy_versions_pol~",
                        column: x => x.policy_version_id,
                        principalTable: "hr_weekly_off_policy_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_holiday_calendars_business_id_operational_location_proje~",
                table: "hr_holiday_calendars",
                columns: new[] { "business_id", "operational_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_holidays_business_id_calendar_id",
                table: "hr_holidays",
                columns: new[] { "business_id", "calendar_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_holidays_calendar_id_code",
                table: "hr_holidays",
                columns: new[] { "calendar_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_shift_breaks_shift_id_sequence",
                table: "hr_shift_breaks",
                columns: new[] { "shift_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_shifts_business_id_code",
                table: "hr_shifts",
                columns: new[] { "business_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_weekly_off_policy_days_policy_version_id_day_of_week",
                table: "hr_weekly_off_policy_days",
                columns: new[] { "policy_version_id", "day_of_week" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_weekly_off_policy_versions_business_id_operational_locat~",
                table: "hr_weekly_off_policy_versions",
                columns: new[] { "business_id", "operational_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_weekly_off_policy_versions_business_id_scope_key_version~",
                table: "hr_weekly_off_policy_versions",
                columns: new[] { "business_id", "scope_key", "version_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_holidays");

            migrationBuilder.DropTable(
                name: "hr_shift_breaks");

            migrationBuilder.DropTable(
                name: "hr_weekly_off_policy_days");

            migrationBuilder.DropTable(
                name: "hr_holiday_calendars");

            migrationBuilder.DropTable(
                name: "hr_shifts");

            migrationBuilder.DropTable(
                name: "hr_weekly_off_policy_versions");
        }
    }
}
