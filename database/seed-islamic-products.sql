-- =====================================================
-- Islamic Fashion Dummy Product Seed
-- Reference: https://iraniborkabazar.com/
-- =====================================================
-- Products covered:
--   • 13 Borka  (IBB-BRK-001 – IBB-BRK-013)
--   • 10 Abaya  (IBB-ABY-001 – IBB-ABY-010)
--   • 10 Hijab  (IBB-HJB-001 – IBB-HJB-010)
--   • 10 Panjabi(IBB-PNJ-001 – IBB-PNJ-010)
--   Total: 43 parent products, 172 variants
--
-- Variation axes used (existing + new Fabric):
--   Color  (reuse existing; new options added)
--   Size   (reuse existing; numeric sizes added: 42–58)
--   Fabric (NEW variation)
--
-- Barcodes:
--   Products:  8903001000001–8903004000010
--   Variants:  no barcodes (distinguished by SKU)
--
-- All inserts are idempotent – safe to re-run.
-- Run AFTER the base seed (apply-seed.ps1) is applied.
-- =====================================================

-- =====================================================
-- A. New Root + Sub-Categories
-- =====================================================

INSERT INTO categories (name, description, parent_category_id, display_order, is_active, created_at, updated_at)
SELECT t.name, t.description, NULL, t.disp, true, NOW(), NOW()
FROM (VALUES
    ('Women''s Islamic Wear', 'Borka, Abaya, Hijab and Khimar collection', 6),
    ('Men''s Islamic Wear',   'Panjabi, Kurta and men''s ethnic wear',     7)
) AS t(name, description, disp)
WHERE NOT EXISTS (SELECT 1 FROM categories c WHERE c.name = t.name AND c.parent_category_id IS NULL);

-- Sub-categories under Women's Islamic Wear
INSERT INTO categories (name, description, parent_category_id, display_order, is_active, created_at, updated_at)
SELECT t.name, t.description,
       (SELECT id FROM categories WHERE name = 'Women''s Islamic Wear'), t.disp, true, NOW(), NOW()
FROM (VALUES
    ('Borka',              'All borka designs – embroidered, stoned, printed, gown, koti',         1),
    ('Abaya',              'All abaya styles – embroidered, stoned, party, koti, short',           2),
    ('Hijab & Khimar',     'Hijab and khimar in all fabrics and colors',                           3)
) AS t(name, description, disp)
WHERE NOT EXISTS (SELECT 1 FROM categories c WHERE c.name = t.name);

-- Borka sub-categories
INSERT INTO categories (name, description, parent_category_id, display_order, is_active, created_at, updated_at)
SELECT t.name, t.description,
       (SELECT id FROM categories WHERE name = 'Borka'), t.disp, true, NOW(), NOW()
FROM (VALUES
    ('Embroidered Borka',  'Borka with embroidery and stone work',  1),
    ('Stoned Borka',       'Borka with stone work',                 2),
    ('Gown Borka',         'Full-length gown style borka',          3),
    ('Koti Borka',         'Koti/jacket style borka',               4),
    ('Straight Cut Borka', 'Classic straight cut borka',            5),
    ('Printed Borka',      'Printed fabric borka',                  6)
) AS t(name, description, disp)
WHERE NOT EXISTS (SELECT 1 FROM categories c WHERE c.name = t.name);

-- Abaya sub-categories
INSERT INTO categories (name, description, parent_category_id, display_order, is_active, created_at, updated_at)
SELECT t.name, t.description,
       (SELECT id FROM categories WHERE name = 'Abaya'), t.disp, true, NOW(), NOW()
FROM (VALUES
    ('Embroidered Abaya', 'Abaya with embroidery and stone work',  1),
    ('Stoned Abaya',      'Abaya with stone work',                 2),
    ('Party Abaya',       'Designer party abaya collection',       3),
    ('Printed Abaya',     'Printed fabric abaya',                  4)
) AS t(name, description, disp)
WHERE NOT EXISTS (SELECT 1 FROM categories c WHERE c.name = t.name);

-- Hijab sub-categories
INSERT INTO categories (name, description, parent_category_id, display_order, is_active, created_at, updated_at)
SELECT t.name, t.description,
       (SELECT id FROM categories WHERE name = 'Hijab & Khimar'), t.disp, true, NOW(), NOW()
FROM (VALUES
    ('Premium Hijab', 'Premium hijab in georgette, chiffon, viscose', 1),
    ('Plain Hijab',   'Everyday plain hijab in nidha and cotton',     2),
    ('Khimar',        'Long covering khimar style headwear',          3)
) AS t(name, description, disp)
WHERE NOT EXISTS (SELECT 1 FROM categories c WHERE c.name = t.name);

-- Sub-categories under Men's Islamic Wear
INSERT INTO categories (name, description, parent_category_id, display_order, is_active, created_at, updated_at)
SELECT t.name, t.description,
       (SELECT id FROM categories WHERE name = 'Men''s Islamic Wear'), t.disp, true, NOW(), NOW()
FROM (VALUES
    ('Panjabi',            'All panjabi styles',                         1),
    ('Embroidered Panjabi','Panjabi with embroidery work',               2),
    ('Cotton Panjabi',     'Everyday cotton panjabi',                    3),
    ('Katan Panjabi',      'Premium katan silk panjabi',                 4)
) AS t(name, description, disp)
WHERE NOT EXISTS (SELECT 1 FROM categories c WHERE c.name = t.name);

-- =====================================================
-- B. Suppliers
-- =====================================================

INSERT INTO suppliers (name, contact, address, credit_limit, created_at, updated_at)
SELECT t.name, t.contact, t.address, t.climit::numeric, NOW(), NOW()
FROM (VALUES
    ('IBB Wholesale BD',    'wholesale@iraniborkabazar.com',  '558 Mominbag, Kamrangir Char, Dhaka-1211', 80000),
    ('Nidha Fabrics Ltd.',  'supply@nidhafabrics.com.bd',     '12 Fabric Lane, Islampur, Dhaka-1100',     40000),
    ('Panjabi Mart BD',     'orders@panjabimart.com.bd',      '45 Nawabpur Road, Old Dhaka, Dhaka-1000',  35000)
) AS t(name, contact, address, climit)
WHERE NOT EXISTS (SELECT 1 FROM suppliers s WHERE s.name = t.name);

-- =====================================================
-- C. Fabric Variation + Options (NEW)
-- =====================================================

INSERT INTO variations (name, display_order, is_active, created_at, updated_at)
SELECT 'Fabric', 4, true, NOW(), NOW()
WHERE NOT EXISTS (SELECT 1 FROM variations WHERE name = 'Fabric');

INSERT INTO variation_options (variation_id, name, price_adjustment, display_order, is_active, created_at, updated_at)
SELECT (SELECT id FROM variations WHERE name = 'Fabric'),
       t.opt_name, 0, t.disp, true, NOW(), NOW()
FROM (VALUES
    ('Korean Georgette', 1),
    ('Nidha',            2),
    ('Cotton',           3),
    ('Chiffon',          4),
    ('Crepe',            5),
    ('Katan Silk',       6),
    ('Linen',            7),
    ('Viscose',          8)
) AS t(opt_name, disp)
WHERE NOT EXISTS (
    SELECT 1 FROM variation_options vo
    WHERE vo.variation_id = (SELECT id FROM variations WHERE name = 'Fabric')
      AND vo.name = t.opt_name
);

-- =====================================================
-- D. New Color Options (added into existing Color variation)
-- =====================================================

INSERT INTO variation_options (variation_id, name, price_adjustment, display_order, is_active, created_at, updated_at)
SELECT (SELECT id FROM variations WHERE name = 'Color'),
       t.opt_name, 0, t.disp, true, NOW(), NOW()
FROM (VALUES
    ('Maroon',         5),
    ('Cream',          6),
    ('Navy',           7),
    ('Sea Green',      8),
    ('Magenta',        9),
    ('Teal Blue',     10),
    ('Rose Pink',     11),
    ('Dark Chocolate',12),
    ('Mocha Brown',   13),
    ('Sunset Nude',   14),
    ('Lemon Mint',    15),
    ('Olive Green',   16),
    ('Dark Blue',     17),
    ('Orange',        18),
    ('Mauve Pink',    19),
    ('Dark Jam',      20),
    ('Purple',        21),
    ('Burgundy',      22),
    ('Floral Print',  23)
) AS t(opt_name, disp)
WHERE NOT EXISTS (
    SELECT 1 FROM variation_options vo
    WHERE vo.variation_id = (SELECT id FROM variations WHERE name = 'Color')
      AND vo.name = t.opt_name
);

-- =====================================================
-- E. New Numeric Size Options (into existing Size variation)
-- =====================================================

INSERT INTO variation_options (variation_id, name, price_adjustment, display_order, is_active, created_at, updated_at)
SELECT (SELECT id FROM variations WHERE name = 'Size'),
       t.opt_name, 0, t.disp, true, NOW(), NOW()
FROM (VALUES
    ('42',  6),
    ('44',  7),
    ('46',  8),
    ('48',  9),
    ('50', 10),
    ('52', 11),
    ('54', 12),
    ('56', 13),
    ('58', 14)
) AS t(opt_name, disp)
WHERE NOT EXISTS (
    SELECT 1 FROM variation_options vo
    WHERE vo.variation_id = (SELECT id FROM variations WHERE name = 'Size')
      AND vo.name = t.opt_name
);

-- =====================================================
-- F. Products
-- All prices in BDT (Bangladeshi Taka).
-- base_price = sale price; cost_price ≈ 60% of base_price.
-- =====================================================

-- ── F-1: Borka Products (13) ─────────────────────────────────────────────

INSERT INTO products (name, description, sku, barcode, category_id, base_price, cost_price, tax_rate, has_variants, is_active, created_at, updated_at)
SELECT t.pname, t.pdesc, t.sku, t.barcode,
       (SELECT id FROM categories WHERE name = t.cat_name),
       t.base_price, t.cost_price, 0.0, true, true, NOW(), NOW()
FROM (VALUES
    ('Black Color Borka with Embroidery & Stone Work',
     'Elegant black straight-cut borka featuring intricate embroidery and hand-applied stone work on Korean Georgette fabric. A timeless choice for Eid, parties, and formal occasions. Sizes 52–56.',
     'IBB-BRK-001', '8903001000001', 'Embroidered Borka',  4667.00, 2800.00),

    ('Cream Color Borka with Lace & Stone Work',
     'Soft cream borka with delicate lace border and sparkling stone detailing on Korean Georgette. Light, breathable, and graceful for all occasions. Sizes 50–56.',
     'IBB-BRK-002', '8903001000002', 'Stoned Borka',        4242.00, 2545.00),

    ('Maroon Color Borka with Stone Work',
     'Rich maroon borka adorned with scattered stone work on premium Nidha fabric. Offers exceptional comfort with a regal look. Sizes 50–54.',
     'IBB-BRK-003', '8903001000003', 'Stoned Borka',        5687.00, 3412.00),

    ('Dark Chocolate Gown Borka with Stone Work',
     'Deep chocolate full-length gown borka with all-over stone work on Korean Georgette. A stunning choice for evening events. Sizes 52–56.',
     'IBB-BRK-004', '8903001000004', 'Gown Borka',          5687.00, 3412.00),

    ('Mocha Brown Koti Borka with Embroidery & Stone',
     'Sophisticated mocha-brown koti-style borka with layered embroidery and premium stone work. Korean Georgette fabric. Sizes 52–56.',
     'IBB-BRK-005', '8903001000005', 'Koti Borka',          8492.00, 5095.00),

    ('Sunset Nude Borka with Embroidery & Stone Work',
     'Warm sunset-nude borka with fine embroidery and stone detailing on soft Nidha fabric. Perfect for day events. Sizes 50–54.',
     'IBB-BRK-006', '8903001000006', 'Embroidered Borka',   5092.00, 3055.00),

    ('Dark Jam Color Borka with Stone Work',
     'Deep dark-jam (wine-purple) borka with all-over stone embellishment on Korean Georgette. Rich and luxurious. Sizes 52–56.',
     'IBB-BRK-007', '8903001000007', 'Stoned Borka',        5092.00, 3055.00),

    ('Mauve Pink Borka with Stone Work',
     'Soft mauve-pink borka with scattered stone work on Nidha fabric. Feminine and elegant for celebrations. Sizes 50–54.',
     'IBB-BRK-008', '8903001000008', 'Stoned Borka',        4242.00, 2545.00),

    ('Black Koti Borka with Embroidery & Stone Work',
     'Classic black koti borka with detailed embroidery and stone work on Nidha fabric. Versatile for daily and festive wear. Sizes 52–56.',
     'IBB-BRK-009', '8903001000009', 'Koti Borka',          3817.00, 2290.00),

    ('Black Gown Borka with Embroidery & Stone Work',
     'Floor-length black gown borka with rich embroidery and stone work on Korean Georgette. The perfect formal statement piece. Sizes 50–54.',
     'IBB-BRK-010', '8903001000010', 'Gown Borka',          5092.00, 3055.00),

    ('Black Koti Borka Premium with Heavy Embroidery',
     'Premium black koti borka with heavy thread embroidery and premium stone work on Korean Georgette. Sizes 52–58. A show-stopper.',
     'IBB-BRK-011', '8903001000011', 'Koti Borka',          6792.00, 4075.00),

    ('Light Mint Premium Borka Design',
     'Fresh light-mint straight-cut borka on soft Nidha fabric with subtle stone trim. Lightweight and comfortable for everyday wear. Sizes 50–54.',
     'IBB-BRK-012', '8903001000012', 'Straight Cut Borka',  2117.00, 1270.00),

    ('Purple Borka with Embroidery & Stone Work',
     'Vibrant purple borka with bold embroidery and stone work on Korean Georgette. Sizes 50–56. Ideal for festive occasions.',
     'IBB-BRK-013', '8903001000013', 'Embroidered Borka',   3817.00, 2290.00)
) AS t(pname, pdesc, sku, barcode, cat_name, base_price, cost_price)
ON CONFLICT (barcode) DO NOTHING;

-- ── F-2: Abaya Products (10) ─────────────────────────────────────────────

INSERT INTO products (name, description, sku, barcode, category_id, base_price, cost_price, tax_rate, has_variants, is_active, created_at, updated_at)
SELECT t.pname, t.pdesc, t.sku, t.barcode,
       (SELECT id FROM categories WHERE name = t.cat_name),
       t.base_price, t.cost_price, 0.0, true, true, NOW(), NOW()
FROM (VALUES
    ('Teal Blue Abaya with Embroidery & Stone Work',
     'Stunning teal-blue open abaya featuring all-over embroidery and sparkling stone work on premium Korean Georgette. Sizes 52–56. Perfect for parties and Eid.',
     'IBB-ABY-001', '8903002000001', 'Embroidered Abaya',  7387.00, 4432.00),

    ('Rose Pink Abaya with Embroidery & Stone Work',
     'Romantic rose-pink abaya with fine floral embroidery and stone work on Nidha fabric. Soft, elegant, and flattering. Sizes 50–54.',
     'IBB-ABY-002', '8903002000002', 'Embroidered Abaya',  4412.00, 2647.00),

    ('Lemon Mint Abaya with Embroidery & Stone Work',
     'Refreshing lemon-mint luxury abaya with intricate hand embroidery and premium stone work on Korean Georgette. Designer party piece. Sizes 52–58.',
     'IBB-ABY-003', '8903002000003', 'Party Abaya',       10192.00, 6115.00),

    ('Olive Green Abaya with Stone Work',
     'Natural olive-green abaya with tasteful stone detailing on Nidha fabric. Effortlessly chic for any occasion. Sizes 50–54.',
     'IBB-ABY-004', '8903002000004', 'Stoned Abaya',       5942.00, 3565.00),

    ('Dark Blue Abaya with Stone Work',
     'Deep dark-blue abaya featuring all-over stone work on Korean Georgette. A classic, sophisticated choice. Sizes 52–56.',
     'IBB-ABY-005', '8903002000005', 'Stoned Abaya',       5517.00, 3310.00),

    ('Orange Abaya with Lace & Stone Work',
     'Vibrant orange abaya with decorative lace border and stone embellishment on Nidha fabric. Bold and fashionable. Sizes 50–54.',
     'IBB-ABY-006', '8903002000006', 'Stoned Abaya',       4667.00, 2800.00),

    ('Black & White Abaya with Stone Work',
     'Two-tone black-and-white abaya with all-over stone work on Nidha fabric. A unique contrast design for special occasions. Sizes 50–56.',
     'IBB-ABY-007', '8903002000007', 'Stoned Abaya',       3987.00, 2392.00),

    ('Cream Abaya with Embroidery & Stone Work',
     'Graceful cream abaya with delicate floral embroidery and stone work on Korean Georgette. Pure elegance for Eid and formal events. Sizes 52–56.',
     'IBB-ABY-008', '8903002000008', 'Embroidered Abaya',  5687.00, 3412.00),

    ('Gorgeous Printed Abaya with Lace & Embroidery',
     'Eye-catching floral-printed open abaya with lace trim and embroidery work on Crepe fabric. Feminine and sophisticated. Sizes 50–54.',
     'IBB-ABY-009', '8903002000009', 'Printed Abaya',      4667.00, 2800.00),

    ('Floral Printed Borka with Stone Work',
     'Colourful floral-print borka with stone work on Nidha fabric. A fresh, modern take on modest fashion. Sizes 50–54.',
     'IBB-ABY-010', '8903002000010', 'Printed Borka',      3987.00, 2392.00)
) AS t(pname, pdesc, sku, barcode, cat_name, base_price, cost_price)
ON CONFLICT (barcode) DO NOTHING;

