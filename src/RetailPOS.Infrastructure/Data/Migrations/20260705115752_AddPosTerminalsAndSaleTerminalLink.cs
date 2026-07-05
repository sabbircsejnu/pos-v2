using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPosTerminalsAndSaleTerminalLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "terminal_id",
                table: "sales",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pos_terminals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_pos_terminals", x => x.id);
                    table.ForeignKey(
                        name: "f_k_pos_terminals_outlets_outlet_id",
                        column: x => x.outlet_id,
                        principalTable: "outlets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_sales_terminal_id",
                table: "sales",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "IX_pos_terminals_outlet_id_code",
                table: "pos_terminals",
                columns: new[] { "outlet_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pos_terminals_outlet_id_is_default",
                table: "pos_terminals",
                columns: new[] { "outlet_id", "is_default" });

            migrationBuilder.AddForeignKey(
                name: "f_k_sales_pos_terminals_terminal_id",
                table: "sales",
                column: "terminal_id",
                principalTable: "pos_terminals",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_sales_pos_terminals_terminal_id",
                table: "sales");

            migrationBuilder.DropTable(
                name: "pos_terminals");

            migrationBuilder.DropIndex(
                name: "i_x_sales_terminal_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "terminal_id",
                table: "sales");
        }
    }
}
