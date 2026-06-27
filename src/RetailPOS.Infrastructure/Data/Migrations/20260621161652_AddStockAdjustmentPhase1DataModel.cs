using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStockAdjustmentPhase1DataModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_stock_adjustments__users_adjuster_id",
                table: "stock_adjustments");

            migrationBuilder.AddColumn<string>(
                name: "adjustment_number",
                table: "stock_adjustments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "approved_at",
                table: "stock_adjustments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "approved_by",
                table: "stock_adjustments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at",
                table: "stock_adjustments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "cancelled_by",
                table: "stock_adjustments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "stock_adjustments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.AddColumn<int>(
                name: "new_quantity",
                table: "stock_adjustments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "stock_adjustments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "previous_quantity",
                table: "stock_adjustments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "rejected_at",
                table: "stock_adjustments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "rejected_by",
                table: "stock_adjustments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                table: "stock_adjustments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "stock_adjustments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_at",
                table: "stock_adjustments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "stock_adjustments",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()");

            migrationBuilder.Sql(@"
                UPDATE stock_adjustments
                SET adjustment_number = 'ADJ-' || TO_CHAR(COALESCE(adjustment_date, NOW()), 'YYYY') || '-' || LPAD(id::text, 5, '0'),
                    status = 'Approved',
                    previous_quantity = 0,
                    new_quantity = quantity_change,
                    notes = NULL,
                    created_at = COALESCE(adjustment_date, NOW()),
                    updated_at = COALESCE(adjustment_date, NOW()),
                    submitted_at = COALESCE(adjustment_date, NOW()),
                    approved_by = adjusted_by,
                    approved_at = COALESCE(adjustment_date, NOW())
                WHERE adjustment_number IS NULL OR status IS NULL;
            ");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "stock_adjustments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Draft",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "adjustment_number",
                table: "stock_adjustments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustments_adjustment_number",
                table: "stock_adjustments",
                column: "adjustment_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustments_approved_by",
                table: "stock_adjustments",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustments_cancelled_by",
                table: "stock_adjustments",
                column: "cancelled_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustments_rejected_by",
                table: "stock_adjustments",
                column: "rejected_by");

            migrationBuilder.CreateIndex(
                name: "IX_stock_adjustments_status",
                table: "stock_adjustments",
                column: "status");

            migrationBuilder.AddForeignKey(
                name: "FK_stock_adjustments_users_adjusted_by",
                table: "stock_adjustments",
                column: "adjusted_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_adjustments_users_approved_by",
                table: "stock_adjustments",
                column: "approved_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_adjustments_users_cancelled_by",
                table: "stock_adjustments",
                column: "cancelled_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_stock_adjustments_users_rejected_by",
                table: "stock_adjustments",
                column: "rejected_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_stock_adjustments_users_adjusted_by",
                table: "stock_adjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_adjustments_users_approved_by",
                table: "stock_adjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_adjustments_users_cancelled_by",
                table: "stock_adjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_stock_adjustments_users_rejected_by",
                table: "stock_adjustments");

            migrationBuilder.DropIndex(
                name: "IX_stock_adjustments_adjustment_number",
                table: "stock_adjustments");

            migrationBuilder.DropIndex(
                name: "IX_stock_adjustments_approved_by",
                table: "stock_adjustments");

            migrationBuilder.DropIndex(
                name: "IX_stock_adjustments_cancelled_by",
                table: "stock_adjustments");

            migrationBuilder.DropIndex(
                name: "IX_stock_adjustments_rejected_by",
                table: "stock_adjustments");

            migrationBuilder.DropIndex(
                name: "IX_stock_adjustments_status",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "adjustment_number",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "approved_at",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "approved_by",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "cancelled_by",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "new_quantity",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "previous_quantity",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "rejected_at",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "rejected_by",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "status",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "submitted_at",
                table: "stock_adjustments");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "stock_adjustments");

            migrationBuilder.AddForeignKey(
                name: "f_k_stock_adjustments__users_adjuster_id",
                table: "stock_adjustments",
                column: "adjusted_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
