using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseOrderOutletDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "warehouse_id",
                table: "purchase_orders",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "outlet_id",
                table: "purchase_orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_outlet_id_idempotency_key",
                table: "purchase_orders",
                columns: new[] { "outlet_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL AND outlet_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchase_orders_single_destination",
                table: "purchase_orders",
                sql: "(warehouse_id IS NOT NULL AND outlet_id IS NULL) OR (warehouse_id IS NULL AND outlet_id IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "f_k_purchase_orders_outlets_outlet_id",
                table: "purchase_orders",
                column: "outlet_id",
                principalTable: "outlets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_purchase_orders_outlets_outlet_id",
                table: "purchase_orders");

            migrationBuilder.DropIndex(
                name: "IX_purchase_orders_outlet_id_idempotency_key",
                table: "purchase_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_purchase_orders_single_destination",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "outlet_id",
                table: "purchase_orders");

            migrationBuilder.AlterColumn<long>(
                name: "warehouse_id",
                table: "purchase_orders",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
