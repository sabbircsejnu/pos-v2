using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditFieldDisplaySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_reference_field",
                table: "audit_event_field_changes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "new_display_value",
                table: "audit_event_field_changes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "old_display_value",
                table: "audit_event_field_changes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference_entity_type",
                table: "audit_event_field_changes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_reference_field",
                table: "audit_event_field_changes");

            migrationBuilder.DropColumn(
                name: "new_display_value",
                table: "audit_event_field_changes");

            migrationBuilder.DropColumn(
                name: "old_display_value",
                table: "audit_event_field_changes");

            migrationBuilder.DropColumn(
                name: "reference_entity_type",
                table: "audit_event_field_changes");
        }
    }
}
