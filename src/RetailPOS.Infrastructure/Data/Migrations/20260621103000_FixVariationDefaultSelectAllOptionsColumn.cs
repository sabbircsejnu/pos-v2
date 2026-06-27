using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RetailPOS.Infrastructure.Data;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(RetailPOSDbContext))]
    [Migration("20260621103000_FixVariationDefaultSelectAllOptionsColumn")]
    public partial class FixVariationDefaultSelectAllOptionsColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keep existing data safe:
            // 1) If old column exists and new one doesn't -> rename (data preserved)
            // 2) If neither exists -> add new column with default false
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'variations'
          AND column_name = 'auto_select_all_options'
    )
    AND NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'variations'
          AND column_name = 'default_select_all_options'
    ) THEN
        ALTER TABLE variations RENAME COLUMN auto_select_all_options TO default_select_all_options;
    ELSIF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'variations'
          AND column_name = 'default_select_all_options'
    ) THEN
        ALTER TABLE variations ADD COLUMN default_select_all_options boolean NOT NULL DEFAULT false;
    END IF;
END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert safely only when the expected state exists.
            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'variations'
          AND column_name = 'default_select_all_options'
    )
    AND NOT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name = 'variations'
          AND column_name = 'auto_select_all_options'
    ) THEN
        ALTER TABLE variations RENAME COLUMN default_select_all_options TO auto_select_all_options;
    END IF;
END $$;");
        }
    }
}
