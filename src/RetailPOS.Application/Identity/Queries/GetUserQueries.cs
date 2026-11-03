using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Identity.Abstractions;
using RetailPOS.Application.Identity.ReadModels;

namespace RetailPOS.Application.Identity.Queries;

public sealed record GetUserByIdQuery(long UserId, long? BusinessId) : IQuery<UserReadModel>;

public sealed class GetUserByIdQueryHandler(IUserReadStore store)
    : IRequestHandler<GetUserByIdQuery, UserReadModel>
{
    public async Task<UserReadModel> Handle(
        GetUserByIdQuery request,
        CancellationToken cancellationToken) =>
        await store.FindByIdAsync(request.UserId, request.BusinessId, cancellationToken)
        ?? throw new KeyNotFoundException($"User with ID {request.UserId} not found");
}

public sealed record GetUsersByOutletQuery(long OutletId, long? BusinessId)
    : IQuery<IReadOnlyList<UserReadModel>>;

public sealed class GetUsersByOutletQueryHandler(IUserReadStore store)
    : IRequestHandler<GetUsersByOutletQuery, IReadOnlyList<UserReadModel>>
{
    public Task<IReadOnlyList<UserReadModel>> Handle(
        GetUsersByOutletQuery request,
        CancellationToken cancellationToken) =>
        store.ListByOutletAsync(request.OutletId, request.BusinessId, cancellationToken);
}

public sealed record GetUsersByRoleQuery(long RoleId, long? BusinessId)
    : IQuery<IReadOnlyList<UserReadModel>>;

public sealed class GetUsersByRoleQueryHandler(IUserReadStore store)
    : IRequestHandler<GetUsersByRoleQuery, IReadOnlyList<UserReadModel>>
{
    public Task<IReadOnlyList<UserReadModel>> Handle(
        GetUsersByRoleQuery request,
        CancellationToken cancellationToken) =>
        store.ListByRoleAsync(request.RoleId, request.BusinessId, cancellationToken);
}
