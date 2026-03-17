using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceProductEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "has_variants",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "image_url",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "sku",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "product_variants",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "has_variants",
                table: "products");

            migrationBuilder.DropColumn(
                name: "image_url",
                table: "products");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "products");

            migrationBuilder.DropColumn(
                name: "sku",
                table: "products");

            migrationBuilder.DropColumn(
                name: "name",
                table: "product_variants");
        }
    }
}
