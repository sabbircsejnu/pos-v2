using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

internal static class PayrollPeriodModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PayrollPeriod>(entity =>
        {
            entity.ToTable("payroll_periods", table =>
            {
                table.HasCheckConstraint("ck_payroll_period_dates", "period_start <= period_end");
                table.HasCheckConstraint("ck_payroll_period_status",
                    "status IN ('Open', 'Processing', 'Locked', 'Approved', 'Posted', 'Closed')");
                table.HasCheckConstraint("ck_payroll_period_version", "version > 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.PeriodCode).HasMaxLength(30).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.ReopenReason).HasMaxLength(1000);
            entity.Property(row => row.ClosedReason).HasMaxLength(1000);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.PeriodStart, row.PeriodEnd })
                .IsUnique().HasDatabaseName("ux_payroll_period_business_dates");
            entity.HasIndex(row => new { row.BusinessId, row.PeriodCode })
                .IsUnique().HasDatabaseName("ux_payroll_period_business_code");
            entity.HasIndex(row => new { row.BusinessId, row.Status, row.PeriodStart })
                .HasDatabaseName("ix_payroll_period_business_status_start");
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollPeriodCloseAssessment>(entity =>
        {
            entity.ToTable("payroll_period_close_assessments", table =>
            {
                table.HasCheckConstraint("ck_payroll_period_close_assessment_version",
                    "period_version > 0 AND result_period_version >= period_version");
                table.HasCheckConstraint("ck_payroll_period_close_assessment_policy", "policy_version > 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.StatusSnapshot).HasMaxLength(20).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.GateResultsJson).IsRequired();
            entity.Property(row => row.RunRevisionRefsJson).IsRequired();
            entity.Property(row => row.PaymentStateVersionsJson).IsRequired();
            entity.Property(row => row.PostingStateVersionsJson).IsRequired();
            entity.Property(row => row.ExceptionsJson).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(1000);
            entity.HasIndex(row => new { row.BusinessId, row.PayrollPeriodId, row.IdempotencyKey })
                .IsUnique().HasDatabaseName("ux_payroll_period_close_assessment_idempotency");
            entity.HasIndex(row => new { row.BusinessId, row.PayrollPeriodId, row.PeriodVersion })
                .HasDatabaseName("ix_payroll_period_close_assessment_history");
            entity.HasOne<PayrollPeriod>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollPeriodId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Core.Entities.Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollPeriodReopenEvent>(entity =>
        {
            entity.ToTable("payroll_period_reopen_events", table =>
            {
                table.HasCheckConstraint("ck_payroll_period_reopen_event_versions",
                    "previous_period_version > 0 AND result_period_version = previous_period_version + 1");
                table.HasCheckConstraint("ck_payroll_period_reopen_event_purpose",
                    "purpose = 'Correction' AND original_payroll_run_id IS NOT NULL " +
                    "AND correction_payroll_run_id IS NOT NULL");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.PreviousStatus).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.Purpose).HasMaxLength(30).IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.PayrollPeriodId, row.ResultPeriodVersion })
                .IsUnique().HasDatabaseName("ux_payroll_period_reopen_event_version");
            entity.HasOne<PayrollPeriod>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollPeriodId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollRun>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.OriginalPayrollRunId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollRun>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.CorrectionPayrollRunId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
