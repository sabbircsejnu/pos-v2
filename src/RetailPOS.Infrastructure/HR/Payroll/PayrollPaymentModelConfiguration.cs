using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

internal static class PayrollPaymentModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PayrollPayment>(entity =>
        {
            entity.ToTable("payroll_payments", table =>
            {
                table.HasCheckConstraint("ck_payroll_payment_method",
                    "method IN ('Cash', 'Bank', 'Mobile', 'Mixed')");
                table.HasCheckConstraint("ck_payroll_payment_status",
                    "status IN ('Pending', 'Ready', 'Processing', 'PartiallyPaid', 'Paid', 'Held', 'Failed', 'Returned', 'Cancelled')");
                table.HasCheckConstraint("ck_payroll_payment_amounts",
                    "payable_amount > 0 AND paid_amount >= 0 AND outstanding_amount >= 0 " +
                    "AND paid_amount + outstanding_amount = payable_amount");
                table.HasCheckConstraint("ck_payroll_payment_source",
                    "(payroll_period_id IS NOT NULL AND payroll_run_id IS NOT NULL " +
                    "AND payroll_employee_result_id IS NOT NULL AND final_settlement_id IS NULL " +
                    "AND final_settlement_revision_id IS NULL AND final_settlement_revision_no IS NULL) OR " +
                    "(payroll_period_id IS NULL AND payroll_run_id IS NULL " +
                    "AND payroll_employee_result_id IS NULL AND final_settlement_id IS NOT NULL " +
                    "AND final_settlement_revision_id IS NOT NULL AND final_settlement_revision_no > 0 " +
                    "AND approval_instance_id IS NOT NULL AND historical_location_id_snapshot > 0 " +
                    "AND decimal_precision_snapshot BETWEEN 0 AND 6 " +
                    "AND rounding_mode_snapshot IS NOT NULL AND length(final_settlement_evidence_hash) = 64)");
                table.HasCheckConstraint("ck_payroll_payment_version_attempt",
                    "version > 0 AND attempt_no > 0");
                table.HasCheckConstraint("ck_payroll_payment_hash",
                    "length(idempotency_payload_hash) = 64");
                table.HasCheckConstraint("ck_payroll_payment_resolution_status",
                    "resolution_status IN ('None', 'PendingInvestigation', 'ResolvedNonSettlement', 'ResolvedReturn', 'ResolvedReissue')");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(row => row.Method).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(30).IsRequired();
            entity.Property(row => row.PayableAmount).HasColumnType("numeric");
            entity.Property(row => row.PaidAmount).HasColumnType("numeric");
            entity.Property(row => row.OutstandingAmount).HasColumnType("numeric");
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.IdempotencyPayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.RoundingModeSnapshot).HasMaxLength(40);
            entity.Property(row => row.FinalSettlementEvidenceHash).HasMaxLength(64);
            entity.Property(row => row.Notes).HasMaxLength(1000);
            entity.Property(row => row.ExternalReference).HasMaxLength(150);
            entity.Property(row => row.FailureReturnReason).HasMaxLength(1000);
            entity.Property(row => row.HoldReason).HasMaxLength(1000);
            entity.Property(row => row.ReleaseReason).HasMaxLength(1000);
            entity.Property(row => row.ResolutionStatus).HasMaxLength(30).IsRequired()
                .HasDefaultValue(PayrollPaymentResolutionStatuses.None);
            entity.Property(row => row.ResolutionEvidenceReference).HasMaxLength(150);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.IdempotencyKey })
                .IsUnique().HasDatabaseName("ux_payroll_payment_idempotency");
            entity.HasIndex(row => new { row.BusinessId, row.PayrollEmployeeResultId, row.AttemptNo })
                .IsUnique().HasFilter("payroll_employee_result_id IS NOT NULL")
                .HasDatabaseName("ux_payroll_payment_result_attempt");
            entity.HasIndex(row => new
                {
                    row.BusinessId, row.FinalSettlementId, row.FinalSettlementRevisionNo, row.AttemptNo
                })
                .IsUnique().HasFilter("final_settlement_id IS NOT NULL")
                .HasDatabaseName("ux_payroll_payment_final_settlement_attempt");
            entity.HasIndex(row => new { row.BusinessId, row.PayrollRunId, row.EmployeeId, row.Status })
                .HasDatabaseName("ix_payroll_payment_run_employee_status");
            entity.HasOne<PayrollPeriod>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollPeriodId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollRun>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollRunId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollEmployeeResult>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollEmployeeResultId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollFinalSettlement>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.FinalSettlementId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollFinalSettlementRevision>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, RevisionId = row.FinalSettlementRevisionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Core.Entities.HR.Approvals.ApprovalInstance>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, Id = row.ApprovalInstanceId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.PaymentBatch).WithMany(batch => batch.Payments)
                .HasForeignKey(row => new { row.BusinessId, row.PaymentBatchId })
                .HasPrincipalKey(batch => new { batch.BusinessId, batch.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollPayment>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.OriginalPaymentId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Core.Entities.Business>().WithMany()
                .HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollPaymentEvidence>(entity =>
        {
            entity.ToTable("payroll_payment_evidence", table =>
            {
                table.HasCheckConstraint("ck_payroll_payment_evidence_type",
                    "type IN ('Settlement', 'Return')");
                table.HasCheckConstraint("ck_payroll_payment_evidence_amount",
                    "amount > 0");
                table.HasCheckConstraint("ck_payroll_payment_evidence_hash",
                    "length(payload_hash) = 64");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.Type).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Amount).HasColumnType("numeric");
            entity.Property(row => row.EvidenceReference).HasMaxLength(150).IsRequired();
            entity.Property(row => row.AllocationEvidenceJson).HasColumnType("text").IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.PayloadHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new
                {
                    row.BusinessId,
                    row.PayrollPaymentId,
                    row.IdempotencyKey
                })
                .IsUnique().HasDatabaseName("ux_payroll_payment_evidence_idempotency");
            entity.HasIndex(row => new
                {
                    row.BusinessId,
                    row.PayrollPaymentId,
                    row.Type,
                    row.EvidenceReference
                })
                .IsUnique().HasDatabaseName("ux_payroll_payment_evidence_reference");
            entity.HasOne<PayrollPayment>().WithMany(payment => payment.Evidence)
                .HasForeignKey(row => new { row.BusinessId, row.PayrollPaymentId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollPaymentReconciliation>(entity =>
        {
            entity.ToTable("payroll_payment_reconciliations", table =>
            {
                table.HasCheckConstraint("ck_payroll_payment_reconciliation_status",
                    "status IN ('Unreconciled', 'PartiallyReconciled', 'Reconciled', 'Exception')");
                table.HasCheckConstraint("ck_payroll_payment_reconciliation_amounts",
                    "version > 0 AND payment_version > 0 AND expected_amount > 0 " +
                    "AND settled_amount >= 0 AND settled_amount <= expected_amount " +
                    "AND difference = expected_amount - settled_amount");
                table.HasCheckConstraint("ck_payroll_payment_reconciliation_hash",
                    "length(payload_hash) = 64");
                table.HasCheckConstraint("ck_payroll_payment_reconciliation_source",
                    "evidence_source IN ('Cash', 'Bank', 'Mobile')");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.ExpectedAmount).HasColumnType("numeric");
            entity.Property(row => row.SettledAmount).HasColumnType("numeric");
            entity.Property(row => row.Difference).HasColumnType("numeric");
            entity.Property(row => row.Status).HasMaxLength(30).IsRequired();
            entity.Property(row => row.EvidenceSource).HasMaxLength(20).IsRequired();
            entity.Property(row => row.ExternalReference).HasMaxLength(150);
            entity.Property(row => row.Notes).HasMaxLength(1000);
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.PayloadHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new
                {
                    row.BusinessId,
                    row.PayrollPaymentId,
                    row.PayrollPaymentAllocationId,
                    row.Version
                })
                .IsUnique().HasDatabaseName("ux_payroll_payment_reconciliation_version");
            entity.HasIndex(row => new
                {
                    row.BusinessId,
                    row.IdempotencyKey,
                    row.PayrollPaymentId,
                    row.PayrollPaymentAllocationId
                })
                .IsUnique().HasDatabaseName("ux_payroll_payment_reconciliation_idempotency");
            entity.HasIndex(row => new
                {
                    row.BusinessId,
                    row.PayrollPaymentId,
                    row.PayrollPaymentAllocationId,
                    row.ExternalReference
                })
                .IsUnique()
                .HasFilter("external_reference IS NOT NULL")
                .HasDatabaseName("ux_payroll_payment_reconciliation_reference");
            entity.HasIndex(row => new
                {
                    row.BusinessId,
                    row.PaymentBatchId,
                    row.ReconciledAtUtc
                })
                .HasDatabaseName("ix_payroll_payment_reconciliation_batch");
            entity.HasOne<PayrollPayment>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollPaymentId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollPaymentAllocation>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollPaymentAllocationId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollPaymentBatch>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PaymentBatchId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollPaymentAllocation>(entity =>
        {
            entity.ToTable("payroll_payment_allocations", table =>
            {
                table.HasCheckConstraint("ck_payroll_payment_allocation_method",
                    "method IN ('Cash', 'Bank', 'Mobile')");
                table.HasCheckConstraint("ck_payroll_payment_allocation_state",
                    "state IN ('Pending', 'PartiallySettled', 'Settled')");
                table.HasCheckConstraint("ck_payroll_payment_allocation_amount",
                    "allocation_no > 0 AND amount > 0 AND settled_amount >= 0 AND settled_amount <= amount");
                table.HasCheckConstraint("ck_payroll_payment_allocation_destination",
                    "(method = 'Cash' AND payment_destination_id IS NULL AND payment_destination_version_no IS NULL " +
                    "AND bank_name_snapshot IS NULL AND account_name_snapshot IS NULL AND account_number_snapshot IS NULL " +
                    "AND mobile_provider_snapshot IS NULL AND mobile_account_name_snapshot IS NULL AND mobile_account_number_snapshot IS NULL) OR " +
                    "(method = 'Bank' AND payment_destination_id IS NOT NULL AND payment_destination_version_no IS NOT NULL " +
                    "AND bank_name_snapshot IS NOT NULL AND account_name_snapshot IS NOT NULL AND account_number_snapshot IS NOT NULL " +
                    "AND mobile_provider_snapshot IS NULL AND mobile_account_name_snapshot IS NULL AND mobile_account_number_snapshot IS NULL) OR " +
                    "(method = 'Mobile' AND payment_destination_id IS NOT NULL AND payment_destination_version_no IS NOT NULL " +
                    "AND mobile_provider_snapshot IS NOT NULL AND mobile_account_name_snapshot IS NOT NULL AND mobile_account_number_snapshot IS NOT NULL " +
                    "AND bank_name_snapshot IS NULL AND account_name_snapshot IS NULL AND account_number_snapshot IS NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.Method).HasMaxLength(20).IsRequired();
            entity.Property(row => row.State).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Amount).HasColumnType("numeric");
            entity.Property(row => row.SettledAmount).HasColumnType("numeric");
            entity.Property(row => row.ExternalReference).HasMaxLength(150);
            entity.Property(row => row.BankNameSnapshot).HasMaxLength(150);
            entity.Property(row => row.AccountNameSnapshot).HasMaxLength(150);
            entity.Property(row => row.AccountNumberSnapshot).HasMaxLength(200);
            entity.Property(row => row.MobileProviderSnapshot).HasMaxLength(100);
            entity.Property(row => row.MobileAccountNameSnapshot).HasMaxLength(150);
            entity.Property(row => row.MobileAccountNumberSnapshot).HasMaxLength(100);
            entity.HasIndex(row => new { row.BusinessId, row.PayrollPaymentId, row.AllocationNo })
                .IsUnique().HasDatabaseName("ux_payroll_payment_allocation_identity");
            entity.HasOne<PayrollPayment>().WithMany(payment => payment.Allocations)
                .HasForeignKey(row => new { row.BusinessId, PayrollPaymentId = row.PayrollPaymentId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollEmployeePaymentDestination>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PaymentDestinationId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollPaymentBatch>(entity =>
        {
            entity.ToTable("payroll_payment_batches", table =>
            {
                table.HasCheckConstraint("ck_payroll_payment_batch_method",
                    "method IN ('Cash', 'Bank', 'Mobile', 'Mixed')");
                table.HasCheckConstraint("ck_payroll_payment_batch_status",
                    "status IN ('Draft', 'Ready', 'Processing', 'Completed', 'PartiallyFailed', 'Failed')");
                table.HasCheckConstraint("ck_payroll_payment_batch_amount_version",
                    "expected_amount > 0 AND version > 0 AND bank_file_version_no >= 0");
                table.HasCheckConstraint("ck_payroll_payment_batch_hash",
                    "length(idempotency_payload_hash) = 64");
                table.HasCheckConstraint("ck_payroll_payment_batch_submission_hash",
                    "(submission_idempotency_key IS NULL AND submission_payload_hash IS NULL) OR " +
                    "(submission_idempotency_key IS NOT NULL AND length(submission_payload_hash) = 64)");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.BatchReference).HasMaxLength(100).IsRequired();
            entity.Property(row => row.Method).HasMaxLength(20).IsRequired();
            entity.Property(row => row.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(row => row.ExpectedAmount).HasColumnType("numeric");
            entity.Property(row => row.Status).HasMaxLength(30).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.IdempotencyPayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.SubmissionExternalReference).HasMaxLength(150);
            entity.Property(row => row.SubmissionReason).HasMaxLength(1000);
            entity.Property(row => row.SubmissionIdempotencyKey).HasMaxLength(150);
            entity.Property(row => row.SubmissionPayloadHash).HasMaxLength(64);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.BatchReference })
                .IsUnique().HasDatabaseName("ux_payroll_payment_batch_reference");
            entity.HasIndex(row => new { row.BusinessId, row.IdempotencyKey })
                .IsUnique().HasDatabaseName("ux_payroll_payment_batch_idempotency");
            entity.HasIndex(row => new { row.BusinessId, row.SubmissionIdempotencyKey })
                .IsUnique().HasDatabaseName("ux_payroll_payment_batch_submission_idempotency");
            entity.HasIndex(row => new { row.BusinessId, row.PayrollRunId, row.CreatedAtUtc })
                .HasDatabaseName("ix_payroll_payment_batch_run_created");
            entity.HasOne<PayrollRun>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollRunId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollPeriod>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollPeriodId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Core.Entities.Business>().WithMany()
                .HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollPaymentBankFileVersion>(entity =>
        {
            entity.ToTable("payroll_payment_bank_file_versions", table =>
            {
                table.HasCheckConstraint("ck_payroll_payment_bank_file_version_values",
                    "version_no > 0 AND row_count > 0 AND expected_amount > 0");
                table.HasCheckConstraint("ck_payroll_payment_bank_file_hashes",
                    "length(payload_hash) = 64 AND length(content_hash) = 64");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.FileName).HasMaxLength(180).IsRequired();
            entity.Property(row => row.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(row => row.ExpectedAmount).HasColumnType("numeric");
            entity.Property(row => row.GenerationReason).HasMaxLength(1000).IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.PaymentBatchId, row.VersionNo })
                .IsUnique().HasDatabaseName("ux_payroll_payment_bank_file_version");
            entity.HasIndex(row => new { row.BusinessId, row.PaymentBatchId, row.IdempotencyKey })
                .IsUnique().HasDatabaseName("ux_payroll_payment_bank_file_idempotency");
            entity.HasOne<PayrollPaymentBatch>().WithMany(batch => batch.BankFileVersions)
                .HasForeignKey(row => new { row.BusinessId, row.PaymentBatchId })
                .HasPrincipalKey(batch => new { batch.BusinessId, batch.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
