using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Core.Entities.HR.Organisation;

namespace RetailPOS.Infrastructure.HR.Exit;

internal static class HrExitModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrClearanceTemplate>(entity =>
        {
            entity.ToTable("hr_clearance_templates", table =>
            {
                table.HasCheckConstraint("ck_hr_clearance_template_business", "business_id > 0");
                table.HasCheckConstraint("ck_hr_clearance_template_version", "version_no > 0");
                table.HasCheckConstraint("ck_hr_clearance_template_dates",
                    "effective_to IS NULL OR effective_to >= effective_from");
                table.HasCheckConstraint("ck_hr_clearance_template_scope",
                    "(scope_type = 'Business' AND scope_reference IS NULL) OR "
                    + "(scope_type <> 'Business' AND scope_reference IS NOT NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.Code).HasMaxLength(50).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(100).IsRequired();
            entity.Property(row => row.ScopeType).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(row => row.ScopeReference).HasMaxLength(150);
            entity.Property(row => row.EffectiveFrom).HasColumnType("date");
            entity.Property(row => row.EffectiveTo).HasColumnType("date");
            entity.HasIndex(row => new { row.BusinessId, row.Code, row.VersionNo }).IsUnique();
            entity.HasIndex(row => new
            {
                row.BusinessId, row.ScopeType, row.ScopeReference, row.EffectiveFrom, row.EffectiveTo
            });
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrClearanceTemplateItem>(entity =>
        {
            entity.ToTable("hr_clearance_template_items", table =>
            {
                table.HasCheckConstraint("ck_hr_clearance_template_item_sequence", "sequence > 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.Code).HasMaxLength(50).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(100).IsRequired();
            entity.Property(row => row.ResponsibleRole).HasMaxLength(100);
            entity.Property(row => row.ResponsibleReference).HasMaxLength(150);
            entity.HasIndex(row => new { row.BusinessId, row.TemplateId, row.Sequence }).IsUnique();
            entity.HasIndex(row => new { row.BusinessId, row.TemplateId, row.Code }).IsUnique();
            entity.HasOne<HrClearanceTemplate>().WithMany(row => row.Items)
                .HasForeignKey(row => new { row.BusinessId, row.TemplateId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrExitCase>(entity =>
        {
            entity.ToTable("hr_exit_cases", table =>
            {
                table.HasCheckConstraint("ck_hr_exit_case_business", "business_id > 0");
                table.HasCheckConstraint("ck_hr_exit_case_dates",
                    "proposed_last_working_date >= initiated_date AND "
                    + "(actual_last_working_date IS NULL OR actual_last_working_date >= initiated_date)");
                table.HasCheckConstraint("ck_hr_exit_case_version", "version > 0");
                table.HasCheckConstraint("ck_hr_exit_case_template_version", "clearance_template_version_no > 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.ExitType).HasMaxLength(50).IsRequired();
            entity.Property(row => row.InitiatedDate).HasColumnType("date");
            entity.Property(row => row.ProposedLastWorkingDate).HasColumnType("date");
            entity.Property(row => row.ActualLastWorkingDate).HasColumnType("date");
            entity.Property(row => row.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(30).IsRequired();
            entity.Property(row => row.LocationTypeSnapshot).HasMaxLength(30);
            entity.Property(row => row.ClearanceTemplateSnapshotJson).HasColumnType("text").IsRequired();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.Status });
            entity.HasIndex(row => new { row.BusinessId, row.InitiatedDate, row.Status });
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.Employee).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrClearanceTemplate>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ClearanceTemplateId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationalLocationProjection>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PrimaryLocationProjectionIdSnapshot })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.ApprovalInstance).WithMany()
                .HasForeignKey(row => row.ApprovalInstanceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrExitCompletionEvidence>(entity =>
        {
            entity.ToTable("hr_exit_completion_evidence", table =>
            {
                table.HasCheckConstraint("ck_hr_exit_completion_business", "business_id > 0");
                table.HasCheckConstraint("ck_hr_exit_completion_version", "exit_version > 0");
                table.HasCheckConstraint("ck_hr_exit_completion_mode",
                    "completion_mode IN ('HrOnly', 'PositiveSettlement', 'ZeroSettlement', 'NegativeSettlement')");
                table.HasCheckConstraint("ck_hr_exit_completion_hashes",
                    "length(evidence_hash) = 64");
                table.HasCheckConstraint("ck_hr_exit_completion_financial",
                    "(payroll_enabled = FALSE AND final_settlement_id IS NULL AND final_settlement_revision_id IS NULL "
                    + "AND final_settlement_revision_no IS NULL AND final_settlement_status IS NULL "
                    + "AND final_settlement_evidence_hash IS NULL AND net_settlement IS NULL AND currency_code IS NULL) OR "
                    + "(payroll_enabled = TRUE AND final_settlement_id IS NOT NULL AND final_settlement_revision_id IS NOT NULL "
                    + "AND final_settlement_revision_no IS NOT NULL AND final_settlement_status IS NOT NULL "
                    + "AND final_settlement_evidence_hash IS NOT NULL AND net_settlement IS NOT NULL AND currency_code IS NOT NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.CompletionMode).HasMaxLength(40).IsRequired();
            entity.Property(row => row.FinalSettlementStatus).HasMaxLength(30);
            entity.Property(row => row.FinalSettlementEvidenceHash).HasMaxLength(64);
            entity.Property(row => row.NetSettlement).HasColumnType("numeric");
            entity.Property(row => row.CurrencyCode).HasMaxLength(3);
            entity.Property(row => row.EvidenceJson).HasColumnType("text").IsRequired();
            entity.Property(row => row.EvidenceHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.ExitCaseId })
                .IsUnique().HasDatabaseName("ux_hr_exit_completion_exit");
            entity.HasOne(row => row.ExitCase).WithOne(row => row.CompletionEvidence)
                .HasForeignKey<HrExitCompletionEvidence>(row => new { row.BusinessId, row.ExitCaseId })
                .HasPrincipalKey<HrExitCase>(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrClearanceItem>(entity =>
        {
            entity.ToTable("hr_clearance_items", table =>
            {
                table.HasCheckConstraint("ck_hr_clearance_item_sequence", "sequence > 0");
                table.HasCheckConstraint("ck_hr_clearance_item_version", "version > 0");
                table.HasCheckConstraint("ck_hr_clearance_item_status",
                    "status IN ('Pending', 'Cleared', 'Exception', 'NotApplicable')");
                table.HasCheckConstraint("ck_hr_clearance_item_clear_fields",
                    "(status = 'Cleared' AND cleared_by_user_id IS NOT NULL AND cleared_at_utc IS NOT NULL) OR "
                    + "(status <> 'Cleared' AND cleared_by_user_id IS NULL AND cleared_at_utc IS NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.Code).HasMaxLength(50).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(100).IsRequired();
            entity.Property(row => row.ResponsibleRole).HasMaxLength(100);
            entity.Property(row => row.ResponsibleReference).HasMaxLength(150);
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Comment).HasMaxLength(1000);
            entity.Property(row => row.EvidenceReference).HasMaxLength(500);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.ExitCaseId, row.Sequence }).IsUnique();
            entity.HasOne<HrExitCase>().WithMany(row => row.ClearanceItems)
                .HasForeignKey(row => new { row.BusinessId, row.ExitCaseId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.TemplateItem).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.TemplateItemId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrExitCaseEvent>(entity =>
        {
            entity.ToTable("hr_exit_case_events", table =>
            {
                table.HasCheckConstraint("ck_hr_exit_event_sequence", "sequence > 0");
                table.HasCheckConstraint("ck_hr_exit_event_actor", "actor_user_id > 0");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.EventType).HasMaxLength(40).IsRequired();
            entity.Property(row => row.PreviousStatus).HasMaxLength(30);
            entity.Property(row => row.NewStatus).HasMaxLength(30).IsRequired();
            entity.Property(row => row.ActualLastWorkingDate).HasColumnType("date");
            entity.Property(row => row.Reason).HasMaxLength(1000);
            entity.HasIndex(row => new { row.BusinessId, row.ExitCaseId, row.Sequence }).IsUnique();
            entity.HasOne<HrExitCase>().WithMany(row => row.Events)
                .HasForeignKey(row => new { row.BusinessId, row.ExitCaseId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
