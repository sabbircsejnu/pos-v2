using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Organisation;

namespace RetailPOS.Infrastructure.HR.Organisation;

internal static class HrOrganisationModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<OperationalLocationProjection>(entity =>
        {
            entity.ToTable("hr_operational_location_projections", table =>
            {
                table.HasCheckConstraint("ck_hr_location_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_location_source_version", "source_version > 0");
                table.HasCheckConstraint("ck_hr_location_source_type", "source_entity_type IN ('Outlet', 'Warehouse')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.SourceSystem).HasMaxLength(50).IsRequired();
            entity.Property(row => row.SourceEntityType).HasMaxLength(50).IsRequired();
            entity.Property(row => row.SourceEntityId).HasMaxLength(80).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(150).IsRequired();
            entity.Property(row => row.Address).HasMaxLength(500).IsRequired();
            entity.Property(row => row.ContactNumber).HasMaxLength(100);
            entity.Property(row => row.ManagerExternalUserId).HasMaxLength(80);
            entity.Property(row => row.SourcePayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.SyncStatus).HasMaxLength(30).IsRequired();
            entity.HasIndex(row => new
            {
                row.BusinessId, row.SourceSystem, row.SourceEntityType, row.SourceEntityId
            }).IsUnique();
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.HasIndex(row => new { row.BusinessId, row.SourceEntityType });
            entity.HasIndex(row => new { row.BusinessId, row.Name });
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrOrgUnit>(entity =>
        {
            entity.ToTable("hr_org_units", table =>
            {
                table.HasCheckConstraint("ck_hr_org_unit_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_org_unit_effective_dates",
                    "effective_to IS NULL OR effective_to >= effective_from");
                table.HasCheckConstraint("ck_hr_org_unit_version", "version > 0");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Code).HasMaxLength(50).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(150).IsRequired();
            entity.Property(row => row.UnitType).HasMaxLength(40).IsRequired();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.HasIndex(row => new { row.BusinessId, row.Code }).IsUnique();
            entity.HasIndex(row => new { row.BusinessId, row.ParentOrgUnitId });
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrOrgUnit>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ParentOrgUnitId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationalLocationProjection>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.OperationalLocationProjectionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
