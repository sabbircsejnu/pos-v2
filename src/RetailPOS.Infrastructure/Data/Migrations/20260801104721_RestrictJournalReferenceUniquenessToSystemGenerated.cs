using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestrictJournalReferenceUniquenessToSystemGenerated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_journal_entries_business_id_journal_type_reference_type_ref~",
                table: "journal_entries");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_business_id_journal_type_reference_type_ref~",
                table: "journal_entries",
                columns: new[] { "business_id", "journal_type", "reference_type", "reference_id" },
                unique: true,
                filter: "is_system_generated = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_journal_entries_business_id_journal_type_reference_type_ref~",
                table: "journal_entries");

            migrationBuilder.CreateIndex(
                name: "IX_journal_entries_business_id_journal_type_reference_type_ref~",
                table: "journal_entries",
                columns: new[] { "business_id", "journal_type", "reference_type", "reference_id" },
                unique: true);
        }
    }
}
