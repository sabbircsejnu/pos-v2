using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5DBusinessMonetaryConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "monetary_configuration_version_id",
                table: "payroll_runs",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payroll_monetary_configuration_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    decimal_precision = table.Column<int>(type: "integer", nullable: false),
                    rounding_mode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_monetary_configuration_versions", x => x.id);
                    table.UniqueConstraint("a_k_payroll_monetary_configuration_versions_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_monetary_configuration_currency", "length(currency_code) = 3 AND currency_code = upper(currency_code)");
                    table.CheckConstraint("ck_payroll_monetary_configuration_precision", "decimal_precision BETWEEN 0 AND 6");
                    table.CheckConstraint("ck_payroll_monetary_configuration_rounding", "rounding_mode IN ('ToEven', 'AwayFromZero', 'ToZero', 'ToNegativeInfinity', 'ToPositiveInfinity')");
                    table.CheckConstraint("ck_payroll_monetary_configuration_version", "version_no > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_monetary_configuration_versions_businesses_business~",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_runs_business_id_monetary_configuration_version_id",
                table: "payroll_runs",
                columns: new[] { "business_id", "monetary_configuration_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_monetary_configuration_effective_from",
                table: "payroll_monetary_configuration_versions",
                columns: new[] { "business_id", "effective_from" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_monetary_configuration_version",
                table: "payroll_monetary_configuration_versions",
                columns: new[] { "business_id", "version_no" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_runs_payroll_monetary_configuration_versions_busine~",
                table: "payroll_runs",
                columns: new[] { "business_id", "monetary_configuration_version_id" },
                principalTable: "payroll_monetary_configuration_versions",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_runs_payroll_monetary_configuration_versions_busine~",
                table: "payroll_runs");

            migrationBuilder.DropTable(
                name: "payroll_monetary_configuration_versions");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_runs_business_id_monetary_configuration_version_id",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "monetary_configuration_version_id",
                table: "payroll_runs");
        }
    }
}
