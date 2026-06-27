using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockTransferPhase1Model : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "approved_at",
                table: "stock_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at",
                table: "stock_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "cancelled_by",
                table: "stock_transfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "dispatched_at",
                table: "stock_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "dispatched_by",
                table: "stock_transfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "stock_transfers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "received_at",
                table: "stock_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "received_by",
                table: "stock_transfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "rejected_at",
                table: "stock_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "rejected_by",
                table: "stock_transfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "related_requisition_id",
                table: "stock_transfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_at",
                table: "stock_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "submitted_by",
                table: "stock_transfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transfer_no",
                table: "stock_transfers",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transfer_type",
                table: "stock_transfers",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "direct");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "stock_transfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "updated_by",
                table: "stock_transfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "accepted_quantity",
                table: "stock_transfer_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rejected_quantity",
                table: "stock_transfer_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "remarks",
                table: "stock_transfer_items",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "requested_quantity",
                table: "stock_transfer_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "transfer_quantity",
                table: "stock_transfer_items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                table: "stock_transfer_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "stock_requisitions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    requisition_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requesting_location_id = table.Column<long>(type: "bigint", nullable: false),
                    requesting_location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_location_id = table.Column<long>(type: "bigint", nullable: false),
                    source_location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by = table.Column<long>(type: "bigint", nullable: false),
                    request_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    submitted_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejected_by = table.Column<long>(type: "bigint", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_requisitions", x => x.id);
                    table.ForeignKey(
                        name: "f_k_stock_requisitions__users_approver_id",
                        column: x => x.approved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_stock_requisitions__users_closer_id",
                        column: x => x.closed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_stock_requisitions__users_rejector_id",
                        column: x => x.rejected_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_stock_requisitions__users_requester_id",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_stock_requisitions__users_submitter_id",
                        column: x => x.submitted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_stock_requisitions__users_updater_id",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "stock_requisition_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    requisition_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_quantity = table.Column<int>(type: "integer", nullable: false),
                    fulfilled_quantity = table.Column<int>(type: "integer", nullable: false),
                    remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_requisition_lines", x => x.id);
                    table.ForeignKey(
                        name: "f_k_stock_requisition_lines_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_stock_requisition_lines_stock_requisitions_requisition_id",
                        column: x => x.requisition_id,
                        principalTable: "stock_requisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_stock_transfers_related_requisition_id",
                table: "stock_transfers",
                column: "related_requisition_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_cancelled_by",
                table: "stock_transfers",
                column: "cancelled_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_dispatched_by",
                table: "stock_transfers",
                column: "dispatched_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_from_location_type_from_location_id",
                table: "stock_transfers",
                columns: new[] { "from_location_type", "from_location_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_received_by",
                table: "stock_transfers",
                column: "received_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_rejected_by",
                table: "stock_transfers",
                column: "rejected_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_status",
                table: "stock_transfers",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_submitted_by",
                table: "stock_transfers",
                column: "submitted_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_to_location_type_to_location_id",
                table: "stock_transfers",
                columns: new[] { "to_location_type", "to_location_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_transfer_date",
                table: "stock_transfers",
                column: "transfer_date");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_transfer_no",
                table: "stock_transfers",
                column: "transfer_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_updated_by",
                table: "stock_transfers",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "i_x_stock_requisition_lines_requisition_id",
                table: "stock_requisition_lines",
                column: "requisition_id");

            migrationBuilder.CreateIndex(
                name: "i_x_stock_requisition_lines_variant_id",
                table: "stock_requisition_lines",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_requisitions_approved_by",
                table: "stock_requisitions",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_requisitions_closed_by",
                table: "stock_requisitions",
                column: "closed_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_requisitions_rejected_by",
                table: "stock_requisitions",
                column: "rejected_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_requisitions_request_date",
                table: "stock_requisitions",
                column: "request_date");

            migrationBuilder.CreateIndex(
                name: "IX_stock_requisitions_requested_by",
                table: "stock_requisitions",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_requisitions_requesting_location_type_requesting_loca~",
                table: "stock_requisitions",
                columns: new[] { "requesting_location_type", "requesting_location_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_requisitions_requisition_no",
                table: "stock_requisitions",
                column: "requisition_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_requisitions_source_location_type_source_location_id",
                table: "stock_requisitions",
                columns: new[] { "source_location_type", "source_location_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_requisitions_status",
                table: "stock_requisitions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_stock_requisitions_submitted_by",
                table: "stock_requisitions",
                column: "submitted_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_requisitions_updated_by",
                table: "stock_requisitions",
                column: "updated_by");

            migrationBuilder.AddForeignKey(
                name: "FK_stock_transfers_users_cancelled_by",
                table: "stock_transfers",
                column: "cancelled_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_transfers_users_dispatched_by",
                table: "stock_transfers",
                column: "dispatched_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_transfers_users_received_by",
                table: "stock_transfers",
                column: "received_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_transfers_users_rejected_by",
                table: "stock_transfers",
                column: "rejected_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_transfers_users_submitted_by",
                table: "stock_transfers",
                column: "submitted_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_transfers_users_updated_by",
                table: "stock_transfers",
                column: "updated_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_stock_transfers_stock_requisitions_related_requisition_id",
                table: "stock_transfers",
                column: "related_requisition_id",
                principalTable: "stock_requisitions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stock_transfers_users_cancelled_by",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_transfers_users_dispatched_by",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_transfers_users_received_by",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_transfers_users_rejected_by",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_transfers_users_submitted_by",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_transfers_users_updated_by",
                table: "stock_transfers");

            migrationBuilder.DropForeignKey(
                name: "f_k_stock_transfers_stock_requisitions_related_requisition_id",
                table: "stock_transfers");

            migrationBuilder.DropTable(
                name: "stock_requisition_lines");

            migrationBuilder.DropTable(
                name: "stock_requisitions");

            migrationBuilder.DropIndex(
                name: "i_x_stock_transfers_related_requisition_id",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfers_cancelled_by",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfers_dispatched_by",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfers_from_location_type_from_location_id",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfers_received_by",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfers_rejected_by",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfers_status",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfers_submitted_by",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfers_to_location_type_to_location_id",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfers_transfer_date",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfers_transfer_no",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "IX_stock_transfers_updated_by",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "cancelled_by",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "dispatched_at",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "dispatched_by",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "received_at",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "received_by",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "rejected_at",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "rejected_by",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "related_requisition_id",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "submitted_by",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "transfer_no",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "transfer_type",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "updated_by",
                table: "stock_transfers");

            migrationBuilder.DropColumn(
                name: "accepted_quantity",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "rejected_quantity",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "remarks",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "requested_quantity",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "transfer_quantity",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                table: "stock_transfer_items");
        }
    }
}
