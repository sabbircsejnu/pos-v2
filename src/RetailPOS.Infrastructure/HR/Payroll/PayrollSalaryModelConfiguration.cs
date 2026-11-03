using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Core.Entities.HR.Employees;
using RetailPOS.Core.Entities.HR.Payroll;

namespace RetailPOS.Infrastructure.HR.Payroll;

internal static class PayrollSalaryModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<PayrollSalaryComponent>(entity =>
        {
            entity.ToTable("payroll_salary_components", table =>
            {
                table.HasCheckConstraint("ck_payroll_salary_component_type",
                    "component_type IN ('Earning', 'Deduction')");
                table.HasCheckConstraint("ck_payroll_salary_component_value_type",
                    "value_type IN ('Fixed', 'Percentage', 'Formula', 'Calculated')");
                table.HasCheckConstraint("ck_payroll_salary_component_order",
                    "calculation_order >= 0 AND version > 0");
                table.HasCheckConstraint("ck_payroll_salary_component_formula",
                    "(value_type = 'Formula' AND formula_expression IS NOT NULL) OR (value_type <> 'Formula' AND formula_expression IS NULL)");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.Code).HasMaxLength(50).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(100).IsRequired();
            entity.Property(row => row.ComponentType).HasMaxLength(20).IsRequired();
            entity.Property(row => row.ValueType).HasMaxLength(20).IsRequired();
            entity.Property(row => row.TaxTreatmentCode).HasMaxLength(50);
            entity.Property(row => row.FormulaExpression).HasMaxLength(1000);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.Code }).IsUnique();
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollSalaryStructure>(entity =>
        {
            entity.ToTable("payroll_salary_structures", table =>
            {
                table.HasCheckConstraint("ck_payroll_salary_structure_version", "version > 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.Code).HasMaxLength(50).IsRequired();
            entity.Property(row => row.Name).HasMaxLength(100).IsRequired();
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.Code }).IsUnique();
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(row => row.Lines).WithOne().HasForeignKey(row => new
                { row.BusinessId, row.StructureId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<PayrollSalaryStructureLine>(entity =>
        {
            entity.ToTable("payroll_salary_structure_lines", table =>
            {
                table.HasCheckConstraint("ck_payroll_salary_structure_line_value",
                    "calculation_order >= 0 AND (default_value IS NULL OR default_value >= 0)");
            });
            entity.HasKey(row => row.Id);
            entity.Property(row => row.DefaultValue).HasPrecision(18, 4);
            entity.Property(row => row.FormulaOverride).HasMaxLength(1000);
            entity.HasIndex(row => new { row.BusinessId, row.StructureId, row.SalaryComponentId })
                .IsUnique();
            entity.HasOne<PayrollSalaryComponent>().WithMany().HasForeignKey(row => new
                { row.BusinessId, row.SalaryComponentId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollSalarySetupRequest>(entity =>
        {
            entity.ToTable("payroll_salary_setup_requests", table =>
            {
                table.HasCheckConstraint("ck_payroll_salary_setup_request_version",
                    "version > 0 AND salary_structure_version > 0");
                table.HasCheckConstraint("ck_payroll_salary_setup_request_status",
                    "status IN ('Draft', 'PendingApproval', 'Approved', 'Rejected', 'Cancelled')");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.StructureSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.ComponentSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.IdempotencyPayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(24).IsRequired();
            entity.Property(row => row.DecisionReason).HasMaxLength(1000);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new
            {
                row.BusinessId, row.CreatedByUserId, row.EmployeeId, row.IdempotencyKey
            }).IsUnique();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.EffectiveFrom, row.Status });
            entity.HasIndex(row => row.ApprovalInstanceId).IsUnique()
                .HasFilter("approval_instance_id IS NOT NULL");
            entity.HasOne<HrEmployee>().WithMany().HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollSalaryStructure>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.SalaryStructureId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.ApprovalInstance).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ApprovalInstanceId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollEmployeeSalaryVersion>(entity =>
        {
            entity.ToTable("payroll_employee_salary_versions", table =>
            {
                table.HasCheckConstraint("ck_payroll_employee_salary_structure_version",
                    "salary_structure_version > 0");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.StructureSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.ComponentSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.EffectiveFrom }).IsUnique();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.SalarySetupRequestId });
            entity.HasIndex(row => row.SalaryRevisionRequestId).IsUnique()
                .HasFilter("salary_revision_request_id IS NOT NULL");
            entity.HasIndex(row => row.PredecessorSalaryVersionId).IsUnique()
                .HasFilter("predecessor_salary_version_id IS NOT NULL");
            entity.HasOne<HrEmployee>().WithMany().HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollSalarySetupRequest>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.SalarySetupRequestId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollSalaryStructure>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.SalaryStructureId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollSalaryRevisionRequest>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.SalaryRevisionRequestId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollEmployeeSalaryVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.PredecessorSalaryVersionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApprovalInstance>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ApprovalInstanceId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<PayrollSalaryRevisionRequest>(entity =>
        {
            entity.ToTable("payroll_salary_revision_requests", table =>
            {
                table.HasCheckConstraint("ck_payroll_salary_revision_request_version",
                    "version > 0");
                table.HasCheckConstraint("ck_payroll_salary_revision_request_status",
                    "status IN ('Draft', 'PendingApproval', 'Approved', 'Rejected', 'Cancelled')");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.StructureSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.CurrentComponentSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.ProposedComponentSnapshotJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.RequestHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.IdempotencyPayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.Status).HasMaxLength(24).IsRequired();
            entity.Property(row => row.DecisionReason).HasMaxLength(1000);
            entity.Property(row => row.Version).IsConcurrencyToken();
            entity.HasIndex(row => new { row.BusinessId, row.CreatedByUserId, row.EmployeeId, row.IdempotencyKey })
                .IsUnique();
            entity.HasIndex(row => new { row.BusinessId, row.EmployeeId, row.Status });
            entity.HasIndex(row => row.ApprovalInstanceId).IsUnique()
                .HasFilter("approval_instance_id IS NOT NULL");
            entity.HasOne<HrEmployee>().WithMany().HasForeignKey(row => new { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollEmployeeSalaryVersion>().WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.SourceSalaryVersionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(row => row.ApprovalInstance).WithMany()
                .HasForeignKey(row => new { row.BusinessId, row.ApprovalInstanceId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Business>().WithMany().HasForeignKey(row => row.BusinessId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    public static void ConfigureAdjustments(ModelBuilder model)
    {
        model.Entity<PayrollEmployeeAdjustmentVersion>(entity =>
        {
            entity.ToTable("payroll_employee_adjustment_versions", table =>
            {
                table.HasCheckConstraint("ck_payroll_employee_adjustment_version",
                    "version > 0 AND amount > 0");
                table.HasCheckConstraint("ck_payroll_employee_adjustment_kind",
                    "adjustment_kind IN ('RecurringAllowance', 'RecurringDeduction', 'OneTimeEarning', 'OneTimeDeduction')");
                table.HasCheckConstraint("ck_payroll_employee_adjustment_classification",
                    "classification IN ('Manual', 'Statutory', 'Incentive', 'Bonus')");
                table.HasCheckConstraint("ck_payroll_employee_adjustment_component_type",
                    "component_type_snapshot IN ('Earning', 'Deduction')");
                table.HasCheckConstraint("ck_payroll_employee_adjustment_status",
                    "status IN ('Active', 'Inactive')");
                table.HasCheckConstraint("ck_payroll_employee_adjustment_dates",
                    "effective_to IS NULL OR effective_to >= effective_from");
            });
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.ComponentCodeSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(row => row.ComponentNameSnapshot).HasMaxLength(100).IsRequired();
            entity.Property(row => row.ComponentTypeSnapshot).HasMaxLength(20).IsRequired();
            entity.Property(row => row.ValueTypeSnapshot).HasMaxLength(20).IsRequired();
            entity.Property(row => row.AdjustmentKind).HasMaxLength(30).IsRequired();
            entity.Property(row => row.Classification).HasMaxLength(20).IsRequired();
            entity.Property(row => row.Amount).HasPrecision(18, 4);
            entity.Property(row => row.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(row => row.Reference).HasMaxLength(150);
            entity.Property(row => row.Status).HasMaxLength(20).IsRequired();
            entity.Property(row => row.IdempotencyKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.IdempotencyPayloadHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new
            {
                row.BusinessId, row.EmployeeId, row.AdjustmentIdentity, row.Version
            }).IsUnique().HasDatabaseName("ux_payroll_adj_identity_version");
            entity.HasIndex(row => row.PredecessorVersionId).IsUnique()
                .HasFilter("predecessor_version_id IS NOT NULL")
                .HasDatabaseName("ux_payroll_adj_predecessor");
            entity.HasIndex(row => new
            {
                row.BusinessId, row.CreatedByUserId, row.EmployeeId, row.IdempotencyKey
            }).IsUnique().HasDatabaseName("ux_payroll_adj_idempotency");
            entity.HasIndex(row => new
            {
                row.BusinessId, row.EmployeeId, row.EffectiveFrom, row.AdjustmentKind
            }).HasDatabaseName("ix_payroll_adj_effective");
            entity.HasOne<HrEmployee>().WithMany().HasForeignKey(row => new
                { row.BusinessId, row.EmployeeId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollSalaryComponent>().WithMany().HasForeignKey(row => new
                { row.BusinessId, row.SalaryComponentId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PayrollEmployeeAdjustmentVersion>().WithMany().HasForeignKey(row => new
                { row.BusinessId, row.PredecessorVersionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RetailPOS.Core.Entities.HR.Organisation.OperationalLocationProjection>()
                .WithMany().HasForeignKey(row => new { row.BusinessId, row.OperationalLocationProjectionId })
                .HasPrincipalKey(row => new { row.BusinessId, row.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
