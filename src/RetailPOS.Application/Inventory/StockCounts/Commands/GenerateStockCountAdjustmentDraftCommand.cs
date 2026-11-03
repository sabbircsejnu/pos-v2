using MediatR;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;

namespace RetailPOS.Application.Inventory.StockCounts.Commands;

public sealed record GenerateStockCountAdjustmentDraftCommand(
    long StockCountId,
    long BusinessId,
    long LocationId,
    string LocationType,
    long ActorUserId) : IRequest<long>;

public sealed class GenerateStockCountAdjustmentDraftCommandHandler(
    IStockCountAdjustmentDraftStore store)
    : IRequestHandler<GenerateStockCountAdjustmentDraftCommand, long>
{
    public async Task<long> Handle(
        GenerateStockCountAdjustmentDraftCommand request,
        CancellationToken cancellationToken)
    {
        await store.GenerateAsync(
            request.StockCountId,
            request.BusinessId,
            request.LocationId,
            request.LocationType,
            request.ActorUserId,
            cancellationToken);
        return request.StockCountId;
    }
}