-- ── F-3: Hijab Products (10) ─────────────────────────────────────────────

INSERT INTO products (name, description, sku, barcode, category_id, base_price, cost_price, tax_rate, has_variants, is_active, created_at, updated_at)
SELECT t.pname, t.pdesc, t.sku, t.barcode,
       (SELECT id FROM categories WHERE name = t.cat_name),
       t.base_price, t.cost_price, 0.0, true, true, NOW(), NOW()
FROM (VALUES
    ('Premium Georgette Hijab',
     'Soft, lightweight Korean Georgette hijab with smooth finish. Drapes beautifully and stays in place. Available in Black, White, and Cream. 180 cm × 70 cm.',
     'IBB-HJB-001', '8903003000001', 'Premium Hijab',     750.00,  380.00),

    ('Plain Nidha Hijab',
     'Everyday soft Nidha hijab. Non-slip, breathable, and comfortable for all-day wear. Available in four versatile colours.',
     'IBB-HJB-002', '8903003000002', 'Plain Hijab',       450.00,  225.00),

    ('Chiffon Embroidered Hijab',
     'Delicate chiffon hijab with hand-embroidered border detailing. Elegant and party-ready. Available in Sea Green, Magenta, and Rose Pink.',
     'IBB-HJB-003', '8903003000003', 'Premium Hijab',     900.00,  450.00),

    ('Premium Crepe Hijab',
     'Smooth-finish crepe hijab with good drape and coverage. Wrinkle-resistant and easy to style. Available in Black, Cream, and Maroon.',
     'IBB-HJB-004', '8903003000004', 'Premium Hijab',     650.00,  325.00),

    ('Soft Cotton Hijab',
     'Breathable 100% cotton hijab – ideal for warm weather and everyday use. Pre-washed, no-bleed colours. Available in four colours.',
     'IBB-HJB-005', '8903003000005', 'Plain Hijab',       350.00,  175.00),

    ('Viscose Hijab with Lace Border',
     'Luxurious viscose hijab with intricate lace border. Silky, cool touch and excellent drape. Available in Black, Cream, and Teal Blue.',
     'IBB-HJB-006', '8903003000006', 'Premium Hijab',     850.00,  425.00),

    ('Printed Chiffon Hijab',
     'Light chiffon hijab with digital floral print. Adds colour and personality to any outfit. Available in Sea Green, Magenta, and Blue.',
     'IBB-HJB-007', '8903003000007', 'Plain Hijab',       550.00,  275.00),

    ('Plain Georgette Hijab',
     'Classic plain Korean Georgette hijab – versatile, durable, and easy to match. Available in Black, Navy, and Burgundy.',
     'IBB-HJB-008', '8903003000008', 'Plain Hijab',       600.00,  300.00),

    ('Party Georgette Hijab with Stone Work',
     'Premium Korean Georgette hijab with hand-applied stone work along the border. Glamorous and elegant. Available in Black, Rose Pink, and Teal Blue.',
     'IBB-HJB-009', '8903003000009', 'Premium Hijab',    1200.00,  600.00),

    ('Nidha Khimar Style Hijab',
     'Full-coverage khimar-style headscarf in premium Nidha fabric. Long, modest, and comfortable. Available in Black, Navy, and Olive Green.',
     'IBB-HJB-010', '8903003000010', 'Khimar',           1500.00,  750.00)
) AS t(pname, pdesc, sku, barcode, cat_name, base_price, cost_price)
ON CONFLICT (barcode) DO NOTHING;

-- ── F-4: Panjabi Products (10) ───────────────────────────────────────────

INSERT INTO products (name, description, sku, barcode, category_id, base_price, cost_price, tax_rate, has_variants, is_active, created_at, updated_at)
SELECT t.pname, t.pdesc, t.sku, t.barcode,
       (SELECT id FROM categories WHERE name = t.cat_name),
       t.base_price, t.cost_price, 0.0, true, true, NOW(), NOW()
FROM (VALUES
    ('Premium Embroidered Panjabi',
     'Exquisitely embroidered katan silk panjabi for Eid and formal occasions. Collar, cuffs, and chest panel with fine thread work. Available in White and Cream, sizes 42–46.',
     'IBB-PNJ-001', '8903004000001', 'Embroidered Panjabi', 2800.00, 1680.00),

    ('Plain Cotton Panjabi',
     'Comfortable everyday 100% combed cotton panjabi. Breathable, easy-wash, and available in three classic colours. Sizes 42–46.',
     'IBB-PNJ-002', '8903004000002', 'Cotton Panjabi',       800.00,  480.00),

    ('Block Print Cotton Panjabi',
     'Handcrafted block-print cotton panjabi with traditional motif. Unique, casual, and culturally rich. Available in White and Blue. Sizes 42–46.',
     'IBB-PNJ-003', '8903004000003', 'Cotton Panjabi',      1200.00,  720.00),

    ('Eid Special Katan Silk Panjabi',
     'Premium katan silk panjabi for Eid and special celebrations. Full chest embroidery with delicate border work. White and Maroon. Sizes 42–48.',
     'IBB-PNJ-004', '8903004000004', 'Katan Panjabi',       3500.00, 2100.00),

    ('Classic Linen Panjabi',
     'Cool, breathable linen panjabi for summer and everyday formal wear. Minimal embroidery on collar. Three colours, sizes 42–46.',
     'IBB-PNJ-005', '8903004000005', 'Cotton Panjabi',      1500.00,  900.00),

    ('Cotton Twill Panjabi',
     'Smooth cotton-twill panjabi with a subtle self-stripe texture. Elegant for office and casual occasions. White, Navy, Maroon. Sizes 44–46.',
     'IBB-PNJ-006', '8903004000006', 'Cotton Panjabi',      1100.00,  660.00),

    ('Formal Embroidered Cotton Panjabi',
     'Semi-formal cotton panjabi with tasteful embroidery on chest and collar. Clean fit. White and Cream. Sizes 44–46.',
     'IBB-PNJ-007', '8903004000007', 'Embroidered Panjabi', 2200.00, 1320.00),

    ('Simple Plain Cotton Panjabi',
     'Budget-friendly plain cotton panjabi for everyday use. Comfortable, durable, and easy to maintain. White and Cream. Sizes 42–46.',
     'IBB-PNJ-008', '8903004000008', 'Cotton Panjabi',       650.00,  390.00),

    ('Navy Dobby Cotton Panjabi',
     'Stylish dobby-weave cotton panjabi with self-textured pattern. Available in Navy, Maroon, and Blue. Sizes 44–46.',
     'IBB-PNJ-009', '8903004000009', 'Cotton Panjabi',      1800.00, 1080.00),

    ('Maroon Embroidered Katan Panjabi',
     'Rich maroon katan silk panjabi with gold-thread embroidery on chest and neck. Festive and luxurious. Maroon and Navy. Sizes 42–46.',
     'IBB-PNJ-010', '8903004000010', 'Katan Panjabi',       2500.00, 1500.00)
) AS t(pname, pdesc, sku, barcode, cat_name, base_price, cost_price)
ON CONFLICT (barcode) DO NOTHING;

-- =====================================================
-- G. Product Variations (axis links: which variation axes per product)
-- =====================================================

-- Borka + Abaya: Color × Size × Fabric
INSERT INTO product_variations (product_id, variation_id, is_required, created_at)
SELECT p.id, va.id, true, NOW()
FROM (VALUES
    ('IBB-BRK-001','Color'),('IBB-BRK-001','Size'),('IBB-BRK-001','Fabric'),
    ('IBB-BRK-002','Color'),('IBB-BRK-002','Size'),('IBB-BRK-002','Fabric'),
    ('IBB-BRK-003','Color'),('IBB-BRK-003','Size'),('IBB-BRK-003','Fabric'),
    ('IBB-BRK-004','Color'),('IBB-BRK-004','Size'),('IBB-BRK-004','Fabric'),
    ('IBB-BRK-005','Color'),('IBB-BRK-005','Size'),('IBB-BRK-005','Fabric'),
    ('IBB-BRK-006','Color'),('IBB-BRK-006','Size'),('IBB-BRK-006','Fabric'),
    ('IBB-BRK-007','Color'),('IBB-BRK-007','Size'),('IBB-BRK-007','Fabric'),
    ('IBB-BRK-008','Color'),('IBB-BRK-008','Size'),('IBB-BRK-008','Fabric'),
    ('IBB-BRK-009','Color'),('IBB-BRK-009','Size'),('IBB-BRK-009','Fabric'),
    ('IBB-BRK-010','Color'),('IBB-BRK-010','Size'),('IBB-BRK-010','Fabric'),
    ('IBB-BRK-011','Color'),('IBB-BRK-011','Size'),('IBB-BRK-011','Fabric'),
    ('IBB-BRK-012','Color'),('IBB-BRK-012','Size'),('IBB-BRK-012','Fabric'),
    ('IBB-BRK-013','Color'),('IBB-BRK-013','Size'),('IBB-BRK-013','Fabric'),
    ('IBB-ABY-001','Color'),('IBB-ABY-001','Size'),('IBB-ABY-001','Fabric'),
    ('IBB-ABY-002','Color'),('IBB-ABY-002','Size'),('IBB-ABY-002','Fabric'),
    ('IBB-ABY-003','Color'),('IBB-ABY-003','Size'),('IBB-ABY-003','Fabric'),
    ('IBB-ABY-004','Color'),('IBB-ABY-004','Size'),('IBB-ABY-004','Fabric'),
    ('IBB-ABY-005','Color'),('IBB-ABY-005','Size'),('IBB-ABY-005','Fabric'),
    ('IBB-ABY-006','Color'),('IBB-ABY-006','Size'),('IBB-ABY-006','Fabric'),
    ('IBB-ABY-007','Color'),('IBB-ABY-007','Size'),('IBB-ABY-007','Fabric'),
    ('IBB-ABY-008','Color'),('IBB-ABY-008','Size'),('IBB-ABY-008','Fabric'),
    ('IBB-ABY-009','Color'),('IBB-ABY-009','Size'),('IBB-ABY-009','Fabric'),
    ('IBB-ABY-010','Color'),('IBB-ABY-010','Size'),('IBB-ABY-010','Fabric')
) AS t(psku, vname)
JOIN products p ON p.sku = t.psku
JOIN variations va ON va.name = t.vname
WHERE NOT EXISTS (SELECT 1 FROM product_variations pv WHERE pv.product_id = p.id AND pv.variation_id = va.id);

-- Hijab: Color × Fabric (no Size)
INSERT INTO product_variations (product_id, variation_id, is_required, created_at)
SELECT p.id, va.id, true, NOW()
FROM (VALUES
    ('IBB-HJB-001','Color'),('IBB-HJB-001','Fabric'),
    ('IBB-HJB-002','Color'),('IBB-HJB-002','Fabric'),
    ('IBB-HJB-003','Color'),('IBB-HJB-003','Fabric'),
    ('IBB-HJB-004','Color'),('IBB-HJB-004','Fabric'),
    ('IBB-HJB-005','Color'),('IBB-HJB-005','Fabric'),
    ('IBB-HJB-006','Color'),('IBB-HJB-006','Fabric'),
    ('IBB-HJB-007','Color'),('IBB-HJB-007','Fabric'),
    ('IBB-HJB-008','Color'),('IBB-HJB-008','Fabric'),
    ('IBB-HJB-009','Color'),('IBB-HJB-009','Fabric'),
    ('IBB-HJB-010','Color'),('IBB-HJB-010','Fabric')
) AS t(psku, vname)
JOIN products p ON p.sku = t.psku
JOIN variations va ON va.name = t.vname
WHERE NOT EXISTS (SELECT 1 FROM product_variations pv WHERE pv.product_id = p.id AND pv.variation_id = va.id);

-- Panjabi: Color × Size × Fabric
INSERT INTO product_variations (product_id, variation_id, is_required, created_at)
SELECT p.id, va.id, true, NOW()
FROM (VALUES
    ('IBB-PNJ-001','Color'),('IBB-PNJ-001','Size'),('IBB-PNJ-001','Fabric'),
    ('IBB-PNJ-002','Color'),('IBB-PNJ-002','Size'),('IBB-PNJ-002','Fabric'),
    ('IBB-PNJ-003','Color'),('IBB-PNJ-003','Size'),('IBB-PNJ-003','Fabric'),
    ('IBB-PNJ-004','Color'),('IBB-PNJ-004','Size'),('IBB-PNJ-004','Fabric'),
    ('IBB-PNJ-005','Color'),('IBB-PNJ-005','Size'),('IBB-PNJ-005','Fabric'),
    ('IBB-PNJ-006','Color'),('IBB-PNJ-006','Size'),('IBB-PNJ-006','Fabric'),
    ('IBB-PNJ-007','Color'),('IBB-PNJ-007','Size'),('IBB-PNJ-007','Fabric'),
    ('IBB-PNJ-008','Color'),('IBB-PNJ-008','Size'),('IBB-PNJ-008','Fabric'),
    ('IBB-PNJ-009','Color'),('IBB-PNJ-009','Size'),('IBB-PNJ-009','Fabric'),
    ('IBB-PNJ-010','Color'),('IBB-PNJ-010','Size'),('IBB-PNJ-010','Fabric')
) AS t(psku, vname)
JOIN products p ON p.sku = t.psku
JOIN variations va ON va.name = t.vname
WHERE NOT EXISTS (SELECT 1 FROM product_variations pv WHERE pv.product_id = p.id AND pv.variation_id = va.id);

-- =====================================================
-- H. Product Variants
-- SKU format:
--   Borka/Abaya: IBB-{TYPE}-{NNN}-{SIZE}-{FABRIC_CODE}
--   Hijab:       IBB-HJB-{NNN}-{COLOR_CODE}-{FABRIC_CODE}
--   Panjabi:     IBB-PNJ-{NNN}-{COLOR_CODE}-{SIZE}
-- Fabric codes: KGG=Korean Georgette, NID=Nidha, CRP=Crepe, CTN=Cotton
--               KTN=Katan Silk, LNN=Linen, CHF=Chiffon, VIS=Viscose
-- =====================================================

-- ── H-1: Borka Variants ──────────────────────────────────────────────────

