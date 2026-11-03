using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6ICorrectionAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_payroll_accounting_posting_run_revision",
                table: "payroll_accounting_postings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_accounting_posting_settlement_identity",
                table: "payroll_accounting_postings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_accounting_posting_type",
                table: "payroll_accounting_postings");

            migrationBuilder.AddColumn<string>(
                name: "correction_policy_version",
                table: "payroll_accounting_postings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_reference",
                table: "payroll_accounting_postings",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "original_posting_id",
                table: "payroll_accounting_postings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reversal_journal_id",
                table: "payroll_accounting_postings",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_accounting_postings_reversal_journal_id",
                table: "payroll_accounting_postings",
                column: "reversal_journal_id");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_accounting_correction_original_posting",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "original_posting_id" },
                unique: true,
                filter: "posting_type = 'PayrollCorrection'");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_accounting_posting_run_revision",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_run_id", "run_revision", "posting_type" },
                unique: true,
                filter: "posting_type IN ('PayrollExpense', 'PayrollCorrection')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_accounting_posting_settlement_identity",
                table: "payroll_accounting_postings",
                sql: "(posting_type = 'PayrollExpense' AND payroll_payment_id IS NULL AND payroll_payment_allocation_id IS NULL AND payroll_payment_evidence_id IS NULL AND payroll_payment_reconciliation_id IS NULL) OR (posting_type = 'SalaryPayment' AND payroll_payment_id IS NOT NULL AND payroll_payment_allocation_id IS NOT NULL AND payroll_payment_evidence_id IS NOT NULL AND (status <> 'Posted' OR payroll_payment_reconciliation_id IS NOT NULL)) OR (posting_type = 'PayrollCorrection' AND payroll_payment_id IS NULL AND payroll_payment_allocation_id IS NULL AND payroll_payment_evidence_id IS NULL AND payroll_payment_reconciliation_id IS NULL AND correction_reference IS NOT NULL AND original_posting_id IS NOT NULL AND correction_policy_version IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_accounting_posting_type",
                table: "payroll_accounting_postings",
                sql: "posting_type IN ('PayrollExpense', 'SalaryPayment', 'PayrollCorrection')");

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_accounting_postings__journal_entries_reversal_journal~",
                table: "payroll_accounting_postings",
                column: "reversal_journal_id",
                principalTable: "journal_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_accounting_postings_payroll_accounting_postings_bus~",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "original_posting_id" },
                principalTable: "payroll_accounting_postings",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_accounting_postings__journal_entries_reversal_journal~",
                table: "payroll_accounting_postings");

            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_accounting_postings_payroll_accounting_postings_bus~",
                table: "payroll_accounting_postings");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_accounting_postings_reversal_journal_id",
                table: "payroll_accounting_postings");

            migrationBuilder.DropIndex(
                name: "ux_payroll_accounting_correction_original_posting",
                table: "payroll_accounting_postings");

            migrationBuilder.DropIndex(
                name: "ux_payroll_accounting_posting_run_revision",
                table: "payroll_accounting_postings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_accounting_posting_settlement_identity",
                table: "payroll_accounting_postings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_accounting_posting_type",
                table: "payroll_accounting_postings");

            migrationBuilder.DropColumn(
                name: "correction_policy_version",
                table: "payroll_accounting_postings");

            migrationBuilder.DropColumn(
                name: "correction_reference",
                table: "payroll_accounting_postings");

            migrationBuilder.DropColumn(
                name: "original_posting_id",
                table: "payroll_accounting_postings");

            migrationBuilder.DropColumn(
                name: "reversal_journal_id",
                table: "payroll_accounting_postings");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_accounting_posting_run_revision",
                table: "payroll_accounting_postings",
                columns: new[] { "business_id", "payroll_run_id", "run_revision", "posting_type" },
                unique: true,
                filter: "posting_type = 'PayrollExpense'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_accounting_posting_settlement_identity",
                table: "payroll_accounting_postings",
                sql: "(posting_type = 'PayrollExpense' AND payroll_payment_id IS NULL AND payroll_payment_allocation_id IS NULL AND payroll_payment_evidence_id IS NULL AND payroll_payment_reconciliation_id IS NULL) OR (posting_type = 'SalaryPayment' AND payroll_payment_id IS NOT NULL AND payroll_payment_allocation_id IS NOT NULL AND payroll_payment_evidence_id IS NOT NULL AND (status <> 'Posted' OR payroll_payment_reconciliation_id IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_accounting_posting_type",
                table: "payroll_accounting_postings",
                sql: "posting_type IN ('PayrollExpense', 'SalaryPayment')");
        }
    }
}
