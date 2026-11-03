using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6DPaymentReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_payment_reconciliations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_payment_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_payment_allocation_id = table.Column<long>(type: "bigint", nullable: false),
                    payment_batch_id = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    payment_version = table.Column<long>(type: "bigint", nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    settled_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    difference = table.Column<decimal>(type: "numeric", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    settlement_date = table.Column<DateOnly>(type: "date", nullable: true),
                    evidence_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reconciled_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    reconciled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_payment_reconciliations", x => x.id);
                    table.UniqueConstraint("a_k_payroll_payment_reconciliations_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_payment_reconciliation_amounts", "version > 0 AND payment_version > 0 AND expected_amount > 0 AND settled_amount >= 0 AND settled_amount <= expected_amount AND difference = expected_amount - settled_amount");
                    table.CheckConstraint("ck_payroll_payment_reconciliation_hash", "length(payload_hash) = 64");
                    table.CheckConstraint("ck_payroll_payment_reconciliation_source", "evidence_source IN ('Cash', 'Bank', 'Mobile')");
                    table.CheckConstraint("ck_payroll_payment_reconciliation_status", "status IN ('Unreconciled', 'PartiallyReconciled', 'Reconciled', 'Exception')");
                    table.ForeignKey(
                        name: "f_k_payroll_payment_reconciliations_payroll_payment_allocations~",
                        columns: x => new { x.business_id, x.payroll_payment_allocation_id },
                        principalTable: "payroll_payment_allocations",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_payment_reconciliations_payroll_payment_batches_bus~",
                        columns: x => new { x.business_id, x.payment_batch_id },
                        principalTable: "payroll_payment_batches",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_payment_reconciliations_payroll_payments_business_i~",
                        columns: x => new { x.business_id, x.payroll_payment_id },
                        principalTable: "payroll_payments",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_payment_reconciliations_business_id_payroll_payment~",
                table: "payroll_payment_reconciliations",
                columns: new[] { "business_id", "payroll_payment_allocation_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_payment_reconciliation_batch",
                table: "payroll_payment_reconciliations",
                columns: new[] { "business_id", "payment_batch_id", "reconciled_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_reconciliation_idempotency",
                table: "payroll_payment_reconciliations",
                columns: new[] { "business_id", "idempotency_key", "payroll_payment_id", "payroll_payment_allocation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_reconciliation_reference",
                table: "payroll_payment_reconciliations",
                columns: new[] { "business_id", "payroll_payment_id", "payroll_payment_allocation_id", "external_reference" },
                unique: true,
                filter: "external_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_reconciliation_version",
                table: "payroll_payment_reconciliations",
                columns: new[] { "business_id", "payroll_payment_id", "payroll_payment_allocation_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_payment_reconciliations");
        }
    }
}
