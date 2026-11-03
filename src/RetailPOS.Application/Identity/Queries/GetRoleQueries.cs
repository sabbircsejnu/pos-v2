using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.Permissions;
using RetailPOS.Application.Identity.ReadModels;

namespace RetailPOS.Application.Identity.Queries;

public sealed record GetRoleByIdQuery(long RoleId, bool IsSuperAdmin) : IQuery<RoleReadModel>;

public sealed class GetRoleByIdQueryHandler(IRoleReadStore store)
    : IRequestHandler<GetRoleByIdQuery, RoleReadModel>
{
    public async Task<RoleReadModel> Handle(
        GetRoleByIdQuery request,
        CancellationToken cancellationToken) =>
        await store.FindByIdAsync(request.RoleId, request.IsSuperAdmin, cancellationToken)
        ?? throw new KeyNotFoundException($"Role with ID {request.RoleId} not found");
}

public sealed record GetPermissionsQuery : IQuery<IReadOnlyList<string>>;

public sealed class GetPermissionsQueryHandler
    : IRequestHandler<GetPermissionsQuery, IReadOnlyList<string>>
{
    public Task<IReadOnlyList<string>> Handle(
        GetPermissionsQuery request,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(PermissionCatalog.All.ToArray());
}
