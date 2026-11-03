using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosV2P4SaleSalesperson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "salesperson_user_id",
                table: "sales",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "i_x_sales_salesperson_user_id",
                table: "sales",
                column: "salesperson_user_id");

            migrationBuilder.AddForeignKey(
                name: "f_k_sales__users_salesperson_user_id",
                table: "sales",
                column: "salesperson_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_sales__users_salesperson_user_id",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "i_x_sales_salesperson_user_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "salesperson_user_id",
                table: "sales");
        }
    }
}
