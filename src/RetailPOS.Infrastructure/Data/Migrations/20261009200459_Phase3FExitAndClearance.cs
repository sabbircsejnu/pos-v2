using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3FExitAndClearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_clearance_templates",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scope_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_clearance_templates", x => x.id);
                    table.UniqueConstraint("a_k_hr_clearance_templates_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_clearance_template_business", "business_id > 0");
                    table.CheckConstraint("ck_hr_clearance_template_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_hr_clearance_template_scope", "(scope_type = 'Business' AND scope_reference IS NULL) OR (scope_type <> 'Business' AND scope_reference IS NOT NULL)");
                    table.CheckConstraint("ck_hr_clearance_template_version", "version_no > 0");
                    table.ForeignKey(
                        name: "f_k_hr_clearance_templates_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_clearance_template_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    template_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    responsible_role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    responsible_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_clearance_template_items", x => x.id);
                    table.UniqueConstraint("a_k_hr_clearance_template_items_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_clearance_template_item_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "f_k_hr_clearance_template_items_hr_clearance_templates_business~",
                        columns: x => new { x.business_id, x.template_id },
                        principalTable: "hr_clearance_templates",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_exit_cases",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    exit_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    initiated_date = table.Column<DateOnly>(type: "date", nullable: false),
                    proposed_last_working_date = table.Column<DateOnly>(type: "date", nullable: false),
                    actual_last_working_date = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    initiated_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    initiated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    primary_location_projection_id_snapshot = table.Column<long>(type: "bigint", nullable: true),
                    location_type_snapshot = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    clearance_template_id = table.Column<long>(type: "bigint", nullable: false),
                    clearance_template_version_no = table.Column<int>(type: "integer", nullable: false),
                    clearance_template_snapshot_json = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_exit_cases", x => x.id);
                    table.UniqueConstraint("a_k_hr_exit_cases_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_exit_case_business", "business_id > 0");
                    table.CheckConstraint("ck_hr_exit_case_dates", "proposed_last_working_date >= initiated_date AND (actual_last_working_date IS NULL OR actual_last_working_date >= initiated_date)");
                    table.CheckConstraint("ck_hr_exit_case_template_version", "clearance_template_version_no > 0");
                    table.CheckConstraint("ck_hr_exit_case_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_exit_cases_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_exit_cases_hr_approval_instances_approval_instance_id",
                        column: x => x.approval_instance_id,
                        principalTable: "hr_approval_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_exit_cases_hr_clearance_templates_business_id_clearance_~",
                        columns: x => new { x.business_id, x.clearance_template_id },
                        principalTable: "hr_clearance_templates",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_exit_cases_hr_employees_business_id_employee_id",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_exit_cases_hr_operational_location_projections_business_~",
                        columns: x => new { x.business_id, x.primary_location_projection_id_snapshot },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_clearance_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    exit_case_id = table.Column<long>(type: "bigint", nullable: false),
                    template_item_id = table.Column<long>(type: "bigint", nullable: true),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    responsible_role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    responsible_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_mandatory = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cleared_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    cleared_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    evidence_reference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_clearance_items", x => x.id);
                    table.UniqueConstraint("a_k_hr_clearance_items_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_clearance_item_clear_fields", "(status = 'Cleared' AND cleared_by_user_id IS NOT NULL AND cleared_at_utc IS NOT NULL) OR (status <> 'Cleared' AND cleared_by_user_id IS NULL AND cleared_at_utc IS NULL)");
                    table.CheckConstraint("ck_hr_clearance_item_sequence", "sequence > 0");
                    table.CheckConstraint("ck_hr_clearance_item_status", "status IN ('Pending', 'Cleared', 'Exception', 'NotApplicable')");
                    table.CheckConstraint("ck_hr_clearance_item_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_clearance_items_hr_clearance_template_items_business_id_~",
                        columns: x => new { x.business_id, x.template_item_id },
                        principalTable: "hr_clearance_template_items",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_clearance_items_hr_exit_cases_business_id_exit_case_id",
                        columns: x => new { x.business_id, x.exit_case_id },
                        principalTable: "hr_exit_cases",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_clearance_items_hr_exit_cases_exit_case_id",
                        column: x => x.exit_case_id,
                        principalTable: "hr_exit_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_exit_case_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    exit_case_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    previous_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    new_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actual_last_working_date = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    actor_user_id = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_exit_case_events", x => x.id);
                    table.CheckConstraint("ck_hr_exit_event_actor", "actor_user_id > 0");
                    table.CheckConstraint("ck_hr_exit_event_sequence", "sequence > 0");
                    table.ForeignKey(
                        name: "f_k_hr_exit_case_events_hr_exit_cases_business_id_exit_case_id",
                        columns: x => new { x.business_id, x.exit_case_id },
                        principalTable: "hr_exit_cases",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_clearance_items_business_id_exit_case_id_sequence",
                table: "hr_clearance_items",
                columns: new[] { "business_id", "exit_case_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_clearance_items_business_id_template_item_id",
                table: "hr_clearance_items",
                columns: new[] { "business_id", "template_item_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_clearance_items_exit_case_id",
                table: "hr_clearance_items",
                column: "exit_case_id");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_clearance_template_items_business_id_template_id_code",
                table: "hr_clearance_template_items",
                columns: new[] { "business_id", "template_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_clearance_template_items_business_id_template_id_sequence",
                table: "hr_clearance_template_items",
                columns: new[] { "business_id", "template_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_clearance_templates_business_id_code_version_no",
                table: "hr_clearance_templates",
                columns: new[] { "business_id", "code", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_clearance_templates_business_id_scope_type_scope_referen~",
                table: "hr_clearance_templates",
                columns: new[] { "business_id", "scope_type", "scope_reference", "effective_from", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_exit_case_events_business_id_exit_case_id_sequence",
                table: "hr_exit_case_events",
                columns: new[] { "business_id", "exit_case_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_exit_cases_approval_instance_id",
                table: "hr_exit_cases",
                column: "approval_instance_id");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_exit_cases_business_id_clearance_template_id",
                table: "hr_exit_cases",
                columns: new[] { "business_id", "clearance_template_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_exit_cases_business_id_employee_id_status",
                table: "hr_exit_cases",
                columns: new[] { "business_id", "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_exit_cases_business_id_initiated_date_status",
                table: "hr_exit_cases",
                columns: new[] { "business_id", "initiated_date", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_exit_cases_business_id_primary_location_projection_id_sn~",
                table: "hr_exit_cases",
                columns: new[] { "business_id", "primary_location_projection_id_snapshot" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_clearance_items");

            migrationBuilder.DropTable(
                name: "hr_exit_case_events");

            migrationBuilder.DropTable(
                name: "hr_clearance_template_items");

            migrationBuilder.DropTable(
                name: "hr_exit_cases");

            migrationBuilder.DropTable(
                name: "hr_clearance_templates");
        }
    }
}
