using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
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
                    raw_ip = table.Column<string>(type: "text", nullable: true),
                    local_machine_ip = table.Column<string>(type: "text", nullable: true),
                    is_local_request = table.Column<bool>(type: "boolean", nullable: false),
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
                name: "businesses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    subscription_plan = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    trial_ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    subscription_ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    max_outlets = table.Column<int>(type: "integer", nullable: true),
                    max_users = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_businesses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    parent_category_id = table.Column<long>(type: "bigint", nullable: true),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_categories", x => x.id);
                    table.ForeignKey(
                        name: "f_k_categories_categories_parent_category_id",
                        column: x => x.parent_category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    customer_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    loyalty_points = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_rules",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    rule_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    target_id = table.Column<long>(type: "bigint", nullable: true),
                    discount_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    min_quantity = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    valid_to = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_price_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    permissions = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "variations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    default_select_all_options = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_variations", x => x.id);
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
                    is_internal_operation = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    internal_operation_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
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
                name: "accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_accounts", x => x.id);
                    table.ForeignKey(
                        name: "f_k_accounts__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

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
                name: "business_feature_settings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    feature_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    limit_value = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_business_feature_settings", x => x.id);
                    table.ForeignKey(
                        name: "f_k_business_feature_settings_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "business_reminder_notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    reminder_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    target_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_delivered = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_business_reminder_notifications", x => x.id);
                    table.ForeignKey(
                        name: "f_k_business_reminder_notifications_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chart_accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    parent_account_id = table.Column<long>(type: "bigint", nullable: true),
                    account_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    account_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    account_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normal_balance_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_system_account = table.Column<bool>(type: "boolean", nullable: false),
                    is_control_account = table.Column<bool>(type: "boolean", nullable: false),
                    allow_manual_posting = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_chart_accounts", x => x.id);
                    table.ForeignKey(
                        name: "f_k_chart_accounts_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_chart_accounts_chart_accounts_parent_account_id",
                        column: x => x.parent_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "financial_years",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    year_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    is_closed = table.Column<bool>(type: "boolean", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_financial_years", x => x.id);
                    table.ForeignKey(
                        name: "f_k_financial_years_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    contact = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    credit_limit = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    payment_terms_days = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_suppliers", x => x.id);
                    table.ForeignKey(
                        name: "f_k_suppliers_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    product_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sku = table.Column<string>(type: "text", nullable: true),
                    barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    category_id = table.Column<long>(type: "bigint", nullable: false),
                    base_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    cost_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    tax_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    has_variants = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)1),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_products", x => x.id);
                    table.ForeignKey(
                        name: "f_k_products_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "audit_event_field_changes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    audit_event_entity_id = table.Column<long>(type: "bigint", nullable: false),
                    field_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true),
                    old_display_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    new_display_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reference_entity_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    is_reference_field = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
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

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    transaction_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reference_id = table.Column<long>(type: "bigint", nullable: true),
                    reference_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_transactions", x => x.id);
                    table.ForeignKey(
                        name: "f_k_transactions_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "accounting_configurations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    cash_account_id = table.Column<long>(type: "bigint", nullable: true),
                    petty_cash_account_id = table.Column<long>(type: "bigint", nullable: true),
                    default_bank_account_id = table.Column<long>(type: "bigint", nullable: true),
                    inventory_account_id = table.Column<long>(type: "bigint", nullable: true),
                    sales_account_id = table.Column<long>(type: "bigint", nullable: true),
                    sales_discount_account_id = table.Column<long>(type: "bigint", nullable: true),
                    sales_return_account_id = table.Column<long>(type: "bigint", nullable: true),
                    cogs_account_id = table.Column<long>(type: "bigint", nullable: true),
                    inventory_shortage_account_id = table.Column<long>(type: "bigint", nullable: true),
                    inventory_gain_account_id = table.Column<long>(type: "bigint", nullable: true),
                    supplier_payable_account_id = table.Column<long>(type: "bigint", nullable: true),
                    supplier_advance_account_id = table.Column<long>(type: "bigint", nullable: true),
                    customer_advance_account_id = table.Column<long>(type: "bigint", nullable: true),
                    other_income_account_id = table.Column<long>(type: "bigint", nullable: true),
                    owner_capital_account_id = table.Column<long>(type: "bigint", nullable: true),
                    owner_drawings_account_id = table.Column<long>(type: "bigint", nullable: true),
                    retained_earnings_account_id = table.Column<long>(type: "bigint", nullable: true),
                    rounding_difference_account_id = table.Column<long>(type: "bigint", nullable: true),
                    cash_difference_gain_account_id = table.Column<long>(type: "bigint", nullable: true),
                    cash_difference_loss_account_id = table.Column<long>(type: "bigint", nullable: true),
                    vat_payable_account_id = table.Column<long>(type: "bigint", nullable: true),
                    vat_receivable_account_id = table.Column<long>(type: "bigint", nullable: true),
                    card_clearing_account_id = table.Column<long>(type: "bigint", nullable: true),
                    mobile_mfs_clearing_account_id = table.Column<long>(type: "bigint", nullable: true),
                    settlement_fee_expense_account_id = table.Column<long>(type: "bigint", nullable: true),
                    bank_fee_expense_account_id = table.Column<long>(type: "bigint", nullable: true),
                    bank_interest_income_account_id = table.Column<long>(type: "bigint", nullable: true),
                    bank_clearing_activation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    default_supplier_payment_terms_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    journal_number_prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    journal_number_padding = table.Column<int>(type: "integer", nullable: false),
                    journal_sequence_reset_policy = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    manual_journal_workflow_mode = table.Column<string>(type: "text", nullable: false),
                    manual_journal_allow_self_approval = table.Column<bool>(type: "boolean", nullable: false),
                    manual_journal_attachment_max_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_accounting_configurations", x => x.id);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_card_clearing_accou~",
                        column: x => x.card_clearing_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_cash_account_id",
                        column: x => x.cash_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_cash_difference_gai~",
                        column: x => x.cash_difference_gain_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_cash_difference_los~",
                        column: x => x.cash_difference_loss_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_cogs_account_id",
                        column: x => x.cogs_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_customer_advance_ac~",
                        column: x => x.customer_advance_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_default_bank_accoun~",
                        column: x => x.default_bank_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_inventory_account_id",
                        column: x => x.inventory_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_inventory_gain_acco~",
                        column: x => x.inventory_gain_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_inventory_shortage_~",
                        column: x => x.inventory_shortage_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_mobile_mfs_clearing~",
                        column: x => x.mobile_mfs_clearing_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_other_income_accoun~",
                        column: x => x.other_income_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_owner_capital_accou~",
                        column: x => x.owner_capital_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_owner_drawings_acco~",
                        column: x => x.owner_drawings_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_petty_cash_account_~",
                        column: x => x.petty_cash_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_retained_earnings_a~",
                        column: x => x.retained_earnings_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_rounding_difference~",
                        column: x => x.rounding_difference_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_sales_account_id",
                        column: x => x.sales_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_sales_discount_acco~",
                        column: x => x.sales_discount_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_sales_return_accoun~",
                        column: x => x.sales_return_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_supplier_advance_ac~",
                        column: x => x.supplier_advance_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_supplier_payable_ac~",
                        column: x => x.supplier_payable_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_vat_payable_account~",
                        column: x => x.vat_payable_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_configurations__chart_accounts_vat_receivable_acco~",
                        column: x => x.vat_receivable_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_categories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    expense_account_id = table.Column<long>(type: "bigint", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_expense_categories", x => x.id);
                    table.ForeignKey(
                        name: "f_k_expense_categories_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_expense_categories_chart_accounts_expense_account_id",
                        column: x => x.expense_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_number_sequences",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    financial_year_id = table.Column<long>(type: "bigint", nullable: true),
                    scope_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    padding = table.Column<int>(type: "integer", nullable: false),
                    last_issued_number = table.Column<long>(type: "bigint", nullable: false),
                    reset_policy = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_accounting_number_sequences", x => x.id);
                    table.ForeignKey(
                        name: "f_k_accounting_number_sequences__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_accounting_number_sequences__financial_years_financial_year_id",
                        column: x => x.financial_year_id,
                        principalTable: "financial_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_periods",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    financial_year_id = table.Column<long>(type: "bigint", nullable: false),
                    period_number = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<long>(type: "bigint", nullable: true),
                    locked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    locked_by = table.Column<long>(type: "bigint", nullable: true),
                    reopened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reopened_by = table.Column<long>(type: "bigint", nullable: true),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_accounting_periods", x => x.id);
                    table.ForeignKey(
                        name: "f_k_accounting_periods__financial_years_financial_year_id",
                        column: x => x.financial_year_id,
                        principalTable: "financial_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_payments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: true),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    payment_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    posting_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    business_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payment_method = table.Column<string>(type: "text", nullable: false),
                    payment_account_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    allocated_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    advance_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    reference_number = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reversal_of_payment_id = table.Column<long>(type: "bigint", nullable: true),
                    reversal_journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<long>(type: "bigint", nullable: true),
                    reversal_reason = table.Column<string>(type: "text", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    posted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    submitted_by = table.Column<long>(type: "bigint", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_supplier_payments", x => x.id);
                    table.ForeignKey(
                        name: "f_k_supplier_payments_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_images",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    original_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    size = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    original_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    medium_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    thumb_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_product_images", x => x.id);
                    table.ForeignKey(
                        name: "f_k_product_images_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product_variants",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    barcode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    attributes = table.Column<string>(type: "jsonb", nullable: false),
                    price_adjustment = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    cost_adjustment = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_product_variants", x => x.id);
                    table.ForeignKey(
                        name: "f_k_product_variants_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "product_variation_selected_options",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<long>(type: "bigint", nullable: false),
                    variation_id = table.Column<long>(type: "bigint", nullable: false),
                    option_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_product_variation_selected_options", x => x.id);
                    table.ForeignKey(
                        name: "f_k_product_variation_selected_options_products_product_id",
                        column: x => x.product_id,
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_product_variation_selected_options_variation_options_option~",
                        column: x => x.option_id,
                        principalTable: "variation_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_product_variation_selected_options_variations_variation_id",
                        column: x => x.variation_id,
                        principalTable: "variations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_period_close_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    accounting_period_id = table.Column<long>(type: "bigint", nullable: false),
                    run_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evaluation_sequence = table.Column<int>(type: "integer", nullable: false),
                    prepared_by = table.Column<long>(type: "bigint", nullable: false),
                    prepared_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    executed_by = table.Column<long>(type: "bigint", nullable: true),
                    executed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    execution_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reopened_by = table.Column<long>(type: "bigint", nullable: true),
                    reopened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reopen_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_accounting_period_close_runs", x => x.id);
                    table.ForeignKey(
                        name: "f_k_accounting_period_close_runs__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_accounting_period_close_runs_accounting_periods_accounting_~",
                        column: x => x.accounting_period_id,
                        principalTable: "accounting_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_advances",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: true),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    payment_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    posting_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    business_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payment_method = table.Column<string>(type: "text", nullable: false),
                    payment_account_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    remaining_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    applied_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    reference_number = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "text", nullable: true),
                    source_payment_id = table.Column<long>(type: "bigint", nullable: true),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    related_payment_id = table.Column<long>(type: "bigint", nullable: true),
                    reversal_of_advance_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    submitted_by = table.Column<long>(type: "bigint", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    posted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_supplier_advances", x => x.id);
                    table.ForeignKey(
                        name: "f_k_supplier_advances__supplier_payments_source_payment_id",
                        column: x => x.source_payment_id,
                        principalTable: "supplier_payments",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "f_k_supplier_advances_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inventories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    low_stock_threshold = table.Column<int>(type: "integer", nullable: false),
                    batch_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    expiry_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_inventories", x => x.id);
                    table.ForeignKey(
                        name: "f_k_inventories__product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.CreateTable(
                name: "accounting_close_gate_snapshots",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    accounting_period_close_run_id = table.Column<long>(type: "bigint", nullable: false),
                    evaluation_sequence = table.Column<int>(type: "integer", nullable: false),
                    gate_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    actual_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_historical_cutover_limitation = table.Column<bool>(type: "boolean", nullable: false),
                    evaluated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_accounting_close_gate_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "f_k_accounting_close_gate_snapshots__accounting_period_close_runs_a~",
                        column: x => x.accounting_period_close_run_id,
                        principalTable: "accounting_period_close_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_close_exception_approvals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    accounting_period_close_run_id = table.Column<long>(type: "bigint", nullable: false),
                    accounting_close_gate_snapshot_id = table.Column<long>(type: "bigint", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    approved_by = table.Column<long>(type: "bigint", nullable: false),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_accounting_close_exception_approvals", x => x.id);
                    table.ForeignKey(
                        name: "f_k_accounting_close_exception_approvals__accounting_close_gate_sna~",
                        column: x => x.accounting_close_gate_snapshot_id,
                        principalTable: "accounting_close_gate_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_close_exception_approvals__accounting_period_close_r~",
                        column: x => x.accounting_period_close_run_id,
                        principalTable: "accounting_period_close_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accounting_financial_year_close_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    financial_year_id = table.Column<long>(type: "bigint", nullable: false),
                    final_accounting_period_id = table.Column<long>(type: "bigint", nullable: false),
                    final_period_close_run_id = table.Column<long>(type: "bigint", nullable: false),
                    run_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prepared_by = table.Column<long>(type: "bigint", nullable: false),
                    prepared_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    executed_by = table.Column<long>(type: "bigint", nullable: true),
                    executed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    year_end_journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reversal_journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reopened_by = table.Column<long>(type: "bigint", nullable: true),
                    reopened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reopen_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_accounting_financial_year_close_runs", x => x.id);
                    table.ForeignKey(
                        name: "f_k_accounting_financial_year_close_runs__accounting_period_close_r~",
                        column: x => x.final_period_close_run_id,
                        principalTable: "accounting_period_close_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_financial_year_close_runs__accounting_periods_fina~",
                        column: x => x.final_accounting_period_id,
                        principalTable: "accounting_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_financial_year_close_runs__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_accounting_financial_year_close_runs__financial_years_financi~",
                        column: x => x.financial_year_id,
                        principalTable: "financial_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "accounting_outlet_configurations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_cash_account_id = table.Column<long>(type: "bigint", nullable: true),
                    outlet_petty_cash_account_id = table.Column<long>(type: "bigint", nullable: true),
                    outlet_bank_account_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_accounting_outlet_configurations", x => x.id);
                    table.ForeignKey(
                        name: "f_k_accounting_outlet_configurations__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_accounting_outlet_configurations__chart_accounts_outlet_bank_~",
                        column: x => x.outlet_bank_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_outlet_configurations__chart_accounts_outlet_cash_~",
                        column: x => x.outlet_cash_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_accounting_outlet_configurations__chart_accounts_outlet_petty~",
                        column: x => x.outlet_petty_cash_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: true),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    module = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entity_id = table.Column<long>(type: "bigint", nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_accounts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    chart_account_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: true),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    account_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    reconciliation_start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    opening_statement_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_bank_accounts", x => x.id);
                    table.ForeignKey(
                        name: "f_k_bank_accounts__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_bank_accounts__chart_accounts_chart_account_id",
                        column: x => x.chart_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_reconciliation_sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_account_id = table.Column<long>(type: "bigint", nullable: false),
                    period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    statement_opening_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    statement_closing_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    gl_opening_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    prepared_by = table.Column<long>(type: "bigint", nullable: true),
                    prepared_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reconciled_by = table.Column<long>(type: "bigint", nullable: true),
                    reconciled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<long>(type: "bigint", nullable: true),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_difference = table.Column<decimal>(type: "numeric", nullable: true),
                    close_result_reference = table.Column<string>(type: "text", nullable: true),
                    reopened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reopened_by = table.Column<long>(type: "bigint", nullable: true),
                    reopen_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_bank_reconciliation_sessions", x => x.id);
                    table.ForeignKey(
                        name: "f_k_bank_reconciliation_sessions__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_bank_reconciliation_sessions_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_imports",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_account_id = table.Column<long>(type: "bigint", nullable: false),
                    source_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    source_file_checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_format = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    imported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    statement_period_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    statement_period_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    opening_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    closing_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    imported_row_count = table.Column<int>(type: "integer", nullable: false),
                    accepted_row_count = table.Column<int>(type: "integer", nullable: false),
                    rejected_row_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error_summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_bank_statement_imports", x => x.id);
                    table.ForeignKey(
                        name: "f_k_bank_statement_imports__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_bank_statement_imports_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_reconciliation_matches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_account_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_reconciliation_session_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    statement_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    gl_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    unmatched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    unmatched_by = table.Column<long>(type: "bigint", nullable: true),
                    unmatch_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_bank_reconciliation_matches", x => x.id);
                    table.ForeignKey(
                        name: "f_k_bank_reconciliation_matches__bank_reconciliation_sessions_bank~",
                        column: x => x.bank_reconciliation_session_id,
                        principalTable: "bank_reconciliation_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_bank_reconciliation_matches__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_bank_reconciliation_matches_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_account_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_statement_import_id = table.Column<long>(type: "bigint", nullable: false),
                    source_row_number = table.Column<int>(type: "integer", nullable: false),
                    transaction_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    value_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    external_transaction_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    debit_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    credit_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    signed_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    running_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    normalized_search_text = table.Column<string>(type: "character varying(2200)", maxLength: 2200, nullable: true),
                    raw_reference_text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_bank_statement_lines", x => x.id);
                    table.ForeignKey(
                        name: "f_k_bank_statement_lines__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_bank_statement_lines_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_bank_statement_lines_bank_statement_imports_bank_statement_~",
                        column: x => x.bank_statement_import_id,
                        principalTable: "bank_statement_imports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_exceptions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_account_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_statement_line_id = table.Column<long>(type: "bigint", nullable: false),
                    classification = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resolution_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_by = table.Column<long>(type: "bigint", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_bank_statement_exceptions", x => x.id);
                    table.ForeignKey(
                        name: "f_k_bank_statement_exceptions__bank_statement_lines_bank_statement~",
                        column: x => x.bank_statement_line_id,
                        principalTable: "bank_statement_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_bank_statement_exceptions__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_bank_statement_exceptions_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bank_reconciliation_match_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    bank_reconciliation_match_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_statement_line_id = table.Column<long>(type: "bigint", nullable: true),
                    journal_entry_line_id = table.Column<long>(type: "bigint", nullable: true),
                    signed_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_bank_reconciliation_match_lines", x => x.id);
                    table.ForeignKey(
                        name: "f_k_bank_reconciliation_match_lines__bank_statement_lines_bank_sta~",
                        column: x => x.bank_statement_line_id,
                        principalTable: "bank_statement_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_bank_reconciliation_match_lines_bank_reconciliation_matches~",
                        column: x => x.bank_reconciliation_match_id,
                        principalTable: "bank_reconciliation_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bank_statement_adjustments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_account_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_statement_line_id = table.Column<long>(type: "bigint", nullable: false),
                    adjustment_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reversal_journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    proposed_by = table.Column<long>(type: "bigint", nullable: false),
                    proposed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_bank_statement_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "f_k_bank_statement_adjustments__bank_statement_lines_bank_statemen~",
                        column: x => x.bank_statement_line_id,
                        principalTable: "bank_statement_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_bank_statement_adjustments__businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_bank_statement_adjustments_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.CreateTable(
                name: "bills",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    po_id = table.Column<long>(type: "bigint", nullable: true),
                    amount_due = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    due_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_bills", x => x.id);
                    table.ForeignKey(
                        name: "f_k_bills__suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_sessions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: true),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    terminal_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    opening_cash_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    expected_closing_cash_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    actual_closing_cash_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    difference_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    opened_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    closed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    opening_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    closing_notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_cash_sessions", x => x.id);
                    table.ForeignKey(
                        name: "f_k_cash_sessions_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    expense_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    expense_category_id = table.Column<long>(type: "bigint", nullable: true),
                    expense_account_id = table.Column<long>(type: "bigint", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    taxable_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    tax_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tax_inclusive = table.Column<bool>(type: "boolean", nullable: true),
                    payment_method_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    payment_account_id = table.Column<long>(type: "bigint", nullable: true),
                    reference_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    attachment_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expense_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_by = table.Column<long>(type: "bigint", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejected_by = table.Column<long>(type: "bigint", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_by = table.Column<long>(type: "bigint", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_expenses", x => x.id);
                    table.ForeignKey(
                        name: "f_k_expenses__expense_categories_expense_category_id",
                        column: x => x.expense_category_id,
                        principalTable: "expense_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_expenses_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_expenses_chart_accounts_expense_account_id",
                        column: x => x.expense_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_expenses_chart_accounts_payment_account_id",
                        column: x => x.payment_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "grn_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    grn_id = table.Column<long>(type: "bigint", nullable: false),
                    po_item_id = table.Column<long>(type: "bigint", nullable: false),
                    received_qty = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    taxable_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    tax_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tax_inclusive = table.Column<bool>(type: "boolean", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_grn_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "grns",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    po_id = table.Column<long>(type: "bigint", nullable: false),
                    received_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    supplier_invoice_number = table.Column<string>(type: "text", nullable: true),
                    supplier_invoice_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_grns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "held_sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    cashier_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    items_json = table.Column<string>(type: "jsonb", nullable: false),
                    discount_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    held_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_held_sales", x => x.id);
                    table.ForeignKey(
                        name: "f_k_held_sales_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "journal_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    journal_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    journal_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    transaction_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    posting_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    business_date = table.Column<DateTime>(type: "date", nullable: true),
                    financial_year_id = table.Column<long>(type: "bigint", nullable: true),
                    accounting_period_id = table.Column<long>(type: "bigint", nullable: true),
                    reference_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reference_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    reference_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    outlet_id = table.Column<long>(type: "bigint", nullable: true),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: true),
                    terminal_id = table.Column<long>(type: "bigint", nullable: true),
                    cash_session_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_system_generated = table.Column<bool>(type: "boolean", nullable: false),
                    reversal_of_journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reversed_by_journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    submitted_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejected_by = table.Column<long>(type: "bigint", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    posted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    posted_by = table.Column<long>(type: "bigint", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<long>(type: "bigint", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_journal_entries", x => x.id);
                    table.ForeignKey(
                        name: "f_k_journal_entries_accounting_periods_accounting_period_id",
                        column: x => x.accounting_period_id,
                        principalTable: "accounting_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_journal_entries_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_journal_entries_cash_sessions_cash_session_id",
                        column: x => x.cash_session_id,
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_journal_entries_financial_years_financial_year_id",
                        column: x => x.financial_year_id,
                        principalTable: "financial_years",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_journal_entries_journal_entries_reversal_of_journal_entry_id",
                        column: x => x.reversal_of_journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_journal_entries_journal_entries_reversed_by_journal_entry_id",
                        column: x => x.reversed_by_journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_entry_attachments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    stored_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    relative_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_journal_entry_attachments", x => x.id);
                    table.ForeignKey(
                        name: "f_k_journal_entry_attachments_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "liquidity_transfers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    transfer_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_account_id = table.Column<long>(type: "bigint", nullable: false),
                    destination_account_id = table.Column<long>(type: "bigint", nullable: false),
                    source_bank_account_id = table.Column<long>(type: "bigint", nullable: true),
                    destination_bank_account_id = table.Column<long>(type: "bigint", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    posting_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reversal_journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<long>(type: "bigint", nullable: true),
                    reversal_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_liquidity_transfers", x => x.id);
                    table.ForeignKey(
                        name: "f_k_liquidity_transfers_bank_accounts_destination_bank_account_~",
                        column: x => x.destination_bank_account_id,
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_liquidity_transfers_bank_accounts_source_bank_account_id",
                        column: x => x.source_bank_account_id,
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_liquidity_transfers_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_liquidity_transfers_chart_accounts_destination_account_id",
                        column: x => x.destination_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_liquidity_transfers_chart_accounts_source_account_id",
                        column: x => x.source_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_liquidity_transfers_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_liquidity_transfers_journal_entries_reversal_journal_entry_~",
                        column: x => x.reversal_journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settlement_batches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    settlement_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    clearing_account_id = table.Column<long>(type: "bigint", nullable: false),
                    bank_account_id = table.Column<long>(type: "bigint", nullable: false),
                    settlement_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    gross_clearing_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    settlement_fee_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    net_bank_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reversal_journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<long>(type: "bigint", nullable: true),
                    reversal_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_settlement_batches", x => x.id);
                    table.ForeignKey(
                        name: "f_k_settlement_batches_bank_accounts_bank_account_id",
                        column: x => x.bank_account_id,
                        principalTable: "bank_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_settlement_batches_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_settlement_batches_chart_accounts_clearing_account_id",
                        column: x => x.clearing_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_settlement_batches_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_settlement_batches_journal_entries_reversal_journal_entry_id",
                        column: x => x.reversal_journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "journal_entry_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    debit_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    credit_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: true),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: true),
                    supplier_id = table.Column<long>(type: "bigint", nullable: true),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    payment_method_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    cost_center_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_journal_entry_lines", x => x.id);
                    table.CheckConstraint("c_k_journal_entry_lines_amount_sides", "(debit_amount > 0 AND credit_amount = 0) OR (credit_amount > 0 AND debit_amount = 0)");
                    table.ForeignKey(
                        name: "f_k_journal_entry_lines__suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_journal_entry_lines_chart_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_journal_entry_lines_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_journal_entry_lines_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "other_income_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: true),
                    income_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    income_category = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    income_account_id = table.Column<long>(type: "bigint", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    taxable_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    tax_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tax_inclusive = table.Column<bool>(type: "boolean", nullable: true),
                    payment_method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    payment_account_id = table.Column<long>(type: "bigint", nullable: true),
                    reference_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    attachment_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_by = table.Column<long>(type: "bigint", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejected_by = table.Column<long>(type: "bigint", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_by = table.Column<long>(type: "bigint", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_other_income_entries", x => x.id);
                    table.ForeignKey(
                        name: "f_k_other_income_entries_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_other_income_entries_chart_accounts_income_account_id",
                        column: x => x.income_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_other_income_entries_chart_accounts_payment_account_id",
                        column: x => x.payment_account_id,
                        principalTable: "chart_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_other_income_entries_journal_entries_journal_entry_id",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "outlet_price_overrides",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    product_variant_id = table.Column<long>(type: "bigint", nullable: false),
                    override_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    override_value = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_outlet_price_overrides", x => x.id);
                    table.ForeignKey(
                        name: "f_k_outlet_price_overrides__product_variants_product_variant_id",
                        column: x => x.product_variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outlets",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    contact_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    manager_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_outlets", x => x.id);
                    table.ForeignKey(
                        name: "f_k_outlets_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "pos_terminals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_pos_terminals", x => x.id);
                    table.ForeignKey(
                        name: "f_k_pos_terminals_outlets_outlet_id",
                        column: x => x.outlet_id,
                        principalTable: "outlets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    role_id = table.Column<long>(type: "bigint", nullable: true),
                    outlet_id = table.Column<long>(type: "bigint", nullable: true),
                    permission_overrides = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    inventory_location_access_scope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "assigned_only"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    must_reset_password = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_users_outlets_outlet_id",
                        column: x => x.outlet_id,
                        principalTable: "outlets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_users_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_users_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sale_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    terminal_id = table.Column<long>(type: "bigint", nullable: true),
                    cash_session_id = table.Column<long>(type: "bigint", nullable: true),
                    customer_id = table.Column<long>(type: "bigint", nullable: true),
                    business_date = table.Column<DateTime>(type: "date", nullable: true),
                    sale_date_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sale_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    tax = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cashier_id = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    original_sale_id = table.Column<long>(type: "bigint", nullable: true),
                    replacement_sale_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_sales", x => x.id);
                    table.ForeignKey(
                        name: "f_k_sales__users_cashier_id",
                        column: x => x.cashier_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_sales_cash_sessions_cash_session_id",
                        column: x => x.cash_session_id,
                        principalTable: "cash_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_sales_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_sales_outlets_outlet_id",
                        column: x => x.outlet_id,
                        principalTable: "outlets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_sales_pos_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalTable: "pos_terminals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "stock_adjustments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    adjustment_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_stock_count_id = table.Column<long>(type: "bigint", nullable: true),
                    adjusted_by = table.Column<long>(type: "bigint", nullable: false),
                    adjustment_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejected_by = table.Column<long>(type: "bigint", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancelled_by = table.Column<long>(type: "bigint", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_adjustments_users_adjusted_by",
                        column: x => x.adjusted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_adjustments_users_approved_by",
                        column: x => x.approved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_adjustments_users_cancelled_by",
                        column: x => x.cancelled_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_adjustments_users_rejected_by",
                        column: x => x.rejected_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

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
                name: "stock_ledgers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    location_id = table.Column<long>(type: "bigint", nullable: false),
                    location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    transaction_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    qty_in = table.Column<int>(type: "integer", nullable: false),
                    qty_out = table.Column<int>(type: "integer", nullable: false),
                    balance_after = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    movement_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    is_cost_valuation_reliable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    business_date = table.Column<DateTime>(type: "date", nullable: false),
                    movement_sequence = table.Column<long>(type: "bigint", nullable: false),
                    reversal_of_movement_id = table.Column<long>(type: "bigint", nullable: true),
                    reference_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_id = table.Column<long>(type: "bigint", nullable: false),
                    remarks = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_ledgers", x => x.id);
                    table.ForeignKey(
                        name: "f_k_stock_ledgers__users_creator_id",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_stock_ledgers_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "user_invitations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_user_invitations", x => x.id);
                    table.ForeignKey(
                        name: "f_k_user_invitations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_outlet_assignments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_user_outlet_assignments", x => x.id);
                    table.ForeignKey(
                        name: "f_k_user_outlet_assignments_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_user_outlet_assignments_outlets_outlet_id",
                        column: x => x.outlet_id,
                        principalTable: "outlets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_user_outlet_assignments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_refresh_tokens",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    replaced_by_token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_user_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "f_k_user_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "warehouses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: true),
                    manager_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_warehouses", x => x.id);
                    table.ForeignKey(
                        name: "f_k_warehouses_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_warehouses_users_manager_id",
                        column: x => x.manager_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "receipt_print_histories",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sale_id = table.Column<long>(type: "bigint", nullable: false),
                    terminal_id = table.Column<long>(type: "bigint", nullable: true),
                    printed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    action_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    printed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_receipt_print_histories", x => x.id);
                    table.ForeignKey(
                        name: "f_k_receipt_print_histories__sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_receipt_print_histories__users_printed_by_user_id",
                        column: x => x.printed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_receipt_print_histories_pos_terminals_terminal_id",
                        column: x => x.terminal_id,
                        principalTable: "pos_terminals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "sale_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sale_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    taxable_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    vat_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    tax_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tax_inclusive = table.Column<bool>(type: "boolean", nullable: true),
                    historical_unit_cost = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    applied_rule_id = table.Column<long>(type: "bigint", nullable: true),
                    discount_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    applied_rule_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_sale_items", x => x.id);
                    table.ForeignKey(
                        name: "f_k_sale_items_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_sale_items_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sale_payments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sale_id = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    payment_account_id = table.Column<long>(type: "bigint", nullable: true),
                    tendered = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_sale_payments", x => x.id);
                    table.ForeignKey(
                        name: "f_k_sale_payments_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sale_voids",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sale_id = table.Column<long>(type: "bigint", nullable: false),
                    voided_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    previous_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    voided_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_sale_voids", x => x.id);
                    table.ForeignKey(
                        name: "f_k_sale_voids__users_voided_by_user_id",
                        column: x => x.voided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_sale_voids_sales_sale_id",
                        column: x => x.sale_id,
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_adjustment_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    stock_adjustment_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    previous_quantity = table.Column<int>(type: "integer", nullable: false),
                    quantity_change = table.Column<int>(type: "integer", nullable: false),
                    new_quantity = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    unit_cost_snapshot = table.Column<decimal>(type: "numeric", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_adjustment_lines", x => x.id);
                    table.ForeignKey(
                        name: "f_k_stock_adjustment_lines_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_stock_adjustment_lines_stock_adjustments_stock_adjustment_id",
                        column: x => x.stock_adjustment_id,
                        principalTable: "stock_adjustments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "stock_transfers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transfer_no = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    transfer_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    from_location_id = table.Column<long>(type: "bigint", nullable: false),
                    from_location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    to_location_id = table.Column<long>(type: "bigint", nullable: false),
                    to_location_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    related_requisition_id = table.Column<long>(type: "bigint", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    transfer_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    submitted_by = table.Column<long>(type: "bigint", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    dispatched_by = table.Column<long>(type: "bigint", nullable: true),
                    dispatched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    received_by = table.Column<long>(type: "bigint", nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejected_by = table.Column<long>(type: "bigint", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<long>(type: "bigint", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_transfers", x => x.id);
                    table.ForeignKey(
                        name: "FK_stock_transfers_users_approved_by",
                        column: x => x.approved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_transfers_users_cancelled_by",
                        column: x => x.cancelled_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_transfers_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_transfers_users_dispatched_by",
                        column: x => x.dispatched_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_transfers_users_received_by",
                        column: x => x.received_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_transfers_users_rejected_by",
                        column: x => x.rejected_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_transfers_users_submitted_by",
                        column: x => x.submitted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_stock_transfers_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_stock_transfers_stock_requisitions_related_requisition_id",
                        column: x => x.related_requisition_id,
                        principalTable: "stock_requisitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    po_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    order_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expected_delivery = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_purchase_orders", x => x.id);
                    table.ForeignKey(
                        name: "f_k_purchase_orders__suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_purchase_orders__users_creator_id",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_purchase_orders__warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_payable_documents",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: true),
                    source_document_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_document_id = table.Column<long>(type: "bigint", nullable: false),
                    source_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    source_journal_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    original_payable_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    recognition_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    supplier_invoice_number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    supplier_invoice_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payment_terms_days = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_disputed = table.Column<bool>(type: "boolean", nullable: false),
                    dispute_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_supplier_payable_documents", x => x.id);
                    table.ForeignKey(
                        name: "f_k_supplier_payable_documents__warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "f_k_supplier_payable_documents_journal_entries_source_journal_e~",
                        column: x => x.source_journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_supplier_payable_documents_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_warehouse_assignments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    warehouse_id = table.Column<long>(type: "bigint", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_user_warehouse_assignments", x => x.id);
                    table.ForeignKey(
                        name: "f_k_user_warehouse_assignments__warehouses_warehouse_id",
                        column: x => x.warehouse_id,
                        principalTable: "warehouses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_user_warehouse_assignments_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_user_warehouse_assignments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stock_transfer_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    transfer_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    requested_quantity = table.Column<int>(type: "integer", nullable: false),
                    transfer_quantity = table.Column<int>(type: "integer", nullable: false),
                    accepted_quantity = table.Column<int>(type: "integer", nullable: false),
                    rejected_quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    remarks = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_stock_transfer_items", x => x.id);
                    table.ForeignKey(
                        name: "f_k_stock_transfer_items_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_stock_transfer_items_stock_transfers_transfer_id",
                        column: x => x.transfer_id,
                        principalTable: "stock_transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    po_id = table.Column<long>(type: "bigint", nullable: false),
                    variant_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    tax = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    tax_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tax_inclusive = table.Column<bool>(type: "boolean", nullable: true),
                    unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_purchase_order_items", x => x.id);
                    table.ForeignKey(
                        name: "f_k_purchase_order_items_product_variants_variant_id",
                        column: x => x.variant_id,
                        principalTable: "product_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_purchase_order_items_purchase_orders_purchase_order_id",
                        column: x => x.po_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_advance_utilizations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_advance_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_payable_document_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    effective_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reversal_of_utilization_id = table.Column<long>(type: "bigint", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_supplier_advance_utilizations", x => x.id);
                    table.ForeignKey(
                        name: "f_k_supplier_advance_utilizations__supplier_payable_documents_supp~",
                        column: x => x.supplier_payable_document_id,
                        principalTable: "supplier_payable_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_supplier_advance_utilizations_journal_entries_journal_entry~",
                        column: x => x.journal_entry_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_supplier_advance_utilizations_supplier_advances_supplier_ad~",
                        column: x => x.supplier_advance_id,
                        principalTable: "supplier_advances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_payment_allocations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    supplier_payment_id = table.Column<long>(type: "bigint", nullable: false),
                    payable_document_type = table.Column<string>(type: "text", nullable: false),
                    payable_document_id = table.Column<long>(type: "bigint", nullable: false),
                    supplier_payable_document_id = table.Column<long>(type: "bigint", nullable: true),
                    allocated_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    outstanding_before_allocation = table.Column<decimal>(type: "numeric", nullable: false),
                    outstanding_after_allocation = table.Column<decimal>(type: "numeric", nullable: false),
                    allocation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_reversed = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    effective_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_effective_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversal_journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    journal_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    reversal_of_allocation_id = table.Column<long>(type: "bigint", nullable: true),
                    source_reference = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_supplier_payment_allocations", x => x.id);
                    table.ForeignKey(
                        name: "f_k_supplier_payment_allocations_supplier_payable_documents_sup~",
                        column: x => x.supplier_payable_document_id,
                        principalTable: "supplier_payable_documents",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "f_k_supplier_payment_allocations_supplier_payments_supplier_pay~",
                        column: x => x.supplier_payment_id,
                        principalTable: "supplier_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_close_exception_approvals_accounting_close_gate_~",
                table: "accounting_close_exception_approvals",
                column: "accounting_close_gate_snapshot_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_close_exception_approvals_accounting_period_clos~",
                table: "accounting_close_exception_approvals",
                column: "accounting_period_close_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_close_gate_snapshots_accounting_period_close_run~",
                table: "accounting_close_gate_snapshots",
                columns: new[] { "accounting_period_close_run_id", "evaluation_sequence", "gate_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_business_id",
                table: "accounting_configurations",
                column: "business_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_card_clearing_account_id",
                table: "accounting_configurations",
                column: "card_clearing_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_cash_account_id",
                table: "accounting_configurations",
                column: "cash_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_cash_difference_gain_account_id",
                table: "accounting_configurations",
                column: "cash_difference_gain_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_cash_difference_loss_account_id",
                table: "accounting_configurations",
                column: "cash_difference_loss_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_cogs_account_id",
                table: "accounting_configurations",
                column: "cogs_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_customer_advance_account_id",
                table: "accounting_configurations",
                column: "customer_advance_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_default_bank_account_id",
                table: "accounting_configurations",
                column: "default_bank_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_inventory_account_id",
                table: "accounting_configurations",
                column: "inventory_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_inventory_gain_account_id",
                table: "accounting_configurations",
                column: "inventory_gain_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_inventory_shortage_account_id",
                table: "accounting_configurations",
                column: "inventory_shortage_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_mobile_mfs_clearing_account_id",
                table: "accounting_configurations",
                column: "mobile_mfs_clearing_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_other_income_account_id",
                table: "accounting_configurations",
                column: "other_income_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_owner_capital_account_id",
                table: "accounting_configurations",
                column: "owner_capital_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_owner_drawings_account_id",
                table: "accounting_configurations",
                column: "owner_drawings_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_petty_cash_account_id",
                table: "accounting_configurations",
                column: "petty_cash_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_retained_earnings_account_id",
                table: "accounting_configurations",
                column: "retained_earnings_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_rounding_difference_account_id",
                table: "accounting_configurations",
                column: "rounding_difference_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_sales_account_id",
                table: "accounting_configurations",
                column: "sales_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_sales_discount_account_id",
                table: "accounting_configurations",
                column: "sales_discount_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_sales_return_account_id",
                table: "accounting_configurations",
                column: "sales_return_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_supplier_advance_account_id",
                table: "accounting_configurations",
                column: "supplier_advance_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_supplier_payable_account_id",
                table: "accounting_configurations",
                column: "supplier_payable_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_vat_payable_account_id",
                table: "accounting_configurations",
                column: "vat_payable_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_configurations_vat_receivable_account_id",
                table: "accounting_configurations",
                column: "vat_receivable_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_financial_year_close_runs_final_accounting_perio~",
                table: "accounting_financial_year_close_runs",
                column: "final_accounting_period_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_financial_year_close_runs_final_period_close_run~",
                table: "accounting_financial_year_close_runs",
                column: "final_period_close_run_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_financial_year_close_runs_financial_year_id",
                table: "accounting_financial_year_close_runs",
                column: "financial_year_id",
                unique: true,
                filter: "status IN ('prepared', 'approved', 'executed')");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_financial_year_close_runs_reversal_journal_entry~",
                table: "accounting_financial_year_close_runs",
                column: "reversal_journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_financial_year_close_runs_year_end_journal_entry~",
                table: "accounting_financial_year_close_runs",
                column: "year_end_journal_entry_id",
                unique: true,
                filter: "year_end_journal_entry_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_financial_year_close_runs_business_id_financial_~",
                table: "accounting_financial_year_close_runs",
                columns: new[] { "business_id", "financial_year_id", "run_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_number_sequences_financial_year_id",
                table: "accounting_number_sequences",
                column: "financial_year_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_number_sequences_business_id_sequence_type_scope~",
                table: "accounting_number_sequences",
                columns: new[] { "business_id", "sequence_type", "scope_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_outlet_configurations_outlet_bank_account_id",
                table: "accounting_outlet_configurations",
                column: "outlet_bank_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_outlet_configurations_outlet_cash_account_id",
                table: "accounting_outlet_configurations",
                column: "outlet_cash_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_outlet_configurations_outlet_id",
                table: "accounting_outlet_configurations",
                column: "outlet_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_outlet_configurations_outlet_petty_cash_account_~",
                table: "accounting_outlet_configurations",
                column: "outlet_petty_cash_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_outlet_configurations_business_id_outlet_id",
                table: "accounting_outlet_configurations",
                columns: new[] { "business_id", "outlet_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_period_close_runs_business_id_accounting_period_~",
                table: "accounting_period_close_runs",
                columns: new[] { "business_id", "accounting_period_id", "run_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_period_close_runs_business_id_accounting_period~1",
                table: "accounting_period_close_runs",
                columns: new[] { "business_id", "accounting_period_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_accounting_period_close_runs_period_active",
                table: "accounting_period_close_runs",
                column: "accounting_period_id",
                unique: true,
                filter: "status IN ('prepared', 'approved', 'executed')");

            migrationBuilder.CreateIndex(
                name: "IX_accounting_periods_financial_year_id_period_number",
                table: "accounting_periods",
                columns: new[] { "financial_year_id", "period_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accounting_periods_financial_year_id_status",
                table: "accounting_periods",
                columns: new[] { "financial_year_id", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_accounts_business_id",
                table: "accounts",
                column: "business_id");

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

            migrationBuilder.CreateIndex(
                name: "i_x_audit_logs_user_id",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_timestamp",
                table: "audit_logs",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "i_x_bank_accounts_chart_account_id",
                table: "bank_accounts",
                column: "chart_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_bank_accounts_outlet_id",
                table: "bank_accounts",
                column: "outlet_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_accounts_business_id_chart_account_id",
                table: "bank_accounts",
                columns: new[] { "business_id", "chart_account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_bank_reconciliation_match_lines_bank_reconciliation_match_id",
                table: "bank_reconciliation_match_lines",
                column: "bank_reconciliation_match_id");

            migrationBuilder.CreateIndex(
                name: "i_x_bank_reconciliation_match_lines_bank_statement_line_id",
                table: "bank_reconciliation_match_lines",
                column: "bank_statement_line_id",
                unique: true,
                filter: "bank_statement_line_id IS NOT NULL AND is_active = true");

            migrationBuilder.CreateIndex(
                name: "i_x_bank_reconciliation_match_lines_journal_entry_line_id",
                table: "bank_reconciliation_match_lines",
                column: "journal_entry_line_id",
                unique: true,
                filter: "journal_entry_line_id IS NOT NULL AND is_active = true");

            migrationBuilder.CreateIndex(
                name: "i_x_bank_reconciliation_matches_bank_account_id",
                table: "bank_reconciliation_matches",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_bank_reconciliation_matches_bank_reconciliation_session_id",
                table: "bank_reconciliation_matches",
                column: "bank_reconciliation_session_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_reconciliation_matches_business_id_bank_account_id_sta~",
                table: "bank_reconciliation_matches",
                columns: new[] { "business_id", "bank_account_id", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_bank_reconciliation_sessions_bank_account_id",
                table: "bank_reconciliation_sessions",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_reconciliation_sessions_business_id_bank_account_id_pe~",
                table: "bank_reconciliation_sessions",
                columns: new[] { "business_id", "bank_account_id", "period_start", "period_end" },
                unique: true,
                filter: "status IN ('open','in_progress','ready_for_close')");

            migrationBuilder.CreateIndex(
                name: "i_x_bank_statement_adjustments_bank_account_id",
                table: "bank_statement_adjustments",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_bank_statement_adjustments_bank_statement_line_id",
                table: "bank_statement_adjustments",
                column: "bank_statement_line_id");

            migrationBuilder.CreateIndex(
                name: "i_x_bank_statement_adjustments_journal_entry_id",
                table: "bank_statement_adjustments",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_bank_statement_adjustments_reversal_journal_entry_id",
                table: "bank_statement_adjustments",
                column: "reversal_journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_adjustments_business_id_idempotency_key",
                table: "bank_statement_adjustments",
                columns: new[] { "business_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_bank_statement_exceptions_bank_account_id",
                table: "bank_statement_exceptions",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_bank_statement_exceptions_bank_statement_line_id",
                table: "bank_statement_exceptions",
                column: "bank_statement_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_exceptions_business_id_bank_account_id_status",
                table: "bank_statement_exceptions",
                columns: new[] { "business_id", "bank_account_id", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_bank_statement_imports_bank_account_id",
                table: "bank_statement_imports",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_imports_business_id_bank_account_id_source_f~",
                table: "bank_statement_imports",
                columns: new[] { "business_id", "bank_account_id", "source_file_checksum" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_bank_statement_lines_bank_account_id",
                table: "bank_statement_lines",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_lines_bank_statement_import_id_source_row_nu~",
                table: "bank_statement_lines",
                columns: new[] { "bank_statement_import_id", "source_row_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bank_statement_lines_business_id_bank_account_id_transactio~",
                table: "bank_statement_lines",
                columns: new[] { "business_id", "bank_account_id", "transaction_date" });

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

            migrationBuilder.CreateIndex(
                name: "i_x_bills_supplier_id",
                table: "bills",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_bills_po_id",
                table: "bills",
                column: "po_id");

            migrationBuilder.CreateIndex(
                name: "IX_business_feature_settings_business_id_feature_key",
                table: "business_feature_settings",
                columns: new[] { "business_id", "feature_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_reminder_notifications_business_id_reminder_type_t~",
                table: "business_reminder_notifications",
                columns: new[] { "business_id", "reminder_type", "target_at" });

            migrationBuilder.CreateIndex(
                name: "IX_businesses_name",
                table: "businesses",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_cash_sessions_business_id",
                table: "cash_sessions",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "i_x_cash_sessions_terminal_id",
                table: "cash_sessions",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "IX_cash_sessions_closed_by_user_id",
                table: "cash_sessions",
                column: "closed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_cash_sessions_opened_at",
                table: "cash_sessions",
                column: "opened_at");

            migrationBuilder.CreateIndex(
                name: "IX_cash_sessions_opened_by_user_id",
                table: "cash_sessions",
                column: "opened_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_cash_sessions_outlet_id_business_date",
                table: "cash_sessions",
                columns: new[] { "outlet_id", "business_date" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_sessions_outlet_id_terminal_id_status",
                table: "cash_sessions",
                columns: new[] { "outlet_id", "terminal_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_cash_sessions_one_open_per_terminal",
                table: "cash_sessions",
                columns: new[] { "outlet_id", "terminal_id" },
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "i_x_categories_parent_category_id",
                table: "categories",
                column: "parent_category_id");

            migrationBuilder.CreateIndex(
                name: "i_x_chart_accounts_parent_account_id",
                table: "chart_accounts",
                column: "parent_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_chart_accounts_business_id_account_code",
                table: "chart_accounts",
                columns: new[] { "business_id", "account_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chart_accounts_business_id_account_type_is_active",
                table: "chart_accounts",
                columns: new[] { "business_id", "account_type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "IX_chart_accounts_business_id_parent_account_id",
                table: "chart_accounts",
                columns: new[] { "business_id", "parent_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customers_customer_code",
                table: "customers",
                column: "customer_code",
                unique: true,
                filter: "customer_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_expense_categories_expense_account_id",
                table: "expense_categories",
                column: "expense_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_categories_business_id_name",
                table: "expense_categories",
                columns: new[] { "business_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_expenses_expense_account_id",
                table: "expenses",
                column: "expense_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_expenses_expense_category_id",
                table: "expenses",
                column: "expense_category_id");

            migrationBuilder.CreateIndex(
                name: "i_x_expenses_journal_entry_id",
                table: "expenses",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_expenses_outlet_id",
                table: "expenses",
                column: "outlet_id");

            migrationBuilder.CreateIndex(
                name: "i_x_expenses_payment_account_id",
                table: "expenses",
                column: "payment_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_business_id_expense_date",
                table: "expenses",
                columns: new[] { "business_id", "expense_date" });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_business_id_expense_number",
                table: "expenses",
                columns: new[] { "business_id", "expense_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_expenses_business_id_status",
                table: "expenses",
                columns: new[] { "business_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_years_business_id_is_current",
                table: "financial_years",
                columns: new[] { "business_id", "is_current" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_years_business_id_name",
                table: "financial_years",
                columns: new[] { "business_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_years_business_id_year_code",
                table: "financial_years",
                columns: new[] { "business_id", "year_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_financial_years_business_current_true",
                table: "financial_years",
                column: "business_id",
                unique: true,
                filter: "is_current = true");

            migrationBuilder.CreateIndex(
                name: "i_x_grn_items_grn_id",
                table: "grn_items",
                column: "grn_id");

            migrationBuilder.CreateIndex(
                name: "IX_grn_items_po_item_id",
                table: "grn_items",
                column: "po_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_grns_created_by",
                table: "grns",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_grns_po_id",
                table: "grns",
                column: "po_id");

            migrationBuilder.CreateIndex(
                name: "i_x_held_sales_cashier_id",
                table: "held_sales",
                column: "cashier_id");

            migrationBuilder.CreateIndex(
                name: "i_x_held_sales_customer_id",
                table: "held_sales",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "i_x_held_sales_outlet_id",
                table: "held_sales",
                column: "outlet_id");

            migrationBuilder.CreateIndex(
                name: "IX_held_sales_held_at",
                table: "held_sales",
                column: "held_at");

            migrationBuilder.CreateIndex(
                name: "IX_inventories_variant_id_location_id_location_type",
                table: "inventories",
                columns: new[] { "variant_id", "location_id", "location_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entries_accounting_period_id",
                table: "journal_entries",
                column: "accounting_period_id");

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entries_cash_session_id",
                table: "journal_entries",
                column: "cash_session_id");

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entries_financial_year_id",
                table: "journal_entries",
                column: "financial_year_id");

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entries_outlet_id",
                table: "journal_entries",
                column: "outlet_id");

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entries_reversal_of_journal_entry_id",
                table: "journal_entries",
                column: "reversal_of_journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entries_reversed_by_journal_entry_id",
                table: "journal_entries",
                column: "reversed_by_journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entries_terminal_id",
                table: "journal_entries",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entries_warehouse_id",
                table: "journal_entries",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_business_id_journal_number",
                table: "journal_entries",
                columns: new[] { "business_id", "journal_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_business_id_journal_type_reference_type_ref~",
                table: "journal_entries",
                columns: new[] { "business_id", "journal_type", "reference_type", "reference_id" },
                unique: true,
                filter: "is_system_generated = true AND status <> 'Reversed'");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_business_id_outlet_id_posting_date",
                table: "journal_entries",
                columns: new[] { "business_id", "outlet_id", "posting_date" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_business_id_posting_date",
                table: "journal_entries",
                columns: new[] { "business_id", "posting_date" });

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_status_posting_date",
                table: "journal_entries",
                columns: new[] { "status", "posting_date" });

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entry_attachments_journal_entry_id",
                table: "journal_entry_attachments",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entry_lines_account_id",
                table: "journal_entry_lines",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entry_lines_customer_id",
                table: "journal_entry_lines",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entry_lines_journal_entry_id",
                table: "journal_entry_lines",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entry_lines_outlet_id",
                table: "journal_entry_lines",
                column: "outlet_id");

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entry_lines_supplier_id",
                table: "journal_entry_lines",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "i_x_journal_entry_lines_warehouse_id",
                table: "journal_entry_lines",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entry_lines_account_id_outlet_id",
                table: "journal_entry_lines",
                columns: new[] { "account_id", "outlet_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_liquidity_transfers_destination_account_id",
                table: "liquidity_transfers",
                column: "destination_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_liquidity_transfers_destination_bank_account_id",
                table: "liquidity_transfers",
                column: "destination_bank_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_liquidity_transfers_journal_entry_id",
                table: "liquidity_transfers",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_liquidity_transfers_reversal_journal_entry_id",
                table: "liquidity_transfers",
                column: "reversal_journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_liquidity_transfers_source_account_id",
                table: "liquidity_transfers",
                column: "source_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_liquidity_transfers_source_bank_account_id",
                table: "liquidity_transfers",
                column: "source_bank_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_liquidity_transfers_business_id_idempotency_key",
                table: "liquidity_transfers",
                columns: new[] { "business_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_other_income_entries_income_account_id",
                table: "other_income_entries",
                column: "income_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_other_income_entries_journal_entry_id",
                table: "other_income_entries",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_other_income_entries_outlet_id",
                table: "other_income_entries",
                column: "outlet_id");

            migrationBuilder.CreateIndex(
                name: "i_x_other_income_entries_payment_account_id",
                table: "other_income_entries",
                column: "payment_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_other_income_entries_business_id_income_date",
                table: "other_income_entries",
                columns: new[] { "business_id", "income_date" });

            migrationBuilder.CreateIndex(
                name: "IX_other_income_entries_business_id_outlet_id_income_date",
                table: "other_income_entries",
                columns: new[] { "business_id", "outlet_id", "income_date" });

            migrationBuilder.CreateIndex(
                name: "IX_other_income_entries_business_id_status",
                table: "other_income_entries",
                columns: new[] { "business_id", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_outlet_price_overrides_product_variant_id",
                table: "outlet_price_overrides",
                column: "product_variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_outlet_price_overrides_outlet_id_product_variant_id",
                table: "outlet_price_overrides",
                columns: new[] { "outlet_id", "product_variant_id" },
                unique: true,
                filter: "is_active = true");

            migrationBuilder.CreateIndex(
                name: "i_x_outlets_business_id",
                table: "outlets",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "IX_outlets_manager_id",
                table: "outlets",
                column: "manager_id");

            migrationBuilder.CreateIndex(
                name: "IX_pos_terminals_outlet_id_code",
                table: "pos_terminals",
                columns: new[] { "outlet_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pos_terminals_outlet_id_is_default",
                table: "pos_terminals",
                columns: new[] { "outlet_id", "is_default" });

            migrationBuilder.CreateIndex(
                name: "IX_price_rules_is_active_rule_type_target_id",
                table: "price_rules",
                columns: new[] { "is_active", "rule_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "IX_price_rules_valid_from_valid_to",
                table: "price_rules",
                columns: new[] { "valid_from", "valid_to" });

            migrationBuilder.CreateIndex(
                name: "IX_product_images_product_id_sort_order",
                table: "product_images",
                columns: new[] { "product_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ux_product_images_one_primary_per_product",
                table: "product_images",
                column: "product_id",
                unique: true,
                filter: "is_primary = true");

            migrationBuilder.CreateIndex(
                name: "i_x_product_variant_options_option_id",
                table: "product_variant_options",
                column: "option_id");

            migrationBuilder.CreateIndex(
                name: "i_x_product_variant_options_variant_id",
                table: "product_variant_options",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_product_variants_product_id",
                table: "product_variants",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_variants_barcode",
                table: "product_variants",
                column: "barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_variants_sku",
                table: "product_variants",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_product_variation_selected_options_option_id",
                table: "product_variation_selected_options",
                column: "option_id");

            migrationBuilder.CreateIndex(
                name: "i_x_product_variation_selected_options_variation_id",
                table: "product_variation_selected_options",
                column: "variation_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_variation_selected_options_product_id_variation_id_~",
                table: "product_variation_selected_options",
                columns: new[] { "product_id", "variation_id", "option_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_product_variations_product_id",
                table: "product_variations",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "i_x_product_variations_variation_id",
                table: "product_variations",
                column: "variation_id");

            migrationBuilder.CreateIndex(
                name: "i_x_products_category_id",
                table: "products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_barcode",
                table: "products",
                column: "barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_product_code",
                table: "products",
                column: "product_code",
                unique: true,
                filter: "product_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_purchase_order_items_variant_id",
                table: "purchase_order_items",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_order_items_po_id",
                table: "purchase_order_items",
                column: "po_id");

            migrationBuilder.CreateIndex(
                name: "i_x_purchase_orders_supplier_id",
                table: "purchase_orders",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_created_by",
                table: "purchase_orders",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_po_number",
                table: "purchase_orders",
                column: "po_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_warehouse_id_idempotency_key",
                table: "purchase_orders",
                columns: new[] { "warehouse_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_receipt_print_histories_printed_by_user_id",
                table: "receipt_print_histories",
                column: "printed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "i_x_receipt_print_histories_sale_id",
                table: "receipt_print_histories",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "i_x_receipt_print_histories_terminal_id",
                table: "receipt_print_histories",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_print_histories_printed_at",
                table: "receipt_print_histories",
                column: "printed_at");

            migrationBuilder.CreateIndex(
                name: "IX_roles_name",
                table: "roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_sale_items_sale_id",
                table: "sale_items",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sale_items_variant_id",
                table: "sale_items",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sale_payments_sale_id",
                table: "sale_payments",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "IX_sale_payments_payment_account_id",
                table: "sale_payments",
                column: "payment_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sale_voids_sale_id",
                table: "sale_voids",
                column: "sale_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_sale_voids_voided_by_user_id",
                table: "sale_voids",
                column: "voided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_sale_voids_voided_at",
                table: "sale_voids",
                column: "voided_at");

            migrationBuilder.CreateIndex(
                name: "i_x_sales_cash_session_id",
                table: "sales",
                column: "cash_session_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sales_cashier_id",
                table: "sales",
                column: "cashier_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sales_customer_id",
                table: "sales",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sales_outlet_id",
                table: "sales",
                column: "outlet_id");

            migrationBuilder.CreateIndex(
                name: "i_x_sales_terminal_id",
                table: "sales",
                column: "terminal_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_business_date",
                table: "sales",
                column: "business_date");

            migrationBuilder.CreateIndex(
                name: "IX_sales_outlet_id_idempotency_key",
                table: "sales",
                columns: new[] { "outlet_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_sale_date",
                table: "sales",
                column: "sale_date");

            migrationBuilder.CreateIndex(
                name: "i_x_settlement_batches_bank_account_id",
                table: "settlement_batches",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_settlement_batches_clearing_account_id",
                table: "settlement_batches",
                column: "clearing_account_id");

            migrationBuilder.CreateIndex(
                name: "i_x_settlement_batches_journal_entry_id",
                table: "settlement_batches",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_settlement_batches_reversal_journal_entry_id",
                table: "settlement_batches",
                column: "reversal_journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_settlement_batches_business_id_external_reference",
                table: "settlement_batches",
                columns: new[] { "business_id", "external_reference" },
                unique: true,
                filter: "external_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_settlement_batches_business_id_idempotency_key",
                table: "settlement_batches",
                columns: new[] { "business_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_stock_adjustment_lines_stock_adjustment_id",
                table: "stock_adjustment_lines",
                column: "stock_adjustment_id");

            migrationBuilder.CreateIndex(
                name: "i_x_stock_adjustment_lines_variant_id",
                table: "stock_adjustment_lines",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustments_adjusted_by",
                table: "stock_adjustments",
                column: "adjusted_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustments_adjustment_number",
                table: "stock_adjustments",
                column: "adjustment_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustments_approved_by",
                table: "stock_adjustments",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustments_cancelled_by",
                table: "stock_adjustments",
                column: "cancelled_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustments_rejected_by",
                table: "stock_adjustments",
                column: "rejected_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustments_source_stock_count_id",
                table: "stock_adjustments",
                column: "source_stock_count_id",
                unique: true,
                filter: "source_stock_count_id is not null");

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustments_status",
                table: "stock_adjustments",
                column: "status");

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

            migrationBuilder.CreateIndex(
                name: "IX_stock_ledgers_created_at",
                table: "stock_ledgers",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_stock_ledgers_created_by",
                table: "stock_ledgers",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_ledgers_reference_type_reference_id",
                table: "stock_ledgers",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_ledgers_variant_id_location_id_location_type_created_~",
                table: "stock_ledgers",
                columns: new[] { "variant_id", "location_id", "location_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_ledgers_variant_id_location_id_location_type_movement~",
                table: "stock_ledgers",
                columns: new[] { "variant_id", "location_id", "location_type", "movement_sequence" });

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

            migrationBuilder.CreateIndex(
                name: "i_x_stock_transfer_items_transfer_id",
                table: "stock_transfer_items",
                column: "transfer_id");

            migrationBuilder.CreateIndex(
                name: "i_x_stock_transfer_items_variant_id",
                table: "stock_transfer_items",
                column: "variant_id");

            migrationBuilder.CreateIndex(
                name: "i_x_stock_transfers_related_requisition_id",
                table: "stock_transfers",
                column: "related_requisition_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_approved_by",
                table: "stock_transfers",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_cancelled_by",
                table: "stock_transfers",
                column: "cancelled_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transfers_created_by",
                table: "stock_transfers",
                column: "created_by");

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
                name: "i_x_supplier_advance_utilizations_journal_entry_id",
                table: "supplier_advance_utilizations",
                column: "journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_advance_utilizations_supplier_advance_id",
                table: "supplier_advance_utilizations",
                column: "supplier_advance_id");

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_advance_utilizations_supplier_payable_document_id",
                table: "supplier_advance_utilizations",
                column: "supplier_payable_document_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_advance_utilizations_business_id_supplier_advance_~",
                table: "supplier_advance_utilizations",
                columns: new[] { "business_id", "supplier_advance_id", "supplier_payable_document_id", "journal_entry_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_advances_source_payment_id",
                table: "supplier_advances",
                column: "source_payment_id");

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_advances_supplier_id",
                table: "supplier_advances",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_payable_documents_source_journal_entry_id",
                table: "supplier_payable_documents",
                column: "source_journal_entry_id");

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_payable_documents_supplier_id",
                table: "supplier_payable_documents",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_payable_documents_warehouse_id",
                table: "supplier_payable_documents",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_payable_documents_business_id_source_journal_entry~",
                table: "supplier_payable_documents",
                columns: new[] { "business_id", "source_journal_entry_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supplier_payable_documents_business_id_supplier_id_due_date",
                table: "supplier_payable_documents",
                columns: new[] { "business_id", "supplier_id", "due_date" });

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_payment_allocations_supplier_payable_document_id",
                table: "supplier_payment_allocations",
                column: "supplier_payable_document_id");

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_payment_allocations_supplier_payment_id",
                table: "supplier_payment_allocations",
                column: "supplier_payment_id");

            migrationBuilder.CreateIndex(
                name: "i_x_supplier_payments_supplier_id",
                table: "supplier_payments",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_business_id_name",
                table: "suppliers",
                columns: new[] { "business_id", "name" },
                unique: true,
                filter: "business_id is not null");

            migrationBuilder.CreateIndex(
                name: "i_x_transactions_account_id",
                table: "transactions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_invitations_token_hash",
                table: "user_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_invitations_user_id_purpose_consumed_at",
                table: "user_invitations",
                columns: new[] { "user_id", "purpose", "consumed_at" });

            migrationBuilder.CreateIndex(
                name: "i_x_user_outlet_assignments_business_id",
                table: "user_outlet_assignments",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "i_x_user_outlet_assignments_outlet_id",
                table: "user_outlet_assignments",
                column: "outlet_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_outlet_assignments_user_id",
                table: "user_outlet_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_outlet_assignments_user_id_outlet_id",
                table: "user_outlet_assignments",
                columns: new[] { "user_id", "outlet_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_refresh_tokens_token_hash",
                table: "user_refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_refresh_tokens_user_id_revoked_at_expires_at",
                table: "user_refresh_tokens",
                columns: new[] { "user_id", "revoked_at", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "i_x_user_warehouse_assignments_business_id",
                table: "user_warehouse_assignments",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "i_x_user_warehouse_assignments_warehouse_id",
                table: "user_warehouse_assignments",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_warehouse_assignments_user_id",
                table: "user_warehouse_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_warehouse_assignments_user_id_warehouse_id",
                table: "user_warehouse_assignments",
                columns: new[] { "user_id", "warehouse_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_users_business_id",
                table: "users",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "i_x_users_role_id",
                table: "users",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_outlet_id",
                table: "users",
                column: "outlet_id");

            migrationBuilder.CreateIndex(
                name: "i_x_variation_options_variation_id",
                table: "variation_options",
                column: "variation_id");

            migrationBuilder.CreateIndex(
                name: "i_x_warehouses_business_id",
                table: "warehouses",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "i_x_warehouses_manager_id",
                table: "warehouses",
                column: "manager_id");

            migrationBuilder.AddForeignKey(
                name: "f_k_accounting_financial_year_close_runs__journal_entries_reversa~",
                table: "accounting_financial_year_close_runs",
                column: "reversal_journal_entry_id",
                principalTable: "journal_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_accounting_financial_year_close_runs__journal_entries_year_en~",
                table: "accounting_financial_year_close_runs",
                column: "year_end_journal_entry_id",
                principalTable: "journal_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_accounting_outlet_configurations__outlets_outlet_id",
                table: "accounting_outlet_configurations",
                column: "outlet_id",
                principalTable: "outlets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_audit_logs__users_user_id",
                table: "audit_logs",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_bank_accounts__outlets_outlet_id",
                table: "bank_accounts",
                column: "outlet_id",
                principalTable: "outlets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_bank_reconciliation_match_lines__journal_entry_lines_journal_e~",
                table: "bank_reconciliation_match_lines",
                column: "journal_entry_line_id",
                principalTable: "journal_entry_lines",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_bank_statement_adjustments__journal_entries_journal_entry_id",
                table: "bank_statement_adjustments",
                column: "journal_entry_id",
                principalTable: "journal_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_bank_statement_adjustments__journal_entries_reversal_journal_~",
                table: "bank_statement_adjustments",
                column: "reversal_journal_entry_id",
                principalTable: "journal_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_barcode_print_histories__outlets_outlet_id",
                table: "barcode_print_histories",
                column: "outlet_id",
                principalTable: "outlets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_barcode_print_histories__users_printed_by_user_id",
                table: "barcode_print_histories",
                column: "printed_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_bills__purchase_orders_purchase_order_id",
                table: "bills",
                column: "po_id",
                principalTable: "purchase_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_cash_sessions_users_closed_by_user_id",
                table: "cash_sessions",
                column: "closed_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_cash_sessions_users_opened_by_user_id",
                table: "cash_sessions",
                column: "opened_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_cash_sessions__outlets_outlet_id",
                table: "cash_sessions",
                column: "outlet_id",
                principalTable: "outlets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_cash_sessions__pos_terminals_terminal_id",
                table: "cash_sessions",
                column: "terminal_id",
                principalTable: "pos_terminals",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_expenses__journal_entries_journal_entry_id",
                table: "expenses",
                column: "journal_entry_id",
                principalTable: "journal_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_expenses__outlets_outlet_id",
                table: "expenses",
                column: "outlet_id",
                principalTable: "outlets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_grn_items__purchase_order_items_purchase_order_item_id",
                table: "grn_items",
                column: "po_item_id",
                principalTable: "purchase_order_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_grn_items_grns_grn_id",
                table: "grn_items",
                column: "grn_id",
                principalTable: "grns",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "f_k_grns__purchase_orders_purchase_order_id",
                table: "grns",
                column: "po_id",
                principalTable: "purchase_orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_grns__users_creator_id",
                table: "grns",
                column: "created_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_held_sales__outlets_outlet_id",
                table: "held_sales",
                column: "outlet_id",
                principalTable: "outlets",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_held_sales__users_cashier_id",
                table: "held_sales",
                column: "cashier_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_journal_entries__outlets_outlet_id",
                table: "journal_entries",
                column: "outlet_id",
                principalTable: "outlets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_journal_entries__pos_terminals_terminal_id",
                table: "journal_entries",
                column: "terminal_id",
                principalTable: "pos_terminals",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_journal_entries__warehouses_warehouse_id",
                table: "journal_entries",
                column: "warehouse_id",
                principalTable: "warehouses",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_journal_entry_lines__outlets_outlet_id",
                table: "journal_entry_lines",
                column: "outlet_id",
                principalTable: "outlets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_journal_entry_lines__warehouses_warehouse_id",
                table: "journal_entry_lines",
                column: "warehouse_id",
                principalTable: "warehouses",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_other_income_entries__outlets_outlet_id",
                table: "other_income_entries",
                column: "outlet_id",
                principalTable: "outlets",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "f_k_outlet_price_overrides_outlets_outlet_id",
                table: "outlet_price_overrides",
                column: "outlet_id",
                principalTable: "outlets",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_outlets_users_manager_id",
                table: "outlets",
                column: "manager_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_outlets_businesses_business_id",
                table: "outlets");

            migrationBuilder.DropForeignKey(
                name: "f_k_users_businesses_business_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "FK_users_outlets_outlet_id",
                table: "users");

            migrationBuilder.DropTable(
                name: "accounting_close_exception_approvals");

            migrationBuilder.DropTable(
                name: "accounting_configurations");

            migrationBuilder.DropTable(
                name: "accounting_financial_year_close_runs");

            migrationBuilder.DropTable(
                name: "accounting_number_sequences");

            migrationBuilder.DropTable(
                name: "accounting_outlet_configurations");

            migrationBuilder.DropTable(
                name: "audit_event_field_changes");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "bank_reconciliation_match_lines");

            migrationBuilder.DropTable(
                name: "bank_statement_adjustments");

            migrationBuilder.DropTable(
                name: "bank_statement_exceptions");

            migrationBuilder.DropTable(
                name: "barcode_print_history_items");

            migrationBuilder.DropTable(
                name: "barcode_template_fields");

            migrationBuilder.DropTable(
                name: "bills");

            migrationBuilder.DropTable(
                name: "business_feature_settings");

            migrationBuilder.DropTable(
                name: "business_reminder_notifications");

            migrationBuilder.DropTable(
                name: "expenses");

            migrationBuilder.DropTable(
                name: "grn_items");

            migrationBuilder.DropTable(
                name: "held_sales");

            migrationBuilder.DropTable(
                name: "inventories");

            migrationBuilder.DropTable(
                name: "journal_entry_attachments");

            migrationBuilder.DropTable(
                name: "liquidity_transfers");

            migrationBuilder.DropTable(
                name: "other_income_entries");

            migrationBuilder.DropTable(
                name: "outlet_price_overrides");

            migrationBuilder.DropTable(
                name: "price_rules");

            migrationBuilder.DropTable(
                name: "product_images");

            migrationBuilder.DropTable(
                name: "product_variant_options");

            migrationBuilder.DropTable(
                name: "product_variation_selected_options");

            migrationBuilder.DropTable(
                name: "product_variations");

            migrationBuilder.DropTable(
                name: "receipt_print_histories");

            migrationBuilder.DropTable(
                name: "sale_items");

            migrationBuilder.DropTable(
                name: "sale_payments");

            migrationBuilder.DropTable(
                name: "sale_voids");

            migrationBuilder.DropTable(
                name: "settlement_batches");

            migrationBuilder.DropTable(
                name: "stock_adjustment_lines");

            migrationBuilder.DropTable(
                name: "stock_count_lines");

            migrationBuilder.DropTable(
                name: "stock_ledgers");

            migrationBuilder.DropTable(
                name: "stock_requisition_lines");

            migrationBuilder.DropTable(
                name: "stock_transfer_items");

            migrationBuilder.DropTable(
                name: "supplier_advance_utilizations");

            migrationBuilder.DropTable(
                name: "supplier_payment_allocations");

            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "user_invitations");

            migrationBuilder.DropTable(
                name: "user_outlet_assignments");

            migrationBuilder.DropTable(
                name: "user_refresh_tokens");

            migrationBuilder.DropTable(
                name: "user_warehouse_assignments");

            migrationBuilder.DropTable(
                name: "accounting_close_gate_snapshots");

            migrationBuilder.DropTable(
                name: "audit_event_entities");

            migrationBuilder.DropTable(
                name: "journal_entry_lines");

            migrationBuilder.DropTable(
                name: "bank_reconciliation_matches");

            migrationBuilder.DropTable(
                name: "bank_statement_lines");

            migrationBuilder.DropTable(
                name: "barcode_print_histories");

            migrationBuilder.DropTable(
                name: "expense_categories");

            migrationBuilder.DropTable(
                name: "purchase_order_items");

            migrationBuilder.DropTable(
                name: "grns");

            migrationBuilder.DropTable(
                name: "variation_options");

            migrationBuilder.DropTable(
                name: "sales");

            migrationBuilder.DropTable(
                name: "stock_adjustments");

            migrationBuilder.DropTable(
                name: "stock_counts");

            migrationBuilder.DropTable(
                name: "stock_transfers");

            migrationBuilder.DropTable(
                name: "supplier_advances");

            migrationBuilder.DropTable(
                name: "supplier_payable_documents");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropTable(
                name: "accounting_period_close_runs");

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "bank_reconciliation_sessions");

            migrationBuilder.DropTable(
                name: "bank_statement_imports");

            migrationBuilder.DropTable(
                name: "barcode_templates");

            migrationBuilder.DropTable(
                name: "product_variants");

            migrationBuilder.DropTable(
                name: "purchase_orders");

            migrationBuilder.DropTable(
                name: "variations");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "stock_requisitions");

            migrationBuilder.DropTable(
                name: "supplier_payments");

            migrationBuilder.DropTable(
                name: "journal_entries");

            migrationBuilder.DropTable(
                name: "bank_accounts");

            migrationBuilder.DropTable(
                name: "products");

            migrationBuilder.DropTable(
                name: "suppliers");

            migrationBuilder.DropTable(
                name: "warehouses");

            migrationBuilder.DropTable(
                name: "accounting_periods");

            migrationBuilder.DropTable(
                name: "cash_sessions");

            migrationBuilder.DropTable(
                name: "chart_accounts");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "financial_years");

            migrationBuilder.DropTable(
                name: "pos_terminals");

            migrationBuilder.DropTable(
                name: "businesses");

            migrationBuilder.DropTable(
                name: "outlets");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "roles");
        }
    }
}
