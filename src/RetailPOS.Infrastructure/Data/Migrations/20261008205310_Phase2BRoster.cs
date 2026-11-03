using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2BRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_roster_assignments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    shift_id = table.Column<long>(type: "bigint", nullable: false),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    work_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    day_classification = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    published_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    shift_snapshot_json = table.Column<string>(type: "text", nullable: true),
                    location_snapshot_json = table.Column<string>(type: "text", nullable: true),
                    calendar_evidence_json = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_roster_assignments", x => x.id);
                    table.CheckConstraint("ck_hr_roster_day", "day_classification IN ('NormalWorkingDay','Holiday','WeeklyOff')");
                    table.CheckConstraint("ck_hr_roster_kind", "work_kind IN ('Normal','Extended','Special')");
                    table.CheckConstraint("ck_hr_roster_publication", "status <> 'Published' OR (published_at_utc IS NOT NULL AND published_by_user_id IS NOT NULL AND shift_snapshot_json IS NOT NULL AND location_snapshot_json IS NOT NULL AND calendar_evidence_json IS NOT NULL)");
                    table.CheckConstraint("ck_hr_roster_status", "status IN ('Draft','Published','Cancelled')");
                    table.CheckConstraint("ck_hr_roster_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_roster_assignments_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_roster_assignments_hr_employees_business_id_employee_id",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_roster_assignments_hr_operational_location_projections_b~",
                        columns: x => new { x.business_id, x.operational_location_projection_id },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_roster_assignments_hr_shifts_shift_id",
                        column: x => x.shift_id,
                        principalTable: "hr_shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_roster_assignments_business_id_employee_id",
                table: "hr_roster_assignments",
                columns: new[] { "business_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_roster_assignments_business_id_operational_location_proj~",
                table: "hr_roster_assignments",
                columns: new[] { "business_id", "operational_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_roster_assignments_employee_id_work_date",
                table: "hr_roster_assignments",
                columns: new[] { "employee_id", "work_date" },
                unique: true,
                filter: "status <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_roster_assignments_shift_id",
                table: "hr_roster_assignments",
                column: "shift_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_roster_assignments");
        }
    }
}
