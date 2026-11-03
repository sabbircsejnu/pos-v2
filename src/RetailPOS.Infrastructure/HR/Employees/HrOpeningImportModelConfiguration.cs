using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;

namespace RetailPOS.Infrastructure.HR.Employees;

public static class HrOpeningImportModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrImportBatch>(entity =>
        {
            entity.ToTable("hr_import_batches", table =>
            {
                table.HasCheckConstraint("ck_hr_import_batch_type",
                    "import_type IN ('Employee', 'Assignment', 'Salary', 'LoanOpening')");
                table.HasCheckConstraint("ck_hr_import_batch_status",
                    "status IN ('Uploaded', 'Ready', 'ValidationFailed', 'Processing', 'Completed', 'Failed')");
                table.HasCheckConstraint("ck_hr_import_batch_counts",
                    "total_rows >= 0 AND valid_rows >= 0 AND invalid_rows >= 0 AND total_rows = valid_rows + invalid_rows");
                table.HasCheckConstraint("ck_hr_import_batch_version", "version > 0 AND revision >= 0");
            });
            entity.HasKey(b => b.Id);
            entity.HasAlternateKey(b => new { b.BusinessId, b.Id });
            entity.HasIndex(b => new { b.BusinessId, b.IdempotencyKey }).IsUnique();
            entity.Property(b => b.ImportType).HasMaxLength(30).IsRequired();
            entity.Property(b => b.TemplateVersion).HasMaxLength(20).IsRequired();
            entity.Property(b => b.IdempotencyKey).HasMaxLength(100).IsRequired();
            entity.Property(b => b.ContentHash).HasMaxLength(64).IsRequired();
            entity.Property(b => b.FileStorageKey).HasMaxLength(500).IsRequired();
            entity.Property(b => b.EncryptionKeyReference).HasMaxLength(500).IsRequired();
            entity.Property(b => b.Status).HasMaxLength(30).IsRequired();
            entity.Property(b => b.Version).IsConcurrencyToken();
            entity.HasOne<Business>().WithMany().HasForeignKey(b => b.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<HrImportRow>(entity =>
        {
            entity.ToTable("hr_import_rows", table =>
            {
                table.HasCheckConstraint("ck_hr_import_row_number", "row_no > 0 AND revision > 0");
                table.HasCheckConstraint("ck_hr_import_row_status", "validation_status IN ('Valid', 'Invalid', 'Completed')");
            });
            entity.HasKey(r => r.Id);
            entity.HasIndex(r => new { r.BusinessId, r.ImportBatchId, r.Revision, r.RowNo }).IsUnique();
            entity.Property(r => r.ExternalKey).HasMaxLength(100).IsRequired();
            entity.Property(r => r.RawJson).HasMaxLength(200).IsRequired();
            entity.Property(r => r.ErrorJson).IsRequired();
            entity.Property(r => r.ValidationStatus).HasMaxLength(20).IsRequired();
            entity.Property(r => r.CreatedEntityType).HasMaxLength(100);
            entity.HasOne<HrImportBatch>().WithMany().HasForeignKey(r => new { r.BusinessId, r.ImportBatchId })
                .HasPrincipalKey(b => new { b.BusinessId, b.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
