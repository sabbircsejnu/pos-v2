using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.ReadModels;
using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Queries;

public sealed record GetInventoryStockLevelsQuery(
    long? LocationId,
    string? LocationType,
    bool LowStockOnly)
    : IQuery<IReadOnlyList<StockLevelReadModel>>;

public sealed class GetInventoryStockLevelsQueryHandler(
    IInventoryReportReadScopeProvider scopeProvider,
    IInventoryReportReadStore store)
    : IRequestHandler<GetInventoryStockLevelsQuery, IReadOnlyList<StockLevelReadModel>>
{
    public async Task<IReadOnlyList<StockLevelReadModel>> Handle(
        GetInventoryStockLevelsQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetStockLevelsAsync(
            scope,
            request.LocationId,
            request.LocationType,
            request.LowStockOnly,
            cancellationToken);
    }
}

public sealed record GetInventoryReportValuationQuery : IQuery<InventoryReportValuationReadModel>;

public sealed class GetInventoryReportValuationQueryHandler(
    IInventoryReportReadScopeProvider scopeProvider,
    IInventoryReportReadStore store)
    : IRequestHandler<GetInventoryReportValuationQuery, InventoryReportValuationReadModel>
{
    public async Task<InventoryReportValuationReadModel> Handle(
        GetInventoryReportValuationQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetValuationAsync(scope, cancellationToken);
    }
}

public sealed record GetSlowMovingInventoryItemsQuery(int Days)
    : IQuery<IReadOnlyList<SlowMovingItemReadModel>>;

public sealed class GetSlowMovingInventoryItemsQueryHandler(
    IInventoryReportReadScopeProvider scopeProvider,
    IInventoryReportReadStore store)
    : IRequestHandler<GetSlowMovingInventoryItemsQuery, IReadOnlyList<SlowMovingItemReadModel>>
{
    public async Task<IReadOnlyList<SlowMovingItemReadModel>> Handle(
        GetSlowMovingInventoryItemsQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetSlowMovingItemsAsync(scope, request.Days, cancellationToken);
    }
}

public sealed record GetCurrentStockReportQuery(CurrentStockReportFilter Filter)
    : IQuery<CurrentStockReportReadModel>;

public sealed class GetCurrentStockReportQueryHandler(
    IInventoryReportReadScopeProvider scopeProvider,
    IInventoryReportReadStore store)
    : IRequestHandler<GetCurrentStockReportQuery, CurrentStockReportReadModel>
{
    public async Task<CurrentStockReportReadModel> Handle(
        GetCurrentStockReportQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetCurrentStockAsync(scope, request.Filter, cancellationToken);
    }
}

public sealed record GetProductLedgerReportQuery(ProductLedgerReportFilter Filter)
    : IQuery<ProductLedgerReportReadModel>;

public sealed class GetProductLedgerReportQueryHandler(
    IInventoryReportReadScopeProvider scopeProvider,
    IProductLedgerReportReadStore store)
    : IRequestHandler<GetProductLedgerReportQuery, ProductLedgerReportReadModel>
{
    public async Task<ProductLedgerReportReadModel> Handle(
        GetProductLedgerReportQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetAsync(scope, request.Filter, cancellationToken);
    }
}

public sealed record GetStockTransactionReportQuery(StockTransactionReportFilter Filter)
    : IQuery<StockTransactionReportReadModel>;

public sealed class GetStockTransactionReportQueryHandler(
    IInventoryReportReadScopeProvider scopeProvider,
    IStockTransactionReportReadStore store)
    : IRequestHandler<GetStockTransactionReportQuery, StockTransactionReportReadModel>
{
    public async Task<StockTransactionReportReadModel> Handle(
        GetStockTransactionReportQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetAsync(scope, request.Filter, cancellationToken);
    }
}

public sealed record GetStockMovementReportQuery(StockMovementReportFilter Filter)
    : IQuery<StockMovementReportReadModel>;

public sealed class GetStockMovementReportQueryHandler(
    IInventoryReportReadScopeProvider scopeProvider,
    IStockMovementReportReadStore store)
    : IRequestHandler<GetStockMovementReportQuery, StockMovementReportReadModel>
{
    public async Task<StockMovementReportReadModel> Handle(
        GetStockMovementReportQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetAsync(scope, request.Filter, cancellationToken);
    }
}

