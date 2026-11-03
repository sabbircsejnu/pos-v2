using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetStockTransferByIdQuery(long Id)
    : IQuery<StockTransferReadModel?>;

public sealed class GetStockTransferByIdQueryHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockTransferReadStore readStore)
    : IRequestHandler<GetStockTransferByIdQuery, StockTransferReadModel?>
{
    public async Task<StockTransferReadModel?> Handle(
        GetStockTransferByIdQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        return await readStore.GetByIdAsync(request.Id, businessId, cancellationToken);
    }
}

public sealed record GetStockTransfersQuery(
    StockTransferListFilter Filter,
    bool CanViewAllLocations,
    string? DefaultLocationType,
    long? DefaultLocationId)
    : IQuery<IReadOnlyList<StockTransferReadModel>>;

public sealed class GetStockTransfersQueryHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockTransferReadStore readStore)
    : IRequestHandler<GetStockTransfersQuery, IReadOnlyList<StockTransferReadModel>>
{
    public async Task<IReadOnlyList<StockTransferReadModel>> Handle(
        GetStockTransfersQuery request,
        CancellationToken cancellationToken)
    {
        var scope = new StockTransferReadScope(
            await scopeProvider.GetBusinessIdAsync(cancellationToken),
            request.CanViewAllLocations,
            request.DefaultLocationType,
            request.DefaultLocationId);
        return await readStore.GetAllAsync(request.Filter, scope, cancellationToken);
    }
}

public sealed record SearchStockTransfersQuery(
    StockTransferSearchFilter Filter,
    bool CanViewAllLocations,
    string? DefaultLocationType,
    long? DefaultLocationId)
    : IQuery<StockTransferSearchResult>;

public sealed class SearchStockTransfersQueryHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockTransferReadStore readStore)
    : IRequestHandler<SearchStockTransfersQuery, StockTransferSearchResult>
{
    public async Task<StockTransferSearchResult> Handle(
        SearchStockTransfersQuery request,
        CancellationToken cancellationToken)
    {
        var scope = new StockTransferReadScope(
            await scopeProvider.GetBusinessIdAsync(cancellationToken),
            request.CanViewAllLocations,
            request.DefaultLocationType,
            request.DefaultLocationId);
        return await readStore.SearchAsync(request.Filter, scope, cancellationToken);
    }
}
