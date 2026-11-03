using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;

namespace RetailPOS.Infrastructure.HR.Employees;

internal static class HrEmployeeHistoryModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrEmployeeUserLink>(entity =>
        {
            entity.ToTable("hr_employee_user_links", table =>
            {
                table.HasCheckConstraint("ck_hr_employee_user_link_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_employee_user_link_identity_user_id", "identity_user_id > 0");
                table.HasCheckConstraint("ck_hr_employee_user_link_linked_by_user_id", "linked_by_user_id > 0");
                table.HasCheckConstraint("ck_hr_employee_user_link_version", "version > 0");
                table.HasCheckConstraint("ck_hr_employee_user_link_state",
                    "(is_active = true AND unlinked_at_utc IS NULL AND unlinked_by_user_id IS NULL "
                    + "AND unlink_reason IS NULL) OR (is_active = false AND unlinked_at_utc IS NOT NULL "
                    + "AND unlinked_by_user_id IS NOT NULL AND unlink_reason IS NOT NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.LinkReason).HasMaxLength(500).IsRequired();
            entity.Property(row => row.UnlinkReason).HasMaxLength(500);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId })
                .IsUnique().HasFilter("is_active = true");
            entity.HasIndex(row => row.IdentityUserId)
                .IsUnique().HasFilter("is_active = true");
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.LinkedAtUtc, row.Id });
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrEmployeeLifecycleEvent>(entity =>
        {
            entity.ToTable("hr_employee_lifecycle_events", table =>
            {
                table.HasCheckConstraint("ck_hr_employee_lifecycle_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_employee_lifecycle_effective_date",
                    "effective_date IS NOT NULL");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.EventType).HasMaxLength(40).IsRequired();
            entity.Property(row => row.PreviousStateJson).IsRequired();
            entity.Property(row => row.NewStateJson).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(1000);
            entity.Property(row => row.OccurredAtUtc).IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.EffectiveDate, row.Id });
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.Employee).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.EmploymentPeriod).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId, row.EmploymentPeriodId })
                .HasPrincipalKey(row => new { row.BusinessId, row.EmployeeId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrEmployeePositionAssignment>(entity =>
        {
            entity.ToTable("hr_employee_position_assignments", table =>
            {
                table.HasCheckConstraint("ck_hr_employee_position_assignment_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_employee_position_assignment_dates",
                    "effective_to IS NULL OR effective_to >= effective_from");
                table.HasCheckConstraint("ck_hr_employee_position_assignment_not_self_manager",
                    "reports_to_employee_id IS NULL OR reports_to_employee_id <> employee_id");
                table.HasCheckConstraint("ck_hr_employee_position_assignment_version", "version > 0");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.AssignmentType).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(500);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.EffectiveFrom, row.EffectiveTo });
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId })
                .IsUnique()
                .HasFilter("is_current = true AND assignment_type = 'Primary'");
            entity.HasIndex(row => new { row.BusinessId, row.PositionId, row.EffectiveFrom });
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrPosition>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PositionId, row.OrgUnitId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id, row.OrgUnitId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrOrgUnit>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.OrgUnitId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ReportsToEmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrEmployeeLocationAssignment>(entity =>
        {
            entity.ToTable("hr_employee_location_assignments", table =>
            {
                table.HasCheckConstraint("ck_hr_employee_location_assignment_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_employee_location_assignment_dates",
                    "effective_to IS NULL OR effective_to >= effective_from");
                table.HasCheckConstraint("ck_hr_employee_location_assignment_type",
                    "assignment_type IN ('Primary', 'Additional', 'Temporary')");
                table.HasCheckConstraint("ck_hr_employee_location_assignment_status",
                    "status IN ('Effective', 'Closed')");
                table.HasCheckConstraint("ck_hr_employee_location_assignment_version", "version > 0");
                table.HasCheckConstraint("ck_hr_employee_location_assignment_approval",
                    "(approved_by_user_id IS NULL AND approved_at_utc IS NULL AND approval_instance_id IS NULL) "
                    + "OR (approved_by_user_id IS NOT NULL AND approved_at_utc IS NOT NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.LocationNameSnapshot).HasMaxLength(150).IsRequired();
            entity.Property(row => row.SourceSystemSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(row => row.SourceEntityTypeSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(row => row.SourceEntityIdSnapshot).HasMaxLength(80).IsRequired();
            entity.Property(row => row.AssignmentType).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(500).IsRequired();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.EffectiveFrom, row.EffectiveTo })
                .HasDatabaseName("ix_hr_emp_loc_assign_emp_dates");
            entity.HasIndex(row => new
                    { row.BusinessId, row.EmployeeId, row.OperationalLocationProjectionId, row.Status, row.EffectiveFrom })
                .HasDatabaseName("ix_hr_emp_loc_assign_emp_location_status");
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId })
                .IsUnique().HasFilter("assignment_type = 'Primary' AND status = 'Effective'")
                .HasDatabaseName("ux_hr_emp_loc_assign_active_primary");
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationalLocationProjection>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.OperationalLocationProjectionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.ApprovalInstance).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ApprovalInstanceId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .HasConstraintName("fk_hr_employee_location_assignment_approval_instance")
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrEmployeeLocationTransferRequest>(entity =>
        {
            entity.ToTable("hr_employee_location_transfer_requests", table =>
            {
                table.HasCheckConstraint("ck_hr_employee_location_transfer_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_employee_location_transfer_status",
                    "status IN ('PendingApproval', 'Approved', 'ApprovedPendingEffective', 'Effective', 'Rejected', 'Cancelled', 'Superseded')");
                table.HasCheckConstraint("ck_hr_employee_location_transfer_version", "version > 0");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.LocationNameSnapshot).HasMaxLength(150).IsRequired();
            entity.Property(row => row.SourceSystemSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(row => row.SourceEntityTypeSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(row => row.SourceEntityIdSnapshot).HasMaxLength(80).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(500).IsRequired();
            entity.Property(row => row.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(30).IsRequired();
            entity.Property(row => row.DecisionReason).HasMaxLength(1000);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.RequestedAtUtc })
                .HasDatabaseName("ix_hr_emp_loc_transfer_emp_requested");
            entity.HasIndex(row => new { row.BusinessId, row.ApprovalInstanceId }).IsUnique()
                .HasFilter("approval_instance_id IS NOT NULL")
                .HasDatabaseName("ux_hr_emp_loc_transfer_approval");
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId }).IsUnique()
                .HasFilter("status IN ('PendingApproval', 'ApprovedPendingEffective')")
                .HasDatabaseName("ux_hr_emp_loc_transfer_pending_emp");
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationalLocationProjection>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.TargetLocationProjectionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.ApprovalInstance).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ApprovalInstanceId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .HasConstraintName("fk_hr_employee_location_transfer_approval_instance")
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrDocumentType>(entity =>
        {
            entity.ToTable("hr_document_types", table =>
            {
                table.HasCheckConstraint("ck_hr_document_type_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_document_type_retention_days",
                    "retention_period_days IS NULL OR retention_period_days > 0");
                table.HasCheckConstraint("ck_hr_document_type_version", "version > 0");
                table.HasCheckConstraint("ck_hr_document_type_classification",
                    "classification IN ('Internal', 'Confidential', 'Restricted')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Code).HasMaxLength(50).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(150).IsRequired();
            entity.Property(row => row.Classification).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.HasIndex(row => new { row.BusinessId, row.Code }).IsUnique();
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrEmployeeDocument>(entity =>
        {
            entity.ToTable("hr_employee_documents", table =>
            {
                table.HasCheckConstraint("ck_hr_employee_document_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_employee_document_dates",
                    "expiry_date IS NULL OR issue_date IS NULL OR expiry_date >= issue_date");
                table.HasCheckConstraint("ck_hr_employee_document_status",
                    "status IN ('Active', 'Expired', 'Replaced', 'Archived')");
                table.HasCheckConstraint("ck_hr_employee_document_version", "version > 0");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.DocumentNumber).HasMaxLength(100);
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Notes).HasMaxLength(1000);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.Status, row.ExpiryDate });
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.DocumentType).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.DocumentTypeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployeeDocumentVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.Id, row.CurrentVersionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.EmployeeDocumentId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrEmployeeDocumentVersion>(entity =>
        {
            entity.ToTable("hr_employee_document_versions", table =>
            {
                table.HasCheckConstraint("ck_hr_employee_document_version_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_employee_document_version_no", "version_no > 0");
                table.HasCheckConstraint("ck_hr_employee_document_version_size", "size_bytes > 0");
                table.HasCheckConstraint("ck_hr_employee_document_version_hash",
                    "length(content_hash) = 64");
                table.HasCheckConstraint("ck_hr_employee_document_version_scan_status",
                    "scan_status IN ('Pending', 'Clean', 'Rejected', 'Error')");
                table.HasCheckConstraint("ck_hr_employee_document_version_storage_key",
                    "scan_status <> 'Clean' OR storage_key IS NOT NULL");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.StorageKey).HasMaxLength(500);
            entity.Property(row => row.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(row => row.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(row => row.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ScanStatus).HasMaxLength(20).IsRequired();
            entity.Property(row => row.EncryptionKeyReference).HasMaxLength(500).IsRequired();
            entity.HasAlternateKey(row => new { row.BusinessId, row.EmployeeDocumentId, row.Id });
            entity.HasIndex(row => new { row.EmployeeDocumentId, row.VersionNo }).IsUnique();
            entity.HasIndex(row => new { row.BusinessId, row.StorageKey }).IsUnique();
            entity.HasOne<HrEmployeeDocument>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeDocumentId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployeeDocumentVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeDocumentId, row.ReplacesVersionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.EmployeeDocumentId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ApprovalInstance>().HasAlternateKey(row => new { row.BusinessId, row.Id });
    }
}
