using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1CPositionManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_positions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    org_unit_id = table.Column<long>(type: "bigint", nullable: false),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: true),
                    reports_to_position_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_positions", x => x.id);
                    table.UniqueConstraint("a_k_hr_positions_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_position_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_position_effective_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_hr_position_not_self_reporting", "reports_to_position_id IS NULL OR reports_to_position_id <> id");
                    table.CheckConstraint("ck_hr_position_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_positions_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_positions_hr_operational_location_projections_business_i~",
                        columns: x => new { x.business_id, x.operational_location_projection_id },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_positions_hr_org_units_business_id_org_unit_id",
                        columns: x => new { x.business_id, x.org_unit_id },
                        principalTable: "hr_org_units",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_positions_hr_positions_business_id_reports_to_position_id",
                        columns: x => new { x.business_id, x.reports_to_position_id },
                        principalTable: "hr_positions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_positions_business_id_code",
                table: "hr_positions",
                columns: new[] { "business_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_positions_business_id_operational_location_projection_id",
                table: "hr_positions",
                columns: new[] { "business_id", "operational_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_positions_business_id_org_unit_id",
                table: "hr_positions",
                columns: new[] { "business_id", "org_unit_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_positions_business_id_reports_to_position_id",
                table: "hr_positions",
                columns: new[] { "business_id", "reports_to_position_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_positions");
        }
    }
}
