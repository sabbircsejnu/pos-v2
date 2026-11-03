using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

internal static class PayrollRunCalculationModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PayrollEmployeeResult>(entity =>
        {
            entity.ToTable("payroll_employee_results", table =>
            {
                table.HasCheckConstraint("ck_payroll_employee_result_status",
                    "status IN ('Calculated', 'Exception')");
                table.HasCheckConstraint("ck_payroll_employee_result_calculation_version",
                    "calculation_version > 0");
                table.HasCheckConstraint("ck_payroll_employee_result_amounts",
                    "(status = 'Exception' AND gross_earnings IS NULL AND total_deductions IS NULL " +
                    "AND net_pay IS NULL AND exception_code IS NOT NULL) OR " +
                    "(status = 'Calculated' AND gross_earnings IS NOT NULL AND total_deductions IS NOT NULL " +
                    "AND net_pay IS NOT NULL AND exception_code IS NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.EmployeeCodeSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(row => row.EmployeeNameSnapshot).HasMaxLength(250).IsRequired();
            entity.Property(row => row.CalculationInputHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.CalculationSnapshotJson).HasColumnType("text");
            entity.Property(row => row.CalculationSnapshotHash).HasMaxLength(64);
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.ExceptionCode).HasMaxLength(60);
            entity.Property(row => row.CalculationPolicySnapshotJson).HasColumnType("jsonb");
            entity.Property(row => row.GrossEarnings).HasColumnType("numeric");
            entity.Property(row => row.TotalDeductions).HasColumnType("numeric");
            entity.Property(row => row.NetPay).HasColumnType("numeric");
            entity.HasIndex(row => new
                {
                    row.BusinessId, row.PayrollRunId, row.CalculationVersion, row.EmployeeId
                })
                .IsUnique().HasDatabaseName("ux_payroll_employee_result_run_version_employee");
            entity.HasIndex(row => new { row.BusinessId, row.PayrollRunId, row.EmployeeCodeSnapshot })
                .HasDatabaseName("ix_payroll_employee_result_page");
            entity.HasOne<PayrollRun>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollRunId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Core.Entities.HR.Employees.HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollEmployeeSalaryVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.SalaryVersionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollCalculationPolicyVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.CalculationPolicyVersionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollEmployeeResultLine>(entity =>
        {
            entity.ToTable("payroll_employee_result_lines", table =>
            {
                table.HasCheckConstraint("ck_payroll_employee_result_line_type",
                    "line_type IN ('Earning', 'Deduction')");
                table.HasCheckConstraint("ck_payroll_employee_result_line_amount", "amount >= 0");
                table.HasCheckConstraint("ck_payroll_employee_result_line_order", "calculation_order >= 0");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.ComponentCodeSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(row => row.ComponentNameSnapshot).HasMaxLength(100).IsRequired();
            entity.Property(row => row.LineType).HasMaxLength(20).IsRequired();
            entity.Property(row => row.SourceType).HasMaxLength(30).IsRequired();
            entity.Property(row => row.SourceId).HasMaxLength(100);
            entity.Property(row => row.EvidenceCode).HasMaxLength(60);
            entity.Property(row => row.InputQuantity).HasColumnType("numeric");
            entity.Property(row => row.InputRate).HasColumnType("numeric");
            entity.Property(row => row.InputAmount).HasColumnType("numeric");
            entity.Property(row => row.Amount).HasColumnType("numeric");
            entity.HasIndex(row => new { row.BusinessId, row.PayrollEmployeeResultId, row.CalculationOrder })
                .HasDatabaseName("ix_payroll_employee_result_line_order");
            entity.HasOne(row => row.PayrollEmployeeResult).WithMany(row => row.Lines)
                .HasForeignKey(row => new { row.BusinessId, row.PayrollEmployeeResultId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollSalaryComponent>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.SalaryComponentId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
