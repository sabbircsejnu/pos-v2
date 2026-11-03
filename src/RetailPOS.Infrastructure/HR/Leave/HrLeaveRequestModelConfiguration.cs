using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Core.Entities.HR.Organisation;

namespace RetailPOS.Infrastructure.HR.Leave;

internal static class HrLeaveRequestModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrLeaveRequest>(entity =>
        {
            entity.ToTable("hr_leave_requests", table =>
            {
                table.HasCheckConstraint("ck_hr_leave_request_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_leave_request_dates", "end_date >= start_date");
                table.HasCheckConstraint("ck_hr_leave_request_units", "requested_units > 0");
                table.HasCheckConstraint("ck_hr_leave_request_status",
                    "status IN ('Draft', 'PendingApproval', 'Approved', 'Rejected', 'Cancelled')");
                table.HasCheckConstraint("ck_hr_leave_request_version", "version > 0");
                table.HasCheckConstraint("ck_hr_leave_request_submitted_version",
                    "submitted_target_version IS NULL OR submitted_target_version > 0");
                table.HasCheckConstraint("ck_hr_leave_request_actor", "requested_by_user_id > 0");
                table.HasCheckConstraint("ck_hr_leave_request_decision",
                    "(decided_by_user_id IS NULL AND decided_at_utc IS NULL) OR "
                    + "(decided_by_user_id IS NOT NULL AND decided_at_utc IS NOT NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.StartDate).HasColumnType("date");
            entity.Property(row => row.EndDate).HasColumnType("date");
            entity.Property(row => row.RequestedUnits).HasPrecision(12, 6);
            entity.Property(row => row.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(30).IsRequired();
            entity.Property(row => row.DecisionReason).HasMaxLength(1000);
            entity.Property(row => row.RequestHash).HasMaxLength(64);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.StartDate, row.Status })
                .HasDatabaseName("ix_hr_leave_request_employee_dates_status");
            entity.HasIndex(row => new { row.BusinessId, row.ApprovalInstanceId }).IsUnique()
                .HasFilter("approval_instance_id IS NOT NULL")
                .HasDatabaseName("ux_hr_leave_request_approval_instance");
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(employee => new { employee.BusinessId, employee.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrLeaveType>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.LeaveTypeId })
                .HasPrincipalKey(type => new { type.BusinessId, type.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationalLocationProjection>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ScopeLocationProjectionId })
                .HasPrincipalKey(location => new { location.BusinessId, location.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.ApprovalInstance).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ApprovalInstanceId })
                .HasPrincipalKey(approval => new { approval.BusinessId, approval.Id })
                .HasConstraintName("fk_hr_leave_request_approval_instance")
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrLeaveRequestDay>(entity =>
        {
            entity.ToTable("hr_leave_request_days", table =>
            {
                table.HasCheckConstraint("ck_hr_leave_request_day_units", "units >= 0");
                table.HasCheckConstraint("ck_hr_leave_request_day_classification",
                    "day_classification IN ('NormalWorkingDay', 'Holiday', 'WeeklyOff')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.WorkDate).HasColumnType("date");
            entity.Property(row => row.Units).HasPrecision(12, 6);
            entity.Property(row => row.DayClassification).HasMaxLength(30).IsRequired();
            entity.Property(row => row.ShiftSnapshotJson).HasColumnType("text");
            entity.Property(row => row.CalendarEvidenceJson).HasColumnType("text").IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.LeaveRequestId, row.WorkDate }).IsUnique()
                .HasDatabaseName("ux_hr_leave_request_day_date");
            entity.HasOne<HrLeaveRequest>().WithMany(row => row.Days)
                .HasForeignKey(row => new { row.BusinessId, row.LeaveRequestId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(employee => new { employee.BusinessId, employee.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrLeavePolicyVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PolicyVersionId })
                .HasPrincipalKey(policy => new { policy.BusinessId, policy.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationalLocationProjection>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.OperationalLocationProjectionId })
                .HasPrincipalKey(location => new { location.BusinessId, location.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrLeaveRequestAttachmentVersion>(entity =>
        {
            entity.ToTable("hr_leave_request_attachment_versions", table =>
            {
                table.HasCheckConstraint("ck_hr_leave_request_attachment_version_no", "version_no > 0");
                table.HasCheckConstraint("ck_hr_leave_request_attachment_size", "size_bytes > 0");
                table.HasCheckConstraint("ck_hr_leave_request_attachment_status",
                    "scan_status IN ('Pending', 'Clean', 'Rejected', 'Error')");
                table.HasCheckConstraint("ck_hr_leave_request_attachment_actor", "uploaded_by_user_id > 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(row => row.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(row => row.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ScanStatus).HasMaxLength(20).IsRequired();
            entity.Property(row => row.EncryptionKeyReference).HasMaxLength(500).IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.LeaveRequestId, row.VersionNo }).IsUnique()
                .HasDatabaseName("ux_hr_leave_request_attachment_version");
            entity.HasOne<HrLeaveRequest>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.LeaveRequestId })
                .HasPrincipalKey(request => new { request.BusinessId, request.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
