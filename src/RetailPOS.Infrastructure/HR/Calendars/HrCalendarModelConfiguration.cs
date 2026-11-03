using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Calendars;
using RetailPOS.Core.Entities.HR.Organisation;

namespace RetailPOS.Infrastructure.HR.Calendars;

internal static class HrCalendarModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrShift>(e =>
        {
            e.ToTable("hr_shifts", t =>
            {
                t.HasCheckConstraint("ck_hr_shift_version", "version > 0");
                t.HasCheckConstraint("ck_hr_shift_work", "minimum_work_minutes > 0 AND (maximum_allowed_work_minutes IS NULL OR maximum_allowed_work_minutes >= minimum_work_minutes)");
            });
            e.HasKey(r => r.Id);
            e.Property(r => r.Code).HasMaxLength(50).IsRequired();
            e.Property(r => r.Name).HasMaxLength(100).IsRequired();
            e.Property(r => r.Version).IsConcurrencyToken();
            e.HasIndex(r => new { r.BusinessId, r.Code }).IsUnique();
            e.HasOne<Business>().WithMany().HasForeignKey(r => r.BusinessId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(r => r.Breaks).WithOne().HasForeignKey(r => r.ShiftId).OnDelete(DeleteBehavior.Cascade);
        });
        model.Entity<HrShiftBreak>(e =>
        {
            e.ToTable("hr_shift_breaks", t => t.HasCheckConstraint("ck_hr_shift_break", "sequence > 0 AND duration_minutes > 0"));
            e.HasKey(r => r.Id);
            e.HasIndex(r => new { r.ShiftId, r.Sequence }).IsUnique();
        });
        model.Entity<HrHolidayCalendar>(e =>
        {
            e.ToTable("hr_holiday_calendars", t =>
            {
                t.HasCheckConstraint("ck_hr_calendar_dates", "effective_to IS NULL OR effective_to >= effective_from");
                t.HasCheckConstraint("ck_hr_calendar_scope", ScopeConstraint);
                t.HasCheckConstraint("ck_hr_calendar_version", "version > 0");
            });
            e.HasKey(r => r.Id);
            e.HasAlternateKey(r => new { r.BusinessId, r.Id });
            e.Property(r => r.Name).HasMaxLength(150).IsRequired();
            e.Property(r => r.ScopeType).HasMaxLength(20).IsRequired();
            e.Property(r => r.LocationType).HasMaxLength(20);
            e.Property(r => r.Version).IsConcurrencyToken();
            e.HasOne<Business>().WithMany().HasForeignKey(r => r.BusinessId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OperationalLocationProjection>().WithMany()
                .HasForeignKey(r => new { r.BusinessId, r.OperationalLocationProjectionId })
                .HasPrincipalKey(r => new { r.BusinessId, r.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<HrHoliday>(e =>
        {
            e.ToTable("hr_holidays", t => t.HasCheckConstraint("ck_hr_holiday_version", "version > 0"));
            e.HasKey(r => r.Id);
            e.Property(r => r.Code).HasMaxLength(50).IsRequired();
            e.Property(r => r.Name).HasMaxLength(150).IsRequired();
            e.Property(r => r.HolidayType).HasMaxLength(50).IsRequired();
            e.Property(r => r.Notes).HasMaxLength(1000);
            e.Property(r => r.Version).IsConcurrencyToken();
            e.HasIndex(r => new { r.CalendarId, r.Code }).IsUnique();
            e.HasOne<HrHolidayCalendar>().WithMany()
                .HasForeignKey(r => new { r.BusinessId, r.CalendarId })
                .HasPrincipalKey(r => new { r.BusinessId, r.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<HrWeeklyOffPolicyVersion>(e =>
        {
            e.ToTable("hr_weekly_off_policy_versions", t =>
            {
                t.HasCheckConstraint("ck_hr_weekly_dates", "effective_to IS NULL OR effective_to >= effective_from");
                t.HasCheckConstraint("ck_hr_weekly_scope", ScopeConstraint);
                t.HasCheckConstraint("ck_hr_weekly_version", "version > 0 AND version_no > 0");
            });
            e.HasKey(r => r.Id);
            e.Property(r => r.ScopeKey).HasMaxLength(100).IsRequired();
            e.Property(r => r.ScopeType).HasMaxLength(20).IsRequired();
            e.Property(r => r.LocationType).HasMaxLength(20);
            e.Property(r => r.Version).IsConcurrencyToken();
            e.HasIndex(r => new { r.BusinessId, r.ScopeKey, r.VersionNo }).IsUnique();
            e.HasOne<Business>().WithMany().HasForeignKey(r => r.BusinessId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OperationalLocationProjection>().WithMany()
                .HasForeignKey(r => new { r.BusinessId, r.OperationalLocationProjectionId })
                .HasPrincipalKey(r => new { r.BusinessId, r.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(r => r.Days).WithOne().HasForeignKey(r => r.PolicyVersionId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<HrWeeklyOffPolicyDay>(e =>
        {
            e.ToTable("hr_weekly_off_policy_days", t => t.HasCheckConstraint("ck_hr_weekly_day", "day_of_week >= 0 AND day_of_week <= 6"));
            e.HasKey(r => r.Id);
            e.HasIndex(r => new { r.PolicyVersionId, r.DayOfWeek }).IsUnique();
        });
    }

    private const string ScopeConstraint =
        "(scope_type = 'Company' AND location_type IS NULL AND operational_location_projection_id IS NULL)"
        + " OR (scope_type = 'LocationType' AND location_type IS NOT NULL AND location_type IN ('Outlet', 'Warehouse', 'Factory') AND operational_location_projection_id IS NULL)"
        + " OR (scope_type = 'Location' AND location_type IS NULL AND operational_location_projection_id IS NOT NULL AND operational_location_projection_id > 0)";
}