INSERT INTO product_variants (product_id, name, sku, barcode, attributes, price_adjustment, cost_adjustment, created_at, updated_at)
SELECT p.id, t.vname, t.sku, NULL, t.attrs::jsonb, 0, 0, NOW(), NOW()
FROM (VALUES
    -- IBB-BRK-001 Black, Korean Georgette, sizes 52/54/56
    ('IBB-BRK-001','Black Borka (Embroidery & Stone) - Size 52','IBB-BRK-001-52-KGG','{"Color":"Black","Size":"52","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-001','Black Borka (Embroidery & Stone) - Size 54','IBB-BRK-001-54-KGG','{"Color":"Black","Size":"54","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-001','Black Borka (Embroidery & Stone) - Size 56','IBB-BRK-001-56-KGG','{"Color":"Black","Size":"56","Fabric":"Korean Georgette"}'),
    -- IBB-BRK-002 Cream, Korean Georgette, sizes 50/52/54/56
    ('IBB-BRK-002','Cream Borka (Lace & Stone) - Size 50','IBB-BRK-002-50-KGG','{"Color":"Cream","Size":"50","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-002','Cream Borka (Lace & Stone) - Size 52','IBB-BRK-002-52-KGG','{"Color":"Cream","Size":"52","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-002','Cream Borka (Lace & Stone) - Size 54','IBB-BRK-002-54-KGG','{"Color":"Cream","Size":"54","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-002','Cream Borka (Lace & Stone) - Size 56','IBB-BRK-002-56-KGG','{"Color":"Cream","Size":"56","Fabric":"Korean Georgette"}'),
    -- IBB-BRK-003 Maroon, Nidha, sizes 50/52/54
    ('IBB-BRK-003','Maroon Borka (Stone Work) - Size 50','IBB-BRK-003-50-NID','{"Color":"Maroon","Size":"50","Fabric":"Nidha"}'),
    ('IBB-BRK-003','Maroon Borka (Stone Work) - Size 52','IBB-BRK-003-52-NID','{"Color":"Maroon","Size":"52","Fabric":"Nidha"}'),
    ('IBB-BRK-003','Maroon Borka (Stone Work) - Size 54','IBB-BRK-003-54-NID','{"Color":"Maroon","Size":"54","Fabric":"Nidha"}'),
    -- IBB-BRK-004 Dark Chocolate, Korean Georgette, sizes 52/54/56
    ('IBB-BRK-004','Dark Chocolate Gown Borka - Size 52','IBB-BRK-004-52-KGG','{"Color":"Dark Chocolate","Size":"52","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-004','Dark Chocolate Gown Borka - Size 54','IBB-BRK-004-54-KGG','{"Color":"Dark Chocolate","Size":"54","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-004','Dark Chocolate Gown Borka - Size 56','IBB-BRK-004-56-KGG','{"Color":"Dark Chocolate","Size":"56","Fabric":"Korean Georgette"}'),
    -- IBB-BRK-005 Mocha Brown, Korean Georgette, sizes 52/54/56
    ('IBB-BRK-005','Mocha Brown Koti Borka - Size 52','IBB-BRK-005-52-KGG','{"Color":"Mocha Brown","Size":"52","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-005','Mocha Brown Koti Borka - Size 54','IBB-BRK-005-54-KGG','{"Color":"Mocha Brown","Size":"54","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-005','Mocha Brown Koti Borka - Size 56','IBB-BRK-005-56-KGG','{"Color":"Mocha Brown","Size":"56","Fabric":"Korean Georgette"}'),
    -- IBB-BRK-006 Sunset Nude, Nidha, sizes 50/52/54
    ('IBB-BRK-006','Sunset Nude Borka (Embroidery) - Size 50','IBB-BRK-006-50-NID','{"Color":"Sunset Nude","Size":"50","Fabric":"Nidha"}'),
    ('IBB-BRK-006','Sunset Nude Borka (Embroidery) - Size 52','IBB-BRK-006-52-NID','{"Color":"Sunset Nude","Size":"52","Fabric":"Nidha"}'),
    ('IBB-BRK-006','Sunset Nude Borka (Embroidery) - Size 54','IBB-BRK-006-54-NID','{"Color":"Sunset Nude","Size":"54","Fabric":"Nidha"}'),
    -- IBB-BRK-007 Dark Jam, Korean Georgette, sizes 52/54/56
    ('IBB-BRK-007','Dark Jam Borka (Stone Work) - Size 52','IBB-BRK-007-52-KGG','{"Color":"Dark Jam","Size":"52","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-007','Dark Jam Borka (Stone Work) - Size 54','IBB-BRK-007-54-KGG','{"Color":"Dark Jam","Size":"54","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-007','Dark Jam Borka (Stone Work) - Size 56','IBB-BRK-007-56-KGG','{"Color":"Dark Jam","Size":"56","Fabric":"Korean Georgette"}'),
    -- IBB-BRK-008 Mauve Pink, Nidha, sizes 50/52/54
    ('IBB-BRK-008','Mauve Pink Borka (Stone Work) - Size 50','IBB-BRK-008-50-NID','{"Color":"Mauve Pink","Size":"50","Fabric":"Nidha"}'),
    ('IBB-BRK-008','Mauve Pink Borka (Stone Work) - Size 52','IBB-BRK-008-52-NID','{"Color":"Mauve Pink","Size":"52","Fabric":"Nidha"}'),
    ('IBB-BRK-008','Mauve Pink Borka (Stone Work) - Size 54','IBB-BRK-008-54-NID','{"Color":"Mauve Pink","Size":"54","Fabric":"Nidha"}'),
    -- IBB-BRK-009 Black, Nidha, sizes 52/54/56
    ('IBB-BRK-009','Black Koti Borka (Emb & Stone) - Size 52','IBB-BRK-009-52-NID','{"Color":"Black","Size":"52","Fabric":"Nidha"}'),
    ('IBB-BRK-009','Black Koti Borka (Emb & Stone) - Size 54','IBB-BRK-009-54-NID','{"Color":"Black","Size":"54","Fabric":"Nidha"}'),
    ('IBB-BRK-009','Black Koti Borka (Emb & Stone) - Size 56','IBB-BRK-009-56-NID','{"Color":"Black","Size":"56","Fabric":"Nidha"}'),
    -- IBB-BRK-010 Black, Korean Georgette, sizes 50/52/54
    ('IBB-BRK-010','Black Gown Borka (Emb & Stone) - Size 50','IBB-BRK-010-50-KGG','{"Color":"Black","Size":"50","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-010','Black Gown Borka (Emb & Stone) - Size 52','IBB-BRK-010-52-KGG','{"Color":"Black","Size":"52","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-010','Black Gown Borka (Emb & Stone) - Size 54','IBB-BRK-010-54-KGG','{"Color":"Black","Size":"54","Fabric":"Korean Georgette"}'),
    -- IBB-BRK-011 Black, Korean Georgette, sizes 52/54/56/58
    ('IBB-BRK-011','Black Premium Koti Borka - Size 52','IBB-BRK-011-52-KGG','{"Color":"Black","Size":"52","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-011','Black Premium Koti Borka - Size 54','IBB-BRK-011-54-KGG','{"Color":"Black","Size":"54","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-011','Black Premium Koti Borka - Size 56','IBB-BRK-011-56-KGG','{"Color":"Black","Size":"56","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-011','Black Premium Koti Borka - Size 58','IBB-BRK-011-58-KGG','{"Color":"Black","Size":"58","Fabric":"Korean Georgette"}'),
    -- IBB-BRK-012 Lemon Mint, Nidha, sizes 50/52/54
    ('IBB-BRK-012','Light Mint Premium Borka - Size 50','IBB-BRK-012-50-NID','{"Color":"Lemon Mint","Size":"50","Fabric":"Nidha"}'),
    ('IBB-BRK-012','Light Mint Premium Borka - Size 52','IBB-BRK-012-52-NID','{"Color":"Lemon Mint","Size":"52","Fabric":"Nidha"}'),
    ('IBB-BRK-012','Light Mint Premium Borka - Size 54','IBB-BRK-012-54-NID','{"Color":"Lemon Mint","Size":"54","Fabric":"Nidha"}'),
    -- IBB-BRK-013 Purple, Korean Georgette, sizes 50/52/54/56
    ('IBB-BRK-013','Purple Borka (Emb & Stone) - Size 50','IBB-BRK-013-50-KGG','{"Color":"Purple","Size":"50","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-013','Purple Borka (Emb & Stone) - Size 52','IBB-BRK-013-52-KGG','{"Color":"Purple","Size":"52","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-013','Purple Borka (Emb & Stone) - Size 54','IBB-BRK-013-54-KGG','{"Color":"Purple","Size":"54","Fabric":"Korean Georgette"}'),
    ('IBB-BRK-013','Purple Borka (Emb & Stone) - Size 56','IBB-BRK-013-56-KGG','{"Color":"Purple","Size":"56","Fabric":"Korean Georgette"}')
) AS t(psku, vname, sku, attrs)
JOIN products p ON p.sku = t.psku
ON CONFLICT (sku) DO NOTHING;

-- ── H-2: Abaya Variants ──────────────────────────────────────────────────

INSERT INTO product_variants (product_id, name, sku, barcode, attributes, price_adjustment, cost_adjustment, created_at, updated_at)
SELECT p.id, t.vname, t.sku, NULL, t.attrs::jsonb, 0, 0, NOW(), NOW()
FROM (VALUES
    -- IBB-ABY-001 Teal Blue, Korean Georgette, sizes 52/54/56
    ('IBB-ABY-001','Teal Blue Abaya (Emb & Stone) - Size 52','IBB-ABY-001-52-KGG','{"Color":"Teal Blue","Size":"52","Fabric":"Korean Georgette"}'),
    ('IBB-ABY-001','Teal Blue Abaya (Emb & Stone) - Size 54','IBB-ABY-001-54-KGG','{"Color":"Teal Blue","Size":"54","Fabric":"Korean Georgette"}'),
    ('IBB-ABY-001','Teal Blue Abaya (Emb & Stone) - Size 56','IBB-ABY-001-56-KGG','{"Color":"Teal Blue","Size":"56","Fabric":"Korean Georgette"}'),
    -- IBB-ABY-002 Rose Pink, Nidha, sizes 50/52/54
    ('IBB-ABY-002','Rose Pink Abaya (Emb & Stone) - Size 50','IBB-ABY-002-50-NID','{"Color":"Rose Pink","Size":"50","Fabric":"Nidha"}'),
    ('IBB-ABY-002','Rose Pink Abaya (Emb & Stone) - Size 52','IBB-ABY-002-52-NID','{"Color":"Rose Pink","Size":"52","Fabric":"Nidha"}'),
    ('IBB-ABY-002','Rose Pink Abaya (Emb & Stone) - Size 54','IBB-ABY-002-54-NID','{"Color":"Rose Pink","Size":"54","Fabric":"Nidha"}'),
    -- IBB-ABY-003 Lemon Mint, Korean Georgette, sizes 52/54/56/58
    ('IBB-ABY-003','Lemon Mint Abaya (Emb & Stone) - Size 52','IBB-ABY-003-52-KGG','{"Color":"Lemon Mint","Size":"52","Fabric":"Korean Georgette"}'),
    ('IBB-ABY-003','Lemon Mint Abaya (Emb & Stone) - Size 54','IBB-ABY-003-54-KGG','{"Color":"Lemon Mint","Size":"54","Fabric":"Korean Georgette"}'),
    ('IBB-ABY-003','Lemon Mint Abaya (Emb & Stone) - Size 56','IBB-ABY-003-56-KGG','{"Color":"Lemon Mint","Size":"56","Fabric":"Korean Georgette"}'),
    ('IBB-ABY-003','Lemon Mint Abaya (Emb & Stone) - Size 58','IBB-ABY-003-58-KGG','{"Color":"Lemon Mint","Size":"58","Fabric":"Korean Georgette"}'),
    -- IBB-ABY-004 Olive Green, Nidha, sizes 50/52/54
    ('IBB-ABY-004','Olive Green Abaya (Stone Work) - Size 50','IBB-ABY-004-50-NID','{"Color":"Olive Green","Size":"50","Fabric":"Nidha"}'),
    ('IBB-ABY-004','Olive Green Abaya (Stone Work) - Size 52','IBB-ABY-004-52-NID','{"Color":"Olive Green","Size":"52","Fabric":"Nidha"}'),
    ('IBB-ABY-004','Olive Green Abaya (Stone Work) - Size 54','IBB-ABY-004-54-NID','{"Color":"Olive Green","Size":"54","Fabric":"Nidha"}'),
    -- IBB-ABY-005 Dark Blue, Korean Georgette, sizes 52/54/56
    ('IBB-ABY-005','Dark Blue Abaya (Stone Work) - Size 52','IBB-ABY-005-52-KGG','{"Color":"Dark Blue","Size":"52","Fabric":"Korean Georgette"}'),
    ('IBB-ABY-005','Dark Blue Abaya (Stone Work) - Size 54','IBB-ABY-005-54-KGG','{"Color":"Dark Blue","Size":"54","Fabric":"Korean Georgette"}'),
    ('IBB-ABY-005','Dark Blue Abaya (Stone Work) - Size 56','IBB-ABY-005-56-KGG','{"Color":"Dark Blue","Size":"56","Fabric":"Korean Georgette"}'),
    -- IBB-ABY-006 Orange, Nidha, sizes 50/52/54
    ('IBB-ABY-006','Orange Abaya (Lace & Stone) - Size 50','IBB-ABY-006-50-NID','{"Color":"Orange","Size":"50","Fabric":"Nidha"}'),
    ('IBB-ABY-006','Orange Abaya (Lace & Stone) - Size 52','IBB-ABY-006-52-NID','{"Color":"Orange","Size":"52","Fabric":"Nidha"}'),
    ('IBB-ABY-006','Orange Abaya (Lace & Stone) - Size 54','IBB-ABY-006-54-NID','{"Color":"Orange","Size":"54","Fabric":"Nidha"}'),
    -- IBB-ABY-007 Black, Nidha, sizes 50/52/54/56
    ('IBB-ABY-007','Black & White Abaya (Stone Work) - Size 50','IBB-ABY-007-50-NID','{"Color":"Black","Size":"50","Fabric":"Nidha"}'),
    ('IBB-ABY-007','Black & White Abaya (Stone Work) - Size 52','IBB-ABY-007-52-NID','{"Color":"Black","Size":"52","Fabric":"Nidha"}'),
    ('IBB-ABY-007','Black & White Abaya (Stone Work) - Size 54','IBB-ABY-007-54-NID','{"Color":"Black","Size":"54","Fabric":"Nidha"}'),
    ('IBB-ABY-007','Black & White Abaya (Stone Work) - Size 56','IBB-ABY-007-56-NID','{"Color":"Black","Size":"56","Fabric":"Nidha"}'),
    -- IBB-ABY-008 Cream, Korean Georgette, sizes 52/54/56
    ('IBB-ABY-008','Cream Abaya (Emb & Stone) - Size 52','IBB-ABY-008-52-KGG','{"Color":"Cream","Size":"52","Fabric":"Korean Georgette"}'),
    ('IBB-ABY-008','Cream Abaya (Emb & Stone) - Size 54','IBB-ABY-008-54-KGG','{"Color":"Cream","Size":"54","Fabric":"Korean Georgette"}'),
    ('IBB-ABY-008','Cream Abaya (Emb & Stone) - Size 56','IBB-ABY-008-56-KGG','{"Color":"Cream","Size":"56","Fabric":"Korean Georgette"}'),
    -- IBB-ABY-009 Floral Print, Crepe, sizes 50/52/54
    ('IBB-ABY-009','Printed Abaya (Lace & Emb) - Size 50','IBB-ABY-009-50-CRP','{"Color":"Floral Print","Size":"50","Fabric":"Crepe"}'),
    ('IBB-ABY-009','Printed Abaya (Lace & Emb) - Size 52','IBB-ABY-009-52-CRP','{"Color":"Floral Print","Size":"52","Fabric":"Crepe"}'),
    ('IBB-ABY-009','Printed Abaya (Lace & Emb) - Size 54','IBB-ABY-009-54-CRP','{"Color":"Floral Print","Size":"54","Fabric":"Crepe"}'),
    -- IBB-ABY-010 Floral Print, Nidha, sizes 50/52/54
    ('IBB-ABY-010','Floral Printed Borka (Stone Work) - Size 50','IBB-ABY-010-50-NID','{"Color":"Floral Print","Size":"50","Fabric":"Nidha"}'),
    ('IBB-ABY-010','Floral Printed Borka (Stone Work) - Size 52','IBB-ABY-010-52-NID','{"Color":"Floral Print","Size":"52","Fabric":"Nidha"}'),
    ('IBB-ABY-010','Floral Printed Borka (Stone Work) - Size 54','IBB-ABY-010-54-NID','{"Color":"Floral Print","Size":"54","Fabric":"Nidha"}')
) AS t(psku, vname, sku, attrs)
JOIN products p ON p.sku = t.psku
ON CONFLICT (sku) DO NOTHING;

-- ── H-3: Hijab Variants ──────────────────────────────────────────────────

