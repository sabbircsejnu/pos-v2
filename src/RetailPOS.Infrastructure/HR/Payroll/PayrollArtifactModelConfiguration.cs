using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

internal static class PayrollArtifactModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PayrollArtifactVersion>(entity =>
        {
            entity.ToTable("payroll_artifact_versions", table =>
            {
                table.HasCheckConstraint("ck_payroll_artifact_version_values",
                    "version_no > 0 AND run_revision > 0 AND byte_length > 0");
                table.HasCheckConstraint("ck_payroll_artifact_pdf",
                    "artifact_type = 'Payslip' AND format = 'PDF' AND content_type = 'application/pdf'");
                table.HasCheckConstraint("ck_payroll_artifact_sha256",
                    "length(payroll_snapshot_hash) = 64 AND length(payment_info_snapshot_hash) = 64 " +
                    "AND length(content_hash) = 64");
                table.HasCheckConstraint("ck_payroll_artifact_scan_status",
                    "scan_status IN ('Clean', 'Rejected', 'Error')");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.ArtifactType).HasMaxLength(30).IsRequired();
            entity.Property(row => row.PayrollSnapshotHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.PaymentInfoSnapshotHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.TemplateVersion).HasMaxLength(50).IsRequired();
            entity.Property(row => row.Format).HasMaxLength(10).IsRequired();
            entity.Property(row => row.StorageKey).HasMaxLength(300).IsRequired();
            entity.Property(row => row.EncryptionKeyReference).HasMaxLength(300).IsRequired();
            entity.Property(row => row.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(row => row.Classification).HasMaxLength(40).IsRequired();
            entity.Property(row => row.ScanStatus).HasMaxLength(20).IsRequired();
            entity.Property(row => row.FileName).HasMaxLength(255).IsRequired();
            entity.HasIndex(row => new
            {
                row.BusinessId, row.PayrollRunId, row.RunRevision, row.PayrollEmployeeResultId,
                row.ArtifactType, row.PayrollSnapshotHash, row.PaymentInfoSnapshotHash, row.TemplateVersion
            }).IsUnique().HasDatabaseName("ux_payroll_artifact_generation_identity");
            entity.HasIndex(row => new
                { row.BusinessId, row.PayrollRunId, row.PayrollEmployeeResultId, row.VersionNo })
                .IsUnique().HasDatabaseName("ux_payroll_artifact_version_no");
            entity.HasOne<PayrollRun>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollRunId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollEmployeeResult>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PayrollEmployeeResultId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollArtifactVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ReplacesVersionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollPayslipDelivery>(entity =>
        {
            entity.ToTable("payroll_payslip_deliveries", table =>
                table.HasCheckConstraint("ck_payroll_payslip_delivery_status",
                    "status IN ('Pending', 'Sent', 'Failed')"));
            entity.HasKey(row => row.Id);
            entity.Property(row => row.RecipientSnapshot).HasMaxLength(320).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.ProviderReference).HasMaxLength(200);
            entity.Property(row => row.FailureCode).HasMaxLength(80);
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new
                { row.BusinessId, row.RequestedByUserId, row.IdempotencyKey })
                .IsUnique().HasDatabaseName("ux_payroll_payslip_delivery_idempotency");
            entity.HasIndex(row => new { row.BusinessId, row.ArtifactVersionId, row.RequestedAtUtc })
                .HasDatabaseName("ix_payroll_payslip_delivery_artifact");
            entity.HasOne<PayrollArtifactVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, Id = row.ArtifactVersionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
