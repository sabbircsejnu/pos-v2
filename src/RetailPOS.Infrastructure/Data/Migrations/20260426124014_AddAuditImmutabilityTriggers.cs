using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditImmutabilityTriggers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION audit_block_modify() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'audit table % is append-only; UPDATE/DELETE not permitted', TG_TABLE_NAME
                        USING ERRCODE = 'P0001';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_audit_events_block_modify
                    BEFORE UPDATE OR DELETE ON audit_events
                    FOR EACH ROW EXECUTE FUNCTION audit_block_modify();

                CREATE TRIGGER trg_audit_event_entities_block_modify
                    BEFORE UPDATE OR DELETE ON audit_event_entities
                    FOR EACH ROW EXECUTE FUNCTION audit_block_modify();

                CREATE TRIGGER trg_audit_event_field_changes_block_modify
                    BEFORE UPDATE OR DELETE ON audit_event_field_changes
                    FOR EACH ROW EXECUTE FUNCTION audit_block_modify();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_audit_event_field_changes_block_modify ON audit_event_field_changes;
                DROP TRIGGER IF EXISTS trg_audit_event_entities_block_modify ON audit_event_entities;
                DROP TRIGGER IF EXISTS trg_audit_events_block_modify ON audit_events;
                DROP FUNCTION IF EXISTS audit_block_modify();
            ");
        }
    }
}
