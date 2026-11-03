using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockLedger;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetStockLedgerEntryQuery(long Id)
    : IQuery<StockLedgerReadModel?>;

public sealed class GetStockLedgerEntryQueryHandler(
    IStockLedgerReadStore store,
    IStockLedgerReadScopeProvider scopeProvider)
    : IRequestHandler<GetStockLedgerEntryQuery, StockLedgerReadModel?>
{
    public async Task<StockLedgerReadModel?> Handle(
        GetStockLedgerEntryQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetByIdAsync(request.Id, scope, cancellationToken);
    }
}

public sealed record GetStockLedgerTimelineQuery(
    long VariantId,
    long LocationId,
    string LocationType)
    : IQuery<IReadOnlyList<StockLedgerReadModel>>;

public sealed class GetStockLedgerTimelineQueryHandler(
    IStockLedgerReadStore store,
    IStockLedgerReadScopeProvider scopeProvider)
    : IRequestHandler<GetStockLedgerTimelineQuery, IReadOnlyList<StockLedgerReadModel>>
{
    public async Task<IReadOnlyList<StockLedgerReadModel>> Handle(
        GetStockLedgerTimelineQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetByVariantAndLocationAsync(
            request.VariantId,
            request.LocationId,
            request.LocationType,
            scope,
            cancellationToken);
    }
}

public sealed record GetStockLedgerByReferenceQuery(
    string ReferenceType,
    long ReferenceId)
    : IQuery<IReadOnlyList<StockLedgerReadModel>>;

public sealed class GetStockLedgerByReferenceQueryHandler(
    IStockLedgerReadStore store,
    IStockLedgerReadScopeProvider scopeProvider)
    : IRequestHandler<GetStockLedgerByReferenceQuery, IReadOnlyList<StockLedgerReadModel>>
{
    public async Task<IReadOnlyList<StockLedgerReadModel>> Handle(
        GetStockLedgerByReferenceQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetByReferenceAsync(
            request.ReferenceType,
            request.ReferenceId,
            scope,
            cancellationToken);
    }
}

public sealed record SearchStockLedgerQuery(StockLedgerSearchFilter Filter)
    : IQuery<StockLedgerSearchReadModel>;

public sealed class SearchStockLedgerQueryHandler(
    IStockLedgerReadStore store,
    IStockLedgerReadScopeProvider scopeProvider)
    : IRequestHandler<SearchStockLedgerQuery, StockLedgerSearchReadModel>
{
    public async Task<StockLedgerSearchReadModel> Handle(
        SearchStockLedgerQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.SearchAsync(request.Filter, scope, cancellationToken);
    }
}
