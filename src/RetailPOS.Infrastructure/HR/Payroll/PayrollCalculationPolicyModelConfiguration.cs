using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

internal static class PayrollCalculationPolicyModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PayrollCalculationPolicyVersion>(entity =>
        {
            entity.ToTable("payroll_calculation_policy_versions", table =>
            {
                table.HasCheckConstraint("ck_payroll_calculation_policy_effective_period",
                    "effective_to IS NULL OR effective_to >= effective_from");
                table.HasCheckConstraint("ck_payroll_calculation_policy_version",
                    "version_no > 0 AND overtime_divisor_days > 0 AND standard_paid_minutes >= 0");
                table.HasCheckConstraint("ck_payroll_calculation_policy_basis",
                    "partial_month_basis IN ('CalendarDays', 'Fixed30', 'WorkingDays')");
                table.HasCheckConstraint("ck_payroll_calculation_policy_overtime_mode",
                    "overtime_mode IS NULL OR overtime_mode IN ('FixedHourlyRate', 'SalaryBaseMultiplier')");
                table.HasCheckConstraint("ck_payroll_calculation_policy_rates",
                    "(late_per_minute_rate IS NULL OR late_per_minute_rate >= 0) AND " +
                    "(early_leave_per_minute_rate IS NULL OR early_leave_per_minute_rate >= 0) AND " +
                    "(normal_day_overtime_rate IS NULL OR normal_day_overtime_rate >= 0) AND " +
                    "(weekly_off_overtime_rate IS NULL OR weekly_off_overtime_rate >= 0) AND " +
                    "(holiday_overtime_rate IS NULL OR holiday_overtime_rate >= 0) AND " +
                    "(special_day_overtime_rate IS NULL OR special_day_overtime_rate >= 0)");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.PartialMonthBasis).HasMaxLength(20).IsRequired();
            entity.Property(row => row.SalaryBaseComponentIdsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.OvertimeMode).HasMaxLength(30);
            entity.Property(row => row.ConfigurationSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.LatePerMinuteRate).HasColumnType("numeric");
            entity.Property(row => row.EarlyLeavePerMinuteRate).HasColumnType("numeric");
            entity.Property(row => row.NormalDayOvertimeRate).HasColumnType("numeric");
            entity.Property(row => row.WeeklyOffOvertimeRate).HasColumnType("numeric");
            entity.Property(row => row.HolidayOvertimeRate).HasColumnType("numeric");
            entity.Property(row => row.SpecialDayOvertimeRate).HasColumnType("numeric");
            entity.HasIndex(row => new { row.BusinessId, row.VersionNo }).IsUnique();
            entity.HasIndex(row => new { row.BusinessId, row.EffectiveFrom });
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            ConfigureComponent(entity, nameof(PayrollCalculationPolicyVersion.AbsenceComponentId));
            ConfigureComponent(entity, nameof(PayrollCalculationPolicyVersion.UnpaidLeaveComponentId));
            ConfigureComponent(entity, nameof(PayrollCalculationPolicyVersion.LateComponentId));
            ConfigureComponent(entity, nameof(PayrollCalculationPolicyVersion.EarlyLeaveComponentId));
            ConfigureComponent(entity, nameof(PayrollCalculationPolicyVersion.OvertimeComponentId));
            ConfigureComponent(entity, nameof(PayrollCalculationPolicyVersion.TaxStatutoryComponentId));
        });
    }

    private static void ConfigureComponent(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<PayrollCalculationPolicyVersion> entity,
        string componentProperty)
    {
        entity.HasOne<PayrollSalaryComponent>().WithMany()
            .HasForeignKey(nameof(PayrollCalculationPolicyVersion.BusinessId), componentProperty)
            .HasPrincipalKey(nameof(PayrollSalaryComponent.BusinessId), nameof(PayrollSalaryComponent.Id))
            .HasConstraintName($"fk_payroll_calculation_policy_{componentProperty.ToLowerInvariant()}")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
