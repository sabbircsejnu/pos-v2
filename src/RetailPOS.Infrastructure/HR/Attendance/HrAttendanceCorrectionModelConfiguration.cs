using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Attendance;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Infrastructure.HR.Attendance;

internal static class HrAttendanceCorrectionModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrAttendanceCorrectionRequest>(entity =>
        {
            entity.ToTable("hr_attendance_correction_requests", table =>
            {
                table.HasCheckConstraint("ck_hr_attendance_correction_sequence",
                    "correction_sequence > 0 AND expected_processing_version > 0 AND version > 0");
                table.HasCheckConstraint("ck_hr_attendance_correction_status",
                    "status IN ('PendingApproval','Approved','Rejected','Cancelled')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.WorkDate).HasColumnType("date");
            entity.Property(row => row.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(500).IsRequired();
            entity.Property(row => row.DecisionReason).HasMaxLength(1000);
            entity.Property(row => row.BeforeEvidenceJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.ProposedEvidenceJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.AppliedAfterEvidenceJson).HasColumnType("jsonb");
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.DailyAttendanceId, row.CorrectionSequence })
                .IsUnique().HasDatabaseName("ux_hr_att_corr_business_daily_sequence");
            entity.HasIndex(row => new { row.BusinessId, row.DailyAttendanceId, row.Status })
                .HasFilter("status = 'PendingApproval'").IsUnique()
                .HasDatabaseName("ux_hr_att_corr_pending_per_daily");
            entity.HasIndex(row => row.ApprovalInstanceId).IsUnique()
                .HasDatabaseName("ux_hr_att_corr_approval_instance");
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrDailyAttendance>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.DailyAttendanceId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.ApprovalInstance).WithMany().HasForeignKey(row => row.ApprovalInstanceId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
