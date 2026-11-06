using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserContextSwitchingAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "context_location_scope",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "selected");

            migrationBuilder.AddColumn<string>(
                name: "context_role_scope",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "mapped");

            migrationBuilder.AddColumn<bool>(
                name: "context_switch_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "user_context_roles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_user_context_roles", x => x.id);
                    table.ForeignKey(
                        name: "f_k_user_context_roles_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_user_context_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_user_context_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_context_location_scope",
                table: "users",
                sql: "context_location_scope IN ('selected', 'all')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_context_role_scope",
                table: "users",
                sql: "context_role_scope IN ('mapped', 'all')");

            migrationBuilder.CreateIndex(
                name: "i_x_user_context_roles_business_id",
                table: "user_context_roles",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "i_x_user_context_roles_role_id",
                table: "user_context_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_context_roles_user_id_role_id",
                table: "user_context_roles",
                columns: new[] { "user_id", "role_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_context_roles");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_context_location_scope",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_context_role_scope",
                table: "users");

            migrationBuilder.DropColumn(
                name: "context_location_scope",
                table: "users");

            migrationBuilder.DropColumn(
                name: "context_role_scope",
                table: "users");

            migrationBuilder.DropColumn(
                name: "context_switch_enabled",
                table: "users");
        }
    }
}
