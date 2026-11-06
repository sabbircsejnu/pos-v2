using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;

namespace RetailPOS.Infrastructure.Outlets;

internal static class OutletSalesTargetModelConfiguration
{
    public const string VersionNumberIndex = "ux_outlet_sales_target_versions_outlet_version";
    public const string ActiveOverrideIndex = "ux_outlet_sales_target_overrides_active_period";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutletSalesTargetVersion>(entity =>
        {
            entity.ToTable("outlet_sales_target_versions");
            entity.HasKey(version => version.Id);
            entity.HasIndex(version => new { version.OutletId, version.VersionNo })
                .IsUnique()
                .HasDatabaseName(VersionNumberIndex);
            entity.HasIndex(version => new { version.BusinessId, version.OutletId, version.EffectiveFrom });
            entity.HasOne(version => version.Outlet)
                .WithMany(outlet => outlet.SalesTargetVersions)
                .HasForeignKey(version => version.OutletId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(version => version.Days)
                .WithOne()
                .HasForeignKey(day => day.VersionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OutletSalesTargetVersionDay>(entity =>
        {
            entity.ToTable("outlet_sales_target_version_days", table =>
            {
                table.HasCheckConstraint(
                    "ck_outlet_sales_target_version_days_weekday",
                    "day_of_week BETWEEN 0 AND 6");
                table.HasCheckConstraint(
                    "ck_outlet_sales_target_version_days_amount",
                    "target_amount IS NULL OR target_amount >= 0");
                table.HasCheckConstraint(
                    "ck_outlet_sales_target_version_days_off_day_no_amount",
                    "NOT is_off_day OR target_amount IS NULL");
            });
            entity.HasKey(day => day.Id);
            entity.Property(day => day.DayOfWeek).HasConversion<int>();
            entity.Property(day => day.TargetAmount).HasPrecision(18, 2);
            entity.HasIndex(day => new { day.VersionId, day.DayOfWeek }).IsUnique();
        });

        modelBuilder.Entity<OutletSalesTargetOverride>(entity =>
        {
            entity.ToTable("outlet_sales_target_overrides", table =>
            {
                table.HasCheckConstraint(
                    "ck_outlet_sales_target_overrides_period_type",
                    "period_type IN ('weekly', 'first_half', 'second_half', 'monthly')");
                table.HasCheckConstraint(
                    "ck_outlet_sales_target_overrides_amounts",
                    "override_amount >= 0 AND calculated_amount >= 0");
                table.HasCheckConstraint(
                    "ck_outlet_sales_target_overrides_range",
                    "period_to >= period_from");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.PeriodType).HasMaxLength(20).IsRequired();
            entity.Property(item => item.CalculatedAmount).HasPrecision(18, 2);
            entity.Property(item => item.OverrideAmount).HasPrecision(18, 2);
            entity.Property(item => item.Reason).HasMaxLength(500);
            entity.HasIndex(item => new { item.OutletId, item.PeriodType, item.PeriodFrom })
                .IsUnique()
                .HasFilter("cleared_at IS NULL")
                .HasDatabaseName(ActiveOverrideIndex);
            entity.HasIndex(item => new { item.BusinessId, item.OutletId, item.PeriodFrom });
            entity.HasOne(item => item.Outlet)
                .WithMany(outlet => outlet.SalesTargetOverrides)
                .HasForeignKey(item => item.OutletId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Version)
                .WithMany()
                .HasForeignKey(item => item.VersionId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
