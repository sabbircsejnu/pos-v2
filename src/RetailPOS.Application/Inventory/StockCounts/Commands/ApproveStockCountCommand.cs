using MediatR;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;

namespace RetailPOS.Application.Inventory.StockCounts.Commands;

public sealed record ApproveStockCountCommand(
    long StockCountId,
    long BusinessId,
    long LocationId,
    string LocationType,
    long ActorUserId) : IRequest<long>;

public sealed class ApproveStockCountCommandHandler(
    IStockCountApprovalStore store)
    : IRequestHandler<ApproveStockCountCommand, long>
{
    public async Task<long> Handle(
        ApproveStockCountCommand request,
        CancellationToken cancellationToken)
    {
        await store.ApproveAsync(
            request.StockCountId,
            request.BusinessId,
            request.LocationId,
            request.LocationType,
            request.ActorUserId,
            cancellationToken);
        return request.StockCountId;
    }
}
