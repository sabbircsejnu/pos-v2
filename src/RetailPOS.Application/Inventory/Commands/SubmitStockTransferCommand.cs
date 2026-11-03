using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Inventory.Abstractions;
using RetailPOS.Application.Inventory.StockTransfers;

namespace RetailPOS.Application.Inventory.Commands;

public sealed record SubmitStockTransferCommand(long Id, long? SubmittedBy)
    : ICommand<StockTransferReadModel>;

public sealed class SubmitStockTransferCommandHandler(
    IStockTransferReadScopeProvider scopeProvider,
    IStockTransferSubmitStore submitStore,
    IStockTransferReadStore readStore)
    : IRequestHandler<SubmitStockTransferCommand, StockTransferReadModel>
{
    public async Task<StockTransferReadModel> Handle(
        SubmitStockTransferCommand request,
        CancellationToken cancellationToken)
    {
        var businessId = await scopeProvider.GetBusinessIdAsync(cancellationToken);
        var submittedId = await submitStore.SubmitAsync(
            request.Id,
            businessId,
            request.SubmittedBy,
            cancellationToken);

        return await readStore.GetByIdAsync(submittedId, businessId, cancellationToken)
            ?? throw new KeyNotFoundException($"Stock transfer with ID {submittedId} not found");
    }
}
