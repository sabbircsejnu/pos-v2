using MediatR;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;

namespace RetailPOS.Application.Inventory.StockCounts.Commands;

public sealed record SubmitStockCountCommand(
    long StockCountId,
    long BusinessId,
    long LocationId,
    string LocationType,
    long ActorUserId) : IRequest<long>;

public sealed class SubmitStockCountCommandHandler(
    IStockCountLifecycleStore store)
    : IRequestHandler<SubmitStockCountCommand, long>
{
    public async Task<long> Handle(
        SubmitStockCountCommand request,
        CancellationToken cancellationToken)
    {
        await store.SubmitAsync(
            request.StockCountId,
            request.BusinessId,
            request.LocationId,
            request.LocationType,
            request.ActorUserId,
            cancellationToken);
        return request.StockCountId;
    }
}

public sealed record RejectStockCountCommand(
    long StockCountId,
    long BusinessId,
    long LocationId,
    string LocationType,
    long ActorUserId,
    string? Reason) : IRequest<long>;

public sealed class RejectStockCountCommandHandler(
    IStockCountLifecycleStore store)
    : IRequestHandler<RejectStockCountCommand, long>
{
    public async Task<long> Handle(
        RejectStockCountCommand request,
        CancellationToken cancellationToken)
    {
        await store.RejectAsync(
            request.StockCountId,
            request.BusinessId,
            request.LocationId,
            request.LocationType,
            request.ActorUserId,
            request.Reason,
            cancellationToken);
        return request.StockCountId;
    }
}

public sealed record ReopenStockCountCommand(
    long StockCountId,
    long BusinessId,
    long LocationId,
    string LocationType,
    long ActorUserId) : IRequest<long>;

public sealed class ReopenStockCountCommandHandler(
    IStockCountLifecycleStore store)
    : IRequestHandler<ReopenStockCountCommand, long>
{
    public async Task<long> Handle(
        ReopenStockCountCommand request,
        CancellationToken cancellationToken)
    {
        await store.ReopenAsync(
            request.StockCountId,
            request.BusinessId,
            request.LocationId,
            request.LocationType,
            request.ActorUserId,
            cancellationToken);
        return request.StockCountId;
    }
}
