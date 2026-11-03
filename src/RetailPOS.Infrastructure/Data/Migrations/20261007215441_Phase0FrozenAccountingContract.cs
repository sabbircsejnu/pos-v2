using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase0FrozenAccountingContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounting_frozen_system_journal_operations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    posting_reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    canonical_payload_json = table.Column<string>(type: "text", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    journal_id = table.Column<long>(type: "bigint", nullable: false),
                    original_journal_id = table.Column<long>(type: "bigint", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_accounting_frozen_system_journal_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_frozen_journal_original",
                        column: x => x.original_journal_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_frozen_journal_result",
                        column: x => x.journal_id,
                        principalTable: "journal_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_frozen_system_journal_operations_business_id_pos~",
                table: "accounting_frozen_system_journal_operations",
                columns: new[] { "business_id", "posting_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_frozen_system_journal_operations_journal_id",
                table: "accounting_frozen_system_journal_operations",
                column: "journal_id");

            migrationBuilder.CreateIndex(
                name: "i_x_accounting_frozen_system_journal_operations_original_journa~",
                table: "accounting_frozen_system_journal_operations",
                column: "original_journal_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounting_frozen_system_journal_operations");
        }
    }
}
