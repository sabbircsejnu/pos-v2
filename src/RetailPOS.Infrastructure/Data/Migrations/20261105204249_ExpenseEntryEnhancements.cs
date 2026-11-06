using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpenseEntryEnhancements : Migration
    {
        /// <summary>
        /// Starting set for the new flag so the lookup is not empty after deploy: active accounts already configured
        /// as cash, petty cash or bank, plus active bank-register accounts. Supplier payable is never flagged, and other
        /// Asset/Liability accounts stay off until accounting enables them on the Chart of Accounts.
        /// </summary>
        public const string BackfillExpensePaymentAccountsSql = """
            UPDATE chart_accounts AS account
            SET is_expense_payment_account = TRUE
            WHERE account.is_active
              AND account.account_type IN ('Asset', 'Liability')
              AND (
                EXISTS (
                    SELECT 1 FROM accounting_configurations AS config
                    WHERE config.business_id = account.business_id
                      AND account.id IN (config.cash_account_id, config.petty_cash_account_id, config.default_bank_account_id))
                OR EXISTS (
                    SELECT 1 FROM accounting_outlet_configurations AS config
                    WHERE config.business_id = account.business_id
                      AND account.id IN (config.outlet_cash_account_id, config.outlet_petty_cash_account_id, config.outlet_bank_account_id))
                OR EXISTS (
                    SELECT 1 FROM bank_accounts AS bank
                    WHERE bank.business_id = account.business_id
                      AND bank.chart_account_id = account.id
                      AND bank.is_active))
              AND NOT EXISTS (
                    SELECT 1 FROM accounting_configurations AS config
                    WHERE config.business_id = account.business_id
                      AND config.supplier_payable_account_id = account.id);
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "warehouse_id",
                table: "expenses",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_expense_payment_account",
                table: "chart_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(BackfillExpensePaymentAccountsSql);

            migrationBuilder.CreateTable(
                name: "expense_attachments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    expense_id = table.Column<long>(type: "bigint", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_expense_attachments", x => x.id);
                    table.ForeignKey(
                        name: "f_k_expense_attachments_expenses_expense_id",
                        column: x => x.expense_id,
                        principalTable: "expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_expenses_warehouse_id",
                table: "expenses",
                column: "warehouse_id");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_business_id_warehouse_id",
                table: "expenses",
                columns: new[] { "business_id", "warehouse_id" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_single_location",
                table: "expenses",
                sql: "outlet_id IS NULL OR warehouse_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "i_x_expense_attachments_expense_id",
                table: "expense_attachments",
                column: "expense_id");

            migrationBuilder.CreateIndex(
                name: "IX_expense_attachments_business_id_expense_id",
                table: "expense_attachments",
                columns: new[] { "business_id", "expense_id" });

            migrationBuilder.AddForeignKey(
                name: "f_k_expenses__warehouses_warehouse_id",
                table: "expenses",
                column: "warehouse_id",
                principalTable: "warehouses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_expenses__warehouses_warehouse_id",
                table: "expenses");

            migrationBuilder.DropTable(
                name: "expense_attachments");

            migrationBuilder.DropIndex(
                name: "i_x_expenses_warehouse_id",
                table: "expenses");

            migrationBuilder.DropIndex(
                name: "IX_expenses_business_id_warehouse_id",
                table: "expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_single_location",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "warehouse_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "is_expense_payment_account",
                table: "chart_accounts");
        }
    }
}
