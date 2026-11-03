using MediatR;
using RetailPOS.Application.Warehouses.Abstractions;

namespace RetailPOS.Application.Warehouses.Queries;

public sealed record GetWarehousesQuery(string? SearchTerm = null)
    : IRequest<IReadOnlyList<WarehouseReadModel>>;

public sealed class GetWarehousesQueryHandler(
    IWarehouseReadStore store,
    IWarehouseReadScopeProvider scopeProvider)
    : IRequestHandler<GetWarehousesQuery, IReadOnlyList<WarehouseReadModel>>
{
    public async Task<IReadOnlyList<WarehouseReadModel>> Handle(
        GetWarehousesQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await store.GetAllAsync(
            businessId,
            string.IsNullOrWhiteSpace(request.SearchTerm) ? null : request.SearchTerm,
            cancellationToken);
    }
}

public sealed record GetWarehouseByIdQuery(long WarehouseId)
    : IRequest<WarehouseReadModel?>;

public sealed class GetWarehouseByIdQueryHandler(
    IWarehouseReadStore store,
    IWarehouseReadScopeProvider scopeProvider)
    : IRequestHandler<GetWarehouseByIdQuery, WarehouseReadModel?>
{
    public async Task<WarehouseReadModel?> Handle(
        GetWarehouseByIdQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await store.GetByIdAsync(request.WarehouseId, businessId, cancellationToken);
    }
}

public sealed record GetBusinessWarehousesQuery(long BusinessId)
    : IRequest<IReadOnlyList<WarehouseReadModel>>;

public sealed class GetBusinessWarehousesQueryHandler(IWarehouseReadStore store)
    : IRequestHandler<GetBusinessWarehousesQuery, IReadOnlyList<WarehouseReadModel>>
{
    public Task<IReadOnlyList<WarehouseReadModel>> Handle(
        GetBusinessWarehousesQuery request,
        CancellationToken cancellationToken) =>
        store.GetByBusinessIdAsync(request.BusinessId, cancellationToken);
}

public sealed record GetWarehouseStatsQuery(long WarehouseId)
    : IRequest<WarehouseStatsReadModel?>;

public sealed class GetWarehouseStatsQueryHandler(
    IWarehouseReadStore store,
    IWarehouseReadScopeProvider scopeProvider)
    : IRequestHandler<GetWarehouseStatsQuery, WarehouseStatsReadModel?>
{
    public async Task<WarehouseStatsReadModel?> Handle(
        GetWarehouseStatsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await store.GetStatsAsync(request.WarehouseId, businessId, cancellationToken);
    }
}
