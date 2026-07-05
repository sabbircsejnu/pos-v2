using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockAdjustmentSourceStockCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "source_stock_count_id",
                table: "stock_adjustments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustments_source_stock_count_id",
                table: "stock_adjustments",
                column: "source_stock_count_id",
                unique: true,
                filter: "source_stock_count_id is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_stock_adjustments_source_stock_count_id",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "source_stock_count_id",
                table: "stock_adjustments");
        }
    }
}
