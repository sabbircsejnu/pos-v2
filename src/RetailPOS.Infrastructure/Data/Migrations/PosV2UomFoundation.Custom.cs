using Microsoft.EntityFrameworkCore.Migrations;

namespace RetailPOS.Infrastructure.Data.Migrations;

/// <summary>
/// Seeds the global units and adds checks the model cannot express. Quantity columns are unconstrained numeric so legacy
/// integers keep scale 0 and read back exactly as before; the checks reject a value finer than stored precision instead
/// of letting a fixed-scale column round it silently. No existing business row is rewritten by this SQL.
/// </summary>
internal static class PosV2UomFoundationSql
{
    private static readonly (string Table, string Column)[] QuantityColumns =
    [
        ("inventories", "quantity"),
        ("stock_ledgers", "qty_in"),
        ("stock_ledgers", "qty_out"),
        ("stock_ledgers", "balance_after"),
        ("purchase_order_items", "quantity"),
        ("grn_items", "received_qty"),
        ("sale_items", "quantity"),
        ("stock_transfer_items", "quantity"),
        ("stock_transfer_items", "transfer_quantity"),
        ("stock_transfer_items", "accepted_quantity"),
        ("stock_adjustment_lines", "quantity_change"),
    ];

    public static void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO uom_units (business_id, code, name, symbol, dimension, quantity_precision, is_whole_unit, is_active, created_at, updated_at)
            VALUES
                (NULL, 'pcs', 'Piece', 'pcs', 'count', 0, TRUE, TRUE, now(), now()),
                (NULL, 'pair', 'Pair', 'pair', 'count', 0, TRUE, TRUE, now(), now()),
                (NULL, 'ml', 'Millilitre', 'ml', 'volume', 2, FALSE, TRUE, now(), now()),
                (NULL, 'L', 'Litre', 'L', 'volume', 4, FALSE, TRUE, now(), now()),
                (NULL, 'g', 'Gram', 'g', 'mass', 2, FALSE, TRUE, now(), now()),
                (NULL, 'kg', 'Kilogram', 'kg', 'mass', 4, FALSE, TRUE, now(), now()),
                (NULL, 'cm', 'Centimetre', 'cm', 'length', 2, FALSE, TRUE, now(), now()),
                (NULL, 'm', 'Metre', 'm', 'length', 4, FALSE, TRUE, now(), now()),
                (NULL, 'jar', 'Jar', 'jar', 'packaging', 0, TRUE, TRUE, now(), now()),
                (NULL, 'bottle', 'Bottle', 'bottle', 'packaging', 0, TRUE, TRUE, now(), now()),
                (NULL, 'box', 'Box', 'box', 'packaging', 0, TRUE, TRUE, now(), now()),
                (NULL, 'roll', 'Roll', 'roll', 'packaging', 0, TRUE, TRUE, now(), now())
            ON CONFLICT DO NOTHING;
            """);

        migrationBuilder.Sql("""
            INSERT INTO uom_standard_conversions (from_unit_id, to_unit_id, numerator, denominator, is_active)
            SELECT from_unit.id, to_unit.id, seed.factor, 1, TRUE
            FROM (VALUES ('L', 'ml', 1000), ('kg', 'g', 1000), ('m', 'cm', 100)) AS seed(from_code, to_code, factor)
            JOIN uom_units from_unit ON from_unit.code = seed.from_code AND from_unit.business_id IS NULL
            JOIN uom_units to_unit ON to_unit.code = seed.to_code AND to_unit.business_id IS NULL
            ON CONFLICT DO NOTHING;
            """);

        foreach (var (table, column) in QuantityColumns)
            migrationBuilder.Sql($"ALTER TABLE {table} ADD CONSTRAINT ck_{table}_{column}_scale CHECK (scale({column}) <= 6);");
    }

    public static void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var (table, column) in QuantityColumns)
            migrationBuilder.Sql($"ALTER TABLE {table} DROP CONSTRAINT IF EXISTS ck_{table}_{column}_scale;");
    }
}
