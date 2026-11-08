using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Pos;

/// <summary>Schema contract for the Phase 3 Cash Session fact tables (LLD §20.3).</summary>
public static class CashSessionModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CashSessionMovement>(entity =>
        {
            entity.ToTable("cash_session_movements");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MovementType).HasMaxLength(40).IsRequired();
            entity.Property(e => e.CashEffect).HasPrecision(18, 2);
            entity.Property(e => e.SourceType).HasMaxLength(40).IsRequired();
            entity.Property(e => e.SourceKey).HasMaxLength(120).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.HasIndex(e => new { e.BusinessId, e.SourceKey }).IsUnique()
                .HasDatabaseName("ux_cash_session_movements_source_key");
            entity.HasIndex(e => e.CashSessionId);
            entity.HasIndex(e => e.OriginalSaleId);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_cash_session_movements_type",
                    "movement_type IN ('cash_refund','drawer_expense','drawer_expense_reversal','correction','exchange_cash_in','exchange_cash_out')");
                t.HasCheckConstraint("ck_cash_session_movements_effect",
                    "(movement_type IN ('cash_refund','drawer_expense','exchange_cash_out') AND cash_effect < 0)"
                    + " OR (movement_type IN ('drawer_expense_reversal','exchange_cash_in') AND cash_effect > 0)"
                    + " OR (movement_type = 'correction' AND cash_effect <> 0)");
            });
            entity.HasOne(e => e.CashSession).WithMany().HasForeignKey(e => e.CashSessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CashSessionCloseSnapshot>(entity =>
        {
            entity.ToTable("cash_session_close_snapshots");
            entity.HasKey(e => e.CashSessionId);
            foreach (var name in new[]
                     {
                         nameof(CashSessionCloseSnapshot.OpeningCash), nameof(CashSessionCloseSnapshot.CashSalesRetained),
                         nameof(CashSessionCloseSnapshot.CashTendered), nameof(CashSessionCloseSnapshot.ChangeGiven),
                         nameof(CashSessionCloseSnapshot.CardTotal), nameof(CashSessionCloseSnapshot.MobileTotal),
                         nameof(CashSessionCloseSnapshot.DrawerExpenses), nameof(CashSessionCloseSnapshot.CashRefunds),
                         nameof(CashSessionCloseSnapshot.Corrections), nameof(CashSessionCloseSnapshot.ExpectedCash),
                         nameof(CashSessionCloseSnapshot.CountedCash), nameof(CashSessionCloseSnapshot.Variance),
                         nameof(CashSessionCloseSnapshot.HandedOverTotal), nameof(CashSessionCloseSnapshot.RetainedAtOutlet)
                     })
                entity.Property(name).HasPrecision(18, 2);
            entity.Property(e => e.VarianceReason).HasMaxLength(1000);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.TimeZoneId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.SnapshotHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.DenominationsJson).HasColumnType("text");
            entity.HasIndex(e => new { e.BusinessId, e.ClosedAtUtc });
            entity.ToTable(t => t.HasCheckConstraint("ck_cash_session_close_snapshots_retained",
                "retained_at_outlet >= 0 AND handed_over_total >= 0 AND counted_cash >= 0"));
            entity.HasOne(e => e.CashSession).WithOne().HasForeignKey<CashSessionCloseSnapshot>(e => e.CashSessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CashSessionHandover>(entity =>
        {
            entity.ToTable("cash_session_handovers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Method).HasMaxLength(30).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.RecipientName).HasMaxLength(150);
            entity.Property(e => e.BankName).HasMaxLength(150);
            entity.Property(e => e.BankAccountReference).HasMaxLength(100);
            entity.Property(e => e.DepositReference).HasMaxLength(100);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.DepositDate).HasColumnType("date");
            entity.HasIndex(e => e.CashSessionId);
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_cash_session_handovers_amount", "amount > 0");
                t.HasCheckConstraint("ck_cash_session_handovers_method",
                    "method IN ('cash_to_manager','cash_to_owner','bank_deposit')");
                t.HasCheckConstraint("ck_cash_session_handovers_status",
                    "status IN ('handed','pending_bank_verification','confirmed')");
            });
            entity.HasOne(e => e.CashSession).WithMany().HasForeignKey(e => e.CashSessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CashSessionAttachment>(entity =>
        {
            entity.ToTable("cash_session_attachments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.StoragePath).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Sha256).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.CashSessionId);
            entity.HasOne(e => e.CashSession).WithMany().HasForeignKey(e => e.CashSessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CashDifferenceApproval>(entity =>
        {
            entity.ToTable("cash_difference_approvals");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.VarianceAmount).HasPrecision(18, 2);
            entity.Property(e => e.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.PostingStatus).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ReviewNotes).HasMaxLength(1000);
            entity.Property(e => e.LastPostingError).HasMaxLength(500);
            entity.HasIndex(e => e.CashSessionId).IsUnique();
            entity.HasIndex(e => new { e.BusinessId, e.Status });
            entity.HasIndex(e => new { e.PostingStatus, e.NextPostingAttemptAtUtc });
            entity.ToTable(t =>
            {
                t.HasCheckConstraint("ck_cash_difference_approvals_status", "status IN ('pending','approved','rejected')");
                t.HasCheckConstraint("ck_cash_difference_approvals_posting",
                    "posting_status IN ('not_required','pending','posted','failed')");
                t.HasCheckConstraint("ck_cash_difference_approvals_nonzero", "variance_amount <> 0");
                // Phase 3.1 backstops: only an approved difference can ever be scheduled or posted, and "posted"
                // always names its journal.
                t.HasCheckConstraint("ck_cash_difference_approvals_posting_requires_approval",
                    "status = 'approved' OR posting_status = 'not_required'");
                t.HasCheckConstraint("ck_cash_difference_approvals_posted_journal",
                    "posting_status <> 'posted' OR journal_entry_id IS NOT NULL");
            });
            entity.HasOne(e => e.CashSession).WithMany().HasForeignKey(e => e.CashSessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        ApplyUtcNormalisation(modelBuilder);
    }

    private static readonly ValueConverter<DateTime, DateTime> UtcConverter =
        new(value => UtcTime.Normalize(value), value => UtcTime.Normalize(value));

    private static readonly ValueConverter<DateTime?, DateTime?> NullableUtcConverter =
        new(value => UtcTime.Normalize(value), value => UtcTime.Normalize(value));

    // Every instant of the Cash Session aggregate is a real UTC DateTime on read and write, whatever Npgsql's
    // timestamp mode and whatever the host time zone are. Calendar dates (BusinessDate, DepositDate) are excluded.
    private static void ApplyUtcNormalisation(ModelBuilder modelBuilder)
    {
        var entityTypes = new[]
        {
            typeof(CashSession), typeof(CashSessionMovement), typeof(CashSessionCloseSnapshot),
            typeof(CashSessionHandover), typeof(CashSessionAttachment), typeof(CashDifferenceApproval)
        };
        var calendarDates = new HashSet<string> { nameof(CashSession.BusinessDate), nameof(CashSessionHandover.DepositDate) };
        foreach (var type in entityTypes)
        {
            var entity = modelBuilder.Entity(type);
            foreach (var property in entity.Metadata.GetProperties().ToList())
            {
                if (calendarDates.Contains(property.Name))
                    continue;
                var builder = entity.Property(property.Name);
                if (property.ClrType == typeof(DateTime))
                    builder.HasConversion(UtcConverter);
                else if (property.ClrType == typeof(DateTime?))
                    builder.HasConversion(NullableUtcConverter);
            }
        }
    }
}
