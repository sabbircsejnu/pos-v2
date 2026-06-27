-- Migration: Stock Count module schema + baseline role permissions
-- Safe to run multiple times where possible.

CREATE TABLE IF NOT EXISTS stock_counts (
    id BIGSERIAL PRIMARY KEY,
    stock_count_no VARCHAR(40) NOT NULL,
    business_id BIGINT NOT NULL,
    location_id BIGINT NOT NULL,
    location_type VARCHAR(20) NOT NULL,
    stock_count_date DATE NOT NULL,
    status VARCHAR(30) NOT NULL DEFAULT 'Draft',
    remarks VARCHAR(1000) NULL,
    total_items INTEGER NOT NULL DEFAULT 0,
    created_by BIGINT NOT NULL,
    created_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW(),
    submitted_by BIGINT NULL,
    submitted_at TIMESTAMP WITHOUT TIME ZONE NULL,
    approved_by BIGINT NULL,
    approved_at TIMESTAMP WITHOUT TIME ZONE NULL,
    rejected_by BIGINT NULL,
    rejected_at TIMESTAMP WITHOUT TIME ZONE NULL,
    rejection_reason VARCHAR(1000) NULL,
    CONSTRAINT fk_stock_counts_business_id FOREIGN KEY (business_id) REFERENCES businesses (id) ON DELETE RESTRICT,
    CONSTRAINT fk_stock_counts_created_by FOREIGN KEY (created_by) REFERENCES users (id) ON DELETE RESTRICT,
    CONSTRAINT fk_stock_counts_submitted_by FOREIGN KEY (submitted_by) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_stock_counts_approved_by FOREIGN KEY (approved_by) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_stock_counts_rejected_by FOREIGN KEY (rejected_by) REFERENCES users (id) ON DELETE SET NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_stock_counts_stock_count_no
    ON stock_counts(stock_count_no);

CREATE INDEX IF NOT EXISTS ix_stock_counts_business_id
    ON stock_counts(business_id);

CREATE INDEX IF NOT EXISTS ix_stock_counts_location_id
    ON stock_counts(location_id);

CREATE INDEX IF NOT EXISTS ix_stock_counts_stock_count_date
    ON stock_counts(stock_count_date);

CREATE INDEX IF NOT EXISTS ix_stock_counts_status
    ON stock_counts(status);

CREATE INDEX IF NOT EXISTS ix_stock_counts_business_location_created_at
    ON stock_counts(business_id, location_type, location_id, created_at);

-- One active stock count per location where active = Draft or Submitted.
CREATE UNIQUE INDEX IF NOT EXISTS ux_stock_counts_one_active_per_location
    ON stock_counts(business_id, location_type, location_id)
    WHERE status IN ('Draft', 'Submitted');

CREATE TABLE IF NOT EXISTS stock_count_lines (
    id BIGSERIAL PRIMARY KEY,
    stock_count_id BIGINT NOT NULL,
    product_id BIGINT NOT NULL,
    variant_id BIGINT NOT NULL,
    product_name VARCHAR(255) NOT NULL,
    product_code VARCHAR(100) NOT NULL,
    variant_name VARCHAR(255) NOT NULL,
    current_stock NUMERIC(18,3) NOT NULL,
    physical_stock NUMERIC(18,3) NULL,
    difference NUMERIC(18,3) NULL,
    remarks VARCHAR(1000) NULL,
    CONSTRAINT fk_stock_count_lines_stock_count_id FOREIGN KEY (stock_count_id) REFERENCES stock_counts (id) ON DELETE CASCADE,
    CONSTRAINT fk_stock_count_lines_product_id FOREIGN KEY (product_id) REFERENCES products (id) ON DELETE RESTRICT,
    CONSTRAINT fk_stock_count_lines_variant_id FOREIGN KEY (variant_id) REFERENCES product_variants (id) ON DELETE RESTRICT
);

CREATE INDEX IF NOT EXISTS ix_stock_count_lines_stock_count_id
    ON stock_count_lines(stock_count_id);

CREATE INDEX IF NOT EXISTS ix_stock_count_lines_variant_id
    ON stock_count_lines(variant_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_stock_count_lines_stock_count_variant
    ON stock_count_lines(stock_count_id, variant_id);

-- Baseline permissions for common owner/admin roles.
UPDATE roles
SET permissions = permissions
    || '["StockCount.ViewOwn","StockCount.ViewAll","StockCount.Create","StockCount.Download","StockCount.Print"]'::jsonb,
    updated_at = NOW()
WHERE name IN ('Admin', 'BusinessOwner', 'BusinessAdmin', 'Stock Manager', 'WarehouseManager', 'OutletManager')
  AND NOT (permissions @> '["StockCount.ViewOwn"]'::jsonb);

-- Verification query
SELECT
    name,
    permissions @> '["StockCount.ViewOwn"]'::jsonb AS has_view_own,
    permissions @> '["StockCount.ViewAll"]'::jsonb AS has_view_all,
    permissions @> '["StockCount.Create"]'::jsonb AS has_create,
    permissions @> '["StockCount.Download"]'::jsonb AS has_download,
    permissions @> '["StockCount.Print"]'::jsonb AS has_print
FROM roles
WHERE name IN ('Admin', 'BusinessOwner', 'BusinessAdmin', 'Stock Manager', 'WarehouseManager', 'OutletManager')
ORDER BY name;
