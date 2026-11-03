using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Api05BMobileStockCountOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "baseline_consistency_status",
                table: "stock_counts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Unverified");

            migrationBuilder.AddColumn<DateTime>(
                name: "baseline_inventory_read_completed_at_utc",
                table: "stock_counts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "baseline_inventory_read_started_at_utc",
                table: "stock_counts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "count_revision",
                table: "stock_counts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "movement_reconciliation_status",
                table: "stock_counts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "NotStarted");

            migrationBuilder.CreateTable(
                name: "stock_count_operation_receipts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    stock_count_id = table.Column<long>(type: "bigint", nullable: false),
                    client_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_session_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    client_sequence = table.Column<long>(type: "bigint", nullable: false),
                    product_variant_id = table.Column<long>(type: "bigint", nullable: false),
                    operation_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    counted_quantity = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    raw_barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    client_observed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    server_received_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    decided_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    accepted_revision = table.Column<long>(type: "bigint", nullable: true),
                    session_revision_at_decision = table.Column<long>(type: "bigint", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rejection_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_count_operation_receipts", x => x.id);
                    table.ForeignKey(
                        name: "f_k_stock_count_operation_receipts_stock_counts_stock_count_id",
                        column: x => x.stock_count_id,
                        principalTable: "stock_counts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_operation_receipts_stock_count_id_accepted_revi~",
                table: "stock_count_operation_receipts",
                columns: new[] { "stock_count_id", "accepted_revision" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_operation_receipts_stock_count_id_client_operat~",
                table: "stock_count_operation_receipts",
                columns: new[] { "stock_count_id", "client_operation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_operation_receipts_stock_count_id_device_sessio~",
                table: "stock_count_operation_receipts",
                columns: new[] { "stock_count_id", "device_session_id", "client_sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_count_operation_receipts");

            migrationBuilder.DropColumn(
                name: "baseline_consistency_status",
                table: "stock_counts");

            migrationBuilder.DropColumn(
                name: "baseline_inventory_read_completed_at_utc",
                table: "stock_counts");

            migrationBuilder.DropColumn(
                name: "baseline_inventory_read_started_at_utc",
                table: "stock_counts");

            migrationBuilder.DropColumn(
                name: "count_revision",
                table: "stock_counts");

            migrationBuilder.DropColumn(
                name: "movement_reconciliation_status",
                table: "stock_counts");
        }
    }
}
