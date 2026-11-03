using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

internal static class PayrollLoanModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PayrollEmployeeLoan>(entity =>
        {
            entity.ToTable("payroll_employee_loans", table =>
            {
                table.HasCheckConstraint("ck_payroll_employee_loan_amounts",
                    "CAST(principal_amount AS NUMERIC) > 0 " +
                    "AND CAST(principal_amount AS NUMERIC) <= 99999999999999.9999 " +
                    "AND (approved_amount IS NULL OR (CAST(approved_amount AS NUMERIC) > 0 " +
                    "AND CAST(approved_amount AS NUMERIC) <= CAST(principal_amount AS NUMERIC))) " +
                    "AND CAST(outstanding_amount AS NUMERIC) >= 0 " +
                    "AND CAST(outstanding_amount AS NUMERIC) <= COALESCE(" +
                    "CAST(approved_amount AS NUMERIC), CAST(principal_amount AS NUMERIC)) " +
                    "AND version > 0");
                table.HasCheckConstraint("ck_payroll_employee_loan_status",
                    "status IN ('Draft', 'PendingApproval', 'Active', 'Rejected', 'Cancelled', 'Settled')");
                table.HasCheckConstraint("ck_payroll_employee_loan_recovery_mode",
                    "recovery_mode IN ('FixedMonthly', 'Manual', 'CustomSchedule')");
                table.HasCheckConstraint("ck_payroll_employee_loan_opening_balance",
                    "(is_opening_balance = FALSE AND opening_disbursed_date IS NULL AND opening_balance_amount IS NULL) OR " +
                    "(is_opening_balance = TRUE AND opening_disbursed_date IS NOT NULL " +
                    "AND CAST(opening_balance_amount AS NUMERIC) >= 0 " +
                    "AND CAST(opening_balance_amount AS NUMERIC) <= CAST(principal_amount AS NUMERIC))");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.LoanType).HasMaxLength(20).IsRequired();
            entity.Property(row => row.PrincipalAmount).HasPrecision(18, 4);
            entity.Property(row => row.ApprovedAmount).HasPrecision(18, 4);
            entity.Property(row => row.OpeningBalanceAmount).HasPrecision(18, 4);
            entity.Property(row => row.FixedInstallment).HasPrecision(18, 4);
            entity.Property(row => row.OutstandingAmount).HasPrecision(18, 4);
            entity.Property(row => row.RecoveryMode).HasMaxLength(24).IsRequired();
            entity.Property(row => row.ProposedScheduleJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.IdempotencyPayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.Reference).HasMaxLength(150);
            entity.Property(row => row.Status).HasMaxLength(24).IsRequired();
            entity.Property(row => row.DecisionReason).HasMaxLength(1000);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.CreatedByUserId, row.EmployeeId, row.IdempotencyKey })
                .IsUnique();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.CreatedAtUtc });
            entity.HasIndex(row => row.ApprovalInstanceId).IsUnique()
                .HasFilter("approval_instance_id IS NOT NULL");
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.ApprovalInstance).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ApprovalInstanceId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollLoanScheduleVersion>(entity =>
        {
            entity.ToTable("payroll_loan_schedule_versions", table =>
                table.HasCheckConstraint("ck_payroll_loan_schedule_version_number", "version_no > 0"));
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.LoanId, row.Id });
            entity.Property(row => row.RecoveryMode).HasMaxLength(24).IsRequired();
            entity.Property(row => row.FixedInstallment).HasPrecision(18, 4);
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.IdempotencyPayloadHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.LoanId, row.VersionNo }).IsUnique();
            entity.HasIndex(row => new
            {
                row.BusinessId, row.LoanId, row.CreatedByUserId, row.IdempotencyKey
            }).IsUnique();
            entity.HasOne<PayrollEmployeeLoan>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.LoanId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(row => row.Lines).WithOne().HasForeignKey(row => new
                { row.BusinessId, row.LoanId, row.ScheduleVersionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.LoanId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollLoanScheduleLine>(entity =>
        {
            entity.ToTable("payroll_loan_schedule_lines", table =>
            {
                table.HasCheckConstraint("ck_payroll_loan_schedule_line_values",
                    "installment_no > 0 AND planned_amount > 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new
                { row.BusinessId, row.LoanId, row.ScheduleVersionId, row.InstallmentNo });
            entity.Property(row => row.PlannedAmount).HasPrecision(18, 4);
            entity.HasIndex(row => new { row.BusinessId, row.LoanId, row.ScheduleVersionId, row.DueDate })
                .IsUnique();
        });

        model.Entity<PayrollLoanRepayment>(entity =>
        {
            entity.ToTable("payroll_loan_repayments", table =>
                table.HasCheckConstraint("ck_payroll_loan_repayment_amount", "amount > 0"));
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.LoanId, row.Id });
            entity.Property(row => row.Amount).HasPrecision(18, 4);
            entity.Property(row => row.Reference).HasMaxLength(150);
            entity.Property(row => row.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.IdempotencyPayloadHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.LoanId, row.RecordedByUserId, row.IdempotencyKey })
                .IsUnique();
            entity.HasOne<PayrollEmployeeLoan>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.LoanId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(row => row.Allocations).WithOne().HasForeignKey(row => new
                { row.BusinessId, row.LoanId, row.RepaymentId })
                .HasPrincipalKey(row => new { row.BusinessId, row.LoanId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollLoanRepaymentAllocation>(entity =>
        {
            entity.ToTable("payroll_loan_repayment_allocations", table =>
                table.HasCheckConstraint("ck_payroll_loan_repayment_allocation_amount", "amount > 0"));
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Amount).HasPrecision(18, 4);
            entity.HasIndex(row => new
                { row.BusinessId, row.LoanId, row.RepaymentId, row.ScheduleVersionId, row.InstallmentNo })
                .IsUnique();
            entity.HasOne<PayrollLoanScheduleLine>().WithMany()
                .HasForeignKey(row => new
                    { row.BusinessId, row.LoanId, row.ScheduleVersionId, row.InstallmentNo })
                .HasPrincipalKey(row => new
                    { row.BusinessId, row.LoanId, row.ScheduleVersionId, row.InstallmentNo })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