INSERT INTO product_variants (product_id, name, sku, barcode, attributes, price_adjustment, cost_adjustment, created_at, updated_at)
SELECT p.id, t.vname, t.sku, NULL, t.attrs::jsonb, 0, 0, NOW(), NOW()
FROM (VALUES
    -- HJB-001 Korean Georgette: Black, White, Cream
    ('IBB-HJB-001','Premium Georgette Hijab - Black', 'IBB-HJB-001-BLK-KGG','{"Color":"Black","Fabric":"Korean Georgette"}'),
    ('IBB-HJB-001','Premium Georgette Hijab - White', 'IBB-HJB-001-WHT-KGG','{"Color":"White","Fabric":"Korean Georgette"}'),
    ('IBB-HJB-001','Premium Georgette Hijab - Cream', 'IBB-HJB-001-CRM-KGG','{"Color":"Cream","Fabric":"Korean Georgette"}'),
    -- HJB-002 Nidha: Black, White, Sea Green, Navy
    ('IBB-HJB-002','Plain Nidha Hijab - Black',     'IBB-HJB-002-BLK-NID','{"Color":"Black","Fabric":"Nidha"}'),
    ('IBB-HJB-002','Plain Nidha Hijab - White',     'IBB-HJB-002-WHT-NID','{"Color":"White","Fabric":"Nidha"}'),
    ('IBB-HJB-002','Plain Nidha Hijab - Sea Green', 'IBB-HJB-002-SGN-NID','{"Color":"Sea Green","Fabric":"Nidha"}'),
    ('IBB-HJB-002','Plain Nidha Hijab - Navy',      'IBB-HJB-002-NAV-NID','{"Color":"Navy","Fabric":"Nidha"}'),
    -- HJB-003 Chiffon: Sea Green, Magenta, Rose Pink
    ('IBB-HJB-003','Chiffon Embroidered Hijab - Sea Green', 'IBB-HJB-003-SGN-CHF','{"Color":"Sea Green","Fabric":"Chiffon"}'),
    ('IBB-HJB-003','Chiffon Embroidered Hijab - Magenta',   'IBB-HJB-003-MAG-CHF','{"Color":"Magenta","Fabric":"Chiffon"}'),
    ('IBB-HJB-003','Chiffon Embroidered Hijab - Rose Pink', 'IBB-HJB-003-RPK-CHF','{"Color":"Rose Pink","Fabric":"Chiffon"}'),
    -- HJB-004 Crepe: Black, Cream, Maroon
    ('IBB-HJB-004','Premium Crepe Hijab - Black', 'IBB-HJB-004-BLK-CRP','{"Color":"Black","Fabric":"Crepe"}'),
    ('IBB-HJB-004','Premium Crepe Hijab - Cream', 'IBB-HJB-004-CRM-CRP','{"Color":"Cream","Fabric":"Crepe"}'),
    ('IBB-HJB-004','Premium Crepe Hijab - Maroon','IBB-HJB-004-MAR-CRP','{"Color":"Maroon","Fabric":"Crepe"}'),
    -- HJB-005 Cotton: Black, White, Navy, Blue
    ('IBB-HJB-005','Soft Cotton Hijab - Black', 'IBB-HJB-005-BLK-CTN','{"Color":"Black","Fabric":"Cotton"}'),
    ('IBB-HJB-005','Soft Cotton Hijab - White', 'IBB-HJB-005-WHT-CTN','{"Color":"White","Fabric":"Cotton"}'),
    ('IBB-HJB-005','Soft Cotton Hijab - Navy',  'IBB-HJB-005-NAV-CTN','{"Color":"Navy","Fabric":"Cotton"}'),
    ('IBB-HJB-005','Soft Cotton Hijab - Blue',  'IBB-HJB-005-BLU-CTN','{"Color":"Blue","Fabric":"Cotton"}'),
    -- HJB-006 Viscose: Black, Cream, Teal Blue
    ('IBB-HJB-006','Viscose Hijab w/ Lace - Black',     'IBB-HJB-006-BLK-VIS','{"Color":"Black","Fabric":"Viscose"}'),
    ('IBB-HJB-006','Viscose Hijab w/ Lace - Cream',     'IBB-HJB-006-CRM-VIS','{"Color":"Cream","Fabric":"Viscose"}'),
    ('IBB-HJB-006','Viscose Hijab w/ Lace - Teal Blue', 'IBB-HJB-006-TBL-VIS','{"Color":"Teal Blue","Fabric":"Viscose"}'),
    -- HJB-007 Chiffon Print: Sea Green, Magenta, Blue
    ('IBB-HJB-007','Printed Chiffon Hijab - Sea Green', 'IBB-HJB-007-SGN-CHF','{"Color":"Sea Green","Fabric":"Chiffon"}'),
    ('IBB-HJB-007','Printed Chiffon Hijab - Magenta',   'IBB-HJB-007-MAG-CHF','{"Color":"Magenta","Fabric":"Chiffon"}'),
    ('IBB-HJB-007','Printed Chiffon Hijab - Blue',      'IBB-HJB-007-BLU-CHF','{"Color":"Blue","Fabric":"Chiffon"}'),
    -- HJB-008 Georgian: Black, Navy, Burgundy
    ('IBB-HJB-008','Plain Georgette Hijab - Black',    'IBB-HJB-008-BLK-KGG','{"Color":"Black","Fabric":"Korean Georgette"}'),
    ('IBB-HJB-008','Plain Georgette Hijab - Navy',     'IBB-HJB-008-NAV-KGG','{"Color":"Navy","Fabric":"Korean Georgette"}'),
    ('IBB-HJB-008','Plain Georgette Hijab - Burgundy', 'IBB-HJB-008-BRG-KGG','{"Color":"Burgundy","Fabric":"Korean Georgette"}'),
    -- HJB-009 Stone Georgette: Black, Rose Pink, Teal Blue
    ('IBB-HJB-009','Party Georgette Hijab - Black',     'IBB-HJB-009-BLK-KGG','{"Color":"Black","Fabric":"Korean Georgette"}'),
    ('IBB-HJB-009','Party Georgette Hijab - Rose Pink', 'IBB-HJB-009-RPK-KGG','{"Color":"Rose Pink","Fabric":"Korean Georgette"}'),
    ('IBB-HJB-009','Party Georgette Hijab - Teal Blue', 'IBB-HJB-009-TBL-KGG','{"Color":"Teal Blue","Fabric":"Korean Georgette"}'),
    -- HJB-010 Nidha Khimar: Black, Navy, Olive Green
    ('IBB-HJB-010','Nidha Khimar - Black',       'IBB-HJB-010-BLK-NID','{"Color":"Black","Fabric":"Nidha"}'),
    ('IBB-HJB-010','Nidha Khimar - Navy',        'IBB-HJB-010-NAV-NID','{"Color":"Navy","Fabric":"Nidha"}'),
    ('IBB-HJB-010','Nidha Khimar - Olive Green', 'IBB-HJB-010-OLV-NID','{"Color":"Olive Green","Fabric":"Nidha"}')
) AS t(psku, vname, sku, attrs)
JOIN products p ON p.sku = t.psku
ON CONFLICT (sku) DO NOTHING;

-- ── H-4: Panjabi Variants ────────────────────────────────────────────────

INSERT INTO product_variants (product_id, name, sku, barcode, attributes, price_adjustment, cost_adjustment, created_at, updated_at)
SELECT p.id, t.vname, t.sku, NULL, t.attrs::jsonb, 0, 0, NOW(), NOW()
FROM (VALUES
    -- PNJ-001 Katan Silk: White/Cream × 42/44/46
    ('IBB-PNJ-001','Premium Emb Panjabi - White 42','IBB-PNJ-001-WHT-42','{"Color":"White","Size":"42","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-001','Premium Emb Panjabi - White 44','IBB-PNJ-001-WHT-44','{"Color":"White","Size":"44","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-001','Premium Emb Panjabi - White 46','IBB-PNJ-001-WHT-46','{"Color":"White","Size":"46","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-001','Premium Emb Panjabi - Cream 42','IBB-PNJ-001-CRM-42','{"Color":"Cream","Size":"42","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-001','Premium Emb Panjabi - Cream 44','IBB-PNJ-001-CRM-44','{"Color":"Cream","Size":"44","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-001','Premium Emb Panjabi - Cream 46','IBB-PNJ-001-CRM-46','{"Color":"Cream","Size":"46","Fabric":"Katan Silk"}'),
    -- PNJ-002 Cotton: White/Navy/Maroon × 42/44/46
    ('IBB-PNJ-002','Plain Cotton Panjabi - White 42', 'IBB-PNJ-002-WHT-42','{"Color":"White","Size":"42","Fabric":"Cotton"}'),
    ('IBB-PNJ-002','Plain Cotton Panjabi - White 44', 'IBB-PNJ-002-WHT-44','{"Color":"White","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-002','Plain Cotton Panjabi - White 46', 'IBB-PNJ-002-WHT-46','{"Color":"White","Size":"46","Fabric":"Cotton"}'),
    ('IBB-PNJ-002','Plain Cotton Panjabi - Navy 42',  'IBB-PNJ-002-NAV-42','{"Color":"Navy","Size":"42","Fabric":"Cotton"}'),
    ('IBB-PNJ-002','Plain Cotton Panjabi - Navy 44',  'IBB-PNJ-002-NAV-44','{"Color":"Navy","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-002','Plain Cotton Panjabi - Navy 46',  'IBB-PNJ-002-NAV-46','{"Color":"Navy","Size":"46","Fabric":"Cotton"}'),
    ('IBB-PNJ-002','Plain Cotton Panjabi - Maroon 42','IBB-PNJ-002-MAR-42','{"Color":"Maroon","Size":"42","Fabric":"Cotton"}'),
    ('IBB-PNJ-002','Plain Cotton Panjabi - Maroon 44','IBB-PNJ-002-MAR-44','{"Color":"Maroon","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-002','Plain Cotton Panjabi - Maroon 46','IBB-PNJ-002-MAR-46','{"Color":"Maroon","Size":"46","Fabric":"Cotton"}'),
    -- PNJ-003 Cotton Print: White/Blue × 42/44/46
    ('IBB-PNJ-003','Block Print Panjabi - White 42','IBB-PNJ-003-WHT-42','{"Color":"White","Size":"42","Fabric":"Cotton"}'),
    ('IBB-PNJ-003','Block Print Panjabi - White 44','IBB-PNJ-003-WHT-44','{"Color":"White","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-003','Block Print Panjabi - White 46','IBB-PNJ-003-WHT-46','{"Color":"White","Size":"46","Fabric":"Cotton"}'),
    ('IBB-PNJ-003','Block Print Panjabi - Blue 42', 'IBB-PNJ-003-BLU-42','{"Color":"Blue","Size":"42","Fabric":"Cotton"}'),
    ('IBB-PNJ-003','Block Print Panjabi - Blue 44', 'IBB-PNJ-003-BLU-44','{"Color":"Blue","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-003','Block Print Panjabi - Blue 46', 'IBB-PNJ-003-BLU-46','{"Color":"Blue","Size":"46","Fabric":"Cotton"}'),
    -- PNJ-004 Katan Silk: White/Maroon × 42/44/46/48
    ('IBB-PNJ-004','Eid Special Katan Panjabi - White 42', 'IBB-PNJ-004-WHT-42','{"Color":"White","Size":"42","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-004','Eid Special Katan Panjabi - White 44', 'IBB-PNJ-004-WHT-44','{"Color":"White","Size":"44","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-004','Eid Special Katan Panjabi - White 46', 'IBB-PNJ-004-WHT-46','{"Color":"White","Size":"46","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-004','Eid Special Katan Panjabi - White 48', 'IBB-PNJ-004-WHT-48','{"Color":"White","Size":"48","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-004','Eid Special Katan Panjabi - Maroon 42','IBB-PNJ-004-MAR-42','{"Color":"Maroon","Size":"42","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-004','Eid Special Katan Panjabi - Maroon 44','IBB-PNJ-004-MAR-44','{"Color":"Maroon","Size":"44","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-004','Eid Special Katan Panjabi - Maroon 46','IBB-PNJ-004-MAR-46','{"Color":"Maroon","Size":"46","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-004','Eid Special Katan Panjabi - Maroon 48','IBB-PNJ-004-MAR-48','{"Color":"Maroon","Size":"48","Fabric":"Katan Silk"}'),
    -- PNJ-005 Linen: White/Cream/Navy × 42/44/46
    ('IBB-PNJ-005','Classic Linen Panjabi - White 42', 'IBB-PNJ-005-WHT-42','{"Color":"White","Size":"42","Fabric":"Linen"}'),
    ('IBB-PNJ-005','Classic Linen Panjabi - White 44', 'IBB-PNJ-005-WHT-44','{"Color":"White","Size":"44","Fabric":"Linen"}'),
    ('IBB-PNJ-005','Classic Linen Panjabi - White 46', 'IBB-PNJ-005-WHT-46','{"Color":"White","Size":"46","Fabric":"Linen"}'),
    ('IBB-PNJ-005','Classic Linen Panjabi - Cream 42', 'IBB-PNJ-005-CRM-42','{"Color":"Cream","Size":"42","Fabric":"Linen"}'),
    ('IBB-PNJ-005','Classic Linen Panjabi - Cream 44', 'IBB-PNJ-005-CRM-44','{"Color":"Cream","Size":"44","Fabric":"Linen"}'),
    ('IBB-PNJ-005','Classic Linen Panjabi - Cream 46', 'IBB-PNJ-005-CRM-46','{"Color":"Cream","Size":"46","Fabric":"Linen"}'),
    ('IBB-PNJ-005','Classic Linen Panjabi - Navy 42',  'IBB-PNJ-005-NAV-42','{"Color":"Navy","Size":"42","Fabric":"Linen"}'),
    ('IBB-PNJ-005','Classic Linen Panjabi - Navy 44',  'IBB-PNJ-005-NAV-44','{"Color":"Navy","Size":"44","Fabric":"Linen"}'),
    ('IBB-PNJ-005','Classic Linen Panjabi - Navy 46',  'IBB-PNJ-005-NAV-46','{"Color":"Navy","Size":"46","Fabric":"Linen"}'),
    -- PNJ-006 Cotton Twill: White/Navy/Maroon × 44/46
    ('IBB-PNJ-006','Cotton Twill Panjabi - White 44', 'IBB-PNJ-006-WHT-44','{"Color":"White","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-006','Cotton Twill Panjabi - White 46', 'IBB-PNJ-006-WHT-46','{"Color":"White","Size":"46","Fabric":"Cotton"}'),
    ('IBB-PNJ-006','Cotton Twill Panjabi - Navy 44',  'IBB-PNJ-006-NAV-44','{"Color":"Navy","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-006','Cotton Twill Panjabi - Navy 46',  'IBB-PNJ-006-NAV-46','{"Color":"Navy","Size":"46","Fabric":"Cotton"}'),
    ('IBB-PNJ-006','Cotton Twill Panjabi - Maroon 44','IBB-PNJ-006-MAR-44','{"Color":"Maroon","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-006','Cotton Twill Panjabi - Maroon 46','IBB-PNJ-006-MAR-46','{"Color":"Maroon","Size":"46","Fabric":"Cotton"}'),
    -- PNJ-007 Cotton: White/Cream × 44/46
    ('IBB-PNJ-007','Formal Emb Cotton Panjabi - White 44', 'IBB-PNJ-007-WHT-44','{"Color":"White","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-007','Formal Emb Cotton Panjabi - White 46', 'IBB-PNJ-007-WHT-46','{"Color":"White","Size":"46","Fabric":"Cotton"}'),
    ('IBB-PNJ-007','Formal Emb Cotton Panjabi - Cream 44', 'IBB-PNJ-007-CRM-44','{"Color":"Cream","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-007','Formal Emb Cotton Panjabi - Cream 46', 'IBB-PNJ-007-CRM-46','{"Color":"Cream","Size":"46","Fabric":"Cotton"}'),
    -- PNJ-008 Cotton: White/Cream × 42/44/46
    ('IBB-PNJ-008','Simple Cotton Panjabi - White 42','IBB-PNJ-008-WHT-42','{"Color":"White","Size":"42","Fabric":"Cotton"}'),
    ('IBB-PNJ-008','Simple Cotton Panjabi - White 44','IBB-PNJ-008-WHT-44','{"Color":"White","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-008','Simple Cotton Panjabi - White 46','IBB-PNJ-008-WHT-46','{"Color":"White","Size":"46","Fabric":"Cotton"}'),
    ('IBB-PNJ-008','Simple Cotton Panjabi - Cream 42','IBB-PNJ-008-CRM-42','{"Color":"Cream","Size":"42","Fabric":"Cotton"}'),
    ('IBB-PNJ-008','Simple Cotton Panjabi - Cream 44','IBB-PNJ-008-CRM-44','{"Color":"Cream","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-008','Simple Cotton Panjabi - Cream 46','IBB-PNJ-008-CRM-46','{"Color":"Cream","Size":"46","Fabric":"Cotton"}'),
    -- PNJ-009 Cotton Dobby: Navy/Maroon/Blue × 44/46
    ('IBB-PNJ-009','Navy Dobby Panjabi - Navy 44',  'IBB-PNJ-009-NAV-44','{"Color":"Navy","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-009','Navy Dobby Panjabi - Navy 46',  'IBB-PNJ-009-NAV-46','{"Color":"Navy","Size":"46","Fabric":"Cotton"}'),
    ('IBB-PNJ-009','Navy Dobby Panjabi - Maroon 44','IBB-PNJ-009-MAR-44','{"Color":"Maroon","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-009','Navy Dobby Panjabi - Maroon 46','IBB-PNJ-009-MAR-46','{"Color":"Maroon","Size":"46","Fabric":"Cotton"}'),
    ('IBB-PNJ-009','Navy Dobby Panjabi - Blue 44',  'IBB-PNJ-009-BLU-44','{"Color":"Blue","Size":"44","Fabric":"Cotton"}'),
    ('IBB-PNJ-009','Navy Dobby Panjabi - Blue 46',  'IBB-PNJ-009-BLU-46','{"Color":"Blue","Size":"46","Fabric":"Cotton"}'),
    -- PNJ-010 Katan Silk: Maroon/Navy × 42/44/46
    ('IBB-PNJ-010','Maroon Emb Katan Panjabi - Maroon 42','IBB-PNJ-010-MAR-42','{"Color":"Maroon","Size":"42","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-010','Maroon Emb Katan Panjabi - Maroon 44','IBB-PNJ-010-MAR-44','{"Color":"Maroon","Size":"44","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-010','Maroon Emb Katan Panjabi - Maroon 46','IBB-PNJ-010-MAR-46','{"Color":"Maroon","Size":"46","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-010','Maroon Emb Katan Panjabi - Navy 42',  'IBB-PNJ-010-NAV-42','{"Color":"Navy","Size":"42","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-010','Maroon Emb Katan Panjabi - Navy 44',  'IBB-PNJ-010-NAV-44','{"Color":"Navy","Size":"44","Fabric":"Katan Silk"}'),
    ('IBB-PNJ-010','Maroon Emb Katan Panjabi - Navy 46',  'IBB-PNJ-010-NAV-46','{"Color":"Navy","Size":"46","Fabric":"Katan Silk"}')
) AS t(psku, vname, sku, attrs)
JOIN products p ON p.sku = t.psku
ON CONFLICT (sku) DO NOTHING;

