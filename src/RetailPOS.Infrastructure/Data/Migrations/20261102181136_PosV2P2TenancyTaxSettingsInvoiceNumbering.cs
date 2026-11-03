using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosV2P2TenancyTaxSettingsInvoiceNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_categories_categories_parent_category_id",
                table: "categories");

            migrationBuilder.DropForeignKey(
                name: "f_k_product_variants_products_product_id",
                table: "product_variants");

            migrationBuilder.DropForeignKey(
                name: "f_k_products_categories_category_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "i_x_products_category_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_barcode",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_product_code",
                table: "products");

            migrationBuilder.DropIndex(
                name: "i_x_product_variants_product_id",
                table: "product_variants");

            migrationBuilder.DropIndex(
                name: "IX_product_variants_barcode",
                table: "product_variants");

            migrationBuilder.DropIndex(
                name: "IX_product_variants_sku",
                table: "product_variants");

            migrationBuilder.DropIndex(
                name: "i_x_outlets_business_id",
                table: "outlets");

            migrationBuilder.DropIndex(
                name: "IX_customers_customer_code",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "i_x_categories_parent_category_id",
                table: "categories");

            migrationBuilder.AddColumn<long>(
                name: "business_id",
                table: "sales",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "invoice_issued_at_utc",
                table: "sales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invoice_prefix",
                table: "sales",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "invoice_sequence",
                table: "sales",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_classification",
                table: "sale_items",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "tax_rate",
                table: "products",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)",
                oldPrecision: 5,
                oldScale: 2);

            // The tenant columns are added nullable, backfilled (Business-scoped data of a single-Business database is
            // unambiguous; anything else aborts the migration) and only then made NOT NULL.
            migrationBuilder.AddColumn<long>(
                name: "business_id",
                table: "products",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_classification",
                table: "products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "business_id",
                table: "product_variants",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invoice_prefix",
                table: "outlets",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_zone_id",
                table: "outlets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "business_id",
                table: "customers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone_normalized",
                table: "customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "business_id",
                table: "categories",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql(BackfillSql);

            foreach (var table in new[] { "products", "product_variants", "customers", "categories" })
            {
                migrationBuilder.AlterColumn<long>(
                    name: "business_id",
                    table: table,
                    type: "bigint",
                    nullable: false,
                    oldClrType: typeof(long),
                    oldType: "bigint",
                    oldNullable: true);
            }

            migrationBuilder.AddUniqueConstraint(
                name: "AK_products_id_business_id",
                table: "products",
                columns: new[] { "id", "business_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_customers_id_business_id",
                table: "customers",
                columns: new[] { "id", "business_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_categories_id_business_id",
                table: "categories",
                columns: new[] { "id", "business_id" });

            migrationBuilder.CreateTable(
                name: "business_pos_settings",
                columns: table => new
                {
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    max_cash_session_duration_hours = table.Column<int>(type: "integer", nullable: false),
                    default_time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    tax_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    default_tax_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    tax_name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    prices_include_tax = table.Column<bool>(type: "boolean", nullable: false),
                    max_manual_discount_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    updated_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_pos_settings", x => x.business_id);
                    table.CheckConstraint("ck_business_pos_settings_discount", "CAST(max_manual_discount_percent AS NUMERIC) BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_business_pos_settings_duration", "max_cash_session_duration_hours BETWEEN 1 AND 20");
                    table.CheckConstraint("ck_business_pos_settings_tax_rate", "CAST(default_tax_rate AS NUMERIC) BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "f_k_business_pos_settings_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sale_invoice_counters",
                columns: table => new
                {
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    outlet_id = table.Column<long>(type: "bigint", nullable: false),
                    business_date = table.Column<DateTime>(type: "date", nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_invoice_counters", x => new { x.business_id, x.outlet_id, x.business_date });
                    table.ForeignKey(
                        name: "FK_sale_invoice_counters_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_invoice_counters_outlets_outlet_id",
                        column: x => x.outlet_id,
                        principalTable: "outlets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sales_business_id_outlet_id_business_date_invoice_sequence",
                table: "sales",
                columns: new[] { "business_id", "outlet_id", "business_date", "invoice_sequence" },
                unique: true,
                filter: "invoice_sequence IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sales_business_id_sale_number",
                table: "sales",
                columns: new[] { "business_id", "sale_number" },
                unique: true,
                filter: "business_id IS NOT NULL AND sale_number <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_products_business_id",
                table: "products",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "IX_products_business_id_barcode",
                table: "products",
                columns: new[] { "business_id", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_products_business_id_product_code",
                table: "products",
                columns: new[] { "business_id", "product_code" },
                unique: true,
                filter: "product_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_products_category_id_business_id",
                table: "products",
                columns: new[] { "category_id", "business_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_tax_classification",
                table: "products",
                sql: "tax_classification IS NULL OR tax_classification IN ('standard','zero_rated','exempt')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_products_tax_rate_range",
                table: "products",
                sql: "tax_rate IS NULL OR (CAST(tax_rate AS NUMERIC) >= 0 AND CAST(tax_rate AS NUMERIC) <= 100)");

            migrationBuilder.CreateIndex(
                name: "IX_product_variants_business_id",
                table: "product_variants",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "IX_product_variants_business_id_barcode",
                table: "product_variants",
                columns: new[] { "business_id", "barcode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_variants_business_id_sku",
                table: "product_variants",
                columns: new[] { "business_id", "sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_variants_product_id_business_id",
                table: "product_variants",
                columns: new[] { "product_id", "business_id" });

            migrationBuilder.CreateIndex(
                name: "IX_outlets_business_id_invoice_prefix",
                table: "outlets",
                columns: new[] { "business_id", "invoice_prefix" },
                unique: true,
                filter: "invoice_prefix IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customers_business_id_customer_code",
                table: "customers",
                columns: new[] { "business_id", "customer_code" },
                unique: true,
                filter: "customer_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_customers_business_id_phone_normalized",
                table: "customers",
                columns: new[] { "business_id", "phone_normalized" },
                unique: true,
                filter: "phone_normalized IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_categories_business_id",
                table: "categories",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "IX_categories_parent_category_id_business_id",
                table: "categories",
                columns: new[] { "parent_category_id", "business_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sale_invoice_counters_outlet_id",
                table: "sale_invoice_counters",
                column: "outlet_id");

            migrationBuilder.AddForeignKey(
                name: "FK_categories_businesses_business_id",
                table: "categories",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_categories_categories_parent_category_id",
                table: "categories",
                columns: new[] { "parent_category_id", "business_id" },
                principalTable: "categories",
                principalColumns: new[] { "id", "business_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_customers_businesses_business_id",
                table: "customers",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_product_variants_businesses_business_id",
                table: "product_variants",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_product_variants_products_product_id",
                table: "product_variants",
                columns: new[] { "product_id", "business_id" },
                principalTable: "products",
                principalColumns: new[] { "id", "business_id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_products_businesses_business_id",
                table: "products",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_products_categories_category_id",
                table: "products",
                columns: new[] { "category_id", "business_id" },
                principalTable: "categories",
                principalColumns: new[] { "id", "business_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_businesses_business_id",
                table: "sales",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(TenantIntegrityTriggersSql);
        }

        private const string BackfillSql = """
            DO $$
            DECLARE
                business_count integer;
                only_business bigint;
            BEGIN
                SELECT count(*), min(id) INTO business_count, only_business FROM businesses;
                IF business_count <> 1 AND (
                    EXISTS (SELECT 1 FROM categories) OR EXISTS (SELECT 1 FROM products)
                    OR EXISTS (SELECT 1 FROM product_variants) OR EXISTS (SELECT 1 FROM customers)) THEN
                    RAISE EXCEPTION 'Phase 2 tenancy backfill needs exactly one Business to own the existing catalogue and customers (found %). Assign business_id manually, then re-run the migration.', business_count;
                END IF;

                UPDATE categories SET business_id = only_business WHERE business_id IS NULL;
                UPDATE products SET business_id = only_business WHERE business_id IS NULL;
                UPDATE product_variants v SET business_id = p.business_id
                    FROM products p WHERE p.id = v.product_id AND v.business_id IS NULL;
                UPDATE customers SET business_id = only_business WHERE business_id IS NULL;
            END $$;

            UPDATE sales s SET business_id = o.business_id
                FROM outlets o WHERE o.id = s.outlet_id AND s.business_id IS NULL;

            -- Same normalisation rules as CustomerPhoneNormalizer (Bangladesh mobile -> +8801XXXXXXXXX, otherwise E.164).
            WITH compact AS (
                SELECT id, regexp_replace(phone, '[^0-9+]', '', 'g') AS p
                FROM customers WHERE phone IS NOT NULL
            ), normalised AS (
                SELECT id, CASE
                    WHEN position('+' IN substr(p, 2)) > 0 THEN NULL
                    WHEN p ~ '^01[3-9][0-9]{8}$' THEN '+88' || p
                    WHEN p ~ '^8801[3-9][0-9]{8}$' THEN '+' || p
                    WHEN p ~ '^\+8801[3-9][0-9]{8}$' THEN p
                    WHEN p ~ '^\+[1-9][0-9]{7,14}$' THEN p
                    ELSE NULL END AS pn
                FROM compact
            )
            UPDATE customers c SET phone_normalized = n.pn FROM normalised n WHERE c.id = n.id;

            -- Duplicates inside one Business keep the lowest id; the raw phone of the others is left untouched.
            UPDATE customers c SET phone_normalized = NULL
            WHERE c.phone_normalized IS NOT NULL AND EXISTS (
                SELECT 1 FROM customers d
                WHERE d.business_id = c.business_id AND d.phone_normalized = c.phone_normalized AND d.id < c.id);

            -- A stored 0 was the old "no rate" default; it becomes "not configured" so the Business default applies.
            UPDATE products SET tax_rate = NULL WHERE tax_rate = 0;

            -- Provisional, editable-until-first-invoice invoice prefixes so existing outlets can keep selling.
            UPDATE outlets SET invoice_prefix = 'O' || lpad(id::text, 3, '0')
                WHERE invoice_prefix IS NULL AND business_id IS NOT NULL;
            """;

        private const string TenantIntegrityTriggersSql = """
            CREATE OR REPLACE FUNCTION customers_protect_walkin()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF OLD.customer_code = 'WALKIN' OR OLD.is_system THEN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'System customers cannot be deleted'
                            USING ERRCODE = 'P0001';
                    END IF;

                    IF OLD.customer_code IS DISTINCT FROM NEW.customer_code
                        OR OLD.name IS DISTINCT FROM NEW.name
                        OR OLD.phone IS DISTINCT FROM NEW.phone
                        OR OLD.email IS DISTINCT FROM NEW.email
                        OR OLD.is_system IS DISTINCT FROM NEW.is_system
                        OR OLD.is_active IS DISTINCT FROM NEW.is_active
                        OR OLD.business_id IS DISTINCT FROM NEW.business_id THEN
                        RAISE EXCEPTION 'System customer identity cannot be changed'
                            USING ERRCODE = 'P0001';
                    END IF;
                END IF;

                IF TG_OP = 'DELETE' THEN
                    RETURN OLD;
                END IF;
                RETURN NEW;
            END;
            $$;

            CREATE FUNCTION sales_enforce_tenant_consistency()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF NEW.business_id IS NOT NULL THEN
                    IF NEW.customer_id IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM customers c WHERE c.id = NEW.customer_id AND c.business_id = NEW.business_id) THEN
                        RAISE EXCEPTION 'A sale customer must belong to the sale business'
                            USING ERRCODE = '23514';
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM outlets o WHERE o.id = NEW.outlet_id AND o.business_id = NEW.business_id) THEN
                        RAISE EXCEPTION 'A sale outlet must belong to the sale business'
                            USING ERRCODE = '23514';
                    END IF;
                END IF;
                RETURN NEW;
            END;
            $$;

            CREATE TRIGGER trg_sales_tenant_consistency
                BEFORE INSERT OR UPDATE OF customer_id, outlet_id, business_id ON sales
                FOR EACH ROW EXECUTE FUNCTION sales_enforce_tenant_consistency();

            CREATE FUNCTION sale_items_enforce_tenant_consistency()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM sales s
                    JOIN product_variants v ON v.id = NEW.variant_id
                    WHERE s.id = NEW.sale_id AND s.business_id IS NOT NULL AND v.business_id <> s.business_id) THEN
                    RAISE EXCEPTION 'A sale line variant must belong to the sale business'
                        USING ERRCODE = '23514';
                END IF;
                RETURN NEW;
            END;
            $$;

            CREATE TRIGGER trg_sale_items_tenant_consistency
                BEFORE INSERT OR UPDATE OF variant_id, sale_id ON sale_items
                FOR EACH ROW EXECUTE FUNCTION sale_items_enforce_tenant_consistency();
            """;

        private const string DropTenantIntegrityTriggersSql = """
            DROP TRIGGER IF EXISTS trg_sale_items_tenant_consistency ON sale_items;
            DROP FUNCTION IF EXISTS sale_items_enforce_tenant_consistency();
            DROP TRIGGER IF EXISTS trg_sales_tenant_consistency ON sales;
            DROP FUNCTION IF EXISTS sales_enforce_tenant_consistency();

            CREATE OR REPLACE FUNCTION customers_protect_walkin()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                IF OLD.customer_code = 'WALKIN' OR OLD.is_system THEN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'System customers cannot be deleted'
                            USING ERRCODE = 'P0001';
                    END IF;

                    IF OLD.customer_code IS DISTINCT FROM NEW.customer_code
                        OR OLD.name IS DISTINCT FROM NEW.name
                        OR OLD.phone IS DISTINCT FROM NEW.phone
                        OR OLD.email IS DISTINCT FROM NEW.email
                        OR OLD.is_system IS DISTINCT FROM NEW.is_system
                        OR OLD.is_active IS DISTINCT FROM NEW.is_active THEN
                        RAISE EXCEPTION 'System customer identity cannot be changed'
                            USING ERRCODE = 'P0001';
                    END IF;
                END IF;

                IF TG_OP = 'DELETE' THEN
                    RETURN OLD;
                END IF;
                RETURN NEW;
            END;
            $$;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DropTenantIntegrityTriggersSql);

            migrationBuilder.DropForeignKey(
                name: "FK_categories_businesses_business_id",
                table: "categories");

            migrationBuilder.DropForeignKey(
                name: "f_k_categories_categories_parent_category_id",
                table: "categories");

            migrationBuilder.DropForeignKey(
                name: "FK_customers_businesses_business_id",
                table: "customers");

            migrationBuilder.DropForeignKey(
                name: "FK_product_variants_businesses_business_id",
                table: "product_variants");

            migrationBuilder.DropForeignKey(
                name: "f_k_product_variants_products_product_id",
                table: "product_variants");

            migrationBuilder.DropForeignKey(
                name: "FK_products_businesses_business_id",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "f_k_products_categories_category_id",
                table: "products");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_businesses_business_id",
                table: "sales");

            migrationBuilder.DropTable(
                name: "business_pos_settings");

            migrationBuilder.DropTable(
                name: "sale_invoice_counters");

            migrationBuilder.DropIndex(
                name: "IX_sales_business_id_outlet_id_business_date_invoice_sequence",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sales_business_id_sale_number",
                table: "sales");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_products_id_business_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_business_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_business_id_barcode",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_business_id_product_code",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_products_category_id_business_id",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_tax_classification",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "ck_products_tax_rate_range",
                table: "products");

            migrationBuilder.DropIndex(
                name: "IX_product_variants_business_id",
                table: "product_variants");

            migrationBuilder.DropIndex(
                name: "IX_product_variants_business_id_barcode",
                table: "product_variants");

            migrationBuilder.DropIndex(
                name: "IX_product_variants_business_id_sku",
                table: "product_variants");

            migrationBuilder.DropIndex(
                name: "IX_product_variants_product_id_business_id",
                table: "product_variants");

            migrationBuilder.DropIndex(
                name: "IX_outlets_business_id_invoice_prefix",
                table: "outlets");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_customers_id_business_id",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "IX_customers_business_id_customer_code",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "IX_customers_business_id_phone_normalized",
                table: "customers");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_categories_id_business_id",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "IX_categories_business_id",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "IX_categories_parent_category_id_business_id",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "business_id",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "invoice_issued_at_utc",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "invoice_prefix",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "invoice_sequence",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "tax_classification",
                table: "sale_items");

            migrationBuilder.DropColumn(
                name: "business_id",
                table: "products");

            migrationBuilder.DropColumn(
                name: "tax_classification",
                table: "products");

            migrationBuilder.DropColumn(
                name: "business_id",
                table: "product_variants");

            migrationBuilder.DropColumn(
                name: "invoice_prefix",
                table: "outlets");

            migrationBuilder.DropColumn(
                name: "time_zone_id",
                table: "outlets");

            migrationBuilder.DropColumn(
                name: "business_id",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "phone_normalized",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "business_id",
                table: "categories");

            migrationBuilder.AlterColumn<decimal>(
                name: "tax_rate",
                table: "products",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)",
                oldPrecision: 5,
                oldScale: 2,
                oldNullable: true);

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
                name: "i_x_outlets_business_id",
                table: "outlets",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "IX_customers_customer_code",
                table: "customers",
                column: "customer_code",
                unique: true,
                filter: "customer_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_categories_parent_category_id",
                table: "categories",
                column: "parent_category_id");

            migrationBuilder.AddForeignKey(
                name: "f_k_categories_categories_parent_category_id",
                table: "categories",
                column: "parent_category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_product_variants_products_product_id",
                table: "product_variants",
                column: "product_id",
                principalTable: "products",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "f_k_products_categories_category_id",
                table: "products",
                column: "category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
