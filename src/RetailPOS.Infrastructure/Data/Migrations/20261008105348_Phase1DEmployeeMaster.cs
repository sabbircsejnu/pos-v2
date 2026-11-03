using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1DEmployeeMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_employment_types",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_employment_types", x => x.id);
                    table.UniqueConstraint("a_k_hr_employment_types_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_employment_type_business_id", "business_id > 0");
                    table.ForeignKey(
                        name: "f_k_hr_employment_types_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_employees",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    gender_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    national_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    passport_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    personal_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    work_email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    mobile = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    present_address = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    permanent_address = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    emergency_contact_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    emergency_contact_phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    current_employment_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    current_employment_type_id = table.Column<long>(type: "bigint", nullable: false),
                    joining_date = table.Column<DateOnly>(type: "date", nullable: false),
                    confirmation_date = table.Column<DateOnly>(type: "date", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_employees", x => x.id);
                    table.UniqueConstraint("a_k_hr_employees_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_hr_employee_business_id", "business_id > 0");
                    table.CheckConstraint("ck_hr_employee_confirmation_date", "confirmation_date IS NULL OR confirmation_date >= joining_date");
                    table.CheckConstraint("ck_hr_employee_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_hr_employees_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employees_hr_employment_types_business_id_current_employ~",
                        columns: x => new { x.business_id, x.current_employment_type_id },
                        principalTable: "hr_employment_types",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_employment_periods",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    employee_id = table.Column<long>(type: "bigint", nullable: false),
                    employment_type_id = table.Column<long>(type: "bigint", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    start_reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    end_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_event_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_hr_employment_periods", x => x.id);
                    table.CheckConstraint("ck_hr_employment_period_dates", "end_date IS NULL OR end_date >= start_date");
                    table.CheckConstraint("ck_hr_employment_period_status", "status IN ('Active', 'Closed')");
                    table.ForeignKey(
                        name: "f_k_hr_employment_periods_hr_employees_business_id_employee_id",
                        columns: x => new { x.business_id, x.employee_id },
                        principalTable: "hr_employees",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_hr_employment_periods_hr_employment_types_business_id_emplo~",
                        columns: x => new { x.business_id, x.employment_type_id },
                        principalTable: "hr_employment_types",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employees_business_id_current_employment_status_current_~",
                table: "hr_employees",
                columns: new[] { "business_id", "current_employment_status", "current_employment_type_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employees_business_id_current_employment_type_id",
                table: "hr_employees",
                columns: new[] { "business_id", "current_employment_type_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employees_business_id_employee_code",
                table: "hr_employees",
                columns: new[] { "business_id", "employee_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employment_periods_business_id_employee_id",
                table: "hr_employment_periods",
                columns: new[] { "business_id", "employee_id" },
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employment_periods_business_id_employee_id_start_date",
                table: "hr_employment_periods",
                columns: new[] { "business_id", "employee_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employment_periods_business_id_employment_type_id",
                table: "hr_employment_periods",
                columns: new[] { "business_id", "employment_type_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_hr_employment_types_business_id_code",
                table: "hr_employment_types",
                columns: new[] { "business_id", "code" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO hr_employment_types (business_id, code, name, is_active)
                SELECT b.id, employment_type.code, employment_type.name, TRUE
                FROM businesses AS b
                CROSS JOIN (VALUES
                    ('Permanent', 'Permanent'),
                    ('Probation', 'Probation'),
                    ('Contractual', 'Contractual'),
                    ('PartTime', 'Part-time'),
                    ('Temporary', 'Temporary'),
                    ('Intern', 'Intern'),
                    ('Other', 'Other')
                ) AS employment_type(code, name);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_employment_periods");

            migrationBuilder.DropTable(
                name: "hr_employees");

            migrationBuilder.DropTable(
                name: "hr_employment_types");
        }
    }
}