-- =====================================================
-- I. Product Variant Options
-- Links each variant to its Color, Size (where applicable), and Fabric options.
-- Uses LIKE patterns for efficiency where color/fabric are shared across a product.
-- =====================================================

-- ── I-1: Borka Variant Options ───────────────────────────────────────────

INSERT INTO product_variant_options (variant_id, option_id, created_at)
SELECT v.id, o.id, NOW()
FROM product_variants v
JOIN variation_options o ON true
WHERE
  -- IBB-BRK-001: Black + Korean Georgette (shared), per-size
  (v.sku LIKE 'IBB-BRK-001-%' AND o.name IN ('Black', 'Korean Georgette')) OR
  (v.sku = 'IBB-BRK-001-52-KGG' AND o.name = '52') OR
  (v.sku = 'IBB-BRK-001-54-KGG' AND o.name = '54') OR
  (v.sku = 'IBB-BRK-001-56-KGG' AND o.name = '56') OR
  -- IBB-BRK-002: Cream + Korean Georgette, per-size
  (v.sku LIKE 'IBB-BRK-002-%' AND o.name IN ('Cream', 'Korean Georgette')) OR
  (v.sku = 'IBB-BRK-002-50-KGG' AND o.name = '50') OR
  (v.sku = 'IBB-BRK-002-52-KGG' AND o.name = '52') OR
  (v.sku = 'IBB-BRK-002-54-KGG' AND o.name = '54') OR
  (v.sku = 'IBB-BRK-002-56-KGG' AND o.name = '56') OR
  -- IBB-BRK-003: Maroon + Nidha, per-size
  (v.sku LIKE 'IBB-BRK-003-%' AND o.name IN ('Maroon', 'Nidha')) OR
  (v.sku = 'IBB-BRK-003-50-NID' AND o.name = '50') OR
  (v.sku = 'IBB-BRK-003-52-NID' AND o.name = '52') OR
  (v.sku = 'IBB-BRK-003-54-NID' AND o.name = '54') OR
  -- IBB-BRK-004: Dark Chocolate + Korean Georgette
  (v.sku LIKE 'IBB-BRK-004-%' AND o.name IN ('Dark Chocolate', 'Korean Georgette')) OR
  (v.sku = 'IBB-BRK-004-52-KGG' AND o.name = '52') OR
  (v.sku = 'IBB-BRK-004-54-KGG' AND o.name = '54') OR
  (v.sku = 'IBB-BRK-004-56-KGG' AND o.name = '56') OR
  -- IBB-BRK-005: Mocha Brown + Korean Georgette
  (v.sku LIKE 'IBB-BRK-005-%' AND o.name IN ('Mocha Brown', 'Korean Georgette')) OR
  (v.sku = 'IBB-BRK-005-52-KGG' AND o.name = '52') OR
  (v.sku = 'IBB-BRK-005-54-KGG' AND o.name = '54') OR
  (v.sku = 'IBB-BRK-005-56-KGG' AND o.name = '56') OR
  -- IBB-BRK-006: Sunset Nude + Nidha
  (v.sku LIKE 'IBB-BRK-006-%' AND o.name IN ('Sunset Nude', 'Nidha')) OR
  (v.sku = 'IBB-BRK-006-50-NID' AND o.name = '50') OR
  (v.sku = 'IBB-BRK-006-52-NID' AND o.name = '52') OR
  (v.sku = 'IBB-BRK-006-54-NID' AND o.name = '54') OR
  -- IBB-BRK-007: Dark Jam + Korean Georgette
  (v.sku LIKE 'IBB-BRK-007-%' AND o.name IN ('Dark Jam', 'Korean Georgette')) OR
  (v.sku = 'IBB-BRK-007-52-KGG' AND o.name = '52') OR
  (v.sku = 'IBB-BRK-007-54-KGG' AND o.name = '54') OR
  (v.sku = 'IBB-BRK-007-56-KGG' AND o.name = '56') OR
  -- IBB-BRK-008: Mauve Pink + Nidha
  (v.sku LIKE 'IBB-BRK-008-%' AND o.name IN ('Mauve Pink', 'Nidha')) OR
  (v.sku = 'IBB-BRK-008-50-NID' AND o.name = '50') OR
  (v.sku = 'IBB-BRK-008-52-NID' AND o.name = '52') OR
  (v.sku = 'IBB-BRK-008-54-NID' AND o.name = '54') OR
  -- IBB-BRK-009: Black + Nidha (koti)
  (v.sku LIKE 'IBB-BRK-009-%' AND o.name IN ('Black', 'Nidha')) OR
  (v.sku = 'IBB-BRK-009-52-NID' AND o.name = '52') OR
  (v.sku = 'IBB-BRK-009-54-NID' AND o.name = '54') OR
  (v.sku = 'IBB-BRK-009-56-NID' AND o.name = '56') OR
  -- IBB-BRK-010: Black + Korean Georgette (gown)
  (v.sku LIKE 'IBB-BRK-010-%' AND o.name IN ('Black', 'Korean Georgette')) OR
  (v.sku = 'IBB-BRK-010-50-KGG' AND o.name = '50') OR
  (v.sku = 'IBB-BRK-010-52-KGG' AND o.name = '52') OR
  (v.sku = 'IBB-BRK-010-54-KGG' AND o.name = '54') OR
  -- IBB-BRK-011: Black + Korean Georgette (premium koti, 52-58)
  (v.sku LIKE 'IBB-BRK-011-%' AND o.name IN ('Black', 'Korean Georgette')) OR
  (v.sku = 'IBB-BRK-011-52-KGG' AND o.name = '52') OR
  (v.sku = 'IBB-BRK-011-54-KGG' AND o.name = '54') OR
  (v.sku = 'IBB-BRK-011-56-KGG' AND o.name = '56') OR
  (v.sku = 'IBB-BRK-011-58-KGG' AND o.name = '58') OR
  -- IBB-BRK-012: Lemon Mint + Nidha
  (v.sku LIKE 'IBB-BRK-012-%' AND o.name IN ('Lemon Mint', 'Nidha')) OR
  (v.sku = 'IBB-BRK-012-50-NID' AND o.name = '50') OR
  (v.sku = 'IBB-BRK-012-52-NID' AND o.name = '52') OR
  (v.sku = 'IBB-BRK-012-54-NID' AND o.name = '54') OR
  -- IBB-BRK-013: Purple + Korean Georgette
  (v.sku LIKE 'IBB-BRK-013-%' AND o.name IN ('Purple', 'Korean Georgette')) OR
  (v.sku = 'IBB-BRK-013-50-KGG' AND o.name = '50') OR
  (v.sku = 'IBB-BRK-013-52-KGG' AND o.name = '52') OR
  (v.sku = 'IBB-BRK-013-54-KGG' AND o.name = '54') OR
  (v.sku = 'IBB-BRK-013-56-KGG' AND o.name = '56')
AND NOT EXISTS (SELECT 1 FROM product_variant_options pvo WHERE pvo.variant_id = v.id AND pvo.option_id = o.id);

-- ── I-2: Abaya Variant Options ───────────────────────────────────────────

INSERT INTO product_variant_options (variant_id, option_id, created_at)
SELECT v.id, o.id, NOW()
FROM product_variants v
JOIN variation_options o ON true
WHERE
  -- IBB-ABY-001: Teal Blue + Korean Georgette
  (v.sku LIKE 'IBB-ABY-001-%' AND o.name IN ('Teal Blue', 'Korean Georgette')) OR
  (v.sku = 'IBB-ABY-001-52-KGG' AND o.name = '52') OR
  (v.sku = 'IBB-ABY-001-54-KGG' AND o.name = '54') OR
  (v.sku = 'IBB-ABY-001-56-KGG' AND o.name = '56') OR
  -- IBB-ABY-002: Rose Pink + Nidha
  (v.sku LIKE 'IBB-ABY-002-%' AND o.name IN ('Rose Pink', 'Nidha')) OR
  (v.sku = 'IBB-ABY-002-50-NID' AND o.name = '50') OR
  (v.sku = 'IBB-ABY-002-52-NID' AND o.name = '52') OR
  (v.sku = 'IBB-ABY-002-54-NID' AND o.name = '54') OR
  -- IBB-ABY-003: Lemon Mint + Korean Georgette, sizes 52-58
  (v.sku LIKE 'IBB-ABY-003-%' AND o.name IN ('Lemon Mint', 'Korean Georgette')) OR
  (v.sku = 'IBB-ABY-003-52-KGG' AND o.name = '52') OR
  (v.sku = 'IBB-ABY-003-54-KGG' AND o.name = '54') OR
  (v.sku = 'IBB-ABY-003-56-KGG' AND o.name = '56') OR
  (v.sku = 'IBB-ABY-003-58-KGG' AND o.name = '58') OR
  -- IBB-ABY-004: Olive Green + Nidha
  (v.sku LIKE 'IBB-ABY-004-%' AND o.name IN ('Olive Green', 'Nidha')) OR
  (v.sku = 'IBB-ABY-004-50-NID' AND o.name = '50') OR
  (v.sku = 'IBB-ABY-004-52-NID' AND o.name = '52') OR
  (v.sku = 'IBB-ABY-004-54-NID' AND o.name = '54') OR
  -- IBB-ABY-005: Dark Blue + Korean Georgette
  (v.sku LIKE 'IBB-ABY-005-%' AND o.name IN ('Dark Blue', 'Korean Georgette')) OR
  (v.sku = 'IBB-ABY-005-52-KGG' AND o.name = '52') OR
  (v.sku = 'IBB-ABY-005-54-KGG' AND o.name = '54') OR
  (v.sku = 'IBB-ABY-005-56-KGG' AND o.name = '56') OR
  -- IBB-ABY-006: Orange + Nidha
  (v.sku LIKE 'IBB-ABY-006-%' AND o.name IN ('Orange', 'Nidha')) OR
  (v.sku = 'IBB-ABY-006-50-NID' AND o.name = '50') OR
  (v.sku = 'IBB-ABY-006-52-NID' AND o.name = '52') OR
  (v.sku = 'IBB-ABY-006-54-NID' AND o.name = '54') OR
  -- IBB-ABY-007: Black + Nidha (sizes 50-56)
  (v.sku LIKE 'IBB-ABY-007-%' AND o.name IN ('Black', 'Nidha')) OR
  (v.sku = 'IBB-ABY-007-50-NID' AND o.name = '50') OR
  (v.sku = 'IBB-ABY-007-52-NID' AND o.name = '52') OR
  (v.sku = 'IBB-ABY-007-54-NID' AND o.name = '54') OR
  (v.sku = 'IBB-ABY-007-56-NID' AND o.name = '56') OR
  -- IBB-ABY-008: Cream + Korean Georgette
  (v.sku LIKE 'IBB-ABY-008-%' AND o.name IN ('Cream', 'Korean Georgette')) OR
  (v.sku = 'IBB-ABY-008-52-KGG' AND o.name = '52') OR
  (v.sku = 'IBB-ABY-008-54-KGG' AND o.name = '54') OR
  (v.sku = 'IBB-ABY-008-56-KGG' AND o.name = '56') OR
  -- IBB-ABY-009: Floral Print + Crepe
  (v.sku LIKE 'IBB-ABY-009-%' AND o.name IN ('Floral Print', 'Crepe')) OR
  (v.sku = 'IBB-ABY-009-50-CRP' AND o.name = '50') OR
  (v.sku = 'IBB-ABY-009-52-CRP' AND o.name = '52') OR
  (v.sku = 'IBB-ABY-009-54-CRP' AND o.name = '54') OR
  -- IBB-ABY-010: Floral Print + Nidha
  (v.sku LIKE 'IBB-ABY-010-%' AND o.name IN ('Floral Print', 'Nidha')) OR
  (v.sku = 'IBB-ABY-010-50-NID' AND o.name = '50') OR
  (v.sku = 'IBB-ABY-010-52-NID' AND o.name = '52') OR
  (v.sku = 'IBB-ABY-010-54-NID' AND o.name = '54')
AND NOT EXISTS (SELECT 1 FROM product_variant_options pvo WHERE pvo.variant_id = v.id AND pvo.option_id = o.id);

-- ── I-3: Hijab Variant Options ───────────────────────────────────────────

