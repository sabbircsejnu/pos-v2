using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;

namespace RetailPOS.Application.Inventory.StockCounts.Queries;

public sealed record SearchStockCountsQuery(
    string? Search,
    long? LocationId,
    string? LocationType,
    DateTime? DateFrom,
    DateTime? DateTo,
    string? Status,
    int PageNumber,
    int PageSize)
    : IQuery<StockCountSearchResultReadModel>;

public sealed class SearchStockCountsQueryHandler(
    IStockCountSearchReadScopeProvider scopeProvider,
    IStockCountSearchReadStore store)
    : IRequestHandler<SearchStockCountsQuery, StockCountSearchResultReadModel>
{
    public async Task<StockCountSearchResultReadModel> Handle(
        SearchStockCountsQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(
            request.LocationId,
            request.LocationType,
            cancellationToken);
        return await store.SearchAsync(
            scope,
            new StockCountSearchFilter(
                request.Search,
                request.DateFrom,
                request.DateTo,
                request.Status,
                request.PageNumber,
                request.PageSize),
            cancellationToken);
    }
}

public sealed record GetStockCountByIdQuery(long Id)
    : IQuery<StockCountDetailReadModel>;

public sealed class GetStockCountByIdQueryHandler(
    IStockCountSearchReadScopeProvider scopeProvider,
    IStockCountSearchReadStore store)
    : IRequestHandler<GetStockCountByIdQuery, StockCountDetailReadModel>
{
    public async Task<StockCountDetailReadModel> Handle(
        GetStockCountByIdQuery request,
        CancellationToken cancellationToken)
    {
        var stockCount = await store.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock count with ID {request.Id} not found");
        await scopeProvider.EnsureDetailAccessAsync(
            stockCount.BusinessId,
            stockCount.LocationId,
            stockCount.LocationType,
            cancellationToken);
        return stockCount;
    }
}
