using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record CancelStockTransferCommand(long Id, string? Reason, long? CancelledBy)
    : ICommand<StockTransferReadModel>;

public sealed class CancelStockTransferCommandHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockTransferCancellationStore cancellationStore,
    IStockTransferReadStore readStore)
    : IRequestHandler<CancelStockTransferCommand, StockTransferReadModel>
{
    public async Task<StockTransferReadModel> Handle(
        CancelStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var transferId = await cancellationStore.CancelAsync(
            request.Id,
            businessId,
            request.Reason,
            request.CancelledBy,
            cancellationToken);

        return await readStore.GetByIdAsync(transferId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock transfer with ID {transferId} not found");
    }
}
