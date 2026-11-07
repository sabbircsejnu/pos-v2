using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosV2UomFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "transfer_quantity",
                table: "stock_transfer_items",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "requested_quantity",
                table: "stock_transfer_items",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "rejected_quantity",
                table: "stock_transfer_items",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "quantity",
                table: "stock_transfer_items",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "missing_quantity",
                table: "stock_transfer_items",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "damaged_quantity",
                table: "stock_transfer_items",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "accepted_quantity",
                table: "stock_transfer_items",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "uom_base_unit_code",
                table: "stock_transfer_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "uom_config_version_id",
                table: "stock_transfer_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_entered_quantity",
                table: "stock_transfer_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_entered_unit_price",
                table: "stock_transfer_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_factor_denominator",
                table: "stock_transfer_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_factor_numerator",
                table: "stock_transfer_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_label",
                table: "stock_transfer_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_option_code",
                table: "stock_transfer_items",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "uom_option_id",
                table: "stock_transfer_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_unit_code",
                table: "stock_transfer_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "requested_quantity",
                table: "stock_requisition_lines",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "fulfilled_quantity",
                table: "stock_requisition_lines",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_cost",
                table: "stock_ledgers",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "qty_out",
                table: "stock_ledgers",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "qty_in",
                table: "stock_ledgers",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "balance_after",
                table: "stock_ledgers",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "quantity_change",
                table: "stock_adjustment_lines",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "previous_quantity",
                table: "stock_adjustment_lines",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "new_quantity",
                table: "stock_adjustment_lines",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<decimal>(
                name: "base_qty_per_pack",
                table: "stock_adjustment_lines",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pack_code",
                table: "stock_adjustment_lines",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pack_state",
                table: "stock_adjustment_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_base_unit_code",
                table: "stock_adjustment_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "uom_config_version_id",
                table: "stock_adjustment_lines",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_entered_quantity",
                table: "stock_adjustment_lines",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_entered_unit_price",
                table: "stock_adjustment_lines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_factor_denominator",
                table: "stock_adjustment_lines",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_factor_numerator",
                table: "stock_adjustment_lines",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_label",
                table: "stock_adjustment_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_option_code",
                table: "stock_adjustment_lines",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "uom_option_id",
                table: "stock_adjustment_lines",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_unit_code",
                table: "stock_adjustment_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_price",
                table: "sale_items",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "quantity",
                table: "sale_items",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "historical_unit_cost",
                table: "sale_items",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,4)",
                oldPrecision: 10,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "base_qty_per_pack",
                table: "sale_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pack_code",
                table: "sale_items",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pack_state",
                table: "sale_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "packaging_cost",
                table: "sale_items",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "uom_base_unit_code",
                table: "sale_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "uom_config_version_id",
                table: "sale_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_entered_quantity",
                table: "sale_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_entered_unit_price",
                table: "sale_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_factor_denominator",
                table: "sale_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_factor_numerator",
                table: "sale_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_label",
                table: "sale_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_option_code",
                table: "sale_items",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "uom_option_id",
                table: "sale_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_unit_code",
                table: "sale_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_price",
                table: "purchase_order_items",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "quantity",
                table: "purchase_order_items",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "uom_base_unit_code",
                table: "purchase_order_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "uom_config_version_id",
                table: "purchase_order_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_entered_quantity",
                table: "purchase_order_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_entered_unit_price",
                table: "purchase_order_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_factor_denominator",
                table: "purchase_order_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_factor_numerator",
                table: "purchase_order_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_label",
                table: "purchase_order_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_option_code",
                table: "purchase_order_items",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "uom_option_id",
                table: "purchase_order_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_unit_code",
                table: "purchase_order_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "quantity",
                table: "inventories",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_cost",
                table: "grn_items",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "received_qty",
                table: "grn_items",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<decimal>(
                name: "accepted_loose_quantity",
                table: "grn_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "accepted_sealed_packs",
                table: "grn_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "damaged_quantity",
                table: "grn_items",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "rejected_quantity",
                table: "grn_items",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "uom_base_unit_code",
                table: "grn_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "uom_config_version_id",
                table: "grn_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_entered_quantity",
                table: "grn_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_entered_unit_price",
                table: "grn_items",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_factor_denominator",
                table: "grn_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "uom_factor_numerator",
                table: "grn_items",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_label",
                table: "grn_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_option_code",
                table: "grn_items",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "uom_option_id",
                table: "grn_items",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uom_unit_code",
                table: "grn_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pack_state_balances",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pack_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    base_qty_per_pack = table.Column<decimal>(type: "numeric", nullable: false),
                    pack_count = table.Column<int>(type: "integer", nullable: false),
                    base_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    capitalized_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_pack_state_balances", x => x.id);
                    table.CheckConstraint("ck_pack_state_balances_not_negative", "pack_count >= 0 AND base_quantity >= 0 AND capitalized_value >= 0");
                    table.ForeignKey(
                        name: "f_k_pack_state_balances_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pack_state_movements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    operation_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pack_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    base_qty_per_pack = table.Column<decimal>(type: "numeric", nullable: false),
                    pack_count_delta = table.Column<int>(type: "integer", nullable: false),
                    base_quantity_delta = table.Column<decimal>(type: "numeric", nullable: false),
                    capitalized_value_delta = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_id = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: true),
                    business_date = table.Column<DateTime>(type: "date", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_pack_state_movements", x => x.id);
                    table.ForeignKey(
                        name: "f_k_pack_state_movements_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "repack_jobs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_variant_id = table.Column<long>(type: "bigint", nullable: false),
                    config_version_id = table.Column<long>(type: "bigint", nullable: false),
                    output_option_id = table.Column<long>(type: "bigint", nullable: false),
                    output_pack_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    output_label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pack_base_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    output_count = table.Column<int>(type: "integer", nullable: false),
                    content_base_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    content_unit_cost = table.Column<decimal>(type: "numeric", nullable: true),
                    content_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    packaging_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    executed_by = table.Column<long>(type: "bigint", nullable: true),
                    executed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<long>(type: "bigint", nullable: true),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_repack_jobs", x => x.id);
                    table.ForeignKey(
                        name: "f_k_repack_jobs_product_variants_source_variant_id",
                        column: x => x.source_variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_item_components",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sale_item_id = table.Column<long>(type: "bigint", nullable: false),
                    component_variant_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_sale_item_components", x => x.id);
                    table.ForeignKey(
                        name: "f_k_sale_item_components_product_variants_component_variant_id",
                        column: x => x.component_variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_sale_item_components_sale_items_sale_item_id",
                        column: x => x.sale_item_id,
                        principalTable: "sale_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sale_return_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sale_id = table.Column<long>(type: "bigint", nullable: false),
                    sale_item_id = table.Column<long>(type: "bigint", nullable: false),
                    returned_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    restocked_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    packaging_reusable = table.Column<bool>(type: "boolean", nullable: false),
                    cost_restored = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_sale_return_lines", x => x.id);
                    table.ForeignKey(
                        name: "f_k_sale_return_lines_sale_items_sale_item_id",
                        column: x => x.sale_item_id,
                        principalTable: "sale_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_count_line_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    stock_count_line_id = table.Column<long>(type: "bigint", nullable: false),
                    base_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    pack_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    base_qty_per_pack = table.Column<decimal>(type: "numeric", nullable: false),
                    pack_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    uom_unit_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    uom_label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    uom_entered_quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    uom_entered_unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    uom_factor_numerator = table.Column<decimal>(type: "numeric", nullable: true),
                    uom_factor_denominator = table.Column<decimal>(type: "numeric", nullable: true),
                    uom_base_unit_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    uom_config_version_id = table.Column<long>(type: "bigint", nullable: true),
                    uom_option_id = table.Column<long>(type: "bigint", nullable: true),
                    uom_option_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_count_line_entries", x => x.id);
                    table.ForeignKey(
                        name: "f_k_stock_count_line_entries_stock_count_lines_stock_count_line~",
                        column: x => x.stock_count_line_id,
                        principalTable: "stock_count_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_item_allocations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transfer_item_id = table.Column<long>(type: "bigint", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    pack_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    base_qty_per_pack = table.Column<decimal>(type: "numeric", nullable: false),
                    pack_count = table.Column<int>(type: "integer", nullable: false),
                    base_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    capitalized_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_transfer_item_allocations", x => x.id);
                    table.ForeignKey(
                        name: "f_k_stock_transfer_item_allocations_stock_transfer_items_transf~",
                        column: x => x.transfer_item_id,
                        principalTable: "stock_transfer_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "uom_units",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: true),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dimension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity_precision = table.Column<int>(type: "integer", nullable: false),
                    is_whole_unit = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_uom_units", x => x.id);
                    table.ForeignKey(
                        name: "f_k_uom_units_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "repack_component_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    repack_job_id = table.Column<long>(type: "bigint", nullable: false),
                    component_variant_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric", nullable: true),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_repack_component_lines", x => x.id);
                    table.ForeignKey(
                        name: "f_k_repack_component_lines__repack_jobs_repack_job_id",
                        column: x => x.repack_job_id,
                        principalTable: "repack_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_repack_component_lines_product_variants_component_variant_id",
                        column: x => x.component_variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "uom_standard_conversions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_unit_id = table.Column<long>(type: "bigint", nullable: false),
                    to_unit_id = table.Column<long>(type: "bigint", nullable: false),
                    numerator = table.Column<decimal>(type: "numeric", nullable: false),
                    denominator = table.Column<decimal>(type: "numeric", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_uom_standard_conversions", x => x.id);
                    table.ForeignKey(
                        name: "f_k_uom_standard_conversions__uom_units_from_unit_id",
                        column: x => x.from_unit_id,
                        principalTable: "uom_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_uom_standard_conversions__uom_units_to_unit_id",
                        column: x => x.to_unit_id,
                        principalTable: "uom_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "variant_uom_configs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    base_unit_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    pack_state_tracking_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    repacking_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    effective_to_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_variant_uom_configs", x => x.id);
                    table.ForeignKey(
                        name: "f_k_variant_uom_configs_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_variant_uom_configs_uom_units_base_unit_id",
                        column: x => x.base_unit_id,
                        principalTable: "uom_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "variant_purchase_unit_options",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    config_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    unit_id = table.Column<long>(type: "bigint", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    factor_numerator = table.Column<decimal>(type: "numeric", nullable: false),
                    factor_denominator = table.Column<decimal>(type: "numeric", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_sealed_pack = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_variant_purchase_unit_options", x => x.id);
                    table.ForeignKey(
                        name: "f_k_variant_purchase_unit_options__variant_uom_configs_config_id",
                        column: x => x.config_id,
                        principalTable: "variant_uom_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_variant_purchase_unit_options_uom_units_unit_id",
                        column: x => x.unit_id,
                        principalTable: "uom_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "variant_selling_options",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    config_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    unit_id = table.Column<long>(type: "bigint", nullable: true),
                    base_quantity = table.Column<decimal>(type: "numeric", nullable: true),
                    factor_numerator = table.Column<decimal>(type: "numeric", nullable: false),
                    factor_denominator = table.Column<decimal>(type: "numeric", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sealed_pack_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    is_prepared_eligible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_variant_selling_options", x => x.id);
                    table.ForeignKey(
                        name: "f_k_variant_selling_options__variant_uom_configs_config_id",
                        column: x => x.config_id,
                        principalTable: "variant_uom_configs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_variant_selling_options_uom_units_unit_id",
                        column: x => x.unit_id,
                        principalTable: "uom_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "selling_option_components",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    selling_option_id = table.Column<long>(type: "bigint", nullable: false),
                    component_variant_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_selling_option_components", x => x.id);
                    table.ForeignKey(
                        name: "f_k_selling_option_components__variant_selling_options_selling_opt~",
                        column: x => x.selling_option_id,
                        principalTable: "variant_selling_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_selling_option_components_product_variants_component_varian~",
                        column: x => x.component_variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_pack_state_balances_business_id_location_type_location_id",
                table: "pack_state_balances",
                columns: new[] { "business_id", "location_type", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ux_pack_state_balances_identity",
                table: "pack_state_balances",
                columns: new[] { "variant_id", "location_id", "location_type", "state", "pack_code", "base_qty_per_pack" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_pack_state_movements_business_id_business_date",
                table: "pack_state_movements",
                columns: new[] { "business_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "i_x_pack_state_movements_reference_type_reference_id",
                table: "pack_state_movements",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_pack_state_movements_variant_id_location_id_location_type_c~",
                table: "pack_state_movements",
                columns: new[] { "variant_id", "location_id", "location_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_pack_state_movements_idempotency",
                table: "pack_state_movements",
                columns: new[] { "business_id", "idempotency_key", "state", "pack_code", "base_qty_per_pack" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_repack_component_lines_component_variant_id",
                table: "repack_component_lines",
                column: "component_variant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_repack_component_lines_repack_job_id_component_variant_id",
                table: "repack_component_lines",
                columns: new[] { "repack_job_id", "component_variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_repack_jobs_business_id_idempotency_key",
                table: "repack_jobs",
                columns: new[] { "business_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_repack_jobs_business_id_location_type_location_id_created_at",
                table: "repack_jobs",
                columns: new[] { "business_id", "location_type", "location_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "i_x_repack_jobs_business_id_reference",
                table: "repack_jobs",
                columns: new[] { "business_id", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_repack_jobs_source_variant_id",
                table: "repack_jobs",
                column: "source_variant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sale_item_components_component_variant_id",
                table: "sale_item_components",
                column: "component_variant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sale_item_components_sale_item_id",
                table: "sale_item_components",
                column: "sale_item_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sale_return_lines_sale_id",
                table: "sale_return_lines",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sale_return_lines_sale_item_id",
                table: "sale_return_lines",
                column: "sale_item_id");

            migrationBuilder.CreateIndex(
                name: "i_x_selling_option_components_component_variant_id",
                table: "selling_option_components",
                column: "component_variant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_selling_option_components_selling_option_id_component_varia~",
                table: "selling_option_components",
                columns: new[] { "selling_option_id", "component_variant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_stock_count_line_entries_stock_count_line_id",
                table: "stock_count_line_entries",
                column: "stock_count_line_id");

            migrationBuilder.CreateIndex(
                name: "i_x_stock_transfer_item_allocations_transfer_item_id",
                table: "stock_transfer_item_allocations",
                column: "transfer_item_id");

            migrationBuilder.CreateIndex(
                name: "i_x_uom_standard_conversions_from_unit_id_to_unit_id",
                table: "uom_standard_conversions",
                columns: new[] { "from_unit_id", "to_unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_uom_standard_conversions_to_unit_id",
                table: "uom_standard_conversions",
                column: "to_unit_id");

            migrationBuilder.CreateIndex(
                name: "ux_uom_units_business_code",
                table: "uom_units",
                columns: new[] { "business_id", "code" },
                unique: true,
                filter: "business_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_uom_units_global_code",
                table: "uom_units",
                column: "code",
                unique: true,
                filter: "business_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_variant_purchase_unit_options_config_id_code",
                table: "variant_purchase_unit_options",
                columns: new[] { "config_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_variant_purchase_unit_options_unit_id",
                table: "variant_purchase_unit_options",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "i_x_variant_selling_options_config_id_code",
                table: "variant_selling_options",
                columns: new[] { "config_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_variant_selling_options_unit_id",
                table: "variant_selling_options",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "i_x_variant_uom_configs_base_unit_id",
                table: "variant_uom_configs",
                column: "base_unit_id");

            migrationBuilder.CreateIndex(
                name: "i_x_variant_uom_configs_business_id",
                table: "variant_uom_configs",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "i_x_variant_uom_configs_variant_id_version",
                table: "variant_uom_configs",
                columns: new[] { "variant_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_variant_uom_configs_current",
                table: "variant_uom_configs",
                column: "variant_id",
                unique: true,
                filter: "effective_to_utc IS NULL");
            PosV2UomFoundationSql.Up(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            PosV2UomFoundationSql.Down(migrationBuilder);

            migrationBuilder.DropTable(
                name: "pack_state_balances");

            migrationBuilder.DropTable(
                name: "pack_state_movements");

            migrationBuilder.DropTable(
                name: "repack_component_lines");

            migrationBuilder.DropTable(
                name: "sale_item_components");

            migrationBuilder.DropTable(
                name: "sale_return_lines");

            migrationBuilder.DropTable(
                name: "selling_option_components");

            migrationBuilder.DropTable(
                name: "stock_count_line_entries");

            migrationBuilder.DropTable(
                name: "stock_transfer_item_allocations");

            migrationBuilder.DropTable(
                name: "uom_standard_conversions");

            migrationBuilder.DropTable(
                name: "variant_purchase_unit_options");

            migrationBuilder.DropTable(
                name: "repack_jobs");

            migrationBuilder.DropTable(
                name: "variant_selling_options");

            migrationBuilder.DropTable(
                name: "variant_uom_configs");

            migrationBuilder.DropTable(
                name: "uom_units");

            migrationBuilder.DropColumn(
                name: "uom_base_unit_code",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "uom_config_version_id",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "uom_entered_quantity",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "uom_entered_unit_price",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "uom_factor_denominator",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "uom_factor_numerator",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "uom_label",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "uom_option_code",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "uom_option_id",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "uom_unit_code",
                table: "stock_transfer_items");

            migrationBuilder.DropColumn(
                name: "base_qty_per_pack",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "pack_code",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "pack_state",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "uom_base_unit_code",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "uom_config_version_id",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "uom_entered_quantity",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "uom_entered_unit_price",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "uom_factor_denominator",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "uom_factor_numerator",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "uom_label",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "uom_option_code",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "uom_option_id",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "uom_unit_code",
                table: "stock_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "base_qty_per_pack",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "pack_code",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "pack_state",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "packaging_cost",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "uom_base_unit_code",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "uom_config_version_id",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "uom_entered_quantity",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "uom_entered_unit_price",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "uom_factor_denominator",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "uom_factor_numerator",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "uom_label",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "uom_option_code",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "uom_option_id",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "uom_unit_code",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "uom_base_unit_code",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "uom_config_version_id",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "uom_entered_quantity",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "uom_entered_unit_price",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "uom_factor_denominator",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "uom_factor_numerator",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "uom_label",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "uom_option_code",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "uom_option_id",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "uom_unit_code",
                table: "purchase_order_items");

            migrationBuilder.DropColumn(
                name: "accepted_loose_quantity",
                table: "grn_items");

            migrationBuilder.DropColumn(
                name: "accepted_sealed_packs",
                table: "grn_items");

            migrationBuilder.DropColumn(
                name: "damaged_quantity",
                table: "grn_items");

            migrationBuilder.DropColumn(
                name: "rejected_quantity",
                table: "grn_items");

            migrationBuilder.DropColumn(
                name: "uom_base_unit_code",
                table: "grn_items");

            migrationBuilder.DropColumn(
                name: "uom_config_version_id",
                table: "grn_items");

            migrationBuilder.DropColumn(
                name: "uom_entered_quantity",
                table: "grn_items");

            migrationBuilder.DropColumn(
                name: "uom_entered_unit_price",
                table: "grn_items");

            migrationBuilder.DropColumn(
                name: "uom_factor_denominator",
                table: "grn_items");

            migrationBuilder.DropColumn(
                name: "uom_factor_numerator",
                table: "grn_items");

            migrationBuilder.DropColumn(
                name: "uom_label",
                table: "grn_items");

            migrationBuilder.DropColumn(
                name: "uom_option_code",
                table: "grn_items");

            migrationBuilder.DropColumn(
                name: "uom_option_id",
                table: "grn_items");

            migrationBuilder.DropColumn(
                name: "uom_unit_code",
                table: "grn_items");

            migrationBuilder.AlterColumn<int>(
                name: "transfer_quantity",
                table: "stock_transfer_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "requested_quantity",
                table: "stock_transfer_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "rejected_quantity",
                table: "stock_transfer_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "quantity",
                table: "stock_transfer_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "missing_quantity",
                table: "stock_transfer_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "damaged_quantity",
                table: "stock_transfer_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "accepted_quantity",
                table: "stock_transfer_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "requested_quantity",
                table: "stock_requisition_lines",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "fulfilled_quantity",
                table: "stock_requisition_lines",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_cost",
                table: "stock_ledgers",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "qty_out",
                table: "stock_ledgers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "qty_in",
                table: "stock_ledgers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "balance_after",
                table: "stock_ledgers",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "quantity_change",
                table: "stock_adjustment_lines",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "previous_quantity",
                table: "stock_adjustment_lines",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "new_quantity",
                table: "stock_adjustment_lines",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_price",
                table: "sale_items",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "quantity",
                table: "sale_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "historical_unit_cost",
                table: "sale_items",
                type: "numeric(10,4)",
                precision: 10,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_price",
                table: "purchase_order_items",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "quantity",
                table: "purchase_order_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "quantity",
                table: "inventories",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "unit_cost",
                table: "grn_items",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<int>(
                name: "received_qty",
                table: "grn_items",
                type: "integer",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");
        }
    }
}
