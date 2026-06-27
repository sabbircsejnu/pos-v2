-- Adds user-level inventory location access controls.
-- Scope values: assigned_only | specific_locations | all_locations

ALTER TABLE users
    ADD COLUMN IF NOT EXISTS inventory_location_access_scope VARCHAR(40) NOT NULL DEFAULT 'assigned_only';

CREATE TABLE IF NOT EXISTS user_outlet_assignments (
    id BIGSERIAL PRIMARY KEY,
    user_id BIGINT NOT NULL,
    outlet_id BIGINT NOT NULL,
    business_id BIGINT NOT NULL,
    is_primary BOOLEAN NOT NULL DEFAULT FALSE,
    is_active BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW(),
    CONSTRAINT fk_user_outlet_assignments_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT fk_user_outlet_assignments_outlet_id FOREIGN KEY (outlet_id) REFERENCES outlets (id) ON DELETE CASCADE,
    CONSTRAINT fk_user_outlet_assignments_business_id FOREIGN KEY (business_id) REFERENCES businesses (id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_user_outlet_assignments_user_outlet
    ON user_outlet_assignments(user_id, outlet_id);

CREATE INDEX IF NOT EXISTS ix_user_outlet_assignments_user_id
    ON user_outlet_assignments(user_id);

CREATE INDEX IF NOT EXISTS ix_user_outlet_assignments_outlet_id
    ON user_outlet_assignments(outlet_id);

CREATE INDEX IF NOT EXISTS ix_user_outlet_assignments_business_id
    ON user_outlet_assignments(business_id);

-- Optional backfill: Users with no explicit scope and no outlet are likely global operators.
-- Uncomment if needed for your tenancy rules.
-- UPDATE users
-- SET inventory_location_access_scope = 'all_locations'
-- WHERE inventory_location_access_scope = 'assigned_only' AND outlet_id IS NULL;
