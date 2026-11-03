using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6APayrollPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_payments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_period_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_run_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_employee_result_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    payable_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    outstanding_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    idempotency_payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    original_payment_id = table.Column<long>(type: "bigint", nullable: true),
                    attempt_no = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_payments", x => x.id);
                    table.UniqueConstraint("a_k_payroll_payments_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_payment_amounts", "payable_amount > 0 AND paid_amount >= 0 AND outstanding_amount >= 0 AND paid_amount + outstanding_amount = payable_amount");
                    table.CheckConstraint("ck_payroll_payment_hash", "length(idempotency_payload_hash) = 64");
                    table.CheckConstraint("ck_payroll_payment_method", "method IN ('Cash', 'Bank', 'Mobile', 'Mixed')");
                    table.CheckConstraint("ck_payroll_payment_status", "status IN ('Pending', 'Ready', 'Processing', 'Paid', 'Held', 'Failed', 'Returned', 'Cancelled')");
                    table.CheckConstraint("ck_payroll_payment_version_attempt", "version > 0 AND attempt_no > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_payments_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_payments_payroll_employee_results_business_id_payro~",
                        columns: x => new { x.business_id, x.payroll_employee_result_id },
                        principalTable: "payroll_employee_results",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_payments_payroll_payments_business_id_original_paym~",
                        columns: x => new { x.business_id, x.original_payment_id },
                        principalTable: "payroll_payments",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_payments_payroll_periods_business_id_payroll_period~",
                        columns: x => new { x.business_id, x.payroll_period_id },
                        principalTable: "payroll_periods",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_payments_payroll_runs_business_id_payroll_run_id",
                        columns: x => new { x.business_id, x.payroll_run_id },
                        principalTable: "payroll_runs",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_payment_allocations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_payment_id = table.Column<long>(type: "bigint", nullable: false),
                    allocation_no = table.Column<int>(type: "integer", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    settled_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    external_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    payment_destination_id = table.Column<long>(type: "bigint", nullable: true),
                    payment_destination_version_no = table.Column<int>(type: "integer", nullable: true),
                    bank_name_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    account_name_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    account_number_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    mobile_provider_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    mobile_account_name_snapshot = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    mobile_account_number_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_payment_allocations", x => x.id);
                    table.UniqueConstraint("a_k_payroll_payment_allocations_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_payment_allocation_amount", "allocation_no > 0 AND amount > 0 AND settled_amount >= 0 AND settled_amount <= amount");
                    table.CheckConstraint("ck_payroll_payment_allocation_destination", "(method = 'Cash' AND payment_destination_id IS NULL AND payment_destination_version_no IS NULL AND bank_name_snapshot IS NULL AND account_name_snapshot IS NULL AND account_number_snapshot IS NULL AND mobile_provider_snapshot IS NULL AND mobile_account_name_snapshot IS NULL AND mobile_account_number_snapshot IS NULL) OR (method = 'Bank' AND payment_destination_id IS NOT NULL AND payment_destination_version_no IS NOT NULL AND bank_name_snapshot IS NOT NULL AND account_name_snapshot IS NOT NULL AND account_number_snapshot IS NOT NULL AND mobile_provider_snapshot IS NULL AND mobile_account_name_snapshot IS NULL AND mobile_account_number_snapshot IS NULL) OR (method = 'Mobile' AND payment_destination_id IS NOT NULL AND payment_destination_version_no IS NOT NULL AND mobile_provider_snapshot IS NOT NULL AND mobile_account_name_snapshot IS NOT NULL AND mobile_account_number_snapshot IS NOT NULL AND bank_name_snapshot IS NULL AND account_name_snapshot IS NULL AND account_number_snapshot IS NULL)");
                    table.CheckConstraint("ck_payroll_payment_allocation_method", "method IN ('Cash', 'Bank', 'Mobile')");
                    table.CheckConstraint("ck_payroll_payment_allocation_state", "state IN ('Pending')");
                    table.ForeignKey(
                        name: "f_k_payroll_payment_allocations_payroll_employee_payment_destin~",
                        columns: x => new { x.business_id, x.payment_destination_id },
                        principalTable: "payroll_employee_payment_destinations",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_payment_allocations_payroll_payments_business_id_pa~",
                        columns: x => new { x.business_id, x.payroll_payment_id },
                        principalTable: "payroll_payments",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_payment_allocations_business_id_payment_destination~",
                table: "payroll_payment_allocations",
                columns: new[] { "business_id", "payment_destination_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_allocation_identity",
                table: "payroll_payment_allocations",
                columns: new[] { "business_id", "payroll_payment_id", "allocation_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_payments_business_id_original_payment_id",
                table: "payroll_payments",
                columns: new[] { "business_id", "original_payment_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_payments_business_id_payroll_period_id",
                table: "payroll_payments",
                columns: new[] { "business_id", "payroll_period_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_payment_run_employee_status",
                table: "payroll_payments",
                columns: new[] { "business_id", "payroll_run_id", "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_idempotency",
                table: "payroll_payments",
                columns: new[] { "business_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_result_attempt",
                table: "payroll_payments",
                columns: new[] { "business_id", "payroll_employee_result_id", "attempt_no" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_payment_allocations");

            migrationBuilder.DropTable(
                name: "payroll_payments");
        }
    }
}
