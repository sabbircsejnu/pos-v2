using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6BPaymentLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_payment_allocation_state",
                table: "payroll_payment_allocations");

            migrationBuilder.AddColumn<string>(
                name: "external_reference",
                table: "payroll_payments",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "failure_return_reason",
                table: "payroll_payments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "held_at_utc",
                table: "payroll_payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "held_by_user_id",
                table: "payroll_payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hold_reason",
                table: "payroll_payments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "paid_at_utc",
                table: "payroll_payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "release_reason",
                table: "payroll_payments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "released_at_utc",
                table: "payroll_payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "released_by_user_id",
                table: "payroll_payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_evidence_reference",
                table: "payroll_payments",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_status",
                table: "payroll_payments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateTable(
                name: "payroll_payment_evidence",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_payment_id = table.Column<long>(type: "bigint", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    evidence_date = table.Column<DateOnly>(type: "date", nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    allocation_evidence_json = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    verified_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    verified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_payment_evidence", x => x.id);
                    table.UniqueConstraint("a_k_payroll_payment_evidence_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_payment_evidence_amount", "amount > 0");
                    table.CheckConstraint("ck_payroll_payment_evidence_hash", "length(payload_hash) = 64");
                    table.CheckConstraint("ck_payroll_payment_evidence_type", "type IN ('Settlement', 'Return')");
                    table.ForeignKey(
                        name: "f_k_payroll_payment_evidence_payroll_payments_business_id_payro~",
                        columns: x => new { x.business_id, x.payroll_payment_id },
                        principalTable: "payroll_payments",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_payment_resolution_status",
                table: "payroll_payments",
                sql: "resolution_status IN ('None', 'PendingInvestigation', 'ResolvedNonSettlement', 'ResolvedReturn', 'ResolvedReissue')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_payment_allocation_state",
                table: "payroll_payment_allocations",
                sql: "state IN ('Pending', 'PartiallySettled', 'Settled')");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_evidence_idempotency",
                table: "payroll_payment_evidence",
                columns: new[] { "business_id", "payroll_payment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_payment_evidence_reference",
                table: "payroll_payment_evidence",
                columns: new[] { "business_id", "payroll_payment_id", "type", "evidence_reference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_payment_evidence");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_payment_resolution_status",
                table: "payroll_payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_payment_allocation_state",
                table: "payroll_payment_allocations");

            migrationBuilder.DropColumn(
                name: "external_reference",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "failure_return_reason",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "held_at_utc",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "held_by_user_id",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "hold_reason",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "paid_at_utc",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "release_reason",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "released_at_utc",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "released_by_user_id",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "resolution_evidence_reference",
                table: "payroll_payments");

            migrationBuilder.DropColumn(
                name: "resolution_status",
                table: "payroll_payments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_payment_allocation_state",
                table: "payroll_payment_allocations",
                sql: "state IN ('Pending')");
        }
    }
}
