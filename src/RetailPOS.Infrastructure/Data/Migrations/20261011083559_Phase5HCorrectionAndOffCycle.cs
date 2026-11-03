using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5HCorrectionAndOffCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_payroll_run_period_type_scope",
                table: "payroll_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_run_regular_only",
                table: "payroll_runs");

            migrationBuilder.AddColumn<bool>(
                name: "accounting_correction_required",
                table: "payroll_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "affected_employee_ids_snapshot_json",
                table: "payroll_runs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_reason",
                table: "payroll_runs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_reference",
                table: "payroll_runs",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_type",
                table: "payroll_runs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "downstream_reversal_required",
                table: "payroll_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "original_calculation_version",
                table: "payroll_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "original_evidence_hash",
                table: "payroll_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "original_payroll_run_id",
                table: "payroll_runs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "payment_correction_required",
                table: "payroll_runs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "run_reference",
                table: "payroll_runs",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "payroll_period_reopen_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    payroll_period_id = table.Column<long>(type: "bigint", nullable: false),
                    previous_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    previous_period_version = table.Column<long>(type: "bigint", nullable: false),
                    result_period_version = table.Column<long>(type: "bigint", nullable: false),
                    original_payroll_run_id = table.Column<long>(type: "bigint", nullable: true),
                    correction_payroll_run_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_period_reopen_events", x => x.id);
                    table.CheckConstraint("ck_payroll_period_reopen_event_purpose", "purpose = 'Correction' AND original_payroll_run_id IS NOT NULL AND correction_payroll_run_id IS NOT NULL");
                    table.CheckConstraint("ck_payroll_period_reopen_event_versions", "previous_period_version > 0 AND result_period_version = previous_period_version + 1");
                    table.ForeignKey(
                        name: "f_k_payroll_period_reopen_events_payroll_periods_business_id_pa~",
                        columns: x => new { x.business_id, x.payroll_period_id },
                        principalTable: "payroll_periods",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_period_reopen_events_payroll_runs_business_id_corre~",
                        columns: x => new { x.business_id, x.correction_payroll_run_id },
                        principalTable: "payroll_runs",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_period_reopen_events_payroll_runs_business_id_origi~",
                        columns: x => new { x.business_id, x.original_payroll_run_id },
                        principalTable: "payroll_runs",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_runs_business_id_original_payroll_run_id",
                table: "payroll_runs",
                columns: new[] { "business_id", "original_payroll_run_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_run_business_correction_reference",
                table: "payroll_runs",
                columns: new[] { "business_id", "correction_reference" },
                unique: true,
                filter: "correction_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_run_business_offcycle_reference",
                table: "payroll_runs",
                columns: new[] { "business_id", "run_reference" },
                unique: true,
                filter: "run_type = 'OffCycle' AND run_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_run_period_type_scope",
                table: "payroll_runs",
                columns: new[] { "payroll_period_id", "run_type", "scope_key" },
                unique: true,
                filter: "run_type = 'Regular'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_run_type",
                table: "payroll_runs",
                sql: "run_type IN ('Regular', 'Correction', 'OffCycle')");

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_period_reopen_events_business_id_correction_payroll~",
                table: "payroll_period_reopen_events",
                columns: new[] { "business_id", "correction_payroll_run_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_period_reopen_events_business_id_original_payroll_r~",
                table: "payroll_period_reopen_events",
                columns: new[] { "business_id", "original_payroll_run_id" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_period_reopen_event_version",
                table: "payroll_period_reopen_events",
                columns: new[] { "business_id", "payroll_period_id", "result_period_version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "f_k_payroll_runs_payroll_runs_business_id_original_payroll_run_~",
                table: "payroll_runs",
                columns: new[] { "business_id", "original_payroll_run_id" },
                principalTable: "payroll_runs",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_payroll_runs_payroll_runs_business_id_original_payroll_run_~",
                table: "payroll_runs");

            migrationBuilder.DropTable(
                name: "payroll_period_reopen_events");

            migrationBuilder.DropIndex(
                name: "i_x_payroll_runs_business_id_original_payroll_run_id",
                table: "payroll_runs");

            migrationBuilder.DropIndex(
                name: "ux_payroll_run_business_correction_reference",
                table: "payroll_runs");

            migrationBuilder.DropIndex(
                name: "ux_payroll_run_business_offcycle_reference",
                table: "payroll_runs");

            migrationBuilder.DropIndex(
                name: "ux_payroll_run_period_type_scope",
                table: "payroll_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_run_type",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "accounting_correction_required",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "affected_employee_ids_snapshot_json",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "correction_reason",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "correction_reference",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "correction_type",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "downstream_reversal_required",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "original_calculation_version",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "original_evidence_hash",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "original_payroll_run_id",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "payment_correction_required",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "run_reference",
                table: "payroll_runs");

            migrationBuilder.CreateIndex(
                name: "ux_payroll_run_period_type_scope",
                table: "payroll_runs",
                columns: new[] { "payroll_period_id", "run_type", "scope_key" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_run_regular_only",
                table: "payroll_runs",
                sql: "run_type = 'Regular'");
        }
    }
}
