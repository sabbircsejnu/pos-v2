using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncWarehouseAssignmentSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "i_x_user_warehouse_assignments_user_id_warehouse_id",
                table: "user_warehouse_assignments",
                newName: "IX_user_warehouse_assignments_user_id_warehouse_id");

            migrationBuilder.RenameIndex(
                name: "i_x_user_warehouse_assignments_user_id",
                table: "user_warehouse_assignments",
                newName: "IX_user_warehouse_assignments_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_user_warehouse_assignments_user_id_warehouse_id",
                table: "user_warehouse_assignments",
                newName: "i_x_user_warehouse_assignments_user_id_warehouse_id");

            migrationBuilder.RenameIndex(
                name: "IX_user_warehouse_assignments_user_id",
                table: "user_warehouse_assignments",
                newName: "i_x_user_warehouse_assignments_user_id");
        }
    }
}
