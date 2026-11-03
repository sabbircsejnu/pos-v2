using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6HPayrollSettlementPosting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_payment_status",
                table: "payroll_payments");

            migrationBuilder.DropIndex(
                name: "ux_payroll_accounting_posting_run_revision",
                table: "payroll_accounting_postings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_accounting_posting_type",
                table: "payroll_accounting_postings");

            migrationBuilder.AddColumn<long>(
                name: "payment_batch_id",
                table: "payroll_accounting_postings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "payroll_payment_allocation_id",
                table: "payroll_accounting_postings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "payroll_payment_evidence_id",
                table: "payroll_accounting_postings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "payroll_payment_id",
                table: "payroll_accounting_postings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "payroll_payment_reconciliation_id",
                table: "payroll_accounting_postings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_payment_status",
                table: "payroll_payments",
                sql: "status IN ('Pending', 'Ready', 'Processing', 'PartiallyPaid', 'Paid', 'Held', 'Failed', 'Returned', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_accounting_postings_business_id_payment_batch_id",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payment_batch_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_accounting_postings_business_id_payroll_payment_all~",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_payment_allocation_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_accounting_postings_business_id_payroll_payment_id",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_payment_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_accounting_postings_business_id_payroll_payment_rec~",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_payment_reconciliation_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_accounting_posting_run_revision",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_run_id", "run_revision", "posting_type" },
                unique: true,
                filter: "posting_type = 'PayrollExpense'");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_accounting_settlement_identity",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_payment_evidence_id", "payroll_payment_allocation_id" },
                unique: true,
                filter: "posting_type = 'SalaryPayment'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_accounting_posting_settlement_identity",
                table: "payroll_accounting_postings",
                sql: "(posting_type = 'PayrollExpense' AND payroll_payment_id IS NULL AND payroll_payment_allocation_id IS NULL AND payroll_payment_evidence_id IS NULL AND payroll_payment_reconciliation_id IS NULL) OR (posting_type = 'SalaryPayment' AND payroll_payment_id IS NOT NULL AND payroll_payment_allocation_id IS NOT NULL AND payroll_payment_evidence_id IS NOT NULL AND (status <> 'Posted' OR payroll_payment_reconciliation_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_accounting_posting_type",
                table: "payroll_accounting_postings",
                sql: "posting_type IN ('PayrollExpense', 'SalaryPayment')");

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_accounting_postings_payroll_payment_allocations_bus~",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_payment_allocation_id" },
                principalTable: "payroll_payment_allocations",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_accounting_postings_payroll_payment_batches_busines~",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payment_batch_id" },
                principalTable: "payroll_payment_batches",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_accounting_postings_payroll_payment_evidence_busine~",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_payment_evidence_id" },
                principalTable: "payroll_payment_evidence",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_accounting_postings_payroll_payment_reconciliations~",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_payment_reconciliation_id" },
                principalTable: "payroll_payment_reconciliations",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_accounting_postings_payroll_payments_business_id_pa~",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_payment_id" },
                principalTable: "payroll_payments",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_accounting_postings_payroll_payment_allocations_bus~",
                table: "payroll_accounting_postings");

            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_accounting_postings_payroll_payment_batches_busines~",
                table: "payroll_accounting_postings");

            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_accounting_postings_payroll_payment_evidence_busine~",
                table: "payroll_accounting_postings");

            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_accounting_postings_payroll_payment_reconciliations~",
                table: "payroll_accounting_postings");

            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_accounting_postings_payroll_payments_business_id_pa~",
                table: "payroll_accounting_postings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_payment_status",
                table: "payroll_payments");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_accounting_postings_business_id_payment_batch_id",
                table: "payroll_accounting_postings");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_accounting_postings_business_id_payroll_payment_all~",
                table: "payroll_accounting_postings");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_accounting_postings_business_id_payroll_payment_id",
                table: "payroll_accounting_postings");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_accounting_postings_business_id_payroll_payment_rec~",
                table: "payroll_accounting_postings");

            migrationBuilder.DropIndex(
                name: "ux_payroll_accounting_posting_run_revision",
                table: "payroll_accounting_postings");

            migrationBuilder.DropIndex(
                name: "ux_payroll_accounting_settlement_identity",
                table: "payroll_accounting_postings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_accounting_posting_settlement_identity",
                table: "payroll_accounting_postings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_accounting_posting_type",
                table: "payroll_accounting_postings");

            migrationBuilder.DropColumn(
                name: "payment_batch_id",
                table: "payroll_accounting_postings");

            migrationBuilder.DropColumn(
                name: "payroll_payment_allocation_id",
                table: "payroll_accounting_postings");

            migrationBuilder.DropColumn(
                name: "payroll_payment_evidence_id",
                table: "payroll_accounting_postings");

            migrationBuilder.DropColumn(
                name: "payroll_payment_id",
                table: "payroll_accounting_postings");

            migrationBuilder.DropColumn(
                name: "payroll_payment_reconciliation_id",
                table: "payroll_accounting_postings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_payment_status",
                table: "payroll_payments",
                sql: "status IN ('Pending', 'Ready', 'Processing', 'Paid', 'Held', 'Failed', 'Returned', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_accounting_posting_run_revision",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_run_id", "run_revision", "posting_type" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_accounting_posting_type",
                table: "payroll_accounting_postings",
                sql: "posting_type = 'PayrollExpense'");
        }
    }
}
