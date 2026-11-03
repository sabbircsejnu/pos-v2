using MediatR;
using RetailPOS.Application.Outlets.Abstractions;

namespace RetailPOS.Application.Outlets.Queries;

public sealed record GetOutletsQuery(string? SearchTerm = null)
    : IRequest<IReadOnlyList<OutletReadModel>>;

public sealed class GetOutletsQueryHandler(
    IOutletReadStore store,
    IOutletScopeProvider scopeProvider)
    : IRequestHandler<GetOutletsQuery, IReadOnlyList<OutletReadModel>>
{
    public async Task<IReadOnlyList<OutletReadModel>> Handle(
        GetOutletsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(request.SearchTerm)
            ? await store.GetAllAsync(businessId, cancellationToken)
            : await store.SearchAsync(request.SearchTerm, businessId, cancellationToken);
    }
}

public sealed record GetOutletByIdQuery(long OutletId) : IRequest<OutletReadModel?>;

public sealed class GetOutletByIdQueryHandler(
    IOutletReadStore store,
    IOutletScopeProvider scopeProvider)
    : IRequestHandler<GetOutletByIdQuery, OutletReadModel?>
{
    public async Task<OutletReadModel?> Handle(
        GetOutletByIdQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await store.GetByIdAsync(request.OutletId, businessId, cancellationToken);
    }
}

public sealed record GetBusinessOutletsQuery(long BusinessId)
    : IRequest<IReadOnlyList<OutletReadModel>>;

public sealed class GetBusinessOutletsQueryHandler(IOutletReadStore store)
    : IRequestHandler<GetBusinessOutletsQuery, IReadOnlyList<OutletReadModel>>
{
    public Task<IReadOnlyList<OutletReadModel>> Handle(
        GetBusinessOutletsQuery request,
        CancellationToken cancellationToken) =>
        store.GetByBusinessIdAsync(request.BusinessId, cancellationToken);
}

public sealed record GetOutletStatsQuery(long OutletId) : IRequest<OutletStatsReadModel?>;

public sealed class GetOutletStatsQueryHandler(
    IOutletReadStore store,
    IOutletScopeProvider scopeProvider)
    : IRequestHandler<GetOutletStatsQuery, OutletStatsReadModel?>
{
    public async Task<OutletStatsReadModel?> Handle(
        GetOutletStatsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await store.GetStatsAsync(request.OutletId, businessId, cancellationToken);
    }
}
