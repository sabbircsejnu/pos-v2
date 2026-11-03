using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase0DurableDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "integration_inbox_messages",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    consumer_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    envelope_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    processed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effect_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_integration_inbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "integration_outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    producer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    source_version = table.Column<long>(type: "bigint", nullable: false),
                    aggregate_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    aggregate_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    causation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    envelope_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lease_owner = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    published_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_integration_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_integration_inbox_messages_business_id_consumer_name_messag~",
                table: "integration_inbox_messages",
                columns: new[] { "business_id", "consumer_name", "message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_integration_outbox_messages_business_id_aggregate_type_aggr~",
                table: "integration_outbox_messages",
                columns: new[] { "business_id", "aggregate_type", "aggregate_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_integration_outbox_messages_status_next_attempt_at_utc_leas~",
                table: "integration_outbox_messages",
                columns: new[] { "status", "next_attempt_at_utc", "lease_expires_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_inbox_messages");

            migrationBuilder.DropTable(
                name: "integration_outbox_messages");
        }
    }
}
