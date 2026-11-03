using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Calendars;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;

namespace RetailPOS.Infrastructure.HR.Attendance;

internal static class HrDailyAttendanceModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrDailyAttendance>(e =>
        {
            e.ToTable("hr_daily_attendance", t =>
            {
                t.HasCheckConstraint("ck_hr_daily_attendance_minutes",
                    "worked_minutes >= 0 AND break_minutes >= 0 AND late_minutes >= 0 AND early_leave_minutes >= 0 AND overtime_minutes_calculated >= 0");
                t.HasCheckConstraint("ck_hr_daily_attendance_version", "processing_version > 0");
                t.HasCheckConstraint("ck_hr_daily_attendance_status",
                    "status IN ('Present','Absent','Late','HalfDay','Leave','Holiday','WeeklyOff','MissingPunch')");
            });
            e.HasKey(a => a.Id);
            e.HasAlternateKey(a => new { a.BusinessId, a.Id });
            e.Property(a => a.WorkDate).HasColumnType("date");
            e.Property(a => a.SourceLocationTypeSnapshot).HasMaxLength(30).IsRequired();
            e.Property(a => a.TimeZoneIdSnapshot).HasMaxLength(100);
            e.Property(a => a.FirstInAtLocal).HasColumnType("timestamp without time zone");
            e.Property(a => a.LastOutAtLocal).HasColumnType("timestamp without time zone");
            e.Property(a => a.Status).HasMaxLength(20).IsRequired();
            e.Property(a => a.ProcessingVersion).IsConcurrencyToken();
            e.Property(a => a.LastProcessedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
            e.Property(a => a.ShiftSnapshotJson).HasColumnType("jsonb").IsRequired();
            e.Property(a => a.LocationSnapshotJson).HasColumnType("jsonb").IsRequired();
            e.Property(a => a.CalendarEvidenceJson).HasColumnType("jsonb").IsRequired();
            e.Property(a => a.PunchIdsJson).HasColumnType("jsonb").IsRequired();
            e.Property(a => a.CalculationFingerprint).HasMaxLength(64).IsRequired();
            e.HasIndex(a => new { a.BusinessId, a.EmployeeId, a.WorkDate }).IsUnique();
            e.HasIndex(a => new { a.BusinessId, a.WorkDate, a.Status });
            e.HasOne<Business>().WithMany().HasForeignKey(a => a.BusinessId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<HrEmployee>().WithMany().HasForeignKey(a => new { a.BusinessId, a.EmployeeId })
                .HasPrincipalKey(employee => new { employee.BusinessId, employee.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OperationalLocationProjection>().WithMany()
                .HasForeignKey(a => new { a.BusinessId, a.OperationalLocationProjectionId })
                .HasPrincipalKey(location => new { location.BusinessId, location.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<HrRosterAssignment>().WithMany().HasForeignKey(a => new { a.BusinessId, a.RosterAssignmentId })
                .HasPrincipalKey(roster => new { roster.BusinessId, roster.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<HrShift>().WithMany().HasForeignKey(a => new { a.BusinessId, a.ShiftId })
                .HasPrincipalKey(shift => new { shift.BusinessId, shift.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
