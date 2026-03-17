using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductAttributeCombinations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_attributes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price_adjustment = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    attribute_value_id = table.Column<long>(type: "bigint", nullable: false),
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_variant_attribute_values");

            migrationBuilder.DropTable(
                name: "product_attribute_values");

            migrationBuilder.DropTable(
                name: "product_attributes");
        }
    }
}
