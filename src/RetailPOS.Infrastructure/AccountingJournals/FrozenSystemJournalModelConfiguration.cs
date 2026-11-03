using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.Accounting;

namespace RetailPOS.Infrastructure.AccountingJournals;

internal static class FrozenSystemJournalModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<FrozenSystemJournalOperation>(entity =>
        {
            entity.ToTable("accounting_frozen_system_journal_operations");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.PostingReference).HasMaxLength(80);
            entity.Property(row => row.Kind).HasMaxLength(20);
            entity.Property(row => row.PayloadHash).HasMaxLength(64);
            entity.HasIndex(row => new { row.BusinessId, row.PostingReference }).IsUnique();
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(row => row.JournalId)
                .HasConstraintName("fk_frozen_journal_result").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntry>().WithMany().HasForeignKey(row => row.OriginalJournalId)
                .HasConstraintName("fk_frozen_journal_original").OnDelete(DeleteBehavior.Restrict);
        });
    }
}
