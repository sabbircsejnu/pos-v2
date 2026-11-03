using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Employees;

namespace RetailPOS.Infrastructure.HR.Attendance;

internal static class HrRawPunchModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrRawAttendancePunch>(e =>
        {
            e.ToTable("hr_raw_attendance_punches", t =>
            {
                t.HasCheckConstraint("ck_hr_raw_punch_source", "source_type IN ('Manual','Device','Import')");
                t.HasCheckConstraint("ck_hr_raw_punch_status", "processing_status IN ('Pending','Unmapped','Processed','Failed')");
                t.HasCheckConstraint("ck_hr_raw_punch_version", "processing_version >= 0");
            });
            e.HasKey(p => p.Id);
            e.HasAlternateKey(p => new { p.BusinessId, p.Id });
            e.Property(p => p.DeviceSerialSnapshot).HasMaxLength(150);
            e.Property(p => p.DeviceUserId).HasMaxLength(100);
            e.Property(p => p.PunchTimeLocal).HasColumnType("timestamp without time zone");
            e.Property(p => p.PunchTimeUtc).HasColumnType("timestamp with time zone");
            e.Property(p => p.ReceivedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
            e.Property(p => p.TimeZoneId).HasMaxLength(100);
            e.Property(p => p.VerificationMethod).HasMaxLength(30).IsRequired();
            e.Property(p => p.DirectionCode).HasMaxLength(40);
            e.Property(p => p.RawPayload).HasMaxLength(4000);
            e.Property(p => p.IdempotencyFingerprint).HasMaxLength(64).IsRequired();
            e.Property(p => p.SourceType).HasMaxLength(20).IsRequired();
            e.Property(p => p.ProcessingStatus).HasMaxLength(20).IsRequired();
            e.Property(p => p.ProcessingError).HasMaxLength(2000);
            e.Property(p => p.ProcessingVersion).IsConcurrencyToken();
            e.HasIndex(p => p.IdempotencyFingerprint).IsUnique();
            e.HasIndex(p => new { p.BusinessId, p.EmployeeId, p.PunchTimeLocal });
            e.HasIndex(p => new { p.BusinessId, p.ProcessingStatus, p.ReceivedAtUtc });
            e.HasOne<Business>().WithMany().HasForeignKey(p => p.BusinessId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<HrAttendanceDevice>().WithMany().HasForeignKey(p => new { p.BusinessId, p.DeviceId })
                .HasPrincipalKey(d => new { d.BusinessId, d.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<HrEmployee>().WithMany().HasForeignKey(p => new { p.BusinessId, p.EmployeeId })
                .HasPrincipalKey(employee => new { employee.BusinessId, employee.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
