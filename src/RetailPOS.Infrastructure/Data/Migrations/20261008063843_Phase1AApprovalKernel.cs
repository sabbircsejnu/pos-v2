using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1AApprovalKernel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_approval_policies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    process_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    scope_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    scope_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    allow_self_approval = table.Column<bool>(type: "boolean", nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_to = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_approval_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_approval_instances",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    policy_id = table.Column<long>(type: "bigint", nullable: false),
                    policy_code_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    policy_version_snapshot = table.Column<int>(type: "integer", nullable: false),
                    allow_self_approval_snapshot = table.Column<bool>(type: "boolean", nullable: false),
                    process_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    target_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_id = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    requested_by_user_id = table.Column<long>(type: "bigint", nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    current_step_sequence = table.Column<int>(type: "integer", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    scope_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    target_version = table.Column<long>(type: "bigint", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_approval_instances", x => x.id);
                    table.ForeignKey(
                        name: "f_k_hr_approval_instances_hr_approval_policies_policy_id",
                        column: x => x.policy_id,
                        principalTable: "hr_approval_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_approval_policy_steps",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    approval_policy_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    approver_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    approver_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    required_approvals = table.Column<int>(type: "integer", nullable: false),
                    can_reject = table.Column<bool>(type: "boolean", nullable: false),
                    can_cancel = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_approval_policy_steps", x => x.id);
                    table.ForeignKey(
                        name: "f_k_hr_approval_policy_steps_hr_approval_policies_approval_poli~",
                        column: x => x.approval_policy_id,
                        principalTable: "hr_approval_policies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_approval_instance_steps",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    resolved_approver_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    resolved_approver_reference = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    resolved_approver_candidates_json = table.Column<string>(type: "jsonb", nullable: false),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    decision_by_user_id = table.Column<long>(type: "bigint", nullable: true),
                    decision_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    required_approvals_snapshot = table.Column<int>(type: "integer", nullable: false),
                    policy_step_version_snapshot = table.Column<int>(type: "integer", nullable: false),
                    can_reject_snapshot = table.Column<bool>(type: "boolean", nullable: false),
                    can_cancel_snapshot = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_approval_instance_steps", x => x.id);
                    table.ForeignKey(
                        name: "f_k_hr_approval_instance_steps_hr_approval_instances_approval_i~",
                        column: x => x.approval_instance_id,
                        principalTable: "hr_approval_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_approval_decisions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    approval_instance_id = table.Column<long>(type: "bigint", nullable: false),
                    approval_instance_step_id = table.Column<long>(type: "bigint", nullable: false),
                    decision_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_real_user_id = table.Column<long>(type: "bigint", nullable: false),
                    acting_role_id = table.Column<long>(type: "bigint", nullable: true),
                    delegated_for_user_id = table.Column<long>(type: "bigint", nullable: true),
                    decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    instance_status_after_decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    current_step_sequence_after_decision = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_approval_decisions", x => x.id);
                    table.ForeignKey(
                        name: "f_k_hr_approval_decisions_hr_approval_instance_steps_approval_i~",
                        column: x => x.approval_instance_step_id,
                        principalTable: "hr_approval_instance_steps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_approval_decisions_hr_approval_instances_approval_instan~",
                        column: x => x.approval_instance_id,
                        principalTable: "hr_approval_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_approval_decisions_approval_instance_id",
                table: "hr_approval_decisions",
                column: "approval_instance_id");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_approval_decisions_approval_instance_step_id_actor_real_~",
                table: "hr_approval_decisions",
                columns: new[] { "approval_instance_step_id", "actor_real_user_id" },
                unique: true,
                filter: "decision IN ('Approve', 'Reject')");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_approval_decisions_business_id_decision_key",
                table: "hr_approval_decisions",
                columns: new[] { "business_id", "decision_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_approval_instance_steps_approval_instance_id_sequence",
                table: "hr_approval_instance_steps",
                columns: new[] { "approval_instance_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_approval_instances_business_id_status_process_type_targe~",
                table: "hr_approval_instances",
                columns: new[] { "business_id", "status", "process_type", "target_type", "target_id" },
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_approval_instances_policy_id",
                table: "hr_approval_instances",
                column: "policy_id");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_approval_policies_business_id_code_version_no",
                table: "hr_approval_policies",
                columns: new[] { "business_id", "code", "version_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_approval_policies_business_id_process_type_scope_type_sc~",
                table: "hr_approval_policies",
                columns: new[] { "business_id", "process_type", "scope_type", "scope_reference", "effective_from", "effective_to" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_approval_policy_steps_approval_policy_id_sequence",
                table: "hr_approval_policy_steps",
                columns: new[] { "approval_policy_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_approval_decisions");

            migrationBuilder.DropTable(
                name: "hr_approval_policy_steps");

            migrationBuilder.DropTable(
                name: "hr_approval_instance_steps");

            migrationBuilder.DropTable(
                name: "hr_approval_instances");

            migrationBuilder.DropTable(
                name: "hr_approval_policies");
        }
    }
}
