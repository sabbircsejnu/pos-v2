using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockAdjustments;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetStockAdjustmentQuery(long Id)
    : IQuery<StockAdjustmentReadModel>;

public sealed class GetStockAdjustmentQueryHandler(
    IStockAdjustmentReadStore store,
    IStockAdjustmentReadScopeProvider scopeProvider)
    : IRequestHandler<GetStockAdjustmentQuery, StockAdjustmentReadModel>
{
    public async Task<StockAdjustmentReadModel> Handle(
        GetStockAdjustmentQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetByIdAsync(request.Id, scope, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock adjustment with ID {request.Id} not found");
    }
}

public sealed record GetStockAdjustmentsQuery(StockAdjustmentListFilter Filter)
    : IQuery<IReadOnlyList<StockAdjustmentReadModel>>;

public sealed class GetStockAdjustmentsQueryHandler(
    IStockAdjustmentReadStore store,
    IStockAdjustmentReadScopeProvider scopeProvider)
    : IRequestHandler<GetStockAdjustmentsQuery, IReadOnlyList<StockAdjustmentReadModel>>
{
    public async Task<IReadOnlyList<StockAdjustmentReadModel>> Handle(
        GetStockAdjustmentsQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetAllAsync(request.Filter, scope, cancellationToken);
    }
}

public sealed record SearchStockAdjustmentsQuery(StockAdjustmentSearchFilter Filter)
    : IQuery<StockAdjustmentSearchReadModel>;

public sealed class SearchStockAdjustmentsQueryHandler(
    IStockAdjustmentReadStore store,
    IStockAdjustmentReadScopeProvider scopeProvider)
    : IRequestHandler<SearchStockAdjustmentsQuery, StockAdjustmentSearchReadModel>
{
    public async Task<StockAdjustmentSearchReadModel> Handle(
        SearchStockAdjustmentsQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.SearchAsync(request.Filter, scope, cancellationToken);
    }
}

public sealed record GetStockAdjustmentHistoryQuery(long VariantId, long LocationId)
    : IQuery<IReadOnlyList<StockAdjustmentReadModel>>;

public sealed class GetStockAdjustmentHistoryQueryHandler(
    IStockAdjustmentReadStore store,
    IStockAdjustmentReadScopeProvider scopeProvider)
    : IRequestHandler<GetStockAdjustmentHistoryQuery, IReadOnlyList<StockAdjustmentReadModel>>
{
    public async Task<IReadOnlyList<StockAdjustmentReadModel>> Handle(
        GetStockAdjustmentHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetHistoryAsync(
            request.VariantId,
            request.LocationId,
            scope,
            cancellationToken);
    }
}
