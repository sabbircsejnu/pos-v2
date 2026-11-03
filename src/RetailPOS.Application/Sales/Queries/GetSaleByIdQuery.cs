using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Sales.Abstractions;
using RetailPOS.Application.Sales.ReadModels;

namespace RetailPOS.Application.Sales.Queries;

public sealed record GetSaleByIdQuery(long SaleId) : IQuery<SaleReadModel>;

public sealed class GetSaleByIdQueryHandler(ISaleReadStore store)
    : IRequestHandler<GetSaleByIdQuery, SaleReadModel>
{
    public async Task<SaleReadModel> Handle(
        GetSaleByIdQuery request,
        CancellationToken cancellationToken)
    {
        return await store.FindByIdAsync(request.SaleId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sale with ID {request.SaleId} not found");
    }
}
