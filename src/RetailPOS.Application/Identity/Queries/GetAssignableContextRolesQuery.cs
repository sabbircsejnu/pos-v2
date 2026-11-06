using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Authentication.Context;
using RetailPOS.Application.Identity.Abstractions;

namespace RetailPOS.Application.Identity.Queries;

public sealed record GetAssignableContextRolesQuery(long BusinessId) : IQuery<IReadOnlyList<UserGovernanceContextRole>>;

public sealed class GetAssignableContextRolesQueryHandler(IUserGovernanceStore store)
    : IRequestHandler<GetAssignableContextRolesQuery, IReadOnlyList<UserGovernanceContextRole>>
{
    public async Task<IReadOnlyList<UserGovernanceContextRole>> Handle(
        GetAssignableContextRolesQuery request,
        CancellationToken cancellationToken) =>
        (await store.ListBusinessContextRolesAsync(
            request.BusinessId,
            UserContextRoleRules.DefaultBusinessRoleNames,
            cancellationToken))
        .Where(role => UserContextRoleRules.IsEligibleActingRole(role.Name, role.Permissions))
        .ToArray();
}