INSERT INTO product_variant_options (variant_id, option_id, created_at)
SELECT v.id, o.id, NOW()
FROM product_variants v
JOIN variation_options o ON true
WHERE
  -- HJB-001 Korean Georgette + colors
  (v.sku LIKE 'IBB-HJB-001-%' AND o.name = 'Korean Georgette') OR
  (v.sku = 'IBB-HJB-001-BLK-KGG' AND o.name = 'Black') OR
  (v.sku = 'IBB-HJB-001-WHT-KGG' AND o.name = 'White') OR
  (v.sku = 'IBB-HJB-001-CRM-KGG' AND o.name = 'Cream') OR
  -- HJB-002 Nidha + colors
  (v.sku LIKE 'IBB-HJB-002-%' AND o.name = 'Nidha') OR
  (v.sku = 'IBB-HJB-002-BLK-NID' AND o.name = 'Black') OR
  (v.sku = 'IBB-HJB-002-WHT-NID' AND o.name = 'White') OR
  (v.sku = 'IBB-HJB-002-SGN-NID' AND o.name = 'Sea Green') OR
  (v.sku = 'IBB-HJB-002-NAV-NID' AND o.name = 'Navy') OR
  -- HJB-003 Chiffon + colors
  (v.sku LIKE 'IBB-HJB-003-%' AND o.name = 'Chiffon') OR
  (v.sku = 'IBB-HJB-003-SGN-CHF' AND o.name = 'Sea Green') OR
  (v.sku = 'IBB-HJB-003-MAG-CHF' AND o.name = 'Magenta') OR
  (v.sku = 'IBB-HJB-003-RPK-CHF' AND o.name = 'Rose Pink') OR
  -- HJB-004 Crepe + colors
  (v.sku LIKE 'IBB-HJB-004-%' AND o.name = 'Crepe') OR
  (v.sku = 'IBB-HJB-004-BLK-CRP' AND o.name = 'Black') OR
  (v.sku = 'IBB-HJB-004-CRM-CRP' AND o.name = 'Cream') OR
  (v.sku = 'IBB-HJB-004-MAR-CRP' AND o.name = 'Maroon') OR
  -- HJB-005 Cotton + colors
  (v.sku LIKE 'IBB-HJB-005-%' AND o.name = 'Cotton') OR
  (v.sku = 'IBB-HJB-005-BLK-CTN' AND o.name = 'Black') OR
  (v.sku = 'IBB-HJB-005-WHT-CTN' AND o.name = 'White') OR
  (v.sku = 'IBB-HJB-005-NAV-CTN' AND o.name = 'Navy') OR
  (v.sku = 'IBB-HJB-005-BLU-CTN' AND o.name = 'Blue') OR
  -- HJB-006 Viscose + colors
  (v.sku LIKE 'IBB-HJB-006-%' AND o.name = 'Viscose') OR
  (v.sku = 'IBB-HJB-006-BLK-VIS' AND o.name = 'Black') OR
  (v.sku = 'IBB-HJB-006-CRM-VIS' AND o.name = 'Cream') OR
  (v.sku = 'IBB-HJB-006-TBL-VIS' AND o.name = 'Teal Blue') OR
  -- HJB-007 Chiffon + colors
  (v.sku LIKE 'IBB-HJB-007-%' AND o.name = 'Chiffon') OR
  (v.sku = 'IBB-HJB-007-SGN-CHF' AND o.name = 'Sea Green') OR
  (v.sku = 'IBB-HJB-007-MAG-CHF' AND o.name = 'Magenta') OR
  (v.sku = 'IBB-HJB-007-BLU-CHF' AND o.name = 'Blue') OR
  -- HJB-008 Korean Georgette + colors
  (v.sku LIKE 'IBB-HJB-008-%' AND o.name = 'Korean Georgette') OR
  (v.sku = 'IBB-HJB-008-BLK-KGG' AND o.name = 'Black') OR
  (v.sku = 'IBB-HJB-008-NAV-KGG' AND o.name = 'Navy') OR
  (v.sku = 'IBB-HJB-008-BRG-KGG' AND o.name = 'Burgundy') OR
  -- HJB-009 Korean Georgette + colors (party)
  (v.sku LIKE 'IBB-HJB-009-%' AND o.name = 'Korean Georgette') OR
  (v.sku = 'IBB-HJB-009-BLK-KGG' AND o.name = 'Black') OR
  (v.sku = 'IBB-HJB-009-RPK-KGG' AND o.name = 'Rose Pink') OR
  (v.sku = 'IBB-HJB-009-TBL-KGG' AND o.name = 'Teal Blue') OR
  -- HJB-010 Nidha Khimar + colors
  (v.sku LIKE 'IBB-HJB-010-%' AND o.name = 'Nidha') OR
  (v.sku = 'IBB-HJB-010-BLK-NID' AND o.name = 'Black') OR
  (v.sku = 'IBB-HJB-010-NAV-NID' AND o.name = 'Navy') OR
  (v.sku = 'IBB-HJB-010-OLV-NID' AND o.name = 'Olive Green')
AND NOT EXISTS (SELECT 1 FROM product_variant_options pvo WHERE pvo.variant_id = v.id AND pvo.option_id = o.id);

-- ── I-4: Panjabi Variant Options ─────────────────────────────────────────

INSERT INTO product_variant_options (variant_id, option_id, created_at)
SELECT v.id, o.id, NOW()
FROM product_variants v
JOIN variation_options o ON true
WHERE
  -- PNJ-001 Katan Silk, White/Cream × 42/44/46
  (v.sku LIKE 'IBB-PNJ-001-%' AND o.name = 'Katan Silk') OR
  (v.sku LIKE 'IBB-PNJ-001-WHT-%' AND o.name = 'White') OR
  (v.sku LIKE 'IBB-PNJ-001-CRM-%' AND o.name = 'Cream') OR
  (v.sku LIKE 'IBB-PNJ-001-%-42' AND o.name = '42') OR
  (v.sku LIKE 'IBB-PNJ-001-%-44' AND o.name = '44') OR
  (v.sku LIKE 'IBB-PNJ-001-%-46' AND o.name = '46') OR
  -- PNJ-002 Cotton, White/Navy/Maroon × 42/44/46
  (v.sku LIKE 'IBB-PNJ-002-%' AND o.name = 'Cotton') OR
  (v.sku LIKE 'IBB-PNJ-002-WHT-%' AND o.name = 'White') OR
  (v.sku LIKE 'IBB-PNJ-002-NAV-%' AND o.name = 'Navy') OR
  (v.sku LIKE 'IBB-PNJ-002-MAR-%' AND o.name = 'Maroon') OR
  (v.sku LIKE 'IBB-PNJ-002-%-42' AND o.name = '42') OR
  (v.sku LIKE 'IBB-PNJ-002-%-44' AND o.name = '44') OR
  (v.sku LIKE 'IBB-PNJ-002-%-46' AND o.name = '46') OR
  -- PNJ-003 Cotton, White/Blue × 42/44/46
  (v.sku LIKE 'IBB-PNJ-003-%' AND o.name = 'Cotton') OR
  (v.sku LIKE 'IBB-PNJ-003-WHT-%' AND o.name = 'White') OR
  (v.sku LIKE 'IBB-PNJ-003-BLU-%' AND o.name = 'Blue') OR
  (v.sku LIKE 'IBB-PNJ-003-%-42' AND o.name = '42') OR
  (v.sku LIKE 'IBB-PNJ-003-%-44' AND o.name = '44') OR
  (v.sku LIKE 'IBB-PNJ-003-%-46' AND o.name = '46') OR
  -- PNJ-004 Katan Silk, White/Maroon × 42/44/46/48
  (v.sku LIKE 'IBB-PNJ-004-%' AND o.name = 'Katan Silk') OR
  (v.sku LIKE 'IBB-PNJ-004-WHT-%' AND o.name = 'White') OR
  (v.sku LIKE 'IBB-PNJ-004-MAR-%' AND o.name = 'Maroon') OR
  (v.sku LIKE 'IBB-PNJ-004-%-42' AND o.name = '42') OR
  (v.sku LIKE 'IBB-PNJ-004-%-44' AND o.name = '44') OR
  (v.sku LIKE 'IBB-PNJ-004-%-46' AND o.name = '46') OR
  (v.sku LIKE 'IBB-PNJ-004-%-48' AND o.name = '48') OR
  -- PNJ-005 Linen, White/Cream/Navy × 42/44/46
  (v.sku LIKE 'IBB-PNJ-005-%' AND o.name = 'Linen') OR
  (v.sku LIKE 'IBB-PNJ-005-WHT-%' AND o.name = 'White') OR
  (v.sku LIKE 'IBB-PNJ-005-CRM-%' AND o.name = 'Cream') OR
  (v.sku LIKE 'IBB-PNJ-005-NAV-%' AND o.name = 'Navy') OR
  (v.sku LIKE 'IBB-PNJ-005-%-42' AND o.name = '42') OR
  (v.sku LIKE 'IBB-PNJ-005-%-44' AND o.name = '44') OR
  (v.sku LIKE 'IBB-PNJ-005-%-46' AND o.name = '46') OR
  -- PNJ-006 Cotton, White/Navy/Maroon × 44/46
  (v.sku LIKE 'IBB-PNJ-006-%' AND o.name = 'Cotton') OR
  (v.sku LIKE 'IBB-PNJ-006-WHT-%' AND o.name = 'White') OR
  (v.sku LIKE 'IBB-PNJ-006-NAV-%' AND o.name = 'Navy') OR
  (v.sku LIKE 'IBB-PNJ-006-MAR-%' AND o.name = 'Maroon') OR
  (v.sku LIKE 'IBB-PNJ-006-%-44' AND o.name = '44') OR
  (v.sku LIKE 'IBB-PNJ-006-%-46' AND o.name = '46') OR
  -- PNJ-007 Cotton, White/Cream × 44/46
  (v.sku LIKE 'IBB-PNJ-007-%' AND o.name = 'Cotton') OR
  (v.sku LIKE 'IBB-PNJ-007-WHT-%' AND o.name = 'White') OR
  (v.sku LIKE 'IBB-PNJ-007-CRM-%' AND o.name = 'Cream') OR
  (v.sku LIKE 'IBB-PNJ-007-%-44' AND o.name = '44') OR
  (v.sku LIKE 'IBB-PNJ-007-%-46' AND o.name = '46') OR
  -- PNJ-008 Cotton, White/Cream × 42/44/46
  (v.sku LIKE 'IBB-PNJ-008-%' AND o.name = 'Cotton') OR
  (v.sku LIKE 'IBB-PNJ-008-WHT-%' AND o.name = 'White') OR
  (v.sku LIKE 'IBB-PNJ-008-CRM-%' AND o.name = 'Cream') OR
  (v.sku LIKE 'IBB-PNJ-008-%-42' AND o.name = '42') OR
  (v.sku LIKE 'IBB-PNJ-008-%-44' AND o.name = '44') OR
  (v.sku LIKE 'IBB-PNJ-008-%-46' AND o.name = '46') OR
  -- PNJ-009 Cotton, Navy/Maroon/Blue × 44/46
  (v.sku LIKE 'IBB-PNJ-009-%' AND o.name = 'Cotton') OR
  (v.sku LIKE 'IBB-PNJ-009-NAV-%' AND o.name = 'Navy') OR
  (v.sku LIKE 'IBB-PNJ-009-MAR-%' AND o.name = 'Maroon') OR
  (v.sku LIKE 'IBB-PNJ-009-BLU-%' AND o.name = 'Blue') OR
  (v.sku LIKE 'IBB-PNJ-009-%-44' AND o.name = '44') OR
  (v.sku LIKE 'IBB-PNJ-009-%-46' AND o.name = '46') OR
  -- PNJ-010 Katan Silk, Maroon/Navy × 42/44/46
  (v.sku LIKE 'IBB-PNJ-010-%' AND o.name = 'Katan Silk') OR
  (v.sku LIKE 'IBB-PNJ-010-MAR-%' AND o.name = 'Maroon') OR
  (v.sku LIKE 'IBB-PNJ-010-NAV-%' AND o.name = 'Navy') OR
  (v.sku LIKE 'IBB-PNJ-010-%-42' AND o.name = '42') OR
  (v.sku LIKE 'IBB-PNJ-010-%-44' AND o.name = '44') OR
  (v.sku LIKE 'IBB-PNJ-010-%-46' AND o.name = '46')
AND NOT EXISTS (SELECT 1 FROM product_variant_options pvo WHERE pvo.variant_id = v.id AND pvo.option_id = o.id);

-- =====================================================
-- J. Product Images (placeholder via picsum.photos)
-- =====================================================

INSERT INTO product_images (product_id, original_name, mime_type, size, width, height,
                             original_path, medium_path, thumb_path,
                             is_primary, sort_order, created_at, updated_at)
SELECT p.id, t.fname, 'image/jpeg', t.fsize, 800, 800,
       t.orig, t.med, t.thumb, t.primary_img, t.sort, NOW(), NOW()
