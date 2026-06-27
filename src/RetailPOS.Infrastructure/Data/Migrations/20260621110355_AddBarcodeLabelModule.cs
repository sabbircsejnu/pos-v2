using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBarcodeLabelModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "barcode_templates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    template_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    paper_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    label_width_mm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    label_height_mm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_barcode_templates", x => x.id);
                    table.ForeignKey(
                        name: "f_k_barcode_templates__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "barcode_print_histories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: true),
                    template_id = table.Column<long>(type: "bigint", nullable: true),
                    printed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    print_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    label_width_mm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    label_height_mm = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    total_labels = table.Column<int>(type: "integer", nullable: false),
                    source_module = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    source_reference_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    source_reference_id = table.Column<long>(type: "bigint", nullable: true),
                    printed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_barcode_print_histories", x => x.id);
                    table.ForeignKey(
                        name: "f_k_barcode_print_histories__barcode_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "barcode_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_barcode_print_histories__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_barcode_print_histories__outlets_outlet_id",
                        column: x => x.outlet_id,
                        principalTable: "outlets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_barcode_print_histories__users_printed_by_user_id",
                        column: x => x.printed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "barcode_template_fields",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    template_id = table.Column<long>(type: "bigint", nullable: false),
                    field_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    x = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    y = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    width = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    height = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    font_size = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    font_weight = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    align = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_barcode_template_fields", x => x.id);
                    table.ForeignKey(
                        name: "f_k_barcode_template_fields_barcode_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "barcode_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "barcode_print_history_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    print_history_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    product_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    variant_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    variant_sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    barcode_value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    variant_attributes = table.Column<string>(type: "text", nullable: true),
                    selling_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    quantity_printed = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_barcode_print_history_items", x => x.id);
                    table.ForeignKey(
                        name: "f_k_barcode_print_history_items__product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_barcode_print_history_items_barcode_print_histories_print_h~",
                        column: x => x.print_history_id,
                        principalTable: "barcode_print_histories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_barcode_print_histories_outlet_id",
                table: "barcode_print_histories",
                column: "outlet_id");

            migrationBuilder.CreateIndex(
                name: "IX_barcode_print_histories_business_id_printed_at",
                table: "barcode_print_histories",
                columns: new[] { "business_id", "printed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_barcode_print_histories_printed_by_user_id_printed_at",
                table: "barcode_print_histories",
                columns: new[] { "printed_by_user_id", "printed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_barcode_print_histories_source_reference_type_source_refere~",
                table: "barcode_print_histories",
                columns: new[] { "source_reference_type", "source_reference_id" });

            migrationBuilder.CreateIndex(
                name: "IX_barcode_print_histories_template_id_printed_at",
                table: "barcode_print_histories",
                columns: new[] { "template_id", "printed_at" });

            migrationBuilder.CreateIndex(
                name: "i_x_barcode_print_history_items_print_history_id",
                table: "barcode_print_history_items",
                column: "print_history_id");

            migrationBuilder.CreateIndex(
                name: "i_x_barcode_print_history_items_variant_id",
                table: "barcode_print_history_items",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_barcode_template_fields_template_id_field_key",
                table: "barcode_template_fields",
                columns: new[] { "template_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_barcode_template_fields_template_id_sort_order",
                table: "barcode_template_fields",
                columns: new[] { "template_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_barcode_templates_business_id_name",
                table: "barcode_templates",
                columns: new[] { "business_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_barcode_templates_one_default_per_business",
                table: "barcode_templates",
                columns: new[] { "business_id", "is_default" },
                filter: "is_default = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "barcode_print_history_items");

            migrationBuilder.DropTable(
                name: "barcode_template_fields");

            migrationBuilder.DropTable(
                name: "barcode_print_histories");

            migrationBuilder.DropTable(
                name: "barcode_templates");
        }
    }
}
