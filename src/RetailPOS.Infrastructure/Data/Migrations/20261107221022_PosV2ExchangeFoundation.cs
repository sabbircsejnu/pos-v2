using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosV2ExchangeFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_session_movements_effect",
                table: "cash_session_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_session_movements_type",
                table: "cash_session_movements");

            migrationBuilder.CreateTable(
                name: "exchange_number_counters",
                columns: table => new
                {
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateTime>(type: "date", nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_exchange_number_counters", x => new { x.business_id, x.outlet_id, x.business_date });
                    table.CheckConstraint("ck_exchange_number_counters_sequence", "last_sequence >= 1");
                    table.ForeignKey(
                        name: "f_k_exchange_number_counters__outlets_outlet_id",
                        column: x => x.outlet_id,
                        principalTable: "outlets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_number_counters_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_policy_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    allow_lower_price_exchange = table.Column<bool>(type: "boolean", nullable: false),
                    exchange_window_days = table.Column<int>(type: "integer", nullable: false),
                    allow_replacement_re_exchange = table.Column<bool>(type: "boolean", nullable: false),
                    replacement_window_days = table.Column<int>(type: "integer", nullable: true),
                    max_exchange_count_per_lineage = table.Column<int>(type: "integer", nullable: true),
                    phone_verification_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    high_value_approval_threshold = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    negative_target_adjustment_mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_exchange_policy_versions", x => x.id);
                    table.CheckConstraint("ck_exchange_policy_versions_negative_mode", "negative_target_adjustment_mode IN ('track_only','deduct_from_exchange_salesperson')");
                    table.CheckConstraint("ck_exchange_policy_versions_phone_mode", "phone_verification_mode IN ('optional','required')");
                    table.CheckConstraint("ck_exchange_policy_versions_values", "version_no >= 1 AND exchange_window_days >= 0 AND (replacement_window_days IS NULL OR replacement_window_days >= 0) AND (max_exchange_count_per_lineage IS NULL OR max_exchange_count_per_lineage >= 1) AND (high_value_approval_threshold IS NULL OR high_value_approval_threshold > 0)");
                    table.ForeignKey(
                        name: "f_k_exchange_policy_versions__users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_policy_versions_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_approvals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    approval_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    receiving_outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    terminal_id = table.Column<long>(type: "bigint", nullable: true),
                    cashier_user_id = table.Column<long>(type: "bigint", nullable: false),
                    supervisor_user_id = table.Column<long>(type: "bigint", nullable: false),
                    permission_used = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    supervisor_outlet_access_basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    authentication_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_by_exchange_id = table.Column<long>(type: "bigint", nullable: true),
                    consumed_by_reversal_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_exchange_approvals", x => x.id);
                    table.CheckConstraint("ck_exchange_approvals_basis", "supervisor_outlet_access_basis IN ('assignment','global_entitlement')");
                    table.CheckConstraint("ck_exchange_approvals_independent", "supervisor_user_id <> cashier_user_id");
                    table.CheckConstraint("ck_exchange_approvals_reason", "length(trim(reason)) > 0");
                    table.CheckConstraint("ck_exchange_approvals_status", "status IN ('granted','consumed','expired','revoked')");
                    table.CheckConstraint("ck_exchange_approvals_type", "approval_type IN ('window','chain','high_value','refund')");
                    table.ForeignKey(
                        name: "f_k_exchange_approvals__users_cashier_user_id",
                        column: x => x.cashier_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_approvals__users_supervisor_user_id",
                        column: x => x.supervisor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_approvals_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_lineage_links",
                columns: table => new
                {
                    return_line_id = table.Column<long>(type: "bigint", nullable: false),
                    replacement_line_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_exchange_lineage_links", x => new { x.return_line_id, x.replacement_line_id });
                });

            migrationBuilder.CreateTable(
                name: "exchange_receipt_print_history",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    exchange_id = table.Column<long>(type: "bigint", nullable: false),
                    terminal_id = table.Column<long>(type: "bigint", nullable: true),
                    printed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    action_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    printed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_exchange_receipt_print_history", x => x.id);
                    table.ForeignKey(
                        name: "f_k_exchange_receipt_print_history__pos_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalTable: "pos_terminals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_exchange_receipt_print_history__users_printed_by_user_id",
                        column: x => x.printed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "exchange_receipt_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    exchange_id = table.Column<long>(type: "bigint", nullable: false),
                    exchange_reversal_id = table.Column<long>(type: "bigint", nullable: true),
                    document_version = table.Column<int>(type: "integer", nullable: false),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_exchange_receipt_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "exchange_replacement_line_components",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    replacement_line_id = table.Column<long>(type: "bigint", nullable: false),
                    component_variant_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_exchange_replacement_line_components", x => x.id);
                    table.CheckConstraint("ck_exchange_replacement_line_components_values", "quantity > 0 AND value >= 0");
                    table.ForeignKey(
                        name: "f_k_exchange_replacement_line_components__product_variants_compon~",
                        column: x => x.component_variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_replacement_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    exchange_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    issued_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    returned_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    line_discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    invoice_discount_share = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    taxable_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tax_classification = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    tax_inclusive = table.Column<bool>(type: "boolean", nullable: false),
                    historical_unit_cost = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    packaging_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    pack_state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    pack_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    base_qty_per_pack = table.Column<decimal>(type: "numeric", nullable: true),
                    lineage_depth = table.Column<int>(type: "integer", nullable: false),
                    applied_rule_id = table.Column<long>(type: "bigint", nullable: true),
                    applied_rule_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("p_k_exchange_replacement_lines", x => x.id);
                    table.CheckConstraint("ck_exchange_replacement_lines_returned_quantity", "issued_quantity > 0 AND returned_quantity >= 0 AND returned_quantity <= issued_quantity");
                    table.CheckConstraint("ck_exchange_replacement_lines_values", "taxable_amount >= 0 AND vat_amount >= 0 AND historical_unit_cost >= 0 AND lineage_depth >= 1");
                    table.ForeignKey(
                        name: "f_k_exchange_replacement_lines__product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_return_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    exchange_id = table.Column<long>(type: "bigint", nullable: false),
                    source_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sale_id = table.Column<long>(type: "bigint", nullable: true),
                    sale_item_id = table.Column<long>(type: "bigint", nullable: true),
                    source_replacement_line_id = table.Column<long>(type: "bigint", nullable: true),
                    root_sale_item_id = table.Column<long>(type: "bigint", nullable: false),
                    lineage_depth = table.Column<int>(type: "integer", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    returned_quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    credit_net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    credit_vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    credit_gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cost_restored = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    packaging_reusable = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("p_k_exchange_return_lines", x => x.id);
                    table.CheckConstraint("ck_exchange_return_lines_condition", "condition = 'resellable'");
                    table.CheckConstraint("ck_exchange_return_lines_source", "(source_kind = 'sale_item' AND sale_item_id IS NOT NULL AND sale_id IS NOT NULL AND source_replacement_line_id IS NULL) OR (source_kind = 'exchange_replacement_line' AND source_replacement_line_id IS NOT NULL AND sale_item_id IS NULL)");
                    table.CheckConstraint("ck_exchange_return_lines_values", "returned_quantity > 0 AND credit_gross = credit_net + credit_vat AND cost_restored >= 0 AND lineage_depth >= 0");
                    table.ForeignKey(
                        name: "f_k_exchange_return_lines__product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_return_lines__sale_items_sale_item_id",
                        column: x => x.sale_item_id,
                        principalTable: "sale_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_return_lines__sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_return_lines_exchange_replacement_lines_source_rep~",
                        column: x => x.source_replacement_line_id,
                        principalTable: "exchange_replacement_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_reversals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    exchange_id = table.Column<long>(type: "bigint", nullable: false),
                    reversal_number = table.Column<string>(type: "character varying(70)", maxLength: 70, nullable: false),
                    business_date = table.Column<DateTime>(type: "date", nullable: false),
                    receiving_outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    terminal_id = table.Column<long>(type: "bigint", nullable: false),
                    cash_session_id = table.Column<long>(type: "bigint", nullable: false),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    net_adjustment_gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_exchange_reversals", x => x.id);
                    table.CheckConstraint("ck_exchange_reversals_reason", "length(trim(reason)) > 0");
                    table.ForeignKey(
                        name: "f_k_exchange_reversals__journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_reversals__users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_reversals_cash_sessions_cash_session_id",
                        column: x => x.cash_session_id,
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchanges",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    exchange_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    receiving_outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    terminal_id = table.Column<long>(type: "bigint", nullable: false),
                    cash_session_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateTime>(type: "date", nullable: false),
                    eligibility_local_date = table.Column<DateTime>(type: "date", nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cashier_user_id = table.Column<long>(type: "bigint", nullable: false),
                    salesperson_user_id = table.Column<long>(type: "bigint", nullable: true),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reason_other_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    internal_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    phone_verification_result = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    return_net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    return_vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    return_gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    return_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    replacement_net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    replacement_discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    replacement_vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    replacement_gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    replacement_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    net_adjustment_net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net_adjustment_vat = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net_adjustment_gross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    high_value_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    high_value_threshold_applied = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    settlement_direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    settlement_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    policy_version_id = table.Column<long>(type: "bigint", nullable: false),
                    policy_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    preview_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    loyalty_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reversal_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_exchanges", x => x.id);
                    table.CheckConstraint("ck_exchanges_loyalty_status", "loyalty_status IN ('module_inactive','applied','pending_reconciliation','not_eligible')");
                    table.CheckConstraint("ck_exchanges_phone_result", "phone_verification_result IN ('not_required','skipped_walkin','matched_customer_record')");
                    table.CheckConstraint("ck_exchanges_reason", "reason_code IN ('size_fitting','colour_design','wrong_product_purchased','customer_preference_changed','other') AND (reason_code <> 'other' OR (reason_other_text IS NOT NULL AND length(trim(reason_other_text)) > 0))");
                    table.CheckConstraint("ck_exchanges_settlement", "settlement_direction IN ('collect','refund','none') AND settlement_amount = abs(net_adjustment_gross) AND ((settlement_direction = 'none') = (net_adjustment_gross = 0)) AND ((settlement_direction = 'collect') = (net_adjustment_gross > 0))");
                    table.CheckConstraint("ck_exchanges_status", "status IN ('completed','reversed')");
                    table.CheckConstraint("ck_exchanges_totals", "return_gross = return_net + return_vat AND replacement_gross = replacement_net + replacement_vat AND net_adjustment_gross = replacement_gross - return_gross AND net_adjustment_net = replacement_net - return_net AND net_adjustment_vat = replacement_vat - return_vat");
                    table.ForeignKey(
                        name: "f_k_exchanges__journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchanges__outlets_receiving_outlet_id",
                        column: x => x.receiving_outlet_id,
                        principalTable: "outlets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchanges__pos_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalTable: "pos_terminals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchanges__users_cashier_user_id",
                        column: x => x.cashier_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchanges__users_salesperson_user_id",
                        column: x => x.salesperson_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchanges_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchanges_cash_sessions_cash_session_id",
                        column: x => x.cash_session_id,
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchanges_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchanges_exchange_policy_versions_policy_version_id",
                        column: x => x.policy_version_id,
                        principalTable: "exchange_policy_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchanges_exchange_reversals_reversal_id",
                        column: x => x.reversal_id,
                        principalTable: "exchange_reversals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_salesperson_adjustments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    exchange_id = table.Column<long>(type: "bigint", nullable: false),
                    exchange_reversal_id = table.Column<long>(type: "bigint", nullable: true),
                    business_date = table.Column<DateTime>(type: "date", nullable: false),
                    receiving_outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    cashier_user_id = table.Column<long>(type: "bigint", nullable: false),
                    salesperson_user_id = table.Column<long>(type: "bigint", nullable: true),
                    positive_amount_net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    negative_amount_net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    signed_amount_net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    target_effect_amount_net = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    negative_mode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    policy_version_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_exchange_salesperson_adjustments", x => x.id);
                    table.CheckConstraint("ck_exchange_salesperson_adjustments_values", "positive_amount_net >= 0 AND negative_amount_net <= 0 AND signed_amount_net = positive_amount_net + negative_amount_net");
                    table.ForeignKey(
                        name: "f_k_exchange_salesperson_adjustments_exchange_reversals_exchang~",
                        column: x => x.exchange_reversal_id,
                        principalTable: "exchange_reversals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_salesperson_adjustments_exchanges_exchange_id",
                        column: x => x.exchange_id,
                        principalTable: "exchanges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_settlements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    exchange_id = table.Column<long>(type: "bigint", nullable: false),
                    exchange_reversal_id = table.Column<long>(type: "bigint", nullable: true),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tendered = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    change_given = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    cash_session_id = table.Column<long>(type: "bigint", nullable: false),
                    cash_session_movement_id = table.Column<long>(type: "bigint", nullable: true),
                    external_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    external_confirmed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    external_confirmed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_exchange_settlements", x => x.id);
                    table.CheckConstraint("ck_exchange_settlements_refund_evidence", "NOT (direction = 'refund' AND method <> 'cash') OR (external_reference IS NOT NULL AND external_confirmed_by_user_id IS NOT NULL AND external_confirmed_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_exchange_settlements_values", "direction IN ('collect','refund') AND method IN ('cash','card','mobile') AND amount > 0 AND (tendered IS NULL OR (method = 'cash' AND direction = 'collect' AND tendered >= amount))");
                    table.ForeignKey(
                        name: "f_k_exchange_settlements_cash_session_movements_cash_session_mo~",
                        column: x => x.cash_session_movement_id,
                        principalTable: "cash_session_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_settlements_cash_sessions_cash_session_id",
                        column: x => x.cash_session_id,
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_settlements_exchange_reversals_exchange_reversal_id",
                        column: x => x.exchange_reversal_id,
                        principalTable: "exchange_reversals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_settlements_exchanges_exchange_id",
                        column: x => x.exchange_id,
                        principalTable: "exchanges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "exchange_source_invoices",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    exchange_id = table.Column<long>(type: "bigint", nullable: false),
                    sale_id = table.Column<long>(type: "bigint", nullable: false),
                    original_outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    invoice_number_snapshot = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    sale_business_date_snapshot = table.Column<DateTime>(type: "date", nullable: true),
                    purchase_local_date = table.Column<DateTime>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_exchange_source_invoices", x => x.id);
                    table.ForeignKey(
                        name: "f_k_exchange_source_invoices__sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_exchange_source_invoices_exchanges_exchange_id",
                        column: x => x.exchange_id,
                        principalTable: "exchanges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_session_movements_effect",
                table: "cash_session_movements",
                sql: "(movement_type IN ('cash_refund','drawer_expense','exchange_cash_out') AND cash_effect < 0) OR (movement_type IN ('drawer_expense_reversal','exchange_cash_in') AND cash_effect > 0) OR (movement_type = 'correction' AND cash_effect <> 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_session_movements_type",
                table: "cash_session_movements",
                sql: "movement_type IN ('cash_refund','drawer_expense','drawer_expense_reversal','correction','exchange_cash_in','exchange_cash_out')");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_approvals_business_id_created_at_utc",
                table: "exchange_approvals",
                columns: new[] { "business_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_approvals_cashier_user_id",
                table: "exchange_approvals",
                column: "cashier_user_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_approvals_consumed_by_exchange_id",
                table: "exchange_approvals",
                column: "consumed_by_exchange_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_approvals_consumed_by_reversal_id",
                table: "exchange_approvals",
                column: "consumed_by_reversal_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_approvals_supervisor_user_id",
                table: "exchange_approvals",
                column: "supervisor_user_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_approvals_token_hash",
                table: "exchange_approvals",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_lineage_links_replacement_line_id",
                table: "exchange_lineage_links",
                column: "replacement_line_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_number_counters_outlet_id",
                table: "exchange_number_counters",
                column: "outlet_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_policy_versions_business_id_version_no",
                table: "exchange_policy_versions",
                columns: new[] { "business_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_policy_versions_created_by_user_id",
                table: "exchange_policy_versions",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_receipt_print_history_exchange_id",
                table: "exchange_receipt_print_history",
                column: "exchange_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_receipt_print_history_printed_by_user_id",
                table: "exchange_receipt_print_history",
                column: "printed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_receipt_print_history_terminal_id",
                table: "exchange_receipt_print_history",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_receipt_snapshots_exchange_id_exchange_reversal_id",
                table: "exchange_receipt_snapshots",
                columns: new[] { "exchange_id", "exchange_reversal_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_receipt_snapshots_exchange_reversal_id",
                table: "exchange_receipt_snapshots",
                column: "exchange_reversal_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_replacement_line_components_component_variant_id",
                table: "exchange_replacement_line_components",
                column: "component_variant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_replacement_line_components_replacement_line_id",
                table: "exchange_replacement_line_components",
                column: "replacement_line_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_replacement_lines_exchange_id",
                table: "exchange_replacement_lines",
                column: "exchange_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_replacement_lines_variant_id",
                table: "exchange_replacement_lines",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_return_lines_exchange_id",
                table: "exchange_return_lines",
                column: "exchange_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_return_lines_sale_id",
                table: "exchange_return_lines",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_return_lines_sale_item_id",
                table: "exchange_return_lines",
                column: "sale_item_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_return_lines_source_replacement_line_id",
                table: "exchange_return_lines",
                column: "source_replacement_line_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_return_lines_variant_id",
                table: "exchange_return_lines",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_reversals_actor_user_id",
                table: "exchange_reversals",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_reversals_business_id_idempotency_key",
                table: "exchange_reversals",
                columns: new[] { "business_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_reversals_cash_session_id",
                table: "exchange_reversals",
                column: "cash_session_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_reversals_exchange_id",
                table: "exchange_reversals",
                column: "exchange_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_reversals_journal_entry_id",
                table: "exchange_reversals",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_salesperson_adjustments_business_id_business_date_~",
                table: "exchange_salesperson_adjustments",
                columns: new[] { "business_id", "business_date", "receiving_outlet_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_salesperson_adjustments_business_id_salesperson_us~",
                table: "exchange_salesperson_adjustments",
                columns: new[] { "business_id", "salesperson_user_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_salesperson_adjustments_exchange_id_exchange_rever~",
                table: "exchange_salesperson_adjustments",
                columns: new[] { "exchange_id", "exchange_reversal_id" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_salesperson_adjustments_exchange_reversal_id",
                table: "exchange_salesperson_adjustments",
                column: "exchange_reversal_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_settlements_cash_session_id",
                table: "exchange_settlements",
                column: "cash_session_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_settlements_cash_session_movement_id",
                table: "exchange_settlements",
                column: "cash_session_movement_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_settlements_exchange_id",
                table: "exchange_settlements",
                column: "exchange_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_settlements_exchange_reversal_id",
                table: "exchange_settlements",
                column: "exchange_reversal_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_source_invoices_exchange_id_sale_id",
                table: "exchange_source_invoices",
                columns: new[] { "exchange_id", "sale_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_source_invoices_invoice_number_snapshot",
                table: "exchange_source_invoices",
                column: "invoice_number_snapshot");

            migrationBuilder.CreateIndex(
                name: "i_x_exchange_source_invoices_sale_id",
                table: "exchange_source_invoices",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_business_id_business_date_receiving_outlet_id",
                table: "exchanges",
                columns: new[] { "business_id", "business_date", "receiving_outlet_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_business_id_customer_id_completed_at_utc",
                table: "exchanges",
                columns: new[] { "business_id", "customer_id", "completed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_business_id_exchange_number",
                table: "exchanges",
                columns: new[] { "business_id", "exchange_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_business_id_idempotency_key",
                table: "exchanges",
                columns: new[] { "business_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_business_id_salesperson_user_id_business_date",
                table: "exchanges",
                columns: new[] { "business_id", "salesperson_user_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_cash_session_id",
                table: "exchanges",
                column: "cash_session_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_cashier_user_id",
                table: "exchanges",
                column: "cashier_user_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_customer_id",
                table: "exchanges",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_journal_entry_id",
                table: "exchanges",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_policy_version_id",
                table: "exchanges",
                column: "policy_version_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_receiving_outlet_id",
                table: "exchanges",
                column: "receiving_outlet_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_reversal_id",
                table: "exchanges",
                column: "reversal_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_salesperson_user_id",
                table: "exchanges",
                column: "salesperson_user_id");

            migrationBuilder.CreateIndex(
                name: "i_x_exchanges_terminal_id",
                table: "exchanges",
                column: "terminal_id");

            migrationBuilder.AddForeignKey(
                name: "f_k_exchange_approvals_exchange_reversals_consumed_by_reversal_~",
                table: "exchange_approvals",
                column: "consumed_by_reversal_id",
                principalTable: "exchange_reversals",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_exchange_approvals_exchanges_consumed_by_exchange_id",
                table: "exchange_approvals",
                column: "consumed_by_exchange_id",
                principalTable: "exchanges",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_exchange_lineage_links_exchange_replacement_lines_replaceme~",
                table: "exchange_lineage_links",
                column: "replacement_line_id",
                principalTable: "exchange_replacement_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_exchange_lineage_links_exchange_return_lines_return_line_id",
                table: "exchange_lineage_links",
                column: "return_line_id",
                principalTable: "exchange_return_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_exchange_receipt_print_history_exchanges_exchange_id",
                table: "exchange_receipt_print_history",
                column: "exchange_id",
                principalTable: "exchanges",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_exchange_receipt_snapshots_exchange_reversals_exchange_reve~",
                table: "exchange_receipt_snapshots",
                column: "exchange_reversal_id",
                principalTable: "exchange_reversals",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_exchange_receipt_snapshots_exchanges_exchange_id",
                table: "exchange_receipt_snapshots",
                column: "exchange_id",
                principalTable: "exchanges",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_exchange_replacement_line_components_exchange_replacement_l~",
                table: "exchange_replacement_line_components",
                column: "replacement_line_id",
                principalTable: "exchange_replacement_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_exchange_replacement_lines_exchanges_exchange_id",
                table: "exchange_replacement_lines",
                column: "exchange_id",
                principalTable: "exchanges",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_exchange_return_lines_exchanges_exchange_id",
                table: "exchange_return_lines",
                column: "exchange_id",
                principalTable: "exchanges",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_exchange_reversals_exchanges_exchange_id",
                table: "exchange_reversals",
                column: "exchange_id",
                principalTable: "exchanges",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(ImmutabilityTriggersSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropImmutabilityTriggersSql);

            migrationBuilder.DropForeignKey(
                name: "f_k_exchanges_exchange_reversals_reversal_id",
                table: "exchanges");

            migrationBuilder.DropTable(
                name: "exchange_approvals");

            migrationBuilder.DropTable(
                name: "exchange_lineage_links");

            migrationBuilder.DropTable(
                name: "exchange_number_counters");

            migrationBuilder.DropTable(
                name: "exchange_receipt_print_history");

            migrationBuilder.DropTable(
                name: "exchange_receipt_snapshots");

            migrationBuilder.DropTable(
                name: "exchange_replacement_line_components");

            migrationBuilder.DropTable(
                name: "exchange_salesperson_adjustments");

            migrationBuilder.DropTable(
                name: "exchange_settlements");

            migrationBuilder.DropTable(
                name: "exchange_source_invoices");

            migrationBuilder.DropTable(
                name: "exchange_return_lines");

            migrationBuilder.DropTable(
                name: "exchange_replacement_lines");

            migrationBuilder.DropTable(
                name: "exchange_reversals");

            migrationBuilder.DropTable(
                name: "exchanges");

            migrationBuilder.DropTable(
                name: "exchange_policy_versions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_session_movements_effect",
                table: "cash_session_movements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_session_movements_type",
                table: "cash_session_movements");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_session_movements_effect",
                table: "cash_session_movements",
                sql: "(movement_type IN ('cash_refund','drawer_expense') AND cash_effect < 0) OR (movement_type = 'drawer_expense_reversal' AND cash_effect > 0) OR (movement_type = 'correction' AND cash_effect <> 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_session_movements_type",
                table: "cash_session_movements",
                sql: "movement_type IN ('cash_refund','drawer_expense','drawer_expense_reversal','correction')");
        }
    }
}
