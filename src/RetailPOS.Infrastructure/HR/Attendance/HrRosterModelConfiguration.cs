using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Calendars;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;

namespace RetailPOS.Infrastructure.HR.Attendance;

internal static class HrRosterModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrRosterAssignment>(e =>
        {
            e.ToTable("hr_roster_assignments", t =>
            {
                t.HasCheckConstraint("ck_hr_roster_version", "version > 0");
                t.HasCheckConstraint("ck_hr_roster_status", "status IN ('Draft','Published','Cancelled')");
                t.HasCheckConstraint("ck_hr_roster_publication",
                    "status <> 'Published' OR (published_at_utc IS NOT NULL AND published_by_user_id IS NOT NULL AND shift_snapshot_json IS NOT NULL AND location_snapshot_json IS NOT NULL AND calendar_evidence_json IS NOT NULL)");
                t.HasCheckConstraint("ck_hr_roster_kind", "work_kind IN ('Normal','Extended','Special')");
                t.HasCheckConstraint("ck_hr_roster_day", "day_classification IN ('NormalWorkingDay','Holiday','WeeklyOff')");
            });
            e.HasKey(r => r.Id);
            e.Property(r => r.Status).HasMaxLength(20).IsRequired();
            e.Property(r => r.Source).HasMaxLength(20).IsRequired();
            e.Property(r => r.WorkKind).HasMaxLength(20).IsRequired();
            e.Property(r => r.DayClassification).HasMaxLength(30).IsRequired();
            e.Property(r => r.Version).IsConcurrencyToken();
            e.HasIndex(r => new { r.EmployeeId, r.WorkDate }).IsUnique().HasFilter("status <> 'Cancelled'");
            e.HasOne<Business>().WithMany().HasForeignKey(r => r.BusinessId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<HrEmployee>().WithMany().HasForeignKey(r => new { r.BusinessId, r.EmployeeId })
                .HasPrincipalKey(r => new { r.BusinessId, r.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OperationalLocationProjection>().WithMany().HasForeignKey(r => new { r.BusinessId, r.OperationalLocationProjectionId })
                .HasPrincipalKey(r => new { r.BusinessId, r.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<HrShift>().WithMany().HasForeignKey(r => r.ShiftId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
