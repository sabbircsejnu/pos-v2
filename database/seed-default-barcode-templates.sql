-- =============================================================
-- Seed default barcode templates for businesses that have none.
-- Safe to run multiple times.
-- =============================================================

WITH target_businesses AS (
    SELECT b.id
    FROM businesses b
    WHERE NOT EXISTS (
        SELECT 1
        FROM barcode_templates bt
        WHERE bt.business_id = b.id
    )
),
template_seed(name, template_type, paper_type, label_width_mm, label_height_mm, is_default) AS (
    VALUES
        ('Small Barcode Label', 'small', 'label', 40.00, 25.00, FALSE),
        ('Retail Price Label', 'retail', 'label', 60.00, 40.00, TRUE),
        ('Detailed Variant Label', 'detailed', 'label', 60.00, 40.00, FALSE),
        ('Warehouse Label', 'warehouse', 'label', 50.00, 25.00, FALSE)
)
INSERT INTO barcode_templates (
    business_id,
    name,
    template_type,
    paper_type,
    label_width_mm,
    label_height_mm,
    is_default,
    is_active,
    created_by,
    updated_by,
    created_at,
    updated_at
)
SELECT
    tb.id,
    ts.name,
    ts.template_type,
    ts.paper_type,
    ts.label_width_mm,
    ts.label_height_mm,
    ts.is_default,
    TRUE,
    NULL,
    NULL,
    NOW(),
    NOW()
FROM target_businesses tb
CROSS JOIN template_seed ts
WHERE NOT EXISTS (
    SELECT 1
    FROM barcode_templates bt
    WHERE bt.business_id = tb.id
      AND bt.name = ts.name
);

WITH field_seed(template_type, field_key, is_enabled, sort_order, align) AS (
    VALUES
        ('small', 'product_name', FALSE, 0, 'left'),
        ('small', 'variant_name', FALSE, 1, 'left'),
        ('small', 'variant_attributes', FALSE, 2, 'left'),
        ('small', 'variant_sku', TRUE, 3, 'left'),
        ('small', 'barcode', TRUE, 4, 'left'),
        ('small', 'selling_price', FALSE, 5, 'right'),
        ('small', 'company_name', FALSE, 6, 'left'),
        ('small', 'company_logo', FALSE, 7, 'left'),

        ('retail', 'product_name', TRUE, 0, 'left'),
        ('retail', 'variant_name', FALSE, 1, 'left'),
        ('retail', 'variant_attributes', FALSE, 2, 'left'),
        ('retail', 'variant_sku', TRUE, 3, 'left'),
        ('retail', 'barcode', TRUE, 4, 'left'),
        ('retail', 'selling_price', TRUE, 5, 'right'),
        ('retail', 'company_name', TRUE, 6, 'left'),
        ('retail', 'company_logo', FALSE, 7, 'left'),

        ('detailed', 'product_name', TRUE, 0, 'left'),
        ('detailed', 'variant_name', TRUE, 1, 'left'),
        ('detailed', 'variant_attributes', TRUE, 2, 'left'),
        ('detailed', 'variant_sku', TRUE, 3, 'left'),
        ('detailed', 'barcode', TRUE, 4, 'left'),
        ('detailed', 'selling_price', TRUE, 5, 'right'),
        ('detailed', 'company_name', FALSE, 6, 'left'),
        ('detailed', 'company_logo', FALSE, 7, 'left'),

        ('warehouse', 'product_name', TRUE, 0, 'left'),
        ('warehouse', 'variant_name', FALSE, 1, 'left'),
        ('warehouse', 'variant_attributes', TRUE, 2, 'left'),
        ('warehouse', 'variant_sku', TRUE, 3, 'left'),
        ('warehouse', 'barcode', TRUE, 4, 'left'),
        ('warehouse', 'selling_price', FALSE, 5, 'right'),
        ('warehouse', 'company_name', FALSE, 6, 'left'),
        ('warehouse', 'company_logo', FALSE, 7, 'left')
)
INSERT INTO barcode_template_fields (
    template_id,
    field_key,
    is_enabled,
    sort_order,
    x,
    y,
    width,
    height,
    font_size,
    font_weight,
    align
)
SELECT
    bt.id,
    fs.field_key,
    fs.is_enabled,
    fs.sort_order,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    NULL,
    fs.align
FROM barcode_templates bt
JOIN field_seed fs ON fs.template_type = bt.template_type
WHERE bt.name IN (
    'Small Barcode Label',
    'Retail Price Label',
    'Detailed Variant Label',
    'Warehouse Label'
)
AND NOT EXISTS (
    SELECT 1
    FROM barcode_template_fields btf
    WHERE btf.template_id = bt.id
      AND btf.field_key = fs.field_key
);

UPDATE barcode_template_fields btf
SET is_enabled = CASE btf.field_key
                                        WHEN 'company_name' THEN TRUE
                                        WHEN 'product_name' THEN TRUE
                                        WHEN 'variant_sku' THEN TRUE
                                        WHEN 'barcode' THEN TRUE
                                        WHEN 'selling_price' THEN TRUE
                                        ELSE FALSE
                                 END,
        sort_order = CASE btf.field_key
                                        WHEN 'product_name' THEN 0
                                        WHEN 'variant_name' THEN 1
                                        WHEN 'variant_attributes' THEN 2
                                        WHEN 'variant_sku' THEN 3
                                        WHEN 'barcode' THEN 4
                                        WHEN 'selling_price' THEN 5
                                        WHEN 'company_name' THEN 6
                                        WHEN 'company_logo' THEN 7
                                        ELSE btf.sort_order
                                 END
FROM barcode_templates bt
WHERE bt.id = btf.template_id
    AND bt.name = 'Retail Price Label';

SELECT
    bt.business_id,
    bt.name,
    bt.template_type,
    bt.label_width_mm,
    bt.label_height_mm,
    bt.is_default
FROM barcode_templates bt
WHERE bt.name IN (
    'Small Barcode Label',
    'Retail Price Label',
    'Detailed Variant Label',
    'Warehouse Label'
)
ORDER BY bt.business_id, bt.is_default DESC, bt.name;
