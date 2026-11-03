using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Approvals;

public sealed class ApprovalKernelStore(RetailPOSDbContext db) : IApprovalKernelStore
{
    public async Task<int> GetLatestPolicyVersionAsync(
        long businessId, string code, CancellationToken cancellationToken) =>
        await db.ApprovalPolicies.AsNoTracking()
            .Where(policy => policy.BusinessId == businessId && policy.Code == code)
            .Select(policy => (int?)policy.VersionNo)
            .MaxAsync(cancellationToken)
            ?? 0;

    public async Task<ApprovalPolicy?> FindEffectivePolicyAsync(
        long businessId,
        ApprovalProcessType processType,
        ApprovalScopeType scopeType,
        string? scopeReference,
        DateOnly effectiveOn,
        CancellationToken cancellationToken)
    {
        var matches = await db.ApprovalPolicies.AsNoTracking()
            .Include(policy => policy.Steps)
            .Where(policy => policy.BusinessId == businessId
                && policy.ProcessType == processType
                && policy.ScopeType == scopeType
                && policy.ScopeReference == scopeReference
                && policy.IsActive
                && policy.EffectiveFrom <= effectiveOn
                && (policy.EffectiveTo == null || policy.EffectiveTo >= effectiveOn))
            .OrderByDescending(policy => policy.VersionNo)
            .Take(2)
            .ToListAsync(cancellationToken);
        if (matches.Count > 1)
            throw new ApprovalKernelConflictException("Multiple effective approval policies match the same Business and scope.");
        return matches.SingleOrDefault();
    }

    public Task<ApprovalDecision?> FindDecisionByKeyAsync(
        long businessId, string decisionKey, CancellationToken cancellationToken) =>
        db.ApprovalDecisions.AsNoTracking().SingleOrDefaultAsync(
            decision => decision.BusinessId == businessId && decision.DecisionKey == decisionKey,
            cancellationToken);

    public async Task<ApprovalInstance?> FindInstanceForUpdateAsync(
        long businessId, long instanceId, CancellationToken cancellationToken)
    {
        var lockedId = instanceId;
        if (db.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            lockedId = await db.Database.SqlQuery<long>(
                    $"SELECT id AS \"Value\" FROM hr_approval_instances WHERE id = {instanceId} AND business_id = {businessId} FOR UPDATE")
                .SingleOrDefaultAsync(cancellationToken);
            if (lockedId == 0)
                return null;
        }
        else if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite")
        {
            throw new InvalidOperationException("Approval instance serialization is configured only for PostgreSQL and SQLite tests.");
        }

        return await db.ApprovalInstances
            .Include(instance => instance.Steps)
            .SingleOrDefaultAsync(instance => instance.Id == lockedId && instance.BusinessId == businessId,
                cancellationToken);
    }

    public async Task<IReadOnlyList<ApprovalDecision>> GetStepDecisionsAsync(
        long businessId, long stepId, CancellationToken cancellationToken) =>
        await db.ApprovalDecisions.AsNoTracking()
            .Where(decision => decision.BusinessId == businessId && decision.ApprovalInstanceStepId == stepId)
            .OrderBy(decision => decision.Id)
            .ToListAsync(cancellationToken);

    public void AddPolicy(ApprovalPolicy policy) => db.ApprovalPolicies.Add(policy);
    public void AddInstance(ApprovalInstance instance) => db.ApprovalInstances.Add(instance);
    public async Task PersistSubmissionAsync(CancellationToken cancellationToken)
    {
        var deferredEntries = db.ChangeTracker.Entries()
            .Where(entry => entry.Entity is not ApprovalInstance
                and not ApprovalInstanceStep
                && entry.State is not EntityState.Unchanged and not EntityState.Detached)
            .Select(entry => new DeferredEntry(
                entry.Entity,
                entry.State,
                entry.OriginalValues.Clone(),
                entry.Properties.Where(property => property.IsModified)
                    .Select(property => property.Metadata.Name).ToArray()))
            .ToArray();

        foreach (var deferred in deferredEntries)
        {
            var entry = db.Entry(deferred.Entity);
            entry.State = EntityState.Detached;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            foreach (var deferred in deferredEntries)
            {
                var entry = db.Entry(deferred.Entity);
                if (deferred.State == EntityState.Modified)
                {
                    entry.State = EntityState.Unchanged;
                    entry.OriginalValues.SetValues(deferred.OriginalValues);
                    foreach (var propertyName in deferred.ModifiedProperties)
                        entry.Property(propertyName).IsModified = true;
                }
                else
                    entry.State = deferred.State;
            }
        }
    }
    public void AddDecision(ApprovalDecision decision) => db.ApprovalDecisions.Add(decision);

    private sealed record DeferredEntry(
        object Entity,
        EntityState State,
        PropertyValues OriginalValues,
        string[] ModifiedProperties);
}
