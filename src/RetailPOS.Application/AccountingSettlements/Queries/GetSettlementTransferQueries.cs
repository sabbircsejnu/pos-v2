using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.AccountingSettlements.Abstractions;
using RetailPOS.Application.AccountingSettlements.ReadModels;

namespace RetailPOS.Application.AccountingSettlements.Queries;

public sealed record ListSettlementBatchesQuery
    : IQuery<IReadOnlyList<SettlementBatchReadModel>>;

public sealed class ListSettlementBatchesQueryHandler(
    ISettlementTransferReadStore readStore,
    ISettlementTransferTenantScope tenantScope)
    : IRequestHandler<ListSettlementBatchesQuery, IReadOnlyList<SettlementBatchReadModel>>
{
    public async Task<IReadOnlyList<SettlementBatchReadModel>> Handle(
        ListSettlementBatchesQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.ListSettlementsAsync(businessId, cancellationToken);
    }
}

public sealed record GetSettlementBatchQuery(long Id)
    : IQuery<SettlementBatchReadModel?>;

public sealed class GetSettlementBatchQueryHandler(
    ISettlementTransferReadStore readStore,
    ISettlementTransferTenantScope tenantScope)
    : IRequestHandler<GetSettlementBatchQuery, SettlementBatchReadModel?>
{
    public async Task<SettlementBatchReadModel?> Handle(
        GetSettlementBatchQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.FindSettlementAsync(request.Id, businessId, cancellationToken);
    }
}

public sealed record ListLiquidityTransfersQuery
    : IQuery<IReadOnlyList<LiquidityTransferReadModel>>;

public sealed class ListLiquidityTransfersQueryHandler(
    ISettlementTransferReadStore readStore,
    ISettlementTransferTenantScope tenantScope)
    : IRequestHandler<ListLiquidityTransfersQuery, IReadOnlyList<LiquidityTransferReadModel>>
{
    public async Task<IReadOnlyList<LiquidityTransferReadModel>> Handle(
        ListLiquidityTransfersQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.ListTransfersAsync(businessId, cancellationToken);
    }
}

public sealed record GetLiquidityTransferQuery(long Id)
    : IQuery<LiquidityTransferReadModel?>;

public sealed class GetLiquidityTransferQueryHandler(
    ISettlementTransferReadStore readStore,
    ISettlementTransferTenantScope tenantScope)
    : IRequestHandler<GetLiquidityTransferQuery, LiquidityTransferReadModel?>
{
    public async Task<LiquidityTransferReadModel?> Handle(
        GetLiquidityTransferQuery request,
        CancellationToken cancellationToken)
    {
        var businessId = await tenantScope.GetBusinessIdAsync(cancellationToken);
        return await readStore.FindTransferAsync(request.Id, businessId, cancellationToken);
    }
}
