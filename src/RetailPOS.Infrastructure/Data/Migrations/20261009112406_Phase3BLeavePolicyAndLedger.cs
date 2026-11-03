using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3BLeavePolicyAndLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_leave_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    requires_document = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_leave_types", x => x.id);
                    table.UniqueConstraint("a_k_hr_leave_types_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_leave_type_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_leave_type_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_leave_types_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_leave_policy_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_type_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    scope_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scope_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    accrual_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    annual_entitlement = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    carry_forward_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    carry_forward_cap = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    carry_forward_expiry_months = table.Column<int>(type: "integer", nullable: true),
                    maximum_balance = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    negative_balance_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    payroll_impact_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    leave_year_start_month = table.Column<int>(type: "integer", nullable: false),
                    leave_year_start_day = table.Column<int>(type: "integer", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_leave_policy_versions", x => x.id);
                    table.UniqueConstraint("a_k_hr_leave_policy_versions_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_leave_policy_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_leave_policy_carry_forward", "carry_forward_allowed OR (carry_forward_cap IS NULL AND carry_forward_expiry_months IS NULL)");
                    table.CheckConstraint("ck_hr_leave_policy_carry_forward_cap", "carry_forward_cap IS NULL OR carry_forward_cap >= 0");
                    table.CheckConstraint("ck_hr_leave_policy_dates", "effective_to IS NULL OR effective_to >= effective_from");
                    table.CheckConstraint("ck_hr_leave_policy_entitlement", "annual_entitlement >= 0");
                    table.CheckConstraint("ck_hr_leave_policy_expiry_months", "carry_forward_expiry_months IS NULL OR carry_forward_expiry_months > 0");
                    table.CheckConstraint("ck_hr_leave_policy_maximum_balance", "maximum_balance IS NULL OR maximum_balance >= 0");
                    table.CheckConstraint("ck_hr_leave_policy_scope", "(scope_type = 'Business' AND scope_reference IS NULL) OR (scope_type <> 'Business' AND scope_reference IS NOT NULL)");
                    table.CheckConstraint("ck_hr_leave_policy_version", "version_no > 0");
                    table.ForeignKey(
                        name: "f_k_hr_leave_policy_versions_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_policy_versions_hr_leave_types_business_id_leave_t~",
                        columns: x => new { x.business_id, x.leave_type_id },
                        principalTable: "hr_leave_types",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_leave_entitlements",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_type_id = table.Column<long>(type: "bigint", nullable: false),
                    policy_version_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_year_start = table.Column<DateOnly>(type: "date", nullable: false),
                    leave_year_end = table.Column<DateOnly>(type: "date", nullable: false),
                    joining_date_snapshot = table.Column<DateOnly>(type: "date", nullable: false),
                    primary_location_projection_id_snapshot = table.Column<long>(type: "bigint", nullable: true),
                    location_type_snapshot = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_leave_entitlements", x => x.id);
                    table.UniqueConstraint("a_k_hr_leave_entitlements_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_leave_entitlement_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_leave_entitlement_dates", "leave_year_end >= leave_year_start");
                    table.CheckConstraint("ck_hr_leave_entitlement_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_leave_entitlements_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_entitlements_hr_employees_business_id_employee_id",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_entitlements_hr_leave_policy_versions_business_id_~",
                        columns: x => new { x.business_id, x.policy_version_id },
                        principalTable: "hr_leave_policy_versions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_entitlements_hr_leave_types_business_id_leave_type~",
                        columns: x => new { x.business_id, x.leave_type_id },
                        principalTable: "hr_leave_types",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_entitlements_hr_operational_location_projections_b~",
                        columns: x => new { x.business_id, x.primary_location_projection_id_snapshot },
                        principalTable: "hr_operational_location_projections",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_leave_ledger",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    entitlement_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    leave_type_id = table.Column<long>(type: "bigint", nullable: false),
                    policy_version_id = table.Column<long>(type: "bigint", nullable: false),
                    transaction_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    source_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    actor_real_user_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_leave_ledger", x => x.id);
                    table.CheckConstraint("ck_hr_leave_ledger_actor", "actor_real_user_id > 0");
                    table.CheckConstraint("ck_hr_leave_ledger_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_leave_ledger_idempotency_key", "length(idempotency_key) BETWEEN 1 AND 150");
                    table.CheckConstraint("ck_hr_leave_ledger_quantity", "quantity <> 0");
                    table.CheckConstraint("ck_hr_leave_ledger_source", "length(source_type) > 0 AND length(source_id) > 0");
                    table.ForeignKey(
                        name: "f_k_hr_leave_ledger_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_ledger_hr_employees_business_id_employee_id",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_ledger_hr_leave_entitlements_business_id_entitleme~",
                        columns: x => new { x.business_id, x.entitlement_id },
                        principalTable: "hr_leave_entitlements",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_ledger_hr_leave_policy_versions_business_id_policy~",
                        columns: x => new { x.business_id, x.policy_version_id },
                        principalTable: "hr_leave_policy_versions",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_leave_ledger_hr_leave_types_business_id_leave_type_id",
                        columns: x => new { x.business_id, x.leave_type_id },
                        principalTable: "hr_leave_types",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_entitlements_business_id_employee_id_leave_type_id~",
                table: "hr_leave_entitlements",
                columns: new[] { "business_id", "employee_id", "leave_type_id", "leave_year_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_entitlements_business_id_leave_type_id",
                table: "hr_leave_entitlements",
                columns: new[] { "business_id", "leave_type_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_entitlements_business_id_policy_version_id",
                table: "hr_leave_entitlements",
                columns: new[] { "business_id", "policy_version_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_entitlements_business_id_primary_location_projecti~",
                table: "hr_leave_entitlements",
                columns: new[] { "business_id", "primary_location_projection_id_snapshot" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_ledger_business_id_employee_id",
                table: "hr_leave_ledger",
                columns: new[] { "business_id", "employee_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_ledger_business_id_entitlement_id_effective_date",
                table: "hr_leave_ledger",
                columns: new[] { "business_id", "entitlement_id", "effective_date" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_ledger_business_id_idempotency_key",
                table: "hr_leave_ledger",
                columns: new[] { "business_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_ledger_business_id_leave_type_id",
                table: "hr_leave_ledger",
                columns: new[] { "business_id", "leave_type_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_ledger_business_id_policy_version_id",
                table: "hr_leave_ledger",
                columns: new[] { "business_id", "policy_version_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_policy_versions_business_id_leave_type_id_code_ver~",
                table: "hr_leave_policy_versions",
                columns: new[] { "business_id", "leave_type_id", "code", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_policy_versions_business_id_leave_type_id_scope_ty~",
                table: "hr_leave_policy_versions",
                columns: new[] { "business_id", "leave_type_id", "scope_type", "scope_reference", "effective_from", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_leave_types_business_id_code",
                table: "hr_leave_types",
                columns: new[] { "business_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_leave_ledger");

            migrationBuilder.DropTable(
                name: "hr_leave_entitlements");

            migrationBuilder.DropTable(
                name: "hr_leave_policy_versions");

            migrationBuilder.DropTable(
                name: "hr_leave_types");
        }
    }
}
