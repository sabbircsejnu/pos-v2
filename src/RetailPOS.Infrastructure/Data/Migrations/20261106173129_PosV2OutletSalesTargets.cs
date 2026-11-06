using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosV2OutletSalesTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outlet_sales_target_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_outlet_sales_target_versions", x => x.id);
                    table.ForeignKey(
                        name: "f_k_outlet_sales_target_versions_outlets_outlet_id",
                        column: x => x.outlet_id,
                        principalTable: "outlets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outlet_sales_target_overrides",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    period_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    period_from = table.Column<DateOnly>(type: "date", nullable: false),
                    period_to = table.Column<DateOnly>(type: "date", nullable: false),
                    calculated_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    override_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    version_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cleared_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cleared_by_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_outlet_sales_target_overrides", x => x.id);
                    table.CheckConstraint("ck_outlet_sales_target_overrides_amounts", "override_amount >= 0 AND calculated_amount >= 0");
                    table.CheckConstraint("ck_outlet_sales_target_overrides_period_type", "period_type IN ('weekly', 'first_half', 'second_half', 'monthly')");
                    table.CheckConstraint("ck_outlet_sales_target_overrides_range", "period_to >= period_from");
                    table.ForeignKey(
                        name: "f_k_outlet_sales_target_overrides_outlet_sales_target_versions_~",
                        column: x => x.version_id,
                        principalTable: "outlet_sales_target_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.NoAction);
                    table.ForeignKey(
                        name: "f_k_outlet_sales_target_overrides_outlets_outlet_id",
                        column: x => x.outlet_id,
                        principalTable: "outlets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outlet_sales_target_version_days",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    version_id = table.Column<long>(type: "bigint", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    is_off_day = table.Column<bool>(type: "boolean", nullable: false),
                    target_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_outlet_sales_target_version_days", x => x.id);
                    table.CheckConstraint("ck_outlet_sales_target_version_days_amount", "target_amount IS NULL OR target_amount >= 0");
                    table.CheckConstraint("ck_outlet_sales_target_version_days_off_day_no_amount", "NOT is_off_day OR target_amount IS NULL");
                    table.CheckConstraint("ck_outlet_sales_target_version_days_weekday", "day_of_week BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "f_k_outlet_sales_target_version_days_outlet_sales_target_versio~",
                        column: x => x.version_id,
                        principalTable: "outlet_sales_target_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_outlet_sales_target_overrides_business_id_outlet_id_period_~",
                table: "outlet_sales_target_overrides",
                columns: new[] { "business_id", "outlet_id", "period_from" });

            migrationBuilder.CreateIndex(
                name: "i_x_outlet_sales_target_overrides_version_id",
                table: "outlet_sales_target_overrides",
                column: "version_id");

            migrationBuilder.CreateIndex(
                name: "ux_outlet_sales_target_overrides_active_period",
                table: "outlet_sales_target_overrides",
                columns: new[] { "outlet_id", "period_type", "period_from" },
                unique: true,
                filter: "cleared_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_outlet_sales_target_version_days_version_id_day_of_week",
                table: "outlet_sales_target_version_days",
                columns: new[] { "version_id", "day_of_week" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_outlet_sales_target_versions_business_id_outlet_id_effectiv~",
                table: "outlet_sales_target_versions",
                columns: new[] { "business_id", "outlet_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ux_outlet_sales_target_versions_outlet_version",
                table: "outlet_sales_target_versions",
                columns: new[] { "outlet_id", "version_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outlet_sales_target_overrides");

            migrationBuilder.DropTable(
                name: "outlet_sales_target_version_days");

            migrationBuilder.DropTable(
                name: "outlet_sales_target_versions");
        }
    }
}
