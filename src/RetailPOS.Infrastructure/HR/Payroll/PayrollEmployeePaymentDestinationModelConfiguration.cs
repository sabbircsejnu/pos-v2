using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

internal static class PayrollEmployeePaymentDestinationModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PayrollEmployeePaymentDestination>(entity =>
        {
            entity.ToTable("payroll_employee_payment_destinations", table =>
            {
                table.HasCheckConstraint("ck_payroll_payment_destination_method",
                    "method IN ('Cash', 'Bank', 'Mobile')");
                table.HasCheckConstraint("ck_payroll_payment_destination_primary",
                    "is_primary");
                table.HasCheckConstraint("ck_payroll_payment_destination_version",
                    "version_no > 0 AND version > 0");
                table.HasCheckConstraint("ck_payroll_payment_destination_effective_date",
                    "effective_from > '0001-01-01'");
                table.HasCheckConstraint("ck_payroll_payment_destination_method_fields",
                    "(method = 'Cash' AND bank_name IS NULL AND account_name IS NULL AND account_number IS NULL AND mobile_provider IS NULL AND mobile_account_name IS NULL AND mobile_account_number IS NULL) OR (method = 'Bank' AND bank_name IS NOT NULL AND length(trim(bank_name)) > 0 AND account_name IS NOT NULL AND length(trim(account_name)) > 0 AND account_number IS NOT NULL AND length(trim(account_number)) > 0 AND mobile_provider IS NULL AND mobile_account_name IS NULL AND mobile_account_number IS NULL) OR (method = 'Mobile' AND mobile_provider IS NOT NULL AND length(trim(mobile_provider)) > 0 AND mobile_account_name IS NOT NULL AND length(trim(mobile_account_name)) > 0 AND mobile_account_number IS NOT NULL AND length(trim(mobile_account_number)) > 0 AND bank_name IS NULL AND account_name IS NULL AND account_number IS NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.Method).HasMaxLength(20).IsRequired();
            entity.Property(row => row.BankName).HasMaxLength(150);
            entity.Property(row => row.AccountName).HasMaxLength(150);
            entity.Property(row => row.AccountNumber).HasMaxLength(200);
            entity.Property(row => row.MobileProvider).HasMaxLength(100);
            entity.Property(row => row.MobileAccountName).HasMaxLength(150);
            entity.Property(row => row.MobileAccountNumber).HasMaxLength(100);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.VersionNo })
                .IsUnique().HasDatabaseName("ux_payroll_payment_destination_version");
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.EffectiveFrom })
                .IsUnique().HasDatabaseName("ux_payroll_payment_destination_effective_from");
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.IsPrimary, row.IsActive,
                row.EffectiveFrom }).HasDatabaseName("ix_payroll_payment_destination_effective");
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailPOS.Core.Entities.Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
