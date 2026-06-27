using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add the status column with a safe default (1 = Active).
            migrationBuilder.AddColumn<short>(
                name: "status",
                table: "products",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            // 2. Migrate existing data: Active → 1, Inactive → 2.
            migrationBuilder.Sql(
                "UPDATE products SET status = CASE WHEN is_active THEN 1 ELSE 2 END;");

            // 3. Now that status is populated, remove the superseded boolean column.
            migrationBuilder.DropColumn(
                name: "is_active",
                table: "products");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "products",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql(
                "UPDATE products SET is_active = (status = 1);");

            migrationBuilder.DropColumn(
                name: "status",
                table: "products");
        }
    }
}
