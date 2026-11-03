using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Organisation;

namespace RetailPOS.Infrastructure.HR.Organisation;

internal static class HrPositionModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrPosition>(entity =>
        {
            entity.ToTable("hr_positions", table =>
            {
                table.HasCheckConstraint("ck_hr_position_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_position_effective_dates",
                    "effective_to IS NULL OR effective_to >= effective_from");
                table.HasCheckConstraint("ck_hr_position_version", "version > 0");
                table.HasCheckConstraint("ck_hr_position_not_self_reporting",
                    "reports_to_position_id IS NULL OR reports_to_position_id <> id");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Code).HasMaxLength(50).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(150).IsRequired();
            entity.Property(row => row.Status).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id, row.OrgUnitId });
            entity.HasIndex(row => new { row.BusinessId, row.Code }).IsUnique();
            entity.HasIndex(row => new { row.BusinessId, row.OrgUnitId });
            entity.HasIndex(row => new { row.BusinessId, row.ReportsToPositionId });
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.OrgUnit).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.OrgUnitId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationalLocationProjection>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.OperationalLocationProjectionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrPosition>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ReportsToPositionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
