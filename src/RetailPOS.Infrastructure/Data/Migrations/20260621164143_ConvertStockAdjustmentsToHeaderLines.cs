using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvertStockAdjustmentsToHeaderLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_adjustment_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    stock_adjustment_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    previous_quantity = table.Column<int>(type: "integer", nullable: false),
                    quantity_change = table.Column<int>(type: "integer", nullable: false),
                    new_quantity = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_adjustment_lines", x => x.id);
                    table.ForeignKey(
                        name: "f_k_stock_adjustment_lines_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_stock_adjustment_lines_stock_adjustments_stock_adjustment_id",
                        column: x => x.stock_adjustment_id,
                        principalTable: "stock_adjustments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_stock_adjustment_lines_stock_adjustment_id",
                table: "stock_adjustment_lines",
                column: "stock_adjustment_id");

            migrationBuilder.CreateIndex(
                name: "i_x_stock_adjustment_lines_variant_id",
                table: "stock_adjustment_lines",
                column: "variant_id");

            migrationBuilder.Sql(@"
                INSERT INTO stock_adjustment_lines
                (
                    stock_adjustment_id,
                    variant_id,
                    previous_quantity,
                    quantity_change,
                    new_quantity,
                    reason,
                    notes,
                    created_at,
                    updated_at
                )
                SELECT
                    sa.id,
                    sa.variant_id,
                    sa.previous_quantity,
                    sa.quantity_change,
                    sa.new_quantity,
                    sa.reason,
                    sa.notes,
                    sa.created_at,
                    sa.updated_at
                FROM stock_adjustments sa
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM stock_adjustment_lines sal
                    WHERE sal.stock_adjustment_id = sa.id
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_adjustment_lines");
        }
    }
}
