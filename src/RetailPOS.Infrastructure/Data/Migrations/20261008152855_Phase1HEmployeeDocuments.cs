using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1HEmployeeDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_document_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    classification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requires_expiry_date = table.Column<bool>(type: "boolean", nullable: false),
                    retention_period_days = table.Column<int>(type: "integer", nullable: true),
                    requires_legal_hold_review = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_document_types", x => x.id);
                    table.UniqueConstraint("a_k_hr_document_types_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_document_type_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_document_type_classification", "classification IN ('Internal', 'Confidential', 'Restricted')");
                    table.CheckConstraint("ck_hr_document_type_retention_days", "retention_period_days IS NULL OR retention_period_days > 0");
                    table.CheckConstraint("ck_hr_document_type_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_document_types_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_employee_document_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_document_id = table.Column<long>(type: "bigint", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scan_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    encryption_key_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    uploaded_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    replaces_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    retain_until_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_legal_hold = table.Column<bool>(type: "boolean", nullable: false),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_employee_document_versions", x => x.id);
                    table.UniqueConstraint("a_k_hr_employee_document_versions_business_id_employee_document~", x => new { x.business_id, x.employee_document_id, x.id });
                    table.CheckConstraint("ck_hr_employee_document_version_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_employee_document_version_hash", "length(content_hash) = 64");
                    table.CheckConstraint("ck_hr_employee_document_version_no", "version_no > 0");
                    table.CheckConstraint("ck_hr_employee_document_version_scan_status", "scan_status IN ('Pending', 'Clean', 'Rejected', 'Error')");
                    table.CheckConstraint("ck_hr_employee_document_version_size", "size_bytes > 0");
                    table.CheckConstraint("ck_hr_employee_document_version_storage_key", "scan_status <> 'Clean' OR storage_key IS NOT NULL");
                    table.ForeignKey(
                        name: "f_k_hr_employee_document_versions_hr_employee_document_versions~",
                        columns: x => new { x.business_id, x.employee_document_id, x.replaces_version_id },
                        principalTable: "hr_employee_document_versions",
                        principalColumns: new[] { "business_id", "employee_document_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_employee_documents",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    document_type_id = table.Column<long>(type: "bigint", nullable: false),
                    document_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    current_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_legal_hold = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_employee_documents", x => x.id);
                    table.UniqueConstraint("a_k_hr_employee_documents_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_employee_document_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_employee_document_dates", "expiry_date IS NULL OR issue_date IS NULL OR expiry_date >= issue_date");
                    table.CheckConstraint("ck_hr_employee_document_status", "status IN ('Active', 'Expired', 'Replaced', 'Archived')");
                    table.CheckConstraint("ck_hr_employee_document_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_employee_documents_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_documents_hr_document_types_business_id_documen~",
                        columns: x => new { x.business_id, x.document_type_id },
                        principalTable: "hr_document_types",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_documents_hr_employee_document_versions_busines~",
                        columns: x => new { x.business_id, x.id, x.current_version_id },
                        principalTable: "hr_employee_document_versions",
                        principalColumns: new[] { "business_id", "employee_document_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_documents_hr_employees_business_id_employee_id",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_document_types_business_id_code",
                table: "hr_document_types",
                columns: new[] { "business_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_document_versions_business_id_employee_document~",
                table: "hr_employee_document_versions",
                columns: new[] { "business_id", "employee_document_id", "replaces_version_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_document_versions_business_id_storage_key",
                table: "hr_employee_document_versions",
                columns: new[] { "business_id", "storage_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_document_versions_employee_document_id_version_~",
                table: "hr_employee_document_versions",
                columns: new[] { "employee_document_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_documents_business_id_document_type_id",
                table: "hr_employee_documents",
                columns: new[] { "business_id", "document_type_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_documents_business_id_employee_id_status_expiry~",
                table: "hr_employee_documents",
                columns: new[] { "business_id", "employee_id", "status", "expiry_date" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_documents_business_id_id_current_version_id",
                table: "hr_employee_documents",
                columns: new[] { "business_id", "id", "current_version_id" });

            migrationBuilder.AddForeignKey(
                name: "f_k_hr_employee_document_versions_hr_employee_documents_busines~",
                table: "hr_employee_document_versions",
                columns: new[] { "business_id", "employee_document_id" },
                principalTable: "hr_employee_documents",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_hr_employee_document_versions_hr_employee_documents_busines~",
                table: "hr_employee_document_versions");

            migrationBuilder.DropTable(
                name: "hr_employee_documents");

            migrationBuilder.DropTable(
                name: "hr_document_types");

            migrationBuilder.DropTable(
                name: "hr_employee_document_versions");
        }
    }
}