public sealed record GetStockValuationReportQuery(StockValuationReportFilter Filter)
    : IQuery<StockValuationReportReadModel>;

public sealed class GetStockValuationReportQueryHandler(
    IInventoryReportReadScopeProvider scopeProvider,
    IStockValuationReportReadStore store)
    : IRequestHandler<GetStockValuationReportQuery, StockValuationReportReadModel>
{
    public async Task<StockValuationReportReadModel> Handle(
        GetStockValuationReportQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetAsync(scope, request.Filter, cancellationToken);
    }
}

public sealed record GetOutletWiseStockReportQuery(OutletWiseStockFilter Filter)
    : IQuery<OutletWiseStockReportReadModel>;

public sealed class GetOutletWiseStockReportQueryHandler(
    IInventoryReportReadScopeProvider scopeProvider,
    IOutletWiseStockReportReadStore store)
    : IRequestHandler<GetOutletWiseStockReportQuery, OutletWiseStockReportReadModel>
{
    public async Task<OutletWiseStockReportReadModel> Handle(
        GetOutletWiseStockReportQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetAsync(scope, request.Filter, cancellationToken);
    }
}

public sealed record GetLowStockReportQuery(LowStockReportFilter Filter)
    : IQuery<LowStockReportReadModel>;

public sealed class GetLowStockReportQueryHandler(
    IInventoryReportReadScopeProvider scopeProvider,
    ILowStockReportReadStore store)
    : IRequestHandler<GetLowStockReportQuery, LowStockReportReadModel>
{
    public async Task<LowStockReportReadModel> Handle(
        GetLowStockReportQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetAsync(scope, request.Filter, cancellationToken);
    }
}

public sealed record GetOutOfStockReportQuery(OutOfStockReportFilter Filter)
    : IQuery<OutOfStockReportReadModel>;

public sealed class GetOutOfStockReportQueryHandler(
    IInventoryReportReadScopeProvider scopeProvider,
    IOutOfStockReportReadStore store)
    : IRequestHandler<GetOutOfStockReportQuery, OutOfStockReportReadModel>
{
    public async Task<OutOfStockReportReadModel> Handle(
        GetOutOfStockReportQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetAsync(scope, request.Filter, cancellationToken);
    }
}

public sealed record GetNegativeStockReportQuery(NegativeStockReportFilter Filter)
    : IQuery<NegativeStockReportReadModel>;

public sealed class GetNegativeStockReportQueryHandler(
    IInventoryReportReadScopeProvider scopeProvider,
    INegativeStockReportReadStore store)
    : IRequestHandler<GetNegativeStockReportQuery, NegativeStockReportReadModel>
{
    public async Task<NegativeStockReportReadModel> Handle(
        GetNegativeStockReportQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetAsync(scope, request.Filter, cancellationToken);
    }
}

public sealed record GetStockAdjustmentReportQuery(StockAdjustmentReportFilter Filter)
    : IQuery<StockAdjustmentReportReadModel>;

public sealed class GetStockAdjustmentReportQueryHandler(
    IInventoryReportReadScopeProvider scopeProvider,
    IStockAdjustmentReportReadStore store)
    : IRequestHandler<GetStockAdjustmentReportQuery, StockAdjustmentReportReadModel>
{
    public async Task<StockAdjustmentReportReadModel> Handle(
        GetStockAdjustmentReportQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        return await store.GetAsync(scope, request.Filter, cancellationToken);
    }
}

public sealed record GetStockTransferReportQuery(
    StockTransferReportFilter Filter,
    bool CanViewAllLocations,
    string? DefaultLocationType,
    long? DefaultLocationId)
    : IQuery<StockTransferReportReadModel>;

public sealed class GetStockTransferReportQueryHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockTransferReportReadStore store)
    : IRequestHandler<GetStockTransferReportQuery, StockTransferReportReadModel>
{
    public async Task<StockTransferReportReadModel> Handle(
        GetStockTransferReportQuery request,
        CancellationToken cancellationToken)
    {
        var scope = new StockTransferReadScope(
            await scopeProvider.GetBusinessIdAsync(cancellationToken),
            request.CanViewAllLocations,
            request.DefaultLocationType,
            request.DefaultLocationId);
        return await store.GetAsync(scope, request.Filter, cancellationToken);
    }
}
