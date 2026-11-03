using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Exit;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

internal static class PayrollFinalSettlementModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PayrollFinalSettlement>(entity =>
        {
            entity.ToTable("payroll_final_settlements", table =>
            {
                table.HasCheckConstraint("ck_payroll_final_settlement_business", "business_id > 0");
                table.HasCheckConstraint("ck_payroll_final_settlement_revision", "current_revision_no > 0");
                table.HasCheckConstraint("ck_payroll_final_settlement_version", "version > 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.ExitCaseId })
                .IsUnique().HasDatabaseName("ux_payroll_final_settlement_exit");
            entity.HasOne<HrExitCase>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ExitCaseId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Core.Entities.Business>().WithMany()
                .HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollFinalSettlementRevision>(entity =>
        {
            entity.ToTable("payroll_final_settlement_revisions", table =>
            {
                table.HasCheckConstraint("ck_payroll_final_settlement_revision_number", "revision_no > 0");
                table.HasCheckConstraint("ck_payroll_final_settlement_revision_precision",
                    "decimal_precision BETWEEN 0 AND 6");
                table.HasCheckConstraint("ck_payroll_final_settlement_revision_amounts",
                    "gross_due >= 0 AND total_deductions >= 0 AND net_settlement = gross_due - total_deductions");
                table.HasCheckConstraint("ck_payroll_final_settlement_revision_hashes",
                    "length(calculation_input_hash) = 64 AND length(snapshot_hash) = 64 "
                    + "AND length(idempotency_payload_hash) = 64");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.SettlementDate).HasColumnType("date");
            entity.Property(row => row.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(row => row.RoundingMode).HasMaxLength(40).IsRequired();
            entity.Property(row => row.GrossDue).HasColumnType("numeric");
            entity.Property(row => row.TotalDeductions).HasColumnType("numeric");
            entity.Property(row => row.NetSettlement).HasColumnType("numeric");
            entity.Property(row => row.CalculationInputHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.SnapshotHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.SnapshotJson).HasColumnType("text").IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.IdempotencyPayloadHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.FinalSettlementId, row.RevisionNo })
                .IsUnique().HasDatabaseName("ux_payroll_final_settlement_revision_number");
            entity.HasIndex(row => new { row.BusinessId, row.IdempotencyKey })
                .IsUnique().HasDatabaseName("ux_payroll_final_settlement_idempotency");
            entity.HasOne<PayrollFinalSettlement>().WithMany(row => row.Revisions)
                .HasForeignKey(row => new { row.BusinessId, row.FinalSettlementId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollFinalSettlementLine>(entity =>
        {
            entity.ToTable("payroll_final_settlement_lines", table =>
            {
                table.HasCheckConstraint("ck_payroll_final_settlement_line_number", "line_no > 0");
                table.HasCheckConstraint("ck_payroll_final_settlement_line_type",
                    "line_type IN ('Earning', 'Deduction', 'NotApplicable')");
                table.HasCheckConstraint("ck_payroll_final_settlement_line_amount",
                    "amount >= 0 AND (is_applicable OR (amount = 0 AND line_type = 'NotApplicable'))");
                table.HasCheckConstraint("ck_payroll_final_settlement_line_reason",
                    "(is_applicable AND not_applicable_reason IS NULL) OR "
                    + "(NOT is_applicable AND not_applicable_reason IS NOT NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.ComponentCode).HasMaxLength(100).IsRequired();
            entity.Property(row => row.ComponentName).HasMaxLength(200).IsRequired();
            entity.Property(row => row.LineType).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Amount).HasColumnType("numeric");
            entity.Property(row => row.NotApplicableReason).HasMaxLength(500);
            entity.Property(row => row.SourceType).HasMaxLength(50).IsRequired();
            entity.Property(row => row.SourceId).HasMaxLength(200);
            entity.Property(row => row.EvidenceSnapshotJson).HasColumnType("text").IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.RevisionId, row.LineNo })
                .IsUnique().HasDatabaseName("ux_payroll_final_settlement_line_number");
            entity.HasOne<PayrollFinalSettlementRevision>().WithMany(row => row.Lines)
                .HasForeignKey(row => new { row.BusinessId, RevisionId = row.RevisionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollFinalSettlementApprovalSubmission>(entity =>
        {
            entity.ToTable("payroll_final_settlement_approval_submissions", table =>
            {
                table.HasCheckConstraint("ck_payroll_final_settlement_submission_hashes",
                    "length(calculation_snapshot_hash) = 64 AND length(component_lines_hash) = 64 "
                    + "AND length(source_evidence_hash) = 64 AND length(clearance_references_hash) = 64 "
                    + "AND length(payload_hash) = 64 AND length(submitted_evidence_hash) = 64");
                table.HasCheckConstraint("ck_payroll_final_settlement_submission_amounts",
                    "gross_payable >= 0 AND total_recovery >= 0 "
                    + "AND net_settlement = gross_payable - total_recovery");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.CalculationSnapshotHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ComponentLinesHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(row => row.SourceEvidenceHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.CalculationPolicyVersion).HasMaxLength(100).IsRequired();
            entity.Property(row => row.ClearanceReferencesJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.ClearanceReferencesHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.SubmittedEvidenceJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.SubmittedEvidenceHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.GrossPayable).HasColumnType("numeric");
            entity.Property(row => row.TotalRecovery).HasColumnType("numeric");
            entity.Property(row => row.NetSettlement).HasColumnType("numeric");
            entity.HasIndex(row => new { row.BusinessId, row.IdempotencyKey })
                .IsUnique().HasDatabaseName("ux_payroll_final_settlement_submission_idempotency");
            entity.HasIndex(row => new { row.BusinessId, row.RevisionId })
                .IsUnique().HasDatabaseName("ux_payroll_final_settlement_submission_revision");
            entity.HasIndex(row => new { row.BusinessId, row.ApprovalInstanceId })
                .IsUnique().HasFilter("approval_instance_id IS NOT NULL")
                .HasDatabaseName("ux_payroll_final_settlement_submission_approval");
            entity.HasOne<PayrollFinalSettlementRevision>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, RevisionId = row.RevisionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .HasConstraintName("fk_payroll_final_settlement_submission_revision")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollFinalSettlement>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.FinalSettlementId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .HasConstraintName("fk_payroll_final_settlement_submission_settlement")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApprovalInstance>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, Id = row.ApprovalInstanceId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .HasConstraintName("fk_payroll_final_settlement_submission_approval")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Core.Entities.Business>().WithMany()
                .HasForeignKey(row => row.BusinessId)
                .HasConstraintName("fk_payroll_final_settlement_submission_business")
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