FROM (VALUES
    -- Borka
    ('IBB-BRK-001','ibb-brk-001-main.jpg',  65000,'https://picsum.photos/seed/ibb-brk-001/800/800','https://picsum.photos/seed/ibb-brk-001/400/400','https://picsum.photos/seed/ibb-brk-001/150/150',true,0),
    ('IBB-BRK-001','ibb-brk-001-detail.jpg',58000,'https://picsum.photos/seed/ibb-brk-001d/800/800','https://picsum.photos/seed/ibb-brk-001d/400/400','https://picsum.photos/seed/ibb-brk-001d/150/150',false,1),
    ('IBB-BRK-002','ibb-brk-002-main.jpg',  62000,'https://picsum.photos/seed/ibb-brk-002/800/800','https://picsum.photos/seed/ibb-brk-002/400/400','https://picsum.photos/seed/ibb-brk-002/150/150',true,0),
    ('IBB-BRK-003','ibb-brk-003-main.jpg',  67000,'https://picsum.photos/seed/ibb-brk-003/800/800','https://picsum.photos/seed/ibb-brk-003/400/400','https://picsum.photos/seed/ibb-brk-003/150/150',true,0),
    ('IBB-BRK-004','ibb-brk-004-main.jpg',  70000,'https://picsum.photos/seed/ibb-brk-004/800/800','https://picsum.photos/seed/ibb-brk-004/400/400','https://picsum.photos/seed/ibb-brk-004/150/150',true,0),
    ('IBB-BRK-004','ibb-brk-004-back.jpg',  66000,'https://picsum.photos/seed/ibb-brk-004b/800/800','https://picsum.photos/seed/ibb-brk-004b/400/400','https://picsum.photos/seed/ibb-brk-004b/150/150',false,1),
    ('IBB-BRK-005','ibb-brk-005-main.jpg',  74000,'https://picsum.photos/seed/ibb-brk-005/800/800','https://picsum.photos/seed/ibb-brk-005/400/400','https://picsum.photos/seed/ibb-brk-005/150/150',true,0),
    ('IBB-BRK-005','ibb-brk-005-detail.jpg',69000,'https://picsum.photos/seed/ibb-brk-005d/800/800','https://picsum.photos/seed/ibb-brk-005d/400/400','https://picsum.photos/seed/ibb-brk-005d/150/150',false,1),
    ('IBB-BRK-006','ibb-brk-006-main.jpg',  60000,'https://picsum.photos/seed/ibb-brk-006/800/800','https://picsum.photos/seed/ibb-brk-006/400/400','https://picsum.photos/seed/ibb-brk-006/150/150',true,0),
    ('IBB-BRK-007','ibb-brk-007-main.jpg',  63000,'https://picsum.photos/seed/ibb-brk-007/800/800','https://picsum.photos/seed/ibb-brk-007/400/400','https://picsum.photos/seed/ibb-brk-007/150/150',true,0),
    ('IBB-BRK-008','ibb-brk-008-main.jpg',  58000,'https://picsum.photos/seed/ibb-brk-008/800/800','https://picsum.photos/seed/ibb-brk-008/400/400','https://picsum.photos/seed/ibb-brk-008/150/150',true,0),
    ('IBB-BRK-009','ibb-brk-009-main.jpg',  55000,'https://picsum.photos/seed/ibb-brk-009/800/800','https://picsum.photos/seed/ibb-brk-009/400/400','https://picsum.photos/seed/ibb-brk-009/150/150',true,0),
    ('IBB-BRK-010','ibb-brk-010-main.jpg',  71000,'https://picsum.photos/seed/ibb-brk-010/800/800','https://picsum.photos/seed/ibb-brk-010/400/400','https://picsum.photos/seed/ibb-brk-010/150/150',true,0),
    ('IBB-BRK-010','ibb-brk-010-back.jpg',  68000,'https://picsum.photos/seed/ibb-brk-010b/800/800','https://picsum.photos/seed/ibb-brk-010b/400/400','https://picsum.photos/seed/ibb-brk-010b/150/150',false,1),
    ('IBB-BRK-011','ibb-brk-011-main.jpg',  76000,'https://picsum.photos/seed/ibb-brk-011/800/800','https://picsum.photos/seed/ibb-brk-011/400/400','https://picsum.photos/seed/ibb-brk-011/150/150',true,0),
    ('IBB-BRK-011','ibb-brk-011-detail.jpg',72000,'https://picsum.photos/seed/ibb-brk-011d/800/800','https://picsum.photos/seed/ibb-brk-011d/400/400','https://picsum.photos/seed/ibb-brk-011d/150/150',false,1),
    ('IBB-BRK-012','ibb-brk-012-main.jpg',  45000,'https://picsum.photos/seed/ibb-brk-012/800/800','https://picsum.photos/seed/ibb-brk-012/400/400','https://picsum.photos/seed/ibb-brk-012/150/150',true,0),
    ('IBB-BRK-013','ibb-brk-013-main.jpg',  59000,'https://picsum.photos/seed/ibb-brk-013/800/800','https://picsum.photos/seed/ibb-brk-013/400/400','https://picsum.photos/seed/ibb-brk-013/150/150',true,0),
    -- Abaya
    ('IBB-ABY-001','ibb-aby-001-main.jpg',  78000,'https://picsum.photos/seed/ibb-aby-001/800/800','https://picsum.photos/seed/ibb-aby-001/400/400','https://picsum.photos/seed/ibb-aby-001/150/150',true,0),
    ('IBB-ABY-001','ibb-aby-001-detail.jpg',74000,'https://picsum.photos/seed/ibb-aby-001d/800/800','https://picsum.photos/seed/ibb-aby-001d/400/400','https://picsum.photos/seed/ibb-aby-001d/150/150',false,1),
    ('IBB-ABY-002','ibb-aby-002-main.jpg',  64000,'https://picsum.photos/seed/ibb-aby-002/800/800','https://picsum.photos/seed/ibb-aby-002/400/400','https://picsum.photos/seed/ibb-aby-002/150/150',true,0),
    ('IBB-ABY-003','ibb-aby-003-main.jpg',  82000,'https://picsum.photos/seed/ibb-aby-003/800/800','https://picsum.photos/seed/ibb-aby-003/400/400','https://picsum.photos/seed/ibb-aby-003/150/150',true,0),
    ('IBB-ABY-003','ibb-aby-003-back.jpg',  79000,'https://picsum.photos/seed/ibb-aby-003b/800/800','https://picsum.photos/seed/ibb-aby-003b/400/400','https://picsum.photos/seed/ibb-aby-003b/150/150',false,1),
    ('IBB-ABY-004','ibb-aby-004-main.jpg',  61000,'https://picsum.photos/seed/ibb-aby-004/800/800','https://picsum.photos/seed/ibb-aby-004/400/400','https://picsum.photos/seed/ibb-aby-004/150/150',true,0),
    ('IBB-ABY-005','ibb-aby-005-main.jpg',  66000,'https://picsum.photos/seed/ibb-aby-005/800/800','https://picsum.photos/seed/ibb-aby-005/400/400','https://picsum.photos/seed/ibb-aby-005/150/150',true,0),
    ('IBB-ABY-006','ibb-aby-006-main.jpg',  59000,'https://picsum.photos/seed/ibb-aby-006/800/800','https://picsum.photos/seed/ibb-aby-006/400/400','https://picsum.photos/seed/ibb-aby-006/150/150',true,0),
    ('IBB-ABY-007','ibb-aby-007-main.jpg',  54000,'https://picsum.photos/seed/ibb-aby-007/800/800','https://picsum.photos/seed/ibb-aby-007/400/400','https://picsum.photos/seed/ibb-aby-007/150/150',true,0),
    ('IBB-ABY-008','ibb-aby-008-main.jpg',  69000,'https://picsum.photos/seed/ibb-aby-008/800/800','https://picsum.photos/seed/ibb-aby-008/400/400','https://picsum.photos/seed/ibb-aby-008/150/150',true,0),
    ('IBB-ABY-009','ibb-aby-009-main.jpg',  63000,'https://picsum.photos/seed/ibb-aby-009/800/800','https://picsum.photos/seed/ibb-aby-009/400/400','https://picsum.photos/seed/ibb-aby-009/150/150',true,0),
    ('IBB-ABY-010','ibb-aby-010-main.jpg',  57000,'https://picsum.photos/seed/ibb-aby-010/800/800','https://picsum.photos/seed/ibb-aby-010/400/400','https://picsum.photos/seed/ibb-aby-010/150/150',true,0),
    -- Hijab
    ('IBB-HJB-001','ibb-hjb-001-main.jpg',  38000,'https://picsum.photos/seed/ibb-hjb-001/800/800','https://picsum.photos/seed/ibb-hjb-001/400/400','https://picsum.photos/seed/ibb-hjb-001/150/150',true,0),
    ('IBB-HJB-002','ibb-hjb-002-main.jpg',  35000,'https://picsum.photos/seed/ibb-hjb-002/800/800','https://picsum.photos/seed/ibb-hjb-002/400/400','https://picsum.photos/seed/ibb-hjb-002/150/150',true,0),
    ('IBB-HJB-003','ibb-hjb-003-main.jpg',  42000,'https://picsum.photos/seed/ibb-hjb-003/800/800','https://picsum.photos/seed/ibb-hjb-003/400/400','https://picsum.photos/seed/ibb-hjb-003/150/150',true,0),
    ('IBB-HJB-004','ibb-hjb-004-main.jpg',  37000,'https://picsum.photos/seed/ibb-hjb-004/800/800','https://picsum.photos/seed/ibb-hjb-004/400/400','https://picsum.photos/seed/ibb-hjb-004/150/150',true,0),
    ('IBB-HJB-005','ibb-hjb-005-main.jpg',  32000,'https://picsum.photos/seed/ibb-hjb-005/800/800','https://picsum.photos/seed/ibb-hjb-005/400/400','https://picsum.photos/seed/ibb-hjb-005/150/150',true,0),
    ('IBB-HJB-006','ibb-hjb-006-main.jpg',  44000,'https://picsum.photos/seed/ibb-hjb-006/800/800','https://picsum.photos/seed/ibb-hjb-006/400/400','https://picsum.photos/seed/ibb-hjb-006/150/150',true,0),
    ('IBB-HJB-007','ibb-hjb-007-main.jpg',  39000,'https://picsum.photos/seed/ibb-hjb-007/800/800','https://picsum.photos/seed/ibb-hjb-007/400/400','https://picsum.photos/seed/ibb-hjb-007/150/150',true,0),
    ('IBB-HJB-008','ibb-hjb-008-main.jpg',  36000,'https://picsum.photos/seed/ibb-hjb-008/800/800','https://picsum.photos/seed/ibb-hjb-008/400/400','https://picsum.photos/seed/ibb-hjb-008/150/150',true,0),
    ('IBB-HJB-009','ibb-hjb-009-main.jpg',  48000,'https://picsum.photos/seed/ibb-hjb-009/800/800','https://picsum.photos/seed/ibb-hjb-009/400/400','https://picsum.photos/seed/ibb-hjb-009/150/150',true,0),
    ('IBB-HJB-010','ibb-hjb-010-main.jpg',  52000,'https://picsum.photos/seed/ibb-hjb-010/800/800','https://picsum.photos/seed/ibb-hjb-010/400/400','https://picsum.photos/seed/ibb-hjb-010/150/150',true,0),
    -- Panjabi
    ('IBB-PNJ-001','ibb-pnj-001-main.jpg',  61000,'https://picsum.photos/seed/ibb-pnj-001/800/800','https://picsum.photos/seed/ibb-pnj-001/400/400','https://picsum.photos/seed/ibb-pnj-001/150/150',true,0),
    ('IBB-PNJ-001','ibb-pnj-001-detail.jpg',57000,'https://picsum.photos/seed/ibb-pnj-001d/800/800','https://picsum.photos/seed/ibb-pnj-001d/400/400','https://picsum.photos/seed/ibb-pnj-001d/150/150',false,1),
    ('IBB-PNJ-002','ibb-pnj-002-main.jpg',  48000,'https://picsum.photos/seed/ibb-pnj-002/800/800','https://picsum.photos/seed/ibb-pnj-002/400/400','https://picsum.photos/seed/ibb-pnj-002/150/150',true,0),
    ('IBB-PNJ-003','ibb-pnj-003-main.jpg',  52000,'https://picsum.photos/seed/ibb-pnj-003/800/800','https://picsum.photos/seed/ibb-pnj-003/400/400','https://picsum.photos/seed/ibb-pnj-003/150/150',true,0),
    ('IBB-PNJ-004','ibb-pnj-004-main.jpg',  68000,'https://picsum.photos/seed/ibb-pnj-004/800/800','https://picsum.photos/seed/ibb-pnj-004/400/400','https://picsum.photos/seed/ibb-pnj-004/150/150',true,0),
    ('IBB-PNJ-004','ibb-pnj-004-detail.jpg',64000,'https://picsum.photos/seed/ibb-pnj-004d/800/800','https://picsum.photos/seed/ibb-pnj-004d/400/400','https://picsum.photos/seed/ibb-pnj-004d/150/150',false,1),
    ('IBB-PNJ-005','ibb-pnj-005-main.jpg',  55000,'https://picsum.photos/seed/ibb-pnj-005/800/800','https://picsum.photos/seed/ibb-pnj-005/400/400','https://picsum.photos/seed/ibb-pnj-005/150/150',true,0),
    ('IBB-PNJ-006','ibb-pnj-006-main.jpg',  50000,'https://picsum.photos/seed/ibb-pnj-006/800/800','https://picsum.photos/seed/ibb-pnj-006/400/400','https://picsum.photos/seed/ibb-pnj-006/150/150',true,0),
    ('IBB-PNJ-007','ibb-pnj-007-main.jpg',  58000,'https://picsum.photos/seed/ibb-pnj-007/800/800','https://picsum.photos/seed/ibb-pnj-007/400/400','https://picsum.photos/seed/ibb-pnj-007/150/150',true,0),
    ('IBB-PNJ-008','ibb-pnj-008-main.jpg',  43000,'https://picsum.photos/seed/ibb-pnj-008/800/800','https://picsum.photos/seed/ibb-pnj-008/400/400','https://picsum.photos/seed/ibb-pnj-008/150/150',true,0),
    ('IBB-PNJ-009','ibb-pnj-009-main.jpg',  54000,'https://picsum.photos/seed/ibb-pnj-009/800/800','https://picsum.photos/seed/ibb-pnj-009/400/400','https://picsum.photos/seed/ibb-pnj-009/150/150',true,0),
    ('IBB-PNJ-010','ibb-pnj-010-main.jpg',  62000,'https://picsum.photos/seed/ibb-pnj-010/800/800','https://picsum.photos/seed/ibb-pnj-010/400/400','https://picsum.photos/seed/ibb-pnj-010/150/150',true,0),
    ('IBB-PNJ-010','ibb-pnj-010-detail.jpg',59000,'https://picsum.photos/seed/ibb-pnj-010d/800/800','https://picsum.photos/seed/ibb-pnj-010d/400/400','https://picsum.photos/seed/ibb-pnj-010d/150/150',false,1)
) AS t(psku, fname, fsize, orig, med, thumb, primary_img, sort)
JOIN products p ON p.sku = t.psku
WHERE NOT EXISTS (
    SELECT 1 FROM product_images pi
    WHERE pi.product_id = p.id AND pi.original_name = t.fname
);

-- =====================================================
-- K. Inventory
-- Distribute stock: Main Store + Central Warehouse + Downtown (hijab & panjabi only)
-- =====================================================

-- Borka variants → Main Store
INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold)
SELECT v.id, (SELECT id FROM outlets WHERE name = 'Main Store'), 'outlet',
       CASE
           WHEN v.sku LIKE 'IBB-BRK-005%' THEN 8    -- premium koti, lower stock
           WHEN v.sku LIKE 'IBB-BRK-011%' THEN 6
           WHEN v.sku LIKE 'IBB-BRK-012%' THEN 20   -- budget item, higher stock
           ELSE 12
       END, 2
FROM product_variants v
WHERE v.sku LIKE 'IBB-BRK-%'
ON CONFLICT (variant_id, location_id, location_type) DO NOTHING;

-- Borka variants → Central Warehouse
INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold)
SELECT v.id, (SELECT id FROM warehouses WHERE name = 'Central Warehouse'), 'warehouse',
       CASE
           WHEN v.sku LIKE 'IBB-BRK-005%' THEN 30
           WHEN v.sku LIKE 'IBB-BRK-011%' THEN 25
           WHEN v.sku LIKE 'IBB-BRK-012%' THEN 80
           ELSE 50
       END, 10
FROM product_variants v
WHERE v.sku LIKE 'IBB-BRK-%'
ON CONFLICT (variant_id, location_id, location_type) DO NOTHING;

-- Abaya variants → Main Store
INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold)
SELECT v.id, (SELECT id FROM outlets WHERE name = 'Main Store'), 'outlet',
       CASE
           WHEN v.sku LIKE 'IBB-ABY-003%' THEN 5    -- luxury, low stock
           WHEN v.sku LIKE 'IBB-ABY-010%' THEN 18
           ELSE 10
       END, 2
FROM product_variants v
WHERE v.sku LIKE 'IBB-ABY-%'
ON CONFLICT (variant_id, location_id, location_type) DO NOTHING;

-- Abaya variants → Central Warehouse
INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold)
SELECT v.id, (SELECT id FROM warehouses WHERE name = 'Central Warehouse'), 'warehouse',
       CASE
           WHEN v.sku LIKE 'IBB-ABY-003%' THEN 20
           ELSE 45
       END, 8
FROM product_variants v
WHERE v.sku LIKE 'IBB-ABY-%'
ON CONFLICT (variant_id, location_id, location_type) DO NOTHING;

-- Hijab variants → Main Store
INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold)
SELECT v.id, (SELECT id FROM outlets WHERE name = 'Main Store'), 'outlet',
       CASE
           WHEN v.sku LIKE 'IBB-HJB-005%' THEN 35   -- budget cotton, high stock
           WHEN v.sku LIKE 'IBB-HJB-002%' THEN 30
           WHEN v.sku LIKE 'IBB-HJB-009%' THEN 12   -- party, lower stock
           WHEN v.sku LIKE 'IBB-HJB-010%' THEN 10
           ELSE 20
       END, 5
FROM product_variants v
WHERE v.sku LIKE 'IBB-HJB-%'
ON CONFLICT (variant_id, location_id, location_type) DO NOTHING;

-- Hijab variants → Downtown Branch (fast fashion at branch)
INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold)
SELECT v.id, (SELECT id FROM outlets WHERE name = 'Downtown Branch'), 'outlet',
       CASE
           WHEN v.sku LIKE 'IBB-HJB-002%' THEN 15
           WHEN v.sku LIKE 'IBB-HJB-005%' THEN 18
           ELSE 8
       END, 3
FROM product_variants v
WHERE v.sku LIKE 'IBB-HJB-%'
ON CONFLICT (variant_id, location_id, location_type) DO NOTHING;

-- Hijab variants → Central Warehouse
INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold)
SELECT v.id, (SELECT id FROM warehouses WHERE name = 'Central Warehouse'), 'warehouse',
       CASE
           WHEN v.sku LIKE 'IBB-HJB-005%' THEN 200
           WHEN v.sku LIKE 'IBB-HJB-002%' THEN 150
           WHEN v.sku LIKE 'IBB-HJB-009%' THEN 60
           ELSE 100
       END, 20
FROM product_variants v
WHERE v.sku LIKE 'IBB-HJB-%'
ON CONFLICT (variant_id, location_id, location_type) DO NOTHING;

-- Panjabi variants → Main Store
INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold)
SELECT v.id, (SELECT id FROM outlets WHERE name = 'Main Store'), 'outlet',
       CASE
           WHEN v.sku LIKE 'IBB-PNJ-004%' THEN 8    -- eid katan, premium
           WHEN v.sku LIKE 'IBB-PNJ-001%' THEN 6
           WHEN v.sku LIKE 'IBB-PNJ-008%' THEN 25   -- budget
           ELSE 15
       END, 3
FROM product_variants v
WHERE v.sku LIKE 'IBB-PNJ-%'
ON CONFLICT (variant_id, location_id, location_type) DO NOTHING;

-- Panjabi variants → Downtown Branch
INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold)
SELECT v.id, (SELECT id FROM outlets WHERE name = 'Downtown Branch'), 'outlet',
       CASE
           WHEN v.sku LIKE 'IBB-PNJ-002%' THEN 12
           WHEN v.sku LIKE 'IBB-PNJ-008%' THEN 14
           ELSE 6
       END, 2
FROM product_variants v
WHERE v.sku LIKE 'IBB-PNJ-002-%' OR v.sku LIKE 'IBB-PNJ-005-%' OR v.sku LIKE 'IBB-PNJ-008-%'
ON CONFLICT (variant_id, location_id, location_type) DO NOTHING;

-- Panjabi variants → Central Warehouse
INSERT INTO inventories (variant_id, location_id, location_type, quantity, low_stock_threshold)
SELECT v.id, (SELECT id FROM warehouses WHERE name = 'Central Warehouse'), 'warehouse',
       CASE
           WHEN v.sku LIKE 'IBB-PNJ-004%' THEN 40
           WHEN v.sku LIKE 'IBB-PNJ-001%' THEN 30
           WHEN v.sku LIKE 'IBB-PNJ-008%' THEN 120
           WHEN v.sku LIKE 'IBB-PNJ-002%' THEN 90
           ELSE 60
       END, 10
