using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalVariationsSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_variant_attribute_values");

            migrationBuilder.DropTable(
                name: "product_attribute_values");

            migrationBuilder.DropTable(
                name: "product_attributes");

            migrationBuilder.CreateTable(
                name: "variations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_variations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "product_variations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variation_id = table.Column<long>(type: "bigint", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_product_variations", x => x.id);
                    table.ForeignKey(
                        name: "f_k_product_variations_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_product_variations_variations_variation_id",
                        column: x => x.variation_id,
                        principalTable: "variations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "variation_options",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    variation_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price_adjustment = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_variation_options", x => x.id);
                    table.ForeignKey(
                        name: "f_k_variation_options_variations_variation_id",
                        column: x => x.variation_id,
                        principalTable: "variations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_variant_options",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    option_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_product_variant_options", x => x.id);
                    table.ForeignKey(
                        name: "f_k_product_variant_options_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_product_variant_options_variation_options_option_id",
                        column: x => x.option_id,
                        principalTable: "variation_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_product_variant_options_option_id",
                table: "product_variant_options",
                column: "option_id");

            migrationBuilder.CreateIndex(
                name: "i_x_product_variant_options_variant_id",
                table: "product_variant_options",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_product_variations_product_id",
                table: "product_variations",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "i_x_product_variations_variation_id",
                table: "product_variations",
                column: "variation_id");

            migrationBuilder.CreateIndex(
                name: "i_x_variation_options_variation_id",
                table: "variation_options",
                column: "variation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_variant_options");

            migrationBuilder.DropTable(
                name: "product_variations");

            migrationBuilder.DropTable(
                name: "variation_options");

            migrationBuilder.DropTable(
                name: "variations");

            migrationBuilder.CreateTable(
                name: "product_attributes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_product_attributes", x => x.id);
                    table.ForeignKey(
                        name: "f_k_product_attributes_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_attribute_values",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    attribute_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price_adjustment = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_product_attribute_values", x => x.id);
                    table.ForeignKey(
                        name: "f_k_product_attribute_values_product_attributes_attribute_id",
                        column: x => x.attribute_id,
                        principalTable: "product_attributes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_variant_attribute_values",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    attribute_value_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_product_variant_attribute_values", x => x.id);
                    table.ForeignKey(
                        name: "f_k_product_variant_attribute_values_product_attribute_values_a~",
                        column: x => x.attribute_value_id,
                        principalTable: "product_attribute_values",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_product_variant_attribute_values_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_product_attribute_values_attribute_id",
                table: "product_attribute_values",
                column: "attribute_id");

            migrationBuilder.CreateIndex(
                name: "i_x_product_attributes_product_id",
                table: "product_attributes",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "i_x_product_variant_attribute_values_attribute_value_id",
                table: "product_variant_attribute_values",
                column: "attribute_value_id");

            migrationBuilder.CreateIndex(
                name: "i_x_product_variant_attribute_values_variant_id",
                table: "product_variant_attribute_values",
                column: "variant_id");
        }
    }
}
