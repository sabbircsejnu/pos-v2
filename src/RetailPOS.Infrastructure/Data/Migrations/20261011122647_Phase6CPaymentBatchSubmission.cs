using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6CPaymentBatchSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "submission_external_reference",
                table: "payroll_payment_batches",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "submission_idempotency_key",
                table: "payroll_payment_batches",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "submission_payload_hash",
                table: "payroll_payment_batches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "submission_reason",
                table: "payroll_payment_batches",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "submitted_by_user_id",
                table: "payroll_payment_batches",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_batch_submission_idempotency",
                table: "payroll_payment_batches",
                columns: new[] { "business_id", "submission_idempotency_key" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_payment_batch_submission_hash",
                table: "payroll_payment_batches",
                sql: "(submission_idempotency_key IS NULL AND submission_payload_hash IS NULL) OR (submission_idempotency_key IS NOT NULL AND length(submission_payload_hash) = 64)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_payroll_payment_batch_submission_idempotency",
                table: "payroll_payment_batches");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_payment_batch_submission_hash",
                table: "payroll_payment_batches");

            migrationBuilder.DropColumn(
                name: "submission_external_reference",
                table: "payroll_payment_batches");

            migrationBuilder.DropColumn(
                name: "submission_idempotency_key",
                table: "payroll_payment_batches");

            migrationBuilder.DropColumn(
                name: "submission_payload_hash",
                table: "payroll_payment_batches");

            migrationBuilder.DropColumn(
                name: "submission_reason",
                table: "payroll_payment_batches");

            migrationBuilder.DropColumn(
                name: "submitted_by_user_id",
                table: "payroll_payment_batches");
        }
    }
}
