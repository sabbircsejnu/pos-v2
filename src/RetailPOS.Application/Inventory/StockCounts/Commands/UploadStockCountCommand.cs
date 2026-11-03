using MediatR;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;

namespace RetailPOS.Application.Inventory.StockCounts.Commands;

public sealed record UploadStockCountCommand(
    long StockCountId,
    long BusinessId,
    long LocationId,
    string LocationType,
    long ActorUserId,
    IReadOnlyList<StockCountUploadLineUpdate> Lines) : IRequest<long>;

public sealed class UploadStockCountCommandHandler(
    IStockCountUploadStore store)
    : IRequestHandler<UploadStockCountCommand, long>
{
    public Task<long> Handle(
        UploadStockCountCommand request,
        CancellationToken cancellationToken) =>
        store.UploadAsync(
            new StockCountUploadInput(
                request.StockCountId,
                request.BusinessId,
                request.LocationId,
                request.LocationType,
                request.ActorUserId,
                request.Lines),
            cancellationToken);
}
