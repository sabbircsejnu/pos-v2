using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

internal static class PayrollMonetaryConfigurationModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PayrollMonetaryConfigurationVersion>(entity =>
        {
            entity.ToTable("payroll_monetary_configuration_versions", table =>
            {
                table.HasCheckConstraint("ck_payroll_monetary_configuration_version",
                    "version_no > 0");
                table.HasCheckConstraint("ck_payroll_monetary_configuration_currency",
                    "length(currency_code) = 3 AND currency_code = upper(currency_code)");
                table.HasCheckConstraint("ck_payroll_monetary_configuration_precision",
                    "decimal_precision BETWEEN 0 AND 6");
                table.HasCheckConstraint("ck_payroll_monetary_configuration_rounding",
                    "rounding_mode IN ('ToEven', 'AwayFromZero', 'ToZero', " +
                    "'ToNegativeInfinity', 'ToPositiveInfinity')");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(row => row.RoundingMode).HasMaxLength(30).IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.VersionNo }).IsUnique()
                .HasDatabaseName("ux_payroll_monetary_configuration_version");
            entity.HasIndex(row => new { row.BusinessId, row.EffectiveFrom }).IsUnique()
                .HasDatabaseName("ux_payroll_monetary_configuration_effective_from");
            entity.HasOne<Core.Entities.Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
