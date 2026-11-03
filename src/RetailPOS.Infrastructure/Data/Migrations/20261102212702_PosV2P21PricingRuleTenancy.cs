using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PosV2P21PricingRuleTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_price_rules_is_active_rule_type_target_id",
                table: "price_rules");

            migrationBuilder.AddColumn<long>(
                name: "business_id",
                table: "price_rules",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "business_id",
                table: "outlet_price_overrides",
                type: "bigint",
                nullable: true);

            // Overrides inherit the Business of their outlet; a variant from another Business is ambiguous.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    UPDATE outlet_price_overrides o
    SET business_id = outlet.business_id
    FROM outlets outlet
    WHERE outlet.id = o.outlet_id;

    IF EXISTS (
        SELECT 1
        FROM outlet_price_overrides o
        JOIN product_variants v ON v.id = o.product_variant_id
        WHERE o.business_id IS NULL OR v.business_id <> o.business_id)
    THEN
        RAISE EXCEPTION 'outlet_price_overrides contains rows whose outlet and variant belong to different or unknown Businesses; resolve them before migrating.';
    END IF;

    -- Rules have no owner today: assign only when exactly one Business exists.
    IF EXISTS (SELECT 1 FROM price_rules WHERE business_id IS NULL) THEN
        IF (SELECT COUNT(*) FROM businesses) = 1 THEN
            UPDATE price_rules SET business_id = (SELECT id FROM businesses) WHERE business_id IS NULL;
        ELSE
            RAISE EXCEPTION 'price_rules contains rows and more than one Business exists; assign each rule to a Business before migrating.';
        END IF;
    END IF;
END $$;");

            migrationBuilder.AlterColumn<long>(
                name: "business_id",
                table: "price_rules",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "business_id",
                table: "outlet_price_overrides",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_price_rules_business_id_is_active_rule_type_target_id",
                table: "price_rules",
                columns: new[] { "business_id", "is_active", "rule_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "IX_outlet_price_overrides_business_id",
                table: "outlet_price_overrides",
                column: "business_id");

            migrationBuilder.AddForeignKey(
                name: "FK_outlet_price_overrides_businesses_business_id",
                table: "outlet_price_overrides",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_price_rules_businesses_business_id",
                table: "price_rules",
                column: "business_id",
                principalTable: "businesses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION fn_outlet_price_override_business_consistency() RETURNS trigger AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM outlets WHERE id = NEW.outlet_id AND business_id = NEW.business_id) THEN
        RAISE EXCEPTION 'Outlet % does not belong to Business %', NEW.outlet_id, NEW.business_id
            USING ERRCODE = '23514';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM product_variants WHERE id = NEW.product_variant_id AND business_id = NEW.business_id) THEN
        RAISE EXCEPTION 'Variant % does not belong to Business %', NEW.product_variant_id, NEW.business_id
            USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_outlet_price_override_business_consistency
BEFORE INSERT OR UPDATE OF business_id, outlet_id, product_variant_id ON outlet_price_overrides
FOR EACH ROW EXECUTE FUNCTION fn_outlet_price_override_business_consistency();

CREATE OR REPLACE FUNCTION fn_price_rule_business_consistency() RETURNS trigger AS $$
BEGIN
    IF NEW.target_id IS NULL THEN
        RETURN NEW;
    END IF;
    IF NEW.rule_type = 'variant' AND NOT EXISTS (
        SELECT 1 FROM product_variants WHERE id = NEW.target_id AND business_id = NEW.business_id) THEN
        RAISE EXCEPTION 'Variant % does not belong to Business %', NEW.target_id, NEW.business_id USING ERRCODE = '23514';
    ELSIF NEW.rule_type = 'product' AND NOT EXISTS (
        SELECT 1 FROM products WHERE id = NEW.target_id AND business_id = NEW.business_id) THEN
        RAISE EXCEPTION 'Product % does not belong to Business %', NEW.target_id, NEW.business_id USING ERRCODE = '23514';
    ELSIF NEW.rule_type = 'category' AND NOT EXISTS (
        SELECT 1 FROM categories WHERE id = NEW.target_id AND business_id = NEW.business_id) THEN
        RAISE EXCEPTION 'Category % does not belong to Business %', NEW.target_id, NEW.business_id USING ERRCODE = '23514';
    ELSIF NEW.rule_type = 'outlet' AND NOT EXISTS (
        SELECT 1 FROM outlets WHERE id = NEW.target_id AND business_id = NEW.business_id) THEN
        RAISE EXCEPTION 'Outlet % does not belong to Business %', NEW.target_id, NEW.business_id USING ERRCODE = '23514';
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_price_rule_business_consistency
BEFORE INSERT OR UPDATE OF business_id, rule_type, target_id ON price_rules
FOR EACH ROW EXECUTE FUNCTION fn_price_rule_business_consistency();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS trg_price_rule_business_consistency ON price_rules;
DROP FUNCTION IF EXISTS fn_price_rule_business_consistency();
DROP TRIGGER IF EXISTS trg_outlet_price_override_business_consistency ON outlet_price_overrides;
DROP FUNCTION IF EXISTS fn_outlet_price_override_business_consistency();");

            migrationBuilder.DropForeignKey(
                name: "FK_outlet_price_overrides_businesses_business_id",
                table: "outlet_price_overrides");

            migrationBuilder.DropForeignKey(
                name: "FK_price_rules_businesses_business_id",
                table: "price_rules");

            migrationBuilder.DropIndex(
                name: "IX_price_rules_business_id_is_active_rule_type_target_id",
                table: "price_rules");

            migrationBuilder.DropIndex(
                name: "IX_outlet_price_overrides_business_id",
                table: "outlet_price_overrides");

            migrationBuilder.DropColumn(
                name: "business_id",
                table: "price_rules");

            migrationBuilder.DropColumn(
                name: "business_id",
                table: "outlet_price_overrides");

            migrationBuilder.CreateIndex(
                name: "IX_price_rules_is_active_rule_type_target_id",
                table: "price_rules",
                columns: new[] { "is_active", "rule_type", "target_id" });
        }
    }
}
