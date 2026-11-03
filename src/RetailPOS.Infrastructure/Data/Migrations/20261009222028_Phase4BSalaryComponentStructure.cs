using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RetailPOS.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4BSalaryComponentStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payroll_salary_components",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    component_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    value_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    tax_treatment_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_recurring = table.Column<bool>(type: "boolean", nullable: false),
                    calculation_order = table.Column<int>(type: "integer", nullable: false),
                    formula_expression = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_salary_components", x => x.id);
                    table.UniqueConstraint("a_k_payroll_salary_components_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_salary_component_formula", "(value_type = 'Formula' AND formula_expression IS NOT NULL) OR (value_type <> 'Formula' AND formula_expression IS NULL)");
                    table.CheckConstraint("ck_payroll_salary_component_order", "calculation_order >= 0 AND version > 0");
                    table.CheckConstraint("ck_payroll_salary_component_type", "component_type IN ('Earning', 'Deduction')");
                    table.CheckConstraint("ck_payroll_salary_component_value_type", "value_type IN ('Fixed', 'Percentage', 'Formula', 'Calculated')");
                    table.ForeignKey(
                        name: "f_k_payroll_salary_components_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_salary_structures",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_salary_structures", x => x.id);
                    table.UniqueConstraint("a_k_payroll_salary_structures_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_payroll_salary_structure_version", "version > 0");
                    table.ForeignKey(
                        name: "f_k_payroll_salary_structures_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_salary_structure_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    business_id = table.Column<long>(type: "bigint", nullable: false),
                    structure_id = table.Column<long>(type: "bigint", nullable: false),
                    salary_component_id = table.Column<long>(type: "bigint", nullable: false),
                    default_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    formula_override = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    calculation_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payroll_salary_structure_lines", x => x.id);
                    table.CheckConstraint("ck_payroll_salary_structure_line_value", "calculation_order >= 0 AND (default_value IS NULL OR default_value >= 0)");
                    table.ForeignKey(
                        name: "f_k_payroll_salary_structure_lines_payroll_salary_components_bu~",
                        columns: x => new { x.business_id, x.salary_component_id },
                        principalTable: "payroll_salary_components",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "f_k_payroll_salary_structure_lines_payroll_salary_structures_bu~",
                        columns: x => new { x.business_id, x.structure_id },
                        principalTable: "payroll_salary_structures",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_components_business_id_code",
                table: "payroll_salary_components",
                columns: new[] { "business_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_structure_lines_business_id_salary_component~",
                table: "payroll_salary_structure_lines",
                columns: new[] { "business_id", "salary_component_id" });

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_structure_lines_business_id_structure_id_sal~",
                table: "payroll_salary_structure_lines",
                columns: new[] { "business_id", "structure_id", "salary_component_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "i_x_payroll_salary_structures_business_id_code",
                table: "payroll_salary_structures",
                columns: new[] { "business_id", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_salary_structure_lines");

            migrationBuilder.DropTable(
                name: "payroll_salary_components");

            migrationBuilder.DropTable(
                name: "payroll_salary_structures");
        }
    }
}
