using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Organisation;

namespace RetailPOS.Infrastructure.HR.Employees;

internal static class HrEmployeeModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<HrEmploymentType>(entity =>
        {
            entity.ToTable("hr_employment_types", table =>
                table.HasCheckConstraint("ck_hr_employment_type_business_id", "business_id > 0"));
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Code).HasMaxLength(50).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(100).IsRequired();
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.HasIndex(row => new { row.BusinessId, row.Code }).IsUnique();
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrEmployee>(entity =>
        {
            entity.ToTable("hr_employees", table =>
            {
                table.HasCheckConstraint("ck_hr_employee_business_id", "business_id > 0");
                table.HasCheckConstraint("ck_hr_employee_version", "version > 0");
                table.HasCheckConstraint("ck_hr_employee_confirmation_date",
                    "confirmation_date IS NULL OR confirmation_date >= joining_date");
                table.HasCheckConstraint("ck_hr_employee_suspended_status",
                    "suspended_from_status IS NULL OR suspended_from_status IN ('Active', 'Probation')");
                table.HasCheckConstraint("ck_hr_employee_current_position_org_unit",
                    "(current_position_id IS NULL AND current_org_unit_id IS NULL) OR "
                    + "(current_position_id IS NOT NULL AND current_org_unit_id IS NOT NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.EmployeeCode).HasMaxLength(50).IsRequired();
            entity.Property(row => row.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(row => row.MiddleName).HasMaxLength(100);
            entity.Property(row => row.LastName).HasMaxLength(100);
            entity.Property(row => row.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(row => row.GenderCode).HasMaxLength(30);
            entity.Property(row => row.NationalId).HasMaxLength(100);
            entity.Property(row => row.PassportNo).HasMaxLength(100);
            entity.Property(row => row.PersonalEmail).HasMaxLength(200);
            entity.Property(row => row.WorkEmail).HasMaxLength(200);
            entity.Property(row => row.Mobile).HasMaxLength(50);
            entity.Property(row => row.PresentAddress).HasMaxLength(4000);
            entity.Property(row => row.PermanentAddress).HasMaxLength(4000);
            entity.Property(row => row.EmergencyContactName).HasMaxLength(150);
            entity.Property(row => row.EmergencyContactPhone).HasMaxLength(50);
            entity.Property(row => row.CurrentEmploymentStatus).HasMaxLength(30).IsRequired();
            entity.Property(row => row.SuspendedFromStatus).HasMaxLength(30);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeCode }).IsUnique();
            entity.HasIndex(row => new
            {
                row.BusinessId, row.CurrentEmploymentStatus, row.CurrentEmploymentTypeId
            });
            entity.HasOne<Business>().WithMany()
                .HasForeignKey(row => row.BusinessId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.CurrentEmploymentType).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.CurrentEmploymentTypeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrPosition>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.CurrentPositionId, row.CurrentOrgUnitId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id, row.OrgUnitId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrOrgUnit>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.CurrentOrgUnitId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<OperationalLocationProjection>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.CurrentPrimaryLocationProjectionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<HrEmploymentPeriod>(entity =>
        {
            entity.ToTable("hr_employment_periods", table =>
            {
                table.HasCheckConstraint("ck_hr_employment_period_dates",
                    "end_date IS NULL OR end_date >= start_date");
                table.HasCheckConstraint("ck_hr_employment_period_status",
                    "status IN ('Active', 'Closed')");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.StartReason).HasMaxLength(250);
            entity.Property(row => row.EndReason).HasMaxLength(500);
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.SourceEventType).HasMaxLength(40).IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.StartDate });
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId })
                .IsUnique().HasFilter("status = 'Active'");
            entity.HasAlternateKey(row => new { row.BusinessId, row.EmployeeId, row.Id });
            entity.HasOne(row => row.Employee).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<HrEmploymentType>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.EmploymentTypeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
