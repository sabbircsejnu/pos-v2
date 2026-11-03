-- Advanced search performance indexes for stock transfer list filters
-- Safe to run multiple times.

CREATE INDEX IF NOT EXISTS ix_stock_transfers_transfer_type
ON stock_transfers (transfer_type);

CREATE INDEX IF NOT EXISTS ix_stock_transfers_created_at_desc
ON stock_transfers (created_at DESC);

CREATE INDEX IF NOT EXISTS ix_stock_transfers_filter_sort
ON stock_transfers (status, transfer_type, transfer_date, created_at DESC, id DESC);

CREATE INDEX IF NOT EXISTS ix_stock_transfer_items_variant_transfer
ON stock_transfer_items (variant_id, transfer_id);
