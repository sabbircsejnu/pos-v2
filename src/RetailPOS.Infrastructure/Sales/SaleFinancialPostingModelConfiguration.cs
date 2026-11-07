using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Sales;

/// <summary>Schema contract for <c>sale_financial_postings</c> (LLD §20.3, D10).</summary>
public static class SaleFinancialPostingModelConfiguration
{
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter =
        new(value => UtcTime.Normalize(value), value => UtcTime.Normalize(value));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter =
        new(value => UtcTime.Normalize(value), value => UtcTime.Normalize(value));

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SaleFinancialPosting>(entity =>
        {
            entity.ToTable("sale_financial_postings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ReversalType).HasMaxLength(20);
            entity.Property(e => e.SourceKey).HasMaxLength(120).IsRequired();
            entity.Property(e => e.SnapshotJson).HasColumnType("text").IsRequired();
            entity.Property(e => e.SnapshotHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.LastErrorCode).HasMaxLength(40);
            entity.Property(e => e.LastError).HasMaxLength(500);
            entity.Property(e => e.BusinessDate).HasColumnType("date");
            entity.Property(e => e.CreatedAtUtc).HasConversion(UtcConverter);
            entity.Property(e => e.UpdatedAtUtc).HasConversion(UtcConverter);
            entity.Property(e => e.NextAttemptAtUtc).HasConversion(NullableUtcConverter);
            entity.Property(e => e.LastAttemptAtUtc).HasConversion(NullableUtcConverter);
            entity.Property(e => e.PostedAtUtc).HasConversion(NullableUtcConverter);

            entity.HasIndex(e => new { e.BusinessId, e.SourceKey }).IsUnique()
                .HasDatabaseName("ux_sale_financial_postings_source_key");
            // One sale journal, one void or pre-existing refund reversal, and one journal for each refund transaction.
            entity.HasIndex(e => new { e.SaleId, e.Kind }).IsUnique()
                .HasFilter("sale_refund_id IS NULL")
                .HasDatabaseName("ux_sale_financial_postings_sale_kind");
            entity.HasIndex(e => e.SaleRefundId).IsUnique()
                .HasFilter("sale_refund_id IS NOT NULL")
                .HasDatabaseName("ux_sale_financial_postings_sale_refund");
            entity.HasIndex(e => new { e.Status, e.NextAttemptAtUtc })
                .HasDatabaseName("ix_sale_financial_postings_due");
            entity.HasIndex(e => new { e.BusinessId, e.Status })
                .HasDatabaseName("ix_sale_financial_postings_business_status");
            entity.HasIndex(e => e.JournalEntryId)
                .HasDatabaseName("ix_sale_financial_postings_journal_entry_id");

            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_sale_financial_postings_kind", "kind IN ('sale','reversal')");
                t.HasCheckConstraint("ck_sale_financial_postings_status", "status IN ('pending','posted','failed')");
                t.HasCheckConstraint("ck_sale_financial_postings_reversal_type",
                    "(kind = 'sale' AND reversal_type IS NULL)"
                    + " OR (kind = 'reversal' AND reversal_type IN ('void','refund'))");
                t.HasCheckConstraint("ck_sale_financial_postings_posted_journal",
                    "(status = 'posted') = (journal_entry_id IS NOT NULL AND posted_at_utc IS NOT NULL)");
                t.HasCheckConstraint("ck_sale_financial_postings_attempts", "attempts >= 0");
            });

            entity.HasOne(e => e.Sale).WithMany().HasForeignKey(e => e.SaleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailPOS.Core.Entities.Uom.SaleRefund>().WithMany().HasForeignKey(e => e.SaleRefundId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(e => e.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FinancialPostingWorkerHeartbeat>(entity =>
        {
            entity.ToTable("financial_posting_worker_heartbeats");
            entity.HasKey(e => new { e.WorkerName, e.InstanceId });
            entity.Property(e => e.WorkerName).HasMaxLength(80);
            entity.Property(e => e.InstanceId).HasMaxLength(120);
            entity.Property(e => e.HostName).HasMaxLength(120).IsRequired();
            entity.Property(e => e.HostRole).HasMaxLength(20).IsRequired();
            entity.Property(e => e.LastError).HasMaxLength(200);
            entity.Property(e => e.StartedAtUtc).HasConversion(UtcConverter);
            entity.Property(e => e.UpdatedAtUtc).HasConversion(UtcConverter);
            entity.Property(e => e.LastRunStartedAtUtc).HasConversion(NullableUtcConverter);
            entity.Property(e => e.LastRunCompletedAtUtc).HasConversion(NullableUtcConverter);
            entity.HasIndex(e => e.UpdatedAtUtc).HasDatabaseName("ix_financial_posting_worker_heartbeats_updated");
            entity.ToTable(t => t.HasCheckConstraint("ck_financial_posting_worker_heartbeats_role",
                "host_role IN ('Api','Worker')"));
        });
    }
}
