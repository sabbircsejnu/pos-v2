using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceCategoryEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_categories_categories_parent_id",
                table: "categories");

            migrationBuilder.RenameColumn(
                name: "parent_id",
                table: "categories",
                newName: "parent_category_id");

            migrationBuilder.RenameIndex(
                name: "i_x_categories_parent_id",
                table: "categories",
                newName: "i_x_categories_parent_category_id");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "categories",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "display_order",
                table: "categories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "image_url",
                table: "categories",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "f_k_categories_categories_parent_category_id",
                table: "categories",
                column: "parent_category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_categories_categories_parent_category_id",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "description",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "display_order",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "image_url",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "categories");

            migrationBuilder.RenameColumn(
                name: "parent_category_id",
                table: "categories",
                newName: "parent_id");

            migrationBuilder.RenameIndex(
                name: "i_x_categories_parent_category_id",
                table: "categories",
                newName: "i_x_categories_parent_id");

            migrationBuilder.AddForeignKey(
                name: "f_k_categories_categories_parent_id",
                table: "categories",
                column: "parent_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
