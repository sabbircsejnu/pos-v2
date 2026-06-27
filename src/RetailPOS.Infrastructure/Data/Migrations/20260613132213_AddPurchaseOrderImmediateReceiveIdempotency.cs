using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchaseOrderImmediateReceiveIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "i_x_purchase_orders_warehouse_id",
                table: "purchase_orders");

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "purchase_orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_warehouse_id_idempotency_key",
                table: "purchase_orders",
                columns: new[] { "warehouse_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_purchase_orders_warehouse_id_idempotency_key",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "purchase_orders");

            migrationBuilder.CreateIndex(
                name: "i_x_purchase_orders_warehouse_id",
                table: "purchase_orders",
                column: "warehouse_id");
        }
    }
}
