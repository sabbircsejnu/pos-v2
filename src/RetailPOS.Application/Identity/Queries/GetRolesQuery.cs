using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.ReadModels;

namespace RetailPOS.Application.Identity.Queries;

public sealed record GetRolesQuery(bool IsSuperAdmin, long? BusinessId)
    : IQuery<IReadOnlyList<RoleReadModel>>;

public sealed class GetRolesQueryHandler(IRoleReadStore store)
    : IRequestHandler<GetRolesQuery, IReadOnlyList<RoleReadModel>>
{
    public Task<IReadOnlyList<RoleReadModel>> Handle(
        GetRolesQuery request,
        CancellationToken cancellationToken) =>
        store.ListAsync(request.IsSuperAdmin, request.BusinessId, cancellationToken);
}
