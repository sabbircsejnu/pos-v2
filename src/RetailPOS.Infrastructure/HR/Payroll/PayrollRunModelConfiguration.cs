using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

internal static class PayrollRunModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PayrollRun>(entity =>
        {
            entity.ToTable("payroll_runs", table =>
            {
                table.HasCheckConstraint("ck_payroll_run_no", "run_no > 0");
                table.HasCheckConstraint("ck_payroll_run_version", "version > 0");
                table.HasCheckConstraint("ck_payroll_run_calculation_version", "calculation_version >= 0");
                table.HasCheckConstraint("ck_payroll_run_scope",
                    "(scope_type = 'Business' AND scope_id IS NULL AND scope_key = 'BUSINESS') OR " +
                    "(scope_type = 'Location' AND scope_id IS NOT NULL AND scope_id > 0)");
                table.HasCheckConstraint("ck_payroll_run_type",
                    "run_type IN ('Regular', 'Correction', 'OffCycle')");
                table.HasCheckConstraint("ck_payroll_run_status",
                    "status IN ('Draft', 'PendingApproval', 'Approved', 'Rejected', 'PaymentInProgress', 'Paid', 'Completed', 'Cancelled')");
                table.HasCheckConstraint("ck_payroll_run_precision",
                    "decimal_precision IS NULL OR decimal_precision BETWEEN 0 AND 6");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.RunType).HasMaxLength(20).IsRequired();
            entity.Property(row => row.ScopeType).HasMaxLength(30).IsRequired();
            entity.Property(row => row.ScopeKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(30).IsRequired();
            entity.Property(row => row.CurrencyCode).HasMaxLength(3);
            entity.Property(row => row.RoundingMode).HasMaxLength(40);
            entity.Property(row => row.CalculationInputHash).HasMaxLength(64);
            entity.Property(row => row.ApprovalEvidenceHash).HasMaxLength(64);
            entity.Property(row => row.DecisionReason).HasMaxLength(1000);
            entity.Property(row => row.RunReference).HasMaxLength(150);
            entity.Property(row => row.CorrectionReference).HasMaxLength(150);
            entity.Property(row => row.OriginalEvidenceHash).HasMaxLength(64);
            entity.Property(row => row.CorrectionType).HasMaxLength(40);
            entity.Property(row => row.CorrectionReason).HasMaxLength(1000);
            entity.Property(row => row.AffectedEmployeeIdsSnapshotJson);
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.IdempotencyPayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.PayrollPeriodId, row.RunNo })
                .IsUnique().HasDatabaseName("ux_payroll_run_period_no");
            entity.HasIndex(row => new { row.PayrollPeriodId, row.RunType, row.ScopeKey })
                .IsUnique().HasFilter("run_type = 'Regular'")
                .HasDatabaseName("ux_payroll_run_period_type_scope");
            entity.HasIndex(row => new { row.BusinessId, row.CorrectionReference })
                .IsUnique().HasFilter("correction_reference IS NOT NULL")
                .HasDatabaseName("ux_payroll_run_business_correction_reference");
            entity.HasIndex(row => new { row.BusinessId, row.RunReference })
                .IsUnique().HasFilter("run_type = 'OffCycle' AND run_reference IS NOT NULL")
                .HasDatabaseName("ux_payroll_run_business_offcycle_reference");
            entity.HasIndex(row => new
                {
                    row.BusinessId, row.CreatedByUserId, row.PayrollPeriodId, row.IdempotencyKey
                })
                .IsUnique().HasDatabaseName("ux_payroll_run_idempotency");
            entity.HasIndex(row => new { row.BusinessId, row.PayrollPeriodId, row.Status })
                .HasDatabaseName("ix_payroll_run_business_period_status");
            entity.HasOne<PayrollPeriod>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollPeriodId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Core.Entities.Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollMonetaryConfigurationVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.MonetaryConfigurationVersionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollRun>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.OriginalPayrollRunId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollRunEmployeeSelection>(entity =>
        {
            entity.ToTable("payroll_run_employee_selections", table =>
            {
                table.HasCheckConstraint("ck_payroll_run_employee_selection_outcome",
                    "(is_included AND exclusion_reason_code IS NULL AND salary_version_id IS NOT NULL) OR " +
                    "((NOT is_included) AND exclusion_reason_code IS NOT NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.EmployeeCodeSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(row => row.EmployeeNameSnapshot).HasMaxLength(250).IsRequired();
            entity.Property(row => row.EmploymentStatusSnapshot).HasMaxLength(30);
            entity.Property(row => row.ExclusionReasonCode).HasMaxLength(50);
            entity.HasIndex(row => new { row.BusinessId, row.PayrollRunId, row.EmployeeId })
                .IsUnique().HasDatabaseName("ux_payroll_run_employee_selection");
            entity.HasIndex(row => new { row.BusinessId, row.PayrollRunId, row.IsIncluded, row.EmployeeCodeSnapshot })
                .HasDatabaseName("ix_payroll_run_employee_selection_page");
            entity.HasOne<PayrollRun>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollRunId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Core.Entities.HR.Employees.HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollEmployeeSalaryVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.SalaryVersionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
