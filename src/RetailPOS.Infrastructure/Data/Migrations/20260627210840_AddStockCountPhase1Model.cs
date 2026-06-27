using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockCountPhase1Model : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_counts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    stock_count_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stock_count_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    total_items = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_by = table.Column<long>(type: "bigint", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejected_by = table.Column<long>(type: "bigint", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_counts", x => x.id);
                    table.ForeignKey(
                        name: "f_k_stock_counts__users_approver_id",
                        column: x => x.approved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_stock_counts__users_creator_id",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_stock_counts__users_rejector_id",
                        column: x => x.rejected_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_stock_counts__users_submitter_id",
                        column: x => x.submitted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "stock_count_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    stock_count_id = table.Column<long>(type: "bigint", nullable: false),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    product_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    product_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    variant_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    current_stock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    physical_stock = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    difference = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: true),
                    remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_count_lines", x => x.id);
                    table.ForeignKey(
                        name: "f_k_stock_count_lines_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_stock_count_lines_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_stock_count_lines_stock_counts_stock_count_id",
                        column: x => x.stock_count_id,
                        principalTable: "stock_counts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_stock_count_lines_product_id",
                table: "stock_count_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "i_x_stock_count_lines_stock_count_id",
                table: "stock_count_lines",
                column: "stock_count_id");

            migrationBuilder.CreateIndex(
                name: "i_x_stock_count_lines_variant_id",
                table: "stock_count_lines",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_count_lines_stock_count_id_variant_id",
                table: "stock_count_lines",
                columns: new[] { "stock_count_id", "variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_approved_by",
                table: "stock_counts",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_business_id",
                table: "stock_counts",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_business_id_location_type_location_id",
                table: "stock_counts",
                columns: new[] { "business_id", "location_type", "location_id" },
                unique: true,
                filter: "status in ('Draft','Submitted')");

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_business_id_location_type_location_id_created_~",
                table: "stock_counts",
                columns: new[] { "business_id", "location_type", "location_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_created_by",
                table: "stock_counts",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_location_id",
                table: "stock_counts",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_rejected_by",
                table: "stock_counts",
                column: "rejected_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_status",
                table: "stock_counts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_stock_count_date",
                table: "stock_counts",
                column: "stock_count_date");

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_stock_count_no",
                table: "stock_counts",
                column: "stock_count_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_counts_submitted_by",
                table: "stock_counts",
                column: "submitted_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_count_lines");

            migrationBuilder.DropTable(
                name: "stock_counts");
        }
    }
}
