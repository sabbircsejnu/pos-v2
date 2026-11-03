using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestoreAuditAndWalkInDatabaseProtections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION audit_block_modify()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION 'Audit records are immutable'
                        USING ERRCODE = 'P0001';
                END;
                $$;

                CREATE TRIGGER trg_audit_events_immutable
                    BEFORE UPDATE OR DELETE ON audit_events
                    FOR EACH ROW EXECUTE FUNCTION audit_block_modify();

                CREATE TRIGGER trg_audit_event_entities_immutable
                    BEFORE UPDATE OR DELETE ON audit_event_entities
                    FOR EACH ROW EXECUTE FUNCTION audit_block_modify();

                CREATE TRIGGER trg_audit_event_field_changes_immutable
                    BEFORE UPDATE OR DELETE ON audit_event_field_changes
                    FOR EACH ROW EXECUTE FUNCTION audit_block_modify();

                CREATE FUNCTION customers_protect_walkin()
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

                CREATE TRIGGER trg_customers_protect_walkin
                    BEFORE UPDATE OR DELETE ON customers
                    FOR EACH ROW EXECUTE FUNCTION customers_protect_walkin();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_customers_protect_walkin ON customers;
                DROP FUNCTION IF EXISTS customers_protect_walkin();

                DROP TRIGGER IF EXISTS trg_audit_event_field_changes_immutable ON audit_event_field_changes;
                DROP TRIGGER IF EXISTS trg_audit_event_entities_immutable ON audit_event_entities;
                DROP TRIGGER IF EXISTS trg_audit_events_immutable ON audit_events;
                DROP FUNCTION IF EXISTS audit_block_modify();
                """);
        }
    }
}
