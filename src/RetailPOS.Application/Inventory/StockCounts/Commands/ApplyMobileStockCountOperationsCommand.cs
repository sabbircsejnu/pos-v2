using MediatR;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;

namespace RetailPOS.Application.Inventory.StockCounts.Commands;

public sealed record ApplyMobileStockCountOperationsCommand(
    ApplyMobileStockCountOperationsInput Input)
    : IRequest<MobileStockCountOperationsBatchResult>;

public sealed class ApplyMobileStockCountOperationsCommandHandler(
    IMobileStockCountOperationStore store)
    : IRequestHandler<ApplyMobileStockCountOperationsCommand, MobileStockCountOperationsBatchResult>
{
    public Task<MobileStockCountOperationsBatchResult> Handle(
        ApplyMobileStockCountOperationsCommand request,
        CancellationToken cancellationToken) =>
        store.ApplyBatchAsync(request.Input, cancellationToken);
}
