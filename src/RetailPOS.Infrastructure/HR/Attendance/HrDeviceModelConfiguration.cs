using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;

namespace RetailPOS.Infrastructure.HR.Attendance;

internal static class HrDeviceModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrAttendanceDevice>(e =>
        {
            e.ToTable("hr_attendance_devices", t =>
            {
                t.HasCheckConstraint("ck_hr_device_version", "version > 0");
                t.HasCheckConstraint("ck_hr_device_status", "status IN ('Active','Offline','Disabled','Error')");
                t.HasCheckConstraint("ck_hr_device_port", "connection_port IS NULL OR connection_port BETWEEN 1 AND 65535");
            });
            e.HasKey(d => d.Id); e.HasAlternateKey(d => new { d.BusinessId, d.Id });
            e.Property(d => d.DeviceCode).HasMaxLength(50).IsRequired();
            e.Property(d => d.SerialNumber).HasMaxLength(150).IsRequired();
            e.Property(d => d.VendorCode).HasMaxLength(50).IsRequired();
            e.Property(d => d.Model).HasMaxLength(100).IsRequired();
            e.Property(d => d.ProtocolType).HasMaxLength(20).IsRequired();
            e.Property(d => d.ConnectionHost).HasMaxLength(253);
            e.Property(d => d.CredentialSecretReference).HasMaxLength(100);
            e.Property(d => d.FirmwareVersion).HasMaxLength(100);
            e.Property(d => d.DeviceTimeZoneId).HasMaxLength(100).IsRequired();
            e.Property(d => d.Status).HasMaxLength(20).IsRequired();
            e.Property(d => d.Version).IsConcurrencyToken();
            e.HasIndex(d => new { d.VendorCode, d.SerialNumber }).IsUnique();
            e.HasIndex(d => new { d.BusinessId, d.DeviceCode }).IsUnique();
            e.HasOne<Business>().WithMany().HasForeignKey(d => d.BusinessId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OperationalLocationProjection>().WithMany().HasForeignKey(d => new { d.BusinessId, d.OperationalLocationProjectionId })
                .HasPrincipalKey(d => new { d.BusinessId, d.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<HrDeviceUserMapping>(e =>
        {
            e.ToTable("hr_device_user_mappings", t =>
            {
                t.HasCheckConstraint("ck_hr_mapping_interval", "effective_to_utc IS NULL OR effective_to_utc > effective_from_utc");
                t.HasCheckConstraint("ck_hr_mapping_open", "is_active = (effective_to_utc IS NULL)");
                t.HasCheckConstraint("ck_hr_mapping_version", "version > 0");
            });
            e.HasKey(m => m.Id); e.Property(m => m.DeviceUserId).HasMaxLength(100).IsRequired();
            e.Property(m => m.Version).IsConcurrencyToken();
            e.HasIndex(m => new { m.DeviceId, m.DeviceUserId }).IsUnique().HasFilter("is_active");
            e.HasIndex(m => new { m.EmployeeId, m.IsActive });
            e.HasOne<HrAttendanceDevice>().WithMany().HasForeignKey(m => new { m.BusinessId, m.DeviceId })
                .HasPrincipalKey(d => new { d.BusinessId, d.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<HrEmployee>().WithMany().HasForeignKey(m => new { m.BusinessId, m.EmployeeId })
                .HasPrincipalKey(e => new { e.BusinessId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
