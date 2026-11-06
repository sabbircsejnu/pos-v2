using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class StockTransferReconciliationAndSystemReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "original_transfer_id",
                table: "stock_transfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "damaged_quantity",
                table: "stock_transfer_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "missing_quantity",
                table: "stock_transfer_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "i_x_stock_transfers_original_transfer_id",
                table: "stock_transfers",
                column: "original_transfer_id",
                unique: true,
                filter: "original_transfer_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_stock_transfer_items_discrepancy_not_negative",
                table: "stock_transfer_items",
                sql: "missing_quantity >= 0 AND damaged_quantity >= 0");

            migrationBuilder.AddForeignKey(
                name: "f_k_stock_transfers_stock_transfers_original_transfer_id",
                table: "stock_transfers",
                column: "original_transfer_id",
                principalTable: "stock_transfers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_stock_transfers_stock_transfers_original_transfer_id",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "i_x_stock_transfers_original_transfer_id",
                table: "stock_transfers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_stock_transfer_items_discrepancy_not_negative",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "original_transfer_id",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "damaged_quantity",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "missing_quantity",
                table: "stock_transfer_items");
        }
    }
}
