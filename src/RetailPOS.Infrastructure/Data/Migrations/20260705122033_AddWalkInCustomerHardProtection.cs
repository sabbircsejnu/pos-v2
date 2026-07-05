using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWalkInCustomerHardProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION customers_protect_walkin() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        IF OLD.customer_code = 'WALKIN' OR OLD.is_system = true THEN
                            RAISE EXCEPTION 'Walk-in system customer cannot be deleted'
                                USING ERRCODE = 'P0001';
                        END IF;
                        RETURN OLD;
                    END IF;

                    IF TG_OP = 'UPDATE' THEN
                        IF OLD.customer_code = 'WALKIN' OR OLD.is_system = true THEN
                            IF NEW.customer_code IS DISTINCT FROM OLD.customer_code
                               OR NEW.name IS DISTINCT FROM OLD.name
                               OR NEW.phone IS DISTINCT FROM OLD.phone
                               OR NEW.email IS DISTINCT FROM OLD.email
                               OR NEW.is_system IS DISTINCT FROM OLD.is_system
                               OR NEW.is_active IS DISTINCT FROM OLD.is_active THEN
                                RAISE EXCEPTION 'Walk-in system customer cannot be edited or deactivated'
                                    USING ERRCODE = 'P0001';
                            END IF;
                        END IF;
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS trg_customers_protect_walkin ON customers;
                CREATE TRIGGER trg_customers_protect_walkin
                    BEFORE UPDATE OR DELETE ON customers
                    FOR EACH ROW EXECUTE FUNCTION customers_protect_walkin();
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS trg_customers_protect_walkin ON customers;
                DROP FUNCTION IF EXISTS customers_protect_walkin();
            ");
        }
    }
}
