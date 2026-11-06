using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosV2OutletInvoicePrintSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bin_number",
                table: "outlets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invoice_print_branch_name",
                table: "outlets",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mushak",
                table: "outlets",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_exchange_policy",
                table: "outlets",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vat_law_notification",
                table: "outlets",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bin_number",
                table: "outlets");

            migrationBuilder.DropColumn(
                name: "invoice_print_branch_name",
                table: "outlets");

            migrationBuilder.DropColumn(
                name: "mushak",
                table: "outlets");

            migrationBuilder.DropColumn(
                name: "return_exchange_policy",
                table: "outlets");

            migrationBuilder.DropColumn(
                name: "vat_law_notification",
                table: "outlets");
        }
    }
}
