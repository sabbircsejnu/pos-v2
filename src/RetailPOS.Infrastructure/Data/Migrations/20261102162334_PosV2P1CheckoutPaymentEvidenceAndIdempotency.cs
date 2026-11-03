using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosV2P1CheckoutPaymentEvidenceAndIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_fingerprint",
                table: "sales",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "change_given",
                table: "sale_payments",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "net_cash_retained",
                table: "sale_payments",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            // Derive the new evidence columns from facts already stored. Cash rows without a stored tender keep
            // NULL tender/change (unknown evidence) but their allocated amount is, by definition, what the drawer retained.
            migrationBuilder.Sql(
                """
                UPDATE sale_payments
                SET net_cash_retained = amount
                WHERE method = 'cash' AND net_cash_retained IS NULL;

                UPDATE sale_payments
                SET change_given = GREATEST(tendered - amount, 0)
                WHERE method = 'cash' AND tendered IS NOT NULL AND change_given IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "idempotency_fingerprint",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "change_given",
                table: "sale_payments");

            migrationBuilder.DropColumn(
                name: "net_cash_retained",
                table: "sale_payments");
        }
    }
}
