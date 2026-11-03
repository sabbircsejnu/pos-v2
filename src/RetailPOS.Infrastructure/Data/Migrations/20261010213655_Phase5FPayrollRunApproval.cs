using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5FPayrollRunApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_run_status",
                table: "payroll_runs");

            migrationBuilder.AddColumn<string>(
                name: "approval_evidence_hash",
                table: "payroll_runs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "approval_instance_id",
                table: "payroll_runs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "decided_at_utc",
                table: "payroll_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "decided_by_user_id",
                table: "payroll_runs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_reason",
                table: "payroll_runs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_at_utc",
                table: "payroll_runs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "submitted_by_user_id",
                table: "payroll_runs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "submitted_target_version",
                table: "payroll_runs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_run_status",
                table: "payroll_runs",
                sql: "status IN ('Draft', 'PendingApproval', 'Approved', 'Rejected', 'PaymentInProgress', 'Paid', 'Completed', 'Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payroll_run_status",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "approval_evidence_hash",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "approval_instance_id",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "decided_at_utc",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "decided_by_user_id",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "decision_reason",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "submitted_at_utc",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "submitted_by_user_id",
                table: "payroll_runs");

            migrationBuilder.DropColumn(
                name: "submitted_target_version",
                table: "payroll_runs");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payroll_run_status",
                table: "payroll_runs",
                sql: "status IN ('Draft', 'PendingApproval', 'Approved', 'PaymentInProgress', 'Paid', 'Completed', 'Cancelled')");
        }
    }
}
