using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

internal static class PayrollAccountingPostingModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PayrollAccountingPosting>(entity =>
        {
            entity.ToTable("payroll_accounting_postings", table =>
            {
                table.HasCheckConstraint("ck_payroll_accounting_posting_revision", "run_revision > 0");
                table.HasCheckConstraint("ck_payroll_accounting_posting_attempts", "attempt_count >= 0");
                table.HasCheckConstraint("ck_payroll_accounting_posting_version", "version > 0");
                table.HasCheckConstraint("ck_payroll_accounting_posting_type",
                    "posting_type IN ('PayrollExpense', 'SalaryPayment', 'PayrollCorrection')");
                table.HasCheckConstraint("ck_payroll_accounting_posting_settlement_identity",
                    "(posting_type = 'PayrollExpense' AND payroll_payment_id IS NULL " +
                    "AND payroll_payment_allocation_id IS NULL AND payroll_payment_evidence_id IS NULL " +
                    "AND payroll_payment_reconciliation_id IS NULL) OR " +
                    "(posting_type = 'SalaryPayment' AND payroll_payment_id IS NOT NULL " +
                    "AND payroll_payment_allocation_id IS NOT NULL AND payroll_payment_evidence_id IS NOT NULL " +
                    "AND (status <> 'Posted' OR payroll_payment_reconciliation_id IS NOT NULL)) OR " +
                    "(posting_type = 'PayrollCorrection' AND payroll_payment_id IS NULL " +
                    "AND payroll_payment_allocation_id IS NULL AND payroll_payment_evidence_id IS NULL " +
                    "AND payroll_payment_reconciliation_id IS NULL AND correction_reference IS NOT NULL " +
                    "AND original_posting_id IS NOT NULL AND correction_policy_version IS NOT NULL)");
                table.HasCheckConstraint("ck_payroll_accounting_posting_status",
                    "status IN ('Pending', 'Processing', 'Posted', 'Failed', 'Conflict')");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.PostingType).HasMaxLength(30).IsRequired();
            entity.Property(row => row.PostingReference).HasMaxLength(80).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(80).IsRequired();
            entity.Property(row => row.RequestPayloadJson).HasColumnType("text");
            entity.Property(row => row.RequestPayloadHash).HasMaxLength(64);
            entity.Property(row => row.ConflictPayloadHash).HasMaxLength(64);
            entity.Property(row => row.AccountingReference).HasMaxLength(80);
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.ErrorMessage).HasMaxLength(1000);
            entity.Property(row => row.CorrectionReference).HasMaxLength(80);
            entity.Property(row => row.CorrectionPolicyVersion).HasMaxLength(80);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.PostingReference })
                .IsUnique().HasDatabaseName("ux_payroll_accounting_posting_reference");
            entity.HasIndex(row => new
                {
                    row.BusinessId, row.PayrollRunId, row.RunRevision, row.PostingType
                })
                .IsUnique()
                .HasFilter("posting_type IN ('PayrollExpense', 'PayrollCorrection')")
                .HasDatabaseName("ux_payroll_accounting_posting_run_revision");
            entity.HasIndex(row => new { row.BusinessId, row.OriginalPostingId })
                .IsUnique()
                .HasFilter("posting_type = 'PayrollCorrection'")
                .HasDatabaseName("ux_payroll_accounting_correction_original_posting");
            entity.HasIndex(row => new
                {
                    row.BusinessId, row.PayrollPaymentEvidenceId, row.PayrollPaymentAllocationId
                })
                .IsUnique()
                .HasFilter("posting_type = 'SalaryPayment'")
                .HasDatabaseName("ux_payroll_accounting_settlement_identity");
            entity.HasOne<PayrollRun>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollRunId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollPeriod>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollPeriodId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollPaymentBatch>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PaymentBatchId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollPayment>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollPaymentId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollPaymentAllocation>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollPaymentAllocationId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollPaymentEvidence>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollPaymentEvidenceId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollPaymentReconciliation>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollPaymentReconciliationId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollAccountingPosting>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.OriginalPostingId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany()
                .HasForeignKey(row => row.ReversalJournalId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Core.Entities.Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollAccountingComponentMapping>(entity =>
        {
            entity.ToTable("payroll_accounting_component_mappings", table =>
            {
                table.HasCheckConstraint("ck_payroll_accounting_mapping_component_type",
                    "component_type IN ('Earning', 'Deduction', 'NetPayable', 'FinalSettlementReceivable')");
                table.HasCheckConstraint("ck_payroll_accounting_mapping_version", "version > 0");
                table.HasCheckConstraint("ck_payroll_accounting_mapping_code",
                    "(component_type = 'NetPayable' AND component_code = 'NET_PAYABLE') OR " +
                    "(component_type = 'FinalSettlementReceivable' AND component_code = 'FINAL_SETTLEMENT_RECEIVABLE') OR " +
                    "(component_type IN ('Earning', 'Deduction') AND length(component_code) > 0)");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.ComponentType).HasMaxLength(40).IsRequired();
            entity.Property(row => row.ComponentCode).HasMaxLength(50).IsRequired();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.ComponentType, row.ComponentCode })
                .IsUnique().HasDatabaseName("ux_payroll_accounting_component_mapping");
            entity.HasOne<Core.Entities.Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Core.Entities.ChartAccount>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.AccountId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
