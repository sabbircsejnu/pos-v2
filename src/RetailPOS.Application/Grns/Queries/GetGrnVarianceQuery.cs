using MediatR;
using RetailPOS.Application.Abstractions.Messaging;
using RetailPOS.Application.Grns.Abstractions;
using RetailPOS.Application.Grns.ReadModels;

namespace RetailPOS.Application.Grns.Queries;

public sealed record GetGrnVarianceQuery(long GrnId) : IQuery<GrnVarianceReadModel>;

public sealed class GetGrnVarianceQueryHandler(
    IGrnReadStore store,
    IGrnReadScopeProvider scopeProvider)
    : IRequestHandler<GetGrnVarianceQuery, GrnVarianceReadModel>
{
    public async Task<GrnVarianceReadModel> Handle(
        GetGrnVarianceQuery request,
        CancellationToken cancellationToken)
    {
        var scope = await scopeProvider.GetAsync(cancellationToken);
        var variance = await store.FindVarianceByIdAsync(
            request.GrnId,
            scope,
            cancellationToken);
        return variance ?? throw new KeyNotFoundException(
            $"GRN with ID {request.GrnId} not found");
    }
}
