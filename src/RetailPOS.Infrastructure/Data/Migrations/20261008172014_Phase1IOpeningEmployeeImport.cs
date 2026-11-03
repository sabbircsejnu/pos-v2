using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1IOpeningEmployeeImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_import_batches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    import_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    template_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    file_storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    encryption_key_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_dry_run = table.Column<bool>(type: "boolean", nullable: false),
                    total_rows = table.Column<int>(type: "integer", nullable: false),
                    valid_rows = table.Column<int>(type: "integer", nullable: false),
                    invalid_rows = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    executed_by = table.Column<long>(type: "bigint", nullable: true),
                    executed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    retain_until_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_import_batches", x => x.id);
                    table.UniqueConstraint("a_k_hr_import_batches_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_import_batch_counts", "total_rows >= 0 AND valid_rows >= 0 AND invalid_rows >= 0 AND total_rows = valid_rows + invalid_rows");
                    table.CheckConstraint("ck_hr_import_batch_status", "status IN ('Uploaded', 'Ready', 'ValidationFailed', 'Processing', 'Completed', 'Failed')");
                    table.CheckConstraint("ck_hr_import_batch_type", "import_type IN ('Employee', 'Assignment')");
                    table.CheckConstraint("ck_hr_import_batch_version", "version > 0 AND revision >= 0");
                    table.ForeignKey(
                        name: "f_k_hr_import_batches_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_import_rows",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    import_batch_id = table.Column<long>(type: "bigint", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    row_no = table.Column<int>(type: "integer", nullable: false),
                    external_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    raw_json = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    validation_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error_json = table.Column<string>(type: "text", nullable: false),
                    created_entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_entity_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_import_rows", x => x.id);
                    table.CheckConstraint("ck_hr_import_row_number", "row_no > 0 AND revision > 0");
                    table.CheckConstraint("ck_hr_import_row_status", "validation_status IN ('Valid', 'Invalid', 'Completed')");
                    table.ForeignKey(
                        name: "f_k_hr_import_rows_hr_import_batches_business_id_import_batch_id",
                        columns: x => new { x.business_id, x.import_batch_id },
                        principalTable: "hr_import_batches",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_import_batches_business_id_idempotency_key",
                table: "hr_import_batches",
                columns: new[] { "business_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_import_rows_business_id_import_batch_id_revision_row_no",
                table: "hr_import_rows",
                columns: new[] { "business_id", "import_batch_id", "revision", "row_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_import_rows");

            migrationBuilder.DropTable(
                name: "hr_import_batches");
        }
    }
}
