using MediatR;
using RetailPOS.Application.Inventory.StockCounts.Abstractions;

namespace RetailPOS.Application.Inventory.StockCounts.Commands;

public sealed record CreateStockCountCommand(
    long BusinessId,
    long LocationId,
    string LocationType,
    DateTime StockCountDate,
    string? Remarks,
    long CreatedBy) : IRequest<long>;

public sealed class CreateStockCountCommandHandler(
    IStockCountCreationStore store)
    : IRequestHandler<CreateStockCountCommand, long>
{
    public Task<long> Handle(
        CreateStockCountCommand request,
        CancellationToken cancellationToken) =>
        store.CreateAsync(
            new StockCountCreateInput(
                request.BusinessId,
                request.LocationId,
                request.LocationType,
                request.StockCountDate,
                request.Remarks,
                request.CreatedBy),
            cancellationToken);
}
