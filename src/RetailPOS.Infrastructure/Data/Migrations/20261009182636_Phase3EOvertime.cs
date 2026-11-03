using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3EOvertime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_overtime_policy_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scope_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    minimum_eligible_minutes = table.Column<int>(type: "integer", nullable: false),
                    maximum_eligible_minutes = table.Column<int>(type: "integer", nullable: true),
                    rounding_unit_minutes = table.Column<int>(type: "integer", nullable: false),
                    rounding_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    include_pre_shift = table.Column<bool>(type: "boolean", nullable: false),
                    include_post_shift = table.Column<bool>(type: "boolean", nullable: false),
                    allow_normal_working_day = table.Column<bool>(type: "boolean", nullable: false),
                    allow_weekly_off = table.Column<bool>(type: "boolean", nullable: false),
                    allow_holiday = table.Column<bool>(type: "boolean", nullable: false),
                    allow_special_day = table.Column<bool>(type: "boolean", nullable: false),
                    rate_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    normal_day_rate = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    weekly_off_rate = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    holiday_rate = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    special_day_rate = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_overtime_policy_versions", x => x.id);
                    table.UniqueConstraint("a_k_hr_overtime_policy_versions_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_overtime_policy_business", "business_id > 0");
                    table.CheckConstraint("ck_hr_overtime_policy_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_hr_overtime_policy_maximum", "maximum_eligible_minutes IS NULL OR maximum_eligible_minutes >= minimum_eligible_minutes");
                    table.CheckConstraint("ck_hr_overtime_policy_minimum", "minimum_eligible_minutes >= 0");
                    table.CheckConstraint("ck_hr_overtime_policy_rates", "normal_day_rate >= 0 AND weekly_off_rate >= 0 AND holiday_rate >= 0 AND special_day_rate >= 0");
                    table.CheckConstraint("ck_hr_overtime_policy_rounding", "rounding_unit_minutes > 0");
                    table.CheckConstraint("ck_hr_overtime_policy_row_version", "version > 0");
                    table.CheckConstraint("ck_hr_overtime_policy_scope", "(scope_type = 'Business' AND scope_reference IS NULL) OR (scope_type <> 'Business' AND scope_reference IS NOT NULL)");
                    table.CheckConstraint("ck_hr_overtime_policy_version", "version_no > 0");
                    table.ForeignKey(
                        name: "f_k_hr_overtime_policy_versions_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_overtime_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    daily_attendance_id = table.Column<long>(type: "bigint", nullable: false),
                    operational_location_projection_id = table.Column<long>(type: "bigint", nullable: false),
                    day_category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    attendance_processing_version = table.Column<int>(type: "integer", nullable: false),
                    policy_version_id = table.Column<long>(type: "bigint", nullable: false),
                    policy_version_no = table.Column<int>(type: "integer", nullable: false),
                    eligible_minutes = table.Column<int>(type: "integer", nullable: false),
                    calculated_minutes = table.Column<int>(type: "integer", nullable: false),
                    approved_minutes = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    calculation_version = table.Column<int>(type: "integer", nullable: false),
                    calculation_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    policy_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    attendance_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    calculation_inputs_json = table.Column<string>(type: "jsonb", nullable: false),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_overtime_entries", x => x.id);
                    table.UniqueConstraint("a_k_hr_overtime_entries_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_overtime_approved", "approved_minutes IS NULL OR approved_minutes <= calculated_minutes");
                    table.CheckConstraint("ck_hr_overtime_business", "business_id > 0");
                    table.CheckConstraint("ck_hr_overtime_minutes", "eligible_minutes >= 0 AND calculated_minutes >= 0 AND (approved_minutes IS NULL OR approved_minutes >= 0)");
                    table.CheckConstraint("ck_hr_overtime_status", "status IN ('Calculated','PendingApproval','PayrollEligible','Rejected','Cancelled','Superseded')");
                    table.CheckConstraint("ck_hr_overtime_version", "attendance_processing_version > 0 AND calculation_version > 0 AND version > 0");
                    table.CheckConstraint("ck_hr_overtime_work_date", "work_date <> '0001-01-01'");
                    table.ForeignKey(
                        name: "f_k_hr_overtime_entries_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_overtime_entries_hr_approval_instances_business_id_appro~",
                        columns: x => new { x.business_id, x.approval_instance_id },
                        principalTable: "hr_approval_instances",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_overtime_entries_hr_daily_attendance_business_id_daily_a~",
                        columns: x => new { x.business_id, x.daily_attendance_id },
                        principalTable: "hr_daily_attendance",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_overtime_entries_hr_employees_business_id_employee_id",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_overtime_entries_hr_operational_location_projections_bus~",
                        columns: x => new { x.business_id, x.operational_location_projection_id },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_overtime_entries_hr_overtime_policy_versions_business_id~",
                        columns: x => new { x.business_id, x.policy_version_id },
                        principalTable: "hr_overtime_policy_versions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_overtime_entries_hr_overtime_policy_versions_policy_vers~",
                        column: x => x.policy_version_id,
                        principalTable: "hr_overtime_policy_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_overtime_calculation_evidence",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    overtime_entry_id = table.Column<long>(type: "bigint", nullable: false),
                    calculation_version = table.Column<int>(type: "integer", nullable: false),
                    attendance_processing_version = table.Column<int>(type: "integer", nullable: false),
                    policy_version_id = table.Column<long>(type: "bigint", nullable: false),
                    eligible_minutes = table.Column<int>(type: "integer", nullable: false),
                    calculated_minutes = table.Column<int>(type: "integer", nullable: false),
                    day_category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    calculation_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    policy_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    attendance_snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    calculation_inputs_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_overtime_calculation_evidence", x => x.id);
                    table.UniqueConstraint("a_k_hr_overtime_calculation_evidence_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_overtime_evidence_actor", "created_by_user_id > 0");
                    table.CheckConstraint("ck_hr_overtime_evidence_minutes", "eligible_minutes >= 0 AND calculated_minutes >= 0");
                    table.CheckConstraint("ck_hr_overtime_evidence_version", "calculation_version > 0 AND attendance_processing_version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_overtime_calculation_evidence_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_overtime_calculation_evidence_hr_overtime_entries_busine~",
                        columns: x => new { x.business_id, x.overtime_entry_id },
                        principalTable: "hr_overtime_entries",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_overtime_calculation_evidence_hr_overtime_policy_version~",
                        columns: x => new { x.business_id, x.policy_version_id },
                        principalTable: "hr_overtime_policy_versions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_overtime_calculation_evidence_business_id_overtime_entry~",
                table: "hr_overtime_calculation_evidence",
                columns: new[] { "business_id", "overtime_entry_id", "calculation_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_overtime_calculation_evidence_business_id_policy_version~",
                table: "hr_overtime_calculation_evidence",
                columns: new[] { "business_id", "policy_version_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_overtime_entries_business_id_approval_instance_id",
                table: "hr_overtime_entries",
                columns: new[] { "business_id", "approval_instance_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_overtime_entries_business_id_daily_attendance_id",
                table: "hr_overtime_entries",
                columns: new[] { "business_id", "daily_attendance_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_overtime_entries_business_id_employee_id_work_date",
                table: "hr_overtime_entries",
                columns: new[] { "business_id", "employee_id", "work_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_overtime_entries_business_id_operational_location_projec~",
                table: "hr_overtime_entries",
                columns: new[] { "business_id", "operational_location_projection_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_overtime_entries_business_id_policy_version_id",
                table: "hr_overtime_entries",
                columns: new[] { "business_id", "policy_version_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_overtime_entries_business_id_work_date_status",
                table: "hr_overtime_entries",
                columns: new[] { "business_id", "work_date", "status" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_overtime_entries_policy_version_id",
                table: "hr_overtime_entries",
                column: "policy_version_id");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_overtime_policy_versions_business_id_code_version_no",
                table: "hr_overtime_policy_versions",
                columns: new[] { "business_id", "code", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_overtime_policy_versions_business_id_scope_type_scope_re~",
                table: "hr_overtime_policy_versions",
                columns: new[] { "business_id", "scope_type", "scope_reference", "effective_from", "effective_to" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_overtime_calculation_evidence");

            migrationBuilder.DropTable(
                name: "hr_overtime_entries");

            migrationBuilder.DropTable(
                name: "hr_overtime_policy_versions");
        }
    }
}
