using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStockAdjustmentLegacyHeaderFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_stock_adjustments_product_variants_variant_id",
                table: "stock_adjustments");

            migrationBuilder.DropIndex(
                name: "i_x_stock_adjustments_variant_id",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "new_quantity",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "previous_quantity",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "quantity_change",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "reason",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "variant_id",
                table: "stock_adjustments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "new_quantity",
                table: "stock_adjustments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "stock_adjustments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "previous_quantity",
                table: "stock_adjustments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "quantity_change",
                table: "stock_adjustments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "reason",
                table: "stock_adjustments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "variant_id",
                table: "stock_adjustments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "i_x_stock_adjustments_variant_id",
                table: "stock_adjustments",
                column: "variant_id");

            migrationBuilder.AddForeignKey(
                name: "f_k_stock_adjustments_product_variants_variant_id",
                table: "stock_adjustments",
                column: "variant_id",
                principalTable: "product_variants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
