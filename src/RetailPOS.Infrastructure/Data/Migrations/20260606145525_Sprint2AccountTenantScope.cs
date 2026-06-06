using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sprint2AccountTenantScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "business_id",
                table: "accounts",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "i_x_accounts_business_id",
                table: "accounts",
                column: "business_id");

            migrationBuilder.AddForeignKey(
                name: "f_k_accounts__businesses_business_id",
                table: "accounts",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_accounts__businesses_business_id",
                table: "accounts");

            migrationBuilder.DropIndex(
                name: "i_x_accounts_business_id",
                table: "accounts");

            migrationBuilder.DropColumn(
                name: "business_id",
                table: "accounts");
        }
    }
}
