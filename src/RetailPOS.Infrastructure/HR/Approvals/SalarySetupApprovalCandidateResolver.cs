using Microsoft.EntityFrameworkCore;
using RetailPOS.Application.HR.Approvals;
using RetailPOS.Core.Entities.HR.Approvals;
using RetailPOS.Infrastructure.Data;

namespace RetailPOS.Infrastructure.HR.Approvals;

public sealed class SalarySetupApprovalCandidateResolver(RetailPOSDbContext db) : IApprovalCandidateResolver
{
    public async Task<IReadOnlyCollection<long>> ResolveAsync(
        long businessId,
        ApprovalPolicy policy,
        ApprovalPolicyStep step,
        long requesterUserId,
        ApprovalScopeType scopeType,
        string? scopeReference,
        CancellationToken cancellationToken)
    {
        if (policy.ProcessType is not (ApprovalProcessType.SalarySetup
                or ApprovalProcessType.SalaryRevision or ApprovalProcessType.EmployeeLoan
                or ApprovalProcessType.PayrollRun or ApprovalProcessType.FinalSettlement)
            || !long.TryParse(step.ApproverReference, out var referenceId)
            || referenceId <= 0)
            return [];

        var users = db.Users.AsNoTracking()
            .Where(user => user.BusinessId == businessId && user.IsActive && user.Id != requesterUserId);
        return step.ApproverType switch
        {
            ApprovalApproverType.User => await users
                .Where(user => user.Id == referenceId)
                .Select(user => user.Id)
                .ToArrayAsync(cancellationToken),
            ApprovalApproverType.Role => await users
                .Where(user => user.RoleId == referenceId)
                .Select(user => user.Id)
                .ToArrayAsync(cancellationToken),
            _ => []
        };
    }
}
