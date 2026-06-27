-- Backfill standard identifiers for existing product and variant records.
-- Safe defaults are applied only when values are missing/blank.

BEGIN;

-- 1) Ensure every variant has a parent main product code.
UPDATE products
SET product_code = UPPER('PRD' || LPAD(id::text, 6, '0') || 'P'),
    updated_at = NOW()
WHERE COALESCE(TRIM(product_code), '') = '';

-- 2) Backfill missing variant SKUs.
WITH sku_base AS (
    SELECT
        pv.id,
        UPPER(p.product_code || '-STD-' || LPAD(ROW_NUMBER() OVER (PARTITION BY p.id ORDER BY pv.id)::text, 3, '0')) AS sku_candidate
    FROM product_variants pv
    INNER JOIN products p ON p.id = pv.product_id
    WHERE COALESCE(TRIM(pv.sku), '') = ''
),
sku_resolved AS (
    SELECT
        sb.id,
        CASE
            WHEN EXISTS (
                SELECT 1
                FROM product_variants x
                WHERE UPPER(x.sku) = sb.sku_candidate
                  AND x.id <> sb.id
            )
            THEN LEFT(sb.sku_candidate, 46) || '-' || sb.id::text
            ELSE sb.sku_candidate
        END AS final_sku
    FROM sku_base sb
)
UPDATE product_variants pv
SET sku = sr.final_sku,
    updated_at = NOW()
FROM sku_resolved sr
WHERE pv.id = sr.id;

-- 3) EAN-13 helper: compute check digit from a 12-digit base.
CREATE OR REPLACE FUNCTION _tmp_ean13_check_digit(base12 text)
RETURNS text
LANGUAGE sql
IMMUTABLE
AS $$
    WITH digits AS (
        SELECT
            i,
            SUBSTRING(base12 FROM i FOR 1)::int AS d
        FROM generate_series(1, 12) AS s(i)
    ),
    total AS (
        SELECT SUM(CASE WHEN i % 2 = 0 THEN d * 3 ELSE d END) AS sum_val
        FROM digits
    )
    SELECT ((10 - (sum_val % 10)) % 10)::text
    FROM total;
$$;

-- 4) Backfill missing variant barcodes with EAN-13-compatible values.
WITH barcode_base AS (
    SELECT
        pv.id,
        '880' ||
        LPAD((COALESCE(p.category_id, 0) % 1000)::text, 3, '0') ||
        LPAD((pv.id % 1000000)::text, 6, '0') AS base12
    FROM product_variants pv
    INNER JOIN products p ON p.id = pv.product_id
    WHERE COALESCE(TRIM(pv.barcode), '') = ''
),
barcode_resolved AS (
    SELECT
        bb.id,
        bb.base12 || _tmp_ean13_check_digit(bb.base12) AS final_barcode
    FROM barcode_base bb
)
UPDATE product_variants pv
SET barcode = br.final_barcode,
    updated_at = NOW()
FROM barcode_resolved br
WHERE pv.id = br.id;

DROP FUNCTION IF EXISTS _tmp_ean13_check_digit(text);

COMMIT;
