using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockRequisitions;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetStockRequisitionByIdQuery(long Id)
    : IQuery<StockRequisitionReadModel>;

public sealed class GetStockRequisitionByIdQueryHandler(
    IStockRequisitionBusinessScopeProvider scopeProvider,
    IStockRequisitionReadStore readStore)
    : IRequestHandler<GetStockRequisitionByIdQuery, StockRequisitionReadModel>
{
    public async Task<StockRequisitionReadModel> Handle(
        GetStockRequisitionByIdQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetByIdAsync(request.Id, businessId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Stock requisition with ID {request.Id} not found");
    }
}

public sealed record GetStockRequisitionsQuery(StockRequisitionFilter Filter)
    : IQuery<IReadOnlyList<StockRequisitionReadModel>>;

public sealed class GetStockRequisitionsQueryHandler(
    IStockRequisitionBusinessScopeProvider scopeProvider,
    IStockRequisitionReadStore readStore)
    : IRequestHandler<GetStockRequisitionsQuery, IReadOnlyList<StockRequisitionReadModel>>
{
    public async Task<IReadOnlyList<StockRequisitionReadModel>> Handle(
        GetStockRequisitionsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetAllAsync(request.Filter, businessId, cancellationToken);
    }
}

public sealed record SearchStockRequisitionsQuery(StockRequisitionFilter Filter)
    : IQuery<StockRequisitionSearchResult>;

public sealed class SearchStockRequisitionsQueryHandler(
    IStockRequisitionBusinessScopeProvider scopeProvider,
    IStockRequisitionReadStore readStore)
    : IRequestHandler<SearchStockRequisitionsQuery, StockRequisitionSearchResult>
{
    public async Task<StockRequisitionSearchResult> Handle(
        SearchStockRequisitionsQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await readStore.SearchAsync(request.Filter, businessId, cancellationToken);
    }
}
