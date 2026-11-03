using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5APayrollPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_periods",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    period_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    pay_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    opened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    opened_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    processing_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processing_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    locked_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    locked_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    approved_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    posted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    posted_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    closed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    reopened_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reopened_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    reopen_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_periods", x => x.id);
                    table.UniqueConstraint("a_k_payroll_periods_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_period_dates", "period_start <= period_end");
                    table.CheckConstraint("ck_payroll_period_status", "status IN ('Open', 'Processing', 'Locked', 'Approved', 'Posted', 'Closed')");
                    table.CheckConstraint("ck_payroll_period_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_periods_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_period_business_status_start",
                table: "payroll_periods",
                columns: new[] { "business_id", "status", "period_start" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_period_business_code",
                table: "payroll_periods",
                columns: new[] { "business_id", "period_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_payroll_period_business_dates",
                table: "payroll_periods",
                columns: new[] { "business_id", "period_start", "period_end" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_periods");
        }
    }
}
