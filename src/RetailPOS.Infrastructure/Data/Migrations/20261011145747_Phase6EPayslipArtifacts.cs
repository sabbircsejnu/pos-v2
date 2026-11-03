using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6EPayslipArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_artifact_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_run_id = table.Column<long>(type: "bigint", nullable: false),
                    run_revision = table.Column<int>(type: "integer", nullable: false),
                    payroll_employee_result_id = table.Column<long>(type: "bigint", nullable: false),
                    artifact_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    payroll_snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payment_info_snapshot_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    template_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    format = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    encryption_key_reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    classification = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    scan_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    retain_until_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    replaces_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_artifact_versions", x => x.id);
                    table.UniqueConstraint("a_k_payroll_artifact_versions_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_artifact_pdf", "artifact_type = 'Payslip' AND format = 'PDF' AND content_type = 'application/pdf'");
                    table.CheckConstraint("ck_payroll_artifact_scan_status", "scan_status IN ('Clean', 'Rejected', 'Error')");
                    table.CheckConstraint("ck_payroll_artifact_sha256", "length(payroll_snapshot_hash) = 64 AND length(payment_info_snapshot_hash) = 64 AND length(content_hash) = 64");
                    table.CheckConstraint("ck_payroll_artifact_version_values", "version_no > 0 AND run_revision > 0 AND byte_length > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_artifact_versions_payroll_artifact_versions_busines~",
                        columns: x => new { x.business_id, x.replaces_version_id },
                        principalTable: "payroll_artifact_versions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_artifact_versions_payroll_employee_results_business~",
                        columns: x => new { x.business_id, x.payroll_employee_result_id },
                        principalTable: "payroll_employee_results",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_artifact_versions_payroll_runs_business_id_payroll_~",
                        columns: x => new { x.business_id, x.payroll_run_id },
                        principalTable: "payroll_runs",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_payslip_deliveries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    artifact_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_snapshot = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_payslip_deliveries", x => x.id);
                    table.CheckConstraint("ck_payroll_payslip_delivery_status", "status IN ('Pending', 'Sent', 'Failed')");
                    table.ForeignKey(
                        name: "f_k_payroll_payslip_deliveries_payroll_artifact_versions_busine~",
                        columns: x => new { x.business_id, x.artifact_version_id },
                        principalTable: "payroll_artifact_versions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_artifact_versions_business_id_payroll_employee_resu~",
                table: "payroll_artifact_versions",
                columns: new[] { "business_id", "payroll_employee_result_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_artifact_versions_business_id_replaces_version_id",
                table: "payroll_artifact_versions",
                columns: new[] { "business_id", "replaces_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_artifact_generation_identity",
                table: "payroll_artifact_versions",
                columns: new[] { "business_id", "payroll_run_id", "run_revision", "payroll_employee_result_id", "artifact_type", "payroll_snapshot_hash", "payment_info_snapshot_hash", "template_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_artifact_version_no",
                table: "payroll_artifact_versions",
                columns: new[] { "business_id", "payroll_run_id", "payroll_employee_result_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_payslip_delivery_artifact",
                table: "payroll_payslip_deliveries",
                columns: new[] { "business_id", "artifact_version_id", "requested_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payslip_delivery_idempotency",
                table: "payroll_payslip_deliveries",
                columns: new[] { "business_id", "requested_by_user_id", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_payslip_deliveries");

            migrationBuilder.DropTable(
                name: "payroll_artifact_versions");
        }
    }
}