FROM product_variants v
WHERE v.sku LIKE 'IBB-PNJ-%'
ON CONFLICT (variant_id, location_id, location_type) DO NOTHING;

-- =====================================================
-- L. Purchase Orders (4 POs: 3 received, 1 approved/pending)
-- po_number format: PO-YYYYMMDD-NNNN (unique, required)
-- Status: fully_received | approved
-- =====================================================

-- PO 1: IBB Wholesale BD → Borka & Abaya (received)
INSERT INTO purchase_orders (po_number, supplier_id, warehouse_id, order_date, expected_delivery,
                              total_amount, status, notes, created_by, created_at, updated_at)
SELECT 'PO-20260518-0201',
       (SELECT id FROM suppliers WHERE name = 'IBB Wholesale BD'),
       (SELECT id FROM warehouses WHERE name = 'Central Warehouse'),
       NOW() - INTERVAL '20 days', NOW() - INTERVAL '10 days',
       512400.00, 'fully_received',
       'Borka and Abaya stock for main season. 20-day delivery confirmed.',
       (SELECT id FROM users WHERE email = 'tom.stock@retailpos.com'),
       NOW() - INTERVAL '20 days', NOW() - INTERVAL '10 days'
WHERE NOT EXISTS (SELECT 1 FROM purchase_orders WHERE po_number = 'PO-20260518-0201');

-- PO 2: Nidha Fabrics Ltd. → Hijab (received)
INSERT INTO purchase_orders (po_number, supplier_id, warehouse_id, order_date, expected_delivery,
                              total_amount, status, notes, created_by, created_at, updated_at)
SELECT 'PO-20260521-0202',
       (SELECT id FROM suppliers WHERE name = 'Nidha Fabrics Ltd.'),
       (SELECT id FROM warehouses WHERE name = 'Central Warehouse'),
       NOW() - INTERVAL '17 days', NOW() - INTERVAL '7 days',
       148500.00, 'fully_received',
       'Hijab seasonal restock – Nidha, Georgette, Cotton lines.',
       (SELECT id FROM users WHERE email = 'tom.stock@retailpos.com'),
       NOW() - INTERVAL '17 days', NOW() - INTERVAL '7 days'
WHERE NOT EXISTS (SELECT 1 FROM purchase_orders WHERE po_number = 'PO-20260521-0202');

-- PO 3: Panjabi Mart BD → Panjabi (received)
INSERT INTO purchase_orders (po_number, supplier_id, warehouse_id, order_date, expected_delivery,
                              total_amount, status, notes, created_by, created_at, updated_at)
SELECT 'PO-20260525-0203',
       (SELECT id FROM suppliers WHERE name = 'Panjabi Mart BD'),
       (SELECT id FROM warehouses WHERE name = 'Central Warehouse'),
       NOW() - INTERVAL '13 days', NOW() - INTERVAL '3 days',
       162400.00, 'fully_received',
       'Panjabi pre-Eid stock – Cotton, Katan Silk, Linen lines.',
       (SELECT id FROM users WHERE email = 'tom.stock@retailpos.com'),
       NOW() - INTERVAL '13 days', NOW() - INTERVAL '3 days'
WHERE NOT EXISTS (SELECT 1 FROM purchase_orders WHERE po_number = 'PO-20260525-0203');

-- PO 4: IBB Wholesale BD → Borka restock, approved/pending receipt
INSERT INTO purchase_orders (po_number, supplier_id, warehouse_id, order_date, expected_delivery,
                              total_amount, status, notes, created_by, created_at, updated_at)
SELECT 'PO-20260604-0204',
       (SELECT id FROM suppliers WHERE name = 'IBB Wholesale BD'),
       (SELECT id FROM warehouses WHERE name = 'North Warehouse'),
       NOW() - INTERVAL '3 days', NOW() + INTERVAL '7 days',
       185600.00, 'approved',
       'Restock order for premium koti borka and abaya lines ahead of Eid.',
       (SELECT id FROM users WHERE email = 'tom.stock@retailpos.com'),
       NOW() - INTERVAL '3 days', NOW() - INTERVAL '3 days'
WHERE NOT EXISTS (SELECT 1 FROM purchase_orders WHERE po_number = 'PO-20260604-0204');

-- =====================================================
-- M. Purchase Order Items
-- =====================================================

-- PO 1 items: Borka & Abaya (received PO)
INSERT INTO purchase_order_items (po_id, variant_id, quantity, unit_price, discount, tax)
SELECT po.id, v.id, t.qty::int, t.price::numeric, 0, 0
FROM (VALUES
    ('IBB-BRK-001-52-KGG', 30, 2800),
    ('IBB-BRK-001-54-KGG', 25, 2800),
    ('IBB-BRK-001-56-KGG', 20, 2800),
    ('IBB-BRK-005-52-KGG', 15, 5095),
    ('IBB-BRK-005-54-KGG', 12, 5095),
    ('IBB-BRK-005-56-KGG', 10, 5095),
    ('IBB-BRK-011-52-KGG', 20, 4075),
    ('IBB-BRK-011-54-KGG', 18, 4075),
    ('IBB-BRK-011-56-KGG', 15, 4075),
    ('IBB-BRK-011-58-KGG', 10, 4075),
    ('IBB-ABY-001-52-KGG', 20, 4432),
    ('IBB-ABY-001-54-KGG', 18, 4432),
    ('IBB-ABY-001-56-KGG', 15, 4432),
    ('IBB-ABY-003-52-KGG', 12, 6115),
    ('IBB-ABY-003-54-KGG', 10, 6115),
    ('IBB-ABY-008-52-KGG', 18, 3412),
    ('IBB-ABY-008-54-KGG', 15, 3412),
    ('IBB-ABY-008-56-KGG', 12, 3412)
) AS t(vsku, qty, price)
JOIN product_variants v ON v.sku = t.vsku
JOIN purchase_orders po ON po.po_number = 'PO-20260518-0201'
WHERE NOT EXISTS (SELECT 1 FROM purchase_order_items poi WHERE poi.po_id = po.id AND poi.variant_id = v.id);

-- PO 2 items: Hijab (received PO)
INSERT INTO purchase_order_items (po_id, variant_id, quantity, unit_price, discount, tax)
SELECT po.id, v.id, t.qty::int, t.price::numeric, 0, 0
FROM (VALUES
    ('IBB-HJB-001-BLK-KGG', 50, 380),
    ('IBB-HJB-001-WHT-KGG', 40, 380),
    ('IBB-HJB-001-CRM-KGG', 35, 380),
    ('IBB-HJB-002-BLK-NID', 60, 225),
    ('IBB-HJB-002-WHT-NID', 55, 225),
    ('IBB-HJB-002-SGN-NID', 40, 225),
    ('IBB-HJB-002-NAV-NID', 40, 225),
    ('IBB-HJB-005-BLK-CTN', 80, 175),
    ('IBB-HJB-005-WHT-CTN', 70, 175),
    ('IBB-HJB-005-NAV-CTN', 50, 175),
    ('IBB-HJB-005-BLU-CTN', 40, 175),
    ('IBB-HJB-008-BLK-KGG', 45, 300),
    ('IBB-HJB-008-NAV-KGG', 35, 300),
    ('IBB-HJB-009-BLK-KGG', 30, 600),
    ('IBB-HJB-009-RPK-KGG', 25, 600),
    ('IBB-HJB-010-BLK-NID', 20, 750),
    ('IBB-HJB-010-NAV-NID', 15, 750)
) AS t(vsku, qty, price)
JOIN product_variants v ON v.sku = t.vsku
JOIN purchase_orders po ON po.po_number = 'PO-20260521-0202'
WHERE NOT EXISTS (SELECT 1 FROM purchase_order_items poi WHERE poi.po_id = po.id AND poi.variant_id = v.id);

-- PO 3 items: Panjabi (received PO)
INSERT INTO purchase_order_items (po_id, variant_id, quantity, unit_price, discount, tax)
SELECT po.id, v.id, t.qty::int, t.price::numeric, 0, 0
FROM (VALUES
    ('IBB-PNJ-002-WHT-42', 30, 480), ('IBB-PNJ-002-WHT-44', 30, 480), ('IBB-PNJ-002-WHT-46', 25, 480),
    ('IBB-PNJ-002-NAV-42', 20, 480), ('IBB-PNJ-002-NAV-44', 20, 480), ('IBB-PNJ-002-NAV-46', 18, 480),
    ('IBB-PNJ-002-MAR-44', 20, 480), ('IBB-PNJ-002-MAR-46', 18, 480),
    ('IBB-PNJ-004-WHT-42', 15, 2100), ('IBB-PNJ-004-WHT-44', 15, 2100),
    ('IBB-PNJ-004-MAR-42', 12, 2100), ('IBB-PNJ-004-MAR-44', 12, 2100),
    ('IBB-PNJ-005-WHT-42', 20, 900), ('IBB-PNJ-005-WHT-44', 20, 900),
    ('IBB-PNJ-005-NAV-44', 15, 900), ('IBB-PNJ-005-NAV-46', 15, 900),
    ('IBB-PNJ-010-MAR-42', 15, 1500), ('IBB-PNJ-010-MAR-44', 15, 1500),
    ('IBB-PNJ-010-NAV-42', 12, 1500), ('IBB-PNJ-010-NAV-44', 12, 1500)
) AS t(vsku, qty, price)
JOIN product_variants v ON v.sku = t.vsku
JOIN purchase_orders po ON po.po_number = 'PO-20260525-0203'
WHERE NOT EXISTS (SELECT 1 FROM purchase_order_items poi WHERE poi.po_id = po.id AND poi.variant_id = v.id);

-- PO 4 items: Borka/Abaya restock (pending/approved – no GRN yet)
INSERT INTO purchase_order_items (po_id, variant_id, quantity, unit_price, discount, tax)
SELECT po.id, v.id, t.qty::int, t.price::numeric, 0, 0
FROM (VALUES
    ('IBB-BRK-004-52-KGG', 25, 3412), ('IBB-BRK-004-54-KGG', 25, 3412), ('IBB-BRK-004-56-KGG', 20, 3412),
    ('IBB-BRK-007-52-KGG', 20, 3055), ('IBB-BRK-007-54-KGG', 20, 3055),
    ('IBB-ABY-003-52-KGG', 15, 6115), ('IBB-ABY-003-54-KGG', 12, 6115), ('IBB-ABY-003-56-KGG', 10, 6115),
    ('IBB-ABY-005-52-KGG', 20, 3310), ('IBB-ABY-005-54-KGG', 18, 3310), ('IBB-ABY-005-56-KGG', 15, 3310),
    ('IBB-BRK-013-50-KGG', 20, 2290), ('IBB-BRK-013-52-KGG', 20, 2290), ('IBB-BRK-013-54-KGG', 18, 2290)
) AS t(vsku, qty, price)
JOIN product_variants v ON v.sku = t.vsku
JOIN purchase_orders po ON po.po_number = 'PO-20260604-0204'
WHERE NOT EXISTS (SELECT 1 FROM purchase_order_items poi WHERE poi.po_id = po.id AND poi.variant_id = v.id);

-- =====================================================
-- N. GRNs (for the 3 fully_received POs only; PO 4 is still open)
-- =====================================================

INSERT INTO grns (po_id, received_date, status, notes, created_by, created_at)
SELECT po.id, po.expected_delivery, 'full',
       'All items received in good condition. Checked and counted.',
       po.created_by, po.expected_delivery
FROM purchase_orders po
WHERE po.po_number IN ('PO-20260518-0201', 'PO-20260521-0202', 'PO-20260525-0203')
  AND NOT EXISTS (SELECT 1 FROM grns g WHERE g.po_id = po.id);

-- =====================================================
-- O. GRN Items (full receipt: received_qty = ordered quantity, unit_cost = po unit_price)
-- =====================================================

INSERT INTO grn_items (grn_id, po_item_id, received_qty, unit_cost)
SELECT g.id, poi.id, poi.quantity, poi.unit_price
FROM grns g
JOIN purchase_order_items poi ON poi.po_id = g.po_id
JOIN purchase_orders po ON po.id = g.po_id
WHERE po.po_number IN ('PO-20260518-0201', 'PO-20260521-0202', 'PO-20260525-0203')
  AND NOT EXISTS (SELECT 1 FROM grn_items gi WHERE gi.grn_id = g.id AND gi.po_item_id = poi.id);

-- =====================================================
-- P. Sample Sales (for Stock Reports & Sales flow testing)
-- =====================================================

-- Sale 1: Main Store – borka & hijab sale
INSERT INTO sales (outlet_id, customer_id, sale_date, total_amount, discount, tax,
                   payment_method, status, cashier_id, created_at)
SELECT (SELECT id FROM outlets WHERE name = 'Main Store'),
       (SELECT id FROM customers WHERE name = 'Alice Johnson'),
       NOW() - INTERVAL '4 days',
       14751.00, 0, 0, 'cash', 'completed',
       (SELECT id FROM users WHERE email = 'mike.cashier@retailpos.com'),
       NOW() - INTERVAL '4 days'
WHERE NOT EXISTS (
    SELECT 1 FROM sales s
    WHERE s.cashier_id = (SELECT id FROM users WHERE email = 'mike.cashier@retailpos.com')
      AND s.total_amount = 14751.00
      AND s.sale_date::date = (NOW() - INTERVAL '4 days')::date
);

INSERT INTO sale_items (sale_id, variant_id, quantity, unit_price, subtotal)
SELECT s.id, v.id, t.qty::int, t.price::numeric, t.qty::int * t.price::numeric
FROM (VALUES
    ('IBB-BRK-001-54-KGG', 2, 4667.00),
    ('IBB-HJB-001-BLK-KGG', 3, 750.00),
    ('IBB-HJB-002-WHT-NID', 4, 450.00)
) AS t(vsku, qty, price)
JOIN product_variants v ON v.sku = t.vsku
JOIN sales s ON s.total_amount = 14751.00
           AND s.cashier_id = (SELECT id FROM users WHERE email = 'mike.cashier@retailpos.com')
           AND s.sale_date::date = (NOW() - INTERVAL '4 days')::date
WHERE NOT EXISTS (SELECT 1 FROM sale_items si WHERE si.sale_id = s.id AND si.variant_id = v.id);

-- Sale 2: Main Store – abaya & panjabi sale
INSERT INTO sales (outlet_id, customer_id, sale_date, total_amount, discount, tax,
                   payment_method, status, cashier_id, created_at)
SELECT (SELECT id FROM outlets WHERE name = 'Main Store'),
       (SELECT id FROM customers WHERE name = 'Carol Davis'),
       NOW() - INTERVAL '2 days',
       22274.00, 0, 0, 'card', 'completed',
       (SELECT id FROM users WHERE email = 'mike.cashier@retailpos.com'),
       NOW() - INTERVAL '2 days'
WHERE NOT EXISTS (
    SELECT 1 FROM sales s
    WHERE s.cashier_id = (SELECT id FROM users WHERE email = 'mike.cashier@retailpos.com')
      AND s.total_amount = 22274.00
      AND s.sale_date::date = (NOW() - INTERVAL '2 days')::date
);

INSERT INTO sale_items (sale_id, variant_id, quantity, unit_price, subtotal)
SELECT s.id, v.id, t.qty::int, t.price::numeric, t.qty::int * t.price::numeric
FROM (VALUES
    ('IBB-ABY-001-54-KGG', 2, 7387.00),
    ('IBB-PNJ-004-WHT-44', 1, 3500.00),
    ('IBB-HJB-009-BLK-KGG', 3, 1200.00)
) AS t(vsku, qty, price)
JOIN product_variants v ON v.sku = t.vsku
JOIN sales s ON s.total_amount = 22274.00
           AND s.cashier_id = (SELECT id FROM users WHERE email = 'mike.cashier@retailpos.com')
           AND s.sale_date::date = (NOW() - INTERVAL '2 days')::date
WHERE NOT EXISTS (SELECT 1 FROM sale_items si WHERE si.sale_id = s.id AND si.variant_id = v.id);

-- =====================================================
-- Seed complete.
-- Products    : 43 (13 Borka + 10 Abaya + 10 Hijab + 10 Panjabi)
-- Variants    : 172 (42 BRK + 32 ABY + 32 HJB + 66 PNJ)
-- Images      : ~56 product images
-- Inventory   : ~3 locations × 172 variants = ~450 records
-- POs         : 4 (3 fully_received + 1 approved)
-- GRNs        : 3 (full receipts)
-- Sample Sales: 2
-- =====================================================
