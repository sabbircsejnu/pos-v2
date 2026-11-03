using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1FEmployeeLoginUserLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_employee_user_links",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    identity_user_id = table.Column<long>(type: "bigint", nullable: false),
                    linked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    linked_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    link_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    unlinked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    unlinked_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    unlink_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_employee_user_links", x => x.id);
                    table.CheckConstraint("ck_hr_employee_user_link_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_employee_user_link_identity_user_id", "identity_user_id > 0");
                    table.CheckConstraint("ck_hr_employee_user_link_linked_by_user_id", "linked_by_user_id > 0");
                    table.CheckConstraint("ck_hr_employee_user_link_state", "(is_active = true AND unlinked_at_utc IS NULL AND unlinked_by_user_id IS NULL AND unlink_reason IS NULL) OR (is_active = false AND unlinked_at_utc IS NOT NULL AND unlinked_by_user_id IS NOT NULL AND unlink_reason IS NOT NULL)");
                    table.CheckConstraint("ck_hr_employee_user_link_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_employee_user_links_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_user_links_hr_employees_business_id_employee_id",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_user_links_business_id_employee_id",
                table: "hr_employee_user_links",
                columns: new[] { "business_id", "employee_id" },
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_user_links_business_id_employee_id_linked_at_ut~",
                table: "hr_employee_user_links",
                columns: new[] { "business_id", "employee_id", "linked_at_utc", "id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_user_links_identity_user_id",
                table: "hr_employee_user_links",
                column: "identity_user_id",
                unique: true,
                filter: "is_active = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_employee_user_links");
        }
    }
}
