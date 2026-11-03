using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1EEmployeeLifecycleAndPositionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "current_org_unit_id",
                table: "hr_employees",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "current_position_id",
                table: "hr_employees",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "last_working_date",
                table: "hr_employees",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suspended_from_status",
                table: "hr_employees",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "a_k_hr_positions_business_id_id_org_unit_id",
                table: "hr_positions",
                columns: new[] { "business_id", "id", "org_unit_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "a_k_hr_employment_periods_business_id_employee_id_id",
                table: "hr_employment_periods",
                columns: new[] { "business_id", "employee_id", "id" });

            migrationBuilder.CreateTable(
                name: "hr_employee_lifecycle_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    employment_period_id = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    previous_state_json = table.Column<string>(type: "text", nullable: false),
                    new_state_json = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: true),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_employee_lifecycle_events", x => x.id);
                    table.CheckConstraint("ck_hr_employee_lifecycle_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_employee_lifecycle_effective_date", "effective_date IS NOT NULL");
                    table.ForeignKey(
                        name: "f_k_hr_employee_lifecycle_events_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_lifecycle_events_hr_employees_business_id_emplo~",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_lifecycle_events_hr_employment_periods_business~",
                        columns: x => new { x.business_id, x.employee_id, x.employment_period_id },
                        principalTable: "hr_employment_periods",
                        principalColumns: new[] { "business_id", "employee_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                INSERT INTO hr_employee_lifecycle_events
                    (business_id, employee_id, employment_period_id, event_type, effective_date,
                     previous_state_json, new_state_json, reason, approval_instance_id, actor_user_id,
                     occurred_at_utc)
                SELECT period.business_id, period.employee_id, period.id, period.source_event_type,
                       period.start_date, '{}',
                       json_build_object('employmentTypeId', period.employment_type_id, 'status', 'Active')::text,
                       period.start_reason, NULL, NULL, CURRENT_TIMESTAMP
                FROM hr_employment_periods AS period
                WHERE period.source_event_type IN ('Joined', 'Rehired');
                """);

            migrationBuilder.CreateTable(
                name: "hr_employee_position_assignments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    position_id = table.Column<long>(type: "bigint", nullable: false),
                    org_unit_id = table.Column<long>(type: "bigint", nullable: false),
                    reports_to_employee_id = table.Column<long>(type: "bigint", nullable: true),
                    assignment_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_employee_position_assignments", x => x.id);
                    table.CheckConstraint("ck_hr_employee_position_assignment_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_employee_position_assignment_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_hr_employee_position_assignment_not_self_manager", "reports_to_employee_id IS NULL OR reports_to_employee_id <> employee_id");
                    table.CheckConstraint("ck_hr_employee_position_assignment_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_employee_position_assignments_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_position_assignments_hr_employees_business_id_e~",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_position_assignments_hr_employees_business_id_r~",
                        columns: x => new { x.business_id, x.reports_to_employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_position_assignments_hr_org_units_business_id_o~",
                        columns: x => new { x.business_id, x.org_unit_id },
                        principalTable: "hr_org_units",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employee_position_assignments_hr_positions_business_id_p~",
                        columns: x => new { x.business_id, x.position_id, x.org_unit_id },
                        principalTable: "hr_positions",
                        principalColumns: new[] { "business_id", "id", "org_unit_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employees_business_id_current_org_unit_id",
                table: "hr_employees",
                columns: new[] { "business_id", "current_org_unit_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employees_business_id_current_position_id_current_org_un~",
                table: "hr_employees",
                columns: new[] { "business_id", "current_position_id", "current_org_unit_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_hr_employee_current_position_org_unit",
                table: "hr_employees",
                sql: "(current_position_id IS NULL AND current_org_unit_id IS NULL) OR (current_position_id IS NOT NULL AND current_org_unit_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_hr_employee_suspended_status",
                table: "hr_employees",
                sql: "suspended_from_status IS NULL OR suspended_from_status IN ('Active', 'Probation')");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_lifecycle_events_business_id_employee_id_effect~",
                table: "hr_employee_lifecycle_events",
                columns: new[] { "business_id", "employee_id", "effective_date", "id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_lifecycle_events_business_id_employee_id_employ~",
                table: "hr_employee_lifecycle_events",
                columns: new[] { "business_id", "employee_id", "employment_period_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_position_assignments_business_id_employee_id",
                table: "hr_employee_position_assignments",
                columns: new[] { "business_id", "employee_id" },
                unique: true,
                filter: "is_current = true AND assignment_type = 'Primary'");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_position_assignments_business_id_employee_id_ef~",
                table: "hr_employee_position_assignments",
                columns: new[] { "business_id", "employee_id", "effective_from", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_position_assignments_business_id_org_unit_id",
                table: "hr_employee_position_assignments",
                columns: new[] { "business_id", "org_unit_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_position_assignments_business_id_position_id_ef~",
                table: "hr_employee_position_assignments",
                columns: new[] { "business_id", "position_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_position_assignments_business_id_position_id_or~",
                table: "hr_employee_position_assignments",
                columns: new[] { "business_id", "position_id", "org_unit_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employee_position_assignments_business_id_reports_to_emp~",
                table: "hr_employee_position_assignments",
                columns: new[] { "business_id", "reports_to_employee_id" });

            migrationBuilder.AddForeignKey(
                name: "f_k_hr_employees_hr_org_units_business_id_current_org_unit_id",
                table: "hr_employees",
                columns: new[] { "business_id", "current_org_unit_id" },
                principalTable: "hr_org_units",
                principalColumns: new[] { "business_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "f_k_hr_employees_hr_positions_business_id_current_position_id_c~",
                table: "hr_employees",
                columns: new[] { "business_id", "current_position_id", "current_org_unit_id" },
                principalTable: "hr_positions",
                principalColumns: new[] { "business_id", "id", "org_unit_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_hr_employees_hr_org_units_business_id_current_org_unit_id",
                table: "hr_employees");

            migrationBuilder.DropForeignKey(
                name: "f_k_hr_employees_hr_positions_business_id_current_position_id_c~",
                table: "hr_employees");

            migrationBuilder.DropTable(
                name: "hr_employee_lifecycle_events");

            migrationBuilder.DropTable(
                name: "hr_employee_position_assignments");

            migrationBuilder.DropUniqueConstraint(
                name: "a_k_hr_positions_business_id_id_org_unit_id",
                table: "hr_positions");

            migrationBuilder.DropUniqueConstraint(
                name: "a_k_hr_employment_periods_business_id_employee_id_id",
                table: "hr_employment_periods");

            migrationBuilder.DropIndex(
                name: "i_x_hr_employees_business_id_current_org_unit_id",
                table: "hr_employees");

            migrationBuilder.DropIndex(
                name: "i_x_hr_employees_business_id_current_position_id_current_org_un~",
                table: "hr_employees");

            migrationBuilder.DropCheckConstraint(
                name: "ck_hr_employee_current_position_org_unit",
                table: "hr_employees");

            migrationBuilder.DropCheckConstraint(
                name: "ck_hr_employee_suspended_status",
                table: "hr_employees");

            migrationBuilder.DropColumn(
                name: "current_org_unit_id",
                table: "hr_employees");

            migrationBuilder.DropColumn(
                name: "current_position_id",
                table: "hr_employees");

            migrationBuilder.DropColumn(
                name: "last_working_date",
                table: "hr_employees");

            migrationBuilder.DropColumn(
                name: "suspended_from_status",
                table: "hr_employees");
        }
    }
}
