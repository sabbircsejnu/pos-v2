using Microsoft.EntityFrameworkCore;
using RetailPOS.Core.Entities.HR.Approvals;

namespace RetailPOS.Infrastructure.HR.Approvals;

internal static class ApprovalKernelModelConfiguration
{
    public static void Configure(ModelBuilder model, string? providerName)
    {
        model.Entity<ApprovalPolicy>(entity =>
        {
            entity.ToTable("hr_approval_policies");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Code).HasMaxLength(100).IsRequired();
            entity.Property(row => row.ProcessType).HasConversion<string>().HasMaxLength(40);
            entity.Property(row => row.Name).HasMaxLength(150).IsRequired();
            entity.Property(row => row.ScopeType).HasConversion<string>().HasMaxLength(30);
            entity.Property(row => row.ScopeReference).HasMaxLength(100);
            entity.HasIndex(row => new { row.BusinessId, row.Code, row.VersionNo }).IsUnique();
            entity.HasIndex(row => new
            {
                row.BusinessId, row.ProcessType, row.ScopeType, row.ScopeReference,
                row.EffectiveFrom, row.EffectiveTo
            });
        });

        model.Entity<ApprovalPolicyStep>(entity =>
        {
            entity.ToTable("hr_approval_policy_steps");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.ApproverType).HasConversion<string>().HasMaxLength(30);
            entity.Property(row => row.ApproverReference).HasMaxLength(150);
            entity.HasIndex(row => new { row.ApprovalPolicyId, row.Sequence }).IsUnique();
            entity.HasOne(row => row.Policy).WithMany(row => row.Steps)
                .HasForeignKey(row => row.ApprovalPolicyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ApprovalInstance>(entity =>
        {
            entity.ToTable("hr_approval_instances");
            entity.HasKey(row => row.Id);
            entity.HasAlternateKey(row => new { row.BusinessId, row.Id });
            entity.Property(row => row.PolicyCodeSnapshot).HasMaxLength(100).IsRequired();
            entity.Property(row => row.ProcessType).HasConversion<string>().HasMaxLength(40);
            entity.Property(row => row.TargetType).HasMaxLength(100).IsRequired();
            entity.Property(row => row.TargetId).HasMaxLength(150).IsRequired();
            entity.Property(row => row.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(row => row.ScopeType).HasConversion<string>().HasMaxLength(30);
            entity.Property(row => row.ScopeReference).HasMaxLength(100);
            entity.Property(row => row.RequestHash).HasMaxLength(64).IsRequired();
            if (providerName == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                entity.Property(row => row.XMin)
                    .HasColumnName("xmin").HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
            }
            else if (providerName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                entity.Ignore(row => row.XMin);
            }
            else
            {
                entity.Ignore(row => row.XMin);
            }
            entity.HasIndex(row => new { row.BusinessId, row.Status, row.ProcessType, row.TargetType, row.TargetId })
                .IsUnique().HasFilter("status = 'Pending'");
            entity.HasOne<ApprovalPolicy>().WithMany().HasForeignKey(row => row.PolicyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ApprovalInstanceStep>(entity =>
        {
            entity.ToTable("hr_approval_instance_steps");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.ResolvedApproverType).HasConversion<string>().HasMaxLength(30);
            entity.Property(row => row.ResolvedApproverReference).HasMaxLength(150).IsRequired();
            entity.Property(row => row.ResolvedApproverCandidatesJson).HasColumnType("jsonb").IsRequired();
            entity.Property(row => row.Decision).HasConversion<string>().HasMaxLength(20);
            entity.Property(row => row.Comment).HasMaxLength(1000);
            entity.HasIndex(row => new { row.ApprovalInstanceId, row.Sequence }).IsUnique();
            entity.HasOne(row => row.Instance).WithMany(row => row.Steps)
                .HasForeignKey(row => row.ApprovalInstanceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<ApprovalDecision>(entity =>
        {
            entity.ToTable("hr_approval_decisions");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.DecisionKey).HasMaxLength(150).IsRequired();
            entity.Property(row => row.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(row => row.Decision).HasConversion<string>().HasMaxLength(20);
            entity.Property(row => row.InstanceStatusAfterDecision).HasConversion<string>().HasMaxLength(20);
            entity.Property(row => row.Reason).HasMaxLength(1000);
            entity.Property(row => row.RequestHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(row => new { row.BusinessId, row.DecisionKey }).IsUnique();
            entity.HasIndex(row => new { row.ApprovalInstanceStepId, row.ActorRealUserId })
                .IsUnique().HasFilter("decision IN ('Approve', 'Reject')");
            entity.HasOne<ApprovalInstance>().WithMany().HasForeignKey(row => row.ApprovalInstanceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApprovalInstanceStep>().WithMany().HasForeignKey(row => row.ApprovalInstanceStepId)
                .OnDelete(DeleteBehavior.Restrict);
       });
    }
}
