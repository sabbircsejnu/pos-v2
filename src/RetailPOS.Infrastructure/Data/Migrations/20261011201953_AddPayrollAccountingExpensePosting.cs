using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollAccountingExpensePosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "a_k_chart_accounts_business_id_id",
                table: "chart_accounts",
                columns: new[] { "business_id", "id" });

            migrationBuilder.CreateTable(
                name: "payroll_accounting_component_mappings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    component_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    component_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    account_id = table.Column<long>(type: "bigint", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    updated_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_accounting_component_mappings", x => x.id);
                    table.CheckConstraint("ck_payroll_accounting_mapping_code", "(component_type = 'NetPayable' AND component_code = 'NET_PAYABLE') OR (component_type IN ('Earning', 'Deduction') AND length(component_code) > 0)");
                    table.CheckConstraint("ck_payroll_accounting_mapping_component_type", "component_type IN ('Earning', 'Deduction', 'NetPayable')");
                    table.CheckConstraint("ck_payroll_accounting_mapping_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_accounting_component_mappings_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_accounting_component_mappings_chart_accounts_busine~",
                        columns: x => new { x.business_id, x.account_id },
                        principalTable: "chart_accounts",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_accounting_postings",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_period_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_run_id = table.Column<long>(type: "bigint", nullable: false),
                    run_revision = table.Column<int>(type: "integer", nullable: false),
                    posting_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    posting_reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    request_payload_json = table.Column<string>(type: "text", nullable: true),
                    request_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    conflict_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    accounting_journal_id = table.Column<long>(type: "bigint", nullable: true),
                    accounting_reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    posted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_accounting_postings", x => x.id);
                    table.UniqueConstraint("a_k_payroll_accounting_postings_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_accounting_posting_attempts", "attempt_count >= 0");
                    table.CheckConstraint("ck_payroll_accounting_posting_revision", "run_revision > 0");
                    table.CheckConstraint("ck_payroll_accounting_posting_status", "status IN ('Pending', 'Processing', 'Posted', 'Failed', 'Conflict')");
                    table.CheckConstraint("ck_payroll_accounting_posting_type", "posting_type = 'PayrollExpense'");
                    table.CheckConstraint("ck_payroll_accounting_posting_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_accounting_postings_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_accounting_postings_payroll_periods_business_id_pay~",
                        columns: x => new { x.business_id, x.payroll_period_id },
                        principalTable: "payroll_periods",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_accounting_postings_payroll_runs_business_id_payrol~",
                        columns: x => new { x.business_id, x.payroll_run_id },
                        principalTable: "payroll_runs",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_accounting_component_mappings_business_id_account_id",
                table: "payroll_accounting_component_mappings",
                columns: new[] { "business_id", "account_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_accounting_component_mapping",
                table: "payroll_accounting_component_mappings",
                columns: new[] { "business_id", "component_type", "component_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_accounting_postings_business_id_payroll_period_id",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_period_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_accounting_posting_reference",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "posting_reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_accounting_posting_run_revision",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_run_id", "run_revision", "posting_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_accounting_component_mappings");

            migrationBuilder.DropTable(
                name: "payroll_accounting_postings");

            migrationBuilder.DropUniqueConstraint(
                name: "a_k_chart_accounts_business_id_id",
                table: "chart_accounts");
        }
    }
}
