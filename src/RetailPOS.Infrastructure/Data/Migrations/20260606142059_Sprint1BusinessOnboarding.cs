using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Sprint1BusinessOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "business_id",
                table: "warehouses",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "business_id",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "must_reset_password",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "business_id",
                table: "outlets",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "businesses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_businesses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_invitations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_user_invitations", x => x.id);
                    table.ForeignKey(
                        name: "f_k_user_invitations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_warehouses_business_id",
                table: "warehouses",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "i_x_users_business_id",
                table: "users",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "i_x_outlets_business_id",
                table: "outlets",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "IX_businesses_name",
                table: "businesses",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_invitations_token_hash",
                table: "user_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_invitations_user_id_purpose_consumed_at",
                table: "user_invitations",
                columns: new[] { "user_id", "purpose", "consumed_at" });

            migrationBuilder.AddForeignKey(
                name: "f_k_outlets_businesses_business_id",
                table: "outlets",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_users_businesses_business_id",
                table: "users",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_warehouses_businesses_business_id",
                table: "warehouses",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_outlets_businesses_business_id",
                table: "outlets");

            migrationBuilder.DropForeignKey(
                name: "f_k_users_businesses_business_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "f_k_warehouses_businesses_business_id",
                table: "warehouses");

            migrationBuilder.DropTable(
                name: "businesses");

            migrationBuilder.DropTable(
                name: "user_invitations");

            migrationBuilder.DropIndex(
                name: "i_x_warehouses_business_id",
                table: "warehouses");

            migrationBuilder.DropIndex(
                name: "i_x_users_business_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "i_x_outlets_business_id",
                table: "outlets");

            migrationBuilder.DropColumn(
                name: "business_id",
                table: "warehouses");

            migrationBuilder.DropColumn(
                name: "business_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "must_reset_password",
                table: "users");

            migrationBuilder.DropColumn(
                name: "business_id",
                table: "outlets");
        }
    }
}
