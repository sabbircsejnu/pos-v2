using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEventIpFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "raw_ip",
                table: "audit_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "local_machine_ip",
                table: "audit_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_local_request",
                table: "audit_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "raw_ip",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "local_machine_ip",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "is_local_request",
                table: "audit_events");
        }
    }
}
