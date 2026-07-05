using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptPrintHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "receipt_print_histories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sale_id = table.Column<long>(type: "bigint", nullable: false),
                    terminal_id = table.Column<long>(type: "bigint", nullable: true),
                    printed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    action_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    printed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_receipt_print_histories", x => x.id);
                    table.ForeignKey(
                        name: "f_k_receipt_print_histories__sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_receipt_print_histories__users_printed_by_user_id",
                        column: x => x.printed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_receipt_print_histories_pos_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalTable: "pos_terminals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_receipt_print_histories_printed_by_user_id",
                table: "receipt_print_histories",
                column: "printed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "i_x_receipt_print_histories_sale_id",
                table: "receipt_print_histories",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "i_x_receipt_print_histories_terminal_id",
                table: "receipt_print_histories",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_print_histories_printed_at",
                table: "receipt_print_histories",
                column: "printed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "receipt_print_histories");
        }
    }
}
