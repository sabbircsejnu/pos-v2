using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6CPaymentBatchAndBankFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "payment_batch_id",
                table: "payroll_payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payroll_payment_batches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_run_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_period_id = table.Column<long>(type: "bigint", nullable: false),
                    batch_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    idempotency_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    bank_file_version_no = table.Column<int>(type: "integer", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    generated_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    generated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_payment_batches", x => x.id);
                    table.UniqueConstraint("a_k_payroll_payment_batches_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_payment_batch_amount_version", "expected_amount > 0 AND version > 0 AND bank_file_version_no >= 0");
                    table.CheckConstraint("ck_payroll_payment_batch_hash", "length(idempotency_payload_hash) = 64");
                    table.CheckConstraint("ck_payroll_payment_batch_method", "method IN ('Cash', 'Bank', 'Mobile', 'Mixed')");
                    table.CheckConstraint("ck_payroll_payment_batch_status", "status IN ('Draft', 'Ready', 'Processing', 'Completed', 'PartiallyFailed', 'Failed')");
                    table.ForeignKey(
                        name: "f_k_payroll_payment_batches_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_payment_batches_payroll_periods_business_id_payroll~",
                        columns: x => new { x.business_id, x.payroll_period_id },
                        principalTable: "payroll_periods",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_payment_batches_payroll_runs_business_id_payroll_ru~",
                        columns: x => new { x.business_id, x.payroll_run_id },
                        principalTable: "payroll_runs",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_payment_bank_file_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    payment_batch_id = table.Column<long>(type: "bigint", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    file_name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    row_count = table.Column<int>(type: "integer", nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    generated_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    generated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    generation_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_payment_bank_file_versions", x => x.id);
                    table.UniqueConstraint("a_k_payroll_payment_bank_file_versions_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_payment_bank_file_hashes", "length(payload_hash) = 64 AND length(content_hash) = 64");
                    table.CheckConstraint("ck_payroll_payment_bank_file_version_values", "version_no > 0 AND row_count > 0 AND expected_amount > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_payment_bank_file_versions_payroll_payment_batches_~",
                        columns: x => new { x.business_id, x.payment_batch_id },
                        principalTable: "payroll_payment_batches",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_payments_business_id_payment_batch_id",
                table: "payroll_payments",
                columns: new[] { "business_id", "payment_batch_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_bank_file_idempotency",
                table: "payroll_payment_bank_file_versions",
                columns: new[] { "business_id", "payment_batch_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_bank_file_version",
                table: "payroll_payment_bank_file_versions",
                columns: new[] { "business_id", "payment_batch_id", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_payment_batches_business_id_payroll_period_id",
                table: "payroll_payment_batches",
                columns: new[] { "business_id", "payroll_period_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_payment_batch_run_created",
                table: "payroll_payment_batches",
                columns: new[] { "business_id", "payroll_run_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_batch_idempotency",
                table: "payroll_payment_batches",
                columns: new[] { "business_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_batch_reference",
                table: "payroll_payment_batches",
                columns: new[] { "business_id", "batch_reference" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_payments_payroll_payment_batches_business_id_paymen~",
                table: "payroll_payments",
                columns: new[] { "business_id", "payment_batch_id" },
                principalTable: "payroll_payment_batches",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_payments_payroll_payment_batches_business_id_paymen~",
                table: "payroll_payments");

            migrationBuilder.DropTable(
                name: "payroll_payment_bank_file_versions");

            migrationBuilder.DropTable(
                name: "payroll_payment_batches");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_payments_business_id_payment_batch_id",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "payment_batch_id",
                table: "payroll_payments");
        }
    }
}
