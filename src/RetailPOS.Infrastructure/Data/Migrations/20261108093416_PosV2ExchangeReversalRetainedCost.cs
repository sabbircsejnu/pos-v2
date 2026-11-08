using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosV2ExchangeReversalRetainedCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "retained_packaging_cost",
                table: "exchange_reversals",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_exchange_reversals_retained_packaging_cost",
                table: "exchange_reversals",
                sql: "retained_packaging_cost >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_exchange_reversals_retained_packaging_cost",
                table: "exchange_reversals");

            migrationBuilder.DropColumn(
                name: "retained_packaging_cost",
                table: "exchange_reversals");
        }
    }
}
