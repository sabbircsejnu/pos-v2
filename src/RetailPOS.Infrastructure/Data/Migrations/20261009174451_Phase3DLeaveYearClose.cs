using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3DLeaveYearClose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "expires_on",
                table: "hr_leave_ledger",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "close_version",
                table: "hr_leave_entitlements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "hr_leave_entitlements",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Open");

            migrationBuilder.AddUniqueConstraint(
                name: "a_k_hr_leave_ledger_business_id_id",
                table: "hr_leave_ledger",
                columns: new[] { "business_id", "id" });

            migrationBuilder.CreateTable(
                name: "hr_leave_year_lifecycle_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    entitlement_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_type_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_year_start = table.Column<DateOnly>(type: "date", nullable: false),
                    close_version = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    balance_before_close = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    carry_forward_amount = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    expired_amount = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    carry_forward_expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    source_ledger_entry_id = table.Column<long>(type: "bigint", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    actor_real_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_leave_year_lifecycle_events", x => x.id);
                    table.UniqueConstraint("a_k_hr_leave_year_lifecycle_events_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_leave_year_event_actor", "actor_real_user_id > 0");
                    table.CheckConstraint("ck_hr_leave_year_event_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_leave_year_event_close_version", "close_version >= 0");
                    table.CheckConstraint("ck_hr_leave_year_event_key", "length(idempotency_key) BETWEEN 1 AND 150");
                    table.CheckConstraint("ck_hr_leave_year_event_quantities", "balance_before_close >= -1000000 AND carry_forward_amount >= 0 AND expired_amount >= 0");
                    table.ForeignKey(
                        name: "f_k_hr_leave_year_lifecycle_events_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_year_lifecycle_events_hr_employees_business_id_emp~",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_year_lifecycle_events_hr_leave_entitlements_busine~",
                        columns: x => new { x.business_id, x.entitlement_id },
                        principalTable: "hr_leave_entitlements",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_year_lifecycle_events_hr_leave_ledger_business_id_~",
                        columns: x => new { x.business_id, x.source_ledger_entry_id },
                        principalTable: "hr_leave_ledger",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_year_lifecycle_events_hr_leave_types_business_id_l~",
                        columns: x => new { x.business_id, x.leave_type_id },
                        principalTable: "hr_leave_types",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_hr_leave_entitlement_close_version",
                table: "hr_leave_entitlements",
                sql: "close_version >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_hr_leave_year_event_employee",
                table: "hr_leave_year_lifecycle_events",
                columns: new[] { "business_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_leave_year_event_entitlement_id",
                table: "hr_leave_year_lifecycle_events",
                columns: new[] { "business_id", "entitlement_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_leave_year_event_idempotency",
                table: "hr_leave_year_lifecycle_events",
                columns: new[] { "business_id", "entitlement_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_leave_year_event_leave_type",
                table: "hr_leave_year_lifecycle_events",
                columns: new[] { "business_id", "leave_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_leave_year_event_source_ledger",
                table: "hr_leave_year_lifecycle_events",
                columns: new[] { "business_id", "source_ledger_entry_id" },
                filter: "source_ledger_entry_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_leave_year_lifecycle_events");

            migrationBuilder.DropUniqueConstraint(
                name: "a_k_hr_leave_ledger_business_id_id",
                table: "hr_leave_ledger");

            migrationBuilder.DropCheckConstraint(
                name: "ck_hr_leave_entitlement_close_version",
                table: "hr_leave_entitlements");

            migrationBuilder.DropColumn(
                name: "expires_on",
                table: "hr_leave_ledger");

            migrationBuilder.DropColumn(
                name: "close_version",
                table: "hr_leave_entitlements");

            migrationBuilder.DropColumn(
                name: "status",
                table: "hr_leave_entitlements");
        }
    }
}
