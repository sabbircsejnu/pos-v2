using RetailPOS.Application.AccountingSettlements.ReadModels;

namespace RetailPOS.Application.AccountingSettlements.Abstractions;

public interface ISettlementTransferReadStore
{
    Task<IReadOnlyList<SettlementBatchReadModel>> ListSettlementsAsync(
        long businessId,
        CancellationToken cancellationToken);

    Task<SettlementBatchReadModel?> FindSettlementAsync(
        long id,
        long businessId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<LiquidityTransferReadModel>> ListTransfersAsync(
        long businessId,
        CancellationToken cancellationToken);

    Task<LiquidityTransferReadModel?> FindTransferAsync(
        long id,
        long businessId,
        CancellationToken cancellationToken);
}

public interface ISettlementTransferTenantScope
{
    Task<long> GetBusinessIdAsync(CancellationToken cancellationToken);
}
