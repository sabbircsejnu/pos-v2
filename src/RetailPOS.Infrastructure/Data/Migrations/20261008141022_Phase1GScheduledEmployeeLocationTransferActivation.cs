using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1GScheduledEmployeeLocationTransferActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_hr_emp_loc_transfer_pending_emp",
                table: "hr_employee_location_transfer_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_hr_employee_location_transfer_status",
                table: "hr_employee_location_transfer_requests");

            migrationBuilder.CreateIndex(
                name: "ux_hr_emp_loc_transfer_pending_emp",
                table: "hr_employee_location_transfer_requests",
                columns: new[] { "business_id", "employee_id" },
                unique: true,
                filter: "status IN ('PendingApproval', 'ApprovedPendingEffective')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_hr_employee_location_transfer_status",
                table: "hr_employee_location_transfer_requests",
                sql: "status IN ('PendingApproval', 'Approved', 'ApprovedPendingEffective', 'Effective', 'Rejected', 'Cancelled', 'Superseded')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_hr_emp_loc_transfer_pending_emp",
                table: "hr_employee_location_transfer_requests");

            migrationBuilder.DropCheckConstraint(
                name: "ck_hr_employee_location_transfer_status",
                table: "hr_employee_location_transfer_requests");

            migrationBuilder.CreateIndex(
                name: "ux_hr_emp_loc_transfer_pending_emp",
                table: "hr_employee_location_transfer_requests",
                columns: new[] { "business_id", "employee_id" },
                unique: true,
                filter: "status = 'PendingApproval'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_hr_employee_location_transfer_status",
                table: "hr_employee_location_transfer_requests",
                sql: "status IN ('PendingApproval', 'Approved', 'Rejected', 'Cancelled')");
        }
    }
}
