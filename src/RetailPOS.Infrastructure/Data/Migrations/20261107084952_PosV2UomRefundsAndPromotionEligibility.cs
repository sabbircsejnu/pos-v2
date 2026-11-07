using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosV2UomRefundsAndPromotionEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_sale_financial_postings_sale_kind",
                table: "sale_financial_postings");

            migrationBuilder.AddColumn<bool>(
                name: "apply_automatic_pricing_rules",
                table: "variant_selling_options",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "net_amount",
                table: "sale_return_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "sale_refund_id",
                table: "sale_return_lines",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "vat_amount",
                table: "sale_return_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "returned_quantity",
                table: "sale_items",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "sale_refund_id",
                table: "sale_financial_postings",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sale_refunds",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: true),
                    sale_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    refund_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: false),
                    dispensing_terminal_id = table.Column<long>(type: "bigint", nullable: true),
                    net_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_restored = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cash_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_sale_refunds", x => x.id);
                    table.CheckConstraint("ck_sale_refunds_amounts", "total_amount = net_amount + vat_amount AND sequence >= 1");
                    table.ForeignKey(
                        name: "f_k_sale_refunds__users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_sale_refunds_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_refund_payments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sale_refund_id = table.Column<long>(type: "bigint", nullable: false),
                    sale_payment_id = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_sale_refund_payments", x => x.id);
                    table.ForeignKey(
                        name: "f_k_sale_refund_payments_sale_payments_sale_payment_id",
                        column: x => x.sale_payment_id,
                        principalTable: "sale_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_sale_refund_payments_sale_refunds_sale_refund_id",
                        column: x => x.sale_refund_id,
                        principalTable: "sale_refunds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_sale_return_lines_sale_refund_id",
                table: "sale_return_lines",
                column: "sale_refund_id");

            // Refunds from before this change recorded their quantities only in the posting snapshot. This fills the new
            // counter from there. No existing quantity, amount, snapshot or journal changes, and their sales stay closed.
            migrationBuilder.Sql("""
                UPDATE sale_items item
                SET returned_quantity = LEAST(item.quantity, returned.quantity)
                FROM (
                    SELECT (line ->> 'saleItemId')::bigint AS sale_item_id, SUM((line ->> 'quantity')::numeric) AS quantity
                    FROM sale_financial_postings posting
                    CROSS JOIN LATERAL jsonb_array_elements(posting.snapshot_json::jsonb -> 'lines') AS line
                    WHERE posting.kind = 'reversal' AND posting.reversal_type = 'refund'
                    GROUP BY 1
                ) returned
                WHERE item.id = returned.sale_item_id;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_sale_items_returned_quantity",
                table: "sale_items",
                sql: "returned_quantity >= 0 AND returned_quantity <= quantity");

            migrationBuilder.CreateIndex(
                name: "ux_sale_financial_postings_sale_kind",
                table: "sale_financial_postings",
                columns: new[] { "sale_id", "kind" },
                unique: true,
                filter: "sale_refund_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_sale_financial_postings_sale_refund",
                table: "sale_financial_postings",
                column: "sale_refund_id",
                unique: true,
                filter: "sale_refund_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_sale_refund_payments_sale_payment_id",
                table: "sale_refund_payments",
                column: "sale_payment_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sale_refund_payments_sale_refund_id_sale_payment_id",
                table: "sale_refund_payments",
                columns: new[] { "sale_refund_id", "sale_payment_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_sale_refunds_actor_user_id",
                table: "sale_refunds",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sale_refunds_sale_id_idempotency_key",
                table: "sale_refunds",
                columns: new[] { "sale_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_sale_refunds_sale_id_sequence",
                table: "sale_refunds",
                columns: new[] { "sale_id", "sequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "f_k_sale_financial_postings_sale_refunds_sale_refund_id",
                table: "sale_financial_postings",
                column: "sale_refund_id",
                principalTable: "sale_refunds",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_sale_return_lines_sale_refunds_sale_refund_id",
                table: "sale_return_lines",
                column: "sale_refund_id",
                principalTable: "sale_refunds",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_sale_financial_postings_sale_refunds_sale_refund_id",
                table: "sale_financial_postings");

            migrationBuilder.DropForeignKey(
                name: "f_k_sale_return_lines_sale_refunds_sale_refund_id",
                table: "sale_return_lines");

            migrationBuilder.DropTable(
                name: "sale_refund_payments");

            migrationBuilder.DropTable(
                name: "sale_refunds");

            migrationBuilder.DropIndex(
                name: "i_x_sale_return_lines_sale_refund_id",
                table: "sale_return_lines");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sale_items_returned_quantity",
                table: "sale_items");

            migrationBuilder.DropIndex(
                name: "ux_sale_financial_postings_sale_kind",
                table: "sale_financial_postings");

            migrationBuilder.DropIndex(
                name: "ux_sale_financial_postings_sale_refund",
                table: "sale_financial_postings");

            migrationBuilder.DropColumn(
                name: "apply_automatic_pricing_rules",
                table: "variant_selling_options");

            migrationBuilder.DropColumn(
                name: "net_amount",
                table: "sale_return_lines");

            migrationBuilder.DropColumn(
                name: "sale_refund_id",
                table: "sale_return_lines");

            migrationBuilder.DropColumn(
                name: "vat_amount",
                table: "sale_return_lines");

            migrationBuilder.DropColumn(
                name: "returned_quantity",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "sale_refund_id",
                table: "sale_financial_postings");

            migrationBuilder.CreateIndex(
                name: "ux_sale_financial_postings_sale_kind",
                table: "sale_financial_postings",
                columns: new[] { "sale_id", "kind" },
                unique: true);
        }
    }
}
