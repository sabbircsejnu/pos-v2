using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;
using RetailPOS.Core.Entities.HR.Overtime;

namespace RetailPOS.Infrastructure.HR.Overtime;

internal static class HrOvertimeModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrOvertimePolicyVersion>(entity =>
        {
            entity.ToTable("hr_overtime_policy_versions", table =>
            {
                table.HasCheckConstraint("ck_hr_overtime_policy_business", "business_id > 0");
                table.HasCheckConstraint("ck_hr_overtime_policy_version", "version_no > 0");
                table.HasCheckConstraint("ck_hr_overtime_policy_row_version", "version > 0");
                table.HasCheckConstraint("ck_hr_overtime_policy_minimum", "minimum_eligible_minutes >= 0");
                table.HasCheckConstraint("ck_hr_overtime_policy_maximum",
                    "maximum_eligible_minutes IS NULL OR maximum_eligible_minutes >= minimum_eligible_minutes");
                table.HasCheckConstraint("ck_hr_overtime_policy_rounding", "rounding_unit_minutes > 0");
                table.HasCheckConstraint("ck_hr_overtime_policy_dates",
                    "effective_to IS NULL OR effective_to >= effective_from");
                table.HasCheckConstraint("ck_hr_overtime_policy_scope",
                    "(scope_type = 'Business' AND scope_reference IS NULL) OR "
                    + "(scope_type <> 'Business' AND scope_reference IS NOT NULL)");
                table.HasCheckConstraint("ck_hr_overtime_policy_rates",
                    "normal_day_rate >= 0 AND weekly_off_rate >= 0 AND holiday_rate >= 0 AND special_day_rate >= 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.Code).HasMaxLength(100).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(150).IsRequired();
            entity.Property(row => row.ScopeType).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(row => row.ScopeReference).HasMaxLength(150);
            entity.Property(row => row.EffectiveFrom).HasColumnType("date");
            entity.Property(row => row.EffectiveTo).HasColumnType("date");
            entity.Property(row => row.RoundingMethod).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(row => row.RateType).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(row => row.NormalDayRate).HasPrecision(12, 6);
            entity.Property(row => row.WeeklyOffRate).HasPrecision(12, 6);
            entity.Property(row => row.HolidayRate).HasPrecision(12, 6);
            entity.Property(row => row.SpecialDayRate).HasPrecision(12, 6);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.Code, row.VersionNo }).IsUnique();
            entity.HasIndex(row => new
            {
                row.BusinessId, row.ScopeType, row.ScopeReference, row.EffectiveFrom, row.EffectiveTo
            });
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrOvertimeEntry>(entity =>
        {
            entity.ToTable("hr_overtime_entries", table =>
            {
                table.HasCheckConstraint("ck_hr_overtime_business", "business_id > 0");
                table.HasCheckConstraint("ck_hr_overtime_work_date", "work_date <> '0001-01-01'");
                table.HasCheckConstraint("ck_hr_overtime_minutes",
                    "eligible_minutes >= 0 AND calculated_minutes >= 0 "
                    + "AND (approved_minutes IS NULL OR approved_minutes >= 0)");
                table.HasCheckConstraint("ck_hr_overtime_version",
                    "attendance_processing_version > 0 AND calculation_version > 0 AND version > 0");
                table.HasCheckConstraint("ck_hr_overtime_status",
                    "status IN ('Calculated','PendingApproval','PayrollEligible','Rejected','Cancelled','Superseded')");
                table.HasCheckConstraint("ck_hr_overtime_approved",
                    "approved_minutes IS NULL OR approved_minutes <= calculated_minutes");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.WorkDate).HasColumnType("date");
            entity.Property(row => row.DayCategory).HasMaxLength(30).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(30).IsRequired();
            entity.Property(row => row.CalculationFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.PolicySnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.AttendanceSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.CalculationInputsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.DailyAttendanceId }).IsUnique();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.WorkDate }).IsUnique();
            entity.HasIndex(row => new { row.BusinessId, row.WorkDate, row.Status });
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(employee => new { employee.BusinessId, employee.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrDailyAttendance>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.DailyAttendanceId })
                .HasPrincipalKey(attendance => new { attendance.BusinessId, attendance.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationalLocationProjection>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.OperationalLocationProjectionId })
                .HasPrincipalKey(location => new { location.BusinessId, location.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrOvertimePolicyVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PolicyVersionId })
                .HasPrincipalKey(policy => new { policy.BusinessId, policy.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.ApprovalInstance).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ApprovalInstanceId })
                .HasPrincipalKey(approval => new { approval.BusinessId, approval.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrOvertimeCalculationEvidence>(entity =>
        {
            entity.ToTable("hr_overtime_calculation_evidence", table =>
            {
                table.HasCheckConstraint("ck_hr_overtime_evidence_version",
                    "calculation_version > 0 AND attendance_processing_version > 0");
                table.HasCheckConstraint("ck_hr_overtime_evidence_minutes",
                    "eligible_minutes >= 0 AND calculated_minutes >= 0");
                table.HasCheckConstraint("ck_hr_overtime_evidence_actor", "created_by_user_id > 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.DayCategory).HasMaxLength(30).IsRequired();
            entity.Property(row => row.CalculationFingerprint).HasMaxLength(64).IsRequired();
            entity.Property(row => row.PolicySnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.AttendanceSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.CalculationInputsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.OvertimeEntryId, row.CalculationVersion }).IsUnique();
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrOvertimeEntry>().WithMany(row => row.CalculationEvidence)
                .HasForeignKey(row => new { row.BusinessId, row.OvertimeEntryId })
                .HasPrincipalKey(entry => new { entry.BusinessId, entry.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrOvertimePolicyVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PolicyVersionId })
                .HasPrincipalKey(policy => new { policy.BusinessId, policy.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
