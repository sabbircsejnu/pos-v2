using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Leave;
using RetailPOS.Core.Entities.HR.Organisation;

namespace RetailPOS.Infrastructure.HR.Leave;

internal static class HrLeaveModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrLeaveType>(entity =>
        {
            entity.ToTable("hr_leave_types", table =>
            {
                table.HasCheckConstraint("ck_hr_leave_type_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_leave_type_version", "version > 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.Code).HasMaxLength(50).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(100).IsRequired();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.Code }).IsUnique();
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrLeavePolicyVersion>(entity =>
        {
            entity.ToTable("hr_leave_policy_versions", table =>
            {
                table.HasCheckConstraint("ck_hr_leave_policy_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_leave_policy_version", "version_no > 0");
                table.HasCheckConstraint("ck_hr_leave_policy_entitlement", "annual_entitlement >= 0");
                table.HasCheckConstraint("ck_hr_leave_policy_carry_forward_cap",
                    "carry_forward_cap IS NULL OR carry_forward_cap >= 0");
                table.HasCheckConstraint("ck_hr_leave_policy_expiry_months",
                    "carry_forward_expiry_months IS NULL OR carry_forward_expiry_months > 0");
                table.HasCheckConstraint("ck_hr_leave_policy_maximum_balance",
                    "maximum_balance IS NULL OR maximum_balance >= 0");
                table.HasCheckConstraint("ck_hr_leave_policy_dates",
                    "effective_to IS NULL OR effective_to >= effective_from");
                table.HasCheckConstraint("ck_hr_leave_policy_scope",
                    "(scope_type = 'Business' AND scope_reference IS NULL) OR "
                    + "(scope_type <> 'Business' AND scope_reference IS NOT NULL)");
                table.HasCheckConstraint("ck_hr_leave_policy_carry_forward",
                    "carry_forward_allowed OR (carry_forward_cap IS NULL AND carry_forward_expiry_months IS NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.Code).HasMaxLength(100).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(150).IsRequired();
            entity.Property(row => row.ScopeType).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(row => row.ScopeReference).HasMaxLength(150);
            entity.Property(row => row.AccrualMethod).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(row => row.AnnualEntitlement).HasPrecision(12, 6);
            entity.Property(row => row.CarryForwardCap).HasPrecision(12, 6);
            entity.Property(row => row.MaximumBalance).HasPrecision(12, 6);
            entity.Property(row => row.PayrollImpactCode).HasMaxLength(100);
            entity.Property(row => row.EffectiveFrom).HasColumnType("date");
            entity.Property(row => row.EffectiveTo).HasColumnType("date");
            entity.HasIndex(row => new
            {
                row.BusinessId, row.LeaveTypeId, row.Code, row.VersionNo
            }).IsUnique();
            entity.HasIndex(row => new
            {
                row.BusinessId, row.LeaveTypeId, row.ScopeType, row.ScopeReference,
                row.EffectiveFrom, row.EffectiveTo
            });
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrLeaveType>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.LeaveTypeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrLeaveEntitlement>(entity =>
        {
            entity.ToTable("hr_leave_entitlements", table =>
            {
                table.HasCheckConstraint("ck_hr_leave_entitlement_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_leave_entitlement_dates", "leave_year_end >= leave_year_start");
                table.HasCheckConstraint("ck_hr_leave_entitlement_version", "version > 0");
                table.HasCheckConstraint("ck_hr_leave_entitlement_close_version", "close_version >= 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.LeaveYearStart).HasColumnType("date");
            entity.Property(row => row.LeaveYearEnd).HasColumnType("date");
            entity.Property(row => row.JoiningDateSnapshot).HasColumnType("date");
            entity.Property(row => row.LocationTypeSnapshot).HasMaxLength(30);
            entity.Property(row => row.Status).HasConversion<string>().HasMaxLength(20)
                .HasDefaultValue(HrLeaveEntitlementStatus.Open).IsRequired();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new
            {
                row.BusinessId, row.EmployeeId, row.LeaveTypeId, row.LeaveYearStart
            }).IsUnique();
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(employee => new { employee.BusinessId, employee.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrLeaveType>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.LeaveTypeId })
                .HasPrincipalKey(type => new { type.BusinessId, type.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrLeavePolicyVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PolicyVersionId })
                .HasPrincipalKey(policy => new { policy.BusinessId, policy.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationalLocationProjection>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PrimaryLocationProjectionIdSnapshot })
                .HasPrincipalKey(location => new { location.BusinessId, location.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrLeaveLedgerEntry>(entity =>
        {
            entity.ToTable("hr_leave_ledger", table =>
            {
                table.HasCheckConstraint("ck_hr_leave_ledger_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_leave_ledger_quantity", "quantity <> 0");
                table.HasCheckConstraint("ck_hr_leave_ledger_source",
                    "length(source_type) > 0 AND length(source_id) > 0");
                table.HasCheckConstraint("ck_hr_leave_ledger_idempotency_key",
                    "length(idempotency_key) BETWEEN 1 AND 150");
                table.HasCheckConstraint("ck_hr_leave_ledger_actor", "actor_real_user_id > 0");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.TransactionType).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(row => row.Quantity).HasPrecision(12, 6);
            entity.Property(row => row.EffectiveDate).HasColumnType("date");
            entity.Property(row => row.ExpiresOn).HasColumnType("date");
            entity.Property(row => row.SourceType).HasMaxLength(50).IsRequired();
            entity.Property(row => row.SourceId).HasMaxLength(150).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.IdempotencyKey }).IsUnique();
            entity.HasIndex(row => new { row.BusinessId, row.EntitlementId, row.EffectiveDate });
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrLeaveEntitlement>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EntitlementId })
                .HasPrincipalKey(entitlement => new { entitlement.BusinessId, entitlement.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(employee => new { employee.BusinessId, employee.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrLeaveType>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.LeaveTypeId })
                .HasPrincipalKey(type => new { type.BusinessId, type.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrLeavePolicyVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PolicyVersionId })
                .HasPrincipalKey(policy => new { policy.BusinessId, policy.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrLeaveYearLifecycleEvent>(entity =>
        {
            entity.ToTable("hr_leave_year_lifecycle_events", table =>
            {
                table.HasCheckConstraint("ck_hr_leave_year_event_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_leave_year_event_close_version", "close_version >= 0");
                table.HasCheckConstraint("ck_hr_leave_year_event_actor", "actor_real_user_id > 0");
                table.HasCheckConstraint("ck_hr_leave_year_event_key",
                    "length(idempotency_key) BETWEEN 1 AND 150");
                table.HasCheckConstraint("ck_hr_leave_year_event_quantities",
                    "balance_before_close >= -1000000 AND carry_forward_amount >= 0 AND expired_amount >= 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.LeaveYearStart).HasColumnType("date");
            entity.Property(row => row.Action).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(row => row.BalanceBeforeClose).HasPrecision(12, 6);
            entity.Property(row => row.CarryForwardAmount).HasPrecision(12, 6);
            entity.Property(row => row.ExpiredAmount).HasPrecision(12, 6);
            entity.Property(row => row.CarryForwardExpiresOn).HasColumnType("date");
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.EntitlementId, row.IdempotencyKey })
                .HasDatabaseName("ix_hr_leave_year_event_idempotency").IsUnique();
            entity.HasIndex(row => new { row.BusinessId, row.EntitlementId, row.Id })
                .HasDatabaseName("ix_hr_leave_year_event_entitlement_id");
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId })
                .HasDatabaseName("ix_hr_leave_year_event_employee");
            entity.HasIndex(row => new { row.BusinessId, row.LeaveTypeId })
                .HasDatabaseName("ix_hr_leave_year_event_leave_type");
            entity.HasIndex(row => new { row.BusinessId, row.SourceLedgerEntryId })
                .HasDatabaseName("ix_hr_leave_year_event_source_ledger")
                .HasFilter("source_ledger_entry_id IS NOT NULL");
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrLeaveEntitlement>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EntitlementId })
                .HasPrincipalKey(entitlement => new { entitlement.BusinessId, entitlement.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmployee>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(employee => new { employee.BusinessId, employee.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrLeaveType>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.LeaveTypeId })
                .HasPrincipalKey(type => new { type.BusinessId, type.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrLeaveLedgerEntry>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.SourceLedgerEntryId })
                .HasPrincipalKey(ledger => new { ledger.BusinessId, ledger.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
