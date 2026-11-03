using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

internal static class PayrollFinalSettlementExecutionModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PayrollFinalSettlementAccountingPosting>(entity =>
        {
            entity.ToTable("payroll_final_settlement_accounting_postings", table =>
            {
                table.HasCheckConstraint("ck_final_settlement_posting_revision", "revision_no > 0");
                table.HasCheckConstraint("ck_final_settlement_posting_type",
                    "posting_type IN ('FinalSettlementExpense', 'FinalSettlementReceivable', 'FinalSettlementPayment')");
                table.HasCheckConstraint("ck_final_settlement_posting_status",
                    "status IN ('Pending', 'Processing', 'Posted', 'Failed', 'Conflict')");
                table.HasCheckConstraint("ck_final_settlement_posting_hashes",
                    "length(request_payload_hash) = 64 AND "
                    + "(conflict_payload_hash IS NULL OR length(conflict_payload_hash) = 64)");
                table.HasCheckConstraint("ck_final_settlement_posting_version",
                    "attempt_count >= 0 AND version > 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.PostingType).HasMaxLength(40).IsRequired();
            entity.Property(row => row.PostingReference).HasMaxLength(80).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(80).IsRequired();
            entity.Property(row => row.RequestPayloadJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.RequestPayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ConflictPayloadHash).HasMaxLength(64);
            entity.Property(row => row.AccountingReference).HasMaxLength(150);
            entity.Property(row => row.Status).HasMaxLength(30).IsRequired();
            entity.Property(row => row.ErrorMessage).HasMaxLength(1000);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.PostingReference })
                .IsUnique().HasDatabaseName("ux_final_settlement_accounting_posting_reference");
            entity.HasIndex(row => new { row.BusinessId, row.FinalSettlementId })
                .HasDatabaseName("ix_final_settlement_posting_settlement");
            entity.HasIndex(row => new { row.BusinessId, row.RevisionId })
                .HasDatabaseName("ix_final_settlement_posting_revision");
            entity.HasIndex(row => new { row.BusinessId, row.PaymentId })
                .HasDatabaseName("ix_final_settlement_posting_payment");
            entity.HasIndex(row => new { row.BusinessId, row.PaymentEvidenceId })
                .HasDatabaseName("ix_final_settlement_posting_evidence");
            entity.HasIndex(row => new
                {
                    row.BusinessId, row.FinalSettlementId, row.RevisionNo, row.PostingType,
                    row.PaymentId, row.PaymentEvidenceId
                })
                .IsUnique().HasDatabaseName("ux_final_settlement_accounting_posting_identity");
            entity.HasOne<PayrollFinalSettlement>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.FinalSettlementId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .HasConstraintName("fk_final_settlement_posting_settlement")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollFinalSettlementRevision>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, RevisionId = row.RevisionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .HasConstraintName("fk_final_settlement_posting_revision")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollPayment>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, Id = row.PaymentId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .HasConstraintName("fk_final_settlement_posting_payment")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollPaymentEvidence>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, Id = row.PaymentEvidenceId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .HasConstraintName("fk_final_settlement_posting_payment_evidence")
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Core.Entities.Business>().WithMany()
                .HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
