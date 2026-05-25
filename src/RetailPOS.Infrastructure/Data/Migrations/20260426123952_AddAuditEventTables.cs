using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEventTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: true),
                    outlet_id = table.Column<long>(type: "bigint", nullable: true),
                    real_user_id = table.Column<long>(type: "bigint", nullable: true),
                    acting_user_id = table.Column<long>(type: "bigint", nullable: true),
                    real_role_id = table.Column<long>(type: "bigint", nullable: true),
                    acting_role_id = table.Column<long>(type: "bigint", nullable: true),
                    action_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    action_summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    module = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    primary_entity_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    primary_entity_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    request_method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    request_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    device_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    browser = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    os = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_audit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_event_entities",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    audit_event_id = table.Column<long>(type: "bigint", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    operation_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fields_changed_count = table.Column<int>(type: "integer", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_audit_event_entities", x => x.id);
                    table.ForeignKey(
                        name: "f_k_audit_event_entities_audit_events_audit_event_id",
                        column: x => x.audit_event_id,
                        principalTable: "audit_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "audit_event_field_changes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    audit_event_entity_id = table.Column<long>(type: "bigint", nullable: false),
                    field_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_audit_event_field_changes", x => x.id);
                    table.ForeignKey(
                        name: "f_k_audit_event_field_changes_audit_event_entities_audit_event_~",
                        column: x => x.audit_event_entity_id,
                        principalTable: "audit_event_entities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_audit_event_entities_audit_event_id",
                table: "audit_event_entities",
                column: "audit_event_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_event_entities_entity_type_entity_id",
                table: "audit_event_entities",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_audit_event_field_changes_audit_event_entity_id",
                table: "audit_event_field_changes",
                column: "audit_event_entity_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_correlation_id",
                table: "audit_events",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_created_at",
                table: "audit_events",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_module_action_type_created_at",
                table: "audit_events",
                columns: new[] { "module", "action_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_outlet_id_created_at",
                table: "audit_events",
                columns: new[] { "outlet_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_primary_entity_type_primary_entity_id",
                table: "audit_events",
                columns: new[] { "primary_entity_type", "primary_entity_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_real_user_id_created_at",
                table: "audit_events",
                columns: new[] { "real_user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_event_field_changes");

            migrationBuilder.DropTable(
                name: "audit_event_entities");

            migrationBuilder.DropTable(
                name: "audit_events");
        }
    }
}
