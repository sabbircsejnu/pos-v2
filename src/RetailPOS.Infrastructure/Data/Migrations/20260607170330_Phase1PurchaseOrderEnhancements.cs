using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1PurchaseOrderEnhancements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "purchase_orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "purchase_orders",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "po_number",
                table: "purchase_orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "purchase_orders",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "discount",
                table: "purchase_order_items",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "tax",
                table: "purchase_order_items",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "unit",
                table: "purchase_order_items",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // Backfill po_number for any existing rows before adding the unique index.
            // Format: PO-YYYYMMDD-{id:0000}
            migrationBuilder.Sql(@"
                UPDATE purchase_orders
                SET po_number = 'PO-' || TO_CHAR(order_date, 'YYYYMMDD') || '-' || LPAD(id::text, 4, '0')
                WHERE po_number = '';
            ");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_po_number",
                table: "purchase_orders",
                column: "po_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_purchase_orders_po_number",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "po_number",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "purchase_orders");

            migrationBuilder.DropColumn(
                name: "discount",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "tax",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "unit",
                table: "purchase_order_items");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "purchase_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);
        }
    }
}
