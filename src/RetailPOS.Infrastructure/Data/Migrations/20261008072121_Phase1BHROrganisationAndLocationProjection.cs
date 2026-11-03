using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1BHROrganisationAndLocationProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "source_version",
                table: "warehouses",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.AddColumn<long>(
                name: "source_version",
                table: "outlets",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "hr_operational_location_projections",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    source_system = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_entity_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    contact_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    manager_external_user_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    source_updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    source_version = table.Column<long>(type: "bigint", nullable: false),
                    source_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_synced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_source_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_source_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    sync_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_operational_location_projections", x => x.id);
                    table.UniqueConstraint("a_k_hr_operational_location_projections_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_location_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_location_source_type", "source_entity_type IN ('Outlet', 'Warehouse')");
                    table.CheckConstraint("ck_hr_location_source_version", "source_version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_operational_location_projections_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_org_units",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    unit_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    parent_org_unit_id = table.Column<long>(type: "bigint", nullable: true),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_org_units", x => x.id);
                    table.UniqueConstraint("a_k_hr_org_units_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_org_unit_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_org_unit_effective_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_hr_org_unit_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_org_units_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_org_units_hr_operational_location_projections_business_i~",
                        columns: x => new { x.business_id, x.operational_location_projection_id },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_org_units_hr_org_units_business_id_parent_org_unit_id",
                        columns: x => new { x.business_id, x.parent_org_unit_id },
                        principalTable: "hr_org_units",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_operational_location_projections_business_id_name",
                table: "hr_operational_location_projections",
                columns: new[] { "business_id", "name" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_operational_location_projections_business_id_source_enti~",
                table: "hr_operational_location_projections",
                columns: new[] { "business_id", "source_entity_type" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_operational_location_projections_business_id_source_syst~",
                table: "hr_operational_location_projections",
                columns: new[] { "business_id", "source_system", "source_entity_type", "source_entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_org_units_business_id_code",
                table: "hr_org_units",
                columns: new[] { "business_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_org_units_business_id_operational_location_projection_id",
                table: "hr_org_units",
                columns: new[] { "business_id", "operational_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_org_units_business_id_parent_org_unit_id",
                table: "hr_org_units",
                columns: new[] { "business_id", "parent_org_unit_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_org_units");

            migrationBuilder.DropTable(
                name: "hr_operational_location_projections");

            migrationBuilder.DropColumn(
                name: "source_version",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "source_version",
                table: "outlets");
        }
    }
}
